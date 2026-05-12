# Atelier CLI Proxy

Ein leichtgewichtiger HTTP-Wrapper, der die `claude` (Claude Code CLI) und `codex` (OpenAI Codex CLI) in eine OpenAI-kompatible REST API übersetzt. Ermöglicht Geef.Atelier, Subscription-basierte CLI-Kapazitäten zu nutzen, ohne per Token abzurechnen.

## Architektur

```
Geef.Atelier Web ──► POST /v1/chat/completions ──► CLI-Proxy
                                                      ├─ claude-* → claude CLI
                                                      └─ gpt-*/o*  → codex CLI
```

Der Proxy ist intern im Docker-Netzwerk erreichbar (`http://cli-proxy:8090/v1`) und hat kein Traefik-Routing nach außen.

## Endpoints

| Methode | Pfad | Beschreibung |
|---------|------|--------------|
| `POST` | `/v1/chat/completions` | OpenAI-konformes Chat Completions |
| `GET` | `/v1/models` | Liste unterstützter Modelle |
| `GET` | `/health` | Health-Check mit CLI-Status |

## Model-Routing

- `claude-*`, `anthropic/claude-*` → `claude` CLI
- `gpt-*`, `o1-*`, `o3-*`, `o4-*`, `openai/*` → `codex` CLI
- Unbekannte Modelle → `claude` CLI (Fallback)

## Tool-Use

Wenn der Request `tools` + `tool_choice` enthält, wird das Schema als Prompt-Addendum eingebettet:

> "Respond ONLY with a valid JSON object conforming to schema ..."

Der CLI-Output wird dann auf einen JSON-Block gescannt (inkl. Markdown-Fence-Stripping) und als `tool_calls`-Response zurückgegeben.

## Setup (Production)

### 1. Images bauen

```bash
cd /srv/docker/websites/geef_atelier
docker compose build --no-cache cli-proxy
```

### 2. Container starten

```bash
docker compose up -d cli-proxy
```

### 3. Claude CLI authentifizieren

```bash
docker exec -it geef-atelier-cli-proxy bash
claude auth login
# Folge den Browser-Anweisungen; Token landet in /auth/claude/
exit
```

Das Auth-Volume `geef-atelier-cli-auth` persistiert die Tokens über Container-Restarts.

### 4. Codex CLI authentifizieren

```bash
docker exec -it geef-atelier-cli-proxy bash
codex auth login
exit
```

### 5. Health prüfen

```bash
docker exec geef-atelier-cli-proxy curl -s localhost:8090/health
# {"status":"ok","cli_status":{"claude":"ready","codex":"ready"}}
```

## Konfiguration (Geef.Atelier)

In `appsettings.json` / Umgebungsvariablen:

```json
{
  "Llm": {
    "Providers": {
      "cli": {
        "Endpoint": "http://cli-proxy:8090/v1",
        "ApiKey": ""
      }
    },
    "Actors": {
      "Executor": { "Provider": "cli", "Model": "claude-opus-4-5" }
    }
  }
}
```

## Concurrency

Standard: 2 gleichzeitige Aufrufe pro CLI (via `asyncio.Semaphore`). Anpassbar über Umgebungsvariablen:

```
CLAUDE_MAX_CONCURRENT=2
CODEX_MAX_CONCURRENT=2
```

## Sicherheitshinweis

CLI-Auth-Tokens (in `/auth/`) sind niemals in Source-Control, Logs oder Berichten.

## Tests

```bash
cd cli-proxy
python -m venv .venv && .venv/bin/pip install ".[dev]"
.venv/bin/python -m pytest tests/ -v
```
