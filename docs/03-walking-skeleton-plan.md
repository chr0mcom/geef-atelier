# Walking Skeleton — Bauplan

*Letzte Aktualisierung: 11. Mai 2026 (Schritt 8 abgeschlossen — Cookie-Auth; 71/71 Tests grün)*

Das Walking Skeleton ist die kleinste end-to-end-funktionale Version von Geef.Atelier: ein Auftrag wird über die UI oder via MCP gestellt, eine echte Geef-Pipeline läuft (mit echten LLM-Calls), Live-Status ist sichtbar, das Ergebnis wird angezeigt und persistiert. Quellen-Upload, Klassifikator, dynamische Crew, Advisor, Multi-Format-Export — alles weitere kommt später.

## Strategie

Jeder Schritt ist einzeln verifizierbar. Kein Schritt setzt voraus, dass alles davor perfekt ist. Nach jedem Schritt sollte das System in einem testbaren Zustand sein. Schritte werden in Reihenfolge umgesetzt, weil jeder auf Vorgängern aufbaut — aber bei Bedarf darf gestoppt, refaktoriert oder neu gedacht werden, bevor der nächste startet.

---

## Parallele Migrations-Tracks

Während die nummerierten Schritte sequenziell durchlaufen werden, gibt es parallele Migrations-Tracks für strukturelle Umbauten, die durch Ereignisse außerhalb des Walking-Skeleton-Plans ausgelöst wurden. Sie laufen auf eigenen Branches und werden nicht automatisch in main gemerged — der Brainstorming-Maintainer entscheidet den Merge-Zeitpunkt.

### M1 — Provider-Migration auf OpenAI-konforme APIs

**Branch:** `feature/openai-compatible-providers`
**Status:** ✅ **Abgeschlossen am 10. Mai 2026.** Branch `feature/openai-compatible-providers` gepusht (4 Commits + 1 nachgereichter Bericht-Commit). 31/31 Tests grün (9 ohne Docker, 22 weitere via Postgres/Orchestrator-Testcontainer). Architect-Antworten zu allen sechs Schwerpunkten getroffen — markante Entscheidung: `LlmActor`-Enum existiert nur als Typen-Dokumentation, Lookup über String-Keys. Workflow-Abweichung: keine formalen R1–R5-Reviewer-Pässe (durch Subagent-Self-Reviews + Build/Test ersetzt) — R2-Nachholpass nach Merge empfohlen. Bericht: [reports/migration-01-report.md](reports/migration-01-report.md). Details siehe Decisions-Log D-018.
**Merge-Status:** ✅ **Abgeschlossen** (Push-Range `28daafb..ad90f65`). main enthält jetzt Schritte 1–7 + M1 zusammen. Branch `feature/openai-compatible-providers` kann gelöscht werden.
**Offen vor Schritt 7:** Real-OpenRouter-Integration-Test (`AtelierPipelineRunsAgainstOpenRouter`) einmal mit echtem Bearer-Key ausführen — verifiziert Modell-ID-Stabilität, Tool-Use-Verhalten, Latenz.
**Auslöser:** D-017 — Anthropic-OAuth-Token wird von Messages-API nicht akzeptiert; Pay-as-you-go-Bearer-Key vermeidbar; Multi-Provider-Vorteil sofort nutzbar.
**Scope:** Ersetzt anthropic-spezifischen LLM-Layer durch OpenAI-API-konformen Adapter (Default: OpenRouter). Pro-Akteur-Modell-Konfiguration. Tool-Use-Format wechselt auf OpenAI-`function`-Schema.
**Nicht im Scope:** Pipeline-Struktur, EventSink, Persistierung, Orchestrator, Domain-Modell.
**Empfohlener Merge-Zeitpunkt:** Vor Schritt 7 (UI), damit die UI direkt gegen die neuen Provider-Verträge gebaut wird.
**Prompt:** [prompts/migration-01-openai-compatible-providers.md](prompts/migration-01-openai-compatible-providers.md)

---

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

**Status:** ✅ **Abgeschlossen am 10. Mai 2026.** 1 Reviewer-Iteration, 2 MAJOR-Findings vor Phase 4 behoben (defensive JSON-Deserialisierung). 11/11 Tests grün (4 neue Mock-Tests + 7 Regression). Anthropic-Tool-Use mit `submit_review`, `Microsoft.Extensions.Http.Resilience` via `AddStandardResilienceHandler`, `ConvergenceFailedException` bei `AbortOnCritical=true` verifiziert. 14 Conventional-Commits. Bericht: [reports/step-03-report.md](reports/step-03-report.md). Details siehe Decisions-Log D-013.

**Offen:** Integration-Test `AtelierPipelineRealAnthropicTests` wurde nicht mit echtem API-Key ausgeführt — vor Schritt 5 nachholen.

**Hinweis:** Die in Schritt 3 etablierte Anthropic-spezifische LLM-Schicht wird durch Migration M1 (siehe oben) durch eine OpenAI-API-konforme Provider-Schicht ersetzt. Die Pipeline-Struktur und Convergence-Logik aus Schritt 3 bleiben unverändert; nur der Client-Adapter und die Konfigurations-Records ändern sich. Details siehe D-017 im Decisions-Log.

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
**Status:** ✅ **Abgeschlossen am 10. Mai 2026.** 1 Reviewer-Iteration, 1 MAJOR-Finding (volatile-Annotation für `_lastExecutionContext`) behoben. 15/15 Tests grün (4 neue Persistence-Tests + 11 Regression). PostgresEventSink mit Variante-A-RunId-Propagation, IRunPersistenceService in Core, typisiertes Token-Tracking via `ContextKey<AnthropicTokenUsage>`, Critical-Abort-Findings aus `PipelineFailedEvent.History` (SDK via Dekompilierung verifiziert). 13 Conventional-Commits. Bericht: [reports/step-04-report.md](reports/step-04-report.md). Details siehe Decisions-Log D-015.

**Offen (verschoben):** `AtelierPipelineRealAnthropicTests` mit echtem API-Bearer-Key — kein Key in Session-Umgebung verfügbar. Real-Lauf in Schritt 5 oder später, wenn Bearer-Key bereitgestellt wird.

---

### Schritt 5 — RunOrchestratorService

✅ **Abgeschlossen am 10. Mai 2026.** 1 Reviewer-Iteration, 6 Findings (alle behoben). Bericht: [docs/reports/step-05-report.md](reports/step-05-report.md). D-016.

**Ziel:** Asynchrone Auftragsverarbeitung über einen `BackgroundService`. Aufträge werden mit Status `Pending` in die DB geschrieben; der Service nimmt sie auf, führt die Pipeline aus, schreibt das Ergebnis zurück.

**Umfang:**
- `RunOrchestratorService : BackgroundService`
- Polling-Intervall (2 Sekunden Default) für `Pending`-Runs; atomarer `Pending→Running`-Claim
- `SemaphoreSlim`-Concurrency-Gate + Task-Tracking (`_runTasks`) mit Drain beim Stop
- Crash-Recovery beim Service-Start: alle `Running`-Runs → `Failed/"Service restarted"`
- Cancellation-Strategie γ: nur `StoppingToken`; `OverrideToAbortedAsync` mit `CancellationToken.None`
- `OrchestratorOptions` (PollingInterval, MaxConcurrentRuns) in `Core/Configuration/`
- `GatedFakeAnthropicClient` für deterministische Concurrency-Tests

**Akzeptanzkriterien:**
- ✅ Mehrere Runs nacheinander automatisch verarbeitet (E2E Pending→Completed)
- ✅ App-Restart markiert laufende Runs als Failed/"Service restarted"
- ✅ Nie mehr als MaxConcurrentRuns=2 Runs gleichzeitig (5/5 deterministisch)
- ✅ StopAsync mid-flight → Status=Aborted
- ✅ 19/19 Tests grün; AC8 Skip (OAuth-only)

**Status:** ✅ **Abgeschlossen am 10. Mai 2026.** 1 Reviewer-Iteration; 4 MAJOR R2 (Drain-Race, Test-Precondition-Guards) + 2 MAJOR R4 (Doku-Updates) — alle behoben. 19/19 Tests grün (4 neue Orchestrator-Tests + 15 Regression), Concurrency-Test 5/5 deterministisch via `GatedFakeAnthropicClient`. Atomarer Pending→Running-Claim, `SemaphoreSlim` + `ConcurrentDictionary<Guid, Task>` + `WhenAll`-Drain, Crash-Recovery beim Service-Start, Cancellation via Option γ (nur StoppingToken). 11 Conventional-Commits. Bericht: [reports/step-05-report.md](reports/step-05-report.md). Details siehe Decisions-Log D-016.

**Offen (verschoben):** AC8 (Real-Anthropic-Test mit Bearer-Key) — 3. Mal Skip wegen OAuth-only Token in Session. `CancelRunAsync` als Stub-Implementierung folgt in Schritt 6 zusammen mit DB-Flag-Migration.

---

### Schritt 6 — IRunService als Application-Service-Layer

**Ziel:** Saubere Anwendungslogik-Schicht, die von beiden Frontends (Web-UI und MCP-Server) konsumiert wird.

**Umfang:**
- `IRunService`-Interface in neuem `Geef.Atelier.Application`-Projekt (Option B, User bestätigt)
- Methoden: `SubmitRunAsync`, `GetRunAsync`, `ListRunsAsync`, `CancelRunAsync`
- `IRunRepository` in Core, `RunRepository` in Infrastructure (Variante β — keine Infra-Dep in Application)
- `RunEntity.CancellationRequested`-Flag + EF-Migration `Step06Cancellation`
- Cancellation-Watcher im Orchestrator (Pattern A, pro-Run, pollt DB jede `CancellationPollingInterval`)
- DI-Registrierung `AddAtelierApplication()` + `AddAtelierApplication()` in Program.cs

**Akzeptanzkriterien:**
- ✅ `SubmitRunAsync` + `GetRunAsync`: End-to-End Pending→Completed
- ✅ `ListRunsAsync`: sortiert nach `CreatedAt desc`, filterbar nach Status
- ✅ `CancelRunAsync` mid-flight: DB-Flag → Watcher → CTS → Pipeline-OCE → Aborted
- ✅ `CancelRunAsync` für terminalen Run: false (idempotent)
- ✅ Input-Validierung: leeres/null-Briefing, null-configJson, ungültiges JSON
- ✅ `dotnet test`: 31/31 grün (5 neue Application-Tests + 26 Regression)
- ✅ AC9: Skip — kein Live-API-Key in Session (Eskalations-Hinweis vor Schritt 9)

**Status:** ✅ **Abgeschlossen am 10. Mai 2026.** 2 Reviewer-Iterationen, 2 R2-MAJOR-Findings (ServiceProvider-Disposal, Test-Race) behoben. 31/31 Tests grün (5 neue Application-Tests). Variante β (Application-Layer ohne Infrastructure-Dep, IRunRepository in Core), Cancellation-Watcher Pattern A (pro-Run-Task), DB-Flag `RunEntity.CancellationRequested` mit Migration `Step06Cancellation`. 6 Conventional-Commits. Bericht: [reports/step-06-report.md](reports/step-06-report.md). Details siehe Decisions-Log D-019.
Details siehe Decisions-Log D-017 (Schritt-6-Abschnitt)

---

### Schritt 7 — Blazor-UI

**Status:** ✅ **Abgeschlossen am 11. Mai 2026.** 2 Reviewer-Iterationen, 1 R2-CRITICAL (fehlendes try/catch in `SignalRRunNotifier`) behoben — doppelter Fail-Safe-Pattern etabliert. 55/55 Tests grün (4 neue bUnit + 4 neue Playwright E2E + bestehende Persistence/Orchestrator/Application). Drei Pages (`/new`, `/runs`, `/runs/{id}`), 9 UI-Komponenten in `Components/UI/` mit scoped CSS, SignalR-Hub `RunHub` mit zwei Groups (`run-{id}` + `all-runs`), `IRunNotifier` in Core und `SignalRRunNotifier` in Web als Singleton. **AC8 endlich grün:** OpenRouter-Real-Pipeline mit 5–12s Latenz und 174–523 Tokens pro Run verifiziert. 12 Conventional-Commits in `main`. Bericht: [reports/step-07-report.md](reports/step-07-report.md). Details siehe Decisions-Log D-020.

**Workflow-Festlegung dieser Stufe:** Plan-Phase-Integration etabliert sich als Architect-Form (seit Schritt 5 verwendet); `geef_architecture.md` als Pflicht-Artefakt wird in der Praxis durch Plan-Dokumente äquivalent ersetzt — R4 prüft Architektur-Compliance gegen den Plan. Atelier-Auslegung der "keine HTML in Pages"-Regel: triviale Page-Steuerelemente (einfache `<button>`/`<div>` ohne State) dürfen in Pages bleiben, nur wiederverwendbare UI-**Logik** muss in `Components/UI/`.

---

### Schritt 8 — Auth (Cookie für UI, Token für MCP-Vorbereitung)

**Ziel:** Anwendung ist nicht mehr ungeschützt im Internet erreichbar.

**Voraussetzung:** Schritte 1–7 + M1 in main. AC8 (Real-OpenRouter-Test) grün. Schritt 8 baut auf der etablierten UI-Schicht (drei Pages + SignalR-Hub) auf und ergänzt Auth-Middleware + Login-Page. Single-User-Setup mit Cookie-basierter Auth.

**Umfang:**
- Cookie-Auth für die Web-UI; ein User aus Environment-Variablen
- Login-Page (Static SSR), Logout-Endpoint (`POST /auth/logout`)
- Bearer-Token-Auth-Schema vorbereitet für MCP-Server (im nächsten Schritt aktiviert)
- Gesundheitscheck bleibt unauthentifiziert

**Akzeptanzkriterien:**
- ✅ Ohne Login: Redirect auf Login-Page (`/login?ReturnUrl=…`)
- ✅ Mit Login (admin/DevPassword! als Dev-Default): alle UI-Routen erreichbar, Logout-Button sichtbar
- ✅ Falsche Credentials: Login schlägt fehl, "Ungültige Anmeldedaten"-Banner, kein Cookie
- ✅ Logout → Cookie gelöscht, folgende Auth-Routen redirigierten wieder zu /login
- ✅ `/health` weiterhin anonym (AllowAnonymous)
- ✅ `tools/HashPassword` CLI für BCrypt-Hash-Generierung
- ✅ 71/71 Tests grün (55 bestehende + 16 neue)

**Status:** ✅ **Abgeschlossen am 11. Mai 2026.** 4 Reviewer-Iterationen (R1–R5 alle 0 Findings). 71/71 Tests grün (55 Regression + 4 Application-Auth + 6 bUnit + 6 Playwright-E2E). Cookie-Auth: BCrypt wf=11, 30d SlidingExpiration, HttpOnly, SameSite=Strict, SecurePolicy Dev/Prod-Switch. Login als Static SSR (`@formname`-Pflicht). Logout via `POST /auth/logout` mit AntiforgeryToken. `TestAuthenticationHandler` in Tests für Bypass. Arch-Trade-off: RunHub ohne `[Authorize]` (Blazor Server server-side HubConnection kann Browser-Cookies nicht forwarden — SSR-Pre-render würde 401 erhalten). `ForwardedHeaders`-Middleware vor `UseAuthentication` (Traefik-TLS-Vorbereitung). 13 Conventional-Commits. Bericht: [reports/step-08-report.md](reports/step-08-report.md). Details siehe Decisions-Log D-021.

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