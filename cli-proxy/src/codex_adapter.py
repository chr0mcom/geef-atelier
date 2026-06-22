"""Adapter that runs the codex CLI and returns its text output."""
from __future__ import annotations

import asyncio
import json
import logging
import os
import tempfile
import time
from pathlib import Path
from typing import Any

import httpx

from usage import UsageParts
from workspace import finalize_instruction, materialize_context

log = logging.getLogger(__name__)

MAX_CONCURRENT = int(os.getenv("CODEX_MAX_CONCURRENT", "2"))
_semaphore = asyncio.Semaphore(MAX_CONCURRENT)

# Home directory for codex auth — mounted as a volume in the container.
CODEX_HOME = os.getenv("CODEX_HOME", "/auth/codex")

# Hard cap on how long a single codex CLI call may run.
# Increase for workloads that produce very long outputs (e.g. 30-page documents).
CLI_TIMEOUT_SECONDS = int(os.getenv("CLI_TIMEOUT_SECONDS", "1800"))

OPENROUTER_API_KEY = os.getenv("OPENROUTER_API_KEY", "")
_OPENROUTER_MODELS_URL = "https://openrouter.ai/api/v1/models"
_SKIP_KEYWORDS = ("image", "audio", "vision", "dall-e", "whisper", "tts", "chat-latest")
_CACHE_TTL = 86400  # 24 hours

# In-memory cache: (model_ids, fetched_at)
_model_cache: tuple[list[str], float] | None = None
_cache_lock = asyncio.Lock()

# Fallback if OpenRouter is unreachable and cache is empty
_FALLBACK_MODELS = [
    "gpt-5.5-pro",
    "gpt-5.5",
    "gpt-5.4-nano",
    "gpt-5.4-mini",
]


async def _fetch_from_openrouter(top_n: int = 10) -> list[str]:
    """Fetches the top_n newest OpenAI models from OpenRouter, excluding image/audio/alias variants."""
    if not OPENROUTER_API_KEY:
        log.warning("OPENROUTER_API_KEY not set — using fallback model list for codex")
        return _FALLBACK_MODELS

    headers = {"Authorization": f"Bearer {OPENROUTER_API_KEY}"}
    async with httpx.AsyncClient(timeout=10) as client:
        resp = await client.get(_OPENROUTER_MODELS_URL, headers=headers)
        resp.raise_for_status()
        data: list[dict[str, Any]] = resp.json().get("data", [])

    openai_models = [
        m for m in data
        if m.get("id", "").startswith("openai/")
        and not any(kw in m["id"].lower() for kw in _SKIP_KEYWORDS)
    ]
    openai_models.sort(key=lambda m: m.get("created", 0), reverse=True)
    return [m["id"].replace("openai/", "") for m in openai_models[:top_n]]


async def _get_cached_models() -> list[str]:
    global _model_cache
    async with _cache_lock:
        if _model_cache is not None and time.time() - _model_cache[1] < _CACHE_TTL:
            return _model_cache[0]
        try:
            models = await _fetch_from_openrouter()
            _model_cache = (models, time.time())
            log.info("Codex model list refreshed: %s", models)
            return models
        except Exception as exc:
            log.warning("Failed to fetch codex models from OpenRouter: %s — using cached/fallback", exc)
            return _model_cache[0] if _model_cache else _FALLBACK_MODELS


def list_models() -> list[str]:
    """Synchronous shim used at startup; returns fallback until async cache is warm."""
    return _model_cache[0] if _model_cache else _FALLBACK_MODELS


async def list_models_async() -> list[str]:
    """Returns live model list, refreshed from OpenRouter every 24 hours."""
    return await _get_cached_models()


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
    Calls the codex CLI in document-edit mode: the CLI reads draft.md in the given
    workspace, applies the instruction, and writes the revised document back.
    Returns (content of draft.md after the CLI exits, token usage).

    Uses --sandbox workspace-write so the agent can only write within the workspace.
    System prompt is embedded in the instruction preamble (codex has no --append-system-prompt).

    context_document holds large background context (grounding + advisor notes); it is
    offloaded to context.md when oversized (see workspace.materialize_context).
    """
    async with _semaphore:
        return await _run_codex_document(
            system_prompt, user_instruction, document, workspace_path, model, max_tokens,
            context_document,
        )


async def complete(prompt: str, model: str | None, max_tokens: int | None) -> str:
    """
    Calls the codex CLI in non-interactive mode and returns the raw text output.

    The semaphore limits concurrent calls to respect subscription rate limits.
    """
    text, _ = await complete_with_usage(prompt, model, max_tokens)
    return text


async def complete_with_usage(
    prompt: str, model: str | None, max_tokens: int | None
) -> tuple[str, UsageParts]:
    """Like complete(), but also returns the real token usage reported by the CLI."""
    async with _semaphore:
        return await _run_codex(prompt, model, max_tokens)


async def stream(prompt: str, model: str | None, max_tokens: int | None):
    """Stream the codex CLI output as (kind, payload) events.

    Yields ("delta", text) for agent message text and finally ("usage", UsageParts).
    codex exec --json emits the agent message as a completed item rather than token-level
    deltas, so output is emitted as one (or few) delta chunks; usage comes from turn.completed.
    """
    async with _semaphore:
        async for event in _stream_codex(prompt, model, max_tokens):
            yield event


async def _stream_codex(prompt: str, model: str | None, max_tokens: int | None):
    args = ["codex", "--search", "exec", "--json", "--skip-git-repo-check"]
    if model:
        bare_model = model.split("/")[-1] if "/" in model else model
        args += ["-m", bare_model]
    args.append("-")

    env = {**os.environ, "HOME": CODEX_HOME}
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
    usage = UsageParts()
    emitted_text = ""
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
                # Final agent message arrives as a completed item; emit the new suffix as a delta.
                item = ev.get("item")
                if isinstance(item, dict) and item.get("type") == "agent_message" and item.get("text"):
                    text = str(item["text"])
                    if text.startswith(emitted_text):
                        delta = text[len(emitted_text):]
                    else:
                        delta = text
                    if delta:
                        emitted_text = text
                        yield ("delta", delta)
                # Usage on turn.completed (last one wins).
                u = ev.get("usage")
                if isinstance(u, dict):
                    cached = _cint(u, "cached_input_tokens")
                    reasoning = _cint(u, "reasoning_output_tokens")
                    usage = UsageParts(
                        input_tokens=_cint(u, "input_tokens"),
                        output_tokens=_cint(u, "output_tokens") + reasoning,
                        cached_tokens=cached,
                        reasoning_tokens=reasoning,
                    )
        await feeder
        await proc.wait()
        if proc.returncode not in (0, None):
            err = (await proc.stderr.read()).decode(errors="replace").strip()
            raise RuntimeError(f"codex CLI (stream) exited with code {proc.returncode}: {err[:200]}")
        yield ("usage", usage)
    except TimeoutError:
        raise RuntimeError(f"codex CLI (stream) timed out after {CLI_TIMEOUT_SECONDS} seconds")
    finally:
        feeder.cancel()
        if proc.returncode is None:
            proc.kill()
            await proc.wait()


async def _run_codex_document(
    system_prompt: str,
    user_instruction: str,
    document: str,
    workspace_path: Path,
    model: str | None,
    max_tokens: int | None,
    context_document: str | None = None,
) -> tuple[str, UsageParts]:
    # Codex has no --append-system-prompt flag — embed system prompt as a [SYSTEM] preamble.
    # Large background context goes to context.md (pointer) or inline if small; the file-contract
    # and user instruction always stay in the prompt.
    context_preamble = materialize_context(workspace_path, context_document)
    instruction = (
        f"[SYSTEM]\n{system_prompt}\n\n"
        + context_preamble
        + "The document you are editing is located at draft.md in the current directory. "
        "Read it, apply the revisions described below, then write the complete updated "
        "document back to draft.md.\n\n"
        + user_instruction
    )

    # --sandbox workspace-write: restricts writes to the workspace directory.
    # -C <workspace>: sets the agent's workspace root (not just subprocess cwd).
    # --skip-git-repo-check: the proxy container is not a git repo.
    # --search is a GLOBAL flag and must precede the `exec` subcommand.
    # --json: emit the JSONL event stream so we can read real token usage; the document
    # text itself is read from draft.md, not from stdout.
    args = [
        "codex", "--search", "exec", "--json",
        "--skip-git-repo-check",
        "--sandbox", "workspace-write",
        "-C", str(workspace_path),
    ]

    if model:
        bare_model = model.split("/")[-1] if "/" in model else model
        args += ["-m", bare_model]

    # Offload to instruction.md if the instruction would exceed the per-argument OS limit.
    args.append(finalize_instruction(workspace_path, instruction))

    env = {**os.environ, "HOME": CODEX_HOME}

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
        raise RuntimeError(f"codex CLI (document mode) timed out after {CLI_TIMEOUT_SECONDS}s")

    if proc.returncode != 0:
        err = stderr.decode(errors="replace").strip()
        raise RuntimeError(f"codex CLI (document mode) exited with code {proc.returncode}: {err}")

    draft_path = workspace_path / "draft.md"
    try:
        result = draft_path.read_text(encoding="utf-8")
    except FileNotFoundError:
        raise RuntimeError(
            "codex document mode: draft.md not found after CLI exit — "
            "the agent may have deleted or moved the file"
        )

    if result == document:
        log.warning("codex document mode: draft.md unchanged after CLI run — no edits applied")

    usage = _parse_codex_usage(stdout.decode(errors="replace"))
    return result, usage


async def _run_codex(
    prompt: str, model: str | None, max_tokens: int | None
) -> tuple[str, UsageParts]:
    # codex exec writes the last message to a file; use a temp file to capture it.
    with tempfile.NamedTemporaryFile(mode="r", suffix=".txt", delete=False) as tmp:
        output_file = tmp.name

    try:
        # --skip-git-repo-check: the proxy container is not a git repo, and codex
        # exec otherwise refuses to run ("Not inside a trusted directory").
        # --search is a GLOBAL flag and must precede the `exec` subcommand
        # (codex exec rejects it). It enables the native Responses web_search
        # tool with no per-call approval.
        # --json: emit the JSONL event stream on stdout so we can read the real token
        # usage from the final turn.completed event. The final message text is still
        # captured reliably via --output-last-message (independent of stdout format).
        args = ["codex", "--search", "exec", "--json", "--skip-git-repo-check"]

        if model:
            bare_model = model.split("/")[-1] if "/" in model else model
            args += ["-m", bare_model]

        # Pass the prompt through stdin ("-"), never as an argv element. A single execve
        # argument may not exceed MAX_ARG_STRLEN (128 KB on Linux); reviewer/advisor prompts
        # embed the full draft (often >128 KB by later iterations), so an argv prompt fails
        # the spawn with OSError "Argument list too long" (E2BIG) and the proxy returns HTTP
        # 500. codex reads instructions from stdin when the prompt positional is "-".
        args += ["--output-last-message", output_file, "-"]

        env = {**os.environ, "HOME": CODEX_HOME}

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
            raise RuntimeError(f"codex CLI timed out after {CLI_TIMEOUT_SECONDS} seconds")

        if proc.returncode != 0:
            err = stderr.decode(errors="replace").strip()
            raise RuntimeError(f"codex CLI exited with code {proc.returncode}: {err}")

        usage = _parse_codex_usage(stdout.decode(errors="replace"))

        try:
            with open(output_file) as f:
                text = f.read().strip()
        except FileNotFoundError:
            text = ""

        # Fallback: if --output-last-message produced nothing, recover the final
        # agent_message text from the JSONL event stream.
        if not text:
            text = _parse_codex_text(stdout.decode(errors="replace"))

        return text, usage
    finally:
        try:
            os.unlink(output_file)
        except OSError:
            pass


def _parse_codex_usage(stdout: str) -> UsageParts:
    """
    Reads token usage from the codex exec --json event stream. The final
    `turn.completed` event carries a `usage` block:
    {"input_tokens", "cached_input_tokens", "output_tokens", "reasoning_output_tokens"}.
    The last such event wins (a turn may be retried). Returns empty usage if absent.
    """
    usage = UsageParts()
    for line in stdout.splitlines():
        line = line.strip()
        if not line or '"usage"' not in line:
            continue
        try:
            event = json.loads(line)
        except (json.JSONDecodeError, ValueError):
            continue
        u = event.get("usage")
        if not isinstance(u, dict):
            continue
        cached = _cint(u, "cached_input_tokens")
        reasoning = _cint(u, "reasoning_output_tokens")
        usage = UsageParts(
            # codex input_tokens already includes the cached subset.
            input_tokens=_cint(u, "input_tokens"),
            # OpenAI completion_tokens includes reasoning tokens; codex reports them separately.
            output_tokens=_cint(u, "output_tokens") + reasoning,
            cached_tokens=cached,
            reasoning_tokens=reasoning,
        )
    return usage


def _parse_codex_text(stdout: str) -> str:
    """Recover the last agent_message text from the codex exec --json event stream."""
    text = ""
    for line in stdout.splitlines():
        line = line.strip()
        if not line or "agent_message" not in line:
            continue
        try:
            event = json.loads(line)
        except (json.JSONDecodeError, ValueError):
            continue
        item = event.get("item")
        if isinstance(item, dict) and item.get("type") == "agent_message" and item.get("text"):
            text = str(item["text"])
    return text.strip()


def _cint(d: dict, key: str) -> int:
    try:
        return int(d.get(key, 0) or 0)
    except (TypeError, ValueError):
        return 0
