"""Per-request reasoning_effort pass-through (BF-WP-116).

The proxy maps `reasoning_effort` onto the CLIs only when the caller sends it:
claude -> `--effort <level>`, codex -> `-c model_reasoning_effort="<level>"`. Without the field
every CLI invocation must stay byte-identical to the default argv, because Geef.Atelier, DMS and
Hermes never send it and must not notice any change.
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
import codex_adapter  # noqa: E402
import failover  # noqa: E402
import main  # noqa: E402
import workspace  # noqa: E402
from usage import UsageParts  # noqa: E402

CLAUDE_MODEL = "claude-haiku-4-5-20251001"
CODEX_MODEL = "gpt-5.6-luna"
SYSTEM = "You are a careful editor."
HEADER = "x-cli-proxy-reasoning-effort"

_AUTH_ERROR = RuntimeError(
    "claude CLI error: Failed to authenticate: OAuth session expired and could not be refreshed")


class _Spawned(Exception):
    """Raised by the fake spawn once the argv has been captured."""


@pytest.fixture(autouse=True)
def _isolate(monkeypatch, tmp_path):
    failover.reset()
    claude_adapter._semaphore = asyncio.Semaphore(claude_adapter.MAX_CONCURRENT)
    codex_adapter._semaphore = asyncio.Semaphore(codex_adapter.MAX_CONCURRENT)
    work_root = tmp_path / "work"
    work_root.mkdir()
    monkeypatch.setattr(workspace, "WORKSPACE_ROOT", str(work_root))
    yield
    failover.reset()


async def _capture_argv(call) -> list[str]:
    """Run one adapter call against a fake spawn and return the normalised argv.

    Ephemeral workspace paths and codex's temporary --output-last-message file differ per
    run; they are replaced by placeholders so two runs can be compared byte for byte.
    """
    captured: list[list[str]] = []
    cwds: list[str | None] = []

    async def fake_exec(*args, **kwargs):
        captured.append(list(args))
        cwds.append(kwargs.get("cwd"))
        raise _Spawned()

    with patch("asyncio.create_subprocess_exec", fake_exec):
        try:
            result = call()
            if hasattr(result, "__anext__"):
                async for _ in result:
                    pass
            else:
                await result
        except _Spawned:
            pass

    assert len(captured) == 1, "exactly one CLI spawn expected"
    argv, cwd = captured[0], cwds[0]
    normalised: list[str] = []
    for index, arg in enumerate(argv):
        if index > 0 and argv[index - 1] == "--output-last-message":
            normalised.append("<OUT>")
        elif cwd and arg == cwd:
            normalised.append("<WS>")
        else:
            normalised.append(arg)
    return normalised


def _paths(tmp_path: Path, **effort):
    """Every adapter entry point that sets a model flag today, as (name, factory) pairs."""
    doc_ws = tmp_path / "doc"
    doc_ws.mkdir(exist_ok=True)
    (doc_ws / "draft.md").write_text("draft", encoding="utf-8")
    return [
        ("claude.print", lambda: claude_adapter.complete_with_usage(
            "hello", CLAUDE_MODEL, None, **effort)),
        ("claude.stream", lambda: claude_adapter.stream("hello", CLAUDE_MODEL, None, **effort)),
        ("claude.document", lambda: claude_adapter.complete_document(
            SYSTEM, "tighten it", "draft", doc_ws, CLAUDE_MODEL, None, None, **effort)),
        ("claude.decision-file", lambda: claude_adapter.complete_agentic_file(
            "pick a tool", CLAUDE_MODEL, **effort)),
        ("codex.print", lambda: codex_adapter.complete_with_usage(
            "hello", CODEX_MODEL, None, **effort)),
        ("codex.stream", lambda: codex_adapter.stream("hello", CODEX_MODEL, None, **effort)),
        ("codex.document", lambda: codex_adapter.complete_document(
            SYSTEM, "tighten it", "draft", doc_ws, CODEX_MODEL, None, None, **effort)),
        ("codex.decision-file", lambda: codex_adapter.complete_agentic_file(
            "pick a tool", CODEX_MODEL, **effort)),
    ]


# The argv of every path without reasoning_effort (the trailing instruction positional of the
# document and decision paths is compared separately, since its text is built elsewhere).
_GOLDEN_PREFIX = {
    "claude.print": [
        "claude", "-p", "--output-format", "json", "--no-session-persistence", "--tools", "",
        "--model", CLAUDE_MODEL,
    ],
    "claude.stream": [
        "claude", "-p", "--output-format", "stream-json", "--verbose",
        "--include-partial-messages", "--no-session-persistence", "--tools", "",
        "--model", CLAUDE_MODEL,
    ],
    "claude.document": [
        "claude", "-p", "--output-format", "json", "--allowedTools", "Read,Edit,Write",
        "--permission-mode", "acceptEdits", "--no-session-persistence",
        "--append-system-prompt", SYSTEM,
        "--model", CLAUDE_MODEL,
    ],
    "claude.decision-file": [
        "claude", "-p", "--output-format", "json", "--allowedTools", "Read,Edit,Write",
        "--permission-mode", "acceptEdits", "--no-session-persistence", "--model", CLAUDE_MODEL,
    ],
    "codex.print": [
        "codex", "exec", "--json", "--ephemeral", "--skip-git-repo-check", "-m", CODEX_MODEL,
        "--output-last-message", "<OUT>", "-",
    ],
    "codex.stream": [
        "codex", "exec", "--json", "--ephemeral", "--skip-git-repo-check", "-m", CODEX_MODEL, "-",
    ],
    "codex.document": [
        "codex", "exec", "--json", "--ephemeral", "--skip-git-repo-check",
        "--sandbox", "workspace-write", "-C", "<WS>", "-m", CODEX_MODEL,
    ],
    "codex.decision-file": [
        "codex", "exec", "--json", "--ephemeral", "--skip-git-repo-check",
        "--sandbox", "workspace-write", "-C", "<WS>", "-m", CODEX_MODEL,
    ],
}
_HAS_TRAILING_INSTRUCTION = {
    "claude.document", "claude.decision-file", "codex.document", "codex.decision-file",
}


def _without_instruction(name: str, argv: list[str]) -> list[str]:
    return argv[:-1] if name in _HAS_TRAILING_INSTRUCTION else argv


def _body(**extra):
    return {"model": CLAUDE_MODEL, "messages": [{"role": "user", "content": "hi"}], **extra}


def _ok(text: str = "ok"):
    return AsyncMock(return_value=(text, UsageParts(input_tokens=1, output_tokens=1)))


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


@pytest.mark.asyncio
async def test_absent_reasoning_effort_keeps_argv_identical_on_every_path(tmp_path):
    absent = {name: await _capture_argv(call) for name, call in _paths(tmp_path)}
    explicit_none = {
        name: await _capture_argv(call) for name, call in _paths(tmp_path, reasoning_effort=None)
    }

    assert set(absent) == set(_GOLDEN_PREFIX)
    for name, argv in absent.items():
        assert _without_instruction(name, argv) == _GOLDEN_PREFIX[name], name
        assert explicit_none[name] == argv, name
        assert "--effort" not in argv, name
        assert not any("model_reasoning_effort" in arg for arg in argv), name


def test_absent_reasoning_effort_passes_no_kwarg_through_main(client):
    """Main must not even hand reasoning_effort=None to an adapter when the field is absent."""
    calls: list[tuple[str, AsyncMock]] = []

    def mocked(name, value):
        mock = AsyncMock(return_value=value)
        calls.append((name, mock))
        return mock

    decision = ('{"final": "done"}', UsageParts())
    tool = {"type": "function", "function": {"name": "lookup", "parameters": {}}}
    with patch.object(main.claude_adapter, "complete_with_usage", mocked("print", ("ok", UsageParts()))), \
         patch.object(main.claude_adapter, "complete_agentic_file", mocked("agentic", decision)), \
         patch.object(main.claude_adapter, "complete_document", mocked("document", ("d", UsageParts()))):
        assert client.post("/v1/claude/chat/completions", json=_body()).status_code == 200
        assert client.post("/v1/claude/chat/completions", json=_body(
            reasoning_effort=None,
            response_format={"type": "json_object"})).status_code == 200
        assert client.post("/v1/claude/chat/completions", json=_body(
            tools=[tool], tool_choice="auto")).status_code == 200
        assert client.post("/v1/claude/chat/completions", json=_body(
            document_mode=True, document="d")).status_code == 200

    for name, mock in calls:
        assert mock.await_count >= 1, name
        for call in mock.await_args_list:
            assert "reasoning_effort" not in call.kwargs, name


@pytest.mark.asyncio
async def test_reasoning_effort_low_maps_to_claude_effort_flag(tmp_path):
    for name, call in _paths(tmp_path, reasoning_effort="low"):
        if not name.startswith("claude."):
            continue
        argv = _without_instruction(name, await _capture_argv(call))
        assert argv == _GOLDEN_PREFIX[name] + ["--effort", "low"], name


@pytest.mark.asyncio
async def test_reasoning_effort_low_maps_to_codex_config_override(tmp_path):
    for name, call in _paths(tmp_path, reasoning_effort="low"):
        if not name.startswith("codex."):
            continue
        argv = _without_instruction(name, await _capture_argv(call))
        expected = list(_GOLDEN_PREFIX[name])
        at = expected.index(CODEX_MODEL) + 1
        expected[at:at] = ["-c", 'model_reasoning_effort="low"']
        assert argv == expected, name


@pytest.mark.parametrize("value", ["minimal", "xhigh", "max", "LOW", "", "none"])
@pytest.mark.parametrize("endpoint", ["/v1/claude/chat/completions", "/v1/codex/chat/completions"])
@pytest.mark.parametrize("stream", [False, True])
def test_unsupported_reasoning_effort_is_rejected_with_400(client, value, endpoint, stream):
    with patch.object(main.claude_adapter, "complete_with_usage", _ok()) as claude, \
         patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
        resp = client.post(endpoint, json=_body(reasoning_effort=value, stream=stream))

    assert resp.status_code == 400
    error = resp.json()["error"]
    assert error["param"] == "reasoning_effort"
    assert error["code"] == "unsupported_value"
    assert HEADER not in resp.headers
    assert claude.await_count == 0 and codex.await_count == 0


@pytest.mark.parametrize("value", ["low", "medium", "high"])
def test_supported_reasoning_effort_values_reach_the_adapter(client, value):
    with patch.object(main.claude_adapter, "complete_with_usage", _ok()) as claude:
        resp = client.post("/v1/claude/chat/completions", json=_body(reasoning_effort=value))
    assert resp.status_code == 200
    assert claude.await_args.kwargs["reasoning_effort"] == value


def test_failover_maps_reasoning_effort_for_the_target_backend(client):
    with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=_AUTH_ERROR)) as claude, \
         patch.object(main.codex_adapter, "complete_with_usage", _ok("from codex")) as codex:
        resp = client.post("/v1/claude/chat/completions", json=_body(reasoning_effort="low"))

    assert resp.status_code == 200
    assert resp.headers["x-cli-proxy-failover"] == "claude->codex"
    assert claude.await_args.kwargs["reasoning_effort"] == "low"
    assert codex.await_args.kwargs == {"reasoning_effort": "low"}
    assert codex.await_args.args[1] == main.FAILOVER_CODEX_MODEL, "model name must still not cross the backend boundary"
    assert resp.headers[HEADER] == "low"


def test_failover_maps_reasoning_effort_for_the_target_backend_when_streaming(client):
    with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=_AUTH_ERROR)), \
         patch.object(main.codex_adapter, "complete_with_usage", _ok('{"a": 1}')) as codex:
        resp = client.post("/v1/claude/chat/completions", json=_body(
            reasoning_effort="low", stream=True, response_format={"type": "json_object"}))
        body = resp.text

    assert resp.status_code == 200
    assert "[DONE]" in body
    assert codex.await_args.kwargs == {"reasoning_effort": "low"}


def test_effort_header_only_when_applied(client):
    with patch.object(main.claude_adapter, "complete_with_usage", _ok()), \
         patch.object(main.codex_adapter, "complete_with_usage", _ok()):
        with_effort = client.post("/v1/claude/chat/completions", json=_body(reasoning_effort="low"))
        codex_effort = client.post("/v1/codex/chat/completions", json=_body(
            model=CODEX_MODEL, reasoning_effort="high"))
        without = client.post("/v1/claude/chat/completions", json=_body())
        explicit_null = client.post("/v1/codex/chat/completions", json=_body(reasoning_effort=None))

    assert with_effort.headers[HEADER] == "low"
    assert codex_effort.headers[HEADER] == "high"
    assert HEADER not in without.headers
    assert HEADER not in explicit_null.headers

    async def fake_stream(prompt, model, max_tokens, **kwargs):
        yield ("delta", "hi")
        yield ("usage", UsageParts())

    with patch.object(main.claude_adapter, "stream", fake_stream):
        streamed = client.post("/v1/claude/chat/completions", json=_body(
            stream=True, reasoning_effort="medium"))
        streamed_plain = client.post("/v1/claude/chat/completions", json=_body(stream=True))
    assert streamed.headers[HEADER] == "medium"
    assert HEADER not in streamed_plain.headers

    # A failed call applied nothing, so it carries no effort header either.
    with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=_AUTH_ERROR)), \
         patch.object(main.codex_adapter, "complete_with_usage", AsyncMock(side_effect=_AUTH_ERROR)):
        failed = client.post("/v1/claude/chat/completions", json=_body(reasoning_effort="low"))
    assert failed.status_code == 502
    assert HEADER not in failed.headers
