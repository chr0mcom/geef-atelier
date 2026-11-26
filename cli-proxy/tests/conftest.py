import sys
import os
import tempfile

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../src"))

# workspace.py defaults to /work, which exists only inside the container. Without this a host
# run fails every test that creates an ephemeral workspace with PermissionError. setdefault keeps
# the container's real WORKSPACE_ROOT (nightly run) untouched.
os.environ.setdefault("WORKSPACE_ROOT", tempfile.mkdtemp(prefix="cli-proxy-ws-"))

import pytest


@pytest.fixture(autouse=True)
def _reset_failover_breaker():
    """Keep the process-global circuit breaker from leaking between test files.

    Without this, a file that leaves claude's breaker open made later files (json retry,
    response_format, streaming) route to codex and fail only in a full-suite run.
    """
    import failover

    failover.reset()
    yield
    failover.reset()


@pytest.fixture(autouse=True)
def _no_real_alt_account(monkeypatch, tmp_path_factory):
    """Keep the suite hermetic: inside the container a real second login lives under the default
    ALT_HOME, which would add a chain member and extra model probes to every test. Tests that want
    an alternative account point ALT_HOME at their own credentials file (fixture `alt_home`).
    Also drops the per-account catalog state so no test sees another one's probe results."""
    import claude_adapter
    import failover

    monkeypatch.setattr(failover, "ALT_HOME", str(tmp_path_factory.mktemp("no-alt") / "claude-alt"))
    monkeypatch.setattr(claude_adapter, "_alt_model_cache", None)
    monkeypatch.setattr(claude_adapter, "_alt_last_attempt_at", None)
    monkeypatch.setattr(claude_adapter, "_not_in_plan_families", {})
    monkeypatch.setattr(claude_adapter, "_family_probe", {})
    monkeypatch.setattr(claude_adapter, "_alt_refresh_task", None)
