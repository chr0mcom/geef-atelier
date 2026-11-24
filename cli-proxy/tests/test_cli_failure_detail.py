"""Error details of failed CLI runs: cause fields instead of the first 200 characters of stdout."""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import claude_adapter  # noqa: E402
import codex_adapter  # noqa: E402
import failover  # noqa: E402
from cli_errors import (  # noqa: E402
    RAW_CHARS,
    RESULT_CHARS,
    claude_failure_detail,
    codex_failure_detail,
    is_empty_document_result,
)

_FAKE_KEY = "sk-ant-api03-AbCdEfGhIjKlMnOp1234"


def _claude_result(**fields) -> str:
    """claude --output-format json result object with the long usage block FIRST, as CLI 2.1.219+
    writes it (the shape that made the old stdout[:200] lose the cause)."""
    data = {
        "duration_api_ms": 46216,
        "stop_reason": "stop_sequence",
        "session_id": "22947b5d-589f-4ee6-abba-553cf16e1422",
        "total_cost_usd": 1.19,
        "usage": {"input_tokens": 24, "cache_creation_input_tokens": 128826,
                  "cache_read_input_tokens": 0, "output_tokens": 0,
                  "server_tool_use": {"web_search_requests": 0, "web_fetch_requests": 0},
                  "service_tier": "standard"},
        "type": "result",
    }
    data.update(fields)
    return json.dumps(data)


class TestClaudeFailureDetail:
    def test_cause_fields_survive_the_long_usage_block(self) -> None:
        stdout = _claude_result(subtype="error_during_execution", is_error=True,
                                api_error_status=429, result="You've hit your session limit")
        assert len(stdout) > 200, "precondition: the old cut would have lost the cause"
        detail = claude_failure_detail(stdout, "")
        assert "You've hit your session limit" in detail
        assert "subtype=error_during_execution" in detail
        assert "is_error=true" in detail
        assert "api_error_status=429" in detail
        assert "cache_creation_input_tokens" not in detail, "usage numbers are not the cause"

    def test_result_is_clipped_to_300_characters(self) -> None:
        stdout = _claude_result(subtype="success", is_error=True, result="x" * 5000)
        detail = claude_failure_detail(stdout, "")
        assert "x" * RESULT_CHARS in detail
        assert "x" * (RESULT_CHARS + 1) not in detail

    def test_result_is_redacted(self) -> None:
        stdout = _claude_result(is_error=True, result=f"upstream rejected key {_FAKE_KEY}")
        detail = claude_failure_detail(stdout, "")
        assert _FAKE_KEY not in detail
        assert "[redacted]" in detail

    def test_missing_fields_are_left_out(self) -> None:
        detail = claude_failure_detail(_claude_result(result="boom"), "")
        assert detail == "boom"

    def test_stderr_is_kept_next_to_the_json_cause(self) -> None:
        detail = claude_failure_detail(_claude_result(is_error=True, result="boom"), "warn: x\n")
        assert detail == "boom (is_error=true); stderr: warn: x"

    def test_non_json_stdout_falls_back_to_first_200_characters(self) -> None:
        stdout = "plain text " + "y" * 1000
        detail = claude_failure_detail(stdout, "")
        assert detail == stdout[:RAW_CHARS]

    def test_non_json_stdout_is_redacted(self) -> None:
        detail = claude_failure_detail(f"oops Bearer abc123DEF456ghi789 {_FAKE_KEY}", "")
        assert "abc123DEF456ghi789" not in detail and _FAKE_KEY not in detail

    def test_json_without_cause_fields_falls_back_to_the_old_cut(self) -> None:
        stdout = json.dumps({"usage": {"input_tokens": 1}, "padding": "z" * 400})
        assert claude_failure_detail(stdout, "") == stdout[:RAW_CHARS]

    def test_stderr_only(self) -> None:
        assert claude_failure_detail("", f"fatal: token {_FAKE_KEY}") == "fatal: token [redacted]"


class TestCodexFailureDetail:
    def test_stderr_stays_the_primary_text(self) -> None:
        assert codex_failure_detail("", "Error: usage limit reached") == "Error: usage limit reached"

    def test_error_event_is_added(self) -> None:
        stdout = "\n".join([
            json.dumps({"type": "thread.started", "thread_id": "x"}),
            json.dumps({"type": "turn.failed", "error": {"message": f"stream error {_FAKE_KEY}"}}),
        ])
        detail = codex_failure_detail(stdout, "")
        assert detail.startswith("error event: stream error ")
        assert _FAKE_KEY not in detail

    def test_plain_error_event(self) -> None:
        stdout = json.dumps({"type": "error", "message": "You've hit your usage limit"})
        assert "usage limit" in codex_failure_detail(stdout, "boom")
        assert codex_failure_detail(stdout, "boom").startswith("boom; error event: ")

    def test_no_stderr_no_event_falls_back_to_first_200_characters(self) -> None:
        stdout = "q" * 500
        assert codex_failure_detail(stdout, "") == "q" * RAW_CHARS


class TestEmptyResultPredicate:
    @pytest.mark.parametrize("result,document,expected", [
        ("", "", True),
        ("  \n", "  \n", True),
        ("text", "", False),
        ("", "previous text", False),
        ("same", "same", False),
        ("\n", "", False),
    ])
    def test_empty_and_unchanged(self, result, document, expected) -> None:
        assert is_empty_document_result(result, document) is expected


def _proc(stdout: str, stderr: str = "", returncode: int = 1) -> MagicMock:
    proc = MagicMock()
    proc.returncode = returncode
    proc.communicate = AsyncMock(return_value=(stdout.encode(), stderr.encode()))
    return proc


class TestAdapters:
    @pytest.mark.asyncio
    async def test_claude_document_mode_reports_the_cause(self, tmp_path: Path) -> None:
        (tmp_path / "draft.md").write_text("", encoding="utf-8")
        stdout = _claude_result(subtype="error_during_execution", is_error=True,
                                api_error_status=None, result="Prompt is too long")
        with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=_proc(stdout))):
            with pytest.raises(RuntimeError) as exc:
                await claude_adapter._run_claude_document(
                    "SYSTEM-PROMPT-MARKER", "USER-INSTRUCTION-MARKER", "", tmp_path, None, None,
                    "CONTEXT-MARKER " * 10)
        message = str(exc.value)
        assert message.startswith("claude CLI (document mode) exited with code 1: ")
        assert "Prompt is too long" in message and "subtype=error_during_execution" in message
        assert "api_error_status" not in message, "a null field is left out"
        for marker in ("SYSTEM-PROMPT-MARKER", "USER-INSTRUCTION-MARKER", "CONTEXT-MARKER"):
            assert marker not in message, "prompt or context text must never reach the error"
        assert failover.classify(exc.value) == "transport", "still a failover-worthy fault"

    @pytest.mark.asyncio
    async def test_claude_document_mode_limit_is_classified_as_limit(self, tmp_path: Path) -> None:
        (tmp_path / "draft.md").write_text("", encoding="utf-8")
        stdout = _claude_result(is_error=True, result="You've hit your session limit · resets 9pm")
        with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=_proc(stdout))):
            with pytest.raises(RuntimeError) as exc:
                await claude_adapter._run_claude_document("s", "u", "", tmp_path, None, None)
        assert failover.classify(exc.value) == "limit"

    @pytest.mark.asyncio
    async def test_claude_print_mode_error_keeps_its_prefix(self) -> None:
        stdout = json.dumps({"is_error": True, "api_error_status": 401,
                             "result": "Failed to authenticate: OAuth session expired"})
        with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=_proc(stdout))):
            with pytest.raises(RuntimeError) as exc:
                await claude_adapter._run_claude("hi", None, None)
        message = str(exc.value)
        assert message.startswith("claude CLI error: Failed to authenticate")
        assert "api_error_status=401" in message
        assert failover.classify(exc.value) == "auth"

    @pytest.mark.asyncio
    async def test_codex_document_mode_reports_the_error_event(self, tmp_path: Path) -> None:
        (tmp_path / "draft.md").write_text("", encoding="utf-8")
        stdout = json.dumps({"type": "turn.failed", "error": {"message": "quota exceeded"}})
        with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=_proc(stdout))):
            with pytest.raises(RuntimeError) as exc:
                await codex_adapter._run_codex_document("s", "u", "", tmp_path, "gpt-6.2-sol", None)
        assert str(exc.value) == ("codex CLI (document mode) exited with code 1: "
                                  "error event: quota exceeded")
        assert failover.classify(exc.value) == "limit"

    @pytest.mark.asyncio
    async def test_codex_print_mode_stderr_is_redacted(self) -> None:
        proc = _proc("", stderr=f"Error: invalid key {_FAKE_KEY}")
        with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=proc)):
            with pytest.raises(RuntimeError) as exc:
                await codex_adapter._run_codex("hi", "gpt-6.2-sol", None)
        assert _FAKE_KEY not in str(exc.value)
        assert str(exc.value).startswith("codex CLI exited with code 1: Error: invalid key ")
