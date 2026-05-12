# Claude-Code-Prompt: Post-Skeleton Schritt 4 — CLI Provider Adapter (Side-Container)

*Dieser Prompt fügt einen zweiten LLM-Provider-Pfad hinzu: über die auf dem Server installierten CLIs `claude` und `codex`, die jeweilige Subscriptions nutzen statt Pay-as-you-go-Tokens. Architektonisch: ein neuer Side-Container im Compose-Stack, der OpenAI-API-kompatibles HTTP exposed.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Das Walking-Skeleton steht, PS-1 bis PS-3 sind durch (Backup, Reviewer-Kalibrierung, Design-Translation). Aktuell läuft die gesamte LLM-Schicht über **einen Provider** (OpenRouter via `OpenAiCompatibleClient` aus M1). Das ist pragmatisch, aber teuer für Heavy-Hitter-Modelle wie Claude Opus, weil pro Token bezahlt wird.

Auf dem Atelier-Server (Hetzner, `95.216.100.213`, Container `geef.stefan-bechtel.de`) sind zwei weitere LLM-CLIs installiert: **`claude`** (Claude Code CLI mit Subscription) und **`codex`** (OpenAI Codex CLI mit Subscription). Die Subscriptions decken signifikante Kapazitäten ab, ohne dass per Token abgerechnet wird.

Deine Aufgabe ist **PS-4**: einen zweiten Provider-Pfad aufbauen, der diese CLIs nutzbar macht. Architekturell konkret: ein **CLI-Side-Container** im Docker-Compose-Stack, der OpenAI-API-kompatibles HTTP exposed und intern die CLIs aufruft. Der Atelier-Code-Hauptstrom soll dabei **minimal** verändert werden — idealerweise nutzt der bestehende `OpenAiCompatibleClient` einfach einen alternativen Endpoint.

OpenRouter bleibt **parallel** verfügbar für günstige Modelle und schnelle Reviewer-Aufgaben. Die Wahl zwischen den Providern wird **pro Akteur** in der Konfiguration getroffen.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors. **Plan-Phase-Integration** als Architect-Form.

**Phase 1.2 ist diesmal stark recherche-lastig:**
- Was unterstützen `claude` und `codex` CLIs aktuell? Print-Modus? JSON-Output? Tool-Use direkt? Modellwahl per Flag?
- Wie funktioniert die Auth aktuell? Browser-Login? API-Key-Env? Beides?
- Wie verhalten sich die CLIs bei concurrenten Aufrufen? Rate-Limits aus Subscription?
- Gibt es bestehende OSS-Wrapper, die CLIs in OpenAI-kompatibles HTTP übersetzen (z.B. `litellm`, eigene Wrapper)?

Bevor du baust, dokumentiere die Recherche-Ergebnisse im Plan-Dokument.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`/srv/CLAUDE.md`** und (falls existent) **`/srv/docker/docs/docker-deployment.md`** — Server-Konventionen für Side-Container, Netzwerke, Volume-Mounts.
3. **`CLAUDE.md`** im Repo-Root.
4. **`docs/02-architecture.md`** — besonders die LLM-Provider-Sektion (aus M1).
5. **`docs/05-decisions-log.md`**, besonders **D-017** und **D-018** (M1: OpenAI-konformer Provider, ToolChoice-Convention, Pro-Akteur-Lookup).
6. **`docs/reports/migration-01-report.md`** — wie `OpenAiCompatibleClient` funktioniert, `OpenAiMessageFormat`-Serialisierung, ToolChoice-Mapping.
7. **Aktueller Code:**
   - `src/Geef.Atelier.Infrastructure/Llm/OpenAiCompatibleClient.cs` — der bestehende Adapter, **soll nicht ersetzt werden**
   - `src/Geef.Atelier.Infrastructure/Llm/LlmOptions.cs` — die Pro-Akteur-Konfiguration, wird erweitert
   - `src/Geef.Atelier.Infrastructure/Llm/OpenAiMessageFormat.cs` — Serialisierungs-Pattern
   - `src/Geef.Atelier.Infrastructure/Pipeline/LlmExecutionStep.cs` und `LlmReviewer.cs` — wie die Akteure den `ILlmClient` aufrufen
   - `docker-compose.yml` aus Schritt 10 + PS-1 — wo der Side-Container eingehängt wird
8. **CLI-Doku** (Web-Recherche):
   - `claude --help` und Doku auf docs.anthropic.com
   - `codex --help` und Doku auf openai.com/codex oder GitHub
   - Existierende OSS-Wrapper, z.B. via `npm`, `pypi` oder `github.com/topics/openai-api-wrapper`

## In Schritten 1–9 + M1 etablierte Realfakten (verbindlich)

Aus D-010 bis D-024 (oder D-025 nach PS-3). Zentrale Punkte für PS-4:

- **`ILlmClient`** mit `CompleteAsync(LlmRequest, ct)` als Provider-Vertrag (D-018).
- **`OpenAiCompatibleClient`** als bestehende Implementierung — soll **wiederverwendet werden** wenn Side-Container OpenAI-kompatibel spricht.
- **`LlmOptions.Actors`-Dictionary** mit Pro-Akteur-Modell-Mapping (D-018 F2: String-Keys, nicht Enum).
- **ToolChoice-Convention** `"function:submit_review"` (D-018 F1) — Side-Container muss das verstehen.
- **`LlmResponse.ToolName` + `LlmResponse.ToolArgumentsJson`** als separate Properties (D-018(b)).
- **OpenRouter-Endpoint** `https://openrouter.ai/api/v1` bleibt aktiv.
- **Side-Container-Netzwerk** im Compose-Stack: `proxy` (D-023, Server-Konvention).

## Architektur — die Idee in groben Zügen

```
┌──────────────────────────────────────────────────────────────────────┐
│ Atelier-Container (Blazor + MCP)                                     │
│                                                                       │
│  ┌─────────────────┐                                                 │
│  │ OpenAiCompati-  │  → endpoint: cli  →  CLI-Proxy:8090            │
│  │ bleClient       │                                                  │
│  │ (unverändert)   │  → endpoint: openrouter  →  api.openrouter.ai  │
│  └─────────────────┘                                                  │
└──────────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼
┌──────────────────────────────────────────────────────────────────────┐
│ CLI-Proxy-Container (NEU)                                            │
│                                                                       │
│  HTTP-Server (FastAPI / .NET Minimal API / Express)                  │
│   ├─ /v1/chat/completions  ← OpenAI-Schema                          │
│   ├─ /health                                                         │
│   └─ /v1/models                                                      │
│                                                                       │
│  Interne Logik:                                                       │
│   1. Request parsen                                                  │
│   2. Model-Routing: claude-* → claude CLI, gpt-* → codex CLI         │
│   3. CLI aufrufen (mit Tool-Schema als Prompt-Erweiterung)           │
│   4. Output parsen: Text → OpenAI ChatCompletion-Response wrappen   │
│   5. Tool-Use-Output strukturieren                                   │
│                                                                       │
│  Volume: /auth (claude + codex Auth-Tokens persistent)               │
│  Semaphore: Rate-Limit-respektierende Sequenzialisierung             │
└──────────────────────────────────────────────────────────────────────┘
```

## Konkrete Anforderungen

### 1. CLI-Proxy-Container

**Tech-Stack-Empfehlung:** Python 3.12 + FastAPI. Begründung: kompakter Code für HTTP-Wrapper, etabliertes Ecosystem für CLI-Subprocess-Handling, gute JSON-Parsing-Libraries. Architect kann anders entscheiden (z.B. .NET Minimal API für Stack-Konsistenz, oder Node falls bestehende OSS-Wrapper genutzt werden).

**Verzeichnis-Struktur (Vorschlag):** Neues Top-Level-Verzeichnis `cli-proxy/` im Repo:
```
cli-proxy/
├── Dockerfile
├── pyproject.toml (oder requirements.txt)
├── src/
│   ├── main.py              # FastAPI app
│   ├── claude_adapter.py    # claude-CLI-Aufrufe
│   ├── codex_adapter.py     # codex-CLI-Aufrufe
│   ├── openai_format.py     # OpenAI-Schema-Konversion
│   └── tool_use_parser.py   # Plaintext → tool_calls Extraktion
├── tests/
│   └── test_*.py
└── README.md
```

**HTTP-Endpoints:**
- `POST /v1/chat/completions` — OpenAI-konform, akzeptiert vollständige Chat-Completions-Request, gibt vollständige Response zurück
- `GET /v1/models` — Liste der unterstützten Modelle (für Debugging)
- `GET /health` — `{ "status": "ok", "cli_status": { "claude": "ready", "codex": "ready" } }`

**Tool-Use-Mapping (der kritische Teil):**
Wenn `tools` und `tool_choice` im Request:
1. Tool-Schema in den Prompt einbetten als zusätzliche System-Instruction: *"Respond ONLY with a JSON object matching this schema: ..."*
2. CLI aufrufen, Plaintext-Output erhalten
3. Output parsen: Markdown-Fences entfernen, Whitespace trimmen, JSON parsen
4. In `tool_calls`-Form bringen: `{"id": "...", "type": "function", "function": {"name": "submit_review", "arguments": "<json>"}}`
5. `finish_reason: "tool_calls"` setzen

Defensives Parsing: Falls JSON-Parse fehlschlägt, Retry-Hint generieren oder `finish_reason: "stop"` mit Plaintext zurückgeben (downstream Reviewer-Code aus D-013(e) kann das handhaben).

**Model-Routing:**
- `claude-*`, `anthropic/claude-*` → claude CLI
- `gpt-*`, `o*`, `openai/gpt-*` → codex CLI
- Architect entscheidet das genaue Mapping basierend auf den Recherche-Ergebnissen (welche Modelle unterstützt jede CLI heute).

**Auth-Strategie:**
- Auth-Verzeichnis als Volume-Mount: `/auth` im Container, persistent
- User loggt sich initial einmal manuell ein: `docker exec -it atelier-cli-proxy claude auth login` (analog für codex)
- Auth-Token landet in `/auth/`, bleibt über Container-Restarts
- README dokumentiert den initialen Login-Schritt

**Concurrency-Strategie:**
- `asyncio.Semaphore` (oder Äquivalent in anderer Sprache) mit konfigurierbarer Maximum-Concurrent-Calls
- Default: `MAX_CONCURRENT_CALLS=2` pro CLI (Architect kalibriert nach Subscription-Rate-Limits)
- Bei Limit-Überschreitung: Warteschlange, keine Fehler

**Image-Build und Compose-Integration:**
- `cli-proxy/Dockerfile` baut das Image mit Python, FastAPI, claude-CLI, codex-CLI installiert
- `docker-compose.yml` erweitern um `cli-proxy`-Service mit `build: ./cli-proxy`
- Service im `proxy`-Netzwerk, **kein** Traefik-Routing nach außen (nur interner Atelier-Zugriff)
- Health-Check über `/health`

### 2. Atelier-Code-Erweiterungen

**Empfohlene Architektur:** Pro-Akteur-Provider-Wahl über erweiterte `LlmOptions`.

**`LlmOptions` umbauen** zu einem **Multi-Provider-Modell**:

```csharp
public sealed class LlmOptions
{
    public Dictionary<string, ProviderConfig> Providers { get; set; } = new();
    public Dictionary<string, ActorConfig> Actors { get; set; } = new();
    public int DefaultMaxTokens { get; set; } = 4096;

    public sealed class ProviderConfig
    {
        public string Endpoint { get; set; } = "";
        public string ApiKey { get; set; } = "";
    }

    public sealed class ActorConfig
    {
        public string Provider { get; set; } = "openrouter";  // Key in Providers-Dict
        public string Model { get; set; } = "";
        public int? MaxTokens { get; set; }
    }
}
```

**`appsettings.json`** entsprechend:
```json
{
  "Llm": {
    "Providers": {
      "openrouter": {
        "Endpoint": "https://openrouter.ai/api/v1",
        "ApiKey": ""
      },
      "cli": {
        "Endpoint": "http://atelier-cli-proxy:8090/v1",
        "ApiKey": ""
      }
    },
    "Actors": {
      "Executor": { "Provider": "cli", "Model": "claude-sonnet" },
      "BriefingTreueReviewer": { "Provider": "openrouter", "Model": "google/gemini-2.5-flash" },
      "KlarheitReviewer": { "Provider": "cli", "Model": "gpt-5-codex" }
    }
  }
}
```

Env-Var-Override für Secrets bleibt: `Llm__Providers__openrouter__ApiKey` aus `.env`.

**`OpenAiCompatibleClient`** wird **nicht** ersetzt, aber muss mit dem neuen Pro-Akteur-Provider-Lookup umgehen können. Drei Möglichkeiten:
- (i) Multi-Instance-Registrierung: pro Provider eine `HttpClient`-Instance, Akteur-spezifischer Lookup zur Runtime
- (ii) Eine Client-Instance, Endpoint und ApiKey pro Request übergeben
- (iii) Adapter-Factory-Pattern, das pro Akteur die richtige Konfiguration injiziert

Empfehlung: **(i) Multi-Instance** — sauber, Typed-HttpClient pro Provider, Resilience-Strategie aus M1 bleibt erhalten. Architect entscheidet.

**`LlmExecutionStep` und `LlmReviewer`** brauchen Anpassung: statt `IOptions<LlmOptions>` direkt zu lesen, brauchen sie einen Mechanismus, um pro Akteur-Name den richtigen `ILlmClient` zu bekommen. Z.B. eine neue `ILlmClientResolver`-Schnittstelle:

```csharp
public interface ILlmClientResolver
{
    (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName);
}
```

Architect detailliert die genaue Form.

**Backward-Compat:** Die alte `LlmOptions`-Form mit flachem `Endpoint`/`ApiKey`/`DefaultModel`/`Actors{Model, MaxTokens}` muss **nicht** unterstützt bleiben — Atelier ist Single-Maintainer-Projekt, harter Cut akzeptabel. Migrations-Anleitung im README.

### 3. Tests

**CLI-Proxy-Tests** in `cli-proxy/tests/` (Python):
- `test_openai_format` — Request-Parsing, Response-Schema-Konformität
- `test_tool_use_parser` — Tool-Use-Output korrekt extrahiert, defensives Parsing
- `test_claude_adapter_mock` — claude CLI mocked, Aufruf-Format korrekt
- `test_codex_adapter_mock` — codex CLI mocked, analog
- `test_concurrency` — Semaphore wirkt
- Echter CLI-Test nur als manueller Integration-Check, nicht Pflicht-CI

**Atelier-Tests** (C#):
- `LlmOptionsMultiProviderTests` — Konfigurations-Parsing, Provider/Actor-Lookup
- `OpenAiCompatibleClientMultiInstanceTests` — Pro-Provider-Konfiguration korrekt registriert
- `LlmClientResolverTests` — pro Akteur-Name den richtigen Client zurück
- Bestehende `OpenAiCompatibleClientTests` weiter grün (3 Unit-Tests aus M1)

**End-to-End-Test:**
- Side-Container hochfahren mit Mock-CLI (Bash-Script statt echte CLI, das ein vorgegebenes JSON zurückgibt)
- Atelier-Pipeline-Lauf mit `Executor → cli`-Konfiguration
- Verifikation: Request kommt am Mock an, Response wird verarbeitet

**Real-Test (manuell, optional für PS-4-Abschluss):**
- Echte CLIs im Side-Container authentifizieren
- Ein einfaches Briefing durch die Pipeline schicken
- Verifikation dass die echte CLI aufgerufen wird, Subscription-Tokens benutzt werden statt OpenRouter-Bezahlung

### 4. Dokumentation

- **`cli-proxy/README.md`** mit Setup-Anleitung: Image bauen, Auth-Login durchführen, Health prüfen
- **Repo-`README.md`** Production-Setup-Sektion erweitern um CLI-Proxy
- **`docs/02-architecture.md`** LLM-Provider-Sektion erweitern um Multi-Provider-Diagramm
- **`docs/05-decisions-log.md`** D-026 (oder nächste freie Nummer) mit Architect-Entscheidungen

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` 0 Errors, 0 Warnings.
2. `dotnet test` — alle bestehenden Tests grün (96 nach PS-3 + neue Atelier-Tests).
3. **CLI-Proxy-Image baut** ohne Fehler.
4. **CLI-Proxy-Tests grün** (Python, sind nicht Teil des dotnet-Test-Laufs, separater pytest-Run).
5. **`docker compose up -d` startet drei Container healthy:** geef-atelier, postgres, postgres-backup, **plus cli-proxy**.
6. **`docker compose exec atelier-cli-proxy curl localhost:8090/health`** liefert HTTP 200 mit JSON-Status.
7. **Atelier-Pipeline funktioniert weiterhin** mit Pure-OpenRouter-Setup (Backward-Sanity).
8. **Pro-Akteur-Provider-Wahl funktional:** Konfigurations-Setup mit gemischten Providern (z.B. Executor=cli, ein Reviewer=openrouter) führt zu erfolgreichen Pipeline-Runs.
9. **CLI-Adapter-Funktionalität verifiziert:** Mindestens ein End-to-End-Test mit Mock-CLI im Side-Container. Optional: ein manueller Real-Test mit echter CLI (im Bericht festhalten).
10. **README aktualisiert** — Setup-Schritte für Side-Container und CLI-Login dokumentiert.
11. **Decisions-Log-Eintrag** mit Architect-Entscheidungen und Recherche-Ergebnissen.

## Was du in diesem Schritt NICHT tust

- **Keine Crew-Templates oder Reviewer-Profile** — PS-5.
- **Keine UI-Anpassungen** für Provider-Wahl — kommt vermutlich in PS-6 oder PS-7.
- **Keine Cost-Tracking-Anpassungen** für CLI-Provider — separater Roadmap-Punkt.
- **Kein Auto-Failover** zwischen Providern (wenn CLI ausfällt → OpenRouter) — kann später.
- **Kein Streaming-Response-Support** — Atelier nutzt komplette Antworten, nicht Streaming.
- **Keine Domain-Modell-Änderungen** — `RunEntity` und Co. bleiben.
- **Keine MCP-Schicht-Änderungen** — MCP-Tools rufen weiter `IRunService`, der ist Provider-agnostisch.
- **Keine Pipeline-Convergence-Logik-Änderungen** — die Reviewer-Kalibrierung aus PS-2 bleibt.

## Architect-Konsultation (Phase 1.4) — sechs Schwerpunkte

1. **Side-Container Tech-Stack:** Python+FastAPI (Empfehlung) vs. .NET Minimal API (Stack-Konsistenz) vs. Node.js (falls bestehende OSS-Wrapper gefunden). Architect entscheidet basierend auf Recherche-Ergebnissen.

2. **CLI-Recherche-Outcomes:** Was tatsächlich von `claude` und `codex` unterstützt wird in der aktuellen Version. Output-Modi, Tool-Use-Fähigkeit, Auth-Verfahren, Rate-Limits. Dieses Wissen formt die `claude_adapter.py` und `codex_adapter.py`.

3. **Interface-Form:** OpenAI-API-kompatibel (Empfehlung — Code-Hauptstrom unverändert) vs. eigenes Atelier-Interface mit neuem `CliLlmClient`. Falls die CLIs sich so unkonventionell verhalten, dass OpenAI-Wrapping unsauber wird, kann eigener Adapter sinnvoller sein.

4. **`LlmOptions`-Erweiterungs-Form:** Multi-Provider-Dict + Pro-Akteur-Provider-Wahl (Empfehlung) vs. alternatives Schema. Wichtig: Backward-Compat ist **nicht** erforderlich.

5. **`OpenAiCompatibleClient`-Multi-Instance-Strategie:** Pro-Provider-Typed-HttpClient (Empfehlung) vs. Per-Request-Konfiguration vs. Factory-Pattern. Resilience-Strategie aus M1 muss erhalten bleiben.

6. **Tool-Use-Parsing-Robustheit:** Wie aggressiv darf das defensive Parsing im Side-Container sein? Wenn der CLI-Output mal kein valides JSON liefert, was passiert? Empfehlung: Retry-Hint im Reviewer-Pattern aus D-013(e), kein Critical-Abort.

`geef_architecture.md` prüft Konsistenz mit dem LLM-Provider-Diagramm in `02-architecture.md`.

## Reviewer-Hinweise

- **R1 (Functional Correctness):** Alle 11 ACs prüfen. Besonders 7 (Backward-Sanity mit OpenRouter), 8 (Pro-Akteur-Mixing), 9 (CLI-Adapter funktional). Live-Test auf Production-Server.
- **R2 (Code Quality):** Saubere Trennung Atelier-Code ↔ Side-Container. Python-Code im Side-Container idiomatisch (PEP-8, Type-Hints, ggf. Pydantic für Schema). C#-Code-Erweiterungen sauber refactort, keine Duplikation.
- **R3 (Test Execution):** dotnet-Tests grün, separater pytest-Lauf im CLI-Proxy. Test-Coverage-Lücken explizit dokumentieren.
- **R4 (Architecture Compliance):** Side-Container-Pattern konsistent mit Server-Konvention. Code-Hauptstrom-Eingriffe minimal. `ILlmClient`-Vertrag respektiert.
- **R5 (Live Sanity):** Side-Container fährt hoch und health-check grün. Atelier kann CLI-Endpoint ansprechen (mind. Mock-Test). Optional: Real-CLI-Test im Bericht.

## Persistenter Abschlussbericht

Bericht nach `docs/reports/post-skeleton-04-cli-adapter-report.md`. Wichtig diesmal:

1. **Recherche-Ergebnisse** zu `claude` und `codex` CLI — welche Modes funktionieren, welche nicht. Diese Information ist später wertvoll, wenn neue CLI-Versionen kommen.
2. **Was wurde umgesetzt** — Side-Container, Adapter-Schichten, Atelier-Erweiterungen.
3. **Architect-Output** — alle sechs Schwerpunkte.
4. **Pre-Mortem & Devil's Advocate** — Auth-Verlust im Side-Container, CLI-Updates die Output-Format ändern, Rate-Limit-Überschreitung, Tool-Use-Parsing-Edge-Cases.
5. **Reviewer-Iterationen** — Tabelle.
6. **Akzeptanzkriterien-Check** — Tabelle mit allen 11 ACs.
7. **Beobachtungen** — wie verhalten sich die CLIs in der Praxis? Welche Edge-Cases sind aufgetaucht?
8. **Optional: Real-CLI-Test-Ergebnis** — wenn manuell durchgeführt, mit Beobachtungen zu Latenz und Subscription-Verbrauch.
9. **Empfehlungen für PS-5** — was sollten wir für die Crew-Template-Schicht aus diesem Schritt mitnehmen?

## Konventionen

- C#-Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Python-Code im Side-Container: **Englisch**, PEP-8-konform.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- **Side-Container-Secrets** (CLI-Auth-Tokens) niemals in source control, niemals in Logs, niemals im Bericht.
- `.gitignore`-Update für Auth-Volume-Ausschluss falls relevant.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.
