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
| `POST` | `/v1/chat/completions` | **DEPRECATED** legacy endpoint with model-name routing. Logs a WARNING. |
| `GET`  | `/v1/claude/models` | Static model list of the claude CLI |
| `GET`  | `/v1/codex/models` | Static model list of the codex CLI |
| `GET`  | `/v1/models` | **DEPRECATED** combined legacy model list |
| `GET`  | `/health` | Health check with CLI status |

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
| **`response_format`** | ✅ `json_object` and `json_schema` (validated server-side against the schema, with one retry, then a `refusal`). |
| **Tool calling** | ✅ `tool_choice` `auto`/`required`/`none`/specific, parallel tool calls, multi-turn replay (emulated via prompt + JSON extraction). |
| **Multimodal** | ⚠️ best-effort: http(s) `image_url` parts are passed as references (claude can WebFetch them); inline `data:` images are dropped with a warning. |
| **Auth** | ✅ opt-in Bearer (`CLI_PROXY_API_KEYS`); unset = open (back-compat). |
| **`logprobs` / `n>1`** | ❌ rejected with `400` — the CLI cannot produce token probabilities, and `n>1` would multiply subscription cost. |
| **`temperature` / `top_p` / `seed` / penalties** | ⚠️ accepted and **ignored** (headless CLIs expose no sampling control). |
| **`system_fingerprint`** | omitted (no stable fingerprint to report). |

Models: `GET /v1/{claude,codex}/models` (list) and `GET /v1/{claude,codex}/models/{id}` (retrieve). Every response carries `x-request-id`, `openai-processing-ms` and informational `x-ratelimit-*` headers. Optional overload protection: set `CLI_PROXY_MAX_INFLIGHT` to return `429` once that many requests are concurrently in flight (default `0` = disabled, requests queue on the per-CLI semaphores).

## Tool use

If the request contains `tools` + `tool_choice`, the schema is embedded as a prompt addendum:

> "Respond ONLY with a valid JSON object conforming to schema ..."

The CLI output is then scanned for a JSON block (incl. Markdown-fence stripping) and returned as a `tool_calls` response.

## Web search

Both backends run with the model-driven web search tool enabled, so an actor can autonomously decide to fetch current information from the web during generation:

- **claude** — invoked with `--allowedTools "WebSearch,WebFetch"`. Only the web tools are allowlisted; Bash/Edit/Write are **not** enabled (no full permission bypass).
- **codex** — invoked with the global `--search` flag, placed **before** the `exec` subcommand (codex rejects it as an `exec` argument). It enables the native Responses `web_search` tool with no per-call approval.

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
