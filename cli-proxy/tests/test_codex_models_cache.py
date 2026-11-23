"""Tests for the codex model catalog read from the CLI's own models_cache.json (D-066)."""
from __future__ import annotations

import json
import os
import sys
import time
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "src"))

import codex_adapter  # noqa: E402


def _write_cache(path: Path, models: list[dict]) -> Path:
    path.write_text(json.dumps({"fetched_at": "2026-11-21T02:41:56Z", "client_version": "0.171.0", "models": models}),
                    encoding="utf-8")
    return path


@pytest.fixture(autouse=True)
def _isolated_cache(monkeypatch, tmp_path):
    monkeypatch.setattr(codex_adapter, "_MODELS_CACHE_PATH", tmp_path / "models_cache.json")
    codex_adapter._model_cache = None
    codex_adapter._cache_mtime = None
    codex_adapter._last_attempt_at = None
    yield
    codex_adapter._model_cache = None
    codex_adapter._cache_mtime = None
    codex_adapter._last_attempt_at = None


class TestReadModelsCache:
    @pytest.mark.asyncio
    async def test_lists_only_visible_models_ordered_by_priority(self) -> None:
        _write_cache(codex_adapter._MODELS_CACHE_PATH, [
            {"slug": "gpt-6.2-astra", "visibility": "list", "priority": 2},
            {"slug": "gpt-reserve", "visibility": "hide", "priority": 4},
            {"slug": "gpt-6.2-sol", "visibility": "list", "priority": 1},
            {"slug": "gpt-6.1-terra", "visibility": "list", "priority": 3},
            {"slug": "codex-auto-review", "visibility": "hide", "priority": 12},
        ])

        models, source = await codex_adapter.list_models_with_source_async()

        assert source == codex_adapter.SOURCE_LIVE
        assert models == ["gpt-6.2-sol", "gpt-6.2-astra", "gpt-6.1-terra"]

    @pytest.mark.asyncio
    async def test_api_only_pro_and_batch_ids_never_appear(self) -> None:
        _write_cache(codex_adapter._MODELS_CACHE_PATH, [
            {"slug": "gpt-6.2-astra-pro", "visibility": "list", "priority": 1},
            {"slug": "gpt-6.2-sol:batch", "visibility": "list", "priority": 2},
            {"slug": " gpt-6.1-astra-pro ", "visibility": "list", "priority": 2},
            {"slug": "gpt-6.2-sol", "visibility": "list", "priority": 3},
        ])

        models, _ = await codex_adapter.list_models_with_source_async()

        assert models == ["gpt-6.2-sol"]

    @pytest.mark.asyncio
    async def test_entries_without_priority_go_last_and_duplicates_collapse(self) -> None:
        _write_cache(codex_adapter._MODELS_CACHE_PATH, [
            {"slug": "gpt-6-sol", "visibility": "list"},
            {"slug": "gpt-6.2-sol", "visibility": "list", "priority": 5},
            {"slug": "gpt-6.2-sol", "visibility": "list", "priority": 6},
            {"slug": "", "visibility": "list", "priority": 1},
            "not-an-object",
        ])

        models, _ = await codex_adapter.list_models_with_source_async()

        assert models == ["gpt-6.2-sol", "gpt-6-sol"]

    @pytest.mark.asyncio
    async def test_missing_file_serves_curated_static_list(self) -> None:
        models, source = await codex_adapter.list_models_with_source_async()

        assert source == codex_adapter.SOURCE_STATIC
        assert models == codex_adapter._FALLBACK_MODELS
        assert codex_adapter._model_cache is None

    @pytest.mark.asyncio
    async def test_broken_json_is_degraded_and_not_cached(self) -> None:
        codex_adapter._MODELS_CACHE_PATH.write_text("{not json", encoding="utf-8")

        models, source = await codex_adapter.list_models_with_source_async()

        assert source == codex_adapter.SOURCE_DEGRADED
        assert models == codex_adapter._FALLBACK_MODELS
        assert codex_adapter._model_cache is None

    @pytest.mark.asyncio
    async def test_schema_without_listed_models_is_degraded(self) -> None:
        _write_cache(codex_adapter._MODELS_CACHE_PATH, [{"slug": "gpt-x", "visibility": "hide"}])

        _, source = await codex_adapter.list_models_with_source_async()

        assert source == codex_adapter.SOURCE_DEGRADED

    @pytest.mark.asyncio
    async def test_a_rewritten_file_is_reread_before_the_ttl_expires(self) -> None:
        path = _write_cache(codex_adapter._MODELS_CACHE_PATH, [{"slug": "gpt-6.2-sol", "visibility": "list", "priority": 1}])
        first, _ = await codex_adapter.list_models_with_source_async()

        _write_cache(path, [{"slug": "gpt-6.3-sol", "visibility": "list", "priority": 1}])
        later = time.time() + 5
        os.utime(path, (later, later))
        second, source = await codex_adapter.list_models_with_source_async()

        assert first == ["gpt-6.2-sol"]
        assert second == ["gpt-6.3-sol"]
        assert source == codex_adapter.SOURCE_LIVE


def test_fallback_contains_the_matrix_codex_models() -> None:
    assert {"gpt-6.2-sol", "gpt-6.2-astra"} <= set(codex_adapter._FALLBACK_MODELS)
    assert not any(m.startswith(("gpt-5", "gpt-4")) or "luna" in m for m in codex_adapter._FALLBACK_MODELS)
