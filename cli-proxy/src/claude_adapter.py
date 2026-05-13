"""Adapter that runs the claude CLI and returns its text output."""
from __future__ import annotations

import asyncio
import json
import os

MAX_CONCURRENT = int(os.getenv("CLAUDE_MAX_CONCURRENT", "2"))
_semaphore = asyncio.Semaphore(MAX_CONCURRENT)

# Home directory for claude auth — mounted as a volume in the container.
CLAUDE_HOME = os.getenv("CLAUDE_HOME", "/auth/claude")

# Static model list — the claude CLI has no model-listing command.
# Update this list when new Claude models become available.
STATIC_MODELS = [
    "claude-opus-4-7",
    "claude-sonnet-4-6",
    "claude-haiku-4-5",
]


def list_models() -> list[str]:
    """Returns the static list of supported Claude models."""
    return list(STATIC_MODELS)


async def complete(prompt: str, model: str | None, max_tokens: int | None) -> str:
    """
    Calls the claude CLI in print mode and returns the raw text output.

    The semaphore limits concurrent calls to respect subscription rate limits.
    """
    async with _semaphore:
        return await _run_claude(prompt, model, max_tokens)


async def _run_claude(prompt: str, model: str | None, max_tokens: int | None) -> str:
    args = ["claude", "-p", "--output-format", "json"]

    if model:
        # Strip provider prefix (e.g. "anthropic/claude-opus-4-5" → "claude-opus-4-5").
        bare_model = model.split("/")[-1] if "/" in model else model
        args += ["--model", bare_model]

    if max_tokens:
        args += ["--max-tokens", str(max_tokens)]

    args.append(prompt)

    env = {**os.environ, "HOME": CLAUDE_HOME}

    proc = await asyncio.create_subprocess_exec(
        *args,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        env=env,
    )
    stdout, stderr = await proc.communicate()

    if proc.returncode != 0:
        err = stderr.decode(errors="replace").strip()
        raise RuntimeError(f"claude CLI exited with code {proc.returncode}: {err}")

    raw = stdout.decode(errors="replace").strip()
    return _extract_result(raw)


def _extract_result(raw: str) -> str:
    """
    claude -p --output-format json outputs a JSON object with a "result" field.
    Falls back to returning the raw string if parsing fails.
    """
    try:
        data = json.loads(raw)
        if isinstance(data, dict) and "result" in data:
            return str(data["result"])
    except (json.JSONDecodeError, ValueError):
        pass
    return raw
