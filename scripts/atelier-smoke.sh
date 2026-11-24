#!/usr/bin/env bash
# Mini-Lauf nach jedem Deploy von Geef.Atelier (Doku: docs/ops/atelier-smoke.md).
#
# Ablauf:
#   1. GET /health der Web-App lokal im Container (docker exec $WEB_CONTAINER curl http://localhost:8080/health).
#   2. Anmeldung mit dem Smoke-Konto: Login-Formular (Cookie), dann OAuth-Freigabe mit PKCE für den registrierten
#      Smoke-Client und Tausch gegen ein Zugriffstoken. Zugangsdaten nur aus der Datei $ATELIER_SMOKE_ENV
#      (Standard /srv/docker/websites/geef_atelier/.smoke.env, Modus 0600, gitignored). Werte werden nie ausgegeben.
#   3. Auftrag über den MCP-Endpunkt (/mcp, Werkzeug submit_request): Ein-Satz-Briefing mit dem Klassik-Template.
#   4. Warten auf das Ende (get_run_status), höchstens ATELIER_SMOKE_TIMEOUT Sekunden.
#   5. Prüfen (get_run_result): Status Completed, Text vorhanden, keine Fehlermeldung.
#   6. Eine Ergebniszeile auf stdout und Exit-Code.
#
# Exit-Code: 0 = bestanden, 1 = Lauf gescheitert oder Zeitlimit, 2 = kein oder unvollständiges Smoke-Konto (nichts geprüft
#            außer /health), 3 = /health, Anmeldung oder MCP-Aufruf gescheitert.
# Variablen (Umgebung schlägt Datei): ATELIER_SMOKE_ENV, ATELIER_SMOKE_BASE_URL, ATELIER_SMOKE_TEMPLATE,
#            ATELIER_SMOKE_TIMEOUT, ATELIER_SMOKE_POLL, ATELIER_SMOKE_REDIRECT_URI, ATELIER_SMOKE_SKIP_HEALTH=1 (nur Tests),
#            ATELIER_SMOKE_VERBOSE=1 (Zwischenschritte auf stderr), WEB_CONTAINER.
# Tests: cli-proxy/tests/test_atelier_smoke_script.py (Attrappe für docker, eigener HTTP-Testserver statt Atelier).

set -uo pipefail

ATELIER_SMOKE_ENV=${ATELIER_SMOKE_ENV:-/srv/docker/websites/geef_atelier/.smoke.env}
WEB_CONTAINER=${WEB_CONTAINER:-geef-atelier-web}

result() { echo "SMOKE $*"; }

if [[ ${ATELIER_SMOKE_SKIP_HEALTH:-0} != 1 ]]; then
  if ! health=$(docker exec "$WEB_CONTAINER" curl -fsS -m 10 http://localhost:8080/health 2>&1); then
    result "FEHLER (health): /health in $WEB_CONTAINER nicht erreichbar: $(head -1 <<< "$health" | cut -c1-200)"
    exit 3
  fi
  if [[ $health != Healthy* ]]; then
    result "FEHLER (health): /health meldet '$(head -1 <<< "$health" | cut -c1-80)'"
    exit 3
  fi
fi

if [[ ! -f $ATELIER_SMOKE_ENV ]]; then
  result "NICHT EINGERICHTET: Smoke-Konto fehlt ($ATELIER_SMOKE_ENV nicht vorhanden). /health ist ok; Einrichtung siehe docs/ops/atelier-smoke.md."
  exit 2
fi
mode=$(stat -c %a "$ATELIER_SMOKE_ENV" 2>/dev/null)
if [[ $mode != 600 && $mode != 400 ]]; then
  result "NICHT EINGERICHTET: $ATELIER_SMOKE_ENV hat Modus ${mode:-?}, erwartet 600 (chmod 600 $ATELIER_SMOKE_ENV)."
  exit 2
fi

exec python3 -I - "$ATELIER_SMOKE_ENV" <<'PY'
import base64
import hashlib
import http.cookiejar
import json
import os
import re
import secrets
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

ENV_FILE = sys.argv[1]
REQUIRED = ("ATELIER_SMOKE_USER", "ATELIER_SMOKE_PASSWORD", "ATELIER_SMOKE_CLIENT_ID")
BRIEFING = "Schreibe genau einen kurzen deutschen Satz, der bestätigt, dass der Atelier-Smoke-Test läuft."
VERBOSE = os.environ.get("ATELIER_SMOKE_VERBOSE") == "1"


def result(text, code):
    print(f"SMOKE {text}")
    sys.exit(code)


def step(text):
    if VERBOSE:
        print(f"[smoke] {text}", file=sys.stderr)


def load_settings(path):
    values = {}
    with open(path, encoding="utf-8") as fh:
        for raw in fh:
            line = raw.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, value = line.split("=", 1)
            key = key.strip().removeprefix("export ").strip()
            value = value.strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in "'\"":
                value = value[1:-1]
            values[key] = value
    for key, value in os.environ.items():
        if key.startswith("ATELIER_SMOKE_") and key not in ("ATELIER_SMOKE_ENV", *REQUIRED):
            values[key] = value
    return values


cfg = load_settings(ENV_FILE)
missing = [k for k in REQUIRED if not cfg.get(k)]
if missing:
    result(f"NICHT EINGERICHTET: in {ENV_FILE} fehlen {', '.join(missing)} (siehe docs/ops/atelier-smoke.md).", 2)

BASE = cfg.get("ATELIER_SMOKE_BASE_URL", "https://geef.stefan-bechtel.de").rstrip("/")
TEMPLATE = cfg.get("ATELIER_SMOKE_TEMPLATE", "klassik")
REDIRECT_URI = cfg.get("ATELIER_SMOKE_REDIRECT_URI", "http://127.0.0.1/atelier-smoke/callback")
try:
    TIMEOUT = int(cfg.get("ATELIER_SMOKE_TIMEOUT", "1500"))
    POLL = max(1, int(cfg.get("ATELIER_SMOKE_POLL", "10")))
except ValueError:
    result("NICHT EINGERICHTET: ATELIER_SMOKE_TIMEOUT und ATELIER_SMOKE_POLL müssen ganze Zahlen sein.", 2)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


jar = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar), NoRedirect())


def call(method, path, form=None, body=None, headers=None, timeout=60):
    data = None
    hdrs = dict(headers or {})
    if form is not None:
        data = urllib.parse.urlencode(form).encode()
        hdrs["Content-Type"] = "application/x-www-form-urlencoded"
    elif body is not None:
        data = json.dumps(body).encode()
        hdrs["Content-Type"] = "application/json"
    req = urllib.request.Request(BASE + path, data=data, method=method, headers=hdrs)
    try:
        with opener.open(req, timeout=timeout) as resp:
            return resp.status, resp.headers, resp.read().decode("utf-8", "replace")
    except urllib.error.HTTPError as err:
        return err.code, err.headers, err.read().decode("utf-8", "replace")
    except (urllib.error.URLError, TimeoutError, OSError) as err:
        reason = getattr(err, "reason", err)
        result(f"FEHLER (verbindung): {method} {path} nicht möglich: {reason}", 3)


def login():
    status, _, page = call("GET", "/login")
    if status != 200:
        result(f"FEHLER (anmeldung): GET /login lieferte HTTP {status}", 3)
    token = None
    for tag in re.findall(r"<input[^>]*>", page):
        if 'name="__RequestVerificationToken"' in tag:
            match = re.search(r'value="([^"]*)"', tag)
            token = match.group(1) if match else None
    if not token:
        result("FEHLER (anmeldung): Login-Seite ohne Antiforgery-Feld", 3)
    status, headers, _ = call("POST", "/login", form={
        "_handler": "login-form",
        "__RequestVerificationToken": token,
        "Model.Username": cfg["ATELIER_SMOKE_USER"],
        "Model.Password": cfg["ATELIER_SMOKE_PASSWORD"],
    })
    if status not in (301, 302, 303) or not any(c.name == "Atelier.Auth" for c in jar):
        result(f"FEHLER (anmeldung): Login abgelehnt (HTTP {status}); Konto, Passwort und Aktiv-Status prüfen", 3)
    step("Login ok")


def access_token():
    verifier = secrets.token_urlsafe(48)
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode()
    state = secrets.token_urlsafe(16)
    status, headers, _ = call("POST", "/oauth/consent", form={
        "action": "approve",
        "response_type": "code",
        "client_id": cfg["ATELIER_SMOKE_CLIENT_ID"],
        "redirect_uri": REDIRECT_URI,
        "scope": "mcp",
        "state": state,
        "code_challenge": challenge,
        "code_challenge_method": "S256",
    })
    location = headers.get("Location", "") if headers else ""
    query = urllib.parse.parse_qs(urllib.parse.urlparse(location).query)
    if status not in (301, 302, 303) or "code" not in query:
        detail = query.get("error", [f"HTTP {status}"])[0]
        result(f"FEHLER (anmeldung): OAuth-Freigabe gescheitert ({detail}); Client-ID und Redirect-URI prüfen", 3)
    if query.get("state", [""])[0] != state:
        result("FEHLER (anmeldung): OAuth-Freigabe mit falschem state", 3)
    status, _, text = call("POST", "/oauth/token", form={
        "grant_type": "authorization_code",
        "code": query["code"][0],
        "client_id": cfg["ATELIER_SMOKE_CLIENT_ID"],
        "redirect_uri": REDIRECT_URI,
        "code_verifier": verifier,
    })
    try:
        token = json.loads(text).get("access_token") if status == 200 else None
    except ValueError:
        token = None
    if not token:
        result(f"FEHLER (anmeldung): Token-Tausch gescheitert (HTTP {status})", 3)
    step("Zugriffstoken erhalten")
    return token


class Mcp:
    def __init__(self, token):
        self.headers = {"Authorization": f"Bearer {token}", "Accept": "application/json, text/event-stream"}
        self.next_id = 1

    def _post(self, payload):
        status, headers, text = call("POST", "/mcp", body=payload, headers=self.headers, timeout=120)
        return status, headers, text

    def rpc(self, method, params):
        payload = {"jsonrpc": "2.0", "id": self.next_id, "method": method, "params": params}
        self.next_id += 1
        status, headers, text = self._post(payload)
        if status != 200:
            result(f"FEHLER (mcp): {method} lieferte HTTP {status}", 3)
        message = None
        if "text/event-stream" in (headers.get("Content-Type", "") if headers else ""):
            for line in text.splitlines():
                if line.startswith("data:"):
                    try:
                        candidate = json.loads(line[5:].strip())
                    except ValueError:
                        continue
                    if candidate.get("id") == payload["id"]:
                        message = candidate
        else:
            try:
                message = json.loads(text)
            except ValueError:
                message = None
        if not isinstance(message, dict):
            result(f"FEHLER (mcp): {method} ohne lesbare Antwort", 3)
        if "error" in message:
            result(f"FEHLER (mcp): {method}: {str(message['error'].get('message', ''))[:200]}", 3)
        return message.get("result") or {}

    def notify(self, method):
        self._post({"jsonrpc": "2.0", "method": method})

    def tool(self, name, arguments):
        res = self.rpc("tools/call", {"name": name, "arguments": arguments})
        if res.get("isError"):
            text = " ".join(c.get("text", "") for c in res.get("content", []) if isinstance(c, dict))
            result(f"FEHLER (mcp): {name}: {text[:200]}", 3)
        data = res.get("structuredContent")
        if isinstance(data, dict) and "result" in data and len(data) == 1:
            data = data["result"]
        if not isinstance(data, dict):
            data = None
            for c in res.get("content", []):
                if isinstance(c, dict) and c.get("type") == "text":
                    try:
                        data = json.loads(c["text"])
                    except ValueError:
                        pass
        if not isinstance(data, dict):
            return {}
        return {k.lower(): v for k, v in data.items()}


login()
mcp = Mcp(access_token())
init = mcp.rpc("initialize", {
    "protocolVersion": "2025-06-18",
    "capabilities": {},
    "clientInfo": {"name": "atelier-smoke", "version": "1"},
})
if init.get("protocolVersion"):
    mcp.headers["MCP-Protocol-Version"] = init["protocolVersion"]
mcp.notify("notifications/initialized")

started = time.monotonic()
submitted = mcp.tool("submit_request", {"briefingText": BRIEFING, "crewTemplate": TEMPLATE})
run_id = submitted.get("runid")
if not run_id:
    result("FEHLER (mcp): submit_request ohne Lauf-ID", 3)
step(f"Lauf {run_id} eingereicht")

status = ""
while True:
    current = mcp.tool("get_run_status", {"runId": run_id})
    status = str(current.get("status") or "")
    if status in ("Completed", "Failed", "Aborted"):
        break
    if time.monotonic() - started > TIMEOUT:
        result(f"FEHLER (zeitlimit): Lauf {run_id} nach {TIMEOUT} s noch im Status {status or '?'}", 1)
    step(f"Status {status or '?'}")
    time.sleep(POLL)

final = mcp.tool("get_run_result", {"runId": run_id})
elapsed = int(time.monotonic() - started)
text = (final.get("finaltext") or "").strip()
error = (final.get("errormessage") or "").strip()
problems = []
if final.get("status", status) != "Completed":
    problems.append(f"Status {final.get('status', status)}")
if not text:
    problems.append("kein Text")
if error:
    problems.append(f"Fehlermeldung: {error[:160]}")
if problems:
    result(f"FEHLER (lauf): Lauf {run_id} nach {elapsed} s: {'; '.join(problems)}", 1)
result(f"OK: Lauf {run_id} Completed in {elapsed} s, {len(text)} Zeichen Text ({TEMPLATE})", 0)
PY
