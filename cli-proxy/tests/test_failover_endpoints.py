"""Endpoint-level tests for the claude <-> codex failover."""
from __future__ import annotations

import os
import sys
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import failover  # noqa: E402
import main  # noqa: E402
from usage import UsageParts  # noqa: E402

_BODY = {"model": "claude-haiku-latest", "messages": [{"role": "user", "content": "hi"}]}

# The verbatim failure from the 2026-07-22 outage.
_AUTH_ERROR = RuntimeError(
    "claude CLI error: Failed to authenticate: OAuth session expired and could not be refreshed")


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


@pytest.fixture(autouse=True)
def _reset() -> None:
    failover.reset()


def _ok(text: str = "ok"):
    return AsyncMock(return_value=(text, UsageParts(input_tokens=1, output_tokens=1)))


def _boom(exc: Exception):
    return AsyncMock(side_effect=exc)


class TestClaudeFailsOverToCodex:
    def test_auth_failure_is_served_by_codex(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH_ERROR)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok("from codex")) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY)

        assert resp.status_code == 200
        assert resp.json()["choices"][0]["message"]["content"] == "from codex"
        assert resp.headers["x-cli-proxy-provider"] == "codex"
        assert resp.headers["x-cli-proxy-failover"] == "claude->codex"
        assert codex.await_count == 1

    def test_model_name_does_not_travel_to_the_partner_cli(self, client: TestClient) -> None:
        # `codex -m claude-haiku-latest` would fail outright; the partner must use its own default.
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH_ERROR)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            client.post("/v1/claude/chat/completions", json=_BODY)

        passed_model = codex.await_args.args[1]
        assert not passed_model, f"model {passed_model!r} must not cross the backend boundary"

    def test_response_still_echoes_the_requested_model(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH_ERROR)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()):
            resp = client.post("/v1/claude/chat/completions", json=_BODY)

        assert resp.json()["model"] == "claude-haiku-latest"

    @pytest.mark.parametrize("exc", [
        RuntimeError("claude CLI error: rate limit exceeded"),
        RuntimeError("claude CLI timed out after 600 seconds"),
    ])
    def test_limit_and_transport_faults_also_fail_over(self, client: TestClient, exc) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(exc)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()):
            resp = client.post("/v1/claude/chat/completions", json=_BODY)
        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "codex"


class TestNoFailover:
    def test_healthy_claude_never_touches_codex(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _ok()), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY)

        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "claude"
        assert "x-cli-proxy-failover" not in resp.headers
        assert codex.await_count == 0

    def test_unclassified_fault_is_not_retried_elsewhere(self, client: TestClient) -> None:
        exc = RuntimeError("Could not produce valid structured output: schema mismatch")
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(exc)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY)

        assert resp.status_code == 502
        assert codex.await_count == 0, "a client-side fault would fail identically on codex"

    def test_both_backends_down_returns_502(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH_ERROR)), \
             patch.object(main.codex_adapter, "complete_with_usage",
                          _boom(RuntimeError("codex CLI timed out after 600 seconds"))):
            resp = client.post("/v1/claude/chat/completions", json=_BODY)

        assert resp.status_code == 502
        assert resp.json()["error"]["type"] == "api_error"

    def test_kill_switch_restores_previous_behaviour(self, client: TestClient, monkeypatch) -> None:
        monkeypatch.setattr(failover, "ENABLED", False)
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH_ERROR)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY)

        assert resp.status_code == 502
        assert codex.await_count == 0


class TestSymmetry:
    def test_codex_falls_over_to_claude(self, client: TestClient) -> None:
        body = {"model": "gpt-5.6-sol", "messages": [{"role": "user", "content": "hi"}]}
        with patch.object(main.codex_adapter, "complete_with_usage",
                          _boom(RuntimeError("codex CLI error: unauthorized"))), \
             patch.object(main.claude_adapter, "complete_with_usage", _ok("from claude")):
            resp = client.post("/v1/codex/chat/completions", json=body)

        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-failover"] == "codex->claude"


class TestBreakerRouting:
    def test_open_breaker_sends_the_next_request_straight_to_the_partner(
        self, client: TestClient, monkeypatch
    ) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH_ERROR)) as claude, \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()):
            client.post("/v1/claude/chat/completions", json=_BODY)
            assert claude.await_count == 1
            client.post("/v1/claude/chat/completions", json=_BODY)
            assert claude.await_count == 1, "breaker is open — claude must not be retried"

    def test_streaming_is_pre_routed_when_the_breaker_is_open(
        self, client: TestClient, monkeypatch
    ) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        failover.record_failure("claude", "auth", "boom")

        async def _stream(prompt, model, max_tokens):
            assert not model, "model must not cross the backend boundary"
            yield ("delta", "streamed by codex")

        with patch.object(main.codex_adapter, "stream", _stream):
            resp = client.post("/v1/claude/chat/completions", json={**_BODY, "stream": True})

        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "codex"
        assert "streamed by codex" in resp.text


_SESSION_LIMIT_ERROR = RuntimeError(
    'claude CLI (agentic file mode) exited with code 1: {"api_error_status":429,'
    '"result":"You\'ve hit your session limit · resets 4:20pm (UTC)"}')

_TOOL_BODY = {
    **_BODY,
    "stream": True,
    "tools": [{"type": "function",
               "function": {"name": "kanban_comment", "parameters": {"type": "object"}}}],
}


class TestStreamingFailover:
    """Until 2026-07-24 a fault inside the SSE generator bypassed failover entirely:
    the 429 of the 2026-07-23 session-limit outage surfaced as an in-band error to
    Hermes while the breaker, /health and the watch cron all stayed green."""

    def test_agentic_stream_hands_over_to_codex_before_first_byte(
        self, client: TestClient
    ) -> None:
        # Since 2026-07-24 the handover keeps the decision-file contract: codex serves the
        # agentic request via its OWN complete_agentic_file (previously it degraded to the
        # buffered print path, whose tool prompt the codex CLI answered with "tools not
        # connected" as plain text — zero tool calls for Hermes).
        with patch.object(main.claude_adapter, "complete_agentic_file",
                          _boom(_SESSION_LIMIT_ERROR)), \
             patch.object(main.codex_adapter, "complete_agentic_file",
                          _ok('{"final": "from codex"}')) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_TOOL_BODY)

        assert resp.status_code == 200
        assert "from codex" in resp.text
        assert '"api_error"' not in resp.text, "the client must see content, not an error frame"
        assert not codex.await_args.args[1], "model must not cross the backend boundary"
        st = failover.snapshot()["backends"]["claude"]
        assert st["last_reason"] == "limit"
        assert st["failover_count"] == 1

    def test_buffered_structured_stream_hands_over_to_codex(self, client: TestClient) -> None:
        body = {**_BODY, "stream": True, "response_format": {"type": "json_object"}}
        with patch.object(main.claude_adapter, "complete_with_usage",
                          _boom(_SESSION_LIMIT_ERROR)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok('{"ok": true}')):
            resp = client.post("/v1/claude/chat/completions", json=body)

        assert resp.status_code == 200
        assert '\\"ok\\": true' in resp.text or '"ok": true' in resp.text
        assert '"api_error"' not in resp.text

    def test_stream_failure_after_first_token_feeds_the_breaker(
        self, client: TestClient
    ) -> None:
        # Mid-stream bytes are already on the wire — the error must surface in-band,
        # but the breaker has to learn about it so the NEXT request pre-routes.
        async def _stream(prompt, model, max_tokens):
            yield ("delta", "partial")
            raise _SESSION_LIMIT_ERROR

        with patch.object(main.claude_adapter, "stream", _stream):
            resp = client.post("/v1/claude/chat/completions", json={**_BODY, "stream": True})

        assert "partial" in resp.text
        assert '"api_error"' in resp.text
        st = failover.snapshot()["backends"]["claude"]
        assert st["last_reason"] == "limit"
        assert st["consecutive_failures"] == 1

    def test_both_backends_down_surfaces_in_band_error(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_agentic_file",
                          _boom(_SESSION_LIMIT_ERROR)), \
             patch.object(main.codex_adapter, "complete_agentic_file",
                          _boom(RuntimeError("codex CLI error: 429 Too Many Requests"))):
            resp = client.post("/v1/claude/chat/completions", json=_TOOL_BODY)

        assert resp.status_code == 200, "headers were already sent — error must be in-band"
        assert '"api_error"' in resp.text
        assert failover.snapshot()["backends"]["codex"]["last_reason"] == "limit"

    def test_unclassified_stream_fault_is_not_retried_elsewhere(
        self, client: TestClient
    ) -> None:
        # The agentic request would reach codex via complete_agentic_file — patch THAT
        # (review 2026-07-24: asserting on complete_with_usage was trivially true and
        # a regression would have spawned the real codex CLI).
        with patch.object(main.claude_adapter, "complete_agentic_file",
                          _boom(RuntimeError("some hitherto unseen local defect"))), \
             patch.object(main.codex_adapter, "complete_agentic_file",
                          _ok('{"final": "x"}')) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_TOOL_BODY)

        assert '"api_error"' in resp.text
        assert codex.await_count == 0, "an unknown fault would fail identically on the partner"


class TestHealth:
    def test_reports_healthy_state(self, client: TestClient) -> None:
        resp = client.get("/health")
        body = resp.json()
        assert body["status"] == "ok"
        assert body["degraded"] is False
        assert body["failover"]["backends"]["claude"]["available"] is True
        # Pre-existing fields must survive untouched.
        assert "cli_status" in body and "synced_providers" in body

    def test_exposes_the_cause_after_an_outage(self, client: TestClient, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        with patch.object(main.claude_adapter, "complete_with_usage", _boom(_AUTH_ERROR)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()):
            client.post("/v1/claude/chat/completions", json=_BODY)

        body = client.get("/health").json()
        assert body["status"] == "ok", "a degraded gateway is still a reachable gateway"
        assert body["degraded"] is True
        claude = body["failover"]["backends"]["claude"]
        assert claude["available"] is False
        assert claude["last_reason"] == "auth"
        assert "OAuth session expired" in claude["last_error"]
        assert claude["failover_count"] == 1
