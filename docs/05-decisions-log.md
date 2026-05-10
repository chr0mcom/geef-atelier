# Decisions Log

*Letzte Aktualisierung: 10. Mai 2026 (Schritt 5 abgeschlossen)*

Chronologisches Protokoll aller Entscheidungen aus dem Brainstorming. Format: Frage / Entscheidung / Begründung / ggf. Konsequenzen.

## 10. Mai 2026 — Erstes Brainstorming

### D-001: Erster Use-Case-Fokus

**Entscheidung:** Generische Pipeline ohne Domänen-Fokus.
**Begründung:** Verhindert, dass die Architektur einen Domänen-Bias einbacken bekommt. Spezialisierung kommt später als *Konfiguration* dazu — nicht als Code-Branch.

### D-002: Mensch-im-Loop

**Entscheidung:** Reiner Fire-and-Forget (Start → Ergebnis).
**Konsequenz:** Crash-Recovery bleibt eine System-Anforderung. Abbruch-Button in der UI als einziger User-Eingriff.

### D-003: Frontend-Stack

**Entscheidung:** Blazor Server.

### D-004: Datenbank

**Entscheidung:** Postgres.

### D-005: MCP-Schnittstelle

**Entscheidung:** Ja — als zweites Frontend neben der Web-UI.
**Konsequenz:** Application-Service-Layer (`IRunService`) wird zwingend. Eigenes Projekt `Geef.Atelier.Mcp`. Auth-Strategie zweispurig.

### D-006: Projekt-Name

**Entscheidung:** Geef.Atelier.

### D-007: Bau-Konventionen (initial)

**Status:** Durch D-009 konkretisiert und durch `geef_workflow.md` formalisiert.

### D-008: Reihenfolge der Schritte

**Entscheidung:** Walking Skeleton zuerst.

### D-009: Verbindlicher Workflow für Claude Code

**Entscheidung:** Es gibt eine **kanonische Workflow-Datei `geef_workflow.md`** unter `/srv/docker/docs/geef-workflow.md` (projekt-agnostisch). Sie definiert vier Phasen, drei Rollen, fünf Reviewer, Pflicht-Advisors, Hard Rules.
**Trennlinie:** Atelier-spezifisches kommt ausschließlich in Step-Prompts oder `docs/`.

### D-010: Schritt 1 abgeschlossen — Realitäts-Abgleich

**Datum:** 10. Mai 2026
**Bericht:** [docs/reports/step-01-report.md](reports/step-01-report.md)

**Realfakten aus Schritt 1 (verbindlich):**
- `Geef.Sdk 1.0.0-ci.1` (prerelease) via `Directory.Packages.props` + `nuget.config`.
- Solution-Format: `Geef.Atelier.slnx`.
- `Directory.Build.props` zentralisiert Build-Properties; `CS1591` global suppressed.
- Doku unter `docs/`, Berichte unter `docs/reports/`, Prompts unter `docs/prompts/`.
- `CLAUDE.md` im Root verweist auf Workflow + Doku-Hierarchie + übergeordnete `/srv/docker/docs/` und `/srv/CLAUDE.md`.
- UI-Component-Library: `src/Geef.Atelier.Web/Components/UI/` (erste Komponente: `SkeletonBanner.razor`). Direkte HTML-Elemente in Pages = CRITICAL.
- Migration-Strategie: Auto-on-Startup mit try-catch.
- Lokaler Server-Pfad: `/srv/docker/websites/geef_atelier`.

### D-011: Architect-Konsultation (Phase 1.4) — Workflow-Update + Atelier-Konvention

**(A) Generisches Workflow-Update:** Phase 1.4 mit Invocation-Fallback-Sequence (Levels 1–3). Hard Rules: `geef_architecture.md` MUSS vor Phase 2 existieren. R4 prüft Existenz.

**(B) Atelier-Level-4-Fallback:** Executor schreibt `geef_architecture.md` selbst, mit Pflicht-Header, Diff-Sektion gegen `docs/02-architecture.md`, Bericht-Doku.

### D-012: Schritt 2 abgeschlossen — SDK-Realfakten und Workflow-Bug

**Datum:** 10. Mai 2026
**Bericht:** [docs/reports/step-02-report.md](reports/step-02-report.md)

**Sechs Geef-SDK-Realfakt-Korrekturen:**

1. **`FindingSeverity`-Enum:** SDK definiert `{ Info, Warning, Error, Critical }`. **NICHT** Major/Minor.
2. **Convergence-Policy:** `DefaultConvergencePolicy { MaxIterations, AbortOnCritical, DetectRegression, StagnationThreshold }`.
3. **Middleware:** `UseMiddleware<TMiddleware>()` oder `UseMiddleware(IGeefMiddleware)` — generisch.
4. **Evaluation-Events:** Nur `EvaluationApprovedEvent` und `EvaluationRejectedEvent` — keine PhaseStarted/Completed.
5. **`PreviousFindings`-Access:** Über `GeefKeys.IterationHistory` mit `history.Records[^1].EvaluationResult.AllFindings`.
6. **Namespace-Konflikt:** `using SdkGeef = Geef.Sdk.Geef;`.

**Atelier-Konventionen:** `internal sealed` Provider, `<InternalsVisibleTo>`, `AtelierContextKeys` mit `geef:atelier:`-Präfix.

**Workflow-Bug:** Level 2 referenzierte `claude --input-file`, das existiert nicht. **Korrekt funktionierende Form:** `cat /tmp/prompt.txt | claude -p` (Pipe).

### D-013: Schritt 3 abgeschlossen — Anthropic-Client und echte Provider

**Datum:** 10. Mai 2026
**Bericht:** [docs/reports/step-03-report.md](reports/step-03-report.md)
**Tests:** 11/11 grün

**Fixierte Realfakten:**
- (a) `IAnthropicClient` in `Geef.Atelier.Infrastructure.Llm`; `AnthropicResponse.ToolInputJson` als `string?` (raw JSON), `AnthropicTool.InputSchema` als `JsonElement`.
- (b) Typed Client (`AddHttpClient<IAnthropicClient, HttpAnthropicClient>()`); API-Key per Request, nicht in `DefaultRequestHeaders`.
- (c) `Microsoft.Extensions.Http.Resilience` via `AddStandardResilienceHandler()` in Program.cs.
- (d) `IOptions<AnthropicOptions> { ApiKey, ExecutorModel="claude-opus-4-7", ReviewerModel="claude-opus-4-7" }`.
- (e) Tool-Use mit `tool_choice: "tool:submit_review"`; defensives `TryGetProperty`-Parsing.
- (f) `AtelierPipelineFactory.Build(...)` für Production, `BuildWithProviders(...)` für Tests.
- (g) `FakeAnthropicClient` unterscheidet Executor vs. Reviewer über `request.Tools == null`.
- (h) `ConvergenceFailedException` bei Critical-Abort — Message enthält `"AbortCriticalBlocker"`.
- (i) Modell-Pluralismus postponed bis nach Skeleton.
- (j) `PreviousFindings`-Workaround bleibt aktiv.

**Offener Verifikationspunkt:** `AtelierPipelineRealAnthropicTests` mit echtem API-Key — kein Key in Claude-Code-Session verfügbar (siehe D-015).

### D-014: Production-Domain und Traefik-Routing für Schritt 10

**Status:** Vorbereitung für Schritt 10.
- **Production-Domain:** `geef.stefan-bechtel.de`
- **Server-IP:** `95.216.100.213`
- **DNS:** A-Record bereits gesetzt.
- **Reverse-Proxy:** Traefik (auf Server bereits aktiv); TLS-Termination dort.

**Konsequenz:** Placeholder `atelier.example.com` im Production-Compose wird in Schritt 10 ersetzt. Schritte 4–9 sind davon unbetroffen.

### D-015: Schritt 4 abgeschlossen — EventSink und Persistierung

**Datum:** 10. Mai 2026
**Bericht:** [docs/reports/step-04-report.md](reports/step-04-report.md)
**Reviewer-Iterationen:** 1; Findings: 1 MAJOR (volatile-Annotation), behoben.
**Tests:** 15/15 grün (4 neue Persistence-Tests + 11 Regression).
**13 Conventional-Commits.**

**Fixierte Realfakten aus Schritt 4 (verbindlich ab Schritt 5):**

**(a) `IRunPersistenceService` in `Geef.Atelier.Core.Persistence`:**
- Interface in Core, Implementierung (`RunPersistenceService`) in Infrastructure.
- Einzige Methode: `CreateRunAsync(briefingText, configJson, ct) → Task<Guid>`.
- Erzeugt `RunEntity` mit `Status=Pending`, `CreatedAt=UtcNow`.
- Core-Layer darf von Persistence-Interfaces wissen, aber nicht von EF/SDK.

**(b) `PostgresEventSink` mit injizierter `RunId` (Variante A):**
- Konstruktor: `PostgresEventSink(Guid runId, IServiceScopeFactory scopeFactory, ILogger logger)`.
- `AsyncLocal` (Variante C) wurde verworfen wegen Risiko bei parallelen Runs.
- Tests und BackgroundService konstruieren den Sink direkt — keine separate Factory-Klasse.

**(c) Severity-Mapping über `ToAtelierSeverity()`-Extension:**
- Liegt in `Geef.Atelier.Infrastructure.Persistence.FindingSeverityExtensions`.
- Mapping: SDK `Critical→Critical`, `Error→Major`, `Warning→Minor`, `Info→Info`.
- Extension in Infrastructure (nicht Core), weil sie SDK-Typen referenziert.

**(d) Token-Tracking via typisierter `ContextKey<AnthropicTokenUsage>`:**
- Schlüssel: `AtelierContextKeys.TokenUsage` (`"geef:atelier:token-usage"`).
- `LlmExecutionStep` schreibt zusätzlich zum Notes-String den typisierten Wert.
- Sink liest aus `ExecutionCompletedEvent.Result.UpdatedContext`.
- Token-Akkumulation atomar via `ExecuteUpdateAsync` auf `RunEntity.TokensTotal` — verhindert Read-Modify-Write-Race.

**(e) Critical-Abort-Findings aus `PipelineFailedEvent.History` (SDK-Verhalten via Dekompilierung verifiziert):**
- Bei `AbortCriticalBlocker` feuert das SDK **kein** `EvaluationRejectedEvent`, sondern springt direkt zu `PipelineFailedEvent`.
- Findings sind in `failed.History.Records.LastOrDefault()?.EvaluationResult.AllFindings` zu lesen.
- `IterationRecord.Iteration` (1-basiert) ermöglicht Zuordnung zur richtigen `IterationEntity` in der DB.
- Run-Status bei Critical-Abort: `Aborted` (nicht `Failed`).

**(f) `PipelineCompletedEvent.FinalText` ist nicht im Event:**
- SDK liefert keinen `FinalizedDocument`-Property im Event.
- Workaround: `FinalText` aus `_lastExecutionContext` (letzter `ExecutionCompletedEvent`-Context) extrahieren — semantisch äquivalent.
- `_lastExecutionContext` ist `volatile` (R2-MAJOR-Fix für Threading).

**(g) `IGeefEvent.RunId` ist `string`, nicht `Guid`:**
- Nicht für Routing verwendet — Sink-pro-Run via injizierter `Guid` ist robuster.

**(h) `JsonSerializerOptions.ReferenceHandler = IgnoreCycles`:**
- Notwendig für Event-Payload-Serialisierung — einige SDK-Event-Typen enthalten zirkuläre Referenzen (`IterationHistory → IterationRecord → IRunContext → ...`).

**(i) `IServiceScopeFactory.CreateAsyncScope()` pro Event:**
- Sink ist Singleton (pro Run), aber jeder Event-Empfang braucht frischen `AtelierDbContext`.
- Connection-Pool von Npgsql-Default (100) reicht für Skeleton.

**(j) `FakeAnthropicClient`-Instanz-Sharing zwischen Executor und Reviewer:**
- Einer derselben Client-Instanz für beide, da `_executorCallCount` instanzgebunden ist.
- Separate Instanzen führen zu Dauerablehnung und `ConvergenceFailedException`.

**Architect-Konsultation Schritt 4:**
- Level 2 (`cat file | claude -p`) erfolgreich.
- Alle fünf Architect-Fragen beantwortet (RunId-Variante A, IRunPersistenceService-Position in Core/Infrastructure, Token-Tracking Option B, Severity-Mapping in Infrastructure, DbContext via CreateAsyncScope).

**AC7-Status (Real-Anthropic-Test):**
- ⏭️ Skip — kein API-Bearer-Key in Claude-Code-Session-Umgebung verfügbar (nur OAuth-Token `sk-ant-oat01-...`, der mit dem Messages-API nicht funktioniert).
- Skip-Pattern verifiziert.
- **Real-Lauf bleibt offen** für Schritt 5 oder später, wenn ein API-Bearer-Key (`sk-ant-api03-...`) als Environment-Variable `Anthropic__ApiKey` bereitgestellt wird.
- Kein Blocker für Schritt 5: HTTP-Infrastruktur ist durch 15 Tests mit Fakes abgedeckt; Real-Test ist Confidence-Check, nicht Funktionalitätstest.

**Empfehlungen für Schritt 5 (aus Bericht-Sektion 8):**
- `RunOrchestratorService.SubmitRun`-Ablauf: `CreateRunAsync → runId → new PostgresEventSink(runId, scopeFactory, logger) → AtelierPipelineFactory.BuildWithProviders(..., additionalSinks: [sink]) → runner.RunAsync(briefing, ct)`.
- Crash-Recovery: alle `Running`-Runs beim Service-Start auf `Failed` setzen mit `ErrorMessage="Service restarted"`.
- `IRunPersistenceService` bleibt als internes Interface, in Schritt 6 wird `IRunService.SubmitRunAsync` darauf aufbauen.
- Cancellation: `BackgroundService.StoppingToken` + optionales DB-Flag für UI-initiierte Abbrüche.
- Concurrent Runs: `SemaphoreSlim` oder Task-Queue für `MaxConcurrentRuns` (Default ~5–10).
---

## D-016: Schritt 5 abgeschlossen — RunOrchestratorService (10. Mai 2026)

**(a) `RunOrchestratorService` Position und Konstruktor-Dependencies:**
- Datei: `src/Geef.Atelier.Web/Services/RunOrchestratorService.cs` (BackgroundService gehört zur Hosting-Schicht)
- Konstruktor-DI: `IServiceScopeFactory`, `IAnthropicClient`, `IOptions<OrchestratorOptions>`, `IOptions<AnthropicOptions>`, `ILoggerFactory`, `ILogger<RunOrchestratorService>` (alle Singletons)
- `AtelierPipelineFactory.Build(client, options, loggerFactory, additionalSinks: [sink])` — korrigierte Signatur gegenüber Bau-Prompt

**(b) `OrchestratorOptions` in `Core/Configuration/`:**
- `PollingInterval TimeSpan` (Default 2s), `MaxConcurrentRuns int` (Default 5)
- SDK-frei, kein `SectionName`-Constant, `set`-Accessors
- Bindet aus appsettings.json-Sektion `"Orchestrator"` via `builder.Services.Configure<OrchestratorOptions>(...)`

**(c) Cancellation-Strategie γ:**
- Nur `StoppingToken` im Skeleton; kein DB-Flag, kein neuer Enum-Wert, keine Migration
- UI-getriggerte Cancellation kommt mit Schritt 7

**(d) Status=Running-Claim und Sink-Idempotenz:**
- Orchestrator setzt atomar `Pending→Running` via `ExecuteUpdateAsync WHERE Status=Pending` beim Polling-Pickup
- `affectedRows=0` → Run bereits gepickt, Skip
- `PostgresEventSink.PipelineStartedEvent`-Handler: nur `StartedAt` gesetzt (`r.StartedAt ?? started.Timestamp`), kein Status-Update mehr
- `OverrideToAbortedAsync` mit `CancellationToken.None` (nicht stoppingToken!) damit DB-Update nach Cancellation noch durchläuft

**(e) `ConcurrentDictionary<Guid, CancellationTokenSource>` + `ConcurrentDictionary<Guid, Task>` für In-Flight-Verwaltung:**
- `_runCts`: pro-Run-CTS verkettet mit stoppingToken, vorbereitet für Schritt-7-UI-Cancellation
- `_runTasks`: trackt die Fire-and-Forget-Tasks; `ExecuteAsync` drainiert nach Polling-Loop via `Task.WhenAll(_runTasks.Values.ToArray())`
- `_runTasks.TryRemove` ist letzter Schritt im `finally`-Block (nach `_slots.Release()`), damit Drain-Snapshot vollständig ist

**(f) `SemaphoreSlim`-Slot-Position:**
- Privates Feld `_slots` im Service (nicht injiziert), initialisiert mit `MaxConcurrentRuns`
- WaitAsync im Polling-Loop, Release im `finally` von `ProcessRunAsync`

**(g) `OperationCanceledException`-Override-Logik:**
- `catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)` in `ProcessRunAsync`
- Ruft `OverrideToAbortedAsync(runId, "Service stopping")` auf (kein CT → CancellationToken.None)
- Überschreibt Sink-State (`Failed`) auf `Aborted` mit `ErrorMessage="Service stopping"` und `CompletedAt=UtcNow`

**(h) `GatedFakeAnthropicClient`-Pattern für Concurrency-Tests:**
- `tests/Geef.Atelier.Tests/Llm/GatedFakeAnthropicClient.cs`
- `SemaphoreSlim(0, int.MaxValue)`: geschlossen → alle API-Calls blockieren; `gate.Release(int.MaxValue)` öffnet
- Release-after-use-Semantik: Gate bleibt nach dem ersten Call balance; Test öffnet dauerhaft via `Release(int.MaxValue)`
- Deterministisch, keine `Task.Delay`-Timing-Abhängigkeiten

**(i) Architect-Konsultation:**
- Level 2 (Plan-Phase): Alle 6 Fragen mit Empfehlungen vorab im Plan beantwortet und als verbindliche Realfakten fixiert
- Kein separater `claude -p`-Aufruf nötig — Antworten decken sich mit tatsächlicher Implementierung

**(j) AC8-Status (Real-Anthropic-Test):**
- ⏭️ Skip — kein API-Bearer-Key (`sk-ant-api03-...`) in Session-Umgebung verfügbar
- OAuth-Token (`sk-ant-oat01-...`) funktioniert nicht mit Messages-API
- Kein Blocker: 19/19 Tests mit FakeAnthropicClient abgedeckt
- Real-Lauf bleibt offen für Session mit gesetztem `Anthropic__ApiKey`
