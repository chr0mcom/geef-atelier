"""Tests for the OpenAI-shaped error envelope and status mapping (WP3)."""
from __future__ import annotations

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


def _assert_envelope(body: dict) -> None:
    assert "error" in body
    err = body["error"]
    for key in ("message", "type", "code", "param"):
        assert key in err


class TestErrorEnvelope:
    def test_validation_error_maps_to_400_envelope(self, client: TestClient) -> None:
        # Missing required `messages` → FastAPI 422 → reshaped to 400 invalid_request_error.
        resp = client.post("/v1/claude/chat/completions", json={"model": "claude-haiku-4-5"})
        assert resp.status_code == 400
        body = resp.json()
        _assert_envelope(body)
        assert body["error"]["type"] == "invalid_request_error"

    def test_cli_failure_maps_to_502_envelope(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage",
                          AsyncMock(side_effect=RuntimeError("auth failed"))):
            resp = client.post("/v1/claude/chat/completions",
                               json={"model": "claude-haiku-4-5",
                                     "messages": [{"role": "user", "content": "hi"}]})
        assert resp.status_code == 502
        body = resp.json()
        _assert_envelope(body)
        assert body["error"]["type"] == "api_error"
        assert "auth failed" in body["error"]["message"]

    def test_envelope_has_no_fastapi_detail_key(self, client: TestClient) -> None:
        resp = client.post("/v1/claude/chat/completions", json={"model": "claude-haiku-4-5"})
        assert "detail" not in resp.json()
