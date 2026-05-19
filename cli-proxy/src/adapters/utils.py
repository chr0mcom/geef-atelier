"""Shared utilities for CLI adapters."""
from __future__ import annotations

import time
import uuid


def format_messages(messages: list[dict]) -> str:
    """Convert OpenAI messages list to a single prompt string."""
    parts = []
    for msg in messages:
        role = msg.get("role", "user")
        content = msg.get("content", "")
        if role == "system":
            parts.append(f"System: {content}")
        elif role == "assistant":
            parts.append(f"Assistant: {content}")
        else:
            parts.append(content)
    return "\n\n".join(parts)


def build_openai_response(request: dict, text: str) -> dict:
    """Build minimal OpenAI-format chat completion response."""
    return build_openai_response_from_parts(request, text, 0, 0)


def build_openai_response_from_parts(
    request: dict, content: str, input_tokens: int, output_tokens: int
) -> dict:
    """Build OpenAI-format chat completion response with token usage."""
    return {
        "id": f"chatcmpl-{uuid.uuid4().hex[:12]}",
        "object": "chat.completion",
        "created": int(time.time()),
        "model": request.get("model", "unknown"),
        "choices": [{
            "index": 0,
            "message": {"role": "assistant", "content": content},
            "finish_reason": "stop",
        }],
        "usage": {
            "prompt_tokens": input_tokens,
            "completion_tokens": output_tokens,
            "total_tokens": input_tokens + output_tokens,
        },
    }
