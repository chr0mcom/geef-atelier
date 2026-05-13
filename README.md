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

**Run-Management:**
- `submit_request` — Neuen Run einreichen (optional: `crew_template`, `custom_crew`)
- `get_run_status` — Status eines Runs abfragen
- `get_run_result` — Ergebnis eines abgeschlossenen Runs abrufen
- `list_runs` — Letzte Runs auflisten
- `get_run_details` — Detailinformationen inkl. Iterationen abrufen
- `cancel_run` — Laufenden Run abbrechen

**Crew-System:**
- `list_crew_templates` — Verfügbare Crew-Templates (System + Custom)
- `list_reviewer_profiles` — Verfügbare Reviewer-Profile (System + Custom)

## Crew-System

Jeder Run verwendet eine konfigurierbare Crew aus Executor (schreibt den Draft) und Reviewern (bewerten den Draft). Das Standard-Template `"klassik"` reproduziert das ursprüngliche Verhalten mit zwei Reviewern in paralleler Ausführung.

**Verfügbare Evaluation-Strategien:** `Parallel` (Standard), `Sequential`, `FailFast`, `Priority`.

**System-Profile** sind im Code versioniert und read-only. **Custom-Profile** können per `ICrewService` oder MCP erstellt und in der DB gespeichert werden (erhalten automatisch den Prefix `"custom-"`).

Jeder Run speichert einen vollständig eingebetteten **CrewSnapshot** in der DB — damit bleibt der Run reproduzierbar, auch wenn Profile später geändert werden.

Details: [`docs/08-crew-system.md`](docs/08-crew-system.md)

---

## Production-Deployment

Voraussetzungen: Traefik läuft auf dem Server, DNS für `geef.stefan-bechtel.de` zeigt auf den Server.

### Erste Einrichtung

**1. Production-Secrets generieren:**

```bash
cd /srv/docker/websites/geef_atelier

# BCrypt-Hash für das UI-Passwort (workFactor 11)
dotnet run --project tools/HashPassword -- "DeinPasswort"

# Zufälligen MCP-Token generieren (64 Hex-Zeichen)
openssl rand -hex 32

# Zufälliges Postgres-Passwort
openssl rand -base64 24
```

**2. `.env`-Datei anlegen** (niemals in Git committen — ist gitignored):

```env
POSTGRES_DB=geef_atelier
POSTGRES_USER=geef_atelier
POSTGRES_PASSWORD=<generiertes-passwort>
ATELIER_USER=admin
ATELIER_PASSWORD_HASH=<bcrypt-hash>
ATELIER_MCP_TOKEN=<hex-token>
LLM_API_KEY=<openrouter-api-key>
```

**3. Stack starten:**

```bash
docker compose up -d --build
```

Migrations laufen automatisch beim Start. Health-Check: `https://geef.stefan-bechtel.de/health`.

### Neustart / Update

```bash
docker compose up -d --build   # baut neu und startet Container
docker compose logs -f web      # Logs folgen
```

### Wichtige URLs

| URL | Beschreibung |
|-----|--------------|
| `https://geef.stefan-bechtel.de/` | Web-UI (Cookie-Auth) |
| `https://geef.stefan-bechtel.de/health` | Health-Check |
| `https://geef.stefan-bechtel.de/mcp` | MCP-Endpoint (Bearer-Auth) |

---

## Backup & Restore

Der `postgres-backup`-Container läuft im Stack und erstellt automatisch tägliche Backups.

### Backup-Konfiguration

- **Zeitplan:** täglich um 03:00 UTC
- **Retention:** 7 Tages-Snapshots, 4 Wochen-Snapshots, 6 Monats-Snapshots
- **Speicherort:** Docker-Volume `geef-atelier-backups`, gemountet auf `/backups` im Backup-Container
- **Format:** `.sql.gz` (gzip-komprimiertes pg_dump SQL)

### Backup manuell auslösen

```bash
docker compose exec postgres-backup /backup.sh
```

### Aktuelle Backups inspizieren

```bash
docker compose exec postgres-backup ls -lh /backups/last/
docker compose exec postgres-backup ls -lh /backups/daily/
```

### Backup-Datei extrahieren

```bash
# Backup-Datei aus dem Container-Volume kopieren
docker cp geef-atelier-postgres-backup:/backups/last/<dateiname>.sql.gz ./
```

### Restore

```bash
# 1. App stoppen (Backup-Container kann weiterlaufen)
docker compose stop web

# 2. Restore via Skript
./scripts/restore-backup.sh <pfad-zur-backup-datei.sql.gz>

# 3. Verifikation
curl https://geef.stefan-bechtel.de/health
```

> **Hinweis:** `scripts/restore-backup.sh` überschreibt alle bestehenden Daten.
> Vor dem Restore immer eine aktuelle Backup-Kopie sichern.

### Off-Site-Backup

Das Volume-Backup schützt gegen DB-Container-Crash und Logic-Errors, **nicht** gegen Server-Hardware-Ausfall oder versehentliches `docker volume rm`. Für robusteren Schutz empfiehlt sich ein regelmäßiges rsync der Backup-Dateien auf einen zweiten Host (z.B. Hetzner Storage Box).

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
