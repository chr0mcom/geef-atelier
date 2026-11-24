"""Adapter that runs the codex CLI and returns its text output."""
from __future__ import annotations

import asyncio
import json
import logging
import os
import re
import shutil
import tempfile
import time
from pathlib import Path
from typing import Any

import policy
from cli_errors import clip_redacted, codex_failure_detail
from usage import UsageParts
from workspace import (
    ephemeral_dir,
    finalize_decision_instruction,
    finalize_instruction,
    materialize_context,
    read_decision_file,
)

log = logging.getLogger(__name__)

MAX_CONCURRENT = int(os.getenv("CODEX_MAX_CONCURRENT", "2"))
_semaphore = asyncio.Semaphore(MAX_CONCURRENT)

# Home directory for codex auth — mounted as a volume in the container.
CODEX_HOME = os.getenv("CODEX_HOME", "/auth/codex")

# Hard cap on how long a single codex CLI call may run.
# Increase for workloads that produce very long outputs (e.g. 30-page documents).
CLI_TIMEOUT_SECONDS = int(os.getenv("CLI_TIMEOUT_SECONDS", "1800"))

# Image generation: one job at a time by default (it draws on the same ChatGPT subscription window
# as every other codex call) and a much shorter hard cap than a text call.
IMAGE_TIMEOUT_SECONDS = int(os.getenv("IMAGE_TIMEOUT_SECONDS", "300"))
# Clamped so that image jobs can never hold every regular codex slot: at least one stays free for
# text calls (with a single slot in total the one image job is all there is).
IMAGE_MAX_CONCURRENT = max(1, min(int(os.getenv("CODEX_IMAGE_MAX_CONCURRENT", "1")), MAX_CONCURRENT - 1))
_image_semaphore = asyncio.Semaphore(IMAGE_MAX_CONCURRENT)
# Image directories older than this are leftovers (a job never runs that long): swept at job start.
IMAGE_STALE_SECONDS = 3600


def _effort_args(reasoning_effort: str | None) -> list[str]:
    """Map a per-request reasoning_effort to a codex config override.

    Only a value the caller actually sent produces the override; without one the argv stays
    exactly as before, so codex keeps model_reasoning_effort from ~/.codex/config.toml
    (BF-WP-116). The quoted form is parsed by codex as a TOML string. Validation of the value
    happens once in main._validate_policy.
    """
    return ["-c", f'model_reasoning_effort="{reasoning_effort}"'] if reasoning_effort else []

_MODELS_CACHE_PATH = Path(os.getenv("CODEX_MODELS_CACHE", str(Path(CODEX_HOME) / "models_cache.json")))
"""The subscription's own model list, written by the codex CLI whenever it refreshes. It is the only source that
reflects what the ChatGPT subscription actually serves; the previous OpenRouter derivation listed API-only
"-pro"/":batch" ids and missed subscription ones."""

_CACHE_TTL = 86400
"""Upper bound of the in-memory cache (24 h); a changed file mtime triggers an earlier re-read."""

_model_cache: tuple[list[str], float] | None = None
_cache_mtime: float | None = None
_cache_lock = asyncio.Lock()
"""In-memory cache `(model_ids, fetched_at)`; `_cache_mtime` holds the source file mtime of that read."""

# Minimum spacing between forced re-fetches, and the timestamp of the last ATTEMPT. Mirrors the
# claude side so the throttle exists per provider, as the guard rule requires; a failed fetch writes
# no cache entry, so a success-based throttle would not throttle during an outage.
_MODEL_REFRESH_MIN_INTERVAL = int(os.getenv("CODEX_MODEL_REFRESH_MIN_INTERVAL", "60"))
_last_attempt_at: float | None = None

# Source markers reported alongside every model list, mirroring claude_adapter.
SOURCE_LIVE = "live"
SOURCE_DEGRADED = "degraded"
SOURCE_STATIC = "static"

_FALLBACK_MODELS = [
    "gpt-6.2-sol",
    "gpt-6.2-astra",
    "gpt-6.1-terra",
]
"""Curated list served when the CLI has not written its cache yet (fresh volume) or the cache is unusable."""


class NoLiveSourceError(RuntimeError):
    """Raised when there is no live source at all (the CLI has not written models_cache.json yet).

    Distinct from a failed read on purpose: a missing cache file is a missing capability, not an
    outage, and reporting it as "degraded" would put a permanent red "provider unreachable" banner
    on every codex picker until the CLI happens to run once.
    """


def _is_listed_subscription_model(entry: Any) -> bool:
    if not isinstance(entry, dict):
        return False
    slug = entry.get("slug")
    if not isinstance(slug, str) or not slug.strip():
        return False
    if entry.get("visibility") != "list":
        return False
    slug = slug.strip()
    return not (slug.endswith("-pro") or ":" in slug)


def _read_models_cache() -> list[str]:
    """Reads the subscription model list from the codex CLI cache.

    Only entries with ``visibility == "list"`` are returned, ordered by ``priority`` (entries
    without a priority go last); API-only ``-pro`` and ``:batch`` ids never appear. Raises
    NoLiveSourceError when the file does not exist and ValueError when it is unusable.
    """
    if not _MODELS_CACHE_PATH.is_file():
        raise NoLiveSourceError(f"{_MODELS_CACHE_PATH} not found")

    data = json.loads(_MODELS_CACHE_PATH.read_text(encoding="utf-8"))
    entries = data.get("models") if isinstance(data, dict) else None
    if not isinstance(entries, list):
        raise ValueError("models_cache.json has no 'models' list")

    listed = [e for e in entries if _is_listed_subscription_model(e)]
    if not listed:
        raise ValueError("models_cache.json lists no subscription models")

    def _priority(entry: dict[str, Any]) -> tuple[int, float]:
        p = entry.get("priority")
        return (0, float(p)) if isinstance(p, (int, float)) and not isinstance(p, bool) else (1, 0.0)

    ordered = sorted(listed, key=_priority)
    seen: set[str] = set()
    result: list[str] = []
    for entry in ordered:
        slug = entry["slug"].strip()
        if slug not in seen:
            seen.add(slug)
            result.append(slug)
    return result


def _cache_file_mtime() -> float | None:
    try:
        return _MODELS_CACHE_PATH.stat().st_mtime
    except OSError:
        return None


async def _get_cached_models(bypass_cache: bool = False, force: bool = False) -> tuple[list[str], str]:
    """Returns (model ids, source).

    "live" for a fresh or cached read of models_cache.json, "degraded" when the file exists but is
    unusable, and "static" when the CLI has not written it yet. Neither non-success case is cached.

    The file system access (`stat` and the read) runs in a worker thread: the auth volume can be
    slow, and other requests of this worker must not wait on it.
    """
    global _model_cache, _cache_mtime, _last_attempt_at
    async with _cache_lock:
        current_mtime = await asyncio.to_thread(_cache_file_mtime)
        cached_fresh = (
            _model_cache is not None
            and current_mtime is not None
            and time.time() - _model_cache[1] < _CACHE_TTL
            and _cache_mtime == current_mtime
        )

        if cached_fresh and not bypass_cache:
            return _model_cache[0], SOURCE_LIVE  # type: ignore[index]

        if bypass_cache and not force and _last_attempt_at is not None \
                and time.time() - _last_attempt_at < _MODEL_REFRESH_MIN_INTERVAL:
            # Freshness is re-checked here on purpose: reporting an EXPIRED entry as live would let
            # the throttle revive stale data, and the caller caches a live answer for another day.
            if cached_fresh:
                return _model_cache[0], SOURCE_LIVE  # type: ignore[index]
            if current_mtime is None:
                return _FALLBACK_MODELS, SOURCE_STATIC
            return (_model_cache[0] if _model_cache else _FALLBACK_MODELS), SOURCE_DEGRADED

        _last_attempt_at = time.time()
        try:
            mtime = current_mtime
            models = await asyncio.to_thread(_read_models_cache)
            _model_cache = (models, time.time())
            _cache_mtime = mtime
            log.info("Codex model list refreshed from %s: %s", _MODELS_CACHE_PATH, models)
            return models, SOURCE_LIVE
        except NoLiveSourceError as exc:
            log.warning("Codex model list has no live source: %s — serving the curated list", exc)
            return _FALLBACK_MODELS, SOURCE_STATIC
        except Exception as exc:
            log.warning("Failed to read codex models cache: %s — using cached/fallback", exc)
            return (_model_cache[0] if _model_cache else _FALLBACK_MODELS), SOURCE_DEGRADED


def list_models() -> list[str]:
    """Synchronous shim used at startup; returns fallback until async cache is warm."""
    return _model_cache[0] if _model_cache else _FALLBACK_MODELS


async def list_models_with_source_async(bypass_cache: bool = False, force: bool = False) -> tuple[list[str], str]:
    """Live model list plus its provenance, read from the codex CLI cache (re-read on change, at most every 24 h).

    `force` bypasses the attempt throttle and is reserved for the internal periodic sweep."""
    return await _get_cached_models(bypass_cache, force)


async def list_models_async(bypass_cache: bool = False, force: bool = False) -> list[str]:
    """Returns the live model list read from the codex CLI cache."""
    models, _source = await _get_cached_models(bypass_cache, force)
    return models


DEFAULT_CODEX_MODEL = "gpt-sol-latest"
"""Model of a codex call that names none, and of a request the failover hands over to codex.

An always-latest alias on purpose: it resolves against the live list, or against `_FALLBACK_MODELS`
without one, so it never reaches the CLI unresolved and never pins a retired id. The container's
codex config sets no default model. The failover (`main.FAILOVER_CODEX_MODEL`) and the codex
adapter of the generic CLI endpoint both use this one value.
"""

FAILOVER_MODEL = "codex-failover:" + DEFAULT_CODEX_MODEL
"""Internal marker the failover passes as model: resolves `DEFAULT_CODEX_MODEL`, and omits `-m` (CLI default) when the
current list has no Sol line. Never accepted from callers' requests as anything special beyond this path."""

ALIASES_COMPLETE = True
"""Reported as `x_aliases_complete` by both codex catalog endpoints.

The alias map is derived deterministically and completely from the model list: every line of the
list gets its `gpt-<line>-latest`, and no alias outside the map resolves. This holds for every
`x_source`, because the list is always a complete one (the live cache file, the last live read or
`_FALLBACK_MODELS`) and `resolve_model_alias` reads the same list through the same
`build_alias_map`. A caller may therefore replace its own alias snapshot with the reported map.
"""

_MODEL_ID_RE = re.compile(r"gpt-(\d+(?:\.\d+)*)-([a-z][a-z0-9]*)")
"""Always-latest aliases work like claude's `claude-opus-latest`: `gpt-<line>-latest` resolves to the newest listed
model of that line. The line is the last name segment of an id shaped `gpt-<version>-<line>` (sol, astra, terra,
luna, ...) and is derived, never enumerated, so a new line gets its alias the moment the CLI lists it."""
_ALIAS_ID_RE = re.compile(r"gpt-([a-z][a-z0-9]*)-latest")


def _parse_model_id(model_id: str) -> tuple[tuple[int, ...], str] | None:
    """(version tuple, line) of an id shaped `gpt-<version>-<line>`, or None.

    Trailing zero components are dropped so that `6` and `6.0` compare equal. `-pro` and
    `:batch` ids and anything with extra segments (`gpt-6.2-sol-mini`) are not part of a line.
    """
    match = _MODEL_ID_RE.fullmatch(model_id)
    if match is None or model_id.endswith("-pro"):
        return None
    version = [int(part) for part in match.group(1).split(".")]
    while len(version) > 1 and version[-1] == 0:
        version.pop()
    return tuple(version), match.group(2)


def alias_for_line(line: str) -> str:
    """The always-latest id surfaced to callers for a model line."""
    return f"gpt-{line}-latest"


def build_alias_map(models: list[str]) -> dict[str, str]:
    """Maps `gpt-<line>-latest` to the newest model of each line in `models`.

    Newest means the highest numeric version (gpt-6.2 > gpt-6.1 > gpt-6 > gpt-5.6); on equal
    versions the earlier list position wins. Aliases are ordered by the first appearance of
    their line in `models`.
    """
    best: dict[str, tuple[tuple[int, ...], str]] = {}
    for model_id in models:
        parsed = _parse_model_id(model_id)
        if parsed is None:
            continue
        version, line = parsed
        if line not in best or version > best[line][0]:
            best[line] = (version, model_id)
    return {alias_for_line(line): model_id for line, (_version, model_id) in best.items()}


def compose_model_list(models: list[str], aliases: dict[str, str]) -> list[str]:
    """Always-latest aliases first, then the concrete model ids (deduped)."""
    out = list(aliases)
    out.extend(m for m in models if m not in aliases)
    return out


async def list_models_with_aliases_async(
    bypass_cache: bool = False, force: bool = False
) -> tuple[list[str], str, dict[str, str]]:
    """(aliases + concrete ids, source, alias map). The alias map is built from the same list
    and source as the concrete ids, so without a live list it resolves against the fallback."""
    models, source = await _get_cached_models(bypass_cache, force)
    aliases = build_alias_map(models)
    return compose_model_list(models, aliases), source, aliases


async def resolve_model_alias(model: str | None) -> str | None:
    """The concrete model id behind an always-latest alias, or `model` unchanged.

    A provider prefix (`openai/gpt-sol-latest`) is ignored for the lookup. An alias that no
    listed model satisfies is returned untouched, so it behaves exactly like any other unknown
    model name instead of being silently replaced. Ids that do not look like an alias never
    touch the model cache.
    """
    if not model:
        return model
    bare = model.rsplit("/", 1)[-1]
    if _ALIAS_ID_RE.fullmatch(bare) is None:
        return model
    models, _source = await _get_cached_models()
    return build_alias_map(models).get(bare, model)


async def _model_arg(model: str) -> str:
    """The value for the CLI's `-m`: alias resolved to its concrete id, provider prefix stripped.

    The internal failover marker (`FAILOVER_MODEL`) is the one exception: it resolves `DEFAULT_CODEX_MODEL`, and when
    the current list has no line for it, an empty string is returned, so the caller omits `-m` and the CLI uses its
    own default instead of failing on an unresolvable alias. An explicitly requested alias is never rewritten that way.
    """
    if model == FAILOVER_MODEL:
        bare = (await resolve_model_alias(DEFAULT_CODEX_MODEL)).split("/")[-1]
        return "" if bare == DEFAULT_CODEX_MODEL else bare
    return (await resolve_model_alias(model)).split("/")[-1]


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
    Calls the codex CLI in document-edit mode: the CLI reads draft.md in the given
    workspace, applies the instruction, and writes the revised document back.
    Returns (content of draft.md after the CLI exits, token usage).

    Uses --sandbox workspace-write so the agent can only write within the workspace.
    System prompt is embedded in the instruction preamble (codex has no --append-system-prompt).

    context_document holds large background context (grounding + advisor notes); it is
    offloaded to context.md when oversized (see workspace.materialize_context).
    """
    async with _semaphore:
        return await _run_codex_document(
            system_prompt, user_instruction, document, workspace_path, model, max_tokens,
            context_document, reasoning_effort=reasoning_effort,
        )


async def complete(
    prompt: str, model: str | None, max_tokens: int | None, *, reasoning_effort: str | None = None
) -> str:
    """
    Calls the codex CLI in non-interactive mode and returns the raw text output.

    The semaphore limits concurrent calls to respect subscription rate limits.
    """
    text, _ = await complete_with_usage(prompt, model, max_tokens, reasoning_effort=reasoning_effort)
    return text


async def complete_with_usage(
    prompt: str, model: str | None, max_tokens: int | None, *, reasoning_effort: str | None = None
) -> tuple[str, UsageParts]:
    """Like complete(), but also returns the real token usage reported by the CLI."""
    async with _semaphore:
        return await _run_codex(prompt, model, max_tokens, reasoning_effort=reasoning_effort)


async def complete_agentic_file(
    instruction: str, model: str | None, *, reasoning_effort: str | None = None
) -> tuple[str, UsageParts]:
    """Agentic tool use via file authoring — the codex counterpart of
    claude_adapter.complete_agentic_file.

    The codex CLI carries its own agent persona: asked inline to "reply with raw tool-call
    JSON" for HOST-executed tools it does not know, it answers "these tools are not
    connected in this session" instead of following the protocol (incident 2026-07-24,
    Hermes case-writer via failover — zero tool calls, protocol violation, gave_up).
    Authoring decision.json is a normal workspace-write action the CLI performs without
    objection; the proxy reads the file back and returns standard OpenAI tool_calls.
    Without this, the claude→codex failover is useless for every agentic consumer."""
    async with _semaphore:
        async with ephemeral_dir() as workspace:
            return await _run_codex_decision(
                instruction, model, workspace, reasoning_effort=reasoning_effort)


def _search_args() -> list[str]:
    """`--search` (native web search, no per-call approval) only when the request opted in.

    It is a GLOBAL flag and must precede the `exec` subcommand; `codex exec` rejects it.
    """
    return ["--search"] if policy.web_search_allowed() else []


async def _run_codex_decision(
    instruction: str, model: str | None, workspace_path: Path,
    *, reasoning_effort: str | None = None,
) -> tuple[str, UsageParts]:
    # --sandbox workspace-write: decision.json is the only artifact we want; no host access.
    # No --search: the decision turn plans the next HOST tool call — web research inside the
    # planning step would only tempt the model to answer from its own findings instead of
    # delegating to the host's tools.
    args = [
        "codex", "exec", "--json", "--ephemeral",
        "--skip-git-repo-check",
        "--sandbox", "workspace-write",
        "-C", str(workspace_path),
    ]
    if model and (model_arg := await _model_arg(model)):
        args += ["-m", model_arg]
    args += _effort_args(reasoning_effort)

    # Offload to instruction.md if the instruction would exceed the safe argv size.
    args.append(finalize_decision_instruction(workspace_path, instruction))

    env = {**os.environ, "HOME": CODEX_HOME}

    proc = await asyncio.create_subprocess_exec(
        *args,
        stdin=asyncio.subprocess.DEVNULL,
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
        raise RuntimeError(f"codex CLI (agentic file mode) timed out after {CLI_TIMEOUT_SECONDS}s")

    stdout_txt = stdout.decode(errors="replace")
    if proc.returncode != 0:
        raise RuntimeError(
            f"codex CLI (agentic file mode) exited with code {proc.returncode}: "
            f"{codex_failure_detail(stdout_txt, stderr.decode(errors='replace'))}"
        )

    usage = _parse_codex_usage(stdout_txt)
    decision = read_decision_file(workspace_path)
    if not decision:
        # The agent answered on the event stream instead of writing the file — use that text
        # (parse_decision treats non-JSON as a final answer, and main retries once).
        decision = _parse_codex_text(stdout_txt)
    return decision, usage


async def stream(
    prompt: str, model: str | None, max_tokens: int | None, *, reasoning_effort: str | None = None
):
    """Stream the codex CLI output as (kind, payload) events.

    Yields ("delta", text) for agent message text and finally ("usage", UsageParts).
    codex exec --json emits the agent message as a completed item rather than token-level
    deltas, so output is emitted as one (or few) delta chunks; usage comes from turn.completed.
    """
    async with _semaphore:
        async for event in _stream_codex(prompt, model, max_tokens, reasoning_effort=reasoning_effort):
            yield event


async def _stream_codex(
    prompt: str, model: str | None, max_tokens: int | None, *, reasoning_effort: str | None = None
):
    args = ["codex", *_search_args(), "exec", "--json", "--ephemeral", "--skip-git-repo-check"]
    if model and (model_arg := await _model_arg(model)):
        args += ["-m", model_arg]
    args += _effort_args(reasoning_effort)
    args.append("-")

    env = {**os.environ, "HOME": CODEX_HOME}
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
    usage = UsageParts()
    emitted_text = ""
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
                # Final agent message arrives as a completed item; emit the new suffix as a delta.
                item = ev.get("item")
                if isinstance(item, dict) and item.get("type") == "agent_message" and item.get("text"):
                    text = str(item["text"])
                    if text.startswith(emitted_text):
                        delta = text[len(emitted_text):]
                    else:
                        delta = text
                    if delta:
                        emitted_text = text
                        yield ("delta", delta)
                # Usage on turn.completed (last one wins).
                u = ev.get("usage")
                if isinstance(u, dict):
                    cached = _cint(u, "cached_input_tokens")
                    reasoning = _cint(u, "reasoning_output_tokens")
                    usage = UsageParts(
                        input_tokens=_cint(u, "input_tokens"),
                        output_tokens=_cint(u, "output_tokens") + reasoning,
                        cached_tokens=cached,
                        reasoning_tokens=reasoning,
                    )
        await feeder
        await proc.wait()
        if proc.returncode not in (0, None):
            err = (await proc.stderr.read()).decode(errors="replace").strip()
            raise RuntimeError(
                f"codex CLI (stream) exited with code {proc.returncode}: {clip_redacted(err, 200)}")
        yield ("usage", usage)
    except TimeoutError:
        raise RuntimeError(f"codex CLI (stream) timed out after {CLI_TIMEOUT_SECONDS} seconds")
    finally:
        feeder.cancel()
        if proc.returncode is None:
            proc.kill()
            await proc.wait()


async def _run_codex_document(
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
    # Codex has no --append-system-prompt flag — embed system prompt as a [SYSTEM] preamble.
    # Large background context goes to context.md (pointer) or inline if small; the file-contract
    # and user instruction always stay in the prompt.
    context_preamble = materialize_context(workspace_path, context_document)
    instruction = (
        f"[SYSTEM]\n{system_prompt}\n\n"
        + context_preamble
        + "The document you are editing is located at draft.md in the current directory. "
        "Read it, apply the revisions described below, then write the complete updated "
        "document back to draft.md.\n\n"
        + user_instruction
    )

    # --sandbox workspace-write: restricts writes to the workspace directory.
    # -C <workspace>: sets the agent's workspace root (not just subprocess cwd).
    # --skip-git-repo-check: the proxy container is not a git repo.
    # --search is a GLOBAL flag and must precede the `exec` subcommand.
    # --json: emit the JSONL event stream so we can read real token usage; the document
    # text itself is read from draft.md, not from stdout.
    args = [
        "codex", *_search_args(), "exec", "--json", "--ephemeral",
        "--skip-git-repo-check",
        "--sandbox", "workspace-write",
        "-C", str(workspace_path),
    ]

    if model and (model_arg := await _model_arg(model)):
        args += ["-m", model_arg]
    args += _effort_args(reasoning_effort)

    # Offload to instruction.md if the instruction would exceed the per-argument OS limit.
    args.append(finalize_instruction(workspace_path, instruction))

    env = {**os.environ, "HOME": CODEX_HOME}

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
        raise RuntimeError(f"codex CLI (document mode) timed out after {CLI_TIMEOUT_SECONDS}s")

    if proc.returncode != 0:
        raise RuntimeError(
            f"codex CLI (document mode) exited with code {proc.returncode}: "
            f"{codex_failure_detail(stdout.decode(errors='replace'), stderr.decode(errors='replace'))}"
        )

    draft_path = workspace_path / "draft.md"
    try:
        result = draft_path.read_text(encoding="utf-8")
    except FileNotFoundError:
        raise RuntimeError(
            "codex document mode: draft.md not found after CLI exit — "
            "the agent may have deleted or moved the file"
        )

    if result == document:
        log.warning("codex document mode: draft.md unchanged after CLI run — no edits applied")

    usage = _parse_codex_usage(stdout.decode(errors="replace"))
    return result, usage


async def _run_codex(
    prompt: str, model: str | None, max_tokens: int | None, *, reasoning_effort: str | None = None
) -> tuple[str, UsageParts]:
    # codex exec writes the last message to a file; use a temp file to capture it.
    with tempfile.NamedTemporaryFile(mode="r", suffix=".txt", delete=False) as tmp:
        output_file = tmp.name

    try:
        # --skip-git-repo-check: the proxy container is not a git repo, and codex
        # exec otherwise refuses to run ("Not inside a trusted directory").
        # --search is a GLOBAL flag and must precede the `exec` subcommand
        # (codex exec rejects it). It enables the native Responses web_search
        # tool with no per-call approval.
        # --json: emit the JSONL event stream on stdout so we can read the real token
        # usage from the final turn.completed event. The final message text is still
        # captured reliably via --output-last-message (independent of stdout format).
        args = ["codex", *_search_args(), "exec", "--json", "--ephemeral", "--skip-git-repo-check"]

        if model and (model_arg := await _model_arg(model)):
            args += ["-m", model_arg]
        args += _effort_args(reasoning_effort)

        # Pass the prompt through stdin ("-"), never as an argv element. A single execve
        # argument may not exceed MAX_ARG_STRLEN (128 KB on Linux); reviewer/advisor prompts
        # embed the full draft (often >128 KB by later iterations), so an argv prompt fails
        # the spawn with OSError "Argument list too long" (E2BIG) and the proxy returns HTTP
        # 500. codex reads instructions from stdin when the prompt positional is "-".
        args += ["--output-last-message", output_file, "-"]

        env = {**os.environ, "HOME": CODEX_HOME}

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
            raise RuntimeError(f"codex CLI timed out after {CLI_TIMEOUT_SECONDS} seconds")

        if proc.returncode != 0:
            raise RuntimeError(
                f"codex CLI exited with code {proc.returncode}: "
                f"{codex_failure_detail(stdout.decode(errors='replace'), stderr.decode(errors='replace'))}"
            )

        usage = _parse_codex_usage(stdout.decode(errors="replace"))

        try:
            with open(output_file) as f:
                text = f.read().strip()
        except FileNotFoundError:
            text = ""

        # Fallback: if --output-last-message produced nothing, recover the final
        # agent_message text from the JSONL event stream.
        if not text:
            text = _parse_codex_text(stdout.decode(errors="replace"))

        return text, usage
    finally:
        try:
            os.unlink(output_file)
        except OSError:
            pass


#: Aspect-ratio wording per OpenAI `size` value. codex has no size switch; the hint goes into the
#: prompt, so it is a request to the model, not a guarantee (the response reports the real size).
IMAGE_SIZE_HINTS = {
    "1024x1024": "square (1:1)",
    "1536x1024": "landscape (3:2)",
    "1024x1536": "portrait (2:3)",
}

class NoImageError(RuntimeError):
    """codex finished normally but produced no image — in practice the model declined the prompt.
    Kept apart from other faults on purpose: it is the caller's prompt, not the backend, so it
    must neither be retried (each run costs ~40k tokens) nor be classified by the agent's wording
    (a refusal text that happens to say "limit" is not a rate limit)."""


_THREAD_ID_RE = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")
_PNG_MAGIC = b"\x89PNG\r\n\x1a\n"


async def generate_image(prompt: str, size: str | None = None) -> tuple[bytes, UsageParts]:
    """Generates one image with codex's `$imagegen` skill and returns (PNG bytes, token usage).

    The skill is only reachable through the agent, so this is a normal `codex exec` turn. The
    image is not part of the JSONL event stream: codex saves it under
    `$CODEX_HOME/generated_images/<thread_id>/` and the thread id is the first event. The proxy
    reads the file from there and removes the directory again — otherwise every image would stay
    in the auth volume for good.

    At most CODEX_IMAGE_MAX_CONCURRENT image jobs run at once, and each of them also holds one
    regular codex slot, so images can never starve text calls of both.
    """
    async with _image_semaphore:
        async with _semaphore:
            return await _run_codex_image(prompt, size)


async def _run_codex_image(prompt: str, size: str | None) -> tuple[bytes, UsageParts]:
    instruction = f"$imagegen {prompt}"
    hint = IMAGE_SIZE_HINTS.get(size or "")
    if hint:
        instruction += f"\n\nUse a {hint} aspect ratio."

    # The instruction is passed as argv (always starts with "$", never read as a flag); the caller
    # limits the prompt length, so it stays far below MAX_ARG_STRLEN. stdin is closed because
    # codex exec otherwise waits for more input.
    _sweep_stale_images()
    async with ephemeral_dir() as workspace:
        args = [
            "codex", "exec", "--json", "--ephemeral", "--skip-git-repo-check",
            "--sandbox", "workspace-write", "-C", str(workspace), instruction,
        ]
        proc = await asyncio.create_subprocess_exec(
            *args,
            stdin=asyncio.subprocess.DEVNULL,
            stdout=asyncio.subprocess.PIPE,
            stderr=asyncio.subprocess.PIPE,
            cwd=str(workspace),
            env={**os.environ, "HOME": CODEX_HOME},
        )
        try:
            stdout, stderr = await asyncio.wait_for(proc.communicate(), timeout=IMAGE_TIMEOUT_SECONDS)
        except BaseException as exc:  # timeout, or the request was cancelled (e.g. shutdown)
            if proc.returncode is None:
                proc.kill()
            await asyncio.shield(proc.wait())  # reap it: no zombie, no run that keeps burning quota
            if isinstance(exc, asyncio.TimeoutError):
                raise RuntimeError(f"codex CLI (image) timed out after {IMAGE_TIMEOUT_SECONDS}s") from None
            raise

    stdout_txt = stdout.decode(errors="replace")
    thread_id = _parse_thread_id(stdout_txt)
    image_dir = Path(CODEX_HOME) / "generated_images" / thread_id if thread_id else None
    try:
        if proc.returncode != 0:
            raise RuntimeError(
                f"codex CLI (image) exited with code {proc.returncode}: "
                f"{codex_failure_detail(stdout_txt, stderr.decode(errors='replace'))}")
        png = _read_generated_png(image_dir)
        if png is None:
            said = _parse_codex_text(stdout_txt)
            raise NoImageError(f"codex produced no image: {said[:300] or 'no agent message'}")
        return png, _parse_codex_usage(stdout_txt)
    finally:
        if image_dir is not None:
            shutil.rmtree(image_dir, ignore_errors=True)


def _sweep_stale_images() -> None:
    """Removes image directories left behind by a job that never reached its cleanup (timeout,
    cancellation, a restart mid-run). The age limit keeps this away from a run in flight."""
    root = Path(CODEX_HOME) / "generated_images"
    if not root.is_dir():
        return
    cutoff = time.time() - IMAGE_STALE_SECONDS
    for entry in root.iterdir():
        try:
            if entry.is_dir() and _THREAD_ID_RE.match(entry.name) and entry.stat().st_mtime < cutoff:
                shutil.rmtree(entry, ignore_errors=True)
                log.info("Removed stale generated image directory %s", entry.name)
        except OSError:
            continue


def _parse_thread_id(stdout: str) -> str | None:
    """The thread id from the `thread.started` event, only if it is a plain UUID (it becomes a
    path component that is later deleted, so anything else is refused)."""
    for line in stdout.splitlines():
        if '"thread.started"' not in line:
            continue
        try:
            event = json.loads(line)
        except (json.JSONDecodeError, ValueError):
            continue
        thread_id = event.get("thread_id")
        if isinstance(thread_id, str) and _THREAD_ID_RE.match(thread_id):
            return thread_id
    return None


def _read_generated_png(image_dir: Path | None) -> bytes | None:
    """The newest PNG below `image_dir`, or None when there is none or it is not a real PNG."""
    if image_dir is None or not image_dir.is_dir():
        return None
    files = sorted(image_dir.glob("*.png"), key=lambda p: p.stat().st_mtime)
    if not files:
        return None
    try:
        data = files[-1].read_bytes()
    except OSError:
        return None
    return data if data.startswith(_PNG_MAGIC) else None


def _parse_codex_usage(stdout: str) -> UsageParts:
    """
    Reads token usage from the codex exec --json event stream. The final
    `turn.completed` event carries a `usage` block:
    {"input_tokens", "cached_input_tokens", "output_tokens", "reasoning_output_tokens"}.
    The last such event wins (a turn may be retried). Returns empty usage if absent.
    """
    usage = UsageParts()
    for line in stdout.splitlines():
        line = line.strip()
        if not line or '"usage"' not in line:
            continue
        try:
            event = json.loads(line)
        except (json.JSONDecodeError, ValueError):
            continue
        u = event.get("usage")
        if not isinstance(u, dict):
            continue
        cached = _cint(u, "cached_input_tokens")
        reasoning = _cint(u, "reasoning_output_tokens")
        usage = UsageParts(
            # codex input_tokens already includes the cached subset.
            input_tokens=_cint(u, "input_tokens"),
            # OpenAI completion_tokens includes reasoning tokens; codex reports them separately.
            output_tokens=_cint(u, "output_tokens") + reasoning,
            cached_tokens=cached,
            reasoning_tokens=reasoning,
        )
    return usage


def _parse_codex_text(stdout: str) -> str:
    """Recover the last agent_message text from the codex exec --json event stream."""
    text = ""
    for line in stdout.splitlines():
        line = line.strip()
        if not line or "agent_message" not in line:
            continue
        try:
            event = json.loads(line)
        except (json.JSONDecodeError, ValueError):
            continue
        item = event.get("item")
        if isinstance(item, dict) and item.get("type") == "agent_message" and item.get("text"):
            text = str(item["text"])
    return text.strip()


def _cint(d: dict, key: str) -> int:
    try:
        return int(d.get(key, 0) or 0)
    except (TypeError, ValueError):
        return 0
