"""Per-request failover switch: `x-cli-proxy-failover: off` keeps a request on its backend."""
from __future__ import annotations

import os
import sys
import time
from pathlib import Path
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import claude_adapter  # noqa: E402
import failover  # noqa: E402
import main  # noqa: E402
from usage import UsageParts  # noqa: E402

_BODY = {"model": "claude-haiku-latest", "messages": [{"role": "user", "content": "hi"}]}
_CODEX_BODY = {"model": "gpt-6.2-sol", "messages": [{"role": "user", "content": "hi"}]}
_AUTH = RuntimeError("claude CLI error: Failed to authenticate: OAuth session expired (is_error=true)")
_OFF = {"x-cli-proxy-failover": "off"}


def _ok(text: str = "ok"):
    return AsyncMock(return_value=(text, UsageParts(input_tokens=1, output_tokens=1)))


def _boom(exc: Exception):
    return AsyncMock(side_effect=exc)


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


@pytest.fixture(autouse=True)
def _no_alt_account(monkeypatch) -> None:
    monkeypatch.setattr(failover, "ALT_ENABLED", False)


def _open_breaker(backend: str, seconds: float = 120) -> None:
    failover.record_failure(backend, "auth", "claude CLI error: Failed to authenticate")
    failover.record_failure(backend, "auth", "claude CLI error: Failed to authenticate")
    failover._state(backend).open_until = time.time() + seconds


class TestHeaderParsing:
    @pytest.mark.parametrize("value", ["off", "OFF", "Off", "0", "false", "FALSE", " false "])
    def test_off_values(self, client: TestClient, value: str) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY,
                               headers={"X-CLI-Proxy-Failover": value})
        assert resp.status_code == 502
        assert codex.await_count == 0

    @pytest.mark.parametrize("value", ["on", "1", "true", "claude->codex", ""])
    def test_other_values_keep_the_failover(self, client: TestClient, value: str) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok("from codex")):
            resp = client.post("/v1/claude/chat/completions", json=_BODY,
                               headers={"x-cli-proxy-failover": value})
        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-failover"] == "claude->codex"


class TestFailoverOff:
    def test_original_error_comes_back_as_envelope(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=_OFF)

        assert resp.status_code == 502
        error = resp.json()["error"]
        assert error["type"] == "api_error"
        assert error["message"] == str(_AUTH)
        assert codex.await_count == 0

    def test_breaker_counts_the_failure_as_usual(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH)):
            client.post("/v1/claude/chat/completions", json=_BODY, headers=_OFF)

        claude = client.get("/health").json()["failover"]["backends"]["claude"]
        assert claude["consecutive_failures"] == 1
        assert claude["last_reason"] == "auth"
        assert claude["failover_count"] == 0
        assert "Failed to authenticate" in claude["last_error_detail"]

    def test_success_carries_no_failover_header(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _ok("from claude")):
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=_OFF)
        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "claude"
        assert "x-cli-proxy-failover" not in resp.headers

    def test_codex_endpoint(self, client: TestClient) -> None:
        with patch.object(main.codex_adapter, "complete_with_usage",
                          _boom(RuntimeError("codex CLI error: unauthorized"))), \
             patch.object(main.claude_adapter, "complete_with_usage", _ok()) as claude:
            resp = client.post("/v1/codex/chat/completions", json=_CODEX_BODY, headers=_OFF)
        assert resp.status_code == 502
        assert claude.await_count == 0

    def test_legacy_endpoint(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/chat/completions", json=_BODY, headers=_OFF)
        assert resp.status_code == 502
        assert codex.await_count == 0

    def test_document_mode(self, client: TestClient) -> None:
        body = {"model": "claude-opus-latest", "document_mode": True, "document": "",
                "messages": [{"role": "user", "content": "Write."}]}
        abort = RuntimeError("claude CLI (document mode) exited with code 1: (subtype=x, is_error=true)")
        with patch.object(main.claude_adapter, "complete_document", _boom(abort)), \
             patch.object(main.codex_adapter, "complete_document", _ok("text")) as codex:
            resp = client.post("/v1/claude/chat/completions", json=body, headers=_OFF)
        assert resp.status_code == 502
        assert "subtype=x" in resp.json()["error"]["message"]
        assert codex.await_count == 0

    def test_alternative_account_is_not_used_either(self, client: TestClient, tmp_path: Path,
                                                    monkeypatch) -> None:
        home = tmp_path / "claude-alt"
        (home / ".claude").mkdir(parents=True)
        (home / ".claude" / ".credentials.json").write_text("{}")
        monkeypatch.setattr(failover, "ALT_HOME", str(home))
        monkeypatch.setattr(failover, "ALT_ENABLED", True)
        homes: list[str] = []

        async def _impl(*_a, **_k):
            homes.append(claude_adapter.current_home())
            raise _AUTH

        with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=_impl)):
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=_OFF)
        assert resp.status_code == 502
        assert homes == [claude_adapter.CLAUDE_HOME]


class TestOpenBreaker:
    def test_off_request_gets_503_instead_of_a_handover(self, client: TestClient) -> None:
        _open_breaker("claude")
        with patch.object(main.claude_adapter, "complete_with_usage", _ok()) as claude, \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=_OFF)

        assert resp.status_code == 503
        error = resp.json()["error"]
        assert error["code"] == "backend_unavailable"
        assert error["type"] == "api_error"
        assert 1 <= int(resp.headers["retry-after"]) <= 120
        assert claude.await_count == 0 and codex.await_count == 0

    def test_without_header_the_partner_still_serves(self, client: TestClient) -> None:
        _open_breaker("claude")
        with patch.object(main.claude_adapter, "complete_with_usage", _ok()) as claude, \
             patch.object(main.codex_adapter, "complete_with_usage", _ok("from codex")):
            resp = client.post("/v1/claude/chat/completions", json=_BODY)
        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-failover"] == "claude->codex"
        assert claude.await_count == 0

    def test_elapsed_window_lets_one_probe_through(self, client: TestClient) -> None:
        _open_breaker("claude")
        failover._state("claude").open_until = time.time() - 1
        with patch.object(main.claude_adapter, "complete_with_usage", _ok("from claude")):
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=_OFF)
        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "claude"

    def test_streaming_off_request_gets_503(self, client: TestClient) -> None:
        _open_breaker("claude")
        body = {**_BODY, "stream": True}
        resp = client.post("/v1/claude/chat/completions", json=body, headers=_OFF)
        assert resp.status_code == 503
        assert resp.json()["error"]["code"] == "backend_unavailable"


class TestStreaming:
    def test_buffered_stream_does_not_hand_over(self, client: TestClient) -> None:
        body = {**_BODY, "stream": True, "response_format": {"type": "json_object"}}
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok('{"a": 1}')) as codex:
            resp = client.post("/v1/claude/chat/completions", json=body, headers=_OFF)
        assert resp.status_code == 200, "SSE headers go out before the CLI runs"
        assert "Failed to authenticate" in resp.text
        assert codex.await_count == 0
        assert "x-cli-proxy-failover" not in resp.headers

    def test_buffered_stream_without_header_still_hands_over(self, client: TestClient) -> None:
        body = {**_BODY, "stream": True, "response_format": {"type": "json_object"}}
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok('{"a": 1}')) as codex:
            resp = client.post("/v1/claude/chat/completions", json=body)
        assert '{\\"a\\": 1}' in resp.text
        assert codex.await_count == 1


class TestHealthFields:
    def test_new_fields_have_neutral_defaults(self, client: TestClient) -> None:
        backends = client.get("/health").json()["failover"]["backends"]
        for name in ("claude", "codex"):
            assert backends[name]["last_error_detail"] is None
            assert backends[name]["empty_results"] == 0

    def test_existing_fields_are_unchanged(self, client: TestClient) -> None:
        body = client.get("/health").json()
        assert set(body) == {"status", "cli_status", "synced_providers", "degraded", "failover"}
        assert set(body["failover"]) == {
            "enabled", "degraded", "backends",
            "empty_result_alert", "empty_result_window_seconds",
        }
        assert set(body["failover"]["backends"]["claude"]) == {
            "available", "healthy", "consecutive_failures", "open_until", "last_reason",
            "last_error", "last_failure_at", "last_success_at", "failover_count", "reserve",
            "last_error_detail", "empty_results",
            "last_empty_result_at", "empty_results_recent", "document_requests_recent",
            "empty_result_rate", "empty_result_alert",
        }

    def test_detail_is_redacted_and_longer_than_last_error(self) -> None:
        secret = "sk-ant-api03-AbCdEfGhIjKlMnOp1234"
        message = "claude CLI (document mode) exited with code 1: " + "y" * 700 + f" key {secret}"
        failover.record_failure("claude", "transport", message)
        claude = failover.snapshot()["backends"]["claude"]
        assert len(claude["last_error"]) == 500
        assert secret not in claude["last_error_detail"]
        assert "[redacted]" in claude["last_error_detail"]
        assert len(claude["last_error_detail"]) > 500
