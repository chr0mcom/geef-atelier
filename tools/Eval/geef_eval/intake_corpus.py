"""Synthetic corpus for the intake clarifier: ``generate`` invents requests, ``label`` annotates which questions are legitimate.

Usage (from ``tools/Eval``): ``python3 -m geef_eval.intake_corpus {generate|label|stats} ...``
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import random
import sys
import tempfile
from collections import Counter
from pathlib import Path

from .claude_call import ClaudeCallError, call_claude
from .cli_runner import LimitError, Runner, run_cli

TOPICS = ("length", "audience", "purpose", "tone", "formality", "language", "sender", "signature", "facts", "format",
          "date", "scope", "other")
LEVELS = ("necessary_ask", "acceptable_ask", "forbidden_ask")
CATEGORIES = ("sparse", "complete", "revision", "researchable", "profile_covered")
FILE_KINDS = ("text", "markdown", "pdf", "docx")
FORMALITIES = ("unset", "formal", "informal")
PROFILE_FIELDS = ("display_name", "role", "organization", "formality", "language", "signature", "no_gos", "free_text")
LANGUAGES = ("de", "en", "fr")
CORPORA = ("main", "second")

MAIN_CATEGORIES = {"sparse": 50, "complete": 40, "revision": 10, "researchable": 10, "profile_covered": 10}
MAIN_FRENCH = 4
MAIN_GERMAN_SHARE = 0.7
SECOND_CATEGORIES = {"sparse": 16, "complete": 10, "revision": 5, "researchable": 5, "profile_covered": 4}
SECOND_GERMAN_SHARE = 0.7

MIN_REVISION_WORDS = 70
MAX_REVISION_WORDS = 450

DEFAULT_SEED = "geef-intake-corpus"
GENERATE_TIMEOUT_S = 600.0
LABEL_TIMEOUT_S = 600.0

SCENARIO_AREAS = (
    "Bewerbung oder Kündigung", "Kundenmail oder Reklamation", "Produkt- oder Shoptext", "Einladung oder Ankündigung",
    "Social-Media-Beitrag", "Rede oder Grußwort", "Vereins- oder Elternbrief", "Behörden- oder Vermieterschreiben",
    "Newsletter oder Pressetext", "Angebot, Mahnung oder Rechnungsbegleitung", "Dank, Gratulation oder Kondolenz",
    "Referenz, Zeugnis oder Empfehlung", "Projektbericht oder Protokoll", "Blogartikel oder Ratgebertext",
    "interne Mitteilung im Betrieb", "Lebenslauf-Anschreiben oder Stipendium", "Verkaufsanzeige oder Kleinanzeige",
    "Beschwerde oder Widerspruch", "Terminabsprache oder Absage", "Lektorat oder Überarbeitung eines eigenen Textes",
)

GENERATOR_SYSTEM = (
    "You invent realistic, fictional test cases for a writing assistant. Reply with exactly one JSON object and nothing else."
)
LABELER_SYSTEM = (
    "You are a strict annotator for a writing assistant's clarifying questions. Reply with exactly one JSON object "
    "and nothing else."
)

CATEGORY_RULES = {
    "sparse": "a thin request: important personal details are missing (who exactly, what exactly, how long, what tone ...). "
              "The request is 1-2 sentences.",
    "complete": "a complete request: it already says what the text is, for whom and with which main content. "
                "The request is 3-8 sentences.",
    "revision": "an attached text of the person shall be revised, shortened or improved. `files` must contain exactly that "
                f"text ({MIN_REVISION_WORDS + 10}-{MAX_REVISION_WORDS - 50} words, written by you, with the flaws a real draft has).",
    "researchable": "something is missing that could be looked up on the internet (facts about a company, place, law or event). "
                    "The person does not need to know it and does not mention it.",
    "profile_covered": "sender, signature, salutation or language are missing in the request but are in the supplied profile. "
                       "`profile` must be filled with a plausible profile; the request does not repeat these details.",
}

STYLE_RULES = {
    "main": "Write the request the way this person would really type it in a few minutes: natural, in their own words, "
            "no technical terms about prompts or writing assistants.",
    "second": "Write the request as a rushed quick tip: all lowercase, typos, half sentences, no punctuation care. "
              "Still a real request in the person's own words, no technical terms.",
}

LANGUAGE_NAMES = {"de": "German", "en": "English", "fr": "French"}


def split(request: str) -> str:
    """Development/hold-out split: same rule as ``LengthCorpusTests.InDevelopmentSet`` in the C# tests."""
    return "dev" if hashlib.sha256(request.encode("utf-8")).digest()[0] % 10 < 7 else "holdout"


def assignments(corpus: str, count: int, seed: str = DEFAULT_SEED) -> list[dict]:
    """Deterministic (category, lang, area) for each index of ``corpus``; the same inputs always give the same list."""
    if corpus not in CORPORA:
        raise ValueError(f"unbekannter Korpus {corpus!r}")
    main = corpus == "main"
    full = MAIN_CATEGORIES if main else SECOND_CATEGORIES
    full_total = sum(full.values())
    if count > full_total:
        raise ValueError(f"Korpus {corpus} hat höchstens {full_total} Einträge, angefragt sind {count}")
    rng = random.Random(f"{seed}:{corpus}")
    french = MAIN_FRENCH if main else 0
    categories = [name for name, n in full.items() for _ in range(n)]
    rng.shuffle(categories)
    # Reserve the French slots from the sparse entries, then split the rest into German and English.
    fr_slots = 0
    french_indexes = set()
    for i, name in enumerate(categories):
        if fr_slots < french and name == "sparse":
            french_indexes.add(i)
            fr_slots += 1
    rest = [i for i in range(full_total) if i not in french_indexes]
    de_n = min(len(rest), round(full_total * (MAIN_GERMAN_SHARE if main else SECOND_GERMAN_SHARE)))
    languages = ["de"] * de_n + ["en"] * (len(rest) - de_n)
    rng.shuffle(languages)
    lang_of = {i: "fr" for i in french_indexes}
    lang_of.update(zip(rest, languages))
    areas = list(SCENARIO_AREAS)
    result = []
    for i in range(full_total):
        if i % len(areas) == 0:
            rng.shuffle(areas)
        result.append({"category": categories[i], "lang": lang_of[i], "area": areas[i % len(areas)]})
    return result[:count]


def entry_id(corpus: str, index: int) -> str:
    return f"{corpus}-{index + 1:03d}"


def _words(text: str) -> int:
    return len(text.split())


def _text(value, name: str, optional: bool = False) -> str | None:
    if value is None and optional:
        return None
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{name} fehlt oder ist leer")
    return value.strip()


def parse_generated(obj: dict, category: str) -> dict:
    """Validates a generator reply strictly and returns its normalized ``request``, ``intent``, ``files``, ``profile``."""
    unknown = set(obj) - {"request", "intent", "files", "profile"}
    if unknown:
        raise ValueError(f"unbekannte Felder: {sorted(unknown)}")
    request = _text(obj.get("request"), "request")
    intent = _text(obj.get("intent"), "intent")
    raw_files = obj.get("files")
    if raw_files is None:
        raw_files = []
    if not isinstance(raw_files, list):
        raise ValueError("files ist keine Liste")
    files = []
    for item in raw_files:
        if not isinstance(item, dict):
            raise ValueError("files-Eintrag ist kein Objekt")
        extra = set(item) - {"name", "kind", "text", "pages"}
        if extra:
            raise ValueError(f"unbekannte Dateifelder: {sorted(extra)}")
        kind = item.get("kind", "text")
        if kind not in FILE_KINDS:
            raise ValueError(f"files.kind {kind!r} nicht erlaubt")
        pages = item.get("pages")
        if pages is not None and (isinstance(pages, bool) or not isinstance(pages, int) or pages < 1):
            raise ValueError("files.pages ist weder Ganzzahl noch null")
        files.append({"name": _text(item.get("name"), "files.name"), "kind": kind,
                      "text": _text(item.get("text"), "files.text"), "pages": pages})
    profile = _parse_profile(obj.get("profile"))
    if category == "revision":
        if not files:
            raise ValueError("revision braucht eine Datei mit dem Text")
        if not MIN_REVISION_WORDS <= _words(files[0]["text"]) <= MAX_REVISION_WORDS:
            raise ValueError(f"Text der Überarbeitung nicht {MIN_REVISION_WORDS}-{MAX_REVISION_WORDS} Wörter lang")
    if category == "profile_covered" and profile is None:
        raise ValueError("profile_covered braucht ein Profil")
    return {"request": request, "intent": intent, "files": files, "profile": profile}


def _parse_profile(value) -> dict | None:
    if value is None:
        return None
    if not isinstance(value, dict):
        raise ValueError("profile ist kein Objekt")
    extra = set(value) - set(PROFILE_FIELDS)
    if extra:
        raise ValueError(f"unbekannte Profilfelder: {sorted(extra)}")
    profile = {}
    for name in PROFILE_FIELDS:
        field = value.get(name)
        if field is not None and not isinstance(field, str):
            raise ValueError(f"profile.{name} ist kein Text")
        profile[name] = field.strip() or None if isinstance(field, str) else None
    if profile["formality"] is not None and profile["formality"] not in FORMALITIES:
        raise ValueError(f"profile.formality {profile['formality']!r} nicht erlaubt")
    return profile


def parse_labels(obj: dict) -> tuple[dict, str]:
    """Validates a label reply strictly: only known topics and levels, a non-empty set, a one-sentence note."""
    unknown = set(obj) - {"labels", "note"}
    if unknown:
        raise ValueError(f"unbekannte Felder: {sorted(unknown)}")
    labels = obj.get("labels")
    if not isinstance(labels, dict) or not labels:
        raise ValueError("labels fehlt oder ist leer")
    for topic, level in labels.items():
        if topic not in TOPICS:
            raise ValueError(f"Thema {topic!r} nicht erlaubt")
        if level not in LEVELS:
            raise ValueError(f"Stufe {level!r} für {topic} nicht erlaubt")
    return dict(labels), _text(obj.get("note"), "note")


def generator_prompt(corpus: str, slot: dict) -> str:
    category, lang = slot["category"], slot["lang"]
    return f"""Invent ONE test case: a person who wants a text written, and the request they type.

Language of the request and of all texts: {LANGUAGE_NAMES[lang]} (lang = "{lang}").
Category: {category} = {CATEGORY_RULES[category]}
Scenario area (use it as inspiration, vary the concrete situation): {slot['area']}
Style: {STYLE_RULES[corpus]}

Rules:
- Everything is fictional: no real persons, no real companies. Use realistic but invented names.
- `intent` is the person's hidden full intention: everything they actually want and know (length, audience, purpose, tone,
  sender, facts, dates ...), written as a short complete paragraph. The request does NOT contain all of it.
- `request` is what the person types, in their own words, no technical terms. Thin requests are 1-2 sentences, complete ones 3-8.
- `files`: a list of attachments, usually empty. Each is {{"name": "...", "kind": "text|markdown|pdf|docx", "text": "full plain text",
  "pages": null or integer}}. Other categories may have a file too (e.g. a letter that is being answered).
- `profile`: null, or {{"display_name", "role", "organization", "formality": "unset|formal|informal", "language",
  "signature", "no_gos", "free_text"}} with null for missing fields.

Reply with exactly one JSON object: {{"request": "...", "intent": "...", "files": [], "profile": null}}"""


def labeler_prompt(entry: dict) -> str:
    shown = {"request": entry["request"], "files": entry.get("files") or [], "profile": entry.get("profile")}
    return f"""A writing assistant may ask the person up to three clarifying questions before it writes. Decide for each topic which
question would be legitimate. You see the request, the attached files, the saved profile of the person, and what the person
really wants (the hidden intention).

Topics: {', '.join(TOPICS)}.
Levels, per topic that plays any role for THIS text:
- necessary_ask: the person knows it, it cannot be derived from request or files, cannot be looked up, is not covered by the
  profile, and guessing wrong would spoil the text.
- acceptable_ask: reasonable to ask, but not needed.
- forbidden_ask: derivable from request or files, can be looked up on the internet, is covered by the profile, or the person
  cannot know it.
Leave out topics that play no role at all for this text.

Request and material:
{json.dumps(shown, ensure_ascii=False, indent=2)}

Hidden intention of the person:
{entry['intent']}

Reply with exactly one JSON object: {{"labels": {{"<topic>": "<level>", ...}}, "note": "one sentence of reasoning"}}"""


def _log(message: str) -> None:
    print(message, file=sys.stderr, flush=True)


def read_entries(path: Path) -> list[dict]:
    """Reads the JSONL file; a corrupt line aborts, so nothing is silently dropped on the next rewrite."""
    if not path.exists():
        return []
    entries = []
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip():
            continue
        try:
            entry = json.loads(line)
        except json.JSONDecodeError as exc:
            raise SystemExit(f"{path}:{number}: ungültige JSON-Zeile ({exc})")
        if not isinstance(entry, dict) or "id" not in entry:
            raise SystemExit(f"{path}:{number}: Zeile ist kein Eintrag mit id")
        entries.append(entry)
    return entries


def _dump(entry: dict) -> str:
    return json.dumps(entry, ensure_ascii=False)


def write_entries(path: Path, entries: list[dict]) -> None:
    """Replaces the file atomically: temporary file in the same directory, then ``os.replace``."""
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            for entry in entries:
                handle.write(_dump(entry) + "\n")
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(tmp, path)
    except BaseException:
        if os.path.exists(tmp):
            os.unlink(tmp)
        raise


class _Budget:
    """Counts real CLI attempts of this invocation against ``--max-calls``."""

    def __init__(self, limit: int | None):
        self.limit = limit
        self.used = 0

    @property
    def remaining(self) -> int | None:
        return None if self.limit is None else self.limit - self.used

    def exhausted(self) -> bool:
        return self.limit is not None and self.used >= self.limit

    def log(self, record: dict) -> None:
        self.used += 1
        state = "ok" if record.get("ok") else f"Fehlversuch ({record.get('error', '?')})"
        _log(f"  Aufruf {self.used}: {record.get('label')} {record.get('model')} Versuch {record.get('attempt')} {state}")

    def tries(self) -> int:
        return 2 if self.remaining is None else max(1, min(2, self.remaining))


def generate(out: Path, main_count: int, second_count: int, *, model: str = "sonnet", max_calls: int | None = None,
             seed: str = DEFAULT_SEED, runner: Runner = run_cli) -> dict:
    """Invents the missing entries one call each; entries already in the file are kept and skipped."""
    plan = [(c, i, slot) for c, n in (("main", main_count), ("second", second_count))
            for i, slot in enumerate(assignments(c, n, seed))]
    present = {e["id"] for e in read_entries(out)}
    budget = _Budget(max_calls)
    summary = {"written": 0, "failed": 0, "skipped": 0, "stopped_by_limit": False, "stopped_by_max_calls": False}
    for corpus, index, slot in plan:
        eid = entry_id(corpus, index)
        if eid in present:
            summary["skipped"] += 1
            continue
        if budget.exhausted():
            summary["stopped_by_max_calls"] = True
            break
        _log(f"{eid}: {slot['category']}/{slot['lang']}")
        try:
            reply = call_claude(generator_prompt(corpus, slot), model=model, system_prompt=GENERATOR_SYSTEM,
                                timeout_s=GENERATE_TIMEOUT_S, runner=runner, json_out=True,
                                validate=lambda o, c=slot["category"]: parse_generated(o, c),
                                tries=budget.tries(), log=budget.log, label=eid)
            parsed = parse_generated(reply.obj, slot["category"])
        except LimitError as exc:
            _log(f"Nutzungslimit erreicht, Lauf angehalten ({exc}). Mit demselben Befehl später fortsetzen.")
            summary["stopped_by_limit"] = True
            break
        except (ClaudeCallError, ValueError) as exc:
            _log(f"{eid}: verworfen, wird beim nächsten Lauf neu versucht ({exc})")
            summary["failed"] += 1
            continue
        entry = {"id": eid, "corpus": corpus, "category": slot["category"], "lang": slot["lang"],
                 "request": parsed["request"], "files": parsed["files"], "profile": parsed["profile"],
                 "intent": parsed["intent"], "generator_model": model}
        out.parent.mkdir(parents=True, exist_ok=True)
        with out.open("a", encoding="utf-8") as handle:
            handle.write(_dump(entry) + "\n")
            handle.flush()
            os.fsync(handle.fileno())
        present.add(eid)
        summary["written"] += 1
    summary["calls"] = budget.used
    return summary


def label(path: Path, *, model: str = "opus", max_calls: int | None = None, runner: Runner = run_cli) -> dict:
    """Annotates every entry without ``labels`` with a second, independent call; rewrites the file after each entry."""
    entries = read_entries(path)
    budget = _Budget(max_calls)
    summary = {"labeled": 0, "failed": 0, "skipped": 0, "stopped_by_limit": False, "stopped_by_max_calls": False}
    for entry in entries:
        if "labels" in entry:
            summary["skipped"] += 1
            continue
        if budget.exhausted():
            summary["stopped_by_max_calls"] = True
            break
        _log(f"{entry['id']}: beschriften")
        try:
            reply = call_claude(labeler_prompt(entry), model=model, system_prompt=LABELER_SYSTEM,
                                timeout_s=LABEL_TIMEOUT_S, runner=runner, json_out=True,
                                validate=lambda o: parse_labels(o), tries=budget.tries(), log=budget.log,
                                label=f"label {entry['id']}")
            labels, note = parse_labels(reply.obj)
        except LimitError as exc:
            _log(f"Nutzungslimit erreicht, Lauf angehalten ({exc}). Mit demselben Befehl später fortsetzen.")
            summary["stopped_by_limit"] = True
            break
        except (ClaudeCallError, ValueError) as exc:
            _log(f"{entry['id']}: verworfen, wird beim nächsten Lauf neu versucht ({exc})")
            summary["failed"] += 1
            continue
        entry.update({"labels": labels, "label_note": note, "label_model": model})
        write_entries(path, entries)
        summary["labeled"] += 1
    summary["calls"] = budget.used
    return summary


def stats(path: Path) -> dict[str, Counter]:
    """Counts per corpus, category, language, split and labeled state."""
    entries = read_entries(path)
    counters = {name: Counter() for name in ("corpus", "category", "lang", "split", "labeled")}
    for entry in entries:
        counters["corpus"][entry.get("corpus", "?")] += 1
        counters["category"][f"{entry.get('corpus', '?')}/{entry.get('category', '?')}"] += 1
        counters["lang"][f"{entry.get('corpus', '?')}/{entry.get('lang', '?')}"] += 1
        counters["split"][split(entry.get("request", ""))] += 1
        counters["labeled"]["beschriftet" if "labels" in entry else "offen"] += 1
    return counters


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="python3 -m geef_eval.intake_corpus",
                                     description="Synthetischer Korpus für den Klärer")
    sub = parser.add_subparsers(dest="command", required=True)
    gen = sub.add_parser("generate", help="Szenarien erfinden (ein Aufruf je Eintrag)")
    gen.add_argument("--out", required=True, help="JSONL-Datei; vorhandene Einträge werden übersprungen")
    gen.add_argument("--main", type=int, default=120, help="Zahl der Hauptkorpus-Einträge (0 = keine, höchstens 120)")
    gen.add_argument("--second", type=int, default=40, help="Zahl der Zweitkorpus-Einträge (0 = keine, höchstens 40)")
    gen.add_argument("--max-calls", type=int, default=None, help="Höchstzahl der Modellaufrufe dieses Laufs")
    gen.add_argument("--model", default="sonnet")
    gen.add_argument("--seed", default=DEFAULT_SEED, help="Seed der Belegung; beim Fortsetzen unverändert lassen")
    lab = sub.add_parser("label", help="Themen unabhängig beschriften (anderes Modell)")
    lab.add_argument("--corpus", required=True, help="JSONL-Datei; sie wird atomar umgeschrieben")
    lab.add_argument("--max-calls", type=int, default=None, help="Höchstzahl der Modellaufrufe dieses Laufs")
    lab.add_argument("--model", default="opus")
    st = sub.add_parser("stats", help="Zählung je Korpus, Kategorie, Sprache, Teilung und Beschriftung")
    st.add_argument("--corpus", required=True)
    return parser


def main(argv: list[str] | None = None, runner: Runner = run_cli) -> int:
    args = build_parser().parse_args(argv)
    if args.command == "stats":
        for name, counter in stats(Path(args.corpus)).items():
            print(f"{name}: " + ", ".join(f"{k}={v}" for k, v in sorted(counter.items())))
        return 0
    if args.max_calls is not None and args.max_calls < 0:
        raise SystemExit("--max-calls darf nicht negativ sein")
    if args.command == "generate":
        try:
            summary = generate(Path(args.out), args.main, args.second, model=args.model, max_calls=args.max_calls,
                               seed=args.seed, runner=runner)
        except ValueError as exc:
            raise SystemExit(str(exc))
    else:
        summary = label(Path(args.corpus), model=args.model, max_calls=args.max_calls, runner=runner)
    print(f"{args.command}: {summary}")
    return 3 if summary["stopped_by_limit"] else 0


if __name__ == "__main__":
    sys.exit(main())
