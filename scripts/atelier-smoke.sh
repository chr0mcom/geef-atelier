#!/usr/bin/env bash
# Mini-Lauf nach jedem Deploy von Geef.Atelier (Doku: docs/ops/atelier-smoke.md).
#
# Ablauf:
#   1. GET /health der Web-App lokal im Container (docker exec $WEB_CONTAINER curl http://localhost:8080/health).
#   2. Anmeldung mit dem Smoke-Konto: Login-Formular (Cookie), dann OAuth-Freigabe mit PKCE für den registrierten
#      Smoke-Client und Tausch gegen ein Zugriffstoken. Zugangsdaten nur aus der Datei $ATELIER_SMOKE_ENV
#      (Standard /srv/docker/websites/geef_atelier/.smoke.env, Modus 0600, gitignored). Werte werden nie ausgegeben.
#   3. Auftrag über den MCP-Endpunkt (/mcp, Werkzeug submit_order, Standardweg): ein Ein-Satz-Auftrag. Stellt Atelier
#      Rückfragen (state needs_input), überspringt das Skript sie mit answer_order(skip=true).
#   4. Warten mit get_order_status (waitSeconds), bis done gesetzt ist und ein outcome vorliegt (der Text ist erst mit
#      gesetztem outcome endgültig), höchstens ATELIER_SMOKE_TIMEOUT Sekunden.
#   5. Erst dann get_order_result lesen und prüfen: outcome Converged oder DeliveredWithOpenPoints und Text vorhanden
#      ist bestanden; outcome Failed oder Aborted, fehlender Text oder Zeitlimit ohne outcome ist gescheitert.
#   6. Eine Ergebniszeile auf stdout und Exit-Code.
#
# Exit-Code: 0 = bestanden, 1 = Lauf gescheitert oder Zeitlimit, 2 = Smoke-Konto fehlt, ist unvollständig oder unzulässig
#            konfiguriert (nichts geprüft außer /health), 3 = /health, Anmeldung oder MCP-Aufruf gescheitert,
#            4 = unerwartete Antwort des Ateliers (HTTP 200, aber nicht das vereinbarte Format, z. B. JSON-Array statt Objekt).
#            Ein Traceback erscheint nie; auch ein unerwarteter interner Fehler endet mit einer Ergebniszeile und Exit 4.
# Passwortschutz: Unverschlüsseltes http:// ist nur für die Hosts in ALLOWED_HTTP_HOSTS erlaubt (localhost, 127.0.0.1, ::1,
#            geef-atelier-web); jeder andere http-Host bricht vor dem Login mit Exit 2 ab. Weiterleitungen folgt das Skript nie.
# Variablen (Umgebung schlägt Datei): ATELIER_SMOKE_ENV, ATELIER_SMOKE_BASE_URL, ATELIER_SMOKE_TIMEOUT,
#            ATELIER_SMOKE_WAIT (waitSeconds je Statusabfrage, Standard 25), ATELIER_SMOKE_POLL (Pause zwischen zwei
#            Statusabfragen, Standard 2), ATELIER_SMOKE_REDIRECT_URI, ATELIER_SMOKE_SKIP_HEALTH=1 (nur Tests),
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
REQUEST = "Schreibe genau einen kurzen deutschen Satz, der bestätigt, dass der Atelier-Smoke-Test läuft."
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
REDIRECT_URI = cfg.get("ATELIER_SMOKE_REDIRECT_URI", "http://127.0.0.1/atelier-smoke/callback")
ALLOWED_HTTP_HOSTS = ("localhost", "127.0.0.1", "::1", "geef-atelier-web")

try:
    _base = urllib.parse.urlsplit(BASE)
    _host = (_base.hostname or "").lower()
except ValueError:
    _base, _host = None, ""
if _base is None or _base.scheme not in ("https", "http") or not _host:
    result("FEHLER (konfiguration): ATELIER_SMOKE_BASE_URL ist keine gültige https://-Adresse.", 2)
if _base.scheme == "http" and _host not in ALLOWED_HTTP_HOSTS:
    result(f"FEHLER (konfiguration): ATELIER_SMOKE_BASE_URL nutzt unverschlüsseltes http:// für den Host {_host}; "
           f"das Passwort ginge im Klartext über das Netz. http:// ist nur für {', '.join(ALLOWED_HTTP_HOSTS)} erlaubt, "
           "sonst https:// verwenden.", 2)
try:
    TIMEOUT = int(cfg.get("ATELIER_SMOKE_TIMEOUT", "1500"))
    WAIT = min(50, max(0, int(cfg.get("ATELIER_SMOKE_WAIT", "25"))))
    POLL = max(0, int(cfg.get("ATELIER_SMOKE_POLL", "2")))
except ValueError:
    result("NICHT EINGERICHTET: ATELIER_SMOKE_TIMEOUT, ATELIER_SMOKE_WAIT und ATELIER_SMOKE_POLL müssen ganze Zahlen sein.", 2)


class UnexpectedResponse(Exception):
    pass


def as_dict(value, what):
    if not isinstance(value, dict):
        raise UnexpectedResponse(f"{what} ist kein JSON-Objekt, sondern {type(value).__name__}")
    return value


def as_text(value, what):
    if value is None:
        return ""
    if not isinstance(value, str):
        raise UnexpectedResponse(f"{what} ist kein Text, sondern {type(value).__name__}")
    return value.strip()


SUCCESS_OUTCOMES = ("Converged", "DeliveredWithOpenPoints")
KNOWN_OUTCOMES = (*SUCCESS_OUTCOMES, "Failed", "Aborted")


def as_outcome(value, what):
    text = as_text(value, what)
    if text and text not in KNOWN_OUTCOMES:
        raise UnexpectedResponse(f"{what} hat den unbekannten Wert {text[:40]!r}")
    return text


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
    if status != 200:
        result(f"FEHLER (anmeldung): Token-Tausch gescheitert (HTTP {status})", 3)
    try:
        data = json.loads(text)
    except ValueError:
        raise UnexpectedResponse("Antwort von /oauth/token ist kein JSON") from None
    token = as_dict(data, "Antwort von /oauth/token").get("access_token")
    if not isinstance(token, str) or not token:
        raise UnexpectedResponse("Antwort von /oauth/token ohne access_token")
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
                    if as_dict(candidate, f"SSE-Datenzeile der Antwort auf {method}").get("id") == payload["id"]:
                        message = candidate
        else:
            try:
                message = json.loads(text)
            except ValueError:
                message = None
        if message is None:
            raise UnexpectedResponse(f"{method} ohne lesbare Antwort")
        as_dict(message, f"Antwort auf {method}")
        if "error" in message:
            error = message["error"]
            detail = error.get("message", "") if isinstance(error, dict) else error
            result(f"FEHLER (mcp): {method}: {str(detail)[:200]}", 3)
        return as_dict(message.get("result") or {}, f"Feld result der Antwort auf {method}")

    def notify(self, method):
        self._post({"jsonrpc": "2.0", "method": method})

    def tool(self, name, arguments):
        res = self.rpc("tools/call", {"name": name, "arguments": arguments})
        content = res.get("content")
        content = content if isinstance(content, list) else []
        if res.get("isError"):
            text = " ".join(str(c.get("text", "")) for c in content if isinstance(c, dict))
            result(f"FEHLER (mcp): {name}: {text[:200]}", 3)
        data = res.get("structuredContent")
        if isinstance(data, dict) and "result" in data and len(data) == 1:
            data = data["result"]
        if not isinstance(data, dict):
            data = None
            for c in content:
                if isinstance(c, dict) and c.get("type") == "text" and isinstance(c.get("text"), str):
                    try:
                        data = json.loads(c["text"])
                    except ValueError:
                        pass
        if not isinstance(data, dict):
            return {}
        return {k.lower(): v for k, v in data.items()}


def main():
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
    submitted = mcp.tool("submit_order", {"request": REQUEST})
    order_id = as_text(submitted.get("orderid"), "orderId von submit_order")
    if not order_id:
        result("FEHLER (mcp): submit_order ohne Auftrags-ID", 3)
    step(f"Auftrag {order_id} eingereicht ({as_text(submitted.get('state'), 'state von submit_order') or '?'})")

    state = ""
    outcome = ""
    skipped = False
    while True:
        current = mcp.tool("get_order_status", {"orderId": order_id, "waitSeconds": WAIT})
        state = as_text(current.get("state"), "state von get_order_status")
        outcome = as_outcome(current.get("outcome"), "outcome von get_order_status")
        if state == "needs_input" and not skipped:
            mcp.tool("answer_order", {"orderId": order_id, "skip": True})
            skipped = True
            step("Rückfragen übersprungen")
            continue
        if current.get("done") is True and outcome:
            break
        if time.monotonic() - started > TIMEOUT:
            if current.get("done") is True:
                result(f"FEHLER (zeitlimit): Auftrag {order_id} nach {TIMEOUT} s fertig im Zustand {state}, aber ohne outcome; "
                       "der Text ist erst mit gesetztem outcome endgültig", 1)
            result(f"FEHLER (zeitlimit): Auftrag {order_id} nach {TIMEOUT} s noch im Zustand {state or '?'}, ohne outcome", 1)
        step(f"Zustand {state or '?'}, outcome noch offen")
        time.sleep(POLL)

    final = mcp.tool("get_order_result", {"orderId": order_id})
    elapsed = int(time.monotonic() - started)
    final_state = as_text(final.get("state"), "state von get_order_result") or state
    text = as_text(final.get("text"), "text von get_order_result")
    note = as_text(final.get("note"), "note von get_order_result")
    problems = []
    if outcome not in SUCCESS_OUTCOMES:
        problems.append(f"outcome {outcome} (Zustand {final_state})")
    if not text:
        problems.append("kein Text")
    if problems:
        if note:
            problems.append(f"Hinweis: {note[:160]}")
        result(f"FEHLER (lauf): Auftrag {order_id} nach {elapsed} s: {'; '.join(problems)}", 1)
    open_points = "" if outcome == "Converged" else f", Zustand {final_state}, offene Punkte"
    asked = ", Rückfragen übersprungen" if skipped else ""
    result(f"OK: Auftrag {order_id} {outcome} in {elapsed} s, {len(text)} Zeichen Text (submit_order){asked}{open_points}", 0)

try:
    main()
except UnexpectedResponse as exc:
    result(f"FEHLER (antwort): {exc}", 4)
except Exception as exc:
    result(f"FEHLER (unerwartet): interner Fehler {type(exc).__name__}", 4)
PY
