"""Tests for agentic tool-use (multi-turn) support."""
import json
import pytest
from tool_use_parser import build_agentic_tool_system_prompt, parse_agentic_response
from openai_format import ChatMessage, ToolChoiceObject, ToolChoiceFunction

TOOLS = [{"function": {"name": "web_search", "description": "Search the web", "parameters": {"type": "object", "properties": {"query": {"type": "string"}}, "required": ["query"]}}}]


def test_build_agentic_prompt_contains_tool_name():
    prompt = build_agentic_tool_system_prompt(TOOLS)
    assert "web_search" in prompt


def test_build_agentic_prompt_contains_protocol():
    prompt = build_agentic_tool_system_prompt(TOOLS)
    assert "tool_call" in prompt


def test_parse_agentic_response_tool_call():
    text = '{"tool_call": {"name": "web_search", "arguments": {"query": "test"}}}'
    name, args = parse_agentic_response(text)
    assert name == "web_search"
    assert json.loads(args)["query"] == "test"


def test_parse_agentic_response_final_text():
    text = "Here is the answer: The capital of France is Paris."
    name, args = parse_agentic_response(text)
    assert name is None
    assert args is None


def test_parse_agentic_response_with_markdown_fence():
    text = "```json\n{\"tool_call\": {\"name\": \"web_search\", \"arguments\": {\"query\": \"foo\"}}}\n```"
    name, args = parse_agentic_response(text)
    assert name == "web_search"


def test_parse_agentic_response_plain_json_no_tool_call():
    # A JSON response that is NOT a tool_call (e.g. forced submit_review) → not agentic
    text = '{"approved": true, "findings": []}'
    name, args = parse_agentic_response(text)
    assert name is None


def test_build_prompt_renders_tool_results():
    """Tool-result messages should be rendered in the prompt."""
    import sys
    sys.path.insert(0, "src")
    from main import _build_prompt
    from openai_format import ChatMessage, ChatCompletionRequest
    req = ChatCompletionRequest(
        model="claude-opus-4-5",
        messages=[
            ChatMessage(role="system", content="System"),
            ChatMessage(role="user", content="Question"),
            ChatMessage(role="tool", content="Search results here", name="web_search", tool_call_id="call_123"),
        ],
    )
    prompt = _build_prompt(req)
    assert "TOOL RESULT" in prompt
    assert "Search results here" in prompt
