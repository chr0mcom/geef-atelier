import hashlib
import json
from collections import Counter

import pytest

from conftest import FakeRunner, claude_json
from geef_eval import intake_corpus as ic
from geef_eval.cli_runner import CliResult


def _generated(request="Schreib mir bitte eine Absage.", files=None, profile=None):
    return json.dumps({"request": request, "intent": "Absage an Firma Brandt, höflich, kurz.",
                       "files": files or [], "profile": profile})


def _profile():
    return {"display_name": "Mara Voss", "role": None, "organization": None, "formality": "formal",
            "language": "de", "signature": "Mara Voss", "no_gos": None, "free_text": None}


def _revision_file(words=120):
    return [{"name": "entwurf.txt", "kind": "text", "text": " ".join(["Wort"] * words), "pages": None}]


def _reply_for_prompt():
    """A runner reply that satisfies whatever category the generator prompt asks for."""
    def reply(cmd, prompt, cwd):
        if "Category: revision" in prompt:
            return claude_json(_generated(files=_revision_file()))
        if "Category: profile_covered" in prompt:
            return claude_json(_generated(profile=_profile()))
        return claude_json(_generated())
    return reply


def _read(path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def _seed_corpus(path, count=3):
    entries = [{"id": f"main-{i:03d}", "corpus": "main", "category": "sparse", "lang": "de",
                "request": f"Auftrag {i} mit Umlaut ä", "files": [], "profile": None, "intent": "alles",
                "generator_model": "sonnet"} for i in range(1, count + 1)]
    ic.write_entries(path, entries)
    return entries


LABELS = {"labels": {"length": "necessary_ask", "audience": "forbidden_ask", "tone": "acceptable_ask"},
          "note": "Länge nur der Person bekannt."}


def test_assignment_counts_main():
    slots = ic.assignments("main", 120)

    assert len(slots) == 120
    assert Counter(s["category"] for s in slots) == {"sparse": 50, "complete": 40, "revision": 10,
                                                      "researchable": 10, "profile_covered": 10}
    langs = Counter(s["lang"] for s in slots)
    assert langs["fr"] == 4
    assert langs["de"] == 84 and langs["en"] == 32
    assert all(s["category"] == "sparse" for s in slots if s["lang"] == "fr")


def test_assignment_second_corpus():
    slots = ic.assignments("second", 40)

    assert len(slots) == 40
    assert {s["lang"] for s in slots} == {"de", "en"}
    assert set(Counter(s["category"] for s in slots)) == set(ic.CATEGORIES)


def test_assignment_is_deterministic_and_seed_dependent():
    assert ic.assignments("main", 120, "a") == ic.assignments("main", 120, "a")
    assert ic.assignments("main", 120, "a") != ic.assignments("main", 120, "b")
    assert ic.assignments("main", 30, "a") == ic.assignments("main", 120, "a")[:30]


def test_assignment_rejects_too_many_entries():
    with pytest.raises(ValueError):
        ic.assignments("second", 41)


def test_split_matches_hashlib_expectation():
    for text, expected in (("Schreib mir eine Einladung zum Sommerfest.", "dev"),
                           ("Kürze bitte meinen Text über die Öffnungszeiten.", "holdout")):
        first_byte = hashlib.sha256(text.encode("utf-8")).digest()[0]
        assert ic.split(text) == ("dev" if first_byte % 10 < 7 else "holdout") == expected


def test_generate_writes_valid_entries(tmp_path):
    out = tmp_path / "corpus.jsonl"
    runner = FakeRunner([_reply_for_prompt()] * 20)

    summary = ic.generate(out, 6, 4, runner=runner)

    entries = _read(out)
    assert summary["written"] == 10 and summary["failed"] == 0
    assert [e["id"] for e in entries] == [f"main-{i:03d}" for i in range(1, 7)] + [f"second-{i:03d}" for i in range(1, 5)]
    assert all(e["generator_model"] == "sonnet" for e in entries)
    assert all(c["cmd"][c["cmd"].index("--model") + 1] == "sonnet" for c in runner.calls)
    slots = ic.assignments("main", 6)
    assert [(e["category"], e["lang"]) for e in entries[:6]] == [(s["category"], s["lang"]) for s in slots]
    assert all(set(e) == {"id", "corpus", "category", "lang", "request", "files", "profile", "intent", "generator_model"}
               for e in entries)
    assert "labels" not in entries[0]


def test_generate_keeps_umlauts_unescaped(tmp_path):
    out = tmp_path / "corpus.jsonl"

    def reply(cmd, prompt, cwd):
        return claude_json(_generated(request="Bitte schön, für Käthe.", files=_revision_file(), profile=_profile()))

    ic.generate(out, 1, 0, runner=FakeRunner([reply] * 3))

    raw = out.read_text(encoding="utf-8")
    assert "Bitte schön, für Käthe." in raw and "\\u" not in raw


def test_generate_resumes_without_duplicates(tmp_path):
    out = tmp_path / "corpus.jsonl"
    ic.generate(out, 5, 0, max_calls=2, runner=FakeRunner([_reply_for_prompt()] * 2))
    assert len(_read(out)) == 2

    runner = FakeRunner([_reply_for_prompt()] * 10)
    summary = ic.generate(out, 5, 0, runner=runner)

    ids = [e["id"] for e in _read(out)]
    assert ids == [f"main-{i:03d}" for i in range(1, 6)]
    assert summary["skipped"] == 2 and len(runner.calls) == 3


def test_generate_max_calls_stops(tmp_path):
    out = tmp_path / "corpus.jsonl"
    runner = FakeRunner([_reply_for_prompt()] * 10)

    summary = ic.generate(out, 10, 0, max_calls=3, runner=runner)

    assert len(runner.calls) == 3
    assert len(_read(out)) == 3
    assert summary["stopped_by_max_calls"] is True


def test_generate_invalid_reply_is_not_written_and_retried_later(tmp_path):
    out = tmp_path / "corpus.jsonl"
    bad = claude_json(json.dumps({"request": "", "intent": "x", "files": [], "profile": None}))

    summary = ic.generate(out, 1, 0, runner=FakeRunner([bad, bad]))

    assert summary["failed"] == 1 and summary["written"] == 0
    assert not out.exists() or _read(out) == []
    ic.generate(out, 1, 0, runner=FakeRunner([_reply_for_prompt()]))
    assert [e["id"] for e in _read(out)] == ["main-001"]


@pytest.mark.parametrize("category, reply, message", [
    ("revision", _generated(), "Datei"),
    ("revision", _generated(files=_revision_file(10)), "Wörter"),
    ("profile_covered", _generated(), "Profil"),
    ("sparse", _generated(files=[{"name": "a", "kind": "odt", "text": "x", "pages": None}]), "kind"),
    ("sparse", _generated(profile={"formality": "wild"}), "formality"),
    ("sparse", _generated(profile={"unbekannt": "x"}), "Profilfelder"),
])
def test_parse_generated_rejects(category, reply, message):
    with pytest.raises(ValueError, match=message):
        ic.parse_generated(json.loads(reply), category)


def test_parse_generated_normalizes_profile():
    parsed = ic.parse_generated(json.loads(_generated(profile={"display_name": "Mara", "formality": "unset"})), "sparse")

    assert set(parsed["profile"]) == set(ic.PROFILE_FIELDS)
    assert parsed["profile"]["signature"] is None


def test_generate_stops_cleanly_on_limit(tmp_path):
    out = tmp_path / "corpus.jsonl"
    runner = FakeRunner([claude_json("You've hit your usage limit · resets 5pm", is_error=True)])

    summary = ic.generate(out, 5, 0, runner=runner)

    assert summary["stopped_by_limit"] is True
    assert len(runner.calls) == 1


def test_label_writes_labels_back_atomically(tmp_path):
    path = tmp_path / "corpus.jsonl"
    _seed_corpus(path, 2)
    runner = FakeRunner([json.dumps(LABELS)] * 2)

    summary = ic.label(path, runner=runner)

    entries = _read(path)
    assert summary["labeled"] == 2
    assert entries[0]["labels"] == LABELS["labels"]
    assert entries[0]["label_note"] == LABELS["note"] and entries[0]["label_model"] == "opus"
    assert all(c["cmd"][c["cmd"].index("--model") + 1] == "opus" for c in runner.calls)
    assert list(tmp_path.glob("*.tmp")) == []
    assert "ä" in path.read_text(encoding="utf-8")


def test_label_prompt_hides_generator_internals(tmp_path):
    path = tmp_path / "corpus.jsonl"
    _seed_corpus(path, 1)
    runner = FakeRunner([json.dumps(LABELS)])

    ic.label(path, runner=runner)

    prompt = runner.calls[0]["prompt"]
    assert "Auftrag 1" in prompt and "alles" in prompt
    for secret in ("main-001", "generator_model", "sonnet", "sparse"):
        assert secret not in prompt


def test_label_skips_labeled_entries_and_resumes(tmp_path):
    path = tmp_path / "corpus.jsonl"
    _seed_corpus(path, 3)
    ic.label(path, max_calls=1, runner=FakeRunner([json.dumps(LABELS)]))
    assert [("labels" in e) for e in _read(path)] == [True, False, False]

    runner = FakeRunner([json.dumps(LABELS)] * 5)
    summary = ic.label(path, runner=runner)

    assert len(runner.calls) == 2 and summary["skipped"] == 1
    assert all("labels" in e for e in _read(path))


def test_label_max_calls_stops(tmp_path):
    path = tmp_path / "corpus.jsonl"
    _seed_corpus(path, 4)
    runner = FakeRunner([json.dumps(LABELS)] * 4)

    summary = ic.label(path, max_calls=2, runner=runner)

    assert len(runner.calls) == 2 and summary["stopped_by_max_calls"] is True


@pytest.mark.parametrize("labels", [
    {"labels": {"wetter": "necessary_ask"}, "note": "x"},
    {"labels": {"length": "must_ask"}, "note": "x"},
    {"labels": {}, "note": "x"},
    {"labels": {"length": "necessary_ask"}, "note": ""},
])
def test_label_rejects_invalid_replies(tmp_path, labels):
    path = tmp_path / "corpus.jsonl"
    _seed_corpus(path, 1)
    bad = json.dumps(labels)

    summary = ic.label(path, runner=FakeRunner([bad, bad]))

    assert summary["failed"] == 1
    assert "labels" not in _read(path)[0]


def test_label_stops_cleanly_on_limit_and_keeps_file(tmp_path):
    path = tmp_path / "corpus.jsonl"
    before = _seed_corpus(path, 2)
    runner = FakeRunner([json.dumps(LABELS), CliResult(json.dumps({"result": "You've hit your usage limit", "is_error": True}),
                                                       "", 1, False, 1.0)])

    summary = ic.label(path, runner=runner)

    entries = _read(path)
    assert summary["stopped_by_limit"] is True and summary["labeled"] == 1
    assert "labels" in entries[0] and "labels" not in entries[1]
    assert entries[1] == before[1]


def test_main_generate_label_and_stats(tmp_path, capsys):
    out = tmp_path / "corpus.jsonl"

    code = ic.main(["generate", "--out", str(out), "--main", "3", "--second", "0"], runner=FakeRunner([_reply_for_prompt()] * 3))
    assert code == 0
    code = ic.main(["label", "--corpus", str(out), "--max-calls", "1"], runner=FakeRunner([json.dumps(LABELS)]))
    assert code == 4
    assert ic.main(["stats", "--corpus", str(out)]) == 0

    text = capsys.readouterr().out
    assert "corpus: main=3" in text and "beschriftet=1" in text and "offen=2" in text
    assert "split:" in text

    code = ic.main(["label", "--corpus", str(out)], runner=FakeRunner([json.dumps(LABELS)] * 2))
    assert code == 0
    assert ic.main(["stats", "--corpus", str(out)]) == 0
    assert "beschriftet=3" in capsys.readouterr().out


def test_main_generate_ends_with_4_while_entries_are_missing(tmp_path, capsys):
    out = tmp_path / "corpus.jsonl"
    bad = claude_json(json.dumps({"request": "", "intent": "x", "files": [], "profile": None}))

    assert ic.main(["generate", "--out", str(out), "--main", "3", "--second", "0", "--max-calls", "2"],
                   runner=FakeRunner([_reply_for_prompt()] * 2)) == 4
    assert ic.main(["generate", "--out", str(out), "--main", "3", "--second", "0"], runner=FakeRunner([bad, bad])) == 4
    assert "1 Einträge fehlen" in capsys.readouterr().err
    assert ic.main(["generate", "--out", str(out), "--main", "3", "--second", "0"], runner=FakeRunner([_reply_for_prompt()])) == 0


def test_main_label_ends_with_4_when_a_reply_is_rejected(tmp_path):
    path = tmp_path / "corpus.jsonl"
    _seed_corpus(path, count=2)
    bad = json.dumps({"labels": {"length": "maybe"}, "note": "x"})

    assert ic.main(["label", "--corpus", str(path)], runner=FakeRunner([json.dumps(LABELS), bad, bad])) == 4
    assert ic.main(["label", "--corpus", str(path)], runner=FakeRunner([json.dumps(LABELS)])) == 0


def test_main_returns_3_on_limit(tmp_path):
    out = tmp_path / "corpus.jsonl"
    runner = FakeRunner([claude_json("You've hit your usage limit", is_error=True)])

    assert ic.main(["generate", "--out", str(out), "--main", "2", "--second", "0"], runner=runner) == 3


def test_main_rejects_too_large_count(tmp_path):
    with pytest.raises(SystemExit):
        ic.main(["generate", "--out", str(tmp_path / "c.jsonl"), "--main", "500"], runner=FakeRunner())
