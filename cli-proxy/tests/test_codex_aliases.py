"""Tests for the always-latest codex aliases (gpt-<line>-latest)."""
from __future__ import annotations

import asyncio
import json
import os
import sys
from pathlib import Path
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import codex_adapter  # noqa: E402
import main  # noqa: E402
from usage import UsageParts  # noqa: E402

LIVE_LIST = [
    "gpt-6.2-sol", "gpt-6.2-astra", "gpt-6.1-terra", "gpt-6.1-sol", "gpt-6-astra",
    "gpt-6-sol", "gpt-6-luna", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna",
]
LIVE_ALIASES = {
    "gpt-sol-latest": "gpt-6.2-sol",
    "gpt-astra-latest": "gpt-6.2-astra",
    "gpt-terra-latest": "gpt-6.1-terra",
    "gpt-luna-latest": "gpt-6-luna",
}


def _write_cache(path: Path, slugs: list[str], extra: list[dict] | None = None) -> None:
    entries = [{"slug": s, "visibility": "list", "priority": i} for i, s in enumerate(slugs)]
    path.write_text(json.dumps({"models": entries + (extra or [])}), encoding="utf-8")


@pytest.fixture(autouse=True)
def _isolated_cache(monkeypatch, tmp_path):
    monkeypatch.setattr(codex_adapter, "_MODELS_CACHE_PATH", tmp_path / "models_cache.json")
    monkeypatch.setattr(codex_adapter, "_semaphore", asyncio.Semaphore(codex_adapter.MAX_CONCURRENT))
    codex_adapter._model_cache = None
    codex_adapter._cache_mtime = None
    codex_adapter._last_attempt_at = None
    yield
    codex_adapter._model_cache = None
    codex_adapter._cache_mtime = None
    codex_adapter._last_attempt_at = None


@pytest.fixture()
def live_cache() -> None:
    _write_cache(codex_adapter._MODELS_CACHE_PATH, LIVE_LIST)


class TestLineDerivation:
    def test_line_is_the_last_segment_of_the_id(self) -> None:
        assert codex_adapter._parse_model_id("gpt-6.2-sol") == ((6, 2), "sol")
        assert codex_adapter._parse_model_id("gpt-6-luna") == ((6,), "luna")
        assert codex_adapter._parse_model_id("gpt-5.6-terra") == ((5, 6), "terra")

    def test_unknown_new_line_gets_an_alias_without_code_change(self) -> None:
        assert codex_adapter.build_alias_map(["gpt-7-nova", "gpt-6.2-sol"]) == {
            "gpt-nova-latest": "gpt-7-nova",
            "gpt-sol-latest": "gpt-6.2-sol",
        }

    @pytest.mark.parametrize("model_id", [
        "codex-auto-review", "gpt-6.2-sol-mini", "gpt-6.2", "o3-sol", "gpt-sol-latest", "gpt-6.2-sol:batch", "gpt-6.2-pro",
    ])
    def test_ids_outside_the_pattern_have_no_line(self, model_id: str) -> None:
        assert codex_adapter._parse_model_id(model_id) is None

    def test_alias_names_one_line(self) -> None:
        assert codex_adapter.alias_for_line("astra") == "gpt-astra-latest"


class TestVersionComparison:
    def test_live_list_resolves_every_line_to_its_newest_model(self) -> None:
        assert codex_adapter.build_alias_map(LIVE_LIST) == LIVE_ALIASES

    def test_numeric_not_lexicographic(self) -> None:
        """Lexicographically "gpt-6.9" > "gpt-6.10" and "gpt-6" > "gpt-10"; numerically it is the reverse."""
        assert codex_adapter.build_alias_map(["gpt-6.9-sol", "gpt-6.10-sol"]) == {"gpt-sol-latest": "gpt-6.10-sol"}
        assert codex_adapter.build_alias_map(["gpt-6-sol", "gpt-10-sol"]) == {"gpt-sol-latest": "gpt-10-sol"}

    def test_list_order_does_not_matter_for_different_versions(self) -> None:
        assert codex_adapter.build_alias_map(["gpt-5.6-sol", "gpt-6-sol", "gpt-6.2-sol"]) == {
            "gpt-sol-latest": "gpt-6.2-sol"}

    def test_equal_versions_fall_back_to_list_order(self) -> None:
        assert codex_adapter.build_alias_map(["gpt-6-sol", "gpt-6.0-sol"]) == {"gpt-sol-latest": "gpt-6-sol"}
        assert codex_adapter.build_alias_map(["gpt-6.0-sol", "gpt-6-sol"]) == {"gpt-sol-latest": "gpt-6.0-sol"}

    def test_aliases_follow_first_appearance_of_their_line(self) -> None:
        assert list(codex_adapter.build_alias_map(LIVE_LIST)) == [
            "gpt-sol-latest", "gpt-astra-latest", "gpt-terra-latest", "gpt-luna-latest"]


class TestExclusions:
    @pytest.mark.asyncio
    async def test_pro_batch_and_hidden_entries_never_win(self) -> None:
        _write_cache(codex_adapter._MODELS_CACHE_PATH, ["gpt-6.2-sol", "gpt-6.1-astra"], extra=[
            {"slug": "gpt-9-sol-pro", "visibility": "list", "priority": 90},
            {"slug": "gpt-9-sol:batch", "visibility": "list", "priority": 91},
            {"slug": "gpt-9-astra", "visibility": "hide", "priority": 92},
        ])

        _models, _source, aliases = await codex_adapter.list_models_with_aliases_async()

        assert aliases == {"gpt-sol-latest": "gpt-6.2-sol", "gpt-astra-latest": "gpt-6.1-astra"}

    def test_pro_id_in_the_input_is_ignored_defensively(self) -> None:
        assert codex_adapter.build_alias_map(["gpt-9-pro", "gpt-6-sol"]) == {"gpt-sol-latest": "gpt-6-sol"}


class TestFallbackResolution:
    @pytest.mark.asyncio
    async def test_missing_cache_resolves_against_the_curated_fallback(self) -> None:
        models, source, aliases = await codex_adapter.list_models_with_aliases_async()

        assert source == codex_adapter.SOURCE_STATIC
        assert aliases == {"gpt-sol-latest": "gpt-6.2-sol", "gpt-astra-latest": "gpt-6.2-astra",
                           "gpt-terra-latest": "gpt-6.1-terra"}
        assert models == [*aliases, *codex_adapter._FALLBACK_MODELS]

    @pytest.mark.asyncio
    async def test_unusable_cache_is_degraded_and_still_resolves(self) -> None:
        codex_adapter._MODELS_CACHE_PATH.write_text("{not json", encoding="utf-8")

        _models, source, aliases = await codex_adapter.list_models_with_aliases_async()

        assert source == codex_adapter.SOURCE_DEGRADED
        assert aliases["gpt-sol-latest"] == "gpt-6.2-sol"

    @pytest.mark.asyncio
    async def test_fallback_serves_execution_too(self) -> None:
        assert await codex_adapter.resolve_model_alias("gpt-sol-latest") == "gpt-6.2-sol"


class TestListComposition:
    @pytest.mark.asyncio
    async def test_aliases_first_then_concrete_ids_as_before(self, live_cache) -> None:
        models, source, aliases = await codex_adapter.list_models_with_aliases_async()

        assert source == codex_adapter.SOURCE_LIVE
        assert models == [*LIVE_ALIASES, *LIVE_LIST]
        assert aliases == LIVE_ALIASES

    @pytest.mark.asyncio
    async def test_plain_listing_stays_concrete_only(self, live_cache) -> None:
        models, _source = await codex_adapter.list_models_with_source_async()

        assert models == LIVE_LIST


class TestResolveModelAlias:
    @pytest.mark.asyncio
    async def test_alias_resolves_to_the_concrete_id(self, live_cache) -> None:
        assert await codex_adapter.resolve_model_alias("gpt-astra-latest") == "gpt-6.2-astra"

    @pytest.mark.asyncio
    async def test_provider_prefix_is_ignored_for_the_lookup(self, live_cache) -> None:
        assert await codex_adapter.resolve_model_alias("openai/gpt-sol-latest") == "gpt-6.2-sol"

    @pytest.mark.asyncio
    async def test_unknown_alias_is_not_replaced(self, live_cache) -> None:
        assert await codex_adapter.resolve_model_alias("gpt-nova-latest") == "gpt-nova-latest"
        assert await codex_adapter.resolve_model_alias("openai/gpt-nova-latest") == "openai/gpt-nova-latest"

    @pytest.mark.asyncio
    async def test_concrete_and_empty_models_pass_through_without_touching_the_cache(self) -> None:
        with patch.object(codex_adapter, "_get_cached_models", AsyncMock(side_effect=AssertionError("cache read"))):
            assert await codex_adapter.resolve_model_alias("gpt-6.2-sol") == "gpt-6.2-sol"
            assert await codex_adapter.resolve_model_alias("") == ""
            assert await codex_adapter.resolve_model_alias(None) is None

    @pytest.mark.asyncio
    async def test_alias_follows_a_changed_cache(self, live_cache) -> None:
        assert await codex_adapter.resolve_model_alias("gpt-sol-latest") == "gpt-6.2-sol"

        _write_cache(codex_adapter._MODELS_CACHE_PATH, ["gpt-7-sol", *LIVE_LIST])
        os.utime(codex_adapter._MODELS_CACHE_PATH, (1, 1_000_000_000))

        assert await codex_adapter.resolve_model_alias("gpt-sol-latest") == "gpt-7-sol"


class _Stop(RuntimeError):
    pass


async def _captured_argv(call) -> list[str]:
    """Run `call` against a spawn that records argv and aborts, so no CLI is needed."""
    captured: list[list[str]] = []

    async def fake_exec(*args, **kwargs):
        captured.append(list(args))
        raise _Stop

    with patch("asyncio.create_subprocess_exec", fake_exec):
        with pytest.raises(_Stop):
            await call()
    return captured[0]


def _model_flag(argv: list[str]) -> str:
    return argv[argv.index("-m") + 1]


class TestCliInvocation:
    @pytest.mark.asyncio
    @pytest.mark.parametrize("requested", ["gpt-sol-latest", "openai/gpt-sol-latest"])
    async def test_complete_passes_the_concrete_id(self, live_cache, requested: str) -> None:
        argv = await _captured_argv(lambda: codex_adapter.complete("hi", requested, None))

        assert _model_flag(argv) == "gpt-6.2-sol"
        assert "gpt-sol-latest" not in argv

    @pytest.mark.asyncio
    async def test_stream_passes_the_concrete_id(self, live_cache) -> None:
        async def _drain() -> None:
            async for _ in codex_adapter.stream("hi", "gpt-astra-latest", None):
                pass

        assert _model_flag(await _captured_argv(_drain)) == "gpt-6.2-astra"

    @pytest.mark.asyncio
    async def test_document_mode_passes_the_concrete_id(self, live_cache, tmp_path) -> None:
        (tmp_path / "draft.md").write_text("draft", encoding="utf-8")

        argv = await _captured_argv(lambda: codex_adapter.complete_document(
            "sys", "do it", "draft", tmp_path, "gpt-terra-latest", None))

        assert _model_flag(argv) == "gpt-6.1-terra"

    @pytest.mark.asyncio
    async def test_agentic_decision_mode_passes_the_concrete_id(self, live_cache) -> None:
        argv = await _captured_argv(lambda: codex_adapter.complete_agentic_file("plan", "gpt-luna-latest"))

        assert _model_flag(argv) == "gpt-6-luna"

    @pytest.mark.asyncio
    async def test_unknown_alias_reaches_the_cli_unchanged(self, live_cache) -> None:
        argv = await _captured_argv(lambda: codex_adapter.complete("hi", "openai/gpt-nova-latest", None))

        assert _model_flag(argv) == "gpt-nova-latest"

    @pytest.mark.asyncio
    async def test_concrete_id_is_untouched(self, live_cache) -> None:
        argv = await _captured_argv(lambda: codex_adapter.complete("hi", "gpt-6.1-sol", None))

        assert _model_flag(argv) == "gpt-6.1-sol"

    @pytest.mark.asyncio
    async def test_empty_model_still_omits_the_flag(self, live_cache) -> None:
        argv = await _captured_argv(lambda: codex_adapter.complete("hi", "", None))

        assert "-m" not in argv


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


class TestEndpoints:
    def test_codex_models_lists_aliases_first_with_x_aliases(self, client: TestClient, live_cache) -> None:
        body = client.get("/v1/codex/models?refresh=1").json()

        ids = [m["id"] for m in body["data"]]
        assert ids == [*LIVE_ALIASES, *LIVE_LIST]
        assert body["x_aliases"] == LIVE_ALIASES
        assert body["x_source"] == "live"
        assert {"id": "gpt-sol-latest", "object": "model", "owned_by": "openai"} in body["data"]

    def test_codex_models_without_cache_uses_fallback_aliases(self, client: TestClient) -> None:
        body = client.get("/v1/codex/models?refresh=1").json()

        assert body["x_source"] == "static"
        assert body["x_aliases"]["gpt-sol-latest"] == "gpt-6.2-sol"
        assert [m["id"] for m in body["data"]][0] == "gpt-sol-latest"

    def test_cli_provider_endpoint_exposes_codex_aliases(self, client: TestClient, monkeypatch, live_cache) -> None:
        monkeypatch.setattr(main.provider_sync, "get_provider_config",
                            lambda name: {"name": name, "settings": {"cli_kind": "codex"}})

        body = client.get("/v1/cli/codex-cli/models?refresh=1").json()

        assert [m["id"] for m in body["data"]] == [*LIVE_ALIASES, *LIVE_LIST]
        assert body["x_aliases"] == LIVE_ALIASES
        assert body["x_source"] == "live"


class TestCompletionResponse:
    def _post(self, client: TestClient, model: str, **extra):
        mock = AsyncMock(return_value=("ok", UsageParts(input_tokens=1, output_tokens=1)))
        with patch.object(main.codex_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/codex/chat/completions", json={
                "model": model, "messages": [{"role": "user", "content": "hi"}], **extra})
        return resp, mock

    def test_response_and_cli_call_carry_the_concrete_id(self, client: TestClient, live_cache) -> None:
        resp, mock = self._post(client, "openai/gpt-sol-latest")

        assert resp.status_code == 200
        assert resp.json()["model"] == "gpt-6.2-sol"
        assert mock.await_args.args[1] == "gpt-6.2-sol"

    def test_unknown_alias_is_echoed_and_passed_on_as_is(self, client: TestClient, live_cache) -> None:
        resp, mock = self._post(client, "gpt-nova-latest")

        assert resp.json()["model"] == "gpt-nova-latest"
        assert mock.await_args.args[1] == "gpt-nova-latest"

    def test_streaming_frames_carry_the_concrete_id(self, client: TestClient, live_cache) -> None:
        async def fake_stream(prompt, model, max_tokens, **kwargs):
            assert model == "gpt-6.2-astra"
            yield ("delta", "hello")
            yield ("usage", UsageParts(input_tokens=1, output_tokens=1))

        with patch.object(main.codex_adapter, "stream", fake_stream):
            resp = client.post("/v1/codex/chat/completions", json={
                "model": "gpt-astra-latest", "stream": True,
                "messages": [{"role": "user", "content": "hi"}]})

        assert resp.status_code == 200
        assert '"model":"gpt-6.2-astra"' in resp.text.replace(" ", "")

    def test_legacy_endpoint_resolves_the_alias_too(self, client: TestClient, live_cache) -> None:
        mock = AsyncMock(return_value=("ok", UsageParts(input_tokens=1, output_tokens=1)))
        with patch.object(main.codex_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/chat/completions", json={
                "model": "gpt-terra-latest", "messages": [{"role": "user", "content": "hi"}]})

        assert resp.status_code == 200
        assert resp.json()["model"] == "gpt-6.1-terra"
        assert mock.await_args.args[1] == "gpt-6.1-terra"

    def test_generic_cli_endpoint_resolves_the_alias(self, client: TestClient, monkeypatch, live_cache) -> None:
        monkeypatch.setattr(main.provider_sync, "get_provider_config",
                            lambda name: {"name": name, "settings": {"cli_kind": "codex"}})
        mock = AsyncMock(return_value=("ok", UsageParts(input_tokens=1, output_tokens=1)))
        with patch.object(codex_adapter, "complete_with_usage", mock):
            resp = client.post("/v1/cli/codex-cli/chat/completions", json={
                "model": "openai/gpt-sol-latest", "messages": [{"role": "user", "content": "hi"}]})

        assert resp.status_code == 200
        assert resp.json()["model"] == "gpt-6.2-sol"
        assert mock.await_args.args[1] == "gpt-6.2-sol"


class TestAliasesComplete:
    """`x_aliases_complete` and the guarantee that the reported map is the one a call uses."""

    @staticmethod
    def _cli_config(monkeypatch) -> None:
        monkeypatch.setattr(main.provider_sync, "get_provider_config",
                            lambda name: {"name": name, "settings": {"cli_kind": "codex"}})

    async def _assert_map_is_what_a_call_uses(self, aliases: dict[str, str]) -> None:
        assert aliases
        for alias, concrete in aliases.items():
            assert await codex_adapter.resolve_model_alias(alias) == concrete
            assert await codex_adapter.resolve_model_alias(f"openai/{alias}") == concrete
            assert await codex_adapter._model_arg(alias) == concrete
            assert await codex_adapter._model_arg(f"openai/{alias}") == concrete
        assert await codex_adapter.resolve_model_alias("gpt-nova-latest") == "gpt-nova-latest"

    @pytest.mark.asyncio
    async def test_live_flag_and_map_match_the_call_path(self, client: TestClient, monkeypatch, live_cache) -> None:
        self._cli_config(monkeypatch)
        for url in ("/v1/codex/models?refresh=1", "/v1/cli/codex-cli/models?refresh=1"):
            body = client.get(url).json()

            assert body["x_source"] == "live"
            assert body["x_aliases_complete"] is True
            await self._assert_map_is_what_a_call_uses(body["x_aliases"])

    @pytest.mark.asyncio
    async def test_static_flag_and_map_match_the_call_path(self, client: TestClient, monkeypatch) -> None:
        self._cli_config(monkeypatch)
        for url in ("/v1/codex/models?refresh=1", "/v1/cli/codex-cli/models?refresh=1"):
            codex_adapter._last_attempt_at = None
            body = client.get(url).json()

            assert body["x_source"] == "static"
            assert body["x_aliases_complete"] is True
            assert body["x_aliases"]["gpt-sol-latest"] == "gpt-6.2-sol"
            await self._assert_map_is_what_a_call_uses(body["x_aliases"])

    @pytest.mark.asyncio
    async def test_degraded_with_a_previous_live_read_reports_that_map(self, client: TestClient, monkeypatch) -> None:
        _write_cache(codex_adapter._MODELS_CACHE_PATH, ["gpt-7-sol", *LIVE_LIST])
        assert client.get("/v1/codex/models?refresh=1").json()["x_aliases"]["gpt-sol-latest"] == "gpt-7-sol"
        codex_adapter._MODELS_CACHE_PATH.write_text("{not json", encoding="utf-8")
        codex_adapter._last_attempt_at = None

        body = client.get("/v1/codex/models?refresh=1").json()

        assert body["x_source"] == "degraded"
        assert body["x_aliases_complete"] is True
        assert body["x_aliases"]["gpt-sol-latest"] == "gpt-7-sol"
        await self._assert_map_is_what_a_call_uses(body["x_aliases"])

    @pytest.mark.asyncio
    async def test_degraded_without_any_live_read_reports_the_fallback_map(self, client: TestClient) -> None:
        codex_adapter._MODELS_CACHE_PATH.write_text("{not json", encoding="utf-8")

        body = client.get("/v1/codex/models?refresh=1").json()

        assert body["x_source"] == "degraded"
        assert body["x_aliases_complete"] is True
        assert body["x_aliases"]["gpt-sol-latest"] == "gpt-6.2-sol"
        await self._assert_map_is_what_a_call_uses(body["x_aliases"])

    def test_claude_endpoints_do_not_carry_the_flag(self, client: TestClient, monkeypatch) -> None:
        monkeypatch.setattr(main.provider_sync, "get_provider_config",
                            lambda name: {"name": name, "settings": {"cli_kind": "claude"}})
        monkeypatch.setattr(main.claude_adapter, "list_models_with_source_async",
                            AsyncMock(return_value=(["claude-opus-latest"], "live", {"claude-opus-latest": "claude-opus-5"})))

        assert "x_aliases_complete" not in client.get("/v1/claude/models").json()
        assert "x_aliases_complete" not in client.get("/v1/cli/claude-cli/models").json()


class TestDefaultModel:
    """A request without `model` and the failover share one default, which always resolves concretely."""

    def test_failover_and_adapter_share_the_constant(self) -> None:
        assert main.FAILOVER_CODEX_MODEL is codex_adapter.FAILOVER_MODEL
        assert codex_adapter.FAILOVER_MODEL.endswith(codex_adapter.DEFAULT_CODEX_MODEL)
        assert codex_adapter.DEFAULT_CODEX_MODEL == "gpt-sol-latest"

    async def _model_of_a_request_without_model(self) -> tuple[str, str]:
        from adapters.codex_adapter import CodexAdapter

        mock = AsyncMock(return_value=("ok", UsageParts(input_tokens=1, output_tokens=1)))
        with patch.object(codex_adapter, "complete_with_usage", mock):
            result = await CodexAdapter().execute({}, {"messages": [{"role": "user", "content": "hi"}]})
        return mock.await_args.args[1], result["model"]

    @pytest.mark.asyncio
    async def test_live_list_resolves_to_the_newest_sol(self, live_cache) -> None:
        assert await self._model_of_a_request_without_model() == ("gpt-6.2-sol", "gpt-6.2-sol")

    @pytest.mark.asyncio
    async def test_static_fallback_list_resolves_to_a_concrete_sol(self) -> None:
        called, reported = await self._model_of_a_request_without_model()

        assert called == reported == "gpt-6.2-sol"
        assert called in codex_adapter._FALLBACK_MODELS


class TestLiveToStaticTransition:
    """A cache file that disappears after a live read must yield the curated list, never the stale live one."""

    @pytest.mark.asyncio
    async def test_removed_cache_file_serves_the_curated_list_and_keeps_the_failover_alias_resolvable(self, tmp_path, monkeypatch) -> None:
        import json as _json
        cache = tmp_path / "models_cache.json"
        cache.write_text(_json.dumps({"models": [{"slug": "gpt-6.2-astra", "visibility": "list", "priority": 1}]}), encoding="utf-8")
        monkeypatch.setattr(codex_adapter, "_MODELS_CACHE_PATH", cache)
        monkeypatch.setattr(codex_adapter, "_model_cache", None)
        monkeypatch.setattr(codex_adapter, "_cache_mtime", None)
        monkeypatch.setattr(codex_adapter, "_last_attempt_at", None)

        models, source = await codex_adapter.list_models_with_source_async()
        assert source == codex_adapter.SOURCE_LIVE
        assert models == ["gpt-6.2-astra"]

        cache.unlink()
        monkeypatch.setattr(codex_adapter, "_last_attempt_at", None)
        models, source = await codex_adapter.list_models_with_source_async(bypass_cache=True, force=True)

        assert source == codex_adapter.SOURCE_STATIC
        assert models == codex_adapter._FALLBACK_MODELS
        resolved = await codex_adapter.resolve_model_alias(codex_adapter.DEFAULT_CODEX_MODEL)
        assert resolved != codex_adapter.DEFAULT_CODEX_MODEL
        assert resolved.endswith("-sol")


class TestFailoverDefaultWithoutSolLine:
    """The internal failover default must never reach `codex -m` unresolved."""

    @pytest.mark.asyncio
    async def test_no_sol_line_means_no_model_flag(self, tmp_path, monkeypatch) -> None:
        import json as _json
        cache = tmp_path / "models_cache.json"
        cache.write_text(_json.dumps({"models": [{"slug": "gpt-6.2-astra", "visibility": "list", "priority": 1}]}), encoding="utf-8")
        monkeypatch.setattr(codex_adapter, "_MODELS_CACHE_PATH", cache)
        monkeypatch.setattr(codex_adapter, "_model_cache", None)
        monkeypatch.setattr(codex_adapter, "_cache_mtime", None)
        monkeypatch.setattr(codex_adapter, "_last_attempt_at", None)

        assert await codex_adapter._model_arg(codex_adapter.FAILOVER_MODEL) == ""
        assert await codex_adapter._model_arg(codex_adapter.DEFAULT_CODEX_MODEL) == "gpt-sol-latest"
        assert await codex_adapter._model_arg("openai/gpt-sol-latest") == "gpt-sol-latest"
        assert await codex_adapter._model_arg("gpt-astra-latest") == "gpt-6.2-astra"
        assert await codex_adapter._model_arg("gpt-nova-latest") == "gpt-nova-latest"

    @pytest.mark.asyncio
    async def test_throttled_refresh_after_cache_loss_reports_the_curated_list(self, tmp_path, monkeypatch) -> None:
        import json as _json
        cache = tmp_path / "models_cache.json"
        cache.write_text(_json.dumps({"models": [{"slug": "gpt-6.2-astra", "visibility": "list", "priority": 1}]}), encoding="utf-8")
        monkeypatch.setattr(codex_adapter, "_MODELS_CACHE_PATH", cache)
        monkeypatch.setattr(codex_adapter, "_model_cache", None)
        monkeypatch.setattr(codex_adapter, "_cache_mtime", None)
        monkeypatch.setattr(codex_adapter, "_last_attempt_at", None)
        await codex_adapter.list_models_with_source_async()

        cache.unlink()
        models, source = await codex_adapter.list_models_with_source_async(bypass_cache=True)

        assert source == codex_adapter.SOURCE_STATIC
        assert models == codex_adapter._FALLBACK_MODELS


class TestFailoverMarkerWithSolLine:
    @pytest.mark.asyncio
    async def test_failover_marker_resolves_to_the_newest_sol(self, tmp_path, monkeypatch) -> None:
        import json as _json
        cache = tmp_path / "models_cache.json"
        cache.write_text(_json.dumps({"models": [
            {"slug": "gpt-6.2-sol", "visibility": "list", "priority": 1},
            {"slug": "gpt-6.1-sol", "visibility": "list", "priority": 2},
        ]}), encoding="utf-8")
        monkeypatch.setattr(codex_adapter, "_MODELS_CACHE_PATH", cache)
        monkeypatch.setattr(codex_adapter, "_model_cache", None)
        monkeypatch.setattr(codex_adapter, "_cache_mtime", None)
        monkeypatch.setattr(codex_adapter, "_last_attempt_at", None)

        assert await codex_adapter._model_arg(codex_adapter.FAILOVER_MODEL) == "gpt-6.2-sol"
