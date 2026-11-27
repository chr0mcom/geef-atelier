"""Minimal MCP client over Streamable HTTP (JSON-RPC, JSON or SSE replies) and the Atelier tool adapter."""
from __future__ import annotations

import base64
import itertools
import json
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from typing import Callable

from .tasks import Attachment

PROTOCOL_VERSION = "2025-06-18"


@dataclass(frozen=True)
class HttpReply:
    """Raw HTTP reply."""

    status: int
    headers: dict[str, str]
    body: str


Transport = Callable[[str, dict[str, str], bytes, float], HttpReply]


class McpError(RuntimeError):
    """The MCP endpoint answered with an error."""


class RedirectRefused(McpError):
    """The MCP endpoint answered with a redirect; it is never followed so the bearer token stays with the configured origin."""


def _origin(url: str) -> str:
    parts = urllib.parse.urlsplit(url)
    return f"{parts.scheme}://{parts.hostname or '?'}" + (f":{parts.port}" if parts.port else "")


class _RefuseRedirects(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        target = urllib.parse.urljoin(req.full_url, newurl)
        raise RedirectRefused(
            f"MCP-Endpunkt leitet weiter (HTTP {code} nach {_origin(target)}). Weiterleitungen werden nicht verfolgt, "
            "damit das Token nicht an einen anderen Ursprung geht; EVAL_ATELIER_URL auf die Zieladresse setzen.")


_OPENER = urllib.request.build_opener(_RefuseRedirects())


def urllib_transport(url: str, headers: dict[str, str], body: bytes, timeout_s: float) -> HttpReply:
    """POST via the standard library; redirects raise ``RedirectRefused`` instead of being followed."""
    request = urllib.request.Request(url, data=body, headers=headers, method="POST")
    try:
        with _OPENER.open(request, timeout=timeout_s) as response:
            return HttpReply(response.status, {k.lower(): v for k, v in response.headers.items()},
                             response.read().decode("utf-8", errors="replace"))
    except urllib.error.HTTPError as exc:
        return HttpReply(exc.code, {k.lower(): v for k, v in exc.headers.items()},
                         exc.read().decode("utf-8", errors="replace"))


def parse_jsonrpc_reply(body: str) -> dict:
    """Accepts a plain JSON body or an SSE stream and returns the last JSON-RPC message with a result or error."""
    stripped = body.strip()
    if stripped.startswith("{"):
        return json.loads(stripped)
    candidates = []
    for line in body.splitlines():
        if line.startswith("data:"):
            data = line[len("data:"):].strip()
            if data.startswith("{"):
                message = json.loads(data)
                if "result" in message or "error" in message:
                    candidates.append(message)
    if not candidates:
        raise McpError(f"Keine JSON-RPC-Antwort im Rückgabetext: {body[:200]}")
    return candidates[-1]


@dataclass
class McpClient:
    """Calls MCP tools with a bearer token; keeps a session id when the server hands one out."""

    base_url: str
    token: str
    transport: Transport = urllib_transport
    timeout_s: float = 120.0
    session_id: str | None = None
    _ids: itertools.count = field(default_factory=lambda: itertools.count(1))
    _initialized: bool = False

    @property
    def endpoint(self) -> str:
        base = self.base_url.rstrip("/")
        return base if base.endswith("/mcp") else f"{base}/mcp"

    def call_tool(self, name: str, arguments: dict) -> object:
        """Calls a tool and returns its structured content, else its first text block parsed as JSON (or as text)."""
        self._ensure_initialized()
        result = self._request("tools/call", {"name": name, "arguments": arguments})
        if result.get("isError"):
            raise McpError(f"Werkzeug {name} meldet einen Fehler: {_first_text(result)[:300]}")
        if isinstance(result.get("structuredContent"), dict):
            return result["structuredContent"]
        text = _first_text(result)
        try:
            return json.loads(text)
        except json.JSONDecodeError:
            return text

    def _ensure_initialized(self) -> None:
        if self._initialized:
            return
        self._request("initialize", {
            "protocolVersion": PROTOCOL_VERSION,
            "capabilities": {},
            "clientInfo": {"name": "geef-eval", "version": "0.1"},
        })
        self._notify("notifications/initialized")
        self._initialized = True

    def _headers(self) -> dict[str, str]:
        headers = {
            "Content-Type": "application/json",
            "Accept": "application/json, text/event-stream",
            "Authorization": f"Bearer {self.token}",
            "MCP-Protocol-Version": PROTOCOL_VERSION,
        }
        if self.session_id:
            headers["Mcp-Session-Id"] = self.session_id
        return headers

    def _request(self, method: str, params: dict) -> dict:
        payload = {"jsonrpc": "2.0", "id": next(self._ids), "method": method, "params": params}
        reply = self.transport(self.endpoint, self._headers(), json.dumps(payload).encode("utf-8"), self.timeout_s)
        if reply.status == 401:
            raise McpError("MCP-Endpunkt lehnt das Token ab (401). EVAL_ATELIER_TOKEN prüfen.")
        if reply.status >= 400:
            raise McpError(f"MCP-Aufruf {method} scheiterte mit HTTP {reply.status}: {reply.body[:300]}")
        if sid := reply.headers.get("mcp-session-id"):
            self.session_id = sid
        message = parse_jsonrpc_reply(reply.body)
        if "error" in message:
            raise McpError(f"MCP-Aufruf {method} meldet: {message['error']}")
        return message.get("result") or {}

    def _notify(self, method: str) -> None:
        payload = {"jsonrpc": "2.0", "method": method}
        self.transport(self.endpoint, self._headers(), json.dumps(payload).encode("utf-8"), self.timeout_s)


def _first_text(result: dict) -> str:
    for block in result.get("content") or []:
        if isinstance(block, dict) and block.get("type") == "text":
            return str(block.get("text") or "")
    return ""


@dataclass(frozen=True)
class AtelierOrderState:
    """
    Status of one Atelier order as seen over ``get_order_status``. A text is final only when the order is ``done`` and the
    ``outcome`` of its current run is set (it is set after everything is stored). A done order that is ``failed`` or ``stopped``
    is final even without an outcome, because it may have ended before a run started.
    """

    state: str
    done: bool = False
    outcome: str | None = None

    @property
    def is_final(self) -> bool:
        return self.done and (bool(self.outcome) or self.state in UNDELIVERABLE_STATES)

    @property
    def needs_answers(self) -> bool:
        return self.state == NEEDS_INPUT


NEEDS_INPUT = "needs_input"
UNDELIVERABLE_STATES = frozenset({"failed", "stopped"})
DELIVERED_OUTCOMES = frozenset({"Converged", "DeliveredWithOpenPoints"})


class AtelierToolAdapter:
    """
    Maps the steps the eval needs (submit, skip questions, status, result) onto the order tools of the Atelier:
    ``submit_order``, ``answer_order`` (always ``skip=true``: nobody is there to answer), ``get_order_status`` (the server
    waits up to ``wait_seconds`` for a change) and ``get_order_result``.
    """

    def __init__(self, client: McpClient, wait_seconds: int = 25):
        self.client = client
        self.wait_seconds = wait_seconds

    def submit(self, brief: str, attachments: tuple[Attachment, ...]) -> str:
        arguments: dict = {"request": brief}
        if attachments:
            arguments["attachments"] = json.dumps([
                {
                    "filename": a.name,
                    "contentType": a.content_type,
                    "contentBase64": base64.b64encode(a.as_bytes()).decode("ascii"),
                }
                for a in attachments
            ])
        reply = self.client.call_tool("submit_order", arguments)
        order_id = reply.get("orderId") if isinstance(reply, dict) else None
        if not order_id:
            raise McpError(f"Antwort von submit_order enthält keine orderId: {str(reply)[:200]}")
        return str(order_id)

    def skip_questions(self, order_id: str) -> None:
        self.client.call_tool("answer_order", {"orderId": order_id, "skip": True})

    def state(self, order_id: str) -> AtelierOrderState:
        reply = self.client.call_tool("get_order_status", {"orderId": order_id, "waitSeconds": self.wait_seconds})
        if not isinstance(reply, dict):
            raise McpError(f"Unerwartete Statusantwort: {str(reply)[:200]}")
        outcome = reply.get("outcome")
        return AtelierOrderState(str(reply.get("state")), reply.get("done") is True,
                                 outcome.strip() if isinstance(outcome, str) and outcome.strip() else None)

    def result_text(self, order_id: str) -> str | None:
        reply = self.client.call_tool("get_order_result", {"orderId": order_id})
        if not isinstance(reply, dict):
            return None
        text = reply.get("text")
        return text if isinstance(text, str) and text.strip() else None
