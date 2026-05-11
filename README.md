# Geef.Atelier

Text-Generations-Pipeline-Plattform auf Basis des [Geef SDK](https://github.com/chr0mcom/geef).

**Schritt 1 ✅** Solution-Struktur, Postgres-Persistence, Health-Check, Docker-Compose.
**Schritt 2 ✅** In-memory Geef-Pipeline mit Stub-Providern — kein LLM, kein DB-Zugriff. Convergence-Loop (2 Iterationen), EventSink, alle vier Provider-Verträge (Grounding/Execution/Reviewer/Finalizer) implementiert und mit xUnit-Tests abgedeckt.
**Schritt 3 ✅** Echte Anthropic-API-Aufrufe — `IAnthropicClient` mit HTTP-Implementierung, `LlmExecutionStep` und zwei `LlmReviewer` (Tool Use mit `submit_review`). 11/11 Tests grün (Mocks + Stub-Regression + Skip-If-No-Key-Integration). Polly-Resilience via `Microsoft.Extensions.Http.Resilience`.
**Schritt 4 ✅** Postgres-Persistierung — `PostgresEventSink` schreibt jeden Pipeline-Run mit Iterationen, Findings, Token-Verbrauch und Event-Log in die DB. `IRunPersistenceService` für Run-Initialisierung. 15/15 Tests grün (4 neue Persistence-Tests mit Testcontainers).
**Schritt 5 ✅** BackgroundService-Orchestrierung — `RunOrchestratorService` pollt für `Pending`-Runs, setzt atomaren Claim, führt die Geef-Pipeline concurrent aus (SemaphoreSlim), recovert crashed Runs beim Start, drainiert In-Flight-Tasks bei StopAsync. 19/19 Tests grün (4 neue Orchestrator-Tests mit deterministischem GatedFakeAnthropicClient).
**Schritt 6 ✅** Application-Service-Layer — `IRunService` (Submit/Get/List/Cancel) in neuem `Geef.Atelier.Application`-Projekt. `IRunRepository`/`RunRepository` (Variante β). `RunEntity.CancellationRequested`-Flag + EF-Migration. Cancellation-Watcher pro Run pollt DB, signalisiert CTS → Pipeline-OCE → Aborted. 31/31 Tests grün (5 neue Application-Tests).
**Schritt 7 ✅** Blazor-UI — drei Pages (`/new`, `/runs`, `/runs/{id}`), SignalR-Hub (`RunHub`) mit zwei Groups, `IRunNotifier`/`SignalRRunNotifier` in Core/Web, 9 UI-Komponenten in `Components/UI/`. bUnit-Komponenten-Tests (4) + Playwright-E2E-Tests (4, mit `WebTestHost`). SignalR Live-Status ohne Page-Reload verifiziert. AC8 (OpenRouter-Real-Pipeline) grün. 55/55 Tests grün.
**Schritt 8 ✅** Cookie-Auth — Single-User-Login (`ATELIER_USER`/`ATELIER_PASSWORD_HASH`), `[Authorize]` auf Pages, Login/Logout-Flow, `TestAuthenticationHandler` für E2E-Tests. 71/71 Tests grün.

Vollständiger Scope: [docs/01-vision-and-scope.md](docs/01-vision-and-scope.md)

---

## Lokaler Start

```bash
# App + Postgres starten
docker compose -f docker-compose.dev.yml up --build -d

# Health-Check
curl http://localhost:8080/health   # → Healthy

# Stack stoppen
docker compose -f docker-compose.dev.yml down
```

## Auth-Setup

Die App erfordert einen einzigen User, der über Umgebungsvariablen konfiguriert wird:

```bash
# BCrypt-Hash für ein Passwort generieren (work factor 11)
dotnet run --project tools/HashPassword -- "DeinPasswort"
# Ausgabe: $2a$11$...

# Umgebungsvariablen setzen (docker-compose.dev.yml überschreibbar)
ATELIER_USER=admin
ATELIER_PASSWORD_HASH=$2a$11$...
```

**Dev-Defaults** (nur für lokale Entwicklung, in `docker-compose.dev.yml`):
- Username: `admin`
- Passwort: `DevPassword!`

**Production**: `ATELIER_USER` und `ATELIER_PASSWORD_HASH` als Container-Umgebungsvariablen setzen. Der Hash wird mit `tools/HashPassword` erzeugt. Alternativ: ASP.NET Core-Konvention `AtelierUser__Username` / `AtelierUser__PasswordHash`.

---

## Migration manuell ausführen

```bash
dotnet ef database update \
  --project src/Geef.Atelier.Infrastructure \
  --startup-project src/Geef.Atelier.Web
```

## Tests

```bash
# Benötigt laufenden Docker-Daemon (Testcontainers)
dotnet test
```

---

## MCP-Server (Schritt 9)

Der MCP-Server läuft unter `/mcp` und ist mit Bearer-Token-Auth gesichert.

### Token generieren

```bash
openssl rand -hex 32
```

### Konfiguration

```bash
# In .env oder docker-compose.dev.yml override:
ATELIER_MCP_TOKEN=<generated-token>
```

### Claude Desktop Konfiguration

```json
{
  "mcpServers": {
    "geef-atelier": {
      "url": "http://95.216.100.213:8080/mcp",
      "headers": {
        "Authorization": "Bearer <your-token>"
      }
    }
  }
}
```

### MCP-Tools

- `submit_request` — Neuen Run einreichen
- `get_run_status` — Status eines Runs abfragen
- `get_run_result` — Ergebnis eines abgeschlossenen Runs abrufen
- `list_runs` — Letzte Runs auflisten
- `get_run_details` — Detailinformationen inkl. Iterationen abrufen
- `cancel_run` — Laufenden Run abbrechen

---

## Projektstruktur

```
src/
  Geef.Atelier.Core/           Domain-Entities, Interfaces (IRunRepository etc.), keine externen Deps
  Geef.Atelier.Application/    IRunService-Vertrag + RunService-Implementierung (→ nur Core-Dep)
  Geef.Atelier.Infrastructure/ EF Core, Npgsql, OpenAiCompatibleClient, Geef.Sdk-Provider-Impl.
  Geef.Atelier.Web/            Blazor Server, RunOrchestratorService, Health-Check
  Geef.Atelier.Mcp/            MCP-Server-Stub (aktiv ab Schritt 9)
tests/
  Geef.Atelier.Tests/          xUnit + Testcontainers
tools/
  HashPassword/                BCrypt-Hash-Generator CLI
docs/
  reports/                     Abschlussberichte je Bau-Schritt
```

## Stack

- .NET 10 / Blazor Server
- PostgreSQL 16 via `Npgsql.EntityFrameworkCore.PostgreSQL`
- [Geef.Sdk 1.0.0-ci.1](https://www.nuget.org/packages/Geef.Sdk/) (prerelease)
- Docker / Traefik
