"""Adapter that runs the codex CLI and returns its text output."""
from __future__ import annotations

import asyncio
import logging
import os
import tempfile
import time
from pathlib import Path
from typing import Any

import httpx

from workspace import materialize_context

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
) -> str:
    """
    Calls the codex CLI in document-edit mode: the CLI reads draft.md in the given
    workspace, applies the instruction, and writes the revised document back.
    Returns the content of draft.md after the CLI exits.

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
    async with _semaphore:
        return await _run_codex(prompt, model, max_tokens)


async def _run_codex_document(
    system_prompt: str,
    user_instruction: str,
    document: str,
    workspace_path: Path,
    model: str | None,
    max_tokens: int | None,
    context_document: str | None = None,
) -> str:
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
    # No --output-last-message: we read draft.md directly, not the last chat turn.
    args = [
        "codex", "--search", "exec",
        "--skip-git-repo-check",
        "--sandbox", "workspace-write",
        "-C", str(workspace_path),
    ]

    if model:
        bare_model = model.split("/")[-1] if "/" in model else model
        args += ["-m", bare_model]

    args.append(instruction)

    env = {**os.environ, "HOME": CODEX_HOME}

    proc = await asyncio.create_subprocess_exec(
        *args,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=str(workspace_path),
        env=env,
    )
    try:
        _, stderr = await asyncio.wait_for(proc.communicate(), timeout=CLI_TIMEOUT_SECONDS)
    except asyncio.TimeoutError:
        proc.kill()
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

    return result


async def _run_codex(prompt: str, model: str | None, max_tokens: int | None) -> str:
    # codex exec writes the last message to a file; use a temp file to capture it.
    with tempfile.NamedTemporaryFile(mode="r", suffix=".txt", delete=False) as tmp:
        output_file = tmp.name

    try:
        # --skip-git-repo-check: the proxy container is not a git repo, and codex
        # exec otherwise refuses to run ("Not inside a trusted directory").
        # --search is a GLOBAL flag and must precede the `exec` subcommand
        # (codex exec rejects it). It enables the native Responses web_search
        # tool with no per-call approval.
        args = ["codex", "--search", "exec", "--skip-git-repo-check"]

        if model:
            bare_model = model.split("/")[-1] if "/" in model else model
            args += ["-m", bare_model]

        args += ["--output-last-message", output_file, prompt]

        env = {**os.environ, "HOME": CODEX_HOME}

        proc = await asyncio.create_subprocess_exec(
            *args,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
            env=env,
        )
        try:
            _, stderr = await asyncio.wait_for(proc.communicate(), timeout=CLI_TIMEOUT_SECONDS)
        except asyncio.TimeoutError:
            proc.kill()
            raise RuntimeError(f"codex CLI timed out after {CLI_TIMEOUT_SECONDS} seconds")

        if proc.returncode != 0:
            err = stderr.decode(errors="replace").strip()
            raise RuntimeError(f"codex CLI exited with code {proc.returncode}: {err}")

        try:
            with open(output_file) as f:
                return f.read().strip()
        except FileNotFoundError:
            return ""
    finally:
        try:
            os.unlink(output_file)
        except OSError:
            pass
