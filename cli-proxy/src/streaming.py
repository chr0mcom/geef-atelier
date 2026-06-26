"""Server-Sent-Events (SSE) formatter for OpenAI streaming chat completions.

Consumes the adapter event stream — ("delta", text) chunks terminated by a single
("usage", UsageParts) — and emits the OpenAI `chat.completion.chunk` SSE wire format:
a role chunk, content delta chunks, a finish chunk, an optional usage chunk
(stream_options.include_usage), and the terminating `data: [DONE]`.
"""
from __future__ import annotations

import json
import time
import uuid
from typing import Any, AsyncIterator

from openai_format import usage_info
from usage import UsageParts


def _sse(obj: dict) -> bytes:
    return f"data: {json.dumps(obj, ensure_ascii=False)}\n\n".encode("utf-8")


def _chunk(
    chunk_id: str, created: int, model: str, delta: dict, finish_reason: str | None = None
) -> dict:
    return {
        "id": chunk_id,
        "object": "chat.completion.chunk",
        "created": created,
        "model": model,
        "choices": [{"index": 0, "delta": delta, "finish_reason": finish_reason}],
    }


async def sse_response(
    model: str,
    event_iter: AsyncIterator[tuple[str, Any]],
    *,
    include_usage: bool = False,
    finish_reason: str = "stop",
) -> AsyncIterator[bytes]:
    """Translate an adapter event stream into OpenAI SSE chunks (yields bytes)."""
    chunk_id = f"chatcmpl-{uuid.uuid4().hex}"
    created = int(time.time())

    # First chunk announces the assistant role (OpenAI convention).
    yield _sse(_chunk(chunk_id, created, model, {"role": "assistant"}))

    usage_parts = UsageParts()
    emitted_tool_calls = False
    try:
        async for kind, payload in event_iter:
            if kind == "delta" and payload:
                yield _sse(_chunk(chunk_id, created, model, {"content": payload}))
            elif kind == "tool_calls" and payload:
                # Agentic tool use: emit the tool calls as a delta (OpenAI streaming shape).
                emitted_tool_calls = True
                yield _sse(_chunk(chunk_id, created, model, {"tool_calls": payload}))
            elif kind == "usage" and isinstance(payload, UsageParts):
                usage_parts = payload
    except Exception as exc:  # noqa: BLE001 — surface mid-stream failures to the client
        # Headers are already sent (HTTP 200), so the only way to report a late error is
        # in-band: emit an error event, then terminate the stream.
        yield _sse({"error": {"message": str(exc), "type": "api_error", "code": None, "param": None}})
        yield b"data: [DONE]\n\n"
        return

    # Final content-less chunk carries the finish reason (tool_calls overrides when emitted).
    final_finish = "tool_calls" if emitted_tool_calls else finish_reason
    yield _sse(_chunk(chunk_id, created, model, {}, finish_reason=final_finish))

    if include_usage:
        ui = usage_info(
            input_tokens=usage_parts.input_tokens,
            output_tokens=usage_parts.output_tokens,
            cached_tokens=usage_parts.cached_tokens,
            reasoning_tokens=usage_parts.reasoning_tokens,
        )
        yield _sse({
            "id": chunk_id,
            "object": "chat.completion.chunk",
            "created": created,
            "model": model,
            "choices": [],
            "usage": ui.model_dump(exclude_none=True),
        })

    yield b"data: [DONE]\n\n"
