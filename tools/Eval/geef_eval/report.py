"""Markdown report: per task, per arm pair (wins, losses, ties, disagreement, sign test), per arm. Counts only, no texts."""
from __future__ import annotations

import datetime as dt
import itertools
import statistics
from collections import defaultdict

from .judge import PairOutcome, outcomes, pair_arms, pair_key
from .stats import sign_test_p
from .store import RunStore
from .tasks import Task

OUTCOME_LABELS = {
    "sieg_a": "Sieg {a}",
    "sieg_b": "Sieg {b}",
    "gleich": "gleich",
    "uneinig": "uneinig",
    "tendenz_a": "Tendenz {a}",
    "tendenz_b": "Tendenz {b}",
}


def _fmt_rule(rule: dict | None) -> str:
    if not rule:
        return "–"
    low, high = rule.get("min_words"), rule.get("max_words")
    if low and high:
        return f"{low}–{high}"
    if high:
        return f"≤ {high}"
    return f"≥ {low}"


def _fmt_bool(value: bool | None) -> str:
    return "–" if value is None else ("ja" if value else "nein")


def _fmt_num(value: float | None, digits: int = 1) -> str:
    if value is None:
        return "–"
    return f"{value:.{digits}f}".replace(".", ",")


def _median(values: list[float]) -> float | None:
    return statistics.median(values) if values else None


def build_report(run_name: str, tasks: list[Task], arm_names: list[str], judge_names: list[str], store: RunStore,
                 seed: str, now: dt.datetime | None = None) -> str:
    """Builds the Markdown report from the files of a run directory."""
    now = now or dt.datetime.now(dt.timezone.utc)
    pair_outcomes = outcomes(tasks, arm_names, store, judge_names)
    lines = [
        f"# Messbericht `{run_name}`",
        "",
        f"Erstellt: {now.strftime('%Y-%m-%d %H:%M')} UTC · Aufgaben: {len(tasks)} · Arme: {', '.join(arm_names)} · "
        f"Juroren: {', '.join(judge_names)} · Seed: `{seed}`",
        "",
        "Der Bericht enthält nur Kennzahlen und Urteilsklassen, keine Texte und keine Begründungen der Juroren "
        "(die können Inhalte echter Aufträge wiedergeben). Rohdaten liegen im Laufverzeichnis.",
        "",
        "## Kennzahlen je Aufgabe und Arm",
        "",
        "| Aufgabe | Arm | Status | Wörter | Platzhalter | Längenvorgabe | eingehalten | Dauer (s) |",
        "|---|---|---|---:|---:|---|---|---:|",
    ]
    per_arm = defaultdict(lambda: {"durations": [], "words": [], "placeholders": 0, "failed": 0, "length_misses": 0})
    for task in tasks:
        for arm in arm_names:
            data = store.read_json(store.output_path(task.id, arm))
            if not data:
                lines.append(f"| {task.id} | {arm} | fehlt | – | – | – | – | – |")
                continue
            if not data.get("ok"):
                per_arm[arm]["failed"] += 1
                lines.append(f"| {task.id} | {arm} | Fehler | – | – | – | – | – |")
                continue
            metrics = data.get("metrics") or {}
            per_arm[arm]["durations"].append(float(data.get("duration_s") or 0))
            per_arm[arm]["words"].append(float(metrics.get("words") or 0))
            per_arm[arm]["placeholders"] += int(metrics.get("placeholders") or 0)
            if metrics.get("length_ok") is False:
                per_arm[arm]["length_misses"] += 1
            lines.append(
                f"| {task.id} | {arm} | ok | {metrics.get('words', '–')} | {metrics.get('placeholders', '–')} | "
                f"{_fmt_rule(metrics.get('length_rule'))} | {_fmt_bool(metrics.get('length_ok'))} | "
                f"{_fmt_num(data.get('duration_s'))} |"
            )

    lines += ["", "## Paarvergleiche je Aufgabe", ""]
    if pair_outcomes:
        lines += [
            "| Aufgabe | Paar | Juror | Durchgang 1 | Durchgang 2 | Ergebnis | erfunden (A / B) | Verstöße (A / B) |",
            "|---|---|---|---|---|---|---|---|",
        ]
        for o in pair_outcomes:
            a, b = pair_arms(o.key)
            lines.append(
                f"| {o.task_id} | {a} ↔ {b} | {o.judge} | {o.winners[0]} | {o.winners[1]} | "
                f"{OUTCOME_LABELS[o.outcome].format(a=a, b=b)} | "
                f"{_fmt_num(o.fabricated[a])} / {_fmt_num(o.fabricated[b])} | "
                f"{_fmt_num(o.violations[a])} / {_fmt_num(o.violations[b])} |"
            )
    else:
        lines.append("Noch keine vollständigen Urteile (je Paar zwei Durchgänge).")

    lines += _aggregate_section(arm_names, judge_names, pair_outcomes)
    lines += _arm_section(arm_names, per_arm, pair_outcomes)
    lines += [
        "",
        "## Lesehilfe",
        "",
        "- Jedes Paar wird je Juror zweimal beurteilt, mit vertauschter Reihenfolge; die Zuordnung Text → Arm steht getrennt in `assignments/`.",
        "- **Sieg** heißt: beide Durchgänge sehen denselben Arm vorn. **Uneinig**: die Durchgänge widersprechen sich. "
        "**Tendenz**: ein Durchgang sieht einen Arm vorn, der andere Gleichstand.",
        "- Der Vorzeichentest (zweiseitig, exakt) vergleicht nur Siege gegen Niederlagen; Gleichstände und Uneinigkeit fallen heraus. "
        "„mit Tendenz“ zählt Tendenzen zusätzlich als Siege.",
        "- „erfunden“ und „Verstöße“ sind die vom Juror genannten Anzahlen, gemittelt über beide Durchgänge.",
    ]
    return "\n".join(lines) + "\n"


def _aggregate_section(arm_names: list[str], judge_names: list[str], pair_outcomes: list[PairOutcome]) -> list[str]:
    lines = ["", "## Aggregat je Armpaar", ""]
    rows = []
    for arm_a, arm_b in itertools.combinations(sorted(arm_names), 2):
        key = pair_key(arm_a, arm_b)
        for judge in judge_names:
            subset = [o for o in pair_outcomes if o.key == key and o.judge == judge]
            if not subset:
                continue
            counts = defaultdict(int)
            for o in subset:
                counts[o.outcome] += 1
            strict_p = sign_test_p(counts["sieg_a"], counts["sieg_b"])
            loose_p = sign_test_p(counts["sieg_a"] + counts["tendenz_a"], counts["sieg_b"] + counts["tendenz_b"])
            rows.append(
                f"| {arm_a} ↔ {arm_b} | {judge} | {len(subset)} | {counts['sieg_a']} | {counts['sieg_b']} | "
                f"{counts['gleich']} | {counts['uneinig']} | {counts['tendenz_a']} / {counts['tendenz_b']} | "
                f"{_fmt_num(strict_p, 3)} | {_fmt_num(loose_p, 3)} |"
            )
    if not rows:
        return lines + ["Noch keine Paare mit vollständigen Urteilen."]
    return lines + [
        "| Paar (A ↔ B) | Juror | n | Siege A | Siege B | gleich | uneinig | Tendenz A / B | p (streng) | p (mit Tendenz) |",
        "|---|---|---:|---:|---:|---:|---:|---|---:|---:|",
        *rows,
    ]


def _arm_section(arm_names: list[str], per_arm: dict, pair_outcomes: list[PairOutcome]) -> list[str]:
    fabricated = defaultdict(list)
    violations = defaultdict(list)
    for o in pair_outcomes:
        for arm, value in o.fabricated.items():
            fabricated[arm].append(value)
        for arm, value in o.violations.items():
            violations[arm].append(value)
    lines = [
        "", "## Je Arm", "",
        "| Arm | Texte ok | Fehler | Dauer Summe (s) | Dauer Median (s) | Wörter Median | Platzhalter gesamt | Länge verfehlt | erfunden Summe | erfunden Median | Verstöße Median |",
        "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|",
    ]
    for arm in arm_names:
        data = per_arm[arm]
        lines.append(
            f"| {arm} | {len(data['durations'])} | {data['failed']} | {_fmt_num(sum(data['durations']))} | "
            f"{_fmt_num(_median(data['durations']))} | {_fmt_num(_median(data['words']), 0)} | {data['placeholders']} | "
            f"{data['length_misses']} | {_fmt_num(sum(fabricated[arm]) if fabricated[arm] else None)} | "
            f"{_fmt_num(_median(fabricated[arm]))} | {_fmt_num(_median(violations[arm]))} |"
        )
    return lines
