"""Adapter that runs the claude CLI and returns its text output."""
from __future__ import annotations

import asyncio
import json
import logging
import os
import time
from pathlib import Path

from usage import UsageParts
from workspace import finalize_instruction, materialize_context

log = logging.getLogger(__name__)

MAX_CONCURRENT = int(os.getenv("CLAUDE_MAX_CONCURRENT", "2"))
_semaphore = asyncio.Semaphore(MAX_CONCURRENT)

# Home directory for claude auth — mounted as a volume in the container.
CLAUDE_HOME = os.getenv("CLAUDE_HOME", "/auth/claude")

# Hard cap on how long a single claude CLI call may run.
# Increase for workloads that produce very long outputs (e.g. 30-page documents).
CLI_TIMEOUT_SECONDS = int(os.getenv("CLI_TIMEOUT_SECONDS", "1800"))

# The claude CLI resolves the family aliases opus/sonnet/haiku to the NEWEST model of each
# family automatically. We surface them as `claude-<family>-latest` entries that never go
# stale, and map them back to the bare CLI alias on invocation (see _normalize_model).
LATEST_ALIAS_MODELS = ["claude-opus-latest", "claude-sonnet-latest", "claude-haiku-latest"]

_ALIAS_TO_CLI = {
    "claude-opus-latest": "opus",
    "claude-sonnet-latest": "sonnet",
    "claude-haiku-latest": "haiku",
    "opus": "opus",
    "sonnet": "sonnet",
    "haiku": "haiku",
}

# Offline fallback when alias resolution is unavailable. Kept reasonably current, but the
# dynamic resolver (list_models_async) supersedes it and the *-latest aliases never go stale.
STATIC_MODELS = [
    "claude-opus-4-8",
    "claude-sonnet-4-6",
    "claude-haiku-4-5",
]

_MODEL_CACHE_TTL = 86400  # 24 hours
# In-memory cache of concrete latest model ids: (model_ids, fetched_at)
_model_cache: tuple[list[str], float] | None = None
_model_cache_lock = asyncio.Lock()


def _normalize_model(model: str | None) -> str | None:
    """Map a requested model to a name the claude CLI accepts.

    Strips any provider prefix and normalises dots to dashes (e.g.
    "anthropic/claude-opus-4.8" -> "claude-opus-4-8"). Family aliases
    (opus/sonnet/haiku and claude-<family>-latest) are passed through as the CLI's
    built-in aliases, which always resolve to the newest model of that family.
    """
    if not model:
        return None
    bare = model.split("/")[-1] if "/" in model else model
    bare = bare.replace(".", "-")
    return _ALIAS_TO_CLI.get(bare.lower(), bare)


async def _probe_alias(alias: str) -> str | None:
    """Ask the claude CLI which concrete model the family alias currently resolves to."""
    args = ["claude", "-p", "--model", alias, "--output-format", "json"]
    env = {**os.environ, "HOME": CLAUDE_HOME}
    try:
        proc = await asyncio.create_subprocess_exec(
            *args,
            stdin=asyncio.subprocess.PIPE,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
            env=env,
        )
        stdout, _ = await asyncio.wait_for(proc.communicate(input=b"hi"), timeout=60)
        data = json.loads(stdout.decode(errors="replace"))
        keys = list((data.get("modelUsage") or {}).keys())
        if keys:
            return keys[0]
    except Exception as exc:  # noqa: BLE001 — best-effort discovery, never fatal
        log.warning("claude model alias probe failed for %s: %s", alias, exc)
    return None


async def _resolve_concrete_models() -> list[str]:
    """Resolve the current concrete model id for each family via the CLI's alias resolution.

    Probes run concurrently so a cache refresh costs one round-trip, not three.
    """
    results = await asyncio.gather(*(_probe_alias(a) for a in ("opus", "sonnet", "haiku")))
    concrete: list[str] = []
    for resolved in results:
        if resolved and resolved not in concrete:
            concrete.append(resolved)
    return concrete or list(STATIC_MODELS)


def _compose_model_list(concrete: list[str]) -> list[str]:
    """Always-latest aliases first, then the concrete current model ids (deduped)."""
    out = list(LATEST_ALIAS_MODELS)
    for m in concrete:
        if m not in out:
            out.append(m)
    return out


async def list_models_async() -> list[str]:
    """Always-current model list: always-latest aliases + the concrete newest model ids
    (resolved via the CLI's own alias resolution, cached 24h). Falls back to STATIC_MODELS
    when probing is unavailable, so the list is never stale and needs no manual updates."""
    global _model_cache
    async with _model_cache_lock:
        if _model_cache is not None and time.time() - _model_cache[1] < _MODEL_CACHE_TTL:
            concrete = _model_cache[0]
        else:
            concrete = await _resolve_concrete_models()
            _model_cache = (concrete, time.time())
    return _compose_model_list(concrete)


def list_models() -> list[str]:
    """Synchronous shim (startup / fallback): always-latest aliases + cached-or-static concrete ids."""
    concrete = _model_cache[0] if _model_cache else list(STATIC_MODELS)
    return _compose_model_list(concrete)


async def complete_document(
    system_prompt: str,
    user_instruction: str,
    document: str,
    workspace_path: Path,
    model: str | None,
    max_tokens: int | None,
    context_document: str | None = None,
) -> tuple[str, UsageParts]:
    """
    Calls the claude CLI in document-edit mode: the CLI reads draft.md in the given
    workspace, applies the instruction, and writes the revised document back.
    Returns (content of draft.md after the CLI exits, token usage).

    Uses --allowedTools Read,Edit,Write with --permission-mode acceptEdits so the
    agent can edit files without interactive confirmation prompts.

    context_document holds large background context (grounding + advisor notes); it is
    offloaded to context.md when oversized (see workspace.materialize_context).
    """
    async with _semaphore:
        return await _run_claude_document(
            system_prompt, user_instruction, document, workspace_path, model, max_tokens,
            context_document,
        )


async def complete(prompt: str, model: str | None, max_tokens: int | None) -> str:
    """
    Calls the claude CLI in print mode and returns the raw text output.

    The semaphore limits concurrent calls to respect subscription rate limits.
    """
    text, _ = await complete_with_usage(prompt, model, max_tokens)
    return text


async def complete_with_usage(
    prompt: str, model: str | None, max_tokens: int | None
) -> tuple[str, UsageParts]:
    """Like complete(), but also returns the real token usage reported by the CLI."""
    async with _semaphore:
        return await _run_claude(prompt, model, max_tokens)


async def stream(prompt: str, model: str | None, max_tokens: int | None):
    """Stream the claude CLI output as (kind, payload) events.

    Yields ("delta", text) for each token chunk and finally ("usage", UsageParts).
    The semaphore is held for the whole stream to respect subscription rate limits.
    """
    async with _semaphore:
        async for event in _stream_claude(prompt, model, max_tokens):
            yield event


async def _stream_claude(prompt: str, model: str | None, max_tokens: int | None):
    args = [
        "claude", "-p",
        "--output-format", "stream-json",
        "--verbose",
        "--include-partial-messages",
        "--allowedTools", "WebSearch,WebFetch",
    ]
    if model:
        args += ["--model", _normalize_model(model)]

    env = {**os.environ, "HOME": CLAUDE_HOME}
    proc = await asyncio.create_subprocess_exec(
        *args,
        stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        env=env,
    )

    async def _feed() -> None:
        try:
            proc.stdin.write(prompt.encode("utf-8"))
            await proc.stdin.drain()
        finally:
            proc.stdin.close()

    feeder = asyncio.create_task(_feed())
    emitted = False
    final_text = ""
    usage = UsageParts()
    try:
        async with asyncio.timeout(CLI_TIMEOUT_SECONDS):
            async for raw_line in proc.stdout:
                line = raw_line.decode(errors="replace").strip()
                if not line:
                    continue
                try:
                    ev = json.loads(line)
                except (json.JSONDecodeError, ValueError):
                    continue
                if not isinstance(ev, dict):
                    continue
                etype = ev.get("type")
                if etype == "stream_event":
                    inner = ev.get("event", {})
                    if isinstance(inner, dict) and inner.get("type") == "content_block_delta":
                        delta = inner.get("delta", {})
                        if isinstance(delta, dict) and delta.get("type") == "text_delta" and delta.get("text"):
                            emitted = True
                            yield ("delta", str(delta["text"]))
                elif etype == "result":
                    if ev.get("is_error"):
                        status = ev.get("api_error_status")
                        raise RuntimeError(
                            f"claude CLI API error (status={status}): {ev.get('result', '')[:300]}"
                        )
                    final_text, usage = _extract_result_and_usage(ev, "")
        await feeder
        await proc.wait()
        if proc.returncode not in (0, None):
            err = (await proc.stderr.read()).decode(errors="replace").strip()
            raise RuntimeError(f"claude CLI (stream) exited with code {proc.returncode}: {err[:200]}")
        # No partial deltas were emitted (older CLI / no partial support) — emit the final text once.
        if not emitted and final_text:
            yield ("delta", final_text)
        yield ("usage", usage)
    except TimeoutError:
        raise RuntimeError(f"claude CLI (stream) timed out after {CLI_TIMEOUT_SECONDS} seconds")
    finally:
        feeder.cancel()
        if proc.returncode is None:
            proc.kill()
            await proc.wait()


async def _run_claude_document(
    system_prompt: str,
    user_instruction: str,
    document: str,
    workspace_path: Path,
    model: str | None,
    max_tokens: int | None,
    context_document: str | None = None,
) -> tuple[str, UsageParts]:
    # Large background context goes to context.md (pointer) or inline if small; the file-contract
    # and user instruction always stay in the prompt.
    context_preamble = materialize_context(workspace_path, context_document)
    instruction = (
        context_preamble
        + "The document you are editing is located at draft.md in the current directory. "
        "Read it, apply the revisions described below, then write the complete updated "
        "document back to draft.md. Do NOT output the document content to stdout — "
        "only edit the file.\n\n"
        + user_instruction
    )

    # --allowedTools Read,Edit,Write: restrict agent to file operations only (no web, no bash).
    # --permission-mode acceptEdits: auto-approve file edits without interactive prompts.
    # --append-system-prompt: injects the writer persona system prompt. Note: this is the one
    # remaining argv value not offloaded to a file; system prompts are operator-defined profiles
    # (a few KB), so they stay well under MAX_ARG_STRLEN. The user-driven variable content
    # (document, context, findings) is all file-backed via draft.md/context.md/instruction.md.
    # --output-format json: the document text is read from draft.md, but stdout still
    # carries the usage block + total_cost_usd, which we parse for faithful token accounting.
    args = [
        "claude", "-p",
        "--output-format", "json",
        "--allowedTools", "Read,Edit,Write",
        "--permission-mode", "acceptEdits",
        "--append-system-prompt", system_prompt,
    ]

    if model:
        args += ["--model", _normalize_model(model)]

    # Offload to instruction.md if the instruction would exceed the per-argument OS limit.
    args.append(finalize_instruction(workspace_path, instruction))

    env = {**os.environ, "HOME": CLAUDE_HOME}

    proc = await asyncio.create_subprocess_exec(
        *args,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=str(workspace_path),
        env=env,
    )
    try:
        stdout, stderr = await asyncio.wait_for(proc.communicate(), timeout=CLI_TIMEOUT_SECONDS)
    except asyncio.TimeoutError:
        proc.kill()
        await proc.wait()  # reap the killed process so it does not linger as a zombie
        raise RuntimeError(f"claude CLI (document mode) timed out after {CLI_TIMEOUT_SECONDS}s")

    if proc.returncode != 0:
        stderr_msg = stderr.decode(errors="replace").strip()
        raise RuntimeError(
            f"claude CLI (document mode) exited with code {proc.returncode}: "
            f"{stderr_msg or stdout.decode(errors='replace')[:200]}"
        )

    draft_path = workspace_path / "draft.md"
    try:
        result = draft_path.read_text(encoding="utf-8")
    except FileNotFoundError:
        raise RuntimeError(
            "claude document mode: draft.md not found after CLI exit — "
            "the agent may have deleted or moved the file"
        )

    if result == document:
        log.warning("claude document mode: draft.md unchanged after CLI run — no edits applied")

    # The document text comes from draft.md; stdout JSON only contributes the usage block.
    usage = UsageParts()
    try:
        data = json.loads(stdout.decode(errors="replace").strip())
        if isinstance(data, dict):
            _, usage = _extract_result_and_usage(data, "")
    except (json.JSONDecodeError, ValueError):
        pass

    return result, usage


async def _run_claude(
    prompt: str, model: str | None, max_tokens: int | None
) -> tuple[str, UsageParts]:
    # Allowlist ONLY web tools — no Bash/Edit/Write, so no full permission bypass.
    # Comma-separated single token: a space-separated variadic would greedily
    # consume the trailing prompt positional.
    args = ["claude", "-p", "--output-format", "json", "--allowedTools", "WebSearch,WebFetch"]

    if model:
        args += ["--model", _normalize_model(model)]

    # Pass the prompt through stdin, never as an argv element. A single execve argument may
    # not exceed MAX_ARG_STRLEN (128 KB on Linux); reviewer/advisor prompts embed the full
    # draft (often >128 KB by later iterations), so an argv prompt fails the spawn with
    # OSError "Argument list too long" (E2BIG) and the proxy returns HTTP 500. claude -p
    # reads the prompt from stdin when no prompt positional is given.
    env = {**os.environ, "HOME": CLAUDE_HOME}

    proc = await asyncio.create_subprocess_exec(
        *args,
        stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        env=env,
    )
    try:
        stdout, stderr = await asyncio.wait_for(
            proc.communicate(input=prompt.encode("utf-8")), timeout=CLI_TIMEOUT_SECONDS)
    except asyncio.TimeoutError:
        proc.kill()
        await proc.wait()  # reap the killed process so it does not linger as a zombie
        raise RuntimeError(f"claude CLI timed out after {CLI_TIMEOUT_SECONDS} seconds")

    raw = stdout.decode(errors="replace").strip()

    data: dict | None = None
    try:
        parsed = json.loads(raw)
        if isinstance(parsed, dict):
            data = parsed
    except (json.JSONDecodeError, ValueError):
        pass

    if proc.returncode != 0:
        # Errors may appear in stdout JSON (is_error: true) rather than stderr.
        stderr_msg = stderr.decode(errors="replace").strip()
        if data and data.get("is_error"):
            raise RuntimeError(f"claude CLI error: {data.get('result', stderr_msg)}")
        raise RuntimeError(f"claude CLI exited with code {proc.returncode}: {stderr_msg or raw[:200]}")

    # claude often exits 0 even when the upstream API failed (auth 401, server 500, …);
    # the failure surfaces as is_error:true in the JSON. Treat it as an error so it maps
    # to an OpenAI error envelope instead of being returned as a "successful" result.
    if data and data.get("is_error"):
        status = data.get("api_error_status")
        raise RuntimeError(
            f"claude CLI API error (status={status}): {data.get('result', '')[:300]}"
        )

    return _extract_result_and_usage(data, raw)


def _extract_result_and_usage(data: dict | None, raw: str) -> tuple[str, UsageParts]:
    """
    claude -p --output-format json outputs a JSON object with a "result" field plus a
    "usage" block and "total_cost_usd". Maps the usage to OpenAI accounting:
    prompt_tokens counts all input (fresh + cache read + cache creation); cached_tokens is
    the cache-read subset. Falls back to the raw string with empty usage if parsing failed.
    """
    if data is None:
        return raw, UsageParts()

    u = data.get("usage") or {}
    fresh = _int(u, "input_tokens")
    cache_read = _int(u, "cache_read_input_tokens")
    cache_creation = _int(u, "cache_creation_input_tokens")
    usage = UsageParts(
        input_tokens=fresh + cache_read + cache_creation,
        output_tokens=_int(u, "output_tokens"),
        cached_tokens=cache_read,
        cost_usd=data.get("total_cost_usd"),
    )
    text = str(data["result"]) if "result" in data else raw
    return text, usage


def _int(d: dict, key: str) -> int:
    try:
        return int(d.get(key, 0) or 0)
    except (TypeError, ValueError):
        return 0
