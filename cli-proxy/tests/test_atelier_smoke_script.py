"""Tests für scripts/atelier-smoke.sh (Betrieb: docs/ops/atelier-smoke.md).

Offline: Statt des Ateliers antwortet ein kleiner HTTP-Testserver (Login-Formular, OAuth-Freigabe, Token, MCP);
docker ist eine Attrappe. Es wird kein echtes Konto, kein echter Lauf und kein echtes Docker benutzt.
"""
import base64
import hashlib
import json
import os
import stat
import subprocess
import textwrap
import threading
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import pytest

SCRIPT = Path(os.environ.get("SMOKE_SCRIPT") or Path(__file__).resolve().parents[2] / "scripts" / "atelier-smoke.sh")
PASSWORD = "pw-SECRET-0123456789"

pytestmark = pytest.mark.skipif(not SCRIPT.exists(), reason="scripts/atelier-smoke.sh fehlt (Test läuft nur auf dem Host)")

DOCKER_FAKE = r"""
    #!/usr/bin/env bash
    printf '%s\n' "$*" >> "$FAKE_DIR/docker.argv"
    [[ -n ${FAKE_HEALTH_FAIL:-} ]] && { echo "curl: (7) Failed to connect" >&2; exit 7; }
    printf '%s' "${FAKE_HEALTH_BODY:-Healthy}"
"""


class FakeAtelier(BaseHTTPRequestHandler):
    scenario: dict = {}

    def log_message(self, *args):
        pass

    def _send(self, code, body="", headers=None, ctype="text/html"):
        data = body.encode()
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        self.end_headers()
        self.wfile.write(data)

    def _form(self):
        length = int(self.headers.get("Content-Length", 0))
        return {k: v[0] for k, v in urllib.parse.parse_qs(self.rfile.read(length).decode()).items()}

    def do_GET(self):
        if self.path.startswith("/login"):
            self._send(200, '<form><input type="hidden" name="__RequestVerificationToken" value="af-123" /></form>',
                       {"Set-Cookie": "af=1; Path=/"})
        else:
            self._send(404)

    def do_POST(self):
        s = self.scenario
        if self.path == "/login":
            f = self._form()
            ok = (f.get("_handler") == "login-form" and f.get("__RequestVerificationToken") == "af-123"
                  and f.get("Model.Username") == "smoke" and f.get("Model.Password") == PASSWORD
                  and "af=1" in (self.headers.get("Cookie") or ""))
            if ok:
                self._send(302, "", {"Location": "/workshop", "Set-Cookie": "Atelier.Auth=cookie-1; Path=/; HttpOnly"})
            else:
                self._send(200, "Invalid credentials")
        elif self.path == "/oauth/consent":
            f = self._form()
            if "Atelier.Auth=cookie-1" not in (self.headers.get("Cookie") or "") or f.get("client_id") != "client-1":
                self._send(302, "", {"Location": f"{f.get('redirect_uri')}?error=access_denied"})
                return
            s["challenge"] = f["code_challenge"]
            state = "falsch" if s.get("bad_state") else f["state"]
            self._send(302, "", {"Location": f"{f['redirect_uri']}?code=code-1&state={urllib.parse.quote(state)}"})
        elif self.path == "/oauth/token":
            f = self._form()
            expected = base64.urlsafe_b64encode(hashlib.sha256(f.get("code_verifier", "").encode()).digest()).rstrip(b"=").decode()
            if f.get("code") == "code-1" and expected == s.get("challenge"):
                self._send(200, json.dumps({"access_token": "tok-SECRET", "token_type": "Bearer"}), ctype="application/json")
            else:
                self._send(400, json.dumps({"error": "invalid_grant"}), ctype="application/json")
        elif self.path == "/mcp":
            if self.headers.get("Authorization") != "Bearer tok-SECRET":
                self._send(401)
                return
            length = int(self.headers.get("Content-Length", 0))
            msg = json.loads(self.rfile.read(length))
            s.setdefault("calls", []).append(msg)
            if "id" not in msg:
                self._send(202)
                return
            if msg["method"] == "initialize":
                res = {"protocolVersion": "2025-06-18", "capabilities": {}, "serverInfo": {"name": "fake"}}
            else:
                name, args = msg["params"]["name"], msg["params"]["arguments"]
                if name == "submit_request":
                    s["submitted"] = args
                    payload = {"runId": "run-1", "status": "Pending", "errorMessage": None}
                elif name == "get_run_status":
                    s["polls"] = s.get("polls", 0) + 1
                    done = s["polls"] >= 2
                    payload = {"runId": "run-1", "status": s.get("final_status", "Completed") if done and not s.get("hang") else "Running", "errorMessage": None}
                else:
                    payload = {"runId": "run-1", "status": s.get("final_status", "Completed"),
                               "finalText": s.get("final_text", "Der Smoke-Test läuft."), "errorMessage": s.get("error")}
                res = {"content": [{"type": "text", "text": json.dumps(payload)}], "isError": False}
            body = "event: message\ndata: " + json.dumps({"jsonrpc": "2.0", "id": msg["id"], "result": res}) + "\n\n"
            self._send(200, body, ctype="text/event-stream")
        else:
            self._send(404)


@pytest.fixture
def server():
    FakeAtelier.scenario = {}
    httpd = ThreadingHTTPServer(("127.0.0.1", 0), FakeAtelier)
    t = threading.Thread(target=httpd.serve_forever, daemon=True)
    t.start()
    yield httpd, FakeAtelier.scenario
    httpd.shutdown()


@pytest.fixture
def sandbox(tmp_path):
    bin_dir = tmp_path / "bin"
    bin_dir.mkdir()
    fake = bin_dir / "docker"
    fake.write_text(textwrap.dedent(DOCKER_FAKE).lstrip("\n"))
    fake.chmod(fake.stat().st_mode | stat.S_IXUSR)
    return tmp_path


def write_env(path: Path, base: str | None = None, mode: int = 0o600, **overrides: str) -> Path:
    values = {"ATELIER_SMOKE_USER": "smoke", "ATELIER_SMOKE_PASSWORD": PASSWORD, "ATELIER_SMOKE_CLIENT_ID": "client-1",
              "ATELIER_SMOKE_POLL": "1"}
    if base:
        values["ATELIER_SMOKE_BASE_URL"] = base
    values.update(overrides)
    path.write_text("# Smoke\n" + "".join(f"{k}={v}\n" for k, v in values.items() if v is not None))
    path.chmod(mode)
    return path


def run(sandbox: Path, env_file: Path, **extra: str) -> subprocess.CompletedProcess:
    env = {"PATH": f"{sandbox / 'bin'}:/usr/bin:/bin", "FAKE_DIR": str(sandbox), "ATELIER_SMOKE_ENV": str(env_file)}
    env.update(extra)
    return subprocess.run(["bash", str(SCRIPT)], env=env, capture_output=True, text=True, timeout=60, cwd=sandbox)


def test_syntax():
    assert subprocess.run(["bash", "-n", str(SCRIPT)]).returncode == 0


def test_missing_account_file_explains_and_exits_2(sandbox):
    r = run(sandbox, sandbox / "fehlt.env")
    assert r.returncode == 2
    assert r.stdout.startswith("SMOKE NICHT EINGERICHTET") and "docs/ops/atelier-smoke.md" in r.stdout
    assert "curl -fsS -m 10 http://localhost:8080/health" in (sandbox / "docker.argv").read_text()


def test_world_readable_account_file_is_refused(sandbox):
    r = run(sandbox, write_env(sandbox / "s.env", mode=0o644))
    assert r.returncode == 2
    assert "Modus 644" in r.stdout
    assert PASSWORD not in r.stdout + r.stderr


def test_incomplete_account_file_names_missing_keys_only(sandbox):
    r = run(sandbox, write_env(sandbox / "s.env", ATELIER_SMOKE_CLIENT_ID=None))
    assert r.returncode == 2
    assert "ATELIER_SMOKE_CLIENT_ID" in r.stdout
    assert PASSWORD not in r.stdout + r.stderr


@pytest.mark.parametrize(("extra", "expected"), [({"FAKE_HEALTH_FAIL": "1"}, "nicht erreichbar"), ({"FAKE_HEALTH_BODY": "Unhealthy"}, "Unhealthy")])
def test_health_failure_exits_3(sandbox, extra, expected):
    r = run(sandbox, write_env(sandbox / "s.env"), **extra)
    assert r.returncode == 3
    assert "FEHLER (health)" in r.stdout and expected in r.stdout


def test_unreachable_atelier_exits_3(sandbox):
    r = run(sandbox, write_env(sandbox / "s.env", base="http://127.0.0.1:9"))
    assert r.returncode == 3
    assert "FEHLER (verbindung)" in r.stdout


def test_happy_path_one_line_and_no_secrets(sandbox, server):
    httpd, scenario = server
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}"))
    assert r.returncode == 0, r.stdout + r.stderr
    lines = r.stdout.strip().splitlines()
    assert len(lines) == 1 and lines[0].startswith("SMOKE OK: Lauf run-1 Completed")
    assert scenario["submitted"]["crewTemplate"] == "klassik"
    assert len(scenario["submitted"]["briefingText"]) < 200
    assert [c["method"] for c in scenario["calls"]][:2] == ["initialize", "notifications/initialized"]
    for secret in (PASSWORD, "tok-SECRET", "cookie-1"):
        assert secret not in r.stdout + r.stderr


def test_wrong_password_exits_3(sandbox, server):
    httpd, _ = server
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}", ATELIER_SMOKE_PASSWORD="falsch"))
    assert r.returncode == 3
    assert "Login abgelehnt" in r.stdout


def test_unknown_client_exits_3(sandbox, server):
    httpd, _ = server
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}", ATELIER_SMOKE_CLIENT_ID="anderer"))
    assert r.returncode == 3
    assert "OAuth-Freigabe gescheitert (access_denied)" in r.stdout


def test_state_mismatch_exits_3(sandbox, server):
    httpd, scenario = server
    scenario["bad_state"] = True
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}"))
    assert r.returncode == 3
    assert "falschem state" in r.stdout


@pytest.mark.parametrize(
    ("scenario_update", "expected"),
    [
        ({"final_status": "Failed", "error": "Did not converge"}, "Status Failed"),
        ({"final_text": ""}, "kein Text"),
        ({"error": "Finalizer kaputt"}, "Fehlermeldung: Finalizer kaputt"),
    ],
)
def test_bad_run_exits_1(sandbox, server, scenario_update, expected):
    httpd, scenario = server
    scenario.update(scenario_update)
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}"))
    assert r.returncode == 1, r.stdout
    assert "FEHLER (lauf)" in r.stdout and expected in r.stdout


def test_timeout_exits_1(sandbox, server):
    httpd, scenario = server
    scenario["hang"] = True
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}", ATELIER_SMOKE_TIMEOUT="2"))
    assert r.returncode == 1
    assert "FEHLER (zeitlimit)" in r.stdout
