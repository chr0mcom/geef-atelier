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
from workspace import (
    ephemeral_dir,
    finalize_decision_instruction,
    finalize_instruction,
    materialize_context,
    read_decision_file,
)

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

# Minimum spacing between forced re-fetches, and the timestamp of the last ATTEMPT. Mirrors the
# claude side so the throttle exists per provider, as the guard rule requires; a failed fetch writes
# no cache entry, so a success-based throttle would not throttle during an outage.
_MODEL_REFRESH_MIN_INTERVAL = int(os.getenv("CODEX_MODEL_REFRESH_MIN_INTERVAL", "60"))
_last_attempt_at: float | None = None

# Source markers reported alongside every model list, mirroring claude_adapter.
SOURCE_LIVE = "live"
SOURCE_DEGRADED = "degraded"
SOURCE_STATIC = "static"

# Fallback if OpenRouter is unreachable and cache is empty
_FALLBACK_MODELS = [
    "gpt-5.6-sol",
    "gpt-5.6-terra",
    "gpt-5.6-luna",
    "gpt-5.5",
]


class NoLiveSourceError(RuntimeError):
    """Raised when there is no configured way to reach the live list at all.

    Distinct from a failed fetch on purpose: an absent API key is a missing capability, not an
    outage, and reporting it as "degraded" would put a permanent red "provider unreachable" banner
    on every codex picker the moment the key is legitimately removed.
    """


async def _fetch_from_openrouter(top_n: int = 10) -> list[str]:
    """Fetches the top_n newest OpenAI models from OpenRouter, excluding image/audio/alias variants."""
    if not OPENROUTER_API_KEY:
        raise NoLiveSourceError("OPENROUTER_API_KEY not set")

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


async def _get_cached_models(bypass_cache: bool = False, force: bool = False) -> tuple[list[str], str]:
    """Returns (model ids, source).

    "live" for a fresh or cached OpenRouter answer, "degraded" when a reachable source failed, and
    "static" when no live source is configured at all. Neither non-success case is cached.
    """
    global _model_cache, _last_attempt_at
    async with _cache_lock:
        cached_fresh = _model_cache is not None and time.time() - _model_cache[1] < _CACHE_TTL

        if cached_fresh and not bypass_cache:
            return _model_cache[0], SOURCE_LIVE  # type: ignore[index]

        if bypass_cache and not force and _last_attempt_at is not None \
                and time.time() - _last_attempt_at < _MODEL_REFRESH_MIN_INTERVAL:
            # Freshness is re-checked here on purpose: reporting an EXPIRED entry as live would let
            # the throttle revive stale data, and the caller caches a live answer for another day.
            if cached_fresh:
                return _model_cache[0], SOURCE_LIVE  # type: ignore[index]
            return (_model_cache[0] if _model_cache else _FALLBACK_MODELS), SOURCE_DEGRADED

        _last_attempt_at = time.time()
        try:
            models = await _fetch_from_openrouter()
            _model_cache = (models, time.time())
            log.info("Codex model list refreshed: %s", models)
            return models, SOURCE_LIVE
        except NoLiveSourceError as exc:
            log.warning("Codex model list has no live source: %s — serving the curated list", exc)
            return (_model_cache[0] if _model_cache else _FALLBACK_MODELS), SOURCE_STATIC
        except Exception as exc:
            log.warning("Failed to fetch codex models from OpenRouter: %s — using cached/fallback", exc)
            return (_model_cache[0] if _model_cache else _FALLBACK_MODELS), SOURCE_DEGRADED


def list_models() -> list[str]:
    """Synchronous shim used at startup; returns fallback until async cache is warm."""
    return _model_cache[0] if _model_cache else _FALLBACK_MODELS


async def list_models_with_source_async(bypass_cache: bool = False, force: bool = False) -> tuple[list[str], str]:
    """Live model list plus its provenance, refreshed from OpenRouter every 24 hours.

    `force` bypasses the attempt throttle and is reserved for the internal periodic sweep."""
    return await _get_cached_models(bypass_cache, force)


async def list_models_async(bypass_cache: bool = False, force: bool = False) -> list[str]:
    """Returns live model list, refreshed from OpenRouter every 24 hours."""
    models, _source = await _get_cached_models(bypass_cache, force)
    return models


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


async def complete_agentic_file(
    instruction: str, model: str | None
) -> tuple[str, UsageParts]:
    """Agentic tool use via file authoring — the codex counterpart of
    claude_adapter.complete_agentic_file.

    The codex CLI carries its own agent persona: asked inline to "reply with raw tool-call
    JSON" for HOST-executed tools it does not know, it answers "these tools are not
    connected in this session" instead of following the protocol (incident 2026-07-24,
    Hermes case-writer via failover — zero tool calls, protocol violation, gave_up).
    Authoring decision.json is a normal workspace-write action the CLI performs without
    objection; the proxy reads the file back and returns standard OpenAI tool_calls.
    Without this, the claude→codex failover is useless for every agentic consumer."""
    async with _semaphore:
        async with ephemeral_dir() as workspace:
            return await _run_codex_decision(instruction, model, workspace)


async def _run_codex_decision(
    instruction: str, model: str | None, workspace_path: Path
) -> tuple[str, UsageParts]:
    # --sandbox workspace-write: decision.json is the only artifact we want; no host access.
    # No --search: the decision turn plans the next HOST tool call — web research inside the
    # planning step would only tempt the model to answer from its own findings instead of
    # delegating to the host's tools.
    args = [
        "codex", "exec", "--json",
        "--skip-git-repo-check",
        "--sandbox", "workspace-write",
        "-C", str(workspace_path),
    ]
    if model:
        bare_model = model.split("/")[-1] if "/" in model else model
        args += ["-m", bare_model]

    # Offload to instruction.md if the instruction would exceed the safe argv size.
    args.append(finalize_decision_instruction(workspace_path, instruction))

    env = {**os.environ, "HOME": CODEX_HOME}

    proc = await asyncio.create_subprocess_exec(
        *args,
        stdin=asyncio.subprocess.DEVNULL,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=str(workspace_path),
        env=env,
    )
    try:
        stdout, stderr = await asyncio.wait_for(proc.communicate(), timeout=CLI_TIMEOUT_SECONDS)
    except asyncio.TimeoutError:
        proc.kill()
        await proc.wait()
        raise RuntimeError(f"codex CLI (agentic file mode) timed out after {CLI_TIMEOUT_SECONDS}s")

    stdout_txt = stdout.decode(errors="replace")
    if proc.returncode != 0:
        err = stderr.decode(errors="replace").strip()
        raise RuntimeError(
            f"codex CLI (agentic file mode) exited with code {proc.returncode}: "
            f"{err or stdout_txt[:200]}"
        )

    usage = _parse_codex_usage(stdout_txt)
    decision = read_decision_file(workspace_path)
    if not decision:
        # The agent answered on the event stream instead of writing the file — use that text
        # (parse_decision treats non-JSON as a final answer, and main retries once).
        decision = _parse_codex_text(stdout_txt)
    return decision, usage


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
