"""The gateway acts like a plain OpenAI API: no browsing unless asked, no memory, no transcripts.

Web search is an opt-in per request (`web_search_options`) or per deployment (`CLI_WEB_SEARCH=1`).
Every CLI process runs without session transcripts and without Claude Code's auto-memory.
"""
from __future__ import annotations

import os
import sys
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import claude_adapter  # noqa: E402
import main  # noqa: E402
import policy  # noqa: E402
from usage import UsageParts  # noqa: E402


@pytest.fixture(autouse=True)
def _no_operator_default(monkeypatch):
    monkeypatch.delenv(policy.WEB_SEARCH_ENV, raising=False)
    monkeypatch.delenv("CLAUDE_LEAN_CHAT", raising=False)


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


def _body(**extra) -> dict:
    return {"model": "claude-haiku-latest", "messages": [{"role": "user", "content": "hi"}], **extra}


def _ok() -> AsyncMock:
    return AsyncMock(return_value=("ok", UsageParts(input_tokens=1, output_tokens=1)))


def _searches_during_call(client: TestClient, **extra) -> bool:
    seen: list[bool] = []

    async def fake(*args, **kwargs):
        seen.append(policy.web_search_allowed())
        return "ok", UsageParts(input_tokens=1, output_tokens=1)

    with patch.object(main.claude_adapter, "complete_with_usage", fake):
        assert client.post("/v1/claude/chat/completions", json=_body(**extra)).status_code == 200
    return seen[0]


class TestRequestPolicy:
    def test_no_web_search_by_default(self, client: TestClient) -> None:
        assert _searches_during_call(client) is False

    def test_web_search_options_opt_in(self, client: TestClient) -> None:
        assert _searches_during_call(client, web_search_options={}) is True

    def test_opt_in_does_not_leak_into_the_next_request(self, client: TestClient) -> None:
        assert _searches_during_call(client, web_search_options={}) is True
        assert _searches_during_call(client) is False

    def test_operator_default_switches_search_on(self, client: TestClient, monkeypatch) -> None:
        monkeypatch.setenv(policy.WEB_SEARCH_ENV, "1")
        assert _searches_during_call(client) is True

    def test_operator_default_off_values(self, monkeypatch) -> None:
        for value in ("", "0", "false", "off"):
            monkeypatch.setenv(policy.WEB_SEARCH_ENV, value)
            assert policy.web_search_default() is False


class TestLeanTools:
    def test_text_only_has_no_tools(self) -> None:
        assert claude_adapter._lean_tools(image_refs=False) == ""

    def test_images_may_be_fetched_but_not_searched(self) -> None:
        assert claude_adapter._lean_tools(image_refs=True) == "WebFetch"

    def test_opt_in_gets_search_and_fetch(self, monkeypatch) -> None:
        monkeypatch.setenv(policy.WEB_SEARCH_ENV, "1")
        assert claude_adapter._lean_tools(image_refs=False) == "WebSearch,WebFetch"


class TestProcessEnvironment:
    def test_auto_memory_is_disabled_and_home_is_the_account_home(self) -> None:
        env = policy.agent_env("/auth/claude")
        assert env["HOME"] == "/auth/claude"
        assert env["CLAUDE_CODE_DISABLE_AUTO_MEMORY"] == "1"

    @pytest.mark.parametrize("name", ["_run_claude_decision", "_run_claude_document", "_stream_claude",
                                      "_exec_print", "_probe_alias"])
    def test_every_claude_spawn_uses_the_policy_environment(self, name: str) -> None:
        import inspect
        assert "policy.agent_env" in inspect.getsource(getattr(claude_adapter, name))
