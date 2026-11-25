"""Stop the CLI work of a request whose caller has gone.

Starlette does not cancel a plain (non-streaming) endpoint when the client disconnects, and a
streaming endpoint only notices it on the next write. A request whose caller gave up (its own
time limit, a cancelled job, a crashed process) therefore kept its CLI running until
``CLI_TIMEOUT_SECONDS`` -- on a subscription shared with other production systems.

This pure ASGI middleware sits outside every other middleware. Once the application has read
the complete request body, it listens for ``http.disconnect``. Per the ASGI spec (and in uvicorn)
``receive()`` returns that message after the body only when the connection is closed or the
response is complete, so a caller that stays connected never triggers it -- however long the
call runs, and with HTTP keep-alive the next request is a new ASGI call with a new guard. A
disconnect after the final response body has been handed to the server is ignored.

On a real disconnect the request's task is cancelled. The cancellation reaches the adapter,
which kills the CLI's whole process group (cli_process.py); semaphores, workspaces and the
in-flight counter are released by their context managers. A cancellation is no exception the
failover catches: it never counts against the breaker and never starts a call on the partner
backend or a retry. One log line per aborted request names the backend, the duration and the
request id -- never any content.
"""
from __future__ import annotations

import asyncio
import logging
import time
import uuid

import cli_process

log = logging.getLogger(__name__)

REQUEST_ID_HEADER = b"x-request-id"


def _request_id(scope) -> str:
    for name, value in scope.get("headers") or []:
        if name.lower() == REQUEST_ID_HEADER:
            text = value.decode("latin-1").strip()
            if text:
                return text
    return f"req_{uuid.uuid4().hex}"


class DisconnectGuard:
    """ASGI middleware: cancel the request when its caller disconnects before the response."""

    def __init__(self, app) -> None:
        self.app = app

    async def __call__(self, scope, receive, send) -> None:
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return

        trace = cli_process.RequestTrace(request_id=_request_id(scope))
        scope.setdefault("state", {})["request_id"] = trace.request_id
        body_read = asyncio.Event()
        response_done = False

        async def guarded_receive():
            message = await receive()
            if message["type"] == "http.request" and not message.get("more_body", False):
                body_read.set()
            return message

        async def guarded_send(message) -> None:
            nonlocal response_done
            if message["type"] == "http.response.body" and not message.get("more_body", False):
                response_done = True
            await send(message)

        async def caller_gone() -> None:
            await body_read.wait()
            while True:
                message = await receive()
                if message["type"] == "http.disconnect":
                    if response_done:
                        await asyncio.Event().wait()
                    return

        token = cli_process.bind_trace(trace)
        try:
            app_task = asyncio.create_task(self.app(scope, guarded_receive, guarded_send))
        finally:
            cli_process._trace.reset(token)
        watcher = asyncio.create_task(caller_gone())
        try:
            await asyncio.wait({app_task, watcher}, return_when=asyncio.FIRST_COMPLETED)
            if app_task.done():
                app_task.result()
                return
            app_task.cancel()
            try:
                await app_task
            except asyncio.CancelledError:
                current = asyncio.current_task()
                if current is not None and current.cancelling():
                    raise
            except Exception:  # noqa: BLE001 -- the caller is gone; nobody can receive this error
                log.exception("request %s failed while it was being aborted", trace.request_id)
            log.warning(
                "caller disconnected, request aborted | request_id=%s | backend=%s | after=%.1fs",
                trace.request_id, trace.backend, time.monotonic() - trace.started)
        finally:
            for task in (watcher, app_task):
                if not task.done():
                    task.cancel()
            await asyncio.gather(watcher, app_task, return_exceptions=True)
