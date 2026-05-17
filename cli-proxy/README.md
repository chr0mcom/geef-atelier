# Atelier CLI Proxy

*[Deutsch](README_de.md) · **English***

A lightweight HTTP wrapper that translates the `claude` (Claude Code CLI) and `codex` (OpenAI Codex CLI) into an OpenAI-compatible REST API. It lets Geef.Atelier use subscription-based CLI capacity without billing per token.

## Architecture

```
Geef.Atelier Web ──► POST /v1/claude/chat/completions ──► CLI proxy ──► claude CLI
                 ──► POST /v1/codex/chat/completions  ──► CLI proxy ──► codex CLI
```

The proxy is reachable internally on the Docker network (`http://cli-proxy:8090`) and has no Traefik routing to the outside.

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

## Tool use

If the request contains `tools` + `tool_choice`, the schema is embedded as a prompt addendum:

> "Respond ONLY with a valid JSON object conforming to schema ..."

The CLI output is then scanned for a JSON block (incl. Markdown-fence stripping) and returned as a `tool_calls` response.

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
