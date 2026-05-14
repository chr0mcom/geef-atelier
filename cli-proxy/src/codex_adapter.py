"""Adapter that runs the codex CLI and returns its text output."""
from __future__ import annotations

import asyncio
import logging
import os
import tempfile
import time
from typing import Any

import httpx

log = logging.getLogger(__name__)

MAX_CONCURRENT = int(os.getenv("CODEX_MAX_CONCURRENT", "2"))
_semaphore = asyncio.Semaphore(MAX_CONCURRENT)

# Home directory for codex auth — mounted as a volume in the container.
CODEX_HOME = os.getenv("CODEX_HOME", "/auth/codex")

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


async def complete(prompt: str, model: str | None, max_tokens: int | None) -> str:
    """
    Calls the codex CLI in non-interactive mode and returns the raw text output.

    The semaphore limits concurrent calls to respect subscription rate limits.
    """
    async with _semaphore:
        return await _run_codex(prompt, model, max_tokens)


async def _run_codex(prompt: str, model: str | None, max_tokens: int | None) -> str:
    # codex exec writes the last message to a file; use a temp file to capture it.
    with tempfile.NamedTemporaryFile(mode="r", suffix=".txt", delete=False) as tmp:
        output_file = tmp.name

    try:
        args = ["codex", "exec"]

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
        _, stderr = await proc.communicate()

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
