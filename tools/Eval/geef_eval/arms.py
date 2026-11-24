"""Arms: the systems under comparison. Built-ins plus plugins loaded from ``module:attribute`` or ``file.py:attribute``."""
from __future__ import annotations

import importlib
import importlib.util
import os
import time
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Protocol

from .claude_call import call_claude
from .cli_runner import Runner, run_cli
from .mcp import TERMINAL_STATUSES, AtelierToolAdapter, McpClient
from .tasks import Task

WRITER_SYSTEM_PROMPT = (
    "Du bist ein erfahrener, sorgfältiger Textautor. Du hast keine Werkzeuge und kannst keine "
    "Rückfragen stellen. Liefere ausschließlich das, was verlangt ist."
)


@dataclass(frozen=True)
class ArmOutput:
    """What an arm produced for one task."""

    text: str
    duration_s: float
    meta: dict = field(default_factory=dict)


class Arm(Protocol):
    """A system that turns a task into a text."""

    name: str

    def produce(self, task: Task) -> ArmOutput:
        ...


def attachment_blocks(task: Task) -> str:
    """Text attachments inline as tagged blocks; binary attachments only as a notice."""
    blocks = []
    for attachment in task.attachments:
        if attachment.text is not None:
            blocks.append(f'<anhang name="{attachment.name}">\n{attachment.text}\n</anhang>')
        else:
            blocks.append(f'<anhang name="{attachment.name}">\n(Binärdatei, in diesem Weg nicht lesbar)\n</anhang>')
    return "\n\n".join(blocks) + "\n\n" if blocks else ""


def raw_prompt(task: Task) -> str:
    """The prompt of the raw single call: attachments, short brief, output rule."""
    return (
        attachment_blocks(task) + "AUFTRAG:\n" + task.brief
        + "\n\nGib ausschließlich den fertigen Text aus, ohne Vorrede, ohne Erläuterungen und ohne Anmerkungen zum Text."
    )


class RawClaudeArm:
    """Arm (a): one ``claude -p`` call, prompt via stdin, empty working directory, no tools."""

    def __init__(self, name: str = "roh", model: str = "opus", timeout_s: float = 900.0,
                 runner: Runner = run_cli, log: Callable[[dict], None] | None = None):
        self.name = name
        self.model = model
        self.timeout_s = timeout_s
        self.runner = runner
        self.log = log

    def produce(self, task: Task) -> ArmOutput:
        reply = call_claude(
            raw_prompt(task), model=self.model, system_prompt=WRITER_SYSTEM_PROMPT, timeout_s=self.timeout_s,
            runner=self.runner, log=self.log, label=f"arm:{self.name}:{task.id}",
        )
        return ArmOutput(reply.text.strip(), reply.duration_s, {
            "model": self.model, "tokens_in": reply.tokens_in, "tokens_out": reply.tokens_out, "attempts": reply.attempts,
        })


class ReferenceArm:
    """Returns the ``referenztext`` stored in the task; useful as a fixed baseline and for smoke runs."""

    def __init__(self, name: str = "referenz"):
        self.name = name

    def produce(self, task: Task) -> ArmOutput:
        if not task.reference_text:
            raise ValueError(f"Aufgabe {task.id} hat keinen referenztext")
        return ArmOutput(task.reference_text, 0.0, {"source": "task"})


class AtelierMcpArm:
    """Arm (b): the running Atelier, driven through its MCP endpoint (submit, poll, fetch result)."""

    def __init__(self, adapter: AtelierToolAdapter, name: str = "atelier", poll_interval_s: float = 15.0,
                 timeout_s: float = 3600.0, sleep: Callable[[float], None] = time.sleep,
                 clock: Callable[[], float] = time.monotonic):
        self.name = name
        self.adapter = adapter
        self.poll_interval_s = poll_interval_s
        self.timeout_s = timeout_s
        self.sleep = sleep
        self.clock = clock

    def produce(self, task: Task) -> ArmOutput:
        started = self.clock()
        run_id = self.adapter.submit(task.brief, task.attachments)
        state = self.adapter.state(run_id)
        while state.status not in TERMINAL_STATUSES:
            if self.clock() - started > self.timeout_s:
                raise TimeoutError(f"Atelier-Lauf {run_id} nach {self.timeout_s:.0f} s nicht fertig (Status {state.status})")
            self.sleep(self.poll_interval_s)
            state = self.adapter.state(run_id)
        text = self.adapter.result_text(run_id)
        duration = round(self.clock() - started, 1)
        if not text:
            raise RuntimeError(f"Atelier-Lauf {run_id} endete mit {state.status} ohne Text ({state.error or 'kein Fehlertext'})")
        return ArmOutput(text.strip(), duration, {"run_id": run_id, "status": state.status, "error": state.error})


def atelier_arm_from_env(name: str = "atelier") -> AtelierMcpArm:
    """Builds arm (b) from EVAL_ATELIER_URL and EVAL_ATELIER_TOKEN (optional EVAL_ATELIER_CREW)."""
    url = os.environ.get("EVAL_ATELIER_URL")
    token = os.environ.get("EVAL_ATELIER_TOKEN")
    if not url or not token:
        raise RuntimeError("Arm 'atelier' braucht EVAL_ATELIER_URL und EVAL_ATELIER_TOKEN")
    adapter = AtelierToolAdapter(McpClient(url, token), crew_template=os.environ.get("EVAL_ATELIER_CREW") or None)
    return AtelierMcpArm(adapter, name=name)


def load_plugin(spec: str) -> Callable[..., Arm]:
    """Loads ``module:attr`` or ``path/to/file.py:attr``; the attribute is an Arm factory (class or function)."""
    target, sep, attribute = spec.rpartition(":")
    if not sep or not target or not attribute:
        raise ValueError(f"Plugin-Angabe {spec!r}: erwartet 'modul:attribut' oder 'datei.py:attribut'")
    if target.endswith(".py"):
        path = Path(target).resolve()
        module_spec = importlib.util.spec_from_file_location(f"geef_eval_plugin_{path.stem}", path)
        if module_spec is None or module_spec.loader is None:
            raise ValueError(f"Plugin-Datei nicht ladbar: {path}")
        module = importlib.util.module_from_spec(module_spec)
        module_spec.loader.exec_module(module)
    else:
        module = importlib.import_module(target)
    factory = getattr(module, attribute, None)
    if factory is None:
        raise ValueError(f"Plugin {spec!r}: Attribut {attribute!r} fehlt")
    return factory


def build_arms(names: list[str], plugins: dict[str, str], runner: Runner = run_cli,
               log: Callable[[dict], None] | None = None) -> list[Arm]:
    """Resolves arm names: ``roh``, ``roh-sonnet``, ``referenz``, ``atelier`` or a name defined via ``plugins``."""
    arms: list[Arm] = []
    for name in names:
        if name in plugins:
            arm = load_plugin(plugins[name])(name=name)
        elif name == "roh":
            arm = RawClaudeArm(name="roh", model="opus", runner=runner, log=log)
        elif name == "roh-sonnet":
            arm = RawClaudeArm(name="roh-sonnet", model="sonnet", runner=runner, log=log)
        elif name == "referenz":
            arm = ReferenceArm()
        elif name == "atelier":
            arm = atelier_arm_from_env()
        else:
            raise ValueError(f"Unbekannter Arm {name!r} (eingebaut: roh, roh-sonnet, referenz, atelier; sonst --plugin name=modul:attribut)")
        arms.append(arm)
    if len({a.name for a in arms}) != len(arms):
        raise ValueError("Arm-Namen müssen eindeutig sein")
    return arms
