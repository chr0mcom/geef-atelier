"""Tests for the JSON-extraction retry logic in _call_claude and _call_codex."""
from __future__ import annotations

import json
import os
import sys
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import main  # noqa: E402


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


_TOOL_BODY = {
    "model": "claude-opus-4-7",
    "messages": [
        {"role": "system", "content": "You are a reviewer."},
        {"role": "user", "content": "Review this text."},
    ],
    "tools": [
        {
            "type": "function",
            "function": {
                "name": "submit_review",
                "description": "Submit your review.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "approved": {"type": "boolean"},
                        "findings": {"type": "array", "items": {"type": "object"}},
                    },
                    "required": ["approved", "findings"],
                },
            },
        }
    ],
    "tool_choice": {"type": "function", "function": {"name": "submit_review"}},
}

_VALID_JSON = '{"approved": true, "findings": []}'
_INVALID_RESPONSE = "I think the text looks great overall and I approve it."


class TestClaudeJsonRetry:
    def test_success_on_first_attempt(self, client: TestClient) -> None:
        """When claude returns valid JSON on first call, no retry happens."""
        mock_complete = AsyncMock(return_value=_VALID_JSON)
        with patch("main.claude_adapter.complete", mock_complete):
            resp = client.post("/v1/claude/chat/completions", json=_TOOL_BODY)
        assert resp.status_code == 200
        data = resp.json()
        assert data["choices"][0]["finish_reason"] == "tool_calls"
        assert data["choices"][0]["message"]["tool_calls"][0]["function"]["name"] == "submit_review"
        mock_complete.assert_called_once()

    def test_retry_on_non_json_first_response(self, client: TestClient) -> None:
        """When first response is not JSON, proxy retries and succeeds on second call."""
        mock_complete = AsyncMock(side_effect=[_INVALID_RESPONSE, _VALID_JSON])
        with patch("main.claude_adapter.complete", mock_complete):
            resp = client.post("/v1/claude/chat/completions", json=_TOOL_BODY)
        assert resp.status_code == 200
        data = resp.json()
        assert data["choices"][0]["finish_reason"] == "tool_calls"
        assert mock_complete.call_count == 2

    def test_retry_prompt_contains_json_reminder(self, client: TestClient) -> None:
        """The retry call must include the explicit JSON reminder suffix."""
        calls: list[str] = []

        async def capture(prompt: str, *args, **kwargs) -> str:
            calls.append(prompt)
            return _VALID_JSON if len(calls) > 1 else _INVALID_RESPONSE

        with patch("main.claude_adapter.complete", side_effect=capture):
            client.post("/v1/claude/chat/completions", json=_TOOL_BODY)

        assert len(calls) == 2
        assert "CRITICAL" in calls[1]
        assert "valid JSON object" in calls[1]
        # First prompt and retry prompt share the same base
        assert calls[0] in calls[1]

    def test_fallback_to_stop_when_both_calls_fail(self, client: TestClient) -> None:
        """When both calls return non-JSON, response still comes back (finish_reason=stop)."""
        mock_complete = AsyncMock(return_value=_INVALID_RESPONSE)
        with patch("main.claude_adapter.complete", mock_complete):
            resp = client.post("/v1/claude/chat/completions", json=_TOOL_BODY)
        assert resp.status_code == 200
        data = resp.json()
        assert data["choices"][0]["finish_reason"] == "stop"
        assert mock_complete.call_count == 2

    def test_no_retry_without_tool_choice(self, client: TestClient) -> None:
        """Plain text requests never trigger retry logic."""
        body = {
            "model": "claude-opus-4-7",
            "messages": [{"role": "user", "content": "Hello"}],
        }
        mock_complete = AsyncMock(return_value="Hello there!")
        with patch("main.claude_adapter.complete", mock_complete):
            resp = client.post("/v1/claude/chat/completions", json=body)
        assert resp.status_code == 200
        mock_complete.assert_called_once()


class TestCodexJsonRetry:
    def test_success_on_first_attempt(self, client: TestClient) -> None:
        body = {**_TOOL_BODY, "model": "gpt-5.5"}
        mock_complete = AsyncMock(return_value=_VALID_JSON)
        with patch("main.codex_adapter.complete", mock_complete):
            resp = client.post("/v1/codex/chat/completions", json=body)
        assert resp.status_code == 200
        assert resp.json()["choices"][0]["finish_reason"] == "tool_calls"
        mock_complete.assert_called_once()

    def test_retry_on_non_json_first_response(self, client: TestClient) -> None:
        body = {**_TOOL_BODY, "model": "gpt-5.5"}
        mock_complete = AsyncMock(side_effect=[_INVALID_RESPONSE, _VALID_JSON])
        with patch("main.codex_adapter.complete", mock_complete):
            resp = client.post("/v1/codex/chat/completions", json=body)
        assert resp.status_code == 200
        assert resp.json()["choices"][0]["finish_reason"] == "tool_calls"
        assert mock_complete.call_count == 2

    def test_fallback_to_stop_when_both_calls_fail(self, client: TestClient) -> None:
        body = {**_TOOL_BODY, "model": "gpt-5.5"}
        mock_complete = AsyncMock(return_value=_INVALID_RESPONSE)
        with patch("main.codex_adapter.complete", mock_complete):
            resp = client.post("/v1/codex/chat/completions", json=body)
        assert resp.status_code == 200
        assert resp.json()["choices"][0]["finish_reason"] == "stop"
        assert mock_complete.call_count == 2
