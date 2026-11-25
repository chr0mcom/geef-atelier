"""Gate N of stage 5 (contract stage 5, 8 and 9): changing a delivered text ("Change something", ``revise_order``).

Data: 8 changes at 4 fresh orders of the measuring user (5 on the web, 3 over MCP), the fact check as set for that user. The
tool never starts or stops a change and never calls a model; it reads what the Atelier shows:

- ``snapshot``: stores the answer of ``get_order_result`` and of ``export_order`` (Markdown, without a version) of one order
  under a label, for example before a change (``v1``) and after it (``v2``). Only ``get_order_result`` shows the open points of
  a version, and only while that version is the one shown, so the snapshot of the base has to be taken before the change.
- ``evaluate``: reads a plan of the 8 cases (which order, which snapshots, which kind of change) and decides gate N.

Thresholds (contract 8, fixed in advance, taken over word for word):

- **N1** local (3: change a date or name, add a sentence, delete a paragraph): unchanged paragraphs ≥ paragraphs(v1) − 2, words
  ±15 %, in the first case the new detail stands word for word in the text.
- **N2** tone (2): ±15 %, new ``not_backed`` with a quote not in v1 ≤ 1 together.
- **N3** Shorter 40–85 %, Longer 115–160 % (one each), Longer: new ``not_backed`` ≤ 1.
- **N4** (all, in code): every open passage of v1 whose quote stands in v2, and every open point of the writer of v1, appears in
  v2; every placeholder in the v2 text is listed in ``placeholders``.
- **N5** start v3 and stop it → result and export show v2 with the sentence.
- Not evaluable: fewer than 6 of 8 with a text; export missing.

How this tool reads them (its own code, independent of the C# ``ParagraphDiff``): a paragraph is a block between blank lines,
compared after collapsing white space, case and quote marks unchanged; unchanged paragraphs are the longest common subsequence of
both lists. A word is a run of characters between white space with at least one letter or digit. A quote "stands in" a text when
it stands there as whole words after normalising white space, invisible characters, quote marks and dashes, ignoring case (as the
Atelier's ``QuoteVerifier`` does). A passage of v1 appears in v2 when a passage of v2 (of any kind) contains its quote. A point of
the writer appears in v2 under ``openPoints`` or under ``earlierOpenPoints`` with the version of v1, compared without case, closing
punctuation and one leading article. Placeholders are text in square brackets (2 to 80 characters, one line), except links,
images, check boxes and footnotes.

Never passed without data: a case counts only with readable snapshots of the planned order, a base with a text and, after a change,
a text of a newer version. A criterion with no such case (Shorter, Longer, the first local case, every tone case) is not
evaluable, as is N4 when a case with a text lacks the fact check (``factCheck.state`` other than ``checked`` or ``open_points``) or
one of its lists, N2/N3 when the fact check did not run, N5 without ``latestVersion`` or without the export, and the whole gate
when the plan does not have the composition of the contract (3 local with the expected detail on the first, 2 tone, 1 Shorter,
1 Longer, 1 stop; 5 web and 3 MCP; 4 orders).

Snapshots and the report hold texts of real orders: the output directory must be outside every Git tree.

Usage (from ``tools/Eval``)::

    python3 -m geef_eval.revise_check snapshot --order <orderId> --label v1 --out-dir <dir> [--env-file <file>]
    python3 -m geef_eval.revise_check evaluate --plan <plan.json> --out-dir <dir>

Plan (JSON, outside Git; the order of the cases decides which local case is the first)::

    {"cases": [
      {"case": "local-date", "kind": "local", "order": "<orderId>", "channel": "web", "base": "v1", "new": "v2",
       "expect": "<new detail, word for word>"},
      {"case": "tone-formal", "kind": "tone", "order": "<orderId>", "channel": "mcp", "base": "v1", "new": "v2"},
      {"case": "stop", "kind": "stop", "order": "<orderId>", "channel": "web", "base": "v2", "new": "after-stop",
       "stopped_version": 3}
    ]}

``kind`` is ``local``, ``tone``, ``shorter``, ``longer`` or ``stop``; ``channel`` ``web`` or ``mcp``.

A failed N4 or N5 is a finding of its own (``halt``: fix it, never switch on for everyone): the gate is then ``failed`` even when
other data is missing. Failed N1–N3 with too little data elsewhere leave the gate ``not_evaluable``.

Exit codes: 0 passed, 1 failed, 2 wrong usage or unreadable plan, 3 sign-in or connection failed (``snapshot``), 4 not evaluable.
"""
from __future__ import annotations

import argparse
import base64
import binascii
import json
import re
import sys
import unicodedata
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
from typing import Callable, Iterator

from .cli import inside_git_worktree
from .mcp import McpError
from .mcp_contract import ContractClient, SignInError, connect

KINDS = {"local": 3, "tone": 2, "shorter": 1, "longer": 1, "stop": 1}
CHANNELS = {"web": 5, "mcp": 3}
ORDER_COUNT = 4
CASE_COUNT = 8
MIN_WITH_TEXT = 6
MAX_CHANGED_PARAGRAPHS = 2
WORD_TOLERANCE = 0.15
SHORTER_RANGE = (0.40, 0.85)
LONGER_RANGE = (1.15, 1.60)
MAX_NEW_NOT_BACKED = 1
NOT_BACKED = "not_backed"
CHECK_RAN = frozenset({"checked", "open_points"})
NOT_FINISHED_FORMAT = "Version {0} could not be finished. This is version {1}."
LEADING_ARTICLES = frozenset({"der", "die", "das", "the"})
CLOSING_PUNCTUATION = ".!?;:…"
QUOTES = str.maketrans({"„": '"', "“": '"', "”": '"', "«": '"', "»": '"', "‟": '"', "″": '"',
                        "‚": "'", "‘": "'", "’": "'", "‹": "'", "›": "'", "‛": "'", "′": "'",
                        "‐": "-", "‑": "-", "‒": "-", "–": "-", "—": "-", "―": "-", "−": "-"})
ORDER_ID_RE = re.compile(r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")
LABEL_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,39}$")
PLACEHOLDER_RE = re.compile(r"\[[^\[\]\n]{2,80}\]")
FOOTNOTE_RE = re.compile(r"^[\d\s,;–-]+$|^\^")
EXPORT_FILE_VERSION_RE = re.compile(r"-v(\d+)\.md$")
FOLLOW_UP = {
    "halt": "Halt: Fehler beheben, Nachbessern nie für alle einschalten (N4/N5).",
    "instruction": "N1–N3 verfehlt: eine Runde nur am Satz RevisionInstruction, dann einmal neu; besteht es nicht, bleibt Revise "
                   "beim Messnutzer.",
    "passed": "N bestanden: zusammen mit Tor M eigener Commit Orders:Revise=On (Zusatz D-075).",
}


def _clean(text: object) -> str:
    value = unicodedata.normalize("NFC", str(text or ""))
    return "".join(ch for ch in value if ch != "\u00ad" and unicodedata.category(ch) != "Cf")


def collapse(text: object) -> str:
    """NFC, without invisible characters, white space collapsed to one blank, trimmed."""
    return " ".join(_clean(text).split())


def normalize_quote(text: object) -> str:
    """:func:`collapse` plus unified quote marks and dashes, ignoring case."""
    return collapse(text).translate(QUOTES).casefold()


def _whole_words(haystack: str, needle: str) -> bool:
    if not needle:
        return False
    start = haystack.find(needle)
    while start >= 0:
        end = start + len(needle)
        clean_start = start == 0 or not needle[0].isalnum() or not haystack[start - 1].isalnum()
        clean_end = end >= len(haystack) or not needle[-1].isalnum() or not haystack[end].isalnum()
        if clean_start and clean_end:
            return True
        start = haystack.find(needle, start + 1)
    return False


def stands_in(text: object, quote: object) -> bool:
    """True when ``quote`` stands in ``text`` as whole words after :func:`normalize_quote`; a blank quote never stands."""
    return _whole_words(normalize_quote(text), normalize_quote(quote))


def contains_literal(text: object, literal: object) -> bool:
    """True when ``literal`` stands in ``text`` word for word (case counts) as whole words, white space and quote marks unified."""
    return _whole_words(collapse(text).translate(QUOTES), collapse(literal).translate(QUOTES))


def paragraphs(text: object) -> list[str]:
    """Blocks between blank lines, white space collapsed; empty blocks dropped."""
    return [p for p in (collapse(block) for block in re.split(r"\n[ \t\r\f\v]*\n", _clean(text).replace("\r\n", "\n"))) if p]


def common_paragraphs(old: list[str], new: list[str]) -> int:
    """Length of the longest common subsequence of two paragraph lists (exact comparison)."""
    previous = [0] * (len(new) + 1)
    for item in old:
        current = [0]
        for index, other in enumerate(new):
            current.append(previous[index] + 1 if item == other else max(previous[index + 1], current[index]))
        previous = current
    return previous[-1]


def paragraph_diff(old_text: object, new_text: object) -> dict:
    old, new = paragraphs(old_text), paragraphs(new_text)
    unchanged = common_paragraphs(old, new)
    return {"old": len(old), "new": len(new), "unchanged": unchanged, "removed": len(old) - unchanged,
            "added": len(new) - unchanged}


def count_words(text: object) -> int:
    """Runs of characters between white space that hold at least one letter or digit."""
    return sum(1 for token in _clean(text).split() if any(ch.isalnum() for ch in token))


def point_key(text: object) -> str:
    """Key of a point of the writer: normalised, without closing punctuation and one leading article, ignoring case."""
    key = normalize_quote(text).rstrip(CLOSING_PUNCTUATION + " ").strip()
    first, _, rest = key.partition(" ")
    return rest if rest and first in LEADING_ARTICLES else key


def find_placeholders(text: object) -> list[str]:
    """Placeholders in square brackets, without links ``[a](b)``, references ``[a]:``, images, check boxes and footnotes."""
    draft = str(text or "")
    found: list[str] = []
    for match in PLACEHOLDER_RE.finditer(draft):
        before = draft[match.start() - 1] if match.start() > 0 else ""
        after = draft[match.end()] if match.end() < len(draft) else ""
        inner = match.group()[1:-1].strip()
        if (after and after in "(:[]") or (before and before in "[!"):
            continue
        if not inner or inner in ("x", "X") or FOOTNOTE_RE.search(inner):
            continue
        if match.group() not in found:
            found.append(match.group())
    return found


def all_strings(value: object) -> Iterator[str]:
    if isinstance(value, str):
        yield value
    elif isinstance(value, dict):
        for item in value.values():
            yield from all_strings(item)
    elif isinstance(value, list):
        for item in value:
            yield from all_strings(item)


def _ratio(base_words: int, new_words: int) -> float | None:
    return new_words / base_words if base_words else None


def _within(ratio: float | None, low: float, high: float) -> bool:
    return ratio is not None and low - 1e-9 <= ratio <= high + 1e-9


def snapshot_path(root: Path, order: str, label: str) -> Path:
    if not ORDER_ID_RE.match(order) or not LABEL_RE.match(label):
        raise ValueError(f"ungültige Auftragskennung oder Bezeichnung: {order!r} / {label!r}")
    return root / "snapshots" / order.lower() / f"{label}.json"


def take_snapshot(client: ContractClient, root: Path, order: str, label: str) -> dict:
    """Stores ``get_order_result`` and ``export_order`` (md, without version) of ``order`` under ``label``; never overwrites."""
    path = snapshot_path(root, order, label)
    if path.exists():
        raise FileExistsError(f"{path} gibt es schon; Schnappschüsse sind Belege, eine neue Bezeichnung wählen")
    result = client.call_tool("get_order_result", {"orderId": order})
    snapshot = {"orderId": order, "label": label, "fetchedAt": datetime.now(timezone.utc).isoformat(timespec="seconds"),
                "result": result, "export": None, "exportError": None}
    try:
        snapshot["export"] = {"md": client.call_tool("export_order", {"orderId": order, "format": "md"})}
    except McpError as exc:
        snapshot["exportError"] = str(exc)[:500]
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(snapshot, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    return snapshot


def load_plan(path: Path) -> list[dict]:
    """The cases of the plan; raises ValueError for a plan that cannot be used at all."""
    plan = json.loads(path.read_text(encoding="utf-8"))
    cases = plan.get("cases") if isinstance(plan, dict) else None
    if not isinstance(cases, list) or not cases:
        raise ValueError("Plan ohne Liste cases")
    names = []
    for case in cases:
        if not isinstance(case, dict):
            raise ValueError("Fall ist kein Objekt")
        for field in ("case", "kind", "order", "channel", "base", "new"):
            if not isinstance(case.get(field), str) or not case[field].strip():
                raise ValueError(f"Fall {case.get('case')!r} ohne {field}")
        snapshot_path(Path("."), case["order"], case["base"])
        snapshot_path(Path("."), case["order"], case["new"])
        names.append(case["case"])
    if twice := sorted({n for n in names if names.count(n) > 1}):
        raise ValueError(f"Fälle doppelt: {', '.join(twice)}")
    return cases


def plan_problems(cases: list[dict]) -> list[str]:
    """Where the plan differs from the data of gate N (contract 8)."""
    problems = []
    kinds = Counter(c["kind"] for c in cases)
    if unknown := sorted(set(kinds) - set(KINDS)):
        problems.append(f"unbekannte Art: {', '.join(unknown)}")
    if any(kinds.get(k, 0) != n for k, n in KINDS.items()) or len(cases) != CASE_COUNT:
        problems.append(f"Plan hat {dict(kinds)}, Tor N verlangt {KINDS} ({CASE_COUNT} Nachbesserungen)")
    channels = Counter(c["channel"] for c in cases)
    if dict(channels) != CHANNELS:
        problems.append(f"Plan hat Wege {dict(channels)}, Tor N verlangt {CHANNELS}")
    if (orders := len({c["order"].lower() for c in cases})) != ORDER_COUNT:
        problems.append(f"Plan hat {orders} Aufträge, Tor N verlangt {ORDER_COUNT} frische")
    local = [c for c in cases if c["kind"] == "local"]
    if local and not str(local[0].get("expect") or "").strip():
        problems.append(f"erster lokaler Fall {local[0]['case']} ohne expect (neue Angabe, wörtlich)")
    stop = [c for c in cases if c["kind"] == "stop"]
    if stop and not isinstance(stop[0].get("stopped_version"), int):
        problems.append(f"Stopp-Fall {stop[0]['case']} ohne stopped_version (Zahl)")
    return problems


def _read_snapshot(root: Path, order: str, label: str) -> tuple[dict | None, str | None]:
    path = snapshot_path(root, order, label)
    try:
        snapshot = json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return None, f"Schnappschuss {label} fehlt"
    except (OSError, ValueError) as exc:
        return None, f"Schnappschuss {label} nicht lesbar: {exc}"
    result = snapshot.get("result") if isinstance(snapshot, dict) else None
    if not isinstance(result, dict):
        return None, f"Schnappschuss {label} ohne Antwort von get_order_result"
    if str(result.get("orderId") or "").lower() != order.lower():
        return None, f"Schnappschuss {label} gehört zu einem anderen Auftrag"
    return snapshot, None


def _text(result: dict) -> str:
    return result.get("text") if isinstance(result.get("text"), str) else ""


def _version(result: dict) -> int | None:
    value = result.get("version")
    return value if isinstance(value, int) and not isinstance(value, bool) else None


def new_not_backed(base_text: str, result: dict) -> list[str]:
    """Quotes of ``not_backed`` passages of ``result`` that do not stand in the base text."""
    return [str(p.get("quote") or "") for p in result.get("unbackedPassages") or []
            if isinstance(p, dict) and p.get("kind") == NOT_BACKED and not stands_in(base_text, p.get("quote"))]


def _fact_check_ran(result: dict) -> bool:
    check = result.get("factCheck")
    return isinstance(check, dict) and check.get("state") in CHECK_RAN and isinstance(result.get("unbackedPassages"), list)


def keep_check(base: dict, new: dict) -> dict:
    """N4 for one change: what of the base is missing in the new version; ``problems`` when a list needed for it is absent."""
    problems = []
    for name, result in (("Ausgangsfassung", base), ("neue Fassung", new)):
        if not _fact_check_ran(result):
            problems.append(f"{name}: Faktenprüfung lief nicht oder unbackedPassages fehlt "
                            f"({(result.get('factCheck') or {}).get('state') if isinstance(result.get('factCheck'), dict) else None})")
        if not isinstance(result.get("openPoints"), list):
            problems.append(f"{name}: openPoints fehlt")
    if not isinstance(new.get("placeholders"), list):
        problems.append("neue Fassung: placeholders fehlt")
    if problems:
        return {"problems": problems, "lost_passages": [], "lost_points": [], "unlisted_placeholders": [], "notes": []}

    new_text = _text(new)
    new_quotes = [normalize_quote(p.get("quote")) for p in new["unbackedPassages"] if isinstance(p, dict)]
    lost_passages = []
    for passage in base["unbackedPassages"]:
        quote = passage.get("quote") if isinstance(passage, dict) else None
        if stands_in(new_text, quote) and not any(normalize_quote(quote) in q for q in new_quotes):
            lost_passages.append(str(quote))
    base_version = _version(base)
    earlier = new.get("earlierOpenPoints") if isinstance(new.get("earlierOpenPoints"), list) else []
    kept = {point_key(p) for p in new["openPoints"]}
    carried = {point_key(e.get("text")) for e in earlier if isinstance(e, dict) and e.get("version") == base_version}
    lost_points = [str(p) for p in base["openPoints"] if point_key(p) not in kept | carried]
    listed = set(str(p) for p in new["placeholders"])
    unlisted = [p for p in find_placeholders(new_text) if p not in listed]

    notes = []
    new_issues = {normalize_quote(i) for i in new.get("sourceIssues") or []}
    if missing := [i for i in base.get("sourceIssues") or [] if normalize_quote(i) not in new_issues]:
        notes.append(f"{len(missing)} Quellenhinweise der Ausgangsfassung fehlen in der neuen (nicht Teil von N4)")
    new_assumptions = {point_key(a) for a in new.get("writerAssumptions") or []}
    if missing := [a for a in base.get("writerAssumptions") or [] if point_key(a) not in new_assumptions]:
        notes.append(f"{len(missing)} Annahmen des Schreibers der Ausgangsfassung fehlen in der neuen (nicht Teil von N4)")
    return {"problems": [], "lost_passages": lost_passages, "lost_points": lost_points, "unlisted_placeholders": unlisted,
            "notes": notes}


def _export_text(snapshot: dict) -> tuple[str | None, str | None]:
    reply = (snapshot.get("export") or {}).get("md") if isinstance(snapshot.get("export"), dict) else None
    if not isinstance(reply, dict):
        return None, None
    file_name = reply.get("fileName") if isinstance(reply.get("fileName"), str) else None
    if isinstance(reply.get("contentBase64"), str):
        try:
            return base64.b64decode(reply["contentBase64"], validate=True).decode("utf-8").lstrip("\ufeff"), file_name
        except (binascii.Error, ValueError):
            return None, file_name
    return (reply["text"], file_name) if isinstance(reply.get("text"), str) else (None, file_name)


def analyse_change(case: dict, base: dict, new: dict) -> dict:
    base_result, new_result = base["result"], new["result"]
    base_text, new_text = _text(base_result), _text(new_result)
    base_words, new_words = count_words(base_text), count_words(new_text)
    report = {"base_version": _version(base_result), "new_version": _version(new_result),
              "words": {"base": base_words, "new": new_words, "ratio": _ratio(base_words, new_words),
                        "api_base": base_result.get("wordCount"), "api_new": new_result.get("wordCount")},
              "paragraphs": paragraph_diff(base_text, new_text),
              "fact_check": {"base": (base_result.get("factCheck") or {}).get("state")
                             if isinstance(base_result.get("factCheck"), dict) else None,
                             "new": (new_result.get("factCheck") or {}).get("state")
                             if isinstance(new_result.get("factCheck"), dict) else None},
              "fact_check_ran": _fact_check_ran(new_result),
              "new_not_backed": new_not_backed(base_text, new_result) if _fact_check_ran(new_result) else None,
              "keep": keep_check(base_result, new_result)}
    if case["kind"] == "local" and str(case.get("expect") or "").strip():
        report["expect"] = {"in_new": contains_literal(new_text, case["expect"]),
                            "in_base": contains_literal(base_text, case["expect"])}
    return report


def analyse_stop(case: dict, base: dict, new: dict) -> dict:
    base_result, new_result = base["result"], new["result"]
    base_version, shown = _version(base_result), _version(new_result)
    stopped = case.get("stopped_version")
    latest = new_result.get("latestVersion")
    sentence = NOT_FINISHED_FORMAT.format(stopped, base_version)
    export_text, file_name = _export_text(new)
    file_version = EXPORT_FILE_VERSION_RE.search(file_name or "")
    failures, problems = [], []
    if latest is None:
        problems.append("latestVersion fehlt: dass v3 gestartet wurde, ist nicht belegt")
    elif latest != stopped or not isinstance(base_version, int) or not latest > base_version:
        problems.append(f"latestVersion {latest!r} passt nicht zur gestoppten Version {stopped!r} (gezeigte Fassung "
                        f"{base_version!r})")
    if shown != base_version:
        failures.append(f"Ergebnis zeigt Version {shown!r} statt {base_version!r}")
    if collapse(_text(new_result)) != collapse(_text(base_result)):
        failures.append("Ergebnis zeigt nicht den Text der Ausgangsfassung")
    if not any(sentence in s for s in all_strings(new_result)):
        failures.append(f"Satz „{sentence}“ fehlt im Ergebnis")
    if export_text is None:
        problems.append(f"Export fehlt ({new.get('exportError') or 'keine lesbare Antwort von export_order'})")
    else:
        if collapse(export_text) != collapse(_text(base_result)) and collapse(_text(base_result)) not in collapse(export_text):
            failures.append("Export ohne Versionsangabe liefert nicht den Text der Ausgangsfassung")
        if file_version and int(file_version.group(1)) != base_version:
            failures.append(f"Exportdatei {file_name} gehört zu Version {file_version.group(1)}, nicht {base_version}")
    return {"base_version": base_version, "shown_version": shown, "latest_version": latest, "state": new_result.get("state"),
            "sentence": sentence, "export_file": file_name, "failures": failures, "problems": problems}


def _sub(cases: list[dict]) -> dict:
    return {"cases": [c["case"] for c in cases], "failures": [], "problems": []}


def _verdict(sub: dict) -> str:
    return "failed" if sub["failures"] else ("not_evaluable" if sub["problems"] else "passed")


def evaluate(cases: list[dict], root: Path) -> dict:
    """Gate N over the snapshots under ``root``; ``verdict`` is ``passed``, ``failed`` or ``not_evaluable``."""
    problems = plan_problems(cases)
    rows = []
    for case in cases:
        row = {"case": case["case"], "kind": case["kind"], "channel": case["channel"], "order": case["order"],
               "has_text": False, "problems": []}
        base, base_problem = _read_snapshot(root, case["order"], case["base"])
        new, new_problem = _read_snapshot(root, case["order"], case["new"])
        row["problems"] += [p for p in (base_problem, new_problem) if p]
        if base and not _text(base["result"]).strip():
            row["problems"].append(f"Ausgangsfassung {case['base']} ohne Text")
        if base and new and not row["problems"]:
            if case["kind"] == "stop":
                row["stop"] = analyse_stop(case, base, new)
                row["has_text"] = bool(_text(new["result"]).strip())
            else:
                base_version, new_version = _version(base["result"]), _version(new["result"])
                if not _text(new["result"]).strip() or not isinstance(base_version, int) or not isinstance(new_version, int) \
                        or new_version <= base_version:
                    row["problems"].append(f"keine neue Fassung mit Text (Version {base_version!r} → {new_version!r})")
                else:
                    row["has_text"] = True
                    row["change"] = analyse_change(case, base, new)
        rows.append(row)

    with_text = sum(1 for r in rows if r["has_text"])
    if with_text < MIN_WITH_TEXT:
        problems.append(f"nur {with_text} von {len(rows)} mit Text (mindestens {MIN_WITH_TEXT} von {CASE_COUNT})")
    changes = [r for r in rows if r["kind"] != "stop"]
    usable = [r for r in changes if r["has_text"]]

    n1 = _sub([r for r in changes if r["kind"] == "local"])
    locals_ = [r for r in changes if r["kind"] == "local"]
    if not any(r["has_text"] for r in locals_):
        n1["problems"].append("kein lokaler Fall mit Text")
    if locals_ and not locals_[0]["has_text"]:
        n1["problems"].append(f"erster lokaler Fall {locals_[0]['case']} ohne neue Fassung: neue Angabe nicht prüfbar")
    for row in (r for r in locals_ if r["has_text"]):
        change = row["change"]
        para, ratio = change["paragraphs"], change["words"]["ratio"]
        if para["unchanged"] < para["old"] - MAX_CHANGED_PARAGRAPHS:
            n1["failures"].append(f"{row['case']}: {para['unchanged']} von {para['old']} Absätzen unverändert "
                                  f"(mindestens {para['old'] - MAX_CHANGED_PARAGRAPHS})")
        if not _within(ratio, 1 - WORD_TOLERANCE, 1 + WORD_TOLERANCE):
            n1["failures"].append(f"{row['case']}: Wörter {change['words']['base']} → {change['words']['new']} (±15 %)")
        if "expect" in change:
            if change["expect"]["in_base"]:
                n1["problems"].append(f"{row['case']}: die neue Angabe stand schon in der Ausgangsfassung, der Fall belegt nichts")
            elif not change["expect"]["in_new"]:
                n1["failures"].append(f"{row['case']}: neue Angabe steht nicht wörtlich im Text")

    n2 = _sub([r for r in changes if r["kind"] == "tone"])
    tones = [r for r in changes if r["kind"] == "tone" and r["has_text"]]
    if not tones:
        n2["problems"].append("kein Ton-Fall mit Text")
    new_total = 0
    for row in tones:
        change = row["change"]
        if not _within(change["words"]["ratio"], 1 - WORD_TOLERANCE, 1 + WORD_TOLERANCE):
            n2["failures"].append(f"{row['case']}: Wörter {change['words']['base']} → {change['words']['new']} (±15 %)")
        if change["new_not_backed"] is None:
            n2["problems"].append(f"{row['case']}: Faktenprüfung lief nicht für die neue Fassung ({change['fact_check']['new']})")
        else:
            new_total += len(change["new_not_backed"])
    if new_total > MAX_NEW_NOT_BACKED:
        n2["failures"].append(f"{new_total} neue not_backed zusammen (höchstens {MAX_NEW_NOT_BACKED})")
    n2["new_not_backed"] = new_total

    n3 = _sub([r for r in changes if r["kind"] in ("shorter", "longer")])
    for kind, (low, high) in (("shorter", SHORTER_RANGE), ("longer", LONGER_RANGE)):
        found = [r for r in changes if r["kind"] == kind and r["has_text"]]
        if not found:
            n3["problems"].append(f"kein Fall {kind} mit Text")
        for row in found:
            change = row["change"]
            if not _within(change["words"]["ratio"], low, high):
                n3["failures"].append(f"{row['case']}: Wörter {change['words']['base']} → {change['words']['new']} "
                                      f"({low:.0%}–{high:.0%})")
            if kind == "longer":
                if change["new_not_backed"] is None:
                    n3["problems"].append(f"{row['case']}: Faktenprüfung lief nicht für die neue Fassung")
                elif len(change["new_not_backed"]) > MAX_NEW_NOT_BACKED:
                    n3["failures"].append(f"{row['case']}: {len(change['new_not_backed'])} neue not_backed "
                                          f"(höchstens {MAX_NEW_NOT_BACKED})")

    n4 = _sub(changes)
    if not usable:
        n4["problems"].append("keine Nachbesserung mit Text")
    for row in usable:
        keep = row["change"]["keep"]
        n4["problems"] += [f"{row['case']}: {p}" for p in keep["problems"]]
        n4["failures"] += [f"{row['case']}: offene Stelle der Vorversion fehlt: „{q}“" for q in keep["lost_passages"]]
        n4["failures"] += [f"{row['case']}: Punkt des Schreibers der Vorversion fehlt: „{p}“" for p in keep["lost_points"]]
        n4["failures"] += [f"{row['case']}: Platzhalter nicht in placeholders: {p}" for p in keep["unlisted_placeholders"]]

    stops = [r for r in rows if r["kind"] == "stop"]
    n5 = _sub(stops)
    if not stops:
        n5["problems"].append("kein Stopp-Fall")
    for row in stops:
        if "stop" not in row:
            n5["problems"].append(f"{row['case']}: {'; '.join(row['problems']) or 'keine Daten'}")
            continue
        n5["failures"] += [f"{row['case']}: {f}" for f in row["stop"]["failures"]]
        n5["problems"] += [f"{row['case']}: {p}" for p in row["stop"]["problems"]]
    if any("Export fehlt" in p for p in n5["problems"]):
        problems.append("Export fehlt")

    parts = {"N1": n1, "N2": n2, "N3": n3, "N4": n4, "N5": n5}
    for sub in parts.values():
        sub["verdict"] = _verdict(sub)
    halt = parts["N4"]["verdict"] == "failed" or parts["N5"]["verdict"] == "failed"
    if halt:
        verdict = "failed"
    elif problems:
        verdict = "not_evaluable"
    elif any(s["verdict"] == "failed" for s in parts.values()):
        verdict = "failed"
    elif any(s["verdict"] == "not_evaluable" for s in parts.values()):
        verdict = "not_evaluable"
    else:
        verdict = "passed"
    follow_up = []
    if halt:
        follow_up.append(FOLLOW_UP["halt"])
    if any(parts[k]["verdict"] == "failed" for k in ("N1", "N2", "N3")):
        follow_up.append(FOLLOW_UP["instruction"])
    if verdict == "passed":
        follow_up.append(FOLLOW_UP["passed"])
    return {"gate": "N", "verdict": verdict, "halt": halt, "problems": problems, "with_text": with_text, "parts": parts,
            "cases": rows, "follow_up": follow_up}


def _print_report(report: dict, echo: Callable[[str], None]) -> None:
    for row in report["cases"]:
        line = f"{row['case']} ({row['kind']}, {row['channel']}): "
        if "change" in row:
            change = row["change"]
            ratio = change["words"]["ratio"]
            line += (f"v{change['base_version']}→v{change['new_version']}, Wörter {change['words']['base']}→"
                     f"{change['words']['new']} ({ratio:.0%}), Absätze unverändert {change['paragraphs']['unchanged']}/"
                     f"{change['paragraphs']['old']}")
        elif "stop" in row:
            line += f"zeigt v{row['stop']['shown_version']}, neueste v{row['stop']['latest_version']}"
        else:
            line += "ohne Text: " + "; ".join(row["problems"])
        echo(line)
    for name, sub in report["parts"].items():
        echo(f"{name}: {sub['verdict']}")
        for failure in sub["failures"]:
            echo(f"  Fehler: {failure}")
        for problem in sub["problems"]:
            echo(f"  nicht auswertbar: {problem}")
    for problem in report["problems"]:
        echo(f"nicht auswertbar: {problem}")
    for line in report["follow_up"]:
        echo(line)
    echo(f"Tor N: {report['verdict']} ({report['with_text']} von {len(report['cases'])} mit Text)")


def main(argv: list[str] | None = None, *, connector: Callable[[Path | None], ContractClient] = connect,
         echo: Callable[[str], None] = print) -> int:
    parser = argparse.ArgumentParser(prog="revise_check", description=__doc__.split("\n\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    snap = sub.add_parser("snapshot", help="get_order_result und export_order eines Auftrags speichern")
    snap.add_argument("--order", required=True)
    snap.add_argument("--label", required=True)
    snap.add_argument("--out-dir", required=True, type=Path)
    snap.add_argument("--env-file", type=Path)
    ev = sub.add_parser("evaluate", help="Tor N")
    ev.add_argument("--plan", required=True, type=Path)
    ev.add_argument("--out-dir", required=True, type=Path)
    for p in (snap, ev):
        p.add_argument("--allow-in-repo", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args(argv)
    if not args.allow_in_repo and inside_git_worktree(args.out_dir):
        echo(f"{args.out_dir} liegt in einem Git-Baum; Texte echter Aufträge gehören nicht ins Repository.")
        return 2
    if args.command == "snapshot":
        try:
            if snapshot_path(args.out_dir, args.order, args.label).exists():
                raise FileExistsError(f"Schnappschuss {args.label} gibt es schon; eine neue Bezeichnung wählen")
            snapshot = take_snapshot(connector(args.env_file), args.out_dir, args.order, args.label)
        except (OSError, ValueError) as exc:
            echo(f"Schnappschuss nicht möglich: {exc}")
            return 2
        except (SignInError, McpError) as exc:
            echo(f"Anmeldung, Verbindung oder Werkzeugaufruf gescheitert: {exc}")
            return 3
        result = snapshot["result"] if isinstance(snapshot["result"], dict) else {}
        echo(f"{args.order} {args.label}: Version {result.get('version')!r}, Zustand {result.get('state')!r}, "
             f"{count_words(result.get('text'))} Wörter, Export {'da' if snapshot['export'] else 'fehlt'}")
        return 0
    try:
        cases = load_plan(args.plan)
    except (OSError, ValueError, AttributeError) as exc:
        echo(f"Plan nicht lesbar: {exc}")
        return 2
    report = evaluate(cases, args.out_dir)
    args.out_dir.mkdir(parents=True, exist_ok=True)
    (args.out_dir / "revise-gate.json").write_text(json.dumps(report, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    _print_report(report, echo)
    return {"passed": 0, "failed": 1}.get(report["verdict"], 4)


if __name__ == "__main__":
    sys.exit(main())
