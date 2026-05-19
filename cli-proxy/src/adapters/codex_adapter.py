"""CodexAdapter — wraps the codex CLI via the legacy codex_adapter module."""
from __future__ import annotations

from typing import Any

import codex_adapter as _cli  # top-level module in src/

from .base import CliAdapter
from .utils import format_messages as _format_messages, build_openai_response_from_parts as _build_openai_response_from_parts


class CodexAdapter(CliAdapter):
    async def execute(self, config: dict[str, Any], request: dict[str, Any]) -> dict[str, Any]:
        model = request.get("model", "openai/gpt-5.5")
        max_tokens = request.get("max_tokens")
        prompt = _format_messages(request.get("messages", []))

        raw = await _cli.complete(prompt, model, max_tokens)
        return _build_openai_response_from_parts(request, raw, 0, 0)

    async def list_models(self, config: dict[str, Any]) -> list[str]:
        settings = config.get("settings", {})
        return settings.get("models", [
            "openai/gpt-5.5",
            "openai/gpt-4o",
        ])

    async def health_check(self, config: dict[str, Any]) -> bool:
        import shutil
        return bool(shutil.which("codex"))
