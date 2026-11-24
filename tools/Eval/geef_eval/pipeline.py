"""Produces arm outputs, resumable: successful outputs on disk are never generated again."""
from __future__ import annotations

import concurrent.futures as cf
from dataclasses import asdict
from typing import Callable

from .arms import Arm
from .cli_runner import LimitError
from .metrics import measure
from .store import RunStore
from .tasks import Task


def produce_outputs(tasks: list[Task], arms: list[Arm], store: RunStore, workers: int = 2,
                    echo: Callable[[str], None] = print) -> dict:
    """Runs every (task, arm) without a successful output; failures are stored and retried on the next call."""
    jobs = [
        (task, arm) for task in tasks for arm in arms
        if not (store.read_json(store.output_path(task.id, arm.name)) or {}).get("ok")
    ]
    done, failed, stopped = 0, 0, False
    with cf.ThreadPoolExecutor(max_workers=max(1, min(workers, 2))) as pool:
        futures = {pool.submit(_produce_one, task, arm, store): (task, arm) for task, arm in jobs}
        for future in cf.as_completed(futures):
            task, arm = futures[future]
            try:
                future.result()
                done += 1
                echo(f"Text fertig: {task.id} / {arm.name}")
            except LimitError as exc:
                failed += 1
                stopped = True
                echo(f"LIMIT erreicht, Erzeugung angehalten: {exc}")
                for other in futures:
                    other.cancel()
            except Exception as exc:
                failed += 1
                echo(f"Text fehlgeschlagen: {task.id} / {arm.name}: {exc}")
    return {"done": done, "failed": failed, "skipped_existing": len(tasks) * len(arms) - len(jobs),
            "stopped_by_limit": stopped}


def _produce_one(task: Task, arm: Arm, store: RunStore) -> None:
    path = store.output_path(task.id, arm.name)
    try:
        output = arm.produce(task)
    except LimitError:
        raise
    except Exception as exc:
        store.write_json(path, {"ok": False, "error": str(exc)[:500], "arm": arm.name, "task": task.id})
        raise
    metrics = measure(output.text, task)
    store.write_json(path, {
        "ok": True,
        "arm": arm.name,
        "task": task.id,
        "text": output.text,
        "duration_s": output.duration_s,
        "meta": output.meta,
        "metrics": {
            "words": metrics.words,
            "placeholders": metrics.placeholders,
            "length_rule": asdict(metrics.length_rule) if metrics.length_rule else None,
            "length_ok": metrics.length_ok,
        },
    })
