"""A stand-in for the claude, codex and gemini binaries that behaves like a CLI with helpers.

Each run records its pid and starts a grandchild (``sleep``) that inherits the output pipes,
like the node workers and tool processes of the real CLIs. Tests use it to prove that the
gateway ends the whole process group, not just the CLI.

Control: ``FAKE_TOKEN``/``FAKE_MODE`` in the environment (in-process adapter tests), otherwise
a marker ``<MODE>-MARK:<token>`` in the arguments or in stdin (tests against a real uvicorn,
where the prompt reaches the CLI that way). Without a marker the run exits at once with
code 3, so the server's start-up probes stay quick and record nothing.

Modes: ``hang`` (never answers), ``slow`` (answers after a pause), ``flood`` (streams far more
than the socket buffers hold, then hangs), ``orphan`` (the CLI exits with code 0 while its
grandchild keeps stdout and stderr open), ``orphanerr`` (the CLI closes stdout, exits a moment
later, and its grandchild keeps only stderr open).

Modes with a lot of stderr (no grandchild): ``noisybefore`` / ``noisybetween`` write far more
than the pipe and stream buffers hold to stderr, before the first event on stdout or between
two events, then answer and exit with code 0; ``noisyfail`` does the same and exits with code 7.
Its stderr starts with ``STDERR-START``; its last 65536 bytes are a line ``CUT-LINE``, a line
``TAIL-HEAD the real cause`` and filler lines. ``errhelper``: the CLI answers and exits with
code 0 while its grandchild keeps only stderr open.
"""
from __future__ import annotations

import os
import stat
import time
from pathlib import Path

SCRIPT = r"""#!/bin/bash
if [ -n "$FAKE_TOKEN" ]; then
  token="$FAKE_TOKEN"; mode="$FAKE_MODE"
else
  input="$* $(cat)"
  case "$input" in
    *HANG-MARK:*) mode=hang ;;
    *SLOW-MARK:*) mode=slow ;;
    *FLOOD-MARK:*) mode=flood ;;
    *ORPHAN-MARK:*) mode=orphan ;;
    *NOISYBEFORE-MARK:*) mode=noisybefore ;;
    *NOISYBETWEEN-MARK:*) mode=noisybetween ;;
    *NOISYFAIL-MARK:*) mode=noisyfail ;;
    *) exit 3 ;;
  esac
  token=$(printf '%s' "$input" | grep -o -E '(HANG|SLOW|FLOOD|ORPHAN|NOISYBEFORE|NOISYBETWEEN|NOISYFAIL)-MARK:[A-Za-z0-9_-]+' | head -1 | cut -d: -f2)
fi
echo "$$" >> "$FAKE_DIR/$token.starts"
if [ "$mode" = slow ]; then
  sleep "${FAKE_SLOW_SECONDS:-3}"
  echo '{"type":"result","subtype":"success","is_error":false,"result":"pong","usage":{"input_tokens":1,"output_tokens":1}}'
  exit 0
fi
event() {
  printf '{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"%s"}},"item":{"type":"agent_message","text":"%s"}}\n' "$1" "$2"
}
answer_end() {
  event "done" "pong done"
  echo '{"type":"result","subtype":"success","is_error":false,"result":"pong done","usage":{"input_tokens":3,"output_tokens":2}}'
}
noise() {
  yes 'noise on stderr' | head -c 2000000 >&2
}
if [ "$mode" = noisybefore ]; then
  noise
  event "pong " "pong "
  answer_end
  exit 0
fi
if [ "$mode" = noisybetween ]; then
  event "pong " "pong "
  noise
  answer_end
  exit 0
fi
if [ "$mode" = noisyfail ]; then
  echo 'STDERR-START' >&2
  noise
  head_lines=$'CUT-LINE\nTAIL-HEAD the real cause\n'
  printf '%s' "$head_lines" >&2
  yes '.' | head -c $((65536 - ${#head_lines})) >&2
  exit 7
fi
if [ "$mode" = errhelper ]; then
  # uvloop also hands the CLI duplicates of its output sockets at higher numbers: the helper
  # closes them, so that stderr (fd 2) is really the only output it holds.
  ( for ((n = 3; n < 64; n++)); do eval "exec $n>&-"; done; exec sleep 300 > /dev/null ) &
  echo "$!" > "$FAKE_DIR/$token.grandchild.$$"
  event "pong " "pong "
  answer_end
  exit 0
fi
if [ "$mode" = orphanerr ]; then
  sleep 300 > /dev/null &
  echo "$!" > "$FAKE_DIR/$token.grandchild.$$"
  exec 1>&-
  sleep 0.5
  exit 0
fi
sleep 300 &
echo "$!" > "$FAKE_DIR/$token.grandchild.$$"
if [ "$mode" = orphan ]; then
  exit 0
fi
if [ "$mode" = flood ]; then
  chunk=$(head -c 16000 /dev/zero | tr '\0' x)
  for ((i = 0; i < 2000; i++)); do
    printf '{"type":"stream_event","event":{"type":"content_block_delta","delta":{"type":"text_delta","text":"%s"}},"item":{"type":"agent_message","text":"%s %s"}}\n' "$chunk" "$i" "$chunk"
  done
fi
wait
"""


def install(bin_dir: Path) -> Path:
    """Write the fake as ``claude``, ``codex``, ``gemini`` and ``fakecli`` into `bin_dir`."""
    bin_dir.mkdir(parents=True, exist_ok=True)
    main = bin_dir / "fakecli"
    main.write_text(SCRIPT, encoding="utf-8")
    main.chmod(main.stat().st_mode | stat.S_IXUSR | stat.S_IXGRP | stat.S_IXOTH)
    for name in ("claude", "codex", "gemini"):
        link = bin_dir / name
        if not link.exists():
            link.symlink_to(main)
    return main


def alive(pid: int) -> bool:
    """True while `pid` runs; a zombie (killed, not yet reaped by its new parent) counts as gone."""
    try:
        state = Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()[0]
    except (FileNotFoundError, ProcessLookupError, IndexError):
        return False
    return state not in ("Z", "X")


def starts(fake_dir: Path, token: str) -> list[int]:
    path = fake_dir / f"{token}.starts"
    return [int(line) for line in path.read_text().split()] if path.exists() else []


def grandchildren(fake_dir: Path, token: str) -> list[int]:
    return [int(p.read_text().strip()) for p in fake_dir.glob(f"{token}.grandchild.*") if p.read_text().strip()]


def wait_for(condition, seconds: float = 10.0, step: float = 0.05) -> bool:
    deadline = time.monotonic() + seconds
    while time.monotonic() < deadline:
        if condition():
            return True
        time.sleep(step)
    return condition()


def _ours(pid: int) -> bool:
    try:
        cmdline = Path(f"/proc/{pid}/cmdline").read_bytes()
    except OSError:
        return False
    return alive(pid) and (b"sleep" in cmdline or b"fakecli" in cmdline or b"bash" in cmdline)


def kill_leftovers(fake_dir: Path) -> None:
    """Safety net for a failing test: never leave a ``sleep 300`` behind (only pids that are
    still one of ours)."""
    pids = [int(p.read_text().strip() or 0) for p in fake_dir.glob("*.grandchild.*")]
    for path in fake_dir.glob("*.starts"):
        pids += [int(line) for line in path.read_text().split()]
    for pid in pids:
        if pid > 0 and _ours(pid):
            try:
                os.kill(pid, 9)
            except (ProcessLookupError, PermissionError):
                pass
