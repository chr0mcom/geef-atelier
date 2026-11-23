"""CodexAdapter — wraps the codex CLI via the legacy codex_adapter module."""
from __future__ import annotations

from typing import Any

import codex_adapter as _cli  # top-level module in src/

from .base import CliAdapter
from .utils import format_messages as _format_messages, build_openai_response_from_parts as _build_openai_response_from_parts


class CodexAdapter(CliAdapter):
    aliases_complete = _cli.ALIASES_COMPLETE

    async def execute(self, config: dict[str, Any], request: dict[str, Any]) -> dict[str, Any]:
        model = await _cli.resolve_model_alias(request.get("model", _cli.DEFAULT_CODEX_MODEL))
        if model != request.get("model"):
            request = {**request, "model": model.split("/")[-1]}
        max_tokens = request.get("max_tokens")
        prompt = _format_messages(request.get("messages", []))

        raw, usage = await _cli.complete_with_usage(prompt, model, max_tokens)
        return _build_openai_response_from_parts(
            request, raw, usage.input_tokens, usage.output_tokens,
            cached_tokens=usage.cached_tokens, reasoning_tokens=usage.reasoning_tokens,
        )

    async def list_models(self, config: dict[str, Any]) -> list[str]:
        models, _source, _aliases = await self.list_models_with_source(config)
        return models

    async def list_models_with_source(
        self, config: dict[str, Any], bypass_cache: bool = False
    ) -> tuple[list[str], str, dict[str, str]]:
        """Resolves the list from the codex CLI's own models_cache.json rather than echoing the
        synced configuration. Always-latest aliases (`gpt-sol-latest`) come first in the list,
        and the alias map says which concrete model each one resolves to right now."""
        return await _cli.list_models_with_aliases_async(bypass_cache)

    async def health_check(self, config: dict[str, Any]) -> bool:
        import shutil
        return bool(shutil.which("codex"))
