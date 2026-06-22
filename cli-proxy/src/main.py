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
from adapters import get_adapter
from fastapi import FastAPI, HTTPException, Request
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
    build_required_tool_system_prompt,
    build_tool_system_prompt,
    extract_json,
    parse_agentic_responses,
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


def _build_prompt(req: ChatCompletionRequest) -> str:
    """Concatenates messages into a plain-text prompt for the CLI."""
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

    prompt = "\n\n".join(parts)

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


async def _run_completion(req: ChatCompletionRequest, backend: str) -> ChatCompletionResponse:
    """Non-streaming completion: build prompt, apply response_format/tool handling, retry once."""
    prompt = _build_prompt(req)
    rf = req.response_format
    structured = rf is not None and rf.type != "text"
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


def _stream_completion(req: ChatCompletionRequest, backend: str) -> StreamingResponse:
    """Streaming (SSE) completion. Tool/structured extraction needs the full text, so when
    those are requested we fall back to a single buffered delta; otherwise we stream tokens."""
    prompt = _build_prompt(req)
    rf = req.response_format
    structured = rf is not None and rf.type != "text"
    if structured:
        prompt += build_response_format_instruction(rf)
    max_tokens = req.effective_max_tokens()
    include_usage = bool(req.stream_options and req.stream_options.include_usage)

    adapter = claude_adapter if backend == "claude" else codex_adapter

    async def _events():
        # When tools/structured output are requested, we must inspect the full text before
        # deciding tool_calls vs content — so buffer, then emit. Pure text streams token-by-token.
        if req.tools or structured:
            raw, parts = await _complete_backend(backend, prompt, req.model, max_tokens)
            if structured:
                json_str, _ = validate_and_extract(rf, raw)
                raw = json_str if json_str is not None else raw
            yield ("delta", raw)
            yield ("usage", parts)
        else:
            async for ev in adapter.stream(prompt, req.model, max_tokens):
                yield ev

    return StreamingResponse(
        sse_response(req.model, _events(), include_usage=include_usage),
        media_type="text/event-stream",
        headers={"Cache-Control": "no-cache", "X-Accel-Buffering": "no"},
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
async def claude_completions(req: ChatCompletionRequest, http_request: Request):
    """Routes directly to the claude CLI — no model-name heuristic."""
    _check_auth(http_request)
    _validate_policy(req)
    if req.stream:
        return _stream_completion(req, "claude")
    try:
        async with _inflight_guard():
            return await _call_claude(req)
    except RuntimeError as exc:
        log.error("claude CLI call failed: %s", exc)
        raise HTTPException(status_code=502, detail=str(exc)) from exc


@app.post("/v1/codex/chat/completions", response_model=ChatCompletionResponse)
async def codex_completions(req: ChatCompletionRequest, http_request: Request):
    """Routes directly to the codex CLI — no model-name heuristic."""
    _check_auth(http_request)
    _validate_policy(req)
    if req.stream:
        return _stream_completion(req, "codex")
    try:
        async with _inflight_guard():
            return await _call_codex(req)
    except RuntimeError as exc:
        log.error("codex CLI call failed: %s", exc)
        raise HTTPException(status_code=502, detail=str(exc)) from exc


@app.post("/v1/chat/completions", response_model=ChatCompletionResponse)
async def chat_completions(req: ChatCompletionRequest, http_request: Request):
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
    try:
        async with _inflight_guard():
            return await (_call_claude(req) if backend == "claude" else _call_codex(req))
    except RuntimeError as exc:
        log.error("CLI call failed: %s", exc)
        raise HTTPException(status_code=502, detail=str(exc)) from exc


@app.get("/v1/claude/models")
async def claude_models() -> JSONResponse:
    """Lists available Claude CLI models (static list — CLI has no model-listing command)."""
    data = [{"id": m, "object": "model", "owned_by": "anthropic"} for m in claude_adapter.list_models()]
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
    claude_data = [{"id": m, "object": "model", "owned_by": "anthropic"} for m in claude_adapter.list_models()]
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
    return JSONResponse({
        "status": "ok",
        "cli_status": {
            "claude": "ready" if claude_ok else "not_found",
            "codex": "ready" if codex_ok else "not_found",
            "gemini": "ready" if gemini_ok else "not_found",
        },
        "synced_providers": provider_sync.all_provider_names(),
    })
