"""GenericAdapter — Custom CLI support via configurable binary + args template."""
from __future__ import annotations

import asyncio
import json
import os
import shutil
from typing import Any

from .base import CliAdapter
from .utils import format_messages as _format_messages, build_openai_response_from_parts as _build_openai_response_from_parts


class GenericAdapter(CliAdapter):
    async def execute(self, config: dict[str, Any], request: dict[str, Any]) -> dict[str, Any]:
        settings = config.get("settings", {})
        binary = settings.get("binary", "")
        if not binary:
            raise ValueError("Generic adapter requires 'binary' in settings")

        model = request.get("model", "")
        prompt = _format_messages(request.get("messages", []))

        stdin_data: bytes | None = None

        if settings.get("stdin_mode"):
            # In stdin mode the prompt goes via stdin — no prompt positional arg.
            cmd = [binary]
            stdin_data = prompt.encode()
        else:
            # Build command from args template.
            args_template: list[str] = settings.get("prompt_args_template", ["{prompt}"])
            cmd = [binary]
            for tmpl in args_template:
                arg = tmpl.replace("{prompt}", prompt).replace("{model}", model)
                cmd.append(arg)

        env = os.environ.copy()
        for env_key, env_val in (settings.get("auth_env_vars") or {}).items():
            if env_val:
                env[env_key] = env_val

        proc = await asyncio.create_subprocess_exec(
            *cmd,
            stdin=asyncio.subprocess.PIPE if stdin_data else None,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
            env=env,
        )
        stdout, stderr = await asyncio.wait_for(proc.communicate(input=stdin_data), timeout=300)

        if proc.returncode != 0:
            raise RuntimeError(f"{binary} exited {proc.returncode}: {stderr.decode()[:500]}")

        raw = stdout.decode()
        output_format = settings.get("output_format", "text")

        if output_format == "text":
            content = raw.strip()
        elif output_format == "openai-json":
            data = json.loads(raw)
            content = data["choices"][0]["message"]["content"]
        elif output_format == "jsonl":
            lines = [line for line in raw.strip().split("\n") if line.strip()]
            data = json.loads(lines[-1])
            json_path = settings.get("output_json_path", "response")
            content = data.get(json_path, raw.strip())
        else:
            content = raw.strip()

        return _build_openai_response_from_parts(request, content, 0, 0)

    async def list_models(self, config: dict[str, Any]) -> list[str]:
        settings = config.get("settings", {})
        return settings.get("models", [])

    async def health_check(self, config: dict[str, Any]) -> bool:
        settings = config.get("settings", {})
        binary = settings.get("binary", "")
        return bool(binary and shutil.which(binary))
