"""Acceptance measurement R2 of stage 4 (contract stage 4, 8): the parts of the gate that code can check.

Input: the stored answers of the MCP tool ``get_order_result`` (camelCase field names) for the orders of R2, one JSON object per
order: ``.json`` files, ``.jsonl`` files or directories (their ``*.json`` files).

Checked here:

- (a) every order has ``lookedUp`` and its status is neither ``not_needed`` nor ``files_attached`` (an order with files never
  searches, so it says nothing about the research).
- (c) passages the fact check flagged as ``not_backed`` although the research covers them: the passage's quote holds a number or a
  name that stands in the quote or title of a ``lookedUp`` entry of the same order. At most :data:`MAX_COVERED_UNBACKED` in total.
  A number is a run of digits; ``.`` and ``,`` between digits are removed (``1.000`` = ``1,000`` = ``1000``); numbers are compared
  as a whole. A name is a capitalised word of at least :data:`MIN_NAME_CHARS` letters that does not start a sentence (start of the
  text or of a line, or after ``.``, ``!``, ``?``); it is compared as a whole word, case counts. Where the passage stands in the
  text decides whether its first word starts a sentence. In German every noun is capitalised and therefore counts as a name: the
  rule errs on the strict side.
- (e) no marker of the writer's notes (:data:`NOTES_MARKER`, the constant ``WriterNotesFormat.StartMarker``) in any text.

Left to a person (reported as ``manual``, never decided here): (b) no question about a researched topic, and (d) the median
``DurationMs`` of the research is at most 60 s. The answers of ``get_order_result`` hold neither.

Not evaluable, never passed without data: fewer than :data:`MIN_ORDERS_WITH_TEXT` orders with a text, the same order twice, an
order with a text whose fact check did not run on it (``factCheck.state`` other than ``checked`` or ``open_points``) or without
``unbackedPassages``, or no ``lookedUp`` entry in any order.

Usage (from ``tools/Eval``)::

    python3 -m geef_eval.research_r2 --results <file or directory> [--results ...] --out <report.json outside Git>

Exit codes: 0 the checked parts passed ((b) and (d) are still open), 1 a checked part failed, 2 wrong usage or unreadable input,
4 not evaluable.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import unicodedata
from pathlib import Path
from typing import Callable

from .cli import inside_git_worktree

NOTES_MARKER = "<!-- atelier-notes"
NOTES_MARKER_RE = re.compile(r"<!--[ \t]*atelier[-_ \u2011]?notes", re.IGNORECASE)
"""Every spelling the C# ``WriterNotesFormat`` accepts, and the defused one (U+2011): none of them belongs into a delivered text."""
MIN_ORDERS_WITH_TEXT = 4
MAX_COVERED_UNBACKED = 1
MIN_NAME_CHARS = 3
NOT_BACKED = "not_backed"
NOT_NEEDED = "not_needed"
FILES_ATTACHED = "files_attached"
CHECK_RAN = frozenset({"checked", "open_points"})
MANUAL = {
    "b": "0 Fragen zu recherchierten Themen: von Hand an den Rückfragen der Aufträge prüfen",
    "d": "Median DurationMs der Recherche höchstens 60 s: von Hand aus den Daten des Ateliers ermitteln",
}

NUMBER_RE = re.compile(r"\d+(?:[.,]\d+)*")
WORD_RE = re.compile(r"[^\W\d_]+(?:[-'’][^\W\d_]+)*")
OPENERS = " \t\r\"'„“”‚‘’«»‹›()[]{}*_#>-–—•"
SENTENCE_ENDS = ".!?…"
ABBREVIATIONS = frozenset({"bzw", "ca", "dr", "fr", "hr", "inkl", "nr", "prof", "st", "mr", "mrs", "ms", "no"})


def _clean(text: object) -> str:
    return unicodedata.normalize("NFC", str(text or "").replace("\u00ad", ""))


def numbers(text: str) -> set[str]:
    return {re.sub(r"[.,]", "", match) for match in NUMBER_RE.findall(_clean(text))}


def _starts_sentence(text: str, index: int) -> bool:
    before = text[:index].rstrip(OPENERS)
    if not before or before.endswith("\n"):
        return True
    if before[-1] not in SENTENCE_ENDS:
        return False
    if before[-1] != ".":
        return True
    last = re.search(r"(\w+)\.$", before)
    return not (last and (last.group(1).isdecimal() or last.group(1).lower() in ABBREVIATIONS))


def names(quote: str, text: str = "") -> set[str]:
    """Capitalised words of ``quote`` that do not start a sentence, judged where the quote stands in ``text`` when it stands there."""
    quote, text = _clean(quote), _clean(text)
    offset = text.find(quote)
    context = text if offset >= 0 else quote
    offset = max(offset, 0)
    return {match.group() for match in WORD_RE.finditer(quote)
            if len(match.group()) >= MIN_NAME_CHARS and match.group()[0].isupper()
            and not _starts_sentence(context, offset + match.start())}


def covered_by_research(quote: str, text: str, entries: list[dict]) -> list[str]:
    """The numbers and names of ``quote`` that stand in a looked-up entry (its quote or title)."""
    found = " \n".join(f"{e.get('quote') or ''} \n{e.get('title') or ''}" for e in entries)
    words = set(WORD_RE.findall(_clean(found)))
    return sorted(numbers(quote) & numbers(found)) + sorted(names(quote, text) & words)


def load_results(paths: list[Path]) -> list[dict]:
    """All stored answers of ``paths``; raises ValueError for anything that is no ``get_order_result`` answer."""
    files: list[Path] = []
    for path in paths:
        files.extend(sorted(path.glob("*.json")) if path.is_dir() else [path])
    results = []
    for path in files:
        content = path.read_text(encoding="utf-8")
        chunks = [line for line in content.split("\n") if line.strip()] if path.suffix == ".jsonl" else [content]
        for chunk in chunks:
            result = json.loads(chunk)
            if not isinstance(result, dict) or not result.get("orderId") or "state" not in result:
                raise ValueError(f"{path} holds no answer of get_order_result (orderId, state)")
            results.append(result)
    return results


def evaluate(results: list[dict]) -> dict:
    """The checked parts of gate R2; ``automated_verdict`` is ``passed``, ``failed`` or ``not_evaluable``."""
    problems: list[str] = []
    ids = [str(r["orderId"]) for r in results]
    if twice := sorted({i for i in ids if ids.count(i) > 1}):
        problems.append(f"Aufträge doppelt: {', '.join(twice)}")
    with_text = [r for r in results if str(r.get("text") or "").strip()]
    if len(with_text) < MIN_ORDERS_WITH_TEXT:
        problems.append(f"nur {len(with_text)} Aufträge mit Text (mindestens {MIN_ORDERS_WITH_TEXT})")

    orders, without_research, covered, markers, entry_total = [], [], [], [], 0
    for result in results:
        order_id = str(result["orderId"])
        looked = result.get("lookedUp")
        status = (looked or {}).get("status")
        entries = (looked or {}).get("entries") or []
        entry_total += len(entries)
        orders.append({"orderId": order_id, "looked_up": status, "entries": len(entries)})
        if looked is None or status in (None, NOT_NEEDED, FILES_ATTACHED):
            without_research.append(order_id)
        text = str(result.get("text") or "")
        if count := len(NOTES_MARKER_RE.findall(text)):
            markers.append({"orderId": order_id, "count": count})
        passages = result.get("unbackedPassages")
        if text.strip():
            check = (result.get("factCheck") or {}).get("state")
            if check not in CHECK_RAN:
                problems.append(f"{order_id}: Faktenprüfung lief nicht für den Text ({check})")
            if not isinstance(passages, list):
                problems.append(f"{order_id}: unbackedPassages fehlt")
        for passage in passages or []:
            if passage.get("kind") != NOT_BACKED:
                continue
            if matches := covered_by_research(str(passage.get("quote") or ""), text, entries):
                covered.append({"orderId": order_id, "quote": passage.get("quote"), "matches": matches})
    if entry_total == 0:
        problems.append("kein lookedUp-Eintrag in irgendeinem Auftrag: (c) hätte keine Daten")

    parts = {
        "a": {"passed": bool(results) and not without_research, "without_research": without_research},
        "c": {"passed": len(covered) <= MAX_COVERED_UNBACKED, "count": len(covered), "passages": covered},
        "e": {"passed": not markers, "count": sum(m["count"] for m in markers), "orders": markers},
    }
    verdict = "not_evaluable" if problems else ("passed" if all(p["passed"] for p in parts.values()) else "failed")
    return {"gate": "R2", "automated_verdict": verdict, "problems": problems, "orders_with_text": len(with_text),
            "parts": parts, "manual": dict(MANUAL), "orders": orders}


def _print_report(report: dict, echo: Callable[[str], None]) -> None:
    parts = report["parts"]
    for order in report["orders"]:
        echo(f"{order['orderId']}: lookedUp {order['looked_up'] or 'null'}, {order['entries']} Einträge")
    echo(f"R2 (a) {'bestanden' if parts['a']['passed'] else 'nicht bestanden'}: {len(parts['a']['without_research'])} Aufträge ohne "
         "Recherche")
    echo(f"R2 (c) {'bestanden' if parts['c']['passed'] else 'nicht bestanden'}: {parts['c']['count']} als ungedeckt gemeldete, aber "
         f"nachgeschlagene Stellen (höchstens {MAX_COVERED_UNBACKED})")
    for passage in parts["c"]["passages"]:
        echo(f"  {passage['orderId']}: „{passage['quote']}“ (Treffer: {', '.join(passage['matches'])})")
    echo(f"R2 (e) {'bestanden' if parts['e']['passed'] else 'nicht bestanden'}: {parts['e']['count']} Notizmarker im Text")
    for key, line in report["manual"].items():
        echo(f"R2 ({key}) manual: {line}")
    for problem in report["problems"]:
        echo(f"nicht auswertbar: {problem}")
    echo(f"R2 [im Code geprüft: a, c, e] {report['automated_verdict']}; (b) und (d) bleiben Handauswertung")


def main(argv: list[str] | None = None, *, echo: Callable[[str], None] = print) -> int:
    parser = argparse.ArgumentParser(prog="research_r2", description=__doc__.split("\n\n")[0])
    parser.add_argument("--results", action="append", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--allow-in-repo", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args(argv)
    if not args.allow_in_repo and inside_git_worktree(args.out.parent):
        echo(f"{args.out} liegt in einem Git-Baum; Zitate aus Texten gehören nicht ins Repository.")
        return 2
    try:
        report = evaluate(load_results(args.results))
    except (OSError, ValueError, AttributeError, TypeError) as exc:
        echo(f"Antworten nicht lesbar: {exc}")
        return 2
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    _print_report(report, echo)
    return {"passed": 0, "failed": 1}.get(report["automated_verdict"], 4)


if __name__ == "__main__":
    sys.exit(main())
