"""A streaming CLI that writes a lot to stderr, against a real uvicorn server and in process.

Before 2026-11-25 the streaming paths read stderr only after stdout had ended: a CLI that wrote
more than the pipe and stream buffers hold to stderr blocked, and the request hung until the time
limit while it occupied a CLI slot. The server cases run once per event loop (see the fixture of
test_gateway_lifetime_e2e).
"""
from __future__ import annotations

import asyncio
import json
import time

import httpx
import pytest

import claude_adapter
import cli_process
import codex_adapter
import fake_cli
from test_gateway_lifetime_e2e import _body, _path, _slot_is_free, server  # noqa: F401
from test_process_lifetime import _run_on, fake  # noqa: F401


@pytest.fixture
def clean_server(server):  # noqa: F811
    """A failing test must not occupy the only slot for the next one."""
    yield server
    fake_cli.kill_leftovers(server["dir"])


def _stream(server, backend: str, token: str, mode: str) -> tuple[list, str, float]:
    started = time.monotonic()
    response = httpx.post(
        server["base"] + _path(backend),
        json=_body(backend, token, mode, stream=True, stream_options={"include_usage": True}),
        headers={"x-cli-proxy-failover": "off"}, timeout=15)
    elapsed = time.monotonic() - started
    assert response.status_code == 200, response.text
    lines = [line[6:] for line in response.text.splitlines() if line.startswith("data: ")]
    assert lines[-1] == "[DONE]", response.text
    return [json.loads(line) for line in lines[:-1]], response.text, elapsed


@pytest.mark.parametrize("mode", ["NOISYBEFORE", "NOISYBETWEEN"])
@pytest.mark.parametrize("backend", ["claude", "codex"])
def test_a_stream_with_much_stderr_is_answered_completely(clean_server, backend, mode):
    server = clean_server
    events, text, elapsed = _stream(server, backend, f"{mode.lower()}-{backend}", mode)

    assert elapsed < 8
    assert not any("error" in e for e in events), text
    choices = [c for e in events for c in e["choices"]]
    assert "".join(c["delta"].get("content", "") for c in choices) == "pong done"
    assert choices[-1]["finish_reason"] == "stop"
    assert events[-1]["usage"]["prompt_tokens"] == 3
    assert events[-1]["usage"]["completion_tokens"] == 2
    _slot_is_free(server, backend, f"after-{mode.lower()}-{backend}")


@pytest.mark.parametrize("backend", ["claude", "codex"])
def test_a_failed_stream_with_much_stderr_reports_the_kept_end_of_it(clean_server, backend):
    server = clean_server
    events, text, elapsed = _stream(server, backend, f"noisyfail-{backend}", "NOISYFAIL")

    assert elapsed < 8
    message = events[-1]["error"]["message"]
    prefix = f"{backend} CLI (stream) exited with code 7: "
    # The kept end of stderr from its first complete line, clipped to 200 characters as before.
    assert message.startswith(prefix + "TAIL-HEAD the real cause\n."), message
    assert len(message) <= len(prefix) + 200
    assert "STDERR-START" not in text and "CUT-LINE" not in text
    _slot_is_free(server, backend, f"after-noisyfail-{backend}")


# --- the kept end -------------------------------------------------------------------------

@pytest.mark.parametrize("written, kept", [
    (b"", b""),
    (b"first line\nsecond line\n", b"first line\nsecond line\n"),
    (b"x" * 64, b"x" * 64),
    # Longer than the limit: from the first complete line of the kept end ...
    (b"a" * 100 + b"\n" + b"b" * 60 + b" cut\nwhole line\n", b"whole line\n"),
    # ... without a line break from the first complete word ...
    (b"a" * 100 + b" whole words", b"whole words"),
    # ... and an unbroken run is kept as it is.
    (b"a" * 100 + b"\n", b"a" * 63 + b"\n"),
])
async def test_only_the_end_of_stderr_is_kept(written, kept):
    stream = asyncio.StreamReader()
    stream.feed_data(written)
    stream.feed_eof()

    assert await cli_process._read_tail(stream, 64) == kept


async def test_a_credential_cut_in_two_does_not_reach_the_error_text(monkeypatch):
    """The cut falls right behind ``Bearer ``: the token alone would not be recognised."""
    token = b"abc123def456ghi789"
    rest = b"\nthe cause\n"
    monkeypatch.setattr(cli_process, "STDERR_TAIL_BYTES", len(token) + len(rest))
    stream = asyncio.StreamReader()
    stream.feed_data(b"noise\nAuthorization: Bearer " + token + rest)
    stream.feed_eof()

    drain = cli_process.StderrDrain(stream)
    try:
        assert await drain.tail() == b"the cause\n"
    finally:
        await drain.close()


# --- in process: the task that empties stderr never outlives the stream -----------------

_STREAMS = {
    "claude": lambda: claude_adapter._stream_claude("prompt", None, None),
    "codex": lambda: codex_adapter._stream_codex("prompt", None, None),
}


async def _no_task_left() -> None:
    others = asyncio.all_tasks() - {asyncio.current_task()}
    # The feeder is cancelled without being awaited and ends on the next turn of the loop.
    if others:
        await asyncio.wait(others, timeout=1)
    assert not (asyncio.all_tasks() - {asyncio.current_task()})


@pytest.mark.parametrize("loop_name", ["asyncio", "uvloop"])
@pytest.mark.parametrize("ending", ["success", "exit code", "closed early", "caller limit", "cancelled"])
@pytest.mark.parametrize("backend", ["claude", "codex"])
def test_no_task_is_left_however_the_stream_ends(fake, monkeypatch, backend, ending, loop_name):
    mode = {"success": "noisybetween", "exit code": "noisyfail", "closed early": "noisybetween",
            "caller limit": "orphanerr", "cancelled": "orphanerr"}[ending]
    token = f"tasks-{backend}-{ending.replace(' ', '-')}-{loop_name}"
    monkeypatch.setenv("FAKE_TOKEN", token)
    monkeypatch.setenv("FAKE_MODE", mode)
    module = claude_adapter if backend == "claude" else codex_adapter
    monkeypatch.setattr(module, "_semaphore", asyncio.Semaphore(1))

    async def drain(events) -> list:
        return [event async for event in events]

    async def probe() -> None:
        events = _STREAMS[backend]()
        if ending == "success":
            assert (await asyncio.wait_for(drain(events), 10))[-1][0] == "usage"
        elif ending == "exit code":
            with pytest.raises(RuntimeError, match="exited with code 7: TAIL-HEAD"):
                await asyncio.wait_for(drain(events), 10)
        elif ending == "closed early":
            assert (await asyncio.wait_for(anext(events), 10))[0] == "delta"
            await asyncio.wait_for(events.aclose(), 10)
        elif ending == "caller limit":
            cli_process.set_deadline(1.0, 1800)
            with pytest.raises(cli_process.CallerTimeoutError):
                await asyncio.wait_for(drain(events), 10)
        else:
            running = asyncio.create_task(drain(events))
            await asyncio.sleep(1.0)
            assert not running.done()
            running.cancel()
            with pytest.raises(asyncio.CancelledError):
                await asyncio.wait_for(running, 10)
        await _no_task_left()

    _run_on(loop_name, probe)


@pytest.mark.parametrize("backend", ["claude", "codex"])
def test_a_successful_stream_does_not_wait_for_a_helper_that_holds_only_stderr(fake, monkeypatch, backend):
    """uvloop reports the end of the CLI although a helper still holds stderr, and a successful
    stream never needed stderr: it must not start to wait for its end. (The standard asyncio loop
    waits for all pipes before it reports the end, so there the time limit applies, as before.)"""
    token = f"errhelper-{backend}"
    monkeypatch.setenv("FAKE_TOKEN", token)
    monkeypatch.setenv("FAKE_MODE", "errhelper")
    module = claude_adapter if backend == "claude" else codex_adapter
    monkeypatch.setattr(module, "_semaphore", asyncio.Semaphore(1))

    async def probe() -> None:
        async def drain() -> list:
            return [event async for event in _STREAMS[backend]()]

        try:
            assert (await asyncio.wait_for(drain(), 5))[-1][0] == "usage"
            await _no_task_left()
        finally:
            fake_cli.kill_leftovers(fake["dir"])

    _run_on("uvloop", probe)
