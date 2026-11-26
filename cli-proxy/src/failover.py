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
from collections import deque

from redaction import redact_secrets

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


def _env_float(name: str, default: float) -> float:
    try:
        return float(os.getenv(name, "") or default)
    except ValueError:
        log.warning("%s is not a number -- using default %s", name, default)
        return default


#: Observation window of the empty-result rate in /health (seconds).
EMPTY_RESULT_WINDOW_SECONDS = _env_int("EMPTY_RESULT_ALERT_WINDOW_SECONDS", 600)
#: `empty_result_alert` needs at least this many empty results inside the window ...
EMPTY_RESULT_ALERT_MIN = _env_int("EMPTY_RESULT_ALERT_MIN", 5)
#: ... and more than this share of the backend's document-mode attempts inside the window.
EMPTY_RESULT_ALERT_RATIO = _env_float("EMPTY_RESULT_ALERT_RATIO", 0.5)
#: Safety cap for the per-backend timestamp queues (they are pruned to the window anyway).
_MAX_RECENT_EVENTS = 10_000

#: Which backend covers for which. Symmetric on purpose -- codex outages are as real
#: as claude ones, and the caller should not have to care which one is degraded.
PARTNER = {"claude": "codex", "codex": "claude"}

#: Name of the alternative Claude subscription in the failover chain. It is the SAME CLI
#: as "claude" run under a different HOME (= a different login), so it can take over when
#: the primary account's session/weekly window is exhausted or its OAuth session died --
#: before the request has to leave for codex.
ALT = "claude-alt"
ALT_HOME = os.getenv("CLAUDE_HOME_ALT", "/auth/claude-alt")
ALT_ENABLED = _env_flag("CLAUDE_ALT_ENABLED", True)

_MAX_ERROR_CHARS = 500
#: Upper bound of `last_error_detail` in /health: the redacted message with the full cause.
_MAX_ERROR_DETAIL_CHARS = 2000

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
        # claude-CLI wording for subscription windows ("You've hit your session limit -
        # resets 4:20pm"): observed 2026-07-23/24 slipping through as "unknown", leaving
        # the healthy codex next to it unused.
        "session limit", "weekly limit", "hit your", "limit resets", "limit will reset",
    )),
    ("transport", (
        "timed out", "timeout", "exited with code", "connection", "broken pipe",
        "econnreset", "502", "503", "504", "unavailable",
    )),
)

#: Reasons that justify handing the request to the partner backend.
_FAILOVER_REASONS = frozenset({"auth", "limit", "transport"})

#: Wording with which the claude CLI says that the subscription of the bound account does not
#: include the requested model at all (as opposed to an exhausted window). The only place that
#: knows these phrases: the probe, the request paths and the generic endpoint all ask
#: `is_not_in_plan`. Observed: "You're out of usage credits. Switch to another model, or manage
#: usage credits at ..." (api_error_status 429, empty modelUsage) for a model the plan lacks.
_NOT_IN_PLAN_PATTERNS: tuple[str, ...] = (
    "out of usage credits",
    "not available on your plan",
    "not available on your current plan",
    "not available with your plan",
    "not included in your plan",
    "does not have access to model",
    "doesn't have access to model",
    "do not have access to model",
    "don't have access to model",
)

#: An ordinary session/weekly/rate window that merely ran out. Such a message is NEVER a
#: "not in plan" answer, whatever else it says: it belongs to the breaker and the failover.
_ORDINARY_LIMIT_PATTERNS: tuple[str, ...] = (
    "session limit", "weekly limit", "rate limit", "rate_limit", "usage limit",
    "limit resets", "limit will reset", "hit your", "too many requests", "overloaded",
)

#: How long a model the CLI refused as "not in plan" stays parked on that account.
NOT_IN_PLAN_TTL_SECONDS = _env_int("CLAUDE_NOT_IN_PLAN_TTL_SECONDS", 3600)

#: A document-mode run that exited normally but produced no text. It is an error of that one
#: request (502 `empty_result`, never handed over, see main.py) and NOT a breaker failure: whether
#: the CLI writes the document depends on the request's content (a refusal, a chat answer instead
#: of a file edit), so it is no evidence that the backend is unhealthy. Before Fix C (2026-11-24)
#: it counted against the breaker, and one request that came back empty twice (the Atelier's
#: direct path retries once) blocked the backend for every other caller for OPEN_SECONDS.
#: It is only counted and reported (`empty_results`, rate, `empty_result_alert` in /health).
EMPTY_RESULT = "empty_result"

#: Reasons that move the circuit breaker.
_BREAKER_REASONS = _FAILOVER_REASONS


class _BackendState:
    __slots__ = ("consecutive_failures", "open_until", "last_reason", "last_error",
                 "last_error_detail", "last_failure_at", "failover_count", "tripped",
                 "last_success_at", "empty_results", "last_empty_result_at",
                 "caller_timeouts", "last_caller_timeout_at", "last_caller_timeout_error",
                 "recent_empty_results", "recent_document_attempts")

    def __init__(self) -> None:
        self.consecutive_failures = 0
        self.open_until = 0.0
        self.last_reason: str | None = None
        self.last_error: str | None = None
        self.last_error_detail: str | None = None
        self.caller_timeouts = 0
        self.last_caller_timeout_at: float | None = None
        self.last_caller_timeout_error: str | None = None
        self.empty_results = 0
        self.last_empty_result_at: float | None = None
        self.recent_empty_results: deque[float] = deque(maxlen=_MAX_RECENT_EVENTS)
        self.recent_document_attempts: deque[float] = deque(maxlen=_MAX_RECENT_EVENTS)
        self.last_failure_at: float | None = None
        self.failover_count = 0
        self.last_success_at: float | None = None
        # True from the moment the breaker trips until the next REAL success. Unlike
        # `available` (which flips back to True when the open window merely elapses, so
        # exactly one probe can pass), this is the honest health signal: a permanently
        # broken backend stays unhealthy. Before 2026-08-19 /health keyed `degraded` off
        # `available`, so a dead claude auth flapped healthy<->degraded every 300s and the
        # cliproxy-watch cron spammed the operator with alarm/recovery pairs all afternoon.
        self.tripped = False


_STATE: dict[str, _BackendState] = {}

#: (Claude account, normalised model name) -> epoch seconds until which the account is taken
#: not to have the model, learned from a "not in plan" answer of a real request.
_NOT_IN_PLAN_MARKS: dict[tuple[str, str], float] = {}


def _state(backend: str) -> _BackendState:
    if backend not in _STATE:
        _STATE[backend] = _BackendState()
    return _STATE[backend]


def reset() -> None:
    """Drop all breaker state and runtime marks. Test seam; also used on process start."""
    _STATE.clear()
    _NOT_IN_PLAN_MARKS.clear()


def is_not_in_plan(exc_or_text: BaseException | str) -> bool:
    """True when the CLI said the bound account's subscription does not include the model.

    Unambiguous wording only (see `_NOT_IN_PLAN_PATTERNS`), and never when the text also names an
    ordinary session/weekly/rate window: those stay `limit` faults for the breaker."""
    text = str(exc_or_text).lower()
    if any(needle in text for needle in _ORDINARY_LIMIT_PATTERNS):
        return False
    return any(needle in text for needle in _NOT_IN_PLAN_PATTERNS)


def mark_not_in_plan(account: str, model: str) -> None:
    """Park `model` on `account` for NOT_IN_PLAN_TTL_SECONDS. Moves no breaker."""
    _NOT_IN_PLAN_MARKS[(account, model)] = time.time() + NOT_IN_PLAN_TTL_SECONDS


def marked_not_in_plan(account: str, model: str) -> bool:
    """True while a runtime mark for (account, model) is running; expired marks are dropped."""
    until = _NOT_IN_PLAN_MARKS.get((account, model))
    if until is None:
        return False
    if time.time() >= until:
        _NOT_IN_PLAN_MARKS.pop((account, model), None)
        return False
    return True


def clear_not_in_plan(account: str, models) -> None:
    """Drop the runtime marks of `account` for each of `models` (a later success proved them wrong)."""
    for model in models:
        if _NOT_IN_PLAN_MARKS.pop((account, model), None) is not None:
            log.info("Model %s is served by %s again -- not-in-plan mark dropped", model or "(default)", account)


def not_in_plan_marks() -> dict[str, dict[str, float]]:
    """Running marks per account: {account: {model: until}} (expired ones are pruned)."""
    now = time.time()
    out: dict[str, dict[str, float]] = {}
    for (account, model), until in list(_NOT_IN_PLAN_MARKS.items()):
        if now >= until:
            _NOT_IN_PLAN_MARKS.pop((account, model), None)
            continue
        out.setdefault(account, {})[model] = until
    return out


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


def cli_of(backend: str) -> str:
    """The CLI behind a chain member: both Claude accounts run the `claude` binary."""
    return "claude" if backend == ALT else backend


def alt_configured() -> bool:
    """True once an alternative account is logged in. Judged by the credentials file on
    every call, so the operator can park a second subscription (`claude` /login under
    HOME=ALT_HOME) without restarting the proxy -- and removing the file retires it."""
    return ALT_ENABLED and os.path.isfile(os.path.join(ALT_HOME, ".claude", ".credentials.json"))


def should_failover(reason: str) -> bool:
    """True when `reason` describes a backend fault the partner might not share."""
    return reason in _FAILOVER_REASONS


def available(backend: str) -> bool:
    """False while the breaker for `backend` is open. Once the open window elapses
    this returns True again, which is what allows exactly one probe through."""
    return time.time() >= _state(backend).open_until


def open_until(backend: str) -> float:
    """Epoch seconds until which the breaker of `backend` stays open (0 when closed)."""
    return _state(backend).open_until


def record_success(backend: str) -> None:
    st = _state(backend)
    if st.consecutive_failures or st.open_until or st.tripped:
        log.info("Backend %s recovered -- breaker closed", backend)
    st.consecutive_failures = 0
    st.open_until = 0.0
    st.tripped = False
    st.last_success_at = time.time()


def record_failure(backend: str, reason: str, error: str) -> None:
    """Record a failure. Only failover-worthy reasons move the breaker: a malformed request is
    not evidence that the backend is unhealthy. Empty document-mode results and runs stopped at
    the caller's own limit never come here (see `record_empty_result`,
    `record_caller_timeout`)."""
    st = _state(backend)
    st.last_reason = reason
    st.last_error = error[:_MAX_ERROR_CHARS]
    st.last_error_detail = redact_secrets(error)[:_MAX_ERROR_DETAIL_CHARS]
    st.last_failure_at = time.time()
    if reason not in _BREAKER_REASONS:
        return
    st.consecutive_failures += 1
    if st.consecutive_failures >= FAILS:
        st.open_until = time.time() + OPEN_SECONDS
        st.tripped = True
        log.warning("Breaker OPEN for %s (%ss) after %s failures | reason=%s | %s",
                    backend, OPEN_SECONDS, st.consecutive_failures, reason,
                    st.last_error)


def _prune(events: deque[float], now: float) -> None:
    cutoff = now - EMPTY_RESULT_WINDOW_SECONDS
    while events and events[0] < cutoff:
        events.popleft()


def record_document_attempt(backend: str) -> None:
    """Count one finished document-mode attempt of `backend` (whatever its outcome): the
    denominator of the empty-result rate in /health."""
    now = time.time()
    st = _state(backend)
    st.recent_document_attempts.append(now)
    _prune(st.recent_document_attempts, now)


def record_empty_result(backend: str, error: str) -> None:
    """Record a document-mode run of `backend` that produced no text.

    Counted and reported only (`empty_results`, `last_empty_result_at`, the rate of the last
    window). It leaves the breaker alone: `consecutive_failures`, `open_until`, `tripped` and
    the `last_reason`/`last_error` picture of real faults stay as they are, and it is no success
    either. `error` is the text of the 502 sent to the caller; it is logged by the caller."""
    now = time.time()
    st = _state(backend)
    st.empty_results += 1
    st.last_empty_result_at = now
    st.recent_empty_results.append(now)
    _prune(st.recent_empty_results, now)
    log.info("Empty document-mode result of %s (not a breaker failure) | %s", backend, error)


def record_caller_timeout(backend: str, error: str) -> None:
    """Record a run of `backend` that was stopped at the caller's own time limit.

    Counted and reported only (`caller_timeouts`, `last_caller_timeout_at`,
    `last_caller_timeout_error`). Any caller can cause it with a tiny limit, so it leaves the
    breaker and the `last_reason`/`last_error` picture of real faults (which the FAILOVER log
    line quotes) as they are."""
    st = _state(backend)
    st.caller_timeouts += 1
    st.last_caller_timeout_at = time.time()
    st.last_caller_timeout_error = error[:_MAX_ERROR_CHARS]


def _empty_result_view(st: _BackendState) -> dict:
    now = time.time()
    _prune(st.recent_empty_results, now)
    _prune(st.recent_document_attempts, now)
    empties = len(st.recent_empty_results)
    attempts = len(st.recent_document_attempts)
    rate = round(empties / attempts, 3) if attempts else None
    alert = (empties >= EMPTY_RESULT_ALERT_MIN and rate is not None
             and rate > EMPTY_RESULT_ALERT_RATIO)
    return {
        "last_empty_result_at": st.last_empty_result_at,
        "empty_results_recent": empties,
        "document_requests_recent": attempts,
        "empty_result_rate": rate,
        "empty_result_alert": alert,
    }


def record_failover(primary: str, used: str) -> None:
    """Count a request that the primary could not serve and the partner did."""
    _state(primary).failover_count += 1
    log.warning("FAILOVER %s->%s | reason=%s | %s",
                primary, used, _state(primary).last_reason, _state(primary).last_error)


def attempt_order(backend: str, to_alt: bool = False) -> list[str]:
    """Backends to try, in order.

    The chain is claude -> (alt Claude account, when configured) -> codex; a codex request
    runs codex -> claude -> alt. Members whose breaker is open move behind the healthy ones
    (stable, so the chain order still decides among equals) -- but with nothing healthy
    left we honour the caller's choice rather than shuffling requests between broken
    backends.

    `to_alt` is the model routing (a model only the alt account has, see model_routing.py), not
    a failover: the chain is then claude-alt -> codex, and with the failover switched off
    (FAILOVER_ENABLED=0) it is claude-alt alone instead of the primary alone.
    """
    if to_alt and backend == "claude":
        if not ENABLED:
            return [ALT]
        chain = [ALT, PARTNER[backend]]
        ready = [b for b in chain if available(b)]
        return chain if not ready else ready + [b for b in chain if b not in ready]
    partner = PARTNER.get(backend)
    if not ENABLED or partner is None:
        return [backend]
    spare = [ALT] if alt_configured() else []
    chain = [backend, *spare, partner] if backend == "claude" else [backend, partner, *spare]
    ready = [b for b in chain if available(b)]
    if not ready:
        return chain
    return ready + [b for b in chain if b not in ready]


def snapshot() -> dict:
    """Machine-readable state for /health. Consumed by the dms-cliproxy-watch cron,
    which raises the operator ticket on the healthy->degraded edge."""
    backends = {}
    reserve = {ALT} if alt_configured() else set()
    for backend in [*PARTNER, *reserve]:
        st = _state(backend)
        backends[backend] = {
            # Routing view: False only while the open window is running (one probe may
            # pass after it elapses). NOT a health statement -- see `healthy`.
            "available": available(backend),
            # Honest health: False from breaker trip until the next real success. This is
            # what `degraded` and the cliproxy-watch cron key off -- keying off `available`
            # made a dead backend flap healthy<->degraded with every elapsed open window
            # (operator alarm spam, 2026-08-19).
            "healthy": not st.tripped,
            "consecutive_failures": st.consecutive_failures,
            "open_until": st.open_until or None,
            "last_reason": st.last_reason,
            "last_error": st.last_error,
            # Redacted error text with the CLI's cause fields (subtype, is_error, result,
            # api_error_status), up to 2000 characters; `last_error` keeps its 500-char cut.
            "last_error_detail": st.last_error_detail,
            "last_failure_at": st.last_failure_at,
            # Runs stopped at the caller's own limit (header x-cli-proxy-timeout-seconds), since
            # start. Display only: no backend fault, so the fields above stay untouched.
            "caller_timeouts": st.caller_timeouts,
            "last_caller_timeout_at": st.last_caller_timeout_at,
            "last_caller_timeout_error": st.last_caller_timeout_error,
            # Proof of life for external monitors: a process restart clears the breaker
            # state (tripped=False) WITHOUT any real success -- the watch cron must not
            # read that as recovery. It closes an alarm card only when a success is
            # recorded AFTER the card was raised (review finding 2026-08-19).
            "last_success_at": st.last_success_at,
            "failover_count": st.failover_count,
            # Document-mode runs that exited normally without writing any text (since start).
            # Display only: they never move the breaker (Fix C, 2026-11-24).
            "empty_results": st.empty_results,
            # Last such run, and the rate inside the last EMPTY_RESULT_WINDOW_SECONDS:
            # empty results / finished document-mode attempts of this backend. The alert is
            # set when both thresholds hold; it changes no behaviour.
            **_empty_result_view(st),
            # The spare account is reported, but never counted towards `degraded`: while
            # the primary and codex serve, a dead reserve is not an incident.
            "reserve": backend in reserve,
        }
    return {
        "enabled": ENABLED,
        "degraded": any(not b["healthy"] for name, b in backends.items() if name not in reserve),
        # True when any backend (reserve included) shows a conspicuous empty-result rate. A hint
        # for the watch scripts and operators, independent of `degraded` and the breaker.
        "empty_result_alert": any(b["empty_result_alert"] for b in backends.values()),
        "empty_result_window_seconds": EMPTY_RESULT_WINDOW_SECONDS,
        "backends": backends,
    }
