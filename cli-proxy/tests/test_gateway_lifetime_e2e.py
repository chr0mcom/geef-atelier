"""Corner cases of the CLI lifetime against a real uvicorn server with one slot per CLI.

The server runs with ``gc.disable()``: a CLI or a slot that is only released because the
garbage collector happens to finalise an abandoned generator stays visible as a leak.

Every case runs once per event loop (``--loop asyncio`` and ``--loop uvloop``): the standard
loop reports the end of a subprocess only after all its pipes are disconnected, uvloop does not.

* a streaming caller that stops reading and then disconnects (back pressure);
* a CLI that has exited while a helper keeps its pipes open;
* the caller's limit while the request waits for a CLI slot;
* the caller's limit and the health picture of real faults;
* the caller's limit on the image endpoint.
"""
from __future__ import annotations

import os
import socket
import subprocess
import sys
import time

import httpx
import pytest

import fake_cli
from test_disconnect_e2e import (
    SRC, _backends, _chat, _free_port, _healthy, _inflight_zero, _raw_request, _wait_started,
)

_SERVER = ("import gc, sys, uvicorn; gc.disable(); "
           "uvicorn.run('main:app', host='127.0.0.1', port=int(sys.argv[1]), loop=sys.argv[2])")
LIMIT = "x-cli-proxy-timeout-seconds"


@pytest.fixture(scope="module", params=["asyncio", "uvloop"])
def server(request, tmp_path_factory):
    if request.param == "uvloop":
        pytest.importorskip("uvloop")
    tmp = tmp_path_factory.mktemp(f"gateway-one-slot-{request.param}")
    bin_dir, fake_dir = tmp / "bin", tmp / "runs"
    fake_dir.mkdir()
    fake_cli.install(bin_dir)
    for name in ("claude", "codex", "ws"):
        (tmp / name).mkdir()
    port = _free_port()
    log_path = tmp / "server.log"
    env = {
        **os.environ,
        "PATH": f"{bin_dir}:{os.environ['PATH']}",
        "PYTHONPATH": str(SRC),
        "FAKE_DIR": str(fake_dir),
        "CLAUDE_HOME": str(tmp / "claude"),
        "CLAUDE_HOME_ALT": str(tmp / "no-alt"),
        "CODEX_HOME": str(tmp / "codex"),
        "WORKSPACE_ROOT": str(tmp / "ws"),
        "CLAUDE_MAX_CONCURRENT": "1",
        "CODEX_MAX_CONCURRENT": "1",
        "CODEX_IMAGE_MAX_CONCURRENT": "1",
        "FAKE_SLOW_SECONDS": "0",
        "ATELIER_BACKEND_URL": "http://127.0.0.1:9",
        "MODEL_WARMUP_INTERVAL": "86400",
        "CLI_PROXY_API_KEYS": "",
    }
    env.pop("FAKE_TOKEN", None)
    with open(log_path, "w", encoding="utf-8") as log:
        proc = subprocess.Popen(
            [sys.executable, "-c", _SERVER, str(port), request.param],
            cwd=str(SRC), env=env, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT)
    base = f"http://127.0.0.1:{port}"
    try:
        assert fake_cli.wait_for(lambda: _healthy(base), seconds=30), log_path.read_text()
        yield {"base": base, "port": port, "dir": fake_dir, "log": log_path}
    finally:
        fake_cli.kill_leftovers(fake_dir)
        proc.terminate()
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait()


@pytest.fixture(autouse=True)
def _no_leftovers(server):
    """A failing test must not occupy the only slot for the next one."""
    yield
    fake_cli.kill_leftovers(server["dir"])


def _path(backend: str) -> str:
    return f"/v1/{backend}/chat/completions"


def _body(backend: str, token: str, mode: str, **extra) -> dict:
    return _chat(token, mode, model="gpt-5" if backend == "codex" else "claude-sonnet-latest", **extra)


def _slot_is_free(server, backend: str, token: str) -> None:
    response = httpx.post(server["base"] + _path(backend), json=_body(backend, token, "SLOW"),
                          headers={"x-cli-proxy-failover": "off"}, timeout=10)
    assert response.status_code == 200, response.text


def _gone(*pids: int) -> bool:
    return fake_cli.wait_for(lambda: not any(fake_cli.alive(p) for p in pids), 10)


def _stalled_reader(port: int, path: str, body: dict, request_id: str) -> socket.socket:
    """A caller with a tiny receive buffer that never reads."""
    import json

    payload = json.dumps(body).encode()
    sock = socket.socket()
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_RCVBUF, 4096)
    sock.connect(("127.0.0.1", port))
    head = (f"POST {path} HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/json\r\n"
            f"Content-Length: {len(payload)}\r\nx-request-id: {request_id}\r\n\r\n")
    sock.sendall(head.encode() + payload)
    return sock


@pytest.mark.parametrize("backend", ["claude", "codex"])
def test_a_caller_that_stops_reading_and_disconnects_frees_cli_and_slot(server, backend):
    token = f"stalled-{backend}"
    sock = _stalled_reader(server["port"], _path(backend),
                           _body(backend, token, "FLOOD", stream=True), f"rid-{token}")
    leader, grandchild = _wait_started(server["dir"], token)
    time.sleep(3)
    assert fake_cli.alive(leader), "the stream should be stuck on back pressure"

    sock.close()

    assert _gone(leader, grandchild), "CLI still running after the stalled caller disconnected"
    _slot_is_free(server, backend, f"after-{token}")
    assert fake_cli.starts(server["dir"], token) == [leader]
    assert fake_cli.wait_for(lambda: _inflight_zero(server["base"]), 5)


@pytest.mark.parametrize("backend", ["claude", "codex"])
def test_a_slow_reader_that_stays_connected_gets_the_whole_stream(server, backend):
    token = f"whole-{backend}"
    with httpx.Client(base_url=server["base"], timeout=30) as client:
        with client.stream("POST", _path(backend), json=_body(backend, token, "SLOW", stream=True)) as response:
            text = "".join(response.iter_text())

    assert response.status_code == 200 and text.rstrip().endswith("data: [DONE]"), text
    assert '"error"' not in text


@pytest.mark.parametrize("backend, stream", [("codex", False), ("claude", True), ("codex", True)])
def test_a_disconnect_ends_the_helper_of_a_cli_that_has_already_exited(server, backend, stream):
    token = f"orphan-{backend}-{stream}"
    sock = _raw_request(server["port"], _path(backend),
                        _body(backend, token, "ORPHAN", stream=stream), f"rid-{token}")
    leader, grandchild = _wait_started(server["dir"], token)
    assert _gone(leader)
    time.sleep(0.5)
    assert fake_cli.alive(grandchild)

    sock.close()

    assert _gone(grandchild), "the helper holding the pipes survived the disconnect"
    _slot_is_free(server, backend, f"after-{token}")


@pytest.mark.parametrize("backend, extra", [
    ("codex", {}),
    ("claude", {}),
    ("claude", {"stream": True}),
    ("codex", {"stream": True}),
    ("claude", {"response_format": {"type": "json_object"}}),
    ("codex", {"document_mode": True, "document": "draft"}),
    ("claude", {"document_mode": True, "document": "draft"}),
    ("codex", {"tools": [{"type": "function", "function": {"name": "f", "parameters": {"type": "object"}}}]}),
])
def test_the_callers_limit_applies_while_waiting_for_a_slot(server, backend, extra):
    token = f"busy-{backend}-{'-'.join(extra) or 'plain'}"
    holder = _raw_request(server["port"], _path(backend), _body(backend, token, "HANG"), f"rid-{token}")
    try:
        leader, _grandchild = _wait_started(server["dir"], token)
        started = time.monotonic()

        response = httpx.post(server["base"] + _path(backend),
                              json=_body(backend, f"queued-{token}", "HANG", **extra),
                              headers={LIMIT: "2"}, timeout=10)

        assert 1.5 < time.monotonic() - started < 6
        if extra.get("stream"):
            assert "caller's limit of 2 s" in response.text and "[DONE]" in response.text
        else:
            assert response.status_code == 504, response.text
            assert response.json()["error"]["code"] == "cli_timeout"
        assert fake_cli.starts(server["dir"], f"queued-{token}") == []
        assert fake_cli.alive(leader), "the call holding the slot must not be touched"
    finally:
        holder.close()
    assert _gone(leader)
    _slot_is_free(server, backend, f"after-{token}")


def test_the_callers_limit_leaves_the_picture_of_a_real_fault_alone(server):
    failed = httpx.post(server["base"] + _path("codex"), json={
        "model": "gpt-5", "messages": [{"role": "user", "content": "no marker, the CLI fails"}]},
        headers={"x-cli-proxy-failover": "off"}, timeout=20)
    assert failed.status_code == 502
    before = _backends(server["base"])["codex"]
    assert before["last_reason"] == "transport"

    response = httpx.post(server["base"] + _path("codex"), json=_body("codex", "keep-picture", "HANG"),
                          headers={LIMIT: "1"}, timeout=20)

    assert response.status_code == 504
    after = _backends(server["base"])["codex"]
    for key in ("last_reason", "last_error", "last_error_detail", "last_failure_at",
                "consecutive_failures", "open_until", "healthy"):
        assert after[key] == before[key], key
    assert after["caller_timeouts"] == before["caller_timeouts"] + 1
    assert after["last_caller_timeout_at"] >= before["last_failure_at"]
    assert "caller's limit of 1 s" in after["last_caller_timeout_error"]
    _slot_is_free(server, "codex", "reset-breaker")


IMAGES = "/v1/codex/images/generations"


def test_the_image_endpoint_rejects_an_invalid_limit(server):
    response = httpx.post(server["base"] + IMAGES, json={"prompt": "HANG-MARK:img-bad"},
                          headers={LIMIT: "abc"}, timeout=5)

    assert response.status_code == 400
    assert response.json()["error"]["code"] == "invalid_header"
    assert fake_cli.starts(server["dir"], "img-bad") == []


def test_the_image_endpoint_applies_the_callers_limit_while_running_and_while_queued(server):
    response = httpx.post(server["base"] + IMAGES, json={"prompt": "HANG-MARK:img-run"},
                          headers={LIMIT: "1"}, timeout=10)

    assert response.status_code == 504, response.text
    assert response.json()["error"]["code"] == "cli_timeout"
    assert response.headers["x-cli-proxy-provider"] == "codex"
    assert _gone(*_wait_started(server["dir"], "img-run"))

    holder = _raw_request(server["port"], IMAGES, {"prompt": "HANG-MARK:img-holder"}, "rid-img-holder")
    try:
        leader, _grandchild = _wait_started(server["dir"], "img-holder")
        queued = httpx.post(server["base"] + IMAGES, json={"prompt": "HANG-MARK:img-queued"},
                            headers={LIMIT: "1"}, timeout=10)
        assert queued.status_code == 504, queued.text
        assert fake_cli.starts(server["dir"], "img-queued") == []
        assert fake_cli.alive(leader)
    finally:
        holder.close()
    assert _gone(leader)
    _slot_is_free(server, "codex", "after-images")
