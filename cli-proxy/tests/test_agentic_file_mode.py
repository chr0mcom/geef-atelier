"""Tests for agentic tool use via decision-file authoring (claude backend).

The file approach has claude write its next tool call (or a final answer) into decision.json
instead of acting/hallucinating in print mode — sidestepping claude-code's injection refusal of
the inline tool-call protocol. These tests cover the parser, the instruction builder, and the
HTTP flow (mocking the adapter so no real CLI / workspace is needed)."""
from __future__ import annotations

import json
import os
import sys
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import main  # noqa: E402
from tool_use_parser import build_decision_file_instruction, parse_decision  # noqa: E402
from usage import UsageParts  # noqa: E402

TOOLS = [
    {
        "type": "function",
        "function": {
            "name": "terminal",
            "description": "Run a shell command on the host.",
            "parameters": {
                "type": "object",
                "properties": {"command": {"type": "string"}},
                "required": ["command"],
            },
        },
    }
]


def _body(messages, tool_choice="auto"):
    return {
        "model": "claude-opus-4-8",
        "messages": messages,
        "tools": TOOLS,
        "tool_choice": tool_choice,
    }


def _u(text: str):
    return text, UsageParts(input_tokens=10, output_tokens=5)


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


# --- parse_decision -------------------------------------------------------

def test_parse_decision_single_tool_call():
    kind, payload = parse_decision('{"tool_call": {"name": "terminal", "arguments": {"command": "whoami"}}}')
    assert kind == "tool_calls"
    assert payload[0][0] == "terminal"
    assert json.loads(payload[0][1])["command"] == "whoami"


def test_parse_decision_parallel_tool_calls():
    kind, payload = parse_decision(
        '{"tool_calls": [{"name": "terminal", "arguments": {"command": "a"}},'
        ' {"name": "terminal", "arguments": {"command": "b"}}]}'
    )
    assert kind == "tool_calls"
    assert [json.loads(a)["command"] for _, a in payload] == ["a", "b"]


def test_parse_decision_final():
    kind, payload = parse_decision('{"final": "The user is developer."}')
    assert kind == "final"
    assert payload == "The user is developer."


def test_parse_decision_markdown_fenced():
    kind, payload = parse_decision('```json\n{"tool_call": {"name": "terminal", "arguments": {}}}\n```')
    assert kind == "tool_calls"
    assert payload[0][0] == "terminal"


def test_parse_decision_garbage_is_none():
    kind, payload = parse_decision("I ran whoami and it said root.")
    assert kind == "none"
    assert payload is None


def test_parse_decision_empty_is_none():
    assert parse_decision("") == ("none", None)


# --- build_decision_file_instruction --------------------------------------

def test_instruction_contains_tools_and_rules():
    instr = build_decision_file_instruction("[USER]\nhi", TOOLS, "auto")
    assert "terminal" in instr
    assert "decision.json" in instr
    assert "NEVER invent" in instr


def test_instruction_required_mode_clause():
    instr = build_decision_file_instruction("[USER]\nhi", TOOLS, "required")
    assert "MUST request a tool" in instr


def test_instruction_specific_mode_clause():
    instr = build_decision_file_instruction("[USER]\nhi", TOOLS, "specific", "terminal")
    assert 'MUST request the tool named "terminal"' in instr


# --- HTTP flow (adapter mocked) -------------------------------------------

def test_http_returns_tool_calls_from_decision(client: TestClient):
    decision = '{"tool_call": {"name": "terminal", "arguments": {"command": "whoami"}}}'
    with patch("main.claude_adapter.complete_agentic_file", AsyncMock(return_value=_u(decision))):
        resp = client.post("/v1/claude/chat/completions",
                           json=_body([{"role": "user", "content": "Run whoami."}]))
    assert resp.status_code == 200
    data = resp.json()
    assert data["choices"][0]["finish_reason"] == "tool_calls"
    tc = data["choices"][0]["message"]["tool_calls"][0]
    assert tc["function"]["name"] == "terminal"
    assert json.loads(tc["function"]["arguments"])["command"] == "whoami"
    assert data["usage"]["prompt_tokens"] == 10


def test_http_returns_final_answer(client: TestClient):
    decision = '{"final": "You are developer."}'
    with patch("main.claude_adapter.complete_agentic_file", AsyncMock(return_value=_u(decision))):
        resp = client.post("/v1/claude/chat/completions", json=_body([
            {"role": "user", "content": "Who am I?"},
            {"role": "assistant", "tool_calls": [
                {"id": "c1", "type": "function",
                 "function": {"name": "terminal", "arguments": '{"command": "whoami"}'}}]},
            {"role": "tool", "name": "terminal", "tool_call_id": "c1", "content": "developer"},
        ]))
    assert resp.status_code == 200
    data = resp.json()
    assert data["choices"][0]["finish_reason"] == "stop"
    assert "developer" in data["choices"][0]["message"]["content"]


def test_http_retries_once_on_garbage_then_succeeds(client: TestClient):
    decision = '{"tool_call": {"name": "terminal", "arguments": {"command": "whoami"}}}'
    mock = AsyncMock(side_effect=[_u("I ran it, result: root."), _u(decision)])
    with patch("main.claude_adapter.complete_agentic_file", mock):
        resp = client.post("/v1/claude/chat/completions",
                           json=_body([{"role": "user", "content": "Run whoami."}]))
    assert resp.status_code == 200
    assert resp.json()["choices"][0]["finish_reason"] == "tool_calls"
    assert mock.call_count == 2


def test_http_required_mode_retries_when_final(client: TestClient):
    # required mode must yield a tool call; a 'final' answer triggers the single retry.
    mock = AsyncMock(side_effect=[
        _u('{"final": "no tool needed"}'),
        _u('{"tool_call": {"name": "terminal", "arguments": {"command": "id"}}}'),
    ])
    with patch("main.claude_adapter.complete_agentic_file", mock):
        resp = client.post("/v1/claude/chat/completions",
                           json=_body([{"role": "user", "content": "Run id."}], tool_choice="required"))
    assert resp.status_code == 200
    assert resp.json()["choices"][0]["finish_reason"] == "tool_calls"
    assert mock.call_count == 2


def test_http_garbage_twice_falls_back_to_text(client: TestClient):
    mock = AsyncMock(return_value=_u("just some prose, no json"))
    with patch("main.claude_adapter.complete_agentic_file", mock):
        resp = client.post("/v1/claude/chat/completions",
                           json=_body([{"role": "user", "content": "hi"}]))
    assert resp.status_code == 200
    assert resp.json()["choices"][0]["finish_reason"] == "stop"
    assert mock.call_count == 2


def test_specific_tool_choice_does_not_use_file_path(client: TestClient):
    # specific tool_choice stays on the print-mode path (complete_with_usage), not the file path.
    file_mock = AsyncMock(return_value=_u('{"final": "x"}'))
    print_mock = AsyncMock(return_value=_u('{"command": "ls"}'))
    with patch("main.claude_adapter.complete_agentic_file", file_mock), \
         patch("main.claude_adapter.complete_with_usage", print_mock):
        client.post("/v1/claude/chat/completions", json=_body(
            [{"role": "user", "content": "list"}],
            tool_choice={"type": "function", "function": {"name": "terminal"}}))
    file_mock.assert_not_called()
    print_mock.assert_called()


def test_streaming_emits_tool_calls(client: TestClient):
    decision = '{"tool_call": {"name": "terminal", "arguments": {"command": "whoami"}}}'
    body = {**_body([{"role": "user", "content": "Run whoami."}]), "stream": True}
    with patch("main.claude_adapter.complete_agentic_file", AsyncMock(return_value=_u(decision))):
        resp = client.post("/v1/claude/chat/completions", json=body)
    assert resp.status_code == 200
    assert "terminal" in resp.text
    assert "tool_calls" in resp.text
