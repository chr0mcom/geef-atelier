"""Adapter that runs the claude CLI and returns its text output."""
from __future__ import annotations

import asyncio
import contextvars
import json
import logging
import os
import re
import time
from contextlib import aclosing
from pathlib import Path

import cli_process
import failover
import policy
from cli_errors import RESULT_CHARS, claude_failure_detail, clip_redacted
from usage import UsageParts
from workspace import (
    ephemeral_dir,
    finalize_decision_instruction,
    finalize_instruction,
    materialize_context,
    read_decision_file,
)

log = logging.getLogger(__name__)

MAX_CONCURRENT = int(os.getenv("CLAUDE_MAX_CONCURRENT", "2"))
_semaphore = asyncio.Semaphore(MAX_CONCURRENT)

# Home directory for claude auth — mounted as a volume in the container.
CLAUDE_HOME = os.getenv("CLAUDE_HOME", "/auth/claude")

# Which Claude subscription the CLI calls of THIS request run under. A ContextVar, because
# the proxy serves many requests concurrently in one process and each may sit on a
# different stage of the failover chain (claude -> claude-alt); an argument would have to
# be threaded through every adapter entry point. asyncio tasks copy their context at
# creation, so a request's binding never leaks into another request.
_account_home: contextvars.ContextVar[str | None] = contextvars.ContextVar(
    "claude_account_home", default=None)


def bind_account(account: str) -> None:
    """Point this request's claude CLI calls at `account` ('claude' or 'claude-alt').

    Always called explicitly for every claude-family attempt -- including the primary --
    so a previous stage's binding can never carry over to the next attempt.
    """
    _account_home.set(failover.ALT_HOME if account == failover.ALT else None)


#: Name of the primary Claude account in the failover chain and in the per-account catalog.
PRIMARY = "claude"


def current_account() -> str:
    """The Claude account the claude CLI calls of this context run under."""
    return failover.ALT if _account_home.get() else PRIMARY


def current_home() -> str:
    """HOME for the claude CLI: the alt account's directory when bound, else the primary."""
    return _account_home.get() or CLAUDE_HOME

# Hard cap on how long a single claude CLI call may run.
# Increase for workloads that produce very long outputs (e.g. 30-page documents).
CLI_TIMEOUT_SECONDS = int(os.getenv("CLI_TIMEOUT_SECONDS", "1800"))


def _effort_args(reasoning_effort: str | None) -> list[str]:
    """Map a per-request reasoning_effort to the claude CLI's --effort flag.

    Only a value the caller actually sent produces a flag; without one the argv stays exactly
    as before, so the CLI keeps its own default effort (BF-WP-116). Validation of the value
    happens once in main._validate_policy.
    """
    return ["--effort", reasoning_effort] if reasoning_effort else []

# The claude CLI resolves a family alias (opus, sonnet, haiku, fable, …) to the NEWEST model of
# that family. We surface each as a `claude-<family>-latest` id that never goes stale and map it
# back to the bare CLI alias on invocation (see _normalize_model).
#
# The families themselves are DISCOVERED, not listed: pinning them would move the staleness one
# level up — a new family (as fable was) would need a code change even though the generations
# inside each family update themselves. `claude --help` names the aliases it accepts; the seed
# below only guarantees the long-standing ones if that text ever changes shape.
_SEED_FAMILIES = ("opus", "sonnet", "haiku", "fable")

_ALIAS_SUFFIX = "-latest"
_ALIAS_PREFIX = "claude-"


def _alias_for(family: str) -> str:
    """The always-latest id surfaced to callers for a CLI family alias."""
    return f"{_ALIAS_PREFIX}{family}{_ALIAS_SUFFIX}"


def _family_of(model: str) -> str | None:
    """The CLI family alias behind a `claude-<family>-latest` id, or None."""
    if model.startswith(_ALIAS_PREFIX) and model.endswith(_ALIAS_SUFFIX):
        family = model[len(_ALIAS_PREFIX):-len(_ALIAS_SUFFIX)]
        return family or None
    return None

# Offline fallback when alias resolution is unavailable. Kept reasonably current, but the
# dynamic resolver (list_models_async) supersedes it and the *-latest aliases never go stale.
STATIC_MODELS = [
    "claude-opus-5",
    "claude-sonnet-5",
    "claude-haiku-4-5",
    "claude-fable-5",
]

# Families discovered from the CLI, cached for the process lifetime; the seed until then.
_families: tuple[str, ...] = _SEED_FAMILIES
_families_discovered = False
_families_lock = asyncio.Lock()


async def _discover_families() -> tuple[str, ...]:
    """Read the family aliases the installed CLI advertises for --model.

    Runs once per process — the installed CLI does not change under a running gateway. Falls back
    to the seed when the help text cannot be read or parsed. Ordering matters: the picker
    auto-selects the first recommended entry, so the seed order (opus first) wins and newly
    discovered families are appended.
    """
    global _families, _families_discovered
    async with _families_lock:
        if _families_discovered:
            return _families
        try:
            proc = await asyncio.create_subprocess_exec(
                "claude", "--help",
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
                env=policy.agent_env(current_home()),
            )
            stdout, _ = await asyncio.wait_for(proc.communicate(), timeout=30)
            help_text = stdout.decode(errors="replace")
        except Exception as exc:  # noqa: BLE001 — discovery is best-effort, never fatal
            log.warning("claude family discovery failed: %s; using the seed list", exc)
            _families = tuple(dict.fromkeys(_SEED_FAMILIES + _families))
            _families_discovered = True
            return _families

        section = help_text.split("--model", 1)[-1][:600]
        found = [m for m in re.findall(r"'([a-z][a-z0-9-]{2,20})'", section)
                 if not m.startswith("claude-")]

        merged = list(_SEED_FAMILIES) + [f for f in found if f not in _SEED_FAMILIES]
        if merged != list(_families):
            log.info("claude model families: %s", merged)
        _families = tuple(merged)
        _families_discovered = True
        return _families

# How long a probe may run before its subprocess is killed.
_PROBE_TIMEOUT_SECONDS = int(os.getenv("CLAUDE_MODEL_PROBE_TIMEOUT", "60"))

# Minimum spacing between forced re-resolutions. `?refresh=1` is unauthenticated and each honoured
# call spends real CLI probes on a subscription shared with Hermes and the DMS worker, so a burst of
# clicks must collapse into one probe.
_MODEL_REFRESH_MIN_INTERVAL = int(os.getenv("CLAUDE_MODEL_REFRESH_MIN_INTERVAL", "60"))

# Timestamp of the last resolution ATTEMPT, success or failure. The success timestamp cannot serve
# as the throttle: a failed probe writes no cache entry, so every further forced call would probe
# again — in exactly the outage the throttle exists to contain.
_last_attempt_at: float | None = None

_MODEL_CACHE_TTL = 86400  # 24 hours
# In-memory cache of a SUCCESSFUL probe: (alias -> concrete id, fetched_at).
# A failed probe is never cached: caching it would serve STATIC_MODELS as if it were live
# for a full day, which is exactly the staleness this resolver exists to prevent.
_model_cache: tuple[dict[str, str], float] | None = None
_model_cache_lock = asyncio.Lock()

# The same two pieces of state for the alternative account (failover.ALT). Accounts can carry
# different models, so each one is probed and cached on its own; `_model_cache` and
# `_last_attempt_at` above stay the primary account's.
_alt_model_cache: tuple[dict[str, str], float] | None = None
_alt_last_attempt_at: float | None = None

# Families a probe of the account answered with the CLI's "not in plan" wording, per account.
# Replaced family by family by later probes; an unknown answer (limit, auth, timeout) leaves it.
_not_in_plan_families: dict[str, set[str]] = {}


class _NotInPlan:
    """Probe answer: the bound account's subscription does not include the probed family."""

    def __repr__(self) -> str:
        return "NOT_IN_PLAN"


NOT_IN_PLAN = _NotInPlan()


def _cache_of(account: str) -> tuple[dict[str, str], float] | None:
    return _alt_model_cache if account == failover.ALT else _model_cache


def _set_cache(account: str, value: tuple[dict[str, str], float]) -> None:
    global _model_cache, _alt_model_cache
    if account == failover.ALT:
        _alt_model_cache = value
    else:
        _model_cache = value


def _last_attempt_of(account: str) -> float | None:
    return _alt_last_attempt_at if account == failover.ALT else _last_attempt_at


def _set_last_attempt(account: str, value: float) -> None:
    global _last_attempt_at, _alt_last_attempt_at
    if account == failover.ALT:
        _alt_last_attempt_at = value
    else:
        _last_attempt_at = value


def _accounts() -> list[str]:
    """Claude accounts whose catalog counts: the primary, and the alternative once logged in."""
    return [PRIMARY, failover.ALT] if failover.alt_configured() else [PRIMARY]

# Source markers reported alongside every model list.
SOURCE_LIVE = "live"
SOURCE_DEGRADED = "degraded"
SOURCE_STATIC = "static"


def _normalize_model(model: str | None) -> str | None:
    """Map a requested model to a name the claude CLI accepts.

    Strips any provider prefix and normalises dots to dashes (e.g.
    "anthropic/claude-opus-4.8" -> "claude-opus-4-8"). Family aliases
    (opus/sonnet/haiku and claude-<family>-latest) are passed through as the CLI's
    built-in aliases, which always resolve to the newest model of that family.
    """
    if not model:
        return None
    bare = model.split("/")[-1] if "/" in model else model
    bare = bare.replace(".", "-").lower()

    family = _family_of(bare)
    return family if family else bare


async def _probe_alias(alias: str) -> str | _NotInPlan | None:
    """Ask the claude CLI which concrete model the family alias currently resolves to.

    Runs under the account bound in this context (see `_resolve_alias_map`). Returns the concrete
    id, NOT_IN_PLAN when the CLI answers with its unambiguous "not in plan" wording, and None for
    every other outcome (limit, auth, timeout, unreadable output) -- None means "unknown".

    Runs under the shared CLI semaphore so a model probe cannot outrank real completions,
    and always reaps its subprocess: without the kill a timed-out probe would leave a
    `claude` process running against the same subscription.
    """
    args = ["claude", "-p", "--model", alias, "--output-format", "json", "--no-session-persistence"]
    env = policy.agent_env(current_home())
    async with _semaphore:
        proc = None
        try:
            proc = await asyncio.create_subprocess_exec(
                *args,
                stdin=asyncio.subprocess.PIPE,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
                env=env,
            )
            stdout, _ = await asyncio.wait_for(
                proc.communicate(input=b"hi"), timeout=_PROBE_TIMEOUT_SECONDS
            )
            proc = None
            data = json.loads(stdout.decode(errors="replace"))
            keys = list((data.get("modelUsage") or {}).keys())
            if keys:
                return keys[0]
            if data.get("is_error") and failover.is_not_in_plan(str(data.get("result", ""))):
                log.info("claude model alias probe for %s on %s: not in plan", alias, current_account())
                return NOT_IN_PLAN
            log.warning("claude model alias probe for %s returned no modelUsage entry", alias)
        except Exception as exc:  # noqa: BLE001 — best-effort discovery, never fatal
            log.warning("claude model alias probe failed for %s: %s", alias, exc)
        finally:
            if proc is not None and proc.returncode is None:
                try:
                    proc.kill()
                    await proc.wait()
                except ProcessLookupError:
                    pass
    return None


async def _resolve_alias_map(account: str = PRIMARY) -> tuple[dict[str, str], bool]:
    """Resolve each family alias to its current concrete model id via the CLI itself.

    Probes run concurrently so a cache refresh costs one round-trip, not three. The second
    tuple element is False when not a single probe succeeded — the caller must not cache
    that result and must report it as degraded rather than live.

    The probes run under `account`: the binding is set for the probe tasks only and reset
    afterwards, so it can never reach a request. A family the account answered with "not in
    plan" is recorded in `_not_in_plan_families` -- but only when another family of the same run
    resolved: an account whose every probe says so is out of credit as a whole (no subscription
    lacks every model), and that is unknown, not knowledge. A resolved family clears both the
    family entry and the runtime marks of its models (failover.clear_not_in_plan).
    """
    families = await _discover_families()
    token = _account_home.set(failover.ALT_HOME if account == failover.ALT else None)
    try:
        results = await asyncio.gather(*(_probe_alias(a) for a in families))
    finally:
        _account_home.reset(token)
    alias_map = {
        _alias_for(family): resolved
        for family, resolved in zip(families, results)
        if isinstance(resolved, str) and resolved
    }
    if alias_map:
        lacking = _not_in_plan_families.setdefault(account, set())
        for family, resolved in zip(families, results):
            if resolved is NOT_IN_PLAN:
                if family not in lacking:
                    log.info("claude family %s is not in the plan of account %s", family, account)
                lacking.add(family)
            elif isinstance(resolved, str) and resolved:
                lacking.discard(family)
                failover.clear_not_in_plan(account, _marked_models_of(account, family))
    return alias_map, bool(alias_map)


def _marked_models_of(account: str, family: str) -> list[str]:
    """Models of `family` that currently carry a runtime not-in-plan mark on `account`."""
    return [m for m in failover.not_in_plan_marks().get(account, {}) if family_of_model(m) == family]


def family_of_model(model: str | None) -> str | None:
    """The CLI family a requested model belongs to, or None.

    `claude-<family>-latest`, the bare family alias and concrete ids `claude-<family>-<version>`
    of a known family (discovered, or seen in a per-account status) all map to the family."""
    bare = _normalize_model(model)
    if not bare:
        return None
    known = set(_families)
    for lacking in _not_in_plan_families.values():
        known |= lacking
    if bare in known:
        return bare
    if bare.startswith(_ALIAS_PREFIX):
        segment = bare[len(_ALIAS_PREFIX):].split("-", 1)[0]
        if segment in known:
            return segment
    return None


def account_catalog(account: str) -> tuple[dict[str, str], frozenset[str]]:
    """What is known about `account` right now, without probing: (alias map of its last successful
    probe, whatever its age; families it answered with "not in plan")."""
    cache = _cache_of(account)
    return (dict(cache[0]) if cache else {}), frozenset(_not_in_plan_families.get(account, ()))


def latest_alias_models() -> list[str]:
    """The always-latest id of every known family, in family order."""
    return [_alias_for(f) for f in _families]


def _compose_model_list(concrete: list[str]) -> list[str]:
    """Always-latest aliases first, then the concrete current model ids (deduped)."""
    out = latest_alias_models()
    for m in concrete:
        if m not in out:
            out.append(m)
    return out


def _concrete_from(alias_map: dict[str, str]) -> list[str]:
    """Concrete ids of an alias map, deduped, in opus/sonnet/haiku order."""
    concrete: list[str] = []
    for alias in list(alias_map) if alias_map else latest_alias_models():
        resolved = alias_map.get(alias)
        if resolved and resolved not in concrete:
            concrete.append(resolved)
    return concrete


def _merge_accounts(primary: dict[str, str], alt: dict[str, str]) -> tuple[dict[str, str], list[str]]:
    """(alias map, concrete ids) of the union of both accounts.

    A family resolves to the primary account's model when the primary has one, else to the
    alternative account's. The concrete ids are the primary's first, then those only the
    alternative account resolves to -- so a model only one subscription carries is listed too."""
    merged = dict(primary)
    for alias, resolved in alt.items():
        merged.setdefault(alias, resolved)
    concrete = _concrete_from(primary) if primary else []
    for m in _concrete_from(alt) if alt else []:
        if m not in concrete:
            concrete.append(m)
    return merged, concrete


async def _account_view(account: str, bypass_cache: bool, force: bool) -> tuple[dict[str, str], str]:
    """(alias map, source) of one account, probing it when its cache does not answer.

    The primary account keeps the contract described in `list_models_with_source_async`. The
    alternative account follows it with one difference: an ordinary call without a fresh cache is
    throttled like a forced one, so a broken reserve login cannot turn every model-list request
    into a round of failing probes."""
    cache = _cache_of(account)
    age = time.time() - cache[1] if cache is not None else None
    cached_fresh = age is not None and age < _MODEL_CACHE_TTL

    if cached_fresh and not bypass_cache:
        return dict(cache[0]), SOURCE_LIVE  # type: ignore[index]

    throttle_applies = bypass_cache or account == failover.ALT
    if throttle_applies and not force and _throttled(_last_attempt_of(account)):
        if cached_fresh:
            return dict(cache[0]), SOURCE_LIVE  # type: ignore[index]
        log.warning("claude model probe of %s throttled; serving no live mapping for it", account)
        return {}, SOURCE_DEGRADED

    _set_last_attempt(account, time.time())
    alias_map, probed = await _resolve_alias_map(account)
    if probed:
        # Merge rather than replace: `probed` is true as soon as ONE family answered, so a
        # partial run must not drop the families that answered on an earlier run. A family
        # the account now answers with "not in plan" leaves the mapping.
        cache = _cache_of(account)
        if cache is not None:
            alias_map = {**cache[0], **alias_map}
        lacking = _not_in_plan_families.get(account, set())
        alias_map = {a: m for a, m in alias_map.items() if _family_of(a) not in lacking}
        _set_cache(account, (alias_map, time.time()))
        return dict(alias_map), SOURCE_LIVE

    if cached_fresh:
        # A forced re-resolution that failed must not throw away a mapping that still holds.
        log.warning("claude model re-resolution of %s failed; keeping the cached mapping", account)
        return dict(cache[0]), SOURCE_DEGRADED  # type: ignore[index]
    return {}, SOURCE_DEGRADED


async def list_models_with_source_async(
    bypass_cache: bool = False,
    force: bool = False,
) -> tuple[list[str], str, dict[str, str]]:
    """Model list plus its provenance and the alias-to-concrete mapping.

    Returns (model ids, source, alias map). The source is "live" when the CLI answered the
    probe and "degraded" when every probe failed — in the degraded case the ids fall back to
    STATIC_MODELS and the result is deliberately NOT cached, so an ordinary next call probes again
    (a forced one waits out the throttle interval below). An expired OAuth session must not look
    like a fresh answer for 24 hours.

    `bypass_cache` requests a fresh resolution; it is throttled by ATTEMPT rather than by success,
    because a failed probe writes no cache entry and a success-based throttle would therefore not
    throttle at all in the outage it exists to contain. The throttle applies only to such forced
    re-resolutions — an ordinary call after a failure still probes, as the no-caching promise above
    states. `force` skips the throttle entirely and is reserved for the internal periodic sweep,
    which is the only remaining path that discovers a new model during an outage.

    With an alternative account logged in (failover.alt_configured) both accounts are probed and
    the result is their union: accounts can carry different models. The alias map takes each
    family from the primary account and falls back to the alternative one; the source is "live"
    when at least one account answered live.
    """
    async with _model_cache_lock:
        accounts = _accounts()
        views = await asyncio.gather(*(_account_view(a, bypass_cache, force) for a in accounts))
    primary_map, primary_source = views[0]
    alt_map, alt_source = views[1] if len(views) > 1 else ({}, SOURCE_DEGRADED)
    alias_map, concrete = _merge_accounts(primary_map, alt_map)
    source = SOURCE_LIVE if SOURCE_LIVE in (primary_source, alt_source) else SOURCE_DEGRADED
    if not alias_map:
        log.warning("claude model probe failed entirely; serving static list as degraded")
        return _compose_model_list(list(STATIC_MODELS)), SOURCE_DEGRADED, {}
    return _compose_model_list(concrete), source, alias_map


def _throttled(last_attempt_at: float | None) -> bool:
    """True when a resolution was already attempted inside the minimum interval."""
    return last_attempt_at is not None and time.time() - last_attempt_at < _MODEL_REFRESH_MIN_INTERVAL


async def list_models_async(bypass_cache: bool = False, force: bool = False) -> list[str]:
    """Always-current model list: always-latest aliases + the concrete newest model ids
    (resolved via the CLI's own alias resolution, cached 24h). Falls back to STATIC_MODELS
    when probing is unavailable, so the list is never stale and needs no manual updates."""
    models, _source, _aliases = await list_models_with_source_async(bypass_cache, force)
    return models


async def resolve_alias_map_async() -> dict[str, str]:
    """Current alias-to-concrete mapping, empty when no successful probe is available."""
    _models, _source, alias_map = await list_models_with_source_async()
    return alias_map


def list_models() -> list[str]:
    """Synchronous shim (startup / fallback): always-latest aliases + cached-or-static concrete ids
    of every account."""
    alt = _alt_model_cache[0] if _alt_model_cache and failover.alt_configured() else {}
    primary = _model_cache[0] if _model_cache else {}
    if not primary and not alt:
        return _compose_model_list(list(STATIC_MODELS))
    return _compose_model_list(_merge_accounts(primary, alt)[1])


async def complete_document(
    system_prompt: str,
    user_instruction: str,
    document: str,
    workspace_path: Path,
    model: str | None,
    max_tokens: int | None,
    context_document: str | None = None,
    *,
    reasoning_effort: str | None = None,
) -> tuple[str, UsageParts]:
    """
    Calls the claude CLI in document-edit mode: the CLI reads draft.md in the given
    workspace, applies the instruction, and writes the revised document back.
    Returns (content of draft.md after the CLI exits, token usage).

    Uses --allowedTools Read,Edit,Write with --permission-mode acceptEdits so the
    agent can edit files without interactive confirmation prompts.

    context_document holds large background context (grounding + advisor notes); it is
    offloaded to context.md when oversized (see workspace.materialize_context).
    """
    async with cli_process.slot(_semaphore, "claude CLI (document mode)"):
        return await _run_claude_document(
            system_prompt, user_instruction, document, workspace_path, model, max_tokens,
            context_document, reasoning_effort=reasoning_effort,
        )


async def complete(
    prompt: str, model: str | None, max_tokens: int | None, *, reasoning_effort: str | None = None
) -> str:
    """
    Calls the claude CLI in print mode and returns the raw text output.

    The semaphore limits concurrent calls to respect subscription rate limits.
    """
    text, _ = await complete_with_usage(prompt, model, max_tokens, reasoning_effort=reasoning_effort)
    return text


async def complete_with_usage(
    prompt: str, model: str | None, max_tokens: int | None, *, reasoning_effort: str | None = None,
    system_prompt: str | None = None, image_refs: bool = False,
) -> tuple[str, UsageParts]:
    """Like complete(), but also returns the real token usage reported by the CLI.

    With `system_prompt` set the call runs in lean mode (see _run_claude_lean): the caller's
    system prompt REPLACES the Claude Code agent prompt. Without it the argv is unchanged.
    """
    async with cli_process.slot(_semaphore, "claude CLI"):
        if system_prompt is not None:
            async with ephemeral_dir() as workspace:
                return await _run_claude_lean(
                    prompt, system_prompt, model, workspace,
                    reasoning_effort=reasoning_effort, image_refs=image_refs)
        return await _run_claude(prompt, model, max_tokens, reasoning_effort=reasoning_effort)


async def complete_agentic_file(
    instruction: str, model: str | None, *, reasoning_effort: str | None = None
) -> tuple[str, UsageParts]:
    """Agentic tool use via file authoring.

    The claude CLI runs in an ephemeral workspace and writes its decision (the next tool call,
    or a final answer) to decision.json — a normal document-mode write, which sidesteps the
    CLI's prompt-injection refusal of the "reply with raw tool-call JSON" protocol. Returns the
    raw decision.json text (parsed by the caller via tool_use_parser.parse_decision) plus usage.
    """
    async with cli_process.slot(_semaphore, "claude CLI (agentic file mode)"):
        async with ephemeral_dir() as workspace:
            return await _run_claude_decision(
                instruction, model, workspace, reasoning_effort=reasoning_effort)


async def _run_claude_decision(
    instruction: str, model: str | None, workspace_path: Path,
    *, reasoning_effort: str | None = None,
) -> tuple[str, UsageParts]:
    # Read,Edit,Write only (no Bash/web) + acceptEdits so the agent writes decision.json without
    # prompts. --output-format json: the decision text comes from the file; stdout carries usage.
    args = [
        "claude", "-p",
        "--output-format", "json",
        "--allowedTools", "Read,Edit,Write",
        "--permission-mode", "acceptEdits",
        "--no-session-persistence",
    ]
    if model:
        args += ["--model", _normalize_model(model)]
    args += _effort_args(reasoning_effort)

    # Offload to instruction.md if the instruction would exceed the per-argument OS limit (E2BIG).
    args.append(finalize_decision_instruction(workspace_path, instruction))

    env = policy.agent_env(current_home())

    proc = await cli_process.spawn(
        *args,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=str(workspace_path),
        env=env,
    )
    stdout, stderr = await cli_process.communicate(
        proc, None, CLI_TIMEOUT_SECONDS,
        lambda: RuntimeError(f"claude CLI (agentic file mode) timed out after {CLI_TIMEOUT_SECONDS}s"),
        "claude CLI (agentic file mode)")

    if proc.returncode != 0:
        # The CLI reports the human-readable cause in the JSON "result" field. Since CLI
        # 2.1.219 the (long) usage object precedes it, so a naive prefix truncation cut the
        # cause off and failover.classify() saw only "exited with code" — a session-limit
        # outage was raised as "transport" instead of "limit" (incident 2026-07-24).
        raise RuntimeError(
            f"claude CLI (agentic file mode) exited with code {proc.returncode}: "
            f"{claude_failure_detail(stdout.decode(errors='replace'), stderr.decode(errors='replace'))}"
        )

    # Usage (and a fallback answer) come from the stdout JSON; the decision itself is the file.
    usage = UsageParts()
    fallback_text = ""
    try:
        data = json.loads(stdout.decode(errors="replace").strip())
        if isinstance(data, dict):
            _raise_if_not_in_plan(data)
            fallback_text, usage = _extract_result_and_usage(data, "")
    except (json.JSONDecodeError, ValueError):
        pass

    decision = read_decision_file(workspace_path)
    if not decision:
        # The agent answered to stdout instead of writing the file — use that as the decision text
        # (parse_decision will treat non-JSON as a final answer).
        decision = fallback_text
    return decision, usage


async def stream(
    prompt: str, model: str | None, max_tokens: int | None, *, reasoning_effort: str | None = None
):
    """Stream the claude CLI output as (kind, payload) events.

    Yields ("delta", text) for each token chunk and finally ("usage", UsageParts).
    The semaphore is held for the whole stream to respect subscription rate limits.
    """
    async with cli_process.slot(_semaphore, "claude CLI (stream)"):
        async with aclosing(_stream_claude(
                prompt, model, max_tokens, reasoning_effort=reasoning_effort)) as events:
            async for event in events:
                yield event


async def _stream_claude(
    prompt: str, model: str | None, max_tokens: int | None, *, reasoning_effort: str | None = None
):
    args = [
        "claude", "-p",
        "--output-format", "stream-json",
        "--verbose",
        "--include-partial-messages",
        "--no-session-persistence",
        *_web_tool_args(),
    ]
    if model:
        args += ["--model", _normalize_model(model)]
    args += _effort_args(reasoning_effort)

    env = policy.agent_env(current_home())
    proc = await cli_process.spawn(
        *args,
        stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        env=env,
    )

    async def _feed() -> None:
        try:
            proc.stdin.write(prompt.encode("utf-8"))
            await proc.stdin.drain()
        finally:
            proc.stdin.close()

    feeder = asyncio.create_task(_feed())
    stderr = cli_process.StderrDrain(proc.stderr)
    emitted = False
    final_text = ""
    usage = UsageParts()
    completed = False
    try:
        async with asyncio.timeout(cli_process.timeout(CLI_TIMEOUT_SECONDS)):
            async for raw_line in proc.stdout:
                line = raw_line.decode(errors="replace").strip()
                if not line:
                    continue
                try:
                    ev = json.loads(line)
                except (json.JSONDecodeError, ValueError):
                    continue
                if not isinstance(ev, dict):
                    continue
                etype = ev.get("type")
                if etype == "stream_event":
                    inner = ev.get("event", {})
                    if isinstance(inner, dict) and inner.get("type") == "content_block_delta":
                        delta = inner.get("delta", {})
                        if isinstance(delta, dict) and delta.get("type") == "text_delta" and delta.get("text"):
                            emitted = True
                            yield ("delta", str(delta["text"]))
                elif etype == "result":
                    if ev.get("is_error"):
                        status = ev.get("api_error_status")
                        raise RuntimeError(
                            f"claude CLI API error (status={status}): "
                            f"{clip_redacted(str(ev.get('result', '')), RESULT_CHARS)}"
                        )
                    final_text, usage = _extract_result_and_usage(ev, "")
            # Inside the time limit too: a helper that keeps stderr open must not hold the
            # stream beyond it.
            await feeder
            await proc.wait()
            if proc.returncode not in (0, None):
                err = (await stderr.tail()).decode(errors="replace").strip()
                raise RuntimeError(
                    f"claude CLI (stream) exited with code {proc.returncode}: {clip_redacted(err, 200)}")
        completed = True
        # No partial deltas were emitted (older CLI / no partial support) — emit the final text once.
        if not emitted and final_text:
            yield ("delta", final_text)
        yield ("usage", usage)
    except TimeoutError:
        if cli_process.caller_limited():
            raise cli_process.caller_timeout("claude CLI (stream)") from None
        raise RuntimeError(f"claude CLI (stream) timed out after {CLI_TIMEOUT_SECONDS} seconds")
    finally:
        feeder.cancel()
        try:
            if not completed:
                await cli_process.stop(proc)
        finally:
            await stderr.close()


async def _run_claude_document(
    system_prompt: str,
    user_instruction: str,
    document: str,
    workspace_path: Path,
    model: str | None,
    max_tokens: int | None,
    context_document: str | None = None,
    *,
    reasoning_effort: str | None = None,
) -> tuple[str, UsageParts]:
    # Large background context goes to context.md (pointer) or inline if small; the file-contract
    # and user instruction always stay in the prompt.
    context_preamble = materialize_context(workspace_path, context_document)
    instruction = (
        context_preamble
        + "The document you are editing is located at draft.md in the current directory. "
        "Read it, apply the revisions described below, then write the complete updated "
        "document back to draft.md. Do NOT output the document content to stdout — "
        "only edit the file.\n\n"
        + user_instruction
    )

    # --allowedTools Read,Edit,Write: restrict agent to file operations only (no web, no bash).
    # --permission-mode acceptEdits: auto-approve file edits without interactive prompts.
    # --append-system-prompt: injects the writer persona system prompt. Note: this is the one
    # remaining argv value not offloaded to a file; system prompts are operator-defined profiles
    # (a few KB), so they stay well under MAX_ARG_STRLEN. The user-driven variable content
    # (document, context, findings) is all file-backed via draft.md/context.md/instruction.md.
    # --output-format json: the document text is read from draft.md, but stdout still
    # carries the usage block + total_cost_usd, which we parse for faithful token accounting.
    args = [
        "claude", "-p",
        "--output-format", "json",
        "--allowedTools", "Read,Edit,Write",
        "--permission-mode", "acceptEdits",
        "--no-session-persistence",
        "--append-system-prompt", system_prompt,
    ]

    if model:
        args += ["--model", _normalize_model(model)]
    args += _effort_args(reasoning_effort)

    # Offload to instruction.md if the instruction would exceed the per-argument OS limit.
    args.append(finalize_instruction(workspace_path, instruction))

    env = policy.agent_env(current_home())

    proc = await cli_process.spawn(
        *args,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=str(workspace_path),
        env=env,
    )
    stdout, stderr = await cli_process.communicate(
        proc, None, CLI_TIMEOUT_SECONDS,
        lambda: RuntimeError(f"claude CLI (document mode) timed out after {CLI_TIMEOUT_SECONDS}s"),
        "claude CLI (document mode)")

    if proc.returncode != 0:
        # The cause fields (subtype, is_error, result, api_error_status) follow the long usage
        # block, so the former stdout[:200] kept only token counts (pre-study V1, 2026-11-24).
        raise RuntimeError(
            f"claude CLI (document mode) exited with code {proc.returncode}: "
            f"{claude_failure_detail(stdout.decode(errors='replace'), stderr.decode(errors='replace'))}"
        )

    try:
        parsed = json.loads(stdout.decode(errors="replace").strip())
    except (json.JSONDecodeError, ValueError):
        parsed = None
    if isinstance(parsed, dict):
        _raise_if_not_in_plan(parsed)

    draft_path = workspace_path / "draft.md"
    try:
        result = draft_path.read_text(encoding="utf-8")
    except FileNotFoundError:
        raise RuntimeError(
            "claude document mode: draft.md not found after CLI exit — "
            "the agent may have deleted or moved the file"
        )

    if result == document:
        log.warning("claude document mode: draft.md unchanged after CLI run — no edits applied")

    # The document text comes from draft.md; stdout JSON only contributes the usage block.
    usage = UsageParts()
    try:
        data = json.loads(stdout.decode(errors="replace").strip())
        if isinstance(data, dict):
            _, usage = _extract_result_and_usage(data, "")
    except (json.JSONDecodeError, ValueError):
        pass

    return result, usage


async def _run_claude(
    prompt: str, model: str | None, max_tokens: int | None, *, reasoning_effort: str | None = None
) -> tuple[str, UsageParts]:
    args = ["claude", "-p", "--output-format", "json", "--no-session-persistence", *_web_tool_args()]

    if model:
        args += ["--model", _normalize_model(model)]
    args += _effort_args(reasoning_effort)

    # Pass the prompt through stdin, never as an argv element. A single execve argument may
    # not exceed MAX_ARG_STRLEN (128 KB on Linux); reviewer/advisor prompts embed the full
    # draft (often >128 KB by later iterations), so an argv prompt fails the spawn with
    # OSError "Argument list too long" (E2BIG) and the proxy returns HTTP 500. claude -p
    # reads the prompt from stdin when no prompt positional is given.
    return await _exec_print(args, prompt)


# Lean print mode. The default `claude -p` call is a full Claude Code agent session: the
# agent system prompt, the built-in tool definitions, the synced skill list and the
# auto-memory of the working directory (/app) all precede the caller's text, ~23k tokens in
# total, and the caller's OpenAI system message arrives only as "[SYSTEM]" text inside the
# user turn. For role-play or "answer from your own perspective" prompts the agent persona
# then often declines in prose ("I haven't actually played this game"), which is the cause
# of the empty structured outputs seen on 2026-11-07. Lean mode passes the caller's system
# prompt as the real system prompt (replacing the agent prompt), disables the built-in tools
# (web tools only when the request references images) and skills, ignores MCP configs,
# runs in an empty per-call directory (no auto-memory) and writes no session transcript.
LEAN_DEFAULT_SYSTEM_PROMPT = (
    "You are a helpful assistant. Follow the user's instructions exactly."
)
_WEB_TOOLS = "WebSearch,WebFetch"
_IMAGE_FETCH_TOOLS = "WebFetch"


def _web_tool_args() -> list[str]:
    """Tool arguments of an agent-mode print call.

    Web search and fetch only when the request opted in (see policy.web_search_allowed).
    Otherwise every built-in tool is switched off, so the call behaves like plain text
    generation. Allowlist ONLY web tools, never Bash/Edit/Write, so there is no permission
    bypass. The value is a comma-separated single token: a space-separated variadic would
    greedily consume the trailing prompt positional.
    """
    if policy.web_search_allowed():
        return ["--allowedTools", _WEB_TOOLS]
    return ["--tools", ""]


def _lean_tools(image_refs: bool) -> str:
    """Tools of a lean call: web search and fetch on request, otherwise fetch only when the
    prompt references http(s) images (the model has to download them), otherwise none."""
    if policy.web_search_allowed():
        return _WEB_TOOLS
    return _IMAGE_FETCH_TOOLS if image_refs else ""


def lean_args(system_prompt_file: Path, model: str | None, *,
              reasoning_effort: str | None = None, image_refs: bool = False) -> list[str]:
    """argv of a lean print-mode call (prompt goes through stdin)."""
    tools = _lean_tools(image_refs)
    args = [
        "claude", "-p",
        "--output-format", "json",
        "--system-prompt-file", str(system_prompt_file),
        "--tools", tools,
        "--disable-slash-commands",
        "--strict-mcp-config",
        "--no-session-persistence",
    ]
    if tools:
        args += ["--allowedTools", tools]
    if model:
        args += ["--model", _normalize_model(model)]
    args += _effort_args(reasoning_effort)
    return args


async def _run_claude_lean(
    prompt: str, system_prompt: str, model: str | None, workspace_path: Path,
    *, reasoning_effort: str | None = None, image_refs: bool = False,
) -> tuple[str, UsageParts]:
    # The system prompt goes through a file: no argv size limit, and it never shows up in ps.
    sp_file = workspace_path / "system-prompt.md"
    sp_file.write_text(system_prompt or LEAN_DEFAULT_SYSTEM_PROMPT, encoding="utf-8")
    args = lean_args(sp_file, model, reasoning_effort=reasoning_effort, image_refs=image_refs)
    return await _exec_print(args, prompt, cwd=workspace_path)


async def _exec_print(
    args: list[str], prompt: str, cwd: Path | None = None
) -> tuple[str, UsageParts]:
    """Run a `claude -p --output-format json` argv with the prompt on stdin."""
    env = policy.agent_env(current_home())

    proc = await cli_process.spawn(
        *args,
        stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        env=env,
        **({"cwd": str(cwd)} if cwd is not None else {}),
    )
    stdout, stderr = await cli_process.communicate(
        proc, prompt.encode("utf-8"), CLI_TIMEOUT_SECONDS,
        lambda: RuntimeError(f"claude CLI timed out after {CLI_TIMEOUT_SECONDS} seconds"),
        "claude CLI")

    raw = stdout.decode(errors="replace").strip()

    data: dict | None = None
    try:
        parsed = json.loads(raw)
        if isinstance(parsed, dict):
            data = parsed
    except (json.JSONDecodeError, ValueError):
        pass

    if proc.returncode != 0:
        # Errors may appear in stdout JSON (is_error: true) rather than stderr.
        stderr_txt = stderr.decode(errors="replace")
        if data and data.get("is_error"):
            raise RuntimeError(f"claude CLI error: {claude_failure_detail(raw, stderr_txt)}")
        raise RuntimeError(
            f"claude CLI exited with code {proc.returncode}: {claude_failure_detail(raw, stderr_txt)}")

    # claude often exits 0 even when the upstream API failed (auth 401, server 500, …);
    # the failure surfaces as is_error:true in the JSON. Treat it as an error so it maps
    # to an OpenAI error envelope instead of being returned as a "successful" result.
    if data and data.get("is_error"):
        status = data.get("api_error_status")
        raise RuntimeError(
            f"claude CLI API error (status={status}): "
            f"{clip_redacted(str(data.get('result', '')), RESULT_CHARS)}"
        )

    return _extract_result_and_usage(data, raw)


def _raise_if_not_in_plan(data: dict) -> None:
    """Raise when a run that exited 0 carries the CLI's "not in plan" answer.

    The file-writing modes (document, decision file) read their result from a file and would
    otherwise take the refusal for an unchanged draft or a final answer. Only this one wording is
    turned into an error here; every other exit-0 result keeps its earlier handling."""
    if data.get("is_error") and failover.is_not_in_plan(str(data.get("result", ""))):
        raise RuntimeError(
            f"claude CLI API error (status={data.get('api_error_status')}): "
            f"{clip_redacted(str(data.get('result', '')), RESULT_CHARS)}"
        )


def _extract_result_and_usage(data: dict | None, raw: str) -> tuple[str, UsageParts]:
    """
    claude -p --output-format json outputs a JSON object with a "result" field plus a
    "usage" block and "total_cost_usd". Maps the usage to OpenAI accounting:
    prompt_tokens counts all input (fresh + cache read + cache creation); cached_tokens is
    the cache-read subset. Falls back to the raw string with empty usage if parsing failed.

    The CLI's top-level usage fields are SUMMED over all internal agentic turns of the
    run (usage.iterations), so on multi-turn runs they overstate the context size by an
    order of magnitude (a 30K-token conversation reports 600K+ prompt tokens). OpenAI
    prompt_tokens semantically describe the prompt of THIS completion, and consumers
    (e.g. Hermes' context compressor) treat them as the live context size — so the
    input side is taken from the LAST iteration when the breakdown is available.
    output_tokens stay cumulative: every generated token belongs to this completion.
    """
    if data is None:
        return raw, UsageParts()

    u = data.get("usage") or {}
    iterations = u.get("iterations")
    last_iter = iterations[-1] if isinstance(iterations, list) and iterations and isinstance(iterations[-1], dict) else u
    fresh = _int(last_iter, "input_tokens")
    cache_read = _int(last_iter, "cache_read_input_tokens")
    cache_creation = _int(last_iter, "cache_creation_input_tokens")
    usage = UsageParts(
        input_tokens=fresh + cache_read + cache_creation,
        output_tokens=_int(u, "output_tokens"),
        cached_tokens=cache_read,
        cost_usd=data.get("total_cost_usd"),
    )
    text = str(data["result"]) if "result" in data else raw
    return text, usage


def _int(d: dict, key: str) -> int:
    try:
        return int(d.get(key, 0) or 0)
    except (TypeError, ValueError):
        return 0
