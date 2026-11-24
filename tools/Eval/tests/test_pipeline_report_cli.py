import json

import pytest

from geef_eval import cli
from geef_eval.arms import ArmOutput, ReferenceArm
from geef_eval.pipeline import produce_outputs
from geef_eval.report import build_report
from geef_eval.store import RunStore
from geef_eval.tasks import load_tasks
from test_judge import ScriptedJudge


class CountingArm:
    def __init__(self, name="zaehler", fail_first=False):
        self.name = name
        self.calls = 0
        self.fail_first = fail_first

    def produce(self, task):
        self.calls += 1
        if self.fail_first and self.calls == 1:
            raise RuntimeError("vorübergehend")
        return ArmOutput(f"gut [Platzhalter] {task.id}", 2.0)


def test_outputs_are_resumable_and_failures_retried(tmp_path, examples_dir):
    tasks = load_tasks(examples_dir)
    store = RunStore(tmp_path)
    arm = CountingArm(fail_first=True)

    first = produce_outputs(tasks, [arm, ReferenceArm()], store, echo=lambda _: None)
    second = produce_outputs(tasks, [arm, ReferenceArm()], store, echo=lambda _: None)

    assert (first["done"], first["failed"]) == (5, 1)
    assert (second["done"], second["skipped_existing"]) == (1, 5)
    assert arm.calls == 4
    saved = store.read_json(store.output_path(tasks[0].id, "zaehler"))
    assert saved["metrics"]["placeholders"] == 1
    assert saved["metrics"]["length_rule"] == {"min_words": None, "max_words": 120}


def test_report_has_tables_but_no_texts_or_reasons(tmp_path, examples_dir):
    tasks = load_tasks(examples_dir)
    store = RunStore(tmp_path)
    produce_outputs(tasks, [CountingArm(), ReferenceArm()], store, echo=lambda _: None)
    from geef_eval.judge import judge_all

    class ReasoningJudge(ScriptedJudge):
        def verdict(self, prompt, label):
            obj, duration = super().verdict(prompt, label)
            obj["begruendung"] = "GEHEIME BEGRÜNDUNG"
            return obj, duration

    judge_all(tasks, ["zaehler", "referenz"], store, [ReasoningJudge()], "seed", echo=lambda _: None)

    report = build_report("probe", tasks, ["zaehler", "referenz"], ["fake"], store, "seed")

    assert "## Aggregat je Armpaar" in report
    assert "| referenz ↔ zaehler | fake | 3 |" in report
    assert "GEHEIME BEGRÜNDUNG" not in report
    assert tasks[0].reference_text.splitlines()[0] not in report
    assert "Längenvorgabe" in report and "≤ 120" in report


def test_cli_all_runs_offline_with_reference_and_plugin_arm(tmp_path, examples_dir, monkeypatch):
    plugin = tmp_path / "plugin_arm.py"
    plugin.write_text(
        "from geef_eval.arms import ArmOutput\n"
        "def make(name):\n"
        "    class A:\n"
        "        pass\n"
        "    a = A()\n"
        "    a.name = name\n"
        "    a.produce = lambda task: ArmOutput('gut ' + task.id, 1.0)\n"
        "    return a\n",
        encoding="utf-8")
    monkeypatch.setattr(cli, "default_judges", lambda store, log=None, codex=False: [ScriptedJudge()])
    out = tmp_path / "out"

    code = cli.main(["all", "--run-name", "probe", "--tasks", str(examples_dir), "--arms", "referenz,eigen",
                     "--plugin", f"eigen={plugin}:make", "--out", str(out), "--max-judge-calls", "6"])

    assert code == 0
    run_dir = out / "probe"
    report = (run_dir / "bericht.md").read_text(encoding="utf-8")
    assert "| eigen ↔ referenz | fake | 3 |" in report
    manifest = json.loads((run_dir / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["arms"] == ["referenz", "eigen"]


def test_cli_refuses_output_inside_a_git_repository(examples_dir, tmp_path):
    repo_dir = cli.Path(__file__).resolve().parent / "_kein_lauf"
    with pytest.raises(SystemExit, match="Git"):
        cli.main(["report", "--run-name", "x", "--tasks", str(examples_dir), "--arms", "referenz",
                  "--out", str(repo_dir)])
    assert not (repo_dir / "x").exists()


def _cli_judge(tmp_path, examples_dir, monkeypatch, *extra):
    monkeypatch.setattr(cli, "default_judges", lambda store, log=None, codex=False: [ScriptedJudge()])
    return cli.main(["all", "--run-name", "fortsetzen", "--tasks", str(examples_dir), "--arms", "referenz",
                     "--out", str(tmp_path / "out"), *extra])


def test_cli_stores_seed_and_refuses_resume_with_another(tmp_path, examples_dir, monkeypatch):
    monkeypatch.delenv("EVAL_SEED", raising=False)
    assert _cli_judge(tmp_path, examples_dir, monkeypatch, "--seed", "erster") == 0
    manifest_path = tmp_path / "out" / "fortsetzen" / "manifest.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    assert (manifest["seed"], manifest["assignment_version"]) == ("erster", 1)

    with pytest.raises(SystemExit, match="Seed 'erster'"):
        _cli_judge(tmp_path, examples_dir, monkeypatch, "--seed", "zweiter")
    monkeypatch.setenv("EVAL_SEED", "zweiter")
    with pytest.raises(SystemExit, match="Seed 'erster'"):
        _cli_judge(tmp_path, examples_dir, monkeypatch)
    assert json.loads(manifest_path.read_text(encoding="utf-8"))["seed"] == "erster"


def test_cli_resume_without_seed_reuses_the_stored_one(tmp_path, examples_dir, monkeypatch):
    monkeypatch.delenv("EVAL_SEED", raising=False)
    assert _cli_judge(tmp_path, examples_dir, monkeypatch, "--seed", "erster") == 0
    assert _cli_judge(tmp_path, examples_dir, monkeypatch) == 0
    report = (tmp_path / "out" / "fortsetzen" / "bericht.md").read_text(encoding="utf-8")
    assert "Seed: `erster`" in report


def test_cli_refuses_run_directory_with_other_assignment_version(tmp_path, examples_dir, monkeypatch):
    run_dir = tmp_path / "out" / "fortsetzen"
    run_dir.mkdir(parents=True)
    (run_dir / "manifest.json").write_text(json.dumps({"seed": "geef-eval", "assignment_version": 0}), encoding="utf-8")
    with pytest.raises(SystemExit, match="Zuordnungsversion 0"):
        _cli_judge(tmp_path, examples_dir, monkeypatch)
