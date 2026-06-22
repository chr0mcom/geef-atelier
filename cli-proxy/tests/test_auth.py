"""Tests for opt-in Bearer auth (WP7)."""
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


_BODY = {"model": "claude-haiku-4-5", "messages": [{"role": "user", "content": "hi"}]}


def _ok():
    return AsyncMock(return_value=("ok", UsageParts(input_tokens=1, output_tokens=1)))


class TestAuthDisabled:
    def test_no_keys_configured_allows_open_access(self, client: TestClient) -> None:
        with patch.object(main, "_API_KEYS", set()), \
             patch.object(main.claude_adapter, "complete_with_usage", _ok()):
            resp = client.post("/v1/claude/chat/completions", json=_BODY)
        assert resp.status_code == 200


class TestAuthEnabled:
    def test_missing_token_rejected_401(self, client: TestClient) -> None:
        with patch.object(main, "_API_KEYS", {"secret-key"}):
            resp = client.post("/v1/claude/chat/completions", json=_BODY)
        assert resp.status_code == 401
        assert resp.json()["error"]["type"] == "authentication_error"

    def test_wrong_token_rejected_401(self, client: TestClient) -> None:
        with patch.object(main, "_API_KEYS", {"secret-key"}):
            resp = client.post("/v1/claude/chat/completions", json=_BODY,
                               headers={"Authorization": "Bearer wrong"})
        assert resp.status_code == 401

    def test_valid_token_accepted(self, client: TestClient) -> None:
        with patch.object(main, "_API_KEYS", {"secret-key"}), \
             patch.object(main.claude_adapter, "complete_with_usage", _ok()):
            resp = client.post("/v1/claude/chat/completions", json=_BODY,
                               headers={"Authorization": "Bearer secret-key"})
        assert resp.status_code == 200
