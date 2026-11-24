"""Command line: ``run`` (produce texts), ``judge`` (blind pairwise), ``report`` (Markdown), ``all``."""
from __future__ import annotations

import argparse
import datetime as dt
import os
import subprocess
import sys
from pathlib import Path

from .arms import build_arms
from .judge import ASSIGNMENT_VERSION, default_judges, judge_all
from .pipeline import produce_outputs
from .report import build_report
from .store import RunStore, safe_name
from .tasks import load_tasks

DEFAULT_SEED = "geef-eval"


def default_out_dir() -> Path:
    """Outside the repository by default: real outputs must never end up in Git."""
    if env := os.environ.get("EVAL_OUT_DIR"):
        return Path(env)
    state = os.environ.get("XDG_STATE_HOME") or str(Path.home() / ".local" / "state")
    return Path(state) / "geef-eval"


def inside_git_worktree(path: Path) -> bool:
    probe = path if path.exists() else path.parent
    while not probe.exists():
        probe = probe.parent
    result = subprocess.run(["git", "-C", str(probe), "rev-parse", "--is-inside-work-tree"],
                            capture_output=True, text=True)
    return result.returncode == 0 and result.stdout.strip() == "true"


def _parse_plugins(values: list[str]) -> dict[str, str]:
    plugins = {}
    for value in values:
        name, sep, spec = value.partition("=")
        if not sep or not name or not spec:
            raise SystemExit(f"--plugin erwartet name=modul:attribut, nicht {value!r}")
        plugins[name] = spec
    return plugins


def resolve_seed(requested: str | None, manifest: dict, run_dir: Path) -> str:
    """
    The seed of a run directory is fixed by its first call: resuming reuses it, a different explicit seed is refused,
    and so is a directory written with another ordering version. Otherwise old verdicts would be mapped to the wrong arm.
    """
    stored = manifest.get("seed")
    version = manifest.get("assignment_version", ASSIGNMENT_VERSION)
    if version != ASSIGNMENT_VERSION:
        raise SystemExit(f"{run_dir} wurde mit Zuordnungsversion {version} angelegt, dieses Werkzeug nutzt "
                         f"{ASSIGNMENT_VERSION}. Neuen Laufnamen wählen.")
    if stored is not None and requested is not None and requested != stored:
        raise SystemExit(f"{run_dir} wurde mit Seed {stored!r} angelegt, angefragt ist {requested!r}. Ein anderer Seed "
                         "würde bestehende Urteile dem falschen Arm zuordnen. Ohne --seed/EVAL_SEED fortsetzen "
                         "(nutzt den gespeicherten Seed) oder einen neuen Laufnamen wählen.")
    return requested or stored or DEFAULT_SEED


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="eval.py", description="Messwerkzeug für Geef.Atelier")
    sub = parser.add_subparsers(dest="command", required=True)
    for name, text in (("run", "Texte je Arm erzeugen"), ("judge", "Blinder Paarvergleich"),
                       ("report", "Markdown-Bericht schreiben"), ("all", "run, judge und report nacheinander")):
        cmd = sub.add_parser(name, help=text)
        cmd.add_argument("--run-name", required=True, help="Name des Laufs (Unterverzeichnis im Ausgabeordner)")
        cmd.add_argument("--tasks", default=os.environ.get("EVAL_TASKS_DIR"),
                         help="Aufgabenverzeichnis (Standard: EVAL_TASKS_DIR)")
        cmd.add_argument("--arms", default="roh,atelier", help="Kommagetrennte Arme, z. B. roh,atelier")
        cmd.add_argument("--plugin", action="append", default=[], help="Zusätzlicher Arm: name=modul:attribut")
        cmd.add_argument("--out", default=None, help="Ausgabeordner (Standard: EVAL_OUT_DIR oder ~/.local/state/geef-eval)")
        cmd.add_argument("--seed", default=None,
                         help="Seed für die Reihenfolge (Standard: EVAL_SEED, beim Fortsetzen der gespeicherte Seed, sonst geef-eval)")
        cmd.add_argument("--max-judge-calls", type=int, default=None, help="Höchstzahl neuer Jurorenaufrufe")
        cmd.add_argument("--codex-juror", action="store_true",
                         help="Zweiten Juror per Codex zuschalten (braucht EVAL_CODEX_MAX; Standard: aus)")
        cmd.add_argument("--allow-in-repo", action="store_true", help="Ausgabe innerhalb eines Git-Repos erlauben")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if not args.tasks:
        raise SystemExit("Kein Aufgabenverzeichnis: --tasks oder EVAL_TASKS_DIR setzen")
    out_root = Path(args.out) if args.out else default_out_dir()
    run_dir = out_root / safe_name(args.run_name)
    if not args.allow_in_repo and inside_git_worktree(run_dir):
        raise SystemExit(f"{run_dir} liegt in einem Git-Repository. Ergebnisse echter Aufträge gehören nicht in Git; "
                         "anderen Ordner wählen oder --allow-in-repo setzen.")

    tasks = load_tasks(args.tasks)
    store = RunStore(run_dir)
    arm_names = [a.strip() for a in args.arms.split(",") if a.strip()]
    judges = default_judges(store, log=store.log_call, codex=args.codex_juror)
    judge_names = [j.name for j in judges]
    manifest = store.read_json(store.manifest_path) or {"created": dt.datetime.now(dt.timezone.utc).isoformat()}
    seed = resolve_seed(args.seed or os.environ.get("EVAL_SEED"), manifest, run_dir)
    judge_names = sorted(set(manifest.get("judges", [])) | set(judge_names))
    manifest.update({"tasks": [t.id for t in tasks], "arms": arm_names, "judges": judge_names, "seed": seed,
                     "assignment_version": ASSIGNMENT_VERSION})
    store.write_json(store.manifest_path, manifest)

    print(f"Lauf {args.run_name}: {len(tasks)} Aufgaben, Arme {', '.join(arm_names)}, Ordner {run_dir}")
    if args.command in ("run", "all"):
        arms = build_arms(arm_names, _parse_plugins(args.plugin), log=store.log_call)
        summary = produce_outputs(tasks, arms, store)
        print(f"Erzeugung: {summary}")
        if summary["stopped_by_limit"]:
            return 3
    if args.command in ("judge", "all"):
        summary = judge_all(tasks, arm_names, store, judges, seed, max_calls=args.max_judge_calls)
        print(f"Bewertung: {summary}")
        if summary["stopped_by_limit"]:
            return 3
    if args.command in ("report", "all"):
        store.write_text(store.report_path, build_report(args.run_name, tasks, arm_names, judge_names, store, seed))
        print(f"Bericht: {store.report_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
