import base64
import json
import os
from pathlib import Path

import pytest

from geef_eval import revise_check as rc
from geef_eval.mcp import McpError

O1 = "11111111-1111-4111-8111-111111111111"
O2 = "22222222-2222-4222-8222-222222222222"
O3 = "33333333-3333-4333-8333-333333333333"
O4 = "44444444-4444-4444-8444-444444444444"


def para(i, n=12, word="Wort"):
    return f"Absatz {i} " + " ".join(f"{word}{i}x{k}" for k in range(n))


def text_of(paragraphs):
    return "\n\n".join(paragraphs)


BASE = [para(i) for i in range(1, 9)]


def result(order, version, text, *, unbacked=None, points=None, placeholders=None, fact="open_points", latest=None,
           earlier=None, assumptions=None, **extra):
    value = {"orderId": order, "state": "delivered_with_open_points", "text": text, "wordCount": rc.count_words(text),
             "version": version, "placeholders": rc.find_placeholders(text) if placeholders is None else placeholders,
             "factCheck": {"state": fact, "filesCompared": False}, "unbackedPassages": unbacked or [],
             "openPoints": points or [], "writerAssumptions": assumptions or [], "sourceIssues": []}
    if latest is not None:
        value["latestVersion"] = latest
    if earlier is not None:
        value["earlierOpenPoints"] = earlier
    value.update(extra)
    return value


def export_md(text, version):
    return {"md": {"fileName": f"rundbrief-v{version}.md", "contentType": "text/markdown; charset=utf-8",
                   "contentBase64": base64.b64encode(text.encode("utf-8")).decode("ascii"), "text": text}}


BASIS_SUFFIX = "-basis"


def write(root, order, label, res, export=None, export_error=None, basis=True):
    """Writes the snapshot and, unless ``basis`` is false, the same state again as ``<label>-basis``: in a chain the base of the
    next change is a snapshot of its own (G1), taken right before that change."""
    labels = [label] + ([label + BASIS_SUFFIX] if basis and not label.endswith(BASIS_SUFFIX) else [])
    for name in labels:
        path = rc.snapshot_path(root, order, name)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps({"orderId": order, "label": name, "fetchedAt": "2026-11-29T00:00:00+00:00", "result": res,
                                    "export": export, "exportError": export_error}, ensure_ascii=False), encoding="utf-8")


def plan_cases():
    return [
        {"case": "local-date", "kind": "local", "order": O1, "channel": "web", "base": "v1", "new": "v2", "expect": "5. Mai"},
        {"case": "local-add", "kind": "local", "order": O2, "channel": "web", "base": "v1", "new": "v2"},
        {"case": "local-delete", "kind": "local", "order": O3, "channel": "mcp", "base": "v1", "new": "v2"},
        {"case": "tone-formal", "kind": "tone", "order": O1, "channel": "web", "base": "v2-basis", "new": "v3"},
        {"case": "tone-friendly", "kind": "tone", "order": O2, "channel": "mcp", "base": "v2-basis", "new": "v3"},
        {"case": "shorter", "kind": "shorter", "order": O3, "channel": "web", "base": "v2-basis", "new": "v3"},
        {"case": "longer", "kind": "longer", "order": O4, "channel": "mcp", "base": "v1", "new": "v2"},
        {"case": "local-chain", "kind": "local", "order": O1, "channel": "web", "base": "v3-basis", "new": "v4"},
        {"case": "stop", "kind": "stop", "order": O4, "channel": "web", "base": "v2-basis", "new": "after-stop",
         "stopped_version": 3},
    ]


def scenario(root, override=None):
    """A passing data set of 8 synthetic changes and the stop; ``override`` replaces single snapshots: key ``(order, label)``."""
    o1_v1 = BASE[:2] + ["Absatz 3 Der Termin ist am 12. Mai im Gruppenraum"] + BASE[3:]
    o1_v2 = BASE[:2] + ["Absatz 3 Der Termin ist am 5. Mai im Gruppenraum"] + BASE[3:]
    o1_v3 = [p.replace("Wort", "Begriff") if i in (0, 4) else p for i, p in enumerate(o1_v2)]
    o1_v4 = o1_v3[:5] + [o1_v3[5] + " Bis bald."] + o1_v3[6:]
    o2_v1 = list(BASE)
    o2_v2 = BASE[:4] + [BASE[4] + " Bitte bringt eine Schere mit."] + BASE[5:]
    o2_v3 = [p.replace("Wort", "Ausdruck") for p in o2_v2]
    o3_v1 = list(BASE)
    o3_v2 = BASE[:5] + BASE[6:]
    o3_v3 = [para(i, n=6) for i in range(1, 8)]
    o4_v1 = list(BASE)
    o4_v2 = BASE + [para(9), para(10)]
    unbacked_o1 = [{"quote": "Absatz 2 Wort2x0 Wort2x1", "kind": "not_backed", "blocking": True}]
    data = {
        (O1, "v1"): result(O1, 1, text_of(o1_v1), unbacked=unbacked_o1, points=["Die Uhrzeit fehlt."]),
        (O1, "v2"): result(O1, 2, text_of(o1_v2), unbacked=[{"quote": "Absatz 2 Wort2x0 Wort2x1 Wort2x2", "kind": "not_backed",
                                                             "blocking": True}], points=["Uhrzeit fehlt"]),
        (O1, "v3"): result(O1, 3, text_of(o1_v3), unbacked=[{"quote": "Absatz 2 Wort2x0 Wort2x1 Wort2x2", "kind": "not_backed",
                                                             "blocking": True}], points=[],
                           earlier=[{"version": 2, "text": "Uhrzeit fehlt."}]),
        (O1, "v4"): result(O1, 4, text_of(o1_v4), unbacked=[{"quote": "Absatz 2 Wort2x0 Wort2x1 Wort2x2", "kind": "not_backed",
                                                             "blocking": True}],
                           earlier=[{"version": 2, "text": "Uhrzeit fehlt."}]),
        (O2, "v1"): result(O2, 1, text_of(o2_v1), unbacked=[{"quote": "Wort9x1 gibt es nicht", "kind": "not_backed",
                                                             "blocking": True}]),
        (O2, "v2"): result(O2, 2, text_of(o2_v2)),
        (O2, "v3"): result(O2, 3, text_of(o2_v3), unbacked=[{"quote": "Ausdruck5x3", "kind": "not_backed", "blocking": True}]),
        (O3, "v1"): result(O3, 1, text_of(o3_v1)),
        (O3, "v2"): result(O3, 2, text_of(o3_v2)),
        (O3, "v3"): result(O3, 3, text_of(o3_v3)),
        (O4, "v1"): result(O4, 1, text_of(o4_v1)),
        (O4, "v2"): result(O4, 2, text_of(o4_v2)),
        (O4, "after-stop"): result(O4, 2, text_of(o4_v2), latest=3, state="stopped",
                                   note="Version 3 could not be finished. This is version 2."),
    }
    data.update(override or {})
    for (order, label), res in data.items():
        if res is None:
            continue
        export = export_md(res["text"], res["version"]) if label == "after-stop" else None
        write(root, order, label, res, export=export)
    return data


def run(tmp_path, cases=None, override=None):
    scenario(tmp_path, override)
    return rc.evaluate(cases or plan_cases(), tmp_path)


def test_paragraphs_split_on_blank_lines_and_collapse_white_space():
    assert rc.paragraphs("Eins  zwei\ndrei\n\n \n\nvier\r\n\r\nfünf") == ["Eins zwei drei", "vier", "fünf"]


def test_common_paragraphs_is_the_longest_common_subsequence():
    assert rc.common_paragraphs(list("abcdef"), list("abxdef")) == 5
    assert rc.common_paragraphs(list("abc"), list("cba")) == 1
    assert rc.common_paragraphs([], list("abc")) == 0


def test_paragraph_diff_counts_added_and_removed():
    diff = rc.paragraph_diff("a\n\nb\n\nc", "a\n\nB\n\nc\n\nd")
    assert diff == {"old": 3, "new": 4, "unchanged": 2, "removed": 1, "added": 2}


def test_words_need_a_letter_or_digit():
    assert rc.count_words("Hallo – Welt, 5 € **") == 3


def test_quote_stands_in_text_as_whole_words_ignoring_case_and_quote_marks():
    assert rc.stands_in("Es kostet 15 Euro pro Kind.", "15 euro pro kind")
    assert not rc.stands_in("Es kostet 15 Euro pro Kind.", "5 Euro pro Kind")
    assert rc.stands_in("Sie sagte „bitte“ – danke", 'sagte "bitte" - danke')
    assert rc.stands_in("Zeile\neins  zwei", "zeile eins zwei")
    assert not rc.stands_in("irgendwas", "   ")


def test_literal_counts_case_and_whole_words():
    assert rc.contains_literal("Treffen am 5. Mai.", "5. Mai")
    assert not rc.contains_literal("Treffen am 15. Mai.", "5. Mai")
    assert not rc.contains_literal("Treffen am 5. mai.", "5. Mai")


def test_point_key_drops_closing_punctuation_and_one_leading_article():
    assert rc.point_key("Die Eltern werden mit Sie angesprochen.") == rc.point_key("eltern werden mit Sie angesprochen")
    assert rc.point_key("The time is missing!") == rc.point_key("time is missing")
    assert rc.point_key("Eltern werden nicht angesprochen") != rc.point_key("Eltern werden angesprochen")


def test_placeholders_skip_links_images_check_boxes_and_footnotes():
    text = ("Hallo [Name], siehe [hier](https://info.example) und ![Bild](a.png). [x] erledigt, [ ] offen, Fußnote [1], [^2], "
            "[ref]: https://info.example, [Name] noch mal, [[doppelt]], [Datum Ostermontag 2027].")
    assert rc.find_placeholders(text) == ["[Name]", "[Datum Ostermontag 2027]"]


def test_passing_scenario(tmp_path):
    report = run(tmp_path)
    assert report["verdict"] == "passed", (report["problems"], {k: (v["failures"], v["problems"]) for k, v in report["parts"].items()})
    assert report["with_text"] == 8
    assert report["changes"] == 8
    assert not report["halt"]
    assert report["follow_up"] == [rc.FOLLOW_UP["passed"]]


def test_local_change_of_more_than_two_paragraphs_fails_n1(tmp_path):
    changed = [p.replace("Wort", "Neu") if i < 3 else p for i, p in enumerate(BASE)]
    report = run(tmp_path, override={(O2, "v2"): rc_result(O2, 2, changed)})
    assert report["parts"]["N1"]["verdict"] == "failed"
    assert report["verdict"] == "failed"
    assert not report["halt"]
    assert rc.FOLLOW_UP["instruction"] in report["follow_up"]


def rc_result(order, version, paragraphs, **kwargs):
    return result(order, version, text_of(paragraphs), **kwargs)


def test_local_change_outside_fifteen_percent_words_fails_n1(tmp_path):
    longer = BASE[:4] + [BASE[4] + " " + " ".join(f"zusatz{k}" for k in range(20))] + BASE[5:]
    report = run(tmp_path, override={(O2, "v2"): rc_result(O2, 2, longer)})
    assert any("Wörter" in f for f in report["parts"]["N1"]["failures"])


def test_new_detail_missing_in_first_case_fails_n1(tmp_path):
    report = run(tmp_path, override={(O1, "v2"): rc_result(O1, 2, BASE[:2] + ["Absatz 3 Der Termin ist am 15. Mai im Gruppenraum"]
                                                    + BASE[3:], points=["Uhrzeit fehlt"],
                                                    unbacked=[{"quote": "Absatz 2 Wort2x0 Wort2x1", "kind": "not_backed"}])})
    assert any("neue Angabe" in f for f in report["parts"]["N1"]["failures"])


def test_new_detail_already_in_base_is_not_evaluable(tmp_path):
    cases = plan_cases()
    cases[0]["expect"] = "Gruppenraum"
    report = run(tmp_path, cases)
    assert report["parts"]["N1"]["verdict"] == "not_evaluable"
    assert report["verdict"] == "not_evaluable"


def test_two_new_not_backed_over_both_tone_cases_fail_n2(tmp_path):
    o1_v2 = BASE[:2] + ["Absatz 3 Der Termin ist am 5. Mai im Gruppenraum"] + BASE[3:]
    o1_v3 = [p.replace("Wort", "Begriff") if i in (0, 4) else p for i, p in enumerate(o1_v2)]
    report = run(tmp_path, override={(O1, "v3"): rc_result(O1, 3, o1_v3, earlier=[{"version": 2, "text": "Uhrzeit fehlt"}],
                                                    unbacked=[{"quote": "Absatz 2 Wort2x0 Wort2x1 Wort2x2", "kind": "not_backed"},
                                                              {"quote": "Begriff1x2", "kind": "not_backed"}])})
    assert report["parts"]["N2"]["new_not_backed"] == 2
    assert report["parts"]["N2"]["verdict"] == "failed"


def test_not_backed_that_stood_in_the_base_is_not_new(tmp_path):
    report = run(tmp_path)
    assert report["parts"]["N2"]["new_not_backed"] == 1


def test_tone_without_fact_check_is_not_evaluable(tmp_path):
    o2_v2 = BASE[:4] + [BASE[4] + " Bitte bringt eine Schere mit."] + BASE[5:]
    report = run(tmp_path, override={(O2, "v3"): rc_result(O2, 3, [p.replace("Wort", "Ausdruck") for p in o2_v2], fact="not_checked")})
    assert report["parts"]["N2"]["verdict"] == "not_evaluable"
    assert report["parts"]["N4"]["verdict"] == "not_evaluable"
    assert report["verdict"] == "not_evaluable"


@pytest.mark.parametrize("words,verdict", [(6, "passed"), (11, "failed"), (2, "failed")])
def test_shorter_must_land_between_40_and_85_percent(tmp_path, words, verdict):
    report = run(tmp_path, override={(O3, "v3"): rc_result(O3, 3, [para(i, n=words) for i in range(1, 8)])})
    assert report["parts"]["N3"]["verdict"] == verdict


def test_longer_must_land_between_115_and_160_percent(tmp_path):
    report = run(tmp_path, override={(O4, "v2"): rc_result(O4, 2, BASE + [para(i) for i in range(9, 15)]),
                              (O4, "after-stop"): rc_result(O4, 2, BASE + [para(i) for i in range(9, 15)], latest=3,
                                                            state="stopped", note="Version 3 could not be finished. This is version 2.")})
    assert report["parts"]["N3"]["verdict"] == "failed"


def test_missing_shorter_case_leaves_n3_not_evaluable_even_with_six_texts(tmp_path):
    report = run(tmp_path, override={(O3, "v3"): None})
    assert report["with_text"] == 7
    assert report["parts"]["N3"]["verdict"] == "not_evaluable"
    assert report["verdict"] == "not_evaluable"


def test_failed_change_shows_the_base_version_and_counts_without_text(tmp_path):
    o2_v2 = BASE[:4] + [BASE[4] + " Bitte bringt eine Schere mit."] + BASE[5:]
    report = run(tmp_path, override={(O2, "v3"): rc_result(O2, 2, o2_v2)})
    row = next(r for r in report["cases"] if r["case"] == "tone-friendly")
    assert not row["has_text"]
    assert report["with_text"] == 7
    assert report["parts"]["N2"]["verdict"] == "passed"


def test_fewer_than_six_with_text_is_not_evaluable(tmp_path):
    report = run(tmp_path, override={(O1, "v4"): None, (O2, "v3"): None, (O3, "v3"): None})
    assert report["with_text"] == 5
    assert report["verdict"] == "not_evaluable"
    assert any("nur 5 von 8" in p for p in report["problems"])


def test_lost_open_passage_halts(tmp_path):
    o1_v2 = BASE[:2] + ["Absatz 3 Der Termin ist am 5. Mai im Gruppenraum"] + BASE[3:]
    report = run(tmp_path, override={(O1, "v2"): rc_result(O1, 2, o1_v2, points=["Uhrzeit fehlt"])})
    assert report["parts"]["N4"]["verdict"] == "failed"
    assert report["halt"]
    assert report["verdict"] == "failed"
    assert rc.FOLLOW_UP["halt"] in report["follow_up"]


def test_open_passage_whose_quote_is_gone_need_not_appear(tmp_path):
    base = rc_result(O3, 1, BASE, unbacked=[{"quote": "Absatz 6 Wort6x0", "kind": "not_backed"}])
    report = run(tmp_path, override={(O3, "v1"): base})
    assert report["parts"]["N4"]["verdict"] == "passed"


def test_lost_point_of_the_writer_halts(tmp_path):
    report = run(tmp_path, override={(O3, "v2"): rc_result(O3, 2, BASE[:5] + BASE[6:]),
                              (O3, "v1"): rc_result(O3, 1, BASE, points=["Der Ort ist offen."])})
    assert any("Punkt des Schreibers" in f for f in report["parts"]["N4"]["failures"])
    assert report["halt"]


def test_earlier_point_must_carry_the_version_of_the_base(tmp_path):
    o1_v2 = BASE[:2] + ["Absatz 3 Der Termin ist am 5. Mai im Gruppenraum"] + BASE[3:]
    o1_v3 = [p.replace("Wort", "Begriff") if i in (0, 4) else p for i, p in enumerate(o1_v2)]
    report = run(tmp_path, override={(O1, "v3"): rc_result(O1, 3, o1_v3, earlier=[{"version": 1, "text": "Uhrzeit fehlt"}],
                                                    unbacked=[{"quote": "Absatz 2 Wort2x0 Wort2x1 Wort2x2",
                                                               "kind": "not_backed"}])})
    assert report["parts"]["N4"]["verdict"] == "failed"


def test_placeholder_not_listed_halts(tmp_path):
    report = run(tmp_path, override={(O3, "v2"): rc_result(O3, 2, BASE[:5] + [BASE[6] + " [Ort]"] + BASE[7:], placeholders=[])})
    assert any("[Ort]" in f for f in report["parts"]["N4"]["failures"])


def test_n4_without_open_points_list_is_not_evaluable(tmp_path):
    res = rc_result(O3, 2, BASE[:5] + BASE[6:])
    del res["openPoints"]
    report = run(tmp_path, override={(O3, "v2"): res})
    assert report["parts"]["N4"]["verdict"] == "not_evaluable"


def test_source_issues_are_only_notes(tmp_path):
    base = rc_result(O3, 1, BASE, sourceIssues=["Summe falsch"])
    report = run(tmp_path, override={(O3, "v1"): base})
    row = next(r for r in report["cases"] if r["case"] == "local-delete")
    assert len(row["change"]["keep"]["notes"]) == 1
    assert report["parts"]["N4"]["verdict"] == "passed"


POINT = "Der Ort ist offen."


@pytest.mark.parametrize("v3_fields,verdict", [
    ({}, "failed"),
    ({"earlier": [{"version": 1, "text": POINT}]}, "passed"),
    ({"earlier": [{"version": 1, "text": "ort ist offen", "kind": "openPoint"}]}, "passed"),
    ({"points": [POINT]}, "passed"),
    ({"earlier": [{"version": 2, "text": POINT}]}, "failed"),
    ({"earlier": [{"version": 1, "text": POINT, "kind": "assumption"}]}, "failed"),
])
def test_n4_keeps_earlier_open_points_through_the_chain(tmp_path, v3_fields, verdict):
    """v1 notes a point, v2 carries it as an earlier point of version 1, v3 has to keep it with that version."""
    data = scenario(tmp_path)
    o2_v1, o2_v2, o2_v3 = (data[(O2, label)] for label in ("v1", "v2", "v3"))
    o2_v1["openPoints"] = [POINT]
    o2_v2["earlierOpenPoints"] = [{"version": 1, "text": POINT}]
    o2_v3.update({"earlierOpenPoints": v3_fields.get("earlier"), "openPoints": v3_fields.get("points", [])})
    for label, res in (("v1", o2_v1), ("v2", o2_v2), ("v3", o2_v3)):
        write(tmp_path, O2, label, res)
    report = rc.evaluate(plan_cases(), tmp_path)
    assert report["parts"]["N4"]["verdict"] == verdict, report["parts"]["N4"]
    assert report["halt"] == (verdict == "failed")
    if verdict == "failed":
        assert any(f.startswith("tone-friendly: offener Punkt aus Version 1") for f in report["parts"]["N4"]["failures"])


ASSUMPTION = "Die Anmeldefrist ist Freitag."


@pytest.mark.parametrize("v2_fields,verdict", [
    ({}, "failed"),
    ({"writerAssumptions": [ASSUMPTION]}, "passed"),
    ({"writerAssumptions": ["Anmeldefrist ist Freitag"]}, "passed"),
    ({"writerAssumptions": ["Die Anmeldefrist ist Montag."]}, "failed"),
    ({"earlierOpenPoints": [{"version": 1, "text": ASSUMPTION, "kind": "assumption"}]}, "passed"),
    ({"earlierOpenPoints": [{"version": 1, "text": ASSUMPTION}]}, "failed"),
    ({"earlierOpenPoints": [{"version": 2, "text": ASSUMPTION, "kind": "assumption"}]}, "failed"),
    ({"openPoints": [ASSUMPTION]}, "failed"),
])
def test_n4_keeps_writer_assumptions_of_the_base(tmp_path, v2_fields, verdict):
    base = rc_result(O3, 1, BASE, assumptions=[ASSUMPTION])
    new = rc_result(O3, 2, BASE[:5] + BASE[6:])
    new.update(v2_fields)
    carried_on = rc_result(O3, 3, [para(i, n=6) for i in range(1, 8)], assumptions=new["writerAssumptions"],
                           earlier=new.get("earlierOpenPoints"), points=new["openPoints"])
    report = run(tmp_path, override={(O3, "v1"): base, (O3, "v2"): new, (O3, "v3"): carried_on})
    lost = [f for f in report["parts"]["N4"]["failures"] if f.startswith("local-delete: Annahme des Schreibers")]
    assert bool(lost) == (verdict == "failed"), report["parts"]["N4"]
    assert report["parts"]["N4"]["verdict"] == verdict, report["parts"]["N4"]


def test_n4_open_point_of_the_base_is_not_kept_as_an_assumption(tmp_path):
    base = rc_result(O3, 1, BASE, points=[POINT])
    new = rc_result(O3, 2, BASE[:5] + BASE[6:], earlier=[{"version": 1, "text": POINT, "kind": "assumption"}])
    report = run(tmp_path, override={(O3, "v1"): base, (O3, "v2"): new})
    assert any(f.startswith("local-delete: Punkt des Schreibers") for f in report["parts"]["N4"]["failures"])


def test_n4_keeps_a_carried_assumption_in_the_next_version(tmp_path):
    entry = {"version": 1, "text": ASSUMPTION, "kind": "assumption"}
    base = rc_result(O3, 1, BASE, assumptions=[ASSUMPTION])
    new = rc_result(O3, 2, BASE[:5] + BASE[6:], earlier=[entry])
    report = run(tmp_path, override={(O3, "v1"): base, (O3, "v2"): new})
    assert any(f.startswith("shorter: Annahme aus Version 1") for f in report["parts"]["N4"]["failures"])
    third = rc_result(O3, 3, [para(i, n=6) for i in range(1, 8)], earlier=[entry])
    write(tmp_path, O3, "v3", third)
    assert rc.evaluate(plan_cases(), tmp_path)["parts"]["N4"]["verdict"] == "passed"


def clarifier(text):
    return [{"id": "a1", "text": text, "unsure": False}]


@pytest.mark.parametrize("kind,v3_fields,verdict", [
    ("assumption", {"writerAssumptions": ["Anmeldefrist ist Freitag"]}, "passed"),
    ("assumption", {"assumptions": clarifier(ASSUMPTION)}, "passed"),
    ("assumption", {"assumptions": clarifier("Die Anmeldefrist ist Montag.")}, "failed"),
    ("assumption", {}, "failed"),
    ("openPoint", {"openPoints": [ASSUMPTION]}, "passed"),
    ("openPoint", {"writerAssumptions": [ASSUMPTION]}, "failed"),
    ("openPoint", {"assumptions": clarifier(ASSUMPTION)}, "failed"),
])
def test_n4_earlier_entry_repeated_in_its_own_kind_is_kept(tmp_path, kind, v3_fields, verdict):
    """The product no longer carries an earlier entry that the new version shows again in the same kind."""
    data = scenario(tmp_path)
    v2, v3 = data[(O2, "v2")], data[(O2, "v3")]
    v2["earlierOpenPoints"] = [{"version": 1, "text": ASSUMPTION, "kind": kind}]
    v3.update(v3_fields)
    write(tmp_path, O2, "v2", v2)
    write(tmp_path, O2, "v3", v3)
    report = rc.evaluate(plan_cases(), tmp_path)
    assert report["parts"]["N4"]["verdict"] == verdict, report["parts"]["N4"]


def test_n4_writer_assumption_of_the_base_shown_by_the_clarifier_is_kept(tmp_path):
    base = rc_result(O3, 1, BASE, assumptions=[ASSUMPTION])
    new = rc_result(O3, 2, BASE[:5] + BASE[6:])
    new["assumptions"] = clarifier("Anmeldefrist ist Freitag")
    third = rc_result(O3, 3, [para(i, n=6) for i in range(1, 8)])
    report = run(tmp_path, override={(O3, "v1"): base, (O3, "v2"): new, (O3, "v3"): third})
    assert report["parts"]["N4"]["verdict"] == "passed", report["parts"]["N4"]


@pytest.mark.parametrize("field,value", [
    ("assumptions", ["nur ein Satz"]),
    ("writerAssumptions", None),
    ("earlierOpenPoints", "kaputt"),
    ("earlierOpenPoints", [{"version": "1", "text": "x"}]),
    ("earlierOpenPoints", [{"version": 1, "text": "x", "kind": "something"}]),
])
def test_n4_with_unreadable_lists_is_not_evaluable(tmp_path, field, value):
    res = rc_result(O3, 2, BASE[:5] + BASE[6:])
    res[field] = value
    report = run(tmp_path, override={(O3, "v2"): res})
    assert report["parts"]["N4"]["verdict"] == "not_evaluable", report["parts"]["N4"]
    assert report["verdict"] == "not_evaluable"


def test_the_stop_does_not_count_as_a_change_with_text(tmp_path):
    report = run(tmp_path, override={(O1, "v4"): None, (O2, "v3"): None, (O3, "v3"): None})
    assert report["parts"]["N5"]["verdict"] == "passed"
    assert report["with_text"] == 5
    assert report["verdict"] == "not_evaluable"
    assert any("nur 5 von 8 echten" in p for p in report["problems"])


def test_six_of_eight_real_changes_with_text_plus_the_stop_are_evaluable(tmp_path):
    o2_v2 = BASE[:4] + [BASE[4] + " Bitte bringt eine Schere mit."] + BASE[5:]
    report = run(tmp_path, override={(O1, "v4"): None, (O2, "v3"): rc_result(O2, 2, o2_v2)})
    assert report["with_text"] == 6
    assert report["problems"] == []
    assert report["verdict"] == "passed", report["parts"]


def test_plan_with_the_stop_as_one_of_eight_is_not_evaluable(tmp_path):
    cases = [c for c in plan_cases() if c["case"] != "local-chain"]
    cases[-1]["channel"] = "web"
    cases[1]["channel"] = "web"
    scenario(tmp_path)
    report = rc.evaluate(cases, tmp_path)
    assert any("8 echte Nachbesserungen" in p for p in report["problems"])
    assert report["verdict"] == "not_evaluable"


def test_channel_of_the_stop_is_free_but_its_order_is_not(tmp_path):
    cases = plan_cases()
    cases[-1]["channel"] = "mcp"
    assert rc.plan_problems(cases) == []
    cases[-1]["order"] = "55555555-5555-4555-8555-555555555555"
    assert any("Stopp-Fall gehört zu keinem Auftrag" in p for p in rc.plan_problems(cases))


@pytest.mark.parametrize("state", ["failed", "delivered_with_open_points", None])
def test_n5_needs_the_state_stopped(tmp_path, state):
    o4_v2 = BASE + [para(9), para(10)]
    report = run(tmp_path, override={(O4, "after-stop"): rc_result(O4, 2, o4_v2, latest=3, state=state,
                                                            note="Version 3 could not be finished. This is version 2.")})
    assert report["parts"]["N5"]["verdict"] == "failed"
    assert len(report["parts"]["N5"]["failures"]) == 1 and "statt 'stopped'" in report["parts"]["N5"]["failures"][0]
    assert report["halt"]


@pytest.mark.parametrize("count,rewritten,verdict", [(2, 2, "not_evaluable"), (3, 2, "not_evaluable"), (4, 2, "passed")])
def test_local_case_with_fewer_than_four_paragraphs_is_not_evaluable(tmp_path, count, rewritten, verdict):
    old = [para(i) for i in range(1, count + 1)]
    new = [p.replace("Wort", "Neu") if i < rewritten else p for i, p in enumerate(old)]
    cases = plan_cases()
    short = next(c for c in cases if c["case"] == "local-chain")
    short.update(base="short", new="short-new")
    report = run(tmp_path, cases, override={(O1, "short"): rc_result(O1, 3, old), (O1, "short-new"): rc_result(O1, 4, new)})
    assert report["parts"]["N1"]["verdict"] == verdict, report["parts"]["N1"]
    assert report["verdict"] == verdict
    if verdict == "not_evaluable":
        assert any(f"nur {count} Absätze" in p for p in report["parts"]["N1"]["problems"])


def test_stop_without_sentence_halts(tmp_path):
    o4_v2 = BASE + [para(9), para(10)]
    report = run(tmp_path, override={(O4, "after-stop"): rc_result(O4, 2, o4_v2, latest=3, state="stopped")})
    assert report["parts"]["N5"]["verdict"] == "failed"
    assert report["halt"]


def test_stop_showing_the_stopped_version_halts(tmp_path):
    report = run(tmp_path, override={(O4, "after-stop"): rc_result(O4, 3, BASE, latest=3, state="stopped",
                                                            note="Version 3 could not be finished. This is version 2.")})
    assert any("zeigt Version 3" in f for f in report["parts"]["N5"]["failures"])


def test_stop_without_latest_version_is_not_evaluable(tmp_path):
    o4_v2 = BASE + [para(9), para(10)]
    report = run(tmp_path, override={(O4, "after-stop"): rc_result(O4, 2, o4_v2, state="stopped",
                                                            note="Version 3 could not be finished. This is version 2.")})
    assert report["parts"]["N5"]["verdict"] == "not_evaluable"
    assert report["verdict"] == "not_evaluable"


def test_missing_export_is_not_evaluable(tmp_path):
    scenario(tmp_path)
    after = json.loads(rc.snapshot_path(tmp_path, O4, "after-stop").read_text(encoding="utf-8"))
    rc.snapshot_path(tmp_path, O4, "after-stop").unlink()
    write(tmp_path, O4, "after-stop", after["result"], export=None, export_error="Werkzeug export_order meldet einen Fehler")
    report = rc.evaluate(plan_cases(), tmp_path)
    assert "Export fehlt" in report["problems"]
    assert report["verdict"] == "not_evaluable"


def test_export_of_another_version_halts(tmp_path):
    scenario(tmp_path)
    after = json.loads(rc.snapshot_path(tmp_path, O4, "after-stop").read_text(encoding="utf-8"))
    rc.snapshot_path(tmp_path, O4, "after-stop").unlink()
    write(tmp_path, O4, "after-stop", after["result"], export=export_md("Etwas anderes", 3))
    report = rc.evaluate(plan_cases(), tmp_path)
    assert report["parts"]["N5"]["verdict"] == "failed"
    assert len(report["parts"]["N5"]["failures"]) == 2


def test_unreadable_snapshot_is_missing_data(tmp_path):
    scenario(tmp_path)
    rc.snapshot_path(tmp_path, O4, "v1").write_text("{kaputt", encoding="utf-8")
    report = rc.evaluate(plan_cases(), tmp_path)
    row = next(r for r in report["cases"] if r["case"] == "longer")
    assert not row["has_text"] and "nicht lesbar" in row["problems"][0]
    assert report["parts"]["N3"]["verdict"] == "not_evaluable"


def test_snapshot_of_another_order_is_rejected(tmp_path):
    report = run(tmp_path, override={(O3, "v3"): rc_result(O2, 3, [para(i, n=6) for i in range(1, 8)])})
    row = next(r for r in report["cases"] if r["case"] == "shorter")
    assert "anderen Auftrag" in row["problems"][0]


def test_plan_with_other_composition_is_not_evaluable(tmp_path):
    cases = plan_cases()
    cases[1]["channel"] = "mcp"
    report = run(tmp_path, cases)
    assert report["verdict"] == "not_evaluable"
    assert any("Wege" in p for p in report["problems"])


def test_plan_without_expected_detail_is_not_evaluable(tmp_path):
    cases = plan_cases()
    del cases[0]["expect"]
    report = run(tmp_path, cases)
    assert any("expect" in p for p in report["problems"])


def test_load_plan_rejects_duplicates_and_bad_ids(tmp_path):
    path = tmp_path / "plan.json"
    cases = plan_cases()
    path.write_text(json.dumps({"cases": cases + [cases[0]]}), encoding="utf-8")
    with pytest.raises(ValueError, match="doppelt"):
        rc.load_plan(path)
    bad = dict(cases[0], order="../../etc")
    path.write_text(json.dumps({"cases": [bad]}), encoding="utf-8")
    with pytest.raises(ValueError, match="ungültige"):
        rc.load_plan(path)


def test_main_evaluate_writes_report_and_returns_zero(tmp_path):
    scenario(tmp_path)
    plan = tmp_path / "plan.json"
    plan.write_text(json.dumps({"cases": plan_cases()}), encoding="utf-8")
    lines = []
    assert rc.main(["evaluate", "--plan", str(plan), "--out-dir", str(tmp_path)], echo=lines.append) == 0
    assert json.loads((tmp_path / "revise-gate.json").read_text(encoding="utf-8"))["verdict"] == "passed"
    assert lines[-1].startswith("Tor N: passed")


def test_main_returns_one_on_halt_and_four_when_not_evaluable(tmp_path):
    scenario(tmp_path, override={(O4, "after-stop"): rc_result(O4, 2, BASE + [para(9), para(10)], latest=3, state="stopped")})
    plan = tmp_path / "plan.json"
    plan.write_text(json.dumps({"cases": plan_cases()}), encoding="utf-8")
    assert rc.main(["evaluate", "--plan", str(plan), "--out-dir", str(tmp_path)], echo=lambda _: None) == 1
    rc.snapshot_path(tmp_path, O4, "after-stop").unlink()
    rc.snapshot_path(tmp_path, O3, "v3").unlink()
    assert rc.main(["evaluate", "--plan", str(plan), "--out-dir", str(tmp_path)], echo=lambda _: None) == 4


def test_main_rejects_unreadable_plan_and_output_inside_git(tmp_path):
    plan = tmp_path / "plan.json"
    plan.write_text("[]", encoding="utf-8")
    assert rc.main(["evaluate", "--plan", str(plan), "--out-dir", str(tmp_path)], echo=lambda _: None) == 2
    lines = []
    inside = Path(__file__).resolve().parent / "nie-hier"
    assert rc.main(["evaluate", "--plan", str(plan), "--out-dir", str(inside)], echo=lines.append) == 2
    assert "Git-Baum" in lines[0]
    assert not inside.exists()


class FakeClient:
    def __init__(self, answers):
        self.answers = answers
        self.calls = []

    def call_tool(self, name, arguments):
        self.calls.append((name, arguments))
        answer = self.answers[name]
        if isinstance(answer, Exception):
            raise answer
        return answer


def test_snapshot_stores_result_and_export_and_never_overwrites(tmp_path):
    text = text_of(BASE)
    client = FakeClient({"get_order_result": result(O1, 1, text), "export_order": export_md(text, 1)["md"]})
    lines = []
    args = ["snapshot", "--order", O1, "--label", "v1", "--out-dir", str(tmp_path)]
    assert rc.main(args, connector=lambda _: client, echo=lines.append) == 0
    stored = json.loads(rc.snapshot_path(tmp_path, O1, "v1").read_text(encoding="utf-8"))
    assert stored["result"]["version"] == 1 and stored["export"]["md"]["text"] == text
    assert client.calls == [("get_order_result", {"orderId": O1}), ("export_order", {"orderId": O1, "format": "md"})]
    assert rc.main(args, connector=lambda _: client, echo=lines.append) == 2
    assert len(client.calls) == 2


def test_snapshot_records_a_failed_export(tmp_path):
    client = FakeClient({"get_order_result": result(O1, 1, "Text"), "export_order": McpError("Werkzeug fehlt")})
    assert rc.main(["snapshot", "--order", O1, "--label", "v1", "--out-dir", str(tmp_path)], connector=lambda _: client,
                   echo=lambda _: None) == 0
    stored = json.loads(rc.snapshot_path(tmp_path, O1, "v1").read_text(encoding="utf-8"))
    assert stored["export"] is None and "Werkzeug fehlt" in stored["exportError"]


def test_snapshot_rejects_bad_order_id_and_reports_sign_in_failure(tmp_path):
    assert rc.main(["snapshot", "--order", "../x", "--label", "v1", "--out-dir", str(tmp_path)],
                   connector=lambda _: pytest.fail("no sign-in for a bad id"), echo=lambda _: None) == 2

    def refuse(_):
        raise rc.SignInError("Login abgelehnt (HTTP 200)")

    assert rc.main(["snapshot", "--order", O1, "--label", "v1", "--out-dir", str(tmp_path)], connector=refuse,
                   echo=lambda _: None) == 3
    assert not os.listdir(tmp_path)


NOTE = "The checks of version 1 were not taken over, so anything they left open was not checked again."


@pytest.mark.parametrize("v3_fields,verdict", [
    ({"earlierOpenPoints": [{"version": 1, "text": NOTE, "kind": "note"}]}, "passed"),
    ({}, "failed"),
    ({"earlierOpenPoints": [{"version": 1, "text": NOTE}]}, "failed"),
    ({"earlierOpenPoints": [{"version": 1, "text": NOTE, "kind": "assumption"}]}, "failed"),
    ({"earlierOpenPoints": [{"version": 2, "text": NOTE, "kind": "note"}]}, "failed"),
    ({"openPoints": [NOTE]}, "failed"),
    ({"writerAssumptions": [NOTE]}, "failed"),
])
def test_n4_keeps_a_fixed_sentence_of_atelier_only_in_its_own_kind(tmp_path, v3_fields, verdict):
    """A fixed sentence of Atelier (kind note) about an earlier version is kept only as a note with that version."""
    data = scenario(tmp_path)
    v2, v3 = data[(O2, "v2")], data[(O2, "v3")]
    v2["earlierOpenPoints"] = [{"version": 1, "text": NOTE, "kind": "note"}]
    v3.update(v3_fields)
    write(tmp_path, O2, "v2", v2)
    write(tmp_path, O2, "v3", v3)
    report = rc.evaluate(plan_cases(), tmp_path)
    assert report["parts"]["N4"]["verdict"] == verdict, report["parts"]["N4"]
    if verdict == "failed":
        assert any(f.startswith("tone-friendly: fester Satz von Atelier aus Version 1") for f in report["parts"]["N4"]["failures"])


def test_n4_reads_the_kind_note_and_still_refuses_unknown_kinds(tmp_path):
    data = scenario(tmp_path)
    v2 = data[(O2, "v2")]
    v2["earlierOpenPoints"] = [{"version": 1, "text": NOTE, "kind": "remark"}]
    write(tmp_path, O2, "v2", v2)
    report = rc.evaluate(plan_cases(), tmp_path)
    assert report["parts"]["N4"]["verdict"] == "not_evaluable", report["parts"]["N4"]
    assert any("note" in p for p in report["parts"]["N4"]["problems"])


def test_plan_counting_the_same_change_twice_is_not_evaluable(tmp_path):
    cases = plan_cases()
    tone = next(c for c in cases if c["case"] == "tone-friendly")
    tone.update(order=O1, base="v2-basis", new="v3")
    report = run(tmp_path, cases)
    assert report["verdict"] == "not_evaluable"
    assert any("dieselbe Nachbesserung" in p and "tone-formal" in p and "tone-friendly" in p for p in report["problems"])
    with pytest.raises(ValueError):
        rc.load_plan(_plan_file(tmp_path, [dict(cases[0]), dict(cases[0])]))


def _plan_file(tmp_path, cases):
    path = tmp_path / "plan.json"
    path.write_text(json.dumps({"cases": cases}), encoding="utf-8")
    return path


def test_snapshot_in_two_cases_is_not_evaluable_also_in_a_chain(tmp_path):
    cases = plan_cases()
    next(c for c in cases if c["case"] == "tone-formal")["base"] = "v2"
    report = run(tmp_path, cases)
    assert report["verdict"] == "not_evaluable"
    assert any(p.startswith(f"Schnappschuss v2 von {O1} in mehreren Fällen: local-date, tone-formal") for p in report["problems"])


def test_stop_may_not_reuse_the_snapshot_of_a_change(tmp_path):
    cases = plan_cases()
    next(c for c in cases if c["case"] == "stop")["base"] = "v2"
    report = run(tmp_path, cases)
    assert report["verdict"] == "not_evaluable"
    assert any("longer, stop" in p for p in report["problems"]), report["problems"]


def test_case_with_the_same_snapshot_as_base_and_new_is_not_evaluable(tmp_path):
    cases = plan_cases()
    next(c for c in cases if c["case"] == "longer").update(base="v2", new="v2")
    report = run(tmp_path, cases)
    assert report["verdict"] == "not_evaluable"
    assert any("derselbe Schnappschuss v2" in p for p in report["problems"]), report["problems"]


def test_other_labels_for_the_same_new_version_are_not_evaluable(tmp_path):
    """The finding of the release gate: cases point at copies of the snapshots of other cases."""
    cases = plan_cases()
    next(c for c in cases if c["case"] == "tone-friendly").update(order=O1, base="v1-basis", new="v2-basis")
    report = run(tmp_path, cases)
    assert report["verdict"] == "not_evaluable"
    assert any(p.startswith(f"dieselbe neue Fassung (Auftrag {O1}, Version 2) in mehreren Fällen: local-date, tone-friendly")
               for p in report["problems"]), report["problems"]


@pytest.mark.parametrize("case,part", [("tone-formal", "N2"), ("local-add", "N1"), ("local-delete", "N1")])
def test_unchanged_text_fails_n1_and_n2(tmp_path, case, part):
    row = next(c for c in plan_cases() if c["case"] == case)
    data = scenario(tmp_path)
    base = data[(row["order"], row["base"].removesuffix("-basis"))]
    unchanged = dict(base, version=data[(row["order"], row["new"])]["version"])
    report = run(tmp_path, override={(row["order"], row["new"]): unchanged})
    assert report["parts"][part]["verdict"] == "failed", report["parts"][part]
    assert f"{case}: Text unverändert, die Änderung wurde nicht gemacht" in report["parts"][part]["failures"]
    assert report["verdict"] == "failed"
    assert rc.FOLLOW_UP["instruction"] in report["follow_up"]


def test_only_white_space_changed_counts_as_unchanged(tmp_path):
    data = scenario(tmp_path)
    base = data[(O2, "v2")]
    spaced = dict(data[(O2, "v3")], text=base["text"].replace("\n\n", "\n\n\n").replace(" ", "  "))
    report = run(tmp_path, override={(O2, "v3"): spaced})
    assert "tone-friendly: Text unverändert, die Änderung wurde nicht gemacht" in report["parts"]["N2"]["failures"]
    row = next(r for r in report["cases"] if r["case"] == "tone-friendly")
    assert row["change"]["text_changed"] is False
