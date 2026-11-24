"""Document mode: a run that writes no text is an error (502 empty_result), not an empty 200.

Pre-study V1 (2026-11-24): three claude document-mode aborts were handed over to codex, and codex
answered each with HTTP 200 and an empty text ("draft.md unchanged after CLI run"). The caller
could not tell that nothing had been written.
"""
from __future__ import annotations

import os
import sys
from unittest.mock import AsyncMock, patch

import pytest
from fastapi.testclient import TestClient

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import failover  # noqa: E402
import main  # noqa: E402
from usage import UsageParts  # noqa: E402

_CLAUDE_ABORT = RuntimeError(
    "claude CLI (document mode) exited with code 1: You've hit your session limit "
    "(subtype=success, is_error=true)")
_TRANSPORT_ABORT = RuntimeError(
    "claude CLI (document mode) exited with code 1: (subtype=error_during_execution, is_error=true)")


def _doc_body(document: str = "", **extra) -> dict:
    return {
        "model": "claude-opus-latest",
        "messages": [{"role": "system", "content": "You write."},
                     {"role": "user", "content": "Write the text."}],
        "document_mode": True,
        "document": document,
        **extra,
    }


def _returns(text: str):
    return AsyncMock(return_value=(text, UsageParts(input_tokens=10, output_tokens=5)))


@pytest.fixture()
def client() -> TestClient:
    return TestClient(main.app, raise_server_exceptions=False)


@pytest.fixture(autouse=True)
def _no_alt_account(monkeypatch) -> None:
    monkeypatch.setattr(failover, "ALT_ENABLED", False)


def _backend(client: TestClient, name: str) -> dict:
    return client.get("/health").json()["failover"]["backends"][name]


class TestEmptyResultIsAnError:
    @pytest.mark.parametrize("document", ["", "   \n"])
    def test_empty_unchanged_draft_is_502_empty_result(self, client: TestClient, document) -> None:
        with patch.object(main.claude_adapter, "complete_document", _returns(document)), \
             patch.object(main.codex_adapter, "complete_document", _returns("x")) as codex:
            resp = client.post("/v1/claude/chat/completions", json=_doc_body(document))

        assert resp.status_code == 502
        error = resp.json()["error"]
        assert error["code"] == "empty_result"
        assert error["type"] == "api_error"
        assert "no text" in error["message"]
        assert resp.headers["x-cli-proxy-provider"] == "claude"
        assert "x-cli-proxy-failover" not in resp.headers
        assert codex.await_count == 0, "an empty result is not handed over"

    def test_counts_as_a_failure_of_the_backend(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_document", _returns("")):
            client.post("/v1/claude/chat/completions", json=_doc_body())

        claude = _backend(client, "claude")
        assert claude["empty_results"] == 1
        assert claude["consecutive_failures"] == 1
        assert claude["last_reason"] == "empty_result"
        assert "no text" in claude["last_error_detail"]

    def test_repeated_empty_results_open_the_breaker(self, client: TestClient, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 2)
        with patch.object(main.claude_adapter, "complete_document", _returns("")):
            client.post("/v1/claude/chat/completions", json=_doc_body())
            client.post("/v1/claude/chat/completions", json=_doc_body())

        claude = _backend(client, "claude")
        assert claude["available"] is False
        assert claude["healthy"] is False
        assert claude["empty_results"] == 2

    def test_codex_endpoint_too(self, client: TestClient) -> None:
        body = _doc_body(model="gpt-6.2-sol")
        with patch.object(main.codex_adapter, "complete_document", _returns("")):
            resp = client.post("/v1/codex/chat/completions", json=body)
        assert resp.status_code == 502
        assert resp.json()["error"]["code"] == "empty_result"
        assert _backend(client, "codex")["empty_results"] == 1


class TestEmptyResultAfterFailover:
    def test_the_v1_case_is_an_error_not_an_empty_200(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_document", AsyncMock(side_effect=_TRANSPORT_ABORT)), \
             patch.object(main.codex_adapter, "complete_document", _returns("")):
            resp = client.post("/v1/claude/chat/completions", json=_doc_body())

        assert resp.status_code == 502
        assert resp.json()["error"]["code"] == "empty_result"
        assert "after failover from claude" in resp.json()["error"]["message"]
        assert resp.headers["x-cli-proxy-provider"] == "codex"
        assert resp.headers["x-cli-proxy-failover"] == "claude->codex"

    def test_origin_breaker_is_not_opened_a_second_time(self, client: TestClient, monkeypatch) -> None:
        monkeypatch.setattr(failover, "FAILS", 2)
        with patch.object(main.claude_adapter, "complete_document", AsyncMock(side_effect=_CLAUDE_ABORT)), \
             patch.object(main.codex_adapter, "complete_document", _returns("")):
            client.post("/v1/claude/chat/completions", json=_doc_body())

        claude = _backend(client, "claude")
        codex = _backend(client, "codex")
        assert claude["consecutive_failures"] == 1, "only claude's own fault counts for claude"
        assert claude["available"] is True
        assert claude["last_reason"] == "limit"
        assert claude["failover_count"] == 0, "codex did not serve the request"
        assert claude["empty_results"] == 0
        assert codex["consecutive_failures"] == 1
        assert codex["empty_results"] == 1
        assert codex["last_reason"] == "empty_result"
        assert codex["last_success_at"] is None

    def test_successful_failover_is_unchanged(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_document", AsyncMock(side_effect=_CLAUDE_ABORT)), \
             patch.object(main.codex_adapter, "complete_document", _returns("Der Text.")):
            resp = client.post("/v1/claude/chat/completions", json=_doc_body())

        assert resp.status_code == 200
        assert resp.json()["choices"][0]["message"]["content"] == "Der Text."
        assert resp.headers["x-cli-proxy-failover"] == "claude->codex"
        assert _backend(client, "claude")["failover_count"] == 1
        assert _backend(client, "codex")["empty_results"] == 0


class TestUnchangedBehaviour:
    def test_text_written_into_an_empty_draft_is_a_success(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_document", _returns("Neuer Text")):
            resp = client.post("/v1/claude/chat/completions", json=_doc_body(""))
        assert resp.status_code == 200
        assert resp.json()["choices"][0]["message"]["content"] == "Neuer Text"

    def test_unchanged_non_empty_draft_stays_a_success(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_document", _returns("Alter Text")):
            resp = client.post("/v1/claude/chat/completions", json=_doc_body("Alter Text"))
        assert resp.status_code == 200
        assert resp.json()["choices"][0]["message"]["content"] == "Alter Text"
        assert _backend(client, "claude")["empty_results"] == 0

    def test_draft_emptied_on_purpose_stays_a_success(self, client: TestClient) -> None:
        with patch.object(main.claude_adapter, "complete_document", _returns("")):
            resp = client.post("/v1/claude/chat/completions", json=_doc_body("Alter Text"))
        assert resp.status_code == 200
        assert resp.json()["choices"][0]["message"]["content"] == ""

    def test_plain_chat_with_empty_text_stays_a_200(self, client: TestClient) -> None:
        body = {"model": "claude-haiku-latest", "messages": [{"role": "user", "content": "hi"}]}
        with patch.object(main.claude_adapter, "complete_with_usage", _returns("")):
            resp = client.post("/v1/claude/chat/completions", json=body)
        assert resp.status_code == 200
        assert resp.json()["choices"][0]["message"]["content"] == ""
        assert _backend(client, "claude")["empty_results"] == 0

    def test_streaming_with_empty_text_is_untouched(self, client: TestClient) -> None:
        async def _stream(*_a, **_k):
            yield ("usage", UsageParts())

        body = {"model": "claude-haiku-latest", "stream": True,
                "messages": [{"role": "user", "content": "hi"}]}
        with patch.object(main.claude_adapter, "stream", _stream):
            resp = client.post("/v1/claude/chat/completions", json=body)
        assert resp.status_code == 200
        assert "data: [DONE]" in resp.text
        assert '"error"' not in resp.text
