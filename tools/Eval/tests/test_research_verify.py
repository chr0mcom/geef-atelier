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


def _audit(entries, raw=RAW, order="0f1e2d3c4b5a69788796a5b4c3d2e1f0"):
    """An audit file exactly as the research service writes it (camelCase, one object per research, indented)."""
    sha = hashlib.sha256(raw.encode("utf-8")).hexdigest()
    return {
        "orderId": "0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0", "at": "2026-11-28T09:15:00+00:00", "status": 1,
        "topicsSent": ["Mindestlohn 2027"],
        "pages": [{"id": "P1", "topic": 1, "url": URL, "title": "Mindestlohn", "rawText": raw, "rawSha256": sha}],
        "entries": [{"id": e["id"], "topic": 1, "quote": e["quote"], "url": e["url"], "title": "Mindestlohn",
                     "retrievedAt": "2026-11-28T09:14:58+00:00", "match": 1, "rawSha256": e["raw_sha256"],
                     "start": e["start"], "length": e["length"]} for e in entries],
        "dropped": {"NotInPage": 1},
    }


def _write_audit(folder, audit, name="0f1e2d3c4b5a69788796a5b4c3d2e1f0-20261128091500000.json"):
    folder.mkdir(exist_ok=True)
    path = folder / name
    path.write_text(json.dumps(audit, ensure_ascii=False, indent=2), encoding="utf-8")
    return path


def test_an_audit_directory_of_atelier_is_checked_by_the_same_rules(tmp_path):
    folder = tmp_path / "audit"
    good = _write_audit(folder, _audit([_entry()]))
    _write_audit(folder, _audit([_entry(id="W1.2", quote=QUOTE.replace("13,90", "14,90"))]), "b-20261128091600000.json")
    (folder / "notes.txt").write_text("not an audit", encoding="utf-8")
    out = tmp_path / "verify.json"

    code = rv.main(["--audit", str(folder), "--out", str(out)])

    report = json.loads(out.read_text(encoding="utf-8"))
    assert code == 1
    assert report["entries"] == 2
    assert [f["kind"] for f in report["files"]] == ["audit", "audit"]
    assert report["files"][0] == {"path": str(good), "sha256": hashlib.sha256(good.read_bytes()).hexdigest(), "lines": 1,
                                  "entries": 1, "kind": "audit"}
    assert report["violations"] == [{"file": str(folder / "b-20261128091600000.json"), "line": 1,
                                     "order": "0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0", "entry": "W1.2", "reason": "range_mismatch"}]


def test_a_clean_audit_file_passes_and_a_tampered_raw_text_does_not(tmp_path):
    audit = _audit([_entry()])
    clean = _write_audit(tmp_path / "a", audit)
    assert rv.main(["--audit", str(clean), "--out", str(tmp_path / "v1.json")]) == 0

    audit["pages"][0]["rawText"] = RAW.replace("13,90", "13,99")
    audit["pages"][0]["rawSha256"] = hashlib.sha256(audit["pages"][0]["rawText"].encode("utf-8")).hexdigest()
    tampered = _write_audit(tmp_path / "b", audit)
    assert rv.main(["--audit", str(tampered), "--out", str(tmp_path / "v2.json")]) == 1
    assert json.loads((tmp_path / "v2.json").read_text(encoding="utf-8"))["violations"][0]["reason"] == "page_missing"


def test_audit_and_result_files_are_reported_together(tmp_path):
    results = _write(tmp_path, [_line([_entry()])])
    audit = _write_audit(tmp_path / "audit", _audit([_entry()]))
    out = tmp_path / "verify.json"

    assert rv.main(["--results", str(results), "--audit", str(audit), "--out", str(out)]) == 0
    report = json.loads(out.read_text(encoding="utf-8"))
    assert [f.get("kind", "results") for f in report["files"]] == ["results", "audit"]
    assert report["entries"] == 2


def test_audit_input_that_verifies_nothing_is_never_a_pass(tmp_path):
    empty = tmp_path / "empty"
    empty.mkdir()
    assert rv.main(["--audit", str(empty), "--out", str(tmp_path / "v.json")]) == 4
    assert rv.main(["--out", str(tmp_path / "v.json")]) == 2
    assert rv.main(["--audit", str(tmp_path / "missing"), "--out", str(tmp_path / "v.json")]) == 2

    no_entries = _write_audit(tmp_path / "n", _audit([]))
    assert rv.main(["--audit", str(no_entries), "--out", str(tmp_path / "v.json")]) == 4

    wrong = tmp_path / "w"
    wrong.mkdir()
    (wrong / "x.json").write_text(json.dumps({"orderId": "x"}), encoding="utf-8")
    assert rv.main(["--audit", str(wrong), "--out", str(tmp_path / "v.json")]) == 2


@pytest.mark.parametrize("raw, quote", [
    ("Das ist unzulaessig ohne eine Einwilligung der Eltern.", "zulaessig ohne eine Einwilligung der Eltern."),
    ("Die Eltern geben eine Einwilligungserklaerung ab.", "Die Eltern geben eine Einwilligung"),
    ("Der Beitrag steigt auf 213 Euro im Jahr.", "13 Euro im Jahr."),
    ("Der Beitrag betraegt im Jahr 2027 genau.", "Der Beitrag betraegt im Jahr 202"),
    ("Der Satz liegt bei 13,90 Euro je Stunde.", "90 Euro je Stunde."),
    ("Der Satz liegt bei 13.90 Euro je Stunde.", ".90 Euro je Stunde."),
    ("Der Satz liegt bei 13,90 Euro je Stunde.", "Der Satz liegt bei 13"),
    ("Der Satz liegt bei 13,90 Euro je Stunde.", "Der Satz liegt bei 13,"),
    ("Die Arbeits\u00adzeit ist hier geregelt.", "zeit ist hier geregelt."),
    ("Die Arbeits\u00adzeit ist hier geregelt.", "Die Arbeits"),
    ("Das Cafe\u0301s oeffnen am Montag wieder.", "Das Cafe"),
    ("Die Zahl 5x gilt hier nicht weiter.", "x gilt hier nicht weiter."),
    ("The fee for the permit is 1 500 euros in total.", "The fee for the permit is 1"),
    ("The fee for the permit is 1 500 euros in total.", "500 euros in total."),
    ("The fee for the permit is 1\u202f500 euros in total.", "The fee for the permit is 1"),
    ("Die Gebuehr betraegt 4'850 Franken im Jahr.", "Die Gebuehr betraegt 4"),
    ("Die Gebuehr betraegt 4\u2019850 Franken im Jahr.", "850 Franken im Jahr."),
    ("Der Preis betraegt 1'250'000 Franken.", "Der Preis betraegt 1'250"),
])
def test_a_passage_cut_inside_a_word_or_number_is_a_violation(raw, quote):
    entry = _entry(raw=raw, quote=quote, start=_utf16_len(raw[:raw.index(quote)]), length=_utf16_len(quote))

    assert rv.check_entry(entry, _line([entry], raw=raw)["pages"]) == "word_cut"


@pytest.mark.parametrize("raw, quote", [
    ("Das ist unzulaessig ohne eine Einwilligung der Eltern.", "unzulaessig ohne eine Einwilligung"),
    ("Er sagt: „Der Beitrag steigt“, mehr nicht.", "Der Beitrag steigt"),
    ("Es sind 13, 90 davon sind neu und gut.", "90 davon sind neu und gut."),
    ("Der Satz gilt seit 2027. Mehr steht dort nicht.", "Der Satz gilt seit 2027"),
    ("Der Satz gilt seit 2027.5 Seiten folgen noch.", "Seiten folgen noch."),
    ("Die Ost-West-Achse ist laengst fertig gebaut.", "West-Achse ist laengst fertig gebaut."),
    ("😀 Der Beitrag steigt auf 13 Euro😀", "Der Beitrag steigt auf 13 Euro"),
    ("Since 2020 500 new permits were granted.", "500 new permits were granted."),
    ("The city granted 12 permits 345 days later.", "The city granted 12 permits"),
    ("The fee for the permit is 1 500 euros in total.", "The fee for the permit is 1 500 euros"),
])
def test_a_passage_with_clean_edges_passes(raw, quote):
    entry = _entry(raw=raw, quote=quote, start=_utf16_len(raw[:raw.index(quote)]), length=_utf16_len(quote))

    assert rv.check_entry(entry, _line([entry], raw=raw)["pages"]) is None
