"""Tests for ProviderConfigSync — fetches CLI provider configs from backend."""
import sys
import os

import httpx
import pytest
import respx

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from provider_sync import ProviderConfigSync


@pytest.fixture
def sync():
    return ProviderConfigSync("http://localhost:8080", "test-token")


@pytest.mark.asyncio
async def test_sync_now_loads_providers(sync):
    providers = [
        {"name": "claude-cli", "displayName": "Claude CLI", "settings": {"cli_kind": "claude"}},
        {"name": "gemini-cli", "displayName": "Gemini CLI", "settings": {"cli_kind": "gemini"}},
    ]
    with respx.mock:
        respx.get("http://localhost:8080/api/internal/providers/cli").mock(
            return_value=httpx.Response(200, json=providers)
        )
        await sync.sync_now()

    assert sync.get_provider_config("claude-cli") is not None
    assert sync.get_provider_config("gemini-cli") is not None
    assert sync.get_provider_config("unknown") is None


@pytest.mark.asyncio
async def test_sync_sends_token_header(sync):
    with respx.mock:
        route = respx.get("http://localhost:8080/api/internal/providers/cli").mock(
            return_value=httpx.Response(200, json=[])
        )
        await sync.sync_now()

    assert route.called
    assert route.calls[0].request.headers["X-Internal-Token"] == "test-token"


@pytest.mark.asyncio
async def test_sync_handles_backend_failure_gracefully(sync):
    with respx.mock:
        respx.get("http://localhost:8080/api/internal/providers/cli").mock(
            return_value=httpx.Response(500)
        )
        # Should not raise, just log warning
        await sync.sync_now()

    # Cache unchanged (empty)
    assert sync.get_provider_config("anything") is None


@pytest.mark.asyncio
async def test_sync_with_no_token_omits_header():
    no_token_sync = ProviderConfigSync("http://localhost:8080", "")
    with respx.mock:
        route = respx.get("http://localhost:8080/api/internal/providers/cli").mock(
            return_value=httpx.Response(200, json=[])
        )
        await no_token_sync.sync_now()

    assert route.called
    assert "X-Internal-Token" not in route.calls[0].request.headers


@pytest.mark.asyncio
async def test_all_provider_names_returns_loaded_names(sync):
    providers = [
        {"name": "claude-cli", "displayName": "Claude CLI", "settings": {}},
        {"name": "codex-cli", "displayName": "Codex CLI", "settings": {}},
    ]
    with respx.mock:
        respx.get("http://localhost:8080/api/internal/providers/cli").mock(
            return_value=httpx.Response(200, json=providers)
        )
        await sync.sync_now()

    names = sync.all_provider_names()
    assert "claude-cli" in names
    assert "codex-cli" in names


@pytest.mark.asyncio
async def test_sync_overwrites_stale_cache(sync):
    """Second sync should replace the first sync's data."""
    first_batch = [{"name": "claude-cli", "displayName": "Claude CLI", "settings": {}}]
    second_batch = [{"name": "gemini-cli", "displayName": "Gemini CLI", "settings": {}}]

    with respx.mock:
        respx.get("http://localhost:8080/api/internal/providers/cli").mock(
            side_effect=[
                httpx.Response(200, json=first_batch),
                httpx.Response(200, json=second_batch),
            ]
        )
        await sync.sync_now()
        assert sync.get_provider_config("claude-cli") is not None

        await sync.sync_now()

    # After second sync, only gemini-cli is in cache
    assert sync.get_provider_config("gemini-cli") is not None
    assert sync.get_provider_config("claude-cli") is None


@pytest.mark.asyncio
async def test_sync_provider_config_contains_expected_fields(sync):
    providers = [
        {
            "name": "claude-cli",
            "displayName": "Claude CLI",
            "settings": {"cli_kind": "claude", "binary": "claude"},
        }
    ]
    with respx.mock:
        respx.get("http://localhost:8080/api/internal/providers/cli").mock(
            return_value=httpx.Response(200, json=providers)
        )
        await sync.sync_now()

    cfg = sync.get_provider_config("claude-cli")
    assert cfg is not None
    assert cfg["name"] == "claude-cli"
    assert cfg["displayName"] == "Claude CLI"
    assert cfg["settings"]["cli_kind"] == "claude"
