"""MCP contract of stage 5 (contract stage 5, 3.2, 4.4, 8): tool list, gate M and the 50-second test V-MCP.

The tool never submits, answers, changes or cancels an order and never calls a model. It does three things:

- ``tools``: signs in (OAuth like the smoke script, or a bearer token from the environment), reads ``tools/list`` and stores it.
  Calls no tool.
- ``import``: turns the calls of one MCP server in a Claude Code session file (``~/.claude/projects/<dir>/<session>.jsonl``) into
  a neutral transcript, one JSON line per call (tool, arguments, start, duration, error, answer). Gate M and V-MCP name Claude Code
  as the client (contract 3.2 and 8), so only such a transcript can decide them.
- ``evaluate`` (gate M) and ``vmcp`` (V-MCP) read the stored files and decide, never without data.

**Gate M** (contract 8, fixed in advance): ``submit_order`` (an order with a question) → ``answer_order`` → ``get_order_status`` →
``get_order_result`` → ``revise_order`` → ``get_order_result`` (``version`` 2) → ``export_order`` md and docx → ``list_orders`` →
``profile`` → ``cancel_order`` (second order): every call without an error, tool list as in 4.4. Not evaluable when the token or
connection is missing; here also when the transcript or tool list is missing or unreadable, when an answer cannot be read (for
example cut short in the session file), when the first order never asked a question, when ``cancel_order`` changed nothing, or
when the client is not Claude Code.

**V-MCP** (contract 3.2, fixed in advance): all ``get_order_status`` calls with ``waitSeconds=50`` take at most 55 s without a
client or transport error → 50 s stays; an error on a long call or a long call over 55 s → ``Orders:MaxWaitSeconds`` 25 s. Not
evaluable without such a call, when none of them waited at least :data:`MIN_LONG_WAIT_MS` (the long wait was never exercised), when
another call of the test failed or lacks its result, or when the client is not Claude Code. Durations from a session file run from the logged tool call to the logged result, so they err on the long side.

Transcripts and tool lists hold request texts and answers: they belong outside Git; the tool refuses an output path inside a Git
tree. Tokens and passwords are never written or printed.

Usage (from ``tools/Eval``)::

    python3 -m geef_eval.mcp_contract tools --out <dir>/tools.json [--env-file <file>]
    python3 -m geef_eval.mcp_contract import --session <session.jsonl> [--session ...] --server <name> --out <dir>/transcript.jsonl
    python3 -m geef_eval.mcp_contract evaluate --tools <dir>/tools.json --transcript <dir>/transcript.jsonl --out <dir>/m-gate.json [--legacy true|false]
    python3 -m geef_eval.mcp_contract vmcp --transcript <dir>/vmcp.jsonl --out <dir>/vmcp-gate.json

Sign-in for ``tools``: ``--env-file`` with ``ATELIER_SMOKE_USER``, ``ATELIER_SMOKE_PASSWORD``, ``ATELIER_SMOKE_CLIENT_ID``,
``ATELIER_SMOKE_BASE_URL`` and optionally ``ATELIER_SMOKE_REDIRECT_URI`` (format of ``docs/ops/atelier-smoke.md``, mode 600 or 400),
otherwise ``EVAL_ATELIER_URL`` and ``EVAL_ATELIER_TOKEN``.

Exit codes: 0 passed (V-MCP: 50 s stays), 1 failed (V-MCP: back to 25 s), 2 wrong usage or unreadable input, 3 sign-in or
connection failed, 4 not evaluable.
"""
from __future__ import annotations

import argparse
import base64
import binascii
import hashlib
import http.cookiejar
import json
import os
import re
import secrets
import stat
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Callable, Iterable, Protocol

from .cli import inside_git_worktree
from .mcp import HttpReply, McpClient, McpError

ORDER_TOOLS = ("submit_order", "answer_order", "get_order_status", "get_order_result",
               "revise_order", "export_order", "cancel_order", "list_orders", "profile")
LEGACY_TOOLS = ("analyze_template_proposal", "cancel_run", "download_run_artifact", "get_provider_models", "get_run_details",
                "get_run_result", "get_run_status", "list_advisor_profiles", "list_crew_templates",
                "list_grounding_provider_profiles", "list_knowledge_documents", "list_learnings", "list_providers",
                "list_reviewer_profiles", "list_run_artifacts", "list_runs", "materialize_template_proposal",
                "submit_auto_crew_request", "submit_request")
SUCCESSORS = {"submit_request": "submit_order", "get_run_status": "get_order_status", "get_run_result": "get_order_result",
              "cancel_run": "cancel_order", "list_runs": "list_orders"}
LEGACY_PREFIX = "Older tool, kept for now: use "
LIST_ORDER_FIELDS = ("orderId", "title", "state", "version", "updatedAt", "wordCount")
# The server leaves out fields whose value is null (MCP serializer), so an order without a text has no wordCount and, before
# its first version, no version; these two may be missing, the others never.
LIST_ORDER_REQUIRED = ("orderId", "title", "state", "updatedAt")
EXPORT_FORMATS = frozenset({"md", "docx", "pdf"})
DOCX_MAGIC = b"PK\x03\x04"
REVISED_VERSION = 2
NEEDS_INPUT = "needs_input"

LONG_WAIT_SECONDS = 50
MAX_CALL_MS = 55_000
MIN_LONG_WAIT_MS = 45_000
CLAUDE_CODE = "claude_code"

ALLOWED_HTTP_HOSTS = frozenset({"localhost", "127.0.0.1", "::1"})
DEFAULT_REDIRECT_URI = "http://127.0.0.1/atelier-smoke/callback"
ENV_KEYS = ("ATELIER_SMOKE_USER", "ATELIER_SMOKE_PASSWORD", "ATELIER_SMOKE_CLIENT_ID", "ATELIER_SMOKE_BASE_URL")


class SignInError(RuntimeError):
    """Sign-in or connection failed; the message never holds a password, cookie or token."""


def load_env_file(path: Path) -> dict[str, str]:
    """``KEY=VALUE`` lines as in the smoke script; the file must have mode 600 or 400 (it holds a password)."""
    mode = stat.S_IMODE(path.stat().st_mode)
    if mode & 0o077:
        raise ValueError(f"{path} hat Modus {mode:o}; erlaubt sind 600 und 400 (die Datei enthält ein Passwort)")
    values: dict[str, str] = {}
    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, value = line.split("=", 1)
        key, value = key.strip().removeprefix("export ").strip(), value.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "'\"":
            value = value[1:-1]
        values[key] = value
    return values


def check_base_url(url: str) -> str:
    """The base URL without a trailing slash; ``https://``, or ``http://`` only for a loopback host (the password goes over it)."""
    parts = urllib.parse.urlsplit(url or "")
    host = (parts.hostname or "").lower()
    if parts.scheme not in ("https", "http") or not host:
        raise ValueError("Basisadresse ist keine gültige https://-Adresse")
    if parts.scheme == "http" and host not in ALLOWED_HTTP_HOSTS:
        raise ValueError(f"http:// ist nur für {', '.join(sorted(ALLOWED_HTTP_HOSTS))} erlaubt; sonst https:// verwenden")
    return url.rstrip("/")


class WebSession(Protocol):
    """Form posts against the web app with cookies and without following redirects."""

    def request(self, method: str, path: str, form: dict[str, str] | None = None) -> HttpReply:
        ...

    def has_cookie(self, name: str) -> bool:
        ...


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


class UrllibWebSession:
    """The standard-library session: a cookie jar and no redirects (the code is read from the ``Location`` header)."""

    def __init__(self, base_url: str, timeout_s: float = 60.0):
        self.base_url = base_url.rstrip("/")
        self.timeout_s = timeout_s
        self._jar = http.cookiejar.CookieJar()
        self._opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self._jar), _NoRedirect())

    def request(self, method: str, path: str, form: dict[str, str] | None = None) -> HttpReply:
        data = urllib.parse.urlencode(form).encode() if form is not None else None
        headers = {"Content-Type": "application/x-www-form-urlencoded"} if form is not None else {}
        req = urllib.request.Request(self.base_url + path, data=data, method=method, headers=headers)
        try:
            with self._opener.open(req, timeout=self.timeout_s) as response:
                return HttpReply(response.status, {k.lower(): v for k, v in response.headers.items()},
                                 response.read().decode("utf-8", "replace"))
        except urllib.error.HTTPError as exc:
            return HttpReply(exc.code, {k.lower(): v for k, v in (exc.headers or {}).items()},
                             exc.read().decode("utf-8", "replace"))
        except (urllib.error.URLError, TimeoutError, OSError) as exc:
            raise SignInError(f"{method} {path} nicht möglich: {getattr(exc, 'reason', exc)}") from None

    def has_cookie(self, name: str) -> bool:
        return any(cookie.name == name for cookie in self._jar)


def _antiforgery(page: str) -> str | None:
    for tag in re.findall(r"<input[^>]*>", page):
        if 'name="__RequestVerificationToken"' in tag:
            match = re.search(r'value="([^"]*)"', tag)
            return match.group(1) if match else None
    return None


def oauth_access_token(session: WebSession, user: str, password: str, client_id: str,
                       redirect_uri: str = DEFAULT_REDIRECT_URI) -> str:
    """Login form, OAuth consent with PKCE and code exchange, as the smoke script does; returns the access token."""
    reply = session.request("GET", "/login")
    token = _antiforgery(reply.body) if reply.status == 200 else None
    if not token:
        raise SignInError(f"Login-Seite nicht lesbar (HTTP {reply.status})")
    reply = session.request("POST", "/login", form={"_handler": "login-form", "__RequestVerificationToken": token,
                                                    "Model.Username": user, "Model.Password": password})
    if reply.status not in (301, 302, 303) or not session.has_cookie("Atelier.Auth"):
        raise SignInError(f"Login abgelehnt (HTTP {reply.status})")
    verifier = secrets.token_urlsafe(48)
    challenge = base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).rstrip(b"=").decode()
    state = secrets.token_urlsafe(16)
    reply = session.request("POST", "/oauth/consent", form={
        "action": "approve", "response_type": "code", "client_id": client_id, "redirect_uri": redirect_uri, "scope": "mcp",
        "state": state, "code_challenge": challenge, "code_challenge_method": "S256"})
    query = urllib.parse.parse_qs(urllib.parse.urlsplit(reply.headers.get("location", "")).query)
    if reply.status not in (301, 302, 303) or "code" not in query:
        raise SignInError(f"OAuth-Freigabe gescheitert ({query.get('error', [f'HTTP {reply.status}'])[0]})")
    if query.get("state", [""])[0] != state:
        raise SignInError("OAuth-Freigabe mit falschem state")
    reply = session.request("POST", "/oauth/token", form={
        "grant_type": "authorization_code", "code": query["code"][0], "client_id": client_id, "redirect_uri": redirect_uri,
        "code_verifier": verifier})
    if reply.status != 200:
        raise SignInError(f"Token-Tausch gescheitert (HTTP {reply.status})")
    try:
        access = json.loads(reply.body).get("access_token")
    except (ValueError, AttributeError):
        access = None
    if not isinstance(access, str) or not access:
        raise SignInError("Antwort von /oauth/token ohne access_token")
    return access


class ContractClient(McpClient):
    """:class:`McpClient` plus ``tools/list``."""

    def list_tools(self) -> list[dict]:
        self._ensure_initialized()
        tools: list[dict] = []
        cursor = None
        for _ in range(50):
            result = self._request("tools/list", {"cursor": cursor} if cursor else {})
            tools.extend(t for t in result.get("tools") or [] if isinstance(t, dict))
            cursor = result.get("nextCursor")
            if not cursor:
                return tools
        raise McpError("tools/list endet nicht (mehr als 50 Seiten)")


def connect(env_file: Path | None, *, session_factory: Callable[[str], WebSession] = UrllibWebSession,
            environ: dict[str, str] | None = None) -> ContractClient:
    """A signed-in client: OAuth with the values of ``env_file``, otherwise ``EVAL_ATELIER_URL`` and ``EVAL_ATELIER_TOKEN``."""
    environ = os.environ if environ is None else environ
    if env_file is not None:
        values = load_env_file(env_file)
        if missing := [k for k in ENV_KEYS if not values.get(k)]:
            raise ValueError(f"in {env_file} fehlen {', '.join(missing)}")
        base = check_base_url(values["ATELIER_SMOKE_BASE_URL"])
        token = oauth_access_token(session_factory(base), values["ATELIER_SMOKE_USER"], values["ATELIER_SMOKE_PASSWORD"],
                                   values["ATELIER_SMOKE_CLIENT_ID"],
                                   values.get("ATELIER_SMOKE_REDIRECT_URI") or DEFAULT_REDIRECT_URI)
        return ContractClient(base, token)
    url, token = environ.get("EVAL_ATELIER_URL"), environ.get("EVAL_ATELIER_TOKEN")
    if not url or not token:
        raise ValueError("Anmeldung fehlt: --env-file oder EVAL_ATELIER_URL und EVAL_ATELIER_TOKEN")
    return ContractClient(check_base_url(url), token)


def _now() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def _parse_time(value: object) -> datetime | None:
    if not isinstance(value, str) or not value:
        return None
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None


def _block_text(content: object) -> str:
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        return "".join(str(b.get("text") or "") for b in content if isinstance(b, dict) and b.get("type") == "text")
    return ""


def _parse_answer(text: str) -> tuple[object, bool]:
    """The answer as JSON (``structuredContent`` of a list comes wrapped in ``result``) and whether it could be read."""
    try:
        value = json.loads(text)
    except (ValueError, TypeError):
        return None, False
    if isinstance(value, dict) and set(value) == {"result"}:
        value = value["result"]
    return value, True


def import_claude_session(lines: Iterable[str], server: str) -> list[dict]:
    """
    The calls of tools ``mcp__<server>__<tool>`` in a Claude Code session file, in the order of the file. A call without a logged
    result is kept with ``ok = null`` (its answer is missing); an answer that is no JSON keeps ``readable = false``.
    """
    prefix = f"mcp__{server}__"
    calls: dict[str, dict] = {}
    ordered: list[dict] = []
    for raw in lines:
        try:
            entry = json.loads(raw)
        except ValueError:
            continue
        if not isinstance(entry, dict):
            continue
        content = (entry.get("message") or {}).get("content") if isinstance(entry.get("message"), dict) else None
        if not isinstance(content, list):
            continue
        stamp = entry.get("timestamp")
        for block in content:
            if not isinstance(block, dict):
                continue
            if block.get("type") == "tool_use" and str(block.get("name") or "").startswith(prefix):
                call = {"seq": len(ordered) + 1, "tool": str(block["name"])[len(prefix):],
                        "arguments": block.get("input") if isinstance(block.get("input"), dict) else {},
                        "started_at": stamp, "duration_ms": None, "ok": None, "error": None, "response": None,
                        "readable": False, "source": CLAUDE_CODE}
                calls[str(block.get("id"))] = call
                ordered.append(call)
            elif block.get("type") == "tool_result" and str(block.get("tool_use_id")) in calls:
                call = calls[str(block.get("tool_use_id"))]
                text = _block_text(block.get("content"))
                start, end = _parse_time(call["started_at"]), _parse_time(stamp)
                if start and end:
                    call["duration_ms"] = max(0, int((end - start).total_seconds() * 1000))
                if block.get("is_error"):
                    call.update(ok=False, error=text[:2000] or "Fehler ohne Text", readable=True)
                else:
                    value, readable = _parse_answer(text)
                    call.update(ok=True, response=value if readable else text[:2000], readable=readable)
    return ordered


def read_transcript(path: Path) -> list[dict]:
    """The calls of a transcript (JSON lines); raises ValueError for a line that is no call."""
    calls = []
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            continue
        call = json.loads(line)
        if not isinstance(call, dict) or not isinstance(call.get("tool"), str):
            raise ValueError(f"{path}:{number} ist kein Aufruf (Feld tool fehlt)")
        calls.append(call)
    return calls


def _properties(tool: dict) -> tuple[dict, list]:
    schema = tool.get("inputSchema") if isinstance(tool.get("inputSchema"), dict) else {}
    props = schema.get("properties") if isinstance(schema.get("properties"), dict) else {}
    required = schema.get("required") if isinstance(schema.get("required"), list) else []
    return props, required


def _first_sentence(text: str) -> str:
    end = text.find("instead.")
    return text[: end + len("instead.")] if end >= 0 else text.split(". ")[0]


def check_tool_list(tools: list[dict], legacy: bool) -> list[str]:
    """What differs from the tool list of contract 4.4 (``legacy`` = ``Mcp:LegacyTools``); empty when it matches."""
    problems: list[str] = []
    by_name: dict[str, dict] = {}
    for tool in tools:
        name = str(tool.get("name") or "")
        if name in by_name:
            problems.append(f"Werkzeug doppelt: {name}")
        by_name[name] = tool
    expected = set(ORDER_TOOLS) | (set(LEGACY_TOOLS) if legacy else set())
    if missing := sorted(expected - set(by_name)):
        problems.append(f"Werkzeuge fehlen: {', '.join(missing)}")
    if extra := sorted(set(by_name) - expected):
        problems.append(f"Werkzeuge zu viel: {', '.join(extra)}")

    def need(tool_name: str, *names: str) -> dict:
        props, _ = _properties(by_name.get(tool_name, {}))
        if tool_name in by_name and (absent := [n for n in names if n not in props]):
            problems.append(f"{tool_name}: Parameter fehlen: {', '.join(absent)}")
        return props

    need("submit_order", "request")
    need("answer_order", "orderId", "answers", "skip")
    need("get_order_status", "orderId", "waitSeconds")
    need("get_order_result", "orderId")
    if "revise_order" in by_name:
        props = need("revise_order", "orderId", "change", "quick")
        _, required = _properties(by_name["revise_order"])
        if set(required) != {"orderId"}:
            problems.append(f"revise_order: Pflichtparameter {sorted(required)} statt nur orderId (change und quick sind wahlweise)")
        if "quick" in props and props["quick"].get("type") not in ("array", ["array", "null"], ["null", "array"]):
            problems.append("revise_order: quick ist keine Liste")
        if "get_order_status" not in str(by_name["revise_order"].get("description") or ""):
            problems.append("revise_order: Beschreibung nennt get_order_status nicht (4.4: danach get_order_status)")
    if "export_order" in by_name:
        props = need("export_order", "orderId", "format", "version")
        _, required = _properties(by_name["export_order"])
        if "orderId" not in required:
            problems.append("export_order: orderId ist kein Pflichtparameter")
        if "version" in required:
            problems.append("export_order: version ist Pflicht (4.4: wahlweise)")
        enum = (props.get("format") or {}).get("enum")
        if isinstance(enum, list) and set(enum) != EXPORT_FORMATS:
            problems.append(f"export_order: format erlaubt {sorted(enum)} statt md, docx, pdf")
    need("cancel_order", "orderId")
    if "list_orders" in by_name:
        need("list_orders", "limit")
        _, required = _properties(by_name["list_orders"])
        if required:
            problems.append(f"list_orders: Pflichtparameter {sorted(required)} (4.4: limit wahlweise)")
    if "profile" in by_name:
        props, _ = _properties(by_name["profile"])
        if props:
            problems.append(f"profile: hat Parameter {', '.join(sorted(props))} (4.4: nur lesen, keine Parameter)")
        if "About me" not in str(by_name["profile"].get("description") or ""):
            problems.append("profile: Beschreibung nennt „About me“ nicht als Ort zum Ändern")
    if legacy:
        for old, new in SUCCESSORS.items():
            description = str(by_name.get(old, {}).get("description") or "")
            if old in by_name and not (description.startswith(LEGACY_PREFIX) and new in _first_sentence(description)):
                problems.append(f"{old}: Beschreibung beginnt nicht mit „{LEGACY_PREFIX}{new} instead.“")
    return problems


def _order_id(value: object) -> str | None:
    return str(value["orderId"]) if isinstance(value, dict) and value.get("orderId") else None


def _decode(value: object) -> bytes | None:
    if not isinstance(value, str):
        return None
    try:
        return base64.b64decode(value, validate=True)
    except (binascii.Error, ValueError):
        return None


def _export_problem(call: dict) -> str | None:
    response = call.get("response")
    if not isinstance(response, dict):
        return "Antwort ist kein Objekt"
    if not all(isinstance(response.get(k), str) and response.get(k) for k in ("fileName", "contentType", "contentBase64")):
        return "fileName, contentType oder contentBase64 fehlt"
    content = _decode(response["contentBase64"])
    if content is None:
        return "contentBase64 ist kein gültiges Base64"
    fmt = str((call.get("arguments") or {}).get("format") or "").lower()
    if fmt == "docx" and not content.startswith(DOCX_MAGIC):
        return "docx ist keine ZIP-Datei"
    if fmt == "md":
        text = response.get("text")
        if not isinstance(text, str) or not text.strip():
            return "md ohne text"
        if content.decode("utf-8", "replace").lstrip("\ufeff").strip() != text.strip():
            return "md: text weicht vom Inhalt der Datei ab"
    return None


def evaluate_gate_m(tools: list[dict] | None, calls: list[dict], legacy: bool = True) -> dict:
    """Gate M; ``verdict`` is ``passed``, ``failed`` or ``not_evaluable``."""
    problems: list[str] = []
    failures: list[str] = []
    steps: dict[str, dict] = {}

    if tools is None:
        problems.append("Werkzeugliste fehlt (mcp_contract tools)")
        list_problems: list[str] = []
    else:
        list_problems = check_tool_list(tools, legacy)
        failures.extend(f"Werkzeugliste: {p}" for p in list_problems)
    if not calls:
        problems.append("Mitschnitt ohne Aufrufe")
    clients = sorted({str(c.get("source") or "unbekannt") for c in calls})
    if calls and clients != [CLAUDE_CODE]:
        problems.append(f"Client ist {', '.join(clients)}, Tor M verlangt Claude Code")

    relevant = [c for c in calls if c.get("tool") in ORDER_TOOLS]
    for call in relevant:
        where = f"#{call.get('seq')} {call['tool']}"
        if call.get("ok") is False:
            failures.append(f"{where}: Fehler: {str(call.get('error') or '')[:200]}")
        elif call.get("ok") is None:
            problems.append(f"{where}: kein Ergebnis im Mitschnitt")
        elif not call.get("readable", True):
            problems.append(f"{where}: Antwort nicht lesbar (im Mitschnitt gekürzt?)")
    ok = [c for c in relevant if c.get("ok") is True and c.get("readable", True)]

    def first(tool: str, after: int = 0, where: Callable[[dict], bool] = lambda _: True) -> dict | None:
        return next((c for c in ok if c["tool"] == tool and int(c.get("seq") or 0) > after and where(c)), None)

    def arg(call: dict, name: str) -> object:
        return (call.get("arguments") or {}).get(name)

    answered = first("answer_order")
    order_a = str(arg(answered, "orderId")) if answered else None
    submit_a = first("submit_order", where=lambda c: _order_id(c.get("response")) == order_a) if order_a else None
    asked = order_a is not None and any(
        isinstance(c.get("response"), dict) and c["response"].get("state") == NEEDS_INPUT
        and _order_id(c["response"]) == order_a and int(c.get("seq") or 0) < int(answered.get("seq") or 0)
        for c in ok if c["tool"] in ("submit_order", "get_order_status"))
    steps["submit_order"] = {"found": submit_a is not None, "order": "A"}
    steps["answer_order"] = {"found": answered is not None and submit_a is not None
                             and int(answered["seq"]) > int(submit_a["seq"])}
    if submit_a is None:
        problems.append("kein submit_order für den Auftrag, der beantwortet wurde (Auftrag A)")
    elif not asked:
        problems.append("Auftrag A hat keine Rückfrage gestellt (state needs_input fehlt vor answer_order); Tor M verlangt einen "
                        "Auftrag mit Rückfrage")

    for_a = (lambda c: str(arg(c, "orderId")) == order_a)
    cursor = int(answered["seq"]) if answered and submit_a else 0
    status = first("get_order_status", cursor, for_a) if cursor else None
    steps["get_order_status"] = {"found": status is not None}
    cursor = int(status["seq"]) if status else 0
    result_1 = first("get_order_result", cursor, lambda c: for_a(c) and _has_text(c)) if cursor else None
    steps["get_order_result"] = {"found": result_1 is not None,
                                 "version": (result_1 or {}).get("response", {}).get("version") if result_1 else None}
    cursor = int(result_1["seq"]) if result_1 else 0
    revised = first("revise_order", cursor, for_a) if cursor else None
    steps["revise_order"] = {"found": revised is not None}
    if revised is not None:
        response = revised.get("response") if isinstance(revised.get("response"), dict) else {}
        steps["revise_order"]["version"] = response.get("version")
        if response.get("version") != REVISED_VERSION or not isinstance(response.get("notices"), list) \
                or _order_id(response) != order_a or not response.get("state"):
            failures.append(f"revise_order: Antwort ohne orderId/state/notices oder mit version {response.get('version')!r} "
                            f"statt {REVISED_VERSION}")
    cursor = int(revised["seq"]) if revised else 0
    result_2 = first("get_order_result", cursor, lambda c: for_a(c) and _has_text(c)
                     and c["response"].get("version") == REVISED_VERSION) if cursor else None
    steps["get_order_result_v2"] = {"found": result_2 is not None}
    if cursor and result_2 is None and first("get_order_result", cursor, for_a):
        failures.append(f"get_order_result nach revise_order zeigt keinen Text der Version {REVISED_VERSION}")
    if result_2 is not None:
        latest = result_2["response"].get("latestVersion")
        if latest is not None and latest != REVISED_VERSION:
            failures.append(f"get_order_result nach revise_order: latestVersion {latest!r} statt {REVISED_VERSION}")
    cursor = int(result_2["seq"]) if result_2 else 0
    for fmt in ("md", "docx"):
        export = first("export_order", cursor,
                       lambda c, f=fmt: for_a(c) and str(arg(c, "format") or "").lower() == f) if cursor else None
        steps[f"export_order_{fmt}"] = {"found": export is not None}
        if export is not None and (problem := _export_problem(export)):
            failures.append(f"export_order {fmt}: {problem}")

    listed = first("list_orders", cursor) if cursor else None
    steps["list_orders"] = {"found": listed is not None}
    if listed is not None:
        rows = listed.get("response")
        if not isinstance(rows, list) or not all(isinstance(r, dict) for r in rows):
            failures.append("list_orders: Antwort ist keine Liste von Objekten")
        else:
            if not any(_order_id(r) == order_a for r in rows):
                failures.append("list_orders: Auftrag A fehlt")
            if incomplete := [r.get("orderId") for r in rows if any(k not in r for k in LIST_ORDER_REQUIRED)]:
                failures.append(f"list_orders: {len(incomplete)} Einträge ohne alle Felder {', '.join(LIST_ORDER_REQUIRED)}")
            row_a = next((r for r in rows if _order_id(r) == order_a), None)
            if row_a is not None and any(k not in row_a for k in LIST_ORDER_FIELDS):
                failures.append(f"list_orders: Auftrag A (mit Text) ohne alle Felder {', '.join(LIST_ORDER_FIELDS)}")
    profile = first("profile")
    steps["profile"] = {"found": profile is not None}
    if profile is not None and not isinstance(profile.get("response"), (dict, type(None))):
        failures.append("profile: Antwort ist kein Objekt")

    submitted = {oid for c in ok if c["tool"] == "submit_order" and (oid := _order_id(c.get("response")))}
    cancelled = next((c for c in ok if c["tool"] == "cancel_order" and str(arg(c, "orderId")) in submitted - {order_a}), None)
    steps["cancel_order"] = {"found": cancelled is not None, "order": "B"}
    if cancelled is not None:
        response = cancelled.get("response") if isinstance(cancelled.get("response"), dict) else {}
        if response.get("changed") is not True:
            problems.append("cancel_order änderte nichts (zweiter Auftrag schon fertig?); Stopp nicht nachgewiesen")
    steps["submits"] = {"count": len(submitted)}

    if missing := [name for name, step in steps.items() if step.get("found") is False]:
        problems.append(f"Schritte fehlen im Mitschnitt: {', '.join(missing)}")
    verdict = "failed" if failures else ("not_evaluable" if problems else "passed")
    return {"gate": "M", "verdict": verdict, "failures": failures, "problems": problems, "steps": steps,
            "tool_list": {"legacy": legacy, "problems": list_problems, "count": len(tools or [])}, "clients": clients,
            "calls": len(relevant)}


def _has_text(call: dict) -> bool:
    response = call.get("response")
    return isinstance(response, dict) and isinstance(response.get("text"), str) and bool(response["text"].strip())


def _wait_seconds(call: dict) -> int | None:
    value = (call.get("arguments") or {}).get("waitSeconds")
    try:
        return int(value) if value is not None and not isinstance(value, bool) else None
    except (TypeError, ValueError):
        return None


def evaluate_vmcp(calls: list[dict]) -> dict:
    """V-MCP; ``verdict`` is ``keep_50``, ``back_to_25`` or ``not_evaluable``."""
    problems: list[str] = []
    clients = sorted({str(c.get("source") or "unbekannt") for c in calls})
    if calls and clients != [CLAUDE_CODE]:
        problems.append(f"Client ist {', '.join(clients)}, V-MCP verlangt Claude Code")
    status_calls = [c for c in calls if c.get("tool") == "get_order_status"]
    long_calls = [c for c in status_calls if _wait_seconds(c) == LONG_WAIT_SECONDS]
    rows, errors = [], []
    for call in long_calls:
        duration = call.get("duration_ms")
        row = {"seq": call.get("seq"), "duration_ms": duration, "ok": call.get("ok"), "error": call.get("error")}
        rows.append(row)
        if call.get("ok") is False:
            errors.append(f"#{call.get('seq')}: Fehler nach {duration} ms: {str(call.get('error') or '')[:200]}")
        elif call.get("ok") is None or not isinstance(duration, (int, float)):
            problems.append(f"#{call.get('seq')}: Dauer oder Ergebnis fehlt im Mitschnitt")
        elif duration > MAX_CALL_MS:
            errors.append(f"#{call.get('seq')}: {duration} ms (mehr als {MAX_CALL_MS} ms)")
    other_errors = [f"#{c.get('seq')} {c.get('tool')}: {str(c.get('error') or '')[:200]}"
                    for c in calls if c not in long_calls and c.get("ok") is False]
    if other_errors:
        problems.append(f"{len(other_errors)} andere Aufrufe mit Fehler: der Versuch lief nicht sauber")
    if unanswered := [c for c in calls if c not in long_calls and c.get("ok") is None]:
        problems.append(f"{len(unanswered)} andere Aufrufe ohne Ergebnis im Mitschnitt")
    if not long_calls:
        problems.append(f"kein get_order_status mit waitSeconds={LONG_WAIT_SECONDS} im Mitschnitt")
    elif not errors and not any(isinstance(r["duration_ms"], (int, float)) and r["duration_ms"] >= MIN_LONG_WAIT_MS
                                for r in rows if r["ok"]):
        problems.append(f"kein Aufruf wartete mindestens {MIN_LONG_WAIT_MS} ms: der lange Aufruf wurde nicht geprüft")
    if errors:
        verdict = "back_to_25"
    else:
        verdict = "not_evaluable" if problems else "keep_50"
    return {"gate": "V-MCP", "verdict": verdict, "errors": errors, "problems": problems, "long_calls": rows,
            "other_errors": other_errors, "orders": len({oid for c in calls if c.get("tool") == "submit_order" and c.get("ok")
                                                         and (oid := _order_id(c.get("response")))}),
            "clients": clients}


def _write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")


def _print_m(report: dict, echo: Callable[[str], None]) -> None:
    for name, step in report["steps"].items():
        if "found" in step:
            echo(f"  {name}: {'da' if step['found'] else 'fehlt'}")
    for failure in report["failures"]:
        echo(f"Fehler: {failure}")
    for problem in report["problems"]:
        echo(f"nicht auswertbar: {problem}")
    echo(f"Tor M: {report['verdict']} ({report['calls']} Aufrufe, Werkzeugliste {report['tool_list']['count']} Werkzeuge, "
         f"Client {', '.join(report['clients']) or '-'})")


def _print_vmcp(report: dict, echo: Callable[[str], None]) -> None:
    for row in report["long_calls"]:
        echo(f"  #{row['seq']}: {row['duration_ms']} ms, {'ok' if row['ok'] else 'Fehler'}")
    for error in report["errors"]:
        echo(f"Fehler: {error}")
    for problem in report["problems"]:
        echo(f"nicht auswertbar: {problem}")
    follow = {"keep_50": "50 s bleibt", "back_to_25": "Orders:MaxWaitSeconds auf 25 s, ein Aufruf zur Bestätigung",
              "not_evaluable": "keine Entscheidung"}[report["verdict"]]
    echo(f"V-MCP: {report['verdict']} → {follow}")


def _outside_git(path: Path, allow: bool, echo: Callable[[str], None]) -> bool:
    if not allow and inside_git_worktree(path.parent):
        echo(f"{path} liegt in einem Git-Baum; Mitschnitte und Antworten gehören nicht ins Repository.")
        return False
    return True


def _arguments(argv: list[str] | None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="mcp_contract", description=__doc__.split("\n\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    tools = sub.add_parser("tools", help="tools/list lesen und speichern")
    tools.add_argument("--out", required=True, type=Path)
    tools.add_argument("--env-file", type=Path)
    imp = sub.add_parser("import", help="Aufrufe aus einer Claude-Code-Sitzung übernehmen")
    imp.add_argument("--session", action="append", required=True, type=Path)
    imp.add_argument("--server", required=True, help="Name des MCP-Servers in Claude Code (Werkzeuge mcp__<name>__…)")
    imp.add_argument("--out", required=True, type=Path)
    ev = sub.add_parser("evaluate", help="Tor M")
    ev.add_argument("--tools", required=True, type=Path)
    ev.add_argument("--transcript", required=True, type=Path)
    ev.add_argument("--legacy", choices=("true", "false"), default="true", help="erwarteter Wert von Mcp:LegacyTools")
    ev.add_argument("--out", required=True, type=Path)
    vm = sub.add_parser("vmcp", help="V-MCP")
    vm.add_argument("--transcript", required=True, type=Path)
    vm.add_argument("--out", required=True, type=Path)
    for p in (tools, imp, ev, vm):
        p.add_argument("--allow-in-repo", action="store_true", help=argparse.SUPPRESS)
    return parser.parse_args(argv)


def main(argv: list[str] | None = None, *, connector: Callable[[Path | None], ContractClient] = connect,
         echo: Callable[[str], None] = print) -> int:
    args = _arguments(argv)
    if not _outside_git(args.out, args.allow_in_repo, echo):
        return 2
    if args.command == "tools":
        try:
            client = connector(args.env_file)
            started = time.monotonic()
            listed = client.list_tools()
        except (OSError, ValueError) as exc:
            echo(f"Anmeldung nicht möglich: {exc}")
            return 2
        except (SignInError, McpError) as exc:
            echo(f"Anmeldung oder Verbindung gescheitert: {exc}")
            return 3
        _write_json(args.out, {"fetchedAt": _now(), "durationMs": int((time.monotonic() - started) * 1000), "tools": listed})
        echo(f"{len(listed)} Werkzeuge gespeichert: {', '.join(sorted(str(t.get('name')) for t in listed))}")
        return 0
    if args.command == "import":
        try:
            calls = [c for path in args.session
                     for c in import_claude_session(path.read_text(encoding="utf-8").splitlines(), args.server)]
        except OSError as exc:
            echo(f"Sitzung nicht lesbar: {exc}")
            return 2
        for number, call in enumerate(calls, 1):
            call["seq"] = number
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text("".join(json.dumps(c, ensure_ascii=False) + "\n" for c in calls), encoding="utf-8")
        echo(f"{len(calls)} Aufrufe von mcp__{args.server}__… übernommen")
        return 0 if calls else 4
    try:
        calls = read_transcript(args.transcript)
        if args.command == "evaluate":
            listed = json.loads(args.tools.read_text(encoding="utf-8")).get("tools")
            if not isinstance(listed, list):
                raise ValueError(f"{args.tools} enthält keine Liste tools")
    except (OSError, ValueError, AttributeError) as exc:
        echo(f"Eingabe nicht lesbar: {exc}")
        return 2
    if args.command == "evaluate":
        report = evaluate_gate_m(listed, calls, legacy=args.legacy == "true")
        _write_json(args.out, report)
        _print_m(report, echo)
        return {"passed": 0, "failed": 1}.get(report["verdict"], 4)
    report = evaluate_vmcp(calls)
    _write_json(args.out, report)
    _print_vmcp(report, echo)
    return {"keep_50": 0, "back_to_25": 1}.get(report["verdict"], 4)


if __name__ == "__main__":
    sys.exit(main())
