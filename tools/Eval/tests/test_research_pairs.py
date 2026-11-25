import json
from pathlib import Path

import pytest

from conftest import claude_json
from geef_eval import research_pairs as rp

CORPUS = Path(__file__).resolve().parents[3] / "tests" / "Geef.Atelier.Tests" / "Intake" / "Corpus"
SETS = ["--intake-results", str(CORPUS / "results-20261125.jsonl"),
        "--corpus", str(CORPUS / "intake-corpus.jsonl"), "--corpus", str(CORPUS / "intake-corpus-second.jsonl")]
PASSAGE = {"quote": "Der Mindestlohn beträgt ab Januar 14,60 Euro je Stunde.", "url": "https://wages.example/a", "title": "Lohn",
           "retrievedAt": "2026-11-28T00:00:00+00:00"}
TOKEN_ON, TOKEN_OFF = "token-on-secret", "token-off-secret"


def orders():
    return rp.load_orders(CORPUS / "results-20261125.jsonl", [CORPUS / "intake-corpus.jsonl", CORPUS / "intake-corpus-second.jsonl"])


class FakeAtelier:
    """The four order tools of one measuring user; ``research`` decides what ``lookedUp`` says."""

    def __init__(self, arm, research=True, fact_state="not_checked", questions=0, fail=(), state="delivered"):
        self.arm, self.research, self.fact_state, self.questions, self.fail, self.state = arm, research, fact_state, questions, set(fail), state
        self.calls = []
        self._orders = {}

    def call_tool(self, name, arguments):
        self.calls.append((name, arguments))
        if name == "submit_order":
            order_id = f"{self.arm}-{len(self._orders) + 1}"
            self._orders[order_id] = arguments["request"]
            if any(f in arguments["request"] for f in self.fail):
                raise RuntimeError("HTTP 502")
            if self.questions:
                return {"orderId": order_id, "state": "needs_input", "questions": [{"id": "length"}] * self.questions}
            return {"orderId": order_id, "state": "working"}
        if name == "answer_order":
            assert arguments["skip"] is True
            return {"state": "queued"}
        if name == "get_order_status":
            return {"done": True, "state": self.state}
        if name == "get_order_result":
            looked = {"status": "found", "note": None, "withheld": 0, "entries": [PASSAGE]} if self.research else None
            return {"state": self.state, "text": f"Text von {self.arm}", "wordCount": 3, "lookedUp": looked,
                    "factCheck": {"state": self.fact_state, "filesCompared": False}}
        raise AssertionError(name)


def no_codex():
    return ""


@pytest.fixture
def lock(tmp_path):
    return tmp_path / "codex-gesperrt"


def run_all(tmp_path, lock, on=None, off=None, max_submits=16, pgrep=no_codex):
    tools = {"on": on or FakeAtelier("on"), "off": off or FakeAtelier("off", research=False)}
    out = []
    code = rp.main(["run", *SETS, "--out-dir", str(tmp_path / "r3"), "--max-submits", str(max_submits), "--codex-lock", str(lock),
                    "--allow-in-repo"], tools=tools, pgrep=pgrep, echo=out.append)
    return code, tools, out


def verdict(first="1", margin="klar", fabricated=None, placeholders=None):
    second = "2" if first == "1" else "1"
    return json.dumps({"rangfolge": [first, second], "abstand": margin, "verstoesse": {"1": [], "2": []},
                       "erfunden": fabricated or {"1": [], "2": []}, "platzhalter": placeholders or {"1": [], "2": []},
                       "begruendung": "kurz"})


def test_the_pair_set_is_eight_reserved_orders_without_profile_or_revision():
    chosen = orders()

    assert tuple(o.id for o in chosen) == rp.PAIR_IDS
    assert len(rp.RESERVED_IDS) == 15 and set(rp.PAIR_IDS) <= set(rp.RESERVED_IDS)
    corpus = {json.loads(line)["id"]: json.loads(line) for name in ("intake-corpus.jsonl", "intake-corpus-second.jsonl")
              for line in (CORPUS / name).read_text(encoding="utf-8").splitlines() if line.strip()}
    left_out = set(rp.RESERVED_IDS) - set(rp.PAIR_IDS)
    assert {"main-061", "main-066", "second-035"} <= left_out and all(corpus[i]["profile"] for i in ("main-061", "main-066", "second-035"))
    assert corpus["second-009"]["category"] == "revision"
    assert all(o.topics and o.intent for o in chosen)
    vr = {"main-004", "main-014", "main-016", "main-025", "main-029", "main-032", "main-033", "main-036", "main-038", "main-040",
          "main-041", "main-059", "main-044", "main-046", "main-079", "main-086", "main-107", "main-116", "second-004", "second-026", "second-038"}
    assert not vr & set(rp.RESERVED_IDS)


def test_other_data_never_give_another_set():
    results = [json.loads(line) for line in (CORPUS / "results-20261125.jsonl").read_text(encoding="utf-8").splitlines() if line.strip()]
    corpus = [json.loads(line) for name in ("intake-corpus.jsonl", "intake-corpus-second.jsonl")
              for line in (CORPUS / name).read_text(encoding="utf-8").splitlines() if line.strip()]
    shifted = [dict(r, research_topics=[]) if r["id"] == "main-061" else r for r in results]

    with pytest.raises(ValueError):
        rp.select(shifted, corpus)


def test_run_submits_each_order_once_per_arm_skips_questions_and_never_writes_tokens(tmp_path, lock, monkeypatch):
    on = FakeAtelier("on", questions=2)
    code, tools, out = run_all(tmp_path, lock, on=on)

    assert code == 0, out
    outputs = sorted((tmp_path / "r3" / "outputs").rglob("*.json"))
    assert len(outputs) == 16
    assert [n for n, _ in on.calls].count("submit_order") == 8 and [n for n, _ in on.calls].count("answer_order") == 8
    line = json.loads((tmp_path / "r3" / "outputs" / "main-063" / "on.json").read_text(encoding="utf-8"))
    assert line["ok"] and line["questions_skipped"] == 2 and line["looked_up"]["status"] == "found"
    first_arms = [next(a["request"] for n, a in t.calls if n == "submit_order") for t in tools.values()]
    assert first_arms[0] == first_arms[1]

    again, tools, _ = run_all(tmp_path, lock)
    assert again == 0 and all(not t.calls for t in tools.values())
    written = "".join(p.read_text(encoding="utf-8") for p in (tmp_path / "r3").rglob("*") if p.is_file())
    assert TOKEN_ON not in written and TOKEN_OFF not in written


def test_tokens_come_only_from_the_environment_and_must_differ(tmp_path, lock, monkeypatch):
    out = []
    monkeypatch.setenv("EVAL_ATELIER_URL", "https://atelier.example")
    monkeypatch.setenv("EVAL_R3_TOKEN_ON", TOKEN_ON)
    monkeypatch.setenv("EVAL_R3_TOKEN_OFF", TOKEN_ON)
    code = rp.main(["run", *SETS, "--out-dir", str(tmp_path / "r3"), "--max-submits", "16", "--codex-lock", str(lock), "--allow-in-repo"],
                   pgrep=no_codex, echo=out.append)
    assert code == 2 and "gleich" in out[-1] and TOKEN_ON not in "".join(out)

    monkeypatch.delenv("EVAL_R3_TOKEN_OFF")
    assert rp.main(["run", *SETS, "--out-dir", str(tmp_path / "r3"), "--max-submits", "16", "--codex-lock", str(lock), "--allow-in-repo"],
                   pgrep=no_codex, echo=out.append) == 2


def test_a_wrong_arm_configuration_stops_the_run_and_makes_the_gate_not_evaluable(tmp_path, lock):
    code, _, out = run_all(tmp_path, lock, off=FakeAtelier("off", research=True))
    assert code == 2 and "Arm off hat nachgeschlagen" in out[-1]
    assert not rp.evaluate(orders(), rp.PairStore(tmp_path / "r3"))["verdict"] == "passed"

    code, _, out = run_all(tmp_path / "b", lock, on=FakeAtelier("on", fact_state="checked"))
    assert code == 2 and "Faktenprüfung lief" in out[-1]


def test_caps_codex_guard_and_early_technical_failure_stop_the_run(tmp_path, lock):
    code, tools, out = run_all(tmp_path, lock, max_submits=3)
    assert code == 4 and sum(1 for t in tools.values() for n, _ in t.calls if n == "submit_order") == 3

    lock.write_text("x", encoding="utf-8")
    code, tools, out = run_all(tmp_path / "b", lock)
    assert code == 3 and all(not t.calls for t in tools.values())
    lock.unlink()
    code, tools, out = run_all(tmp_path / "c", lock, pgrep=lambda: "123 codex exec --json\n")
    assert code == 3 and all(not t.calls for t in tools.values())

    first = orders()[1].request[:20]
    code, tools, out = run_all(tmp_path / "d", lock, off=FakeAtelier("off", research=False, fail=[first]))
    assert code == 4 and "ersten drei Paaren" in out[-1]
    assert sum(1 for t in tools.values() for n, _ in t.calls if n == "submit_order") == 4


def judged(tmp_path, lock, replies, **arms):
    code, _, out = run_all(tmp_path, lock, **arms)
    assert code == 0, out
    runner_calls = []

    def runner(cmd, prompt, cwd, timeout_s):
        runner_calls.append(prompt)
        reply = replies(len(runner_calls), prompt)
        return claude_json(reply)

    out = []
    code = rp.main(["judge", *SETS, "--out-dir", str(tmp_path / "r3"), "--max-judge-calls", "32", "--codex-lock", str(lock),
                    "--allow-in-repo"], runner=runner, pgrep=no_codex, echo=out.append)
    return code, runner_calls, out


def on_label(prompt):
    return "1" if prompt.index("Text von on") < prompt.index("Text von off") else "2"


def test_the_juror_is_blind_sees_the_passages_and_swaps_the_order(tmp_path, lock):
    code, prompts, out = judged(tmp_path, lock, lambda n, prompt: verdict(on_label(prompt)))

    assert code == 0, out
    assert len(prompts) == 16
    for prompt in prompts:
        assert PASSAGE["quote"] in prompt and "<recherchierbare_themen>" in prompt
    pairs = [prompts[i:i + 2] for i in range(0, 16, 2)]
    assert all(on_label(a) != on_label(b) for a, b in pairs)

    def without_texts(prompt):
        return prompt.replace("Text von on", "T").replace("Text von off", "T")

    assert all(without_texts(a) == without_texts(b) for a, b in pairs)


def test_research_that_wins_with_fewer_placeholders_passes_the_gate(tmp_path, lock):
    def reply(n, prompt):
        on = on_label(prompt)
        off = "2" if on == "1" else "1"
        return verdict(on, placeholders={off: ["[Betrag]"], on: []})

    judged(tmp_path, lock, reply)
    out = []
    code = rp.main(["evaluate", *SETS, "--out-dir", str(tmp_path / "r3"), "--allow-in-repo"], echo=out.append)

    report = json.loads((tmp_path / "r3" / "r3-gate.json").read_text(encoding="utf-8"))
    assert code == 0 and report["verdict"] == "passed", out
    assert report["parts"]["wins"] == {"passed": True, "on": 8, "off": 0}
    assert report["parts"]["placeholders"] == {"passed": True, "on": 0, "off": 8}
    assert report["valid_pairs"] == 8 and report["research_seen"] == 8


def test_disagreeing_passes_are_no_wins_and_more_inventions_fail_the_gate(tmp_path, lock):
    def reply(n, prompt):
        on = on_label(prompt)
        return verdict("1", fabricated={on: ["14,60 Euro"]})

    judged(tmp_path, lock, reply)
    report = rp.evaluate(orders(), rp.PairStore(tmp_path / "r3"))

    assert report["verdict"] == "failed"
    assert report["outcomes"]["uneinig"] == 8 and report["parts"]["wins"]["on"] == 0
    assert report["parts"]["fabricated"] == {"passed": False, "on": 8, "off": 0}
    assert report["parts"]["placeholders"]["passed"] is False


def test_without_enough_pairs_or_visible_research_the_gate_is_not_evaluable(tmp_path, lock):
    store = rp.PairStore(tmp_path / "empty")
    assert rp.evaluate(orders(), store)["verdict"] == "not_evaluable"
    assert rp.main(["evaluate", *SETS, "--out-dir", str(tmp_path / "empty"), "--allow-in-repo"], echo=lambda _: None) == 4

    judged(tmp_path, lock, lambda n, prompt: verdict(on_label(prompt)), on=FakeAtelier("on", research=False))
    report = rp.evaluate(orders(), rp.PairStore(tmp_path / "r3"))
    assert report["verdict"] == "not_evaluable"
    assert any("keinem Ergebnis eine Recherche" in p for p in report["problems"])


def test_an_invalid_verdict_is_not_stored_and_a_stray_output_spoils_the_gate(tmp_path, lock):
    code, prompts, out = judged(tmp_path, lock, lambda n, prompt: json.dumps({"rangfolge": ["1", "2"], "abstand": "klar"}))
    assert code == 4
    assert not list((tmp_path / "r3").glob("judgments/**/*.json"))

    store = rp.PairStore(tmp_path / "r3")
    store.write_json(store.output_path("main-004", "on"), {"id": "main-004", "arm": "on", "ok": True})
    assert any("außerhalb von R3" in p for p in rp.evaluate(orders(), store)["problems"])


def test_results_inside_a_git_tree_are_refused(tmp_path):
    inside = Path(__file__).resolve().parent / "r3-out"
    assert rp.main(["evaluate", *SETS, "--out-dir", str(inside)], echo=lambda _: None) == 2
    assert not inside.exists()
