"""Tests for codex_adapter using a mocked subprocess."""
import os
import sys
from unittest.mock import AsyncMock, MagicMock, mock_open, patch

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import codex_adapter


@pytest.fixture(autouse=True)
def reset_semaphore():
    import asyncio
    codex_adapter._semaphore = asyncio.Semaphore(codex_adapter.MAX_CONCURRENT)
    yield


def _make_proc(returncode: int = 0, stderr: str = "") -> MagicMock:
    proc = MagicMock()
    proc.returncode = returncode
    proc.communicate = AsyncMock(return_value=(b"", stderr.encode()))
    return proc


@pytest.mark.asyncio
async def test_complete_reads_output_file():
    proc = _make_proc()

    with (
        patch("asyncio.create_subprocess_exec", AsyncMock(return_value=proc)),
        patch("builtins.open", mock_open(read_data="Codex response text")),
        patch("os.unlink"),
        patch("tempfile.NamedTemporaryFile") as mock_tmp,
    ):
        mock_tmp.return_value.__enter__.return_value.name = "/tmp/fake.txt"
        result = await codex_adapter.complete("Hello", "gpt-4o", 512)

    assert result == "Codex response text"


@pytest.mark.asyncio
async def test_complete_strips_provider_prefix():
    proc = _make_proc()
    captured: list[list] = []

    async def fake_exec(*args, **kwargs):
        captured.append(list(args))
        return proc

    with (
        patch("asyncio.create_subprocess_exec", fake_exec),
        patch("builtins.open", mock_open(read_data="ok")),
        patch("os.unlink"),
        patch("tempfile.NamedTemporaryFile") as mock_tmp,
    ):
        mock_tmp.return_value.__enter__.return_value.name = "/tmp/fake.txt"
        await codex_adapter.complete("test", "openai/gpt-4o", None)

    args = captured[0]
    model_index = args.index("-m")
    assert args[model_index + 1] == "gpt-4o"


@pytest.mark.asyncio
async def test_complete_enables_web_search():
    proc = _make_proc()
    captured: list[list] = []

    async def fake_exec(*args, **kwargs):
        captured.append(list(args))
        return proc

    with (
        patch("asyncio.create_subprocess_exec", fake_exec),
        patch("builtins.open", mock_open(read_data="ok")),
        patch("os.unlink"),
        patch("tempfile.NamedTemporaryFile") as mock_tmp,
    ):
        mock_tmp.return_value.__enter__.return_value.name = "/tmp/fake.txt"
        await codex_adapter.complete("test", None, None)

    args = captured[0]
    # --search is a global flag and MUST precede the `exec` subcommand.
    assert "--search" in args
    assert args.index("--search") < args.index("exec")


@pytest.mark.asyncio
async def test_complete_raises_on_nonzero_exit():
    proc = _make_proc(returncode=1, stderr="quota exceeded")

    with (
        patch("asyncio.create_subprocess_exec", AsyncMock(return_value=proc)),
        patch("tempfile.NamedTemporaryFile") as mock_tmp,
        patch("os.unlink"),
    ):
        mock_tmp.return_value.__enter__.return_value.name = "/tmp/fake.txt"
        with pytest.raises(RuntimeError, match="quota exceeded"):
            await codex_adapter.complete("Hello", None, None)
