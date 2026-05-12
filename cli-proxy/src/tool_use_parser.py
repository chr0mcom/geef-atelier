"""Extracts JSON tool call payloads from CLI plain-text output."""
from __future__ import annotations

import json
import re


def extract_json(text: str) -> str | None:
    """
    Attempts to find the first valid JSON object in *text*.

    Strategy:
    1. Strip markdown code fences (```json ... ``` or ``` ... ```).
    2. Try to parse the entire stripped text as JSON.
    3. If that fails, search for the first {...} block using a balanced-brace scan.
    4. Return the raw JSON string, or None if nothing parseable was found.
    """
    stripped = _strip_markdown_fences(text).strip()

    if _is_valid_json(stripped):
        return stripped

    candidate = _find_json_object(stripped)
    if candidate and _is_valid_json(candidate):
        return candidate

    return None


def build_tool_system_prompt(tools: list[dict]) -> str:
    """
    Generates the system prompt addendum that instructs the CLI to output
    a raw JSON object conforming to the first tool's schema.
    """
    if not tools:
        return ""

    tool = tools[0]
    function = tool.get("function", tool)
    name = function.get("name", "unknown")
    schema = function.get("parameters", {})
    schema_json = json.dumps(schema, ensure_ascii=False)

    return (
        f"\n\nIMPORTANT: You MUST respond with ONLY a valid JSON object. "
        f"Do not include any explanation, markdown, or prose. "
        f"The JSON must conform to this schema for the function '{name}':\n"
        f"{schema_json}"
    )


# ---------------------------------------------------------------------------
# Internal helpers
# ---------------------------------------------------------------------------

def _strip_markdown_fences(text: str) -> str:
    """Removes leading/trailing ```json ... ``` or ``` ... ``` fences."""
    pattern = re.compile(r"```(?:json)?\s*([\s\S]*?)\s*```", re.IGNORECASE)
    match = pattern.search(text)
    if match:
        return match.group(1)
    return text


def _is_valid_json(text: str) -> bool:
    try:
        json.loads(text)
        return True
    except (json.JSONDecodeError, ValueError):
        return False


def _find_json_object(text: str) -> str | None:
    """Finds the first balanced {...} substring in *text*."""
    start = text.find("{")
    if start == -1:
        return None

    depth = 0
    in_string = False
    escape_next = False

    for i, ch in enumerate(text[start:], start=start):
        if escape_next:
            escape_next = False
            continue
        if ch == "\\" and in_string:
            escape_next = True
            continue
        if ch == '"':
            in_string = not in_string
            continue
        if in_string:
            continue
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[start : i + 1]

    return None
