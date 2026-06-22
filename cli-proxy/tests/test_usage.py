"""Tests for real token-usage parsing from the CLI outputs (WP1)."""
from __future__ import annotations

import os
import sys

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import claude_adapter  # noqa: E402
import codex_adapter  # noqa: E402


class TestClaudeUsage:
    def test_maps_usage_and_result(self) -> None:
        data = {
            "result": "hello",
            "is_error": False,
            "total_cost_usd": 0.0123,
            "usage": {
                "input_tokens": 100,
                "cache_read_input_tokens": 40,
                "cache_creation_input_tokens": 10,
                "output_tokens": 25,
            },
        }
        text, usage = claude_adapter._extract_result_and_usage(data, "")
        assert text == "hello"
        # prompt_tokens counts fresh + cache-read + cache-creation
        assert usage.input_tokens == 150
        assert usage.output_tokens == 25
        assert usage.cached_tokens == 40
        assert usage.cost_usd == 0.0123
        assert usage.total_tokens == 175

    def test_missing_usage_is_zero(self) -> None:
        text, usage = claude_adapter._extract_result_and_usage({"result": "x"}, "")
        assert text == "x"
        assert usage.input_tokens == 0
        assert usage.output_tokens == 0

    def test_non_dict_falls_back_to_raw(self) -> None:
        text, usage = claude_adapter._extract_result_and_usage(None, "raw text")
        assert text == "raw text"
        assert usage.total_tokens == 0


class TestCodexUsage:
    def test_parses_turn_completed_usage(self) -> None:
        stdout = (
            '{"type":"thread.started","thread_id":"t1"}\n'
            '{"type":"turn.started"}\n'
            '{"type":"item.completed","item":{"id":"i0","type":"agent_message","text":"hi"}}\n'
            '{"type":"turn.completed","usage":{"input_tokens":2000,"cached_input_tokens":1800,'
            '"output_tokens":12,"reasoning_output_tokens":8}}\n'
        )
        usage = codex_adapter._parse_codex_usage(stdout)
        assert usage.input_tokens == 2000
        # completion_tokens includes reasoning tokens
        assert usage.output_tokens == 20
        assert usage.cached_tokens == 1800
        assert usage.reasoning_tokens == 8

    def test_last_usage_event_wins(self) -> None:
        stdout = (
            '{"type":"turn.completed","usage":{"input_tokens":1,"output_tokens":1}}\n'
            '{"type":"turn.completed","usage":{"input_tokens":5,"output_tokens":7}}\n'
        )
        usage = codex_adapter._parse_codex_usage(stdout)
        assert usage.input_tokens == 5
        assert usage.output_tokens == 7

    def test_no_usage_is_zero(self) -> None:
        usage = codex_adapter._parse_codex_usage('{"type":"turn.started"}\n')
        assert usage.total_tokens == 0

    def test_recover_text_from_stream(self) -> None:
        stdout = (
            '{"type":"item.completed","item":{"type":"agent_message","text":"final answer"}}\n'
        )
        assert codex_adapter._parse_codex_text(stdout) == "final answer"
