"""Tests for request-schema completeness and the honest param policy (WP4)."""
from __future__ import annotations

import os
import sys
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import main  # noqa: E402
from openai_format import ChatCompletionRequest, message_text  # noqa: E402
from usage import UsageParts  # noqa: E402


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


def _ok(text: str = "hi"):
    return AsyncMock(return_value=(text, UsageParts(input_tokens=1, output_tokens=1)))


_BASE = {"model": "claude-haiku-4-5", "messages": [{"role": "user", "content": "Hello"}]}


class TestParamPolicy:
    def test_logprobs_rejected_400(self, client: TestClient) -> None:
        resp = client.post("/v1/claude/chat/completions", json={**_BASE, "logprobs": True})
        assert resp.status_code == 400
        body = resp.json()
        assert body["error"]["param"] == "logprobs"
        assert body["error"]["type"] == "invalid_request_error"

    def test_n_greater_than_one_rejected_400(self, client: TestClient) -> None:
        resp = client.post("/v1/claude/chat/completions", json={**_BASE, "n": 3})
        assert resp.status_code == 400
        assert resp.json()["error"]["param"] == "n"

    def test_n_equals_one_allowed(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _ok()):
            resp = client.post("/v1/claude/chat/completions", json={**_BASE, "n": 1})
        assert resp.status_code == 200

    def test_sampling_hints_accepted_and_ignored(self, client: TestClient) -> None:
        body = {**_BASE, "temperature": 0.7, "top_p": 0.9, "seed": 42,
                "presence_penalty": 0.5, "frequency_penalty": 0.1}
        with patch.object(main.claude_adapter, "complete_with_usage", _ok()):
            resp = client.post("/v1/claude/chat/completions", json=body)
        assert resp.status_code == 200

    def test_unknown_fields_ignored_not_422(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _ok()):
            resp = client.post("/v1/claude/chat/completions", json={**_BASE, "future_param": "x"})
        assert resp.status_code == 200


class TestContentParts:
    def test_content_array_text_parts_flattened(self) -> None:
        content = [{"type": "text", "text": "Hello"}, {"type": "text", "text": "World"}]
        assert message_text(content) == "Hello\nWorld"

    def test_content_string_passthrough(self) -> None:
        assert message_text("plain") == "plain"

    def test_content_array_accepted_by_endpoint(self, client: TestClient) -> None:
        body = {
            "model": "claude-haiku-4-5",
            "messages": [{"role": "user", "content": [{"type": "text", "text": "Hi"}]}],
        }
        with patch.object(main.claude_adapter, "complete_with_usage", _ok()):
            resp = client.post("/v1/claude/chat/completions", json=body)
        assert resp.status_code == 200

    def test_developer_role_accepted(self, client: TestClient) -> None:
        body = {
            "model": "claude-haiku-4-5",
            "messages": [
                {"role": "developer", "content": "You are terse."},
                {"role": "user", "content": "Hi"},
            ],
        }
        with patch.object(main.claude_adapter, "complete_with_usage", _ok()):
            resp = client.post("/v1/claude/chat/completions", json=body)
        assert resp.status_code == 200


class TestMaxTokensAlias:
    def test_max_completion_tokens_supersedes_max_tokens(self) -> None:
        req = ChatCompletionRequest(model="m", messages=[], max_tokens=100, max_completion_tokens=200)
        assert req.effective_max_tokens() == 200

    def test_max_tokens_fallback(self) -> None:
        req = ChatCompletionRequest(model="m", messages=[], max_tokens=100)
        assert req.effective_max_tokens() == 100
