"""GeminiAdapter — wraps the gemini CLI."""
from __future__ import annotations

import asyncio
import json
import os
import shutil
from typing import Any

from .base import CliAdapter
from .utils import format_messages as _format_messages, build_openai_response_from_parts as _build_openai_response_from_parts


class GeminiAdapter(CliAdapter):
    async def execute(self, config: dict[str, Any], request: dict[str, Any]) -> dict[str, Any]:
        settings = config.get("settings", {})
        binary = settings.get("binary", "gemini")
        model = request.get("model", "google/gemini-2.5-pro")
        cli_model = model.split("/")[-1] if "/" in model else model

        prompt = _format_messages(request.get("messages", []))

        cmd = [binary, prompt, "-m", cli_model, "-y", "--output-format", "json"]

        env = os.environ.copy()
        api_key_env = settings.get("auth_env_var_alternative")
        if api_key_env and os.getenv(api_key_env):
            env["GEMINI_API_KEY"] = os.getenv(api_key_env)  # type: ignore[assignment]

        auth_volume = settings.get("auth_volume", "/auth/gemini")
        if os.path.isdir(auth_volume):
            env["HOME"] = auth_volume

        proc = await asyncio.create_subprocess_exec(
            *cmd,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
            env=env,
        )
        stdout, stderr = await asyncio.wait_for(proc.communicate(), timeout=300)

        if proc.returncode != 0:
            raise RuntimeError(f"gemini exited {proc.returncode}: {stderr.decode()[:500]}")

        try:
            data = json.loads(stdout.decode())
            content = data.get("response", stdout.decode().strip())
            input_tokens = 0
            output_tokens = 0
            if "stats" in data and "models" in data["stats"]:
                for model_stats in data["stats"]["models"].values():
                    tokens = model_stats.get("tokens", {})
                    input_tokens += tokens.get("prompt", 0)
                    output_tokens += tokens.get("candidates", 0)
        except json.JSONDecodeError:
            content = stdout.decode().strip()
            input_tokens = 0
            output_tokens = 0

        return _build_openai_response_from_parts(request, content, input_tokens, output_tokens)

    async def list_models(self, config: dict[str, Any]) -> list[str]:
        settings = config.get("settings", {})
        return settings.get("models", [
            "google/gemini-2.5-pro",
            "google/gemini-2.5-flash",
        ])

    async def health_check(self, config: dict[str, Any]) -> bool:
        settings = config.get("settings", {})
        binary = settings.get("binary", "gemini")
        return bool(shutil.which(binary))
