import base64
import json
import os
from pathlib import Path

import pytest

from geef_eval import mcp_contract as mc
from geef_eval.mcp import HttpReply

A = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
B = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
SECRET = "geheim-passwort-123"


def tool(name, props=(), required=(), description="A tool."):
    return {"name": name, "description": description,
            "inputSchema": {"type": "object", "properties": {p: {"type": "string"} for p in props}, "required": list(required)}}


def contract_tools(tickets=False):
    tools = [
        tool("submit_order", ("request", "attachments"), ("request",)),
        tool("answer_order", ("orderId", "answers", "skip"), ("orderId",)),
        tool("get_order_status", ("orderId", "waitSeconds"), ("orderId",)),
        tool("get_order_result", ("orderId",), ("orderId",)),
        tool("revise_order", ("orderId", "change", "quick"), ("orderId",),
             "Pass the person's words as they are. Then call get_order_status."),
        tool("export_order", ("orderId", "format", "version"), ("orderId", "format")),
        tool("cancel_order", ("orderId",), ("orderId",)),
        tool("list_orders", ("limit",)),
        tool("profile", (), (), "Reads the About me details of the person. They change them under About me in the web app."),
    ]
    tools[4]["inputSchema"]["properties"]["quick"] = {"type": "array", "items": {"type": "string"}}
    tools[5]["inputSchema"]["properties"]["format"] = {"type": "string", "enum": ["md", "docx", "pdf"]}
    tools.extend(tool(name, description=f"{mc.RETIRED_PREFIX}{new} instead. Every call returns an error that names it.")
                 for name, new in mc.RETIRED_TOOLS.items())
    if tickets:
        tools.append(tool("prepare_attachment", ("fileName",)))
    return tools


def call(seq, name, arguments, response=None, *, ok=True, error=None, duration=800, source=mc.CLAUDE_CODE, readable=True):
    return {"seq": seq, "tool": name, "arguments": arguments, "started_at": "2026-11-29T22:00:00Z", "duration_ms": duration,
            "ok": ok, "error": error, "response": response, "readable": readable, "source": source}


def md_export(text):
    return {"fileName": "rundbrief-v2.md", "contentType": "text/markdown; charset=utf-8",
            "contentBase64": base64.b64encode(text.encode()).decode(), "text": text}


def docx_export():
    return {"fileName": "rundbrief-v2.docx", "contentType": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "contentBase64": base64.b64encode(b"PK\x03\x04rest").decode()}


def gate_m_calls(**replace):
    rows = [
        call(1, "submit_order", {"request": "Kurzer Rundbrief"}, {"orderId": A, "state": "needs_input", "questions": [{"id": "q1"}]}),
        call(2, "answer_order", {"orderId": A, "answers": [{"questionId": "q1", "optionId": "o1", "origin": "user"}]},
             {"orderId": A, "state": "working"}),
        call(3, "get_order_status", {"orderId": A, "waitSeconds": 50}, {"orderId": A, "state": "working", "done": False},
             duration=50_100),
        call(4, "get_order_status", {"orderId": A, "waitSeconds": 50}, {"orderId": A, "state": "delivered", "done": True},
             duration=12_000),
        call(5, "get_order_result", {"orderId": A}, {"orderId": A, "state": "delivered", "text": "Text eins", "version": 1}),
        call(6, "revise_order", {"orderId": A, "change": "Kürzer bitte"},
             {"orderId": A, "state": "working", "version": 2, "notices": []}),
        call(7, "get_order_status", {"orderId": A, "waitSeconds": 50}, {"orderId": A, "state": "delivered", "done": True},
             duration=40_000),
        call(8, "get_order_result", {"orderId": A},
             {"orderId": A, "state": "delivered", "text": "Text zwei", "version": 2, "latestVersion": 2}),
        call(9, "export_order", {"orderId": A, "format": "md"}, md_export("Text zwei")),
        call(10, "export_order", {"orderId": A, "format": "docx"}, docx_export()),
        call(11, "submit_order", {"request": "Zweiter Auftrag"}, {"orderId": B, "state": "preparing"}),
        call(12, "list_orders", {"limit": 10}, [
            {"orderId": B, "title": "Zweiter", "state": "preparing", "version": None, "updatedAt": "x", "wordCount": None},
            {"orderId": A, "title": "Rundbrief", "state": "delivered", "version": 2, "updatedAt": "x", "wordCount": 2}]),
        call(13, "profile", {}, {"displayName": None}),
        call(14, "cancel_order", {"orderId": B}, {"orderId": B, "state": "stopped", "changed": True}),
    ]
    for seq, row in replace.items():
        index = int(seq.removeprefix("c")) - 1
        if row is None:
            rows.pop(index)
        else:
            rows[index] = row
    return rows


class FakeSession:
    def __init__(self, *, login_status=302, cookie=True, state=None, token_body=None):
        self.requests = []
        self.login_status, self.cookie, self.state, self.token_body = login_status, cookie, state, token_body
        self._cookie = False

    def request(self, method, path, form=None):
        self.requests.append((method, path, dict(form or {})))
        if path == "/login" and method == "GET":
            return HttpReply(200, {}, '<form><input type="hidden" name="__RequestVerificationToken" value="af-1" /></form>')
        if path == "/login":
            self._cookie = self.cookie
            return HttpReply(self.login_status, {"location": "/"}, "")
        if path.startswith("/oauth/authorize?") and method == "GET":
            return HttpReply(200, {}, '<form method="post" action="/oauth/consent">'
                                      '<input type="hidden" name="__RequestVerificationToken" value="cf-2" /></form>')
        if path == "/oauth/consent":
            state = self.state or form["state"]
            return HttpReply(302, {"location": f"http://127.0.0.1/cb?code=c-1&state={state}"}, "")
        if path == "/oauth/token":
            return HttpReply(200, {}, self.token_body if self.token_body is not None else json.dumps({"access_token": "tok-1"}))
        raise AssertionError(path)

    def has_cookie(self, name):
        return self._cookie and name == "Atelier.Auth"


def test_env_file_needs_mode_600_and_strips_quotes(tmp_path):
    path = tmp_path / "messnutzer.env"
    path.write_text(f"# Kommentar\nexport ATELIER_SMOKE_USER=nutzer\nATELIER_SMOKE_PASSWORD='{SECRET}'\n", encoding="utf-8")
    os.chmod(path, 0o644)
    with pytest.raises(ValueError, match="Modus 644"):
        mc.load_env_file(path)
    os.chmod(path, 0o600)
    assert mc.load_env_file(path) == {"ATELIER_SMOKE_USER": "nutzer", "ATELIER_SMOKE_PASSWORD": SECRET}


@pytest.mark.parametrize("url,ok", [("https://atelier.example/", True), ("http://127.0.0.1:8080", True),
                                    ("http://atelier.example", False), ("ftp://atelier.example", False), ("", False)])
def test_base_url_needs_https_unless_loopback(url, ok):
    if ok:
        assert not mc.check_base_url(url).endswith("/")
    else:
        with pytest.raises(ValueError):
            mc.check_base_url(url)


def test_oauth_runs_login_consent_and_pkce_exchange():
    session = FakeSession()
    assert mc.oauth_access_token(session, "nutzer", SECRET, "client-1") == "tok-1"
    paths = [(m, p.split("?")[0]) for m, p, _ in session.requests]
    assert paths == [("GET", "/login"), ("POST", "/login"), ("GET", "/oauth/authorize"), ("POST", "/oauth/consent"),
                     ("POST", "/oauth/token")]
    login, consent, exchange = session.requests[1][2], session.requests[3][2], session.requests[4][2]
    assert consent["__RequestVerificationToken"] == "cf-2"
    assert login["__RequestVerificationToken"] == "af-1" and login["Model.Password"] == SECRET
    assert consent["code_challenge_method"] == "S256" and consent["scope"] == "mcp"
    assert exchange["code"] == "c-1" and exchange["code_verifier"]


@pytest.mark.parametrize("session,message", [
    (FakeSession(login_status=200), "Login abgelehnt"),
    (FakeSession(cookie=False), "Login abgelehnt"),
    (FakeSession(state="fremd"), "falschem state"),
    (FakeSession(token_body="[]"), "ohne access_token"),
])
def test_oauth_failures_never_show_the_password(session, message):
    with pytest.raises(mc.SignInError, match=message) as info:
        mc.oauth_access_token(session, "nutzer", SECRET, "client-1")
    assert SECRET not in str(info.value)


def test_connect_with_env_file_or_token(tmp_path):
    path = tmp_path / "m.env"
    path.write_text(f"ATELIER_SMOKE_USER=nutzer\nATELIER_SMOKE_PASSWORD={SECRET}\nATELIER_SMOKE_CLIENT_ID=c\n"
                    "ATELIER_SMOKE_BASE_URL=https://atelier.example\n", encoding="utf-8")
    os.chmod(path, 0o600)
    client = mc.connect(path, session_factory=lambda base: FakeSession())
    assert client.endpoint == "https://atelier.example/mcp" and client.token == "tok-1"
    client = mc.connect(None, environ={"EVAL_ATELIER_URL": "https://atelier.example", "EVAL_ATELIER_TOKEN": "t"})
    assert client.token == "t"
    with pytest.raises(ValueError, match="Anmeldung fehlt"):
        mc.connect(None, environ={})
    path.write_text("ATELIER_SMOKE_USER=nutzer\n", encoding="utf-8")
    with pytest.raises(ValueError, match="fehlen"):
        mc.connect(path, session_factory=lambda base: FakeSession())


def test_list_tools_follows_cursor():
    seen = []

    def transport(url, headers, body, timeout_s):
        message = json.loads(body)
        seen.append(message.get("method"))
        if message["method"] == "tools/list":
            page = 2 if message["params"].get("cursor") else 1
            result = {"tools": [{"name": f"t{page}"}], **({"nextCursor": "n"} if page == 1 else {})}
            return HttpReply(200, {"content-type": "application/json"}, json.dumps({"jsonrpc": "2.0", "id": message["id"],
                                                                                    "result": result}))
        if "id" in message:
            return HttpReply(200, {}, json.dumps({"jsonrpc": "2.0", "id": message["id"], "result": {}}))
        return HttpReply(202, {}, "")

    client = mc.ContractClient("https://atelier.example", "t", transport=transport)
    assert [t["name"] for t in client.list_tools()] == ["t1", "t2"]
    assert seen == ["initialize", "notifications/initialized", "tools/list", "tools/list"]


def session_lines():
    def use(uid, name, arguments, stamp):
        return json.dumps({"type": "assistant", "timestamp": stamp,
                           "message": {"content": [{"type": "tool_use", "id": uid, "name": name, "input": arguments}]}})

    def res(uid, content, stamp, error=False):
        return json.dumps({"type": "user", "timestamp": stamp, "message": {"content": [
            {"type": "tool_result", "tool_use_id": uid, "content": content, "is_error": error}]}})

    return [
        use("u1", "mcp__geef__submit_order", {"request": "x"}, "2026-11-29T22:00:00.000Z"),
        "kein json",
        res("u1", [{"type": "text", "text": json.dumps({"orderId": A, "state": "needs_input"})}], "2026-11-29T22:00:02.500Z"),
        use("u2", "mcp__other__submit_order", {}, "2026-11-29T22:00:03.000Z"),
        use("u3", "mcp__geef__get_order_status", {"orderId": A, "waitSeconds": 50}, "2026-11-29T22:00:04.000Z"),
        res("u3", "MCP error -32001: Request timed out", "2026-11-29T22:01:04.000Z", error=True),
        use("u4", "mcp__geef__list_orders", {}, "2026-11-29T22:01:05.000Z"),
        res("u4", [{"type": "text", "text": json.dumps({"result": [{"orderId": A}]})}], "2026-11-29T22:01:05.300Z"),
        use("u5", "mcp__geef__export_order", {"orderId": A, "format": "docx"}, "2026-11-29T22:01:06.000Z"),
        res("u5", [{"type": "text", "text": "{\"contentBase64\": \"UEsDB... (gekürzt)"}], "2026-11-29T22:01:07.000Z"),
        use("u6", "mcp__geef__profile", {}, "2026-11-29T22:01:08.000Z"),
    ]


def test_import_claude_session_keeps_order_durations_errors_and_unreadable_answers():
    calls = mc.import_claude_session(session_lines(), "geef")
    assert [c["tool"] for c in calls] == ["submit_order", "get_order_status", "list_orders", "export_order", "profile"]
    assert calls[0]["duration_ms"] == 2500 and calls[0]["response"]["state"] == "needs_input"
    assert calls[1]["ok"] is False and "timed out" in calls[1]["error"] and calls[1]["duration_ms"] == 60_000
    assert calls[2]["response"] == [{"orderId": A}]
    assert calls[3]["ok"] is True and calls[3]["readable"] is False
    assert calls[4]["ok"] is None
    assert {c["source"] for c in calls} == {mc.CLAUDE_CODE}


def test_tool_list_of_the_contract_passes_with_and_without_upload_tickets():
    assert mc.check_tool_list(contract_tools()) == []
    assert mc.check_tool_list(contract_tools(tickets=True)) == []


def test_the_expected_tools_are_the_ten_order_tools_and_five_stubs():
    assert len(mc.ORDER_TOOLS + mc.TICKET_TOOLS) == 10
    assert sorted(mc.RETIRED_TOOLS) == ["get_run_details", "get_run_result", "get_run_status", "submit_auto_crew_request",
                                        "submit_request"]


def test_profile_may_offer_exactly_the_saving_parameters():
    tools = contract_tools()
    profile = next(t for t in tools if t["name"] == "profile")
    profile["inputSchema"]["properties"] = {p: {"type": "string"} for p in mc.PROFILE_SAVING_PARAMETERS}
    assert mc.check_tool_list(tools) == []


def test_tool_list_differences_are_named():
    tools = contract_tools()
    assert any("zu viel: list_runs" in p for p in mc.check_tool_list([*tools, tool("list_runs")]))
    assert any("fehlen: profile" in p for p in mc.check_tool_list([t for t in tools if t["name"] != "profile"]))
    broken = contract_tools()
    by_name = {t["name"]: t for t in broken}
    by_name["revise_order"]["inputSchema"]["required"] = ["orderId", "change"]
    by_name["profile"]["inputSchema"]["properties"]["displayName"] = {"type": "string"}
    by_name["export_order"]["inputSchema"]["properties"]["format"]["enum"] = ["md", "html"]
    by_name["get_run_status"]["description"] = "Lists the status."
    by_name["get_run_result"]["description"] = f"{mc.RETIRED_PREFIX}get_order_status instead."
    problems = mc.check_tool_list(broken)
    assert any(p.startswith("get_run_status: Beschreibung") for p in problems)
    assert any(p.startswith("get_run_result: Beschreibung") for p in problems)
    assert any(p.startswith("revise_order: Pflichtparameter") for p in problems)
    assert any(p.startswith("profile: hat unbekannte Parameter displayName") for p in problems)
    assert any(p.startswith("export_order: format") for p in problems)


def test_gate_m_passes_with_a_complete_claude_code_transcript():
    report = mc.evaluate_gate_m(contract_tools(), gate_m_calls())
    assert report["verdict"] == "passed", (report["failures"], report["problems"])
    assert report["steps"]["submits"]["count"] == 2


def test_gate_m_fails_on_any_tool_error():
    rows = gate_m_calls(c13=call(13, "profile", {}, ok=False, error="Something went wrong"))
    report = mc.evaluate_gate_m(contract_tools(), rows)
    assert report["verdict"] == "failed"
    assert any("profile" in f for f in report["failures"])


@pytest.mark.parametrize("replace,needle", [
    ({"c6": call(6, "revise_order", {"orderId": A}, {"orderId": A, "state": "working", "version": 3, "notices": []})},
     "revise_order"),
    ({"c9": call(9, "export_order", {"orderId": A, "format": "md"}, dict(md_export("Text zwei"), text="anders"))}, "md"),
    ({"c10": call(10, "export_order", {"orderId": A, "format": "docx"},
                  dict(docx_export(), contentBase64=base64.b64encode(b"%PDF").decode()))}, "ZIP"),
    ({"c12": call(12, "list_orders", {"limit": 10}, [{"orderId": B}])}, "list_orders"),
    ({"c8": call(8, "get_order_result", {"orderId": A}, {"orderId": A, "state": "delivered", "text": "eins", "version": 1})},
     "Version 2"),
])
def test_gate_m_fails_on_wrong_answers(replace, needle):
    report = mc.evaluate_gate_m(contract_tools(), gate_m_calls(**replace))
    assert report["verdict"] == "failed"
    assert any(needle in f for f in report["failures"]), report["failures"]


@pytest.mark.parametrize("replace,needle", [
    ({"c1": call(1, "submit_order", {"request": "x"}, {"orderId": A, "state": "working"})}, "keine Rückfrage"),
    ({"c14": call(14, "cancel_order", {"orderId": B}, {"orderId": B, "state": "delivered", "changed": False})}, "änderte nichts"),
    ({"c13": None}, "profile"),
    ({"c10": call(10, "export_order", {"orderId": A, "format": "docx"}, "{\"contentBase64\": \"UEs", readable=False)},
     "nicht lesbar"),
    ({"c12": call(12, "list_orders", {"limit": 10}, None, ok=None)}, "kein Ergebnis"),
])
def test_gate_m_is_not_evaluable_without_data(replace, needle):
    report = mc.evaluate_gate_m(contract_tools(), gate_m_calls(**replace))
    assert report["verdict"] == "not_evaluable", (report["failures"], report["problems"])
    assert any(needle in p for p in report["problems"]), report["problems"]


def test_gate_m_accepts_list_rows_without_the_null_fields_the_server_leaves_out():
    rows = gate_m_calls(c12=call(12, "list_orders", {"limit": 10}, [
        {"orderId": B, "title": "Zweiter", "state": "preparing", "updatedAt": "x"},
        {"orderId": A, "title": "Rundbrief", "state": "delivered", "version": 2, "updatedAt": "x", "wordCount": 2}]))
    assert mc.evaluate_gate_m(contract_tools(), rows)["verdict"] == "passed"

    without_words = gate_m_calls(c12=call(12, "list_orders", {"limit": 10}, [
        {"orderId": A, "title": "Rundbrief", "state": "delivered", "version": 2, "updatedAt": "x"}]))
    report = mc.evaluate_gate_m(contract_tools(), without_words)
    assert report["verdict"] == "failed"
    assert any("Auftrag A (mit Text)" in f for f in report["failures"])

    without_state = gate_m_calls(c12=call(12, "list_orders", {"limit": 10}, [
        {"orderId": B, "title": "Zweiter", "updatedAt": "x"},
        {"orderId": A, "title": "Rundbrief", "state": "delivered", "version": 2, "updatedAt": "x", "wordCount": 2}]))
    assert mc.evaluate_gate_m(contract_tools(), without_state)["verdict"] == "failed"


def test_gate_m_needs_claude_code_and_a_tool_list():
    python_rows = [dict(row, source="python") for row in gate_m_calls()]
    assert mc.evaluate_gate_m(contract_tools(), python_rows)["verdict"] == "not_evaluable"
    assert mc.evaluate_gate_m(None, gate_m_calls())["verdict"] == "not_evaluable"
    assert mc.evaluate_gate_m(contract_tools(), [])["verdict"] == "not_evaluable"


def test_gate_m_fails_on_a_wrong_tool_list():
    report = mc.evaluate_gate_m([t for t in contract_tools() if t["name"] != "submit_request"], gate_m_calls())
    assert report["verdict"] == "failed"
    assert any(f.startswith("Werkzeugliste:") for f in report["failures"])


def vmcp_calls(*durations, errors=()):
    rows = [call(1, "submit_order", {"request": "x"}, {"orderId": A, "state": "working"})]
    for index, duration in enumerate(durations, 2):
        failed = index - 2 in errors
        rows.append(call(index, "get_order_status", {"orderId": A, "waitSeconds": 50},
                         None if failed else {"orderId": A, "state": "working"}, ok=not failed,
                         error="MCP error -32001: Request timed out" if failed else None, duration=duration))
    return rows


def test_vmcp_keeps_50_seconds_when_long_calls_pass():
    report = mc.evaluate_vmcp(vmcp_calls(50_300, 50_100, 9_000))
    assert report["verdict"] == "keep_50" and report["orders"] == 1


@pytest.mark.parametrize("calls", [vmcp_calls(50_300, 57_000), vmcp_calls(50_300, 60_000, errors=(1,))])
def test_vmcp_falls_back_to_25_seconds(calls):
    assert mc.evaluate_vmcp(calls)["verdict"] == "back_to_25"


@pytest.mark.parametrize("calls,needle", [
    (vmcp_calls(), "kein get_order_status"),
    (vmcp_calls(10_000, 20_000), "wartete"),
    ([dict(c, source="python") for c in vmcp_calls(50_300)], "Claude Code"),
    (vmcp_calls(50_300) + [call(9, "get_order_result", {"orderId": A}, ok=False, error="boom")], "andere Aufrufe"),
    (vmcp_calls(50_300) + [call(9, "get_order_status", {"orderId": A, "waitSeconds": 50}, ok=None, duration=None)],
     "fehlt im Mitschnitt"),
])
def test_vmcp_is_not_evaluable_without_a_real_long_call(calls, needle):
    report = mc.evaluate_vmcp(calls)
    assert report["verdict"] == "not_evaluable"
    assert any(needle in p for p in report["problems"]), report["problems"]


def md_call(seq, text, **arguments):
    return call(seq, "export_order", {"orderId": A, "format": "md", **arguments}, md_export(text))


def test_gate_m_fails_when_the_export_without_version_holds_the_old_text():
    report = mc.evaluate_gate_m(contract_tools(), gate_m_calls(c9=md_call(9, "Text eins")))
    assert report["verdict"] == "failed"
    assert report["failures"] == ["export_order md ohne version (#9) weicht vom Text der Version 2 aus get_order_result ab"]


def test_gate_m_checks_every_later_export_without_version_also_with_the_default_format():
    rows = gate_m_calls() + [call(15, "export_order", {"orderId": A}, md_export("Text eins"))]
    report = mc.evaluate_gate_m(contract_tools(), rows)
    assert report["verdict"] == "failed"
    assert any("#15" in f for f in report["failures"])


def test_gate_m_md_step_needs_an_export_without_version():
    report = mc.evaluate_gate_m(contract_tools(), gate_m_calls(c9=md_call(9, "Text eins", version=1)))
    assert report["verdict"] == "not_evaluable", report["failures"]
    assert report["steps"]["export_order_md"] == {"found": False}
    rows = gate_m_calls() + [md_call(15, "Text eins", version=1)]
    assert mc.evaluate_gate_m(contract_tools(), rows)["verdict"] == "passed"


def test_gate_m_export_comparison_forgives_only_line_ends_and_outer_white_space():
    shown = call(8, "get_order_result", {"orderId": A},
                 {"orderId": A, "state": "delivered", "text": "Eins\n\nzwei", "version": 2, "latestVersion": 2})
    rows = gate_m_calls(c8=shown, c9=md_call(9, "Eins\r\n\r\nzwei\r\n"))
    assert mc.evaluate_gate_m(contract_tools(), rows)["verdict"] == "passed"
    rows = gate_m_calls(c8=shown, c9=md_call(9, "Eins\n\nzwei und mehr"))
    assert mc.evaluate_gate_m(contract_tools(), rows)["verdict"] == "failed"


def write_lines(path, rows):
    path.write_text("".join(json.dumps(r, ensure_ascii=False) + "\n" for r in rows), encoding="utf-8")


def test_main_tools_import_evaluate_and_vmcp(tmp_path):
    class Client:
        def list_tools(self):
            return contract_tools()

    lines = []
    assert mc.main(["tools", "--out", str(tmp_path / "tools.json")], connector=lambda _: Client(), echo=lines.append) == 0
    stored = json.loads((tmp_path / "tools.json").read_text(encoding="utf-8"))
    assert len(stored["tools"]) == len(mc.ORDER_TOOLS) + len(mc.RETIRED_TOOLS)

    session = tmp_path / "sitzung.jsonl"
    session.write_text("\n".join(session_lines()) + "\n", encoding="utf-8")
    assert mc.main(["import", "--session", str(session), "--server", "geef", "--out", str(tmp_path / "imp.jsonl")],
                   echo=lines.append) == 0
    assert len(mc.read_transcript(tmp_path / "imp.jsonl")) == 5
    assert mc.main(["import", "--session", str(session), "--server", "nichts", "--out", str(tmp_path / "leer.jsonl")],
                   echo=lines.append) == 4

    write_lines(tmp_path / "m.jsonl", gate_m_calls())
    assert mc.main(["evaluate", "--tools", str(tmp_path / "tools.json"), "--transcript", str(tmp_path / "m.jsonl"),
                    "--out", str(tmp_path / "m-gate.json")], echo=lines.append) == 0
    assert json.loads((tmp_path / "m-gate.json").read_text(encoding="utf-8"))["verdict"] == "passed"
    (tmp_path / "wrong-tools.json").write_text(json.dumps({"tools": contract_tools()[1:]}), encoding="utf-8")
    assert mc.main(["evaluate", "--tools", str(tmp_path / "wrong-tools.json"), "--transcript", str(tmp_path / "m.jsonl"),
                    "--out", str(tmp_path / "m-gate.json")], echo=lines.append) == 1

    write_lines(tmp_path / "v.jsonl", vmcp_calls(50_200))
    assert mc.main(["vmcp", "--transcript", str(tmp_path / "v.jsonl"), "--out", str(tmp_path / "v.json")],
                   echo=lines.append) == 0
    write_lines(tmp_path / "v.jsonl", vmcp_calls(9_000))
    assert mc.main(["vmcp", "--transcript", str(tmp_path / "v.jsonl"), "--out", str(tmp_path / "v.json")],
                   echo=lines.append) == 4


def test_main_rejects_unreadable_input_sign_in_failure_and_output_inside_git(tmp_path):
    (tmp_path / "t.jsonl").write_text("{\"kein\": \"aufruf\"}\n", encoding="utf-8")
    assert mc.main(["vmcp", "--transcript", str(tmp_path / "t.jsonl"), "--out", str(tmp_path / "v.json")],
                   echo=lambda _: None) == 2

    def refuse(_):
        raise mc.SignInError("Login abgelehnt (HTTP 200)")

    assert mc.main(["tools", "--out", str(tmp_path / "tools.json")], connector=refuse, echo=lambda _: None) == 3
    lines = []
    inside = Path(__file__).resolve().parent / "nie-hier.json"
    assert mc.main(["vmcp", "--transcript", str(tmp_path / "t.jsonl"), "--out", str(inside)], echo=lines.append) == 2
    assert "Git-Baum" in lines[0] and not inside.exists()


def test_md_export_with_byte_order_mark_matches_its_text():
    export = dict(md_export("Text zwei"), contentBase64=base64.b64encode("﻿Text zwei".encode()).decode())
    report = mc.evaluate_gate_m(contract_tools(), gate_m_calls(c9=call(9, "export_order", {"orderId": A, "format": "md"}, export)))
    assert report["verdict"] == "passed", report["failures"]


def docx_call(seq, file_name="rundbrief-v2.docx", **arguments):
    return call(seq, "export_order", {"orderId": A, "format": "docx", **arguments}, dict(docx_export(), fileName=file_name))


def test_gate_m_docx_step_never_counts_an_export_of_version_1():
    report = mc.evaluate_gate_m(contract_tools(), gate_m_calls(c10=docx_call(10, "rundbrief-v1.docx", version=1)))
    assert report["verdict"] == "not_evaluable", (report["failures"], report["problems"])
    assert report["steps"]["export_order_docx"] == {"found": False}
    rows = gate_m_calls(c10=docx_call(10, "rundbrief-v1.docx", version=1)) + [docx_call(15)]
    assert mc.evaluate_gate_m(contract_tools(), rows)["verdict"] == "passed"


def test_gate_m_docx_step_accepts_version_2_named_explicitly():
    report = mc.evaluate_gate_m(contract_tools(), gate_m_calls(c10=docx_call(10, version=2)))
    assert report["verdict"] == "passed", (report["failures"], report["problems"])
    assert report["steps"]["export_order_docx"] == {"found": True, "file_version": 2}


@pytest.mark.parametrize("row", [
    docx_call(10, "rundbrief-v1.docx"),
    docx_call(10, "rundbrief-v1.docx", version=2),
    call(9, "export_order", {"orderId": A, "format": "md"}, dict(md_export("Text zwei"), fileName="rundbrief-v1.md")),
])
def test_gate_m_fails_when_an_export_of_version_2_names_another_version(row):
    report = mc.evaluate_gate_m(contract_tools(), gate_m_calls(**{f"c{row['seq']}": row}))
    assert report["verdict"] == "failed"
    assert any(f"(#{row['seq']}): Datei gehört zu Version 1, nicht 2" in f for f in report["failures"]), report["failures"]


def test_gate_m_later_docx_export_without_version_must_be_version_2():
    rows = gate_m_calls() + [docx_call(15, "rundbrief-v1.docx")]
    report = mc.evaluate_gate_m(contract_tools(), rows)
    assert report["verdict"] == "failed"
    assert any("#15" in f for f in report["failures"])
    rows = gate_m_calls() + [docx_call(15, "rundbrief-v1.docx", version=1)]
    assert mc.evaluate_gate_m(contract_tools(), rows)["verdict"] == "passed"


def test_gate_m_docx_step_without_a_version_in_its_file_name_is_not_evaluable():
    report = mc.evaluate_gate_m(contract_tools(), gate_m_calls(c10=docx_call(10, "rundbrief.docx")))
    assert report["verdict"] == "not_evaluable", (report["failures"], report["problems"])
    assert any("Dateiname nennt keine Version" in p for p in report["problems"]), report["problems"]


def test_export_file_version_reads_the_version_of_the_file_name():
    assert mc.export_file_version(docx_call(1, "a-b-v12.docx")) == 12
    assert mc.export_file_version(docx_call(1, "rundbrief-v2.docx.txt")) is None
    assert mc.export_file_version(docx_call(1, "rundbrief.docx")) is None
    assert mc.export_file_version(call(1, "export_order", {}, None)) is None
