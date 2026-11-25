import json
from collections import Counter
from decimal import Decimal

import pytest

from conftest import FakeRunner, claude_json
from geef_eval import factcheck_corpus as fc
from geef_eval.cli_runner import CliResult

SLOT = {s["id"]: s for s in fc.SLOTS}


def _words(n, word="Wort"):
    return " ".join([word] * n)


def _generated(slot, **overrides):
    """A generator reply that satisfies the rules of ``slot``."""
    if slot.get("case") == fc.PARENTS_EVENING:
        return {"clean_text": "Liebe Eltern, der Ausflug ins Naturkundemuseum ist am Dienstag, 6. Mai. " + _words(120)}
    long = slot["size"] == "long"
    files = [{"name": "notizen.txt", "text": "Notiz " * (2_500 if long else 300)}]
    reply = {"request": "Bitte schreib den Text aus meinen Notizen.", "answers": [], "profile": None, "files": files,
             "seed_text": None, "source_errors": [], "clean_text": _words(2_100 if long else 200)}
    if slot["kind"] == "revision":
        reply["seed_text"] = _words(150, "Entwurf")
    if slot["kind"] == "profile":
        reply["profile"] = {"display_name": "Ida Brenner", "role": None, "organization": None, "signature": None,
                            "no_gos": "Nie Ausrufezeichen verwenden."}
    if slot["kind"] == "faulty_source":
        files[0]["text"] += "\nBeitrag laut Einladung 240 EUR.\nBeitrag laut Protokoll 280 EUR.\n"
        reply["source_errors"] = [{"kind": "conflict", "quote": "Beitrag laut Einladung 240 EUR.",
                                   "quote_2": "Beitrag laut Protokoll 280 EUR.", "stated": None}]
    reply.update(overrides)
    return reply


def _reply_for_prompt(cmd, prompt, cwd):
    for slot in fc.SLOTS:
        if slot.get("case") == fc.PARENTS_EVENING and "Request and notes are fixed" in prompt:
            return claude_json(json.dumps(_generated(slot)))
    for slot in fc.SLOTS:
        if slot["area"] and slot["area"] in prompt:
            return claude_json(json.dumps(_generated(slot)))
    raise AssertionError("unknown prompt")


def _basis(slot_id="fc-01", **extra):
    slot = SLOT[slot_id]
    parsed = fc.parse_generated(_generated(slot), slot)
    return {"id": slot_id, "size": slot["size"], "lang": slot["lang"], "kind": slot["kind"], "case": slot["case"], **parsed, **extra}


def _read(path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def test_slots_follow_the_contract_mix():
    normal = [s for s in fc.SLOTS if s["kind"] != "faulty_source"]
    faulty = [s for s in fc.SLOTS if s["kind"] == "faulty_source"]

    assert len(fc.SLOTS) == 16 and len({s["id"] for s in fc.SLOTS}) == 16
    assert Counter(s["size"] for s in normal) == {"short": 8, "long": 4}
    assert Counter(s["lang"] for s in normal) == {"de": 8, "en": 4}
    assert sum(s["kind"] == "revision" for s in normal) >= 2 and sum(s["kind"] == "profile" for s in normal) >= 2
    assert {s["case"] for s in faulty} == {fc.PARENTS_EVENING, "conflicting_amounts", "superseded_decision", "wrong_subtotal"}


def test_parents_evening_is_always_in_the_development_set_and_the_held_out_ids_are_fixed():
    assert fc.split({"case": fc.PARENTS_EVENING, "id": "fc-02"}) == fc.DEV
    assert [s["id"] for s in fc.SLOTS if fc.split(s) == fc.HOLDOUT] == ["fc-02", "fc-04", "fc-06", "fc-10", "fc-14"]
    assert fc.text_split("split-vector-1") == "dev"
    assert fc.text_split("split-vector-2") == "holdout"


@pytest.mark.parametrize("text,value", [
    ("1.234,50 €", Decimal("1234.50")), ("$1,234.50", Decimal("1234.50")), ("22,50 €", Decimal("22.50")),
    ("Summe: 72 €", Decimal("72")), ("1.234", Decimal("1234")), ("−5", Decimal("-5")), ("12.5", Decimal("12.5")),
    ("keine Zahl", None), (None, None),
])
def test_amounts_are_read_like_the_csharp_reader(text, value):
    assert fc.parse_number(text) == value


def test_recalculation_multiplies_before_it_adds():
    assert fc.recalculate({"items": ["22", "31", "19"], "ops": []}) == Decimal("72")
    assert fc.recalculate({"items": ["10", "3", "24 €"], "ops": ["+", "x"]}) == Decimal("82")
    assert fc.recalculate({"items": ["10", "3"], "ops": ["/"]}) is None


def _snippet(planted=True, stated="62", text=None):
    line = f"Summe: {stated} €"
    return {"id": "ar-01", "planted": planted, "text": text or f"Bus 22 €\nEintritt 31 €\nEssen 19 €\n{line}",
            "trace": {"total_line": line, "items": ["22 €", "31 €", "19 €"], "ops": [], "stated": stated, "correct": "72"}}


def test_trace_check_accepts_only_traces_that_recompute():
    assert fc.trace_problem(_snippet()) is None
    assert fc.trace_problem(_snippet(planted=False, stated="72")) is None
    assert "stimmt" in fc.trace_problem(_snippet(planted=True, stated="72"))
    assert "stimmt nicht" in fc.trace_problem(_snippet(planted=False, stated="62"))
    assert "Summenzeile" in fc.trace_problem(_snippet(text="Bus 22 €\nEintritt 31 €\nEssen 19 €"))
    assert fc.trace_problem({"id": "x", "planted": False, "text": "Am 3. Mai um 14 Uhr", "trace": None}) is None
    assert fc.trace_problem({"id": "x", "planted": True, "text": "x", "trace": None}) == "geplant ohne Spur"


def test_parents_evening_keeps_its_fixed_material_and_wrong_total():
    parsed = fc.parse_generated(_generated(SLOT["fc-13"]), SLOT["fc-13"])

    assert parsed["request"] == fc.PARENTS_EVENING_REQUEST
    assert parsed["files"][0]["text"] == fc.PARENTS_EVENING_NOTES
    assert parsed["source_errors"][0]["quote"] in fc.PARENTS_EVENING_NOTES
    assert "Busfahrt 9 €" in fc.PARENTS_EVENING_NOTES and parsed["source_errors"][0]["stated"] == "17"
    with pytest.raises(ValueError):
        fc.parse_generated({"clean_text": "x", "request": "anders"}, SLOT["fc-13"])


def test_generator_reply_rules():
    revision = fc.parse_generated(_generated(SLOT["fc-04"]), SLOT["fc-04"])
    assert revision["files"][0]["id"] == "S0" and revision["files"][1]["id"] == "S1"

    with pytest.raises(ValueError, match="no_gos"):
        fc.parse_generated(_generated(SLOT["fc-05"], profile=None), SLOT["fc-05"])
    with pytest.raises(ValueError, match="wörtlich"):
        fc.parse_generated(_generated(SLOT["fc-14"], source_errors=[{"kind": "conflict", "quote": "erfunden 1", "quote_2": "erfunden 2"}]), SLOT["fc-14"])
    with pytest.raises(ValueError, match="faulty_source"):
        fc.parse_generated(_generated(SLOT["fc-01"], source_errors=[{"kind": "conflict", "quote": "Notiz", "quote_2": "Notiz"}]), SLOT["fc-01"])
    with pytest.raises(ValueError, match="Wörter"):
        fc.parse_generated(_generated(SLOT["fc-01"], clean_text=_words(700)), SLOT["fc-01"])
    with pytest.raises(ValueError, match="1.500"):
        fc.parse_generated(_generated(SLOT["fc-09"], clean_text=_words(500)), SLOT["fc-09"])
    with pytest.raises(ValueError, match="unbekannte"):
        fc.parse_generated(_generated(SLOT["fc-01"], intent="geheim"), SLOT["fc-01"])


def test_labels_must_quote_the_text_word_for_word():
    text = "Das Fest beginnt am Samstag um 15 Uhr im Garten."
    assert fc.parse_labels({"unsupported": []}, text) == []
    assert fc.parse_labels({"unsupported": [{"quote": "beginnt am Samstag um 15 Uhr", "why": "nicht genannt"}]}, text)[0]["quote"] \
        == "beginnt am Samstag um 15 Uhr"
    with pytest.raises(ValueError, match="wörtlich"):
        fc.parse_labels({"unsupported": [{"quote": "beginnt am Sonntag um 15 Uhr", "why": ""}]}, text)
    with pytest.raises(ValueError, match="fünf"):
        fc.parse_labels({"unsupported": [{"quote": "um 15 Uhr", "why": ""}]}, text)


def test_plants_are_checked_word_for_word_and_the_parents_evening_needs_an_invented_contact():
    basis = {"clean_text": "Der Ausflug ist am Dienstag. Bitte zahlen Sie bis Ende April.", "case": None}
    copy = "Der Ausflug ist am Mittwoch, dem 7. Mai. Bitte zahlen Sie bis Ende April. Fragen gern an die Nummer 0151 22334455."
    plants = [{"kind": "changed_date", "inserted": "Der Ausflug ist am Mittwoch, dem 7. Mai."},
              {"kind": "invented_contact", "inserted": "Fragen gern an die Nummer 0151 22334455."}]

    text, parsed = fc.parse_plants({"text": copy, "plants": plants}, basis)
    assert text == copy and [p["kind"] for p in parsed] == ["changed_date", "invented_contact"]

    with pytest.raises(ValueError, match="Pflichtfehler"):
        fc.parse_plants({"text": copy, "plants": plants[:1] + [{"kind": "invented_fact", "inserted": "Fragen gern an die Nummer 0151 22334455."}]},
                        {**basis, "case": fc.PARENTS_EVENING})
    with pytest.raises(ValueError, match="nicht wörtlich"):
        fc.parse_plants({"text": copy, "plants": [plants[0], {"kind": "invented_fact", "inserted": "Es gibt Kuchen für alle Kinder."}]}, basis)
    with pytest.raises(ValueError, match="schon im sauberen"):
        fc.parse_plants({"text": copy, "plants": [plants[0], {"kind": "invented_fact", "inserted": "Bitte zahlen Sie bis Ende April."}]}, basis)
    with pytest.raises(ValueError, match="2-4"):
        fc.parse_plants({"text": copy, "plants": plants[:1]}, basis)


def test_generate_writes_every_basis_and_logs_every_call(tmp_path):
    corpus = tmp_path / "factcheck-corpus.jsonl"
    runner = FakeRunner([_reply_for_prompt] * 16)

    summary = fc.generate(corpus, max_calls=16, runner=runner)

    entries = _read(corpus)
    assert summary["missing"] == 0 and summary["calls"] == 16
    assert [e["id"] for e in entries] == [s["id"] for s in fc.SLOTS]
    assert len(_read(fc.calls_log(corpus))) == 16
    assert all(call["cwd_empty"] for call in runner.calls)


def test_the_cap_holds_across_invocations_and_counts_failed_attempts(tmp_path):
    corpus = tmp_path / "factcheck-corpus.jsonl"
    broken = CliResult("kein json", "", 1, False, 0.1)
    first = fc.generate(corpus, max_calls=60, runner=FakeRunner([broken] * 60))
    second = fc.generate(corpus, max_calls=60, runner=FakeRunner([broken] * 60))

    summary = fc.generate(corpus, max_calls=10, runner=FakeRunner([_reply_for_prompt] * 10))

    assert first["calls"] == 32 and second["calls"] == 28
    assert len(_read(fc.calls_log(corpus))) == 60
    assert summary["calls"] == 0 and summary["stopped_by_max_calls"] and summary["missing"] == 16


def test_max_calls_is_required_and_gaps_end_with_exit_code_4(tmp_path):
    corpus = tmp_path / "factcheck-corpus.jsonl"
    with pytest.raises(SystemExit) as missing:
        fc.main(["generate", "--corpus", str(corpus)], runner=FakeRunner())
    assert missing.value.code == 2

    assert fc.main(["generate", "--corpus", str(corpus), "--max-calls", "2"], runner=FakeRunner([_reply_for_prompt] * 2)) == 4
    assert len(_read(corpus)) == 2


def test_usage_limit_stops_with_exit_code_3(tmp_path):
    corpus = tmp_path / "factcheck-corpus.jsonl"
    assert fc.main(["generate", "--corpus", str(corpus), "--max-calls", "5"],
                   runner=FakeRunner([claude_json("You've hit your usage limit", is_error=True)])) == 3


def test_label_and_plant_skip_done_bases_and_never_show_the_labels_to_the_planter(tmp_path):
    corpus = tmp_path / "factcheck-corpus.jsonl"
    basis = _basis("fc-01")
    fc.write_entries(corpus, [basis])
    quote = " ".join(basis["clean_text"].split()[:6])

    fc.label(corpus, max_calls=2, runner=FakeRunner([json.dumps({"unsupported": [{"quote": quote, "why": "GEHEIMER-GRUND"}]})]))
    copy = basis["clean_text"] + " Rückfragen bitte an die Nummer 0151 22334455 richten."
    plant_runner = FakeRunner([json.dumps({"text": copy, "plants": [
        {"kind": "invented_contact", "inserted": "Rückfragen bitte an die Nummer 0151 22334455 richten."},
        {"kind": "invented_fact", "inserted": "Rückfragen bitte an die Nummer 0151 22334455 richten."}]})])
    fc.plant(corpus, max_calls=2, runner=plant_runner)
    again = fc.label(corpus, max_calls=2, runner=FakeRunner())

    stored = _read(corpus)[0]
    assert stored["labels"][0]["quote"] == quote and stored["planted_text"] == copy
    assert "GEHEIMER-GRUND" not in plant_runner.calls[0]["prompt"] and "unsupported" not in plant_runner.calls[0]["prompt"]
    assert again["skipped"] == 1 and again["calls"] == 0


def _arith_reply(batch, broken=()):
    snippets = []
    for slot in batch:
        if slot["form"] == "none":
            snippets.append({"id": slot["id"], "text": "Treffen am 3. Mai um 14 Uhr, Hausnummer 12.", "trace": None})
            continue
        stated = "62" if slot["planted"] else "72"
        line = f"Summe: {stated} €"
        trace = {"total_line": line, "items": ["22 €", "31 €", "19 €"], "ops": [], "stated": stated, "correct": "72"}
        if slot["id"] in broken:
            trace["correct"] = "70"
        snippets.append({"id": slot["id"], "text": f"Bus 22 €\nEintritt 31 €\nEssen 19 €\n{line}", "trace": trace})
    return json.dumps({"snippets": snippets})


def test_arithmetic_slots_have_the_contract_mix():
    slots = fc.arithmetic_slots()

    assert len(slots) == 60
    assert sum(s["planted"] for s in slots) == 30
    assert sum(s["form"] == "none" for s in slots) == 15
    assert {s["lang"] for s in slots} == {"de", "en"}
    assert {s["form"] for s in slots if s["planted"]} == {"expression", "list", "sentence", "table", "notes"}


def test_arithmetic_keeps_good_snippets_and_leaves_broken_traces_missing(tmp_path):
    out = tmp_path / "arithmetic-corpus.jsonl"
    slots = fc.arithmetic_slots()
    first, second = slots[:5], slots[5:10]

    fc.arithmetic(out, max_calls=1, runner=FakeRunner([_arith_reply(first, broken={"ar-01"})]))
    after_first = [e["id"] for e in _read(out)]
    summary = fc.arithmetic(out, max_calls=1, runner=FakeRunner([_arith_reply([slots[0]] + second[:4])]))

    ids = [e["id"] for e in _read(out)]
    assert "ar-01" not in after_first and len(after_first) == 4
    assert "ar-01" in ids and len(ids) == 9
    assert summary["calls"] == 1 and summary["missing"] == 51 and summary["stopped_by_max_calls"]
    assert all(fc.trace_problem(e) is None for e in _read(out))


def test_arithmetic_cap_is_twelve_calls(tmp_path):
    out = tmp_path / "arithmetic-corpus.jsonl"
    broken = CliResult("kein json", "", 1, False, 0.1)

    summary = fc.arithmetic(out, max_calls=50, runner=FakeRunner([broken] * 50))

    assert summary["calls"] == 12 and len(_read(fc.calls_log(out))) == 12


def test_stats_counts_bases_and_calls(tmp_path):
    corpus = tmp_path / "factcheck-corpus.jsonl"
    fc.write_entries(corpus, [_basis("fc-01", labels=[]), _basis("fc-13")])

    counters = fc.stats(corpus)

    assert counters["labelled"] == {"True": 1, "False": 1}
    assert counters["clean"]["True"] == 1
    assert counters["split"]["dev"] >= 1
    assert counters["calls"]["logged"] == 0
