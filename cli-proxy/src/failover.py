"""Provider failover between the claude and codex CLIs.

Both CLIs are subscription-covered and interchangeable for most workloads, but each
can fail on its own: an expired OAuth session, an exhausted subscription window, a
crashed or hung process. Before this module a claude outage simply propagated as HTTP
502 to every consumer (Hermes, dms-ai-router, Geef.Atelier) while a healthy codex sat
next to it unused -- see ../../../../docs/cli-proxy-failover.md for the incident.

Two pieces:

* `classify()` decides whether a failure is worth retrying elsewhere. Adapter errors
  are plain RuntimeErrors carrying the CLI's own message, so classification is textual.
  Client-side faults (bad schema, unsupported params) are deliberately NOT failed over
  -- they would fail identically on the partner and the retry would only obscure the bug.
* A per-backend circuit breaker so a sustained outage does not pay the failed-attempt
  latency on every single request, while still probing for recovery on its own.

State is per-process and intentionally not persisted: a restart should re-probe.
"""
from __future__ import annotations

import logging
import os
import time

log = logging.getLogger(__name__)


def _env_flag(name: str, default: bool) -> bool:
    raw = os.getenv(name)
    if raw is None:
        return default
    return raw.strip().lower() in ("1", "true", "yes", "on")


def _env_int(name: str, default: int) -> int:
    try:
        return int(os.getenv(name, "") or default)
    except ValueError:
        log.warning("%s is not an integer -- using default %s", name, default)
        return default


ENABLED = _env_flag("FAILOVER_ENABLED", True)
FAILS = _env_int("FAILOVER_FAILS", 2)
OPEN_SECONDS = _env_int("FAILOVER_OPEN_SECONDS", 300)

#: Which backend covers for which. Symmetric on purpose -- codex outages are as real
#: as claude ones, and the caller should not have to care which one is degraded.
PARTNER = {"claude": "codex", "codex": "claude"}

_MAX_ERROR_CHARS = 500

# Ordered: the first matching group wins. `auth` deliberately precedes `transport`
# because the real outage message ("...exited with code 1: Failed to authenticate...")
# matches both, and the auth cause is the one an operator can act on.
_PATTERNS: tuple[tuple[str, tuple[str, ...]], ...] = (
    ("auth", (
        "failed to authenticate", "authentication", "oauth", "session expired",
        "not logged in", "please run /login", "invalid api key", "unauthorized",
        "401", "credentials",
    )),
    ("limit", (
        "rate limit", "rate_limit", "usage limit", "quota", "429",
        "too many requests", "overloaded", "capacity",
    )),
    ("transport", (
        "timed out", "timeout", "exited with code", "connection", "broken pipe",
        "econnreset", "502", "503", "504", "unavailable",
    )),
)

#: Reasons that justify handing the request to the partner backend.
_FAILOVER_REASONS = frozenset({"auth", "limit", "transport"})


class _BackendState:
    __slots__ = ("consecutive_failures", "open_until", "last_reason", "last_error",
                 "last_failure_at", "failover_count")

    def __init__(self) -> None:
        self.consecutive_failures = 0
        self.open_until = 0.0
        self.last_reason: str | None = None
        self.last_error: str | None = None
        self.last_failure_at: float | None = None
        self.failover_count = 0


_STATE: dict[str, _BackendState] = {}


def _state(backend: str) -> _BackendState:
    if backend not in _STATE:
        _STATE[backend] = _BackendState()
    return _STATE[backend]


def reset() -> None:
    """Drop all breaker state. Test seam; also used on process start."""
    _STATE.clear()


def classify(exc: BaseException) -> str:
    """Map an adapter failure to 'auth' | 'limit' | 'transport' | 'unknown'."""
    # A missing or non-executable CLI binary surfaces as OSError from the spawn, not as
    # a RuntimeError with a readable message -- and "the binary is gone" is exactly the
    # kind of local fault the partner backend does not share.
    if isinstance(exc, OSError):
        return "transport"
    text = str(exc).lower()
    for reason, needles in _PATTERNS:
        if any(needle in text for needle in needles):
            return reason
    return "unknown"


def should_failover(reason: str) -> bool:
    """True when `reason` describes a backend fault the partner might not share."""
    return reason in _FAILOVER_REASONS


def available(backend: str) -> bool:
    """False while the breaker for `backend` is open. Once the open window elapses
    this returns True again, which is what allows exactly one probe through."""
    return time.time() >= _state(backend).open_until


def record_success(backend: str) -> None:
    st = _state(backend)
    if st.consecutive_failures or st.open_until:
        log.info("Backend %s recovered -- breaker closed", backend)
    st.consecutive_failures = 0
    st.open_until = 0.0


def record_failure(backend: str, reason: str, error: str) -> None:
    """Record a failure. Only failover-worthy reasons move the breaker: a malformed
    request is not evidence that the backend is unhealthy."""
    st = _state(backend)
    st.last_reason = reason
    st.last_error = error[:_MAX_ERROR_CHARS]
    st.last_failure_at = time.time()
    if not should_failover(reason):
        return
    st.consecutive_failures += 1
    if st.consecutive_failures >= FAILS:
        st.open_until = time.time() + OPEN_SECONDS
        log.warning("Breaker OPEN for %s (%ss) after %s failures | reason=%s | %s",
                    backend, OPEN_SECONDS, st.consecutive_failures, reason,
                    st.last_error)


def record_failover(primary: str, used: str) -> None:
    """Count a request that the primary could not serve and the partner did."""
    _state(primary).failover_count += 1
    log.warning("FAILOVER %s->%s | reason=%s | %s",
                primary, used, _state(primary).last_reason, _state(primary).last_error)


def attempt_order(backend: str) -> list[str]:
    """Backends to try, in order. The partner leads only when the primary's breaker is
    open AND the partner's is not -- with nothing healthy left we honour the caller's
    choice rather than shuffling requests between two broken backends."""
    partner = PARTNER.get(backend)
    if not ENABLED or partner is None:
        return [backend]
    if not available(backend) and available(partner):
        return [partner, backend]
    return [backend, partner]


def snapshot() -> dict:
    """Machine-readable state for /health. Consumed by the dms-cliproxy-watch cron,
    which raises the operator ticket on the healthy->degraded edge."""
    backends = {}
    for backend in PARTNER:
        st = _state(backend)
        backends[backend] = {
            "available": available(backend),
            "consecutive_failures": st.consecutive_failures,
            "open_until": st.open_until or None,
            "last_reason": st.last_reason,
            "last_error": st.last_error,
            "last_failure_at": st.last_failure_at,
            "failover_count": st.failover_count,
        }
    return {
        "enabled": ENABLED,
        "degraded": any(not b["available"] for b in backends.values()),
        "backends": backends,
    }
