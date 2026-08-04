"""Tests for the model-catalog provenance contract.

The proxy must never let a failed probe look like a fresh answer: an expired CLI session
otherwise serves the offline fallback list with HTTP 200 for a full day, which is exactly the
staleness the dynamic resolver exists to prevent.
"""
from __future__ import annotations

import asyncio
import os
import sys
import time

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import claude_adapter  # noqa: E402
import codex_adapter  # noqa: E402
import main  # noqa: E402


@pytest.fixture(autouse=True)
def _clear_caches():
    claude_adapter._model_cache = None
    claude_adapter._last_attempt_at = None
    codex_adapter._model_cache = None
    codex_adapter._last_attempt_at = None
    yield
    claude_adapter._model_cache = None
    claude_adapter._last_attempt_at = None
    codex_adapter._model_cache = None
    codex_adapter._last_attempt_at = None


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app)


class TestClaudeProvenance:
    @pytest.mark.asyncio
    async def test_failed_probe_reports_degraded_and_is_not_cached(self, monkeypatch) -> None:
        calls: list[str] = []

        async def _always_fails(alias: str) -> str | None:
            calls.append(alias)
            return None

        monkeypatch.setattr(claude_adapter, "_probe_alias", _always_fails)
        # Throttle disabled here so this test speaks only about caching; the throttle has its own.
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 0)

        models, source, aliases = await claude_adapter.list_models_with_source_async()

        assert source == claude_adapter.SOURCE_DEGRADED
        assert aliases == {}
        assert set(claude_adapter.STATIC_MODELS).issubset(set(models))
        assert claude_adapter._model_cache is None, "a failed probe must never be cached"

        await claude_adapter.list_models_with_source_async()
        assert len(calls) == 6, "the second call must probe again instead of serving a cached failure"

    @pytest.mark.asyncio
    async def test_successful_probe_reports_live_and_is_cached(self, monkeypatch) -> None:
        calls: list[str] = []

        async def _resolves(alias: str) -> str | None:
            calls.append(alias)
            return {"opus": "claude-opus-5", "sonnet": "claude-sonnet-5", "haiku": "claude-haiku-4-5"}[alias]

        monkeypatch.setattr(claude_adapter, "_probe_alias", _resolves)

        models, source, aliases = await claude_adapter.list_models_with_source_async()

        assert source == claude_adapter.SOURCE_LIVE
        assert aliases["claude-opus-latest"] == "claude-opus-5"
        assert "claude-opus-5" in models
        assert "claude-opus-latest" in models

        await claude_adapter.list_models_with_source_async()
        assert len(calls) == 3, "the second call must be served from the cache"

    @pytest.mark.asyncio
    async def test_refresh_bypasses_the_cache(self, monkeypatch) -> None:
        calls: list[str] = []

        async def _resolves(alias: str) -> str | None:
            calls.append(alias)
            return f"claude-{alias}-9"

        monkeypatch.setattr(claude_adapter, "_probe_alias", _resolves)
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 0)

        await claude_adapter.list_models_with_source_async()
        await claude_adapter.list_models_with_source_async(bypass_cache=True)

        assert len(calls) == 6

    @pytest.mark.asyncio
    async def test_partial_probe_success_still_counts_as_live(self, monkeypatch) -> None:
        async def _only_opus(alias: str) -> str | None:
            return "claude-opus-5" if alias == "opus" else None

        monkeypatch.setattr(claude_adapter, "_probe_alias", _only_opus)

        models, source, aliases = await claude_adapter.list_models_with_source_async()

        assert source == claude_adapter.SOURCE_LIVE
        assert aliases == {"claude-opus-latest": "claude-opus-5"}
        assert "claude-opus-5" in models

    def test_endpoint_exposes_source_and_aliases(self, client: TestClient) -> None:
        claude_adapter._model_cache = ({"claude-opus-latest": "claude-opus-5"}, time.time())

        body = client.get("/v1/claude/models").json()

        assert body["x_source"] == "live"
        assert body["x_aliases"] == {"claude-opus-latest": "claude-opus-5"}
        assert {"id": "claude-opus-5", "object": "model", "owned_by": "anthropic"} in body["data"]


class TestPartialProbe:
    @pytest.mark.asyncio
    async def test_a_partial_probe_keeps_the_families_that_answered_earlier(self, monkeypatch) -> None:
        """One family answering is enough to call the run live, so a later partial run must add to
        the cached mapping rather than replace it."""
        async def _all(alias: str) -> str | None:
            return f"claude-{alias}-5"

        monkeypatch.setattr(claude_adapter, "_probe_alias", _all)
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 0)
        await claude_adapter.list_models_with_source_async()

        async def _only_opus(alias: str) -> str | None:
            return "claude-opus-6" if alias == "opus" else None

        monkeypatch.setattr(claude_adapter, "_probe_alias", _only_opus)
        _models, source, aliases = await claude_adapter.list_models_with_source_async(bypass_cache=True)

        assert source == claude_adapter.SOURCE_LIVE
        assert aliases["claude-opus-latest"] == "claude-opus-6"
        assert aliases["claude-sonnet-latest"] == "claude-sonnet-5"
        assert aliases["claude-haiku-latest"] == "claude-haiku-5"


class TestProbeIsolation:
    @pytest.mark.asyncio
    async def test_probe_waits_for_the_shared_semaphore(self, monkeypatch) -> None:
        started = asyncio.Event()

        async def _fake_exec(*args, **kwargs):
            started.set()
            raise RuntimeError("probe should not have started")

        monkeypatch.setattr(asyncio, "create_subprocess_exec", _fake_exec)
        monkeypatch.setattr(claude_adapter, "_semaphore", asyncio.Semaphore(1))

        await claude_adapter._semaphore.acquire()
        task = asyncio.create_task(claude_adapter._probe_alias("opus"))
        await asyncio.sleep(0.05)

        assert not started.is_set(), "the probe must not outrank real completions"

        claude_adapter._semaphore.release()
        await task
        assert started.is_set()

    @pytest.mark.asyncio
    async def test_probe_kills_its_subprocess_on_timeout(self, monkeypatch) -> None:
        killed = asyncio.Event()

        class _HangingProc:
            returncode = None

            async def communicate(self, input: bytes | None = None):
                await asyncio.sleep(10)
                return b"", b""

            def kill(self) -> None:
                killed.set()
                self.returncode = -9

            async def wait(self) -> int:
                return -9

        async def _fake_exec(*args, **kwargs):
            return _HangingProc()

        monkeypatch.setattr(asyncio, "create_subprocess_exec", _fake_exec)
        monkeypatch.setattr(claude_adapter, "_PROBE_TIMEOUT_SECONDS", 0.05)

        assert await claude_adapter._probe_alias("opus") is None
        assert killed.is_set(), "a timed-out probe must not leave a claude process behind"


class TestCodexProvenance:
    @pytest.mark.asyncio
    async def test_fetch_failure_reports_degraded(self, monkeypatch) -> None:
        async def _boom(top_n: int = 10):
            raise RuntimeError("openrouter down")

        monkeypatch.setattr(codex_adapter, "_fetch_from_openrouter", _boom)

        models, source = await codex_adapter.list_models_with_source_async()

        assert source == codex_adapter.SOURCE_DEGRADED
        assert models == codex_adapter._FALLBACK_MODELS

    @pytest.mark.asyncio
    async def test_fetch_success_reports_live(self, monkeypatch) -> None:
        async def _ok(top_n: int = 10):
            return ["gpt-9-nova"]

        monkeypatch.setattr(codex_adapter, "_fetch_from_openrouter", _ok)

        models, source = await codex_adapter.list_models_with_source_async()

        assert source == codex_adapter.SOURCE_LIVE
        assert models == ["gpt-9-nova"]


class TestProviderKeyedEndpoint:
    def test_claude_provider_serves_the_resolved_list_not_the_synced_config(
        self, client: TestClient, monkeypatch
    ) -> None:
        monkeypatch.setattr(
            main.provider_sync,
            "get_provider_config",
            lambda name: {"name": name, "settings": {"cli_kind": "claude", "models": ["stale-from-atelier"]}},
        )
        claude_adapter._model_cache = ({"claude-opus-latest": "claude-opus-5"}, time.time())

        body = client.get("/v1/cli/claude-cli/models").json()
        ids = [entry["id"] for entry in body["data"]]

        assert "stale-from-atelier" not in ids, "the endpoint must not echo what the Atelier pushed in"
        assert "claude-opus-5" in ids
        assert body["x_source"] == "live"
        assert body["x_aliases"]["claude-opus-latest"] == "claude-opus-5"

    def test_gemini_provider_is_static_by_design(self, client: TestClient, monkeypatch) -> None:
        monkeypatch.setattr(
            main.provider_sync,
            "get_provider_config",
            lambda name: {"name": name, "settings": {"cli_kind": "gemini", "models": ["google/gemini-3.1-pro"]}},
        )

        body = client.get("/v1/cli/gemini-cli/models").json()

        assert body["x_source"] == "static"
        assert [entry["id"] for entry in body["data"]] == ["google/gemini-3.1-pro"]
        assert body["x_aliases"] == {}

    def test_unknown_provider_still_404s(self, client: TestClient, monkeypatch) -> None:
        monkeypatch.setattr(main.provider_sync, "get_provider_config", lambda name: None)

        assert client.get("/v1/cli/nope/models").status_code == 404


class TestRefreshAtTheHttpEdge:
    def test_refresh_query_parameter_forces_a_new_resolution(self, client: TestClient, monkeypatch) -> None:
        calls: list[str] = []

        async def _resolves(alias: str) -> str | None:
            calls.append(alias)
            return f"claude-{alias}-5"

        monkeypatch.setattr(claude_adapter, "_probe_alias", _resolves)
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 0)

        client.get("/v1/claude/models")
        assert len(calls) == 3

        client.get("/v1/claude/models")
        assert len(calls) == 3, "a plain call must be served from the cache"

        client.get("/v1/claude/models?refresh=1")
        assert len(calls) == 6, "refresh=1 must re-resolve"

    def test_refresh_is_throttled_so_repeated_clicks_do_not_multiply_probes(
        self, client: TestClient, monkeypatch
    ) -> None:
        calls: list[str] = []

        async def _resolves(alias: str) -> str | None:
            calls.append(alias)
            return f"claude-{alias}-5"

        monkeypatch.setattr(claude_adapter, "_probe_alias", _resolves)
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 3600)

        client.get("/v1/claude/models?refresh=1")
        client.get("/v1/claude/models?refresh=1")
        client.get("/v1/claude/models?refresh=1")

        assert len(calls) == 3, "only the first resolution inside the interval may probe"

    def test_provider_endpoint_forwards_the_refresh_flag(self, client: TestClient, monkeypatch) -> None:
        seen: list[bool] = []

        async def _spy(bypass_cache: bool = False, force: bool = False):
            seen.append(bypass_cache)
            return ["claude-opus-latest"], "live", {}

        monkeypatch.setattr(claude_adapter, "list_models_with_source_async", _spy)
        monkeypatch.setattr(
            main.provider_sync,
            "get_provider_config",
            lambda name: {"name": name, "settings": {"cli_kind": "claude"}},
        )

        client.get("/v1/cli/claude-cli/models")
        client.get("/v1/cli/claude-cli/models?refresh=1")

        assert seen == [False, True]


class TestThrottleUnderFailure:
    """The throttle must key off the ATTEMPT, not the success: a failed probe writes no cache entry,
    so a success-based throttle does not throttle at all in the outage it exists to contain."""

    @pytest.mark.asyncio
    async def test_an_ordinary_call_after_a_failure_still_probes(self, monkeypatch) -> None:
        # The throttle covers forced re-resolutions only. A plain call must keep the promise that a
        # failed probe is not cached, otherwise the two contracts contradict each other.
        calls: list[str] = []

        async def _always_fails(alias: str) -> str | None:
            calls.append(alias)
            return None

        monkeypatch.setattr(claude_adapter, "_probe_alias", _always_fails)
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 3600)

        await claude_adapter.list_models_with_source_async()
        await claude_adapter.list_models_with_source_async()

        assert len(calls) == 6

    @pytest.mark.asyncio
    async def test_repeated_forced_calls_do_not_multiply_probes_while_failing(self, monkeypatch) -> None:
        calls: list[str] = []

        async def _always_fails(alias: str) -> str | None:
            calls.append(alias)
            return None

        monkeypatch.setattr(claude_adapter, "_probe_alias", _always_fails)
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 3600)

        await claude_adapter.list_models_with_source_async(bypass_cache=True)
        await claude_adapter.list_models_with_source_async(bypass_cache=True)
        await claude_adapter.list_models_with_source_async(bypass_cache=True)

        assert len(calls) == 3, "only the first attempt inside the interval may probe"

    @pytest.mark.asyncio
    async def test_the_internal_sweep_is_exempt_from_the_throttle(self, monkeypatch) -> None:
        # The sweep is the only path that still discovers a new model during an outage; letting the
        # throttle swallow it would be silent.
        calls: list[str] = []

        async def _always_fails(alias: str) -> str | None:
            calls.append(alias)
            return None

        monkeypatch.setattr(claude_adapter, "_probe_alias", _always_fails)
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 3600)

        await claude_adapter.list_models_with_source_async(bypass_cache=True)
        await claude_adapter.list_models_with_source_async(bypass_cache=True, force=True)

        assert len(calls) == 6

    @pytest.mark.asyncio
    async def test_a_failed_re_resolution_keeps_the_cached_mapping(self, monkeypatch) -> None:
        async def _resolves(alias: str) -> str | None:
            return f"claude-{alias}-5"

        monkeypatch.setattr(claude_adapter, "_probe_alias", _resolves)
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 0)
        await claude_adapter.list_models_with_source_async()

        async def _always_fails(alias: str) -> str | None:
            return None

        monkeypatch.setattr(claude_adapter, "_probe_alias", _always_fails)
        models, source, aliases = await claude_adapter.list_models_with_source_async(bypass_cache=True)

        assert source == claude_adapter.SOURCE_DEGRADED
        assert aliases["claude-opus-latest"] == "claude-opus-5"
        assert "claude-opus-5" in models


class TestCodexThrottle:
    @pytest.mark.asyncio
    async def test_forced_refetches_are_throttled_per_provider(self, monkeypatch) -> None:
        fetches: list[int] = []

        async def _boom(top_n: int = 10):
            fetches.append(1)
            raise RuntimeError("openrouter down")

        monkeypatch.setattr(codex_adapter, "_fetch_from_openrouter", _boom)
        monkeypatch.setattr(codex_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 3600)
        monkeypatch.setattr(codex_adapter, "_last_attempt_at", None)

        await codex_adapter.list_models_with_source_async(bypass_cache=True)
        await codex_adapter.list_models_with_source_async(bypass_cache=True)

        assert len(fetches) == 1

    @pytest.mark.asyncio
    async def test_the_sweep_is_exempt_for_codex_too(self, monkeypatch) -> None:
        fetches: list[int] = []

        async def _boom(top_n: int = 10):
            fetches.append(1)
            raise RuntimeError("openrouter down")

        monkeypatch.setattr(codex_adapter, "_fetch_from_openrouter", _boom)
        monkeypatch.setattr(codex_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 3600)
        monkeypatch.setattr(codex_adapter, "_last_attempt_at", None)

        await codex_adapter.list_models_with_source_async(bypass_cache=True)
        await codex_adapter.list_models_with_source_async(bypass_cache=True, force=True)

        assert len(fetches) == 2


    @pytest.mark.asyncio
    async def test_a_throttled_refetch_does_not_revive_an_expired_cache_as_live(self, monkeypatch) -> None:
        # Reporting an expired entry as live would let the throttle launder stale data: the caller
        # caches a live answer for another 24 h.
        codex_adapter._model_cache = (["gpt-stale"], time.time() - codex_adapter._CACHE_TTL - 1)
        monkeypatch.setattr(codex_adapter, "_last_attempt_at", time.time())
        monkeypatch.setattr(codex_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 3600)

        models, source = await codex_adapter.list_models_with_source_async(bypass_cache=True)

        assert source == codex_adapter.SOURCE_DEGRADED
        assert models == ["gpt-stale"]


class TestCodexNoLiveSource:
    @pytest.mark.asyncio
    async def test_a_missing_api_key_is_static_not_degraded(self, monkeypatch) -> None:
        # An absent key is a missing capability, not an outage. Reporting "degraded" would put a
        # permanent red "provider unreachable" banner on every codex picker.
        monkeypatch.setattr(codex_adapter, "OPENROUTER_API_KEY", "")

        models, source = await codex_adapter.list_models_with_source_async()

        assert source == codex_adapter.SOURCE_STATIC
        assert models == codex_adapter._FALLBACK_MODELS
        assert codex_adapter._model_cache is None, "a non-success must not be cached for 24 h"


class TestPeriodicWarmUp:
    @pytest.mark.asyncio
    async def test_the_periodic_sweep_bypasses_the_cache(self, monkeypatch) -> None:
        # A 12 h sweep that honours a 24 h cache would only re-read its own entry and could never
        # discover a newly released model — the whole point of the sweep.
        seen: list[bool] = []

        async def _spy(bypass_cache: bool = False, force: bool = False):
            seen.append((bypass_cache, force))
            return [], "live", {}

        async def _spy_codex(bypass_cache: bool = False, force: bool = False):
            seen.append((bypass_cache, force))
            return [], "live"

        monkeypatch.setattr(claude_adapter, "list_models_with_source_async", _spy)
        monkeypatch.setattr(codex_adapter, "list_models_with_source_async", _spy_codex)

        await main.warm_model_caches(bypass_cache=True)

        assert seen == [(True, True), (True, True)], "the sweep must also bypass the attempt throttle"

    @pytest.mark.asyncio
    async def test_the_startup_sweep_does_not_bypass(self, monkeypatch) -> None:
        seen: list[bool] = []

        async def _spy(bypass_cache: bool = False, force: bool = False):
            seen.append((bypass_cache, force))
            return [], "live", {}

        async def _spy_codex(bypass_cache: bool = False, force: bool = False):
            seen.append((bypass_cache, force))
            return [], "live"

        monkeypatch.setattr(claude_adapter, "list_models_with_source_async", _spy)
        monkeypatch.setattr(codex_adapter, "list_models_with_source_async", _spy_codex)

        await main.warm_model_caches()

        assert seen == [(False, False), (False, False)]


class TestStartupWarmUp:
    @pytest.mark.asyncio
    async def test_warm_up_fills_both_caches(self, monkeypatch) -> None:
        async def _resolves(alias: str) -> str | None:
            return f"claude-{alias}-5"

        async def _ok(top_n: int = 10):
            return ["gpt-9-nova"]

        monkeypatch.setattr(claude_adapter, "_probe_alias", _resolves)
        monkeypatch.setattr(codex_adapter, "_fetch_from_openrouter", _ok)

        await main.warm_model_caches()

        assert claude_adapter._model_cache is not None
        assert codex_adapter._model_cache is not None

    @pytest.mark.asyncio
    async def test_warm_up_swallows_failures(self, monkeypatch) -> None:
        async def _boom(bypass_cache: bool = False, force: bool = False):
            raise RuntimeError("cli missing")

        monkeypatch.setattr(claude_adapter, "list_models_with_source_async", _boom)
        monkeypatch.setattr(codex_adapter, "list_models_with_source_async", _boom)

        await main.warm_model_caches()
