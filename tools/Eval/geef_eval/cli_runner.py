"""Runs CLI calls (claude, optionally codex) with stdin prompt, timeout and a global concurrency cap."""
from __future__ import annotations

import os
import re
import signal
import subprocess
import tempfile
import threading
import time
from dataclasses import dataclass
from typing import Callable

MAX_PARALLEL_CLI_CALLS = 2
CLI_SLOTS = threading.BoundedSemaphore(MAX_PARALLEL_CLI_CALLS)
LIMIT_RE = re.compile(
    r"(usage limit|rate limit|limit reached|hit your limit|quota|you've hit|exceeded your)", re.IGNORECASE
)


class LimitError(RuntimeError):
    """The subscription reported a usage limit; the run should stop instead of hammering it."""


@dataclass(frozen=True)
class CliResult:
    """Outcome of one CLI process."""

    stdout: str
    stderr: str
    returncode: int
    timed_out: bool
    duration_s: float


Runner = Callable[[list[str], str, str, float], CliResult]


def run_cli(cmd: list[str], prompt: str, cwd: str, timeout_s: float) -> CliResult:
    """Starts ``cmd`` in its own process group, feeds ``prompt`` via stdin and kills the group on timeout."""
    started = time.monotonic()
    with CLI_SLOTS:
        proc = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            cwd=cwd,
            text=True,
            start_new_session=True,
        )
        try:
            out, err = proc.communicate(prompt, timeout=timeout_s)
            return CliResult(out, err, proc.returncode, False, time.monotonic() - started)
        except subprocess.TimeoutExpired:
            _kill_group(proc, signal.SIGTERM)
            try:
                out, err = proc.communicate(timeout=20)
            except subprocess.TimeoutExpired:
                _kill_group(proc, signal.SIGKILL)
                out, err = proc.communicate()
            return CliResult(out or "", err or "", -9, True, time.monotonic() - started)


def _kill_group(proc: subprocess.Popen, sig: int) -> None:
    try:
        os.killpg(proc.pid, sig)
    except ProcessLookupError:
        pass


def empty_workdir(prefix: str) -> str:
    """Creates an empty temporary working directory so the CLI sees no project files."""
    return tempfile.mkdtemp(prefix=f"geef-eval-{prefix}-")


def claude_command(model: str, system_prompt: str) -> list[str]:
    """The tool-less, settings-free ``claude -p`` invocation used by every claude call of this tool."""
    return [
        "claude", "-p",
        "--model", model,
        "--tools", "",
        "--strict-mcp-config",
        "--disable-slash-commands",
        "--no-session-persistence",
        "--system-prompt", system_prompt,
        "--output-format", "json",
    ]
