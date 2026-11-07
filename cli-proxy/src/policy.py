"""What a call to the gateway may use beyond plain text generation.

The gateway should behave like any other OpenAI-compatible API: a request carries everything
the model needs, nothing is remembered between calls, and the model does not browse unless
the caller asks for it. This module holds the two pieces of that policy that are shared by
all adapters:

* **Web search** is off by default. A caller opts in per request with the standard OpenAI
  parameter ``web_search_options`` (any object, usually ``{}``). The operator can switch the
  default with ``CLI_WEB_SEARCH=1`` (the behaviour before 2026-11-07).
* **Subprocess hygiene**: no session transcript, no auto-memory. ``agent_env`` is the one
  place that builds the environment of a CLI process.
"""

from __future__ import annotations

import os
from contextvars import ContextVar

WEB_SEARCH_ENV = "CLI_WEB_SEARCH"

_web_search_override: ContextVar[bool | None] = ContextVar("web_search_override", default=None)


def _env_flag(name: str, default: bool) -> bool:
    raw = os.getenv(name)
    if raw is None or raw.strip() == "":
        return default
    return raw.strip().lower() in ("1", "true", "yes", "on")


def web_search_default() -> bool:
    """Operator default (``CLI_WEB_SEARCH``); off unless set."""
    return _env_flag(WEB_SEARCH_ENV, False)


def set_web_search(requested: bool) -> None:
    """Record the choice of the current request (call once per request, before the adapters)."""
    _web_search_override.set(True if requested else None)


def web_search_allowed() -> bool:
    """Whether the current request may use the CLI's web search and fetch tools."""
    return _web_search_override.get() is True or web_search_default()


def agent_env(home: str) -> dict[str, str]:
    """Environment of a CLI subprocess: the account home plus no auto-memory.

    Claude Code loads per-directory memory notes into every session unless
    ``CLAUDE_CODE_DISABLE_AUTO_MEMORY`` is set. They are the operator's personal notes and
    must never reach a caller's prompt.
    """
    return {**os.environ, "HOME": home, "CLAUDE_CODE_DISABLE_AUTO_MEMORY": "1"}
