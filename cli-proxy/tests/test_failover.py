"""Tests for the claude <-> codex provider failover (classifier + circuit breaker).

Background: on 2026-07-22 the claude CLI dropped its OAuth tokens and every
/v1/claude call returned 502 for 1h42 while a healthy, subscription-covered codex
sat next to it unused. See ../../../../docs/cli-proxy-failover.md.
"""
from __future__ import annotations

import os
import sys

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import failover  # noqa: E402


@pytest.fixture(autouse=True)
def _reset() -> None:
    failover.reset()


class TestClassify:
    @pytest.mark.parametrize("message", [
        # The verbatim message from the 2026-07-22 outage.
        "claude CLI error: Failed to authenticate: OAuth session expired and could not be refreshed",
        "claude CLI exited with code 1: Invalid API key provided.",
        "Not logged in. Please run /login",
        "codex CLI error: unauthorized",
        "request failed with status 401",
    ])
    def test_auth_failures(self, message: str) -> None:
        assert failover.classify(RuntimeError(message)) == "auth"

    @pytest.mark.parametrize("message", [
        "claude CLI error: rate limit exceeded, try again later",
        "You have hit your usage limit for this 5-hour window",
        "codex CLI exited with code 1: 429 Too Many Requests",
        "quota exceeded for this organization",
        "API is temporarily overloaded",
    ])
    def test_limit_failures(self, message: str) -> None:
        assert failover.classify(RuntimeError(message)) == "limit"

    @pytest.mark.parametrize("message", [
        "claude CLI timed out after 600 seconds",
        "claude CLI exited with code 137: ",
        "connection reset by peer",
        "broken pipe while writing to CLI stdin",
        "upstream returned 503",
    ])
    def test_transport_failures(self, message: str) -> None:
        assert failover.classify(RuntimeError(message)) == "transport"

    @pytest.mark.parametrize("message", [
        "Could not produce valid structured output: schema mismatch at $.items[0]",
        "something entirely unexpected happened",
        "",
    ])
    def test_unknown_is_not_failover_worthy(self, message: str) -> None:
        reason = failover.classify(RuntimeError(message))
        assert reason == "unknown"
        assert not failover.should_failover(reason)

    def test_auth_wins_over_transport_when_both_patterns_present(self) -> None:
        # The real outage message contains BOTH "exited with code" (transport) and the
        # auth wording. Auth is the more actionable cause and must win.
        exc = RuntimeError(
            "claude CLI exited with code 1: Failed to authenticate: OAuth session expired")
        assert failover.classify(exc) == "auth"

    def test_classification_is_case_insensitive(self) -> None:
        assert failover.classify(RuntimeError("FAILED TO AUTHENTICATE")) == "auth"

    @pytest.mark.parametrize("reason", ["auth", "limit", "transport"])
    def test_failover_worthy_reasons(self, reason: str) -> None:
        assert failover.should_failover(reason)


class TestBreaker:
    def test_backend_starts_available(self) -> None:
        assert failover.available("claude")
        assert failover.available("codex")

    def test_opens_after_threshold_failures(self, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 2)
        failover.record_failure("claude", "auth", "boom")
        assert failover.available("claude"), "one failure must not open the breaker"
        failover.record_failure("claude", "auth", "boom")
        assert not failover.available("claude")

    def test_unknown_reason_does_not_count_toward_breaker(self, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        failover.record_failure("claude", "unknown", "weird")
        assert failover.available("claude")

    def test_success_resets_the_counter(self, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 2)
        failover.record_failure("claude", "auth", "boom")
        failover.record_success("claude")
        failover.record_failure("claude", "auth", "boom")
        assert failover.available("claude"), "counter must have been reset by the success"

    def test_probe_after_open_window_expires(self, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        monkeypatch.setattr(failover, "OPEN_SECONDS", 300)
        now = [1_000_000.0]
        monkeypatch.setattr(failover.time, "time", lambda: now[0])

        failover.record_failure("claude", "auth", "boom")
        assert not failover.available("claude")

        now[0] += 299
        assert not failover.available("claude"), "still inside the open window"

        now[0] += 2
        assert failover.available("claude"), "window elapsed — one probe must be allowed"

    def test_failed_probe_reopens_the_breaker(self, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        monkeypatch.setattr(failover, "OPEN_SECONDS", 10)
        now = [1_000_000.0]
        monkeypatch.setattr(failover.time, "time", lambda: now[0])

        failover.record_failure("claude", "auth", "boom")
        now[0] += 11
        assert failover.available("claude")

        failover.record_failure("claude", "auth", "still broken")
        assert not failover.available("claude")

    def test_successful_probe_restores_the_backend(self, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        monkeypatch.setattr(failover, "OPEN_SECONDS", 10)
        now = [1_000_000.0]
        monkeypatch.setattr(failover.time, "time", lambda: now[0])

        failover.record_failure("claude", "auth", "boom")
        now[0] += 11
        failover.record_success("claude")
        assert failover.available("claude")
        assert failover.snapshot()["backends"]["claude"]["consecutive_failures"] == 0


class TestAttemptOrder:
    def test_healthy_primary_goes_first(self) -> None:
        assert failover.attempt_order("claude") == ["claude", "codex"]
        assert failover.attempt_order("codex") == ["codex", "claude"]

    def test_open_primary_puts_partner_first(self, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        failover.record_failure("claude", "auth", "boom")
        assert failover.attempt_order("claude") == ["codex", "claude"]

    def test_both_open_still_tries_primary_first(self, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        failover.record_failure("claude", "auth", "boom")
        failover.record_failure("codex", "auth", "boom")
        assert failover.attempt_order("claude") == ["claude", "codex"], \
            "with no healthy backend left, honour the caller's choice"

    def test_disabled_yields_primary_only(self, monkeypatch) -> None:
        monkeypatch.setattr(failover, "ENABLED", False)
        assert failover.attempt_order("claude") == ["claude"]

    def test_unknown_backend_has_no_partner(self) -> None:
        assert failover.attempt_order("gemini") == ["gemini"]


class TestSnapshot:
    def test_reports_not_degraded_when_all_healthy(self) -> None:
        snap = failover.snapshot()
        assert snap["enabled"] is True
        assert snap["degraded"] is False
        assert snap["backends"]["claude"]["available"] is True

    def test_reports_degraded_with_cause_when_a_breaker_is_open(self, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        failover.record_failure("claude", "auth", "claude CLI error: Failed to authenticate")

        snap = failover.snapshot()
        assert snap["degraded"] is True
        claude = snap["backends"]["claude"]
        assert claude["available"] is False
        assert claude["last_reason"] == "auth"
        assert "Failed to authenticate" in claude["last_error"]
        assert claude["open_until"] is not None
        assert claude["last_failure_at"] is not None

    def test_counts_completed_failovers(self) -> None:
        failover.record_failover("claude", "codex")
        failover.record_failover("claude", "codex")
        assert failover.snapshot()["backends"]["claude"]["failover_count"] == 2

    def test_long_error_text_is_truncated(self, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        failover.record_failure("claude", "auth", "x" * 5000)
        assert len(failover.snapshot()["backends"]["claude"]["last_error"]) <= 500


class TestOsErrors:
    def test_missing_cli_binary_is_a_transport_fault(self) -> None:
        # A vanished/non-executable CLI raises from the spawn as OSError, without a
        # message our text patterns would recognise -- but it is exactly the kind of
        # local fault the partner backend does not share.
        assert failover.classify(FileNotFoundError(2, "No such file or directory", "claude")) \
            == "transport"

    def test_permission_error_is_a_transport_fault(self) -> None:
        assert failover.classify(PermissionError(13, "Permission denied")) == "transport"
