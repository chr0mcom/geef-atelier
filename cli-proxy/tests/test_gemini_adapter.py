"""Tests for GeminiAdapter using mocked subprocess."""
import asyncio
import json
import sys
import os
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from adapters.gemini_adapter import GeminiAdapter


@pytest.fixture
def adapter():
    return GeminiAdapter()


@pytest.fixture
def config():
    return {
        "name": "gemini-cli",
        "settings": {
            "cli_kind": "gemini",
            "binary": "gemini",
            "max_concurrent": 2,
            "models": ["google/gemini-2.5-pro"],
            "auth_volume": "/auth/gemini",
        },
    }


def _make_proc(stdout: bytes, returncode: int = 0, stderr: bytes = b"") -> MagicMock:
    proc = MagicMock()
    proc.returncode = returncode
    proc.communicate = AsyncMock(return_value=(stdout, stderr))
    return proc


@pytest.mark.asyncio
async def test_list_models_returns_from_settings(adapter, config):
    models = await adapter.list_models(config)
    assert "google/gemini-2.5-pro" in models


@pytest.mark.asyncio
async def test_list_models_returns_defaults_when_no_models_key(adapter):
    cfg = {"name": "gemini-cli", "settings": {"cli_kind": "gemini", "binary": "gemini"}}
    models = await adapter.list_models(cfg)
    assert len(models) >= 1
    assert any("gemini" in m for m in models)


@pytest.mark.asyncio
async def test_execute_parses_json_response(adapter, config):
    mock_response = json.dumps({
        "response": "Hello, world!",
        "stats": {
            "models": {
                "gemini-2.5-pro": {
                    "tokens": {"prompt": 10, "candidates": 20}
                }
            }
        },
    }).encode()

    proc = _make_proc(mock_response)
    with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=proc)):
        request = {
            "model": "google/gemini-2.5-pro",
            "messages": [{"role": "user", "content": "Hi"}],
        }
        result = await adapter.execute(config, request)

    assert result["choices"][0]["message"]["content"] == "Hello, world!"
    assert result["usage"]["prompt_tokens"] == 10
    assert result["usage"]["completion_tokens"] == 20


@pytest.mark.asyncio
async def test_execute_handles_plain_text_fallback(adapter, config):
    """When gemini output is not JSON, use raw text as content."""
    proc = _make_proc(b"Plain text response")
    with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=proc)):
        request = {
            "model": "google/gemini-2.5-pro",
            "messages": [{"role": "user", "content": "Hi"}],
        }
        result = await adapter.execute(config, request)

    assert result["choices"][0]["message"]["content"] == "Plain text response"


@pytest.mark.asyncio
async def test_execute_raises_on_nonzero_exit(adapter, config):
    proc = _make_proc(b"", returncode=1, stderr=b"auth required")
    with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=proc)):
        request = {
            "model": "google/gemini-2.5-pro",
            "messages": [{"role": "user", "content": "Hi"}],
        }
        with pytest.raises(RuntimeError, match="gemini exited 1"):
            await adapter.execute(config, request)


@pytest.mark.asyncio
async def test_execute_strips_provider_prefix_from_model(adapter, config):
    """Model 'google/gemini-2.5-pro' should pass 'gemini-2.5-pro' to the CLI."""
    captured: list[list] = []

    async def fake_exec(*args, **kwargs):
        captured.append(list(args))
        proc = MagicMock()
        proc.returncode = 0
        proc.communicate = AsyncMock(return_value=(b"ok", b""))
        return proc

    with patch("asyncio.create_subprocess_exec", side_effect=fake_exec):
        request = {
            "model": "google/gemini-2.5-pro",
            "messages": [{"role": "user", "content": "Hi"}],
        }
        await adapter.execute(config, request)

    args = captured[0]
    assert "gemini-2.5-pro" in args
    assert "google/gemini-2.5-pro" not in args


@pytest.mark.asyncio
async def test_execute_accumulates_tokens_from_multiple_models(adapter, config):
    """Token counts from multiple model entries in stats should be summed."""
    mock_response = json.dumps({
        "response": "Combined response",
        "stats": {
            "models": {
                "gemini-2.5-pro": {"tokens": {"prompt": 5, "candidates": 10}},
                "gemini-2.5-flash": {"tokens": {"prompt": 3, "candidates": 7}},
            }
        },
    }).encode()

    proc = _make_proc(mock_response)
    with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=proc)):
        request = {
            "model": "google/gemini-2.5-pro",
            "messages": [{"role": "user", "content": "Hi"}],
        }
        result = await adapter.execute(config, request)

    assert result["usage"]["prompt_tokens"] == 8
    assert result["usage"]["completion_tokens"] == 17
