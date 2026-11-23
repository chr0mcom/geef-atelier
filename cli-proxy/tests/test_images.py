"""Tests for image generation: codex_adapter.generate_image and POST /v1/codex/images/generations."""
from __future__ import annotations

import asyncio
import base64
import json
import os
import sys
from pathlib import Path
from unittest.mock import AsyncMock, MagicMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import codex_adapter  # noqa: E402
import failover  # noqa: E402
import main  # noqa: E402
from usage import UsageParts  # noqa: E402

THREAD = "01a1cf33-edc2-74b1-a19d-3ed7070df180"
OTHER_THREAD = "0aaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee"


def _png(width: int = 1672, height: int = 941) -> bytes:
    """Smallest byte string with a valid PNG signature and IHDR (all the proxy ever inspects)."""
    return (b"\x89PNG\r\n\x1a\n" + (13).to_bytes(4, "big") + b"IHDR"
            + width.to_bytes(4, "big") + height.to_bytes(4, "big") + b"\x08\x06\x00\x00\x00")


def _events(thread_id: str | None = THREAD, text: str = "") -> bytes:
    lines = []
    if thread_id is not None:
        lines.append({"type": "thread.started", "thread_id": thread_id})
    lines.append({"type": "turn.started"})
    if text:
        lines.append({"type": "item.completed", "item": {"id": "i0", "type": "agent_message", "text": text}})
    lines.append({"type": "turn.completed", "usage": {
        "input_tokens": 41941, "cached_input_tokens": 32128, "output_tokens": 263,
        "reasoning_output_tokens": 0}})
    return "\n".join(json.dumps(e) for e in lines).encode()


def _proc(stdout: bytes = b"", returncode: int = 0, stderr: bytes = b"") -> MagicMock:
    proc = MagicMock()
    proc.returncode = returncode
    proc.communicate = AsyncMock(return_value=(stdout, stderr))
    proc.kill = MagicMock()
    proc.wait = AsyncMock(return_value=0)
    return proc


def _put_image(home: Path, thread_id: str = THREAD, data: bytes | None = None, name: str = "exec-1.png") -> Path:
    directory = home / "generated_images" / thread_id
    directory.mkdir(parents=True)
    (directory / name).write_bytes(_png() if data is None else data)
    return directory


@pytest.fixture(autouse=True)
def _isolated(tmp_path, monkeypatch):
    monkeypatch.setattr(codex_adapter, "CODEX_HOME", str(tmp_path))
    monkeypatch.setattr(codex_adapter, "_semaphore", asyncio.Semaphore(2))
    monkeypatch.setattr(codex_adapter, "_image_semaphore", asyncio.Semaphore(1))
    monkeypatch.setattr(main, "_image_day", "")
    monkeypatch.setattr(main, "_image_count", 0)
    monkeypatch.setattr(main, "IMAGE_DAILY_MAX", 50)


async def _generate(proc: MagicMock, *, size: str | None = None, prompt: str = "Eine Berghütte"):
    captured: dict = {}

    async def fake_exec(*args, **kwargs):
        captured["args"] = list(args)
        captured["kwargs"] = kwargs
        return proc

    with patch("asyncio.create_subprocess_exec", fake_exec):
        try:
            result = await codex_adapter.generate_image(prompt, size)
        except Exception as exc:  # noqa: BLE001 — the tests inspect the error
            result = exc
    return result, captured


class TestAdapter:
    async def test_returns_png_and_usage_and_removes_the_image_directory(self, tmp_path):
        directory = _put_image(tmp_path)
        result, _ = await _generate(_proc(_events()))
        png, usage = result
        assert png == _png()
        assert usage.input_tokens == 41941 and usage.output_tokens == 263
        assert not directory.exists(), "the generated image must not stay in the auth volume"

    async def test_argv_is_a_plain_ephemeral_imagegen_run(self, tmp_path):
        _put_image(tmp_path)
        _, captured = await _generate(_proc(_events()))
        args = captured["args"]
        assert args[:2] == ["codex", "exec"]
        assert "--ephemeral" in args and "--skip-git-repo-check" in args
        assert "-m" not in args, "no model is forwarded: the image model is codex's own"
        assert "--search" not in args
        assert args[-1].startswith("$imagegen Eine Berghütte")
        assert captured["kwargs"]["stdin"] == asyncio.subprocess.DEVNULL
        assert captured["kwargs"]["env"]["HOME"] == str(tmp_path)

    async def test_size_becomes_an_aspect_ratio_hint(self, tmp_path):
        _put_image(tmp_path)
        _, captured = await _generate(_proc(_events()), size="1024x1536")
        assert "portrait (2:3)" in captured["args"][-1]

    async def test_auto_size_adds_no_hint(self, tmp_path):
        _put_image(tmp_path)
        _, captured = await _generate(_proc(_events()), size="auto")
        assert captured["args"][-1] == "$imagegen Eine Berghütte"

    async def test_no_image_raises_with_the_agent_message(self):
        result, _ = await _generate(_proc(_events(text="I cannot create that image.")))
        assert isinstance(result, codex_adapter.NoImageError)
        assert "codex produced no image" in str(result)
        assert "I cannot create that image." in str(result)

    async def test_non_zero_exit_raises_and_still_cleans_up(self, tmp_path):
        directory = _put_image(tmp_path)
        result, _ = await _generate(_proc(_events(), returncode=1, stderr=b"rate limit reached"))
        assert isinstance(result, RuntimeError)
        assert "exited with code 1" in str(result) and "rate limit reached" in str(result)
        assert not directory.exists()

    async def test_a_file_that_is_not_a_png_is_refused(self, tmp_path):
        directory = _put_image(tmp_path, data=b"<html>not an image</html>")
        result, _ = await _generate(_proc(_events()))
        assert isinstance(result, RuntimeError) and "no image" in str(result)
        assert not directory.exists()

    async def test_the_newest_png_wins(self, tmp_path):
        directory = _put_image(tmp_path, name="exec-old.png", data=_png(10, 10))
        newer = directory / "exec-new.png"
        newer.write_bytes(_png(20, 20))
        os.utime(directory / "exec-old.png", (1, 1))
        result, _ = await _generate(_proc(_events()))
        assert result[0] == _png(20, 20)

    @pytest.mark.parametrize("bad_id", ["../../etc", "not-a-uuid", "", None])
    async def test_a_thread_id_that_is_not_a_uuid_is_never_used_as_a_path(self, tmp_path, bad_id):
        bystander = _put_image(tmp_path, thread_id=OTHER_THREAD)
        result, _ = await _generate(_proc(_events(thread_id=bad_id)))
        assert isinstance(result, RuntimeError)
        assert bystander.exists(), "another image must never be deleted"

    async def test_timeout_kills_the_process(self, monkeypatch):
        monkeypatch.setattr(codex_adapter, "IMAGE_TIMEOUT_SECONDS", 0.05)
        proc = _proc()
        proc.returncode = None  # still running

        async def hang():
            await asyncio.sleep(5)

        proc.communicate = hang
        result, _ = await _generate(proc)
        assert isinstance(result, RuntimeError) and "timed out" in str(result)
        proc.kill.assert_called_once()
        proc.wait.assert_awaited()

    async def test_cancellation_kills_the_process_and_propagates(self):
        proc = _proc()
        proc.returncode = None
        started = asyncio.Event()

        async def hang():
            started.set()
            await asyncio.sleep(5)

        proc.communicate = hang

        async def fake_exec(*args, **kwargs):
            return proc

        with patch("asyncio.create_subprocess_exec", fake_exec):
            task = asyncio.ensure_future(codex_adapter.generate_image("x"))
            await started.wait()
            task.cancel()
            with pytest.raises(asyncio.CancelledError):
                await task
        proc.kill.assert_called_once()
        proc.wait.assert_awaited()

    async def test_stale_image_directories_are_swept_but_fresh_ones_are_not(self, tmp_path):
        stale = _put_image(tmp_path, thread_id=OTHER_THREAD)
        old = 1_000_000
        os.utime(stale, (old, old))
        fresh = _put_image(tmp_path, thread_id="0bbbbbbb-bbbb-4ccc-8ddd-eeeeeeeeeeee")
        foreign = tmp_path / "generated_images" / "not-a-thread-dir"
        foreign.mkdir()
        os.utime(foreign, (old, old))
        _put_image(tmp_path)  # the job's own image
        await _generate(_proc(_events()))
        assert not stale.exists(), "a leftover of a crashed job is removed"
        assert fresh.exists(), "a directory that may belong to a running job stays"
        assert foreign.exists(), "only UUID-named directories are ever touched"

    async def test_only_one_image_runs_at_a_time(self, tmp_path):
        running = 0
        peak = 0

        async def slow_communicate():
            nonlocal running, peak
            running += 1
            peak = max(peak, running)
            await asyncio.sleep(0.05)
            running -= 1
            return _events(), b""

        def make_proc(*args, **kwargs):
            proc = _proc()
            proc.communicate = slow_communicate
            _put_image(tmp_path, thread_id=f"{len(args):08d}-0000-4000-8000-{running:012d}")
            return proc

        async def fake_exec(*args, **kwargs):
            return make_proc(*args, **kwargs)

        with patch("asyncio.create_subprocess_exec", fake_exec):
            await asyncio.gather(
                *[codex_adapter.generate_image("x") for _ in range(3)], return_exceptions=True)
        assert peak == 1


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


def _ok(png: bytes | None = None) -> AsyncMock:
    return AsyncMock(return_value=(_png() if png is None else png, UsageParts(input_tokens=100, output_tokens=20)))


URL = "/v1/codex/images/generations"


class TestEndpoint:
    def test_returns_the_openai_images_shape(self, client):
        with patch.object(main.codex_adapter, "generate_image", _ok()) as gen, \
                patch.object(main.claude_adapter, "complete_with_usage",
                             AsyncMock(side_effect=AssertionError("claude must not be called"))):
            resp = client.post(URL, json={"prompt": "Eine Berghütte", "size": "1536x1024"})
        assert resp.status_code == 200
        body = resp.json()
        assert base64.b64decode(body["data"][0]["b64_json"]) == _png()
        assert body["output_format"] == "png"
        assert body["size"] == "1672x941", "the real size is reported, not the requested one"
        assert "usage" not in body, "the agent turn's tokens are not the image model's usage"
        assert isinstance(body["created"], int)
        assert resp.headers["x-cli-proxy-provider"] == "codex"
        gen.assert_awaited_once_with("Eine Berghütte", "1536x1024")

    def test_accepted_and_ignored_parameters(self, client):
        with patch.object(main.codex_adapter, "generate_image", _ok()):
            resp = client.post(URL, json={
                "prompt": "x", "model": "gpt-image-2", "n": 1, "quality": "high",
                "background": "opaque", "user": "u1", "response_format": "b64_json",
                "output_format": "png", "size": "auto", "some_future_field": True})
        assert resp.status_code == 200

    @pytest.mark.parametrize("body, param", [
        ({"prompt": "x", "n": 2}, "n"),
        ({"prompt": "x", "response_format": "url"}, "response_format"),
        ({"prompt": "x", "output_format": "webp"}, "output_format"),
        ({"prompt": "x", "size": "512x512"}, "size"),
        ({"prompt": "x", "background": "transparent"}, "background"),
        ({"prompt": ""}, "prompt"),
        ({}, "prompt"),
    ])
    def test_unsupported_requests_are_400_and_cost_nothing(self, client, body, param):
        with patch.object(main.codex_adapter, "generate_image", _ok()) as gen:
            resp = client.post(URL, json=body)
        assert resp.status_code == 400
        assert resp.json()["error"]["param"] == param
        gen.assert_not_awaited()
        assert main._image_count == 0, "a rejected request must not use up the daily cap"

    def test_requires_the_bearer_key_when_keys_are_configured(self, client):
        with patch.object(main, "_API_KEYS", {"secret-key"}), \
                patch.object(main.codex_adapter, "generate_image", _ok()) as gen:
            denied = client.post(URL, json={"prompt": "x"})
            wrong = client.post(URL, json={"prompt": "x"}, headers={"Authorization": "Bearer nope"})
            allowed = client.post(URL, json={"prompt": "x"}, headers={"Authorization": "Bearer secret-key"})
        assert denied.status_code == 401 and wrong.status_code == 401
        assert allowed.status_code == 200
        gen.assert_awaited_once()

    def test_a_limit_error_is_429(self, client):
        gen = AsyncMock(side_effect=RuntimeError("codex CLI (image) exited with code 1: usage limit reached"))
        with patch.object(main.codex_adapter, "generate_image", gen):
            resp = client.post(URL, json={"prompt": "x"})
        assert resp.status_code == 429
        assert resp.json()["error"]["type"] == "rate_limit_error"

    def test_any_other_cli_error_is_502(self, client):
        gen = AsyncMock(side_effect=RuntimeError("codex CLI (image) timed out after 300s"))
        with patch.object(main.codex_adapter, "generate_image", gen):
            resp = client.post(URL, json={"prompt": "x"})
        assert resp.status_code == 502
        assert "timed out" in resp.json()["error"]["message"]

    def test_a_declined_prompt_is_a_400_even_if_the_refusal_mentions_a_limit(self, client):
        """No image after a clean exit is the caller's prompt, not the backend: no retry (502)
        and no wording-based classification (a refusal that says "limit" is not a rate limit)."""
        gen = AsyncMock(side_effect=codex_adapter.NoImageError(
            "codex produced no image: I can't create this, it exceeds the content limit."))
        with patch.object(main.codex_adapter, "generate_image", gen):
            resp = client.post(URL, json={"prompt": "x"})
        assert resp.status_code == 400
        error = resp.json()["error"]
        assert error["code"] == "image_generation_failed"
        assert "content limit" in error["message"]

    def test_an_image_failure_never_moves_the_chat_circuit_breaker(self, client):
        gen = AsyncMock(side_effect=RuntimeError("codex CLI (image) exited with code 1: Failed to authenticate"))
        with patch.object(main.codex_adapter, "generate_image", gen):
            for _ in range(5):
                assert client.post(URL, json={"prompt": "x"}).status_code == 502
        state = failover.snapshot()
        assert state["degraded"] is False
        assert all(b["consecutive_failures"] == 0 for b in state["backends"].values())
        assert failover.attempt_order("codex")[0] == "codex"

    def test_the_daily_cap_returns_429_and_zero_disables_it(self, client, monkeypatch):
        monkeypatch.setattr(main, "IMAGE_DAILY_MAX", 2)
        with patch.object(main.codex_adapter, "generate_image", _ok()) as gen:
            codes = [client.post(URL, json={"prompt": "x"}).status_code for _ in range(3)]
            assert codes == [200, 200, 429]
            assert gen.await_count == 2, "the third request must not reach the CLI"
            monkeypatch.setattr(main, "IMAGE_DAILY_MAX", 0)
            assert client.post(URL, json={"prompt": "x"}).status_code == 200

    def test_the_daily_cap_resets_on_a_new_utc_day(self, client, monkeypatch):
        monkeypatch.setattr(main, "IMAGE_DAILY_MAX", 1)
        monkeypatch.setattr(main, "_image_day", "2000-01-01")
        monkeypatch.setattr(main, "_image_count", 1)
        with patch.object(main.codex_adapter, "generate_image", _ok()):
            assert client.post(URL, json={"prompt": "x"}).status_code == 200
