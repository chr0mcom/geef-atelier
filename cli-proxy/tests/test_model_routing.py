"""Model-dependent routing between the two Claude accounts.

Accounts can carry different models. A model only the alternative account has goes straight to
that account (routing, not failover: no failover header, no breaker movement, also with the
failover switched off); everything else stays on the primary first. The knowledge comes from the
per-account alias probes and from "not in plan" answers of real requests -- nothing is hard-wired.
See ../../../../docs/cli-proxy-failover.md (Teil C).
"""
from __future__ import annotations

import asyncio
import json
import os
import sys
import time
from pathlib import Path
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import claude_adapter  # noqa: E402
import failover  # noqa: E402
import main  # noqa: E402
import model_routing  # noqa: E402
from usage import UsageParts  # noqa: E402

NOT_IN_PLAN_TEXT = ("You're out of usage credits. Switch to another model, or manage usage credits at "
                    "https://claude.ai/settings/usage?from=cli to continue.")
NOT_IN_PLAN = RuntimeError(f"claude CLI API error (status=429): {NOT_IN_PLAN_TEXT}")
LIMIT = RuntimeError("claude CLI error: You've hit your session limit · resets 4:20pm (UTC)")
OFF = {"x-cli-proxy-failover": "off"}


def _body(model: str, **extra) -> dict:
    return {"model": model, "messages": [{"role": "user", "content": "hi"}], **extra}


@pytest.fixture()
def alt_home(tmp_path: Path, monkeypatch) -> str:
    home = tmp_path / "claude-alt"
    (home / ".claude").mkdir(parents=True)
    (home / ".claude" / ".credentials.json").write_text("{}")
    monkeypatch.setattr(failover, "ALT_HOME", str(home))
    return str(home)


@pytest.fixture(autouse=True)
def _catalog_state(monkeypatch):
    monkeypatch.setattr(claude_adapter, "_model_cache", None)
    monkeypatch.setattr(claude_adapter, "_last_attempt_at", None)
    monkeypatch.setattr(claude_adapter, "_families", ("opus", "sonnet", "haiku", "fable"))
    monkeypatch.setattr(claude_adapter, "_families_discovered", True)


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


def _on_alt() -> bool:
    return claude_adapter.current_home() != claude_adapter.CLAUDE_HOME


def _probe_by_account(primary: dict, alt: dict, calls: list | None = None):
    async def _probe(alias: str):
        account = "claude-alt" if _on_alt() else "claude"
        if calls is not None:
            calls.append((account, alias))
        return (alt if account == "claude-alt" else primary).get(alias)
    return _probe


PRIMARY_PROBES = {"opus": "claude-opus-5-5", "sonnet": "claude-sonnet-5", "haiku": "claude-haiku-4-5",
       "fable": claude_adapter.NOT_IN_PLAN}
ALT_PROBES = {"opus": "claude-opus-5-5", "sonnet": "claude-sonnet-5", "haiku": "claude-haiku-4-5",
       "fable": "claude-fable-5-2"}


def _seed(monkeypatch, primary_map: dict, alt_map: dict | None = None, primary_lacks=(), alt_lacks=()):
    """Catalog knowledge as a probe run would leave it."""
    now = time.time()
    monkeypatch.setattr(claude_adapter, "_model_cache", (dict(primary_map), now))
    if alt_map is not None:
        monkeypatch.setattr(claude_adapter, "_alt_model_cache", (dict(alt_map), now))
    monkeypatch.setattr(claude_adapter, "_not_in_plan_families",
                        {"claude": set(primary_lacks), "claude-alt": set(alt_lacks)})


def _seed_primary_lacks_fable(monkeypatch):
    _seed(monkeypatch,
          {"claude-opus-latest": "claude-opus-5-5", "claude-sonnet-latest": "claude-sonnet-5"},
          {"claude-opus-latest": "claude-opus-5-5", "claude-fable-latest": "claude-fable-5-2"},
          primary_lacks={"fable"})


def _recording_claude(fail_on_primary: dict[str, Exception] | None = None, text: str = "ok"):
    """complete_with_usage stand-in: records (account, model) and raises per model on the primary."""
    seen: list[tuple[str, str]] = []

    async def _impl(prompt, model, max_tokens, **_):
        account = "claude-alt" if _on_alt() else "claude"
        seen.append((account, model))
        exc = (fail_on_primary or {}).get(claude_adapter._normalize_model(model) or "")
        if account == "claude" and exc is not None:
            raise exc
        return f"{text} via {account}", UsageParts(input_tokens=1, output_tokens=1)
    return AsyncMock(side_effect=_impl), seen


def _ok(text: str = "ok"):
    return AsyncMock(return_value=(text, UsageParts(input_tokens=1, output_tokens=1)))


class TestNotInPlanWording:
    def test_the_observed_answer_is_not_in_plan(self) -> None:
        assert failover.is_not_in_plan(NOT_IN_PLAN) is True

    @pytest.mark.parametrize("text", [
        "This model is not available on your plan.",
        "Your organization does not have access to model claude-x-9.",
        "claude CLI error: model not included in your plan",
    ])
    def test_obvious_plan_wordings(self, text: str) -> None:
        assert failover.is_not_in_plan(text) is True

    @pytest.mark.parametrize("text", [
        "claude CLI error: You've hit your session limit · resets 4:20pm",
        "claude CLI error: You've hit your weekly limit · resets Sep 24, 11pm (UTC)",
        "codex CLI error: rate limit exceeded",
        "claude CLI API error (status=429): Too Many Requests",
        "claude CLI error: Failed to authenticate: OAuth session expired",
        "You're out of usage credits and hit your weekly limit",
    ])
    def test_ordinary_limits_are_never_not_in_plan(self, text: str) -> None:
        assert failover.is_not_in_plan(text) is False

    def test_classification_of_ordinary_faults_is_unchanged(self) -> None:
        assert failover.classify(LIMIT) == "limit"


class TestRuntimeMarks:
    def test_mark_expires(self, monkeypatch) -> None:
        monkeypatch.setattr(failover, "NOT_IN_PLAN_TTL_SECONDS", 0)
        failover.mark_not_in_plan("claude", "fable")
        assert failover.marked_not_in_plan("claude", "fable") is False
        assert failover.not_in_plan_marks() == {}

    def test_mark_holds_and_clears(self) -> None:
        failover.mark_not_in_plan("claude", "fable")
        assert failover.marked_not_in_plan("claude", "fable") is True
        failover.clear_not_in_plan("claude", ["fable"])
        assert failover.marked_not_in_plan("claude", "fable") is False

    def test_marks_are_per_account(self) -> None:
        failover.mark_not_in_plan("claude", "fable")
        assert failover.marked_not_in_plan("claude-alt", "fable") is False


class TestProbe:
    def _process(self, payload: dict):
        class _Proc:
            returncode = 0

            async def communicate(self, input=None):
                return json.dumps(payload).encode(), b""

        async def _spawn(*args, **kwargs):
            return _Proc()
        return _spawn

    @pytest.mark.asyncio
    async def test_not_in_plan_answer(self, monkeypatch) -> None:
        monkeypatch.setattr(asyncio, "create_subprocess_exec", self._process(
            {"is_error": True, "api_error_status": 429, "result": NOT_IN_PLAN_TEXT, "modelUsage": {}}))
        assert await claude_adapter._probe_alias("fable") is claude_adapter.NOT_IN_PLAN

    @pytest.mark.asyncio
    async def test_session_limit_is_unknown(self, monkeypatch) -> None:
        monkeypatch.setattr(asyncio, "create_subprocess_exec", self._process(
            {"is_error": True, "api_error_status": 429,
             "result": "You've hit your session limit · resets 4:20pm", "modelUsage": {}}))
        assert await claude_adapter._probe_alias("opus") is None

    @pytest.mark.asyncio
    async def test_resolved_alias(self, monkeypatch) -> None:
        monkeypatch.setattr(asyncio, "create_subprocess_exec", self._process(
            {"is_error": False, "modelUsage": {"claude-fable-5-2": {}}}))
        assert await claude_adapter._probe_alias("fable") == "claude-fable-5-2"


class TestCatalogUnion:
    @pytest.mark.asyncio
    async def test_both_accounts_are_probed_and_united(self, alt_home, monkeypatch) -> None:
        calls: list = []
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account(PRIMARY_PROBES, ALT_PROBES, calls))

        models, source, aliases = await claude_adapter.list_models_with_source_async()

        assert source == "live"
        assert "claude-fable-5-2" in models
        assert "claude-fable-latest" in models
        assert aliases["claude-fable-latest"] == "claude-fable-5-2"
        assert aliases["claude-opus-latest"] == "claude-opus-5-5"
        assert claude_adapter._not_in_plan_families["claude"] == {"fable"}
        assert not claude_adapter._not_in_plan_families.get("claude-alt")
        assert sorted(a for a, _ in calls).count("claude-alt") == 4
        assert claude_adapter.current_home() == claude_adapter.CLAUDE_HOME, "probe binding leaked"

    @pytest.mark.asyncio
    async def test_primary_resolution_wins_per_family(self, alt_home, monkeypatch) -> None:
        primary = {**PRIMARY_PROBES, "fable": "claude-fable-5"}
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account(primary, ALT_PROBES))

        models, _source, aliases = await claude_adapter.list_models_with_source_async()

        assert aliases["claude-fable-latest"] == "claude-fable-5"
        assert "claude-fable-5" in models and "claude-fable-5-2" in models

    @pytest.mark.asyncio
    async def test_failed_alt_probe_leaves_the_primary_list_live(self, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account(PRIMARY_PROBES, {}))

        models, source, aliases = await claude_adapter.list_models_with_source_async()

        assert source == "live"
        assert "claude-fable-latest" not in aliases
        assert "claude-opus-5-5" in models
        assert claude_adapter._alt_model_cache is None, "a failed probe must never be cached"

    @pytest.mark.asyncio
    async def test_failed_primary_probe_with_live_alt_is_live(self, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account({}, ALT_PROBES))

        models, source, aliases = await claude_adapter.list_models_with_source_async()

        assert source == "live"
        assert aliases["claude-fable-latest"] == "claude-fable-5-2"
        assert claude_adapter._model_cache is None

    @pytest.mark.asyncio
    async def test_both_failing_is_degraded_static(self, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account({}, {}))

        models, source, aliases = await claude_adapter.list_models_with_source_async()

        assert source == "degraded"
        assert aliases == {}
        assert set(claude_adapter.STATIC_MODELS) <= set(models)

    @pytest.mark.asyncio
    async def test_unknown_never_erases_knowledge(self, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 0)
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account(PRIMARY_PROBES, ALT_PROBES))
        await claude_adapter.list_models_with_source_async()

        partial_primary = {"opus": "claude-opus-5-5"}
        partial_alt = {"opus": "claude-opus-5-5"}
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account(partial_primary, partial_alt))
        _models, _source, aliases = await claude_adapter.list_models_with_source_async(bypass_cache=True)

        assert claude_adapter._not_in_plan_families["claude"] == {"fable"}
        assert aliases["claude-fable-latest"] == "claude-fable-5-2"

    @pytest.mark.asyncio
    async def test_an_all_refusing_account_is_unknown_not_lacking(self, alt_home, monkeypatch) -> None:
        refuses = {f: claude_adapter.NOT_IN_PLAN for f in ("opus", "sonnet", "haiku", "fable")}
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account(refuses, ALT_PROBES))

        await claude_adapter.list_models_with_source_async()

        assert not claude_adapter._not_in_plan_families.get("claude")
        assert model_routing.route_to_alt("claude-opus-latest") is None

    @pytest.mark.asyncio
    async def test_a_later_successful_probe_clears_status_and_marks(self, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 0)
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account(PRIMARY_PROBES, ALT_PROBES))
        await claude_adapter.list_models_with_source_async()
        failover.mark_not_in_plan("claude", "claude-fable-5-2")

        granted = {**PRIMARY_PROBES, "fable": "claude-fable-5-2"}
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account(granted, ALT_PROBES))
        await claude_adapter.list_models_with_source_async(bypass_cache=True)

        assert "fable" not in claude_adapter._not_in_plan_families["claude"]
        assert failover.marked_not_in_plan("claude", "claude-fable-5-2") is False
        assert model_routing.route_to_alt("claude-fable-latest") is None

    @pytest.mark.asyncio
    async def test_a_family_newly_refused_leaves_the_primary_mapping(self, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 0)
        granted = {**PRIMARY_PROBES, "fable": "claude-fable-5"}
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account(granted, ALT_PROBES))
        await claude_adapter.list_models_with_source_async()

        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account(PRIMARY_PROBES, ALT_PROBES))
        _models, _source, aliases = await claude_adapter.list_models_with_source_async(bypass_cache=True)

        assert aliases["claude-fable-latest"] == "claude-fable-5-2"
        assert "claude-fable-latest" not in claude_adapter._model_cache[0]

    @pytest.mark.asyncio
    async def test_without_alt_only_the_primary_is_probed(self, monkeypatch) -> None:
        calls: list = []
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account(PRIMARY_PROBES, ALT_PROBES, calls))

        models, _source, aliases = await claude_adapter.list_models_with_source_async()

        assert {a for a, _ in calls} == {"claude"}
        assert len(calls) == 4
        assert "claude-fable-5-2" not in models
        assert "claude-fable-latest" not in aliases

    @pytest.mark.asyncio
    async def test_a_dead_reserve_is_not_reprobed_on_every_call(self, alt_home, monkeypatch) -> None:
        calls: list = []
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 3600)
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account(PRIMARY_PROBES, {}, calls))

        await claude_adapter.list_models_with_source_async()
        await claude_adapter.list_models_with_source_async()
        await claude_adapter.list_models_with_source_async()

        assert sum(1 for a, _ in calls if a == "claude-alt") == 4

    @pytest.mark.asyncio
    async def test_the_sweep_reprobes_the_reserve(self, alt_home, monkeypatch) -> None:
        calls: list = []
        monkeypatch.setattr(claude_adapter, "_MODEL_REFRESH_MIN_INTERVAL", 3600)
        monkeypatch.setattr(claude_adapter, "_probe_alias", _probe_by_account(PRIMARY_PROBES, ALT_PROBES, calls))

        await claude_adapter.list_models_with_source_async()
        await claude_adapter.list_models_with_source_async(bypass_cache=True, force=True)

        assert sum(1 for a, _ in calls if a == "claude-alt") == 8

    def test_endpoints_serve_the_union(self, client, alt_home, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        monkeypatch.setattr(main.provider_sync, "get_provider_config",
                            lambda name: {"name": name, "settings": {"cli_kind": "claude"}})

        for path in ("/v1/claude/models", "/v1/cli/claude-cli/models"):
            body = client.get(path).json()
            ids = [m["id"] for m in body["data"]]
            assert "claude-fable-5-2" in ids, path
            assert body["x_source"] == "live"
            assert body["x_aliases"]["claude-fable-latest"] == "claude-fable-5-2"
            assert body["x_aliases"]["claude-opus-latest"] == "claude-opus-5-5"
        legacy = [m["id"] for m in client.get("/v1/models").json()["data"]]
        assert "claude-fable-5-2" in legacy


class TestRouteDecision:
    @pytest.mark.parametrize("model", [
        "claude-fable-latest", "fable", "anthropic/claude-fable-latest", "claude-fable-5", "claude-fable-5-2",
    ])
    def test_family_not_in_primary_plan(self, alt_home, monkeypatch, model) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        assert model_routing.route_to_alt(model) is not None

    @pytest.mark.parametrize("model", ["claude-opus-latest", "opus", "claude-opus-5-5", "claude-opus-4-1", ""])
    def test_models_of_the_primary_stay(self, alt_home, monkeypatch, model) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        assert model_routing.route_to_alt(model) is None

    def test_without_alt_account_nothing_routes(self, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        assert model_routing.route_to_alt("claude-fable-latest") is None

    def test_alt_known_to_lack_it_too(self, alt_home, monkeypatch) -> None:
        _seed(monkeypatch, {"claude-opus-latest": "claude-opus-5-5"}, {"claude-opus-latest": "claude-opus-5-5"},
              primary_lacks={"fable"}, alt_lacks={"fable"})
        assert model_routing.route_to_alt("claude-fable-latest") is None

    def test_runtime_mark_routes_exactly_that_model(self, alt_home, monkeypatch) -> None:
        _seed(monkeypatch, {}, {})
        failover.mark_not_in_plan("claude", "claude-fable-5")
        assert model_routing.route_to_alt("claude-fable-5") is not None
        assert model_routing.route_to_alt("claude-fable-latest") is None

    def test_concrete_id_only_the_alt_resolves(self, alt_home, monkeypatch) -> None:
        _seed(monkeypatch,
              {"claude-fable-latest": "claude-fable-5"},
              {"claude-fable-latest": "claude-fable-5-2"})
        assert model_routing.route_to_alt("claude-fable-5-2") is not None
        assert model_routing.route_to_alt("claude-fable-5") is None
        assert model_routing.route_to_alt("claude-fable-latest") is None

    def test_concrete_id_with_unknown_primary_family_stays(self, alt_home, monkeypatch) -> None:
        _seed(monkeypatch, {}, {"claude-fable-latest": "claude-fable-5-2"})
        assert model_routing.route_to_alt("claude-fable-5-2") is None


class TestRoutedEndpoint:
    def test_alt_only_model_goes_to_the_alt_without_failover_traces(self, client, alt_home, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        fake, seen = _recording_claude()
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_body("claude-fable-latest"))

        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert "x-cli-proxy-failover" not in resp.headers
        assert seen == [("claude-alt", "claude-fable-latest")]
        assert codex.await_count == 0
        health = client.get("/health").json()["failover"]["backends"]
        assert health["claude"]["consecutive_failures"] == 0
        assert health["claude"]["failover_count"] == 0
        assert health["claude-alt"]["failover_count"] == 0

    @pytest.mark.parametrize("headers", [OFF, {}])
    def test_routing_holds_with_failover_off_and_disabled(self, client, alt_home, monkeypatch, headers) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        if not headers:
            monkeypatch.setattr(failover, "ENABLED", False)
        fake, seen = _recording_claude()
        with patch.object(main.claude_adapter, "complete_with_usage", fake):
            resp = client.post("/v1/claude/chat/completions", json=_body("claude-fable-5-2"), headers=headers)

        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert "x-cli-proxy-failover" not in resp.headers
        assert seen == [("claude-alt", "claude-fable-5-2")]

    def test_failover_off_with_open_alt_breaker_is_503(self, client, alt_home, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        monkeypatch.setattr(failover, "FAILS", 1)
        failover.record_failure("claude-alt", "limit", "weekly limit")
        fake, seen = _recording_claude()
        with patch.object(main.claude_adapter, "complete_with_usage", fake):
            resp = client.post("/v1/claude/chat/completions", json=_body("fable"), headers=OFF)

        assert resp.status_code == 503
        assert resp.json()["error"]["code"] == "backend_unavailable"
        assert seen == []

    def test_routed_alt_fault_hands_over_to_codex_with_failover_headers(self, client, alt_home, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=LIMIT)) as claude, \
             patch.object(main.codex_adapter, "complete_with_usage", _ok("from codex")):
            resp = client.post("/v1/claude/chat/completions", json=_body("claude-fable-latest"))

        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "codex"
        assert resp.headers["x-cli-proxy-failover"] == "claude->codex"
        assert claude.await_count == 1, "the primary lacks the model and must not be tried"
        health = client.get("/health").json()["failover"]["backends"]
        assert health["claude-alt"]["failover_count"] == 1
        assert health["claude-alt"]["consecutive_failures"] == 1
        assert health["claude"]["consecutive_failures"] == 0

    def test_other_models_stay_on_the_primary(self, client, alt_home, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        fake, seen = _recording_claude()
        with patch.object(main.claude_adapter, "complete_with_usage", fake):
            resp = client.post("/v1/claude/chat/completions", json=_body("claude-opus-latest"))

        assert resp.headers["x-cli-proxy-provider"] == "claude"
        assert seen == [("claude", "claude-opus-latest")]

    def test_binding_does_not_leak_into_the_next_request(self, client, alt_home, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        fake, seen = _recording_claude()
        with patch.object(main.claude_adapter, "complete_with_usage", fake):
            client.post("/v1/claude/chat/completions", json=_body("claude-fable-latest"))
            client.post("/v1/claude/chat/completions", json=_body("claude-opus-latest"))
            client.post("/v1/claude/chat/completions", json=_body("claude-fable-latest"))
            client.post("/v1/claude/chat/completions", json=_body("claude-sonnet-latest"))

        assert [a for a, _ in seen] == ["claude-alt", "claude", "claude-alt", "claude"]

    def test_legacy_endpoint_routes_too(self, client, alt_home, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        fake, seen = _recording_claude()
        with patch.object(main.claude_adapter, "complete_with_usage", fake):
            resp = client.post("/v1/chat/completions", json=_body("claude-fable-latest"))

        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert "x-cli-proxy-failover" not in resp.headers

    def test_agentic_decision_file_routes(self, client, alt_home, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        homes: list[str] = []

        async def _decision(instruction, model, **_):
            homes.append("claude-alt" if _on_alt() else "claude")
            return '{"final": "done"}', UsageParts(input_tokens=1, output_tokens=1)

        body = _body("claude-fable-latest", tools=[{"type": "function", "function": {
            "name": "lookup", "parameters": {"type": "object", "properties": {}}}}])
        with patch.object(main.claude_adapter, "complete_agentic_file", AsyncMock(side_effect=_decision)):
            resp = client.post("/v1/claude/chat/completions", json=body)

        assert resp.status_code == 200
        assert homes == ["claude-alt"]
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"

    def test_document_mode_routes(self, client, alt_home, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        homes: list[str] = []

        async def _document(*args, **kwargs):
            homes.append("claude-alt" if _on_alt() else "claude")
            return "written", UsageParts(input_tokens=1, output_tokens=1)

        body = {"model": "claude-fable-latest", "document_mode": True, "document": "",
                "messages": [{"role": "user", "content": "Write."}]}
        with patch.object(main.claude_adapter, "complete_document", AsyncMock(side_effect=_document)):
            resp = client.post("/v1/claude/chat/completions", json=body)

        assert resp.status_code == 200
        assert homes == ["claude-alt"]
        assert "x-cli-proxy-failover" not in resp.headers


class TestRuntimeLearning:
    def test_not_in_plan_answer_moves_to_the_alt_without_touching_the_breaker(
        self, client, alt_home, monkeypatch,
    ) -> None:
        fake, seen = _recording_claude({"fable": NOT_IN_PLAN})
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            first = client.post("/v1/claude/chat/completions", json=_body("claude-fable-latest"))
            second = client.post("/v1/claude/chat/completions", json=_body("claude-fable-latest"))
            third = client.post("/v1/claude/chat/completions", json=_body("claude-opus-latest"))

        for resp in (first, second):
            assert resp.status_code == 200
            assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
            assert "x-cli-proxy-failover" not in resp.headers
        assert third.headers["x-cli-proxy-provider"] == "claude"
        assert [a for a, _ in seen] == ["claude", "claude-alt", "claude-alt", "claude"]
        assert codex.await_count == 0
        snap = client.get("/health").json()
        claude = snap["failover"]["backends"]["claude"]
        assert claude["consecutive_failures"] == 0
        assert claude["healthy"] is True and claude["available"] is True
        assert claude["last_reason"] is None
        assert claude["failover_count"] == 0
        assert "fable" in snap["claude_accounts"]["claude"]["not_in_plan_models"]

    def test_two_fable_requests_never_open_the_primary_breaker(self, client, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(failover, "NOT_IN_PLAN_TTL_SECONDS", 0)
        fake, seen = _recording_claude({"fable": NOT_IN_PLAN})
        with patch.object(main.claude_adapter, "complete_with_usage", fake):
            for _ in range(3):
                client.post("/v1/claude/chat/completions", json=_body("fable"))

        assert failover.available("claude") is True
        assert failover._state("claude").consecutive_failures == 0

    def test_learning_works_with_failover_off(self, client, alt_home) -> None:
        fake, seen = _recording_claude({"fable": NOT_IN_PLAN})
        with patch.object(main.claude_adapter, "complete_with_usage", fake), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_body("fable"), headers=OFF)

        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert "x-cli-proxy-failover" not in resp.headers
        assert codex.await_count == 0

    def test_neither_account_has_it_failover_on_goes_to_codex(self, client, alt_home) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=NOT_IN_PLAN)) as claude, \
             patch.object(main.codex_adapter, "complete_with_usage", _ok("from codex")):
            resp = client.post("/v1/claude/chat/completions", json=_body("fable"))

        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "codex"
        assert resp.headers["x-cli-proxy-failover"] == "claude->codex"
        assert claude.await_count == 2
        assert failover._state("claude").consecutive_failures == 0
        assert failover._state("claude-alt").consecutive_failures == 0

    def test_neither_account_has_it_failover_off_is_404(self, client, alt_home) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=NOT_IN_PLAN)) as claude, \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_body("fable"), headers=OFF)

        assert resp.status_code == 404
        error = resp.json()["error"]
        assert error["code"] == "model_not_found"
        assert error["param"] == "model"
        assert "out of usage credits" in error["message"]
        assert claude.await_count == 2
        assert codex.await_count == 0

    def test_known_lacking_alt_is_skipped(self, client, alt_home, monkeypatch) -> None:
        _seed(monkeypatch, {"claude-opus-latest": "claude-opus-5-5"}, {"claude-opus-latest": "claude-opus-5-5"},
              alt_lacks={"fable"})
        with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=NOT_IN_PLAN)) as claude, \
             patch.object(main.codex_adapter, "complete_with_usage", _ok("from codex")):
            resp = client.post("/v1/claude/chat/completions", json=_body("fable"))

        assert resp.headers["x-cli-proxy-provider"] == "codex"
        assert claude.await_count == 1

    def test_success_clears_the_mark(self, client, alt_home) -> None:
        failover.mark_not_in_plan("claude-alt", "fable")
        failover.mark_not_in_plan("claude", "fable")
        with patch.object(main.claude_adapter, "complete_with_usage", _ok()):
            client.post("/v1/claude/chat/completions", json=_body("fable"))

        assert failover.marked_not_in_plan("claude", "fable") is False

    def test_document_mode_exit_zero_refusal_is_not_an_unchanged_draft(self) -> None:
        with pytest.raises(RuntimeError, match="out of usage credits"):
            claude_adapter._raise_if_not_in_plan(
                {"is_error": True, "api_error_status": 429, "result": NOT_IN_PLAN_TEXT})
        claude_adapter._raise_if_not_in_plan({"is_error": True, "result": "something else"})


class TestOrdinaryLimitsUnchanged:
    def test_session_limit_on_primary_still_counts_and_fails_over(self, client, alt_home) -> None:
        fake, seen = _recording_claude({"claude-haiku-latest": LIMIT, "haiku": LIMIT})
        with patch.object(main.claude_adapter, "complete_with_usage", fake):
            resp = client.post("/v1/claude/chat/completions", json=_body("claude-haiku-latest"))

        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert resp.headers["x-cli-proxy-failover"] == "claude->claude-alt"
        assert failover._state("claude").consecutive_failures == 1
        assert failover._state("claude").last_reason == "limit"
        assert failover.not_in_plan_marks() == {}

    def test_without_alt_account_chain_and_success_are_unchanged(self, client) -> None:
        assert failover.attempt_order("claude") == ["claude", "codex"]
        with patch.object(main.claude_adapter, "complete_with_usage", _ok()) as claude:
            resp = client.post("/v1/claude/chat/completions", json=_body("claude-fable-latest"))
        assert resp.headers["x-cli-proxy-provider"] == "claude"
        assert claude.await_count == 1
        assert "claude-alt" not in client.get("/health").json()["claude_accounts"]

    def test_without_alt_account_limit_still_hands_to_codex(self, client) -> None:
        with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=LIMIT)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok("from codex")):
            resp = client.post("/v1/claude/chat/completions", json=_body("claude-haiku-latest"))
        assert resp.headers["x-cli-proxy-failover"] == "claude->codex"
        assert failover._state("claude").consecutive_failures == 1


class TestStreaming:
    def test_token_stream_is_routed_to_the_alt(self, client, alt_home, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        homes: list[str] = []

        async def _stream(prompt, model, max_tokens, **_):
            homes.append("claude-alt" if _on_alt() else "claude")
            yield ("delta", "streamed")

        with patch.object(main.claude_adapter, "stream", _stream):
            resp = client.post("/v1/claude/chat/completions", json=_body("claude-fable-latest", stream=True))

        assert "streamed" in resp.text
        assert homes == ["claude-alt"]
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert "x-cli-proxy-failover" not in resp.headers

    def test_token_stream_switches_accounts_before_the_first_event(self, client, alt_home) -> None:
        homes: list[str] = []

        async def _stream(prompt, model, max_tokens, **_):
            homes.append("claude-alt" if _on_alt() else "claude")
            if not _on_alt():
                raise NOT_IN_PLAN
            yield ("delta", "streamed by alt")

        with patch.object(main.claude_adapter, "stream", _stream):
            resp = client.post("/v1/claude/chat/completions",
                               json=_body("claude-fable-latest", stream=True), headers=OFF)

        assert "streamed by alt" in resp.text
        assert homes == ["claude", "claude-alt"]
        assert failover._state("claude").consecutive_failures == 0
        assert failover.marked_not_in_plan("claude", "fable") is True

    def test_buffered_stream_learns_and_routes(self, client, alt_home) -> None:
        fake, seen = _recording_claude({"fable": NOT_IN_PLAN}, text='{"a": 1}')

        async def _json(prompt, model, max_tokens, **kw):
            await fake(prompt, model, max_tokens, **kw)
            return '{"a": 1}', UsageParts(input_tokens=1, output_tokens=1)

        body = _body("fable", stream=True, response_format={"type": "json_object"})
        with patch.object(main.claude_adapter, "complete_with_usage", AsyncMock(side_effect=_json)), \
             patch.object(main.codex_adapter, "complete_with_usage", _ok()) as codex:
            resp = client.post("/v1/claude/chat/completions", json=body)

        assert resp.status_code == 200
        assert '{\\"a\\": 1}' in resp.text
        assert [a for a, _ in seen] == ["claude", "claude-alt"]
        assert codex.await_count == 0


class TestGenericEndpoint:
    @pytest.fixture(autouse=True)
    def _claude_provider(self, monkeypatch):
        monkeypatch.setattr(main.provider_sync, "get_provider_config",
                            lambda name: {"name": name, "settings": {"cli_kind": "claude"}})

    def test_alt_only_model_is_served_by_the_alt(self, client, alt_home, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        fake, seen = _recording_claude()
        with patch.object(claude_adapter, "complete_with_usage", fake):
            resp = client.post("/v1/cli/claude-cli/chat/completions", json=_body("anthropic/claude-fable-latest"))

        assert resp.status_code == 200
        assert resp.headers["x-cli-proxy-provider"] == "claude-alt"
        assert seen == [("claude-alt", "anthropic/claude-fable-latest")]

    def test_not_in_plan_answer_switches_to_the_alt(self, client, alt_home) -> None:
        fake, seen = _recording_claude({"fable": NOT_IN_PLAN})
        with patch.object(claude_adapter, "complete_with_usage", fake):
            resp = client.post("/v1/cli/claude-cli/chat/completions", json=_body("claude-fable-latest"))

        assert resp.status_code == 200
        assert [a for a, _ in seen] == ["claude", "claude-alt"]
        assert failover._state("claude").consecutive_failures == 0

    def test_binding_does_not_leak_between_generic_requests(self, client, alt_home, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        fake, seen = _recording_claude()
        with patch.object(claude_adapter, "complete_with_usage", fake):
            client.post("/v1/cli/claude-cli/chat/completions", json=_body("claude-fable-latest"))
            client.post("/v1/cli/claude-cli/chat/completions", json=_body("claude-opus-latest"))

        assert [a for a, _ in seen] == ["claude-alt", "claude"]

    def test_without_alt_account_unchanged(self, client) -> None:
        fake, seen = _recording_claude()
        with patch.object(claude_adapter, "complete_with_usage", fake):
            resp = client.post("/v1/cli/claude-cli/chat/completions", json=_body("claude-fable-latest"))
        assert resp.status_code == 200
        assert seen == [("claude", "claude-fable-latest")]

    def test_without_alt_account_not_in_plan_is_404(self, client) -> None:
        with patch.object(claude_adapter, "complete_with_usage", AsyncMock(side_effect=NOT_IN_PLAN)):
            resp = client.post("/v1/cli/claude-cli/chat/completions", json=_body("claude-fable-latest"))
        assert resp.status_code == 404
        assert resp.json()["error"]["code"] == "model_not_found"

    def test_ordinary_errors_stay_502(self, client, alt_home) -> None:
        with patch.object(claude_adapter, "complete_with_usage", AsyncMock(side_effect=LIMIT)) as claude:
            resp = client.post("/v1/cli/claude-cli/chat/completions", json=_body("claude-opus-latest"))
        assert resp.status_code == 502
        assert claude.await_count == 1


class TestHealth:
    def test_accounts_field(self, client, alt_home, monkeypatch) -> None:
        _seed_primary_lacks_fable(monkeypatch)
        failover.mark_not_in_plan("claude", "claude-fable-5-2")

        accounts = client.get("/health").json()["claude_accounts"]

        assert accounts["claude"]["available_families"]["opus"] == "claude-opus-5-5"
        assert accounts["claude"]["not_in_plan_families"] == ["fable"]
        assert "claude-fable-5-2" in accounts["claude"]["not_in_plan_models"]
        assert accounts["claude"]["reserve"] is False
        assert accounts["claude-alt"]["available_families"]["fable"] == "claude-fable-5-2"
        assert accounts["claude-alt"]["reserve"] is True

    def test_reserve_still_not_in_degraded(self, client, alt_home, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 1)
        failover.record_failure("claude-alt", "auth", "expired")
        assert client.get("/health").json()["degraded"] is False
