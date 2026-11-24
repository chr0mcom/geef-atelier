"""Deterministic text metrics: word count, bracket placeholders, length rule taken from the brief."""
from __future__ import annotations

import re
from dataclasses import dataclass

from .tasks import LengthRule, Task

PLACEHOLDER_RE = re.compile(r"\[[^\[\]\n]{1,200}\]")
WORD_UNIT = r"(?:wörtern|wörter|worte|words?)"
NUMBER = r"(\d{1,3}(?:[.  ]\d{3})*|\d+)"
DASH = r"\s*(?:-|–|—|bis|to)\s*"

RANGE_RE = re.compile(rf"(?:zwischen\s+|between\s+)?{NUMBER}{DASH}{NUMBER}\s*{WORD_UNIT}", re.IGNORECASE)
BETWEEN_RE = re.compile(rf"(?:zwischen|between)\s+{NUMBER}\s+(?:und|and)\s+{NUMBER}\s*{WORD_UNIT}", re.IGNORECASE)
MAX_RE = re.compile(
    rf"(?:höchstens|maximal|max\.?|nicht mehr als|bis zu|unter|at most|no more than|up to|under|maximum(?: of)?)\s+{NUMBER}\s*{WORD_UNIT}",
    re.IGNORECASE,
)
MIN_RE = re.compile(
    rf"(?:mindestens|wenigstens|min\.?|nicht weniger als|at least|no fewer than|minimum(?: of)?)\s+{NUMBER}\s*{WORD_UNIT}",
    re.IGNORECASE,
)
ABOUT_RE = re.compile(rf"(?:ca\.?|circa|etwa|rund|ungefähr|about|around|approximately|roughly)\s+{NUMBER}\s*{WORD_UNIT}", re.IGNORECASE)
APPROX_TOLERANCE = 0.15


@dataclass(frozen=True)
class TextMetrics:
    """Deterministic figures for one text."""

    words: int
    placeholders: int
    length_rule: LengthRule | None
    length_ok: bool | None


def count_words(text: str) -> int:
    """Counts whitespace-separated tokens that contain at least one word character."""
    return len([t for t in re.findall(r"\S+", text) if re.search(r"\w", t)])


def find_placeholders(text: str) -> list[str]:
    """Returns bracket placeholders such as ``[Datum der Veranstaltung]``."""
    return PLACEHOLDER_RE.findall(text)


def _to_int(value: str) -> int:
    return int(re.sub(r"[.  ]", "", value))


def length_rule_from_text(text: str) -> LengthRule | None:
    """Reads a word-count rule from free text (German or English); None when the text names none."""
    if match := BETWEEN_RE.search(text) or RANGE_RE.search(text):
        low, high = sorted((_to_int(match.group(1)), _to_int(match.group(2))))
        return LengthRule(low, high)
    if match := MAX_RE.search(text):
        return LengthRule(None, _to_int(match.group(1)))
    if match := MIN_RE.search(text):
        return LengthRule(_to_int(match.group(1)), None)
    if match := ABOUT_RE.search(text):
        target = _to_int(match.group(1))
        return LengthRule(round(target * (1 - APPROX_TOLERANCE)), round(target * (1 + APPROX_TOLERANCE)))
    return None


def length_rule_for(task: Task) -> LengthRule | None:
    """Explicit task rule first, then the short brief, then the withheld original brief."""
    if task.length is not None:
        return task.length
    return length_rule_from_text(task.brief) or (
        length_rule_from_text(task.original_brief) if task.original_brief else None
    )


def complies(words: int, rule: LengthRule | None) -> bool | None:
    """True/False against a rule; None when there is no rule."""
    if rule is None:
        return None
    if rule.min_words is not None and words < rule.min_words:
        return False
    if rule.max_words is not None and words > rule.max_words:
        return False
    return True


def measure(text: str, task: Task) -> TextMetrics:
    """All deterministic metrics of one arm output for one task."""
    words = count_words(text)
    rule = length_rule_for(task)
    return TextMetrics(words, len(find_placeholders(text)), rule, complies(words, rule))
