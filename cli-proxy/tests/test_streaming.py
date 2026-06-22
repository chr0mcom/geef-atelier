"""Tests for SSE streaming (WP2)."""
from __future__ import annotations

import json
import os
import sys
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import main  # noqa: E402
from usage import UsageParts  # noqa: E402


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


async def _fake_stream(prompt, model, max_tokens):
    yield ("delta", "Hello")
    yield ("delta", " world")
    yield ("usage", UsageParts(input_tokens=3, output_tokens=2))


def _parse_sse(text: str):
    chunks = []
    done = False
    for line in text.splitlines():
        line = line.strip()
        if not line.startswith("data:"):
            continue
        payload = line[len("data:"):].strip()
        if payload == "[DONE]":
            done = True
            continue
        chunks.append(json.loads(payload))
    return chunks, done


_BODY = {
    "model": "claude-haiku-4-5",
    "messages": [{"role": "user", "content": "hi"}],
    "stream": True,
}


class TestStreaming:
    def test_streams_content_deltas_and_done(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "stream", _fake_stream):
            resp = client.post("/v1/claude/chat/completions", json=_BODY)
        assert resp.status_code == 200
        assert resp.headers["content-type"].startswith("text/event-stream")
        chunks, done = _parse_sse(resp.text)
        assert done is True
        # first chunk announces role
        assert chunks[0]["choices"][0]["delta"].get("role") == "assistant"
        assert all(c["object"] == "chat.completion.chunk" for c in chunks)
        content = "".join(
            c["choices"][0]["delta"].get("content", "")
            for c in chunks if c["choices"]
        )
        assert content == "Hello world"
        # a finish chunk with finish_reason=stop exists
        assert any(
            c["choices"] and c["choices"][0]["finish_reason"] == "stop" for c in chunks
        )

    def test_usage_chunk_only_with_include_usage(self, client: TestClient) -> None:
        body = {**_BODY, "stream_options": {"include_usage": True}}
        with patch.object(main.claude_adapter, "stream", _fake_stream):
            resp = client.post("/v1/claude/chat/completions", json=body)
        chunks, _ = _parse_sse(resp.text)
        usage_chunks = [c for c in chunks if c.get("usage")]
        assert len(usage_chunks) == 1
        assert usage_chunks[0]["usage"]["prompt_tokens"] == 3
        assert usage_chunks[0]["usage"]["completion_tokens"] == 2

    def test_no_usage_chunk_by_default(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "stream", _fake_stream):
            resp = client.post("/v1/claude/chat/completions", json=_BODY)
        chunks, _ = _parse_sse(resp.text)
        assert not any(c.get("usage") for c in chunks)

    def test_stream_validates_policy(self, client: TestClient) -> None:
        # logprobs must be rejected before streaming starts.
        resp = client.post("/v1/claude/chat/completions", json={**_BODY, "logprobs": True})
        assert resp.status_code == 400
