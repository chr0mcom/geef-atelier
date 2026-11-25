"""Acceptance measurement R2 of stage 4 (contract stage 4, 8): the parts of the gate that code can check.

Input: the stored answers of the MCP tool ``get_order_result`` (camelCase field names) for the orders of R2, one JSON object per
order: ``.json`` files, ``.jsonl`` files or directories (their ``*.json`` files).

Checked here:

- (a) every order has ``lookedUp`` and its status is neither ``not_needed`` nor ``files_attached`` (an order with files never
  searches, so it says nothing about the research).
- (c) passages the fact check flagged as ``not_backed`` although the research covers them. At most :data:`MAX_COVERED_UNBACKED`
  in total. A passage counts in code when its quote holds a number that stands in the quote or title of a ``lookedUp`` entry of the
  same order. A number is a run of digits; ``.`` and ``,`` between digits are removed (``1.000`` = ``1,000`` = ``1000``), and a
  number with a space or an apostrophe before groups of three digits counts joined as well as in its parts (``1 000`` = ``1000``,
  errs on the strict side); numbers are compared as a whole, so dates count by their digits. In an English text a passage
  also counts in code when it holds a name of such an entry: a capitalised word of at least :data:`MIN_NAME_CHARS` letters that does
  not start a sentence (start of the text or of a line, or after ``.``, ``!``, ``?``), compared as a whole word, case counts; where
  the passage stands in the text decides whether its first word starts a sentence. In a German text every noun is capitalised, so
  a shared capitalised word proves nothing: such a passage without a shared number is a case for a person (``--confirmations``,
  per case ``covered`` or ``not_covered``), and while any case is undecided the checked parts are not evaluable. The language of a
  text is German unless it holds more English than German function words (:data:`ENGLISH_WORDS`, :data:`GERMAN_WORDS`).
- (e) no marker of the writer's notes (:data:`NOTES_MARKER`, the constant ``WriterNotesFormat.StartMarker``) in any text.

Left to a person (reported as ``manual``, never decided here): (b) no question about a researched topic, and (d) the median
``DurationMs`` of the research is at most 60 s. The answers of ``get_order_result`` hold neither.

Not evaluable, never passed without data: fewer than :data:`MIN_ORDERS_WITH_TEXT` orders with a text, fewer than
:data:`MIN_ORDERS_FOUND` orders whose ``lookedUp`` is ``found`` with at least one entry (otherwise (c) has hardly any data), the
same order twice, an order with a text whose fact check did not run on it (``factCheck.state`` other than ``checked`` or
``open_points``) or without ``unbackedPassages``, a defect of the confirmation file, or an undecided case of (c).

Usage (from ``tools/Eval``)::

    python3 -m geef_eval.research_r2 --results <file or directory> [--results ...] [--confirmations <json>] --out <report.json outside Git>

The confirmation file is ``{"confirmations": [{"order": ..., "case": ..., "quote": ..., "decision": "covered" | "not_covered"}]}``;
an entry counts only when it names a case of this evaluation, repeats its quote exactly and is the only one for that case.

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
MIN_ORDERS_FOUND = 4
FOUND = "found"
COVERED = "covered"
NOT_COVERED = "not_covered"
DECISIONS = (COVERED, NOT_COVERED)
GERMAN_WORDS = frozenset({"der", "die", "das", "und", "ist", "nicht", "mit", "für", "ein", "eine", "einen", "den", "dem", "des",
                          "wir", "ich", "sie", "auf", "bei", "von", "sich", "auch", "es", "im", "zu", "zum", "zur", "wird", "sind"})
ENGLISH_WORDS = frozenset({"the", "and", "is", "not", "with", "for", "of", "to", "on", "we", "you", "it", "this", "that", "are",
                           "be", "by", "from", "will", "at", "our", "your", "has", "have"})
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
GROUPED_RE = re.compile(r"(?<!\d)\d{1,3}(?:[ '\u2019\u02bc\u00a0\u202f\u2009\u2007\u2008]\d{3})+(?:[.,]\d+)?(?!\d)")
WORD_RE = re.compile(r"[^\W\d_]+(?:[-'’][^\W\d_]+)*")
OPENERS = " \t\r\"'„“”‚‘’«»‹›()[]{}*_#>-–—•"
SENTENCE_ENDS = ".!?…"
ABBREVIATIONS = frozenset({"bzw", "ca", "dr", "fr", "hr", "inkl", "nr", "prof", "st", "mr", "mrs", "ms", "no"})


def _clean(text: object) -> str:
    return unicodedata.normalize("NFC", str(text or "").replace("\u00ad", ""))


def numbers(text: str) -> set[str]:
    clean = _clean(text)
    return {re.sub(r"\D", "", match) for match in NUMBER_RE.findall(clean) + GROUPED_RE.findall(clean)}


def language(text: str) -> str:
    """``en`` when ``text`` holds more English than German function words, otherwise ``de`` (the side with the hand check)."""
    words = [w.lower() for w in WORD_RE.findall(_clean(text))]
    return "en" if sum(w in ENGLISH_WORDS for w in words) > sum(w in GERMAN_WORDS for w in words) else "de"


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


def _entry_text(entries: list[dict]) -> str:
    return " \n".join(f"{e.get('quote') or ''} \n{e.get('title') or ''}" for e in entries)


def covered_numbers(quote: str, entries: list[dict]) -> list[str]:
    """The numbers of ``quote`` that stand in a looked-up entry (its quote or title)."""
    return sorted(numbers(quote) & numbers(_entry_text(entries)))


def shared_names(quote: str, text: str, entries: list[dict]) -> list[str]:
    """The capitalised words of ``quote`` (not starting a sentence) that stand in a looked-up entry."""
    return sorted(names(quote, text) & set(WORD_RE.findall(_clean(_entry_text(entries)))))


def _decisions(confirmations: object, cases: list[dict]) -> tuple[dict[tuple[str, str], str], list[str]]:
    """The hand decisions per ``(order, case)`` and what is wrong with the confirmation file."""
    if confirmations is None:
        return {}, []
    entries = confirmations.get("confirmations") if isinstance(confirmations, dict) else None
    if not isinstance(entries, list):
        return {}, ["Bestätigungsdatei ohne Liste confirmations"]
    known = {(c["orderId"], c["case"]): c for c in cases}
    decisions: dict[tuple[str, str], str] = {}
    problems: list[str] = []
    seen: set[tuple[str, str]] = set()
    for entry in entries:
        if not isinstance(entry, dict):
            problems.append("Bestätigung ist kein Objekt")
            continue
        key = (str(entry.get("order")), str(entry.get("case")))
        name = f"{key[0]}/{key[1]}"
        if key in seen:
            problems.append(f"Bestätigung doppelt: {name}")
            decisions.pop(key, None)
            continue
        seen.add(key)
        if key not in known:
            problems.append(f"Bestätigung zu unbekanntem Fall: {name}")
        elif entry.get("quote") != known[key]["quote"]:
            problems.append(f"Bestätigung passt nicht zum Wortlaut des Falls: {name}")
        elif entry.get("decision") not in DECISIONS:
            problems.append(f"Bestätigung ohne gültige Entscheidung ({' oder '.join(DECISIONS)}): {name}")
        else:
            decisions[key] = entry["decision"]
    return decisions, problems


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


def evaluate(results: list[dict], confirmations: object = None) -> dict:
    """The checked parts of gate R2; ``automated_verdict`` is ``passed``, ``failed`` or ``not_evaluable``."""
    problems: list[str] = []
    ids = [str(r["orderId"]) for r in results]
    if twice := sorted({i for i in ids if ids.count(i) > 1}):
        problems.append(f"Aufträge doppelt: {', '.join(twice)}")
    with_text = [r for r in results if str(r.get("text") or "").strip()]
    if len(with_text) < MIN_ORDERS_WITH_TEXT:
        problems.append(f"nur {len(with_text)} Aufträge mit Text (mindestens {MIN_ORDERS_WITH_TEXT})")

    orders, without_research, covered, cases, markers, found = [], [], [], [], [], 0
    for result in results:
        order_id = str(result["orderId"])
        looked = result.get("lookedUp")
        status = (looked or {}).get("status")
        entries = (looked or {}).get("entries") or []
        if status == FOUND and entries:
            found += 1
        text = str(result.get("text") or "")
        text_language = language(text)
        orders.append({"orderId": order_id, "looked_up": status, "entries": len(entries), "language": text_language})
        if looked is None or status in (None, NOT_NEEDED, FILES_ATTACHED):
            without_research.append(order_id)
        if count := len(NOTES_MARKER_RE.findall(text)):
            markers.append({"orderId": order_id, "count": count})
        passages = result.get("unbackedPassages")
        if text.strip():
            check = (result.get("factCheck") or {}).get("state")
            if check not in CHECK_RAN:
                problems.append(f"{order_id}: Faktenprüfung lief nicht für den Text ({check})")
            if not isinstance(passages, list):
                problems.append(f"{order_id}: unbackedPassages fehlt")
        for index, passage in enumerate(passages or []):
            if passage.get("kind") != NOT_BACKED:
                continue
            quote = str(passage.get("quote") or "")
            if matches := covered_numbers(quote, entries):
                covered.append({"orderId": order_id, "quote": passage.get("quote"), "matches": matches})
            elif not (shared := shared_names(quote, text, entries)):
                continue
            elif text_language == "en":
                covered.append({"orderId": order_id, "quote": passage.get("quote"), "matches": shared})
            else:
                cases.append({"orderId": order_id, "case": f"n{index + 1}", "quote": passage.get("quote"), "names": shared})
    if found < MIN_ORDERS_FOUND:
        problems.append(f"nur {found} Aufträge mit lookedUp „found“ und mindestens einem Eintrag (mindestens {MIN_ORDERS_FOUND}): "
                        "(c) hätte zu wenig Daten")

    decisions, defects = _decisions(confirmations, cases)
    problems.extend(defects)
    for case in cases:
        case["decision"] = decisions.get((case["orderId"], case["case"]))
    if open_cases := [c for c in cases if c["decision"] is None]:
        problems.append(f"{len(open_cases)} Stellen in deutschen Texten teilen nur ein großgeschriebenes Wort mit einem Eintrag und sind "
                        "nicht von Hand entschieden (--confirmations)")
    count = len(covered) + sum(1 for c in cases if c["decision"] == COVERED)

    parts = {
        "a": {"passed": bool(results) and not without_research, "without_research": without_research},
        "c": {"passed": count <= MAX_COVERED_UNBACKED, "count": count, "passages": covered, "cases": cases,
              "open_cases": len(open_cases)},
        "e": {"passed": not markers, "count": sum(m["count"] for m in markers), "orders": markers},
    }
    verdict = "not_evaluable" if problems else ("passed" if all(p["passed"] for p in parts.values()) else "failed")
    return {"gate": "R2", "automated_verdict": verdict, "problems": problems, "orders_with_text": len(with_text),
            "parts": parts, "manual": dict(MANUAL), "orders": orders}


def _print_report(report: dict, echo: Callable[[str], None]) -> None:
    parts = report["parts"]
    for order in report["orders"]:
        echo(f"{order['orderId']}: lookedUp {order['looked_up'] or 'null'}, {order['entries']} Einträge, Sprache {order['language']}")
    echo(f"R2 (a) {'bestanden' if parts['a']['passed'] else 'nicht bestanden'}: {len(parts['a']['without_research'])} Aufträge ohne "
         "Recherche")
    echo(f"R2 (c) {'bestanden' if parts['c']['passed'] else 'nicht bestanden'}: {parts['c']['count']} als ungedeckt gemeldete, aber "
         f"nachgeschlagene Stellen (höchstens {MAX_COVERED_UNBACKED})")
    for passage in parts["c"]["passages"]:
        echo(f"  {passage['orderId']}: „{passage['quote']}“ (Treffer: {', '.join(passage['matches'])})")
    for case in parts["c"]["cases"]:
        echo(f"  Fall {case['orderId']}/{case['case']} ({case['decision'] or 'offen'}): „{case['quote']}“ "
             f"(gemeinsame Wörter: {', '.join(case['names'])})")
    echo(f"R2 (e) {'bestanden' if parts['e']['passed'] else 'nicht bestanden'}: {parts['e']['count']} Notizmarker im Text")
    for key, line in report["manual"].items():
        echo(f"R2 ({key}) manual: {line}")
    for problem in report["problems"]:
        echo(f"nicht auswertbar: {problem}")
    echo(f"R2 [im Code geprüft: a, c, e] {report['automated_verdict']}; (b) und (d) bleiben Handauswertung")


def main(argv: list[str] | None = None, *, echo: Callable[[str], None] = print) -> int:
    parser = argparse.ArgumentParser(prog="research_r2", description=__doc__.split("\n\n")[0])
    parser.add_argument("--results", action="append", required=True, type=Path)
    parser.add_argument("--confirmations", type=Path, help="hand decisions on the cases of (c) in German texts (JSON)")
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--allow-in-repo", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args(argv)
    if not args.allow_in_repo and inside_git_worktree(args.out.parent):
        echo(f"{args.out} liegt in einem Git-Baum; Zitate aus Texten gehören nicht ins Repository.")
        return 2
    try:
        confirmations = None if args.confirmations is None else json.loads(args.confirmations.read_text(encoding="utf-8"))
        report = evaluate(load_results(args.results), confirmations)
    except (OSError, ValueError, AttributeError, TypeError) as exc:
        echo(f"Antworten nicht lesbar: {exc}")
        return 2
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    _print_report(report, echo)
    return {"passed": 0, "failed": 1}.get(report["automated_verdict"], 4)


if __name__ == "__main__":
    sys.exit(main())
