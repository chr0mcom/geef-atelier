"""Lifetime of the CLI processes the gateway starts for a request.

Every CLI call of a request runs in its own process group (``start_new_session``), so the
gateway can end the CLI together with everything it started (node workers, MCP servers,
tool subprocesses). Before 2026-11-25 a timeout killed only the CLI itself, and a caller that
gave up or disconnected left the CLI running until ``CLI_TIMEOUT_SECONDS`` (1800 s) -- on a
subscription shared with other production systems.

Three ways a call ends early, all through :func:`communicate` (or :func:`stop` on the
streaming paths):

* the gateway's own cap (``CLI_TIMEOUT_SECONDS``): the whole group is killed, the error text
  is the same as before, so failover and breaker behave as before;
* the caller's limit from the request header ``x-cli-proxy-timeout-seconds`` (capped by the
  gateway's own limit): the group is killed and :class:`CallerTimeoutError` is raised. It is not
  a backend fault: it never moves the breaker and never hands the request over (main.py);
* the request is cancelled, because the caller disconnected (disconnect_guard.py) or the
  server shuts down: the group is killed and the cancellation propagates, so nothing else
  starts for that request.

"The whole group" includes the case that the CLI itself has already exited while one of its
helpers still holds stdout or stderr, so the gateway is still waiting for output (see
:func:`stop`). The caller's limit also covers the wait for a free CLI slot (:func:`slot`).
Processes that leave the group (their own session) are out of reach.

The streaming paths read stdout line by line and empty stderr beside it (:class:`StderrDrain`).

Requests without the header and with a connected caller behave exactly as before.
"""
from __future__ import annotations

import asyncio
import math
import os
import re
import signal
import time
from collections.abc import Callable
from contextlib import asynccontextmanager
from contextvars import ContextVar
from dataclasses import dataclass, field

#: Request header with the caller's time limit for the CLI work of this request, in seconds.
TIMEOUT_HEADER = "x-cli-proxy-timeout-seconds"

#: How much of the end of a streaming CLI's stderr is kept for its error message, in bytes.
STDERR_TAIL_BYTES = 64 * 1024


class CallerTimeoutError(RuntimeError):
    """The caller's own time limit (``x-cli-proxy-timeout-seconds``) ran out.

    The message says "timed out" like every other timeout, but main.py handles this class before
    the generic failover path: it is the caller's choice, not evidence that the backend is down.
    """


@dataclass(frozen=True)
class _Deadline:
    at: float
    seconds: float


_deadline: ContextVar[_Deadline | None] = ContextVar("cli_call_deadline", default=None)


@dataclass
class RequestTrace:
    """What the disconnect guard needs for its log line: no request content, only names and times."""

    request_id: str
    started: float = field(default_factory=time.monotonic)
    backends: list[str] = field(default_factory=list)

    @property
    def backend(self) -> str:
        return self.backends[-1] if self.backends else "none"


_trace: ContextVar[RequestTrace | None] = ContextVar("cli_request_trace", default=None)


def bind_trace(trace: RequestTrace | None):
    """Attach `trace` to the current context; returns the token for ``_trace.reset``."""
    return _trace.set(trace)


def current_trace() -> RequestTrace | None:
    return _trace.get()


def parse_timeout(raw: str | None) -> float | None:
    """The header value in seconds, or None without header. Raises ValueError for anything but a
    finite positive number."""
    if raw is None:
        return None
    try:
        value = float(raw.strip())
    except ValueError:
        raise ValueError(f"{TIMEOUT_HEADER} must be a positive number of seconds") from None
    if not math.isfinite(value) or value <= 0:
        raise ValueError(f"{TIMEOUT_HEADER} must be a positive number of seconds")
    return value


def set_deadline(seconds: float | None, ceiling: float) -> None:
    """Start the caller's limit for this request. A limit at or above `ceiling` changes nothing:
    the gateway's own cap applies, with its own error, exactly as without the header."""
    if seconds is None or seconds >= ceiling:
        _deadline.set(None)
        return
    _deadline.set(_Deadline(time.monotonic() + seconds, seconds))


def caller_limited() -> bool:
    """Whether the caller's limit is stricter than the gateway's own cap for this request."""
    return _deadline.get() is not None


def timeout(ceiling: float) -> float:
    """Seconds the next CLI run of this request may take: `ceiling`, or what is left of the
    caller's limit when that is shorter. The caller's limit counts from the arrival of the
    request, so queueing for a CLI slot and an in-proxy retry use it up too."""
    deadline = _deadline.get()
    if deadline is None:
        return ceiling
    return max(0.0, min(ceiling, deadline.at - time.monotonic()))


def caller_timeout(label: str) -> CallerTimeoutError:
    deadline = _deadline.get()
    seconds = deadline.seconds if deadline is not None else 0.0
    return CallerTimeoutError(f"{label} timed out after the caller's limit of {seconds:g} s")


@asynccontextmanager
async def slot(semaphore: asyncio.Semaphore, label: str):
    """Hold one CLI slot. With a caller's limit the wait for the slot ends when the limit runs
    out (:class:`CallerTimeoutError`); without one this is a plain ``async with semaphore``."""
    if not caller_limited():
        async with semaphore:
            yield
        return
    try:
        async with asyncio.timeout(timeout(math.inf)):
            await semaphore.acquire()
    except TimeoutError:
        raise caller_timeout(f"{label} (waiting for a free slot)") from None
    try:
        yield
    finally:
        semaphore.release()


async def spawn(*args, label: str | None = None, **kwargs) -> asyncio.subprocess.Process:
    """``asyncio.create_subprocess_exec`` in a new process group.

    Refuses to start once the caller's limit is used up, so a retry inside the proxy never
    starts a CLI after the caller has stopped waiting.
    """
    name = label or os.path.basename(str(args[0]))
    if caller_limited() and timeout(math.inf) <= 0:
        raise caller_timeout(name)
    proc = await asyncio.create_subprocess_exec(*args, start_new_session=True, **kwargs)
    trace = _trace.get()
    if trace is not None:
        trace.backends.append(name)
    return proc


def _output_open(proc) -> bool:
    """Whether stdout or stderr of `proc` has not reached its end: somebody still holds the pipe."""
    streams = (getattr(proc, "stdout", None), getattr(proc, "stderr", None))
    return any(isinstance(s, asyncio.StreamReader) and not s.at_eof() for s in streams)


def _pid_in_use(pid: int) -> bool:
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def _close_pipes(proc) -> None:
    """Close the gateway's end of stdin, stdout and stderr, discarding what is still in them.

    The standard asyncio loop reports the end of a subprocess only after all its pipes are
    disconnected. A pipe that back pressure has paused (the caller stopped reading) never sees
    its end, so ``proc.wait()`` would not return, and neither would a wait on a pipe that a
    process outside the group still holds. Closing ends both, on every loop: a read waiting
    on the stream gets a plain end of stream, and data already buffered stays readable.
    """
    if not isinstance(proc, asyncio.subprocess.Process):
        return
    # asyncio.subprocess.Process has no public accessor for its transport.
    transport = getattr(proc, "_transport", None)
    if transport is None:
        return
    stdin = transport.get_pipe_transport(0)
    # abort, not close: close would first wait until unsent input has been read. A transport
    # that is closing with nothing left to send must not be aborted a second time.
    if stdin is not None and (not stdin.is_closing() or stdin.get_write_buffer_size() > 0):
        stdin.abort()
    for fd in (1, 2):
        pipe = transport.get_pipe_transport(fd)
        if pipe is not None:
            pipe.close()


async def stop(proc) -> None:
    """Kill the CLI and every process of its group, close its pipes, then reap the CLI.

    Whatever the pipes still hold is discarded: call this only when the output of the CLI is
    no longer wanted.

    The group id is the pid of the CLI. When may it be signalled without reaching a stranger?

    * The CLI is not yet reaped: its pid is still reserved for it.
    * The CLI is reaped, but its output is still open, so a helper is (very likely) still
      holding the pipes. Linux does not hand out a number as a new pid while any process still
      uses it as its group or session id, so as long as a member of our group lives, the id is
      ours. If the group is empty (the pipe holder left it), the signal finds nobody -- unless
      the number has been reused meanwhile. A reused number shows as a process with exactly
      this pid, which cannot be one of ours (ours is reaped), so then nothing is signalled.

    With the CLI reaped and its output at its end, nothing is signalled.
    """
    reaped = getattr(proc, "returncode", None) is not None
    pid = getattr(proc, "pid", None)
    if isinstance(pid, int) and pid > 0 and (
            not reaped or (_output_open(proc) and not _pid_in_use(pid))):
        try:
            os.killpg(pid, signal.SIGKILL)
        except (ProcessLookupError, PermissionError):
            pass
    if not reaped:
        try:
            proc.kill()
        except ProcessLookupError:
            pass
    # After the signals, which depend on whether the output was still open.
    _close_pipes(proc)
    await proc.wait()


def _from_a_boundary(cut: bytes) -> bytes:
    """`cut`, the end of a longer text, from its first complete line (without a line break: from
    its first complete word). The cut may fall inside a credential; without its beginning
    (``sk-``, ``Bearer``) redaction.py would not recognise the rest."""
    for boundary in (rb"\n", rb"\s"):
        found = re.search(boundary, cut)
        if found and cut[found.end():].strip():
            return cut[found.end():]
    return cut


async def _read_tail(stream: asyncio.StreamReader, limit: int) -> bytes:
    """Read `stream` to its end and return it; of more than `limit` bytes only the end."""
    kept = bytearray()
    dropped = False
    while chunk := await stream.read(limit):
        kept += chunk
        if len(kept) > limit:
            del kept[:-limit]
            dropped = True
    return _from_a_boundary(bytes(kept)) if dropped else bytes(kept)


class StderrDrain:
    """Empties the stderr of a streaming CLI while the gateway reads its stdout.

    A CLI that writes more to stderr than the pipe and stream buffers hold blocks until somebody
    reads it, and a streaming path reads stdout to its end before it looks at stderr. Only the
    last :data:`STDERR_TAIL_BYTES` are kept. The non-streaming paths need none of this:
    :func:`communicate` reads both at once.
    """

    def __init__(self, stream: asyncio.StreamReader) -> None:
        self._task = asyncio.create_task(_read_tail(stream, STDERR_TAIL_BYTES))

    async def tail(self) -> bytes:
        """The kept end of stderr, once stderr has reached its end."""
        return await self._task

    async def close(self) -> None:
        """Stop reading, if still reading, and wait until the task is gone. For every way out of
        a stream. A successful stream does not wait for the end of stderr: a helper may still
        hold it."""
        self._task.cancel()
        await asyncio.wait([self._task])
        if not self._task.cancelled():
            self._task.exception()  # retrieved, so asyncio does not log it as lost


async def communicate(
    proc,
    data: bytes | None,
    ceiling: float,
    on_timeout: Callable[[], BaseException],
    label: str,
) -> tuple[bytes, bytes]:
    """``proc.communicate`` with the effective time limit.

    On the gateway's own cap the group is killed and ``on_timeout()`` is raised (the error the
    call site raised before); on the caller's limit :class:`CallerTimeoutError`; on cancellation
    the group is killed and the cancellation propagates.
    """
    limit = timeout(ceiling)
    by_caller = caller_limited() and limit < ceiling
    pending = proc.communicate(input=data) if data is not None else proc.communicate()
    try:
        return await asyncio.wait_for(pending, timeout=limit)
    except asyncio.TimeoutError:
        await stop(proc)
        if by_caller:
            raise caller_timeout(label) from None
        raise on_timeout() from None
    except BaseException:
        await stop(proc)
        raise
