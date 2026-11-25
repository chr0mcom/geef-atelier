"""Synthetic corpora for the fact check (V2) and the arithmetic check of the files (V3).

Usage (from ``tools/Eval``): ``python3 -m geef_eval.factcheck_corpus {generate|label|plant|stats|arithmetic} ...``

- ``generate``: one Sonnet call per basis writes request, material and a clean text that uses only the material.
- ``label``: an independent Opus call marks statements of the clean text the material does not back (word for word).
- ``plant``: one Sonnet call per basis plants 2-4 mistakes into a copy and lists them; the code checks every wording.
- ``arithmetic``: 60 snippets for ``SourceArithmeticCheck`` (5 per Sonnet call); the code recomputes every trace.

Every command except ``stats`` needs ``--max-calls``. Every attempt is logged in ``<file>.calls.log`` (JSON lines; not ``*.jsonl``, so no corpus glob picks it up) and counts against the
fixed cap of the corpus (60 for the fact-check corpus, 12 for the arithmetic corpus), across all invocations.

Exit codes: 0 complete, 2 wrong usage, 3 stopped by the usage limit, 4 bases or snippets still missing (also when the cap is used up).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import sys
import tempfile
from collections import Counter
from decimal import Decimal, InvalidOperation
from pathlib import Path

from .claude_call import ClaudeCallError, call_claude
from .cli_runner import LimitError, Runner, run_cli

FACT_CEILING = 60
ARITHMETIC_CEILING = 12
SNIPPETS_PER_CALL = 5
ARITHMETIC_SIZE = 60
TIMEOUT_S = 900.0
TOLERANCE = Decimal("0.005")

SIZES = ("short", "long")
KINDS = ("plain", "revision", "profile", "faulty_source")
SOURCE_ERROR_KINDS = ("wrong_total", "conflict", "superseded")
PLANT_KINDS = {
    "changed_number": "a number, amount or price from the material is changed",
    "changed_date": "a date or time from the material is changed",
    "changed_name": "a name of a person, place or organisation from the material is changed",
    "invented_fact": "a fact nobody gave is added (an event, a promise, a detail)",
    "invented_contact": "an invented way to reply or get in touch is added (phone number, mail address, chat group, office hours)",
    "assumed_action": "the text claims an attachment, enclosure or action the material does not mention (for example: the photos are attached)",
    "broken_instruction": "the text breaks an instruction given in the request, the answers or the profile (only if there is one)",
    "superseded_use": "the text uses information that the material itself replaces or drops, such as a moved date or a cancelled room (only if the material has such)",
    "silent_total": "the text silently adopts or silently corrects a wrong total of the material (only if the material has one)",
}
SUM_WORDS = {"de": "Summe, Gesamt, insgesamt, zusammen", "en": "total, sum, altogether, in total"}
LANGUAGE_NAMES = {"de": "German", "en": "English"}
PARENTS_EVENING = "elternabend"
DEV, HOLDOUT = "dev", "holdout"

SLOTS = (
    {"id": "fc-01", "size": "short", "lang": "de", "kind": "plain", "case": None, "area": "Einladung zu einem Vereinsfest"},
    {"id": "fc-02", "size": "short", "lang": "de", "kind": "plain", "case": None, "area": "Antwort auf eine Kundenanfrage mit Angebot"},
    {"id": "fc-03", "size": "short", "lang": "de", "kind": "plain", "case": None, "area": "Mitteilung an Mieter über Bauarbeiten"},
    {"id": "fc-04", "size": "short", "lang": "de", "kind": "revision", "case": None, "area": "Überarbeitung eines Newsletter-Absatzes"},
    {"id": "fc-05", "size": "short", "lang": "de", "kind": "profile", "case": None, "area": "Dankesbrief an Ehrenamtliche"},
    {"id": "fc-06", "size": "short", "lang": "en", "kind": "profile", "case": None, "area": "product announcement for customers"},
    {"id": "fc-07", "size": "short", "lang": "en", "kind": "plain", "case": None, "area": "reply to a job applicant"},
    {"id": "fc-08", "size": "short", "lang": "de", "kind": "plain", "case": None, "area": "Pressetext zu einer Ausstellung"},
    {"id": "fc-09", "size": "long", "lang": "de", "kind": "plain", "case": None, "area": "Jahresbericht eines Sportvereins aus Protokollen"},
    {"id": "fc-10", "size": "long", "lang": "de", "kind": "plain", "case": None, "area": "Projektbericht aus Statusnotizen"},
    {"id": "fc-11", "size": "long", "lang": "en", "kind": "revision", "case": None, "area": "revision of a long community report"},
    {"id": "fc-12", "size": "long", "lang": "en", "kind": "plain", "case": None, "area": "guide for new staff from internal notes"},
    {"id": "fc-13", "size": "short", "lang": "de", "kind": "faulty_source", "case": PARENTS_EVENING, "area": None},
    {"id": "fc-14", "size": "short", "lang": "de", "kind": "faulty_source", "case": "conflicting_amounts", "area": "Rundschreiben zu einer Kostenumlage"},
    {"id": "fc-15", "size": "short", "lang": "en", "kind": "faulty_source", "case": "superseded_decision", "area": "summary of a committee decision"},
    {"id": "fc-16", "size": "short", "lang": "de", "kind": "faulty_source", "case": "wrong_subtotal", "area": "Begleitschreiben zu einem Angebot"},
)

CASE_RULES = {
    "conflicting_amounts": "two files state different amounts for the same thing (for example 240 EUR in the invitation and 280 EUR in the "
                           "minutes). Report both passages as one source error of kind \"conflict\" (quote and quote_2).",
    "superseded_decision": "minutes with an earlier decision and a later one that replaces it (for example a date or a budget changed at a "
                           "later meeting). Report the old and the new passage as one source error of kind \"superseded\" (quote = old, quote_2 = new).",
    "wrong_subtotal": "an offer with a markdown table of items whose subtotal row is wrong. Report the subtotal row as a source error of kind "
                      "\"wrong_total\" with the stated subtotal in \"stated\" (digits as written).",
}

PARENTS_EVENING_REQUEST = ("Kannst du aus meinen Notizen einen kurzen Elternbrief zum Museumsausflug und zum Abschlussfest machen? "
                           "Bitte kurz die Gesamtkosten pro Kind nennen.")
PARENTS_EVENING_NOTES = """Notizen Elternabend Klasse 3b, 12. März

Anwesend: 17 Eltern, Frau Kranich (Klassenleitung), Herr Odenthal (Elternvertretung)

1. Ausflug ins Naturkundemuseum
- Termin ursprünglich Donnerstag, 24. April. NEU: verschoben auf Dienstag, 6. Mai, weil der Bus am 24. April nicht fährt.
- Der Kostenbeitrag ist bis zum ursprünglichen Termin, also bis 24. April, auf das Klassenkonto zu überweisen.
- Kosten pro Kind:
  Busfahrt 9 €
  Eintritt Museum 6 €
  Workshop Fossilien 4 €
  Gesamt pro Kind: 17 €

2. Raum für das Abschlussfest am 27. Juni
- Zuerst war die Aula angedacht. Die Aula ist an dem Tag belegt, deshalb fällt sie weg.
- Stattdessen feiern wir in der Pausenhalle, Aufbau ab 14 Uhr.

3. Sonstiges
- Allergiehinweis: Ein Kind hat eine Nussallergie. Zum Abschlussfest bitte keine Speisen mit Nüssen mitbringen.
- Intern, nicht an die Eltern weitergeben: Herr Odenthal fragt beim Förderverein, ob der Workshop bezuschusst wird.
"""
PARENTS_EVENING_ERRORS = [{"kind": "wrong_total", "quote": "Gesamt pro Kind: 17 €", "quote_2": None, "stated": "17"}]

GENERATOR_SYSTEM = "You invent realistic, fictional test material for a fact checker. Reply with exactly one JSON object and nothing else."
LABELER_SYSTEM = "You are a strict, independent checker of facts. Reply with exactly one JSON object and nothing else."
PLANTER_SYSTEM = "You prepare test texts with deliberate, documented mistakes. Reply with exactly one JSON object and nothing else."
ARITHMETIC_SYSTEM = "You write realistic, fictional snippets with numbers for a test. Reply with exactly one JSON object and nothing else."


HELD_OUT_IDS = ("fc-02", "fc-04", "fc-06", "fc-10", "fc-14")


def split(basis: dict) -> str:
    """Development/hold-out split of a fact-check basis, same rule as ``FactCheckCorpus.SplitOf`` in C#: fixed ids, chosen before
    the checker ran for the first time; the parents' evening is always in the development set."""
    if basis.get("case") == PARENTS_EVENING:
        return DEV
    return HOLDOUT if basis.get("id") in HELD_OUT_IDS else DEV


def text_split(text: str) -> str:
    """Same rule as ``EvalBasics.InDevelopmentSet`` in C#: first byte of the SHA-256 modulo 10 below 7."""
    return DEV if hashlib.sha256(text.encode("utf-8")).digest()[0] % 10 < 7 else HOLDOUT


def words(text: str) -> int:
    return len(text.split())


def parse_number(text: str | None) -> Decimal | None:
    """First amount in ``text``, read like ``Amounts.Parse`` in C# (German and English separators, currency around it)."""
    if not text or not text.strip():
        return None
    match = re.search(r"-?\d[\d.,']*", text.replace("−", "-").replace(" ", ""))
    if not match:
        return None
    token = match.group(0).rstrip(".,'")
    negative = token.startswith("-")
    token = token.lstrip("-")
    if "." in token and "," in token:
        decimal_sep = "." if token.rfind(".") > token.rfind(",") else ","
        thousands = "," if decimal_sep == "." else "."
        normalized = token.replace(thousands, "").replace("'", "").replace(",", ".")
    elif re.fullmatch(r"\d{1,3}([.,'])\d{3}(\1\d{3})*", token):
        normalized = token.replace(".", "").replace(",", "").replace("'", "")
    else:
        normalized = token.replace("'", "").replace(",", ".")
    try:
        value = Decimal(normalized)
    except InvalidOperation:
        return None
    return -value if negative else value


def recalculate(trace: dict) -> Decimal | None:
    """Total of the items with their operators (multiplication first), like ``ArithmeticCorpusEvaluation.Recalculate``."""
    values = [parse_number(item) for item in trace.get("items") or []]
    if not values or any(v is None for v in values):
        return None
    ops = trace.get("ops") or ["+"] * (len(values) - 1)
    if len(ops) != len(values) - 1:
        return None
    terms, signs = [values[0]], [1]
    for op, value in zip(ops, values[1:]):
        if op == "+":
            terms.append(value)
            signs.append(1)
        elif op in ("-", "−"):
            terms.append(value)
            signs.append(-1)
        elif op in ("*", "×", "x", "·"):
            terms[-1] *= value
        else:
            return None
    return sum(t * s for t, s in zip(terms, signs))


def trace_problem(snippet: dict) -> str | None:
    """Why the trace of a snippet does not hold (same rules as ``ArithmeticCorpusEvaluation.TraceProblem``); None when it does."""
    trace = snippet.get("trace")
    text = snippet.get("text", "")
    if trace is None:
        return "geplant ohne Spur" if snippet.get("planted") else None
    if not isinstance(trace, dict):
        return "Spur ist kein Objekt"
    for name in ("total_line", "stated", "correct"):
        if not isinstance(trace.get(name), str) or not trace[name].strip():
            return f"Spur ohne {name}"
    if not isinstance(trace.get("items"), list) or not all(isinstance(i, str) for i in trace["items"]):
        return "Spur ohne Posten"
    if trace["total_line"] not in text:
        return "Summenzeile steht nicht im Text"
    if trace["stated"] not in trace["total_line"]:
        return "angegebene Summe steht nicht in der Summenzeile"
    if any(item not in text for item in trace["items"]):
        return "ein Posten steht nicht im Text"
    computed, stated, correct = recalculate(trace), parse_number(trace["stated"]), parse_number(trace["correct"])
    if computed is None or stated is None or correct is None:
        return "Zahlen nicht lesbar"
    if abs(computed - correct) > TOLERANCE:
        return f"Posten ergeben {computed}, nicht {trace['correct']}"
    adds = abs(computed - stated) <= TOLERANCE
    if snippet.get("planted") and adds:
        return "geplant, aber die Summe stimmt"
    if not snippet.get("planted") and not adds:
        return "sauber, aber die Summe stimmt nicht"
    return None


def _text(value, name: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{name} fehlt oder ist leer")
    return value.strip()


def _in_material(quote: str, files: list[dict]) -> bool:
    return any(quote in f["text"] for f in files)


def parse_generated(obj: dict, slot: dict) -> dict:
    """Validates a generator reply strictly and returns request, answers, profile, files (S0 for the text to revise) and errors."""
    if slot.get("case") == PARENTS_EVENING:
        unknown = set(obj) - {"clean_text"}
        if unknown:
            raise ValueError(f"unbekannte Felder: {sorted(unknown)}")
        clean = _text(obj.get("clean_text"), "clean_text")
        if not 80 <= words(clean) <= 600:
            raise ValueError("Text nicht 80-600 Wörter lang")
        return {"request": PARENTS_EVENING_REQUEST, "answers": [], "profile": None,
                "files": [{"id": "S1", "name": "notizen-elternabend.txt", "text": PARENTS_EVENING_NOTES}],
                "source_errors": [dict(e) for e in PARENTS_EVENING_ERRORS], "clean_text": clean}

    unknown = set(obj) - {"request", "answers", "profile", "files", "seed_text", "source_errors", "clean_text"}
    if unknown:
        raise ValueError(f"unbekannte Felder: {sorted(unknown)}")
    request = _text(obj.get("request"), "request")
    answers = obj.get("answers") or []
    if not isinstance(answers, list) or len(answers) > 3 or not all(isinstance(a, str) and a.strip() for a in answers):
        raise ValueError("answers ist keine Liste mit höchstens 3 Texten")
    profile = _parse_profile(obj.get("profile"))
    if slot["kind"] == "profile" and (profile is None or not profile.get("no_gos")):
        raise ValueError("profile braucht ein Profil mit no_gos")

    raw_files = obj.get("files") or []
    fewest = 0 if slot["kind"] == "revision" else 1
    if not isinstance(raw_files, list) or not fewest <= len(raw_files) <= 2:
        raise ValueError(f"files braucht {fewest}-2 Dateien")
    files = []
    seed = obj.get("seed_text")
    if slot["kind"] == "revision":
        seed = _text(seed, "seed_text")
        if words(seed) < 120:
            raise ValueError("seed_text kürzer als 120 Wörter")
        files.append({"id": "S0", "name": "text to revise", "text": seed})
    elif seed is not None:
        raise ValueError("seed_text nur bei revision")
    for item in raw_files:
        if not isinstance(item, dict) or set(item) - {"name", "text"}:
            raise ValueError("files-Eintrag braucht genau name und text")
        text = _text(item.get("text"), "files.text")
        if len(text) > 30_000:
            raise ValueError("eine Datei ist länger als 30.000 Zeichen")
        files.append({"id": f"S{len([f for f in files if f['id'] != 'S0']) + 1}", "name": _text(item.get("name"), "files.name"), "text": text})
    attached = sum(len(f["text"]) for f in files)
    if slot["size"] == "short" and not 1_000 <= attached <= 10_000:
        raise ValueError("kurze Grundlage braucht 1.000-10.000 Zeichen Material")
    if slot["size"] == "long" and attached < 12_000:
        raise ValueError("lange Grundlage braucht mindestens 12.000 Zeichen Material")

    errors = obj.get("source_errors") or []
    if not isinstance(errors, list):
        raise ValueError("source_errors ist keine Liste")
    if (slot["kind"] == "faulty_source") != bool(errors):
        raise ValueError("source_errors nur und immer bei faulty_source")
    parsed_errors = []
    for error in errors:
        if not isinstance(error, dict) or set(error) - {"kind", "quote", "quote_2", "stated"}:
            raise ValueError("source_errors-Eintrag hat unbekannte Felder")
        kind = error.get("kind")
        if kind not in SOURCE_ERROR_KINDS:
            raise ValueError(f"source_errors.kind {kind!r} nicht erlaubt")
        quote = _text(error.get("quote"), "source_errors.quote")
        quote_2 = error.get("quote_2")
        if kind in ("conflict", "superseded"):
            quote_2 = _text(quote_2, "source_errors.quote_2")
        stated = error.get("stated")
        if kind == "wrong_total":
            stated = _text(stated, "source_errors.stated")
        for q in (quote, quote_2):
            if q is not None and not _in_material(q, files):
                raise ValueError(f"Quellenzitat steht nicht wörtlich im Material: {q[:60]!r}")
        parsed_errors.append({"kind": kind, "quote": quote, "quote_2": quote_2, "stated": stated})

    clean = _text(obj.get("clean_text"), "clean_text")
    if slot["size"] == "short" and not 120 <= words(clean) <= 600:
        raise ValueError("kurzer Text nicht 120-600 Wörter lang")
    if slot["size"] == "long" and words(clean) < 1_500:
        raise ValueError("langer Text kürzer als 1.500 Wörter")
    return {"request": request, "answers": [a.strip() for a in answers], "profile": profile, "files": files,
            "source_errors": parsed_errors, "clean_text": clean}


def _parse_profile(value) -> dict | None:
    if value is None:
        return None
    fields = ("display_name", "role", "organization", "signature", "no_gos")
    if not isinstance(value, dict) or set(value) - set(fields):
        raise ValueError("profile hat unbekannte Felder")
    profile = {}
    for name in fields:
        field = value.get(name)
        if field is not None and not isinstance(field, str):
            raise ValueError(f"profile.{name} ist kein Text")
        profile[name] = field.strip() or None if isinstance(field, str) else None
    return profile


def parse_labels(obj: dict, text: str) -> list[dict]:
    """Validates a label reply: every quote word for word in the text and at least five words long."""
    if set(obj) - {"unsupported"} or not isinstance(obj.get("unsupported"), list):
        raise ValueError("Antwort braucht genau die Liste unsupported")
    labels = []
    for item in obj["unsupported"]:
        if not isinstance(item, dict) or set(item) - {"quote", "why"}:
            raise ValueError("unsupported-Eintrag hat unbekannte Felder")
        quote = _text(item.get("quote"), "quote")
        if quote not in text:
            raise ValueError(f"Zitat steht nicht wörtlich im Text: {quote[:60]!r}")
        if words(quote) < 5:
            raise ValueError("Zitat kürzer als fünf Wörter")
        labels.append({"quote": quote, "why": item.get("why") if isinstance(item.get("why"), str) else None})
    return labels


def required_plants(basis: dict) -> list[str]:
    return ["invented_contact"] if basis.get("case") == PARENTS_EVENING else []


def parse_plants(obj: dict, basis: dict) -> tuple[str, list[dict]]:
    """Validates a plant reply: 2-4 known mistakes, every wording word for word in the copy and new against the clean text."""
    if set(obj) - {"text", "plants"}:
        raise ValueError("Antwort braucht genau text und plants")
    text = _text(obj.get("text"), "text")
    plants = obj.get("plants")
    if not isinstance(plants, list) or not 2 <= len(plants) <= 4:
        raise ValueError("plants braucht 2-4 Einträge")
    if text == basis["clean_text"]:
        raise ValueError("Kopie ist unverändert")
    parsed = []
    for plant in plants:
        if not isinstance(plant, dict) or set(plant) - {"kind", "inserted"}:
            raise ValueError("plants-Eintrag hat unbekannte Felder")
        kind = plant.get("kind")
        if kind not in PLANT_KINDS:
            raise ValueError(f"plants.kind {kind!r} nicht erlaubt")
        inserted = _text(plant.get("inserted"), "inserted")
        if inserted not in text:
            raise ValueError(f"eingefügter Wortlaut steht nicht wörtlich in der Kopie: {inserted[:60]!r}")
        if inserted in basis["clean_text"]:
            raise ValueError(f"eingefügter Wortlaut steht schon im sauberen Text: {inserted[:60]!r}")
        if words(inserted) < 5:
            raise ValueError("eingefügter Wortlaut kürzer als fünf Wörter")
        parsed.append({"kind": kind, "inserted": inserted})
    missing = [k for k in required_plants(basis) if k not in {p["kind"] for p in parsed}]
    if missing:
        raise ValueError(f"Pflichtfehler fehlt: {missing}")
    return text, parsed


def _material(basis: dict) -> str:
    shown = {"request": basis["request"], "answers_by_person": basis.get("answers") or [], "about_the_person": basis.get("profile"),
             "files": [{"id": f["id"], "name": f["name"], "text": f["text"]} for f in basis.get("files") or []]}
    return json.dumps(shown, ensure_ascii=False, indent=2)


def generator_prompt(slot: dict) -> str:
    if slot.get("case") == PARENTS_EVENING:
        return f"""A parent wants a short letter to the other parents. Request and notes are fixed:

Request: {PARENTS_EVENING_REQUEST}

Notes (file S1):
{PARENTS_EVENING_NOTES}
Write the letter (German, 150-350 words) using only what the notes say. Leave out the internal note. Do not invent a way to
reply, a contact, an account number or anything else the notes do not give; use a placeholder in square brackets for a needed
fact that is missing.

Reply with exactly one JSON object: {{"clean_text": "..."}}"""

    size_rule = ("text 250-550 words; 1-2 files, together 2,000-8,000 characters" if slot["size"] == "short"
                 else "text 2,000-2,600 words; 1-2 files, together 15,000-30,000 characters, each at most 30,000")
    kind_rules = {
        "plain": "a normal order with attached files.",
        "revision": "the person attaches their own draft (`seed_text`, 150-400 words, with the flaws a real draft has) and asks for a "
                    "revision; `clean_text` is the revised version. Further files are background material.",
        "profile": "the person has a saved profile with a concrete rule in `no_gos` (for example: never use exclamation marks, never "
                   "mention prices, never write \"we\" for the company). The clean text obeys it.",
        "faulty_source": "the material itself has a mistake: " + CASE_RULES.get(slot.get("case") or "", "") +
                         " Copy the passages word for word into `source_errors`. The clean text does not repeat the mistake silently.",
    }
    return f"""Invent ONE test case: a person orders a text from a writing assistant and attaches files.

Language of request, files and text: {LANGUAGE_NAMES[slot['lang']]}.
Topic area (vary the concrete situation): {slot['area']}
Size: {size_rule}.
Kind: {kind_rules[slot['kind']]}

Rules:
- Everything is fictional: invented people, organisations, places, dates and amounts; no real persons or companies.
- `request` is what the person types (1-6 sentences, own words). `answers` are 0-2 short answers the person gave to questions
  of the assistant (for example \"Length: about 300 words\"), or an empty list.
- `files`: [{{"name": "...", "text": "full plain text"}}], realistic notes, minutes, lists, mails or tables with concrete facts.
- `clean_text` is the finished text. It uses ONLY facts from the request, the answers, the profile and the files: no invented
  names, numbers, dates, places, contacts, attachments or promises. General phrases any reader accepts are fine. A needed fact
  that is missing becomes a placeholder in square brackets.
- `profile`: null, or {{"display_name", "role", "organization", "signature", "no_gos"}} with null for missing fields.
- `seed_text`: null unless the kind asks for it. `source_errors`: [] unless the kind asks for it; each is
  {{"kind": "wrong_total|conflict|superseded", "quote": "...", "quote_2": null or "...", "stated": null or "the stated total"}}.

Reply with exactly one JSON object: {{"request": "...", "answers": [], "profile": null, "files": [], "seed_text": null,
"source_errors": [], "clean_text": "..."}}"""


def labeler_prompt(basis: dict) -> str:
    return f"""Below are the material a text was written from and the text. List every statement of the text that the material does
not back: names, numbers, dates, places, prices, events, actions that happened or will happen, promises, ways to reply or get in
touch, enclosures and attachments. Facts the material states in other words are backed. General statements any reader would
accept need no backing. Placeholders in square brackets are fine. Do not judge style, length or tone.

Copy each passage word for word from the text (at least five words, at most two sentences) and say briefly why.

Material:
{_material(basis)}

Text:
{basis['clean_text']}

Reply with exactly one JSON object: {{"unsupported": [{{"quote": "...", "why": "..."}}]}} (an empty list when everything is backed)."""


def plant_prompt(basis: dict) -> str:
    kinds = "\n".join(f"- {name}: {rule}" for name, rule in PLANT_KINDS.items())
    required = required_plants(basis)
    must = f"\nOne of the mistakes MUST be of kind: {', '.join(required)}." if required else ""
    return f"""Below are the material a text was written from and the text. Make a copy of the text with 2 to 4 deliberate mistakes
of these kinds (use only kinds that fit this material):
{kinds}{must}

Change only the sentences that carry a mistake; keep everything else word for word. The mistakes must read naturally. For every
mistake give its kind and the complete sentence of the new copy that carries it, copied word for word (\"inserted\", at least
five words, not present in the original text).

Material:
{_material(basis)}

Text:
{basis['clean_text']}

Reply with exactly one JSON object: {{"text": "the complete copy", "plants": [{{"kind": "...", "inserted": "..."}}]}}"""


def _log(message: str) -> None:
    print(message, file=sys.stderr, flush=True)


def read_entries(path: Path) -> list[dict]:
    """Reads a JSONL file; a corrupt line aborts, so nothing is silently dropped on the next rewrite."""
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


def write_entries(path: Path, entries: list[dict]) -> None:
    """Replaces the file atomically: temporary file in the same directory, then ``os.replace``."""
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, tmp = tempfile.mkstemp(prefix=path.name + ".", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            for entry in entries:
                handle.write(json.dumps(entry, ensure_ascii=False) + "\n")
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(tmp, path)
    except BaseException:
        if os.path.exists(tmp):
            os.unlink(tmp)
        raise


def calls_log(path: Path) -> Path:
    return path.with_name(path.name + ".calls.log")


class Budget:
    """Counts real CLI attempts against ``--max-calls`` and against the fixed cap of the corpus (all invocations, from the log)."""

    def __init__(self, path: Path, max_calls: int, ceiling: int):
        self.log_path = calls_log(path)
        previous = len([line for line in self.log_path.read_text(encoding="utf-8").splitlines() if line.strip()]) \
            if self.log_path.exists() else 0
        self.limit = max(0, min(max_calls, ceiling - previous))
        self.previous = previous
        self.ceiling = ceiling
        self.used = 0

    @property
    def remaining(self) -> int:
        return self.limit - self.used

    def exhausted(self) -> bool:
        return self.used >= self.limit

    def log(self, record: dict) -> None:
        self.used += 1
        entry = {k: record.get(k) for k in ("label", "model", "attempt", "ok", "error", "duration_s")}
        self.log_path.parent.mkdir(parents=True, exist_ok=True)
        with self.log_path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(entry, ensure_ascii=False) + "\n")
            handle.flush()
            os.fsync(handle.fileno())
        state = "ok" if record.get("ok") else f"Fehlversuch ({record.get('error', '?')})"
        _log(f"  Aufruf {self.previous + self.used} von höchstens {self.ceiling}: {record.get('label')} {record.get('model')} {state}")

    def tries(self) -> int:
        return max(1, min(2, self.remaining))


def _summary() -> dict:
    return {"done": 0, "failed": 0, "skipped": 0, "stopped_by_limit": False, "stopped_by_max_calls": False}


def _call(prompt: str, *, model: str, system: str, validate, budget: Budget, label: str, runner: Runner):
    return call_claude(prompt, model=model, system_prompt=system, timeout_s=TIMEOUT_S, runner=runner, json_out=True,
                       validate=validate, tries=budget.tries(), log=budget.log, label=label)


def generate(path: Path, *, max_calls: int, model: str = "sonnet", runner: Runner = run_cli) -> dict:
    """Writes the missing bases, one call each; bases already in the file are kept and skipped."""
    entries = {e["id"]: e for e in read_entries(path)}
    budget = Budget(path, max_calls, FACT_CEILING)
    summary = _summary()
    for slot in SLOTS:
        if slot["id"] in entries:
            summary["skipped"] += 1
            continue
        if budget.exhausted():
            summary["stopped_by_max_calls"] = True
            break
        _log(f"{slot['id']}: {slot['size']}/{slot['lang']}/{slot['kind']} erzeugen")
        try:
            reply = _call(generator_prompt(slot), model=model, system=GENERATOR_SYSTEM,
                          validate=lambda o, s=slot: parse_generated(o, s), budget=budget, label=f"generate {slot['id']}", runner=runner)
            parsed = parse_generated(reply.obj, slot)
        except LimitError as exc:
            _log(f"Nutzungslimit erreicht, Lauf angehalten ({exc}). Mit demselben Befehl später fortsetzen.")
            summary["stopped_by_limit"] = True
            break
        except (ClaudeCallError, ValueError) as exc:
            _log(f"{slot['id']}: verworfen, wird beim nächsten Lauf neu versucht ({exc})")
            summary["failed"] += 1
            continue
        entries[slot["id"]] = {"id": slot["id"], "size": slot["size"], "lang": slot["lang"], "kind": slot["kind"], "case": slot["case"],
                               **parsed, "generator_model": model}
        write_entries(path, [entries[s["id"]] for s in SLOTS if s["id"] in entries])
        summary["done"] += 1
    summary["calls"] = budget.used
    summary["missing"] = sum(1 for s in SLOTS if s["id"] not in entries)
    return summary


def label(path: Path, *, max_calls: int, model: str = "opus", runner: Runner = run_cli) -> dict:
    """Labels every basis without ``labels`` with an independent call; the generator never sees the result."""
    return _stage(path, max_calls=max_calls, model=model, runner=runner, field="labels", system=LABELER_SYSTEM,
                  prompt=labeler_prompt, parse=lambda o, b: parse_labels(o, b["clean_text"]),
                  store=lambda b, value, m: b.update({"labels": value, "label_model": m}))


def plant(path: Path, *, max_calls: int, model: str = "sonnet", runner: Runner = run_cli) -> dict:
    """Plants 2-4 mistakes into a copy of every basis without ``planted_text``; every wording is checked word for word."""
    def store(basis, value, m):
        text, plants = value
        basis.update({"planted_text": text, "plants": plants, "plant_model": m})
    return _stage(path, max_calls=max_calls, model=model, runner=runner, field="planted_text", system=PLANTER_SYSTEM,
                  prompt=plant_prompt, parse=parse_plants, store=store)


def _stage(path: Path, *, max_calls: int, model: str, runner: Runner, field: str, system: str, prompt, parse, store) -> dict:
    entries = read_entries(path)
    budget = Budget(path, max_calls, FACT_CEILING)
    summary = _summary()
    for basis in entries:
        if field in basis:
            summary["skipped"] += 1
            continue
        if budget.exhausted():
            summary["stopped_by_max_calls"] = True
            break
        _log(f"{basis['id']}: {field}")
        try:
            reply = _call(prompt(basis), model=model, system=system, validate=lambda o, b=basis: parse(o, b),
                          budget=budget, label=f"{field} {basis['id']}", runner=runner)
            value = parse(reply.obj, basis)
        except LimitError as exc:
            _log(f"Nutzungslimit erreicht, Lauf angehalten ({exc}). Mit demselben Befehl später fortsetzen.")
            summary["stopped_by_limit"] = True
            break
        except (ClaudeCallError, ValueError) as exc:
            _log(f"{basis['id']}: verworfen, wird beim nächsten Lauf neu versucht ({exc})")
            summary["failed"] += 1
            continue
        store(basis, value, model)
        write_entries(path, entries)
        summary["done"] += 1
    summary["calls"] = budget.used
    summary["missing"] = sum(1 for s in SLOTS if not any(e["id"] == s["id"] and field in e for e in entries))
    return summary


def arithmetic_slots() -> list[dict]:
    """60 fixed slots: 30 planted wrong totals, 15 right calculations, 15 numbers without calculation; forms and languages rotate."""
    forms = ("expression", "list", "sentence", "table", "notes")
    plain_numbers = ("dates", "house_numbers", "times", "phone_numbers", "prices_without_total")
    slots = []
    for i in range(ARITHMETIC_SIZE):
        lang = "en" if i % 3 == 2 else "de"
        if i % 2 == 0:
            slots.append({"id": f"ar-{i + 1:02d}", "lang": lang, "form": forms[(i // 2) % len(forms)], "planted": True})
        elif i % 4 == 1:
            slots.append({"id": f"ar-{i + 1:02d}", "lang": lang, "form": forms[(i // 4) % len(forms)], "planted": False})
        else:
            slots.append({"id": f"ar-{i + 1:02d}", "lang": lang, "form": "none", "planted": False,
                          "numbers": plain_numbers[(i // 4) % len(plain_numbers)]})
    return slots


ARITHMETIC_FORMS = {
    "expression": "a written calculation in one line, such as \"3 x 24 € = 72 €\" or \"22 + 31 + 19 = 72\" (+, -, x or * allowed)",
    "list": "2-5 lines with exactly one amount each, directly followed by a line with one amount and a sum word",
    "sentence": "one sentence with at least two amounts of the same unit before a sum word and exactly one amount after it",
    "table": "a markdown table with an amount column whose last row has a sum word in the first cell",
    "notes": "short meeting notes with a few items with amounts and a total line",
}


def arithmetic_prompt(slots: list[dict]) -> str:
    specs = []
    for slot in slots:
        if slot["form"] == "none":
            what = (f"numbers WITHOUT any calculation and without a total: {slot['numbers'].replace('_', ' ')}; "
                    "no sum word used as a total; trace null")
        else:
            total = "the stated total is WRONG (a plausible slip: typo, forgotten item, swapped digits)" if slot["planted"] \
                else "the stated total is RIGHT"
            what = f"form {slot['form']}: {ARITHMETIC_FORMS[slot['form']]}; {total}"
        specs.append(f"- id {slot['id']}, language {LANGUAGE_NAMES[slot['lang']]}: {what}")
    sum_words = "; ".join(f"{LANGUAGE_NAMES[k]}: {v}" for k, v in SUM_WORDS.items())
    return f"""Write {len(slots)} short, realistic, fictional snippets (2-8 lines each) as they appear in notes, lists, letters or tables
of associations, small firms or households. Amounts in the spelling of the language (German 1.234,50 €, English $1,234.50 or
1,234.50 EUR). Sum words: {sum_words}.

{chr(10).join(specs)}

For every snippet with a calculation give the trace: "total_line" = the exact line (or sentence) with the stated total, copied
word for word; "items" = the amounts as written in the text, word for word; "ops" = the operators between them (empty list when
all are added); "stated" = the stated total as written; "correct" = the right result.

Reply with exactly one JSON object: {{"snippets": [{{"id": "...", "text": "...", "trace": null or {{"total_line": "...",
"items": ["..."], "ops": [], "stated": "...", "correct": "..."}}}}]}}"""


def parse_arithmetic_reply(obj: dict, wanted: list[dict]) -> tuple[list[dict], list[str]]:
    """Accepts every snippet of the reply whose trace the code can recompute; returns the snippets and why others were dropped."""
    if set(obj) - {"snippets"} or not isinstance(obj.get("snippets"), list):
        raise ValueError("Antwort braucht genau die Liste snippets")
    by_id = {s["id"]: s for s in wanted}
    accepted, dropped = [], []
    for raw in obj["snippets"]:
        if not isinstance(raw, dict) or raw.get("id") not in by_id:
            dropped.append(f"unbekannter Ausschnitt {raw.get('id') if isinstance(raw, dict) else raw!r}")
            continue
        slot = by_id.pop(raw["id"])
        text = raw.get("text")
        if not isinstance(text, str) or not text.strip():
            dropped.append(f"{slot['id']}: kein Text")
            continue
        snippet = {"id": slot["id"], "lang": slot["lang"], "form": slot["form"], "text": text.strip(), "planted": slot["planted"],
                   "trace": raw.get("trace")}
        if slot["form"] == "none" and snippet["trace"] is not None:
            dropped.append(f"{slot['id']}: Zahlen ohne Rechnung, aber mit Spur")
            continue
        if slot["form"] != "none" and snippet["trace"] is None:
            dropped.append(f"{slot['id']}: Rechnung ohne Spur")
            continue
        if snippet["trace"] is not None:
            snippet["trace"] = {"total_line": snippet["trace"].get("total_line"), "items": snippet["trace"].get("items"),
                                "ops": snippet["trace"].get("ops") or [], "stated": snippet["trace"].get("stated"),
                                "correct": snippet["trace"].get("correct")} if isinstance(snippet["trace"], dict) else snippet["trace"]
        problem = trace_problem(snippet)
        if problem:
            dropped.append(f"{slot['id']}: {problem}")
            continue
        accepted.append(snippet)
    dropped.extend(f"{sid}: fehlt in der Antwort" for sid in by_id)
    return accepted, dropped


def arithmetic(path: Path, *, max_calls: int, model: str = "sonnet", runner: Runner = run_cli) -> dict:
    """Writes the missing snippets, five slots per call; a snippet whose trace does not hold stays missing for a later call."""
    entries = {e["id"]: e for e in read_entries(path)}
    budget = Budget(path, max_calls, ARITHMETIC_CEILING)
    summary = _summary()
    slots = arithmetic_slots()
    while True:
        missing = [s for s in slots if s["id"] not in entries]
        if not missing:
            break
        if budget.exhausted():
            summary["stopped_by_max_calls"] = True
            break
        batch = missing[:SNIPPETS_PER_CALL]
        _log(f"Ausschnitte {batch[0]['id']}–{batch[-1]['id']}")
        try:
            reply = call_claude(arithmetic_prompt(batch), model=model, system_prompt=ARITHMETIC_SYSTEM, timeout_s=TIMEOUT_S,
                                runner=runner, json_out=True, validate=lambda o, b=batch: parse_arithmetic_reply(o, b),
                                tries=1, log=budget.log, label=f"arithmetic {batch[0]['id']}")
            accepted, dropped = parse_arithmetic_reply(reply.obj, batch)
        except LimitError as exc:
            _log(f"Nutzungslimit erreicht, Lauf angehalten ({exc}). Mit demselben Befehl später fortsetzen.")
            summary["stopped_by_limit"] = True
            break
        except (ClaudeCallError, ValueError) as exc:
            _log(f"Antwort verworfen ({exc})")
            summary["failed"] += 1
            continue
        for reason in dropped:
            _log(f"  verworfen: {reason}")
        summary["failed"] += len(dropped)
        for snippet in accepted:
            entries[snippet["id"]] = {**snippet, "generator_model": model}
        summary["done"] += len(accepted)
        write_entries(path, [entries[s["id"]] for s in slots if s["id"] in entries])
    summary["calls"] = budget.used
    summary["missing"] = sum(1 for s in slots if s["id"] not in entries)
    return summary


def stats(path: Path) -> dict[str, Counter]:
    """Counts of a fact-check corpus (bases) or an arithmetic corpus (snippets) and the calls used so far."""
    entries = read_entries(path)
    counters: dict[str, Counter] = {}
    for entry in entries:
        if "planted" in entry:
            keys = {"form": entry.get("form"), "lang": entry.get("lang"), "planted": entry.get("planted"),
                    "split": text_split(entry.get("text", ""))}
        else:
            keys = {"size": entry.get("size"), "lang": entry.get("lang"), "kind": entry.get("kind"), "split": split(entry),
                    "labelled": "labels" in entry, "planted": "planted_text" in entry,
                    "clean": "labels" in entry and not entry["labels"]}
            for plant_entry in entry.get("plants") or []:
                counters.setdefault("plant_kind", Counter())[plant_entry["kind"]] += 1
            counters.setdefault("label_marks", Counter())["marks"] += len(entry.get("labels") or [])
        for name, value in keys.items():
            counters.setdefault(name, Counter())[str(value)] += 1
    log = calls_log(path)
    counters["calls"] = Counter({"logged": len([l for l in log.read_text(encoding="utf-8").splitlines() if l.strip()]) if log.exists() else 0})
    return counters


def _positive(value: str) -> int:
    number = int(value)
    if number < 1:
        raise argparse.ArgumentTypeError("muss mindestens 1 sein")
    return number


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="python3 -m geef_eval.factcheck_corpus",
                                     description="Synthetische Korpora für Faktenprüfer (V2) und Rechenprüfung (V3)")
    sub = parser.add_subparsers(dest="command", required=True)
    for name, help_text, default_model in (("generate", "Grundlagen erzeugen (ein Aufruf je Grundlage)", "sonnet"),
                                           ("label", "saubere Texte unabhängig beschriften", "opus"),
                                           ("plant", "Fehler in eine Kopie pflanzen", "sonnet")):
        cmd = sub.add_parser(name, help=help_text)
        cmd.add_argument("--corpus", required=True, help="JSONL-Datei der Grundlagen; wird atomar umgeschrieben")
        cmd.add_argument("--max-calls", type=_positive, required=True, help=f"Höchstzahl der Aufrufe dieses Laufs (Kappe gesamt {FACT_CEILING})")
        cmd.add_argument("--model", default=default_model)
    ar = sub.add_parser("arithmetic", help="Ausschnitte für die Rechenprüfung (5 je Aufruf)")
    ar.add_argument("--out", required=True, help="JSONL-Datei der Ausschnitte; vorhandene bleiben")
    ar.add_argument("--max-calls", type=_positive, required=True, help=f"Höchstzahl der Aufrufe dieses Laufs (Kappe gesamt {ARITHMETIC_CEILING})")
    ar.add_argument("--model", default="sonnet")
    st = sub.add_parser("stats", help="Zählung je Größe, Sprache, Art, Teilung, Beschriftung, Fehlerart und Aufrufe")
    st.add_argument("--corpus", required=True)
    return parser


def main(argv: list[str] | None = None, runner: Runner = run_cli) -> int:
    args = build_parser().parse_args(argv)
    if args.command == "stats":
        for name, counter in stats(Path(args.corpus)).items():
            print(f"{name}: " + ", ".join(f"{k}={v}" for k, v in sorted(counter.items())))
        return 0
    if args.command == "arithmetic":
        summary = arithmetic(Path(args.out), max_calls=args.max_calls, model=args.model, runner=runner)
    else:
        command = {"generate": generate, "label": label, "plant": plant}[args.command]
        summary = command(Path(args.corpus), max_calls=args.max_calls, model=args.model, runner=runner)
    print(f"{args.command}: {summary}")
    if summary["stopped_by_limit"]:
        return 3
    if summary["missing"]:
        _log(f"{summary['missing']} Einträge fehlen; der Korpus ist unvollständig und nicht auswertbar. "
             "Mit demselben Befehl fortsetzen (die Kappe gilt über alle Läufe).")
        return 4
    return 0


if __name__ == "__main__":
    sys.exit(main())
