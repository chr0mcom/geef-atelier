"""Background sync of CLI provider configs from the Atelier backend."""
from __future__ import annotations

import asyncio
import logging

import httpx

logger = logging.getLogger(__name__)


def _built_in(name: str, display_name: str, **settings: object) -> dict:
    return {"name": name, "displayName": display_name, "settings": settings}


# The CLI providers that ship with the Atelier (SystemProviders.cs of the web app; a test compares the two). They apply
# until a first sync succeeds, so that they keep working, model lists included, when the web app refuses the sync or
# cannot be reached. From then on only the synced list counts: a provider switched off in the web app stays off.
BUILT_IN_PROVIDERS: dict[str, dict] = {
    config["name"]: config
    for config in (
        _built_in("claude-cli", "Claude Code CLI", cli_kind="claude", binary="claude", auth_volume="/auth/claude"),
        _built_in("codex-cli", "Codex CLI", cli_kind="codex", binary="codex", auth_volume="/auth/codex"),
        _built_in("gemini-cli", "Gemini CLI", cli_kind="gemini", binary="gemini", auth_volume="/auth/gemini",
                  auth_env_var_alternative="GEMINI_API_KEY",
                  models=["gemini-2-5-pro", "gemini-2-5-flash", "gemini-3-1-pro", "gemini-3-1-flash"]),
    )
}


class ProviderConfigSync:
    def __init__(self, backend_url: str, internal_token: str = "") -> None:
        self._backend_url = backend_url.rstrip("/")
        self._token = internal_token
        self._configs: dict[str, dict] = {}
        self._synced = False
        self._lock = asyncio.Lock()

    async def sync_now(self) -> None:
        """Fetch CLI provider configs from backend and update cache."""
        url = f"{self._backend_url}/api/internal/providers/cli"
        headers: dict[str, str] = {}
        if self._token:
            headers["X-Internal-Token"] = self._token
        try:
            async with httpx.AsyncClient(timeout=10.0) as client:
                resp = await client.get(url, headers=headers)
                resp.raise_for_status()
                providers: list[dict] = resp.json()
            async with self._lock:
                self._configs = {p["name"]: p for p in providers}
                self._synced = True
            logger.info("Provider config sync: loaded %d CLI providers", len(providers))
        except httpx.HTTPStatusError as exc:
            if exc.response.status_code == 401:
                logger.warning(
                    "Provider config sync refused (401): set ATELIER_INTERNAL_API_TOKEN to the same value for the web "
                    "app and the gateway. The built-in CLI providers keep working; own CLI providers need the sync.")
            else:
                logger.warning("Provider config sync failed: %s", exc)
        except Exception as exc:
            logger.warning("Provider config sync failed: %s", exc)

    async def background_sync_loop(self, interval: int = 60) -> None:
        """Run sync_now every `interval` seconds."""
        while True:
            await asyncio.sleep(interval)
            await self.sync_now()

    def get_provider_config(self, name: str) -> dict | None:
        """Get a cached provider config by name; before the first successful sync, the built-in one."""
        return (self._configs if self._synced else BUILT_IN_PROVIDERS).get(name)

    def all_provider_names(self) -> list[str]:
        """Return the names of the synced providers."""
        return list(self._configs)
