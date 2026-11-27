"""Per-request failover mode `x-cli-proxy-failover: same-model`.

A caller whose result depends on the model (a writer, a model-bound check) cannot accept an answer
from another CLI, but a second Claude account runs the same model under the same name. With
`same-model` the request may therefore move between the Claude accounts (claude <-> claude-alt) with
the requested model unchanged, and never across to codex or back; for a codex request it is the same
as `off`. Breaker, limit detection, "not in plan" routing and response headers behave as for the
ordinary failover between the accounts. See ../../../../docs/cli-proxy-failover.md.
"""
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

SAME = {"x-cli-proxy-failover": "same-model"}
_BODY = {"model": "claude-opus-latest", "messages": [{"role": "user", "content": "hi"}]}
_CODEX_BODY = {"model": "gpt-6.2-sol", "messages": [{"role": "user", "content": "hi"}]}
_LIMIT = RuntimeError("claude CLI error: You've hit your session limit · resets 4:20pm (UTC)")
_AUTH = RuntimeError("claude CLI error: Failed to authenticate: OAuth session expired")
_TRANSPORT = RuntimeError("claude CLI timed out after 600 seconds")
_NOT_IN_PLAN = claude_adapter.NotInPlanError(
    "claude CLI API error (status=429): You're out of usage credits. Switch to another model.")
_TOOLS = [{"type": "function", "function": {"name": "lookup", "parameters": {"type": "object", "properties": {}}}}]


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


@pytest.fixture()
def alt_home(tmp_path: Path, monkeypatch) -> str:
    home = tmp_path / "claude-alt"
    (home / ".claude").mkdir(parents=True)
    (home / ".claude" / ".credentials.json").write_text("{}")
    monkeypatch.setattr(failover, "ALT_HOME", str(home))
    monkeypatch.setattr(failover, "ALT_ENABLED", True)
    return str(home)


def _account() -> str:
    return "claude" if claude_adapter.current_home() == claude_adapter.CLAUDE_HOME else "claude-alt"


def _claude(fail: dict[str, Exception] | None = None, text: str = "ok"):
    """complete_with_usage stand-in: records (account, model) and raises per account."""
    seen: list[tuple[str, str]] = []

    async def _impl(prompt, model, max_tokens, **_):
        account = _account()
        seen.append((account, model))
        exc = (fail or {}).get(account)
        if exc is not None:
            raise exc
        return f"{text} via {account}", UsageParts(input_tokens=1, output_tokens=1)
    return AsyncMock(side_effect=_impl), seen


def _ok(text: str = "ok"):
    return AsyncMock(return_value=(text, UsageParts(input_tokens=1, output_tokens=1)))


def _open(backend: str, seconds: float = 120) -> None:
    failover.record_failure(backend, "limit", "session limit")
    failover.record_failure(backend, "limit", "session limit")
    failover._state(backend).open_until = time.time() + seconds


class TestHeaderParsing:
    @pytest.mark.parametrize("value", ["same-model", "SAME-MODEL", "Same-Model", " same-model "])
    def test_same_model_values(self, value: str) -> None:
        request = type("R", (), {"headers": {"x-cli-proxy-failover": value}})()
        assert main._failover_mode(request) == main.FAILOVER_SAME_MODEL

    @pytest.mark.parametrize("value,mode", [
        (None, "on"), ("off", "off"), ("0", "off"), ("false", "off"), (" FALSE ", "off"),
        ("on", "on"), ("", "on"), ("same_model", "on"), ("samemodel", "on"), ("same-model,off", "on"),
    ])
    def test_other_values_are_unchanged(self, value, mode) -> None:
        headers = {} if value is None else {"x-cli-proxy-failover": value}
        request = type("R", (), {"headers": headers})()
        assert main._failover_mode(request) == mode

    def test_unknown_spelling_keeps_the_full_failover(self, client, alt_home) -> None:
        fake, _ = _claude({"claude": _LIMIT, "claude-alt": _LIMIT})
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok("from codex")):
            resp = client.post("/v1/claude/chat/completions", json=_BODY,
                               headers={"x-cli-proxy-failover": "same_model"})
        assert resp.headers["x-cli-proxy-provider"] == "codex"


class TestAttemptOrder:
    def test_claude_chain_without_codex(self, alt_home) -> None:
        assert main._attempt_order("claude", main.FAILOVER_SAME_MODEL) == ["claude", "claude-alt"]

    def test_without_alt_only_the_primary(self) -> None:
        assert main._attempt_order("claude", main.FAILOVER_SAME_MODEL) == ["claude"]

    def test_open_primary_leaves_the_alt(self, alt_home) -> None:
        _open("claude")
        assert main._attempt_order("claude", main.FAILOVER_SAME_MODEL) == ["claude-alt"]

    def test_routed_to_alt_never_adds_the_primary(self, alt_home) -> None:
        assert main._attempt_order("claude", main.FAILOVER_SAME_MODEL, route_to_alt=True) == ["claude-alt"]

    def test_codex_is_like_off(self, alt_home) -> None:
        assert main._attempt_order("codex", main.FAILOVER_SAME_MODEL) == ["codex"]

    def test_global_kill_switch_is_like_off(self, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(failover, "ENABLED", False)
        assert main._attempt_order("claude", main.FAILOVER_SAME_MODEL) == ["claude"]

    def test_off_and_on_are_untouched(self, alt_home) -> None:
        assert main._attempt_order("claude", main.FAILOVER_OFF) == ["claude"]
        assert main._attempt_order("claude", main.FAILOVER_ON) == ["claude", "claude-alt", "codex"]


class TestNonStreaming:
    def test_limit_on_primary_moves_to_the_alt_with_the_same_model(self, client, alt_home) -> None:
        fake, seen = _claude({"claude": _LIMIT})
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=SAME)

        assert resp.status_code == 200
        assert resp.json()["choices"][0]["message"]["content"] == "ok via claude-alt"
        assert resp.json()["model"] == "claude-opus-latest"
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert resp.headers["x-cli-proxy-failover"] == "claude->claude-alt"
        assert seen == [("claude", "claude-opus-latest"), ("claude-alt", "claude-opus-latest")]
        assert codex.await_count == 0
        backends = client.get("/health").json()["failover"]["backends"]
        assert backends["claude"]["consecutive_failures"] == 1
        assert backends["claude"]["last_reason"] == "limit"
        assert backends["claude"]["failover_count"] == 1
        assert backends["claude-alt"]["consecutive_failures"] == 0

    def test_auth_on_primary_moves_to_the_alt(self, client, alt_home) -> None:
        fake, seen = _claude({"claude": _AUTH})
        with patch.object(main.claude_adapter, "complete_with_usage", fake):
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=SAME)
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert [a for a, _ in seen] == ["claude", "claude-alt"]

    def test_both_accounts_at_their_limit_is_502_and_never_codex(self, client, alt_home) -> None:
        fake, seen = _claude({"claude": _LIMIT, "claude-alt": _LIMIT})
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=SAME)

        assert resp.status_code == 502
        assert "session limit" in resp.json()["error"]["message"]
        assert [a for a, _ in seen] == ["claude", "claude-alt"]
        assert codex.await_count == 0
        assert failover._state("claude-alt").consecutive_failures == 1

    def test_without_alt_a_limit_is_502_and_never_codex(self, client) -> None:
        fake, seen = _claude({"claude": _LIMIT})
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=SAME)
        assert resp.status_code == 502
        assert codex.await_count == 0
        assert "x-cli-proxy-failover" not in resp.headers

    def test_transport_fault_skips_the_alt_like_the_ordinary_failover(self, client, alt_home) -> None:
        fake, seen = _claude({"claude": _TRANSPORT})
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=SAME)
        assert resp.status_code == 502
        assert [a for a, _ in seen] == ["claude"]
        assert codex.await_count == 0

    def test_unknown_fault_is_not_handed_over(self, client, alt_home) -> None:
        fake, seen = _claude({"claude": RuntimeError("claude CLI error: schema rejected")})
        with patch.object(main.claude_adapter, "complete_with_usage", fake):
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=SAME)
        assert resp.status_code == 502
        assert [a for a, _ in seen] == ["claude"]

    def test_success_on_the_primary_carries_no_failover_header(self, client, alt_home) -> None:
        fake, seen = _claude()
        with patch.object(main.claude_adapter, "complete_with_usage", fake):
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=SAME)
        assert resp.headers["x-cli-proxy-provider"] == "claude"
        assert "x-cli-proxy-failover" not in resp.headers
        assert seen == [("claude", "claude-opus-latest")]

    def test_legacy_endpoint(self, client, alt_home) -> None:
        fake, seen = _claude({"claude": _LIMIT, "claude-alt": _LIMIT})
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/chat/completions", json=_BODY, headers=SAME)
        assert resp.status_code == 502
        assert [a for a, _ in seen] == ["claude", "claude-alt"]
        assert codex.await_count == 0


class TestOpenBreaker:
    def test_open_primary_goes_straight_to_the_alt(self, client, alt_home) -> None:
        _open("claude")
        fake, seen = _claude()
        with patch.object(main.claude_adapter, "complete_with_usage", fake):
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=SAME)
        assert resp.status_code == 200
        assert seen == [("claude-alt", "claude-opus-latest")]
        assert resp.headers["x-cli-proxy-failover"] == "claude->claude-alt"

    def test_both_open_is_503_with_the_earliest_retry_after(self, client, alt_home) -> None:
        _open("claude", 200)
        _open("claude-alt", 60)
        fake, seen = _claude()
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=SAME)

        assert resp.status_code == 503
        error = resp.json()["error"]
        assert error["code"] == "backend_unavailable"
        assert error["type"] == "api_error"
        assert "same-model" in error["message"]
        assert 1 <= int(resp.headers["retry-after"]) <= 60
        assert seen == [] and codex.await_count == 0

    def test_open_primary_without_alt_is_503(self, client) -> None:
        _open("claude")
        with patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=SAME)
        assert resp.status_code == 503
        assert resp.json()["error"]["code"] == "backend_unavailable"
        assert codex.await_count == 0

    def test_streaming_with_both_open_is_503(self, client, alt_home) -> None:
        _open("claude")
        _open("claude-alt")
        resp = client.post("/v1/claude/chat/completions", json={**_BODY, "stream": True}, headers=SAME)
        assert resp.status_code == 503
        assert resp.json()["error"]["code"] == "backend_unavailable"

    def test_off_message_is_unchanged(self) -> None:
        _open("claude")
        error = main._backend_unavailable("claude")
        assert error.message.startswith(
            "Backend claude is temporarily unavailable after repeated failures, and failover is "
            "switched off for this request. Retry in about ")


class TestCodex:
    def test_codex_fault_never_reaches_claude(self, client, alt_home) -> None:
        with patch.object(main.codex_adapter, "complete_with_usage",
                          AsyncMock(side_effect=RuntimeError("codex CLI error: unauthorized"))), \
             patch.object(main.claude_adapter, "complete_with_usage", _ok()) as claude:
            resp = client.post("/v1/codex/chat/completions", json=_CODEX_BODY, headers=SAME)
        assert resp.status_code == 502
        assert claude.await_count == 0
        assert "x-cli-proxy-failover" not in resp.headers

    def test_open_codex_breaker_is_503(self, client, alt_home) -> None:
        _open("codex")
        with patch.object(main.claude_adapter, "complete_with_usage", _ok()) as claude:
            resp = client.post("/v1/codex/chat/completions", json=_CODEX_BODY, headers=SAME)
        assert resp.status_code == 503
        assert resp.json()["error"]["code"] == "backend_unavailable"
        assert claude.await_count == 0

    def test_codex_success(self, client) -> None:
        with patch.object(main.codex_adapter, "complete_with_usage", _ok("from codex")):
            resp = client.post("/v1/codex/chat/completions", json=_CODEX_BODY, headers=SAME)
        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "codex"
        assert "x-cli-proxy-failover" not in resp.headers


class TestNotInPlan:
    def test_not_in_plan_on_primary_routes_to_the_alt_without_failover_traces(self, client, alt_home) -> None:
        fake, seen = _claude({"claude": _NOT_IN_PLAN})
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=SAME)
        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert "x-cli-proxy-failover" not in resp.headers
        assert codex.await_count == 0
        assert failover._state("claude").consecutive_failures == 0

    def test_neither_account_has_it_is_404_and_never_codex(self, client, alt_home) -> None:
        fake, seen = _claude({"claude": _NOT_IN_PLAN, "claude-alt": _NOT_IN_PLAN})
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=SAME)
        assert resp.status_code == 404
        assert resp.json()["error"]["code"] == "model_not_found"
        assert [a for a, _ in seen] == ["claude", "claude-alt"]
        assert codex.await_count == 0

    def test_not_in_plan_with_open_alt_breaker_is_503(self, client, alt_home) -> None:
        _open("claude-alt")
        fake, seen = _claude({"claude": _NOT_IN_PLAN})
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY, headers=SAME)
        assert resp.status_code == 503
        assert resp.json()["error"]["code"] == "backend_unavailable"
        assert [a for a, _ in seen] == ["claude"]
        assert codex.await_count == 0

    def test_routed_model_with_alt_at_its_limit_never_tries_primary_or_codex(
        self, client, alt_home, monkeypatch,
    ) -> None:
        monkeypatch.setattr(main.model_routing, "route_to_alt", lambda model: "family fable not in plan")
        fake, seen = _claude({"claude-alt": _LIMIT})
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions",
                               json={**_BODY, "model": "claude-fable-latest"}, headers=SAME)
        assert resp.status_code == 502
        assert seen == [("claude-alt", "claude-fable-latest")]
        assert codex.await_count == 0


class TestStreaming:
    def test_buffered_stream_moves_to_the_alt_and_never_codex(self, client, alt_home) -> None:
        fake, seen = _claude({"claude": _LIMIT}, text='{"a": 1}')

        async def _json(prompt, model, max_tokens, **kw):
            await fake(prompt, model, max_tokens, **kw)
            return '{"a": 1}', UsageParts(input_tokens=1, output_tokens=1)

        body = {**_BODY, "stream": True, "response_format": {"type": "json_object"}}
        with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=_json)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=body, headers=SAME)

        assert resp.status_code == 200
        assert '{\\"a\\": 1}' in resp.text
        assert seen == [("claude", "claude-opus-latest"), ("claude-alt", "claude-opus-latest")]
        assert codex.await_count == 0
        assert failover._state("claude").failover_count == 1

    def test_buffered_stream_with_both_accounts_failing_never_reaches_codex(self, client, alt_home) -> None:
        body = {**_BODY, "stream": True, "response_format": {"type": "json_object"}}
        with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=_LIMIT)) as claude, \
             patch.object(main.codex_adapter, "complete_with_usage", _ok('{"a": 1}')) as codex:
            resp = client.post("/v1/claude/chat/completions", json=body, headers=SAME)
        assert "session limit" in resp.text
        assert claude.await_count == 2
        assert codex.await_count == 0

    def test_token_stream_switches_accounts_before_the_first_event(self, client, alt_home) -> None:
        homes: list[str] = []

        async def _stream(prompt, model, max_tokens, **_):
            homes.append(_account())
            if _account() == "claude":
                raise _AUTH
            yield ("delta", f"streamed {model}")

        with patch.object(main.claude_adapter, "stream", _stream), \
             patch.object(main.codex_adapter, "stream") as codex:
            resp = client.post("/v1/claude/chat/completions", json={**_BODY, "stream": True}, headers=SAME)

        assert "streamed claude-opus-latest" in resp.text
        assert homes == ["claude", "claude-alt"]
        assert resp.headers["x-cli-proxy-provider"] == "claude"
        assert codex.call_count == 0

    def test_open_primary_stream_reports_the_handover_in_its_headers(self, client, alt_home) -> None:
        _open("claude")

        async def _stream(prompt, model, max_tokens, **_):
            yield ("delta", _account())

        with patch.object(main.claude_adapter, "stream", _stream):
            resp = client.post("/v1/claude/chat/completions", json={**_BODY, "stream": True}, headers=SAME)
        assert "claude-alt" in resp.text
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert resp.headers["x-cli-proxy-failover"] == "claude->claude-alt"


class TestDecisionFile:
    def test_agentic_request_moves_to_the_alt_and_never_codex(self, client, alt_home) -> None:
        homes: list[str] = []

        async def _decision(instruction, model, **_):
            homes.append(_account())
            if _account() == "claude":
                raise _LIMIT
            return '{"final": "done"}', UsageParts(input_tokens=1, output_tokens=1)

        with patch.object(main.claude_adapter, "complete_agentic_file", AsyncMock(side_effect=_decision)), \
             patch.object(main.codex_adapter, "complete_agentic_file", AsyncMock()) as codex:
            resp = client.post("/v1/claude/chat/completions", json={**_BODY, "tools": _TOOLS}, headers=SAME)

        assert resp.status_code == 200
        assert homes == ["claude", "claude-alt"]
        assert resp.headers["x-cli-proxy-failover"] == "claude->claude-alt"
        assert codex.await_count == 0


class TestDocumentMode:
    _DOC = {"model": "claude-opus-latest", "document_mode": True, "document": "",
            "messages": [{"role": "user", "content": "Write."}]}

    def test_limit_moves_the_document_to_the_alt(self, client, alt_home) -> None:
        calls: list[tuple[str, str]] = []

        async def _document(system, instruction, document, ws, model, *a, **k):
            calls.append((_account(), model))
            if _account() == "claude":
                raise RuntimeError("claude CLI (document mode) exited with code 1: You've hit your session limit")
            return "written", UsageParts(input_tokens=1, output_tokens=1)

        with patch.object(main.claude_adapter, "complete_document", AsyncMock(side_effect=_document)), \
             patch.object(main.codex_adapter, "complete_document", _ok("codex text")) as codex:
            resp = client.post("/v1/claude/chat/completions", json=self._DOC, headers=SAME)

        assert resp.status_code == 200
        assert resp.json()["choices"][0]["message"]["content"] == "written"
        assert calls == [("claude", "claude-opus-latest"), ("claude-alt", "claude-opus-latest")]
        assert resp.headers["x-cli-proxy-failover"] == "claude->claude-alt"
        assert codex.await_count == 0

    def test_empty_result_after_the_handover_names_it_and_is_not_retried(self, client, alt_home) -> None:
        async def _document(system, instruction, document, ws, model, *a, **k):
            if _account() == "claude":
                raise RuntimeError("claude CLI (document mode) exited with code 1: You've hit your session limit")
            return "", UsageParts(input_tokens=1, output_tokens=1)

        with patch.object(main.claude_adapter, "complete_document", AsyncMock(side_effect=_document)), \
             patch.object(main.codex_adapter, "complete_document", _ok("codex text")) as codex:
            resp = client.post("/v1/claude/chat/completions", json=self._DOC, headers=SAME)

        assert resp.status_code == 502
        assert resp.json()["error"]["code"] == "empty_result"
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert resp.headers["x-cli-proxy-failover"] == "claude->claude-alt"
        assert codex.await_count == 0
        assert failover._state("claude-alt").consecutive_failures == 0


class TestGenericEndpoint:
    def test_header_changes_nothing_on_the_generic_endpoint(self, client, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(main.provider_sync, "get_provider_config",
                            lambda name: {"name": name, "settings": {"cli_kind": "claude"}})
        fake, seen = _claude({"claude": _LIMIT})
        with patch.object(claude_adapter, "complete_with_usage", fake):
            resp = client.post("/v1/cli/claude-cli/chat/completions", json=_BODY, headers=SAME)
        assert resp.status_code == 502
        assert [a for a, _ in seen] == ["claude"]
