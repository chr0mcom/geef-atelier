"""Adapter that runs the claude CLI and returns its text output."""
from __future__ import annotations

import asyncio
import json
import logging
import os
import re
import time
from pathlib import Path

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

# Hard cap on how long a single claude CLI call may run.
# Increase for workloads that produce very long outputs (e.g. 30-page documents).
CLI_TIMEOUT_SECONDS = int(os.getenv("CLI_TIMEOUT_SECONDS", "1800"))

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
                env={**os.environ, "HOME": CLAUDE_HOME},
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


async def _probe_alias(alias: str) -> str | None:
    """Ask the claude CLI which concrete model the family alias currently resolves to.

    Runs under the shared CLI semaphore so a model probe cannot outrank real completions,
    and always reaps its subprocess: without the kill a timed-out probe would leave a
    `claude` process running against the same subscription.
    """
    args = ["claude", "-p", "--model", alias, "--output-format", "json"]
    env = {**os.environ, "HOME": CLAUDE_HOME}
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


async def _resolve_alias_map() -> tuple[dict[str, str], bool]:
    """Resolve each family alias to its current concrete model id via the CLI itself.

    Probes run concurrently so a cache refresh costs one round-trip, not three. The second
    tuple element is False when not a single probe succeeded — the caller must not cache
    that result and must report it as degraded rather than live.
    """
    families = await _discover_families()
    results = await asyncio.gather(*(_probe_alias(a) for a in families))
    alias_map = {
        _alias_for(family): resolved
        for family, resolved in zip(families, results)
        if resolved
    }
    return alias_map, bool(alias_map)


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
    """
    global _model_cache, _last_attempt_at
    async with _model_cache_lock:
        age = time.time() - _model_cache[1] if _model_cache is not None else None
        cached_fresh = age is not None and age < _MODEL_CACHE_TTL

        if cached_fresh and not bypass_cache:
            alias_map = _model_cache[0]  # type: ignore[index]
            return _compose_model_list(_concrete_from(alias_map)), SOURCE_LIVE, dict(alias_map)

        if bypass_cache and not force and _throttled(_last_attempt_at):
            if cached_fresh:
                alias_map = _model_cache[0]  # type: ignore[index]
                return _compose_model_list(_concrete_from(alias_map)), SOURCE_LIVE, dict(alias_map)
            log.warning("claude model probe throttled; serving static list as degraded")
            return _compose_model_list(list(STATIC_MODELS)), SOURCE_DEGRADED, {}

        _last_attempt_at = time.time()
        alias_map, probed = await _resolve_alias_map()
        if probed:
            # Merge rather than replace: `probed` is true as soon as ONE family answered, so a
            # partial run must not drop the families that answered on an earlier run.
            if _model_cache is not None:
                alias_map = {**_model_cache[0], **alias_map}
            _model_cache = (alias_map, time.time())
            return _compose_model_list(_concrete_from(alias_map)), SOURCE_LIVE, dict(alias_map)

        if cached_fresh:
            # A forced re-resolution that failed must not throw away a mapping that still holds.
            alias_map = _model_cache[0]  # type: ignore[index]
            log.warning("claude model re-resolution failed; keeping the cached mapping")
            return _compose_model_list(_concrete_from(alias_map)), SOURCE_DEGRADED, dict(alias_map)

    log.warning("claude model probe failed entirely; serving static list as degraded")
    return _compose_model_list(list(STATIC_MODELS)), SOURCE_DEGRADED, {}


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
    """Synchronous shim (startup / fallback): always-latest aliases + cached-or-static concrete ids."""
    concrete = _concrete_from(_model_cache[0]) if _model_cache else list(STATIC_MODELS)
    return _compose_model_list(concrete)


async def complete_document(
    system_prompt: str,
    user_instruction: str,
    document: str,
    workspace_path: Path,
    model: str | None,
    max_tokens: int | None,
    context_document: str | None = None,
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
    async with _semaphore:
        return await _run_claude_document(
            system_prompt, user_instruction, document, workspace_path, model, max_tokens,
            context_document,
        )


async def complete(prompt: str, model: str | None, max_tokens: int | None) -> str:
    """
    Calls the claude CLI in print mode and returns the raw text output.

    The semaphore limits concurrent calls to respect subscription rate limits.
    """
    text, _ = await complete_with_usage(prompt, model, max_tokens)
    return text


async def complete_with_usage(
    prompt: str, model: str | None, max_tokens: int | None
) -> tuple[str, UsageParts]:
    """Like complete(), but also returns the real token usage reported by the CLI."""
    async with _semaphore:
        return await _run_claude(prompt, model, max_tokens)


async def complete_agentic_file(
    instruction: str, model: str | None
) -> tuple[str, UsageParts]:
    """Agentic tool use via file authoring.

    The claude CLI runs in an ephemeral workspace and writes its decision (the next tool call,
    or a final answer) to decision.json — a normal document-mode write, which sidesteps the
    CLI's prompt-injection refusal of the "reply with raw tool-call JSON" protocol. Returns the
    raw decision.json text (parsed by the caller via tool_use_parser.parse_decision) plus usage.
    """
    async with _semaphore:
        async with ephemeral_dir() as workspace:
            return await _run_claude_decision(instruction, model, workspace)


async def _run_claude_decision(
    instruction: str, model: str | None, workspace_path: Path
) -> tuple[str, UsageParts]:
    # Read,Edit,Write only (no Bash/web) + acceptEdits so the agent writes decision.json without
    # prompts. --output-format json: the decision text comes from the file; stdout carries usage.
    args = [
        "claude", "-p",
        "--output-format", "json",
        "--allowedTools", "Read,Edit,Write",
        "--permission-mode", "acceptEdits",
    ]
    if model:
        args += ["--model", _normalize_model(model)]

    # Offload to instruction.md if the instruction would exceed the per-argument OS limit (E2BIG).
    args.append(finalize_decision_instruction(workspace_path, instruction))

    env = {**os.environ, "HOME": CLAUDE_HOME}

    proc = await asyncio.create_subprocess_exec(
        *args,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=str(workspace_path),
        env=env,
    )
    try:
        stdout, stderr = await asyncio.wait_for(proc.communicate(), timeout=CLI_TIMEOUT_SECONDS)
    except asyncio.TimeoutError:
        proc.kill()
        await proc.wait()
        raise RuntimeError(f"claude CLI (agentic file mode) timed out after {CLI_TIMEOUT_SECONDS}s")

    if proc.returncode != 0:
        stderr_msg = stderr.decode(errors="replace").strip()
        stdout_txt = stdout.decode(errors="replace")
        # The CLI reports the human-readable cause in the JSON "result" field. Since CLI
        # 2.1.219 the (long) usage object precedes it, so a naive prefix truncation cut the
        # cause off and failover.classify() saw only "exited with code" — a session-limit
        # outage was raised as "transport" instead of "limit" (incident 2026-07-24).
        cause = ""
        try:
            cause = str(json.loads(stdout_txt).get("result") or "")[:300]
        except ValueError:
            pass
        raise RuntimeError(
            f"claude CLI (agentic file mode) exited with code {proc.returncode}: "
            f"{cause or stderr_msg or stdout_txt[:200]}"
        )

    # Usage (and a fallback answer) come from the stdout JSON; the decision itself is the file.
    usage = UsageParts()
    fallback_text = ""
    try:
        data = json.loads(stdout.decode(errors="replace").strip())
        if isinstance(data, dict):
            fallback_text, usage = _extract_result_and_usage(data, "")
    except (json.JSONDecodeError, ValueError):
        pass

    decision = read_decision_file(workspace_path)
    if not decision:
        # The agent answered to stdout instead of writing the file — use that as the decision text
        # (parse_decision will treat non-JSON as a final answer).
        decision = fallback_text
    return decision, usage


async def stream(prompt: str, model: str | None, max_tokens: int | None):
    """Stream the claude CLI output as (kind, payload) events.

    Yields ("delta", text) for each token chunk and finally ("usage", UsageParts).
    The semaphore is held for the whole stream to respect subscription rate limits.
    """
    async with _semaphore:
        async for event in _stream_claude(prompt, model, max_tokens):
            yield event


async def _stream_claude(prompt: str, model: str | None, max_tokens: int | None):
    args = [
        "claude", "-p",
        "--output-format", "stream-json",
        "--verbose",
        "--include-partial-messages",
        "--allowedTools", "WebSearch,WebFetch",
    ]
    if model:
        args += ["--model", _normalize_model(model)]

    env = {**os.environ, "HOME": CLAUDE_HOME}
    proc = await asyncio.create_subprocess_exec(
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
    emitted = False
    final_text = ""
    usage = UsageParts()
    try:
        async with asyncio.timeout(CLI_TIMEOUT_SECONDS):
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
                            f"claude CLI API error (status={status}): {ev.get('result', '')[:300]}"
                        )
                    final_text, usage = _extract_result_and_usage(ev, "")
        await feeder
        await proc.wait()
        if proc.returncode not in (0, None):
            err = (await proc.stderr.read()).decode(errors="replace").strip()
            raise RuntimeError(f"claude CLI (stream) exited with code {proc.returncode}: {err[:200]}")
        # No partial deltas were emitted (older CLI / no partial support) — emit the final text once.
        if not emitted and final_text:
            yield ("delta", final_text)
        yield ("usage", usage)
    except TimeoutError:
        raise RuntimeError(f"claude CLI (stream) timed out after {CLI_TIMEOUT_SECONDS} seconds")
    finally:
        feeder.cancel()
        if proc.returncode is None:
            proc.kill()
            await proc.wait()


async def _run_claude_document(
    system_prompt: str,
    user_instruction: str,
    document: str,
    workspace_path: Path,
    model: str | None,
    max_tokens: int | None,
    context_document: str | None = None,
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
        "--append-system-prompt", system_prompt,
    ]

    if model:
        args += ["--model", _normalize_model(model)]

    # Offload to instruction.md if the instruction would exceed the per-argument OS limit.
    args.append(finalize_instruction(workspace_path, instruction))

    env = {**os.environ, "HOME": CLAUDE_HOME}

    proc = await asyncio.create_subprocess_exec(
        *args,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        cwd=str(workspace_path),
        env=env,
    )
    try:
        stdout, stderr = await asyncio.wait_for(proc.communicate(), timeout=CLI_TIMEOUT_SECONDS)
    except asyncio.TimeoutError:
        proc.kill()
        await proc.wait()  # reap the killed process so it does not linger as a zombie
        raise RuntimeError(f"claude CLI (document mode) timed out after {CLI_TIMEOUT_SECONDS}s")

    if proc.returncode != 0:
        stderr_msg = stderr.decode(errors="replace").strip()
        raise RuntimeError(
            f"claude CLI (document mode) exited with code {proc.returncode}: "
            f"{stderr_msg or stdout.decode(errors='replace')[:200]}"
        )

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
    prompt: str, model: str | None, max_tokens: int | None
) -> tuple[str, UsageParts]:
    # Allowlist ONLY web tools — no Bash/Edit/Write, so no full permission bypass.
    # Comma-separated single token: a space-separated variadic would greedily
    # consume the trailing prompt positional.
    args = ["claude", "-p", "--output-format", "json", "--allowedTools", "WebSearch,WebFetch"]

    if model:
        args += ["--model", _normalize_model(model)]

    # Pass the prompt through stdin, never as an argv element. A single execve argument may
    # not exceed MAX_ARG_STRLEN (128 KB on Linux); reviewer/advisor prompts embed the full
    # draft (often >128 KB by later iterations), so an argv prompt fails the spawn with
    # OSError "Argument list too long" (E2BIG) and the proxy returns HTTP 500. claude -p
    # reads the prompt from stdin when no prompt positional is given.
    env = {**os.environ, "HOME": CLAUDE_HOME}

    proc = await asyncio.create_subprocess_exec(
        *args,
        stdin=asyncio.subprocess.PIPE,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE,
        env=env,
    )
    try:
        stdout, stderr = await asyncio.wait_for(
            proc.communicate(input=prompt.encode("utf-8")), timeout=CLI_TIMEOUT_SECONDS)
    except asyncio.TimeoutError:
        proc.kill()
        await proc.wait()  # reap the killed process so it does not linger as a zombie
        raise RuntimeError(f"claude CLI timed out after {CLI_TIMEOUT_SECONDS} seconds")

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
        stderr_msg = stderr.decode(errors="replace").strip()
        if data and data.get("is_error"):
            raise RuntimeError(f"claude CLI error: {data.get('result', stderr_msg)}")
        raise RuntimeError(f"claude CLI exited with code {proc.returncode}: {stderr_msg or raw[:200]}")

    # claude often exits 0 even when the upstream API failed (auth 401, server 500, …);
    # the failure surfaces as is_error:true in the JSON. Treat it as an error so it maps
    # to an OpenAI error envelope instead of being returned as a "successful" result.
    if data and data.get("is_error"):
        status = data.get("api_error_status")
        raise RuntimeError(
            f"claude CLI API error (status={status}): {data.get('result', '')[:300]}"
        )

    return _extract_result_and_usage(data, raw)


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
