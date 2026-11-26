"""CLI-Proxy: OpenAI-compatible HTTP API backed by claude and codex CLIs."""
from __future__ import annotations

import asyncio
import base64
import logging
import os
import shutil
import time
import uuid
from datetime import datetime, timezone
from contextlib import aclosing, asynccontextmanager

import claude_adapter
import cli_process
import codex_adapter
import failover
import model_routing
import policy
from adapters import get_adapter
from fastapi import FastAPI, HTTPException, Request, Response
from fastapi.responses import JSONResponse, StreamingResponse
from openai_format import (
    ChatCompletionRequest,
    ChatCompletionResponse,
    ImageGenerationRequest,
    ToolChoiceObject,
    UsageInfo,
    png_dimensions,
    make_refusal_response,
    make_text_response,
    make_tool_calls_response,
    make_tool_response,
    message_images,
    message_text,
    usage_info,
)
from structured import build_response_format_instruction, validate_and_extract
from streaming import ClosingStreamingResponse, sse_response
from usage import UsageParts
from cli_errors import EmptyResultError, is_empty_document_result
from disconnect_guard import DisconnectGuard
from errors import OpenAIError, install_error_handlers
from provider_sync import ProviderConfigSync
from tool_use_parser import (
    build_agentic_tool_system_prompt,
    build_decision_file_instruction,
    build_required_tool_system_prompt,
    build_tool_system_prompt,
    extract_json,
    parse_agentic_responses,
    parse_decision,
)
from workspace import ephemeral_workspace

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger(__name__)

# ---------------------------------------------------------------------------
# Provider sync setup
# ---------------------------------------------------------------------------

BACKEND_URL = os.getenv("ATELIER_BACKEND_URL", "http://web:8080")
INTERNAL_TOKEN = os.getenv("ATELIER_INTERNAL_API_TOKEN", "")
provider_sync = ProviderConfigSync(BACKEND_URL, INTERNAL_TOKEN)


MODEL_WARMUP_INTERVAL_SECONDS = int(os.getenv("MODEL_WARMUP_INTERVAL", "43200"))


async def warm_model_caches(bypass_cache: bool = False) -> None:
    """Resolve both CLI model lists so callers hit a warm cache.

    Without this the first `/v1/*/models` request after a restart pays for three cold CLI
    probes, which no interactive caller can wait for. Failures are logged and swallowed:
    a cold cache degrades the model picker, it must never keep the gateway from starting.

    `bypass_cache` decides whether this is a warm-up or a re-resolution. The start-up call leaves
    it False — the cache is empty anyway. The periodic call MUST set it True: the resolver caches
    for 24 h, so a 12 h sweep that honours that cache would only re-read its own entry and could
    never discover a newly released model. The sweep also passes `force`, because it is the only
    remaining path that discovers a new model during an outage and the attempt throttle — which
    exists to contain repeated clicks — must not swallow it.
    """
    for name, resolver in (
        ("claude", claude_adapter.list_models_with_source_async),
        ("codex", codex_adapter.list_models_with_source_async),
    ):
        try:
            result = await resolver(bypass_cache, force=bypass_cache)
            log.info("Model cache warm-up for %s: source=%s (bypass=%s)", name, result[1], bypass_cache)
        except Exception as exc:  # noqa: BLE001 — warm-up is best-effort by design
            log.warning("Model cache warm-up for %s failed: %s", name, exc)


async def model_warmup_loop(interval: int = MODEL_WARMUP_INTERVAL_SECONDS) -> None:
    """Re-resolve both model caches every `interval` seconds so a released model shows up
    without anyone having to restart the gateway or click anything."""
    while True:
        await asyncio.sleep(interval)
        await warm_model_caches(bypass_cache=True)


@asynccontextmanager
async def lifespan(app: FastAPI):  # noqa: ANN001
    # Re-initialise semaphores after the event loop is running.
    claude_adapter._semaphore = asyncio.Semaphore(claude_adapter.MAX_CONCURRENT)
    claude_adapter._probe_semaphore = asyncio.Semaphore(claude_adapter._PROBE_CONCURRENCY)
    codex_adapter._semaphore = asyncio.Semaphore(codex_adapter.MAX_CONCURRENT)
    codex_adapter._image_semaphore = asyncio.Semaphore(codex_adapter.IMAGE_MAX_CONCURRENT)
    # Sync provider configs from backend (best-effort; failure is non-fatal).
    await provider_sync.sync_now()
    # Handles are retained: asyncio keeps only weak references to running tasks, so a bare
    # create_task can be collected mid-flight and the loop silently stops.
    background_tasks = {
        asyncio.create_task(provider_sync.background_sync_loop(interval=60)),
        # Warm the model caches in the background so start-up is not blocked by CLI probes.
        asyncio.create_task(warm_model_caches()),
        asyncio.create_task(model_warmup_loop()),
    }
    app.state.background_tasks = background_tasks
    try:
        yield
    finally:
        for task in background_tasks:
            task.cancel()


app = FastAPI(title="Atelier CLI Proxy", version="1.0.0", lifespan=lifespan)
install_error_handlers(app)

# Total concurrency the CLIs allow (claude + codex semaphores). Used for informational
# rate-limit headers and the optional overload guard.
_TOTAL_CONCURRENCY = claude_adapter.MAX_CONCURRENT + codex_adapter.MAX_CONCURRENT
# Optional overload protection: reject with 429 once this many requests are in flight.
# 0 (default) disables it — requests then queue on the per-CLI semaphores as before.
_MAX_INFLIGHT = int(os.getenv("CLI_PROXY_MAX_INFLIGHT", "0"))
_inflight = 0


@app.middleware("http")
async def _request_context(request: Request, call_next):
    """Attach x-request-id, openai-processing-ms and informational rate-limit headers."""
    request_id = (request.headers.get("x-request-id") or getattr(request.state, "request_id", None)
                  or f"req_{uuid.uuid4().hex}")
    start = time.monotonic()
    response = await call_next(request)
    response.headers["x-request-id"] = request_id
    response.headers["openai-processing-ms"] = str(int((time.monotonic() - start) * 1000))
    response.headers["x-ratelimit-limit-requests"] = str(_TOTAL_CONCURRENCY)
    response.headers["x-ratelimit-remaining-requests"] = str(max(0, _TOTAL_CONCURRENCY - _inflight))
    return response


# Outermost: cancels a request whose caller disconnected, so its CLI process group is ended
# (see disconnect_guard.py). Added after _request_context so that it wraps it.
app.add_middleware(DisconnectGuard)


@asynccontextmanager
async def _inflight_guard():
    """Optional overload guard: raise 429 when too many requests are concurrently in flight."""
    global _inflight
    if _MAX_INFLIGHT and _inflight >= _MAX_INFLIGHT:
        raise OpenAIError(
            "The proxy is at capacity. Please retry after a short delay.",
            status_code=429, type="rate_limit_error", code="rate_limit_exceeded",
        )
    _inflight += 1
    try:
        yield
    finally:
        _inflight -= 1


# ---------------------------------------------------------------------------
# Routing helpers
# ---------------------------------------------------------------------------

_CLAUDE_PREFIXES = ("claude", "anthropic/claude")
_CODEX_PREFIXES = ("gpt-", "o1-", "o3-", "o4-", "openai/")


def _route_model(model: str) -> str:
    """Returns 'claude' or 'codex' based on the model name."""
    lower = model.lower()
    if any(lower.startswith(p) for p in _CLAUDE_PREFIXES):
        return "claude"
    if any(lower.startswith(p) for p in _CODEX_PREFIXES):
        return "codex"
    # Default to claude for unknown models.
    return "claude"


async def _resolve_codex_alias(req: ChatCompletionRequest) -> ChatCompletionRequest:
    """Swap an always-latest codex alias (`gpt-sol-latest`) for the concrete model id.

    Done at the entry point so that the CLI call, the response `model` field and the SSE frames
    all carry the concrete id. Anything that is not a known alias passes through untouched.
    """
    resolved = await codex_adapter.resolve_model_alias(req.model)
    if resolved == req.model:
        return req
    log.info("codex alias %s resolved to %s", req.model, resolved)
    return req.model_copy(update={"model": resolved.split("/")[-1]})


_API_KEYS = {k.strip() for k in os.getenv("CLI_PROXY_API_KEYS", "").split(",") if k.strip()}


def _check_auth(request: Request) -> None:
    """Bearer-token auth, opt-in via CLI_PROXY_API_KEYS. No keys configured → open (back-compat)."""
    if not _API_KEYS:
        return
    header = request.headers.get("authorization", "")
    token = header[7:].strip() if header[:7].lower() == "bearer " else ""
    if token not in _API_KEYS:
        raise OpenAIError(
            "Invalid API key provided.",
            status_code=401, type="authentication_error", code="invalid_api_key",
        )


def _begin_timeout(request: Request) -> None:
    """Start the caller's time limit of this request (`x-cli-proxy-timeout-seconds`), capped by
    CLI_TIMEOUT_SECONDS. Without the header (or with a value at or above the cap) nothing changes."""
    try:
        seconds = cli_process.parse_timeout(request.headers.get(cli_process.TIMEOUT_HEADER))
    except ValueError as exc:
        raise OpenAIError(str(exc), status_code=400, code="invalid_header",
                          param=cli_process.TIMEOUT_HEADER) from exc
    cli_process.set_deadline(seconds, CLI_TIMEOUT_SECONDS)


#: Hard cap of one CLI run (both adapters read the same variable); the caller's limit can only shorten it.
CLI_TIMEOUT_SECONDS = codex_adapter.CLI_TIMEOUT_SECONDS


def _caller_timeout(target: str, exc: cli_process.CallerTimeoutError) -> OpenAIError:
    """504 `cli_timeout` for a CLI run stopped at the caller's own limit.

    Not a backend fault: it is counted in /health in fields of its own (`caller_timeouts`), moves
    no breaker, leaves the picture of real faults alone, and the request is not handed over -- the
    caller has stopped waiting."""
    failover.record_caller_timeout(target, str(exc))
    trace = cli_process.current_trace()
    log.warning("caller time limit reached, CLI process group stopped | request_id=%s | backend=%s | after=%.1fs",
                trace.request_id if trace else "-", target,
                time.monotonic() - trace.started if trace else 0.0)
    return OpenAIError(str(exc), status_code=504, type="api_error", code="cli_timeout",
                       headers={"x-cli-proxy-provider": target})


def _begin_request(req: ChatCompletionRequest) -> None:
    """Validate the request and record what it may use. Runs first in every entry point."""
    _validate_policy(req)
    policy.set_web_search(req.web_search_options is not None)


def _validate_policy(req: ChatCompletionRequest) -> None:
    """Enforce the honest param policy: reject output features the CLI backend cannot
    satisfy (faking them would mean fabricated data or multiplied subscription cost).
    Sampling hints (temperature/top_p/seed/penalties) are accepted and silently ignored.
    """
    if req.logprobs or req.top_logprobs:
        raise OpenAIError(
            "logprobs are not supported by the CLI backend (token probabilities are unavailable).",
            status_code=400, code="unsupported_parameter", param="logprobs",
        )
    if req.n is not None and req.n != 1:
        raise OpenAIError(
            "n>1 is not supported by the CLI backend (each completion is one subscription-billed "
            "CLI invocation).",
            status_code=400, code="unsupported_parameter", param="n",
        )
    if req.reasoning_effort is not None and req.reasoning_effort not in REASONING_EFFORTS:
        # Both CLIs verifiably understand only these three levels (claude --effort has no
        # `minimal`, codex's acceptance of it is unproven). Silently dropping an unknown
        # value would let the caller believe a level was applied that never was.
        raise OpenAIError(
            f"reasoning_effort {req.reasoning_effort!r} is not supported by the CLI backend; "
            f"use one of: {', '.join(REASONING_EFFORTS)}.",
            status_code=400, code="unsupported_value", param="reasoning_effort",
        )


REASONING_EFFORTS: tuple[str, ...] = ("low", "medium", "high")
REASONING_EFFORT_HEADER = "x-cli-proxy-reasoning-effort"


def _effort_kwargs(req: ChatCompletionRequest) -> dict[str, str]:
    """Adapter kwargs for the per-request reasoning effort — empty when the caller sent none.

    Passing nothing (rather than reasoning_effort=None) keeps every adapter call, and so every
    CLI argv, byte-identical for callers that do not use the field (Geef.Atelier, DMS, Hermes).
    """
    return {"reasoning_effort": req.reasoning_effort} if req.reasoning_effort else {}


# ---------------------------------------------------------------------------
# Request processing
# ---------------------------------------------------------------------------

def _image_references(content) -> str:
    """Best-effort multimodal: pass http(s) image URLs as references (claude can WebFetch
    them); data:-URLs cannot be reached by the CLI and are dropped with a warning."""
    images = message_images(content)
    if not images:
        return ""
    refs: list[str] = []
    dropped = 0
    for img in images:
        url = str(img.get("url", ""))
        if url.startswith("http://") or url.startswith("https://"):
            refs.append(f"[IMAGE: {url}]")
        else:
            dropped += 1
    if dropped:
        log.warning("Dropping %d inline (data:) image(s) — not reachable by the CLI backend", dropped)
    return "\n".join(refs)


def _render_conversation(req: ChatCompletionRequest) -> str:
    """Renders the message list into a plain-text transcript (shared by the prompt builder and
    the agentic decision-file instruction). Multi-turn tool calls and tool results are rendered
    as [TOOL CALLS] / [TOOL RESULT: ...] blocks so the model can continue an agentic loop."""
    parts: list[str] = []

    for msg in req.messages:
        role = msg.role.upper()
        text = message_text(msg.content)
        image_refs = _image_references(msg.content)
        if image_refs:
            text = (text + "\n" + image_refs).strip() if text else image_refs
        if msg.role == "tool":
            # Tool result — render with tool name and call ID context
            tool_name = msg.name or "tool"
            parts.append(f"[TOOL RESULT: {tool_name}]\n{text}")
        elif msg.role == "assistant" and msg.tool_calls:
            # Replay a prior assistant turn that issued tool calls (multi-turn agentic loop).
            calls = "\n".join(
                f"- {c.get('function', {}).get('name', '?')}"
                f"({c.get('function', {}).get('arguments', '')})"
                for c in msg.tool_calls
            )
            block = f"[ASSISTANT]\n{text}" if text else "[ASSISTANT]"
            parts.append(f"{block}\n[TOOL CALLS]\n{calls}")
        elif msg.role == "assistant" and not text:
            # Empty assistant message with no tool calls — nothing to add.
            continue
        else:
            parts.append(f"[{role}]\n{text}")

    return "\n\n".join(parts)


def _build_prompt(req: ChatCompletionRequest) -> str:
    """Concatenates messages into a plain-text prompt for the CLI."""
    prompt = _render_conversation(req)

    mode = _tool_choice_mode(req)
    if req.tools and mode != "none":
        tools_dicts = [t.model_dump() for t in req.tools]
        if mode == "specific":
            # Force the single named tool (constrain to its schema).
            named = [t for t in tools_dicts if t.get("function", {}).get("name") == _extract_tool_name(req)]
            prompt += build_tool_system_prompt(named or tools_dicts)
        elif mode == "required":
            prompt += build_required_tool_system_prompt(tools_dicts)
        else:  # auto
            prompt += build_agentic_tool_system_prompt(tools_dicts)

    return prompt


def _tool_choice_mode(req: ChatCompletionRequest) -> str:
    """Classify tool_choice: 'absent' | 'none' | 'auto' | 'required' | 'specific'."""
    tc = req.tool_choice
    if tc is None:
        return "absent" if not req.tools else "auto"
    if isinstance(tc, ToolChoiceObject):
        return "specific"
    if isinstance(tc, str):
        if tc == "none":
            return "none"
        if tc == "required":
            return "required"
        return "auto"  # "auto" or any unknown string
    return "auto"


def _extract_parts(req: ChatCompletionRequest) -> tuple[str, str]:
    """Extracts (system_prompt, user_instruction) from the message list."""
    system_parts: list[str] = []
    user_parts: list[str] = []
    for msg in req.messages:
        text = message_text(msg.content)
        if msg.role in ("system", "developer"):
            system_parts.append(text)
        elif msg.role in ("user", "assistant"):
            user_parts.append(f"[{msg.role.upper()}]\n{text}")
    return "\n\n".join(system_parts), "\n\n".join(user_parts)


def _extract_tool_name(req: ChatCompletionRequest) -> str | None:
    """Returns the requested tool function name, if any."""
    if not req.tool_choice or not req.tools:
        return None
    if isinstance(req.tool_choice, str):
        return None
    return req.tool_choice.function.name


def _to_usage_info(parts: UsageParts) -> UsageInfo:
    """Convert an adapter UsageParts into the OpenAI UsageInfo response model."""
    return usage_info(
        input_tokens=parts.input_tokens,
        output_tokens=parts.output_tokens,
        cached_tokens=parts.cached_tokens,
        reasoning_tokens=parts.reasoning_tokens,
    )


_JSON_RETRY_SUFFIX = (
    "\n\nCRITICAL: Your previous response did not contain valid JSON. "
    "You MUST respond with ONLY a valid JSON object — no explanation, no prose, no markdown fences. "
    "Start your response with '{' and end it with '}'. Nothing else."
)


def _build_response(
    req: ChatCompletionRequest, raw_text: str, usage: UsageInfo | None = None
) -> ChatCompletionResponse:
    """Converts raw CLI output into an OpenAI-compatible response."""
    mode = _tool_choice_mode(req)

    # Forced single named tool: extract one JSON object as its arguments.
    if mode == "specific":
        tool_name = _extract_tool_name(req)
        json_str = extract_json(raw_text)
        if json_str and tool_name:
            return make_tool_response(req.model, tool_name, json_str, usage)
        log.warning("Tool-use requested but JSON extraction failed; raw=%r", raw_text[:200])
        return make_text_response(req.model, raw_text, usage)

    # Agentic / required: model may emit one or several tool calls (or final text).
    if req.tools and mode in ("auto", "required"):
        calls = parse_agentic_responses(raw_text)
        if calls:
            log.info("Agentic tool call(s) detected: %s", [c[0] for c in calls])
            return make_tool_calls_response(req.model, calls, usage)

    return make_text_response(req.model, raw_text, usage)


def _needs_json_retry(resp: ChatCompletionResponse, req: ChatCompletionRequest) -> bool:
    """Returns True when a tool was expected but the response lacks tool_calls."""
    return bool(_extract_tool_name(req)) and resp.choices[0].finish_reason != "tool_calls"


# ---------------------------------------------------------------------------
# Request processing helpers (adapter-level, no routing logic)
# ---------------------------------------------------------------------------

async def _complete_backend(
    backend: str, prompt: str, model: str, max_tokens: int | None,
    effort: dict[str, str] | None = None,
) -> tuple[str, UsageParts]:
    adapter = claude_adapter if backend == "claude" else codex_adapter
    return await adapter.complete_with_usage(prompt, model, max_tokens, **(effort or {}))


def _env_flag(name: str, default: bool) -> bool:
    raw = os.getenv(name)
    if raw is None or raw.strip() == "":
        return default
    return raw.strip().lower() in ("1", "true", "yes", "on")


def _structured_lean_retry_enabled() -> bool:
    """CLAUDE_STRUCTURED_LEAN_RETRY (default on): the one retry of an invalid claude
    structured output runs in lean mode instead of repeating the agent-mode call."""
    return _env_flag("CLAUDE_STRUCTURED_LEAN_RETRY", True)


def _lean_chat_enabled() -> bool:
    """CLAUDE_LEAN_CHAT (default on; 0 restores the agent-session behaviour of before
    2026-11-07): plain chat (no tools, no document mode, not token-streamed) and
    structured-output requests to claude run in lean mode from the first attempt on, so the
    gateway acts like a plain OpenAI API and not like a Claude Code agent."""
    return _env_flag("CLAUDE_LEAN_CHAT", True)


def _has_image_refs(req: ChatCompletionRequest) -> bool:
    return any(message_images(m.content) for m in req.messages)


def _lean_parts(req: ChatCompletionRequest) -> tuple[str, str]:
    """(system prompt, prompt) for a lean claude call: system/developer messages become the
    real system prompt, everything else is rendered exactly like the agent-mode prompt."""
    system = "\n\n".join(
        message_text(m.content) for m in req.messages if m.role in ("system", "developer")
    ).strip()
    rest = req.model_copy(update={
        "messages": [m for m in req.messages if m.role not in ("system", "developer")]})
    prompt = _build_prompt(rest)
    if req.response_format is not None and req.response_format.type != "text":
        prompt += build_response_format_instruction(req.response_format)
    return system or claude_adapter.LEAN_DEFAULT_SYSTEM_PROMPT, prompt


async def _complete_claude_lean(
    req: ChatCompletionRequest, max_tokens: int | None, effort: dict[str, str] | None,
    suffix: str = "",
) -> tuple[str, UsageParts]:
    system, prompt = _lean_parts(req)
    return await claude_adapter.complete_with_usage(
        prompt + suffix, req.model, max_tokens, **(effort or {}),
        system_prompt=system, image_refs=_has_image_refs(req))


_LEAN_JSON_RETRY_SUFFIX = (
    "\n\nReply with the JSON object only: start with '{' and end with '}'."
)


async def _structured_completion(
    req: ChatCompletionRequest, cli: str, prompt: str, max_tokens: int | None,
    effort: dict[str, str] | None,
) -> tuple[str | None, str | None, UsageParts]:
    """Structured output with one retry. Returns (json_str, error, summed usage).

    claude: an invalid first answer is retried in lean mode (CLAUDE_STRUCTURED_LEAN_RETRY),
    because the typical failure is the agent persona declining in prose — repeating the
    same agent-mode call with a louder "CRITICAL" reminder failed just as often (2026-11-07).
    codex: unchanged (same call plus the JSON reminder).
    """
    rf = req.response_format
    lean_first = cli == "claude" and _lean_chat_enabled()
    if lean_first:
        raw, parts = await _complete_claude_lean(req, max_tokens, effort)
    else:
        raw, parts = await _complete_backend(cli, prompt, req.model, max_tokens, effort)
    json_str, err = validate_and_extract(rf, raw)
    if err is None:
        return json_str, None, parts

    lean_retry = cli == "claude" and (lean_first or _structured_lean_retry_enabled())
    log.warning("%s structured output invalid (%s) — retrying%s",
                cli, err, " in lean mode" if lean_retry else "")
    if lean_retry:
        raw, retry_parts = await _complete_claude_lean(
            req, max_tokens, effort, _LEAN_JSON_RETRY_SUFFIX if lean_first else "")
    else:
        raw, retry_parts = await _complete_backend(
            cli, prompt + _JSON_RETRY_SUFFIX, req.model, max_tokens, effort)
    parts = _sum_usage(parts, retry_parts)  # both runs were billed
    json_str, err = validate_and_extract(rf, raw)
    if err is not None:
        log.warning("%s structured output still invalid after retry (%s)", cli, err)
    return json_str, err, parts


def _sum_usage(a: UsageParts, b: UsageParts) -> UsageParts:
    """Combined accounting for a retried call: both CLI runs happened and were billed."""
    cost = None
    if a.cost_usd is not None or b.cost_usd is not None:
        cost = (a.cost_usd or 0.0) + (b.cost_usd or 0.0)
    return UsageParts(
        input_tokens=a.input_tokens + b.input_tokens,
        output_tokens=a.output_tokens + b.output_tokens,
        cached_tokens=a.cached_tokens + b.cached_tokens,
        reasoning_tokens=a.reasoning_tokens + b.reasoning_tokens,
        cost_usd=cost,
    )


_DECISION_RETRY_SUFFIX = (
    "\n\nIMPORTANT: Your previous attempt did not produce a usable decision.json. Write a file "
    "named decision.json in the current directory containing ONLY one JSON object of the form "
    '{"tool_call": {"name": "...", "arguments": {...}}} (or {"tool_calls": [...]}), or '
    '{"final": "..."} — no markdown, no prose, nothing else.'
)


def _decision_unacceptable(kind: str, payload, mode: str, specific: str | None) -> bool:
    """Whether a parsed decision warrants a single retry (empty/invalid, or a required/specific
    tool turn that did not produce the mandated tool call)."""
    if kind == "none":
        return True
    if mode == "required" and kind != "tool_calls":
        return True
    if mode == "specific":
        if kind != "tool_calls":
            return True
        names = [name for name, _ in payload]
        if specific and specific not in names:
            return True
    return False


async def _agentic_file_decision(
    req: ChatCompletionRequest, backend: str,
) -> tuple[str, object, UsageParts]:
    """Runs the decision-file flow of `backend` (with one retry) and returns
    (kind, payload, usage).

    kind is 'tool_calls' (payload = [(name, args_json), ...]) or 'final' (payload = answer text).
    A 'none' parse is normalised to ('final', raw_text) so the caller always gets a usable answer.
    """
    adapter = claude_adapter if backend == "claude" else codex_adapter
    conversation = _render_conversation(req)
    mode = _tool_choice_mode(req)
    specific = _extract_tool_name(req)
    tools_dicts = [t.model_dump() for t in req.tools]
    instruction = build_decision_file_instruction(conversation, tools_dicts, mode, specific)

    log.info("Agentic file mode | backend=%s | model=%s | mode=%s | tools=%d",
             backend, req.model, mode, len(tools_dicts))

    decision, parts = await adapter.complete_agentic_file(
        instruction, req.model, **_effort_kwargs(req))
    kind, payload = parse_decision(decision)

    if _decision_unacceptable(kind, payload, mode, specific):
        log.warning("Agentic file mode (%s): decision unacceptable (kind=%s, mode=%s) — retrying once",
                    backend, kind, mode)
        decision, retry_parts = await adapter.complete_agentic_file(
            instruction + _DECISION_RETRY_SUFFIX, req.model, **_effort_kwargs(req))
        # Both runs were billed — the usage block must count them both (review 2026-07-24:
        # overwriting `parts` under-reported every retried request by a full CLI run).
        parts = _sum_usage(parts, retry_parts)
        kind, payload = parse_decision(decision)

    if kind == "none":
        # No structured decision even after retry — surface the raw text as a final answer.
        return ("final", decision, parts)
    return (kind, payload, parts)


async def _run_agentic_file(req: ChatCompletionRequest, backend: str) -> ChatCompletionResponse:
    """Non-streaming agentic tool use via decision-file authoring (both backends).

    The CLI writes the next tool call (or a final answer) into decision.json instead of acting
    or hallucinating output itself — the document-mode framing sidesteps claude-code's injection
    refusal of the inline "emit tool-call JSON" protocol, and codex's "these tools are not
    connected" persona objection (incident 2026-07-24: the claude→codex failover left every
    agentic consumer without tools). Returns a standard OpenAI tool_calls / content response,
    so callers (Hermes, Geef.Atelier) are unchanged.
    """
    kind, payload, parts = await _agentic_file_decision(req, backend)
    usage = _to_usage_info(parts)
    if kind == "tool_calls":
        log.info("Agentic file decision (%s): tool call(s) %s", backend, [c[0] for c in payload])
        return make_tool_calls_response(req.model, payload, usage)
    return make_text_response(req.model, payload, usage)


def _is_agentic_tool_request(req: ChatCompletionRequest, backend: str, structured: bool) -> bool:
    """The file-authoring path applies to open-ended tool use (auto/required), where the model
    would otherwise try to execute action tools itself, hallucinate their output, or refuse
    ("tools not connected"). Since 2026-07-24 it covers BOTH backends — the claude→codex
    failover must keep agentic consumers (Hermes) working.

    Excluded:
    - 'specific' tool_choice (a forced single tool, e.g. Geef's submit_review) — that is
      structured-output-like; the print-mode path produces the JSON reliably and is well tested.
    - structured-output calls — response_format takes precedence."""
    return (
        bool(req.tools)
        and not structured
        and _tool_choice_mode(req) in ("auto", "required")
    )


async def _run_completion(req: ChatCompletionRequest, backend: str) -> ChatCompletionResponse:
    """Non-streaming completion: build prompt, apply response_format/tool handling, retry once."""
    rf = req.response_format
    structured = rf is not None and rf.type != "text"

    # Agentic tool use (both backends): author the next tool call into a file rather than
    # acting/hallucinating/refusing in print mode.
    if _is_agentic_tool_request(req, backend, structured):
        return await _run_agentic_file(req, backend)

    prompt = _build_prompt(req)
    if structured:
        prompt += build_response_format_instruction(rf)
    max_tokens = req.effective_max_tokens()

    log.info("Dispatching to %s | model=%s | tool=%s | structured=%s",
             backend, req.model, _extract_tool_name(req), structured)

    effort = _effort_kwargs(req)

    # Structured outputs take precedence over tool extraction.
    if structured:
        json_str, err, parts = await _structured_completion(req, backend, prompt, max_tokens, effort)
        usage = _to_usage_info(parts)
        if json_str is not None:
            return make_text_response(req.model, json_str, usage)
        return make_refusal_response(
            req.model, f"Could not produce valid structured output: {err}", usage)

    if backend == "claude" and not req.tools and _lean_chat_enabled():
        raw, parts = await _complete_claude_lean(req, max_tokens, effort)
        return _build_response(req, raw, _to_usage_info(parts))

    raw, parts = await _complete_backend(backend, prompt, req.model, max_tokens, effort)
    usage = _to_usage_info(parts)
    resp = _build_response(req, raw, usage)
    if _needs_json_retry(resp, req):
        log.warning("%s JSON extraction failed — retrying with explicit JSON reminder", backend)
        raw, retry_parts = await _complete_backend(
            backend, prompt + _JSON_RETRY_SUFFIX, req.model, max_tokens, effort)
        parts = _sum_usage(parts, retry_parts)  # both runs were billed
        resp = _build_response(req, raw, _to_usage_info(parts))
    return resp


async def _call_claude(req: ChatCompletionRequest) -> ChatCompletionResponse:
    if req.document_mode:
        system_prompt, user_instruction = _extract_parts(req)
        log.info("Dispatching to claude (document mode) | model=%s", req.model)
        async with ephemeral_workspace(req.document or "") as ws:
            raw, parts = await claude_adapter.complete_document(
                system_prompt, user_instruction, req.document or "", ws, req.model,
                req.effective_max_tokens(), req.context_document, **_effort_kwargs(req),
            )
        if is_empty_document_result(raw, req.document or ""):
            raise EmptyResultError("claude")
        return make_text_response(req.model, raw, _to_usage_info(parts))
    return await _run_completion(req, "claude")


async def _call_codex(req: ChatCompletionRequest) -> ChatCompletionResponse:
    if req.document_mode:
        system_prompt, user_instruction = _extract_parts(req)
        log.info("Dispatching to codex (document mode) | model=%s", req.model)
        async with ephemeral_workspace(req.document or "") as ws:
            raw, parts = await codex_adapter.complete_document(
                system_prompt, user_instruction, req.document or "", ws, req.model,
                req.effective_max_tokens(), req.context_document, **_effort_kwargs(req),
            )
        if is_empty_document_result(raw, req.document or ""):
            raise EmptyResultError("codex")
        return make_text_response(req.model, raw, _to_usage_info(parts))
    return await _run_completion(req, "codex")


def _bind_account(target: str) -> None:
    """Select the Claude subscription (primary or alt) this attempt runs under. No-op for
    codex. Must run for EVERY claude-family attempt so no earlier binding carries over."""
    if failover.cli_of(target) == "claude":
        claude_adapter.bind_account(target)


def _more_targets(order: list[str], index: int, dead_cli: str | None) -> bool:
    """Whether a later chain member can still be tried. After a transport-level fault
    (hung or crashed CLI) the remaining accounts of the SAME CLI are not worth a try --
    they share the fault, and retrying only delays the hand-over to the other CLI."""
    return any(failover.cli_of(t) != dead_cli for t in order[index + 1:])


async def _call_backend(req: ChatCompletionRequest, backend: str) -> ChatCompletionResponse:
    _bind_account(backend)
    return await (_call_claude(req) if failover.cli_of(backend) == "claude" else _call_codex(req))


#: Model used when a request is handed over to codex by the failover (always-latest alias, D-066/10);
#: the same constant is the codex adapter's default for a request without a model.
FAILOVER_CODEX_MODEL = codex_adapter.FAILOVER_MODEL


def _retarget(req: ChatCompletionRequest, primary: str, target: str) -> ChatCompletionRequest:
    """Adapt the request for a backend the caller did not ask for.

    The model name must NOT travel across the backend boundary: handing
    `claude-haiku-latest` to `codex -m` is a guaranteed failure. A request handed to codex
    runs on `gpt-sol-latest`, which the codex adapter resolves to the newest listed Sol model:
    the container's codex config sets no default model, so an empty name would leave the
    model to whatever the CLI build defaults to (D-066/10). A request handed to claude gets
    an empty name, i.e. the claude CLI's own default.
    """
    if failover.cli_of(target) == failover.cli_of(primary):
        return req  # a second account of the same CLI speaks the same model names
    return req.model_copy(update={"model": FAILOVER_CODEX_MODEL if failover.cli_of(target) == "codex" else ""})


#: Request header that switches the failover off for one request (values off/0/false, any case).
#: The same name as the response header that reports a handover; a request never carries that one.
FAILOVER_CONTROL_HEADER = "x-cli-proxy-failover"
_FAILOVER_OFF_VALUES = frozenset({"off", "0", "false"})


def _failover_allowed(request: Request) -> bool:
    """False when the caller asked for no handover (`x-cli-proxy-failover: off`): its result
    depends on the model, so a different backend's answer is worse than an error. Without the
    header, or with any other value, the gateway behaves exactly as before."""
    value = request.headers.get(FAILOVER_CONTROL_HEADER)
    return value is None or value.strip().lower() not in _FAILOVER_OFF_VALUES


def _backend_unavailable(backend: str) -> OpenAIError:
    """503 `backend_unavailable` for a request that may not leave `backend` while its breaker is open."""
    retry_after = max(1, int(failover.open_until(backend) - time.time()))
    return OpenAIError(
        f"Backend {backend} is temporarily unavailable after repeated failures, and failover "
        f"is switched off for this request. Retry in about {retry_after} s.",
        status_code=503, type="api_error", code="backend_unavailable",
        headers={"Retry-After": str(retry_after)},
    )


def _route_reason(backend: str, model: str) -> str | None:
    """Why the request belongs on the alternative Claude account (model routing), or None."""
    return model_routing.route_to_alt(model) if backend == "claude" else None


def _attempt_order(backend: str, allow_failover: bool, route_to_alt: bool = False) -> list[str]:
    """The failover chain, or only `backend` when the caller switched the failover off.

    An open breaker is honoured for such a request too: it is answered at once with 503
    `backend_unavailable` instead of being sent to a backend the breaker currently keeps out
    (or to its partner). Once the open window elapses one probe passes, as for every request.

    `route_to_alt` (a model only the alternative Claude account has) replaces the primary account
    by the alternative one in both cases; it is routing, so the failover switch does not stop it.
    """
    if allow_failover:
        return failover.attempt_order(backend, to_alt=route_to_alt)
    target = failover.ALT if route_to_alt and backend == "claude" else backend
    if not failover.available(target):
        raise _backend_unavailable(target)
    return [target]


def _not_in_plan_error(target: str, exc: BaseException) -> OpenAIError:
    """404 `model_not_found` when no Claude account may serve the model and no handover is left.

    404 rather than 429/502/503: the OpenAI SDKs and most callers retry those at once, but the
    subscription will not gain the model within seconds. The message is the CLI's own."""
    return OpenAIError(
        f"The requested model is not available on the Claude subscription(s) of this gateway: {exc}",
        status_code=404, type="invalid_request_error", code="model_not_found", param="model",
        headers={"x-cli-proxy-provider": target},
    )


def _requeue_after_not_in_plan(
    target: str, model: str, rest: list[str], tried: set[str], allow_failover: bool,
) -> tuple[list[str], str | None]:
    """Remember that `target` lacks `model` and return (remaining attempts, account switched to).

    The other Claude account comes next when it may have the model -- also with the failover off,
    since it is the same CLI and the same model. Claude accounts known to lack the model drop out.
    A sibling whose breaker is open is answered with 503 when the failover is off (as for every
    request) and otherwise goes behind the remaining members, like the breaker sorting does."""
    failover.mark_not_in_plan(target, model_routing.model_key(model))
    log.info("%s: model %s is not in the plan of this account (marked for %ss, no breaker change)",
             target, model or "(default)", failover.NOT_IN_PLAN_TTL_SECONDS)
    rest = [t for t in rest
            if not (failover.cli_of(t) == "claude" and model_routing.lacks(t, model))]
    other = model_routing.sibling_candidate(target, model, tried)
    if other is None:
        return rest, None
    rest = [t for t in rest if t != other]
    if failover.available(other):
        return [other, *rest], other
    if not allow_failover:
        raise _backend_unavailable(other)
    return [*rest, other], other


def _empty_result_error(backend: str, target: str, handed_over_flag: bool | None = None) -> OpenAIError:
    """502 `empty_result` for a document-mode run of `target` that produced no text."""
    is_handover = target != backend if handed_over_flag is None else handed_over_flag
    handed_over = f" after failover from {backend}" if is_handover else ""
    headers = {"x-cli-proxy-provider": target}
    if is_handover:
        headers["x-cli-proxy-failover"] = f"{backend}->{target}"
    return OpenAIError(
        f"Document mode returned no text: {target}{handed_over} finished without writing "
        "anything to the document (draft.md is still empty and unchanged). The request was "
        "not retried.",
        status_code=502, type="api_error", code="empty_result", headers=headers,
    )


async def _complete_with_failover(
    req: ChatCompletionRequest, backend: str, response: Response, allow_failover: bool = True,
) -> ChatCompletionResponse:
    """Serve the completion, handing over to the partner CLI when this one is faulted.

    Only auth/limit/transport faults trigger the handover; a client-side fault would
    fail identically on the partner and retrying it would just hide the bug.

    A document-mode run that exits normally without writing any text is answered with 502
    `empty_result` and is not handed over again. It is an error of this request only: whether the
    CLI writes the document depends on the request's content, so it is counted and reported in
    /health but moves no breaker -- neither the producing backend's nor, after a handover, the
    origin's (which already holds the origin's own fault).
    """
    route = _route_reason(backend, req.model)
    queue = _attempt_order(backend, allow_failover, route_to_alt=route is not None)
    routed: set[str] = set()
    origin = backend
    if route is not None:
        routed.add(failover.ALT)
        origin = failover.ALT
        model_routing.log_route(req.model, backend, failover.ALT, route)
    tried: set[str] = set()
    dead_cli: str | None = None
    while queue:
        target = queue.pop(0)
        if failover.cli_of(target) == dead_cli:
            continue
        tried.add(target)
        finished = False
        target_req = _retarget(req, backend, target)
        try:
            resp = await _call_backend(target_req, target)
            finished = True
        except cli_process.CallerTimeoutError as exc:
            finished = True
            raise _caller_timeout(target, exc) from exc
        except EmptyResultError as exc:
            finished = True
            error = _empty_result_error(backend, target, target != backend and target not in routed)
            failover.record_empty_result(target, error.message)
            log.error("%s document mode produced no text (%s)", target, exc)
            raise error from exc
        except (RuntimeError, OSError) as exc:
            finished = True
            if isinstance(exc, claude_adapter.NotInPlanError):
                queue, switched = _requeue_after_not_in_plan(
                    target, target_req.model, queue, tried, allow_failover)
                if switched is not None and backend == "claude":
                    routed.add(switched)
                    origin = switched
                    model_routing.log_route(target_req.model, target, switched, "not in plan (answer)")
                if not queue:
                    raise _not_in_plan_error(target, exc) from exc
                continue
            reason = failover.classify(exc)
            failover.record_failure(target, reason, str(exc))
            log.error("%s CLI call failed (%s): %s", target, reason, exc)
            if reason == "transport":
                dead_cli = failover.cli_of(target)
            if not failover.should_failover(reason) or not _more_targets([target, *queue], 0, dead_cli):
                raise HTTPException(status_code=502, detail=str(exc)) from exc
            continue
        finally:
            # A run cut short by the caller's disconnect (cancellation) is no finished attempt.
            if req.document_mode and finished:
                failover.record_document_attempt(target)
        failover.record_success(target)
        if failover.cli_of(target) == "claude":
            failover.clear_not_in_plan(target, [model_routing.model_key(target_req.model)])
        response.headers["x-cli-proxy-provider"] = target
        if req.reasoning_effort:
            response.headers[REASONING_EFFORT_HEADER] = req.reasoning_effort
            log.info("reasoning_effort=%s applied via %s", req.reasoning_effort, target)
        if target != backend and target not in routed:
            failover.record_failover(origin, target)
            response.headers["x-cli-proxy-failover"] = f"{backend}->{target}"
            # Echo the model the caller asked for -- OpenAI clients key off this field.
            resp.model = req.model
        return resp
    raise HTTPException(status_code=502, detail="no backend available")  # pragma: no cover


def _stream_completion(
    req: ChatCompletionRequest, backend: str, allow_failover: bool = True,
) -> StreamingResponse:
    """Streaming (SSE) completion. Tool/structured extraction needs the full text, so when
    those are requested we fall back to a single buffered delta; otherwise we stream tokens.

    Failover lives INSIDE the event generator: the HTTP 200 goes out before the CLI even
    runs, so an HTTP-level retry is impossible — but the agentic and buffered branches
    complete their backend call before yielding anything, and until the first event is
    yielded the stream can be handed to the partner seamlessly. Before 2026-07-24 stream
    failures bypassed the breaker entirely (no record_failure, /health stayed green, no
    watch alarm) while claude sat at its session limit — see docs/cli-proxy-failover.md.
    A fault after the first yielded event still surfaces in-band to the client, but now
    at least feeds the breaker so the NEXT request routes pre-emptively.
    """
    primary = backend
    route = _route_reason(primary, req.model)
    order = _attempt_order(primary, allow_failover, route_to_alt=route is not None)
    routed: set[str] = {failover.ALT} if route is not None else set()
    if route is not None:
        model_routing.log_route(req.model, primary, failover.ALT, route)

    prompt = _build_prompt(req)
    rf = req.response_format
    structured = rf is not None and rf.type != "text"
    if structured:
        prompt += build_response_format_instruction(rf)
    max_tokens = req.effective_max_tokens()
    include_usage = bool(req.stream_options and req.stream_options.include_usage)

    async def _serve(target: str, target_req: ChatCompletionRequest):
        cli_model = target_req.model
        cli = failover.cli_of(target)  # both Claude accounts run the same adapter
        effort = _effort_kwargs(target_req)
        # Agentic tool use (both backends): resolve the decision via file authoring, then emit
        # either proper streaming tool_calls or the final answer — no hallucination, no
        # injection refusal, no "tools not connected" persona objection. The claude→codex
        # failover keeps the SAME decision-file contract (incident 2026-07-24: the buffered
        # fallback yielded the tool prompt's answer as plain text deltas, so Hermes workers
        # saw zero tool calls and crashed with protocol violations).
        if _is_agentic_tool_request(target_req, cli, structured):
            kind, payload, parts = await _agentic_file_decision(target_req, cli)
            if kind == "tool_calls":
                tool_calls = [
                    {
                        "index": i,
                        "id": f"call_{uuid.uuid4().hex[:24]}",
                        "type": "function",
                        "function": {"name": name, "arguments": args},
                    }
                    for i, (name, args) in enumerate(payload)
                ]
                yield ("tool_calls", tool_calls)
            else:
                yield ("delta", payload)
            yield ("usage", parts)
        # When tools/structured output are requested, we must inspect the full text before
        # deciding tool_calls vs content — so buffer, then emit. Pure text streams token-by-token.
        elif structured:
            # Mirror the non-streaming contract: one retry on invalid structured output,
            # then a refusal — not the raw text as a fake success (review 2026-07-24).
            json_str, err, parts = await _structured_completion(
                target_req, cli, prompt, max_tokens, effort)
            if json_str is not None:
                yield ("delta", json_str)
            else:
                yield ("refusal", f"Could not produce valid structured output: {err}")
            yield ("usage", parts)
        elif target_req.tools:
            raw, parts = await _complete_backend(cli, prompt, cli_model, max_tokens, effort)
            if _extract_tool_name(target_req):
                # Forced single named tool (tool_choice=specific) — the SSE path must honour
                # the tool contract like the non-streaming one (review finding 2026-07-24:
                # it only ever yielded text deltas). One retry, mirroring _needs_json_retry.
                tool_name = _extract_tool_name(target_req)
                json_str = extract_json(raw)
                if json_str is None:
                    log.warning("%s specific-tool stream: JSON extraction failed — retrying", target)
                    raw, retry_parts = await _complete_backend(
                        cli, prompt + _JSON_RETRY_SUFFIX, cli_model, max_tokens, effort)
                    parts = _sum_usage(parts, retry_parts)  # both runs were billed
                    json_str = extract_json(raw)
                if json_str is not None:
                    yield ("tool_calls", [{
                        "index": 0,
                        "id": f"call_{uuid.uuid4().hex[:24]}",
                        "type": "function",
                        "function": {"name": tool_name, "arguments": json_str},
                    }])
                else:
                    yield ("delta", raw)
            else:
                yield ("delta", raw)
            yield ("usage", parts)
        else:
            adapter = claude_adapter if cli == "claude" else codex_adapter
            async with aclosing(adapter.stream(prompt, cli_model, max_tokens, **effort)) as events:
                async for ev in events:
                    yield ev

    async def _events():
        dead_cli: str | None = None
        queue = list(order)
        tried: set[str] = set()
        origin = failover.ALT if route is not None else primary
        while queue:
            target = queue.pop(0)
            if failover.cli_of(target) == dead_cli:
                continue
            tried.add(target)
            # The model name may not cross the backend boundary (see _retarget); the SSE
            # frames still echo the model the caller asked for.
            target_req = _retarget(req, primary, target)
            _bind_account(target)
            emitted = False
            try:
                async with aclosing(_serve(target, target_req)) as events:
                    async for ev in events:
                        emitted = True
                        yield ev
            except cli_process.CallerTimeoutError as exc:
                _caller_timeout(target, exc)
                raise
            except (RuntimeError, OSError) as exc:
                if not emitted and isinstance(exc, claude_adapter.NotInPlanError):
                    try:
                        queue, switched = _requeue_after_not_in_plan(
                            target, target_req.model, queue, tried, allow_failover)
                    except OpenAIError as unavailable:
                        raise RuntimeError(unavailable.message) from exc
                    if switched is not None and primary == "claude":
                        routed.add(switched)
                        origin = switched
                        model_routing.log_route(target_req.model, target, switched, "not in plan (answer)")
                    if not queue:
                        raise
                    continue
                reason = failover.classify(exc)
                failover.record_failure(target, reason, str(exc))
                log.error("%s CLI stream failed (%s): %s", target, reason, exc)
                if reason == "transport":
                    dead_cli = failover.cli_of(target)
                if emitted or not _more_targets([target, *queue], 0, dead_cli) \
                        or not failover.should_failover(reason):
                    raise
                continue
            failover.record_success(target)
            if failover.cli_of(target) == "claude":
                failover.clear_not_in_plan(target, [model_routing.model_key(target_req.model)])
            if req.reasoning_effort:
                log.info("reasoning_effort=%s applied via %s (stream)", req.reasoning_effort, target)
            if target != primary and target not in routed:
                failover.record_failover(origin, target)
            return

    handed_over = order[0] != primary and order[0] not in routed
    return ClosingStreamingResponse(
        sse_response(req.model, _events(), include_usage=include_usage),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "X-Accel-Buffering": "no",
            "x-cli-proxy-provider": order[0],
            **({"x-cli-proxy-failover": f"{primary}->{order[0]}"} if handed_over else {}),
            **({REASONING_EFFORT_HEADER: req.reasoning_effort} if req.reasoning_effort else {}),
        },
    )


# ---------------------------------------------------------------------------
# Generic CLI provider endpoint (new)
# ---------------------------------------------------------------------------

@app.post("/v1/cli/{provider_name}/chat/completions")
async def cli_chat_completions(
    provider_name: str, req: ChatCompletionRequest, http_request: Request
) -> JSONResponse:
    """Generic endpoint: routes to the CLI adapter configured for provider_name."""
    _check_auth(http_request)
    _begin_request(req)
    _begin_timeout(http_request)
    if req.stream:
        raise HTTPException(
            status_code=400,
            detail="Streaming is not supported on the generic /v1/cli/{provider_name} endpoint.",
        )
    if req.document_mode:
        raise HTTPException(
            status_code=400,
            detail=(
                "document_mode is not supported on the generic /v1/cli/{provider_name}/chat/completions "
                "endpoint. Use /v1/claude/chat/completions or /v1/codex/chat/completions instead."
            ),
        )

    config = provider_sync.get_provider_config(provider_name)
    if config is None:
        raise HTTPException(
            status_code=404,
            detail=f"CLI provider '{provider_name}' not found or not active",
        )

    settings = config.get("settings", {})
    cli_kind = settings.get("cli_kind", "generic")

    try:
        adapter = get_adapter(cli_kind)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

    if cli_kind == "claude":
        return await _generic_claude(adapter, config, req, provider_name)

    try:
        result = await adapter.execute(config, req.model_dump())
        return JSONResponse(result)
    except cli_process.CallerTimeoutError as exc:
        raise _caller_timeout(provider_name, exc) from exc
    except Exception as exc:
        log.error("CLI adapter error for %s: %s", provider_name, exc)
        raise HTTPException(status_code=502, detail=str(exc)) from exc


async def _generic_claude(adapter, config: dict, req: ChatCompletionRequest, provider_name: str) -> JSONResponse:
    """The generic endpoint for a provider of cli_kind claude: same model routing between the
    Claude accounts as the explicit endpoints, but -- as before -- no failover to codex and no
    breaker bookkeeping. Every attempt binds its account explicitly, so no binding of an earlier
    request or stage can carry over; the serving account is reported in x-cli-proxy-provider."""
    route = model_routing.route_to_alt(req.model)
    account = failover.ALT if route is not None else model_routing.PRIMARY
    if route is not None:
        model_routing.log_route(req.model, model_routing.PRIMARY, account, route)
    tried: set[str] = set()
    while True:
        tried.add(account)
        _bind_account(account)
        try:
            result = await adapter.execute(config, req.model_dump())
        except cli_process.CallerTimeoutError as exc:
            raise _caller_timeout(provider_name, exc) from exc
        except Exception as exc:
            if isinstance(exc, claude_adapter.NotInPlanError):
                failover.mark_not_in_plan(account, model_routing.model_key(req.model))
                log.info("%s: model %s is not in the plan of this account (generic endpoint)",
                         account, req.model or "(default)")
                other = model_routing.sibling_candidate(account, req.model, tried)
                if other is not None:
                    model_routing.log_route(req.model, account, other, "not in plan (answer)")
                    account = other
                    continue
                raise _not_in_plan_error(account, exc) from exc
            log.error("CLI adapter error for %s: %s", provider_name, exc)
            raise HTTPException(status_code=502, detail=str(exc)) from exc
        failover.clear_not_in_plan(account, [model_routing.model_key(req.model)])
        return JSONResponse(result, headers={"x-cli-proxy-provider": account})


@app.get("/v1/cli/{provider_name}/models")
async def cli_list_models(provider_name: str, refresh: int = 0) -> JSONResponse:
    """List models available for a configured CLI provider.

    This is the single provider-name-keyed model endpoint; consumers do not need to know
    which CLI kinds have a live source. `x_source` states where the list came from and
    `x_aliases` maps always-latest alias ids to the concrete model they resolve to right now.
    Pass `refresh=1` to bypass the proxy's own cache.
    """
    config = provider_sync.get_provider_config(provider_name)
    if config is None:
        raise HTTPException(
            status_code=404,
            detail=f"CLI provider '{provider_name}' not found",
        )

    settings = config.get("settings", {})
    cli_kind = settings.get("cli_kind", "generic")

    try:
        adapter = get_adapter(cli_kind)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

    models, source, aliases = await adapter.list_models_with_source(config, bypass_cache=bool(refresh))
    body: dict[str, object] = {
        "object": "list",
        "data": [{"id": m, "object": "model"} for m in models],
        "x_source": source,
        "x_aliases": aliases,
    }
    if adapter.aliases_complete:
        body["x_aliases_complete"] = True
    return JSONResponse(body)


# ---------------------------------------------------------------------------
# Legacy endpoints — kept for backwards compatibility
# ---------------------------------------------------------------------------

@app.post("/v1/claude/chat/completions", response_model=ChatCompletionResponse)
async def claude_completions(req: ChatCompletionRequest, http_request: Request, response: Response):
    """Routes to the claude CLI — no model-name heuristic. Falls over to codex when
    claude is faulted (auth/limit/transport); see failover.py."""
    _check_auth(http_request)
    _begin_request(req)
    _begin_timeout(http_request)
    allow_failover = _failover_allowed(http_request)
    if req.stream:
        return _stream_completion(req, "claude", allow_failover)
    async with _inflight_guard():
        return await _complete_with_failover(req, "claude", response, allow_failover)


@app.post("/v1/codex/chat/completions", response_model=ChatCompletionResponse)
async def codex_completions(req: ChatCompletionRequest, http_request: Request, response: Response):
    """Routes to the codex CLI — no model-name heuristic. Falls over to claude when
    codex is faulted (auth/limit/transport); see failover.py."""
    _check_auth(http_request)
    _begin_request(req)
    _begin_timeout(http_request)
    req = await _resolve_codex_alias(req)
    allow_failover = _failover_allowed(http_request)
    if req.stream:
        return _stream_completion(req, "codex", allow_failover)
    async with _inflight_guard():
        return await _complete_with_failover(req, "codex", response, allow_failover)


# ---------------------------------------------------------------------------
# Image generation (codex only — claude cannot generate images, so there is no failover)
# ---------------------------------------------------------------------------

# Images draw on the same ChatGPT subscription window as BookForge's codex work, so a runaway
# caller must not be able to empty it. 0 disables the cap. The counter is per UTC day, in memory:
# a restart resets it, which is acceptable for a safety net.
IMAGE_DAILY_MAX = int(os.getenv("IMAGE_DAILY_MAX", "50"))
_image_day = ""
_image_count = 0

_IMAGE_SIZES = ("auto", *codex_adapter.IMAGE_SIZE_HINTS)


def _validate_image_request(req: ImageGenerationRequest) -> None:
    """Honest param policy, like the chat route: reject what codex cannot deliver instead of
    pretending. `model`, `quality` and `user` are accepted and ignored."""
    if req.n is not None and req.n != 1:
        raise OpenAIError(
            "n>1 is not supported (each image is one subscription-billed CLI run); send one "
            "request per image.", status_code=400, code="unsupported_parameter", param="n")
    if req.response_format not in (None, "b64_json"):
        raise OpenAIError(
            "Only response_format=b64_json is supported; images are returned inline, not as URLs.",
            status_code=400, code="unsupported_value", param="response_format")
    if req.output_format not in (None, "png"):
        raise OpenAIError(
            "Only output_format=png is supported.",
            status_code=400, code="unsupported_value", param="output_format")
    if req.background not in (None, "auto", "opaque"):
        raise OpenAIError(
            f"background {req.background!r} is not supported; images are always opaque.",
            status_code=400, code="unsupported_value", param="background")
    if req.size is not None and req.size not in _IMAGE_SIZES:
        raise OpenAIError(
            f"size {req.size!r} is not supported; use one of: {', '.join(_IMAGE_SIZES)}.",
            status_code=400, code="unsupported_value", param="size")


def _take_image_slot() -> None:
    """Count one image job against today's cap; 429 once the cap is reached."""
    global _image_day, _image_count
    today = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    if today != _image_day:
        _image_day, _image_count = today, 0
    if IMAGE_DAILY_MAX and _image_count >= IMAGE_DAILY_MAX:
        raise OpenAIError(
            f"Daily image limit of {IMAGE_DAILY_MAX} reached; it resets at 00:00 UTC.",
            status_code=429, type="rate_limit_error", code="rate_limit_exceeded")
    _image_count += 1


@app.post("/v1/codex/images/generations")
async def codex_image_generations(
    req: ImageGenerationRequest, http_request: Request
) -> JSONResponse:
    """OpenAI Images API (`images.generate`) backed by codex's `$imagegen`. One PNG per request,
    returned as base64. Not part of the claude⇄codex failover and it never touches the circuit
    breaker: an image fault says nothing about whether text completions work."""
    _check_auth(http_request)
    _validate_image_request(req)
    _begin_timeout(http_request)
    _take_image_slot()
    async with _inflight_guard():
        try:
            png, parts = await codex_adapter.generate_image(req.prompt, req.size)
        except cli_process.CallerTimeoutError as exc:
            raise _caller_timeout("codex", exc) from exc
        except codex_adapter.NoImageError as exc:
            log.warning("codex image generation produced no image: %s", exc)
            raise OpenAIError(str(exc), status_code=400, code="image_generation_failed") from exc
        except (RuntimeError, OSError) as exc:
            reason = failover.classify(exc)
            log.error("codex image generation failed (%s): %s", reason, exc)
            if reason == "limit":
                raise OpenAIError(str(exc), status_code=429, type="rate_limit_error",
                                  code="rate_limit_exceeded") from exc
            raise OpenAIError(str(exc), status_code=502, type="api_error") from exc
    dims = png_dimensions(png)
    # The usage of the surrounding agent turn is not the image model's usage, so it is logged
    # for the operator but not returned (the field is optional in the Images API).
    log.info("codex image generated | %d bytes | %s | agent turn tokens in=%d out=%d",
             len(png), dims, parts.input_tokens, parts.output_tokens)
    body = {
        "created": int(time.time()),
        "data": [{"b64_json": base64.b64encode(png).decode("ascii")}],
        "output_format": "png",
    }
    if dims:
        body["size"] = f"{dims[0]}x{dims[1]}"
    return JSONResponse(body, headers={"x-cli-proxy-provider": "codex"})


@app.post("/v1/chat/completions", response_model=ChatCompletionResponse)
async def chat_completions(req: ChatCompletionRequest, http_request: Request, response: Response):
    """Legacy endpoint with model-name routing. Use /v1/claude/... or /v1/codex/... instead."""
    log.warning(
        "DEPRECATED: /v1/chat/completions endpoint with model-name routing. "
        "Use /v1/claude/chat/completions or /v1/codex/chat/completions instead."
    )
    _check_auth(http_request)
    _begin_request(req)
    _begin_timeout(http_request)
    backend = _route_model(req.model)
    if backend == "codex":
        req = await _resolve_codex_alias(req)
    if req.document_mode:
        raise HTTPException(
            status_code=400,
            detail=(
                "document_mode is not supported on the legacy /v1/chat/completions endpoint. "
                "Use /v1/claude/chat/completions or /v1/codex/chat/completions instead."
            ),
        )
    allow_failover = _failover_allowed(http_request)
    if req.stream:
        return _stream_completion(req, backend, allow_failover)
    async with _inflight_guard():
        return await _complete_with_failover(req, backend, response, allow_failover)


@app.get("/v1/claude/models")
async def claude_models(refresh: int = 0) -> JSONResponse:
    """Lists Claude models: always-latest aliases + the concrete newest model ids, resolved via
    the CLI's own alias resolution (24 h cache). Auto-updates as new models ship — never stale.

    `x_source` is "live" when the CLI answered the probe and "degraded" when every probe failed
    and the ids below are the offline fallback — a distinction callers cannot make from the
    status code alone. `refresh=1` bypasses the cache.
    """
    models, source, aliases = await claude_adapter.list_models_with_source_async(bool(refresh))
    data = [{"id": m, "object": "model", "owned_by": "anthropic"} for m in models]
    return JSONResponse({"object": "list", "data": data, "x_source": source, "x_aliases": aliases})


@app.get("/v1/codex/models")
async def codex_models(refresh: int = 0) -> JSONResponse:
    """Lists the codex subscription models read from the CLI's models_cache.json, preceded by
    the always-latest aliases (`gpt-sol-latest`, ...). `x_aliases` maps each alias to the
    concrete model it resolves to right now. `x_source` is "degraded" when the cache file was
    unusable and "static" when the CLI has not written it yet; both serve cached/curated ids.
    `x_aliases_complete` is always true here: the map is derived completely from the served list,
    the same list and function `resolve_model_alias` uses for a call, whatever `x_source` says.
    `refresh=1` bypasses the cache."""
    models, source, aliases = await codex_adapter.list_models_with_aliases_async(bool(refresh))
    data = [{"id": m, "object": "model", "owned_by": "openai"} for m in models]
    return JSONResponse({"object": "list", "data": data, "x_source": source, "x_aliases": aliases,
                         "x_aliases_complete": codex_adapter.ALIASES_COMPLETE})


@app.get("/v1/models")
async def list_models() -> JSONResponse:
    """Legacy combined model list. Use /v1/claude/models or /v1/codex/models instead."""
    claude_model_list = await claude_adapter.list_models_async()
    claude_data = [{"id": m, "object": "model", "owned_by": "anthropic"} for m in claude_model_list]
    codex_model_list = await codex_adapter.list_models_async()
    codex_data = [{"id": m, "object": "model", "owned_by": "openai"} for m in codex_model_list]
    return JSONResponse({"object": "list", "data": claude_data + codex_data})


def _model_object(model_id: str, owned_by: str) -> dict:
    return {"id": model_id, "object": "model", "created": 0, "owned_by": owned_by}


@app.get("/v1/claude/models/{model_id}")
async def claude_model_retrieve(model_id: str) -> JSONResponse:
    """Retrieve a single Claude model (OpenAI Models.retrieve)."""
    return JSONResponse(_model_object(model_id, "anthropic"))


@app.get("/v1/codex/models/{model_id}")
async def codex_model_retrieve(model_id: str) -> JSONResponse:
    """Retrieve a single codex/OpenAI model (OpenAI Models.retrieve)."""
    return JSONResponse(_model_object(model_id, "openai"))


@app.get("/v1/models/{model_id}")
async def model_retrieve(model_id: str) -> JSONResponse:
    """Retrieve a single model by id; owner inferred from the model-name routing heuristic."""
    owned_by = "anthropic" if _route_model(model_id) == "claude" else "openai"
    return JSONResponse(_model_object(model_id, owned_by))


@app.get("/health")
async def health() -> JSONResponse:
    claude_ok = shutil.which("claude") is not None
    codex_ok = shutil.which("codex") is not None
    gemini_ok = shutil.which("gemini") is not None
    # `status` stays "ok" even while degraded: existing consumers (dms-ai-router,
    # Geef.Atelier) treat a 200 as "gateway reachable", which is still true when one
    # backend has failed over. Degradation is reported additively instead.
    snapshot = failover.snapshot()
    return JSONResponse({
        "status": "ok",
        "cli_status": {
            "claude": "ready" if claude_ok else "not_found",
            "codex": "ready" if codex_ok else "not_found",
            "gemini": "ready" if gemini_ok else "not_found",
        },
        "synced_providers": provider_sync.all_provider_names(),
        "degraded": snapshot["degraded"],
        "failover": snapshot,
        # Per Claude account: families it resolves, families its probe answered with "not in
        # plan", and the running runtime marks (model -> epoch until). No account data.
        "claude_accounts": model_routing.snapshot(),
    })
