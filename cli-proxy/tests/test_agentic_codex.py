"""Agentic decision-file mode on the codex backend + tool-preserving failover.

Incident 2026-07-24 (Hermes case-writer, cards t_3db36481 / t_87366a1c): with claude at its
session limit the proxy failed over to codex, but the agentic path was claude-only — the
request degraded to the buffered branch, whose tool-protocol prompt the codex CLI answered
with "these tools are not connected in this session" as plain text. Hermes saw zero tool
calls, crashed with protocol violations, and gave up. Since then the decision-file path
covers BOTH backends: the failover must hand over the tool contract, not just the transport.
"""
from __future__ import annotations

import json
import os
import sys
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import failover  # noqa: E402
import main  # noqa: E402
from usage import UsageParts  # noqa: E402

TOOLS = [
    {
        "type": "function",
        "function": {
            "name": "kanban_show",
            "description": "Show a kanban card.",
            "parameters": {
                "type": "object",
                "properties": {"task_id": {"type": "string"}},
                "required": ["task_id"],
            },
        },
    }
]

DECISION = '{"tool_call": {"name": "kanban_show", "arguments": {"task_id": "t_3db36481"}}}'
LIMIT_ERROR = RuntimeError(
    "claude CLI (agentic file mode) exited with code 1: "
    "You've hit your session limit · resets 9pm (UTC)"
)


def _body(model="gpt-5.6-sol", stream=False, tool_choice="auto"):
    body = {
        "model": model,
        "messages": [{"role": "user", "content": "work kanban task t_3db36481"}],
        "tools": TOOLS,
        "tool_choice": tool_choice,
    }
    if stream:
        body["stream"] = True
    return body


def _u(text: str):
    return text, UsageParts(input_tokens=20, output_tokens=7)


@pytest.fixture(autouse=True)
def _fresh_breaker():
    failover.reset()
    yield
    failover.reset()


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


# --- codex backend uses the decision-file path directly ---------------------

def test_codex_endpoint_returns_tool_calls_from_decision(client: TestClient):
    codex_mock = AsyncMock(return_value=_u(DECISION))
    print_mock = AsyncMock(return_value=_u("must not be used"))
    with patch("main.codex_adapter.complete_agentic_file", codex_mock), \
         patch("main.codex_adapter.complete_with_usage", print_mock):
        resp = client.post("/v1/codex/chat/completions", json=_body())
    assert resp.status_code == 200
    data = resp.json()
    assert data["choices"][0]["finish_reason"] == "tool_calls"
    tc = data["choices"][0]["message"]["tool_calls"][0]
    assert tc["function"]["name"] == "kanban_show"
    assert json.loads(tc["function"]["arguments"])["task_id"] == "t_3db36481"
    codex_mock.assert_called_once()
    print_mock.assert_not_called()


def test_codex_endpoint_final_answer(client: TestClient):
    with patch("main.codex_adapter.complete_agentic_file",
               AsyncMock(return_value=_u('{"final": "Karte ist erledigt."}'))):
        resp = client.post("/v1/codex/chat/completions", json=_body())
    assert resp.status_code == 200
    assert resp.json()["choices"][0]["finish_reason"] == "stop"
    assert "erledigt" in resp.json()["choices"][0]["message"]["content"]


def test_codex_agentic_retries_once_on_garbage(client: TestClient):
    mock = AsyncMock(side_effect=[_u("these tools are not connected"), _u(DECISION)])
    with patch("main.codex_adapter.complete_agentic_file", mock):
        resp = client.post("/v1/codex/chat/completions", json=_body())
    assert resp.status_code == 200
    assert resp.json()["choices"][0]["finish_reason"] == "tool_calls"
    assert mock.call_count == 2


def test_codex_streaming_emits_tool_calls(client: TestClient):
    with patch("main.codex_adapter.complete_agentic_file", AsyncMock(return_value=_u(DECISION))):
        resp = client.post("/v1/codex/chat/completions", json=_body(stream=True))
    assert resp.status_code == 200
    assert "kanban_show" in resp.text
    assert "tool_calls" in resp.text


def test_codex_specific_tool_choice_stays_on_print_path(client: TestClient):
    file_mock = AsyncMock(return_value=_u('{"final": "x"}'))
    print_mock = AsyncMock(return_value=_u('{"task_id": "t_1"}'))
    with patch("main.codex_adapter.complete_agentic_file", file_mock), \
         patch("main.codex_adapter.complete_with_usage", print_mock):
        client.post("/v1/codex/chat/completions", json=_body(
            tool_choice={"type": "function", "function": {"name": "kanban_show"}}))
    file_mock.assert_not_called()
    print_mock.assert_called()


# --- failover hands over the tool contract (the incident scenario) ----------

def test_failover_serves_tool_calls_via_codex_decision_file(client: TestClient):
    """The exact incident shape: claude at its session limit, an agentic Hermes request —
    codex must answer with REAL tool_calls via its own decision-file run."""
    claude_mock = AsyncMock(side_effect=LIMIT_ERROR)
    codex_mock = AsyncMock(return_value=_u(DECISION))
    with patch("main.claude_adapter.complete_agentic_file", claude_mock), \
         patch("main.codex_adapter.complete_agentic_file", codex_mock):
        resp = client.post("/v1/claude/chat/completions",
                           json=_body(model="claude-sonnet-latest"))
    assert resp.status_code == 200
    data = resp.json()
    assert data["choices"][0]["finish_reason"] == "tool_calls"
    assert data["choices"][0]["message"]["tool_calls"][0]["function"]["name"] == "kanban_show"
    assert resp.headers["x-cli-proxy-provider"] == "codex"
    assert resp.headers["x-cli-proxy-failover"] == "claude->codex"
    # The model name must not travel to the codex CLI (empty -> its own default).
    assert not codex_mock.await_args.args[1]
    # The response still echoes the requested model.
    assert data["model"] == "claude-sonnet-latest"


def test_streaming_failover_serves_tool_calls_via_codex(client: TestClient):
    """Hermes always streams — the SSE path must hand the decision-file contract to codex
    too (before 2026-07-24 it degraded to plain-text deltas: zero tool calls, gave_up)."""
    claude_mock = AsyncMock(side_effect=LIMIT_ERROR)
    codex_mock = AsyncMock(return_value=_u(DECISION))
    with patch("main.claude_adapter.complete_agentic_file", claude_mock), \
         patch("main.codex_adapter.complete_agentic_file", codex_mock):
        resp = client.post("/v1/claude/chat/completions",
                           json=_body(model="claude-sonnet-latest", stream=True))
    assert resp.status_code == 200
    assert "kanban_show" in resp.text
    assert "tool_calls" in resp.text
    codex_mock.assert_called_once()


def test_failover_agentic_failure_feeds_the_breaker(client: TestClient):
    with patch("main.claude_adapter.complete_agentic_file", AsyncMock(side_effect=LIMIT_ERROR)), \
         patch("main.codex_adapter.complete_agentic_file", AsyncMock(return_value=_u(DECISION))):
        client.post("/v1/claude/chat/completions", json=_body(model="claude-sonnet-latest"))
    snap = failover.snapshot()
    assert snap["backends"]["claude"]["consecutive_failures"] == 1
    assert snap["backends"]["claude"]["last_reason"] == "limit"


def test_both_backends_down_returns_502(client: TestClient):
    codex_error = RuntimeError("codex CLI (agentic file mode) exited with code 1: stream error")
    with patch("main.claude_adapter.complete_agentic_file", AsyncMock(side_effect=LIMIT_ERROR)), \
         patch("main.codex_adapter.complete_agentic_file", AsyncMock(side_effect=codex_error)):
        resp = client.post("/v1/claude/chat/completions", json=_body(model="claude-sonnet-latest"))
    assert resp.status_code == 502


# --- SSE honours the specific-tool and structured contracts (review findings) -

def test_streaming_specific_tool_choice_emits_tool_calls(client: TestClient):
    """tool_choice=specific over SSE must yield a tool_calls frame, not text deltas
    (review finding 2026-07-24: the buffered branch only ever yielded deltas)."""
    body = _body(model="claude-sonnet-latest", stream=True,
                 tool_choice={"type": "function", "function": {"name": "kanban_show"}})
    with patch("main.claude_adapter.complete_with_usage",
               AsyncMock(return_value=_u('{"task_id": "t_42"}'))):
        resp = client.post("/v1/claude/chat/completions", json=body)
    assert resp.status_code == 200
    assert "tool_calls" in resp.text
    assert '\\"task_id\\": \\"t_42\\"' in resp.text or '"task_id": "t_42"' in resp.text


def test_streaming_specific_tool_retries_once_then_falls_back_to_text(client: TestClient):
    body = _body(model="claude-sonnet-latest", stream=True,
                 tool_choice={"type": "function", "function": {"name": "kanban_show"}})
    mock = AsyncMock(side_effect=[_u("no json at all"), _u("still no json")])
    with patch("main.claude_adapter.complete_with_usage", mock):
        resp = client.post("/v1/claude/chat/completions", json=body)
    assert resp.status_code == 200
    assert mock.call_count == 2
    assert "still no json" in resp.text


def test_streaming_structured_retries_once_on_invalid_json(client: TestClient):
    body = {
        "model": "claude-sonnet-latest",
        "messages": [{"role": "user", "content": "gib json"}],
        "response_format": {"type": "json_object"},
        "stream": True,
    }
    mock = AsyncMock(side_effect=[_u("not json"), _u('{"ok": true}')])
    with patch("main.claude_adapter.complete_with_usage", mock):
        resp = client.post("/v1/claude/chat/completions", json=body)
    assert resp.status_code == 200
    assert mock.call_count == 2
    assert '\\"ok\\": true' in resp.text or '"ok": true' in resp.text


def test_decision_retry_usage_counts_both_runs(client: TestClient):
    """Both CLI runs of a retried decision are billed — the usage block must sum them
    (review finding 2026-07-24: the retry overwrote the first run's usage)."""
    first = ("no json here", UsageParts(input_tokens=100, output_tokens=10))
    second = (DECISION, UsageParts(input_tokens=120, output_tokens=15))
    with patch("main.codex_adapter.complete_agentic_file", AsyncMock(side_effect=[first, second])):
        resp = client.post("/v1/codex/chat/completions", json=_body())
    assert resp.status_code == 200
    usage = resp.json()["usage"]
    assert usage["prompt_tokens"] == 220
    assert usage["completion_tokens"] == 25
