"""Fix C: an empty document-mode result must not open the circuit breaker.

Whether the CLI writes the document depends on the request's content (a refusal, a chat answer
instead of a file edit). Before Fix C one request that came back empty twice (the Atelier's direct
path retries once) opened claude's breaker for OPEN_SECONDS, and every other caller's direct request
was refused with 503 or redirected. Now the empty result is an error of that request only; /health
counts it and raises `empty_result_alert` when the rate is conspicuous, without changing behaviour.
"""
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

_TRANSPORT = RuntimeError(
    "claude CLI (document mode) exited with code 1: (subtype=error_during_execution, is_error=true)")
_LIMIT = RuntimeError(
    "claude CLI (document mode) exited with code 1: You've hit your session limit "
    "(subtype=success, is_error=true)")
_OFF = {"x-cli-proxy-failover": "off"}


def _doc_body(document: str = "", **extra) -> dict:
    return {
        "model": "claude-opus-latest",
        "messages": [{"role": "system", "content": "You write."},
                     {"role": "user", "content": "Write the text."}],
        "document_mode": True,
        "document": document,
        **extra,
    }


def _usage() -> UsageParts:
    return UsageParts(input_tokens=10, output_tokens=5)


def _returns(text: str):
    return AsyncMock(return_value=(text, _usage()))


def _sequence(*outcomes):
    """An adapter mock that returns text or raises, one outcome per call."""
    effects = [o if isinstance(o, BaseException) else (o, _usage()) for o in outcomes]
    return AsyncMock(side_effect=effects)


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


@pytest.fixture(autouse=True)
def _defaults(monkeypatch) -> None:
    monkeypatch.setattr(failover, "ALT_ENABLED", False)
    monkeypatch.setattr(failover, "FAILS", 2)
    monkeypatch.setattr(failover, "EMPTY_RESULT_WINDOW_SECONDS", 600)
    monkeypatch.setattr(failover, "EMPTY_RESULT_ALERT_MIN", 5)
    monkeypatch.setattr(failover, "EMPTY_RESULT_ALERT_RATIO", 0.5)


def _health(client: TestClient) -> dict:
    return client.get("/health").json()


def _backend(client: TestClient, name: str) -> dict:
    return _health(client)["failover"]["backends"][name]


class TestEmptyResultsDoNotOpenTheBreaker:
    @pytest.mark.parametrize("count", [2, 10])
    def test_consecutive_empty_results_leave_the_breaker_closed(
            self, client: TestClient, count: int) -> None:
        with patch.object(main.claude_adapter, "complete_document", _returns("")), \
             patch.object(main.codex_adapter, "complete_document", _returns("x")) as codex:
            codes = [client.post("/v1/claude/chat/completions", json=_doc_body()).status_code
                     for _ in range(count)]

        assert codes == [502] * count
        assert codex.await_count == 0, "an empty result is still never handed over"
        health = _health(client)
        claude = health["failover"]["backends"]["claude"]
        assert claude["empty_results"] == count
        assert claude["empty_results_recent"] == count
        assert claude["document_requests_recent"] == count
        assert claude["empty_result_rate"] == 1.0
        assert claude["last_empty_result_at"] is not None
        assert claude["consecutive_failures"] == 0
        assert claude["available"] is True
        assert claude["healthy"] is True
        assert claude["open_until"] is None
        assert health["degraded"] is False

    def test_other_callers_are_served_after_an_off_caller_came_back_empty_twice(
            self, client: TestClient) -> None:
        """The review scenario M2: caller A's request is answered without a document twice."""
        adapter = _sequence("", "", "Text von B", "Text von C")
        with patch.object(main.claude_adapter, "complete_document", adapter), \
             patch.object(main.codex_adapter, "complete_document", _returns("codex")) as codex:
            first = client.post("/v1/claude/chat/completions", json=_doc_body(), headers=_OFF)
            retry = client.post("/v1/claude/chat/completions", json=_doc_body(), headers=_OFF)
            other_off = client.post("/v1/claude/chat/completions", json=_doc_body(), headers=_OFF)
            other = client.post("/v1/claude/chat/completions", json=_doc_body())

        assert [first.status_code, retry.status_code] == [502, 502]
        assert first.json()["error"]["code"] == "empty_result"
        assert other_off.status_code == 200, "no 503 backend_unavailable for the next caller"
        assert other_off.json()["choices"][0]["message"]["content"] == "Text von B"
        assert other.status_code == 200
        assert other.headers["x-cli-proxy-provider"] == "claude", "no redirect to codex"
        assert "x-cli-proxy-failover" not in other.headers
        assert codex.await_count == 0


class TestEmptyResultBetweenRealFailures:
    def test_neither_resets_nor_raises_the_failure_count(self, client: TestClient) -> None:
        adapter = _sequence(_TRANSPORT, "", _TRANSPORT)
        with patch.object(main.claude_adapter, "complete_document", adapter):
            client.post("/v1/claude/chat/completions", json=_doc_body(), headers=_OFF)
            assert _backend(client, "claude")["consecutive_failures"] == 1

            client.post("/v1/claude/chat/completions", json=_doc_body(), headers=_OFF)
            after_empty = _backend(client, "claude")
            assert after_empty["consecutive_failures"] == 1
            assert after_empty["last_reason"] == "transport", "the fault picture stays"
            assert after_empty["last_success_at"] is None

            client.post("/v1/claude/chat/completions", json=_doc_body(), headers=_OFF)

        claude = _backend(client, "claude")
        assert claude["consecutive_failures"] == 2
        assert claude["available"] is False, "two real faults still open the breaker"
        assert claude["healthy"] is False
        assert claude["empty_results"] == 1

    def test_a_success_still_resets_the_counters(self, client: TestClient) -> None:
        adapter = _sequence(_TRANSPORT, "", "Der Text.")
        with patch.object(main.claude_adapter, "complete_document", adapter):
            for _ in range(3):
                client.post("/v1/claude/chat/completions", json=_doc_body(), headers=_OFF)

        claude = _backend(client, "claude")
        assert claude["consecutive_failures"] == 0
        assert claude["last_success_at"] is not None
        assert claude["empty_results"] == 1

    def test_an_empty_probe_does_not_close_an_open_breaker(self, client: TestClient, monkeypatch) -> None:
        now = [1_000_000.0]
        monkeypatch.setattr(failover.time, "time", lambda: now[0])
        failover.record_failure("claude", "transport", str(_TRANSPORT))
        failover.record_failure("claude", "transport", str(_TRANSPORT))
        assert failover.available("claude") is False

        now[0] += failover.OPEN_SECONDS + 1
        with patch.object(main.claude_adapter, "complete_document", _returns("")):
            resp = client.post("/v1/claude/chat/completions", json=_doc_body(), headers=_OFF)

        assert resp.status_code == 502
        assert resp.json()["error"]["code"] == "empty_result"
        claude = failover.snapshot()["backends"]["claude"]
        assert claude["healthy"] is False, "only a real success closes the breaker"
        assert claude["consecutive_failures"] == 2


class TestEmptyResultAfterFailover:
    def test_burdens_neither_the_origin_nor_the_target(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_document", AsyncMock(side_effect=_LIMIT)), \
             patch.object(main.codex_adapter, "complete_document", _returns("")):
            resp = client.post("/v1/claude/chat/completions", json=_doc_body())

        assert resp.status_code == 502
        assert resp.headers["x-cli-proxy-failover"] == "claude->codex"
        claude = _backend(client, "claude")
        codex = _backend(client, "codex")
        assert claude["consecutive_failures"] == 1, "only claude's own fault"
        assert claude["empty_results"] == 0
        assert claude["document_requests_recent"] == 1
        assert codex["consecutive_failures"] == 0
        assert codex["empty_results"] == 1
        assert codex["document_requests_recent"] == 1
        assert codex["healthy"] is True

    def test_repeated_handovers_open_only_the_faulted_origin(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_document", AsyncMock(side_effect=_LIMIT)), \
             patch.object(main.codex_adapter, "complete_document", _returns("")):
            for _ in range(3):
                client.post("/v1/claude/chat/completions", json=_doc_body())

        claude = _backend(client, "claude")
        codex = _backend(client, "codex")
        assert claude["healthy"] is False, "claude's own limit faults trip its breaker as before"
        assert codex["healthy"] is True
        assert codex["available"] is True
        assert codex["consecutive_failures"] == 0
        assert codex["empty_results"] == 3


class TestEmptyResultAlert:
    def _empties(self, client: TestClient, count: int) -> None:
        with patch.object(main.claude_adapter, "complete_document", _returns("")):
            for _ in range(count):
                client.post("/v1/claude/chat/completions", json=_doc_body())

    def _successes(self, client: TestClient, count: int) -> None:
        with patch.object(main.claude_adapter, "complete_document", _returns("Der Text.")):
            for _ in range(count):
                client.post("/v1/claude/chat/completions", json=_doc_body())

    def test_defaults_are_neutral(self, client: TestClient) -> None:
        health = _health(client)
        assert health["failover"]["empty_result_alert"] is False
        assert health["failover"]["empty_result_window_seconds"] == 600
        for name in ("claude", "codex"):
            backend = health["failover"]["backends"][name]
            assert backend["empty_result_alert"] is False
            assert backend["empty_results_recent"] == 0
            assert backend["document_requests_recent"] == 0
            assert backend["empty_result_rate"] is None
            assert backend["last_empty_result_at"] is None

    def test_five_empty_results_out_of_five_raise_the_alert(self, client: TestClient) -> None:
        self._empties(client, 5)
        health = _health(client)
        assert health["failover"]["backends"]["claude"]["empty_result_alert"] is True
        assert health["failover"]["empty_result_alert"] is True
        assert health["failover"]["backends"]["codex"]["empty_result_alert"] is False
        assert health["degraded"] is False, "the alert is independent of the breaker"

    def test_four_empty_results_are_below_the_minimum(self, client: TestClient) -> None:
        self._empties(client, 4)
        assert _health(client)["failover"]["empty_result_alert"] is False

    def test_exactly_half_is_not_more_than_half(self, client: TestClient) -> None:
        self._empties(client, 5)
        self._successes(client, 5)
        claude = _backend(client, "claude")
        assert claude["empty_result_rate"] == 0.5
        assert claude["empty_result_alert"] is False

    def test_more_than_half_raises_the_alert(self, client: TestClient) -> None:
        self._empties(client, 5)
        self._successes(client, 4)
        claude = _backend(client, "claude")
        assert claude["empty_result_rate"] == pytest.approx(5 / 9, abs=0.001)
        assert claude["empty_result_alert"] is True

    def test_failed_document_attempts_count_in_the_rate(self, client: TestClient, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 100)
        self._empties(client, 5)
        with patch.object(main.claude_adapter, "complete_document", AsyncMock(side_effect=_TRANSPORT)):
            for _ in range(5):
                client.post("/v1/claude/chat/completions", json=_doc_body(), headers=_OFF)
        claude = _backend(client, "claude")
        assert claude["document_requests_recent"] == 10
        assert claude["empty_result_alert"] is False

    def test_chat_requests_do_not_count_as_document_requests(self, client: TestClient) -> None:
        body = {"model": "claude-haiku-latest", "messages": [{"role": "user", "content": "hi"}]}
        with patch.object(main.claude_adapter, "complete_with_usage", _returns("Hallo")):
            client.post("/v1/claude/chat/completions", json=body)
        assert _backend(client, "claude")["document_requests_recent"] == 0

    def test_the_window_forgets_old_empty_results(self, monkeypatch) -> None:
        now = [2_000_000.0]
        monkeypatch.setattr(failover.time, "time", lambda: now[0])
        for _ in range(5):
            failover.record_document_attempt("claude")
            failover.record_empty_result("claude", "no text")
        assert failover.snapshot()["empty_result_alert"] is True

        now[0] += 601
        claude = failover.snapshot()["backends"]["claude"]
        assert claude["empty_result_alert"] is False
        assert claude["empty_results_recent"] == 0
        assert claude["document_requests_recent"] == 0
        assert claude["empty_result_rate"] is None
        assert claude["empty_results"] == 5, "the total since start stays"
        assert claude["last_empty_result_at"] == 2_000_000.0

    def test_thresholds_are_configurable(self, client: TestClient, monkeypatch) -> None:
        monkeypatch.setattr(failover, "EMPTY_RESULT_ALERT_MIN", 2)
        self._empties(client, 2)
        assert _health(client)["failover"]["empty_result_alert"] is True

    def test_the_alert_changes_no_behaviour(self, client: TestClient) -> None:
        self._empties(client, 6)
        assert _health(client)["failover"]["empty_result_alert"] is True
        with patch.object(main.claude_adapter, "complete_document", _returns("Der Text.")), \
             patch.object(main.codex_adapter, "complete_document", _returns("codex")) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_doc_body(), headers=_OFF)
        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "claude"
        assert codex.await_count == 0
