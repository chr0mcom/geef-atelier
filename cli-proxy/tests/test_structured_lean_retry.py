"""Structured output: lean-mode retry for claude and tolerant JSON extraction (2026-11-07).

Bug: with response_format json_schema and a role-play prompt ("you played this game, answer
from your own perspective"), the claude CLI in its default agent mode often declined in
prose. Both the first call and the identical "CRITICAL" retry produced no JSON, so the proxy
answered 200 with refusal "no JSON object found in the response" although ~1.5k tokens had
been generated. The retry now runs in lean mode (caller's system prompt as the real system
prompt, no agent prompt/tools/skills/memory), and the extractor considers every JSON object
in the text instead of only the first brace.
"""
from __future__ import annotations

import asyncio
import json
import os
import sys
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import claude_adapter  # noqa: E402
import main  # noqa: E402
from openai_format import ResponseFormat  # noqa: E402
from structured import iter_json_object_candidates, validate_and_extract  # noqa: E402
from usage import UsageParts  # noqa: E402

# Recorded verbatim (shortened) from a failing replay of the LLM-Shootout debriefing request.
RECORDED_AGENT_REFUSAL = (
    "I appreciate the detailed setup, but I need to be direct: I cannot truthfully answer this "
    "survey because I haven't actually played this game. The scenario describes actions and "
    "internal reasoning attributed to me, but I didn't participate in this Split or Steal "
    "interaction.\n\nIf this is:\n- **A research simulation** where you want me to roleplay an "
    "AI participant: I'd be happy to do that.\n\nWhat would be most useful?"
)

_SCHEMA = {
    "type": "object",
    "additionalProperties": False,
    "required": ["strategy", "trust_in_opponent", "tried_to_deceive"],
    "properties": {
        "strategy": {"type": "string"},
        "trust_in_opponent": {"type": "integer"},
        "tried_to_deceive": {"type": "boolean"},
    },
}
_VALID = '{"strategy": "Ich hielt mein Wort.", "trust_in_opponent": 4, "tried_to_deceive": false}'
_SYSTEM = "Das Spiel ist beendet. Antworte wahrheitsgemäß aus deiner eigenen Sicht."


def _rf(schema: dict | None = _SCHEMA) -> ResponseFormat:
    if schema is None:
        return ResponseFormat(type="json_object")
    return ResponseFormat(type="json_schema", json_schema={"name": "a", "schema": schema, "strict": True})


def _u(text: str, inp: int = 10, out: int = 5):
    return text, UsageParts(input_tokens=inp, output_tokens=out)


def _body(**extra) -> dict:
    body = {
        "model": "claude-haiku-latest",
        "messages": [
            {"role": "system", "content": _SYSTEM},
            {"role": "user", "content": "Beantworte die Fragen."},
        ],
        "response_format": {
            "type": "json_schema",
            "json_schema": {"name": "answer", "schema": _SCHEMA, "strict": True},
        },
    }
    body.update(extra)
    return body


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


@pytest.fixture(autouse=True)
def _clean_env(monkeypatch):
    monkeypatch.delenv("CLAUDE_LEAN_CHAT", raising=False)
    monkeypatch.delenv("CLAUDE_STRUCTURED_LEAN_RETRY", raising=False)


# ---------------------------------------------------------------------------
# Extraction
# ---------------------------------------------------------------------------

class TestExtraction:
    def test_recorded_agent_refusal_has_no_json(self) -> None:
        assert validate_and_extract(_rf(), RECORDED_AGENT_REFUSAL) == (
            None, "no JSON object found in the response")

    def test_fenced_answer(self) -> None:
        json_str, err = validate_and_extract(_rf(), f"```json\n{_VALID}\n```")
        assert err is None and json.loads(json_str)["trust_in_opponent"] == 4

    def test_stray_brace_in_leading_prose_does_not_hide_the_answer(self) -> None:
        text = "Hinweis: Platzhalter wie {name} ersetze ich nicht.\n" + _VALID
        json_str, err = validate_and_extract(_rf(), text)
        assert err is None and json.loads(json_str)["strategy"] == "Ich hielt mein Wort."

    def test_echoed_schema_before_answer_picks_the_conforming_object(self) -> None:
        text = f"Schema: {json.dumps(_SCHEMA)}\nAntwort:\n{_VALID}"
        json_str, err = validate_and_extract(_rf(), text)
        assert err is None and json.loads(json_str)["tried_to_deceive"] is False

    def test_first_fence_not_json_second_fence_is(self) -> None:
        text = "```text\nkein json\n```\n\n```json\n" + _VALID + "\n```"
        json_str, err = validate_and_extract(_rf(), text)
        assert err is None and json.loads(json_str)["trust_in_opponent"] == 4

    def test_backticks_inside_a_string_value(self) -> None:
        obj = {"strategy": "Code: ```x```", "trust_in_opponent": 2, "tried_to_deceive": True}
        json_str, err = validate_and_extract(_rf(), "Hier:\n" + json.dumps(obj))
        assert err is None and json.loads(json_str) == obj

    def test_schema_mismatch_reports_the_first_object(self) -> None:
        json_str, err = validate_and_extract(_rf(), '{"wrong": 1}')
        assert json_str is None and err.startswith("response does not match schema")

    def test_bare_array_is_not_an_object(self) -> None:
        assert validate_and_extract(_rf(None), "[1, 2]") == (None, "response JSON is not an object")

    def test_json_object_takes_the_outermost_object(self) -> None:
        json_str, err = validate_and_extract(_rf(None), 'ok: {"a": {"b": 1}} fertig')
        assert err is None and json.loads(json_str) == {"a": {"b": 1}}

    def test_candidate_scan_is_bounded(self) -> None:
        text = "{" * 50_000 + _VALID
        assert list(iter_json_object_candidates(text)) == []

    def test_huge_prose_wrapped_object_still_found_via_legacy_path(self) -> None:
        big = {"strategy": "x" * 2_500_000, "trust_in_opponent": 1, "tried_to_deceive": False}
        text = "Hier ist es: " + json.dumps(big) + " Ende"
        json_str, err = validate_and_extract(_rf(), text)
        assert err is None and json.loads(json_str)["trust_in_opponent"] == 1


# ---------------------------------------------------------------------------
# Endpoint behaviour
# ---------------------------------------------------------------------------

class TestLeanRetry:
    """The retry as it works when the first attempt is an agent-mode call (CLAUDE_LEAN_CHAT=0)."""

    @pytest.fixture(autouse=True)
    def _agent_first(self, monkeypatch) -> None:
        monkeypatch.setenv("CLAUDE_LEAN_CHAT", "0")

    def test_agent_refusal_then_lean_retry_succeeds(self, client: TestClient) -> None:
        mock = AsyncMock(side_effect=[_u(RECORDED_AGENT_REFUSAL, 100, 10), _u(_VALID, 20, 7)])
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json=_body())
        assert resp.status_code == 200
        data = resp.json()
        assert json.loads(data["choices"][0]["message"]["content"])["trust_in_opponent"] == 4
        assert data["choices"][0]["message"]["refusal"] is None
        assert data["usage"]["prompt_tokens"] == 120 and data["usage"]["completion_tokens"] == 17

        first, second = mock.await_args_list
        assert "system_prompt" not in first.kwargs, "first attempt keeps the agent-mode argv"
        assert "[SYSTEM]" in first.args[0]
        assert second.kwargs["system_prompt"] == _SYSTEM
        assert second.kwargs["image_refs"] is False
        lean_prompt = second.args[0]
        assert "[SYSTEM]" not in lean_prompt and _SYSTEM not in lean_prompt
        assert "[USER]\nBeantworte die Fragen." in lean_prompt
        assert "JSON Schema" in lean_prompt
        assert "previous response" not in lean_prompt
        assert second.args[1] == "claude-haiku-latest"

    def test_still_invalid_after_lean_retry_keeps_refusal_shape(self, client: TestClient) -> None:
        mock = AsyncMock(side_effect=[_u(RECORDED_AGENT_REFUSAL), _u("immer noch keins")])
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json=_body())
        assert resp.status_code == 200
        msg = resp.json()["choices"][0]["message"]
        assert msg["content"] is None
        assert msg["refusal"].startswith("Could not produce valid structured output")
        assert mock.await_count == 2

    def test_valid_first_answer_makes_exactly_one_agent_mode_call(self, client: TestClient) -> None:
        mock = AsyncMock(return_value=_u(_VALID))
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json=_body())
        assert resp.status_code == 200
        mock.assert_awaited_once()
        assert mock.await_args.kwargs == {}

    def test_no_system_message_uses_neutral_default(self, client: TestClient) -> None:
        mock = AsyncMock(side_effect=[_u("nope"), _u(_VALID)])
        body = _body(messages=[{"role": "user", "content": "Gib JSON."}])
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            client.post("/v1/claude/chat/completions", json=body)
        assert mock.await_args_list[1].kwargs["system_prompt"] == claude_adapter.LEAN_DEFAULT_SYSTEM_PROMPT

    def test_image_reference_keeps_image_fetch(self, client: TestClient) -> None:
        mock = AsyncMock(side_effect=[_u("nope"), _u(_VALID)])
        body = _body(messages=[{"role": "user", "content": [
            {"type": "text", "text": "Beschreibe"},
            {"type": "image_url", "image_url": {"url": "https://example.org/a.png"}},
        ]}])
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            client.post("/v1/claude/chat/completions", json=body)
        assert mock.await_args_list[1].kwargs["image_refs"] is True

    def test_reasoning_effort_travels_into_the_lean_retry(self, client: TestClient) -> None:
        mock = AsyncMock(side_effect=[_u("nope"), _u(_VALID)])
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            client.post("/v1/claude/chat/completions", json=_body(reasoning_effort="low"))
        assert mock.await_args_list[0].kwargs == {"reasoning_effort": "low"}
        assert mock.await_args_list[1].kwargs["reasoning_effort"] == "low"

    def test_kill_switch_restores_identical_retry(self, client: TestClient, monkeypatch) -> None:
        monkeypatch.setenv("CLAUDE_STRUCTURED_LEAN_RETRY", "0")
        mock = AsyncMock(side_effect=[_u("nope"), _u(_VALID)])
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json=_body())
        assert resp.status_code == 200
        second = mock.await_args_list[1]
        assert second.kwargs == {}
        assert "CRITICAL" in second.args[0]

    def test_codex_retry_is_unchanged(self, client: TestClient) -> None:
        mock = AsyncMock(side_effect=[_u("nope"), _u(_VALID)])
        with patch.object(main.codex_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/codex/chat/completions", json=_body(model="gpt-5.6-sol"))
        assert resp.status_code == 200
        assert all("system_prompt" not in c.kwargs for c in mock.await_args_list)
        assert "CRITICAL" in mock.await_args_list[1].args[0]

    def test_streaming_structured_uses_lean_retry(self, client: TestClient) -> None:
        mock = AsyncMock(side_effect=[_u(RECORDED_AGENT_REFUSAL), _u(_VALID)])
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json=_body(stream=True))
        assert resp.status_code == 200
        assert "Ich hielt mein Wort" in resp.text and "[DONE]" in resp.text
        assert '"refusal"' not in resp.text
        assert mock.await_args_list[1].kwargs["system_prompt"] == _SYSTEM


class TestLeanChatSwitch:
    def test_default_plain_chat_runs_lean(self, client: TestClient) -> None:
        mock = AsyncMock(return_value=_u("Hallo"))
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json=_body(response_format=None))
        assert resp.json()["choices"][0]["message"]["content"] == "Hallo"
        assert mock.await_args.kwargs["system_prompt"] == _SYSTEM

    def test_switch_off_plain_chat_is_agent_mode(self, client: TestClient, monkeypatch) -> None:
        monkeypatch.setenv("CLAUDE_LEAN_CHAT", "0")
        mock = AsyncMock(return_value=_u("Hallo"))
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            client.post("/v1/claude/chat/completions", json=_body(response_format=None))
        assert mock.await_args.kwargs == {}

    def test_default_structured_first_attempt_runs_lean(self, client: TestClient) -> None:
        mock = AsyncMock(side_effect=[_u("nope"), _u(_VALID)])
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json=_body())
        assert resp.status_code == 200
        first, second = mock.await_args_list
        assert first.kwargs["system_prompt"] == _SYSTEM
        assert second.kwargs["system_prompt"] == _SYSTEM
        assert second.args[0].endswith("start with '{' and end with '}'.")

    def test_specific_tool_calls_stay_in_print_mode(self, client: TestClient) -> None:
        tool = {"type": "function", "function": {"name": "submit_review", "parameters": {
            "type": "object", "properties": {"ok": {"type": "boolean"}}}}}
        mock = AsyncMock(return_value=_u('{"ok": true}'))
        body = _body(response_format=None, tools=[tool],
                     tool_choice={"type": "function", "function": {"name": "submit_review"}})
        with patch.object(main.claude_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/claude/chat/completions", json=body)
        assert resp.json()["choices"][0]["finish_reason"] == "tool_calls"
        assert mock.await_args.kwargs == {}

    def test_codex_is_never_lean(self, client: TestClient) -> None:
        mock = AsyncMock(return_value=_u("Hallo"))
        with patch.object(main.codex_adapter, "complete_with_usage", mock):
            client.post("/v1/codex/chat/completions", json=_body(model="gpt-5.6-sol", response_format=None))
        assert mock.await_args.kwargs == {}


# ---------------------------------------------------------------------------
# Adapter argv
# ---------------------------------------------------------------------------

def _proc(result: str) -> MagicMock:
    proc = MagicMock()
    proc.returncode = 0
    proc.communicate = AsyncMock(return_value=(json.dumps({
        "result": result, "is_error": False,
        "usage": {"input_tokens": 3, "cache_read_input_tokens": 2000, "output_tokens": 50},
    }).encode(), b""))
    return proc


class TestLeanAdapter:
    @pytest.fixture(autouse=True)
    def _sem(self):
        claude_adapter._semaphore = asyncio.Semaphore(claude_adapter.MAX_CONCURRENT)

    async def test_lean_argv_and_system_prompt_file(self) -> None:
        seen: dict = {}
        proc = _proc(_VALID)

        async def fake_exec(*args, **kwargs):
            seen["args"] = list(args)
            seen["kwargs"] = kwargs
            sp = Path(args[args.index("--system-prompt-file") + 1])
            seen["sp_text"] = sp.read_text(encoding="utf-8")
            seen["sp_parent_is_cwd"] = str(sp.parent) == kwargs.get("cwd")
            return proc

        with patch("asyncio.create_subprocess_exec", fake_exec):
            text, usage = await claude_adapter.complete_with_usage(
                "Frage", "claude-haiku-latest", None, system_prompt=_SYSTEM, reasoning_effort="low")

        assert text == _VALID and usage.input_tokens == 2003 and usage.output_tokens == 50
        args = seen["args"]
        sp_path = args[args.index("--system-prompt-file") + 1]
        assert args == [
            "claude", "-p", "--output-format", "json",
            "--system-prompt-file", sp_path,
            "--tools", "",
            "--disable-slash-commands", "--strict-mcp-config", "--no-session-persistence",
            "--model", "haiku", "--effort", "low",
        ]
        assert "--allowedTools" not in args and "--system-prompt" not in args
        assert seen["sp_text"] == _SYSTEM and seen["sp_parent_is_cwd"]
        assert proc.communicate.call_args.kwargs["input"] == "Frage".encode("utf-8")
        assert not Path(sp_path).exists(), "the per-call directory is removed afterwards"

    async def test_lean_with_image_references_may_fetch_but_not_search(self) -> None:
        seen: list = []

        async def fake_exec(*args, **kwargs):
            seen.extend(args)
            return _proc("ok")

        with patch("asyncio.create_subprocess_exec", fake_exec):
            await claude_adapter.complete_with_usage(
                "x", None, None, system_prompt="s", image_refs=True)
        assert seen[seen.index("--tools") + 1] == "WebFetch"
        assert seen[seen.index("--allowedTools") + 1] == "WebFetch"
        assert "WebSearch" not in seen
        assert "--model" not in seen

    async def test_agent_mode_argv_has_no_tools_and_no_transcript(self) -> None:
        seen: dict = {}

        async def fake_exec(*args, **kwargs):
            seen["args"] = list(args)
            seen["kwargs"] = kwargs
            return _proc("ok")

        with patch("asyncio.create_subprocess_exec", fake_exec):
            await claude_adapter.complete_with_usage("x", "claude-haiku-latest", None)
        assert seen["args"] == ["claude", "-p", "--output-format", "json",
                                "--no-session-persistence", "--tools", "", "--model", "haiku"]
        assert "cwd" not in seen["kwargs"]

    async def test_lean_is_error_still_raises(self) -> None:
        proc = MagicMock()
        proc.returncode = 0
        proc.communicate = AsyncMock(return_value=(json.dumps({
            "is_error": True, "api_error_status": 429, "result": "usage limit reached"}).encode(), b""))
        with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=proc)):
            with pytest.raises(RuntimeError, match="usage limit"):
                await claude_adapter.complete_with_usage("x", None, None, system_prompt="s")
