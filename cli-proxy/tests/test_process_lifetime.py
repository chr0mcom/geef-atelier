"""Every CLI path ends the CLI with its whole process group (real processes, no mocks).

A fake CLI (tests/fake_cli.py) starts a grandchild that inherits its output pipes, like the
helpers of the real CLIs. Three ways a call ends early are checked on every path:

* the request is cancelled (the caller disconnected): the group is killed, the cancellation
  propagates;
* the caller's limit (``x-cli-proxy-timeout-seconds``) runs out: the group is killed and
  ``CallerTimeoutError`` is raised;
* the gateway's own cap runs out (no header): the group is killed and the error is the same
  as before the change.
"""
from __future__ import annotations

import asyncio
import os
import time
from pathlib import Path

import pytest

import claude_adapter
import cli_process
import codex_adapter
import fake_cli
from adapters.gemini_adapter import GeminiAdapter
from adapters.generic_adapter import GenericAdapter


@pytest.fixture
def fake(tmp_path, monkeypatch):
    bin_dir = tmp_path / "bin"
    fake_dir = tmp_path / "runs"
    fake_dir.mkdir()
    main = fake_cli.install(bin_dir)
    monkeypatch.setenv("PATH", f"{bin_dir}:{__import__('os').environ['PATH']}")
    monkeypatch.setenv("FAKE_DIR", str(fake_dir))
    monkeypatch.setenv("FAKE_MODE", "hang")
    monkeypatch.setattr(codex_adapter, "CODEX_HOME", str(tmp_path / "codex-home"))
    (tmp_path / "codex-home").mkdir()
    cli_process.set_deadline(None, 1800)
    yield {"dir": fake_dir, "main": main, "tmp": tmp_path}
    cli_process.set_deadline(None, 1800)
    fake_cli.kill_leftovers(fake_dir)


async def _drain(gen):
    async for _ in gen:
        pass


def _paths(tmp):
    ws = tmp / "ws"
    ws.mkdir(exist_ok=True)
    (ws / "draft.md").write_text("draft", encoding="utf-8")
    messages = {"messages": [{"role": "user", "content": "hi"}], "model": "x"}
    return {
        "codex print": lambda: codex_adapter._run_codex("prompt", None, None),
        "codex document": lambda: codex_adapter._run_codex_document("s", "u", "draft", ws, None, None),
        "codex decision": lambda: codex_adapter._run_codex_decision("i", None, ws),
        "codex stream": lambda: _drain(codex_adapter._stream_codex("prompt", None, None)),
        "codex image": lambda: codex_adapter._run_codex_image("a cat", None),
        "claude print": lambda: claude_adapter._run_claude("prompt", None, None),
        "claude lean": lambda: claude_adapter._run_claude_lean("prompt", "system", None, ws),
        "claude document": lambda: claude_adapter._run_claude_document("s", "u", "draft", ws, None, None),
        "claude decision": lambda: claude_adapter._run_claude_decision("i", None, ws),
        "claude stream": lambda: _drain(claude_adapter._stream_claude("prompt", None, None)),
        "gemini": lambda: GeminiAdapter().execute({"settings": {"binary": "gemini"}}, messages),
        "generic": lambda: GenericAdapter().execute(
            {"settings": {"binary": str(tmp / "bin" / "fakecli"), "stdin_mode": True}}, messages),
    }


PATHS = ["codex print", "codex document", "codex decision", "codex stream", "codex image",
         "claude print", "claude lean", "claude document", "claude decision", "claude stream",
         "gemini", "generic"]


async def _started(fake_dir, token) -> tuple[int, int]:
    for _ in range(200):
        leaders = fake_cli.starts(fake_dir, token)
        children = fake_cli.grandchildren(fake_dir, token)
        if leaders and children:
            return leaders[0], children[0]
        await asyncio.sleep(0.05)
    raise AssertionError("the fake CLI did not start")


async def _gone(*pids: int) -> bool:
    for _ in range(200):
        if not any(fake_cli.alive(p) for p in pids):
            return True
        await asyncio.sleep(0.05)
    return False


@pytest.mark.parametrize("path", PATHS)
async def test_cancellation_ends_the_cli_and_its_grandchild(fake, monkeypatch, path):
    token = f"cancel-{path.replace(' ', '-')}"
    monkeypatch.setenv("FAKE_TOKEN", token)
    task = asyncio.create_task(_paths(fake["tmp"])[path]())
    leader, grandchild = await _started(fake["dir"], token)
    assert fake_cli.alive(leader) and fake_cli.alive(grandchild)

    task.cancel()
    with pytest.raises(asyncio.CancelledError):
        await task

    assert await _gone(leader, grandchild), f"{path}: CLI or grandchild still running after cancellation"
    assert fake_cli.starts(fake["dir"], token) == [leader]


@pytest.mark.parametrize("path", PATHS)
async def test_the_callers_limit_ends_the_cli_and_its_grandchild(fake, monkeypatch, path):
    token = f"limit-{path.replace(' ', '-')}"
    monkeypatch.setenv("FAKE_TOKEN", token)
    cli_process.set_deadline(1.0, 1800)
    started = time.monotonic()

    with pytest.raises(cli_process.CallerTimeoutError, match="caller's limit of 1 s"):
        await _paths(fake["tmp"])[path]()

    assert time.monotonic() - started < 10
    leader, grandchild = await _started(fake["dir"], token)
    assert await _gone(leader, grandchild), f"{path}: CLI or grandchild still running after the caller's limit"


@pytest.mark.parametrize("path, adapter, message", [
    ("codex print", codex_adapter, "codex CLI timed out after 1 seconds"),
    ("claude print", claude_adapter, "claude CLI timed out after 1 seconds"),
    ("codex document", codex_adapter, "codex CLI (document mode) timed out after 1s"),
])
async def test_without_header_the_gateway_cap_keeps_its_error_and_now_ends_the_grandchild(
        fake, monkeypatch, path, adapter, message):
    token = f"cap-{path.replace(' ', '-')}"
    monkeypatch.setenv("FAKE_TOKEN", token)
    monkeypatch.setattr(adapter, "CLI_TIMEOUT_SECONDS", 1)

    with pytest.raises(RuntimeError) as caught:
        await _paths(fake["tmp"])[path]()

    assert not isinstance(caught.value, cli_process.CallerTimeoutError)
    assert str(caught.value) == message
    leader, grandchild = await _started(fake["dir"], token)
    assert await _gone(leader, grandchild)


async def test_no_cli_starts_once_the_callers_limit_is_used_up(fake, monkeypatch):
    monkeypatch.setenv("FAKE_TOKEN", "spent")
    cli_process.set_deadline(0.05, 1800)
    await asyncio.sleep(0.1)

    with pytest.raises(cli_process.CallerTimeoutError):
        await codex_adapter._run_codex("prompt", None, None)

    assert fake_cli.starts(fake["dir"], "spent") == []


async def test_a_finished_call_is_unchanged(fake, monkeypatch):
    monkeypatch.setenv("FAKE_TOKEN", "ok")
    monkeypatch.setenv("FAKE_MODE", "slow")
    monkeypatch.setenv("FAKE_SLOW_SECONDS", "0")

    text, _usage = await claude_adapter._run_claude("prompt", None, None)

    assert text == "pong"


async def _exited(fake_dir, token) -> tuple[int, int]:
    """(CLI, grandchild) once the CLI is gone and asyncio has had time to reap it."""
    leader, grandchild = await _started(fake_dir, token)
    assert await _gone(leader)
    await asyncio.sleep(0.3)
    assert fake_cli.alive(grandchild)
    return leader, grandchild


@pytest.mark.parametrize("path", PATHS)
async def test_cancellation_ends_the_group_even_when_the_cli_has_already_exited(fake, monkeypatch, path):
    token = f"orphan-cancel-{path.replace(' ', '-')}"
    monkeypatch.setenv("FAKE_TOKEN", token)
    monkeypatch.setenv("FAKE_MODE", "orphan")
    task = asyncio.create_task(_paths(fake["tmp"])[path]())
    _leader, grandchild = await _exited(fake["dir"], token)
    assert not task.done()

    task.cancel()
    with pytest.raises(asyncio.CancelledError):
        await asyncio.wait_for(task, 10)

    assert await _gone(grandchild), f"{path}: the helper holding the pipes survived the cancellation"


@pytest.mark.parametrize("path", PATHS)
async def test_the_callers_limit_ends_the_group_even_when_the_cli_has_already_exited(fake, monkeypatch, path):
    token = f"orphan-limit-{path.replace(' ', '-')}"
    monkeypatch.setenv("FAKE_TOKEN", token)
    monkeypatch.setenv("FAKE_MODE", "orphan")
    cli_process.set_deadline(2.0, 1800)

    with pytest.raises(cli_process.CallerTimeoutError):
        await asyncio.wait_for(_paths(fake["tmp"])[path](), 10)

    _leader, grandchild = await _started(fake["dir"], token)
    assert await _gone(grandchild), f"{path}: the helper holding the pipes survived the caller's limit"


@pytest.mark.parametrize("path", ["codex stream", "claude stream"])
async def test_a_stream_limit_also_covers_a_helper_that_holds_only_stderr(fake, monkeypatch, path):
    token = f"orphanerr-{path.replace(' ', '-')}"
    monkeypatch.setenv("FAKE_TOKEN", token)
    monkeypatch.setenv("FAKE_MODE", "orphanerr")
    cli_process.set_deadline(2.0, 1800)

    with pytest.raises(cli_process.CallerTimeoutError):
        await asyncio.wait_for(_paths(fake["tmp"])[path](), 10)

    _leader, grandchild = await _started(fake["dir"], token)
    assert await _gone(grandchild)


def _run_on(loop_name: str, coroutine_function):
    """Run on an explicitly chosen event loop: the end of a subprocess is reported differently."""
    if loop_name == "uvloop":
        factory = pytest.importorskip("uvloop").new_event_loop
    else:
        factory = asyncio.SelectorEventLoop
    with asyncio.Runner(loop_factory=factory) as runner:
        runner.run(coroutine_function())


async def _until(condition, what: str) -> None:
    for _ in range(200):
        if condition():
            return
        await asyncio.sleep(0.05)
    raise AssertionError(f"never happened: {what}")


@pytest.mark.parametrize("loop_name", ["asyncio", "uvloop"])
@pytest.mark.parametrize("fd", [1, 2], ids=["stdout", "stderr"])
def test_stop_returns_although_back_pressure_has_paused_a_pipe(loop_name, fd):
    """Nobody reads, so the StreamReader pauses its pipe, which then never sees its end."""
    async def probe() -> None:
        proc = await cli_process.spawn(
            "bash", "-c", f"head -c 20000000 /dev/zero >&{fd}; exec sleep 300",
            stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE, limit=4096)
        try:
            reader = proc.stdout if fd == 1 else proc.stderr
            await _until(lambda: len(reader._buffer) > 2 * 4096, "back pressure")
            await asyncio.sleep(0.2)
            assert proc.returncode is None

            await asyncio.wait_for(cli_process.stop(proc), 5)

            assert proc.returncode == -9
            await asyncio.wait_for(reader.read(), 5)
            assert reader.at_eof()
        finally:
            if proc.returncode is None:
                proc._transport.close()

    _run_on(loop_name, probe)


@pytest.mark.parametrize("loop_name", ["asyncio", "uvloop"])
def test_stop_does_not_wait_for_pipes_held_outside_the_group_and_ends_a_pending_read(loop_name, tmp_path):
    """A process in its own session survives the group signal and keeps all three pipes, with
    input still unsent: ``stop`` returns, and a read waiting elsewhere sees a plain end."""
    pid_file = tmp_path / "outsider.pid"

    async def probe() -> None:
        proc = await cli_process.spawn(
            "bash", "-c",
            f"setsid sleep 300 <&0 & echo $! > {pid_file}.tmp; mv {pid_file}.tmp {pid_file}; exec sleep 300",
            stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE)
        try:
            await _until(pid_file.exists, "the outsider")
            await asyncio.sleep(0.3)
            proc.stdin.write(b"x" * 20_000_000)
            pending = asyncio.create_task(proc.stdout.read())
            await asyncio.sleep(0.2)
            assert not pending.done()

            await asyncio.wait_for(cli_process.stop(proc), 5)

            assert proc.returncode == -9
            assert await asyncio.wait_for(pending, 5) == b""
        finally:
            if pid_file.exists():
                outsider = int(pid_file.read_text())
                if fake_cli.alive(outsider) and b"sleep" in Path(f"/proc/{outsider}/cmdline").read_bytes():
                    os.kill(outsider, 9)
            if proc.returncode is None:
                proc._transport.close()

    _run_on(loop_name, probe)


async def test_a_reused_group_id_is_never_signalled(monkeypatch):
    """The CLI is reaped, its pipes look open, but its number belongs to a running process again:
    that process cannot be ours, so nothing is signalled."""
    signalled: list[int] = []
    monkeypatch.setattr(cli_process.os, "killpg", lambda pid, sig: signalled.append(pid))

    class _Reaped:
        returncode = 0
        stderr = None

        def __init__(self, pid: int) -> None:
            self.pid = pid
            self.stdout = asyncio.StreamReader()

        async def wait(self) -> int:
            return 0

    await cli_process.stop(_Reaped(__import__("os").getpid()))
    assert signalled == []

    free = 2 ** 22 + 12345
    await cli_process.stop(_Reaped(free))
    assert signalled == [free]

    closed = _Reaped(free)
    closed.stdout.feed_eof()
    await cli_process.stop(closed)
    assert signalled == [free]


class TestHeaderParsing:
    def test_values(self):
        assert cli_process.parse_timeout(None) is None
        assert cli_process.parse_timeout(" 85 ") == 85.0
        assert cli_process.parse_timeout("0.5") == 0.5
        for bad in ("", "abc", "0", "-3", "nan", "inf"):
            with pytest.raises(ValueError):
                cli_process.parse_timeout(bad)

    def test_a_limit_at_or_above_the_cap_changes_nothing(self):
        cli_process.set_deadline(1800, 1800)
        assert not cli_process.caller_limited()
        assert cli_process.timeout(1800) == 1800
        cli_process.set_deadline(85, 1800)
        assert cli_process.caller_limited()
        assert 84 < cli_process.timeout(1800) <= 85
        assert cli_process.timeout(30) == 30
        cli_process.set_deadline(None, 1800)
        assert not cli_process.caller_limited()
