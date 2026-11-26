"""Model-dependent choice between the two Claude accounts.

Claude subscriptions can carry different models, and which ones changes over time. The gateway
therefore keeps the primary account first for everything it has, and sends a request for a model
only the alternative account (failover.ALT) carries straight to that account. This is routing,
not failover: it holds with the failover switched off, moves no breaker and sets no failover
header. Nothing here names a model: the knowledge comes from the per-account alias probes
(claude_adapter.account_catalog) and from "not in plan" answers of real requests
(failover.mark_not_in_plan).
"""
from __future__ import annotations

import logging

import claude_adapter
import failover

log = logging.getLogger(__name__)

PRIMARY = claude_adapter.PRIMARY
ALT = failover.ALT
CLAUDE_ACCOUNTS = (PRIMARY, ALT)


def model_key(model: str | None) -> str:
    """The name a runtime mark is kept under: what the claude CLI receives for `model`."""
    return claude_adapter._normalize_model(model) or ""


def _is_concrete(model: str | None) -> bool:
    """True for a concrete id (`claude-fable-5`), False for an alias or a bare family name."""
    key = model_key(model)
    family = claude_adapter.family_of_model(model)
    return bool(key) and family is not None and key != family


def lacks(account: str, model: str | None) -> str | None:
    """Why `account` is known not to have `model`, or None.

    (a) its probe answered the model's family with "not in plan" (covers the family alias, the
    always-latest id and every concrete id of the family), (b) a real request for exactly this
    model got that answer within the mark's lifetime."""
    family = claude_adapter.family_of_model(model)
    _aliases, lacking = claude_adapter.account_catalog(account)
    if family is not None and family in lacking:
        return f"family {family} not in plan"
    if failover.marked_not_in_plan(account, model_key(model)):
        return "not in plan (learned)"
    return None


def _only_on_alt(model: str | None) -> str | None:
    """(c) A concrete id only the alternative account resolves to, while the primary resolves the
    same family to a different model -- the CLI would otherwise silently swap the model."""
    if not _is_concrete(model):
        return None
    key = model_key(model)
    family = claude_adapter.family_of_model(model)
    primary_map, _ = claude_adapter.account_catalog(PRIMARY)
    alt_map, _ = claude_adapter.account_catalog(ALT)
    if key in primary_map.values() or key not in alt_map.values():
        return None
    resolved = primary_map.get(claude_adapter._alias_for(family))
    if resolved is None or resolved == key:
        return None
    return f"{key} only on {ALT} (primary resolves {family} to {resolved})"


def route_to_alt(model: str | None) -> str | None:
    """Why a request for `model` belongs on the alternative account, or None for the usual chain.

    Only when the alternative account is logged in and not itself known to lack the model."""
    if not failover.alt_configured() or lacks(ALT, model):
        return None
    return lacks(PRIMARY, model) or _only_on_alt(model)


def sibling(account: str) -> str:
    """The other Claude account."""
    return ALT if account == PRIMARY else PRIMARY


def sibling_candidate(account: str, model: str | None, tried) -> str | None:
    """The other Claude account when it may still serve `model` after `account` said "not in
    plan": configured, not yet tried for this request and not known to lack the model."""
    other = sibling(account)
    if other in tried:
        return None
    if other == ALT and not failover.alt_configured():
        return None
    if lacks(other, model):
        return None
    return other


def log_route(model: str | None, origin: str, target: str, reason: str) -> None:
    """One INFO line per routed request (no WARNING: this is no fault)."""
    log.info("ROUTE %s->%s | model=%s | %s", origin, target, model or "(default)", reason)


def snapshot() -> dict:
    """Per-account view for /health: resolved and not-in-plan families plus the running runtime
    marks. No account data: no names, addresses or tokens."""
    marks = failover.not_in_plan_marks()
    accounts = [PRIMARY, ALT] if failover.alt_configured() else [PRIMARY]
    out: dict[str, dict] = {}
    for account in accounts:
        aliases, lacking = claude_adapter.account_catalog(account)
        available = {}
        for alias, resolved in aliases.items():
            family = claude_adapter._family_of(alias)
            if family:
                available[family] = resolved
        out[account] = {
            "available_families": available,
            "not_in_plan_families": sorted(lacking),
            "not_in_plan_models": {m or "(default)": until for m, until in marks.get(account, {}).items()},
            "reserve": account == ALT,
        }
    return out
