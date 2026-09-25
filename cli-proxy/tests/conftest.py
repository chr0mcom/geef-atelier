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
