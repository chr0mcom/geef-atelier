import json

import pytest

from conftest import FakeRunner, verdict_json
from geef_eval.cli_runner import LimitError
from geef_eval.judge import (
    AssignmentConflict, ClaudeJudge, CodexBusyError, CodexJudge, JudgeJob, assignment, classify, default_judges,
    judge_all, judge_prompt, order_for_pass, outcomes, pair_key, run_job, validate_verdict,
)
from geef_eval.store import RunStore
from geef_eval.tasks import Task

TASKS = [Task(id=f"t{i}", brief=f"Kurz {i}", original_brief=f"Original {i}") for i in range(1, 9)]


def write_output(store, task_id, arm, text):
    store.write_json(store.output_path(task_id, arm), {"ok": True, "text": text, "duration_s": 1.0,
                                                       "metrics": {"words": len(text.split()), "placeholders": 0}})


class ScriptedJudge:
    """Prefers the text that contains the word 'gut'; records prompts."""

    def __init__(self, name="fake", fail_with=None):
        self.name = name
        self.prompts = []
        self.fail_with = fail_with

    def verdict(self, prompt, label):
        self.prompts.append(prompt)
        if self.fail_with:
            raise self.fail_with
        first = "1" if "<text_1>\ngut" in prompt else "2"
        return json.loads(verdict_json(first, fabricated={"1": [], "2": ["erfunden"]} if first == "1" else {"1": ["erfunden"], "2": []})), 0.5


def test_assignment_is_deterministic_varies_and_pass_two_swaps():
    key = pair_key("roh", "atelier")
    orders = {assignment("seed", t.id, key, "j") for t in TASKS}

    assert assignment("seed", "t1", key, "j") == assignment("seed", "t1", key, "j")
    assert len(orders) == 2
    first = order_for_pass("seed", "t1", key, "j", 1)
    assert order_for_pass("seed", "t1", key, "j", 2) == (first[1], first[0])
    assert key == "atelier__vs__roh"


def test_judge_prompt_is_blind_and_uses_the_original_brief():
    task = Task(id="t", brief="KURZ", original_brief="ORIGINAL")

    prompt = judge_prompt(task, ["Text eins", "Text zwei"])

    assert "<original_briefing>" in prompt and "ORIGINAL" in prompt and "KURZ" in prompt
    assert "<text_1>\nText eins\n</text_1>" in prompt and "<text_2>\nText zwei\n</text_2>" in prompt
    assert "roh" not in prompt and "atelier" not in prompt
    assert '"rangfolge"' in prompt and '"erfunden"' in prompt


def test_validate_verdict():
    validate_verdict(json.loads(verdict_json("2")))
    with pytest.raises(ValueError):
        validate_verdict({"rangfolge": ["1"], "abstand": "klar"})
    with pytest.raises(ValueError):
        validate_verdict({"rangfolge": ["1", "2"], "abstand": "riesig"})


@pytest.mark.parametrize("w1, w2, expected", [
    ("a", "a", "sieg_a"), ("b", "b", "sieg_b"), ("gleich", "gleich", "gleich"),
    ("a", "b", "uneinig"), ("a", "gleich", "tendenz_a"), ("gleich", "b", "tendenz_b"),
])
def test_classify(w1, w2, expected):
    assert classify(w1, w2, "a", "b") == expected


def test_judge_all_two_passes_maps_labels_back_to_arms_and_logs_assignment_separately(tmp_path):
    store = RunStore(tmp_path)
    for task in TASKS[:3]:
        write_output(store, task.id, "roh", "gut und klar")
        write_output(store, task.id, "atelier", "schwach")
    judge = ScriptedJudge()

    summary = judge_all(TASKS[:3], ["roh", "atelier"], store, [judge], "seed", echo=lambda _: None)

    assert summary["done"] == 6
    results = outcomes(TASKS[:3], ["roh", "atelier"], store, ["fake"])
    assert [o.outcome for o in results] == ["sieg_b"] * 3
    assert all(o.winners == ("roh", "roh") for o in results)
    assert all(o.fabricated == {"atelier": 1.0, "roh": 0.0} for o in results)
    assigned = store.read_json(store.assignment_path("t1", "atelier__vs__roh", "fake"))
    assert set(assigned) == {"seed", "version", "pass1", "pass2"}
    verdict_file = store.read_json(store.judgment_path("t1", "atelier__vs__roh", "fake", 1))
    assert "roh" not in json.dumps(verdict_file) and "atelier" not in json.dumps(verdict_file)


def test_judge_all_resumes_and_respects_the_call_budget(tmp_path):
    store = RunStore(tmp_path)
    for task in TASKS[:3]:
        write_output(store, task.id, "roh", "gut")
        write_output(store, task.id, "atelier", "schwach")
    first = ScriptedJudge()

    summary = judge_all(TASKS[:3], ["roh", "atelier"], store, [first], "seed", max_calls=4, echo=lambda _: None)
    assert (summary["done"], summary["skipped_by_budget"]) == (4, 2)

    second = ScriptedJudge()
    judge_all(TASKS[:3], ["roh", "atelier"], store, [second], "seed", echo=lambda _: None)
    assert len(second.prompts) == 2


def test_missing_or_failed_outputs_are_not_judged(tmp_path):
    store = RunStore(tmp_path)
    write_output(store, "t1", "roh", "gut")
    store.write_json(store.output_path("t1", "atelier"), {"ok": False, "error": "x"})
    judge = ScriptedJudge()

    summary = judge_all(TASKS[:1], ["roh", "atelier"], store, [judge], "seed", echo=lambda _: None)

    assert summary["done"] == 0 and judge.prompts == []


def test_limit_stops_judging(tmp_path):
    store = RunStore(tmp_path)
    write_output(store, "t1", "roh", "gut")
    write_output(store, "t1", "atelier", "schwach")

    summary = judge_all(TASKS[:1], ["roh", "atelier"], store, [ScriptedJudge(fail_with=LimitError("limit"))], "seed",
                        echo=lambda _: None)

    assert summary["stopped_by_limit"] is True


def test_claude_judge_uses_sonnet_and_validates():
    runner = FakeRunner(['{"rangfolge": ["1"], "abstand": "klar"}', verdict_json("2")])

    obj, _ = ClaudeJudge(runner=runner).verdict("PROMPT", "label")

    assert obj["rangfolge"] == ["2", "1"]
    assert runner.calls[0]["cmd"][runner.calls[0]["cmd"].index("--model") + 1] == "sonnet"
    assert len(runner.calls) == 2


def test_codex_judge_refuses_while_codex_runs_and_at_the_cap(tmp_path):
    store = RunStore(tmp_path)
    runner = FakeRunner()
    busy = CodexJudge(store, 2, runner=runner, pgrep=lambda: "4711 codex exec --foo\n")
    with pytest.raises(CodexBusyError, match="belegt"):
        busy.verdict("p", "l")

    idle = CodexJudge(store, 1, runner=runner, pgrep=lambda: "99 node codex app-server\n")
    store.write_text(store.counter_path("codex"), "1")
    with pytest.raises(CodexBusyError, match="Obergrenze"):
        idle.verdict("p", "l")
    assert runner.calls == []


def test_codex_judge_is_off_by_default(tmp_path, monkeypatch):
    store = RunStore(tmp_path)
    assert [j.name for j in default_judges(store)] == ["claude-sonnet"]

    monkeypatch.delenv("EVAL_CODEX_MAX", raising=False)
    with pytest.raises(ValueError, match="EVAL_CODEX_MAX"):
        default_judges(store, codex=True)


def _seed_that_swaps_pass_one(task_id, key, judge, base="seed"):
    original = assignment(base, task_id, key, judge)
    for i in range(100):
        candidate = f"anders-{i}"
        if assignment(candidate, task_id, key, judge) != original:
            return candidate
    raise AssertionError("kein Seed mit vertauschter Reihenfolge gefunden")


def test_resume_with_other_seed_never_rewrites_the_assignment(tmp_path):
    store = RunStore(tmp_path)
    write_output(store, "t1", "roh", "gut")
    write_output(store, "t1", "atelier", "schwach")
    key = pair_key("roh", "atelier")
    judge_all(TASKS[:1], ["roh", "atelier"], store, [ScriptedJudge()], "seed", max_calls=1, echo=lambda _: None)
    path = store.assignment_path("t1", key, "fake")
    before = path.read_text(encoding="utf-8")
    other = _seed_that_swaps_pass_one("t1", key, "fake")
    late = ScriptedJudge()

    with pytest.raises(AssignmentConflict, match="Seed 'seed'"):
        judge_all(TASKS[:1], ["roh", "atelier"], store, [late], other, echo=lambda _: None)

    assert late.prompts == []
    assert path.read_text(encoding="utf-8") == before
    assert store.read_json(store.judgment_path("t1", key, "fake", 2)) is None


def test_run_job_refuses_a_foreign_assignment(tmp_path):
    store = RunStore(tmp_path)
    write_output(store, "t1", "roh", "gut")
    write_output(store, "t1", "atelier", "schwach")
    key = pair_key("roh", "atelier")
    store.write_json(store.assignment_path("t1", key, "fake"), {"seed": "seed", "version": 99,
                     "pass1": {"text_1": "roh", "text_2": "atelier"}, "pass2": {"text_1": "atelier", "text_2": "roh"}})
    judge = ScriptedJudge()

    with pytest.raises(AssignmentConflict, match="Version 99"):
        run_job(JudgeJob(TASKS[0], key, judge, 2), store, "seed")

    assert judge.prompts == []


def test_resume_with_same_seed_keeps_assignment_and_completes(tmp_path):
    store = RunStore(tmp_path)
    write_output(store, "t1", "roh", "gut")
    write_output(store, "t1", "atelier", "schwach")
    judge_all(TASKS[:1], ["roh", "atelier"], store, [ScriptedJudge()], "seed", max_calls=1, echo=lambda _: None)
    summary = judge_all(TASKS[:1], ["roh", "atelier"], store, [ScriptedJudge()], "seed", echo=lambda _: None)

    assert summary["done"] == 1
    assert [o.winners for o in outcomes(TASKS[:1], ["roh", "atelier"], store, ["fake"])] == [("roh", "roh")]
