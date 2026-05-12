"""Tests for JSON extraction and tool-use system prompt generation."""
import json
import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from tool_use_parser import build_tool_system_prompt, extract_json


# ---------------------------------------------------------------------------
# extract_json
# ---------------------------------------------------------------------------

def test_extract_plain_json():
    text = '{"approved": true, "findings": []}'
    result = extract_json(text)
    assert result is not None
    data = json.loads(result)
    assert data["approved"] is True


def test_extract_json_from_markdown_fence():
    text = '```json\n{"approved": false, "findings": []}\n```'
    result = extract_json(text)
    assert result is not None
    data = json.loads(result)
    assert data["approved"] is False


def test_extract_json_from_bare_fence():
    text = '```\n{"key": "value"}\n```'
    result = extract_json(text)
    assert result is not None
    assert json.loads(result)["key"] == "value"


def test_extract_json_embedded_in_prose():
    text = 'Here is my review: {"approved": true, "findings": []} Thank you.'
    result = extract_json(text)
    assert result is not None
    data = json.loads(result)
    assert data["approved"] is True


def test_extract_json_returns_none_for_no_json():
    result = extract_json("This is just plain text with no JSON content.")
    assert result is None


def test_extract_json_returns_none_for_malformed():
    result = extract_json("{this is not valid json}")
    assert result is None


def test_extract_json_nested_object():
    text = '{"a": {"b": {"c": 1}}}'
    result = extract_json(text)
    assert result is not None
    assert json.loads(result)["a"]["b"]["c"] == 1


# ---------------------------------------------------------------------------
# build_tool_system_prompt
# ---------------------------------------------------------------------------

def test_build_tool_system_prompt_contains_schema():
    tools = [
        {
            "type": "function",
            "function": {
                "name": "submit_review",
                "parameters": {
                    "type": "object",
                    "properties": {"approved": {"type": "boolean"}},
                },
            },
        }
    ]
    prompt = build_tool_system_prompt(tools)
    assert "submit_review" in prompt
    assert "approved" in prompt
    assert "JSON" in prompt


def test_build_tool_system_prompt_empty_tools():
    assert build_tool_system_prompt([]) == ""
