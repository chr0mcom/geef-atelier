"""The web app answers the provider sync with 401 when no internal token is configured (or the two sides differ).

The gateway must keep running, and the built-in CLI providers must keep working, including their model lists, which the
web app reads from ``/v1/cli/{name}/models``. Offline: a small HTTP server stands in for the web app, fake CLIs for the
real ones.
"""
from __future__ import annotations

import json
import logging
import os
import re
import subprocess
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

import httpx
import pytest
import respx

import fake_cli
from provider_sync import ProviderConfigSync
from test_disconnect_e2e import SRC, _chat, _free_port, _healthy

BACKEND = "http://web.test:8080"
SYNC_URL = f"{BACKEND}/api/internal/providers/cli"
BUILT_IN = {"claude-cli": "claude", "codex-cli": "codex", "gemini-cli": "gemini"}
SYSTEM_PROVIDERS = Path(__file__).resolve().parents[2] / "src" / "Geef.Atelier.Core" / "Domain" / "Providers" / "SystemProviders.cs"


async def _sync_answered(sync: ProviderConfigSync, *responses: httpx.Response) -> None:
    with respx.mock:
        respx.get(SYNC_URL).mock(side_effect=list(responses))
        for _ in responses:
            await sync.sync_now()


@pytest.mark.asyncio
async def test_built_in_providers_work_while_the_sync_is_refused():
    sync = ProviderConfigSync(BACKEND, "")

    await _sync_answered(sync, httpx.Response(401), httpx.Response(401))

    for name, kind in BUILT_IN.items():
        config = sync.get_provider_config(name)
        assert config is not None, name
        assert config["settings"]["cli_kind"] == kind
    assert sync.get_provider_config("custom-own") is None
    assert sync.all_provider_names() == [], "synced_providers must keep saying that nothing was synced"


@pytest.mark.asyncio
async def test_once_synced_only_the_list_of_the_web_app_counts():
    sync = ProviderConfigSync(BACKEND, "token")
    only_codex = [{"name": "codex-cli", "displayName": "Codex CLI", "settings": {"cli_kind": "codex"}}]

    await _sync_answered(sync, httpx.Response(200, json=only_codex), httpx.Response(401))

    assert sync.get_provider_config("codex-cli") is not None
    assert sync.get_provider_config("claude-cli") is None, "a provider switched off in the web app stays off"


@pytest.mark.asyncio
async def test_an_empty_synced_list_also_counts():
    sync = ProviderConfigSync(BACKEND, "token")

    await _sync_answered(sync, httpx.Response(200, json=[]))

    assert sync.get_provider_config("claude-cli") is None


@pytest.mark.asyncio
async def test_the_warning_names_the_setting_and_never_the_token(caplog):
    sync = ProviderConfigSync(BACKEND, "tok-SECRET-123")

    with caplog.at_level(logging.WARNING):
        await _sync_answered(sync, httpx.Response(401))

    assert "ATELIER_INTERNAL_API_TOKEN" in caplog.text
    assert "tok-SECRET-123" not in caplog.text


@pytest.mark.skipif(not SYSTEM_PROVIDERS.exists(), reason="the web sources are only there in a checkout")
def test_built_in_defaults_match_the_system_providers_of_the_web_app():
    source = SYSTEM_PROVIDERS.read_text(encoding="utf-8")
    declared = {}
    for block in re.findall(r"CreateCliProvider\(\s*name:.*?\);", source, re.S):
        text = dict(re.findall(r'(\w+): "([^"]*)"', block))
        models = re.findall(r'"([^"]+)"', re.search(r"models: \[(.*?)\]", block, re.S).group(1))
        declared[text["name"]] = (text, models)
    assert set(declared) == set(BUILT_IN)
    sync = ProviderConfigSync(BACKEND, "")
    for name, (text, models) in declared.items():
        settings = sync.get_provider_config(name)["settings"]
        assert settings["cli_kind"] == text["cliKind"]
        assert settings["binary"] == text["binary"]
        assert settings.get("auth_volume") == text.get("authVolume")
        assert settings.get("auth_env_var_alternative") == text.get("authEnvVarAlternative")
        if text["cliKind"] == "gemini":
            assert settings["models"] == models


class _RefusingWebApp(BaseHTTPRequestHandler):
    seen: list[dict] = []

    def log_message(self, *args):
        pass

    def do_GET(self):
        self.seen.append({"path": self.path, "token": self.headers.get("X-Internal-Token")})
        self.send_response(401)
        self.send_header("Content-Length", "0")
        self.end_headers()


@pytest.fixture(scope="module")
def gateway(tmp_path_factory):
    tmp = tmp_path_factory.mktemp("gateway-sync-refused")
    bin_dir, fake_dir = tmp / "bin", tmp / "runs"
    fake_dir.mkdir()
    fake_cli.install(bin_dir)
    for name in ("claude", "codex", "ws"):
        (tmp / name).mkdir()
    _RefusingWebApp.seen = []
    web = ThreadingHTTPServer(("127.0.0.1", 0), _RefusingWebApp)
    threading.Thread(target=web.serve_forever, daemon=True).start()
    port = _free_port()
    log_path = tmp / "server.log"
    env = {
        **os.environ,
        "PATH": f"{bin_dir}:{os.environ['PATH']}",
        "PYTHONPATH": str(SRC),
        "FAKE_DIR": str(fake_dir),
        "CLAUDE_HOME": str(tmp / "claude"),
        "CLAUDE_HOME_ALT": str(tmp / "no-alt"),
        "CODEX_HOME": str(tmp / "codex"),
        "WORKSPACE_ROOT": str(tmp / "ws"),
        "FAKE_SLOW_SECONDS": "0",
        "ATELIER_BACKEND_URL": f"http://127.0.0.1:{web.server_port}",
        "MODEL_WARMUP_INTERVAL": "86400",
        "CLI_PROXY_API_KEYS": "",
    }
    env.pop("ATELIER_INTERNAL_API_TOKEN", None)
    env.pop("FAKE_TOKEN", None)
    with open(log_path, "w", encoding="utf-8") as log:
        proc = subprocess.Popen(
            [sys.executable, "-m", "uvicorn", "main:app", "--host", "127.0.0.1", "--port", str(port)],
            cwd=str(SRC), env=env, stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT)
    base = f"http://127.0.0.1:{port}"
    try:
        assert fake_cli.wait_for(lambda: _healthy(base), seconds=30), log_path.read_text()
        yield {"base": base, "dir": fake_dir, "log": log_path}
    finally:
        fake_cli.kill_leftovers(fake_dir)
        proc.terminate()
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait()
        web.shutdown()


def test_the_gateway_starts_and_stays_healthy(gateway):
    health = httpx.get(gateway["base"] + "/health", timeout=10)

    assert health.status_code == 200
    assert health.json()["status"] == "ok"
    assert health.json()["synced_providers"] == []
    assert _RefusingWebApp.seen == [{"path": "/api/internal/providers/cli", "token": None}]
    assert "ATELIER_INTERNAL_API_TOKEN" in gateway["log"].read_text()


@pytest.mark.parametrize("name", sorted(BUILT_IN))
def test_the_model_list_of_a_built_in_provider_is_served(gateway, name):
    response = httpx.get(f"{gateway['base']}/v1/cli/{name}/models", timeout=90)

    assert response.status_code == 200, response.text
    body = response.json()
    assert body["data"] and all(entry["id"] for entry in body["data"])
    assert body["x_source"] in ("live", "degraded", "static")


def test_an_own_cli_provider_is_unknown_without_the_sync(gateway):
    assert httpx.get(gateway["base"] + "/v1/cli/custom-own/models", timeout=10).status_code == 404


@pytest.mark.parametrize(("backend", "model"), [("claude", "claude-sonnet-latest"), ("codex", "gpt-5")])
def test_completions_of_the_built_in_clis_do_not_depend_on_the_sync(gateway, backend, model):
    response = httpx.post(f"{gateway['base']}/v1/{backend}/chat/completions",
                          json=_chat(f"refused-{backend}", "SLOW", model=model),
                          headers={"x-cli-proxy-failover": "off"}, timeout=30)

    assert response.status_code == 200, response.text
    assert len(fake_cli.starts(gateway["dir"], f"refused-{backend}")) == 1
