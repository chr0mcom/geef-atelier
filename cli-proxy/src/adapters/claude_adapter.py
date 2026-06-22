"""ClaudeAdapter — wraps the claude CLI via the legacy claude_adapter module."""
from __future__ import annotations

from typing import Any

import claude_adapter as _cli  # top-level module in src/

from .base import CliAdapter
from .utils import format_messages as _format_messages, build_openai_response_from_parts as _build_openai_response_from_parts


class ClaudeAdapter(CliAdapter):
    async def execute(self, config: dict[str, Any], request: dict[str, Any]) -> dict[str, Any]:
        model = request.get("model", "anthropic/claude-opus-4-7")
        max_tokens = request.get("max_tokens")
        prompt = _format_messages(request.get("messages", []))

        raw, usage = await _cli.complete_with_usage(prompt, model, max_tokens)
        return _build_openai_response_from_parts(
            request, raw, usage.input_tokens, usage.output_tokens,
            cached_tokens=usage.cached_tokens, reasoning_tokens=usage.reasoning_tokens,
        )

    async def list_models(self, config: dict[str, Any]) -> list[str]:
        settings = config.get("settings", {})
        return settings.get("models", [
            "anthropic/claude-opus-4-7",
            "anthropic/claude-sonnet-4-6",
            "anthropic/claude-haiku-4-5",
        ])

    async def health_check(self, config: dict[str, Any]) -> bool:
        import shutil
        return bool(shutil.which("claude"))
