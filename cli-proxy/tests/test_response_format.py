"""Tests for response_format / structured outputs (WP5)."""
from __future__ import annotations

import json
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


def _u(text: str):
    return text, UsageParts(input_tokens=1, output_tokens=1)


_SCHEMA = {
    "type": "object",
    "properties": {"answer": {"type": "string"}},
    "required": ["answer"],
    "additionalProperties": False,
}


class TestJsonObject:
    def test_valid_json_returned_as_content(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(return_value=_u('{"a": 1}'))):
            resp = client.post("/v1/claude/chat/completions", json={
                "model": "claude-haiku-4-5",
                "messages": [{"role": "user", "content": "give json"}],
                "response_format": {"type": "json_object"},
            })
        assert resp.status_code == 200
        content = resp.json()["choices"][0]["message"]["content"]
        assert json.loads(content) == {"a": 1}

    def test_extracts_json_from_prose_then_no_retry(self, client: TestClient) -> None:
        mock = AsyncMock(return_value=_u('Sure! {"a": 2} done'))
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json={
                "model": "claude-haiku-4-5",
                "messages": [{"role": "user", "content": "give json"}],
                "response_format": {"type": "json_object"},
            })
        assert resp.status_code == 200
        assert json.loads(resp.json()["choices"][0]["message"]["content"]) == {"a": 2}
        mock.assert_called_once()

    def test_retry_then_refusal_when_no_json(self, client: TestClient) -> None:
        mock = AsyncMock(side_effect=[_u("no json here"), _u("still no json")])
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json={
                "model": "claude-haiku-4-5",
                "messages": [{"role": "user", "content": "give json"}],
                "response_format": {"type": "json_object"},
            })
        assert resp.status_code == 200
        msg = resp.json()["choices"][0]["message"]
        assert msg.get("content") is None
        assert msg.get("refusal")
        assert mock.call_count == 2


class TestJsonSchema:
    def test_schema_valid_passes(self, client: TestClient) -> None:
        pytest.importorskip("jsonschema")
        with patch.object(main.claude_adapter, "complete_with_usage",
                          AsyncMock(return_value=_u('{"answer": "yes"}'))):
            resp = client.post("/v1/claude/chat/completions", json={
                "model": "claude-haiku-4-5",
                "messages": [{"role": "user", "content": "give json"}],
                "response_format": {
                    "type": "json_schema",
                    "json_schema": {"name": "ans", "schema": _SCHEMA, "strict": True},
                },
            })
        assert resp.status_code == 200
        assert json.loads(resp.json()["choices"][0]["message"]["content"]) == {"answer": "yes"}

    def test_schema_mismatch_retries_then_refuses(self, client: TestClient) -> None:
        pytest.importorskip("jsonschema")
        # First and retry both violate the schema (missing required "answer").
        mock = AsyncMock(side_effect=[_u('{"wrong": 1}'), _u('{"still_wrong": 2}')])
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json={
                "model": "claude-haiku-4-5",
                "messages": [{"role": "user", "content": "give json"}],
                "response_format": {
                    "type": "json_schema",
                    "json_schema": {"name": "ans", "schema": _SCHEMA, "strict": True},
                },
            })
        assert resp.status_code == 200
        assert resp.json()["choices"][0]["message"].get("refusal")
        assert mock.call_count == 2
