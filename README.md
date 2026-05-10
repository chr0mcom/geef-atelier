# Geef.Atelier

Text-Generations-Pipeline-Plattform auf Basis des [Geef SDK](https://github.com/chr0mcom/geef).

**Schritt 1 ✅** Solution-Struktur, Postgres-Persistence, Health-Check, Docker-Compose.
**Schritt 2 ✅** In-memory Geef-Pipeline mit Stub-Providern — kein LLM, kein DB-Zugriff. Convergence-Loop (2 Iterationen), EventSink, alle vier Provider-Verträge (Grounding/Execution/Reviewer/Finalizer) implementiert und mit xUnit-Tests abgedeckt.
**Schritt 3 ✅** Echte Anthropic-API-Aufrufe — `IAnthropicClient` mit HTTP-Implementierung, `LlmExecutionStep` und zwei `LlmReviewer` (Tool Use mit `submit_review`). 11/11 Tests grün (Mocks + Stub-Regression + Skip-If-No-Key-Integration). Polly-Resilience via `Microsoft.Extensions.Http.Resilience`.
**Schritt 4 ✅** Postgres-Persistierung — `PostgresEventSink` schreibt jeden Pipeline-Run mit Iterationen, Findings, Token-Verbrauch und Event-Log in die DB. `IRunPersistenceService` für Run-Initialisierung. 15/15 Tests grün (4 neue Persistence-Tests mit Testcontainers).

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

## Projektstruktur

```
src/
  Geef.Atelier.Core/           Domain-Entities, keine externen Abhängigkeiten
  Geef.Atelier.Infrastructure/ EF Core, Npgsql, Geef.Sdk-Provider-Implementierungen
  Geef.Atelier.Web/            Blazor Server, IRunService, Health-Check
  Geef.Atelier.Mcp/            MCP-Server-Stub (aktiv ab Schritt 9)
tests/
  Geef.Atelier.Tests/          xUnit + Testcontainers
docs/
  reports/                     Abschlussberichte je Bau-Schritt
```

## Stack

- .NET 10 / Blazor Server
- PostgreSQL 16 via `Npgsql.EntityFrameworkCore.PostgreSQL`
- [Geef.Sdk 1.0.0-ci.1](https://www.nuget.org/packages/Geef.Sdk/) (prerelease)
- Docker / Traefik
