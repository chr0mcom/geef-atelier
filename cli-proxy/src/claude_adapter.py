"""Adapter that runs the claude CLI and returns its text output."""
from __future__ import annotations

import asyncio
import json
import logging
import os
from pathlib import Path

from workspace import finalize_instruction, materialize_context

log = logging.getLogger(__name__)

MAX_CONCURRENT = int(os.getenv("CLAUDE_MAX_CONCURRENT", "2"))
_semaphore = asyncio.Semaphore(MAX_CONCURRENT)

# Home directory for claude auth — mounted as a volume in the container.
CLAUDE_HOME = os.getenv("CLAUDE_HOME", "/auth/claude")

# Hard cap on how long a single claude CLI call may run.
# Increase for workloads that produce very long outputs (e.g. 30-page documents).
CLI_TIMEOUT_SECONDS = int(os.getenv("CLI_TIMEOUT_SECONDS", "1800"))

# Static model list — the claude CLI has no model-listing command.
# Update this list when new Claude models become available.
STATIC_MODELS = [
    "claude-opus-4-7",
    "claude-sonnet-4-6",
    "claude-haiku-4-5",
]


def list_models() -> list[str]:
    """Returns the static list of supported Claude models."""
    return list(STATIC_MODELS)


async def complete_document(
    system_prompt: str,
    user_instruction: str,
    document: str,
    workspace_path: Path,
    model: str | None,
    max_tokens: int | None,
    context_document: str | None = None,
) -> str:
    """
    Calls the claude CLI in document-edit mode: the CLI reads draft.md in the given
    workspace, applies the instruction, and writes the revised document back.
    Returns the content of draft.md after the CLI exits.

    Uses --allowedTools Read,Edit,Write with --permission-mode acceptEdits so the
    agent can edit files without interactive confirmation prompts.

    context_document holds large background context (grounding + advisor notes); it is
    offloaded to context.md when oversized (see workspace.materialize_context).
    """
    async with _semaphore:
        return await _run_claude_document(
            system_prompt, user_instruction, document, workspace_path, model, max_tokens,
            context_document,
        )


async def complete(prompt: str, model: str | None, max_tokens: int | None) -> str:
    """
    Calls the claude CLI in print mode and returns the raw text output.

    The semaphore limits concurrent calls to respect subscription rate limits.
    """
    async with _semaphore:
        return await _run_claude(prompt, model, max_tokens)


async def _run_claude_document(
    system_prompt: str,
    user_instruction: str,
    document: str,
    workspace_path: Path,
    model: str | None,
    max_tokens: int | None,
    context_document: str | None = None,
) -> str:
    # Large background context goes to context.md (pointer) or inline if small; the file-contract
    # and user instruction always stay in the prompt.
    context_preamble = materialize_context(workspace_path, context_document)
    instruction = (
        context_preamble
        + "The document you are editing is located at draft.md in the current directory. "
        "Read it, apply the revisions described below, then write the complete updated "
        "document back to draft.md. Do NOT output the document content to stdout — "
        "only edit the file.\n\n"
        + user_instruction
    )

    # --allowedTools Read,Edit,Write: restrict agent to file operations only (no web, no bash).
    # --permission-mode acceptEdits: auto-approve file edits without interactive prompts.
    # --append-system-prompt: injects the writer persona system prompt. Note: this is the one
    # remaining argv value not offloaded to a file; system prompts are operator-defined profiles
    # (a few KB), so they stay well under MAX_ARG_STRLEN. The user-driven variable content
    # (document, context, findings) is all file-backed via draft.md/context.md/instruction.md.
    # No --output-format json: stdout is ignored; we read draft.md directly.
    args = [
        "claude", "-p",
        "--allowedTools", "Read,Edit,Write",
        "--permission-mode", "acceptEdits",
        "--append-system-prompt", system_prompt,
    ]

    if model:
        bare_model = model.split("/")[-1] if "/" in model else model
        bare_model = bare_model.replace(".", "-")
        args += ["--model", bare_model]

    # Offload to instruction.md if the instruction would exceed the per-argument OS limit.
    args.append(finalize_instruction(workspace_path, instruction))

    env = {**os.environ, "HOME": CLAUDE_HOME}

    proc = await asyncio.create_subprocess_exec(
        *args,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=str(workspace_path),
        env=env,
    )
    try:
        stdout, stderr = await asyncio.wait_for(proc.communicate(), timeout=CLI_TIMEOUT_SECONDS)
    except asyncio.TimeoutError:
        proc.kill()
        await proc.wait()  # reap the killed process so it does not linger as a zombie
        raise RuntimeError(f"claude CLI (document mode) timed out after {CLI_TIMEOUT_SECONDS}s")

    if proc.returncode != 0:
        stderr_msg = stderr.decode(errors="replace").strip()
        raise RuntimeError(
            f"claude CLI (document mode) exited with code {proc.returncode}: "
            f"{stderr_msg or stdout.decode(errors='replace')[:200]}"
        )

    draft_path = workspace_path / "draft.md"
    try:
        result = draft_path.read_text(encoding="utf-8")
    except FileNotFoundError:
        raise RuntimeError(
            "claude document mode: draft.md not found after CLI exit — "
            "the agent may have deleted or moved the file"
        )

    if result == document:
        log.warning("claude document mode: draft.md unchanged after CLI run — no edits applied")

    return result


async def _run_claude(prompt: str, model: str | None, max_tokens: int | None) -> str:
    # Allowlist ONLY web tools — no Bash/Edit/Write, so no full permission bypass.
    # Comma-separated single token: a space-separated variadic would greedily
    # consume the trailing prompt positional.
    args = ["claude", "-p", "--output-format", "json", "--allowedTools", "WebSearch,WebFetch"]

    if model:
        # Strip provider prefix and normalize dots to dashes
        # (e.g. "anthropic/claude-opus-4.7" → "claude-opus-4-7").
        bare_model = model.split("/")[-1] if "/" in model else model
        bare_model = bare_model.replace(".", "-")
        args += ["--model", bare_model]

    # Pass the prompt through stdin, never as an argv element. A single execve argument may
    # not exceed MAX_ARG_STRLEN (128 KB on Linux); reviewer/advisor prompts embed the full
    # draft (often >128 KB by later iterations), so an argv prompt fails the spawn with
    # OSError "Argument list too long" (E2BIG) and the proxy returns HTTP 500. claude -p
    # reads the prompt from stdin when no prompt positional is given.
    env = {**os.environ, "HOME": CLAUDE_HOME}

    proc = await asyncio.create_subprocess_exec(
        *args,
        stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        env=env,
    )
    try:
        stdout, stderr = await asyncio.wait_for(
            proc.communicate(input=prompt.encode("utf-8")), timeout=CLI_TIMEOUT_SECONDS)
    except asyncio.TimeoutError:
        proc.kill()
        await proc.wait()  # reap the killed process so it does not linger as a zombie
        raise RuntimeError(f"claude CLI timed out after {CLI_TIMEOUT_SECONDS} seconds")

    raw = stdout.decode(errors="replace").strip()

    if proc.returncode != 0:
        # Errors may appear in stdout JSON (is_error: true) rather than stderr.
        stderr_msg = stderr.decode(errors="replace").strip()
        try:
            data = json.loads(raw)
            if isinstance(data, dict) and data.get("is_error"):
                raise RuntimeError(f"claude CLI error: {data.get('result', stderr_msg)}")
        except (json.JSONDecodeError, ValueError):
            pass
        raise RuntimeError(f"claude CLI exited with code {proc.returncode}: {stderr_msg or raw[:200]}")

    return _extract_result(raw)


def _extract_result(raw: str) -> str:
    """
    claude -p --output-format json outputs a JSON object with a "result" field.
    Falls back to returning the raw string if parsing fails.
    """
    try:
        data = json.loads(raw)
        if isinstance(data, dict) and "result" in data:
            return str(data["result"])
    except (json.JSONDecodeError, ValueError):
        pass
    return raw
