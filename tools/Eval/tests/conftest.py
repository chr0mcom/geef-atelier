import json
import sys
from pathlib import Path

import pytest

EVAL_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(EVAL_ROOT))

from geef_eval.cli_runner import CliResult  # noqa: E402

EXAMPLES = EVAL_ROOT / "beispiele"


class FakeRunner:
    """Records CLI invocations and answers from a queue of (result_text | CliResult) items."""

    def __init__(self, replies=None):
        self.replies = list(replies or [])
        self.calls = []

    def __call__(self, cmd, prompt, cwd, timeout_s):
        self.calls.append({"cmd": cmd, "prompt": prompt, "cwd": cwd, "timeout_s": timeout_s,
                           "cwd_empty": Path(cwd).is_dir() and not any(Path(cwd).iterdir())})
        reply = self.replies.pop(0) if self.replies else "ok"
        if isinstance(reply, CliResult):
            return reply
        if callable(reply):
            return reply(cmd, prompt, cwd)
        return claude_json(reply)


def claude_json(text, is_error=False, tokens_in=10, tokens_out=5):
    payload = {"type": "result", "result": text, "is_error": is_error,
               "usage": {"input_tokens": tokens_in, "output_tokens": tokens_out}}
    return CliResult(json.dumps(payload), "", 0, False, 1.5)


def verdict_json(first="1", margin="klar", fabricated=None, violations=None, reason="kurz"):
    second = "2" if first == "1" else "1"
    return json.dumps({
        "rangfolge": [first, second],
        "abstand": margin,
        "verstoesse": violations or {"1": [], "2": []},
        "erfunden": fabricated or {"1": [], "2": []},
        "begruendung": reason,
    })


@pytest.fixture
def examples_dir():
    return EXAMPLES
