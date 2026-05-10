# Claude-Code-Prompt: Schritt 1 — Solution-Setup mit Postgres und EF Core

*Diese Datei ist als Eingabe für Claude Code gedacht. Sie enthält den vollständigen Prompt, den du in Claude Code einkopieren kannst, um Schritt 1 des Walking Skeleton umzusetzen.*

---

## Mission

Du bist Senior .NET-Architekt und arbeitest am neuen Projekt **Geef.Atelier** — einer Text-Manufaktur, die auf dem bestehenden SDK [Geef](https://github.com/chr0mcom/geef) aufbaut. Deine Aufgabe ist **Schritt 1 von 10** im Walking-Skeleton-Plan: das Solution-Setup mit Postgres und EF Core.

## Vorgehen

**Du folgst strikt dem Workflow in [`geef_workflow.md`](../geef_workflow.md).** Das Dokument definiert die vier Phasen (Grounding / Execution / Evaluation / Finalize), die fünf Reviewer, die Advisor-Konsultationen und alle Hard Rules. Lies es zuerst und vollständig. Bei Konflikten zwischen diesem Prompt und dem Workflow gilt der Workflow.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

Vor Beginn der Implementierung lies in dieser Reihenfolge:

1. **`geef_workflow.md`** — der Workflow, dem du folgst.
2. **`geef-atelier-docs/01-vision-and-scope.md`** — was dieses Projekt ist und nicht ist.
3. **`geef-atelier-docs/02-architecture.md`** — Solution-Struktur, DB-Schema, Layer-Aufteilung. **Diese Architektur ist verbindlich** — sie ersetzt die Phase-1.4-Architect-Konsultation für diesen Schritt nicht, aber sie definiert den Korridor, innerhalb dessen der Architect arbeitet. Der Architect prüft Konsistenz, Vollständigkeit und füllt Lücken — er definiert die Struktur nicht neu.
4. **`geef-atelier-docs/03-walking-skeleton-plan.md`**, Abschnitt "Schritt 1" — der Scope dieses Tasks.
5. **Geef SDK auf GitHub**: [chr0mcom/geef](https://github.com/chr0mcom/geef) — README und `src/`-Struktur. Notiere die Konventionen (sealed records, required init, immutable contexts, XML-Docs auf Englisch, geef:-Präfix bei ContextKeys).

## Konkrete technische Anforderungen für Schritt 1

### Solution-Struktur

```
Geef.Atelier/
├── Geef.Atelier.sln
├── README.md
├── docker-compose.yml              // Production-Skelett
├── docker-compose.dev.yml          // Lokale Entwicklung mit Postgres-Container
├── .gitignore
├── .editorconfig
├── Directory.Packages.props        // zentrale NuGet-Versionsverwaltung
├── src/
│   ├── Geef.Atelier.Core/
│   ├── Geef.Atelier.Infrastructure/
│   ├── Geef.Atelier.Web/           // enthält Dockerfile
│   └── Geef.Atelier.Mcp/           // Stub im Skeleton, wird in Schritt 9 aktiv
└── tests/
    └── Geef.Atelier.Tests/
```

### Projekt-Eigenschaften

- TargetFramework: **net10.0**
- Sprachversion: latest
- Nullable: enabled
- ImplicitUsings: enabled
- TreatWarningsAsErrors: true (mit sinnvollen Suppressions wo nötig)
- Central Package Management via `Directory.Packages.props`

### Projekt-Referenzen

- `Web` → `Core`, `Infrastructure`
- `Infrastructure` → `Core`
- `Mcp` → `Core` (Skeleton-Stub)
- `Tests` → alle src-Projekte

### Geef-SDK-Einbindung

Prüfe, ob `Geef.Sdk` als NuGet-Paket verfügbar ist. Falls ja: PackageReference. Falls nein: pragmatische Alternative wählen (Git-Submodul, lokales Repo-Klonen mit ProjectReference auf relativen Pfad) und im README dokumentieren, mit `TODO: replace with NuGet when published`-Marker.

### NuGet-Pakete

- `Microsoft.EntityFrameworkCore` (latest stable für .NET 10)
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- `Microsoft.EntityFrameworkCore.Design` (Migrations-Tooling, in Web)
- ASP.NET Core 10 Blazor-Pakete (in Web)
- `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Testcontainers.PostgreSql` (in Tests)

### Domain-Records (in `Core/Domain/`)

Vier Entities laut `02-architecture.md` Abschnitt "Datenmodell": `RunEntity`, `IterationEntity`, `FindingEntity`, `EventEntity`. Plus Enums `RunStatus`, `FindingSeverity`. Verwende `sealed record` mit `required init`-Properties, IDs als `Guid` (Events als `long` identity), Timestamps als `DateTimeOffset`.

### DbContext (in `Infrastructure/Persistence/`)

`AtelierDbContext : DbContext` mit den vier `DbSet<>`-Properties. `OnModelCreating` konfiguriert: jsonb-Mapping für JSON-Felder, enum-to-string-Mapping, Indices auf `Runs.Status`, `Events.RunId`, `Iterations.RunId`.

### Web-Host (in `Web/Program.cs`)

- Kestrel + Blazor Server konfiguriert
- DbContext via DI registriert (Connection-String aus `IConfiguration`)
- Health-Check-Endpoint `/health` mit DB-Connection-Probe (200 OK wenn DB erreichbar)
- Auto-Migration beim Start
- Eine minimale Index-Page (`Index.razor`) mit Lebenszeichen-Text "Geef.Atelier — Walking Skeleton"

### Konfiguration

- `appsettings.json` mit Default-Connection-String für lokale Dev
- Production überschreibt via Environment-Variable `ConnectionStrings__Postgres`

### Erste Migration

Migration-Name: `InitialCreate`. Erzeugt alle vier Tabellen mit Indices.

### docker-compose.dev.yml

Postgres + App. Postgres-Image: `postgres:16-alpine`. Volumen für Persistenz. App-Service baut über `src/Geef.Atelier.Web/Dockerfile`.

### docker-compose.yml (Production-Skelett)

Nur die App. Connection-String aus Environment-Variable `ATELIER_POSTGRES_CONNECTION`. Keine eigene Postgres — verbindet sich mit der existierenden Postgres-Instanz auf dem Server.

### Dockerfile (in `src/Geef.Atelier.Web/`)

Multi-Stage: `mcr.microsoft.com/dotnet/sdk:10.0` → `mcr.microsoft.com/dotnet/aspnet:10.0`. Non-root User. HEALTHCHECK-Direktive auf `/health`.

### README.md

Konzise Anleitung: Was ist Geef.Atelier (Verweis auf `geef-atelier-docs/`), lokale Entwicklung (`docker compose -f docker-compose.dev.yml up`), Migration manuell, Tests laufen lassen.

### Tests (mind. drei Smoke-Tests)

1. DbContext kann gegen Testcontainers-Postgres erstellt werden.
2. Migration läuft erfolgreich gegen Testcontainers-Postgres.
3. `WebApplicationFactory`-Test ruft `/health` auf, erwartet 200 OK.

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` läuft ohne Fehler oder Warnungen, die deinen Code betreffen.
2. `dotnet test` läuft alle Tests grün.
3. `dotnet ef database update` läuft erfolgreich gegen lokale Postgres.
4. `docker compose -f docker-compose.dev.yml up --build` startet App und DB; `curl http://localhost:8080/health` antwortet 200 OK.
5. Index-Page im Browser zeigt das Lebenszeichen.
6. README erklärt den lokalen Start.

Diese Kriterien werden während Phase 3 von Reviewer 1 und Reviewer 3 explizit geprüft.

## Was du in diesem Schritt NICHT tust

- Keine echten LLM-Calls — kommt in Schritt 3.
- Keine Pipeline-Provider implementieren — kommt in Schritt 2.
- Keine Auth — kommt in Schritt 8.
- Keine MCP-Tools — `Geef.Atelier.Mcp` wird angelegt aber bleibt leer (Class mit `// TODO: Schritt 9`-Kommentar reicht).
- Keine UI-Seiten außer der minimalen Index-Page.

## Persistenter Abschlussbericht für den Brainstorming-Chat

**Zusätzlich zur normalen Phase-4.5-Summary** (die in den Chat geht und gemäß Workflow ausreicht): Lege einen ausführlichen Abschlussbericht ab unter:

```
geef-atelier-docs/reports/step-01-report.md
```

Dieser Pfad liegt **außerhalb** der `geef_*.md`-Cleanup-Liste aus Phase 4.3 und überlebt die Cleanup-Phase. Er wird in einem separaten Brainstorming-Chat gelesen, um Schritt 2 zu planen.

Inhalt des Berichts (Markdown, Deutsch):

1. **Was wurde umgesetzt** — Datei-für-Datei mit kurzer Beschreibung, gruppiert nach Bereichen (Solution, Projekte, DbContext, Konfiguration, Docker, Tests, README).
2. **Annahmen** — wo Anforderungen uneindeutig waren und wie du entschieden hast (z.B. exakte Paket-Versionen, Geef-SDK-Einbindung, Naming-Konventionen).
3. **Abweichungen vom Plan** — falls vorhanden, mit Begründung. Sonst "Keine Abweichungen."
4. **Architect-Output** — Zusammenfassung der Phase-1.4-Konsultation: hat der Architect Lücken in `02-architecture.md` identifiziert? Hat er Konflikte mit dem Plan gefunden? Was wurde übernommen, was abgelehnt?
5. **Reviewer-Iterationen** — pro Iteration: Strategie (parallel/fail-fast/priority), Findings pro Reviewer (zusammengefasst), Iteration-Advisor-Erkenntnisse (ab Iter. 2), Aktionen, Begründung für abgelehnte Findings.
6. **Akzeptanzkriterien-Check** — pro Kriterium: ✓/✗/! mit Beleg (Test-Output, curl-Output, Screenshot).
7. **Lokaler Start** — kopierfertige Command-Sequenz vom frischen Clone bis zum laufenden System.
8. **Offene Punkte und Empfehlungen für Schritt 2** — was ist während der Arbeit aufgefallen, was sollte beim nächsten Schritt beachtet werden, gibt es Vorschläge für Anpassungen am `03-walking-skeleton-plan.md`?

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch** (Geef-SDK-Konvention).
- Doku, Bericht, Commits: **Deutsch** (Projekt-Konvention).
- Commits: fein-granulare Conventional Commits via GitHub/Git plugin (siehe Workflow Hard Rules).

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.