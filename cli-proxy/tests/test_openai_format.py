"""Tests for OpenAI schema serialisation helpers."""
import json

import pytest

import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from openai_format import (
    ChatCompletionRequest,
    ChatCompletionResponse,
    make_text_response,
    make_tool_response,
)


def test_make_text_response_structure():
    resp = make_text_response("claude-opus-4-5", "Hello world")
    assert resp.model == "claude-opus-4-5"
    assert len(resp.choices) == 1
    assert resp.choices[0].message.content == "Hello world"
    assert resp.choices[0].message.tool_calls is None
    assert resp.choices[0].finish_reason == "stop"


def test_make_tool_response_structure():
    args = json.dumps({"approved": False, "findings": []})
    resp = make_tool_response("claude-opus-4-5", "submit_review", args)
    assert resp.choices[0].finish_reason == "tool_calls"
    assert resp.choices[0].message.content is None
    calls = resp.choices[0].message.tool_calls
    assert calls is not None and len(calls) == 1
    assert calls[0].function.name == "submit_review"
    assert calls[0].function.arguments == args


def test_chat_completion_request_parsing():
    payload = {
        "model": "claude-opus-4-5",
        "messages": [{"role": "user", "content": "Hello"}],
    }
    req = ChatCompletionRequest.model_validate(payload)
    assert req.model == "claude-opus-4-5"
    assert req.messages[0].role == "user"
    assert req.stream is False


def test_chat_completion_response_is_serialisable():
    resp = make_text_response("test-model", "test output")
    data = json.loads(resp.model_dump_json())
    assert data["object"] == "chat.completion"
    assert data["choices"][0]["finish_reason"] == "stop"
