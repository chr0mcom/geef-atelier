"""Run directory: resumable intermediate results, written atomically."""
from __future__ import annotations

import json
import os
import re
import tempfile
import threading
from pathlib import Path

SAFE_RE = re.compile(r"[^A-Za-z0-9._-]+")


def safe_name(value: str) -> str:
    """Turns an id into a file-system-safe name."""
    return SAFE_RE.sub("_", value).strip("_") or "_"


class RunStore:
    """All files of one evaluation run (``<out>/<run name>/``)."""

    def __init__(self, root: str | Path):
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)
        self._lock = threading.Lock()

    def output_path(self, task_id: str, arm: str) -> Path:
        return self.root / "outputs" / safe_name(task_id) / f"{safe_name(arm)}.json"

    def judgment_path(self, task_id: str, pair: str, judge: str, run: int) -> Path:
        return self.root / "judgments" / safe_name(task_id) / safe_name(pair) / f"{safe_name(judge)}_pass{run}.json"

    def assignment_path(self, task_id: str, pair: str, judge: str) -> Path:
        return self.root / "assignments" / safe_name(task_id) / f"{safe_name(pair)}__{safe_name(judge)}.json"

    def counter_path(self, name: str) -> Path:
        return self.root / f"{safe_name(name)}.count"

    @property
    def calls_log(self) -> Path:
        return self.root / "calls.jsonl"

    @property
    def report_path(self) -> Path:
        return self.root / "bericht.md"

    @property
    def manifest_path(self) -> Path:
        return self.root / "manifest.json"

    def read_json(self, path: Path) -> dict | None:
        if not path.is_file():
            return None
        return json.loads(path.read_text(encoding="utf-8"))

    def write_json(self, path: Path, payload: dict) -> None:
        self.write_text(path, json.dumps(payload, ensure_ascii=False, indent=2))

    def write_text(self, path: Path, text: str) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        fd, tmp = tempfile.mkstemp(dir=path.parent, prefix=".tmp-", suffix=path.suffix)
        try:
            with os.fdopen(fd, "w", encoding="utf-8") as handle:
                handle.write(text)
            os.replace(tmp, path)
        except BaseException:
            if os.path.exists(tmp):
                os.remove(tmp)
            raise

    def log_call(self, record: dict) -> None:
        with self._lock:
            self.calls_log.parent.mkdir(parents=True, exist_ok=True)
            with self.calls_log.open("a", encoding="utf-8") as handle:
                handle.write(json.dumps(record, ensure_ascii=False) + "\n")

    def read_counter(self, name: str) -> int:
        path = self.counter_path(name)
        try:
            return int(path.read_text(encoding="utf-8").strip())
        except (FileNotFoundError, ValueError):
            return 0

    def try_reserve(self, name: str, cap: int) -> bool:
        """Atomically increments a counter unless it already reached ``cap``."""
        with self._lock:
            value = self.read_counter(name)
            if value >= cap:
                return False
            self.write_text(self.counter_path(name), str(value + 1))
            return True

    def increment_counter(self, name: str) -> int:
        with self._lock:
            value = self.read_counter(name) + 1
            self.write_text(self.counter_path(name), str(value))
            return value
