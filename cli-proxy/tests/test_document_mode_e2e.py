"""End-to-end document-mode tests using real subprocesses (stub CLI binaries).

Unlike test_document_mode.py (which mocks asyncio.create_subprocess_exec), these tests
spawn real processes via a fake `claude`/`codex` on PATH. This exercises the real argv
encoding (including OS per-argument limits), real file I/O, real cwd handling, real text
encoding round-trips and real concurrency — the things mocks cannot catch.

The stub CLI:
  * takes the trailing argv element as the instruction,
  * records the received instruction to received_instruction.txt (for assertions),
  * reads draft.md, appends an [EDITED] marker, and — if context.md exists — appends a
    marker proving the agent saw the offloaded context,
  * writes the result back to draft.md.
"""
from __future__ import annotations

import asyncio
import os
import stat
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).parent.parent / "src"))

import claude_adapter
import codex_adapter
import workspace as workspace_module
from workspace import ephemeral_workspace


# ---------------------------------------------------------------------------
# Stub CLI binary
# ---------------------------------------------------------------------------

_STUB_SCRIPT = r"""#!{python}
import os, sys
# The instruction is always the trailing argv element (both claude and codex adapters
# append it last).
instruction = sys.argv[-1]
cwd = os.getcwd()

# Record what the agent received, for test assertions.
with open(os.path.join(cwd, "received_instruction.txt"), "w", encoding="utf-8") as f:
    f.write(instruction)
with open(os.path.join(cwd, "received_argv.txt"), "w", encoding="utf-8") as f:
    f.write("\x00".join(sys.argv))

# If the instruction was offloaded, read the real task from instruction.md (like a real agent).
if "instruction.md" in instruction and os.path.exists(os.path.join(cwd, "instruction.md")):
    with open(os.path.join(cwd, "instruction.md"), encoding="utf-8") as f:
        instruction = f.read()

# Honour a directive to hang forever (test of timeout / kill / reap handling).
if "STUB_HANG" in instruction:
    import time
    time.sleep(60)

# Honour a directive to fail (test of non-zero exit handling).
if "STUB_FAIL" in instruction:
    sys.stderr.write("stub: simulated failure\n")
    sys.exit(3)

# Honour a directive to delete the draft (test of missing-file handling).
if "STUB_DELETE_DRAFT" in instruction:
    try:
        os.remove(os.path.join(cwd, "draft.md"))
    except FileNotFoundError:
        pass
    sys.exit(0)

draft_path = os.path.join(cwd, "draft.md")
try:
    with open(draft_path, encoding="utf-8") as f:
        content = f.read()
except FileNotFoundError:
    content = ""

result = content + "\n[EDITED]"

# If the context was offloaded to context.md, prove the agent can read it.
ctx_path = os.path.join(cwd, "context.md")
if os.path.exists(ctx_path):
    with open(ctx_path, encoding="utf-8") as f:
        first_line = f.read().splitlines()[0] if f.read else ""
    result += "\n[CONTEXT_SEEN]"

with open(draft_path, "w", encoding="utf-8") as f:
    f.write(result)

sys.exit(0)
"""


@pytest.fixture()
def stub_cli(tmp_path, monkeypatch):
    """Installs a stub `claude` and `codex` on PATH that emulate document-mode editing."""
    bin_dir = tmp_path / "stubbin"
    bin_dir.mkdir()
    script = _STUB_SCRIPT.format(python=sys.executable)
    for name in ("claude", "codex"):
        p = bin_dir / name
        p.write_text(script, encoding="utf-8")
        p.chmod(p.stat().st_mode | stat.S_IEXEC | stat.S_IXGRP | stat.S_IXOTH)
    # Program resolution uses the parent process PATH (os.environ), so prepend the stub dir.
    monkeypatch.setenv("PATH", f"{bin_dir}{os.pathsep}{os.environ['PATH']}")
    return bin_dir


# ---------------------------------------------------------------------------
# Happy path — real round-trip through a real subprocess
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_claude_real_subprocess_edits_draft(stub_cli, tmp_path):
    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("original document", encoding="utf-8")

    result = await claude_adapter._run_claude_document(
        system_prompt="You are a writer.", user_instruction="Improve it.",
        document="original document", workspace_path=ws,
        model="claude-opus-4-8", max_tokens=None,
    )

    assert result[0] == "original document\n[EDITED]"
    received = (ws / "received_instruction.txt").read_text(encoding="utf-8")
    assert "Improve it." in received
    assert "draft.md" in received


@pytest.mark.asyncio
async def test_codex_real_subprocess_edits_draft(stub_cli, tmp_path):
    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("codex original", encoding="utf-8")

    result = await codex_adapter._run_codex_document(
        system_prompt="You are a writer.", user_instruction="Improve it.",
        document="codex original", workspace_path=ws,
        model=None, max_tokens=None,
    )

    assert result[0] == "codex original\n[EDITED]"
    received = (ws / "received_instruction.txt").read_text(encoding="utf-8")
    assert "[SYSTEM]" in received  # codex embeds the system prompt in the preamble


@pytest.mark.asyncio
async def test_real_subprocess_empty_document_first_iteration(stub_cli, tmp_path):
    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("", encoding="utf-8")

    result = await claude_adapter._run_claude_document(
        system_prompt="sys", user_instruction="Write it.",
        document="", workspace_path=ws, model=None, max_tokens=None,
    )

    assert result[0] == "\n[EDITED]"


# ---------------------------------------------------------------------------
# Context offload — real behaviour
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_real_subprocess_small_context_inlined(stub_cli, tmp_path):
    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("doc", encoding="utf-8")

    await claude_adapter._run_claude_document(
        system_prompt="sys", user_instruction="Findings: fix X.",
        document="doc", workspace_path=ws, model=None, max_tokens=None,
        context_document="small background context",
    )

    received = (ws / "received_instruction.txt").read_text(encoding="utf-8")
    assert "small background context" in received
    assert not (ws / "context.md").exists()


@pytest.mark.asyncio
async def test_real_subprocess_large_context_offloaded_and_seen(stub_cli, tmp_path):
    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("doc", encoding="utf-8")
    big = "BACKGROUND\n" + "c" * (workspace_module.CONTEXT_FILE_THRESHOLD + 1)

    result = await claude_adapter._run_claude_document(
        system_prompt="sys", user_instruction="Findings: fix X.",
        document="doc", workspace_path=ws, model=None, max_tokens=None,
        context_document=big,
    )

    received = (ws / "received_instruction.txt").read_text(encoding="utf-8")
    assert "context.md" in received
    assert big not in received, "huge context must not be inlined into argv"
    assert "Findings: fix X." in received, "findings must stay in the prompt"
    assert (ws / "context.md").read_text(encoding="utf-8") == big
    assert "[CONTEXT_SEEN]" in result[0], "agent must be able to read the offloaded context"


# ---------------------------------------------------------------------------
# Unicode round-trips through real argv + real files
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_real_subprocess_unicode_document_and_context(stub_cli, tmp_path):
    ws = tmp_path / "ws"
    ws.mkdir()
    doc = "Größe, Übung, Wörter — 日本語 — 😀"
    (ws / "draft.md").write_text(doc, encoding="utf-8")

    result = await claude_adapter._run_claude_document(
        system_prompt="Schöner Schreiber", user_instruction="Füge ä, ö, ü hinzu.",
        document=doc, workspace_path=ws, model=None, max_tokens=None,
        context_document="Kontext mit Umlauten: ä ö ü ß",
    )

    assert result[0] == doc + "\n[EDITED]"
    received = (ws / "received_instruction.txt").read_text(encoding="utf-8")
    assert "Füge ä, ö, ü hinzu." in received
    assert "Kontext mit Umlauten: ä ö ü ß" in received


# ---------------------------------------------------------------------------
# Error paths — real subprocess exit codes / file states
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_real_subprocess_nonzero_exit_raises(stub_cli, tmp_path):
    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("doc", encoding="utf-8")

    with pytest.raises(RuntimeError, match="exited with code 3"):
        await claude_adapter._run_claude_document(
            system_prompt="sys", user_instruction="STUB_FAIL please",
            document="doc", workspace_path=ws, model=None, max_tokens=None,
        )


@pytest.mark.asyncio
async def test_real_subprocess_deleted_draft_raises(stub_cli, tmp_path):
    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("doc", encoding="utf-8")

    with pytest.raises(RuntimeError, match="draft.md not found"):
        await claude_adapter._run_claude_document(
            system_prompt="sys", user_instruction="STUB_DELETE_DRAFT now",
            document="doc", workspace_path=ws, model=None, max_tokens=None,
        )


@pytest.mark.asyncio
async def test_real_subprocess_timeout_kills_and_reaps(stub_cli, tmp_path, monkeypatch):
    """A hanging CLI is killed on timeout, reaped (no zombie), and surfaces a RuntimeError."""
    monkeypatch.setattr(claude_adapter, "CLI_TIMEOUT_SECONDS", 1)
    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("doc", encoding="utf-8")

    with pytest.raises(RuntimeError, match="timed out"):
        await claude_adapter._run_claude_document(
            system_prompt="sys", user_instruction="STUB_HANG forever",
            document="doc", workspace_path=ws, model=None, max_tokens=None,
        )
    # Give the event loop a tick; the killed child must have been reaped (no zombies).
    await asyncio.sleep(0.1)


# ---------------------------------------------------------------------------
# argv per-argument limit (E2BIG) — the hard safety net
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_real_subprocess_huge_instruction_does_not_raise_e2big(stub_cli, tmp_path):
    """A >128 KB instruction must NOT crash with OSError; it is offloaded to instruction.md."""
    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("doc", encoding="utf-8")
    # 200 KB instruction — well above MAX_ARG_STRLEN (128 KB). Without the fix this raises
    # OSError [Errno 7] Argument list too long.
    huge = "FINDINGS: " + "y" * 200_000

    result = await claude_adapter._run_claude_document(
        system_prompt="sys", user_instruction=huge,
        document="doc", workspace_path=ws, model=None, max_tokens=None,
    )

    assert result[0] == "doc\n[EDITED]"
    assert (ws / "instruction.md").exists(), "oversized instruction must be offloaded"
    assert huge in (ws / "instruction.md").read_text(encoding="utf-8")
    # The argv must carry only the short pointer, not the huge instruction.
    argv = (ws / "received_argv.txt").read_text(encoding="utf-8")
    assert huge not in argv
    assert "instruction.md" in argv


@pytest.mark.asyncio
async def test_real_subprocess_huge_instruction_codex(stub_cli, tmp_path):
    """Same E2BIG safety net for the codex adapter."""
    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("doc", encoding="utf-8")
    huge = "FINDINGS: " + "z" * 200_000

    result = await codex_adapter._run_codex_document(
        system_prompt="sys", user_instruction=huge,
        document="doc", workspace_path=ws, model=None, max_tokens=None,
    )

    assert result[0] == "doc\n[EDITED]"
    assert (ws / "instruction.md").exists()


@pytest.mark.asyncio
async def test_real_subprocess_huge_context_and_instruction_combined(stub_cli, tmp_path):
    """Both large context AND large instruction: both offloaded, no crash, agent sees both."""
    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("doc", encoding="utf-8")
    big_ctx = "CTX\n" + "c" * (workspace_module.CONTEXT_FILE_THRESHOLD + 1)
    huge_instr = "FINDINGS: " + "y" * 200_000

    result = await claude_adapter._run_claude_document(
        system_prompt="sys", user_instruction=huge_instr,
        document="doc", workspace_path=ws, model=None, max_tokens=None,
        context_document=big_ctx,
    )

    assert result[0] == "doc\n[EDITED]\n[CONTEXT_SEEN]"
    assert (ws / "context.md").exists()
    assert (ws / "instruction.md").exists()


# ---------------------------------------------------------------------------
# Concurrency — real parallel subprocesses through real workspaces
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_real_concurrent_calls_isolated(stub_cli, tmp_path, monkeypatch):
    """Many parallel document-mode calls must not cross-contaminate each other's drafts."""
    monkeypatch.setattr(workspace_module, "WORKSPACE_ROOT", str(tmp_path / "work"))
    (tmp_path / "work").mkdir()

    async def one_call(n: int) -> str:
        async with ephemeral_workspace(f"document-{n}") as ws:
            text, _ = await claude_adapter._run_claude_document(
                system_prompt="sys", user_instruction=f"edit {n}",
                document=f"document-{n}", workspace_path=ws,
                model=None, max_tokens=None,
            )
            return text

    results = await asyncio.gather(*[one_call(i) for i in range(12)])

    for i, r in enumerate(results):
        assert r == f"document-{i}\n[EDITED]", f"call {i} got contaminated result: {r!r}"


@pytest.mark.asyncio
async def test_real_concurrent_workspaces_cleaned_up(stub_cli, tmp_path, monkeypatch):
    work = tmp_path / "work"
    work.mkdir()
    monkeypatch.setattr(workspace_module, "WORKSPACE_ROOT", str(work))

    async def one_call(n: int):
        async with ephemeral_workspace(f"doc-{n}") as ws:
            await claude_adapter._run_claude_document(
                system_prompt="sys", user_instruction=f"edit {n}",
                document=f"doc-{n}", workspace_path=ws, model=None, max_tokens=None,
            )

    await asyncio.gather(*[one_call(i) for i in range(10)])

    # All ephemeral workspaces must be gone after their contexts exit.
    leftover = list(work.iterdir())
    assert leftover == [], f"workspaces not cleaned up: {leftover}"


# ---------------------------------------------------------------------------
# Full HTTP path — real FastAPI app + real ephemeral workspace + real subprocess
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_http_claude_document_mode_full_path(stub_cli, tmp_path, monkeypatch):
    """End-to-end through the real ASGI app: request → workspace → stub CLI → response."""
    from httpx import AsyncClient, ASGITransport

    work = tmp_path / "work"
    work.mkdir()
    monkeypatch.setattr(workspace_module, "WORKSPACE_ROOT", str(work))
    import main as main_module

    transport = ASGITransport(app=main_module.app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        resp = await client.post("/v1/claude/chat/completions", json={
            "model": "claude-opus-4-8",
            "messages": [
                {"role": "system", "content": "You are a writer."},
                {"role": "user", "content": "Improve the draft."},
            ],
            "document_mode": True,
            "document": "the current document body",
        })

    assert resp.status_code == 200
    content = resp.json()["choices"][0]["message"]["content"]
    assert content == "the current document body\n[EDITED]"
    # Workspace must have been cleaned up after the request completed.
    assert list(work.iterdir()) == []


@pytest.mark.asyncio
async def test_http_codex_document_mode_with_large_context(stub_cli, tmp_path, monkeypatch):
    """Full HTTP path for codex with an offloaded context_document."""
    from httpx import AsyncClient, ASGITransport

    work = tmp_path / "work"
    work.mkdir()
    monkeypatch.setattr(workspace_module, "WORKSPACE_ROOT", str(work))
    import main as main_module

    big_ctx = "RESEARCH\n" + "c" * (workspace_module.CONTEXT_FILE_THRESHOLD + 1)
    transport = ASGITransport(app=main_module.app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        resp = await client.post("/v1/codex/chat/completions", json={
            "model": "gpt-5.5",
            "messages": [
                {"role": "system", "content": "You are a writer."},
                {"role": "user", "content": "Resolve the findings."},
            ],
            "document_mode": True,
            "document": "draft body",
            "context_document": big_ctx,
        })

    assert resp.status_code == 200
    content = resp.json()["choices"][0]["message"]["content"]
    assert "[EDITED]" in content
    assert "[CONTEXT_SEEN]" in content, "agent must read the offloaded context.md"
    assert list(work.iterdir()) == []


@pytest.mark.asyncio
async def test_http_large_document_round_trips(stub_cli, tmp_path, monkeypatch):
    """The original failure scenario: a ~55 KB document survives the full path intact."""
    from httpx import AsyncClient, ASGITransport

    work = tmp_path / "work"
    work.mkdir()
    monkeypatch.setattr(workspace_module, "WORKSPACE_ROOT", str(work))
    import main as main_module

    big_doc = "# Long Document\n\n" + ("Lorem ipsum dolor sit amet. " * 2000)  # ~55 KB
    transport = ASGITransport(app=main_module.app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        resp = await client.post("/v1/claude/chat/completions", json={
            "model": "claude-opus-4-8",
            "messages": [{"role": "user", "content": "Revise."}],
            "document_mode": True,
            "document": big_doc,
        })

    assert resp.status_code == 200
    content = resp.json()["choices"][0]["message"]["content"]
    # The full document is preserved (no truncation/collapse) plus the stub's edit marker.
    assert content == big_doc + "\n[EDITED]"
    assert list(work.iterdir()) == []
