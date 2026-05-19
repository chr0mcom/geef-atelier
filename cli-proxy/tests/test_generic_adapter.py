"""Tests for GenericAdapter using mocked subprocess."""
import asyncio
import sys
import os
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

from adapters.generic_adapter import GenericAdapter


@pytest.fixture
def adapter():
    return GenericAdapter()


@pytest.fixture
def text_config():
    return {
        "name": "custom-echo",
        "settings": {
            "cli_kind": "generic",
            "binary": "echo",
            "prompt_args_template": ["{prompt}"],
            "output_format": "text",
            "models": ["my-model-v1"],
        },
    }


def _make_proc(stdout: bytes, returncode: int = 0, stderr: bytes = b"") -> MagicMock:
    proc = MagicMock()
    proc.returncode = returncode
    proc.communicate = AsyncMock(return_value=(stdout, stderr))
    return proc


@pytest.mark.asyncio
async def test_list_models(adapter, text_config):
    models = await adapter.list_models(text_config)
    assert models == ["my-model-v1"]


@pytest.mark.asyncio
async def test_list_models_empty_when_not_configured(adapter):
    cfg = {"name": "custom-x", "settings": {"cli_kind": "generic", "binary": "echo"}}
    models = await adapter.list_models(cfg)
    assert models == []


@pytest.mark.asyncio
async def test_execute_text_output(adapter, text_config):
    proc = _make_proc(b"  hello world  ")
    with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=proc)):
        request = {"model": "my-model-v1", "messages": [{"role": "user", "content": "test"}]}
        result = await adapter.execute(text_config, request)

    assert result["choices"][0]["message"]["content"] == "hello world"


@pytest.mark.asyncio
async def test_execute_template_substitution(adapter, text_config):
    """Verify {prompt} is substituted in args template."""
    captured_cmd: list[str] = []

    async def capture_exec(*args, **kwargs):
        captured_cmd.extend(args)
        proc = MagicMock()
        proc.returncode = 0
        proc.communicate = AsyncMock(return_value=(b"ok", b""))
        return proc

    with patch("asyncio.create_subprocess_exec", side_effect=capture_exec):
        request = {
            "model": "my-model-v1",
            "messages": [{"role": "user", "content": "hello prompt"}],
        }
        await adapter.execute(text_config, request)

    assert "hello prompt" in " ".join(captured_cmd)


@pytest.mark.asyncio
async def test_execute_raises_when_binary_missing(adapter):
    cfg = {
        "name": "custom-nobinary",
        "settings": {"cli_kind": "generic", "binary": "", "models": []},
    }
    request = {"model": "m", "messages": [{"role": "user", "content": "x"}]}
    with pytest.raises(ValueError, match="binary"):
        await adapter.execute(cfg, request)


@pytest.mark.asyncio
async def test_execute_raises_on_nonzero_exit(adapter, text_config):
    proc = _make_proc(b"", returncode=1, stderr=b"command not found")
    with patch("asyncio.create_subprocess_exec", AsyncMock(return_value=proc)):
        request = {"model": "my-model-v1", "messages": [{"role": "user", "content": "test"}]}
        with pytest.raises(RuntimeError, match="echo exited 1"):
            await adapter.execute(text_config, request)


@pytest.mark.asyncio
async def test_execute_stdin_mode_does_not_pass_prompt_as_arg(adapter):
    """In stdin_mode the prompt goes via stdin, not a positional arg."""
    stdin_config = {
        "name": "custom-stdin",
        "settings": {
            "cli_kind": "generic",
            "binary": "cat",
            "stdin_mode": True,
            "output_format": "text",
            "models": ["stdin-model"],
        },
    }
    captured_cmd: list[str] = []
    captured_input: list[bytes | None] = []

    async def capture_exec(*args, **kwargs):
        captured_cmd.extend(args)
        captured_input.append(None)  # stdin handled by communicate
        proc = MagicMock()
        proc.returncode = 0
        proc.communicate = AsyncMock(return_value=(b"stdin response", b""))
        return proc

    with patch("asyncio.create_subprocess_exec", side_effect=capture_exec):
        request = {
            "model": "stdin-model",
            "messages": [{"role": "user", "content": "my prompt text"}],
        }
        result = await adapter.execute(stdin_config, request)

    # The binary should be the only arg (no positional prompt arg)
    assert captured_cmd == ["cat"]
    assert result["choices"][0]["message"]["content"] == "stdin response"


@pytest.mark.asyncio
async def test_execute_model_substitution_in_template(adapter):
    """Verify {model} is substituted in args template."""
    cfg = {
        "name": "custom-model-arg",
        "settings": {
            "cli_kind": "generic",
            "binary": "my-llm",
            "prompt_args_template": ["--model", "{model}", "{prompt}"],
            "output_format": "text",
            "models": ["llm-v2"],
        },
    }
    captured_cmd: list[str] = []

    async def capture_exec(*args, **kwargs):
        captured_cmd.extend(args)
        proc = MagicMock()
        proc.returncode = 0
        proc.communicate = AsyncMock(return_value=(b"result", b""))
        return proc

    with patch("asyncio.create_subprocess_exec", side_effect=capture_exec):
        request = {"model": "llm-v2", "messages": [{"role": "user", "content": "q"}]}
        await adapter.execute(cfg, request)

    assert "llm-v2" in captured_cmd
