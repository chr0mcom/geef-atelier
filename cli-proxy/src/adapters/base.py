"""Abstract base class for CLI adapters."""
from __future__ import annotations

from abc import ABC, abstractmethod
from typing import Any


class CliAdapter(ABC):
    @abstractmethod
    async def execute(
        self, config: dict[str, Any], request: dict[str, Any]
    ) -> dict[str, Any]:
        """Execute a chat completion request. Returns OpenAI-format response dict."""
        ...

    @abstractmethod
    async def list_models(self, config: dict[str, Any]) -> list[str]:
        """Return list of supported model names for this provider."""
        ...

    async def list_models_with_source(
        self, config: dict[str, Any], bypass_cache: bool = False
    ) -> tuple[list[str], str, dict[str, str]]:
        """Return (models, source, alias map) for this provider.

        The source tells the caller whether the list was resolved from the CLI itself
        ("live"), comes from a provider that has no live source at all ("static"), or is a
        substitute served because a live lookup failed ("degraded"). Adapters without a live
        source inherit this default, which reports their configured list as "static" — an
        authoritative answer, not a degradation.
        """
        return await self.list_models(config), "static", {}

    async def health_check(self, config: dict[str, Any]) -> bool:
        """Check if the CLI is available. Default: check binary exists."""
        import shutil
        binary = config.get("settings", {}).get("binary", "")
        return bool(shutil.which(binary))
