"""Independent check of measurement VR (contract stage 4, 3.3): every stored dossier entry stands word for word in its raw text.

Own implementation, not a port of the C# code. White space is collapsed (every run of Unicode white space becomes one space, the
ends are trimmed); nothing else is normalised, case counts. ``start`` and ``length`` of an entry are UTF-16 code units, as in .NET.

Checks per entry, the first failing one is reported:

- ``empty_quote``: the quote is blank.
- ``page_missing``: no stored page of the line has the SHA-256 (UTF-8) the entry names; also when the line stored no raw texts.
- ``url_mismatch``: the page with that raw text has another address.
- ``range_outside``: the stored range does not lie inside the raw text.
- ``range_mismatch``: the stored range does not hold the quote.
- ``not_in_page``: the quote is not in the raw text.

Usage (from ``tools/Eval``)::

    python3 -m geef_eval.research_verify --results run-holdout.jsonl [--results run-dev.jsonl ...] --out verify.json
    python3 -m geef_eval.research_verify --audit <audit directory or file> [--audit ...] --out verify.json

``--results`` reads the JSON lines of ``ResearchEval run`` (snake_case). ``--audit`` reads the audit files the research service of
Atelier writes for the acceptance (``Research:AuditDirectory``, contract stage 4, 4.8 and 9.1): one JSON object per research with
``orderId``, ``pages[{id, topic, url, title, rawText, rawSha256}]`` and ``entries[{id, quote, url, rawSha256, start, length, …}]``
(camelCase); a directory stands for all its ``*.json`` files. Both kinds are checked by the same rules. The report names every file
it read by the SHA-256 of its bytes; ``ResearchEval evaluate`` accepts it only for exactly the files it evaluates. Exit codes: 0 every entry found, 1 at least one violation, 2 wrong usage or unreadable file, 4 no entry at all (nothing
was verified, never a pass).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

TOOL = "research_verify"
VERSION = 1


def collapse(text: str) -> str:
    """Every run of white space as one space, ends trimmed; case and all other characters unchanged."""
    return " ".join(text.split())


def sha256_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def utf16_slice(text: str, start: int, length: int) -> str | None:
    """The part of ``text`` from UTF-16 unit ``start`` with ``length`` units, or None when it lies outside or splits a character."""
    units = text.encode("utf-16-le")
    if start < 0 or length <= 0 or (start + length) * 2 > len(units):
        return None
    try:
        return units[start * 2:(start + length) * 2].decode("utf-16-le")
    except UnicodeDecodeError:
        return None


def check_entry(entry: dict, pages: list[dict]) -> str | None:
    """The reason why ``entry`` is not word for word in its page, or None."""
    quote = collapse(str(entry.get("quote") or ""))
    if not quote:
        return "empty_quote"
    wanted = str(entry.get("raw_sha256") or "").lower()
    matching = [p for p in pages if sha256_text(str(p.get("raw_text") or "")) == wanted]
    if not matching:
        return "page_missing"
    same_address = [p for p in matching if p.get("url") == entry.get("url")]
    if not same_address:
        return "url_mismatch"
    raw = str(same_address[0].get("raw_text") or "")
    start, length = entry.get("start"), entry.get("length")
    if not isinstance(start, int) or not isinstance(length, int):
        return "range_outside"
    stored = utf16_slice(raw, start, length)
    if stored is None:
        return "range_outside"
    if collapse(stored) != quote:
        return "range_mismatch"
    if quote not in collapse(raw):
        return "not_in_page"
    return None


AUDIT_PAGE_KEYS = {"raw_text": "rawText", "raw_sha256": "rawSha256"}
AUDIT_ENTRY_KEYS = {"raw_sha256": "rawSha256"}


def _renamed(item: object, keys: dict[str, str]) -> object:
    """``item`` with the camelCase keys of an audit file under the names of a result line; other keys stay."""
    if not isinstance(item, dict):
        return item
    renamed = dict(item)
    for line_key, audit_key in keys.items():
        if line_key not in renamed and audit_key in renamed:
            renamed[line_key] = renamed[audit_key]
    return renamed


def audit_as_line(audit: dict) -> dict:
    """An audit file of the research service in the shape of a result line (``id``, ``pages``, ``entries``)."""
    return {
        "id": audit.get("orderId"),
        "status": audit.get("status"),
        "pages": [_renamed(p, AUDIT_PAGE_KEYS) for p in audit.get("pages") or []],
        "entries": [_renamed(e, AUDIT_ENTRY_KEYS) for e in audit.get("entries") or []],
    }


def _result_lines(path: Path, data: bytes) -> list[tuple[int, dict]]:
    lines = []
    for number, text in enumerate(data.decode("utf-8").splitlines(), start=1):
        if not text.strip():
            continue
        try:
            line = json.loads(text)
        except json.JSONDecodeError as error:
            raise ValueError(f"{path}:{number} is no JSON: {error}") from error
        if not isinstance(line, dict):
            raise ValueError(f"{path}:{number} is no JSON object")
        lines.append((number, line))
    return lines


def _audit_lines(path: Path, data: bytes) -> list[tuple[int, dict]]:
    try:
        audit = json.loads(data.decode("utf-8"))
    except json.JSONDecodeError as error:
        raise ValueError(f"{path} is no JSON: {error}") from error
    if not isinstance(audit, dict) or not isinstance(audit.get("pages"), list) or not isinstance(audit.get("entries"), list):
        raise ValueError(f"{path} is no audit file of the research (object with pages and entries)")
    return [(1, audit_as_line(audit))]


def audit_files(paths: list[Path]) -> list[Path]:
    """The audit files named by ``paths``: a file stands for itself, a directory for its ``*.json`` files in name order."""
    found: list[Path] = []
    for path in paths:
        found.extend(sorted(path.glob("*.json")) if path.is_dir() else [path])
    return found


def verify_files(paths: list[Path], audits: list[Path] | None = None) -> dict:
    """
    Checks every entry of every line of the result files ``paths`` and of every audit file ``audits``; raises ValueError for a
    line or file that is not what it should be.
    """
    files, violations, total = [], [], 0
    sources = [(p, "results") for p in paths] + [(p, "audit") for p in audits or []]
    for path, kind in sources:
        data = path.read_bytes()
        lines = entries = 0
        for number, line in (_result_lines(path, data) if kind == "results" else _audit_lines(path, data)):
            lines += 1
            pages = [p for p in line.get("pages") or [] if isinstance(p, dict)]
            for entry in line.get("entries") or []:
                entries += 1
                reason = check_entry(entry if isinstance(entry, dict) else {}, pages)
                if reason is not None:
                    violations.append({"file": str(path), "line": number, "order": line.get("id"),
                                       "entry": entry.get("id") if isinstance(entry, dict) else None, "reason": reason})
        total += entries
        record = {"path": str(path), "sha256": hashlib.sha256(data).hexdigest(), "lines": lines, "entries": entries}
        if kind == "audit":
            record["kind"] = "audit"
        files.append(record)
    return {"tool": TOOL, "version": VERSION, "files": files, "entries": total, "violations": violations}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="research_verify", description=__doc__.split("\n\n")[0])
    parser.add_argument("--results", action="append", default=[], type=Path, help="result file of ResearchEval run (repeatable)")
    parser.add_argument("--audit", action="append", default=[], type=Path,
                        help="audit file or directory of audit files written by Atelier (Research:AuditDirectory, repeatable)")
    parser.add_argument("--out", required=True, type=Path, help="report for ResearchEval evaluate --verification")
    args = parser.parse_args(argv)

    if not args.results and not args.audit:
        print("name at least one --results or --audit", file=sys.stderr)
        return 2
    missing = [str(p) for p in args.results if not p.is_file()] + [str(p) for p in args.audit if not p.exists()]
    if missing:
        print(f"missing files: {', '.join(missing)}", file=sys.stderr)
        return 2
    audits = audit_files(args.audit)
    if args.audit and not audits:
        print("the audit directories hold no *.json file: nothing was verified (not evaluable)", file=sys.stderr)
        return 4
    try:
        report = verify_files(args.results, audits)
    except (ValueError, UnicodeDecodeError) as error:
        print(str(error), file=sys.stderr)
        return 2

    args.out.write_text(json.dumps(report, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    for violation in report["violations"][:20]:
        print(f"VIOLATION {violation['order']}/{violation['entry']} ({violation['file']}:{violation['line']}): {violation['reason']}")
    print(f"{report['entries']} entries in {len(report['files'])} files, {len(report['violations'])} violations")
    if report["entries"] == 0:
        print("no entry at all: nothing was verified (not evaluable)")
        return 4
    return 1 if report["violations"] else 0


if __name__ == "__main__":
    sys.exit(main())
