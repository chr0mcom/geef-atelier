"""Verifies that the semaphore limits concurrent CLI calls."""
import asyncio
import sys
import os
from unittest.mock import AsyncMock, MagicMock, patch

import pytest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src"))

import claude_adapter


@pytest.fixture(autouse=True)
def fresh_semaphore():
    claude_adapter._semaphore = asyncio.Semaphore(claude_adapter.MAX_CONCURRENT)
    yield


@pytest.mark.asyncio
async def test_semaphore_limits_concurrent_calls():
    """No more than MAX_CONCURRENT calls should be in-flight simultaneously."""
    import json

    in_flight: list[int] = []
    peak: list[int] = [0]
    lock = asyncio.Lock()

    async def slow_exec(*args, **kwargs):
        async with lock:
            in_flight.append(1)
            peak[0] = max(peak[0], len(in_flight))

        await asyncio.sleep(0.05)

        async with lock:
            in_flight.pop()

        proc = MagicMock()
        proc.returncode = 0
        proc.communicate = AsyncMock(
            return_value=(json.dumps({"result": "ok"}).encode(), b"")
        )
        return proc

    with patch("asyncio.create_subprocess_exec", slow_exec):
        tasks = [
            claude_adapter.complete(f"prompt {i}", None, None)
            for i in range(claude_adapter.MAX_CONCURRENT + 2)
        ]
        await asyncio.gather(*tasks)

    assert peak[0] <= claude_adapter.MAX_CONCURRENT
