"""Tests for /v1/claude/models and /v1/codex/models endpoints."""
from __future__ import annotations

import sys
import os

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import main  # noqa: E402


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app)


class TestClaudeModelsEndpoint:
    def test_returns_200(self, client: TestClient) -> None:
        resp = client.get("/v1/claude/models")
        assert resp.status_code == 200

    def test_returns_openai_compatible_schema(self, client: TestClient) -> None:
        data = client.get("/v1/claude/models").json()
        assert data["object"] == "list"
        assert isinstance(data["data"], list)
        assert len(data["data"]) > 0

    def test_all_entries_have_id_and_object_fields(self, client: TestClient) -> None:
        models = client.get("/v1/claude/models").json()["data"]
        for m in models:
            assert "id" in m, f"Missing 'id' in entry: {m}"
            assert m["object"] == "model", f"Expected 'model', got '{m['object']}'"

    def test_contains_claude_models(self, client: TestClient) -> None:
        ids = {m["id"] for m in client.get("/v1/claude/models").json()["data"]}
        assert any("claude" in mid for mid in ids), f"No claude model found in {ids}"

    def test_all_owned_by_anthropic(self, client: TestClient) -> None:
        models = client.get("/v1/claude/models").json()["data"]
        for m in models:
            assert m.get("owned_by") == "anthropic"


class TestCodexModelsEndpoint:
    def test_returns_200(self, client: TestClient) -> None:
        resp = client.get("/v1/codex/models")
        assert resp.status_code == 200

    def test_returns_openai_compatible_schema(self, client: TestClient) -> None:
        data = client.get("/v1/codex/models").json()
        assert data["object"] == "list"
        assert isinstance(data["data"], list)
        assert len(data["data"]) > 0

    def test_all_entries_have_id_and_object_fields(self, client: TestClient) -> None:
        models = client.get("/v1/codex/models").json()["data"]
        for m in models:
            assert "id" in m
            assert m["object"] == "model"

    def test_contains_openai_models(self, client: TestClient) -> None:
        ids = {m["id"] for m in client.get("/v1/codex/models").json()["data"]}
        assert any(("gpt" in mid or "o4" in mid or "o3" in mid) for mid in ids), f"No gpt/o-series model in {ids}"

    def test_all_owned_by_openai(self, client: TestClient) -> None:
        models = client.get("/v1/codex/models").json()["data"]
        for m in models:
            assert m.get("owned_by") == "openai"


class TestStaticFallback:
    def test_claude_adapter_list_models_returns_nonempty(self) -> None:
        import claude_adapter
        models = claude_adapter.list_models()
        assert len(models) > 0
        assert all(isinstance(m, str) for m in models)

    def test_codex_adapter_list_models_returns_nonempty(self) -> None:
        import codex_adapter
        models = codex_adapter.list_models()
        assert len(models) > 0
        assert all(isinstance(m, str) for m in models)

    def test_claude_and_codex_models_are_disjoint(self) -> None:
        import claude_adapter, codex_adapter
        claude_ids = set(claude_adapter.list_models())
        codex_ids  = set(codex_adapter.list_models())
        overlap = claude_ids & codex_ids
        assert not overlap, f"Overlapping model IDs: {overlap}"
