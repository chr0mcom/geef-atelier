import hashlib
import json

import pytest

from geef_eval import research_verify as rv

RAW = "Seite über den Mindestlohn.\nDer gesetzliche Mindestlohn steigt\n  am 1. Januar auf 13,90 Euro je Stunde. Mehr nicht."
QUOTE = "Der gesetzliche Mindestlohn steigt am 1. Januar auf 13,90 Euro je Stunde."
URL = "https://pages.example/mindestlohn"


def _utf16_len(text):
    return len(text.encode("utf-16-le")) // 2


def _entry(raw=RAW, quote=QUOTE, url=URL, **overrides):
    start_text = raw[:raw.index("Der gesetzliche")] if "Der gesetzliche" in raw else ""
    stored = "Der gesetzliche Mindestlohn steigt\n  am 1. Januar auf 13,90 Euro je Stunde."
    entry = {"id": "W1.1", "topic": 1, "quote": quote, "url": url, "title": "Mindestlohn", "retrieved_at": "2026-11-28T00:00:00+00:00",
             "match": "Normalized", "raw_sha256": hashlib.sha256(raw.encode("utf-8")).hexdigest().upper(),
             "start": _utf16_len(start_text), "length": _utf16_len(stored)}
    entry.update(overrides)
    return entry


def _line(entries, raw=RAW, order="h-01", pages=None):
    page = {"id": "P1", "topic": 1, "url": URL, "title": "Mindestlohn", "raw_text": raw,
            "raw_sha256": hashlib.sha256(raw.encode("utf-8")).hexdigest()}
    return {"id": order, "set": "holdout", "status": "Found", "pages": [page] if pages is None else pages, "entries": entries}


def _write(tmp_path, lines, name="run.jsonl"):
    path = tmp_path / name
    path.write_text("".join(json.dumps(line, ensure_ascii=False) + "\n" for line in lines), encoding="utf-8")
    return path


def _run(tmp_path, *paths):
    out = tmp_path / "verify.json"
    argv = [arg for p in paths for arg in ("--results", str(p))] + ["--out", str(out)]
    code = rv.main(argv)
    return code, json.loads(out.read_text(encoding="utf-8")) if out.exists() else None


def test_an_entry_that_stands_in_its_raw_text_passes_and_the_report_names_the_file(tmp_path):
    path = _write(tmp_path, [_line([_entry()]), _line([], order="h-02")])

    code, report = _run(tmp_path, path)

    assert code == 0
    assert report["tool"] == rv.TOOL and report["violations"] == [] and report["entries"] == 1
    assert report["files"] == [{"path": str(path), "sha256": hashlib.sha256(path.read_bytes()).hexdigest(), "lines": 2, "entries": 1}]


@pytest.mark.parametrize("change, reason", [
    ({"quote": QUOTE.replace("13,90", "14,90")}, "range_mismatch"),
    ({"quote": QUOTE.replace("Der gesetzliche", "der gesetzliche")}, "range_mismatch"),
    ({"start": 1}, "range_mismatch"),
    ({"start": 10_000}, "range_outside"),
    ({"raw_sha256": hashlib.sha256(b"other").hexdigest()}, "page_missing"),
    ({"url": "https://other.example/"}, "url_mismatch"),
    ({"quote": "   "}, "empty_quote"),
])
def test_a_manipulated_entry_turns_the_check_red(tmp_path, change, reason):
    path = _write(tmp_path, [_line([_entry(), _entry(id="W1.2", **change)])])

    code, report = _run(tmp_path, path)

    assert code == 1
    assert report["violations"] == [{"file": str(path), "line": 1, "order": "h-01", "entry": "W1.2", "reason": reason}]


def test_the_stored_range_must_hold_the_quote_itself(tmp_path):
    raw = "Erster Satz der Seite hier. Zweiter Satz."
    entry = _entry(raw=raw, quote="Erster Satz der Seite hier.", start=0, length=_utf16_len("Erster Satz der Seite hier."))
    assert rv.check_entry(entry, _line([entry], raw=raw)["pages"]) is None

    lying = dict(entry, quote="Ganz anderer Satz steht da.")
    assert rv.check_entry(lying, _line([lying], raw=raw)["pages"]) == "range_mismatch"


def test_missing_raw_texts_are_a_violation_not_a_pass(tmp_path):
    path = _write(tmp_path, [_line([_entry()], pages=[])])

    code, report = _run(tmp_path, path)

    assert code == 1
    assert report["violations"][0]["reason"] == "page_missing"


def test_offsets_are_utf16_units_as_in_dotnet(tmp_path):
    raw = "Emoji 😀 vorweg. " + QUOTE
    start = _utf16_len("Emoji 😀 vorweg. ")
    entry = _entry(raw=raw, start=start, length=_utf16_len(QUOTE))
    assert start == len("Emoji 😀 vorweg. ") + 1

    code, report = _run(tmp_path, _write(tmp_path, [_line([entry], raw=raw)]))

    assert code == 0, report["violations"]
    assert rv.utf16_slice("a😀b", 1, 1) is None


def test_white_space_may_differ_but_nothing_else(tmp_path):
    assert rv.collapse(" a\n\t b  c ") == "a b c"
    assert rv.collapse("13,90") != rv.collapse("13.90")


def test_without_entries_nothing_was_verified(tmp_path):
    code, report = _run(tmp_path, _write(tmp_path, [_line([])]))

    assert code == 4
    assert report["entries"] == 0


def test_several_files_are_checked_together_and_bad_input_is_refused(tmp_path):
    first = _write(tmp_path, [_line([_entry()])], "dev.jsonl")
    second = _write(tmp_path, [_line([_entry(id="W1.1", quote="Erfunden und nicht auf der Seite.")], order="h-03")], "holdout.jsonl")

    code, report = _run(tmp_path, first, second)

    assert code == 1
    assert [f["entries"] for f in report["files"]] == [1, 1]
    assert [(v["order"], v["file"]) for v in report["violations"]] == [("h-03", str(second))]

    broken = tmp_path / "broken.jsonl"
    broken.write_text("{not json\n", encoding="utf-8")
    assert rv.main(["--results", str(broken), "--out", str(tmp_path / "x.json")]) == 2
    assert rv.main(["--results", str(tmp_path / "missing.jsonl"), "--out", str(tmp_path / "x.json")]) == 2
