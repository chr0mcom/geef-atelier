"""One ``claude -p`` call with stdin prompt, JSON output parsing, limit detection and a retry."""
from __future__ import annotations

import json
import re
import shutil
from dataclasses import dataclass
from typing import Callable

from .cli_runner import LIMIT_RE, LimitError, Runner, claude_command, empty_workdir, run_cli


@dataclass(frozen=True)
class ClaudeReply:
    """Successful reply of a claude call."""

    text: str
    obj: dict | None
    duration_s: float
    tokens_in: int
    tokens_out: int
    attempts: int


class ClaudeCallError(RuntimeError):
    """All attempts failed."""


def extract_json(text: str) -> dict:
    """Extracts the first JSON object from a model reply (tolerates code fences and surrounding prose)."""
    stripped = re.sub(r"^```(?:json)?\s*", "", text.strip())
    stripped = re.sub(r"\s*```$", "", stripped)
    start, end = stripped.find("{"), stripped.rfind("}")
    if start < 0 or end < start:
        raise ValueError("kein JSON-Objekt in der Antwort")
    value = json.loads(stripped[start:end + 1])
    if not isinstance(value, dict):
        raise ValueError("JSON-Antwort ist kein Objekt")
    return value


def call_claude(
    prompt: str,
    *,
    model: str,
    system_prompt: str,
    timeout_s: float,
    runner: Runner = run_cli,
    json_out: bool = False,
    validate: Callable[[dict], None] | None = None,
    tries: int = 2,
    log: Callable[[dict], None] | None = None,
    label: str = "",
) -> ClaudeReply:
    """Runs ``claude -p`` in an empty working directory; retries once on invalid or empty output."""
    total = 0.0
    last_error = "unbekannt"
    for attempt in range(1, tries + 1):
        workdir = empty_workdir("claude")
        try:
            result = runner(claude_command(model, system_prompt), prompt, workdir, timeout_s)
        finally:
            shutil.rmtree(workdir, ignore_errors=True)
        total += result.duration_s
        record = {
            "label": label, "cli": "claude", "model": model, "attempt": attempt,
            "duration_s": round(result.duration_s, 1), "rc": result.returncode, "timeout": result.timed_out,
        }
        text, raw_text, tokens_in, tokens_out = None, "", 0, 0
        try:
            payload = json.loads(result.stdout)
            text = raw_text = str(payload.get("result") or "")
            usage = payload.get("usage") or {}
            tokens_in = int(usage.get("input_tokens", 0)) + int(usage.get("cache_creation_input_tokens", 0)) \
                + int(usage.get("cache_read_input_tokens", 0))
            tokens_out = int(usage.get("output_tokens", 0))
            if payload.get("is_error"):
                last_error = f"is_error: {text[:200]}"
                text = None
        except (json.JSONDecodeError, AttributeError, TypeError, ValueError) as exc:
            last_error = f"Ausgabe nicht lesbar ({exc}); rc={result.returncode}; stderr={result.stderr[:200]}"

        if _hits_limit(raw_text, result.stderr):
            _log(log, {**record, "ok": False, "error": "LIMIT"})
            raise LimitError((raw_text or result.stderr)[:200])

        obj = None
        if text is not None and json_out:
            try:
                obj = extract_json(text)
                if validate is not None:
                    validate(obj)
            except ValueError as exc:
                last_error = f"JSON ungültig: {exc}"
                text = None

        if text is not None and text.strip():
            _log(log, {**record, "ok": True, "tokens_in": tokens_in, "tokens_out": tokens_out})
            return ClaudeReply(text, obj, round(total, 1), tokens_in, tokens_out, attempt)

        if result.timed_out:
            last_error = f"Zeitlimit {timeout_s:.0f} s überschritten"
        _log(log, {**record, "ok": False, "error": last_error})
    raise ClaudeCallError(f"{label}: {last_error}")


def _hits_limit(text: str, stderr: str) -> bool:
    if len(text) >= 400:
        return False
    return bool(LIMIT_RE.search(text[:400]) or LIMIT_RE.search(stderr[:400]))


def _log(log: Callable[[dict], None] | None, record: dict) -> None:
    if log is not None:
        log(record)
