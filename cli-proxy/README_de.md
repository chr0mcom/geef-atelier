# Atelier CLI Proxy

*[English](README.md) · **Deutsch***

Ein leichtgewichtiger HTTP-Wrapper, der die `claude` (Claude Code CLI) und `codex` (OpenAI Codex CLI) in eine OpenAI-kompatible REST API übersetzt. Ermöglicht Geef.Atelier, Subscription-basierte CLI-Kapazitäten zu nutzen, ohne per Token abzurechnen.

## Architektur

```
Geef.Atelier Web ──► POST /v1/claude/chat/completions ──► CLI-Proxy ──► claude CLI
                 ──► POST /v1/codex/chat/completions  ──► CLI-Proxy ──► codex CLI
```

Der Proxy hängt am internen `geef-atelier-network` UND am gemeinsamen Portfolio-Netz `proxy` — damit kann **jede** App ihn als OpenAI-kompatibles Gateway unter `http://cli-proxy:8090` nutzen. Kein Traefik-Routing, nicht öffentlich exponiert.

## Nutzung aus anderen Apps (portfolio-weites Gateway)

Jeder Container im gemeinsamen `proxy`-Docker-Netz kann diesen Proxy als Drop-in-OpenAI-Endpoint ansprechen — Abo-gedeckt, keine Pro-Token-Abrechnung. Ein OpenAI-SDK einfach auf `http://cli-proxy:8090/v1/claude` oder `http://cli-proxy:8090/v1/codex` zeigen lassen (beliebiger `api_key`, sofern `CLI_PROXY_API_KEYS` nicht gesetzt ist):

```python
from openai import OpenAI
client = OpenAI(base_url="http://cli-proxy:8090/v1/codex", api_key="unused")
print(client.chat.completions.create(
    model="gpt-5.5", messages=[{"role": "user", "content": "ping"}]).choices[0].message.content)
```

Hängt eine App noch nicht am `proxy`-Netz, ergänzen:

```yaml
services:
  my-app:
    networks: [proxy]
networks:
  proxy:
    external: true
```

Vollständige Cross-App-Anleitung: [`/srv/docker/docs/cli-proxy-openai-gateway.md`](../../../docs/cli-proxy-openai-gateway.md).

## Endpoints

| Methode | Pfad | Beschreibung |
|---------|------|--------------|
| `POST` | `/v1/claude/chat/completions` | Direkt zur claude CLI — kein Model-Name-Routing |
| `POST` | `/v1/codex/chat/completions`  | Direkt zur codex CLI — kein Model-Name-Routing |
| `POST` | `/v1/chat/completions` | **DEPRECATED** Legacy-Endpoint mit Model-Name-Routing. Loggt WARNING. |
| `GET`  | `/v1/claude/models` | Statische Modell-Liste der claude CLI |
| `GET`  | `/v1/codex/models` | Statische Modell-Liste der codex CLI |
| `GET`  | `/v1/models` | **DEPRECATED** kombinierte Legacy-Modell-Liste |
| `GET`  | `/health` | Health-Check mit CLI-Status |

### Explizite Endpoints (empfohlen)

Die neuen Endpunkte `/v1/claude/chat/completions` und `/v1/codex/chat/completions` routen deterministisch anhand des Pfades — unabhängig vom `model`-Feld im Request. Dies ist die empfohlene Variante für alle neuen Profile.

### Legacy-Endpoint (veraltet)

`/v1/chat/completions` bleibt für Backward-Kompatibilität erhalten und nutzt weiterhin Model-Name-Routing:

- `claude-*`, `anthropic/claude-*` → claude CLI
- `gpt-*`, `o1-*`, `o3-*`, `o4-*`, `openai/*` → codex CLI
- Unbekannte Modelle → claude CLI (Fallback)

Jeder Aufruf dieses Endpunkts loggt ein DEPRECATED-Warning. Geplante Entfernung nach 2 Atelier-Versionen.

## OpenAI-Kompatibilität

Der Proxy zielt auf praktische Drop-in-Kompatibilität mit der OpenAI-Chat-Completions-API. Was unterstützt wird — und was die CLI-Backends prinzipiell nicht können:

| Feature | Status |
|---------|--------|
| **Streaming** (`stream: true`) | ✅ SSE-`chat.completion.chunk`-Stream, abschließendes `data: [DONE]`. `stream_options.include_usage` ergänzt einen finalen Usage-Chunk. claude streamt Token-Deltas; codex liefert die Agent-Nachricht als (gepufferten) Delta. Anfragen mit `tools`/`response_format` puffern den vollen Text und emittieren ihn dann. |
| **Echte Token-Usage** | ✅ `usage` wird aus dem CLI-Output geparst (claude `--output-format json`, codex `exec --json`), inkl. `prompt_tokens_details.cached_tokens` und `completion_tokens_details.reasoning_tokens`. |
| **Error-Envelope** | ✅ `{"error": {"message","type","param","code"}}` mit korrekten Status-Codes (422→400, 401, 404, 429, 5xx). |
| **`response_format`** | ✅ `json_object` und `json_schema` (serverseitig gegen das Schema validiert, mit einem Retry, sonst `refusal`). |
| **Tool-Calling** | ✅ `tool_choice` `auto`/`required`/`none`/spezifisch, parallele Tool-Calls, Multi-Turn-Replay (emuliert via Prompt + JSON-Extraktion). |
| **Multimodal** | ⚠️ Best-Effort: http(s)-`image_url`-Parts werden als Referenzen durchgereicht (claude kann sie per WebFetch laden); inline `data:`-Bilder werden mit Warnung verworfen. |
| **Auth** | ✅ Opt-in Bearer (`CLI_PROXY_API_KEYS`); ungesetzt = offen (rückwärtskompatibel). |
| **`logprobs` / `n>1`** | ❌ mit `400` abgelehnt — die CLI liefert keine Token-Wahrscheinlichkeiten, und `n>1` würde den Abo-Verbrauch vervielfachen. |
| **`temperature` / `top_p` / `seed` / Penalties** | ⚠️ angenommen und **ignoriert** (Headless-CLIs bieten keine Sampling-Steuerung). |
| **`system_fingerprint`** | weggelassen (kein stabiler Fingerprint verfügbar). |

Modelle: `GET /v1/{claude,codex}/models` (Liste) und `GET /v1/{claude,codex}/models/{id}` (Einzelabruf). Jede Antwort trägt `x-request-id`, `openai-processing-ms` und informative `x-ratelimit-*`-Header. Optionaler Überlastschutz: `CLI_PROXY_MAX_INFLIGHT` setzen, um bei so vielen gleichzeitigen Anfragen `429` zu liefern (Default `0` = aus, Anfragen reihen sich an den Per-CLI-Semaphoren ein).

## Tool-Use

Wenn der Request `tools` + `tool_choice` enthält, wird das Schema als Prompt-Addendum eingebettet:

> "Respond ONLY with a valid JSON object conforming to schema ..."

Der CLI-Output wird dann auf einen JSON-Block gescannt (inkl. Markdown-Fence-Stripping) und als `tool_calls`-Response zurückgegeben.

## Websuche

Beide Backends laufen mit aktiviertem modell-gesteuertem Web-Search-Tool — ein Akteur kann während der Generierung selbst entscheiden, aktuelle Informationen aus dem Web zu holen:

- **claude** — Aufruf mit `--allowedTools "WebSearch,WebFetch"`. Nur die Web-Tools sind allowlisted; Bash/Edit/Write sind **nicht** aktiviert (kein voller Permission-Bypass).
- **codex** — Aufruf mit dem globalen `--search`-Flag, platziert **vor** dem `exec`-Subcommand (codex lehnt es als `exec`-Argument ab). Es aktiviert das native Responses-`web_search`-Tool ohne Per-Call-Approval.

**Kosten:** Die Websuche läuft über das Abo (Claude / Codex), nicht pro Suche abgerechnet — kein Tavily-artiges Per-Request-Billing auf dem CLI-Pfad.

**Scope / Einschränkung:** Die vom Modell gesuchten Quellen werden intern verarbeitet und **nicht** in `GroundingConsultation` / RunDetail erfasst. Es gibt keine Citation- oder Nachvollziehbarkeits-Spur für die CLI-Websuche — das bleibt Aufgabe des Tavily-Grounding-Providers (explizite, deterministische Vor-Briefing-Anreicherung). Beide ergänzen sich, sind kein Entweder-oder. OpenRouter-geroutete Akteure (single-shot) können keine agentische Websuche durchführen.

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

> **Hinweis:** `appsettings.json` konfiguriert nur die Provider-Endpunkte. Welcher
> Akteur welchen Provider und welches Modell nutzt, bestimmen seit dem Crew-System
> die Reviewer-/Executor-/Advisor-**Profile** (siehe
> [`docs/08-crew-system.md`](../docs/08-crew-system_de.md)) — nicht mehr ein
> `Llm.Actors`-Block.

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
pip3 install ".[dev]" --break-system-packages
python3 -m pytest tests/ -v
```
