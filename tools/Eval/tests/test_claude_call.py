from pathlib import Path

import pytest

from conftest import FakeRunner, claude_json
from geef_eval.claude_call import ClaudeCallError, call_claude, extract_json
from geef_eval.cli_runner import CliResult, LimitError


def test_extract_json_tolerates_fences_and_prose():
    assert extract_json('Hier:\n```json\n{"a": 1}\n```') == {"a": 1}
    with pytest.raises(ValueError):
        extract_json("kein json")


def test_prompt_goes_via_stdin_into_an_empty_directory_with_tool_less_claude():
    runner = FakeRunner(["Antworttext"])

    reply = call_claude("PROMPT", model="opus", system_prompt="SYS", timeout_s=5, runner=runner)

    call = runner.calls[0]
    assert reply.text == "Antworttext"
    assert call["prompt"] == "PROMPT"
    assert call["cwd_empty"] is True
    assert not Path(call["cwd"]).exists()
    cmd = call["cmd"]
    assert cmd[:2] == ["claude", "-p"]
    assert cmd[cmd.index("--model") + 1] == "opus"
    assert cmd[cmd.index("--tools") + 1] == ""
    assert "--no-session-persistence" in cmd
    assert "codex" not in cmd


def test_invalid_json_is_retried_once():
    runner = FakeRunner(["kein json", '{"ok": true}'])
    log = []

    reply = call_claude("p", model="sonnet", system_prompt="s", timeout_s=5, runner=runner, json_out=True, log=log.append)

    assert reply.obj == {"ok": True}
    assert reply.attempts == 2
    assert [r["ok"] for r in log] == [False, True]


def test_validation_failure_counts_as_invalid():
    def validate(obj):
        raise ValueError("schema")

    with pytest.raises(ClaudeCallError, match="schema"):
        call_claude("p", model="sonnet", system_prompt="s", timeout_s=5,
                    runner=FakeRunner(['{"a":1}', '{"a":2}']), json_out=True, validate=validate)


def test_usage_limit_stops_immediately():
    runner = FakeRunner([claude_json("You've hit your usage limit · resets 5pm", is_error=True)])

    with pytest.raises(LimitError):
        call_claude("p", model="opus", system_prompt="s", timeout_s=5, runner=runner)
    assert len(runner.calls) == 1


def test_timeout_is_reported_after_all_attempts():
    timed_out = CliResult("", "", -9, True, 5.0)

    with pytest.raises(ClaudeCallError, match="Zeitlimit"):
        call_claude("p", model="opus", system_prompt="s", timeout_s=5, runner=FakeRunner([timed_out, timed_out]))
