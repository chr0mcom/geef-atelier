"""Gates T-A2 to T-A5 of stage 6 (contract stage 6, sections 9, 10 and 11): files over the upload ticket and counted refusals.

The tool never uploads, never starts an order and never calls a model. It

- ``generate``: writes synthetic files for the gates into a directory outside every Git tree (a text PDF of at least 200 KB
  with 24 pages and marker sentences on the first and the last page, a DOCX, a second text PDF for the upload page, a scan PDF
  without a text layer, a PNG, a placeholder text with its ``attachments`` value and a Markdown file for a wrong announced
  size), plus ``files.json`` and a plan template. Every run gets its own nonce inside the files, so no file is a duplicate of an
  earlier upload of the same person.
- ``mark``: stores the time of the database (``now()``) as start or end of gate T-A4 or T-A5 in ``marks.json``.
- ``compare``: SHA-256 of a local file against ``Sources.Sha256`` of the source of an upload ticket; prints yes or no.
- ``largest-base64``: the longest ``contentBase64`` in a Claude Code ``stream-json`` transcript (a count).
- ``evaluate``: decides the gates from a plan (JSON, outside Git), the transcripts and captured answers it names, and read-only
  queries of the database.

Database access is read-only twice over: ``PGOPTIONS=-c default_transaction_read_only=on`` and every query runs inside
``BEGIN TRANSACTION READ ONLY … ROLLBACK``. Hashes are compared inside the query; no query returns a file name, a user name, a
hash or any content. The output (screen and ``upload-gates.json``) holds counts and yes/no only: no file names, ids, hashes or
contents. Transcripts hold the one-time token and file content; they stay where they are and are only read.

Thresholds (contract 10, fixed in advance, word for word):

- **T-A2** Claude Code, 2 sessions (S1 PDF ≥ 200 KB, ≥ 20 pages; S2 DOCX): (a) ``prepare_attachment`` called, (b) ``curl``
  with 201, (c) ``submit_order`` with ``uploadIds``, (d) ``Sources.Sha256`` = local SHA-256, (e) largest ``contentBase64`` in
  the transcript ≤ 1 KB; S1 in addition: ``RunSourceUsages`` target ``writer``, mode ``FullText``, ``CharsIncluded`` =
  ``CharCount`` of the source. 2/2.
- **T-A3** upload page: (a) the page shows the receipt and "Done …", (b) ``Sources.Sha256`` equal, (c) second account: the
  page says "Not found", ``submit_order`` with the id says "not found".
- **T-A4** scan PDF over the token and over the page: both 422 with the sentence of ``PdfSourceExtractor.NoTextReason``; 0 new
  ``Sources``; exactly 2 refusal rows ``pdf_no_text`` (``mcp_ticket``, ``upload_page``).
- **T-A5** a PNG over ``/api/sources``, a placeholder text in ``attachments``, a file with a wrong announced size over the
  token: ``SourceRefusals`` since the start of the gate = exactly the expected rows (code, path); no column holds a file name.

Decisions of this tool on top of the contract (each one stricter, none can let a gate pass that the contract fails):

- K1 asks for the source to be linked to the order: T-A2 (f) ``OrderSources`` links the order of ``submit_order`` with the
  source of the ticket, in both sessions.
- (b) counts only a ``curl`` answer that names the upload id, a SHA-256 and a usable receipt; a refused answer never counts.
  (d) also needs the ticket ``Used``, its source of the same owner, origin ``McpAttachment`` (token way) and the SHA-256 in the
  ``curl`` answer equal to the local one. T-A3 (b) needs origin ``WebUpload`` (page way), so a ``curl`` upload cannot stand in.
- T-A3 (c) also fails when the page of the second account shows the file name or a sentence of the upload page, or when an
  order of another person links the source. The capture of that page must name its address in its first line
  (``<!-- url: <address> -->``) and the address must be ``/u/<upload id of the plan>``; without it (c) is ``fehlt``, with
  another address ``nein``: any 404 page of a wrong address can no longer pass.
- T-A4 and T-A5: the captured answers must be the expected refusals (status 422 and sentence; for the placeholder the sentence
  of the parser); a capture that is missing makes the gate not evaluable (an action not taken is not a counting error); the
  refused tickets have no source and are not ``Used``, and they show a checked and refused attempt (``Attempts`` ≥ 1 and
  ``LastRefusalCode`` = the expected code), so an untouched ticket cannot pass; the captured 422 answers of the ticket ways
  must name the upload id of the plan (``uploadId``); 0 new ``Sources`` in the window of T-A5 as well; the expected rows also
  carry the expected extension (``pdf``, ``png``, ``txt``, ``md``); every column of ``SourceRefusals`` is one of the contract
  (7.1) and every value is within its vocabulary.
- Windows of T-A4 and T-A5 come from ``mark`` (database clock), inclusive at both ends, and must not overlap. Rows of every
  person count: a refusal of someone else in the window fails the gate (conservative).
- S1 without a writer row is a failure once a run of the order has finished, and not evaluable before.

Never passed without data: every criterion needs its data (plan entry, readable file of the required kind, complete transcript
with its closing ``result`` line, captured answers, marks, database rows); a criterion without data is ``fehlt`` and the gate
is not evaluable. A failed criterion makes the gate ``failed`` even when data is missing elsewhere. Criteria that depend on a
failed one are ``folgt`` (not checked).

Follow-up (contract 10): T-A2 failed only at (a), (b), (c) or (e) → one round at the sentences 5.5, then two new sessions (cap 4);
(d) failed → halt, switch ``Off`` (the same for (f) and the writer check, decision of this tool); T-A3, T-A4 or T-A5 failed →
halt, switch ``Off``; all four passed → own commit ``Uploads:Tickets=On``.

Usage (from ``tools/Eval``)::

    python3 -m geef_eval.upload_check generate --out-dir <dir>
    python3 -m geef_eval.upload_check mark --gate T-A4 --at start --out-dir <dir>
    python3 -m geef_eval.upload_check compare --file <file> --upload-id <id>
    python3 -m geef_eval.upload_check largest-base64 --transcript <stream.jsonl>
    python3 -m geef_eval.upload_check evaluate --plan <dir>/plan.json --out-dir <dir> [--gate T-A2 …]

Exit codes: 0 passed, 1 failed, 2 wrong usage or unreadable plan, 3 database query failed, 4 not evaluable.
"""
from __future__ import annotations

import argparse
import base64
import hashlib
import html
import json
import os
import random
import re
import secrets
import struct
import subprocess
import sys
import zipfile
import zlib
from dataclasses import dataclass, field
from datetime import datetime
from urllib.parse import urlsplit
from io import BytesIO
from pathlib import Path
from typing import Callable, Iterable, Iterator, Protocol

from .cli import inside_git_worktree

GATES = ("T-A2", "T-A3", "T-A4", "T-A5")
WINDOW_GATES = ("T-A4", "T-A5")

MAX_INLINE_BASE64 = 1024
MIN_S1_BYTES = 200 * 1024
MIN_S1_PAGES = 20

YES, NO, MISSING, SKIPPED = "ja", "nein", "fehlt", "folgt"

PAGE_DONE = "Done. The file has arrived. Go back to your AI assistant and tell it the file is uploaded."
PAGE_HEADING = "Add a file for your AI assistant"
PAGE_INTRO_START = "Your AI assistant asked for this file:"
PAGE_ALREADY_USED = "This file has already arrived. You can close this page."
PAGE_EXPIRED = "This link has expired. Ask your AI assistant for a new one."
NOT_FOUND_FORMAT = "Upload {0} was not found. Nothing was started."
SIZE_MISMATCH_FORMAT = "The file is {0} bytes, not the {1} bytes announced. Nothing was stored."
LARGER_THAN_ANNOUNCED_FORMAT = "The file is larger than the {0} bytes announced. Nothing was stored."
NO_TEXT_REASON = "This PDF has no readable text. It may be a scan; scanned files are not supported yet."
UNSUPPORTED_REASON = "This file type is not supported. Use PDF, Word (.docx), text or Markdown."
PLACEHOLDER_PROBLEM = "contains only a placeholder or a reference to the file instead of the file content"
NOT_FOUND_PAGE = "not found"
RECEIPT_MARK = 'data-testid="source-card-summary"'
TOKEN_UPLOAD_PATH = "/api/uploads/"
PAGE_PATH = "/u/"
PAGE_URL_RE = re.compile(r"\A\s*<!--\s*url:\s*(\S+)\s*-->")

REFUSAL_CODES = frozenset({
    "too_large", "user_busy", "busy", "interrupted", "save_failed", "empty", "quota", "unsupported_type", "old_word",
    "docx_archive", "pdf_no_text", "pdf_encrypted", "unreadable", "unavailable", "too_complex", "timeout", "too_much_text",
    "too_many_pages", "size_mismatch", "too_long_for_ticket", "mcp_json", "mcp_placeholder", "mcp_base64_invalid", "mcp_type_mismatch",
    "mcp_unsupported_type", "mcp_empty", "other"})
REFUSAL_PATHS = frozenset({"web", "mcp_inline", "mcp_ticket", "upload_page"})
SOURCE_KINDS = frozenset({"pdf", "docx", "text", "markdown"})
REFUSAL_COLUMNS = frozenset({"Id", "CreatedAt", "OwnerUsername", "Path", "Code", "Kind", "Extension", "DeclaredType", "SizeBytes"})
FINISHED_RUN_STATES = ("Completed", "Failed", "Aborted")

UUID_RE = re.compile(r"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")
SHA_RE = re.compile(r"^[0-9a-f]{64}$")
SAFE_NAME_RE = re.compile(r"^[a-z0-9][a-z0-9.-]{0,120}$")
# Names the generator makes: S2 carries an apostrophe, spaces and brackets on purpose; no LIKE wildcard or backslash.
GENERATED_NAME_RE = re.compile(r"^[a-z0-9][a-z0-9 .'()-]{0,120}$")
EXTENSION_RE = re.compile(r"^[a-z0-9]{1,8}$")
MEDIA_TYPE_RE = re.compile(r"^[a-z0-9!#$&^_.+-]+/[a-z0-9!#$&^_.+-]+$")
TOKEN_URL_RE = re.compile(r"/api/uploads/([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?![0-9a-fA-F/-])")
BASE64_FIELD_RE = re.compile(r"""contentBase64\\?["']?\s*[:=]\s*\\?["']([A-Za-z0-9+/=_\-\s\\]*)""", re.IGNORECASE)
PAGE_OBJECT_RE = re.compile(rb"/Type\s*/Page(?![A-Za-z])")
STATUS_LINE_RE = re.compile(r"^\d{3}$")

FOLLOW_UP = {
    "sentences": "T-A2 verfehlt nur bei (a)/(b)/(c)/(e): eine Runde nur an den Sätzen 5.5 (Beschreibung, next, Hinweis Schritt 2), "
                 "dann zwei neue Sitzungen (Kappe gesamt 4).",
    "bytes": "T-A2 (d) verfehlt: Halt, Schalter Off.",
    "source": "T-A2 (f) oder Schreiber-Prüfung verfehlt: Halt, Schalter Off, Fehler beheben (Vorgabe dieses Werkzeugs).",
    "halt": "Halt, Schalter Off, Fehler beheben.",
    "passed": "Alle vier Tore bestanden: eigener Commit Uploads:Tickets=On in appsettings.json (Zusatz D-076).",
}


class UsageError(Exception):
    """Wrong call or unreadable plan (exit code 2)."""


class DatabaseError(Exception):
    """A read-only query failed (exit code 3)."""




def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def pdf_page_count(data: bytes) -> int:
    return len(PAGE_OBJECT_RE.findall(data))


def is_pdf(data: bytes) -> bool:
    return b"%PDF-" in data[:1024]


def is_docx(path: Path) -> bool:
    try:
        with zipfile.ZipFile(path) as archive:
            return "word/document.xml" in archive.namelist()
    except (zipfile.BadZipFile, OSError):
        return False


def _pdf_escape(text: str) -> str:
    return text.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")


def _pdf_stream(header: str, data: bytes) -> bytes:
    return f"<< {header} /Length {len(data)} >>\nstream\n".encode("ascii") + data + b"\nendstream"


def build_pdf(pages: list[tuple[list[str], tuple[int, int, bytes] | None]]) -> bytes:
    """A PDF 1.4 without compression: per page lines of Helvetica text and/or one grey image (width, height, samples)."""
    objects: list[bytes] = [b"", b""]
    font = None
    kids = []
    for lines, image in pages:
        ops, resources = [], []
        if image is not None:
            width, height, samples = image
            objects.append(_pdf_stream(f"/Type /XObject /Subtype /Image /Width {width} /Height {height} "
                                       "/ColorSpace /DeviceGray /BitsPerComponent 8", samples))
            ops.append("q 480 0 0 300 56 60 cm /Im1 Do Q")
            resources.append(f"/XObject << /Im1 {len(objects)} 0 R >>")
        if lines:
            if font is None:
                objects.append(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
                font = len(objects)
            ops.append("BT /F1 10 Tf 12 TL 56 790 Td")
            ops.extend(f"({_pdf_escape(line)}) Tj T*" for line in lines)
            ops.append("ET")
            resources.append(f"/Font << /F1 {font} 0 R >>")
        objects.append(_pdf_stream("", "\n".join(ops).encode("latin-1")))
        content = len(objects)
        objects.append(f"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << {' '.join(resources)} >> "
                       f"/Contents {content} 0 R >>".encode("ascii"))
        kids.append(len(objects))
    objects[0] = b"<< /Type /Catalog /Pages 2 0 R >>"
    objects[1] = f"<< /Type /Pages /Kids [{' '.join(f'{k} 0 R' for k in kids)}] /Count {len(kids)} >>".encode("ascii")
    out = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
    offsets = []
    for number, body in enumerate(objects, 1):
        offsets.append(len(out))
        out += f"{number} 0 obj\n".encode("ascii") + body + b"\nendobj\n"
    xref = len(out)
    out += f"xref\n0 {len(objects) + 1}\n0000000000 65535 f \n".encode("ascii")
    out += b"".join(f"{offset:010d} 00000 n \n".encode("ascii") for offset in offsets)
    out += f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n".encode("ascii")
    return bytes(out)


_SUBJECTS = ("The planning group", "A small workshop", "The harbour office", "Our test garden", "The night shift",
             "The village library", "A travelling choir", "The repair team", "The weather station", "A school class")
_VERBS = ("records", "compares", "describes", "collects", "measures", "explains", "reviews", "sorts", "counts", "maps")
_OBJECTS = ("the morning deliveries", "seven kinds of apples", "the old bridge timbers", "a list of quiet streets",
            "the rain of the last week", "three paper models", "the tide tables", "the colours of the boats",
            "a set of wooden tools", "the noise of the market")
_ENDINGS = ("before noon.", "with great care.", "for the next report.", "in a short table.", "without any hurry.",
            "for the visitors.", "as agreed last month.", "in plain words.", "on two pages.", "for the archive.")


def _sentences(rng: random.Random, count: int) -> list[str]:
    return [f"{rng.choice(_SUBJECTS)} {rng.choice(_VERBS)} {rng.choice(_OBJECTS)} {rng.choice(_ENDINGS)}" for _ in range(count)]


def _wrap(words: Iterable[str], width: int = 88) -> list[str]:
    lines, line = [], ""
    for word in words:
        if line and len(line) + 1 + len(word) > width:
            lines.append(line)
            line = word
        else:
            line = f"{line} {word}" if line else word
    if line:
        lines.append(line)
    return lines


def markers(nonce: str) -> tuple[str, str]:
    return (f"Marker first page: the copper kettle sings at dawn ({nonce}).",
            f"Marker last page: the paper boat reaches the far shore ({nonce}).")


def text_pdf(rng: random.Random, nonce: str, pages: int, image_size: tuple[int, int] | None) -> bytes:
    first, last = markers(nonce)
    specs = []
    for number in range(1, pages + 1):
        words = " ".join(_sentences(rng, 18)).split()
        lines = [f"Page {number} of {pages}. Synthetic test document {nonce}."] + _wrap(words)[:16]
        if number == 1:
            lines.insert(0, first)
        if number == pages:
            lines.append(last)
        image = None
        if image_size is not None:
            width, height = image_size
            image = (width, height, rng.randbytes(width * height))
        specs.append((lines, image))
    return build_pdf(specs)


def scan_pdf(rng: random.Random, pages: int = 3) -> bytes:
    return build_pdf([([], (240, 160, rng.randbytes(240 * 160))) for _ in range(pages)])


def docx_bytes(rng: random.Random, nonce: str, paragraphs: int = 30) -> bytes:
    first, last = markers(nonce)
    texts = [first] + [" ".join(_sentences(rng, 3)) for _ in range(paragraphs)] + [last]
    body = "".join(f'<w:p><w:r><w:t xml:space="preserve">{html.escape(t, quote=False)}</w:t></w:r></w:p>' for t in texts)
    parts = {
        "[Content_Types].xml": '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
        '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
        '<Default Extension="xml" ContentType="application/xml"/>'
        '<Override PartName="/word/document.xml" '
        'ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>',
        "_rels/.rels": '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
        '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" '
        'Target="word/document.xml"/></Relationships>',
        "word/document.xml": '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
        '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>'
        f"{body}<w:sectPr/></w:body></w:document>",
    }
    buffer = BytesIO()
    with zipfile.ZipFile(buffer, "w", zipfile.ZIP_DEFLATED) as archive:
        for name, text in parts.items():
            info = zipfile.ZipInfo(name, date_time=(2026, 1, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, text.encode("utf-8"))
    return buffer.getvalue()


def png_bytes(rng: random.Random, width: int = 64, height: int = 64) -> bytes:
    def chunk(kind: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + rng.randbytes(width * 3) for _ in range(height))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


PLACEHOLDER_TEXT = "See the attached file for the full version of the report."


def generate(out_dir: Path, seed: int | None = None) -> dict:
    """Writes the synthetic files, ``files.json`` and ``plan-vorlage.json``; returns the manifest."""
    rng = random.Random(seed if seed is not None else secrets.randbits(64))
    nonce = f"{rng.getrandbits(32):08x}"
    out_dir.mkdir(parents=True, exist_ok=True)
    first, last = markers(nonce)
    size_text = "\n\n".join(["# Notes for the size check", *_sentences(rng, 30)]) + "\n"
    files = {
        "s1": (f"report-s1-{nonce}.pdf", text_pdf(rng, nonce, 24, (120, 80))),
        "s2": (f"Anna's notes s2 ({nonce}).docx", docx_bytes(rng, nonce)),
        "page": (f"letter-page-{nonce}.pdf", text_pdf(rng, nonce, 6, None)),
        "scan": (f"scan-{nonce}.pdf", scan_pdf(rng)),
        "png": (f"picture-{nonce}.png", png_bytes(rng)),
        "placeholder": (f"placeholder-{nonce}.txt", PLACEHOLDER_TEXT.encode("ascii")),
        "size": (f"size-{nonce}.md", size_text.encode("ascii")),
    }
    manifest: dict = {"nonce": nonce, "files": {}}
    for key, (name, data) in files.items():
        (out_dir / name).write_bytes(data)
        entry = {"file": name, "bytes": len(data)}
        if name.endswith(".pdf"):
            entry["pages"] = pdf_page_count(data)
        manifest["files"][key] = entry
    manifest["files"]["s1"]["markers"] = [first, last]
    manifest["files"]["s2"]["markers"] = [first, last]
    manifest["files"]["size"]["declared_bytes"] = len(files["size"][1]) + 100
    placeholder = files["placeholder"]
    attachments = [{"filename": placeholder[0], "contentType": "text/plain",
                    "contentBase64": base64.b64encode(placeholder[1]).decode("ascii")}]
    (out_dir / f"placeholder-attachments-{nonce}.json").write_text(json.dumps(attachments) + "\n", encoding="utf-8")
    manifest["files"]["placeholder"]["attachments"] = f"placeholder-attachments-{nonce}.json"
    (out_dir / "files.json").write_text(json.dumps(manifest, indent=1) + "\n", encoding="utf-8")
    plan = {
        "t_a2": {"sessions": [
            {"label": "S1", "kind": "pdf", "file": files["s1"][0], "transcript": "s1-stream.jsonl"},
            {"label": "S2", "kind": "docx", "file": files["s2"][0], "transcript": "s2-stream.jsonl"}]},
        "t_a3": {"upload_id": "<uploadId>", "file": files["page"][0], "page_done": "t-a3-done.html",
                 "foreign_page": "t-a3-foreign.html", "foreign_submit": "t-a3-foreign-submit.txt"},
        "t_a4": {"file": files["scan"][0], "token_upload_id": "<uploadId>", "page_upload_id": "<uploadId>",
                 "token_answer": "t-a4-token.txt", "page_answer": "t-a4-page.txt"},
        "t_a5": {"png_file": files["png"][0], "png_answer": "t-a5-png.txt",
                 "placeholder_file": placeholder[0], "placeholder_answer": "t-a5-placeholder.txt",
                 "size_file": files["size"][0], "size_upload_id": "<uploadId>",
                 "size_declared": manifest["files"]["size"]["declared_bytes"], "size_answer": "t-a5-size.txt"},
    }
    (out_dir / "plan-vorlage.json").write_text(json.dumps(plan, indent=1) + "\n", encoding="utf-8")
    return manifest




@dataclass
class ToolCall:
    name: str
    arguments: dict
    ok: bool | None = None
    text: str = ""


@dataclass
class Transcript:
    calls: list[ToolCall] = field(default_factory=list)
    finished: bool = False


def _block_text(content: object) -> str:
    if isinstance(content, str):
        return content
    if isinstance(content, list):
        return "".join(str(b.get("text") or "") for b in content if isinstance(b, dict) and b.get("type") == "text")
    return ""


def read_stream_json(lines: Iterable[str]) -> Transcript:
    """Tool calls of a Claude Code ``stream-json`` transcript (``claude -p --output-format stream-json --verbose``)."""
    transcript = Transcript()
    by_id: dict[str, ToolCall] = {}
    for raw in lines:
        try:
            entry = json.loads(raw)
        except ValueError:
            continue
        if not isinstance(entry, dict):
            continue
        if entry.get("type") == "result":
            transcript.finished = True
            continue
        message = entry.get("message")
        content = message.get("content") if isinstance(message, dict) else None
        if not isinstance(content, list):
            continue
        for block in content:
            if not isinstance(block, dict):
                continue
            if block.get("type") == "tool_use":
                call = ToolCall(str(block.get("name") or ""), block.get("input") if isinstance(block.get("input"), dict) else {})
                by_id[str(block.get("id"))] = call
                transcript.calls.append(call)
            elif block.get("type") == "tool_result" and str(block.get("tool_use_id")) in by_id:
                call = by_id[str(block.get("tool_use_id"))]
                call.ok = not bool(block.get("is_error"))
                call.text = _block_text(block.get("content"))
    return transcript


def _walk_strings(value: object) -> Iterator[tuple[str | None, str]]:
    if isinstance(value, dict):
        for key, item in value.items():
            if isinstance(item, str):
                yield str(key), item
            yield from _walk_strings(item)
    elif isinstance(value, list):
        for item in value:
            if isinstance(item, str):
                yield None, item
            yield from _walk_strings(item)


def _embedded_json(text: str) -> object | None:
    stripped = text.strip()
    if not stripped or stripped[0] not in "[{":
        return None
    try:
        return json.loads(stripped)
    except ValueError:
        return None


def _base64_lengths(value: object, depth: int = 0) -> Iterator[int]:
    for key, text in _walk_strings(value):
        if key is not None and key.lower() == "contentbase64":
            yield len(text)
        for match in BASE64_FIELD_RE.finditer(text):
            yield len(re.sub(r"\\[nrt]|\s", "", match.group(1)))
        if depth < 3:
            embedded = _embedded_json(text)
            if embedded is not None:
                yield from _base64_lengths(embedded, depth + 1)


def largest_base64(transcript: Transcript) -> int:
    return max((n for call in transcript.calls for n in _base64_lengths(call.arguments)), default=0)


def _json_objects(text: str) -> list[object]:
    decoder = json.JSONDecoder()
    found, index = [], 0
    while True:
        start = min((p for p in (text.find("{", index), text.find("[", index)) if p >= 0), default=-1)
        if start < 0:
            return found
        try:
            value, end = decoder.raw_decode(text, start)
        except ValueError:
            index = start + 1
            continue
        found.append(value)
        index = end


def _pairs(value: object) -> Iterator[tuple[str, object]]:
    if isinstance(value, dict):
        for key, item in value.items():
            yield str(key), item
            yield from _pairs(item)
    elif isinstance(value, list):
        for item in value:
            yield from _pairs(item)


def _answer(call: ToolCall) -> dict | None:
    for value in _json_objects(call.text):
        if isinstance(value, dict) and set(value) == {"result"} and isinstance(value["result"], dict):
            return value["result"]
        if isinstance(value, dict):
            return value
    return None


def _same_id(left: object, right: object) -> bool:
    return isinstance(left, str) and isinstance(right, str) and left.lower() == right.lower()


def _upload_ids_of(arguments: dict) -> list[str]:
    value = arguments.get("uploadIds")
    if isinstance(value, str):
        value = _embedded_json(value)
    return [v for v in value if isinstance(v, str)] if isinstance(value, list) else []


def curl_evidence(call: ToolCall, upload_id: str) -> str | None:
    """The SHA-256 of a usable 201 answer of ``curl`` for ``upload_id`` in a shell call, or ``None``."""
    command = call.arguments.get("command")
    if not call.ok or not isinstance(command, str) or "curl" not in command:
        return None
    if not any(_same_id(m.group(1), upload_id) for m in TOKEN_URL_RE.finditer(command)):
        return None
    for value in _json_objects(call.text):
        pairs = list(_pairs(value))
        ids = [v for k, v in pairs if k == "uploadId"]
        shas = [v for k, v in pairs if k == "sha256" and isinstance(v, str) and SHA_RE.match(v.lower())]
        usable = [v for k, v in pairs if k == "usable"]
        if any(_same_id(i, upload_id) for i in ids) and shas and usable and all(u is True for u in usable):
            return str(shas[0]).lower()
    return None


def _tool(call: ToolCall, name: str) -> bool:
    return call.name == name or call.name.endswith(f"__{name}")




class Database(Protocol):
    def now(self) -> str: ...

    def ticket(self, upload_id: str, local_sha: str, order_id: str | None) -> dict | None: ...

    def window(self, start: str, end: str, names: list[str]) -> dict: ...


def _uuid_literal(value: str | None) -> str:
    if value is None:
        return "NULL::uuid"
    if not UUID_RE.match(value):
        raise UsageError("keine gültige Id")
    return f"'{value.lower()}'::uuid"


def _sha_literal(value: str) -> str:
    if not SHA_RE.match(value):
        raise UsageError("kein gültiger SHA-256")
    return f"'{value}'"


def _time_literal(value: str) -> str:
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exc:
        raise UsageError("keine gültige Zeitmarke") from exc
    if parsed.tzinfo is None:
        raise UsageError("Zeitmarke ohne Zeitzone")
    return f"'{parsed.isoformat()}'::timestamptz"


def _names_literal(names: list[str]) -> str:
    patterns = []
    for name in names:
        if not GENERATED_NAME_RE.match(name):
            raise UsageError("Dateiname außerhalb von [a-z0-9 .'()-]; das Werkzeug vergleicht nur selbst erzeugte Namen")
        escaped = name.replace("'", "''")
        patterns.append(f"'%{escaped}%'")
    return f"ARRAY[{', '.join(patterns)}]::text[]" if patterns else "ARRAY[]::text[]"


def ticket_sql(upload_id: str, local_sha: str, order_id: str | None) -> str:
    order = _uuid_literal(order_id)
    finished = ", ".join(f"'{s}'" for s in FINISHED_RUN_STATES)
    return f"""SELECT json_build_object(
  'state', t."State",
  'attempts', t."Attempts",
  'last_refusal_code', t."LastRefusalCode",
  'has_source', t."SourceId" IS NOT NULL,
  'source_exists', s."Id" IS NOT NULL,
  'same_owner', s."OwnerUsername" = t."OwnerUsername",
  'sha_equal', s."Sha256" = {_sha_literal(local_sha)},
  'origin', s."Origin",
  'linked', EXISTS (SELECT 1 FROM "OrderSources" os WHERE os."OrderId" = {order} AND os."SourceId" = t."SourceId"),
  'foreign_links', (SELECT count(*) FROM "OrderSources" os JOIN "Orders" o ON o."Id" = os."OrderId"
                    WHERE os."SourceId" = t."SourceId" AND o."CreatedByUser" IS DISTINCT FROM t."OwnerUsername"),
  'runs_finished', (SELECT count(*) FROM "Runs" r WHERE r."OrderId" = {order} AND r."Status" IN ({finished})),
  'writer_rows', (SELECT count(*) FROM "RunSourceUsages" u JOIN "Runs" r ON r."Id" = u."RunId"
                  WHERE r."OrderId" = {order} AND u."SourceId" = t."SourceId" AND u."Target" = 'writer'),
  'writer_full', (SELECT count(*) FROM "RunSourceUsages" u JOIN "Runs" r ON r."Id" = u."RunId"
                  WHERE r."OrderId" = {order} AND u."SourceId" = t."SourceId" AND u."Target" = 'writer'
                    AND u."Mode" = 'FullText' AND u."CharsIncluded" = s."CharCount"))
FROM "UploadTickets" t LEFT JOIN "Sources" s ON s."Id" = t."SourceId"
WHERE t."Id" = {_uuid_literal(upload_id)}"""


def window_sql(start: str, end: str, names: list[str]) -> str:
    span = f'"CreatedAt" >= {_time_literal(start)} AND "CreatedAt" <= {_time_literal(end)}'
    return f"""SELECT json_build_object(
  'sources', (SELECT count(*) FROM "Sources" WHERE {span}),
  'refusals', (SELECT coalesce(json_agg(to_jsonb(r) - 'OwnerUsername' - 'Id' ORDER BY r."CreatedAt"), '[]'::json)
               FROM "SourceRefusals" r WHERE {span}),
  'owner_has_name', (SELECT count(*) FROM "SourceRefusals" r WHERE {span}
                     AND r."OwnerUsername" ILIKE ANY ({_names_literal(names)})),
  'columns', (SELECT coalesce(json_agg(column_name::text ORDER BY ordinal_position), '[]'::json)
              FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = 'SourceRefusals'))"""


NOW_SQL = "SELECT json_build_object('now', now())"

PSQL_SCRIPT = ('PGOPTIONS="-c default_transaction_read_only=on" exec psql -X -q -At -v ON_ERROR_STOP=1 '
               '-U "$POSTGRES_USER" -d "$POSTGRES_DB" -f -')


class PsqlDatabase:
    """Read-only queries over ``docker exec <container> psql``; user and database come from the container's environment."""

    def __init__(self, container: str, runner: Callable[..., subprocess.CompletedProcess] = subprocess.run):
        self.container = container
        self.runner = runner

    def command(self) -> list[str]:
        return ["docker", "exec", "-i", self.container, "sh", "-c", PSQL_SCRIPT]

    def query(self, sql: str) -> object:
        script = f"BEGIN TRANSACTION READ ONLY;\n{sql};\nROLLBACK;\n"
        try:
            done = self.runner(self.command(), input=script, capture_output=True, text=True, timeout=120)
        except (OSError, subprocess.TimeoutExpired) as exc:
            raise DatabaseError(f"Abfrage nicht möglich: {type(exc).__name__}") from exc
        if done.returncode != 0:
            first = (done.stderr or "").strip().splitlines()[:1]
            raise DatabaseError(f"Abfrage gescheitert (Rückgabe {done.returncode}): {first[0][:200] if first else 'ohne Meldung'}")
        out = (done.stdout or "").strip()
        if not out:
            return None
        try:
            return json.loads(out)
        except ValueError as exc:
            raise DatabaseError("Antwort der Datenbank ist kein JSON") from exc

    def now(self) -> str:
        value = self.query(NOW_SQL)
        if not isinstance(value, dict) or not isinstance(value.get("now"), str):
            raise DatabaseError("Zeit der Datenbank nicht lesbar")
        return value["now"]

    def ticket(self, upload_id: str, local_sha: str, order_id: str | None) -> dict | None:
        value = self.query(ticket_sql(upload_id, local_sha, order_id))
        if value is not None and not isinstance(value, dict):
            raise DatabaseError("Antwort zum Ticket nicht lesbar")
        return value

    def window(self, start: str, end: str, names: list[str]) -> dict:
        value = self.query(window_sql(start, end, names))
        if not isinstance(value, dict):
            raise DatabaseError("Antwort zum Zeitfenster nicht lesbar")
        return value




def _norm(value: object) -> str:
    return re.sub(r"[_\s-]", "", str(value)).lower() if value is not None else ""


def collapse(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def page_text(markup: str) -> str:
    stripped = re.sub(r"<(script|style)\b.*?</\1\s*>", " ", markup, flags=re.IGNORECASE | re.DOTALL)
    return collapse(html.unescape(re.sub(r"<[^>]+>", " ", stripped)))


def _all_text(body: str) -> str:
    parts = [body]
    for value in _json_objects(body):
        parts.extend(text for _, text in _walk_strings(value))
    return collapse("\n".join(parts))


def read_answer(path: Path) -> tuple[int | None, str]:
    """A captured HTTP answer: the body, then the status code alone on the last line (``curl -w '\\n%{http_code}\\n'``)."""
    lines = path.read_text(encoding="utf-8").rstrip().splitlines()
    if lines and STATUS_LINE_RE.match(lines[-1].strip()):
        return int(lines[-1].strip()), "\n".join(lines[:-1])
    return None, "\n".join(lines)


def answer_says(body: str, sentence: str, ignore_case: bool = False) -> bool:
    if ignore_case:
        return collapse(sentence).lower() in _all_text(body).lower()
    return collapse(sentence) in _all_text(body)


@dataclass
class Part:
    """One criterion: ``ja``, ``nein``, ``fehlt`` or ``folgt`` and a German reason without names, ids or contents."""

    state: str
    why: str = ""


def _gate(name: str, parts: dict[str, Part], problems: list[str], counts: dict | None = None) -> dict:
    states = [p.state for p in parts.values()]
    if NO in states:
        verdict = "failed"
    elif MISSING in states or problems or not parts:
        verdict = "not_evaluable"
    else:
        verdict = "passed"
    return {"gate": name, "verdict": verdict, "parts": {k: {"state": p.state, "why": p.why} for k, p in parts.items()},
            "problems": problems, "counts": counts or {}}


class Inputs:
    """Resolves plan paths against the plan directory and records what is missing (never a name in the message)."""

    def __init__(self, base: Path):
        self.base = base

    def path(self, value: object) -> Path | None:
        if not isinstance(value, str) or not value or value.startswith("<"):
            return None
        path = Path(value)
        return path if path.is_absolute() else self.base / path


def _uuid(value: object) -> str | None:
    return value.lower() if isinstance(value, str) and UUID_RE.match(value) else None


def _file_names(path: Path) -> list[str]:
    name = path.name.lower()
    stem = name.rsplit(".", 1)[0]
    return [n for n in dict.fromkeys([name, stem]) if len(n) >= 6]




def _session(entry: dict, inputs: Inputs, db: Database) -> tuple[dict, dict]:
    label = str(entry.get("label") or "?")
    kind = entry.get("kind")
    parts: dict[str, Part] = {}
    counts: dict = {}
    keys = ["a_prepare", "b_curl_201", "c_submit_upload_ids", "d_bytes_equal", "e_base64_small", "f_linked"]
    if kind == "pdf":
        keys.append("g_writer_full_text")
    file = inputs.path(entry.get("file"))
    transcript_path = inputs.path(entry.get("transcript"))
    local_sha = None
    if file is None or not file.is_file():
        parts["file"] = Part(MISSING, "lokale Datei fehlt")
    else:
        data = file.read_bytes()
        if kind == "pdf" and not (is_pdf(data) and len(data) >= MIN_S1_BYTES and pdf_page_count(data) >= MIN_S1_PAGES):
            parts["file"] = Part(MISSING, f"Datei ist kein PDF mit ≥ {MIN_S1_BYTES} Bytes und ≥ {MIN_S1_PAGES} Seiten")
        elif kind == "docx" and not is_docx(file):
            parts["file"] = Part(MISSING, "Datei ist kein DOCX")
        else:
            local_sha = hashlib.sha256(data).hexdigest()
            counts["bytes"] = len(data)
    transcript = None
    if transcript_path is None or not transcript_path.is_file():
        parts["transcript"] = Part(MISSING, "Mitschnitt fehlt")
    else:
        transcript = read_stream_json(transcript_path.read_text(encoding="utf-8").splitlines())
        if not transcript.finished:
            parts["transcript"] = Part(MISSING, "Mitschnitt unvollständig (keine Ergebniszeile)")
            transcript = None
    if transcript is None or local_sha is None:
        for key in keys:
            parts[key] = Part(MISSING, "ohne Mitschnitt oder Datei nicht prüfbar")
        return {"label": label, "kind": kind, "parts": parts, "counts": counts}, {}

    biggest = largest_base64(transcript)
    counts["largest_content_base64"] = biggest
    counts["tool_calls"] = len(transcript.calls)
    prepared = []
    for call in transcript.calls:
        if _tool(call, "prepare_attachment") and call.ok:
            answer = _answer(call)
            upload = _uuid(answer.get("uploadId")) if answer else None
            if upload:
                prepared.append(upload)
    counts["prepare_attachment_calls"] = sum(1 for c in transcript.calls if _tool(c, "prepare_attachment"))
    parts["a_prepare"] = Part(YES) if prepared else Part(NO, "kein erfolgreicher Aufruf von prepare_attachment")

    uploaded: dict[str, str] = {}
    for call in transcript.calls:
        for upload in prepared:
            sha = curl_evidence(call, upload)
            if sha:
                uploaded[upload] = sha
    if not prepared:
        parts["b_curl_201"] = Part(SKIPPED)
    else:
        parts["b_curl_201"] = Part(YES) if uploaded else Part(NO, "kein curl mit brauchbarer Quittung (201) für ein Ticket")

    chosen, order = None, None
    for call in transcript.calls:
        if _tool(call, "submit_order") and call.ok:
            answer = _answer(call)
            order_id = _uuid(answer.get("orderId")) if answer else None
            hits = [u for u in _upload_ids_of(call.arguments) if _uuid(u) in uploaded]
            if order_id and hits:
                chosen, order = _uuid(hits[-1]), order_id
    if not uploaded:
        parts["c_submit_upload_ids"] = Part(SKIPPED)
    else:
        parts["c_submit_upload_ids"] = (Part(YES) if order else
                                        Part(NO, "kein erfolgreiches submit_order mit der Upload-Id in uploadIds"))
    if chosen is None and uploaded:
        chosen = next(reversed(uploaded))

    parts["e_base64_small"] = (Part(YES) if biggest <= MAX_INLINE_BASE64 else
                               Part(NO, f"größtes contentBase64 {biggest} Zeichen > {MAX_INLINE_BASE64}"))

    row = db.ticket(chosen, local_sha, order) if chosen else None
    if chosen is None:
        parts["d_bytes_equal"] = Part(SKIPPED)
    elif row is None:
        parts["d_bytes_equal"] = Part(NO, "Ticket in der Datenbank nicht gefunden")
    else:
        problems = []
        if _norm(row.get("state")) != "used" or not row.get("has_source") or not row.get("source_exists"):
            problems.append("Ticket nicht Used mit Quelle")
        if row.get("same_owner") is not True:
            problems.append("Quelle gehört nicht dem Besitzer des Tickets")
        if row.get("sha_equal") is not True:
            problems.append("Sources.Sha256 ≠ lokale SHA-256")
        if uploaded.get(chosen) != local_sha:
            problems.append("sha256 der curl-Antwort ≠ lokale SHA-256")
        if _norm(row.get("origin")) != "mcpattachment":
            problems.append("Herkunft der Quelle nicht McpAttachment")
        parts["d_bytes_equal"] = Part(NO, "; ".join(problems)) if problems else Part(YES)

    if order is None or row is None:
        parts["f_linked"] = Part(SKIPPED)
        if kind == "pdf":
            parts["g_writer_full_text"] = Part(SKIPPED)
    else:
        parts["f_linked"] = Part(YES) if row.get("linked") is True else Part(NO, "Auftrag nicht mit der Quelle verknüpft")
        if kind == "pdf":
            writer, full, finished = (int(row.get(k) or 0) for k in ("writer_rows", "writer_full", "runs_finished"))
            counts["writer_rows"] = writer
            if writer == 0 and finished == 0:
                parts["g_writer_full_text"] = Part(MISSING, "noch kein Lauf des Auftrags fertig und keine Schreiber-Zeile")
            elif writer == 0:
                parts["g_writer_full_text"] = Part(NO, "keine Schreiber-Zeile in RunSourceUsages")
            elif full != writer:
                parts["g_writer_full_text"] = Part(NO, "Schreiber-Zeile nicht FullText oder CharsIncluded ≠ CharCount")
            else:
                parts["g_writer_full_text"] = Part(YES)
    ordered = {k: parts[k] for k in ("file", "transcript", *keys) if k in parts}
    return {"label": label, "kind": kind, "parts": ordered, "counts": counts}, {"upload": chosen, "sha": local_sha}


def evaluate_t_a2(plan: dict, inputs: Inputs, db: Database) -> tuple[dict, set[str]]:
    section = plan.get("t_a2")
    sessions = section.get("sessions") if isinstance(section, dict) else None
    problems: list[str] = []
    if not isinstance(sessions, list) or not all(isinstance(s, dict) for s in sessions):
        return _gate("T-A2", {}, ["Plan nennt keine Sitzungen"]), set()
    kinds = sorted(str(s.get("kind")) for s in sessions)
    if kinds != ["docx", "pdf"]:
        problems.append("Plan braucht genau zwei Sitzungen: eine pdf (S1) und eine docx (S2)")
    labels = [str(s.get("label")) for s in sessions]
    if len(set(labels)) != len(labels):
        problems.append("Bezeichnungen der Sitzungen nicht eindeutig")
    rows, parts, uploads, shas = [], {}, [], []
    for entry in sessions:
        row, used = _session(entry, inputs, db)
        rows.append(row)
        for key, part in row["parts"].items():
            parts[f"{row['label']}.{key}"] = part
        if used.get("upload"):
            uploads.append(used["upload"])
        if used.get("sha"):
            shas.append(used["sha"])
    if len(set(uploads)) != len(uploads):
        problems.append("beide Sitzungen nutzen dasselbe Ticket")
    if len(set(shas)) != len(shas):
        problems.append("beide Sitzungen nutzen dieselbe Datei")
    report = _gate("T-A2", parts, problems)
    report["sessions"] = [{"label": r["label"], "kind": r["kind"],
                           "parts": {k: {"state": p.state, "why": p.why} for k, p in r["parts"].items()},
                           "counts": r["counts"]} for r in rows]
    report["passed_sessions"] = sum(1 for r in rows if all(p.state == YES for p in r["parts"].values()))
    halt = any(k.endswith(("d_bytes_equal", "f_linked", "g_writer_full_text")) and p.state == NO for k, p in parts.items())
    report["halt"] = halt
    follow = []
    if any(k.endswith("d_bytes_equal") and p.state == NO for k, p in parts.items()):
        follow.append(FOLLOW_UP["bytes"])
    if any(k.endswith(("f_linked", "g_writer_full_text")) and p.state == NO for k, p in parts.items()):
        follow.append(FOLLOW_UP["source"])
    if report["verdict"] == "failed" and not halt:
        follow.append(FOLLOW_UP["sentences"])
    report["follow_up"] = follow
    return report, set(uploads)




def evaluate_t_a3(plan: dict, inputs: Inputs, db: Database) -> tuple[dict, set[str]]:
    section = plan.get("t_a3")
    if not isinstance(section, dict):
        return _gate("T-A3", {}, ["Plan nennt T-A3 nicht"]), set()
    parts: dict[str, Part] = {}
    upload = _uuid(section.get("upload_id"))
    file = inputs.path(section.get("file"))
    done, foreign, submit = (inputs.path(section.get(k)) for k in ("page_done", "foreign_page", "foreign_submit"))
    local_sha = sha256_file(file) if file and file.is_file() else None
    names = _file_names(file) if file else []

    if done is None or not done.is_file():
        parts["a_page_done"] = Part(MISSING, "Seitenabbild nach dem Hochladen fehlt")
    else:
        markup = done.read_text(encoding="utf-8")
        problems = []
        if collapse(PAGE_DONE) not in page_text(markup):
            problems.append("Satz „Done …“ fehlt")
        if RECEIPT_MARK not in markup:
            problems.append("Quittung fehlt")
        parts["a_page_done"] = Part(NO, "; ".join(problems)) if problems else Part(YES)

    row = None
    if upload is None or local_sha is None:
        parts["b_bytes_equal"] = Part(MISSING, "Upload-Id oder lokale Datei fehlt")
    else:
        row = db.ticket(upload, local_sha, None)
        if row is None:
            parts["b_bytes_equal"] = Part(NO, "Ticket in der Datenbank nicht gefunden")
        else:
            problems = []
            if _norm(row.get("state")) != "used" or not row.get("has_source") or not row.get("source_exists"):
                problems.append("Ticket nicht Used mit Quelle")
            if row.get("same_owner") is not True:
                problems.append("Quelle gehört nicht dem Besitzer des Tickets")
            if row.get("sha_equal") is not True:
                problems.append("Sources.Sha256 ≠ lokale SHA-256")
            if _norm(row.get("origin")) != "webupload":
                problems.append("Herkunft der Quelle nicht WebUpload (nicht über die Seite)")
            parts["b_bytes_equal"] = Part(NO, "; ".join(problems)) if problems else Part(YES)

    if foreign is None or not foreign.is_file() or submit is None or not submit.is_file() or upload is None or row is None:
        parts["c_second_account"] = Part(MISSING, "Seitenabbild oder submit_order-Antwort des zweiten Kontos fehlt")
    else:
        markup = foreign.read_text(encoding="utf-8")
        text = page_text(markup)
        lowered = text.lower()
        problems = []
        address = page_address(markup)
        if address is not None and not is_page_of(address, upload):
            problems.append("Seitenabbild des zweiten Kontos zeigt nicht die Seite dieses Uploads")
        if NOT_FOUND_PAGE not in lowered:
            problems.append("Seite des zweiten Kontos sagt nicht „Not found“")
        if any(n in lowered for n in names):
            problems.append("Seite des zweiten Kontos zeigt den Dateinamen")
        if any(collapse(s).lower() in lowered for s in (PAGE_DONE, PAGE_HEADING, PAGE_INTRO_START, PAGE_ALREADY_USED, PAGE_EXPIRED)):
            problems.append("Seite des zweiten Kontos zeigt die Upload-Seite")
        if not answer_says(submit.read_text(encoding="utf-8"), NOT_FOUND_FORMAT.format(upload), ignore_case=True):
            problems.append("submit_order des zweiten Kontos sagt nicht „not found“")
        if int(row.get("foreign_links") or 0) != 0:
            problems.append("ein Auftrag einer anderen Person verknüpft die Quelle")
        if problems:
            parts["c_second_account"] = Part(NO, "; ".join(problems))
        elif address is None:
            parts["c_second_account"] = Part(MISSING, "Seitenabbild des zweiten Kontos nennt seine Adresse nicht (<!-- url: … -->)")
        else:
            parts["c_second_account"] = Part(YES)
    report = _gate("T-A3", parts, [])
    report["halt"] = report["verdict"] == "failed"
    report["follow_up"] = [FOLLOW_UP["halt"]] if report["halt"] else []
    return report, {upload} if upload else set()




def page_address(markup: str) -> str | None:
    """The address a capture names in its first line (``<!-- url: <address> -->``), or ``None``."""
    match = PAGE_URL_RE.match(markup)
    return match.group(1) if match else None


def is_page_of(address: str, upload: str) -> bool:
    """Whether ``address`` is the upload page ``/u/<upload>`` (any host, no further path)."""
    path = urlsplit(address).path.rstrip("/")
    return path.lower().endswith((PAGE_PATH + upload).lower()) and path.lower().count(PAGE_PATH) == 1


def _answer_upload_ids(body: str) -> list[str]:
    return [str(value) for obj in _json_objects(body) for key, value in _pairs(obj) if key == "uploadId"]


def _check_answer(path: Path | None, status: int, sentence: str, what: str, upload: str | None = None) -> Part:
    if path is None or not path.is_file():
        return Part(MISSING, f"Antwort {what} fehlt")
    code, body = read_answer(path)
    if code is None:
        return Part(MISSING, f"Antwort {what} ohne Statuszeile")
    problems = []
    if code != status:
        problems.append(f"Status {code} statt {status}")
    if not answer_says(body, sentence):
        problems.append("erwarteter Satz fehlt")
    if upload is not None:
        ids = _answer_upload_ids(body)
        if not ids or any(not _same_id(i, upload) for i in ids):
            problems.append("Antwort nennt nicht die Upload-Id des Plans")
    return Part(NO, f"{what}: " + "; ".join(problems)) if problems else Part(YES)


def _refused_ticket(db: Database, upload: str | None, path: Path | None, what: str, code: str) -> Part:
    if upload is None or path is None or not path.is_file():
        return Part(MISSING, f"Upload-Id oder Datei {what} fehlt")
    row = db.ticket(upload, sha256_file(path), None)
    if row is None:
        return Part(NO, f"Ticket {what} in der Datenbank nicht gefunden")
    if row.get("has_source") or _norm(row.get("state")) == "used":
        return Part(NO, f"Ticket {what} hat eine Quelle oder ist Used")
    attempts = row.get("attempts")
    if not isinstance(attempts, int) or isinstance(attempts, bool) or attempts < 1 or row.get("last_refusal_code") != code:
        return Part(NO, f"Ticket {what} zeigt keinen geprüften, abgelehnten Versuch mit Code {code}")
    return Part(YES)


def _vocabulary_problems(rows: list[dict], columns: list) -> list[str]:
    problems = []
    if set(map(str, columns)) != REFUSAL_COLUMNS:
        problems.append("Spalten von SourceRefusals weichen vom Vertrag ab")
    for row in rows:
        extra = set(row) - (REFUSAL_COLUMNS - {"Id", "OwnerUsername"})
        if extra:
            problems.append("Zeile mit Spalte außerhalb des Vertrags")
        if _norm(row.get("Path")) not in {_norm(p) for p in REFUSAL_PATHS}:
            problems.append("Weg außerhalb des Wortschatzes")
        if row.get("Code") not in REFUSAL_CODES:
            problems.append("Code außerhalb des Wortschatzes")
        if row.get("Kind") is not None and _norm(row.get("Kind")) not in SOURCE_KINDS:
            problems.append("Art außerhalb des Wortschatzes")
        if row.get("Extension") is not None and not EXTENSION_RE.match(str(row.get("Extension"))):
            problems.append("Endung außerhalb des Wortschatzes")
        declared = row.get("DeclaredType")
        if declared is not None and (len(str(declared)) > 100 or not MEDIA_TYPE_RE.match(str(declared))):
            problems.append("erklärter Typ außerhalb des Wortschatzes")
        size = row.get("SizeBytes")
        if size is not None and (not isinstance(size, int) or isinstance(size, bool) or size < 0):
            problems.append("Größe außerhalb des Wortschatzes")
    return list(dict.fromkeys(problems))


def _rows_part(window: dict, expected: list[tuple[str, str, str]], names: list[str]) -> tuple[Part, Part, dict]:
    rows = window.get("refusals")
    columns = window.get("columns")
    if not isinstance(rows, list) or not isinstance(columns, list) or not all(isinstance(r, dict) for r in rows):
        return Part(MISSING, "Zählzeilen nicht lesbar"), Part(MISSING, "Zählzeilen nicht lesbar"), {}
    canonical = {_norm(p): p for p in REFUSAL_PATHS}
    seen = sorted((str(r.get("Code")), _norm(r.get("Path")), str(r.get("Extension"))) for r in rows)
    want = sorted((code, _norm(path), ext) for code, path, ext in expected)
    counts = {"refusal_rows": len(rows), "expected_rows": len(expected),
              "by_code_and_path": sorted(f"{c if c in REFUSAL_CODES else 'other'}/{canonical.get(p, 'other')}" for c, p, _ in seen)}
    rows_ok = Part(YES) if seen == want else Part(NO, f"{len(rows)} Zählzeilen, erwartet genau {len(expected)} (Code, Weg, Endung)")
    problems = _vocabulary_problems(rows, columns)
    values = [str(v).lower() for r in rows for v in r.values() if v is not None]
    if any(n in v for n in names for v in values):
        problems.append("eine Spalte enthält einen Dateinamen")
    if int(window.get("owner_has_name") or 0):
        problems.append("OwnerUsername enthält einen Dateinamen")
    clean = Part(NO, "; ".join(problems)) if problems else Part(YES)
    return rows_ok, clean, counts


def load_marks(out_dir: Path) -> dict:
    path = out_dir / "marks.json"
    if not path.is_file():
        return {}
    value = json.loads(path.read_text(encoding="utf-8"))
    return value if isinstance(value, dict) else {}


def _window_bounds(marks: dict, gate: str) -> tuple[str, str] | None:
    entry = marks.get(gate)
    if not isinstance(entry, dict) or not isinstance(entry.get("start"), str) or not isinstance(entry.get("end"), str):
        return None
    try:
        start = datetime.fromisoformat(entry["start"].replace("Z", "+00:00"))
        end = datetime.fromisoformat(entry["end"].replace("Z", "+00:00"))
    except ValueError:
        return None
    if start.tzinfo is None or end.tzinfo is None or end <= start:
        return None
    return entry["start"], entry["end"]


def windows_overlap(marks: dict) -> bool:
    bounds = [_window_bounds(marks, g) for g in WINDOW_GATES]
    if any(b is None for b in bounds):
        return False
    (s4, e4), (s5, e5) = [(datetime.fromisoformat(s.replace("Z", "+00:00")), datetime.fromisoformat(e.replace("Z", "+00:00")))
                          for s, e in bounds]
    return s4 <= e5 and s5 <= e4


def evaluate_t_a4(plan: dict, inputs: Inputs, db: Database, marks: dict) -> tuple[dict, set[str]]:
    section = plan.get("t_a4")
    if not isinstance(section, dict):
        return _gate("T-A4", {}, ["Plan nennt T-A4 nicht"]), set()
    parts: dict[str, Part] = {}
    problems: list[str] = []
    file = inputs.path(section.get("file"))
    token_id, page_id = _uuid(section.get("token_upload_id")), _uuid(section.get("page_upload_id"))
    if token_id and token_id == page_id:
        problems.append("Token- und Seiten-Weg nutzen dasselbe Ticket")
    if file is None or not file.is_file() or not is_pdf(file.read_bytes()):
        parts["file"] = Part(MISSING, "Scan-PDF fehlt")
    parts["a_token_422"] = _check_answer(inputs.path(section.get("token_answer")), 422, NO_TEXT_REASON, "Token-Weg", token_id)
    parts["a_page_422"] = _check_answer(inputs.path(section.get("page_answer")), 422, NO_TEXT_REASON, "Seite", page_id)
    parts["a_token_ticket"] = _refused_ticket(db, token_id, file, "Token-Weg", "pdf_no_text")
    parts["a_page_ticket"] = _refused_ticket(db, page_id, file, "Seite", "pdf_no_text")
    bounds = _window_bounds(marks, "T-A4")
    counts: dict = {}
    if bounds is None or windows_overlap(marks) or file is None:
        why = "Marken von T-A4 fehlen, sind ungültig oder überlappen T-A5" if file is not None else "Scan-PDF fehlt"
        parts["b_no_new_sources"] = Part(MISSING, why)
        parts["c_rows_exact"] = Part(MISSING, why)
        parts["c_rows_clean"] = Part(MISSING, why)
    else:
        names = _file_names(file)
        window = db.window(bounds[0], bounds[1], names)
        counts["new_sources"] = int(window.get("sources") or 0)
        parts["b_no_new_sources"] = Part(YES) if counts["new_sources"] == 0 else Part(NO, f"{counts['new_sources']} neue Sources")
        expected = [("pdf_no_text", "mcp_ticket", "pdf"), ("pdf_no_text", "upload_page", "pdf")]
        parts["c_rows_exact"], parts["c_rows_clean"], more = _rows_part(window, expected, names)
        counts.update(more)
    report = _gate("T-A4", parts, problems, counts)
    report["halt"] = report["verdict"] == "failed"
    report["follow_up"] = [FOLLOW_UP["halt"]] if report["halt"] else []
    return report, {u for u in (token_id, page_id) if u}


def _size_sentence(actual: int, declared: int) -> str:
    return SIZE_MISMATCH_FORMAT.format(actual, declared) if actual < declared else LARGER_THAN_ANNOUNCED_FORMAT.format(declared)


def evaluate_t_a5(plan: dict, inputs: Inputs, db: Database, marks: dict) -> tuple[dict, set[str]]:
    section = plan.get("t_a5")
    if not isinstance(section, dict):
        return _gate("T-A5", {}, ["Plan nennt T-A5 nicht"]), set()
    parts: dict[str, Part] = {}
    files = {k: inputs.path(section.get(k)) for k in ("png_file", "placeholder_file", "size_file")}
    missing = [k for k, p in files.items() if p is None or not p.is_file()]
    for key in missing:
        parts[key] = Part(MISSING, "Datei fehlt")
    parts["a_png_web"] = _check_answer(inputs.path(section.get("png_answer")), 422, UNSUPPORTED_REASON, "PNG im Web")
    placeholder_answer = inputs.path(section.get("placeholder_answer"))
    if placeholder_answer is None or not placeholder_answer.is_file():
        parts["a_placeholder_mcp"] = Part(MISSING, "Antwort zum Platzhalter fehlt")
    else:
        parts["a_placeholder_mcp"] = (Part(YES) if answer_says(placeholder_answer.read_text(encoding="utf-8"), PLACEHOLDER_PROBLEM)
                                      else Part(NO, "Platzhalter-Antwort ohne den Satz des Parsers"))
    size_upload = _uuid(section.get("size_upload_id"))
    declared = section.get("size_declared")
    size_file = files["size_file"]
    if size_file is None or not size_file.is_file() or not isinstance(declared, int) or isinstance(declared, bool):
        parts["a_size_token"] = Part(MISSING, "Datei oder erklärte Größe fehlt")
    elif declared == size_file.stat().st_size:
        parts["a_size_token"] = Part(MISSING, "erklärte Größe gleich der echten Größe: keine falsche Größe")
    else:
        parts["a_size_token"] = _check_answer(inputs.path(section.get("size_answer")), 422,
                                              _size_sentence(size_file.stat().st_size, declared), "falsche Größe", size_upload)
    parts["a_size_ticket"] = _refused_ticket(db, size_upload, size_file, "falsche Größe", "size_mismatch")
    bounds = _window_bounds(marks, "T-A5")
    counts: dict = {}
    if bounds is None or windows_overlap(marks) or missing:
        why = "Marken von T-A5 fehlen, sind ungültig oder überlappen T-A4" if not missing else "Dateien fehlen"
        for key in ("b_no_new_sources", "c_rows_exact", "c_rows_clean"):
            parts[key] = Part(MISSING, why)
    else:
        names = [n for p in files.values() for n in _file_names(p)]
        window = db.window(bounds[0], bounds[1], names)
        counts["new_sources"] = int(window.get("sources") or 0)
        parts["b_no_new_sources"] = Part(YES) if counts["new_sources"] == 0 else Part(NO, f"{counts['new_sources']} neue Sources")
        expected = [("unsupported_type", "web", "png"), ("mcp_placeholder", "mcp_inline", "txt"),
                    ("size_mismatch", "mcp_ticket", "md")]
        parts["c_rows_exact"], parts["c_rows_clean"], more = _rows_part(window, expected, names)
        counts.update(more)
    report = _gate("T-A5", parts, [], counts)
    report["halt"] = report["verdict"] == "failed"
    report["follow_up"] = [FOLLOW_UP["halt"]] if report["halt"] else []
    return report, {size_upload} if size_upload else set()




def load_plan(path: Path) -> dict:
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise UsageError("Plan ist kein JSON-Objekt")
    return value


def evaluate(plan: dict, base: Path, db: Database, marks: dict, gates: Iterable[str] = GATES) -> dict:
    inputs = Inputs(base)
    chosen = [g for g in GATES if g in set(gates)]
    reports: dict[str, dict] = {}
    used: dict[str, set[str]] = {}
    for gate in chosen:
        if gate == "T-A2":
            reports[gate], used[gate] = evaluate_t_a2(plan, inputs, db)
        elif gate == "T-A3":
            reports[gate], used[gate] = evaluate_t_a3(plan, inputs, db)
        elif gate == "T-A4":
            reports[gate], used[gate] = evaluate_t_a4(plan, inputs, db, marks)
        else:
            reports[gate], used[gate] = evaluate_t_a5(plan, inputs, db, marks)
    for gate in chosen:
        others = set().union(*(ids for g, ids in used.items() if g != gate)) if len(used) > 1 else set()
        if used[gate] & others:
            reports[gate]["problems"].append("ein Ticket steht in mehreren Toren")
            if reports[gate]["verdict"] == "passed":
                reports[gate]["verdict"] = "not_evaluable"
    verdicts = [r["verdict"] for r in reports.values()]
    if "failed" in verdicts:
        verdict = "failed"
    elif "not_evaluable" in verdicts or not verdicts:
        verdict = "not_evaluable"
    else:
        verdict = "passed"
    follow = [line for r in reports.values() for line in r.get("follow_up", [])]
    if verdict == "passed" and set(chosen) == set(GATES):
        follow.append(FOLLOW_UP["passed"])
    return {"tool": "upload_check", "verdict": verdict, "gates": reports, "follow_up": list(dict.fromkeys(follow))}


def print_report(report: dict, echo: Callable[[str], None]) -> None:
    for name, gate in report["gates"].items():
        for session in gate.get("sessions", []):
            states = ", ".join(f"{k} {v['state']}" for k, v in session["parts"].items())
            numbers = ", ".join(f"{k} {v}" for k, v in session["counts"].items())
            echo(f"{name} {session['label']} ({session['kind']}): {states}" + (f" | {numbers}" if numbers else ""))
        if "sessions" not in gate:
            echo(f"{name}: " + ", ".join(f"{k} {v['state']}" for k, v in gate["parts"].items()))
            if gate["counts"]:
                echo(f"{name} Zählungen: " + ", ".join(f"{k} {v}" for k, v in gate["counts"].items()))
        for key, part in gate["parts"].items():
            if part["state"] in (NO, MISSING) and part["why"]:
                echo(f"  {key} {part['state']}: {part['why']}")
        for problem in gate["problems"]:
            echo(f"  nicht auswertbar: {problem}")
        echo(f"{name}: {gate['verdict']}")
    for line in report["follow_up"]:
        echo(line)
    echo(f"Gesamt: {report['verdict']}")




def _outside_git(path: Path, echo: Callable[[str], None], what: str) -> bool:
    if inside_git_worktree(path):
        echo(f"{what} liegt in einem Git-Baum; Mitschnitte, Antworten und Testdateien gehören nicht ins Repository.")
        return False
    return True


def main(argv: list[str] | None = None, *, database: Callable[[], Database] | None = None,
         echo: Callable[[str], None] = print) -> int:
    parser = argparse.ArgumentParser(prog="upload_check", description=__doc__.split("\n\n")[0])
    sub = parser.add_subparsers(dest="command", required=True)
    gen = sub.add_parser("generate", help="synthetische Testdateien erzeugen")
    gen.add_argument("--out-dir", required=True, type=Path)
    gen.add_argument("--seed", type=int)
    mark = sub.add_parser("mark", help="Zeit der Datenbank als Beginn oder Ende von T-A4/T-A5 merken")
    mark.add_argument("--gate", required=True, choices=WINDOW_GATES)
    mark.add_argument("--at", required=True, choices=("start", "end"))
    mark.add_argument("--out-dir", required=True, type=Path)
    comp = sub.add_parser("compare", help="lokale SHA-256 gegen die Quelle eines Tickets")
    comp.add_argument("--file", required=True, type=Path)
    comp.add_argument("--upload-id", required=True)
    big = sub.add_parser("largest-base64", help="größtes contentBase64 eines stream-json-Mitschnitts")
    big.add_argument("--transcript", required=True, type=Path)
    ev = sub.add_parser("evaluate", help="Tore T-A2 bis T-A5")
    ev.add_argument("--plan", required=True, type=Path)
    ev.add_argument("--out-dir", required=True, type=Path)
    ev.add_argument("--gate", action="append", choices=GATES)
    for p in (mark, comp, ev):
        p.add_argument("--container", default=os.environ.get("EVAL_PG_CONTAINER", "geef-atelier-postgres"))
    args = parser.parse_args(argv)
    connect = database or (lambda: PsqlDatabase(args.container))
    try:
        if args.command == "generate":
            if not _outside_git(args.out_dir, echo, "--out-dir"):
                return 2
            manifest = generate(args.out_dir, args.seed)
            echo(f"{len(manifest['files'])} synthetische Dateien erzeugt; Übersicht in files.json, Planvorlage in plan-vorlage.json "
                 f"(beides in --out-dir).")
            return 0
        if args.command == "largest-base64":
            if not args.transcript.is_file():
                raise UsageError("Mitschnitt fehlt")
            transcript = read_stream_json(args.transcript.read_text(encoding="utf-8").splitlines())
            biggest = largest_base64(transcript)
            echo(f"Werkzeugaufrufe {len(transcript.calls)}, Mitschnitt vollständig: {YES if transcript.finished else NO}, "
                 f"größtes contentBase64 {biggest} Zeichen, ≤ {MAX_INLINE_BASE64}: {YES if biggest <= MAX_INLINE_BASE64 else NO}")
            return 0 if transcript.finished else 4
        if args.command == "compare":
            upload = _uuid(args.upload_id)
            if upload is None or not args.file.is_file():
                raise UsageError("Upload-Id ungültig oder Datei fehlt")
            row = connect().ticket(upload, sha256_file(args.file), None)
            if row is None:
                echo("Ticket gefunden: nein")
                return 1
            equal = row.get("sha_equal") is True
            echo(f"Ticket gefunden: ja, Zustand Used: {YES if _norm(row.get('state')) == 'used' else NO}, "
                 f"Quelle: {YES if row.get('source_exists') else NO}, SHA-256 gleich: {YES if equal else NO}")
            return 0 if equal else 1
        if not _outside_git(args.out_dir, echo, "--out-dir"):
            return 2
        if args.command == "mark":
            marks = load_marks(args.out_dir)
            now = connect().now()
            marks.setdefault(args.gate, {})[args.at] = now
            args.out_dir.mkdir(parents=True, exist_ok=True)
            (args.out_dir / "marks.json").write_text(json.dumps(marks, indent=1) + "\n", encoding="utf-8")
            echo(f"Marke {args.gate} {args.at} gesetzt (Zeit der Datenbank).")
            return 0
        plan = load_plan(args.plan)
        report = evaluate(plan, args.plan.resolve().parent, connect(), load_marks(args.out_dir), args.gate or GATES)
    except UsageError as exc:
        echo(f"Aufruf oder Plan nicht brauchbar: {exc}")
        return 2
    except (OSError, ValueError) as exc:
        echo(f"Aufruf oder Plan nicht brauchbar: {type(exc).__name__}")
        return 2
    except DatabaseError as exc:
        echo(f"Datenbank: {exc}")
        return 3
    args.out_dir.mkdir(parents=True, exist_ok=True)
    (args.out_dir / "upload-gates.json").write_text(json.dumps(report, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print_report(report, echo)
    return {"passed": 0, "failed": 1}.get(report["verdict"], 4)


if __name__ == "__main__":
    sys.exit(main())
