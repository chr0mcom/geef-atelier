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

The report names every file it read by the SHA-256 of its bytes; ``ResearchEval evaluate`` accepts it only for exactly the files it
evaluates. Exit codes: 0 every entry found, 1 at least one violation, 2 wrong usage or unreadable file, 4 no entry at all (nothing
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


def verify_files(paths: list[Path]) -> dict:
    """Checks every entry of every line of ``paths``; raises ValueError for a line that is no JSON object."""
    files, violations, total = [], [], 0
    for path in paths:
        data = path.read_bytes()
        lines = entries = 0
        for number, text in enumerate(data.decode("utf-8").splitlines(), start=1):
            if not text.strip():
                continue
            try:
                line = json.loads(text)
            except json.JSONDecodeError as error:
                raise ValueError(f"{path}:{number} is no JSON: {error}") from error
            if not isinstance(line, dict):
                raise ValueError(f"{path}:{number} is no JSON object")
            lines += 1
            pages = [p for p in line.get("pages") or [] if isinstance(p, dict)]
            for entry in line.get("entries") or []:
                entries += 1
                reason = check_entry(entry if isinstance(entry, dict) else {}, pages)
                if reason is not None:
                    violations.append({"file": str(path), "line": number, "order": line.get("id"),
                                       "entry": entry.get("id") if isinstance(entry, dict) else None, "reason": reason})
        total += entries
        files.append({"path": str(path), "sha256": hashlib.sha256(data).hexdigest(), "lines": lines, "entries": entries})
    return {"tool": TOOL, "version": VERSION, "files": files, "entries": total, "violations": violations}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="research_verify", description=__doc__.split("\n\n")[0])
    parser.add_argument("--results", action="append", required=True, type=Path, help="result file of ResearchEval run (repeatable)")
    parser.add_argument("--out", required=True, type=Path, help="report for ResearchEval evaluate --verification")
    args = parser.parse_args(argv)

    missing = [str(p) for p in args.results if not p.is_file()]
    if missing:
        print(f"missing result files: {', '.join(missing)}", file=sys.stderr)
        return 2
    try:
        report = verify_files(args.results)
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
