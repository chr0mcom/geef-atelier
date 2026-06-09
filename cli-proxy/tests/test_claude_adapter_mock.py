"""Tests for claude_adapter using a mocked subprocess."""
import json
import sys
import os
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import claude_adapter


@pytest.fixture(autouse=True)
def reset_semaphore():
    """Ensure a fresh semaphore for each test."""
    import asyncio
    claude_adapter._semaphore = asyncio.Semaphore(claude_adapter.MAX_CONCURRENT)
    yield


def _make_proc(stdout: str, returncode: int = 0, stderr: str = "") -> MagicMock:
    proc = MagicMock()
    proc.returncode = returncode
    proc.communicate = AsyncMock(
        return_value=(stdout.encode(), stderr.encode())
    )
    return proc


@pytest.mark.asyncio
async def test_complete_returns_result_field():
    output = json.dumps({"result": "This is the answer.", "is_error": False})
    proc = _make_proc(output)

    with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=proc)):
        result = await claude_adapter.complete("Hello", "claude-opus-4-5", 1024)

    assert result == "This is the answer."


@pytest.mark.asyncio
async def test_complete_strips_provider_prefix():
    output = json.dumps({"result": "ok"})
    proc = _make_proc(output)

    captured: list[list] = []

    async def fake_exec(*args, **kwargs):
        captured.append(list(args))
        return proc

    with patch("asyncio.create_subprocess_exec", fake_exec):
        await claude_adapter.complete("test", "anthropic/claude-opus-4-5", None)

    args = captured[0]
    # --model flag should use the bare name without "anthropic/" prefix.
    model_index = args.index("--model")
    assert args[model_index + 1] == "claude-opus-4-5"


@pytest.mark.asyncio
async def test_complete_allowlists_only_web_tools():
    output = json.dumps({"result": "ok"})
    proc = _make_proc(output)

    captured: list[list] = []

    async def fake_exec(*args, **kwargs):
        captured.append(list(args))
        return proc

    with patch("asyncio.create_subprocess_exec", fake_exec):
        await claude_adapter.complete("test", None, None)

    args = captured[0]
    tools_index = args.index("--allowedTools")
    assert args[tools_index + 1] == "WebSearch,WebFetch"
    # No full bypass / no shell or edit tools leaked in.
    assert "--dangerously-skip-permissions" not in args
    assert "Bash" not in args[tools_index + 1]


@pytest.mark.asyncio
async def test_complete_passes_prompt_via_stdin_not_argv():
    """Large reviewer/advisor prompts must go through stdin, never argv.

    A single execve argument is capped at MAX_ARG_STRLEN (128 KB on Linux); reviewer
    prompts embed the full draft (often >128 KB), so an argv prompt would fail the spawn
    with E2BIG ("Argument list too long") and the proxy would return HTTP 500.
    """
    output = json.dumps({"result": "ok"})
    proc = _make_proc(output)
    captured_args: list[list] = []
    captured_kwargs: list[dict] = []

    async def fake_exec(*args, **kwargs):
        captured_args.append(list(args))
        captured_kwargs.append(kwargs)
        return proc

    big_prompt = "Ω" * 200_000  # ~400 KB UTF-8 — well past MAX_ARG_STRLEN

    with patch("asyncio.create_subprocess_exec", fake_exec):
        await claude_adapter.complete(big_prompt, None, None)

    args = captured_args[0]
    assert big_prompt not in args, "prompt must not be passed as an argv element"
    assert captured_kwargs[0].get("stdin") is not None, "stdin pipe must be opened"
    proc.communicate.assert_awaited_once()
    assert proc.communicate.call_args.kwargs["input"] == big_prompt.encode("utf-8")


@pytest.mark.asyncio
async def test_complete_raises_on_nonzero_exit():
    proc = _make_proc("", returncode=1, stderr="auth error")

    with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=proc)):
        with pytest.raises(RuntimeError, match="auth error"):
            await claude_adapter.complete("Hello", None, None)


@pytest.mark.asyncio
async def test_complete_falls_back_to_raw_when_no_result_field():
    raw_text = "Just a plain string response."
    proc = _make_proc(raw_text)

    with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=proc)):
        result = await claude_adapter.complete("Hello", None, None)

    assert result == raw_text
