"""Task set: one JSON or Markdown file per task in the directory EVAL_TASKS_DIR points to."""
from __future__ import annotations

import json
import re
from dataclasses import dataclass, field
from pathlib import Path

TEXT_SUFFIXES = {".md": "text/markdown", ".txt": "text/plain"}
BINARY_SUFFIXES = {".pdf": "application/pdf"}
TASK_ID_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,80}$")


class TaskError(ValueError):
    """A task file is malformed."""


@dataclass(frozen=True)
class Attachment:
    """A file handed to the writers together with the short brief."""

    name: str
    content_type: str
    text: str | None = None
    data: bytes | None = None

    def as_bytes(self) -> bytes:
        if self.data is not None:
            return self.data
        return (self.text or "").encode("utf-8")


@dataclass(frozen=True)
class LengthRule:
    """Explicit word-count limits of a task (inclusive, either side optional)."""

    min_words: int | None = None
    max_words: int | None = None


@dataclass(frozen=True)
class Task:
    """One evaluation task."""

    id: str
    brief: str
    attachments: tuple[Attachment, ...] = ()
    original_brief: str | None = None
    reference_text: str | None = None
    length: LengthRule | None = None
    source: str = ""
    extra: dict = field(default_factory=dict)

    @property
    def judge_brief(self) -> str:
        """The yardstick for the judge: the withheld original brief, else the short brief."""
        return self.original_brief or self.brief


def load_tasks(directory: str | Path) -> list[Task]:
    """Loads all ``*.json`` and ``*.md`` task files of a directory, sorted by id."""
    root = Path(directory)
    if not root.is_dir():
        raise TaskError(f"Aufgabenverzeichnis fehlt: {root}")
    tasks = []
    for path in sorted(root.iterdir()):
        if path.name.startswith((".", "_")) or path.name.upper() == "README.MD":
            continue
        if path.suffix == ".json":
            tasks.append(_load_json(path))
        elif path.suffix == ".md":
            tasks.append(_load_markdown(path))
    ids = [t.id for t in tasks]
    duplicates = sorted({i for i in ids if ids.count(i) > 1})
    if duplicates:
        raise TaskError(f"Doppelte Aufgaben-IDs: {', '.join(duplicates)}")
    return sorted(tasks, key=lambda t: t.id)


def _check_id(task_id: str, path: Path) -> str:
    if not TASK_ID_RE.match(task_id):
        raise TaskError(f"{path.name}: ungültige id {task_id!r} (erlaubt: Buchstaben, Ziffern, . _ -)")
    return task_id


def _load_json(path: Path) -> Task:
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise TaskError(f"{path.name}: kein gültiges JSON ({exc})") from exc
    if not isinstance(raw, dict):
        raise TaskError(f"{path.name}: erwartet ein JSON-Objekt")
    brief = raw.get("kurzauftrag")
    if not isinstance(brief, str) or not brief.strip():
        raise TaskError(f"{path.name}: Feld 'kurzauftrag' fehlt oder ist leer")
    attachments = tuple(_attachment_from_json(a, path) for a in raw.get("anhaenge", []) or [])
    length = None
    if isinstance(raw.get("laenge"), dict):
        length = LengthRule(
            min_words=_optional_int(raw["laenge"].get("min"), path),
            max_words=_optional_int(raw["laenge"].get("max"), path),
        )
    known = {"id", "kurzauftrag", "anhaenge", "original_briefing", "referenztext", "laenge"}
    return Task(
        id=_check_id(str(raw.get("id") or path.stem), path),
        brief=brief.strip(),
        attachments=attachments,
        original_brief=_optional_text(raw.get("original_briefing")),
        reference_text=_optional_text(raw.get("referenztext")),
        length=length,
        source=path.name,
        extra={k: v for k, v in raw.items() if k not in known},
    )


def _attachment_from_json(raw: object, task_path: Path) -> Attachment:
    if not isinstance(raw, dict) or not isinstance(raw.get("name"), str):
        raise TaskError(f"{task_path.name}: jeder Anhang braucht ein Feld 'name'")
    name = raw["name"]
    if isinstance(raw.get("text"), str):
        return Attachment(name=name, content_type=_content_type(name, task_path), text=raw["text"])
    if isinstance(raw.get("pfad"), str):
        file_path = _attachment_path(raw["pfad"], name, task_path)
        if not file_path.is_file():
            raise TaskError(f"{task_path.name}: Anhang {name!r} nicht gefunden ({raw['pfad']})")
        content_type = _content_type(name, task_path)
        if content_type in BINARY_SUFFIXES.values():
            return Attachment(name=name, content_type=content_type, data=file_path.read_bytes())
        return Attachment(name=name, content_type=content_type, text=file_path.read_text(encoding="utf-8"))
    raise TaskError(f"{task_path.name}: Anhang {name!r} braucht 'text' oder 'pfad'")


def _attachment_path(relative: str, name: str, task_path: Path) -> Path:
    """Resolves an attachment path; it must stay inside the task directory (no absolute paths, no escape via .. or symlinks)."""
    root = task_path.parent.resolve()
    if Path(relative).is_absolute():
        raise TaskError(f"{task_path.name}: Anhang {name!r}: absolute Pfade sind nicht erlaubt ({relative}); "
                        "den Pfad relativ zum Aufgabenordner angeben")
    target = (root / relative).resolve()
    if not target.is_relative_to(root):
        raise TaskError(f"{task_path.name}: Anhang {name!r}: Pfad {relative!r} verlässt den Aufgabenordner {root}")
    return target


def _content_type(name: str, task_path: Path) -> str:
    suffix = Path(name).suffix.lower()
    if suffix in TEXT_SUFFIXES:
        return TEXT_SUFFIXES[suffix]
    if suffix in BINARY_SUFFIXES:
        return BINARY_SUFFIXES[suffix]
    raise TaskError(f"{task_path.name}: Anhang {name!r}: nur .md, .txt und .pdf werden unterstützt")


def _optional_text(value: object) -> str | None:
    return value.strip() if isinstance(value, str) and value.strip() else None


def _optional_int(value: object, path: Path) -> int | None:
    if value is None:
        return None
    if isinstance(value, int) and not isinstance(value, bool) and value > 0:
        return value
    raise TaskError(f"{path.name}: 'laenge' erwartet positive ganze Zahlen")


SECTION_RE = re.compile(r"^#\s+(.+?)\s*$", re.MULTILINE)


def _load_markdown(path: Path) -> Task:
    text = path.read_text(encoding="utf-8")
    sections: list[tuple[str, str]] = []
    matches = list(SECTION_RE.finditer(text))
    for i, match in enumerate(matches):
        end = matches[i + 1].start() if i + 1 < len(matches) else len(text)
        sections.append((match.group(1).strip(), text[match.end():end].strip()))

    brief = None
    original = None
    reference = None
    attachments = []
    for title, body in sections:
        lowered = title.lower()
        if lowered == "kurzauftrag":
            brief = body
        elif lowered == "original-briefing":
            original = body or None
        elif lowered == "referenztext":
            reference = body or None
        elif lowered.startswith("anhang:"):
            name = title.split(":", 1)[1].strip()
            content_type = _content_type(name, path)
            if content_type not in TEXT_SUFFIXES.values():
                raise TaskError(f"{path.name}: Markdown-Aufgaben erlauben nur Text-Anhänge; PDF bitte per JSON und 'pfad'")
            attachments.append(Attachment(name=name, content_type=content_type, text=body))
    if not brief:
        raise TaskError(f"{path.name}: Abschnitt '# Kurzauftrag' fehlt oder ist leer")
    return Task(
        id=_check_id(path.stem, path),
        brief=brief,
        attachments=tuple(attachments),
        original_brief=original,
        reference_text=reference,
        source=path.name,
    )
