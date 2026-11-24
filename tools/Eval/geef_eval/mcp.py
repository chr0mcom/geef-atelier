"""Minimal MCP client over Streamable HTTP (JSON-RPC, JSON or SSE replies) and the Atelier tool adapter."""
from __future__ import annotations

import base64
import itertools
import json
import urllib.error
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


def urllib_transport(url: str, headers: dict[str, str], body: bytes, timeout_s: float) -> HttpReply:
    """POST via the standard library."""
    request = urllib.request.Request(url, data=body, headers=headers, method="POST")
    try:
        with urllib.request.urlopen(request, timeout=timeout_s) as response:
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
class AtelierRunState:
    """Status of one Atelier run as seen over MCP."""

    status: str
    error: str | None = None


TERMINAL_STATUSES = frozenset({"Completed", "Failed", "Aborted"})


class AtelierToolAdapter:
    """
    Maps the three steps the eval needs (submit, status, result) onto the Atelier's MCP tools. The tool
    names will change in later stages (``submit_order`` …); a subclass or ``tool_names`` override keeps
    the rest of the tool untouched.
    """

    DEFAULT_TOOL_NAMES = {"submit": "submit_request", "status": "get_run_status", "result": "get_run_result"}

    def __init__(self, client: McpClient, tool_names: dict[str, str] | None = None, crew_template: str | None = None):
        self.client = client
        self.tool_names = {**self.DEFAULT_TOOL_NAMES, **(tool_names or {})}
        self.crew_template = crew_template

    def submit(self, brief: str, attachments: tuple[Attachment, ...]) -> str:
        arguments: dict = {"briefingText": brief}
        if self.crew_template:
            arguments["crewTemplate"] = self.crew_template
        if attachments:
            arguments["attachments"] = json.dumps([
                {
                    "filename": a.name,
                    "contentType": a.content_type,
                    "contentBase64": base64.b64encode(a.as_bytes()).decode("ascii"),
                }
                for a in attachments
            ])
        reply = self.client.call_tool(self.tool_names["submit"], arguments)
        run_id = reply.get("runId") if isinstance(reply, dict) else None
        if not run_id:
            raise McpError(f"Antwort von {self.tool_names['submit']} enthält keine runId: {str(reply)[:200]}")
        return str(run_id)

    def state(self, run_id: str) -> AtelierRunState:
        reply = self.client.call_tool(self.tool_names["status"], {"runId": run_id})
        if not isinstance(reply, dict):
            raise McpError(f"Unerwartete Statusantwort: {str(reply)[:200]}")
        return AtelierRunState(str(reply.get("status")), reply.get("errorMessage"))

    def result_text(self, run_id: str) -> str | None:
        reply = self.client.call_tool(self.tool_names["result"], {"runId": run_id})
        if not isinstance(reply, dict):
            return None
        text = reply.get("finalText")
        return text if isinstance(text, str) and text.strip() else None
