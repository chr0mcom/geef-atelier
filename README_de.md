# Geef.Atelier

*[English](README.md) · **Deutsch***

Text-Generations-Pipeline-Plattform auf Basis des [Geef SDK](https://github.com/chr0mcom/geef). Mehrere Modelle arbeiten in konfigurierbaren Crews zusammen — Executor schreibt, Reviewer bewerten, die Pipeline iteriert bis zur Konvergenz.

## Implementierungsstand

**Schritt 1 ✅** Solution-Struktur, Postgres-Persistence, Health-Check, Docker-Compose.

**Schritt 2 ✅** In-memory Geef-Pipeline mit Stub-Providern — kein LLM, kein DB-Zugriff. Convergence-Loop, EventSink, alle vier Provider-Verträge implementiert und mit xUnit-Tests abgedeckt.

**Schritt 3 ✅** Echte Anthropic-API-Aufrufe — `IAnthropicClient`, `LlmExecutionStep`, zwei `LlmReviewer` (Tool Use). Polly-Resilience.

**Schritt 4 ✅** Postgres-Persistierung — `PostgresEventSink` schreibt jeden Pipeline-Run mit Iterationen, Findings, Token-Verbrauch und Event-Log in die DB.

**Schritt 5 ✅** BackgroundService-Orchestrierung — `RunOrchestratorService` pollt für Pending-Runs, setzt atomaren Claim, führt die Geef-Pipeline concurrent aus, recovert crashed Runs beim Start.

**Schritt 6 ✅** Application-Service-Layer — `IRunService` (Submit/Get/List/Cancel), `IRunRepository`, Cancellation-Watcher.

**Schritt 7 ✅** Blazor-UI — drei Pages (`/new`, `/runs`, `/runs/{id}`), SignalR-Hub mit Live-Status, 9 UI-Komponenten, bUnit- und Playwright-Tests.

**Schritt 8 ✅** Cookie-Auth — Single-User-Login, `[Authorize]` auf Pages, Login/Logout, `TestAuthenticationHandler` für E2E-Tests.

**Schritt 9+ ✅** Crew-System, Advisor-Pässe, Template Studio, Domain-Templates, Grounding-Provider-CRUD, Vector-Store-RAG, PDF-Support, Cost-Tracking.

**MCP-OAuth ✅** Self-hosted OAuth 2.1 Authorization Server (RFC 8414/7591/7636/7009/8252) — Authorization-Code-Flow mit Pflicht-PKCE/S256, Opaque Tokens, Refresh-Rotation, Reuse-Detection.

**Multi-User ✅** DB-basierte Benutzerverwaltung mit BCrypt, Admin-UI unter `/admin/users`, Startup-Seeding des Admin-Accounts aus Env-Vars.

**Run-User-Isolation ✅** Jeder Nutzer sieht nur seine eigenen Runs; Admin-Override per expliziten Umschaltern. MCP-Runs werden dem autorisierenden OAuth-Nutzer zugeordnet, Claude-Code-CLI-Runs (statisches Token) dem Admin (D-042).

Aktuell: **über 800 Tests** (grün; 2 bekannte Testcontainers-Flakes).

Vollständiger Scope: [docs/01-vision-and-scope.md](docs/01-vision-and-scope_de.md)

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

### Benutzer

Die App unterstützt mehrere Benutzerkonten. Der erste Admin-Account wird automatisch aus Env-Vars gesetzt und beim Start angelegt bzw. synchronisiert:

```bash
# BCrypt-Hash für ein Passwort generieren (work factor 11)
dotnet run --project tools/HashPassword -- "DeinPasswort"
# Ausgabe: $2a$11$...
```

```env
ATELIER_USER=stefan
ATELIER_PASSWORD_HASH=$2a$11$...
```

Weitere Benutzer können im Admin-Panel unter `/admin/users` angelegt werden (nur für den Admin-Account sichtbar).

**Dev-Defaults** (nur für lokale Entwicklung, in `docker-compose.dev.yml`):
- Username: `admin`
- Passwort: `DevPassword!`

### MCP-Token (Claude Code CLI)

```bash
# Zufälligen Token generieren
openssl rand -hex 32
```

```env
ATELIER_MCP_TOKEN=<hex-token>
```

---

## Tests

```bash
# Benötigt laufenden Docker-Daemon (Testcontainers)
dotnet test
```

## Migration manuell ausführen

```bash
dotnet ef database update \
  --project src/Geef.Atelier.Infrastructure \
  --startup-project src/Geef.Atelier.Web
```

---

## MCP-Server

Der MCP-Server läuft unter `/mcp`. Zwei Auth-Pfade stehen parallel zur Verfügung:

### Pfad A: Statisches Bearer-Token (Claude Code CLI)

```json
{
  "mcpServers": {
    "geef-atelier": {
      "url": "https://<your-domain>/mcp",
      "transport": "streamable-http",
      "auth": {
        "type": "bearer",
        "token": "<ATELIER_MCP_TOKEN>"
      }
    }
  }
}
```

### Pfad B: OAuth 2.1 (Claude Desktop / Claude.ai Custom Connector)

URL `https://<your-domain>/mcp` im Client eintragen — der Client erkennt den `WWW-Authenticate`-Header mit der Resource-Metadata-URL und startet den OAuth-Flow automatisch (Dynamic Client Registration → Browser-Login → Consent-Seite → Token-Exchange).

Voraussetzung: Der OAuth-Client muss im Admin-Panel unter `/admin/oauth-clients` registriert sein, oder der Client registriert sich selbst per Dynamic Client Registration (`POST /oauth/register`).

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
- `list_advisor_profiles` — Verfügbare Advisor-Profile (System + Custom)
- `list_grounding_provider_profiles` — Verfügbare Grounding-Provider-Profile (System + Custom)

**Wissensbasis & Template Studio:**
- `list_knowledge_documents` — Globale Wissensbasis-Dokumente auflisten
- `analyze_template_proposal` — Aufgabenbeschreibung analysieren, Template-Vorschlag erzeugen (persistiert)
- `materialize_template_proposal` — Geprüften Vorschlag als Custom-Template + -Profile materialisieren

Insgesamt 13 MCP-Tools. Vollständige Endpoint-Dokumentation: [docs/09-endpoint-reference.md](docs/09-endpoint-reference_de.md)

---

## Crew-System

Jeder Run verwendet eine konfigurierbare Crew aus Executor (schreibt den Draft) und Reviewern (bewerten den Draft). Das Standard-Template `"klassik"` reproduziert das ursprüngliche Verhalten mit zwei Reviewern in paralleler Ausführung.

**Verfügbare Evaluation-Strategien:** `Parallel` (Standard), `Sequential`, `FailFast`, `Priority`.

**System-Profile** sind im Code versioniert und read-only. **Custom-Profile** können per `ICrewService` oder MCP erstellt und in der DB gespeichert werden.

Jeder Run speichert einen vollständig eingebetteten **CrewSnapshot** in der DB — damit bleibt der Run reproduzierbar, auch wenn Profile später geändert werden.

### Crew-Verwaltung in der UI

| Seite | URL |
|-------|-----|
| Crew-Übersicht | `/crew` |
| Template-Liste | `/crew/templates` |
| Template anlegen/bearbeiten | `/crew/templates/new`, `/crew/templates/{name}` |
| Reviewer-Profile | `/crew/profiles/reviewers` |
| Executor-Profile | `/crew/profiles/executors` |
| Grounding-Provider | `/crew/profiles/grounding-providers` |

Details: [docs/08-crew-system.md](docs/08-crew-system_de.md)

---

## Production-Deployment

Voraussetzungen: Traefik läuft auf dem Server, DNS der Domain zeigt auf den Server.

### Erste Einrichtung

**1. Secrets generieren:**

```bash
# BCrypt-Hash für das UI-Passwort (work factor 11)
dotnet run --project tools/HashPassword -- "DeinPasswort"

# MCP-Token (64 Hex-Zeichen)
openssl rand -hex 32

# Postgres-Passwort
openssl rand -base64 24
```

**2. `.env`-Datei anlegen** (niemals in Git committen — ist gitignored):

```env
POSTGRES_DB=geef_atelier
POSTGRES_USER=geef_atelier
POSTGRES_PASSWORD=<generiertes-passwort>

ATELIER_USER=<admin-username>
ATELIER_PASSWORD_HASH=<bcrypt-hash>
ATELIER_MCP_TOKEN=<hex-token>
ATELIER_DOMAIN=<your-domain>

LLM_OPENROUTER_API_KEY=<openrouter-api-key>

# Tavily Web Search (https://tavily.com) — optional
TAVILY_API_KEY=
```

**3. Stack starten:**

```bash
docker compose up -d --build
```

Migrations laufen automatisch beim Start. Health-Check: `https://<your-domain>/health`.

### Neustart / Update

```bash
docker compose build --no-cache web && docker compose up -d web
docker compose logs -f web
```

### Wichtige URLs

| URL | Beschreibung |
|-----|--------------|
| `https://<your-domain>/` | Web-UI (Cookie-Auth) |
| `https://<your-domain>/health` | Health-Check |
| `https://<your-domain>/mcp` | MCP-Endpunkt (Bearer / OAuth 2.1) |
| `https://<your-domain>/admin/users` | Benutzerverwaltung (nur Admin) |
| `https://<your-domain>/admin/oauth-clients` | OAuth-Client-Verwaltung (nur Admin) |
| `https://<your-domain>/.well-known/oauth-authorization-server` | OAuth Server Metadata |

---

## Backup & Restore

Der `postgres-backup`-Container erstellt automatisch tägliche Backups.

- **Zeitplan:** täglich 03:00 UTC
- **Retention:** 7 Tages-, 4 Wochen-, 6 Monats-Snapshots
- **Speicherort:** Docker-Volume `geef-atelier-backups`
- **Format:** `.sql.gz` (gzip-komprimiertes pg_dump)

```bash
# Backup manuell auslösen
docker compose exec postgres-backup /backup.sh

# Backups inspizieren
docker compose exec postgres-backup ls -lh /backups/last/

# Backup aus Volume kopieren
docker cp geef-atelier-postgres-backup:/backups/last/<datei>.sql.gz ./
```

### Restore

```bash
docker compose stop web
./scripts/restore-backup.sh <pfad-zur-backup-datei.sql.gz>
curl https://<your-domain>/health
```

> **Hinweis:** `scripts/restore-backup.sh` überschreibt alle bestehenden Daten. Vor dem Restore eine aktuelle Kopie sichern.

---

## Projektstruktur

```
src/
  Geef.Atelier.Core/           Domain-Entities, Interfaces — keine externen Abhängigkeiten
  Geef.Atelier.Application/    IRunService, IOAuthService, IUserAdminService u.a.
  Geef.Atelier.Infrastructure/ EF Core, Npgsql, LLM-Clients, Geef.Sdk-Provider-Impl.
  Geef.Atelier.Web/            Blazor Server, BackgroundService, Endpoints, MCP-Server
  Geef.Atelier.Mcp/            MCP-Tool-Definitionen (Class Library)
tests/
  Geef.Atelier.Tests/          xUnit + Testcontainers + bUnit
tools/
  HashPassword/                BCrypt-Hash-Generator CLI
docs/
  reports/                     Abschlussberichte je Bau-Schritt
```

## Stack

- .NET 10 / Blazor Server / Minimal API
- PostgreSQL 16 + pgvector via `Npgsql.EntityFrameworkCore.PostgreSQL`
- [Geef.Sdk 1.0.0-ci.1](https://www.nuget.org/packages/Geef.Sdk/) (prerelease)
- Docker / Traefik
