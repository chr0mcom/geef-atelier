"""Second Claude subscription as a fallback stage: claude -> claude-alt -> codex.

Background: the gateway ran on exactly one Claude login (CLAUDE_HOME). When that account hit
its session/weekly limit or its OAuth session died, requests went straight to codex. The
operator wants to park an alternative Claude subscription that takes over first, and only
then fall back to codex. See ../../../../docs/cli-proxy-failover.md (Teil C).
"""
from __future__ import annotations

import asyncio
import os
import sys
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
_LIMIT = RuntimeError("claude CLI error: You've hit your weekly limit · resets Sep 24, 11pm (UTC)")
_AUTH = RuntimeError("claude CLI error: Failed to authenticate: OAuth session expired")
_TRANSPORT = RuntimeError("claude CLI timed out after 600 seconds")


@pytest.fixture()
def alt_home(tmp_path: Path, monkeypatch) -> str:
    """A configured alternative account: credentials file present under its own HOME."""
    home = tmp_path / "claude-alt"
    (home / ".claude").mkdir(parents=True)
    (home / ".claude" / ".credentials.json").write_text("{}")
    monkeypatch.setattr(failover, "ALT_HOME", str(home))
    return str(home)


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


def _by_account(primary_exc: Exception, alt_result: str = "from alt"):
    """complete_with_usage stand-in that fails on the primary HOME and works on the alt one."""
    async def _impl(prompt, model, max_tokens, **_):
        if claude_adapter.current_home() == claude_adapter.CLAUDE_HOME:
            raise primary_exc
        return alt_result, UsageParts(input_tokens=1, output_tokens=1)
    return AsyncMock(side_effect=_impl)


def _ok(text: str = "ok"):
    return AsyncMock(return_value=(text, UsageParts(input_tokens=1, output_tokens=1)))


class TestConfiguration:
    def test_not_configured_without_credentials(self, tmp_path, monkeypatch) -> None:
        monkeypatch.setattr(failover, "ALT_HOME", str(tmp_path / "leer"))
        assert failover.alt_configured() is False

    def test_configured_once_credentials_exist(self, alt_home) -> None:
        assert failover.alt_configured() is True

    def test_kill_switch_disables_the_alt_account(self, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(failover, "ALT_ENABLED", False)
        assert failover.alt_configured() is False

    def test_cli_of_maps_accounts_to_their_cli(self) -> None:
        assert failover.cli_of("claude") == "claude"
        assert failover.cli_of("claude-alt") == "claude"
        assert failover.cli_of("codex") == "codex"


class TestAttemptOrder:
    def test_unconfigured_chain_is_unchanged(self, tmp_path, monkeypatch) -> None:
        monkeypatch.setattr(failover, "ALT_HOME", str(tmp_path / "leer"))
        assert failover.attempt_order("claude") == ["claude", "codex"]

    def test_alt_sits_between_claude_and_codex(self, alt_home) -> None:
        assert failover.attempt_order("claude") == ["claude", "claude-alt", "codex"]

    def test_codex_primary_ends_with_both_claude_accounts(self, alt_home) -> None:
        assert failover.attempt_order("codex") == ["codex", "claude", "claude-alt"]

    def test_open_primary_promotes_alt_before_codex(self, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        failover.record_failure("claude", "limit", "weekly limit")
        assert failover.attempt_order("claude") == ["claude-alt", "codex", "claude"]

    def test_both_claude_accounts_open_leaves_codex_first(self, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        failover.record_failure("claude", "limit", "weekly limit")
        failover.record_failure("claude-alt", "limit", "weekly limit")
        assert failover.attempt_order("claude") == ["codex", "claude", "claude-alt"]

    def test_everything_open_honours_the_callers_choice(self, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        for name in ("claude", "claude-alt", "codex"):
            failover.record_failure(name, "auth", "boom")
        assert failover.attempt_order("claude") == ["claude", "claude-alt", "codex"]

    def test_disabled_failover_yields_primary_only(self, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(failover, "ENABLED", False)
        assert failover.attempt_order("claude") == ["claude"]


class TestSnapshot:
    def test_alt_is_absent_when_not_configured(self, tmp_path, monkeypatch) -> None:
        monkeypatch.setattr(failover, "ALT_HOME", str(tmp_path / "leer"))
        assert "claude-alt" not in failover.snapshot()["backends"]

    def test_alt_is_reported_as_reserve_when_configured(self, alt_home) -> None:
        alt = failover.snapshot()["backends"]["claude-alt"]
        assert alt["reserve"] is True
        assert alt["healthy"] is True

    def test_a_broken_reserve_does_not_raise_the_operator_alarm(self, alt_home, monkeypatch) -> None:
        # cliproxy-watch keys off `degraded`; a dead spare must not page anyone while the
        # primary and codex are serving -- it is visible under backends["claude-alt"] instead.
        monkeypatch.setattr(failover, "FAILS", 1)
        failover.record_failure("claude-alt", "auth", "expired")
        snap = failover.snapshot()
        assert snap["backends"]["claude-alt"]["healthy"] is False
        assert snap["degraded"] is False


class TestAccountBinding:
    def test_default_home_is_the_primary_account(self) -> None:
        assert claude_adapter.current_home() == claude_adapter.CLAUDE_HOME

    def test_bind_and_reset_switch_the_home(self, alt_home) -> None:
        async def _run():
            claude_adapter.bind_account("claude-alt")
            assert claude_adapter.current_home() == alt_home
            claude_adapter.bind_account("claude")
            assert claude_adapter.current_home() == claude_adapter.CLAUDE_HOME
        asyncio.run(_run())

    def test_concurrent_requests_do_not_see_each_others_account(self, alt_home) -> None:
        async def _worker(account: str) -> str:
            claude_adapter.bind_account(account)
            await asyncio.sleep(0.01)  # let the other task bind in between
            return claude_adapter.current_home()

        async def _run():
            return await asyncio.gather(_worker("claude-alt"), _worker("claude"))

        alt, primary = asyncio.run(_run())
        assert alt == alt_home
        assert primary == claude_adapter.CLAUDE_HOME


class TestEndpointFailover:
    @pytest.mark.parametrize("exc", [_LIMIT, _AUTH])
    def test_limit_or_auth_on_primary_is_served_by_the_alt_account(
        self, client, alt_home, exc,
    ) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _by_account(exc)) as claude, \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY)

        assert resp.status_code == 200
        assert resp.json()["choices"][0]["message"]["content"] == "from alt"
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert resp.headers["x-cli-proxy-failover"] == "claude->claude-alt"
        assert claude.await_count == 2
        assert codex.await_count == 0

    def test_model_name_stays_on_the_alt_account(self, client, alt_home) -> None:
        # Same CLI, same model namespace -- unlike the codex hand-over, the name must survive.
        with patch.object(main.claude_adapter, "complete_with_usage", _by_account(_LIMIT)) as claude, \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()):
            client.post("/v1/claude/chat/completions", json=_BODY)

        assert claude.await_args_list[1].args[1] == "claude-haiku-latest"

    def test_both_claude_accounts_exhausted_falls_through_to_codex(self, client, alt_home) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage",
                          AsyncMock(side_effect=_LIMIT)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok("from codex")) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_BODY)

        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "codex"
        assert resp.headers["x-cli-proxy-failover"] == "claude->codex"
        assert codex.await_count == 1

    def test_transport_fault_skips_the_alt_account(self, client, alt_home) -> None:
        # A hung/crashed claude CLI is shared by both accounts; retrying it on the spare
        # would only double the wait before codex gets its turn.
        with patch.object(main.claude_adapter, "complete_with_usage",
                          AsyncMock(side_effect=_TRANSPORT)) as claude, \
             patch.object(main.codex_adapter, "complete_with_usage", _ok("from codex")):
            resp = client.post("/v1/claude/chat/completions", json=_BODY)

        assert resp.headers["x-cli-proxy-provider"] == "codex"
        assert claude.await_count == 1

    def test_without_alt_credentials_behaviour_is_unchanged(self, client, tmp_path, monkeypatch) -> None:
        monkeypatch.setattr(failover, "ALT_HOME", str(tmp_path / "leer"))
        with patch.object(main.claude_adapter, "complete_with_usage",
                          AsyncMock(side_effect=_LIMIT)) as claude, \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()):
            resp = client.post("/v1/claude/chat/completions", json=_BODY)

        assert resp.headers["x-cli-proxy-provider"] == "codex"
        assert claude.await_count == 1

    def test_healthy_primary_never_touches_the_alt_account(self, client, alt_home) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", _ok()) as claude:
            resp = client.post("/v1/claude/chat/completions", json=_BODY)

        assert resp.headers["x-cli-proxy-provider"] == "claude"
        assert claude.await_count == 1

    def test_open_breaker_routes_straight_to_the_alt_account(
        self, client, alt_home, monkeypatch,
    ) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        failover.record_failure("claude", "limit", "weekly limit")
        with patch.object(main.claude_adapter, "complete_with_usage", _by_account(_LIMIT)) as claude:
            resp = client.post("/v1/claude/chat/completions", json=_BODY)

        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert claude.await_count == 1, "primary must not be tried while its breaker is open"

    def test_codex_endpoint_reaches_the_alt_account_last(self, client, alt_home) -> None:
        with patch.object(main.codex_adapter, "complete_with_usage",
                          AsyncMock(side_effect=RuntimeError("codex CLI error: rate limit exceeded"))), \
             patch.object(main.claude_adapter, "complete_with_usage", _by_account(_LIMIT)):
            resp = client.post("/v1/codex/chat/completions", json={**_BODY, "model": ""})

        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"


class TestStreamingFailover:
    def test_buffered_stream_moves_to_the_alt_account_before_the_first_byte(
        self, client, alt_home,
    ) -> None:
        body = {**_BODY, "stream": True, "response_format": {"type": "json_object"}}
        ok_json = ('{"a": 1}', UsageParts(input_tokens=1, output_tokens=1))

        async def _impl(prompt, model, max_tokens, **_):
            if claude_adapter.current_home() == claude_adapter.CLAUDE_HOME:
                raise _LIMIT
            return ok_json
        with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=_impl)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=body)

        assert resp.status_code == 200
        assert '{\\"a\\": 1}' in resp.text or '{"a": 1}' in resp.text
        assert codex.await_count == 0

    def test_token_stream_uses_the_alt_account_when_the_primary_breaker_is_open(
        self, client, alt_home, monkeypatch,
    ) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        failover.record_failure("claude", "limit", "weekly limit")
        seen: list[str] = []

        async def _stream(prompt, model, max_tokens, **_):
            seen.append(claude_adapter.current_home())
            yield ("delta", "streamed by alt")

        with patch.object(main.claude_adapter, "stream", _stream):
            resp = client.post("/v1/claude/chat/completions", json={**_BODY, "stream": True})

        assert "streamed by alt" in resp.text
        assert seen == [alt_home]
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
