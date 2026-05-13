"""Adapter that runs the codex CLI and returns its text output."""
from __future__ import annotations

import asyncio
import os
import tempfile

MAX_CONCURRENT = int(os.getenv("CODEX_MAX_CONCURRENT", "2"))
_semaphore = asyncio.Semaphore(MAX_CONCURRENT)

# Home directory for codex auth — mounted as a volume in the container.
CODEX_HOME = os.getenv("CODEX_HOME", "/auth/codex")

# Static model list — the codex CLI has no model-listing command.
# Update this list when new Codex/OpenAI models become available.
STATIC_MODELS = [
    "o4-mini",
    "gpt-4o",
    "gpt-4o-mini",
    "o3",
]


def list_models() -> list[str]:
    """Returns the static list of supported Codex/OpenAI models."""
    return list(STATIC_MODELS)


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
