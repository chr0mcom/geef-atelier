"""Tests for the JSON-extraction retry logic in _run_completion."""
from __future__ import annotations

import os
import sys
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import main  # noqa: E402
from usage import UsageParts  # noqa: E402


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


def _u(text: str) -> tuple[str, UsageParts]:
    """Wrap a text result as the (text, usage) tuple complete_with_usage returns."""
    return text, UsageParts(input_tokens=10, output_tokens=5)


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
        mock = AsyncMock(return_value=_u(_VALID_JSON))
        with patch("main.claude_adapter.complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json=_TOOL_BODY)
        assert resp.status_code == 200
        data = resp.json()
        assert data["choices"][0]["finish_reason"] == "tool_calls"
        assert data["choices"][0]["message"]["tool_calls"][0]["function"]["name"] == "submit_review"
        # usage is now surfaced from the adapter
        assert data["usage"]["prompt_tokens"] == 10
        assert data["usage"]["completion_tokens"] == 5
        mock.assert_called_once()

    def test_retry_on_non_json_first_response(self, client: TestClient) -> None:
        mock = AsyncMock(side_effect=[_u(_INVALID_RESPONSE), _u(_VALID_JSON)])
        with patch("main.claude_adapter.complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json=_TOOL_BODY)
        assert resp.status_code == 200
        assert resp.json()["choices"][0]["finish_reason"] == "tool_calls"
        assert mock.call_count == 2

    def test_retry_prompt_contains_json_reminder(self, client: TestClient) -> None:
        calls: list[str] = []

        async def capture(prompt: str, *args, **kwargs):
            calls.append(prompt)
            return _u(_VALID_JSON if len(calls) > 1 else _INVALID_RESPONSE)

        with patch("main.claude_adapter.complete_with_usage", side_effect=capture):
            client.post("/v1/claude/chat/completions", json=_TOOL_BODY)

        assert len(calls) == 2
        assert "CRITICAL" in calls[1]
        assert "valid JSON object" in calls[1]
        assert calls[0] in calls[1]

    def test_fallback_to_stop_when_both_calls_fail(self, client: TestClient) -> None:
        mock = AsyncMock(return_value=_u(_INVALID_RESPONSE))
        with patch("main.claude_adapter.complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json=_TOOL_BODY)
        assert resp.status_code == 200
        assert resp.json()["choices"][0]["finish_reason"] == "stop"
        assert mock.call_count == 2

    def test_no_retry_without_tool_choice(self, client: TestClient) -> None:
        body = {
            "model": "claude-opus-4-7",
            "messages": [{"role": "user", "content": "Hello"}],
        }
        mock = AsyncMock(return_value=_u("Hello there!"))
        with patch("main.claude_adapter.complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json=body)
        assert resp.status_code == 200
        mock.assert_called_once()


class TestCodexJsonRetry:
    def test_success_on_first_attempt(self, client: TestClient) -> None:
        body = {**_TOOL_BODY, "model": "gpt-5.5"}
        mock = AsyncMock(return_value=_u(_VALID_JSON))
        with patch("main.codex_adapter.complete_with_usage", mock):
            resp = client.post("/v1/codex/chat/completions", json=body)
        assert resp.status_code == 200
        assert resp.json()["choices"][0]["finish_reason"] == "tool_calls"
        mock.assert_called_once()

    def test_retry_on_non_json_first_response(self, client: TestClient) -> None:
        body = {**_TOOL_BODY, "model": "gpt-5.5"}
        mock = AsyncMock(side_effect=[_u(_INVALID_RESPONSE), _u(_VALID_JSON)])
        with patch("main.codex_adapter.complete_with_usage", mock):
            resp = client.post("/v1/codex/chat/completions", json=body)
        assert resp.status_code == 200
        assert resp.json()["choices"][0]["finish_reason"] == "tool_calls"
        assert mock.call_count == 2

    def test_fallback_to_stop_when_both_calls_fail(self, client: TestClient) -> None:
        body = {**_TOOL_BODY, "model": "gpt-5.5"}
        mock = AsyncMock(return_value=_u(_INVALID_RESPONSE))
        with patch("main.codex_adapter.complete_with_usage", mock):
            resp = client.post("/v1/codex/chat/completions", json=body)
        assert resp.status_code == 200
        assert resp.json()["choices"][0]["finish_reason"] == "stop"
        assert mock.call_count == 2
