"""Tests for the document-mode editing path (workspace + adapters + routing)."""
from __future__ import annotations

import asyncio
import os
import shutil
import sys
from pathlib import Path
from unittest.mock import MagicMock, patch

import pytest

# Ensure src/ is on the path (mirrors conftest or direct execution).
sys.path.insert(0, str(Path(__file__).parent.parent / "src"))

import workspace as workspace_module
from workspace import ephemeral_workspace, finalize_instruction, materialize_context


# ---------------------------------------------------------------------------
# workspace.py tests
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_workspace_creates_draft_and_cleans_up(tmp_path):
    """ephemeral_workspace writes draft.md and removes the directory on exit."""
    document = "# Test Document\n\nSome content."
    created_path: Path | None = None

    with patch.object(workspace_module, "WORKSPACE_ROOT", str(tmp_path)):
        async with ephemeral_workspace(document) as ws:
            created_path = ws
            draft = ws / "draft.md"
            assert ws.exists()
            assert draft.exists()
            assert draft.read_text(encoding="utf-8") == document

    assert created_path is not None
    assert not created_path.exists(), "workspace must be removed after context exits"


@pytest.mark.asyncio
async def test_workspace_empty_document_creates_empty_draft(tmp_path):
    """An empty document string creates an empty draft.md (initial iteration)."""
    with patch.object(workspace_module, "WORKSPACE_ROOT", str(tmp_path)):
        async with ephemeral_workspace("") as ws:
            draft = ws / "draft.md"
            assert draft.exists()
            assert draft.read_text(encoding="utf-8") == ""


@pytest.mark.asyncio
async def test_workspace_cleanup_survives_exception(tmp_path):
    """Workspace is cleaned up even when the body of the context raises."""
    created_path: Path | None = None
    try:
        with patch.object(workspace_module, "WORKSPACE_ROOT", str(tmp_path)):
            async with ephemeral_workspace("content") as ws:
                created_path = ws
                raise RuntimeError("simulated error inside context")
    except RuntimeError:
        pass

    assert created_path is not None
    assert not created_path.exists(), "workspace must be cleaned up after exception"


@pytest.mark.asyncio
async def test_workspace_unique_per_call(tmp_path):
    """Each call gets a distinct workspace directory."""
    paths: list[Path] = []
    with patch.object(workspace_module, "WORKSPACE_ROOT", str(tmp_path)):
        async with ephemeral_workspace("a") as ws1:
            paths.append(ws1)
            async with ephemeral_workspace("b") as ws2:
                paths.append(ws2)

    assert paths[0] != paths[1]


# ---------------------------------------------------------------------------
# materialize_context — inline vs context.md offloading
# ---------------------------------------------------------------------------

def test_materialize_context_none_returns_empty(tmp_path):
    """No context document → empty preamble, no file written."""
    assert materialize_context(tmp_path, None) == ""
    assert materialize_context(tmp_path, "") == ""
    assert not (tmp_path / "context.md").exists()


def test_materialize_context_small_inlines(tmp_path):
    """Context at or below the threshold is returned inline, not written to a file."""
    ctx = "short background notes"
    result = materialize_context(tmp_path, ctx)
    assert result == ctx + "\n\n"
    assert not (tmp_path / "context.md").exists()


def test_materialize_context_large_writes_file_and_returns_pointer(tmp_path):
    """Context above the threshold is written to context.md; a pointer line is returned."""
    big = "x" * (workspace_module.CONTEXT_FILE_THRESHOLD + 1)
    result = materialize_context(tmp_path, big)
    assert "context.md" in result
    assert big not in result, "oversized context must not be inlined into the prompt"
    assert (tmp_path / "context.md").read_text(encoding="utf-8") == big


def test_materialize_context_threshold_boundary_inlines(tmp_path):
    """Exactly at the threshold (in bytes) the context is still inlined (boundary is inclusive)."""
    exact = "y" * workspace_module.CONTEXT_FILE_THRESHOLD  # ASCII: 1 byte/char
    result = materialize_context(tmp_path, exact)
    assert result == exact + "\n\n"
    assert not (tmp_path / "context.md").exists()


def test_materialize_context_measures_utf8_bytes_not_chars(tmp_path):
    """Multi-byte context under the char threshold but over the byte limit is offloaded (M-1)."""
    # Each "中" is 3 UTF-8 bytes: char count is half the threshold, byte count well above it.
    multibyte = "中" * (workspace_module.CONTEXT_FILE_THRESHOLD // 2)
    assert len(multibyte) <= workspace_module.CONTEXT_FILE_THRESHOLD  # would inline if counted by chars
    assert len(multibyte.encode("utf-8")) > workspace_module.CONTEXT_FILE_THRESHOLD
    result = materialize_context(tmp_path, multibyte)
    assert "context.md" in result
    assert multibyte not in result
    assert (tmp_path / "context.md").read_text(encoding="utf-8") == multibyte


# ---------------------------------------------------------------------------
# finalize_instruction — the E2BIG safety net
# ---------------------------------------------------------------------------

def test_finalize_instruction_small_passes_through(tmp_path):
    """A small instruction is returned verbatim; no instruction.md is written."""
    instr = "Edit draft.md to fix the introduction."
    assert finalize_instruction(tmp_path, instr) == instr
    assert not (tmp_path / "instruction.md").exists()


def test_finalize_instruction_oversized_offloads_to_file(tmp_path):
    """An instruction above the limit is written to instruction.md and replaced by a pointer."""
    big = "FINDINGS: " + "y" * (workspace_module.INSTRUCTION_ARG_LIMIT + 1)
    result = finalize_instruction(tmp_path, big)
    assert "instruction.md" in result
    assert big not in result
    assert "draft.md" in result, "pointer must still steer the agent to edit draft.md"
    assert (tmp_path / "instruction.md").read_text(encoding="utf-8") == big


def test_finalize_instruction_measures_utf8_bytes(tmp_path):
    """Limit is measured in UTF-8 bytes, not characters (multi-byte safety)."""
    multibyte = "あ" * ((workspace_module.INSTRUCTION_ARG_LIMIT // 3) + 1)  # 3 bytes each
    assert len(multibyte) <= workspace_module.INSTRUCTION_ARG_LIMIT  # inline if counted by chars
    assert len(multibyte.encode("utf-8")) > workspace_module.INSTRUCTION_ARG_LIMIT
    result = finalize_instruction(tmp_path, multibyte)
    assert "instruction.md" in result
    assert (tmp_path / "instruction.md").read_text(encoding="utf-8") == multibyte


def test_finalize_instruction_boundary_inclusive(tmp_path):
    """Exactly at the byte limit the instruction is still passed inline."""
    exact = "y" * workspace_module.INSTRUCTION_ARG_LIMIT  # ASCII 1 byte/char
    assert finalize_instruction(tmp_path, exact) == exact
    assert not (tmp_path / "instruction.md").exists()


# ---------------------------------------------------------------------------
# Helper: a wait_for replacement that actually awaits the coroutine
# ---------------------------------------------------------------------------

async def _passthrough_wait_for(coro, timeout):
    return await coro


# ---------------------------------------------------------------------------
# claude_adapter.complete_document — subprocess args
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_claude_complete_document_passes_correct_args(tmp_path):
    """_run_claude_document spawns claude with --allowedTools Read,Edit,Write and --permission-mode acceptEdits."""
    import claude_adapter

    captured_args: list[str] = []
    revised_content = "# Revised Document"

    async def fake_exec(*args, **kwargs):
        captured_args.extend(args)
        cwd = kwargs.get("cwd")
        if cwd:
            (Path(cwd) / "draft.md").write_text(revised_content, encoding="utf-8")
        proc = MagicMock()
        proc.returncode = 0
        async def communicate():
            return b"", b""
        proc.communicate = communicate
        return proc

    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("original", encoding="utf-8")

    with patch("asyncio.create_subprocess_exec", side_effect=fake_exec), \
         patch("asyncio.wait_for", side_effect=_passthrough_wait_for):
        result = await claude_adapter._run_claude_document(
            system_prompt="You are a writer.",
            user_instruction="Improve the document.",
            document="original",
            workspace_path=ws,
            model="claude-opus-4-8",
            max_tokens=None,
        )

    assert result == revised_content
    assert "claude" in captured_args
    assert "--allowedTools" in captured_args
    assert captured_args[captured_args.index("--allowedTools") + 1] == "Read,Edit,Write"
    assert "--permission-mode" in captured_args
    assert captured_args[captured_args.index("--permission-mode") + 1] == "acceptEdits"
    assert "--append-system-prompt" in captured_args
    assert "--output-format" not in captured_args
    assert "--model" in captured_args
    assert captured_args[captured_args.index("--model") + 1] == "claude-opus-4-8"


@pytest.mark.asyncio
async def test_claude_complete_document_uses_workspace_cwd(tmp_path):
    """_run_claude_document sets cwd to the workspace path."""
    import claude_adapter

    captured_kwargs: dict = {}

    async def fake_exec(*args, **kwargs):
        captured_kwargs.update(kwargs)
        cwd = kwargs.get("cwd")
        if cwd:
            (Path(cwd) / "draft.md").write_text("# Done", encoding="utf-8")
        proc = MagicMock()
        proc.returncode = 0
        async def communicate():
            return b"", b""
        proc.communicate = communicate
        return proc

    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("orig", encoding="utf-8")

    with patch("asyncio.create_subprocess_exec", side_effect=fake_exec), \
         patch("asyncio.wait_for", side_effect=_passthrough_wait_for):
        await claude_adapter._run_claude_document(
            system_prompt="sys", user_instruction="do it",
            document="orig", workspace_path=ws,
            model=None, max_tokens=None,
        )

    assert captured_kwargs.get("cwd") == str(ws)


@pytest.mark.asyncio
async def test_claude_document_offloads_large_context_keeps_findings_in_prompt(tmp_path):
    """Oversized context goes to context.md; the findings instruction stays in the argv prompt."""
    import claude_adapter

    captured_args: list[str] = []
    big_context = "CONTEXT-" + "c" * workspace_module.CONTEXT_FILE_THRESHOLD
    findings = "Reviewer finding: fix the introduction."

    async def fake_exec(*args, **kwargs):
        captured_args.extend(args)
        cwd = kwargs.get("cwd")
        if cwd:
            (Path(cwd) / "draft.md").write_text("# Edited", encoding="utf-8")
        proc = MagicMock()
        proc.returncode = 0
        async def communicate():
            return b"", b""
        proc.communicate = communicate
        return proc

    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("original", encoding="utf-8")

    with patch("asyncio.create_subprocess_exec", side_effect=fake_exec), \
         patch("asyncio.wait_for", side_effect=_passthrough_wait_for):
        await claude_adapter._run_claude_document(
            system_prompt="sys", user_instruction=findings,
            document="original", workspace_path=ws,
            model=None, max_tokens=None, context_document=big_context,
        )

    instruction = captured_args[-1]  # the prompt is the trailing positional arg
    assert (ws / "context.md").read_text(encoding="utf-8") == big_context
    assert big_context not in instruction, "oversized context must not be in the argv prompt"
    assert "context.md" in instruction, "prompt must point to context.md"
    assert findings in instruction, "findings must stay in the argv prompt"
    assert "draft.md" in instruction


@pytest.mark.asyncio
async def test_claude_document_inlines_small_context(tmp_path):
    """Small context is inlined into the prompt; no context.md is written."""
    import claude_adapter

    captured_args: list[str] = []
    small_context = "Background: prior research summary."

    async def fake_exec(*args, **kwargs):
        captured_args.extend(args)
        cwd = kwargs.get("cwd")
        if cwd:
            (Path(cwd) / "draft.md").write_text("# Edited", encoding="utf-8")
        proc = MagicMock()
        proc.returncode = 0
        async def communicate():
            return b"", b""
        proc.communicate = communicate
        return proc

    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("original", encoding="utf-8")

    with patch("asyncio.create_subprocess_exec", side_effect=fake_exec), \
         patch("asyncio.wait_for", side_effect=_passthrough_wait_for):
        await claude_adapter._run_claude_document(
            system_prompt="sys", user_instruction="do it",
            document="original", workspace_path=ws,
            model=None, max_tokens=None, context_document=small_context,
        )

    instruction = captured_args[-1]
    assert small_context in instruction
    assert not (ws / "context.md").exists()


# ---------------------------------------------------------------------------
# codex_adapter.complete_document — subprocess args
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_codex_complete_document_passes_correct_args(tmp_path):
    """_run_codex_document spawns codex with --sandbox workspace-write and -C <workspace>."""
    import codex_adapter

    captured_args: list[str] = []
    revised_content = "# Revised by codex"

    async def fake_exec(*args, **kwargs):
        captured_args.extend(args)
        try:
            c_idx = list(args).index("-C")
            (Path(args[c_idx + 1]) / "draft.md").write_text(revised_content, encoding="utf-8")
        except ValueError:
            pass
        proc = MagicMock()
        proc.returncode = 0
        async def communicate():
            return b"", b""
        proc.communicate = communicate
        return proc

    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("original", encoding="utf-8")

    with patch("asyncio.create_subprocess_exec", side_effect=fake_exec), \
         patch("asyncio.wait_for", side_effect=_passthrough_wait_for):
        result = await codex_adapter._run_codex_document(
            system_prompt="You are a writer.",
            user_instruction="Improve the document.",
            document="original",
            workspace_path=ws,
            model=None,
            max_tokens=None,
        )

    assert result == revised_content
    assert "codex" in captured_args
    assert "--sandbox" in captured_args
    assert captured_args[captured_args.index("--sandbox") + 1] == "workspace-write"
    assert "-C" in captured_args
    assert captured_args[captured_args.index("-C") + 1] == str(ws)
    assert "--skip-git-repo-check" in captured_args
    assert "--output-last-message" not in captured_args


@pytest.mark.asyncio
async def test_codex_complete_document_uses_workspace_cwd(tmp_path):
    """_run_codex_document sets cwd to the workspace path (in addition to -C)."""
    import codex_adapter

    captured_kwargs: dict = {}

    async def fake_exec(*args, **kwargs):
        captured_kwargs.update(kwargs)
        try:
            c_idx = list(args).index("-C")
            (Path(args[c_idx + 1]) / "draft.md").write_text("# Done", encoding="utf-8")
        except ValueError:
            pass
        proc = MagicMock()
        proc.returncode = 0
        async def communicate():
            return b"", b""
        proc.communicate = communicate
        return proc

    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("orig", encoding="utf-8")

    with patch("asyncio.create_subprocess_exec", side_effect=fake_exec), \
         patch("asyncio.wait_for", side_effect=_passthrough_wait_for):
        await codex_adapter._run_codex_document(
            system_prompt="sys", user_instruction="do it",
            document="orig", workspace_path=ws,
            model=None, max_tokens=None,
        )

    assert captured_kwargs.get("cwd") == str(ws)


@pytest.mark.asyncio
async def test_codex_document_offloads_large_context_keeps_findings_in_prompt(tmp_path):
    """Codex path: oversized context goes to context.md; findings stay in the argv prompt."""
    import codex_adapter

    captured_args: list[str] = []
    big_context = "CTX-" + "c" * workspace_module.CONTEXT_FILE_THRESHOLD
    findings = "Reviewer finding: tighten the abstract."

    async def fake_exec(*args, **kwargs):
        captured_args.extend(args)
        try:
            c_idx = list(args).index("-C")
            (Path(args[c_idx + 1]) / "draft.md").write_text("# Edited", encoding="utf-8")
        except ValueError:
            pass
        proc = MagicMock()
        proc.returncode = 0
        async def communicate():
            return b"", b""
        proc.communicate = communicate
        return proc

    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("original", encoding="utf-8")

    with patch("asyncio.create_subprocess_exec", side_effect=fake_exec), \
         patch("asyncio.wait_for", side_effect=_passthrough_wait_for):
        await codex_adapter._run_codex_document(
            system_prompt="sys", user_instruction=findings,
            document="original", workspace_path=ws,
            model=None, max_tokens=None, context_document=big_context,
        )

    instruction = captured_args[-1]
    assert (ws / "context.md").read_text(encoding="utf-8") == big_context
    assert big_context not in instruction
    assert "context.md" in instruction
    assert findings in instruction


# ---------------------------------------------------------------------------
# Error handling — FileNotFoundError → RuntimeError
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_claude_document_mode_raises_when_draft_missing(tmp_path):
    """_run_claude_document raises RuntimeError when CLI exits 0 but draft.md is absent."""
    import claude_adapter

    async def fake_exec(*args, **kwargs):
        # CLI exits successfully but does NOT write draft.md.
        proc = MagicMock()
        proc.returncode = 0
        async def communicate():
            return b"", b""
        proc.communicate = communicate
        return proc

    ws = tmp_path / "ws"
    ws.mkdir()
    (ws / "draft.md").write_text("original", encoding="utf-8")
    (ws / "draft.md").unlink()  # simulate CLI deleting the file

    with patch("asyncio.create_subprocess_exec", side_effect=fake_exec), \
         patch("asyncio.wait_for", side_effect=_passthrough_wait_for):
        with pytest.raises(RuntimeError, match="draft.md not found"):
            await claude_adapter._run_claude_document(
                system_prompt="sys", user_instruction="do it",
                document="original", workspace_path=ws,
                model=None, max_tokens=None,
            )


@pytest.mark.asyncio
async def test_codex_document_mode_raises_when_draft_missing(tmp_path):
    """_run_codex_document raises RuntimeError when CLI exits 0 but draft.md is absent."""
    import codex_adapter

    async def fake_exec(*args, **kwargs):
        proc = MagicMock()
        proc.returncode = 0
        async def communicate():
            return b"", b""
        proc.communicate = communicate
        return proc

    ws = tmp_path / "ws"
    ws.mkdir()
    # draft.md deliberately not created — simulates agent deleting the file

    with patch("asyncio.create_subprocess_exec", side_effect=fake_exec), \
         patch("asyncio.wait_for", side_effect=_passthrough_wait_for):
        with pytest.raises(RuntimeError, match="draft.md not found"):
            await codex_adapter._run_codex_document(
                system_prompt="sys", user_instruction="do it",
                document="original", workspace_path=ws,
                model=None, max_tokens=None,
            )


# ---------------------------------------------------------------------------
# main.py routing
# ---------------------------------------------------------------------------

@pytest.mark.asyncio
async def test_main_routes_document_mode_to_complete_document(tmp_path):
    """POST /v1/claude/chat/completions with document_mode=true calls complete_document, not complete."""
    from httpx import AsyncClient, ASGITransport
    from contextlib import asynccontextmanager

    document_call_args: list = []

    async def mock_complete_document(system_prompt, user_instruction, document, workspace_path, model, max_tokens, context_document=None):
        document_call_args.append((system_prompt, user_instruction, document))
        return "# Edited document"

    complete_call_count = [0]

    async def mock_complete(prompt, model, max_tokens):
        complete_call_count[0] += 1
        return "should not be called"

    dummy_ws = tmp_path / "dummy-ws"
    dummy_ws.mkdir()

    @asynccontextmanager
    async def mock_ephemeral(document):
        yield dummy_ws

    with patch("claude_adapter.complete_document", side_effect=mock_complete_document), \
         patch("claude_adapter.complete", side_effect=mock_complete):
        import main as main_module
        with patch.object(main_module, "ephemeral_workspace", mock_ephemeral):
            transport = ASGITransport(app=main_module.app)
            async with AsyncClient(transport=transport, base_url="http://test") as client:
                resp = await client.post("/v1/claude/chat/completions", json={
                    "model": "claude-opus-4-8",
                    "messages": [
                        {"role": "system", "content": "You are a writer."},
                        {"role": "user", "content": "Improve the draft."},
                    ],
                    "document_mode": True,
                    "document": "# Original",
                })

    assert resp.status_code == 200
    assert len(document_call_args) == 1, "complete_document must be called exactly once"
    assert complete_call_count[0] == 0, "text-mode complete must NOT be called"


@pytest.mark.asyncio
async def test_main_routes_codex_document_mode_to_complete_document(tmp_path):
    """POST /v1/codex/chat/completions with document_mode=true calls codex complete_document."""
    from httpx import AsyncClient, ASGITransport
    from contextlib import asynccontextmanager

    document_call_args: list = []

    async def mock_complete_document(system_prompt, user_instruction, document, workspace_path, model, max_tokens, context_document=None):
        document_call_args.append((system_prompt, user_instruction, document))
        return "# Codex-edited document"

    complete_call_count = [0]

    async def mock_complete(prompt, model, max_tokens):
        complete_call_count[0] += 1
        return "should not be called"

    dummy_ws = tmp_path / "dummy-ws"
    dummy_ws.mkdir()

    @asynccontextmanager
    async def mock_ephemeral(document):
        yield dummy_ws

    with patch("codex_adapter.complete_document", side_effect=mock_complete_document), \
         patch("codex_adapter.complete", side_effect=mock_complete):
        import main as main_module
        with patch.object(main_module, "ephemeral_workspace", mock_ephemeral):
            transport = ASGITransport(app=main_module.app)
            async with AsyncClient(transport=transport, base_url="http://test") as client:
                resp = await client.post("/v1/codex/chat/completions", json={
                    "model": "gpt-5.5",
                    "messages": [
                        {"role": "system", "content": "You are a writer."},
                        {"role": "user", "content": "Improve the draft."},
                    ],
                    "document_mode": True,
                    "document": "# Original",
                })

    assert resp.status_code == 200
    assert len(document_call_args) == 1, "codex complete_document must be called exactly once"
    assert complete_call_count[0] == 0, "text-mode complete must NOT be called"


@pytest.mark.asyncio
async def test_main_uses_text_mode_without_document_mode_flag():
    """POST /v1/claude/chat/completions without document_mode uses the original text-completion path."""
    from httpx import AsyncClient, ASGITransport

    document_call_count = [0]
    complete_call_count = [0]

    async def mock_complete_document(*args, **kwargs):
        document_call_count[0] += 1
        return "should not be called"

    async def mock_complete(prompt, model, max_tokens):
        complete_call_count[0] += 1
        return "text response"

    with patch("claude_adapter.complete_document", side_effect=mock_complete_document), \
         patch("claude_adapter.complete", side_effect=mock_complete):
        import main as main_module
        transport = ASGITransport(app=main_module.app)
        async with AsyncClient(transport=transport, base_url="http://test") as client:
            resp = await client.post("/v1/claude/chat/completions", json={
                "model": "claude-opus-4-8",
                "messages": [{"role": "user", "content": "Hello"}],
            })

    assert resp.status_code == 200
    assert complete_call_count[0] == 1, "text-mode complete must be called"
    assert document_call_count[0] == 0, "document-mode complete_document must NOT be called"
