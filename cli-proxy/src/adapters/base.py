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

    async def health_check(self, config: dict[str, Any]) -> bool:
        """Check if the CLI is available. Default: check binary exists."""
        import shutil
        binary = config.get("settings", {}).get("binary", "")
        return bool(shutil.which(binary))
