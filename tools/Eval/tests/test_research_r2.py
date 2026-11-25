import json
import re
from pathlib import Path

import pytest

from geef_eval import research_r2 as r2

WRITER_NOTES = Path(__file__).resolve().parents[3] / "src" / "Geef.Atelier.Core" / "Domain" / "Writing" / "WriterNotes.cs"
ENTRY = {"quote": "Ab Januar gilt laut Verband Waldhof ein Beitrag von 1.250,50 Euro.", "url": "https://pages.example/beitrag",
         "title": "Beitragsordnung Musterstadt"}


def result(order="o-1", passages=(), text="Hallo zusammen,\nder Beitrag ist neu.", status="found", entries=(ENTRY,), fact="checked"):
    looked = None if status is None else {"status": status, "note": None, "withheld": 0, "entries": list(entries)}
    return {"orderId": order, "state": "delivered", "text": text, "unbackedPassages": list(passages), "lookedUp": looked,
            "factCheck": {"state": fact, "filesCompared": False}}


def five(**first):
    return [result(**first)] + [result(order=f"o-{n}") for n in range(2, 6)]


def flagged(quote, kind="not_backed", blocking=True):
    return {"quote": quote, "kind": kind, "blocking": blocking}


def test_five_clean_orders_pass_the_checked_parts_and_b_and_d_stay_manual():
    report = r2.evaluate(five())

    assert report["automated_verdict"] == "passed" and report["problems"] == []
    assert all(report["parts"][key]["passed"] for key in ("a", "c", "e"))
    assert sorted(report["manual"]) == ["b", "d"] and "b" not in report["parts"] and "d" not in report["parts"]


ENGLISH_TEXT = "Hello all,\nthe fee is new and it will be the same for you."


@pytest.mark.parametrize("quote, covered", [
    ("der Satz liegt bei 1250,50 Euro", True),
    ("der Satz liegt bei 1,250.50 Dollar", True),
    ("der Satz liegt bei 250 Münzen", False),
    ("der Satz liegt bei 50 Münzen", False),
    ("wir treffen uns bald wieder", False),
])
def test_a_flagged_passage_counts_in_code_when_an_entry_holds_its_number(quote, covered):
    report = r2.evaluate(five(passages=[flagged(quote)]))

    assert report["parts"]["c"]["count"] == int(covered)
    assert report["parts"]["c"]["cases"] == []


@pytest.mark.parametrize("quote, covered", [
    ("the fee is 1250,50 a year", True),
    ("so says the Verband about it", True),
    ("here in Musterstadt it is so", True),
    ("Verband and so on", False),
    ("this holds in waldhof and musterstadt", False),
    ("this holds in Waldhofen and elsewhere", False),
    ("we meet again soon", False),
])
def test_in_an_english_text_a_name_of_an_entry_counts_in_code(quote, covered):
    report = r2.evaluate(five(passages=[flagged(quote)], text=ENGLISH_TEXT))

    assert report["orders"][0]["language"] == "en"
    assert report["parts"]["c"]["count"] == int(covered)
    assert report["parts"]["c"]["cases"] == []


@pytest.mark.parametrize("quote, names", [
    ("so sagt es der Verband dazu", ["Verband"]),
    ("bei uns in Musterstadt ist das so", ["Musterstadt"]),
])
def test_in_a_german_text_a_shared_capitalised_word_is_a_case_for_a_person(quote, names):
    report = r2.evaluate(five(passages=[flagged(quote)]))

    assert report["orders"][0]["language"] == "de"
    assert report["automated_verdict"] == "not_evaluable"
    assert report["parts"]["c"]["count"] == 0 and report["parts"]["c"]["open_cases"] == 1
    assert report["parts"]["c"]["cases"] == [{"orderId": "o-1", "case": "n1", "quote": quote, "names": names, "decision": None}]
    assert any("nicht von Hand entschieden" in p for p in report["problems"])


def confirm(*entries):
    return {"confirmations": [dict(zip(("order", "case", "quote", "decision"), e)) for e in entries]}


def test_decided_cases_count_only_when_covered():
    quote = "so sagt es der Verband dazu"
    second = "bei uns in Musterstadt ist das so"
    passages = [flagged(quote), flagged("ohne Treffer hier"), flagged(second)]

    report = r2.evaluate(five(passages=passages), confirm(("o-1", "n1", quote, "covered"), ("o-1", "n3", second, "not_covered")))
    assert report["automated_verdict"] == "passed" and report["parts"]["c"]["count"] == 1

    report = r2.evaluate(five(passages=passages), confirm(("o-1", "n1", quote, "covered"), ("o-1", "n3", second, "covered")))
    assert report["automated_verdict"] == "failed" and report["parts"]["c"]["count"] == 2


def test_a_number_settles_a_german_passage_in_code_without_a_case():
    report = r2.evaluate(five(passages=[flagged("laut Verband sind es 1.250,50 Euro")]))

    assert report["parts"]["c"]["count"] == 1 and report["parts"]["c"]["cases"] == []


@pytest.mark.parametrize("confirmations, problem", [
    ({"x": []}, "ohne Liste"),
    (confirm(("o-1", "n9", "so sagt es der Verband dazu", "covered")), "unbekanntem Fall"),
    (confirm(("o-1", "n1", "so sagt es der Verband", "covered")), "Wortlaut"),
    (confirm(("o-1", "n1", "so sagt es der Verband dazu", "maybe")), "gültige Entscheidung"),
    (confirm(("o-1", "n1", "so sagt es der Verband dazu", "covered"), ("o-1", "n1", "so sagt es der Verband dazu", "covered")), "doppelt"),
])
def test_a_defect_of_the_confirmation_file_is_not_evaluable(confirmations, problem):
    report = r2.evaluate(five(passages=[flagged("so sagt es der Verband dazu")]), confirmations)

    assert report["automated_verdict"] == "not_evaluable"
    assert any(problem in p for p in report["problems"]), report["problems"]


def test_the_language_of_a_text_is_german_unless_english_function_words_win():
    assert r2.language(ENGLISH_TEXT) == "en"
    assert r2.language("Hallo zusammen,\nder Beitrag ist neu.") == "de"
    assert r2.language("") == "de"
    assert r2.language("Termin: Montag") == "de"


def test_numbers_with_a_space_or_an_apostrophe_between_groups_count_joined_and_in_parts():
    assert {"1500", "1", "500"} <= r2.numbers("es sind 1 500 Euro")
    assert "4850" in r2.numbers("es sind 4'850 Franken") and "4850" in r2.numbers("es sind 4\u2019850 Franken")
    assert "2020500" not in r2.numbers("seit 2020 500 neue")
    assert r2.numbers("1.250,50 und 1,250.50") == {"125050"}


@pytest.mark.parametrize("statuses, evaluable", [
    (["found", "failed", "failed", "daily_limit", "all_withheld"], False),
    (["found", "found", "found", "failed", "nothing_found"], False),
    (["found", "found", "found", "found", "failed"], True),
])
def test_c_needs_four_orders_with_found_research_and_an_entry(statuses, evaluable):
    results = [result(order=f"o-{n}", status=status, entries=(ENTRY,) if status == "found" else ())
               for n, status in enumerate(statuses, start=1)]

    report = r2.evaluate(results)

    assert (report["automated_verdict"] != "not_evaluable") == evaluable, report["problems"]
    assert evaluable or any("mindestens 4" in p and "found" in p for p in report["problems"])


def test_found_without_an_entry_does_not_count_as_found():
    results = [result(order=f"o-{n}") for n in range(1, 4)] + [result(order=f"o-{n}", entries=()) for n in (4, 5)]

    assert r2.evaluate(results)["automated_verdict"] == "not_evaluable"


def test_where_the_passage_stands_decides_whether_its_first_word_starts_a_sentence():
    assert r2.names("Waldhof sagt das", "Das meint der Verein Waldhof sagt das auch.") == {"Waldhof"}
    assert r2.names("Waldhof sagt das", "Das ist neu. Waldhof sagt das auch.") == set()
    assert r2.names("Waldhof sagt das", "Hallo,\n- Waldhof sagt das auch.") == set()
    assert r2.names("Waldhof sagt das", "Nichts davon steht im Text.") == set()
    assert r2.names("bitte an Dr. Waldhof wenden") == {"Waldhof"}
    assert r2.names("seit dem 1. Januar gilt das") == {"Januar"}
    assert r2.names("so ist es. Dann kam er") == set()
    assert r2.names("wir bei Ab und zu") == set()


def test_one_covered_passage_is_allowed_two_fail_and_other_kinds_never_count():
    one = flagged("der Beitrag liegt bei 1.250,50 Euro", blocking=False)
    others = [flagged(one["quote"], "against_instruction"), flagged(one["quote"], "replaced_in_files")]
    assert r2.evaluate(five(passages=[one, *others]))["automated_verdict"] == "passed"

    results = five(passages=[one])
    results[1]["unbackedPassages"] = [one]
    report = r2.evaluate(results)
    assert report["automated_verdict"] == "failed" and report["parts"]["c"]["count"] == 2


def test_entries_of_another_order_cover_nothing():
    other = {"quote": "Der Zuschuss beträgt genau 777 Euro im ganzen Jahr.", "url": "https://other.example/", "title": "Zuschuss"}
    results = five(passages=[flagged("es sind 777 Münzen"), flagged("es sind 777 Taler")])
    results[1] = result(order="o-2", entries=[other])

    assert r2.evaluate(results)["parts"]["c"]["count"] == 0


@pytest.mark.parametrize("change", [{"status": None}, {"status": "not_needed", "entries": []}, {"status": "files_attached", "entries": []}])
def test_every_order_needs_research(change):
    report = r2.evaluate(five(**change))

    assert report["automated_verdict"] == "failed"
    assert report["parts"]["a"] == {"passed": False, "without_research": ["o-1"]}


@pytest.mark.parametrize("marker", ["<!-- atelier-notes {} -->", "<!--atelier_notes", "<!--  Atelier Notes", "<!-- atelier‑notes"])
def test_a_marker_of_the_writer_notes_in_a_text_fails(marker):
    report = r2.evaluate(five(text=f"Hallo.\n{marker}"))

    assert report["automated_verdict"] == "failed"
    assert report["parts"]["e"] == {"passed": False, "count": 1, "orders": [{"orderId": "o-1", "count": 1}]}


def test_the_marker_is_the_constant_of_the_writer_notes_format():
    declared = re.search(r'const string StartMarker = "([^"]+)";', WRITER_NOTES.read_text(encoding="utf-8")).group(1)

    assert r2.NOTES_MARKER == declared
    assert r2.NOTES_MARKER_RE.search(declared)


@pytest.mark.parametrize("results, problem", [
    ([], "mindestens 4"),
    ([result(order=f"o-{n}") for n in range(1, 4)] + [result(order="o-4", text=None, fact="not_checked")], "nur 3 Aufträge"),
    ([result(order="o-1")] * 4, "doppelt"),
    (five(fact="not_checked"), "Faktenprüfung lief nicht"),
    (five(fact="not_completed"), "Faktenprüfung lief nicht"),
    (five(fact="version_not_checked"), "Faktenprüfung lief nicht"),
    ([result(order=f"o-{n}", status="failed", entries=[]) for n in range(1, 6)], "zu wenig Daten"),
])
def test_without_data_the_checked_parts_are_not_evaluable(results, problem):
    report = r2.evaluate(results)

    assert report["automated_verdict"] == "not_evaluable"
    assert any(problem in p for p in report["problems"]), report["problems"]


def test_a_missing_list_of_flagged_passages_is_not_evaluable():
    results = five()
    results[0]["unbackedPassages"] = None

    assert r2.evaluate(results)["automated_verdict"] == "not_evaluable"


def test_open_points_count_as_a_completed_fact_check_and_four_texts_suffice():
    results = [result(order=f"o-{n}", fact="open_points") for n in range(1, 5)] + [result(order="o-5", text=None, fact="not_checked")]

    assert r2.evaluate(results)["automated_verdict"] == "passed"


def run_main(tmp_path, *paths):
    out = []
    argv = [arg for p in paths for arg in ("--results", str(p))] + ["--out", str(tmp_path / "out" / "r2.json"), "--allow-in-repo"]
    return r2.main(argv, echo=out.append), out


def test_confirmations_are_read_from_a_file(tmp_path):
    quote = "so sagt es der Verband dazu"
    path = tmp_path / "a.jsonl"
    path.write_text("".join(json.dumps(a, ensure_ascii=False) + "\n" for a in five(passages=[flagged(quote)])), encoding="utf-8")
    assert run_main(tmp_path, path)[0] == 4

    confirmations = tmp_path / "c.json"
    confirmations.write_text(json.dumps(confirm(("o-1", "n1", quote, "not_covered")), ensure_ascii=False), encoding="utf-8")
    out = []
    argv = ["--results", str(path), "--confirmations", str(confirmations), "--out", str(tmp_path / "out" / "r2.json"), "--allow-in-repo"]
    assert r2.main(argv, echo=out.append) == 0, out
    assert any("Fall o-1/n1 (not_covered)" in line for line in out)


def test_answers_are_read_from_a_directory_and_from_jsonl(tmp_path):
    folder = tmp_path / "answers"
    folder.mkdir()
    for answer in five()[:2]:
        (folder / f"{answer['orderId']}.json").write_text(json.dumps(answer, ensure_ascii=False, indent=2), encoding="utf-8")
    lines = tmp_path / "rest.jsonl"
    lines.write_text("".join(json.dumps(a, ensure_ascii=False) + "\n" for a in five()[2:]), encoding="utf-8")

    code, out = run_main(tmp_path, folder, lines)

    assert code == 0, out
    report = json.loads((tmp_path / "out" / "r2.json").read_text(encoding="utf-8"))
    assert report["orders_with_text"] == 5
    assert "(b) und (d) bleiben Handauswertung" in out[-1]
    assert any(line.startswith("R2 (b) manual") for line in out) and any(line.startswith("R2 (d) manual") for line in out)


def test_exit_codes_and_unreadable_input(tmp_path):
    path = tmp_path / "a.jsonl"
    path.write_text("".join(json.dumps(a) + "\n" for a in five(status=None)), encoding="utf-8")
    assert run_main(tmp_path, path)[0] == 1

    path.write_text(json.dumps(result()) + "\n", encoding="utf-8")
    assert run_main(tmp_path, path)[0] == 4

    for content in ("{not json\n", json.dumps({"text": "x"}) + "\n", "[1]\n"):
        path.write_text(content, encoding="utf-8")
        assert run_main(tmp_path, path)[0] == 2
    assert run_main(tmp_path, tmp_path / "missing.json")[0] == 2


def test_a_report_inside_a_git_tree_is_refused(tmp_path):
    inside = Path(__file__).resolve().parent / "r2-out.json"

    assert r2.main(["--results", str(tmp_path), "--out", str(inside)], echo=lambda _: None) == 2
    assert not inside.exists()
