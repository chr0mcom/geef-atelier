"""CLI-Proxy: OpenAI-compatible HTTP API backed by claude and codex CLIs."""
from __future__ import annotations

import asyncio
import logging
import os
import shutil
import time
import uuid
from contextlib import asynccontextmanager

import claude_adapter
import codex_adapter
import failover
from adapters import get_adapter
from fastapi import FastAPI, HTTPException, Request, Response
from fastapi.responses import JSONResponse, StreamingResponse
from openai_format import (
    ChatCompletionRequest,
    ChatCompletionResponse,
    ToolChoiceObject,
    UsageInfo,
    make_refusal_response,
    make_text_response,
    make_tool_calls_response,
    make_tool_response,
    message_images,
    message_text,
    usage_info,
)
from structured import build_response_format_instruction, validate_and_extract
from streaming import sse_response
from usage import UsageParts
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


@asynccontextmanager
async def lifespan(app: FastAPI):  # noqa: ANN001
    # Re-initialise semaphores after the event loop is running.
    claude_adapter._semaphore = asyncio.Semaphore(claude_adapter.MAX_CONCURRENT)
    codex_adapter._semaphore = asyncio.Semaphore(codex_adapter.MAX_CONCURRENT)
    # Sync provider configs from backend (best-effort; failure is non-fatal).
    await provider_sync.sync_now()
    asyncio.create_task(provider_sync.background_sync_loop(interval=60))
    yield


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
    request_id = request.headers.get("x-request-id") or f"req_{uuid.uuid4().hex}"
    start = time.monotonic()
    response = await call_next(request)
    response.headers["x-request-id"] = request_id
    response.headers["openai-processing-ms"] = str(int((time.monotonic() - start) * 1000))
    response.headers["x-ratelimit-limit-requests"] = str(_TOTAL_CONCURRENCY)
    response.headers["x-ratelimit-remaining-requests"] = str(max(0, _TOTAL_CONCURRENCY - _inflight))
    return response


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
    backend: str, prompt: str, model: str, max_tokens: int | None
) -> tuple[str, UsageParts]:
    if backend == "claude":
        return await claude_adapter.complete_with_usage(prompt, model, max_tokens)
    return await codex_adapter.complete_with_usage(prompt, model, max_tokens)


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

    decision, parts = await adapter.complete_agentic_file(instruction, req.model)
    kind, payload = parse_decision(decision)

    if _decision_unacceptable(kind, payload, mode, specific):
        log.warning("Agentic file mode (%s): decision unacceptable (kind=%s, mode=%s) — retrying once",
                    backend, kind, mode)
        decision, retry_parts = await adapter.complete_agentic_file(
            instruction + _DECISION_RETRY_SUFFIX, req.model)
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

    raw, parts = await _complete_backend(backend, prompt, req.model, max_tokens)
    usage = _to_usage_info(parts)

    # Structured outputs take precedence over tool extraction.
    if structured:
        json_str, err = validate_and_extract(rf, raw)
        if err:
            log.warning("%s structured output invalid (%s) — retrying", backend, err)
            raw, parts = await _complete_backend(
                backend, prompt + _JSON_RETRY_SUFFIX, req.model, max_tokens)
            usage = _to_usage_info(parts)
            json_str, err = validate_and_extract(rf, raw)
        if json_str is not None:
            return make_text_response(req.model, json_str, usage)
        return make_refusal_response(
            req.model, f"Could not produce valid structured output: {err}", usage)

    resp = _build_response(req, raw, usage)
    if _needs_json_retry(resp, req):
        log.warning("%s JSON extraction failed — retrying with explicit JSON reminder", backend)
        raw, parts = await _complete_backend(
            backend, prompt + _JSON_RETRY_SUFFIX, req.model, max_tokens)
        resp = _build_response(req, raw, _to_usage_info(parts))
    return resp


async def _call_claude(req: ChatCompletionRequest) -> ChatCompletionResponse:
    if req.document_mode:
        system_prompt, user_instruction = _extract_parts(req)
        log.info("Dispatching to claude (document mode) | model=%s", req.model)
        async with ephemeral_workspace(req.document or "") as ws:
            raw, parts = await claude_adapter.complete_document(
                system_prompt, user_instruction, req.document or "", ws, req.model,
                req.effective_max_tokens(), req.context_document,
            )
        return make_text_response(req.model, raw, _to_usage_info(parts))
    return await _run_completion(req, "claude")


async def _call_codex(req: ChatCompletionRequest) -> ChatCompletionResponse:
    if req.document_mode:
        system_prompt, user_instruction = _extract_parts(req)
        log.info("Dispatching to codex (document mode) | model=%s", req.model)
        async with ephemeral_workspace(req.document or "") as ws:
            raw, parts = await codex_adapter.complete_document(
                system_prompt, user_instruction, req.document or "", ws, req.model,
                req.effective_max_tokens(), req.context_document,
            )
        return make_text_response(req.model, raw, _to_usage_info(parts))
    return await _run_completion(req, "codex")


async def _call_backend(req: ChatCompletionRequest, backend: str) -> ChatCompletionResponse:
    return await (_call_claude(req) if backend == "claude" else _call_codex(req))


def _retarget(req: ChatCompletionRequest, primary: str, target: str) -> ChatCompletionRequest:
    """Adapt the request for a backend the caller did not ask for.

    The model name must NOT travel across the backend boundary: handing
    `claude-haiku-latest` to `codex -m` is a guaranteed failure. Both adapters skip the
    model flag on a falsy name, so an empty model means "use that CLI's own default" --
    which for codex is the newest top model configured in ~/.codex/config.toml.
    """
    if target == primary:
        return req
    return req.model_copy(update={"model": ""})


async def _complete_with_failover(
    req: ChatCompletionRequest, backend: str, response: Response
) -> ChatCompletionResponse:
    """Serve the completion, handing over to the partner CLI when this one is faulted.

    Only auth/limit/transport faults trigger the handover; a client-side fault would
    fail identically on the partner and retrying it would just hide the bug.
    """
    order = failover.attempt_order(backend)
    for index, target in enumerate(order):
        is_last = index == len(order) - 1
        try:
            resp = await _call_backend(_retarget(req, backend, target), target)
        except (RuntimeError, OSError) as exc:
            reason = failover.classify(exc)
            failover.record_failure(target, reason, str(exc))
            log.error("%s CLI call failed (%s): %s", target, reason, exc)
            if is_last or not failover.should_failover(reason):
                raise HTTPException(status_code=502, detail=str(exc)) from exc
            continue
        failover.record_success(target)
        response.headers["x-cli-proxy-provider"] = target
        if target != backend:
            failover.record_failover(backend, target)
            response.headers["x-cli-proxy-failover"] = f"{backend}->{target}"
            # Echo the model the caller asked for -- OpenAI clients key off this field.
            resp.model = req.model
        return resp
    raise HTTPException(status_code=502, detail="no backend available")  # pragma: no cover


def _stream_completion(req: ChatCompletionRequest, backend: str) -> StreamingResponse:
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
    order = failover.attempt_order(primary)

    prompt = _build_prompt(req)
    rf = req.response_format
    structured = rf is not None and rf.type != "text"
    if structured:
        prompt += build_response_format_instruction(rf)
    max_tokens = req.effective_max_tokens()
    include_usage = bool(req.stream_options and req.stream_options.include_usage)

    async def _serve(target: str, target_req: ChatCompletionRequest):
        cli_model = target_req.model
        # Agentic tool use (both backends): resolve the decision via file authoring, then emit
        # either proper streaming tool_calls or the final answer — no hallucination, no
        # injection refusal, no "tools not connected" persona objection. The claude→codex
        # failover keeps the SAME decision-file contract (incident 2026-07-24: the buffered
        # fallback yielded the tool prompt's answer as plain text deltas, so Hermes workers
        # saw zero tool calls and crashed with protocol violations).
        if _is_agentic_tool_request(target_req, target, structured):
            kind, payload, parts = await _agentic_file_decision(target_req, target)
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
        elif target_req.tools or structured:
            raw, parts = await _complete_backend(target, prompt, cli_model, max_tokens)
            if structured:
                # Mirror the non-streaming contract: one retry on invalid structured output
                # (review finding 2026-07-24 — the SSE path silently shipped the raw text).
                json_str, err = validate_and_extract(rf, raw)
                if err:
                    log.warning("%s structured output invalid in stream (%s) — retrying", target, err)
                    raw, parts = await _complete_backend(
                        target, prompt + _JSON_RETRY_SUFFIX, cli_model, max_tokens)
                    json_str, err = validate_and_extract(rf, raw)
                yield ("delta", json_str if json_str is not None else raw)
            elif _extract_tool_name(target_req):
                # Forced single named tool (tool_choice=specific) — the SSE path must honour
                # the tool contract like the non-streaming one (review finding 2026-07-24:
                # it only ever yielded text deltas). One retry, mirroring _needs_json_retry.
                tool_name = _extract_tool_name(target_req)
                json_str = extract_json(raw)
                if json_str is None:
                    log.warning("%s specific-tool stream: JSON extraction failed — retrying", target)
                    raw, parts = await _complete_backend(
                        target, prompt + _JSON_RETRY_SUFFIX, cli_model, max_tokens)
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
            adapter = claude_adapter if target == "claude" else codex_adapter
            async for ev in adapter.stream(prompt, cli_model, max_tokens):
                yield ev

    async def _events():
        for index, target in enumerate(order):
            # The model name may not cross the backend boundary (see _retarget); the SSE
            # frames still echo the model the caller asked for.
            target_req = _retarget(req, primary, target)
            emitted = False
            try:
                async for ev in _serve(target, target_req):
                    emitted = True
                    yield ev
            except (RuntimeError, OSError) as exc:
                reason = failover.classify(exc)
                failover.record_failure(target, reason, str(exc))
                log.error("%s CLI stream failed (%s): %s", target, reason, exc)
                is_last = index == len(order) - 1
                if emitted or is_last or not failover.should_failover(reason):
                    raise
                continue
            failover.record_success(target)
            if target != primary:
                failover.record_failover(primary, target)
            return

    return StreamingResponse(
        sse_response(req.model, _events(), include_usage=include_usage),
        media_type="text/event-stream",
        headers={
            "Cache-Control": "no-cache",
            "X-Accel-Buffering": "no",
            "x-cli-proxy-provider": order[0],
            **({"x-cli-proxy-failover": f"{primary}->{order[0]}"} if order[0] != primary else {}),
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
    _validate_policy(req)
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

    try:
        result = await adapter.execute(config, req.model_dump())
        return JSONResponse(result)
    except Exception as exc:
        log.error("CLI adapter error for %s: %s", provider_name, exc)
        raise HTTPException(status_code=502, detail=str(exc)) from exc


@app.get("/v1/cli/{provider_name}/models")
async def cli_list_models(provider_name: str) -> JSONResponse:
    """List models available for a configured CLI provider."""
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

    models = await adapter.list_models(config)
    return JSONResponse({"object": "list", "data": [{"id": m, "object": "model"} for m in models]})


# ---------------------------------------------------------------------------
# Legacy endpoints — kept for backwards compatibility
# ---------------------------------------------------------------------------

@app.post("/v1/claude/chat/completions", response_model=ChatCompletionResponse)
async def claude_completions(req: ChatCompletionRequest, http_request: Request, response: Response):
    """Routes to the claude CLI — no model-name heuristic. Falls over to codex when
    claude is faulted (auth/limit/transport); see failover.py."""
    _check_auth(http_request)
    _validate_policy(req)
    if req.stream:
        return _stream_completion(req, "claude")
    async with _inflight_guard():
        return await _complete_with_failover(req, "claude", response)


@app.post("/v1/codex/chat/completions", response_model=ChatCompletionResponse)
async def codex_completions(req: ChatCompletionRequest, http_request: Request, response: Response):
    """Routes to the codex CLI — no model-name heuristic. Falls over to claude when
    codex is faulted (auth/limit/transport); see failover.py."""
    _check_auth(http_request)
    _validate_policy(req)
    if req.stream:
        return _stream_completion(req, "codex")
    async with _inflight_guard():
        return await _complete_with_failover(req, "codex", response)


@app.post("/v1/chat/completions", response_model=ChatCompletionResponse)
async def chat_completions(req: ChatCompletionRequest, http_request: Request, response: Response):
    """Legacy endpoint with model-name routing. Use /v1/claude/... or /v1/codex/... instead."""
    log.warning(
        "DEPRECATED: /v1/chat/completions endpoint with model-name routing. "
        "Use /v1/claude/chat/completions or /v1/codex/chat/completions instead."
    )
    _check_auth(http_request)
    _validate_policy(req)
    backend = _route_model(req.model)
    if req.document_mode:
        raise HTTPException(
            status_code=400,
            detail=(
                "document_mode is not supported on the legacy /v1/chat/completions endpoint. "
                "Use /v1/claude/chat/completions or /v1/codex/chat/completions instead."
            ),
        )
    if req.stream:
        return _stream_completion(req, backend)
    async with _inflight_guard():
        return await _complete_with_failover(req, backend, response)


@app.get("/v1/claude/models")
async def claude_models() -> JSONResponse:
    """Lists Claude models: always-latest aliases + the concrete newest model ids, resolved via
    the CLI's own alias resolution (24 h cache). Auto-updates as new models ship — never stale."""
    models = await claude_adapter.list_models_async()
    data = [{"id": m, "object": "model", "owned_by": "anthropic"} for m in models]
    return JSONResponse({"object": "list", "data": data})


@app.get("/v1/codex/models")
async def codex_models() -> JSONResponse:
    """Lists the 10 newest OpenAI models, fetched live from OpenRouter (24 h cache)."""
    models = await codex_adapter.list_models_async()
    data = [{"id": m, "object": "model", "owned_by": "openai"} for m in models]
    return JSONResponse({"object": "list", "data": data})


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
    })
