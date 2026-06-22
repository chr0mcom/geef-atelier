"""Tests for the explicit /v1/claude and /v1/codex endpoints and legacy deprecation."""
from __future__ import annotations

import sys
import os
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import main  # noqa: E402  (must import after path setup)
from usage import UsageParts  # noqa: E402


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


def _claude_ok(text: str = "Claude response") -> AsyncMock:
    return AsyncMock(return_value=(text, UsageParts(input_tokens=7, output_tokens=3)))


def _codex_ok(text: str = "Codex response") -> AsyncMock:
    return AsyncMock(return_value=(text, UsageParts(input_tokens=7, output_tokens=3)))


_CHAT_BODY = {
    "model": "gpt-4o",  # model name that would normally route to codex
    "messages": [{"role": "user", "content": "Hello"}],
}


class TestClaudeEndpoint:
    def test_routes_to_claude_adapter_regardless_of_model_name(self, client: TestClient) -> None:
        """POST /v1/claude/chat/completions always calls claude_adapter, ignoring model name."""
        with (
            patch.object(main.claude_adapter, "complete_with_usage", _claude_ok("hi from claude")),
            patch.object(main.codex_adapter, "complete_with_usage", AsyncMock(side_effect=AssertionError("codex must not be called"))),
        ):
            resp = client.post("/v1/claude/chat/completions", json=_CHAT_BODY)

        assert resp.status_code == 200
        data = resp.json()
        assert data["choices"][0]["message"]["content"] == "hi from claude"
        assert data["usage"]["total_tokens"] == 10

    def test_claude_endpoint_ignores_openai_model_prefix(self, client: TestClient) -> None:
        """Even with an OpenAI model name, /v1/claude routes to claude CLI."""
        body = {**_CHAT_BODY, "model": "gpt-4o"}
        with (
            patch.object(main.claude_adapter, "complete_with_usage", _claude_ok("claude handled openai model")),
            patch.object(main.codex_adapter, "complete_with_usage", AsyncMock(side_effect=AssertionError("codex must not be called"))),
        ):
            resp = client.post("/v1/claude/chat/completions", json=body)

        assert resp.status_code == 200

    def test_claude_endpoint_returns_502_on_cli_failure(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=RuntimeError("auth error"))):
            resp = client.post("/v1/claude/chat/completions", json=_CHAT_BODY)

        assert resp.status_code == 502


class TestCodexEndpoint:
    def test_routes_to_codex_adapter_regardless_of_model_name(self, client: TestClient) -> None:
        """POST /v1/codex/chat/completions always calls codex_adapter, ignoring model name."""
        claude_body = {**_CHAT_BODY, "model": "claude-opus-4-5"}  # would normally route to claude
        with (
            patch.object(main.codex_adapter, "complete_with_usage", _codex_ok("hi from codex")),
            patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=AssertionError("claude must not be called"))),
        ):
            resp = client.post("/v1/codex/chat/completions", json=claude_body)

        assert resp.status_code == 200
        data = resp.json()
        assert data["choices"][0]["message"]["content"] == "hi from codex"

    def test_codex_endpoint_ignores_claude_model_prefix(self, client: TestClient) -> None:
        """Even with a Claude model name, /v1/codex routes to codex CLI."""
        body = {**_CHAT_BODY, "model": "claude-sonnet-4-5"}
        with (
            patch.object(main.codex_adapter, "complete_with_usage", _codex_ok("codex handled claude model")),
            patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=AssertionError("claude must not be called"))),
        ):
            resp = client.post("/v1/codex/chat/completions", json=body)

        assert resp.status_code == 200

    def test_codex_endpoint_returns_502_on_cli_failure(self, client: TestClient) -> None:
        with patch.object(main.codex_adapter, "complete_with_usage", AsyncMock(side_effect=RuntimeError("codex crashed"))):
            resp = client.post("/v1/codex/chat/completions", json=_CHAT_BODY)

        assert resp.status_code == 502


class TestLegacyEndpoint:
    def test_legacy_endpoint_still_works_with_claude_model(self, client: TestClient) -> None:
        body = {**_CHAT_BODY, "model": "claude-opus-4-5"}
        with patch.object(main.claude_adapter, "complete_with_usage", _claude_ok("legacy claude")):
            resp = client.post("/v1/chat/completions", json=body)

        assert resp.status_code == 200
        assert resp.json()["choices"][0]["message"]["content"] == "legacy claude"

    def test_legacy_endpoint_still_works_with_codex_model(self, client: TestClient) -> None:
        body = {**_CHAT_BODY, "model": "gpt-4o"}
        with patch.object(main.codex_adapter, "complete_with_usage", _codex_ok("legacy codex")):
            resp = client.post("/v1/chat/completions", json=body)

        assert resp.status_code == 200
        assert resp.json()["choices"][0]["message"]["content"] == "legacy codex"

    def test_legacy_endpoint_logs_deprecation_warning(self, client: TestClient, caplog: pytest.LogCaptureFixture) -> None:
        import logging
        with (
            patch.object(main.claude_adapter, "complete_with_usage", _claude_ok()),
            caplog.at_level(logging.WARNING, logger="main"),
        ):
            client.post("/v1/chat/completions", json={**_CHAT_BODY, "model": "claude-opus-4-5"})

        assert any("DEPRECATED" in record.message for record in caplog.records)
