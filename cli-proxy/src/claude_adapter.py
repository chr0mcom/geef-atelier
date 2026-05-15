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
        # Strip provider prefix and normalize dots to dashes
        # (e.g. "anthropic/claude-opus-4.7" → "claude-opus-4-7").
        bare_model = model.split("/")[-1] if "/" in model else model
        bare_model = bare_model.replace(".", "-")
        args += ["--model", bare_model]

    args.append(prompt)

    env = {**os.environ, "HOME": CLAUDE_HOME}

    proc = await asyncio.create_subprocess_exec(
        *args,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        env=env,
    )
    try:
        stdout, stderr = await asyncio.wait_for(proc.communicate(), timeout=270)
    except asyncio.TimeoutError:
        proc.kill()
        raise RuntimeError("claude CLI timed out after 4.5 minutes")

    raw = stdout.decode(errors="replace").strip()

    if proc.returncode != 0:
        # Errors may appear in stdout JSON (is_error: true) rather than stderr.
        stderr_msg = stderr.decode(errors="replace").strip()
        try:
            data = json.loads(raw)
            if isinstance(data, dict) and data.get("is_error"):
                raise RuntimeError(f"claude CLI error: {data.get('result', stderr_msg)}")
        except (json.JSONDecodeError, ValueError):
            pass
        raise RuntimeError(f"claude CLI exited with code {proc.returncode}: {stderr_msg or raw[:200]}")

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
