"""response_format / Structured Outputs support.

The CLIs have no native JSON-mode, so we constrain via a prompt addendum, extract the
JSON object from the (possibly fenced/prose-wrapped) CLI output, and validate it:
- json_object  → must parse as a JSON object
- json_schema  → must parse AND validate against the supplied JSON Schema
On failure the caller retries once; if still invalid it returns a refusal.
"""
from __future__ import annotations

import json
import logging

from openai_format import ResponseFormat
from tool_use_parser import extract_json

log = logging.getLogger(__name__)

try:
    import jsonschema
    from jsonschema import Draft202012Validator

    _HAVE_JSONSCHEMA = True
except ImportError:  # pragma: no cover - dependency declared in pyproject
    _HAVE_JSONSCHEMA = False


def build_response_format_instruction(rf: ResponseFormat) -> str:
    """Build the prompt addendum that constrains the model to the requested format."""
    if rf.type == "json_object":
        return (
            "\n\nIMPORTANT: Respond with ONLY a single valid JSON object. "
            "No prose, no explanation, no markdown fences. "
            "Start your response with '{' and end it with '}'."
        )
    if rf.type == "json_schema" and rf.json_schema is not None:
        schema = rf.json_schema.schema_ or {}
        schema_text = json.dumps(schema, ensure_ascii=False)
        return (
            "\n\nIMPORTANT: Respond with ONLY a single valid JSON object that strictly "
            "conforms to this JSON Schema. No prose, no markdown fences.\n"
            f"JSON Schema:\n{schema_text}"
        )
    return ""


def validate_and_extract(rf: ResponseFormat, raw_text: str) -> tuple[str | None, str | None]:
    """Extract and validate structured output.

    Returns (json_string, None) on success, or (None, error_message) on failure.
    """
    json_str = extract_json(raw_text)
    if not json_str:
        return None, "no JSON object found in the response"

    try:
        parsed = json.loads(json_str)
    except (json.JSONDecodeError, ValueError) as exc:
        return None, f"response is not valid JSON: {exc}"

    if not isinstance(parsed, dict):
        return None, "response JSON is not an object"

    if rf.type == "json_schema" and rf.json_schema is not None and rf.json_schema.schema_:
        if not _HAVE_JSONSCHEMA:
            log.warning("jsonschema not installed — skipping schema validation")
            return json_str, None
        try:
            Draft202012Validator(rf.json_schema.schema_).validate(parsed)
        except jsonschema.ValidationError as exc:
            return None, f"response does not match schema: {exc.message}"
        except jsonschema.SchemaError as exc:
            # A broken schema is the caller's fault, not the model's — treat as success
            # to avoid an infinite retry on an unvalidatable schema.
            log.warning("Invalid JSON schema supplied: %s", exc)
            return json_str, None

    return json_str, None
