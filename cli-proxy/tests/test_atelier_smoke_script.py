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


def _outcome(s):
    """Outcome erst, wenn der Status terminal ist und weitere ``outcome_delay`` Abfragen vergangen sind (Abschlussschritte)."""
    if s.get("hang") or s.get("never_outcome"):
        return None
    return s.get("final_outcome", "Converged") if s.get("polls", 0) >= 2 + s.get("outcome_delay", 0) else None


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
        self.scenario.setdefault("requests", []).append(("GET", self.path))
        if self.path.startswith("/login"):
            self._send(200, '<form><input type="hidden" name="__RequestVerificationToken" value="af-123" /></form>',
                       {"Set-Cookie": "af=1; Path=/"})
        else:
            self._send(404)

    def do_POST(self):
        s = self.scenario
        s.setdefault("requests", []).append(("POST", self.path))
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
            if s.get("token_body") is not None:
                self._send(200, s["token_body"], ctype="application/json")
            elif f.get("code") == "code-1" and expected == s.get("challenge"):
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
            if msg["method"] == s.get("raw_for", msg["method"]) and s.get("sse_data") is not None:
                self._send(200, "event: message\ndata: " + s["sse_data"] + "\n\n", ctype="text/event-stream")
                return
            if msg["method"] == s.get("raw_for", msg["method"]) and s.get("plain_body") is not None:
                self._send(200, s["plain_body"], ctype="application/json")
                return
            if msg["method"] == s.get("raw_for", msg["method"]) and "rpc_error" in s:
                body = "data: " + json.dumps({"jsonrpc": "2.0", "id": msg["id"], "error": s["rpc_error"]}) + "\n\n"
                self._send(200, body, ctype="text/event-stream")
                return
            if msg["method"] == s.get("raw_for", msg["method"]) and "rpc_result" in s:
                body = "data: " + json.dumps({"jsonrpc": "2.0", "id": msg["id"], "result": s["rpc_result"]}) + "\n\n"
                self._send(200, body, ctype="text/event-stream")
                return
            if msg["method"] == "initialize":
                res = {"protocolVersion": "2025-06-18", "capabilities": {}, "serverInfo": {"name": "fake"}}
            else:
                name, args = msg["params"]["name"], msg["params"]["arguments"]
                s.setdefault("tools", []).append(name)
                asking = s.get("ask") and "answered" not in s
                if name == "submit_order":
                    s["submitted"] = args
                    payload = {"orderId": "order-1", "state": "needs_input" if asking else "queued",
                               "runId": None if asking else "run-1", "message": "", "sources": []}
                elif name == "answer_order":
                    s["answered"] = args
                    payload = {"orderId": "order-1", "state": "queued", "runId": "run-1", "message": ""}
                elif name == "get_order_status":
                    s.setdefault("waits", []).append(args.get("waitSeconds"))
                    if asking:
                        payload = {"orderId": "order-1", "state": "needs_input", "done": False, "step": "Waiting for answers",
                                   "elapsedSeconds": 1, "warnings": [], "nextCheckSeconds": 0, "outcome": None}
                    else:
                        s["polls"] = s.get("polls", 0) + 1
                        done = s["polls"] >= 2 and not s.get("hang")
                        payload = {"orderId": "order-1", "state": s.get("final_state", "delivered") if done else "working",
                                   "done": done, "step": "Done" if done else "Writing", "elapsedSeconds": 1, "runId": "run-1",
                                   "warnings": [], "nextCheckSeconds": 0 if done else 5, "outcome": _outcome(s) if done else None}
                else:
                    s["result_reads"] = s.get("result_reads", 0) + 1
                    s["result_read_at_poll"] = s.get("polls", 0)
                    final = _outcome(s) is not None
                    payload = {"orderId": "order-1", "state": s.get("final_state", "delivered"),
                               "text": s.get("final_text", "Der Smoke-Test läuft.") if final else "VORLÄUFIG vor dem Abschlussschritt",
                               "note": s.get("note")}
                    if "result_payload" in s:
                        payload = s["result_payload"]
                res = {"content": [{"type": "text", "text": json.dumps(payload)}], "isError": False}
            body = "event: message\ndata: " + json.dumps({"jsonrpc": "2.0", "id": msg["id"], "result": res}) + "\n\n"
            self._send(200, body, ctype="text/event-stream")
        else:
            self._send(404)


@pytest.fixture
def server(request):
    FakeAtelier.scenario = {}
    httpd = ThreadingHTTPServer((getattr(request, "param", "127.0.0.1"), 0), FakeAtelier)
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
    assert len(lines) == 1 and lines[0].startswith("SMOKE OK: Auftrag order-1 Converged")
    assert "offene Punkte" not in lines[0] and "Rückfragen" not in lines[0]
    assert set(scenario["submitted"]) == {"request"}
    assert len(scenario["submitted"]["request"]) < 200
    assert [c["method"] for c in scenario["calls"]][:2] == ["initialize", "notifications/initialized"]
    assert set(scenario["tools"]) == {"submit_order", "get_order_status", "get_order_result"}
    assert set(scenario["waits"]) == {25}
    for secret in (PASSWORD, "tok-SECRET", "cookie-1"):
        assert secret not in r.stdout + r.stderr


def test_questions_are_skipped_once_and_the_order_still_passes(sandbox, server):
    httpd, scenario = server
    scenario["ask"] = True
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}"))
    assert r.returncode == 0, r.stdout + r.stderr
    assert r.stdout.startswith("SMOKE OK: Auftrag order-1 Converged") and "Rückfragen übersprungen" in r.stdout
    assert scenario["answered"] == {"orderId": "order-1", "skip": True}
    assert scenario["tools"].count("answer_order") == 1


def test_no_removed_tool_is_called(sandbox, server):
    httpd, scenario = server
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}"))
    assert r.returncode == 0, r.stdout + r.stderr
    assert not {"submit_request", "get_run_status", "get_run_result"} & set(scenario["tools"])
    assert "klassik" not in SCRIPT.read_text(encoding="utf-8") and "submit_request" not in SCRIPT.read_text(encoding="utf-8")


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
        ({"final_state": "failed", "final_outcome": "Failed", "note": "No text was written."},
         "outcome Failed (Zustand failed); Hinweis: No text was written."),
        ({"final_state": "stopped", "final_outcome": "Aborted"}, "outcome Aborted (Zustand stopped)"),
        ({"final_state": "delivered", "final_outcome": "Failed"}, "outcome Failed (Zustand delivered)"),
        ({"final_text": ""}, "kein Text"),
        ({"final_outcome": "DeliveredWithOpenPoints", "final_state": "delivered_with_open_points", "final_text": ""}, "kein Text"),
    ],
)
def test_bad_run_exits_1(sandbox, server, scenario_update, expected):
    httpd, scenario = server
    scenario.update(scenario_update)
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}"))
    assert r.returncode == 1, r.stdout
    assert "FEHLER (lauf)" in r.stdout and expected in r.stdout


def test_delivered_with_open_points_passes_and_says_so(sandbox, server):
    httpd, scenario = server
    scenario.update(final_outcome="DeliveredWithOpenPoints", final_state="delivered_with_open_points")
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}"))
    assert r.returncode == 0, r.stdout + r.stderr
    assert r.stdout.startswith("SMOKE OK: Auftrag order-1 DeliveredWithOpenPoints")
    assert "Zustand delivered_with_open_points, offene Punkte" in r.stdout


def test_waits_for_outcome_not_for_terminal_status_and_reads_text_once_afterwards(sandbox, server):
    httpd, scenario = server
    scenario["outcome_delay"] = 3
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}"))
    assert r.returncode == 0, r.stdout + r.stderr
    assert scenario["polls"] == 5
    assert scenario["result_reads"] == 1 and scenario["result_read_at_poll"] == 5
    assert f"{len('Der Smoke-Test läuft.')} Zeichen Text" in r.stdout


def test_timeout_exits_1(sandbox, server):
    httpd, scenario = server
    scenario["hang"] = True
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}", ATELIER_SMOKE_TIMEOUT="2"))
    assert r.returncode == 1
    assert "FEHLER (zeitlimit)" in r.stdout and "Zustand working, ohne outcome" in r.stdout


def test_terminal_status_without_outcome_times_out_with_clear_error(sandbox, server):
    httpd, scenario = server
    scenario["never_outcome"] = True
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}", ATELIER_SMOKE_TIMEOUT="2"))
    assert r.returncode == 1, r.stdout
    assert r.stdout.startswith("SMOKE FEHLER (zeitlimit)")
    assert "fertig im Zustand delivered, aber ohne outcome" in r.stdout
    assert scenario.get("result_reads", 0) == 0


# Kein Passwort über unverschlüsseltes HTTP an fremde Hosts (Q15)

@pytest.mark.parametrize("server", ["127.0.0.2"], indirect=True)
def test_http_to_non_allowed_host_is_refused_before_any_request(sandbox, server):
    httpd, scenario = server
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.2:{httpd.server_port}"))
    assert r.returncode == 2, r.stdout + r.stderr
    assert r.stdout.startswith("SMOKE FEHLER (konfiguration)") and "unverschlüsseltes http://" in r.stdout
    assert "127.0.0.2" in r.stdout
    assert scenario.get("requests", []) == []
    assert PASSWORD not in r.stdout + r.stderr


@pytest.mark.parametrize("base", ["http://geef.example.org", "http://10.0.0.5:8080", "http://geef-atelier-web.evil.example",
                                  "ftp://geef.example.org", "geef.example.org", "https://"])
def test_bad_base_urls_exit_2(sandbox, base):
    r = run(sandbox, write_env(sandbox / "s.env", base=base))
    assert r.returncode == 2, r.stdout
    assert r.stdout.startswith("SMOKE FEHLER (konfiguration)")
    assert "Traceback" not in r.stderr


@pytest.mark.parametrize("host", ["localhost", "LOCALHOST", "127.0.0.1"])
def test_http_to_loopback_is_allowed(sandbox, server, host):
    httpd, _ = server
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://{host}:{httpd.server_port}"))
    assert r.returncode == 0, r.stdout + r.stderr


@pytest.mark.parametrize("base", ["http://[::1]:9", "http://geef-atelier-web:8080", "https://geef.invalid"])
def test_allowed_or_https_bases_pass_the_config_check(sandbox, base):
    r = run(sandbox, write_env(sandbox / "s.env", base=base))
    assert r.returncode == 3, r.stdout
    assert "FEHLER (verbindung)" in r.stdout


# Unerwartete Antworten: eigene Ergebniszeile und Exit 4, nie ein Traceback (Q20)

def _assert_unexpected(r, expected):
    assert r.returncode == 4, r.stdout + r.stderr
    lines = r.stdout.strip().splitlines()
    assert len(lines) == 1 and lines[0].startswith("SMOKE FEHLER (antwort)"), r.stdout
    assert expected in lines[0]
    assert "Traceback" not in r.stderr
    for secret in (PASSWORD, "tok-SECRET", "cookie-1"):
        assert secret not in r.stdout + r.stderr


@pytest.mark.parametrize(
    ("body", "expected"),
    [("[]", "kein JSON-Objekt, sondern list"), ("null", "kein JSON-Objekt, sondern NoneType"),
     ("kein json", "kein JSON"), ('{"token_type": "Bearer"}', "ohne access_token"), ('{"access_token": 5}', "ohne access_token")],
)
def test_unexpected_token_response_exits_4(sandbox, server, body, expected):
    httpd, scenario = server
    scenario["token_body"] = body
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}"))
    _assert_unexpected(r, expected)


@pytest.mark.parametrize(
    ("update", "expected"),
    [
        ({"sse_data": "[1, 2]"}, "SSE-Datenzeile der Antwort auf initialize ist kein JSON-Objekt"),
        ({"sse_data": '"text"'}, "SSE-Datenzeile der Antwort auf initialize ist kein JSON-Objekt"),
        ({"plain_body": "[]"}, "Antwort auf initialize ist kein JSON-Objekt"),
        ({"plain_body": "<html>"}, "initialize ohne lesbare Antwort"),
        ({"rpc_result": [1]}, "Feld result der Antwort auf initialize ist kein JSON-Objekt"),
        ({"raw_for": "tools/call", "sse_data": "[]"}, "SSE-Datenzeile der Antwort auf tools/call"),
        ({"result_payload": {"orderId": "order-1", "state": "delivered", "text": 42}},
         "text von get_order_result ist kein Text"),
        ({"final_outcome": "Weird"}, "outcome von get_order_status hat den unbekannten Wert 'Weird'"),
        ({"final_outcome": 3}, "outcome von get_order_status ist kein Text"),
    ],
)
def test_unexpected_mcp_response_exits_4(sandbox, server, update, expected):
    httpd, scenario = server
    scenario.update(update)
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}"))
    _assert_unexpected(r, expected)


def test_string_rpc_error_is_reported_as_mcp_failure(sandbox, server):
    httpd, scenario = server
    scenario["rpc_error"] = "kaputt"
    r = run(sandbox, write_env(sandbox / "s.env", base=f"http://127.0.0.1:{httpd.server_port}"))
    assert r.returncode == 3, r.stdout + r.stderr
    assert "FEHLER (mcp): initialize: kaputt" in r.stdout
    assert "Traceback" not in r.stderr
