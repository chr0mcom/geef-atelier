"""Blind pairwise judging: random but seeded order, two passes with swapped order, assignment logged separately."""
from __future__ import annotations

import concurrent.futures as cf
import itertools
import os
import random
import shutil
import subprocess
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Protocol

from .claude_call import call_claude, extract_json
from .cli_runner import LimitError, Runner, empty_workdir, run_cli
from .store import RunStore
from .tasks import Task

ASSIGNMENT_VERSION = 1
JUDGE_SYSTEM_PROMPT = "Du bist ein präziser Analyst. Du hast keine Werkzeuge. Antworte exakt im verlangten Format."
ALLOWED_MARGINS = {"klar", "knapp", "gleichwertig"}
TIE = "gleich"


class AssignmentConflict(RuntimeError):
    """A stored text-to-arm assignment was made with another seed or ordering version; judging would mix them."""


def pair_key(arm_a: str, arm_b: str) -> str:
    """Canonical, order-independent key of an arm pair."""
    first, second = sorted((arm_a, arm_b))
    return f"{first}__vs__{second}"


def pair_arms(key: str) -> tuple[str, str]:
    first, _, second = key.partition("__vs__")
    return first, second


def assignment(seed: str, task_id: str, key: str, judge: str) -> tuple[str, str]:
    """Order of the two arms as text 1 and text 2 in pass 1 (pass 2 swaps it). Deterministic per seed."""
    first, second = pair_arms(key)
    rng = random.Random(f"{seed}|{task_id}|{key}|{judge}")
    return (first, second) if rng.random() < 0.5 else (second, first)


def order_for_pass(seed: str, task_id: str, key: str, judge: str, run: int) -> tuple[str, str]:
    first, second = assignment(seed, task_id, key, judge)
    return (first, second) if run == 1 else (second, first)


def _attachments(task: Task) -> str:
    blocks = [
        f'<anhang name="{a.name}">\n{a.text}\n</anhang>' for a in task.attachments if a.text is not None
    ]
    return "\n\n".join(blocks) + "\n\n" if blocks else "(keine)\n\n"


def judge_prompt(task: Task, texts: list[str]) -> str:
    """Generalised juror prompt of the 2026-11-24 measurement (any number of texts, labels 1..n)."""
    labels = [str(i) for i in range(1, len(texts) + 1)]
    if task.original_brief:
        intro = (
            "Du bist ein strenger, unabhängiger Prüfer. Der folgende Auftraggeber hat ein ausführliches Briefing geschrieben; "
            f"{len(texts)} Autoren haben jeweils einen Text dazu geliefert, alle aber nur auf Basis einer verkürzten Fassung. "
            "Beurteile, welcher Text besser erfüllt, was der Auftraggeber WIRKLICH wollte. Du kennst weder Herkunft noch Entstehungsreihenfolge der Texte.\n\n"
            "<original_briefing>\n(das wollte der Auftraggeber wirklich; verbindlich)\n" + task.original_brief + "\n</original_briefing>\n\n"
            "<kurzfassung>\n(nur das bekamen die Autoren)\n" + task.brief + "\n</kurzfassung>\n\n"
        )
    else:
        intro = (
            f"Du bist ein strenger, unabhängiger Prüfer. Unten stehen ein Auftrag und {len(texts)} Texte, die ihn erfüllen sollen. "
            "Du kennst weder Herkunft noch Entstehungsreihenfolge der Texte.\n\n"
            "<briefing>\n" + task.brief + "\n</briefing>\n\n"
        )
    body = "".join(f"<text_{label}>\n{text}\n</text_{label}>\n\n" for label, text in zip(labels, texts))
    per_text = ", ".join(f'"{label}": ["..."]' for label in labels)
    example_order = ", ".join(f'"{label}"' for label in reversed(labels))
    return (
        intro
        + "BEIGELEGTE DATEIEN:\n" + _attachments(task)
        + body
        + "Vorgehen:\n"
        "1. Prüfe zuerst jede harte Vorgabe des Briefings (Länge, Verbote, Format, Sprache, Stil- und Tonregeln, Pflichtinhalte, Gliederung) an jedem Text und nenne die Verstöße je Text knapp.\n"
        "2. Nenne erfundene Tatsachen je Text: Fakten, Zahlen, Namen, Ereignisse, Zitate, Quellen, die weder durch das Briefing noch durch die Anhänge gedeckt sind. "
        "Eckige Platzhalter sind keine Erfindung; sie sind aber ein Verstoß, wenn das Briefing Platzhalter verbietet oder ein fertiger, versandfertiger Text verlangt war.\n"
        "3. Die Länge eines Textes ist kein Qualitätsmerkmal; zählt nur die Einhaltung einer Längenvorgabe des Briefings.\n"
        "4. Ordne die Texte vom besten zum schlechtesten. \"gleichwertig\" nur, wenn sich die beiden besten wirklich nicht unterscheiden.\n"
        "Antworte ausschließlich als JSON, ohne weiteren Text und ohne Codeblock: "
        f'{{"rangfolge": [{example_order}], "abstand": "klar|knapp|gleichwertig", '
        f'"verstoesse": {{{per_text}}}, "erfunden": {{{per_text}}}, "begruendung": "höchstens 60 Wörter"}}\n'
        "\"rangfolge\" nennt alle Textnummern genau einmal, beste zuerst; \"abstand\" bezieht sich auf Platz 1 gegenüber Platz 2."
    )


def validate_verdict(obj: dict, count: int = 2) -> None:
    """Raises ValueError when the juror reply does not follow the schema."""
    labels = [str(i) for i in range(1, count + 1)]
    ranking = obj.get("rangfolge")
    if not isinstance(ranking, list) or sorted(str(x) for x in ranking) != labels:
        raise ValueError(f"rangfolge muss genau {labels} enthalten, war {ranking!r}")
    if str(obj.get("abstand", "")).strip().lower() not in ALLOWED_MARGINS:
        raise ValueError(f"abstand ungültig: {obj.get('abstand')!r}")
    for key in ("verstoesse", "erfunden"):
        value = obj.get(key, {})
        if not isinstance(value, dict):
            raise ValueError(f"{key} muss ein Objekt sein")


def winner_label(obj: dict) -> str | None:
    """Label of the better text, or None for a tie."""
    if str(obj.get("abstand", "")).strip().lower() == "gleichwertig":
        return None
    return str(obj["rangfolge"][0])


def findings_per_label(obj: dict, key: str, label: str) -> int:
    value = (obj.get(key) or {}).get(label) or []
    return len([v for v in value if str(v).strip()]) if isinstance(value, list) else 0


class Judge(Protocol):
    """A juror that returns a validated verdict object for a prompt."""

    name: str

    def verdict(self, prompt: str, label: str) -> tuple[dict, float]:
        ...


class ClaudeJudge:
    """Default juror: ``claude -p --model sonnet``."""

    def __init__(self, name: str = "claude-sonnet", model: str = "sonnet", timeout_s: float = 600.0,
                 runner: Runner = run_cli, log: Callable[[dict], None] | None = None):
        self.name = name
        self.model = model
        self.timeout_s = timeout_s
        self.runner = runner
        self.log = log

    def verdict(self, prompt: str, label: str) -> tuple[dict, float]:
        reply = call_claude(
            prompt, model=self.model, system_prompt=JUDGE_SYSTEM_PROMPT, timeout_s=self.timeout_s, runner=self.runner,
            json_out=True, validate=validate_verdict, log=self.log, label=label,
        )
        assert reply.obj is not None
        return reply.obj, reply.duration_s


class CodexBusyError(RuntimeError):
    """Codex is in use elsewhere (shared BookForge subscription) or the cap is reached."""


def codex_processes(pgrep: Callable[[], str] | None = None) -> list[str]:
    """Lines of ``pgrep -a codex`` that belong to real work (the long-lived editor app-server is ignored)."""
    if pgrep is None:
        def pgrep() -> str:
            return subprocess.run(["pgrep", "-a", "codex"], capture_output=True, text=True).stdout
    return [line for line in pgrep().splitlines() if line.strip() and "app-server" not in line]


class CodexJudge:
    """
    Optional second juror via ``codex exec``. Off unless explicitly enabled: the Codex subscription is a
    production resource of BookForge. Refuses to start while other codex processes run and stops at a hard cap.
    """

    def __init__(self, store: RunStore, max_calls: int, name: str = "codex", timeout_s: float = 600.0,
                 runner: Runner = run_cli, pgrep: Callable[[], str] | None = None,
                 log: Callable[[dict], None] | None = None):
        if max_calls <= 0:
            raise ValueError("CodexJudge braucht eine positive Obergrenze")
        self.name = name
        self.store = store
        self.max_calls = max_calls
        self.timeout_s = timeout_s
        self.runner = runner
        self.pgrep = pgrep
        self.log = log

    def verdict(self, prompt: str, label: str) -> tuple[dict, float]:
        busy = codex_processes(self.pgrep)
        if busy:
            raise CodexBusyError(f"Codex ist belegt ({len(busy)} Prozess(e)); Juror übersprungen")
        if not self.store.try_reserve(self.name, self.max_calls):
            raise CodexBusyError(f"Codex-Obergrenze {self.max_calls} erreicht")
        workdir = empty_workdir("codex")
        out_file = Path(workdir) / "antwort.txt"
        cmd = ["codex", "exec", "--skip-git-repo-check", "-C", workdir, "-s", "read-only", "--ephemeral",
               "-o", str(out_file), "-"]
        try:
            result = self.runner(cmd, "Führe keine Befehle aus und lies keine Dateien; alles Nötige steht unten.\n\n" + prompt,
                                 workdir, self.timeout_s)
            text = out_file.read_text(encoding="utf-8") if out_file.is_file() else ""
        finally:
            shutil.rmtree(workdir, ignore_errors=True)
        if self.log:
            self.log({"label": label, "cli": "codex", "duration_s": round(result.duration_s, 1), "rc": result.returncode,
                      "timeout": result.timed_out, "ok": bool(text.strip())})
        obj = extract_json(text)
        validate_verdict(obj)
        return obj, round(result.duration_s, 1)


@dataclass(frozen=True)
class JudgeJob:
    task: Task
    key: str
    judge: Judge
    run: int


def pending_jobs(tasks: list[Task], arm_names: list[str], store: RunStore, judges: list[Judge]) -> list[JudgeJob]:
    """All judgments whose two arm outputs exist and that are not on disk yet, in a stable order."""
    jobs = []
    for task in tasks:
        available = [a for a in arm_names if _ok_output(store, task.id, a) is not None]
        for arm_a, arm_b in itertools.combinations(sorted(available), 2):
            key = pair_key(arm_a, arm_b)
            for judge in judges:
                for run in (1, 2):
                    if store.read_json(store.judgment_path(task.id, key, judge.name, run)) is None:
                        jobs.append(JudgeJob(task, key, judge, run))
    return jobs


def _ok_output(store: RunStore, task_id: str, arm: str) -> dict | None:
    data = store.read_json(store.output_path(task_id, arm))
    return data if data and data.get("ok") and str(data.get("text", "")).strip() else None


def expected_assignment(seed: str, task_id: str, key: str, judge: str) -> dict:
    passes = {f"pass{run}": dict(zip(("text_1", "text_2"), order_for_pass(seed, task_id, key, judge, run)))
              for run in (1, 2)}
    return {"seed": seed, "version": ASSIGNMENT_VERSION, **passes}


def check_assignment(store: RunStore, seed: str, task_id: str, key: str, judge: str) -> dict | None:
    """Returns the stored assignment (None if absent); raises AssignmentConflict if it does not match this seed."""
    stored = store.read_json(store.assignment_path(task_id, key, judge))
    if stored is None:
        return None
    expected = expected_assignment(seed, task_id, key, judge)
    if (stored.get("seed") != seed or stored.get("version", ASSIGNMENT_VERSION) != ASSIGNMENT_VERSION
            or stored.get("pass1") != expected["pass1"] or stored.get("pass2") != expected["pass2"]):
        raise AssignmentConflict(
            f"Zuordnung {task_id} {key} {judge} stammt von Seed {stored.get('seed')!r} "
            f"(Version {stored.get('version', ASSIGNMENT_VERSION)}), dieser Aufruf nutzt Seed {seed!r} "
            f"(Version {ASSIGNMENT_VERSION}). Mit dem ursprünglichen Seed fortsetzen oder einen neuen Laufnamen wählen.")
    return stored


def run_job(job: JudgeJob, store: RunStore, seed: str) -> None:
    """Performs one judgment and writes verdict and assignment to separate files."""
    stored = check_assignment(store, seed, job.task.id, job.key, job.judge.name)
    first, second = order_for_pass(seed, job.task.id, job.key, job.judge.name, job.run)
    texts = [_ok_output(store, job.task.id, first)["text"], _ok_output(store, job.task.id, second)["text"]]
    prompt = judge_prompt(job.task, texts)
    obj, duration = job.judge.verdict(prompt, label=f"judge:{job.judge.name}:{job.task.id}:{job.key}:p{job.run}")
    if stored is None:
        store.write_json(store.assignment_path(job.task.id, job.key, job.judge.name),
                         expected_assignment(seed, job.task.id, job.key, job.judge.name))
    store.write_json(store.judgment_path(job.task.id, job.key, job.judge.name, job.run),
                     {"verdict": obj, "duration_s": duration})


def judge_all(tasks: list[Task], arm_names: list[str], store: RunStore, judges: list[Judge], seed: str,
              max_calls: int | None = None, workers: int = 2, echo: Callable[[str], None] = print) -> dict:
    """Runs all missing judgments (at most ``max_calls`` new ones, at most ``workers`` at a time)."""
    jobs = pending_jobs(tasks, arm_names, store, judges)
    for job in jobs:
        check_assignment(store, seed, job.task.id, job.key, job.judge.name)
    skipped_by_budget = 0
    if max_calls is not None and len(jobs) > max_calls:
        skipped_by_budget = len(jobs) - max_calls
        jobs = jobs[:max_calls]
    done, failed, stopped = 0, 0, False
    with cf.ThreadPoolExecutor(max_workers=max(1, min(workers, 2))) as pool:
        futures = {pool.submit(run_job, job, store, seed): job for job in jobs}
        for future in cf.as_completed(futures):
            job = futures[future]
            try:
                future.result()
                done += 1
                echo(f"Urteil fertig: {job.task.id} {job.key} {job.judge.name} Durchgang {job.run}")
            except LimitError as exc:
                stopped = True
                failed += 1
                echo(f"LIMIT erreicht, Bewertung angehalten: {exc}")
                for other in futures:
                    other.cancel()
            except CodexBusyError as exc:
                failed += 1
                echo(f"Codex-Juror übersprungen: {exc}")
            except Exception as exc:
                failed += 1
                echo(f"Urteil fehlgeschlagen: {job.task.id} {job.key} {job.judge.name} Durchgang {job.run}: {exc}")
    return {"done": done, "failed": failed, "skipped_by_budget": skipped_by_budget, "stopped_by_limit": stopped}


@dataclass(frozen=True)
class PairOutcome:
    """Combined result of both passes of one judge for one task and arm pair."""

    task_id: str
    key: str
    judge: str
    winners: tuple[str | None, str | None]
    outcome: str
    fabricated: dict[str, float]
    violations: dict[str, float]


def classify(winner_1: str, winner_2: str, arm_a: str, arm_b: str) -> str:
    """Maps the two passes onto win/tie/disagreement classes (``sieg_a`` means arm_a won both passes)."""
    if winner_1 == winner_2 == arm_a:
        return "sieg_a"
    if winner_1 == winner_2 == arm_b:
        return "sieg_b"
    if winner_1 == winner_2 == TIE:
        return "gleich"
    if {winner_1, winner_2} == {arm_a, arm_b}:
        return "uneinig"
    if {winner_1, winner_2} == {arm_a, TIE}:
        return "tendenz_a"
    return "tendenz_b"


def outcomes(tasks: list[Task], arm_names: list[str], store: RunStore, judge_names: list[str]) -> list[PairOutcome]:
    """Reads all complete two-pass judgments from disk."""
    result = []
    for task in tasks:
        for arm_a, arm_b in itertools.combinations(sorted(arm_names), 2):
            key = pair_key(arm_a, arm_b)
            for judge in judge_names:
                verdicts = [store.read_json(store.judgment_path(task.id, key, judge, run)) for run in (1, 2)]
                assigned = store.read_json(store.assignment_path(task.id, key, judge)) or {}
                if any(v is None for v in verdicts) or "pass1" not in assigned or "pass2" not in assigned:
                    continue
                winners = []
                fabricated = {arm_a: 0.0, arm_b: 0.0}
                violations = {arm_a: 0.0, arm_b: 0.0}
                for run, data in zip((1, 2), verdicts):
                    obj = data["verdict"]
                    mapping = assigned[f"pass{run}"]
                    label = winner_label(obj)
                    winners.append(TIE if label is None else mapping[f"text_{label}"])
                    for text_label in ("1", "2"):
                        arm = mapping[f"text_{text_label}"]
                        fabricated[arm] += findings_per_label(obj, "erfunden", text_label) / 2
                        violations[arm] += findings_per_label(obj, "verstoesse", text_label) / 2
                result.append(PairOutcome(
                    task.id, key, judge, (winners[0], winners[1]),
                    classify(winners[0], winners[1], arm_a, arm_b), fabricated, violations,
                ))
    return result


def default_judges(store: RunStore, runner: Runner = run_cli, log: Callable[[dict], None] | None = None,
                   codex: bool = False) -> list[Judge]:
    """Claude juror always; the codex juror only when explicitly requested and EVAL_CODEX_MAX is positive."""
    judges: list[Judge] = [ClaudeJudge(runner=runner, log=log)]
    if codex:
        cap = int(os.environ.get("EVAL_CODEX_MAX", "0") or "0")
        if cap <= 0:
            raise ValueError("Codex-Juror verlangt EVAL_CODEX_MAX > 0 (Obergrenze der Aufrufe je Lauf)")
        judges.append(CodexJudge(store, cap, runner=runner, log=log))
    return judges
