import base64
import json

import pytest

from conftest import FakeRunner
from geef_eval.arms import AtelierMcpArm, RawClaudeArm, ReferenceArm, build_arms, load_plugin, raw_prompt
from geef_eval.mcp import AtelierRunState, AtelierToolAdapter, HttpReply, McpClient, McpError, parse_jsonrpc_reply
from geef_eval.tasks import Attachment, Task

TASK = Task(id="t1", brief="Schreib eine Einladung.",
            attachments=(Attachment("eckdaten.md", "text/markdown", text="Samstag 15 Uhr"),),
            reference_text="Referenz")


def test_raw_arm_sends_attachments_and_brief_through_claude_opus():
    runner = FakeRunner(["  Fertiger Text  "])

    output = RawClaudeArm(runner=runner).produce(TASK)

    assert output.text == "Fertiger Text"
    assert output.meta["model"] == "opus"
    prompt = runner.calls[0]["prompt"]
    assert '<anhang name="eckdaten.md">\nSamstag 15 Uhr\n</anhang>' in prompt
    assert prompt.index("eckdaten") < prompt.index("AUFTRAG:\nSchreib eine Einladung.")


def test_raw_prompt_marks_binary_attachments_as_unreadable():
    task = Task(id="t", brief="x", attachments=(Attachment("a.pdf", "application/pdf", data=b"%PDF"),))
    assert "Binärdatei" in raw_prompt(task)


def test_reference_arm_needs_a_reference_text():
    assert ReferenceArm().produce(TASK).text == "Referenz"
    with pytest.raises(ValueError):
        ReferenceArm().produce(Task(id="x", brief="y"))


def test_plugin_from_file(tmp_path):
    plugin = tmp_path / "mein_arm.py"
    plugin.write_text(
        "from geef_eval.arms import ArmOutput\n"
        "class Echo:\n"
        "    def __init__(self, name):\n"
        "        self.name = name\n"
        "    def produce(self, task):\n"
        "        return ArmOutput(task.brief.upper(), 0.1)\n",
        encoding="utf-8")

    arms = build_arms(["echo", "referenz"], {"echo": f"{plugin}:Echo"})

    assert [a.name for a in arms] == ["echo", "referenz"]
    assert arms[0].produce(TASK).text == "SCHREIB EINE EINLADUNG."


def test_unknown_arm_and_bad_plugin_spec_are_rejected():
    with pytest.raises(ValueError, match="Unbekannter Arm"):
        build_arms(["gibtsnicht"], {})
    with pytest.raises(ValueError, match="modul:attribut"):
        load_plugin("ohne_doppelpunkt")


def test_atelier_arm_without_environment_fails_clearly(monkeypatch):
    monkeypatch.delenv("EVAL_ATELIER_URL", raising=False)
    monkeypatch.delenv("EVAL_ATELIER_TOKEN", raising=False)
    with pytest.raises(RuntimeError, match="EVAL_ATELIER_TOKEN"):
        build_arms(["atelier"], {})


class FakeAdapter:
    def __init__(self, states, text):
        self.states = list(states)
        self.text = text
        self.submitted = None

    def submit(self, brief, attachments):
        self.submitted = (brief, attachments)
        return "run-1"

    def state(self, run_id):
        return self.states.pop(0)

    def result_text(self, run_id):
        return self.text


class FakeClock:
    def __init__(self):
        self.now = 0.0

    def __call__(self):
        return self.now

    def sleep(self, seconds):
        self.now += seconds


def test_atelier_arm_polls_until_terminal_and_accepts_failed_with_text():
    clock = FakeClock()
    adapter = FakeAdapter([AtelierRunState("Pending"), AtelierRunState("Running"),
                           AtelierRunState("Failed", "Pipeline stopped")], "Bester Entwurf")

    output = AtelierMcpArm(adapter, poll_interval_s=10, sleep=clock.sleep, clock=clock).produce(TASK)

    assert output.text == "Bester Entwurf"
    assert output.meta == {"run_id": "run-1", "status": "Failed", "error": "Pipeline stopped"}
    assert output.duration_s == 20.0
    assert adapter.submitted == (TASK.brief, TASK.attachments)


def test_atelier_arm_without_text_or_in_time_fails():
    clock = FakeClock()
    with pytest.raises(RuntimeError, match="ohne Text"):
        AtelierMcpArm(FakeAdapter([AtelierRunState("Failed")], None), sleep=clock.sleep, clock=clock).produce(TASK)

    clock = FakeClock()
    endless = FakeAdapter([AtelierRunState("Running")] * 100, "x")
    with pytest.raises(TimeoutError):
        AtelierMcpArm(endless, poll_interval_s=10, timeout_s=30, sleep=clock.sleep, clock=clock).produce(TASK)


class FakeTransport:
    def __init__(self, replies):
        self.replies = list(replies)
        self.requests = []

    def __call__(self, url, headers, body, timeout_s):
        self.requests.append({"url": url, "headers": dict(headers), "body": json.loads(body)})
        return self.replies.pop(0)


def sse(result, msg_id=1):
    return "event: message\ndata: " + json.dumps({"jsonrpc": "2.0", "id": msg_id, "result": result}) + "\n\n"


def tool_text(payload):
    return {"content": [{"type": "text", "text": json.dumps(payload)}]}


def test_mcp_client_initializes_keeps_session_and_parses_sse():
    transport = FakeTransport([
        HttpReply(200, {"mcp-session-id": "abc"}, sse({"protocolVersion": "2025-06-18"})),
        HttpReply(202, {}, ""),
        HttpReply(200, {}, sse(tool_text({"runId": "r-9", "status": "Pending"}), 2)),
    ])
    client = McpClient("https://atelier.example/", "TOKEN", transport=transport)

    reply = client.call_tool("submit_request", {"briefingText": "x"})

    assert reply == {"runId": "r-9", "status": "Pending"}
    assert [r["body"].get("method") for r in transport.requests] == ["initialize", "notifications/initialized", "tools/call"]
    assert transport.requests[0]["url"] == "https://atelier.example/mcp"
    assert transport.requests[2]["headers"]["Mcp-Session-Id"] == "abc"
    assert transport.requests[2]["headers"]["Authorization"] == "Bearer TOKEN"


def test_mcp_client_reports_401_and_tool_errors():
    with pytest.raises(McpError, match="401"):
        McpClient("https://a", "bad", transport=FakeTransport([HttpReply(401, {}, "")])).call_tool("x", {})

    transport = FakeTransport([
        HttpReply(200, {}, '{"jsonrpc":"2.0","id":1,"result":{}}'),
        HttpReply(202, {}, ""),
        HttpReply(200, {}, sse({"isError": True, "content": [{"type": "text", "text": "kaputt"}]}, 2)),
    ])
    with pytest.raises(McpError, match="kaputt"):
        McpClient("https://a", "t", transport=transport).call_tool("x", {})


def test_parse_jsonrpc_reply_rejects_bodies_without_result():
    with pytest.raises(McpError):
        parse_jsonrpc_reply("event: ping\n\n")


class RecordingClient:
    def __init__(self, replies):
        self.replies = replies
        self.calls = []

    def call_tool(self, name, arguments):
        self.calls.append((name, arguments))
        return self.replies[name]


def test_adapter_encodes_attachments_and_allows_renamed_tools():
    client = RecordingClient({
        "submit_order": {"runId": "o-1"},
        "get_order_status": {"status": "Completed", "errorMessage": None},
        "get_run_result": {"finalText": "Text"},
    })
    adapter = AtelierToolAdapter(client, tool_names={"submit": "submit_order", "status": "get_order_status"},
                                 crew_template="klassik")

    run_id = adapter.submit("Auftrag", TASK.attachments)
    state = adapter.state(run_id)
    text = adapter.result_text(run_id)

    name, arguments = client.calls[0]
    assert (name, run_id, state.status, text) == ("submit_order", "o-1", "Completed", "Text")
    assert arguments["crewTemplate"] == "klassik"
    sent = json.loads(arguments["attachments"])[0]
    assert sent["filename"] == "eckdaten.md"
    assert base64.b64decode(sent["contentBase64"]).decode("utf-8") == "Samstag 15 Uhr"
