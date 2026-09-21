import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "../src"))

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
