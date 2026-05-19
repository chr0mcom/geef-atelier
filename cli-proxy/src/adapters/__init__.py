"""CLI adapter registry."""
from __future__ import annotations

from .base import CliAdapter  # noqa: F401 (re-export)
from .claude_adapter import ClaudeAdapter
from .codex_adapter import CodexAdapter
from .gemini_adapter import GeminiAdapter
from .generic_adapter import GenericAdapter

_ADAPTERS: dict[str, CliAdapter] = {
    "claude": ClaudeAdapter(),
    "codex": CodexAdapter(),
    "gemini": GeminiAdapter(),
    "generic": GenericAdapter(),
}


def get_adapter(cli_kind: str) -> CliAdapter:
    """Return the registered adapter for the given cli_kind.

    Raises ValueError for unknown cli_kind values.
    """
    adapter = _ADAPTERS.get(cli_kind)
    if not adapter:
        raise ValueError(f"Unknown cli_kind: {cli_kind!r}. Valid: {list(_ADAPTERS)}")
    return adapter
