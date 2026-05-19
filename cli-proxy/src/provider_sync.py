"""Background sync of CLI provider configs from the Atelier backend."""
from __future__ import annotations

import asyncio
import logging

import httpx

logger = logging.getLogger(__name__)


class ProviderConfigSync:
    def __init__(self, backend_url: str, internal_token: str = "") -> None:
        self._backend_url = backend_url.rstrip("/")
        self._token = internal_token
        self._configs: dict[str, dict] = {}
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
            logger.info("Provider config sync: loaded %d CLI providers", len(providers))
        except Exception as exc:
            logger.warning("Provider config sync failed: %s", exc)

    async def background_sync_loop(self, interval: int = 60) -> None:
        """Run sync_now every `interval` seconds."""
        while True:
            await asyncio.sleep(interval)
            await self.sync_now()

    def get_provider_config(self, name: str) -> dict | None:
        """Get a cached provider config by name."""
        return self._configs.get(name)

    def all_provider_names(self) -> list[str]:
        """Return all cached provider names."""
        return list(self._configs)
