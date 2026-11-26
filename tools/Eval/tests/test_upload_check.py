import base64
import copy
import json
import re
import shutil
import struct
import subprocess
import zipfile
import zlib
from pathlib import Path

import pytest

from geef_eval import upload_check as uc
from upload_world import OTHER, OWNER, TOKEN, FakeDatabase, Scenario, sha, stream, tool_result, tool_use, INIT, RESULT

REPO = Path(__file__).resolve().parents[3]


@pytest.fixture
def s(tmp_path):
    return Scenario(tmp_path / "run")


def verdicts(report):
    return {name: gate["verdict"] for name, gate in report["gates"].items()}


def part(report, gate, key):
    return report["gates"][gate]["parts"][key]["state"]


def test_dry_run_with_correct_behaviour_passes_every_gate(s):
    report = s.evaluate()
    assert verdicts(report) == {g: "passed" for g in uc.GATES}
    assert report["verdict"] == "passed"
    assert uc.FOLLOW_UP["passed"] in report["follow_up"]
    assert report["gates"]["T-A2"]["passed_sessions"] == 2
    assert all(p["state"] == uc.YES for g in report["gates"].values() for p in g["parts"].values())


def test_one_gate_alone_never_brings_the_switch_on_commit(s):
    report = s.evaluate(["T-A4"])
    assert report["verdict"] == "passed"
    assert uc.FOLLOW_UP["passed"] not in report["follow_up"]


def _rewrite_session(s, label, transform):
    path = s.root / f"{label.lower()}-stream.jsonl"
    lines = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]
    s.write_session(label, transform(lines))


def _drop_tool(name):
    def transform(lines):
        drop = {b["id"] for e in lines for b in (e.get("message") or {}).get("content", [])
                if b.get("type") == "tool_use" and (b["name"] == name or b["name"].endswith("__" + name))}
        return [e for e in lines if not any(b.get("id") in drop or b.get("tool_use_id") in drop
                                            for b in (e.get("message") or {}).get("content", []))]
    return transform


def _mark_error(name):
    def transform(lines):
        ids = {b["id"] for e in lines for b in (e.get("message") or {}).get("content", [])
               if b.get("type") == "tool_use" and (b["name"] == name or b["name"].endswith("__" + name))}
        for e in lines:
            for b in (e.get("message") or {}).get("content", []):
                if b.get("tool_use_id") in ids:
                    b["is_error"] = True
                    b["content"] = [{"type": "text", "text": "Error: something went wrong"}]
        return lines
    return transform


def _edit_args(name, edit):
    def transform(lines):
        for e in lines:
            for b in (e.get("message") or {}).get("content", []):
                if b.get("type") == "tool_use" and (b["name"] == name or b["name"].endswith("__" + name)):
                    edit(b["input"])
        return lines
    return transform


def _edit_result(name, edit):
    def transform(lines):
        ids = {b["id"] for e in lines for b in (e.get("message") or {}).get("content", [])
               if b.get("type") == "tool_use" and (b["name"] == name or b["name"].endswith("__" + name))}
        for e in lines:
            for b in (e.get("message") or {}).get("content", []):
                if b.get("tool_use_id") in ids:
                    value = json.loads(b["content"][0]["text"])
                    edit(value)
                    b["content"][0]["text"] = json.dumps(value)
        return lines
    return transform


def _insert_before_result(*entries):
    def transform(lines):
        return lines[:-1] + list(entries) + lines[-1:]
    return transform


def _attachments(length):
    return json.dumps([{"filename": "a.pdf", "contentType": "application/pdf", "contentBase64": "A" * length}])


def _typed_instead_of_uploaded(s):
    file = s.file("s1")
    s.write_session("S1", [INIT, tool_use("x1", "mcp__atelier__submit_order",
                                          {"request": "Summarise.", "attachments": _attachments(5000)}),
                           tool_result("x1", json.dumps({"orderId": s.ids["o_s1"]})), RESULT])
    assert file.is_file()


def _other_upload_id_in_curl(lines):
    for e in lines:
        for b in (e.get("message") or {}).get("content", []):
            if b.get("type") == "tool_use" and b["name"] == "Bash":
                b["input"]["command"] = b["input"]["command"].replace("/api/uploads/", "/api/uploads/00000000-0000-4000-8000-")[:-12]
    return lines


T_A2_FAILURES = {
    "typed Base64 instead of a ticket": (_typed_instead_of_uploaded, "S1.a_prepare"),
    "prepare_attachment failed": (lambda s: _rewrite_session(s, "S1", _mark_error("prepare_attachment")), "S1.a_prepare"),
    "only the link, no curl": (lambda s: _rewrite_session(s, "S1", _drop_tool("Bash")), "S1.b_curl_201"),
    "curl refused (422)": (lambda s: _rewrite_session(s, "S1", _mark_error("Bash")), "S1.b_curl_201"),
    "curl answer not usable": (lambda s: _rewrite_session(s, "S1", _edit_result("Bash", lambda v: v["receipt"].update(usable=False))),
                               "S1.b_curl_201"),
    "curl to another upload": (lambda s: _rewrite_session(s, "S1", _other_upload_id_in_curl), "S1.b_curl_201"),
    "submit_order without uploadIds": (lambda s: _rewrite_session(s, "S1", _edit_args("submit_order", lambda a: a.pop("uploadIds"))),
                                       "S1.c_submit_upload_ids"),
    "submit_order with another id": (lambda s: _rewrite_session(s, "S1", _edit_args(
        "submit_order", lambda a: a.update(uploadIds=["00000000-0000-4000-8000-000000000001"]))), "S1.c_submit_upload_ids"),
    "submit_order failed": (lambda s: _rewrite_session(s, "S1", _mark_error("submit_order")), "S1.c_submit_upload_ids"),
    "stored bytes differ": (lambda s: s.world.sources[s.ids["src_s1"]].update(sha="0" * 64), "S1.d_bytes_equal"),
    "ticket not used": (lambda s: s.world.tickets[s.ids["u_s1"]].update(state="Open"), "S1.d_bytes_equal"),
    "ticket missing in the database": (lambda s: s.world.tickets.pop(s.ids["u_s1"]), "S1.d_bytes_equal"),
    "source of another person": (lambda s: s.world.sources[s.ids["src_s1"]].update(owner=OTHER), "S1.d_bytes_equal"),
    "sha256 of the curl answer differs": (lambda s: _rewrite_session(s, "S1", _edit_result("Bash", lambda v: v.update(sha256="1" * 64))),
                                          "S1.d_bytes_equal"),
    "source came over the page": (lambda s: s.world.sources[s.ids["src_s1"]].update(origin="WebUpload"), "S1.d_bytes_equal"),
    "Base64 over 1 KB in another call": (lambda s: _rewrite_session(s, "S1", _insert_before_result(
        tool_use("y1", "mcp__atelier__submit_order", {"request": "x", "attachments": _attachments(1025)}),
        tool_result("y1", "attachment rejected", is_error=True))), "S1.e_base64_small"),
    "Base64 over 1 KB in a shell command": (lambda s: _rewrite_session(s, "S1", _insert_before_result(
        tool_use("y2", "Bash", {"command": "curl -d '{\"contentBase64\": \"" + "B" * 2000 + "\"}' https://atelier.example/mcp"}),
        tool_result("y2", "ok"))), "S1.e_base64_small"),
    "order not linked to the source": (lambda s: s.world.order_sources.remove((s.ids["o_s1"], s.ids["src_s1"])), "S1.f_linked"),
    "writer got a map": (lambda s: s.world.usages[0].update(mode="FullTextWithMap"), "S1.g_writer_full_text"),
    "writer got fewer characters": (lambda s: s.world.usages[0].update(chars=30000), "S1.g_writer_full_text"),
    "no writer row after a finished run": (lambda s: s.world.usages.clear(), "S1.g_writer_full_text"),
    "only S2 broken": (lambda s: s.world.sources[s.ids["src_s2"]].update(sha="2" * 64), "S2.d_bytes_equal"),
}


@pytest.mark.parametrize("name", list(T_A2_FAILURES))
def test_t_a2_fails_on_each_single_misbehaviour(s, name):
    mutate, key = T_A2_FAILURES[name]
    mutate(s)
    report = s.evaluate()
    assert part(report, "T-A2", key) == uc.NO
    assert report["gates"]["T-A2"]["verdict"] == "failed"
    assert report["verdict"] == "failed"
    assert uc.FOLLOW_UP["passed"] not in report["follow_up"]
    assert {g: v for g, v in verdicts(report).items() if g != "T-A2"} == {g: "passed" for g in uc.GATES if g != "T-A2"}


@pytest.mark.parametrize("name,halt", [("stored bytes differ", True), ("writer got a map", True), ("only the link, no curl", False),
                                       ("Base64 over 1 KB in another call", False), ("order not linked to the source", True)])
def test_t_a2_follow_up_separates_model_behaviour_from_halt(s, name, halt):
    T_A2_FAILURES[name][0](s)
    gate = s.evaluate(["T-A2"])["gates"]["T-A2"]
    assert gate["halt"] is halt
    assert (uc.FOLLOW_UP["sentences"] in gate["follow_up"]) is (not halt)


def test_t_a2_base64_limit_is_inclusive(s):
    _rewrite_session(s, "S1", _insert_before_result(
        tool_use("y1", "mcp__atelier__submit_order", {"request": "x", "attachments": _attachments(1024)}),
        tool_result("y1", "attachment rejected", is_error=True)))
    report = s.evaluate(["T-A2"])
    assert part(report, "T-A2", "S1.e_base64_small") == uc.YES
    assert report["gates"]["T-A2"]["sessions"][0]["counts"]["largest_content_base64"] == 1024


def _small_pdf_as_s1(s):
    s.file("s1").write_bytes(uc.text_pdf(__import__("random").Random(1), "x", 24, None))


def _few_pages_as_s1(s):
    s.file("s1").write_bytes(uc.text_pdf(__import__("random").Random(1), "x", 10, (300, 300)))


T_A2_MISSING = {
    "transcript missing": (lambda s: (s.root / "s1-stream.jsonl").unlink(), "S1.transcript"),
    "transcript without result line": (lambda s: _rewrite_session(s, "S1", lambda lines: lines[:-1]), "S1.transcript"),
    "local file missing": (lambda s: s.file("s2").unlink(), "S2.file"),
    "S1 smaller than 200 KB": (_small_pdf_as_s1, "S1.file"),
    "S1 fewer than 20 pages": (_few_pages_as_s1, "S1.file"),
    "S2 not a DOCX": (lambda s: s.file("s2").write_bytes(b"plain text"), "S2.file"),
    "writer check before any finished run": (lambda s: (s.world.usages.clear(), s.world.runs[s.ids["run_s1"]].update(status="Running")),
                                             "S1.g_writer_full_text"),
}


@pytest.mark.parametrize("name", list(T_A2_MISSING))
def test_t_a2_without_data_is_not_evaluable(s, name):
    mutate, key = T_A2_MISSING[name]
    mutate(s)
    report = s.evaluate(["T-A2"])
    assert part(report, "T-A2", key) == uc.MISSING
    assert report["gates"]["T-A2"]["verdict"] == "not_evaluable"


@pytest.mark.parametrize("sessions", [
    lambda p: p[:1],
    lambda p: [p[0], dict(p[0], label="S2")],
    lambda p: [p[0], p[1], dict(p[1], label="S3")],
    lambda p: [p[0], dict(p[1], label="S1")],
])
def test_t_a2_needs_exactly_one_pdf_and_one_docx_session(s, sessions):
    s.plan["t_a2"]["sessions"] = sessions(s.plan["t_a2"]["sessions"])
    assert s.evaluate(["T-A2"])["gates"]["T-A2"]["verdict"] != "passed"


def test_t_a2_same_ticket_in_both_sessions_is_not_evaluable(s):
    s.write_session("S2", s.session_lines("S2", s.ids["u_s1"], s.ids["o_s1"], s.file("s1")))
    s.plan["t_a2"]["sessions"][1]["file"] = str(s.file("s1"))
    s.plan["t_a2"]["sessions"][1]["kind"] = "docx"
    gate = s.evaluate(["T-A2"])["gates"]["T-A2"]
    assert gate["verdict"] != "passed"


T_A3_FAILURES = {
    "page without Done": (lambda s: (s.root / "t-a3-done.html").write_text(
        (s.root / "t-a3-done.html").read_text().replace(uc.PAGE_DONE, "Something went wrong."), encoding="utf-8"), "a_page_done"),
    "page without receipt": (lambda s: (s.root / "t-a3-done.html").write_text(f"<p>{uc.PAGE_DONE}</p>", encoding="utf-8"),
                             "a_page_done"),
    "stored bytes differ": (lambda s: s.world.sources[s.ids["src_t3"]].update(sha="3" * 64), "b_bytes_equal"),
    "file came over curl": (lambda s: s.world.sources[s.ids["src_t3"]].update(origin="McpAttachment"), "b_bytes_equal"),
    "ticket still open": (lambda s: s.world.tickets[s.ids["u_t3"]].update(state="Open", source_id=None), "b_bytes_equal"),
    "source of another person": (lambda s: s.world.sources[s.ids["src_t3"]].update(owner=OTHER), "b_bytes_equal"),
    "second account sees the page": (lambda s: (s.root / "t-a3-foreign.html").write_text(
        f"<h1>{uc.PAGE_HEADING}</h1><p>{uc.PAGE_INTRO_START} {s.file('page').name}. Drop it here.</p>", encoding="utf-8"),
        "c_second_account"),
    "second account sees the file name": (lambda s: (s.root / "t-a3-foreign.html").write_text(
        f"<h3>Not Found</h3><p>{s.file('page').name}</p>", encoding="utf-8"), "c_second_account"),
    "second account sees a used page": (lambda s: (s.root / "t-a3-foreign.html").write_text(
        f"<h3>Not Found</h3><p>{uc.PAGE_ALREADY_USED}</p>", encoding="utf-8"), "c_second_account"),
    "second account page is empty": (lambda s: (s.root / "t-a3-foreign.html").write_text("<main></main>", encoding="utf-8"),
                                     "c_second_account"),
    "second account can order with the id": (lambda s: (s.root / "t-a3-foreign-submit.txt").write_text(
        json.dumps({"orderId": s.ids["o_foreign"], "state": "clarifying"}), encoding="utf-8"), "c_second_account"),
    "order of another person links the file": (lambda s: s.world.order_sources.append((s.ids["o_foreign"], s.ids["src_t3"])),
                                               "c_second_account"),
    "second account captured the 404 of another address": (lambda s: (s.root / "t-a3-foreign.html").write_text(
        f"<!-- url: https://atelier.example/u/{s.ids['u_s1']} -->\n<main><h3>Not Found</h3></main>", encoding="utf-8"),
        "c_second_account"),
    "second account captured a wrong path": (lambda s: (s.root / "t-a3-foreign.html").write_text(
        f"<!-- url: https://atelier.example/x/{s.ids['u_t3']} -->\n<main><h3>Not Found</h3></main>", encoding="utf-8"),
        "c_second_account"),
}


@pytest.mark.parametrize("name", list(T_A3_FAILURES))
def test_t_a3_fails_on_each_single_misbehaviour(s, name):
    mutate, key = T_A3_FAILURES[name]
    mutate(s)
    report = s.evaluate()
    assert part(report, "T-A3", key) == uc.NO
    assert report["gates"]["T-A3"]["verdict"] == "failed"
    assert report["gates"]["T-A3"]["halt"] is True
    assert {g: v for g, v in verdicts(report).items() if g != "T-A3"} == {g: "passed" for g in uc.GATES if g != "T-A3"}


T_A3_MISSING = {
    "page after upload not captured": (lambda s: (s.root / "t-a3-done.html").unlink(), "a_page_done"),
    "page of the second account not captured": (lambda s: (s.root / "t-a3-foreign.html").unlink(), "c_second_account"),
    "page of the second account without its address": (lambda s: (s.root / "t-a3-foreign.html").write_text(
        "<main><h3>Not Found</h3><p>Sorry, the content you are looking for does not exist.</p></main>", encoding="utf-8"),
        "c_second_account"),
    "answer of the second account not captured": (lambda s: (s.root / "t-a3-foreign-submit.txt").unlink(), "c_second_account"),
    "upload id still the template": (lambda s: s.plan["t_a3"].update(upload_id="<uploadId>"), "b_bytes_equal"),
    "local file missing": (lambda s: s.file("page").unlink(), "b_bytes_equal"),
}


@pytest.mark.parametrize("name", list(T_A3_MISSING))
def test_t_a3_without_data_is_not_evaluable(s, name):
    mutate, key = T_A3_MISSING[name]
    mutate(s)
    report = s.evaluate(["T-A3"])
    assert part(report, "T-A3", key) == uc.MISSING
    assert report["gates"]["T-A3"]["verdict"] == "not_evaluable"


def _answer(s, name, body, status):
    (s.root / name).write_text(s.answer(body, status), encoding="utf-8")


def _refusals_at(s, prefix):
    return [r for r in s.world.refusals if r["CreatedAt"].startswith(prefix)]


T_A4_FAILURES = {
    "token way stored the scan": (lambda s: _answer(s, "t-a4-token.txt", {"usable": True}, 201), "a_token_422"),
    "token way other sentence": (lambda s: _answer(s, "t-a4-token.txt", {"reason": "This file could not be read."}, 422), "a_token_422"),
    "page way status 400": (lambda s: _answer(s, "t-a4-page.txt", {"reason": uc.NO_TEXT_REASON}, 400), "a_page_422"),
    "page way without sentence": (lambda s: _answer(s, "t-a4-page.txt", {"reason": None}, 422), "a_page_422"),
    "token ticket got a source": (lambda s: s.world.tickets[s.ids["u_scan_token"]].update(state="Used", source_id=s.ids["src_s1"]),
                                  "a_token_ticket"),
    "page ticket used": (lambda s: s.world.tickets[s.ids["u_scan_page"]].update(state="Used"), "a_page_ticket"),
    "page ticket unknown": (lambda s: s.world.tickets.pop(s.ids["u_scan_page"]), "a_page_ticket"),
    "token ticket untouched": (lambda s: s.world.tickets[s.ids["u_scan_token"]].update(attempts=0, last_refusal_code=None),
                               "a_token_ticket"),
    "page ticket refused for something else": (lambda s: s.world.tickets[s.ids["u_scan_page"]].update(last_refusal_code="user_busy"),
                                               "a_page_ticket"),
    "token answer of another ticket": (lambda s: _answer(s, "t-a4-token.txt", {"reason": uc.NO_TEXT_REASON, "uploadId": s.ids["u_size"]}, 422),
                                       "a_token_422"),
    "page answer without upload id": (lambda s: _answer(s, "t-a4-page.txt", {"reason": uc.NO_TEXT_REASON}, 422), "a_page_422"),
    "a new source in the window": (lambda s: s.world.sources.__setitem__("x", {"owner": OWNER, "sha": "4" * 64, "origin": "McpAttachment",
                                                                             "kind": "Pdf", "char_count": 1,
                                                                             "created_at": "2026-12-01T10:03:00+00:00"}),
                                   "b_no_new_sources"),
    "page refusal not counted": (lambda s: s.world.refusals.remove(_refusals_at(s, "2026-12-01T10:05")[0]), "c_rows_exact"),
    "token refusal counted twice": (lambda s: s.refusal("2026-12-01T10:02:30+00:00", "mcp_ticket", "pdf_no_text", "pdf", "Pdf"),
                                    "c_rows_exact"),
    "a third refusal": (lambda s: s.refusal("2026-12-01T10:06:00+00:00", "web", "unsupported_type", "png"), "c_rows_exact"),
    "a refusal of another person": (lambda s: s.refusal("2026-12-01T10:07:00+00:00", "web", "too_large", "pdf", owner=OTHER),
                                    "c_rows_exact"),
    "wrong code": (lambda s: _refusals_at(s, "2026-12-01T10:02")[0].update(Code="unreadable"), "c_rows_exact"),
    "wrong path": (lambda s: _refusals_at(s, "2026-12-01T10:05")[0].update(Path="web"), "c_rows_exact"),
    "wrong extension": (lambda s: _refusals_at(s, "2026-12-01T10:05")[0].update(Extension="bin"), "c_rows_exact"),
    "file name in a column": (lambda s: _refusals_at(s, "2026-12-01T10:05")[0].update(DeclaredType="application/" + s.file("scan").name),
                              "c_rows_clean"),
    "extra column with the name": (lambda s: (s.world.refusal_columns.append("FileName"),
                                              [r.update(FileName=s.file("scan").name) for r in s.world.refusals]), "c_rows_clean"),
    "extra column without values": (lambda s: s.world.refusal_columns.append("Preview"), "c_rows_clean"),
    "file name in the owner column": (lambda s: _refusals_at(s, "2026-12-01T10:02")[0].update(OwnerUsername=s.file("scan").stem),
                                      "c_rows_clean"),
    "code outside the vocabulary": (lambda s: (_refusals_at(s, "2026-12-01T10:02")[0].update(Code="scan pdf"),), "c_rows_clean"),
}


@pytest.mark.parametrize("name", list(T_A4_FAILURES))
def test_t_a4_fails_on_each_single_misbehaviour(s, name):
    mutate, key = T_A4_FAILURES[name]
    mutate(s)
    report = s.evaluate(["T-A3", "T-A4", "T-A5"])
    assert part(report, "T-A4", key) == uc.NO
    assert report["gates"]["T-A4"]["verdict"] == "failed"
    assert report["gates"]["T-A4"]["halt"] is True
    table_wide = name.startswith("extra column")
    assert report["gates"]["T-A5"]["verdict"] == ("failed" if table_wide else "passed")


T_A4_MISSING = {
    "no marks": (lambda s: s.marks.pop("T-A4"), "c_rows_exact"),
    "end before start": (lambda s: s.marks["T-A4"].update(end="2026-12-01T09:00:00+00:00"), "c_rows_exact"),
    "mark without time zone": (lambda s: s.marks["T-A4"].update(start="2026-12-01T10:00:00"), "c_rows_exact"),
    "windows overlap": (lambda s: s.marks["T-A5"].update(start="2026-12-01T10:05:00+00:00"), "c_rows_exact"),
    "token answer not captured": (lambda s: (s.root / "t-a4-token.txt").unlink(), "a_token_422"),
    "page answer without status line": (lambda s: (s.root / "t-a4-page.txt").write_text(uc.NO_TEXT_REASON, encoding="utf-8"),
                                        "a_page_422"),
    "page ticket id missing": (lambda s: s.plan["t_a4"].pop("page_upload_id"), "a_page_ticket"),
    "scan file missing": (lambda s: s.file("scan").unlink(), "file"),
}


@pytest.mark.parametrize("name", list(T_A4_MISSING))
def test_t_a4_without_data_is_not_evaluable(s, name):
    mutate, key = T_A4_MISSING[name]
    mutate(s)
    report = s.evaluate(["T-A4"])
    assert part(report, "T-A4", key) == uc.MISSING
    assert report["gates"]["T-A4"]["verdict"] == "not_evaluable"


def _size_numbers(s):
    return s.file("size").stat().st_size, s.manifest["files"]["size"]["declared_bytes"]


T_A5_FAILURES = {
    "PNG accepted": (lambda s: _answer(s, "t-a5-png.txt", {"usable": True}, 201), "a_png_web"),
    "PNG other sentence": (lambda s: _answer(s, "t-a5-png.txt", {"reason": "Something went wrong."}, 422), "a_png_web"),
    "placeholder accepted": (lambda s: (s.root / "t-a5-placeholder.txt").write_text(json.dumps({"orderId": s.ids["o_foreign"]}),
                                                                                    encoding="utf-8"), "a_placeholder_mcp"),
    "size accepted": (lambda s: _answer(s, "t-a5-size.txt", {"usable": True}, 201), "a_size_token"),
    "size with wrong numbers": (lambda s: _answer(s, "t-a5-size.txt", {"reason": uc.SIZE_MISMATCH_FORMAT.format(1, 2)}, 422),
                                "a_size_token"),
    "size ticket got a source": (lambda s: s.world.tickets[s.ids["u_size"]].update(state="Used", source_id=s.ids["src_s2"]),
                                 "a_size_ticket"),
    "size ticket untouched": (lambda s: s.world.tickets[s.ids["u_size"]].update(attempts=0, last_refusal_code=None), "a_size_ticket"),
    "size answer of another ticket": (lambda s: _answer(s, "t-a5-size.txt", {"reason": uc.SIZE_MISMATCH_FORMAT.format(*_size_numbers(s)),
                                                                              "uploadId": s.ids["u_scan_page"]}, 422), "a_size_token"),
    "web refusal not counted": (lambda s: s.world.refusals.remove(_refusals_at(s, "2026-12-01T10:21")[0]), "c_rows_exact"),
    "placeholder counted as ticket": (lambda s: _refusals_at(s, "2026-12-01T10:23")[0].update(Path="mcp_ticket"), "c_rows_exact"),
    "placeholder with another code": (lambda s: _refusals_at(s, "2026-12-01T10:23")[0].update(Code="mcp_empty"), "c_rows_exact"),
    "size refusal counted twice": (lambda s: s.refusal("2026-12-01T10:26:00+00:00", "mcp_ticket", "size_mismatch", "md"),
                                   "c_rows_exact"),
    "PNG without extension": (lambda s: _refusals_at(s, "2026-12-01T10:21")[0].update(Extension=None), "c_rows_exact"),
    "a new source in the window": (lambda s: s.world.sources.__setitem__("y", {"owner": OWNER, "sha": "5" * 64, "origin": "WebUpload",
                                                                             "kind": "Text", "char_count": 1,
                                                                             "created_at": "2026-12-01T10:22:00+00:00"}),
                                   "b_no_new_sources"),
    "file name in a column": (lambda s: _refusals_at(s, "2026-12-01T10:23")[0].update(DeclaredType="text/" + s.file("placeholder").name),
                              "c_rows_clean"),
    "kind outside the vocabulary": (lambda s: _refusals_at(s, "2026-12-01T10:21")[0].update(Kind="Image"), "c_rows_clean"),
    "negative size": (lambda s: _refusals_at(s, "2026-12-01T10:25")[0].update(SizeBytes=-1), "c_rows_clean"),
}


@pytest.mark.parametrize("name", list(T_A5_FAILURES))
def test_t_a5_fails_on_each_single_misbehaviour(s, name):
    mutate, key = T_A5_FAILURES[name]
    mutate(s)
    report = s.evaluate()
    assert part(report, "T-A5", key) == uc.NO
    assert report["gates"]["T-A5"]["verdict"] == "failed"
    assert report["gates"]["T-A5"]["halt"] is True
    assert report["gates"]["T-A4"]["verdict"] == "passed"


def test_t_a5_accepts_the_sentence_for_a_larger_file(s):
    actual, _ = _size_numbers(s)
    s.plan["t_a5"]["size_declared"] = actual - 10
    _answer(s, "t-a5-size.txt", {"reason": uc.LARGER_THAN_ANNOUNCED_FORMAT.format(actual - 10), "uploadId": s.ids["u_size"]}, 422)
    assert s.evaluate(["T-A5"])["gates"]["T-A5"]["verdict"] == "passed"


T_A5_MISSING = {
    "PNG answer not captured": (lambda s: (s.root / "t-a5-png.txt").unlink(), "a_png_web"),
    "placeholder answer not captured": (lambda s: (s.root / "t-a5-placeholder.txt").unlink(), "a_placeholder_mcp"),
    "size answer not captured": (lambda s: (s.root / "t-a5-size.txt").unlink(), "a_size_token"),
    "announced size equals the real size": (lambda s: s.plan["t_a5"].update(size_declared=s.file("size").stat().st_size), "a_size_token"),
    "announced size missing": (lambda s: s.plan["t_a5"].pop("size_declared"), "a_size_token"),
    "no marks": (lambda s: s.marks.pop("T-A5"), "c_rows_exact"),
    "PNG file missing": (lambda s: s.file("png").unlink(), "png_file"),
}


@pytest.mark.parametrize("name", list(T_A5_MISSING))
def test_t_a5_without_data_is_not_evaluable(s, name):
    mutate, key = T_A5_MISSING[name]
    mutate(s)
    report = s.evaluate(["T-A5"])
    assert part(report, "T-A5", key) == uc.MISSING
    assert report["gates"]["T-A5"]["verdict"] == "not_evaluable"


def test_no_gate_passes_without_data(tmp_path):
    db = FakeDatabase(__import__("upload_world").World())
    assert verdicts(uc.evaluate({}, tmp_path, db, {})) == {g: "not_evaluable" for g in uc.GATES}
    template = tmp_path / "files"
    uc.generate(template, seed=3)
    plan = json.loads((template / "plan-vorlage.json").read_text())
    report = uc.evaluate(plan, template, db, {})
    assert "passed" not in verdicts(report).values()
    assert report["verdict"] != "passed"


def test_no_gate_passes_with_files_but_an_empty_database(s):
    s.world.__init__()
    report = s.evaluate()
    assert "passed" not in verdicts(report).values()


def test_a_ticket_in_two_gates_is_not_evaluable(s):
    s.plan["t_a5"]["size_upload_id"] = s.ids["u_scan_token"]
    report = s.evaluate(["T-A4", "T-A5"])
    assert report["gates"]["T-A4"]["verdict"] == "not_evaluable"


def test_output_holds_no_names_ids_hashes_or_contents(s, tmp_path):
    out = tmp_path / "out"
    out.mkdir()
    (out / "marks.json").write_text(json.dumps(s.marks), encoding="utf-8")
    lines = []
    T_A2_FAILURES["stored bytes differ"][0](s)
    T_A4_FAILURES["file name in a column"][0](s)
    code = uc.main(["evaluate", "--plan", str(s.write_plan()), "--out-dir", str(out)], database=s.db, echo=lines.append)
    assert code == 1
    written = (out / "upload-gates.json").read_text(encoding="utf-8") + "\n".join(lines)
    forbidden = [TOKEN, OWNER, OTHER, *s.ids.values(), *(s.ids[k].upper() for k in s.ids)]
    for key, entry in s.manifest["files"].items():
        path = s.file(key)
        forbidden += [entry["file"], path.stem, sha(path), *entry.get("markers", [])]
    forbidden.append(s.manifest["nonce"])
    for item in forbidden:
        assert item not in written


def test_cli_exit_codes(s, tmp_path):
    out = tmp_path / "out"
    out.mkdir()
    (out / "marks.json").write_text(json.dumps(s.marks), encoding="utf-8")
    plan = s.write_plan()
    run = lambda *extra, db=s.db: uc.main(["evaluate", "--plan", str(plan), "--out-dir", str(out), *extra], database=db, echo=lambda _: None)
    assert run() == 0
    assert run("--gate", "T-A4") == 0
    (s.root / "t-a4-page.txt").unlink()
    assert run("--gate", "T-A4") == 4
    T_A5_FAILURES["PNG accepted"][0](s)
    assert run("--gate", "T-A5") == 1

    def broken():
        raise uc.DatabaseError("weg")
    assert run(db=broken) == 3
    plan.write_text("[1, 2]", encoding="utf-8")
    assert run() == 2
    plan.write_text("{not json", encoding="utf-8")
    assert run() == 2


def test_cli_refuses_directories_inside_git(tmp_path):
    inside = Path(__file__).resolve().parent / "generated-should-not-exist"
    lines = []
    assert uc.main(["generate", "--out-dir", str(inside)], echo=lines.append) == 2
    assert not inside.exists()
    assert uc.main(["evaluate", "--plan", str(tmp_path / "p.json"), "--out-dir", str(inside)], database=lambda: None,
                   echo=lines.append) == 2
    assert uc.main(["mark", "--gate", "T-A4", "--at", "start", "--out-dir", str(inside)], database=lambda: None,
                   echo=lines.append) == 2
    assert not inside.exists()


def test_mark_stores_the_database_time(tmp_path):
    out = tmp_path / "out"
    db = FakeDatabase(__import__("upload_world").World(), now="2026-12-01T10:00:00.123+00:00")
    assert uc.main(["mark", "--gate", "T-A4", "--at", "start", "--out-dir", str(out)], database=lambda: db, echo=lambda _: None) == 0
    db.clock = "2026-12-01T10:09:00+00:00"
    assert uc.main(["mark", "--gate", "T-A4", "--at", "end", "--out-dir", str(out)], database=lambda: db, echo=lambda _: None) == 0
    marks = json.loads((out / "marks.json").read_text())
    assert marks == {"T-A4": {"start": "2026-12-01T10:00:00.123+00:00", "end": "2026-12-01T10:09:00+00:00"}}
    assert uc._window_bounds(marks, "T-A4") is not None


def test_compare_prints_yes_or_no_only(s):
    lines = []
    file = s.file("s1")
    assert uc.main(["compare", "--file", str(file), "--upload-id", s.ids["u_s1"]], database=s.db, echo=lines.append) == 0
    assert lines[-1].endswith("SHA-256 gleich: ja")
    s.world.sources[s.ids["src_s1"]]["sha"] = "6" * 64
    assert uc.main(["compare", "--file", str(file), "--upload-id", s.ids["u_s1"]], database=s.db, echo=lines.append) == 1
    assert lines[-1].endswith("SHA-256 gleich: nein")
    assert uc.main(["compare", "--file", str(file), "--upload-id", "x"], database=s.db, echo=lines.append) == 2
    assert all(file.name not in line and s.ids["u_s1"] not in line for line in lines)


def test_largest_base64_reads_nested_json_and_shell_commands(tmp_path):
    attachments = json.dumps([{"filename": "a.txt", "contentType": "text/plain", "contentBase64": "QUJD" * 300}])
    path = tmp_path / "t.jsonl"
    path.write_text(stream(INIT, tool_use("1", "mcp__x__submit_order", {"attachments": attachments}), tool_result("1", "{}"),
                           tool_use("2", "Bash", {"command": 'echo \'{"contentBase64":"' + "Z" * 1500 + '"}\''}), tool_result("2", ""),
                           RESULT), encoding="utf-8")
    transcript = uc.read_stream_json(path.read_text().splitlines())
    assert transcript.finished and len(transcript.calls) == 2
    assert uc.largest_base64(transcript) == 1500
    lines = []
    assert uc.main(["largest-base64", "--transcript", str(path)], echo=lines.append) == 0
    assert "größtes contentBase64 1500 Zeichen" in lines[0] and "≤ 1024: nein" in lines[0]
    path.write_text(stream(INIT, tool_use("1", "Bash", {"command": "ls"})), encoding="utf-8")
    assert uc.main(["largest-base64", "--transcript", str(path)], echo=lines.append) == 4


def test_curl_evidence_needs_the_token_path_of_the_ticket():
    upload = "11111111-2222-4333-8444-555555555555"
    answer = json.dumps({"uploadId": upload, "sha256": "a" * 64, "receipt": {"usable": True}})
    make = lambda command, ok=True, text=answer: uc.ToolCall("Bash", {"command": command}, ok, text)
    assert uc.curl_evidence(make(f"curl -T f https://h.example/api/uploads/{upload}"), upload) == "a" * 64
    assert uc.curl_evidence(make(f"curl -T f https://h.example/api/uploads/{upload.upper()} -w '%{{http_code}}'"), upload) == "a" * 64
    assert uc.curl_evidence(make(f"curl -T f https://h.example/api/uploads/{upload}/page"), upload) is None
    assert uc.curl_evidence(make(f"wget https://h.example/api/uploads/{upload}"), upload) is None
    assert uc.curl_evidence(make(f"curl -T f https://h.example/api/uploads/{upload}", ok=False), upload) is None
    refused = json.dumps({"uploadId": upload, "sha256": "a" * 64, "receipt": {"usable": False}})
    assert uc.curl_evidence(make(f"curl https://h.example/api/uploads/{upload}", text=refused), upload) is None
    assert uc.curl_evidence(make(f"curl https://h.example/api/uploads/{upload}", text="curl: (22) 422\n" + answer), upload) == "a" * 64


class Recorder:
    def __init__(self, stdout="", returncode=0, stderr=""):
        self.stdout, self.returncode, self.stderr = stdout, returncode, stderr
        self.calls = []

    def __call__(self, cmd, **kwargs):
        self.calls.append((cmd, kwargs))
        return subprocess.CompletedProcess(cmd, self.returncode, self.stdout, self.stderr)


def test_psql_database_is_read_only_and_parses_json():
    recorder = Recorder(stdout='{"now": "2026-12-01T10:00:00+00:00"}\n')
    db = uc.PsqlDatabase("some-postgres", runner=recorder)
    assert db.now() == "2026-12-01T10:00:00+00:00"
    cmd, kwargs = recorder.calls[0]
    assert cmd[:4] == ["docker", "exec", "-i", "some-postgres"]
    assert "default_transaction_read_only=on" in cmd[-1]
    assert kwargs["input"].startswith("BEGIN TRANSACTION READ ONLY;")
    assert kwargs["input"].rstrip().endswith("ROLLBACK;")
    recorder.stdout = ""
    assert db.ticket("11111111-2222-4333-8444-555555555555", "a" * 64, None) is None
    with pytest.raises(uc.DatabaseError):
        uc.PsqlDatabase("x", runner=Recorder(returncode=1, stderr='ERROR: relation "UploadTickets" does not exist')).now()
    with pytest.raises(uc.DatabaseError):
        uc.PsqlDatabase("x", runner=Recorder(stdout="not json")).now()


def test_queries_never_write_and_never_return_names_hashes_or_users():
    sql = "\n".join([uc.NOW_SQL, uc.ticket_sql("11111111-2222-4333-8444-555555555555", "a" * 64, "11111111-2222-4333-8444-555555555556"),
                     uc.window_sql("2026-12-01T10:00:00+00:00", "2026-12-01T11:00:00+00:00", ["scan-1234abcd.pdf"])])
    assert not re.search(r"\b(INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|TRUNCATE|COPY|GRANT|MERGE|CALL)\b", sql, re.IGNORECASE)
    returned = re.findall(r"'(\w+)', ", sql)
    assert not {"FileName", "OwnerUsername", "Sha256", "ExtractedText", "TokenHash"} & set(returned)
    assert "- 'OwnerUsername'" in sql and '"FileName"' not in sql and '"ExtractedText"' not in sql


@pytest.mark.parametrize("call", [
    lambda: uc.ticket_sql("x'; DROP TABLE \"Sources\"; --", "a" * 64, None),
    lambda: uc.ticket_sql("11111111-2222-4333-8444-555555555555", "A" * 64, None),
    lambda: uc.ticket_sql("11111111-2222-4333-8444-555555555555", "a" * 64, "1 OR 1=1"),
    lambda: uc.window_sql("now()", "2026-12-01T11:00:00+00:00", []),
    lambda: uc.window_sql("2026-12-01T10:00:00", "2026-12-01T11:00:00+00:00", []),
    lambda: uc.window_sql("2026-12-01T10:00:00+00:00", "2026-12-01T11:00:00+00:00", ["a\";b.pdf"]),
    lambda: uc.window_sql("2026-12-01T10:00:00+00:00", "2026-12-01T11:00:00+00:00", ["50%_off.pdf"]),
])
def test_query_literals_are_validated(call):
    with pytest.raises(uc.UsageError):
        call()


def test_generated_files_meet_the_gate_requirements(tmp_path):
    manifest = uc.generate(tmp_path / "g", seed=11)
    files = {k: tmp_path / "g" / v["file"] for k, v in manifest["files"].items()}
    s1 = files["s1"].read_bytes()
    assert uc.is_pdf(s1) and len(s1) >= uc.MIN_S1_BYTES and uc.pdf_page_count(s1) == 24 >= uc.MIN_S1_PAGES
    first, last = manifest["files"]["s1"]["markers"]
    assert s1.index(first.encode().replace(b"(", b"\\(").replace(b")", b"\\)")) < s1.index(
        last.encode().replace(b"(", b"\\(").replace(b")", b"\\)"))
    scan = files["scan"].read_bytes()
    assert uc.is_pdf(scan) and uc.pdf_page_count(scan) == 3
    assert b"/Font" not in scan and b"Tj" not in b"".join(re.findall(rb"stream\n(q [^\n]*Do Q)\nendstream", scan))
    assert len(re.findall(rb"stream\n(q [^\n]*Do Q)\nendstream", scan)) == 3
    assert uc.is_docx(files["s2"])
    with zipfile.ZipFile(files["s2"]) as archive:
        document = archive.read("word/document.xml").decode()
    assert first in document and last in document
    png = files["png"].read_bytes()
    assert png.startswith(b"\x89PNG\r\n\x1a\n")
    pos = 8
    while pos < len(png):
        length = struct.unpack(">I", png[pos:pos + 4])[0]
        kind, data = png[pos + 4:pos + 8], png[pos + 8:pos + 8 + length]
        assert struct.unpack(">I", png[pos + 8 + length:pos + 12 + length])[0] == zlib.crc32(kind + data) & 0xFFFFFFFF
        pos += 12 + length
    assert manifest["files"]["size"]["declared_bytes"] == files["size"].stat().st_size + 100
    attachments = json.loads((tmp_path / "g" / manifest["files"]["placeholder"]["attachments"]).read_text())
    text = base64.b64decode(attachments[0]["contentBase64"]).decode()
    assert text == files["placeholder"].read_text() and attachments[0]["filename"] == files["placeholder"].name
    assert re.search(r"\b(see|refer\s+to)\s+(the\s+)?(attached|attachment|enclosed)", text, re.IGNORECASE)
    assert len(re.sub(r"\s", "", text)) >= 10
    assert all(uc.SAFE_NAME_RE.match(p.name) for key, p in files.items() if key != "s2")
    assert "'" in files["s2"].name and " " in files["s2"].name, "T-A2 measures quoting with an apostrophe and spaces"
    plan = json.loads((tmp_path / "g" / "plan-vorlage.json").read_text())
    assert plan["t_a2"]["sessions"][0]["file"] == files["s1"].name


def test_every_run_gets_new_bytes_and_a_seed_repeats_them(tmp_path):
    a, b, c = (uc.generate(tmp_path / n, seed=seed) for n, seed in (("a", 1), ("b", 2), ("c", 1)))
    for key in ("s1", "s2", "page", "scan", "png", "size"):
        bytes_a = (tmp_path / "a" / a["files"][key]["file"]).read_bytes()
        assert bytes_a != (tmp_path / "b" / b["files"][key]["file"]).read_bytes()
        assert bytes_a == (tmp_path / "c" / c["files"][key]["file"]).read_bytes()
    unseeded = uc.generate(tmp_path / "d")
    assert unseeded["nonce"] != uc.generate(tmp_path / "e")["nonce"]


@pytest.mark.skipif(shutil.which("pdftotext") is None or shutil.which("pdfinfo") is None, reason="poppler-utils fehlen")
def test_generated_pdfs_read_with_a_real_pdf_reader(tmp_path):
    manifest = uc.generate(tmp_path, seed=5)
    s1 = tmp_path / manifest["files"]["s1"]["file"]
    first, last = manifest["files"]["s1"]["markers"]
    info = subprocess.run(["pdfinfo", str(s1)], capture_output=True, text=True, check=True).stdout
    assert re.search(r"^Pages:\s+24$", info, re.MULTILINE)
    page = lambda n: subprocess.run(["pdftotext", "-f", str(n), "-l", str(n), str(s1), "-"], capture_output=True, text=True,
                                    check=True).stdout
    assert first in page(1) and last in page(24)
    text = subprocess.run(["pdftotext", str(s1), "-"], capture_output=True, text=True, check=True).stdout
    assert len(text) / 24 >= 200
    scan = tmp_path / manifest["files"]["scan"]["file"]
    assert subprocess.run(["pdftotext", str(scan), "-"], capture_output=True, text=True, check=True).stdout.strip() == ""


def _csharp_constant(relative, name):
    source = (REPO / relative).read_text(encoding="utf-8")
    match = re.search(rf'const string {name}\s*=\s*((?:"(?:[^"\\]|\\.)*"\s*\+?\s*)+);', source)
    assert match, f"{name} nicht in {relative}"
    return "".join(json.loads(part) for part in re.findall(r'"(?:[^"\\]|\\.)*"', match.group(1)))


@pytest.mark.parametrize("relative,name,value", [
    ("src/Geef.Atelier.Application/Sources/UploadTicketLines.cs", "PageDone", uc.PAGE_DONE),
    ("src/Geef.Atelier.Application/Sources/UploadTicketLines.cs", "PageHeading", uc.PAGE_HEADING),
    ("src/Geef.Atelier.Application/Sources/UploadTicketLines.cs", "PageAlreadyUsed", uc.PAGE_ALREADY_USED),
    ("src/Geef.Atelier.Application/Sources/UploadTicketLines.cs", "PageExpired", uc.PAGE_EXPIRED),
    ("src/Geef.Atelier.Application/Sources/UploadTicketLines.cs", "NotFoundFormat", uc.NOT_FOUND_FORMAT),
    ("src/Geef.Atelier.Application/Sources/UploadTicketLines.cs", "SizeMismatchFormat", uc.SIZE_MISMATCH_FORMAT),
    ("src/Geef.Atelier.Application/Sources/UploadTicketLines.cs", "LargerThanAnnouncedFormat", uc.LARGER_THAN_ANNOUNCED_FORMAT),
    ("src/Geef.Atelier.Infrastructure/Sources/Extraction/PdfSourceExtractor.cs", "NoTextReason", uc.NO_TEXT_REASON),
    ("src/Geef.Atelier.Infrastructure/Sources/SourceKindDetector.cs", "UnsupportedReason", uc.UNSUPPORTED_REASON),
    ("src/Geef.Atelier.Application/Sources/UploadTicketRoutes.cs", "TokenUploadPathFormat", uc.TOKEN_UPLOAD_PATH + "{0}"),
])
def test_sentences_match_the_csharp_constants(relative, name, value):
    assert _csharp_constant(relative, name) == value


def test_intro_start_and_placeholder_problem_match_the_csharp_sources():
    lines = (REPO / "src/Geef.Atelier.Application/Sources/UploadTicketLines.cs").read_text(encoding="utf-8")
    assert f'"{uc.PAGE_INTRO_START} {{0}}.' in lines
    parser = (REPO / "src/Geef.Atelier.Mcp/Tools/McpAttachmentParser.cs").read_text(encoding="utf-8")
    assert f'"{uc.PLACEHOLDER_PROBLEM}"' in parser


def test_codes_and_paths_match_the_csharp_sources():
    codes = (REPO / "src/Geef.Atelier.Core/Domain/Sources/SourceRefusalCodes.cs").read_text(encoding="utf-8")
    assert set(re.findall(r'const string \w+ = "([a-z0-9_]+)";', codes)) == uc.REFUSAL_CODES
    types = (REPO / "src/Geef.Atelier.Core/Domain/Sources/UploadTicketTypes.cs").read_text(encoding="utf-8")
    paths = re.search(r"enum RefusalPath \{([^}]*)\}", types).group(1)
    assert {uc._norm(p.split("=")[0]) for p in paths.split(",")} == {uc._norm(p) for p in uc.REFUSAL_PATHS}
    states = re.search(r"enum UploadTicketState \{([^}]*)\}", types).group(1)
    assert "Used" in states
    entity = (REPO / "src/Geef.Atelier.Core/Domain/Sources/SourceRefusalEntity.cs").read_text(encoding="utf-8")
    assert set(re.findall(r"public (?:required )?[\w?]+ (\w+) \{ get; init; \}", entity)) == uc.REFUSAL_COLUMNS
    kinds = (REPO / "src/Geef.Atelier.Core/Domain/Sources/SourceTypes.cs").read_text(encoding="utf-8")
    assert {uc._norm(k.split("=")[0]) for k in re.search(r"enum SourceKind \{([^}]*)\}", kinds).group(1).split(",")} == uc.SOURCE_KINDS


def test_the_scenario_is_deep_copy_safe(s):
    snapshot = copy.deepcopy(s.world)
    s.evaluate()
    assert s.world == snapshot


def test_the_name_with_an_apostrophe_is_quoted_for_the_query_and_wildcards_are_refused():
    assert uc._names_literal(["anna's notes s2 (1f).docx"]) == "ARRAY['%anna''s notes s2 (1f).docx%']::text[]"
    for hostile in ("a%b.txt", "a_b.txt", "a\\b.txt", "a;b.txt", "a\"b.txt", "'; drop table x; --"):
        with pytest.raises(uc.UsageError):
            uc._names_literal([hostile])

