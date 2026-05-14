"""CLI-Proxy: OpenAI-compatible HTTP API backed by claude and codex CLIs."""
from __future__ import annotations

import asyncio
import logging
import shutil
from contextlib import asynccontextmanager

import claude_adapter
import codex_adapter
from fastapi import FastAPI, HTTPException
from fastapi.responses import JSONResponse
from openai_format import (
    ChatCompletionRequest,
    ChatCompletionResponse,
    make_text_response,
    make_tool_response,
)
from tool_use_parser import build_tool_system_prompt, extract_json

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI):  # noqa: ANN001
    # Re-initialise semaphores after the event loop is running.
    claude_adapter._semaphore = asyncio.Semaphore(claude_adapter.MAX_CONCURRENT)
    codex_adapter._semaphore = asyncio.Semaphore(codex_adapter.MAX_CONCURRENT)
    yield


app = FastAPI(title="Atelier CLI Proxy", version="1.0.0", lifespan=lifespan)


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


# ---------------------------------------------------------------------------
# Request processing
# ---------------------------------------------------------------------------

def _build_prompt(req: ChatCompletionRequest) -> str:
    """Concatenates messages into a plain-text prompt for the CLI."""
    parts: list[str] = []

    for msg in req.messages:
        role = msg.role.upper()
        content = msg.content or ""
        parts.append(f"[{role}]\n{content}")

    prompt = "\n\n".join(parts)

    # Append tool-use schema as additional system instruction if needed.
    if req.tools:
        tools_dicts = [t.model_dump() for t in req.tools]
        prompt += build_tool_system_prompt(tools_dicts)

    return prompt


def _extract_tool_name(req: ChatCompletionRequest) -> str | None:
    """Returns the requested tool function name, if any."""
    if not req.tool_choice or not req.tools:
        return None
    if isinstance(req.tool_choice, str):
        return None
    return req.tool_choice.function.name


async def _dispatch(req: ChatCompletionRequest) -> str:
    """Calls the appropriate CLI adapter and returns raw text output."""
    backend = _route_model(req.model)
    prompt = _build_prompt(req)
    max_tokens = req.max_tokens

    log.info("Dispatching to %s | model=%s | tool=%s", backend, req.model, _extract_tool_name(req))

    if backend == "claude":
        return await claude_adapter.complete(prompt, req.model, max_tokens)
    else:
        return await codex_adapter.complete(prompt, req.model, max_tokens)


def _build_response(req: ChatCompletionRequest, raw_text: str) -> ChatCompletionResponse:
    """Converts raw CLI output into an OpenAI-compatible response."""
    tool_name = _extract_tool_name(req)

    if tool_name:
        json_str = extract_json(raw_text)
        if json_str:
            return make_tool_response(req.model, tool_name, json_str)
        # Defensive fallback: return as plain text — downstream will handle gracefully.
        log.warning("Tool-use requested but JSON extraction failed; returning as plain text")
        return make_text_response(req.model, raw_text)

    return make_text_response(req.model, raw_text)


# ---------------------------------------------------------------------------
# Request processing helpers (adapter-level, no routing logic)
# ---------------------------------------------------------------------------

async def _call_claude(req: ChatCompletionRequest) -> ChatCompletionResponse:
    prompt = _build_prompt(req)
    log.info("Dispatching to claude | model=%s | tool=%s", req.model, _extract_tool_name(req))
    raw = await claude_adapter.complete(prompt, req.model, req.max_tokens)
    return _build_response(req, raw)


async def _call_codex(req: ChatCompletionRequest) -> ChatCompletionResponse:
    prompt = _build_prompt(req)
    log.info("Dispatching to codex | model=%s | tool=%s", req.model, _extract_tool_name(req))
    raw = await codex_adapter.complete(prompt, req.model, req.max_tokens)
    return _build_response(req, raw)


# ---------------------------------------------------------------------------
# Endpoints
# ---------------------------------------------------------------------------

@app.post("/v1/claude/chat/completions", response_model=ChatCompletionResponse)
async def claude_completions(req: ChatCompletionRequest) -> ChatCompletionResponse:
    """Routes directly to the claude CLI — no model-name heuristic."""
    if req.stream:
        raise HTTPException(status_code=400, detail="Streaming is not supported by the CLI proxy.")
    try:
        return await _call_claude(req)
    except RuntimeError as exc:
        log.error("claude CLI call failed: %s", exc)
        raise HTTPException(status_code=502, detail=str(exc)) from exc


@app.post("/v1/codex/chat/completions", response_model=ChatCompletionResponse)
async def codex_completions(req: ChatCompletionRequest) -> ChatCompletionResponse:
    """Routes directly to the codex CLI — no model-name heuristic."""
    if req.stream:
        raise HTTPException(status_code=400, detail="Streaming is not supported by the CLI proxy.")
    try:
        return await _call_codex(req)
    except RuntimeError as exc:
        log.error("codex CLI call failed: %s", exc)
        raise HTTPException(status_code=502, detail=str(exc)) from exc


@app.post("/v1/chat/completions", response_model=ChatCompletionResponse)
async def chat_completions(req: ChatCompletionRequest) -> ChatCompletionResponse:
    """Legacy endpoint with model-name routing. Use /v1/claude/... or /v1/codex/... instead."""
    log.warning(
        "DEPRECATED: /v1/chat/completions endpoint with model-name routing. "
        "Use /v1/claude/chat/completions or /v1/codex/chat/completions instead."
    )
    if req.stream:
        raise HTTPException(status_code=400, detail="Streaming is not supported by the CLI proxy.")
    try:
        raw = await _dispatch(req)
        return _build_response(req, raw)
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
    codex_models = await codex_adapter.list_models_async()
    codex_data   = [{"id": m, "object": "model", "owned_by": "openai"} for m in codex_models]
    return JSONResponse({"object": "list", "data": claude_data + codex_data})


@app.get("/health")
async def health() -> JSONResponse:
    claude_ok = shutil.which("claude") is not None
    codex_ok = shutil.which("codex") is not None
    return JSONResponse({
        "status": "ok",
        "cli_status": {
            "claude": "ready" if claude_ok else "not_found",
            "codex": "ready" if codex_ok else "not_found",
        },
    })
