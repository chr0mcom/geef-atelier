# Atelier CLI Proxy

*[Deutsch](README_de.md) · **English***

A lightweight HTTP wrapper that translates the `claude` (Claude Code CLI) and `codex` (OpenAI Codex CLI) into an OpenAI-compatible REST API. It lets Geef.Atelier use subscription-based CLI capacity without billing per token.

## Architecture

```
Geef.Atelier Web ──► POST /v1/claude/chat/completions ──► CLI proxy ──► claude CLI
                 ──► POST /v1/codex/chat/completions  ──► CLI proxy ──► codex CLI
```

The proxy is reachable on the internal `geef-atelier-network` AND on the shared portfolio `proxy` network, so **any** app can use it as an OpenAI-compatible gateway at `http://cli-proxy:8090`. It has no Traefik routing and is not exposed publicly.

## Use from other apps (portfolio-wide gateway)

Any container on the shared `proxy` Docker network can call this proxy as a drop-in OpenAI endpoint — subscription-covered, no per-token billing. Point an OpenAI SDK at `http://cli-proxy:8090/v1/claude` or `http://cli-proxy:8090/v1/codex` (any `api_key` value unless `CLI_PROXY_API_KEYS` is set):

```python
from openai import OpenAI
client = OpenAI(base_url="http://cli-proxy:8090/v1/codex", api_key="unused")
print(client.chat.completions.create(
    model="gpt-5.5", messages=[{"role": "user", "content": "ping"}]).choices[0].message.content)
```

If an app is not yet on the `proxy` network, add it:

```yaml
services:
  my-app:
    networks: [proxy]
networks:
  proxy:
    external: true
```

Full cross-app guide: [`/srv/docker/docs/cli-proxy-openai-gateway.md`](../../../docs/cli-proxy-openai-gateway.md).

## Endpoints

| Method | Path | Description |
|---------|------|--------------|
| `POST` | `/v1/claude/chat/completions` | Directly to the claude CLI — no model-name routing |
| `POST` | `/v1/codex/chat/completions`  | Directly to the codex CLI — no model-name routing |
| `POST` | `/v1/codex/images/generations` | Image generation (OpenAI Images API, codex only, `b64_json`/PNG, one image per request) |
| `POST` | `/v1/chat/completions` | **DEPRECATED** legacy endpoint with model-name routing. Logs a WARNING. |
| `GET`  | `/v1/claude/models` | Model list of the claude CLI (always-latest aliases plus the concrete ids the CLI resolves them to; with several Claude accounts the union of all of them) |
| `GET`  | `/v1/codex/models` | Subscription model list of the codex CLI, read from `$CODEX_HOME/models_cache.json` (only `visibility: list`, ordered by `priority`, never `-pro`/`:batch`), preceded by the always-latest aliases `gpt-<line>-latest` (`x_aliases` maps each to its concrete id, `x_aliases_complete: true`); curated fallback `gpt-6.2-sol`, `gpt-6.2-astra`, `gpt-6.1-terra` while the CLI has not written the cache (`x_source: static`) |
| `GET`  | `/v1/models` | **DEPRECATED** combined legacy model list |
| `GET`  | `/health` | Health check with CLI status and failover state (`degraded`, per-backend breaker, `last_error_detail`, `empty_results`, `empty_result_alert`, `caller_timeouts`) |

### Explicit endpoints (recommended)

The newer endpoints `/v1/claude/chat/completions` and `/v1/codex/chat/completions` route deterministically by path — independent of the `model` field in the request. This is the recommended variant for all new profiles.

### Legacy endpoint (deprecated)

`/v1/chat/completions` is retained for backward compatibility and still uses model-name routing:

- `claude-*`, `anthropic/claude-*` → claude CLI
- `gpt-*`, `o1-*`, `o3-*`, `o4-*`, `openai/*` → codex CLI
- Unknown models → claude CLI (fallback)

Every call to this endpoint logs a DEPRECATED warning. Planned removal after 2 Atelier versions.

## OpenAI compatibility

The proxy targets practical drop-in compatibility with the OpenAI Chat Completions API. What is supported, and what the CLI backends physically cannot do:

| Feature | Status |
|---------|--------|
| **Streaming** (`stream: true`) | ✅ SSE `chat.completion.chunk` stream, terminating `data: [DONE]`. `stream_options.include_usage` adds a final usage chunk. claude streams token deltas; codex emits the agent message as a (buffered) delta. Requests with `tools`/`response_format` buffer the full text, then emit. |
| **Real token usage** | ✅ `usage` is parsed from the CLI output (claude `--output-format json`, codex `exec --json`), incl. `prompt_tokens_details.cached_tokens` and `completion_tokens_details.reasoning_tokens`. |
| **Error envelope** | ✅ `{"error": {"message","type","param","code"}}` with faithful status codes (422→400, 401, 404, 429, 5xx). |
| **`response_format`** | ✅ `json_object` and `json_schema` (validated server-side against the schema, with one retry, then a `refusal`). Every JSON object in the answer is considered (fences, leading prose, stray braces, an echoed schema); the first one that satisfies the schema wins. claude retries in **lean mode** (see below). |
| **Tool calling** | ✅ `tool_choice` `auto`/`required`/`none`/specific, parallel tool calls, multi-turn replay (emulated via prompt + JSON extraction). |
| **Multimodal** | ⚠️ best-effort: http(s) `image_url` parts are passed as references (claude can WebFetch them); inline `data:` images are dropped with a warning. |
| **Auth** | ✅ opt-in Bearer (`CLI_PROXY_API_KEYS`); unset = open (back-compat). |
| **`logprobs` / `n>1`** | ❌ rejected with `400` — the CLI cannot produce token probabilities, and `n>1` would multiply subscription cost. |
| **`temperature` / `top_p` / `seed` / penalties** | ⚠️ accepted and **ignored** (headless CLIs expose no sampling control). |
| **`reasoning_effort`** | ✅ applied **per request, only when sent**: `low`/`medium`/`high` → claude `--effort <level>`, codex `-c model_reasoning_effort="<level>"` (also on the failover target). Any other value (incl. `minimal`) → `400`. Applied level is echoed in `x-cli-proxy-reasoning-effort`. Without the field the CLI argv is unchanged, i.e. the CLI default applies. |
| **`system_fingerprint`** | omitted (no stable fingerprint to report). |

Models: `GET /v1/{claude,codex}/models` (list) and `GET /v1/{claude,codex}/models/{id}` (retrieve). Every response carries `x-request-id`, `openai-processing-ms` and informational `x-ratelimit-*` headers. Optional overload protection: set `CLI_PROXY_MAX_INFLIGHT` to return `429` once that many requests are concurrently in flight (default `0` = disabled, requests queue on the per-CLI semaphores).

### Codex always-latest aliases

Like `claude-opus-latest`, codex has `gpt-<line>-latest` aliases so that consumers never get stuck on an outdated model. The line is the last name segment of an id shaped `gpt-<version>-<line>` (`sol`, `astra`, `terra`, `luna`, ...); it is derived from the listed ids, so a new line gets its alias automatically. `gpt-sol-latest` resolves to the newest listed `sol` model.

- **Newest** means the highest numeric version (`gpt-6.2` > `gpt-6.1` > `gpt-6` > `gpt-5.6`, compared as numbers, not as text); on equal versions the list order decides. `-pro`, `:batch` and hidden entries never take part.
- **Source:** the live list from `models_cache.json`; without it (`x_source: static` or `degraded`) the aliases resolve against the curated fallback list.
- **Endpoints:** `GET /v1/codex/models` and `GET /v1/cli/codex-cli/models` list the aliases first in `data`, then the concrete ids, and return the mapping in `x_aliases` (for example `{"gpt-sol-latest": "gpt-6.2-sol", "gpt-astra-latest": "gpt-6.2-astra"}`) plus `"x_aliases_complete": true`. The flag says the mapping is derived completely and deterministically from the model list and is exactly the one the proxy applies to a call, so a consumer may replace its own alias state with it instead of merging; it holds for `x_source` `live`, `static` and `degraded`. The claude endpoints omit the field (absent means `false`). The failover to codex uses `gpt-sol-latest` internally (`DEFAULT_CODEX_MODEL`). The HTTP API still requires `model`; a request without it is rejected with 400.
- **Requests:** a request with `model: "gpt-sol-latest"` (a prefix such as `openai/gpt-sol-latest` is ignored) runs `codex -m <concrete id>`, and the response `model` carries the concrete id. An alias no listed model satisfies is not replaced; it behaves like any other unknown model name.
- **Reproducibility:** the alias moves with every release. Run snapshots and consumers that need reproducible results store the concrete id from `x_aliases`, not the alias.

## Failover, error details and empty results

The explicit endpoints hand a request over to the partner CLI (claude ⇄ codex) when the requested one fails with an auth, limit or transport fault; a circuit breaker keeps a failing backend out for a while and then lets one probe through. Successful handovers carry `x-cli-proxy-failover: <from>-><to>`, and every success carries `x-cli-proxy-provider`.

- **Switching the failover off per request:** send the request header `x-cli-proxy-failover: off` (also `0` or `false`, any case) when your result depends on the model and an answer from another backend is worse than an error. The request then stays on the requested backend (no second account as a failover stage, no partner CLI; the model routing between Claude accounts described below still applies). A failure comes back as the usual error envelope with the CLI's cause, and it counts for the breaker as usual. If the breaker of the requested backend is open, the request is answered at once with `503`, `code: backend_unavailable` and a `Retry-After` header instead of being redirected. Without the header, or with any other value, nothing changes.
- **Same model only, per request:** send `x-cli-proxy-failover: same-model` (any case) when your result depends on the model but another account of the same CLI may serve it, because it runs the same model under the same name. A Claude request may then move between the Claude accounts (`claude` ⇄ `claude-alt`) with the requested model unchanged, for the same reasons as the ordinary failover (auth and limit faults; a transport fault skips the second account, as always), but never to codex. A codex request is treated exactly like `off` (it never moves to Claude). Breaker, limit detection, "not in plan" routing and response headers behave as in the ordinary failover between the accounts: a handover carries `x-cli-proxy-provider: claude-alt` and `x-cli-proxy-failover: claude->claude-alt`. Accounts whose breaker is open are skipped; if every account of the CLI is blocked, the answer is `503`, `code: backend_unavailable`, with `Retry-After` up to the first account that takes a probe again. With `FAILOVER_ENABLED=0` the value behaves like `off`. Only this exact spelling counts: any other value (also `same_model`) is treated like no header, that is full failover. A gateway older than this value treats it the same way, so update the gateway before its callers start sending it.
- **Several Claude accounts, different models:** with a second Claude login configured, accounts can carry different models. The alias probe runs for every account, and the claude model lists (`/v1/claude/models`, `/v1/cli/<claude provider>/models`, `/v1/models`) return the union: `x_aliases` takes each family from the primary account and falls back to the second one. A request for a model only the second account carries goes straight to it (`x-cli-proxy-provider: claude-alt`, no `x-cli-proxy-failover` header). This is routing, not failover: it also holds with `x-cli-proxy-failover: off` and moves no circuit breaker. When an account answers that its plan does not include the model, the gateway remembers that for a while (`CLAUDE_NOT_IN_PLAN_TTL_SECONDS`, default 3600), tries the same model on the other Claude account and, if neither has it, hands over to codex when failover is allowed or answers `404` with `code: model_not_found`. Only the CLI's structured error result (`is_error: true` with a matching message, the requested model name cut out) counts, never free text, and an ordinary session or weekly limit is never treated as "not in plan". At most `CLAUDE_MODEL_PROBE_CONCURRENCY` (default 2) probes run at once, and an ordinary model-list call never waits for probes of the second account (it is refreshed in the background). `/health` shows per account which families resolve and which are not in its plan under `claude_accounts`.
- **Error details:** when a CLI exits non-zero, the error message names the cause instead of the first 200 characters of stdout: for claude the fields `result` (at most 300 characters), `subtype`, `is_error` and `api_error_status` of its JSON result, for codex stderr plus the message of the last error event; without such fields the first 200 characters of the output, as before. Credential-like fragments (`sk-…`, `tvly-…`, `AIza…`, `gsk_…`, bearer tokens, `key=…` and similar) are replaced by `[redacted]` (`src/redaction.py`, the same rules as Geef.Atelier). Prompt and context texts never appear in these messages. On the streaming paths stderr is read alongside stdout, so a CLI that writes a lot to it never blocks; only its last 64 KB are kept (if anything was dropped, from the first complete line on), and the message of a failed stream shows the first 200 characters of that part.
- **Empty document-mode results:** a document-mode run (claude or codex, also after a handover) that exits normally but leaves `draft.md` empty and unchanged is answered with `502`, `type: api_error`, `code: empty_result` instead of `200` with empty text. It is an error of that one request only and is not handed over again. It does **not** count for the circuit breaker, neither for the backend that produced it nor, after a handover, for the origin: whether the CLI writes the document depends on the request's content (a refusal, a chat answer instead of a file edit), so it is no evidence of an unhealthy backend, and one request must not block a backend for every other caller. It neither raises nor resets `consecutive_failures`; it is only counted and reported in `/health`. Text written into the draft, an unchanged non-empty draft and plain chat or streaming answers behave as before.
- **`/health`:** each entry under `failover.backends` additionally carries `last_error_detail` (the redacted error message with the cause, up to 2000 characters; `last_error` keeps its 500-character cut) and `empty_results` (count of empty document-mode results since start). For the empty results there are also `last_empty_result_at` (epoch seconds), `empty_results_recent` and `document_requests_recent` (empty results and finished document-mode attempts of this backend in the last `failover.empty_result_window_seconds`, default 600) and `empty_result_rate` (their ratio, `null` without document-mode attempts).
- **`empty_result_alert`:** `true` on a backend when, inside the window, it produced at least 5 empty results **and** more than 50 % of its document-mode attempts came back empty; `failover.empty_result_alert` is `true` when any backend is. Display only for watchers and operators: it changes no routing and is independent of `degraded`. Thresholds: `EMPTY_RESULT_ALERT_WINDOW_SECONDS` (600), `EMPTY_RESULT_ALERT_MIN` (5), `EMPTY_RESULT_ALERT_RATIO` (0.5).
- **Known limitation, codex document mode in containers:** codex runs document mode with `--sandbox workspace-write`. Its Linux sandbox (bubblewrap) needs user namespaces, which Docker's default seccomp profile does not grant, so codex can neither read `context.md` nor write `draft.md` there. Such runs now end as `empty_result` instead of an empty success.

## Process lifetime, caller time limit and disconnects (since 2026-11-25)

Every CLI run of a request starts in its own process group, and whenever a run ends early the gateway kills the **whole group** (the CLI plus everything it started: node workers, tool and MCP helpers), not just the CLI. This applies to all paths that start a CLI for a request: claude and codex, print and lean mode, document mode, decision-file tool use, streaming and non-streaming, codex images, and the gemini/generic adapters of `/v1/cli/{provider}`. The "whole group" also holds when the CLI itself has already exited while one of its helpers still holds stdout or stderr, so the gateway is still waiting for output. The group is then signalled only while that output is still open and no running process carries the group id (a reused number could not be ours). Not reached: processes that leave the group (their own session), and helpers that hold no output pipe and outlive a run that ended normally. The model-list probes (`GET …/models`, warm-up) keep their own short timeouts.

- **Caller disconnects:** when the caller closes the connection before the response is complete (its own time limit, a cancelled job, a crashed process), the request is cancelled and its CLI process group is killed. Nothing else starts for that request: no handover to the partner CLI, no claude-alt stage, no JSON retry. A disconnect never counts against the circuit breaker and does not appear in `last_error`; CLI slots (semaphores), workspaces and the in-flight counter are released. This also holds for a streaming caller that stops reading and then disconnects: the response closes the whole chain of stream generators when it ends, so CLI and slot are freed at once, with the standard asyncio loop as well as with uvloop (the gateway closes the pipes of a CLI it kills, so a pipe paused by back pressure cannot hold the slot). One log line per aborted request: `caller disconnected, request aborted | request_id=… | backend=… | after=…s` (no content). Before, such a run kept going until `CLI_TIMEOUT_SECONDS` (1800 s by default). Detection follows the ASGI semantics: only after the complete request body has been read, and only an `http.disconnect` before the final response body counts, so a caller that stays connected notices nothing, however long the call runs, also with HTTP keep-alive. A client that half-closes its socket while still waiting (rare) is treated as gone; it could not have received the response before either.
- **Caller time limit (optional):** request header `x-cli-proxy-timeout-seconds: <seconds>` (a positive number; anything else is rejected with `400`, `code: invalid_header`). It applies to the chat endpoints, `/v1/cli/{provider}` and the image endpoint (there the own cap of an image run, `IMAGE_TIMEOUT_SECONDS`, applies as well; an image request that was accepted counts against the daily limit even if its time runs out). The limit counts from the arrival of the request (queueing for a CLI slot and in-proxy retries use it up; a request still waiting for a slot is answered as soon as the limit runs out, without starting a CLI) and is capped by `CLI_TIMEOUT_SECONDS`; a value at or above the cap changes nothing. When it runs out, the CLI process group is killed and the request is answered with `504`, `type: api_error`, `code: cli_timeout`. This is the caller's choice, not a backend fault: it is **not** handed over to the partner CLI and does **not** move the breaker. `/health` reports it in fields of its own per backend (`caller_timeouts`, `last_caller_timeout_at`, `last_caller_timeout_error`); `last_reason`, `last_error`, `last_error_detail`, `last_failure_at` (and with them the `FAILOVER` log line) keep describing real faults. On a stream the error arrives in-band. Send it a few seconds below your own client timeout, so that the gateway stops the CLI before your client gives up. Geef.Atelier sends it for the fact check (85 s for a 90 s limit, 55 s for the 60 s fallback).
- **Without the header, with a connected caller:** answers, errors and timeouts are the same as before; the gateway's own cap (`CLI_TIMEOUT_SECONDS`) keeps its error text and its effect on failover and breaker, but now also ends the CLI's helper processes.
- **Container note:** uvicorn as PID 1 would not reap orphaned grandchildren, so the Compose service sets `init: true`: an init process runs as PID 1 and reaps killed helpers, none remain as zombies. Run the image some other way with an init process too (`docker run --init`).

## Lean mode (claude, since 2026-11-07)

A default `claude -p` call is a full Claude Code agent session: agent system prompt, built-in tool definitions, the synced skill list and the auto-memory of the working directory precede the caller's text (~23k prompt tokens), and the caller's OpenAI system message only arrives as `[SYSTEM]` text inside the user turn. For role-play or "answer from your own perspective" prompts the agent persona then often declines in prose, so structured outputs came back as `refusal: "no JSON object found"` (2026-11-07, LLM Shootout debriefing: 4 of 6 replays).

Lean mode runs `claude -p --output-format json --system-prompt-file <file> --tools "" --disable-slash-commands --strict-mcp-config --no-session-persistence` in an empty per-call directory: the caller's system/developer messages become the real system prompt (neutral default if there is none), no tools (WebSearch/WebFetch only when the request references http(s) images), no skills, no memory, no session transcript. Prompt size drops to ~2k tokens for a ~1.5k-token request.

| Variable | Default | Effect |
|----------|---------|--------|
| `CLAUDE_STRUCTURED_LEAN_RETRY` | `1` | The single retry of an invalid claude structured output runs in lean mode (instead of repeating the agent-mode call with a "CRITICAL" reminder). `0` restores the old retry. |
| `CLAUDE_LEAN_CHAT` | `1` | Non-streaming plain chat (no `tools`, no document mode) and all `response_format` requests to claude use lean mode from the first attempt, so the gateway behaves like a plain OpenAI API. `0` restores the agent-session behaviour of before 2026-11-07. Token-streamed chat, tool calling (incl. `tool_choice` specific), decision-file and document mode are never affected; codex is never affected. |

Lean mode is the default since 2026-11-07. Trade-off: no web search unless requested (next section), and the model no longer sees the Claude Code agent instructions, which plain chat and structured callers do not need.

Independent of the mode, **no CLI process leaves a trace or reads one**: every claude call runs with `--no-session-persistence` and `CLAUDE_CODE_DISABLE_AUTO_MEMORY=1` (the operator's memory notes never reach a prompt), every codex call with `--ephemeral`. See `src/policy.py`.

## Tool use

If the request contains `tools` + `tool_choice`, the schema is embedded as a prompt addendum:

> "Respond ONLY with a valid JSON object conforming to schema ..."

The CLI output is then scanned for a JSON block (incl. Markdown-fence stripping) and returned as a `tool_calls` response.

## Web search (opt-in since 2026-11-07)

Like any OpenAI-compatible API, the gateway does not browse unless asked. A caller opts in per request with the standard `web_search_options` parameter (any object, usually `{}`); the operator can switch the default for the whole deployment with `CLI_WEB_SEARCH=1` on the `cli-proxy` service (the behaviour before 2026-11-07). Without it, claude runs with `--tools ""` and codex without `--search`.

With search enabled, the model decides itself whether to fetch current information:

- **claude** — invoked with `--allowedTools "WebSearch,WebFetch"`. Only the web tools are allowlisted; Bash/Edit/Write are **not** enabled (no full permission bypass).
- **codex** — invoked with the global `--search` flag, placed **before** the `exec` subcommand (codex rejects it as an `exec` argument). It enables the native Responses `web_search` tool with no per-call approval.

A request that references http(s) images keeps `WebFetch` (claude must download them) but never gets search by that route.

**Cost:** web search runs on the subscription (Claude / Codex), not metered per search — no Tavily-style per-request billing on the CLI path.

**Scope / limitation:** the sources the model searches are consumed internally and are **not** captured into `GroundingConsultation` / RunDetail. There is no citation or observability trail for CLI web search — that remains the job of the Tavily grounding provider (explicit, deterministic pre-briefing enrichment). The two are complementary, not either/or. OpenRouter-routed actors (single-shot) cannot perform agentic web search.

## Setup (production)

### 1. Build the images

```bash
cd /srv/docker/websites/geef_atelier
docker compose build --no-cache cli-proxy
```

### 2. Start the container

```bash
docker compose up -d cli-proxy
```

### 3. Authenticate the Claude CLI

```bash
docker exec -it geef-atelier-cli-proxy bash
claude auth login
# Follow the browser instructions; the token lands in /auth/claude/
exit
```

The auth volume `geef-atelier-cli-auth` persists the tokens across container restarts.

### 4. Authenticate the Codex CLI

```bash
docker exec -it geef-atelier-cli-proxy bash
codex auth login
exit
```

### 5. Check health

```bash
docker exec geef-atelier-cli-proxy curl -s localhost:8090/health
# {"status":"ok","cli_status":{"claude":"ready","codex":"ready"}}
```

## Configuration (Geef.Atelier)

In `appsettings.json` / environment variables:

```json
{
  "Llm": {
    "Providers": {
      "claude-cli": {
        "Endpoint": "http://cli-proxy:8090/v1/claude",
        "ApiKey": ""
      },
      "codex-cli": {
        "Endpoint": "http://cli-proxy:8090/v1/codex",
        "ApiKey": ""
      }
    }
  }
}
```

> **Note:** `appsettings.json` only configures the provider endpoints. Which actor
> uses which provider and which model is determined, since the crew system, by the
> reviewer/executor/advisor **profiles** (see
> [`docs/08-crew-system.md`](../docs/08-crew-system.md)) — no longer by an
> `Llm.Actors` block.

## Concurrency

Default: 2 simultaneous calls per CLI (via `asyncio.Semaphore`). Adjustable via environment variables:

```
CLAUDE_MAX_CONCURRENT=2
CODEX_MAX_CONCURRENT=2
```

## Security note

CLI auth tokens (in `/auth/`) are never in source control, logs or reports.

## Tests

```bash
cd cli-proxy
pip3 install ".[dev]" --break-system-packages
python3 -m pytest tests/ -v
```
