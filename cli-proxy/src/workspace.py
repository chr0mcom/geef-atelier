"""Ephemeral per-call workspace for document-mode CLI editing.

Each call that uses document mode gets an isolated directory under WORKSPACE_ROOT
(/work/<uuid>/) with a single 'draft.md' containing the current document. The CLI
agent edits that file in place; the caller reads it back after the subprocess exits.

Security note: the workspace directory is temporary and isolated per call, but
Docker container isolation is the primary security boundary. The CLI tools
(Read, Edit, Write) can theoretically use relative paths to escape the workspace
directory — this is an accepted risk given the container isolation.
"""
from __future__ import annotations

import os
import shutil
import uuid
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from pathlib import Path

WORKSPACE_ROOT: str = os.getenv("WORKSPACE_ROOT", "/work")

# Validate at import time so a misconfigured container fails loudly on startup,
# not silently at runtime when a document-mode call arrives.
if not Path(WORKSPACE_ROOT).is_absolute():
    raise ValueError(
        f"WORKSPACE_ROOT must be an absolute path, got: {WORKSPACE_ROOT!r}. "
        "Set WORKSPACE_ROOT in the environment (default: /work)."
    )

# Background context whose UTF-8 byte length exceeds this is offloaded to context.md instead
# of being inlined into the argv instruction. Kept well below the Linux per-argument limit
# (MAX_ARG_STRLEN = 128 KB) to leave headroom for the file-contract preamble, briefing and
# findings that always stay in the prompt, plus UTF-8 multi-byte expansion.
CONTEXT_FILE_THRESHOLD: int = int(os.getenv("CONTEXT_FILE_THRESHOLD", "50000"))

# Hard safety net: a single execve argument may not exceed MAX_ARG_STRLEN (128 KB on Linux).
# The whole instruction (file-contract + findings + any inlined context) is one argv element,
# so if it gets close to that limit it is offloaded to instruction.md and replaced by a short
# pointer — otherwise the spawn fails with OSError "Argument list too long" (E2BIG). Normal
# instructions stay in argv so the findings remain prominent; only pathological inputs offload.
INSTRUCTION_ARG_LIMIT: int = int(os.getenv("INSTRUCTION_ARG_LIMIT", "100000"))


def materialize_context(workspace_path: Path, context_document: str | None) -> str:
    """
    Decides how background context (grounding + advisor notes) reaches the CLI agent and
    returns the text to prepend to the instruction:

    - None / empty  → "" (no context).
    - <= threshold  → the context itself (inlined into the prompt, as before).
    - >  threshold  → writes context.md into the workspace and returns a short pointer line,
                      keeping the argv instruction small (avoids MAX_ARG_STRLEN).
    """
    if not context_document:
        return ""
    # Measure in UTF-8 bytes, not characters: execve enforces MAX_ARG_STRLEN on the encoded
    # argument, so multi-byte text (CJK, emoji) must be weighed by its byte length.
    if len(context_document.encode("utf-8")) <= CONTEXT_FILE_THRESHOLD:
        return context_document + "\n\n"
    (workspace_path / "context.md").write_text(context_document, encoding="utf-8")
    return (
        "Background research and advisor notes for this task are in context.md in the "
        "current directory. Read context.md first, then proceed.\n\n"
    )


def finalize_instruction(workspace_path: Path, instruction: str) -> str:
    """
    Returns the string to pass as the trailing CLI argument. If the instruction's UTF-8 byte
    length exceeds the safe per-argument size it is written to instruction.md and a short
    pointer is returned instead, preventing E2BIG (OSError "Argument list too long").
    """
    if len(instruction.encode("utf-8")) <= INSTRUCTION_ARG_LIMIT:
        return instruction
    (workspace_path / "instruction.md").write_text(instruction, encoding="utf-8")
    return (
        "Your task is described in instruction.md in the current directory. Read "
        "instruction.md first, then edit draft.md exactly as instructed and write the "
        "complete updated document back to draft.md. Do NOT output the document to stdout."
    )


@asynccontextmanager
async def ephemeral_workspace(document: str) -> AsyncIterator[Path]:
    """
    Creates a temporary workspace directory, writes 'draft.md', yields the path,
    then cleans up unconditionally in the finally block.

    Args:
        document: Initial document content written to draft.md. May be empty (iter 1).

    Yields:
        Path to the workspace directory (contains 'draft.md').
    """
    workspace = Path(WORKSPACE_ROOT) / str(uuid.uuid4())
    workspace.mkdir(parents=True, exist_ok=True)
    draft = workspace / "draft.md"
    draft.write_text(document, encoding="utf-8")
    try:
        yield workspace
    finally:
        shutil.rmtree(workspace, ignore_errors=True)
