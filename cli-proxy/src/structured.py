"""response_format / Structured Outputs support.

The CLIs have no native JSON-mode, so we constrain via a prompt addendum, extract the
JSON object from the (possibly fenced/prose-wrapped) CLI output, and validate it:
- json_object  → must parse as a JSON object
- json_schema  → must parse AND validate against the supplied JSON Schema
On failure the caller retries once (claude: in lean mode, see claude_adapter); if still
invalid it returns a refusal.
"""
from __future__ import annotations

import json
import logging
import re

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


# Bounds for the candidate scan: how many '{' positions it tries and how many characters it
# may inspect in total. A long prose answer with many unbalanced braces must not turn the
# scan quadratic; real answers hit a candidate far earlier.
_MAX_SCAN_STARTS = 400
_MAX_SCAN_STEPS = 2_000_000

_FENCE_RE = re.compile(r"```[a-zA-Z0-9_-]*[ \t]*\n?([\s\S]*?)```")


def _balanced_object_at(text: str, start: int, limit: int) -> int | None:
    """End index (exclusive) of the balanced {...} starting at `start`, or None. Inspects
    at most `limit` characters."""
    depth = 0
    in_string = False
    escape_next = False
    for i in range(start, min(len(text), start + limit)):
        ch = text[i]
        if escape_next:
            escape_next = False
            continue
        if in_string:
            if ch == "\\":
                escape_next = True
            elif ch == '"':
                in_string = False
            continue
        if ch == '"':
            in_string = True
        elif ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return i + 1
    return None


def iter_json_object_candidates(text: str):
    """Yield every JSON object that can be found in `text`, best guesses first.

    Order: the whole (stripped) text, the body of every markdown fence, then each balanced
    {...} span in reading order. Unlike tool_use_parser.extract_json, which only ever looks
    at the first fence and the first '{', a stray brace or an echoed schema before the
    answer does not hide the real object. Only dicts are yielded, each at most once.
    """
    seen: set[str] = set()

    def _try(chunk: str):
        chunk = chunk.strip()
        if not chunk or chunk in seen:
            return None
        seen.add(chunk)
        try:
            parsed = json.loads(chunk)
        except (json.JSONDecodeError, ValueError):
            return None
        return (chunk, parsed) if isinstance(parsed, dict) else None

    for chunk in [text] + [m.group(1) for m in _FENCE_RE.finditer(text)]:
        hit = _try(chunk)
        if hit:
            yield hit

    pos = 0
    starts = 0
    budget = _MAX_SCAN_STEPS
    while starts < _MAX_SCAN_STARTS and budget > 0:
        start = text.find("{", pos)
        if start == -1:
            return
        starts += 1
        end = _balanced_object_at(text, start, budget)
        budget -= (end - start) if end is not None else (len(text) - start)
        if end is None:
            pos = start + 1
            continue
        hit = _try(text[start:end])
        if hit:
            yield hit
            pos = end
        else:
            pos = start + 1


def _schema_error(rf: ResponseFormat, parsed: dict) -> str | None:
    """Schema violation message for `parsed`, or None when it conforms (or cannot be checked)."""
    if not (rf.type == "json_schema" and rf.json_schema is not None and rf.json_schema.schema_):
        return None
    if not _HAVE_JSONSCHEMA:
        log.warning("jsonschema not installed — skipping schema validation")
        return None
    try:
        Draft202012Validator(rf.json_schema.schema_).validate(parsed)
    except jsonschema.ValidationError as exc:
        return f"response does not match schema: {exc.message}"
    except jsonschema.SchemaError as exc:
        # A broken schema is the caller's fault, not the model's — treat as success
        # to avoid an infinite retry on an unvalidatable schema.
        log.warning("Invalid JSON schema supplied: %s", exc)
    return None


def validate_and_extract(rf: ResponseFormat, raw_text: str) -> tuple[str | None, str | None]:
    """Extract and validate structured output.

    Returns (json_string, None) on success, or (None, error_message) on failure. Every JSON
    object found in the text is considered; the first one that satisfies the schema wins.
    When none does, the error describes the first object found (or that there was none).
    """
    first_error: str | None = None
    for json_str, parsed in iter_json_object_candidates(raw_text or ""):
        err = _schema_error(rf, parsed)
        if err is None:
            return json_str, None
        first_error = first_error or err

    if first_error:
        return None, first_error
    # The previous extractor (first fence / first balanced brace, unbounded) as a safety net,
    # so the bounded scan above never finds less than before — and for the diagnosis of
    # non-object JSON such as a bare array.
    legacy = extract_json(raw_text or "")
    if legacy is None:
        return None, "no JSON object found in the response"
    parsed = json.loads(legacy)
    if not isinstance(parsed, dict):
        return None, "response JSON is not an object"
    err = _schema_error(rf, parsed)
    return (legacy, None) if err is None else (None, err)
