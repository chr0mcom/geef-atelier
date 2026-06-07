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
