"""Dry run of the gates T-A2 to T-A5: a synthetic world of correct behaviour (files, transcripts, captured answers, marks and
database rows) and a fake database with the semantics of the queries of ``upload_check``. Every misbehaviour of the tests is one
change of this world."""
from __future__ import annotations

import hashlib
import json
import uuid
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

from geef_eval import upload_check as uc

OWNER = "measure-user"
OTHER = "second-user"
TOKEN = "tok-" + "Q" * 40
ISSUER = "https://atelier.example"


def _id(n: int) -> str:
    return str(uuid.UUID(int=0xA7E1E0000000400080000000000000 + n))


def _time(text: str) -> datetime:
    return datetime.fromisoformat(text.replace("Z", "+00:00"))


@dataclass
class World:
    sources: dict = field(default_factory=dict)
    orders: dict = field(default_factory=dict)
    order_sources: list = field(default_factory=list)
    runs: dict = field(default_factory=dict)
    usages: list = field(default_factory=list)
    tickets: dict = field(default_factory=dict)
    refusals: list = field(default_factory=list)
    refusal_columns: list = field(default_factory=lambda: ["Id", "CreatedAt", "OwnerUsername", "Path", "Code", "Kind",
                                                            "Extension", "DeclaredType", "SizeBytes"])


class FakeDatabase:
    """Answers like ``PsqlDatabase`` over a :class:`World` (same NULL rules as the SQL)."""

    def __init__(self, world: World, now: str = "2026-12-01T12:00:00+00:00"):
        self.world = world
        self.clock = now
        self.calls: list[tuple] = []

    def now(self) -> str:
        return self.clock

    def ticket(self, upload_id: str, local_sha: str, order_id: str | None) -> dict | None:
        self.calls.append(("ticket", upload_id, order_id))
        w = self.world
        t = w.tickets.get(upload_id.lower())
        if t is None:
            return None
        sid = t.get("source_id")
        s = w.sources.get(sid) if sid else None
        order_runs = [rid for rid, r in w.runs.items() if order_id is not None and r["order"] == order_id]
        writer = [u for u in w.usages if sid and u["run"] in order_runs and u["source"] == sid and u["target"] == "writer"]
        return {
            "state": t["state"],
            "attempts": t.get("attempts", 0),
            "last_refusal_code": t.get("last_refusal_code"),
            "has_source": sid is not None,
            "source_exists": s is not None,
            "same_owner": None if s is None else s["owner"] == t["owner"],
            "sha_equal": None if s is None else s["sha"] == local_sha,
            "origin": None if s is None else s["origin"],
            "linked": bool(sid) and order_id is not None and (order_id, sid) in w.order_sources,
            "foreign_links": sum(1 for o, so in w.order_sources if sid and so == sid and w.orders.get(o) != t["owner"]),
            "runs_finished": sum(1 for rid in order_runs if w.runs[rid]["status"] in uc.FINISHED_RUN_STATES),
            "writer_rows": len(writer),
            "writer_full": sum(1 for u in writer if s is not None and u["mode"] == "FullText" and u["chars"] == s["char_count"]),
        }

    def window(self, start: str, end: str, names: list[str]) -> dict:
        self.calls.append(("window", start, end))
        lo, hi = _time(start), _time(end)
        inside = [r for r in sorted(self.world.refusals, key=lambda r: _time(r["CreatedAt"])) if lo <= _time(r["CreatedAt"]) <= hi]
        return {
            "sources": sum(1 for s in self.world.sources.values() if lo <= _time(s["created_at"]) <= hi),
            "refusals": [{k: v for k, v in r.items() if k not in ("OwnerUsername", "Id")} for r in inside],
            "owner_has_name": sum(1 for r in inside if any(n.lower() in str(r["OwnerUsername"]).lower() for n in names)),
            "columns": list(self.world.refusal_columns),
        }


def stream(*entries: dict) -> str:
    return "\n".join(json.dumps(e) for e in entries) + "\n"


def tool_use(call_id: str, name: str, arguments: dict) -> dict:
    return {"type": "assistant", "message": {"role": "assistant", "content": [
        {"type": "tool_use", "id": call_id, "name": name, "input": arguments}]}}


def tool_result(call_id: str, text: str, is_error: bool = False) -> dict:
    return {"type": "user", "message": {"role": "user", "content": [
        {"type": "tool_result", "tool_use_id": call_id, "content": [{"type": "text", "text": text}], "is_error": is_error}]}}


INIT = {"type": "system", "subtype": "init", "tools": ["Bash", "mcp__atelier__prepare_attachment"]}
RESULT = {"type": "result", "subtype": "success", "is_error": False, "result": "Done."}


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class Scenario:
    """A complete, correct run of all four gates in ``root`` (outside Git)."""

    def __init__(self, root: Path):
        self.root = root
        self.files_dir = root / "files"
        self.manifest = uc.generate(self.files_dir, seed=7)
        self.world = World()
        self.marks = {"T-A4": {"start": "2026-12-01T10:00:00+00:00", "end": "2026-12-01T10:10:00+00:00"},
                      "T-A5": {"start": "2026-12-01T10:20:00+00:00", "end": "2026-12-01T10:30:00+00:00"}}
        self.ids = {k: _id(i) for i, k in enumerate(
            ["u_s1", "u_s2", "u_t3", "u_scan_token", "u_scan_page", "u_size", "o_s1", "o_s2", "o_t3", "src_s1", "src_s2",
             "src_t3", "run_s1", "run_s2", "o_foreign"], 1)}
        self.plan: dict = {}
        self._t_a2()
        self._t_a3()
        self._t_a4()
        self._t_a5()

    def file(self, key: str) -> Path:
        return self.files_dir / self.manifest["files"][key]["file"]

    def db(self) -> FakeDatabase:
        return FakeDatabase(self.world)

    def evaluate(self, gates=uc.GATES) -> dict:
        return uc.evaluate(self.plan, self.root, self.db(), self.marks, gates)

    def write_plan(self) -> Path:
        path = self.root / "plan.json"
        path.write_text(json.dumps(self.plan), encoding="utf-8")
        return path

    def session_lines(self, label: str, upload: str, order: str, file: Path, *, curl_status_ok: bool = True,
                      answer_sha: str | None = None) -> list[dict]:
        prepared = {"uploadId": upload,
                    "curlCommand": f"curl --fail-with-body -sS --globoff -T '<path to the file>' -H 'X-Upload-Token: {TOKEN}' "
                                   f"{ISSUER}/api/uploads/{upload}",
                    "uploadPage": f"{ISSUER}/u/{upload}", "tokenExpiresAt": "2026-12-01T09:15:00+00:00",
                    "pageExpiresAt": "2026-12-02T09:00:00+00:00", "maxBytes": 26214400,
                    "next": "Run curlCommand with the path of the file."}
        receipt = {"uploadId": upload, "sha256": answer_sha or sha(file), "next": f'Now call submit_order with uploadIds: ["{upload}"].',
                   "receipt": {"fileName": file.name, "kind": "pdf", "usable": curl_status_ok, "charCount": 38000,
                               "reason": None if curl_status_ok else uc.NO_TEXT_REASON}}
        return [
            INIT,
            tool_use(f"{label}-1", "mcp__atelier__prepare_attachment", {"fileName": file.name, "sizeBytes": file.stat().st_size}),
            tool_result(f"{label}-1", json.dumps(prepared)),
            tool_use(f"{label}-2", "Bash", {"command": f"curl --fail-with-body -sS --globoff -T '{file}' -H 'X-Upload-Token: {TOKEN}' "
                                                       f"{ISSUER}/api/uploads/{upload}"}),
            tool_result(f"{label}-2", json.dumps(receipt), is_error=not curl_status_ok),
            tool_use(f"{label}-3", "mcp__atelier__submit_order", {"request": "Summarise the attached file.", "uploadIds": [upload]}),
            tool_result(f"{label}-3", json.dumps({"orderId": order, "state": "clarifying", "files": [{"fileName": file.name}]})),
            RESULT,
        ]

    def write_session(self, label: str, lines: list[dict]) -> None:
        (self.root / f"{label.lower()}-stream.jsonl").write_text(stream(*lines), encoding="utf-8")

    def _t_a2(self) -> None:
        i, w = self.ids, self.world
        for label, key, upload, order, source, run, chars in (("S1", "s1", i["u_s1"], i["o_s1"], i["src_s1"], i["run_s1"], 38000),
                                                              ("S2", "s2", i["u_s2"], i["o_s2"], i["src_s2"], i["run_s2"], 4000)):
            file = self.file(key)
            w.sources[source] = {"owner": OWNER, "sha": sha(file), "origin": "McpAttachment", "kind": key.upper(),
                                 "char_count": chars, "created_at": "2026-12-01T09:00:00+00:00"}
            w.tickets[upload] = {"owner": OWNER, "state": "Used", "source_id": source}
            w.orders[order] = OWNER
            w.order_sources.append((order, source))
            self.write_session(label, self.session_lines(label, upload, order, file))
        w.runs[i["run_s1"]] = {"order": i["o_s1"], "status": "Completed"}
        w.usages.append({"run": i["run_s1"], "source": i["src_s1"], "target": "writer", "mode": "FullText", "chars": 38000})
        w.runs[i["run_s2"]] = {"order": i["o_s2"], "status": "Aborted"}
        self.plan["t_a2"] = {"sessions": [
            {"label": "S1", "kind": "pdf", "file": str(self.file("s1")), "transcript": "s1-stream.jsonl"},
            {"label": "S2", "kind": "docx", "file": str(self.file("s2")), "transcript": "s2-stream.jsonl"}]}

    def _t_a3(self) -> None:
        i, w = self.ids, self.world
        file = self.file("page")
        w.sources[i["src_t3"]] = {"owner": OWNER, "sha": sha(file), "origin": "WebUpload", "kind": "Pdf", "char_count": 9000,
                                  "created_at": "2026-12-01T09:30:00+00:00"}
        w.tickets[i["u_t3"]] = {"owner": OWNER, "state": "Used", "source_id": i["src_t3"]}
        w.orders[i["o_t3"]] = OWNER
        w.order_sources.append((i["o_t3"], i["src_t3"]))
        w.orders[i["o_foreign"]] = OTHER
        (self.root / "t-a3-done.html").write_text(
            f'<main><h1>{uc.PAGE_HEADING}</h1><ul><li class="source-card" data-testid="source-card" data-status="ready">'
            f'<span data-testid="source-card-name">{file.name}</span><div class="source-card-meta" '
            f'data-testid="source-card-summary">PDF, 6 pages, 1,400 words</div></li></ul><p>{uc.PAGE_DONE}</p></main>',
            encoding="utf-8")
        (self.root / "t-a3-foreign.html").write_text(
            f"<!-- url: {ISSUER}/u/{i['u_t3']} -->\n"
            "<main><h3>Not Found</h3><p>Sorry, the content you are looking for does not exist.</p></main>", encoding="utf-8")
        (self.root / "t-a3-foreign-submit.txt").write_text(uc.NOT_FOUND_FORMAT.format(i["u_t3"]) + "\n", encoding="utf-8")
        self.plan["t_a3"] = {"upload_id": i["u_t3"], "file": str(file), "page_done": "t-a3-done.html",
                             "foreign_page": "t-a3-foreign.html", "foreign_submit": "t-a3-foreign-submit.txt"}

    def refusal(self, at: str, path: str, code: str, ext: str | None, kind: str | None = None, declared: str | None = None,
                size: int | None = 1000, owner: str = OWNER) -> dict:
        row = {"Id": str(uuid.uuid4()), "CreatedAt": at, "OwnerUsername": owner, "Path": path, "Code": code, "Kind": kind,
               "Extension": ext, "DeclaredType": declared, "SizeBytes": size}
        self.world.refusals.append(row)
        return row

    @staticmethod
    def answer(body: object, status: int) -> str:
        text = body if isinstance(body, str) else json.dumps(body)
        return f"{text}\n{status}\n"

    def _t_a4(self) -> None:
        i, w = self.ids, self.world
        scan = self.file("scan")
        for key in ("u_scan_token", "u_scan_page"):
            w.tickets[i[key]] = {"owner": OWNER, "state": "Open", "source_id": None, "attempts": 1, "last_refusal_code": "pdf_no_text"}
        (self.root / "t-a4-token.txt").write_text(
            self.answer({"usable": False, "reason": uc.NO_TEXT_REASON, "uploadId": i["u_scan_token"]}, 422), encoding="utf-8")
        (self.root / "t-a4-page.txt").write_text(
            self.answer({"fileName": scan.name, "usable": False, "reason": uc.NO_TEXT_REASON, "uploadId": i["u_scan_page"]}, 422),
            encoding="utf-8")
        size = scan.stat().st_size
        self.refusal("2026-12-01T10:02:00+00:00", "mcp_ticket", "pdf_no_text", "pdf", "Pdf", None, size)
        self.refusal("2026-12-01T10:05:00+00:00", "upload_page", "pdf_no_text", "pdf", "Pdf", "application/pdf", size)
        self.plan["t_a4"] = {"file": str(scan), "token_upload_id": i["u_scan_token"], "page_upload_id": i["u_scan_page"],
                             "token_answer": "t-a4-token.txt", "page_answer": "t-a4-page.txt"}

    def _t_a5(self) -> None:
        i, w = self.ids, self.world
        png, placeholder, size_file = self.file("png"), self.file("placeholder"), self.file("size")
        declared = self.manifest["files"]["size"]["declared_bytes"]
        actual = size_file.stat().st_size
        w.tickets[i["u_size"]] = {"owner": OWNER, "state": "Open", "source_id": None, "attempts": 1, "last_refusal_code": "size_mismatch"}
        (self.root / "t-a5-png.txt").write_text(self.answer({"usable": False, "reason": uc.UNSUPPORTED_REASON}, 422), encoding="utf-8")
        (self.root / "t-a5-placeholder.txt").write_text(
            f"The attachment '{placeholder.name}' {uc.PLACEHOLDER_PROBLEM}. Attach the file with its original content.\n",
            encoding="utf-8")
        (self.root / "t-a5-size.txt").write_text(
            self.answer({"usable": False, "reason": uc.SIZE_MISMATCH_FORMAT.format(actual, declared), "uploadId": i["u_size"]}, 422),
            encoding="utf-8")
        self.refusal("2026-12-01T10:21:00+00:00", "web", "unsupported_type", "png", None, "image/png", png.stat().st_size)
        self.refusal("2026-12-01T10:23:00+00:00", "mcp_inline", "mcp_placeholder", "txt", None, "text/plain", 58)
        self.refusal("2026-12-01T10:25:00+00:00", "mcp_ticket", "size_mismatch", "md", None, None, actual)
        self.plan["t_a5"] = {"png_file": str(png), "png_answer": "t-a5-png.txt", "placeholder_file": str(placeholder),
                             "placeholder_answer": "t-a5-placeholder.txt", "size_file": str(size_file),
                             "size_upload_id": i["u_size"], "size_declared": declared, "size_answer": "t-a5-size.txt"}
