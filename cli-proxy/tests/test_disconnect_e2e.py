"""The gateway against a real uvicorn server, real sockets and a fake CLI with a grandchild.

* A caller that disconnects ends the CLI and its grandchild; nothing else starts for that
  request (no failover, no claude-alt stage, no JSON retry), the breaker does not move, the
  in-flight counter returns to zero, and one log line names backend, duration and request id.
* A caller that stays connected notices nothing: long calls and keep-alive (two calls on one
  connection), plain and streaming, are answered as before.
* The caller's limit (`x-cli-proxy-timeout-seconds`) answers 504 `cli_timeout`, ends the group,
  starts nothing on the partner and never opens the breaker.
"""
from __future__ import annotations

import json
import os
import socket
import subprocess
import sys
import time
from pathlib import Path

import httpx
import pytest

import fake_cli

SRC = Path(__file__).resolve().parent.parent / "src"


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


@pytest.fixture(scope="module")
def server(tmp_path_factory):
    tmp = tmp_path_factory.mktemp("gateway")
    bin_dir, fake_dir = tmp / "bin", tmp / "runs"
    fake_dir.mkdir()
    fake_cli.install(bin_dir)
    alt = tmp / "claude-alt"
    (alt / ".claude").mkdir(parents=True)
    (alt / ".claude" / ".credentials.json").write_text("{}", encoding="utf-8")
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
        "CLAUDE_HOME_ALT": str(alt),
        "CODEX_HOME": str(tmp / "codex"),
        "WORKSPACE_ROOT": str(tmp / "ws"),
        "ATELIER_BACKEND_URL": "http://127.0.0.1:9",
        "MODEL_WARMUP_INTERVAL": "86400",
        "CLI_PROXY_API_KEYS": "",
    }
    env.pop("FAKE_TOKEN", None)
    with open(log_path, "w", encoding="utf-8") as log:
        proc = subprocess.Popen(
            [sys.executable, "-m", "uvicorn", "main:app", "--host", "127.0.0.1", "--port", str(port)],
            cwd=str(SRC), env=env, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT)
    base = f"http://127.0.0.1:{port}"
    try:
        assert fake_cli.wait_for(lambda: _healthy(base), seconds=30), log_path.read_text()
        yield {"base": base, "port": port, "dir": fake_dir, "log": log_path}
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()
        fake_cli.kill_leftovers(fake_dir)


def _healthy(base: str) -> bool:
    try:
        return httpx.get(f"{base}/health", timeout=2).status_code == 200
    except httpx.HTTPError:
        return False


def _backends(base: str) -> dict:
    return httpx.get(f"{base}/health", timeout=5).json()["failover"]["backends"]


def _chat(token: str, mode: str = "HANG", **extra) -> dict:
    return {"model": extra.pop("model", "claude-sonnet-latest"),
            "messages": [{"role": "user", "content": f"Please answer. {mode}-MARK:{token}"}], **extra}


def _raw_request(port: int, path: str, body: dict, request_id: str, headers: dict | None = None) -> socket.socket:
    payload = json.dumps(body).encode()
    lines = [f"POST {path} HTTP/1.1", "Host: 127.0.0.1", "Content-Type: application/json",
             f"Content-Length: {len(payload)}", f"x-request-id: {request_id}"]
    lines += [f"{k}: {v}" for k, v in (headers or {}).items()]
    sock = socket.create_connection(("127.0.0.1", port))
    sock.sendall(("\r\n".join(lines) + "\r\n\r\n").encode() + payload)
    return sock


def _wait_started(fake_dir: Path, token: str) -> tuple[int, int]:
    assert fake_cli.wait_for(lambda: fake_cli.starts(fake_dir, token) and fake_cli.grandchildren(fake_dir, token), 20)
    return fake_cli.starts(fake_dir, token)[0], fake_cli.grandchildren(fake_dir, token)[0]


def _assert_untouched(base: str, *names: str) -> None:
    backends = _backends(base)
    for name in names:
        state = backends[name]
        assert state["consecutive_failures"] == 0 and state["available"] and state["healthy"], (name, state)


def _inflight_zero(base: str) -> bool:
    response = httpx.get(f"{base}/health", timeout=5)
    return response.headers.get("x-ratelimit-remaining-requests") == response.headers.get("x-ratelimit-limit-requests")


@pytest.mark.parametrize("path, body, backend", [
    ("/v1/codex/chat/completions",
     {"model": "gpt-5", "response_format": {"type": "json_object"}}, "codex"),
    ("/v1/claude/chat/completions",
     {"response_format": {"type": "json_object"}}, "claude"),
    ("/v1/claude/chat/completions", {"stream": True}, "claude"),
    ("/v1/codex/chat/completions", {"model": "gpt-5", "stream": True}, "codex"),
])
def test_a_disconnected_caller_ends_the_cli_and_nothing_else_starts(server, path, body, backend):
    token = f"gone-{backend}-{'stream' if body.get('stream') else 'json'}"
    request_id = f"rid-{token}"
    sock = _raw_request(server["port"], path, {**_chat(token), **body}, request_id)
    leader, grandchild = _wait_started(server["dir"], token)

    sock.close()

    assert fake_cli.wait_for(lambda: not fake_cli.alive(leader) and not fake_cli.alive(grandchild), 10), \
        "CLI or grandchild still running after the caller disconnected"
    time.sleep(2)
    assert fake_cli.starts(server["dir"], token) == [leader], "a second CLI run started after the disconnect"
    _assert_untouched(server["base"], "claude", "codex", "claude-alt")
    assert fake_cli.wait_for(lambda: _inflight_zero(server["base"]), 5)
    log = server["log"].read_text()
    line = next(l for l in log.splitlines() if request_id in l and "caller disconnected" in l)
    assert f"backend={backend}" in line and "after=" in line and "MARK" not in line


def test_a_connected_caller_notices_nothing_even_with_keep_alive_and_long_calls(server):
    with httpx.Client(base_url=server["base"], timeout=30) as client:
        first = client.post("/v1/claude/chat/completions", json=_chat("slow-1", "SLOW"))
        second = client.post("/v1/claude/chat/completions", json=_chat("slow-2", "SLOW"))
        with client.stream("POST", "/v1/claude/chat/completions", json=_chat("slow-3", "SLOW", stream=True)) as stream:
            streamed = "".join(stream.iter_text())

    assert first.status_code == 200 and first.json()["choices"][0]["message"]["content"] == "pong"
    assert second.status_code == 200 and second.json()["choices"][0]["message"]["content"] == "pong"
    assert "pong" in streamed and "[DONE]" in streamed
    for token in ("slow-1", "slow-2", "slow-3"):
        assert len(fake_cli.starts(server["dir"], token)) == 1
    assert "caller disconnected" not in "\n".join(
        l for l in server["log"].read_text().splitlines() if "slow-" in l or "rid-" not in l)


def test_the_callers_limit_answers_504_ends_the_cli_and_never_opens_the_breaker(server):
    for round_ in (1, 2, 3):
        token = f"limit-{round_}"
        started = time.monotonic()
        response = httpx.post(f"{server['base']}/v1/codex/chat/completions",
                              json=_chat(token, model="gpt-5"),
                              headers={"x-cli-proxy-timeout-seconds": "1"}, timeout=30)

        assert response.status_code == 504
        assert response.json()["error"]["code"] == "cli_timeout"
        assert time.monotonic() - started < 10
        leader, grandchild = _wait_started(server["dir"], token)
        assert fake_cli.wait_for(lambda: not fake_cli.alive(leader) and not fake_cli.alive(grandchild), 10)
        assert fake_cli.starts(server["dir"], token) == [leader]

    _assert_untouched(server["base"], "codex", "claude", "claude-alt")
    codex = _backends(server["base"])["codex"]
    assert codex["caller_timeouts"] >= 3 and codex["last_caller_timeout_at"]
    assert "caller's limit of 1 s" in codex["last_caller_timeout_error"]
    assert codex["last_reason"] != "caller_timeout"


def test_an_invalid_limit_is_rejected(server):
    response = httpx.post(f"{server['base']}/v1/codex/chat/completions", json=_chat("bad", model="gpt-5"),
                          headers={"x-cli-proxy-timeout-seconds": "soon"}, timeout=10)

    assert response.status_code == 400
    assert response.json()["error"]["code"] == "invalid_header"
    assert fake_cli.starts(server["dir"], "bad") == []
