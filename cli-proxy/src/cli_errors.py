"""Diagnostic texts for failed CLI runs, and the document-mode "empty result" failure.

Before 2026-11-24 a claude run that exited non-zero was reported as `stderr or stdout[:200]`.
claude writes its JSON result object to stdout, and since CLI 2.1.219 the long `usage` block
precedes the fields that name the cause (`subtype`, `is_error`, `result`, `api_error_status`),
so the 200-character cut kept only token counts and the cause was lost (Atelier pre-study V1,
three document-mode aborts that could not be diagnosed). These helpers pick the cause fields
instead and redact credential-like fragments, because the text travels into HTTP error bodies,
logs and the unauthenticated `/health`.

Nothing from the prompt or the background context is ever included: only fields of the CLI's own
result object, its stderr, or -- when stdout is not a JSON object -- the first 200 characters of
stdout, as before.
"""
from __future__ import annotations

import json

from redaction import redact_secrets

#: Maximum characters of the CLI's `result` text (or a codex error event) in a message.
RESULT_CHARS = 300
#: Maximum characters of raw stdout/stderr when there is no structured cause.
RAW_CHARS = 200
#: Characters scanned for secrets before clipping, so a key that straddles the cut is still caught.
_SCAN_CHARS = 8000

_CAUSE_FIELDS = ("subtype", "is_error", "api_error_status")


def clip_redacted(text: str, limit: int) -> str:
    """`text` redacted, then cut to `limit` characters."""
    return redact_secrets(text[:_SCAN_CHARS])[:limit]


def _json_object(text: str) -> dict | None:
    try:
        parsed = json.loads(text.strip())
    except (json.JSONDecodeError, ValueError):
        return None
    return parsed if isinstance(parsed, dict) else None


def _field(value: object) -> str:
    return value if isinstance(value, str) else json.dumps(value)


def result_cause(data: dict) -> str:
    """`<result> (subtype=…, is_error=…, api_error_status=…)` from a claude result object; empty
    when it carries none of these fields."""
    fields = ", ".join(f"{k}={_field(data[k])}" for k in _CAUSE_FIELDS if data.get(k) is not None)
    raw_result = data.get("result")
    result = clip_redacted(str(raw_result), RESULT_CHARS).strip() if raw_result not in (None, "") else ""
    if result and fields:
        return f"{result} ({fields})"
    return result or (f"({fields})" if fields else "")


def claude_failure_detail(stdout: str, stderr: str = "") -> str:
    """Cause of a claude run that exited non-zero.

    stdout a JSON object with cause fields: those fields (result clipped to 300 characters), plus
    stderr when there is any. Otherwise stderr, or the first 200 characters of stdout. Always
    redacted.
    """
    stderr = stderr.strip()
    data = _json_object(stdout)
    cause = result_cause(data) if data is not None else ""
    if cause:
        return f"{cause}; stderr: {clip_redacted(stderr, RAW_CHARS)}" if stderr else cause
    if stderr:
        return redact_secrets(stderr)
    return clip_redacted(stdout, RAW_CHARS)


def _codex_error_message(stdout: str) -> str:
    """Message of the last `error` / `turn.failed` event of a `codex exec --json` stream."""
    message = ""
    for line in stdout.splitlines():
        if '"error' not in line and '"turn.failed"' not in line:
            continue
        event = _json_object(line)
        if event is None:
            continue
        if event.get("type") == "error" and event.get("message"):
            message = str(event["message"])
        elif event.get("type") == "turn.failed":
            error = event.get("error")
            if isinstance(error, dict) and error.get("message"):
                message = str(error["message"])
    return message


def codex_failure_detail(stdout: str, stderr: str = "") -> str:
    """Cause of a codex run that exited non-zero: stderr as before, plus the message of the last
    error event of the JSONL stream (clipped to 300 characters); without either, the first 200
    characters of stdout. Always redacted."""
    stderr = stderr.strip()
    event = _codex_error_message(stdout)
    parts = []
    if stderr:
        parts.append(redact_secrets(stderr))
    if event:
        parts.append(f"error event: {clip_redacted(event, RESULT_CHARS)}")
    if parts:
        return "; ".join(parts)
    return clip_redacted(stdout, RAW_CHARS)


class EmptyResultError(Exception):
    """A document-mode run exited normally but left draft.md empty and unchanged.

    Deliberately not a RuntimeError: the failover loop must not hand it to the partner CLI as if
    it were a transport fault, and it must not end up as a successful HTTP 200 with empty text.
    """

    def __init__(self, backend: str) -> None:
        super().__init__(
            f"{backend} finished the document-mode run without writing any text: draft.md is "
            "still empty and unchanged.")
        self.backend = backend


def is_empty_document_result(result: str, document: str) -> bool:
    """True when the run produced no text: empty or whitespace-only AND identical to the input."""
    return not result.strip() and result == document
