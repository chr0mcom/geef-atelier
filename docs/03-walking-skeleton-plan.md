# Walking Skeleton — Bauplan

*Letzte Aktualisierung: 10. Mai 2026 (Schritte 1–3 abgeschlossen)*

Das Walking Skeleton ist die kleinste end-to-end-funktionale Version von Geef.Atelier: ein Auftrag wird über die UI oder via MCP gestellt, eine echte Geef-Pipeline läuft (mit echten LLM-Calls), Live-Status ist sichtbar, das Ergebnis wird angezeigt und persistiert. Quellen-Upload, Klassifikator, dynamische Crew, Advisor, Multi-Format-Export — alles weitere kommt später.

## Strategie

Jeder Schritt ist einzeln verifizierbar. Kein Schritt setzt voraus, dass alles davor perfekt ist. Nach jedem Schritt sollte das System in einem testbaren Zustand sein. Schritte werden in Reihenfolge umgesetzt, weil jeder auf Vorgängern aufbaut — aber bei Bedarf darf gestoppt, refaktoriert oder neu gedacht werden, bevor der nächste startet.

## Die zehn Schritte

### Schritt 1 — Solution-Setup mit Postgres und EF Core

**Ziel:** Lauffähige Solution mit allen Projekten, Postgres-Anbindung über Npgsql und EF Core, erste Migration angelegt, Docker-Compose für lokale Entwicklung.

**Umfang:**
- Solution `Geef.Atelier.sln` mit vier Projekten (Core, Infrastructure, Web, Mcp) plus Tests
- Geef SDK referenziert (NuGet wenn verfügbar, sonst dokumentiert wie eingebunden)
- DbContext mit den vier Entities (Runs, Iterations, Findings, Events)
- Migration angelegt, gegen lokale Postgres ausführbar
- `docker-compose.yml` für lokale Entwicklung mit App + Postgres
- Health-Check-Endpoint
- README im Repo

**Akzeptanzkriterien:**
- `dotnet build` ohne Fehler oder Warnungen über Skeleton-Code
- `dotnet ef database update` läuft erfolgreich gegen Postgres
- `docker compose up` startet App und DB; Health-Check antwortet 200 OK
- Tests-Projekt enthält mindestens einen Smoke-Test (DbContext lädt, Migration läuft in Test-DB)

**Status:** ✅ **Abgeschlossen am 10. Mai 2026.** 1 Reviewer-Iteration, alle 5 Reviewer durch (1 CRITICAL + 4 MAJOR Findings, alle behoben). 9 Conventional-Commits. Bericht: [reports/step-01-report.md](reports/step-01-report.md). Details siehe Decisions-Log D-010.

---

### Schritt 2 — Pipeline-Skelett mit Stub-Providern

**Ziel:** Geef-Pipeline läuft mit ausgeklügelten Stub-Providern, ohne echte LLM-Calls. Beweist, dass Convergence-Loop und EventSink funktionieren.

**Umfang:**
- `BriefingGroundingStep` (Stub: Briefing in Context schreiben)
- `LlmExecutionStep` (Stub: Echo + Iterations-Marker)
- Zwei `LlmReviewer` (Stub: Iteration 1 = Findings, Iteration 2+ = keine Findings)
- `MarkdownFinalizer`
- Pipeline-Builder-Konfiguration mit `MaxIterationsPolicy(3)` und `ParallelEvaluationStrategy`
- Einfacher Konsolen-Test, der die Pipeline einmal ausführt

**Akzeptanzkriterien:**
- Pipeline läuft 2 Iterationen und konvergiert
- Alle Geef-Events werden in der Konsole geloggt
- Final-Output enthält den erwarteten Marker

**Status:** ✅ **Abgeschlossen am 10. Mai 2026.** 1 Reviewer-Iteration, alle 5 Reviewer mit 0 aktionierbaren Findings durch. 7/7 Tests grün (5 Schritt-1-Tests + 2 neue Pipeline-Tests). 6 SDK-Realfakt-Korrekturen vs. Bau-Prompt (FindingSeverity-Enum, DefaultConvergencePolicy, UseMiddleware-Generic, Evaluation-Event-Namen, IterationHistory-Workaround, Namespace-Alias). Bericht: [reports/step-02-report.md](reports/step-02-report.md). Details siehe Decisions-Log D-012.

---

### Schritt 3 — Anthropic-Client und echte Provider

**Ziel:** Stubs ersetzen durch echte Anthropic-API-Aufrufe.

**Umfang:**
- `IAnthropicClient` mit `CompleteAsync(systemPrompt, userPrompt, options)`
- HTTP-Implementierung gegen `/v1/messages`
- API-Key aus `IConfiguration` (`ANTHROPIC_API_KEY`)
- `LlmExecutionStep` ruft Anthropic mit Executor-System-Prompt + Briefing + PreviousFindings
- `LlmReviewer` ruft Anthropic mit Reviewer-System-Prompt + Artefakt; Reviewer-Output als JSON-strukturiert
- `ReviewerResponseSchema` definieren (findings: [{severity, message}])

**Akzeptanzkriterien:**
- Pipeline läuft mit echtem Anthropic-Modell und konvergiert (mit einem trivialen Briefing)
- Token-Verbrauch wird erfasst und im Final-Output ausgewiesen
- Strukturierter Reviewer-Output wird korrekt geparst

**Status:** ✅ **Abgeschlossen am 10. Mai 2026.** 1 Reviewer-Iteration, alle 5 Reviewer mit 2 aktionierbaren Findings durch (beide R2 MAJOR-Findings vor Phase 4 behoben: defensive JSON-Deserialisierung). 11/11 Tests grün (4 neue Mock-Tests + 7 Regression). Bericht: [reports/step-03-report.md](reports/step-03-report.md). Details siehe Decisions-Log D-013.

---

### Schritt 4 — EventSink und Persistierung

**Ziel:** Jeder Run wird mit allen Iterationen, Findings und Events in Postgres gespeichert.

**Umfang:**
- `PostgresEventSink` (Implementierung von `IGeefEventSink`)
- Iterations-Snapshots werden bei `ExecutionPhaseCompleted` extrahiert und persistiert
- Findings werden bei `EvaluationPhaseCompleted` persistiert
- Token- und Kosten-Akkumulation pro Run

**Akzeptanzkriterien:**
- Nach einem Pipeline-Run sind in der DB: ein Run, mehrere Iterations, Findings (für die Iterationen, in denen welche gefunden wurden), und ein vollständiger Event-Log
- Kein doppeltes Event, keine verlorene Iteration

---

### Schritt 5 — RunOrchestratorService

**Ziel:** Asynchrone Auftragsverarbeitung über einen `BackgroundService`. Aufträge werden mit Status `Pending` in die DB geschrieben; der Service nimmt sie auf, führt die Pipeline aus, schreibt das Ergebnis zurück.

**Umfang:**
- `RunOrchestratorService : BackgroundService`
- Polling-Intervall (z.B. 2 Sekunden) für `Pending`-Runs
- Status-Übergänge: `Pending` → `Running` → `Completed` / `Failed`
- Crash-Recovery (naive Variante): Beim Service-Start alle `Running`-Runs auf `Failed` setzen mit Error "Service restarted"
- Cancellation-Token-Verkettung: Run kann via DB-Flag (`CancellationRequested`) abgebrochen werden

**Akzeptanzkriterien:**
- Mehrere Runs nacheinander automatisch verarbeitet
- App-Restart bricht laufende Runs sauber ab und markiert sie als Failed

---

### Schritt 6 — IRunService als Application-Service-Layer

**Ziel:** Saubere Anwendungslogik-Schicht, die von beiden Frontends (Web-UI und MCP-Server) konsumiert wird.

**Umfang:**
- `IRunService`-Interface in Core
- Implementierung in Web (oder eigenes Application-Projekt)
- Methoden: `SubmitRunAsync`, `GetRunStatusAsync`, `GetRunResultAsync`, `ListRunsAsync`, `CancelRunAsync`
- DI-Registrierung
- Repository-Pattern für DB-Zugriff (ein einfaches `IRunRepository` reicht)

**Akzeptanzkriterien:**
- Service ist über DI in Tests aufrufbar
- Tests für jeden Service-Methoden-Call (gegen Test-DB oder InMemory)

---

### Schritt 7 — Blazor-UI

**Ziel:** Drei UI-Seiten für den Workflow.

**Umfang:**
- `/new` — Auftragsformular (Briefing-Textarea, Modell-Auswahl, Submit)
- `/runs` — Run-Liste mit Status-Badge, Auto-Refresh via SignalR
- `/runs/{id}` — Detail-Seite mit Briefing, Iterations-Akkordeon (jede Iteration zeigt Artefakt + Findings), Live-Event-Log, Final-Text bei Status=Completed, Abbruch-Button bei Status=Running
- `IRunService` wird per DI in die Pages injiziert
- SignalR-Hub für Live-Events; UI registriert sich pro Run-Detail-View

**Akzeptanzkriterien:**
- Auftrag stellen und live durchlaufen sehen, ohne Page-Reload
- Liste aktualisiert sich live wenn neue Runs entstehen oder ihren Status wechseln
- Abbruch-Button bricht Run sauber ab

---

### Schritt 8 — Auth (Cookie für UI, Token für MCP-Vorbereitung)

**Ziel:** Anwendung ist nicht mehr ungeschützt im Internet erreichbar.

**Umfang:**
- Cookie-Auth für die Web-UI; ein User aus Environment-Variablen
- Login-Page, Logout-Endpoint
- Bearer-Token-Auth-Schema vorbereitet für MCP-Server (im nächsten Schritt aktiviert)
- Gesundheitscheck bleibt unauthentifiziert

**Akzeptanzkriterien:**
- Ohne Login: Redirect auf Login-Page
- Mit Login: alle UI-Routen erreichbar
- Falsche Credentials: Login schlägt fehl, kein Cookie

---

### Schritt 9 — MCP-Server

**Ziel:** Zweiter Frontend-Adapter neben der Web-UI. Externe MCP-Clients (Claude Desktop, Claude Code, eigene Agenten) können Aufträge stellen, Status abfragen, Ergebnisse abholen.

**Umfang:**
- `Geef.Atelier.Mcp` als ASP.NET-Core-Host
- Verwendung des offiziellen [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- Transport: Streamable HTTP (Standard für moderne MCP-Server)
- Tools:
  - `submit_request(briefing, options?)` → returns `run_id`
  - `get_run_status(run_id)` → returns `{status, current_phase, iteration, tokens_used, cost}`
  - `get_run_result(run_id)` → returns `{final_text}` (nur bei Status=Completed)
  - `list_runs(limit?, status_filter?)` → returns `[run_summaries]`
  - `get_run_details(run_id)` → returns `{iterations, findings, events}`
  - `cancel_run(run_id)` → returns `{success}`
- Auth über Bearer-Token (siehe Schritt 8)
- Alle Tools rufen `IRunService` (kein direkter DB-Zugriff)

**Akzeptanzkriterien:**
- MCP-Inspector kann sich verbinden und alle Tools auflisten
- Auftrag via MCP stellen, parallel in der Web-UI live mitverfolgen
- Ergebnis via MCP abholen entspricht dem in der UI

---

### Schritt 10 — Dockerfile und Compose-Setup für Production

**Ziel:** Deploybar auf dem Zielserver.

**Umfang:**
- Multi-Stage `Dockerfile` (.NET 10 SDK build → ASP.NET Core 10 runtime)
- Non-root User im Container
- Healthcheck im Image
- `docker-compose.prod.yml` für Production: nur die App, Postgres-Connection-String zeigt auf existierende Server-Postgres-Instanz
- Environment-Variable-Dokumentation (`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `OPENROUTER_API_KEY`, `POSTGRES_CONNECTION_STRING`, `ATELIER_USER`, `ATELIER_PASSWORD_HASH`, `ATELIER_MCP_TOKEN`)
- Migration läuft beim Container-Start automatisch (oder als Init-Container/Migrations-Job)

**Akzeptanzkriterien:**
- Container baut und startet ohne manuelle Eingriffe
- App verbindet sich mit der bestehenden Postgres-Instanz
- Health-Check und Auth funktionieren hinter Reverse-Proxy

---

## Was bewusst NICHT im Skeleton ist

Damit der Scope klar bleibt:

- **Quellen-Upload und RAG** (kommt mit pgvector in einem späteren Schritt)
- **Klassifikator und dynamische Crew-Composition** (Crew ist im Skeleton fest verdrahtet)
- **Multi-Provider-Adapter** für OpenAI und OpenRouter (Skeleton nutzt nur Anthropic; Reviewer können dasselbe Anbieter-API mit anderem Modell nutzen)
- **Advisor-Integration** (Skeleton nutzt Geef-Advisor-Pattern noch nicht)
- **Echtes Crash-Resume** mit Wiederaufsatz an der letzten abgeschlossenen Phase (Skeleton macht naives Failed-Markieren)
- **Cost-Budget-Caps** mit Abbruch bei Überschreitung
- **Export nach DOCX/PDF** (Skeleton liefert Markdown)
- **Crew-Templates und Reviewer-Profile** als versionierte Daten (Skeleton hat zwei hartkodierte Reviewer)
- **OAuth-2.0** für MCP (Skeleton nutzt Bearer-Token)

## Wo

Im File `docs/03-walking-skeleton-plan.md`, **im Abschnitt "Schritt 1 — Solution-Setup mit Postgres und EF Core"**, ganz am Ende des Schritt-1-Blocks (direkt vor dem `---`-Trenner zu Schritt 2).

## Was ersetzen

**Alte Zeile:**
```markdown
**Status:** Prompt vorbereitet (siehe [prompts/step-01-solution-setup.md](prompts/step-01-solution-setup.md))
```

**Neuer Block:**
```markdown
**Status:** ✅ **Abgeschlossen am 10. Mai 2026.** 1 Reviewer-Iteration, alle 5 Reviewer durch (1 CRITICAL + 4 MAJOR Findings, alle behoben). 9 Conventional-Commits. Bericht: [reports/step-01-report.md](reports/step-01-report.md). Details siehe Decisions-Log D-010.
```

## Zusätzlich

Im Header der Datei das Datum aktualisieren:
```markdown
*Letzte Aktualisierung: 10. Mai 2026 (Schritt 1 abgeschlossen)*