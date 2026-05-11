# Decisions Log

*Letzte Aktualisierung: 11. Mai 2026 (Schritt 7 abgeschlossen)*

Chronologisches Protokoll aller Entscheidungen aus dem Brainstorming.

## 10. Mai 2026 — Erstes Brainstorming

### D-001 bis D-009 (kondensiert)

Use-Case-Fokus generisch (D-001), Fire-and-Forget (D-002), Blazor Server (D-003), Postgres (D-004), MCP zweite Schnittstelle (D-005), Projekt-Name Geef.Atelier (D-006), kanonische `geef_workflow.md` (D-009), Walking Skeleton zuerst (D-008).

### D-010: Schritt 1 — Solution-Setup
Geef.Sdk 1.0.0-ci.1, `.slnx`, UI-Component-Library, Auto-Migration mit try-catch.

### D-011: Architect-Workflow-Update + Atelier-Fallback
(A) Phase 1.4 mit Fallback-Sequence; R4 prüft `geef_architecture.md`-Existenz. (B) Atelier-Level-4-Fallback.

### D-012: Schritt 2 — SDK-Realfakten
Sechs Korrekturen: SDK-`FindingSeverity { Info, Warning, Error, Critical }`; `DefaultConvergencePolicy`; `UseMiddleware<T>()`; nur `EvaluationApprovedEvent`/`RejectedEvent`; `IterationHistory`-Workaround; `using SdkGeef = Geef.Sdk.Geef;`.

### D-013: Schritt 3 — Anthropic-Client (in M1 ersetzt durch ILlmClient)
Realfakten zur ursprünglichen Anthropic-Schicht. Konzepte (Tool-Use, defensive JSON, API-Key per Request, Resilience) bleiben in M1 gültig — nur Adapter ändert sich.

### D-014: Production-Domain für Schritt 10
`geef.stefan-bechtel.de`, IP `95.216.100.213`, Traefik mit TLS auf Server.

### D-015: Schritt 4 — EventSink und Persistierung
`IRunPersistenceService` in Core, `PostgresEventSink` mit injizierter `Guid runId`, Severity-Mapping via `ToAtelierSeverity()`, Token-Tracking via typisierter Context-Key, Critical-Abort-Findings aus `PipelineFailedEvent.History.Records[^1].EvaluationResult.AllFindings` (SDK-Dekompilierung), `_lastExecutionContext` als `volatile`, `IServiceScopeFactory.CreateAsyncScope()` pro Event.

### D-016: Schritt 5 — RunOrchestratorService
Atomarer Pending→Running-Claim, `SemaphoreSlim` + `ConcurrentDictionary<Guid, Task>` + `WhenAll`-Drain, `OverrideToAbortedAsync` mit `CancellationToken.None`, `_runCts`-Dictionary für spätere Cancellation-Reaktion. `OrchestratorOptions` in Core. Cancellation-Strategie γ (nur StoppingToken); Implementierung via DB-Flag in Schritt 6.

### D-017: Provider-Strategie-Wechsel auf OpenAI-konform (Migration M1 — Auslöser)
Wechsel von Anthropic-spezifisch auf OpenAI-API-konform via OpenRouter. Default-Endpoint `https://openrouter.ai/api/v1`. Pro-Akteur-Modell-Mapping. Modell-Pluralismus aus Vision sofort verfügbar.

### D-018: Migration M1 abgeschlossen — Provider-Realfakten
Branch `feature/openai-compatible-providers`. 31/31 Tests grün. **Architect-Antworten zu sechs Schwerpunkten:** ToolChoice als String-Convention (F1); Pro-Akteur-Lookup über String-Keys nicht Enum (F2); `OpenAiMessageFormat` als internal static (F3); `LlmOptions.Endpoint` konfigurierbar, kein `BaseAddress` am HttpClient (F4); `02-architecture.md` voll umgeschrieben (F5); `anthropic-version`-Header entfernt, kein Provider-Header-Framework (F6). **Realfakten:** `ILlmClient` mit `ToolName`+`ToolArgumentsJson` separate Properties, Lazy-Validation des API-Keys, `LlmServiceExtensions.AddLlmClient` mit Analytics-Headern in `DefaultRequestHeaders`, `CountingEventSink.TotalEvents` neu. **Workflow-Abweichung:** keine formalen R1–R5-Pässe (Subagent-Self-Reviews + Build/Test stattdessen). R2-Nachholpass nach Merge empfohlen.

### D-019: Schritt 6 abgeschlossen — IRunService Application-Layer + Cancellation

**Datum:** 10. Mai 2026
**Bericht:** [reports/step-06-report.md](reports/step-06-report.md) (auf Branch `feature/openai-compatible-providers`, wird mit M1-Merge in main übergehen)
**Branch-Strategie:** Schritt 6 wurde **direkt auf dem M1-Feature-Branch** entwickelt (nicht parallel in main). Damit umfasst der M1-Merge in main jetzt sowohl Provider-Migration als auch IRunService — vereinfacht die Merge-Komplexität erheblich.
**Reviewer-Iterationen:** 2 (Iteration 1 mit 2 R2-MAJOR-Fixes, Iteration 2 grün).
**Tests:** 31/31 grün. **6 Conventional-Commits.**

**Architect-Konsultation — Antworten auf die fünf Schwerpunkte aus dem Step-6-Prompt:**

**(F1) Projekt-Layout:** Option B — eigenes `Geef.Atelier.Application`-Projekt mit `IRunService` + `RunService` in `Application/Runs/`. Kein Inline-in-Web. Begründung: MCP (Schritt 9) braucht diese Schicht ohne Web-Dep, und Verschieben später wäre teurer als jetzt sauber strukturieren.

**(F2) DB-Zugriff aus Application:** Variante β — `IRunRepository` in Core, Implementierung in Infrastructure. `Geef.Atelier.Application.csproj` referenziert nur `Geef.Atelier.Core`, **keine Infrastructure-Dep**. Onion-Architecture-konsequent. Mehraufwand: ein Interface + eine Klasse.

**(F3) Cancellation-Watcher-Pattern:** Pattern A — pro-Run-Task. Gemeinsame `linkedCts`. `await watcherTask` im `finally` joined sauber. Pattern B (zentral für alle Runs) wäre effizienter bei vielen parallelen Runs, aber für Skeleton-`MaxConcurrentRuns=5` ist Pattern A einfacher und korrekt.

**(F4) Eager-Loading in `GetRunAsync`:** Keine Includes (Skeleton-YAGNI). UI-Schicht (Schritt 7) kann separate Calls für Iterations/Findings machen oder bei Bedarf später eine `GetRunDetailsAsync`-Erweiterung.

**(F5) `configJson`-Typ:** `string` mit `JsonDocument.Parse`-Validierung in `RunService.SubmitRunAsync`. Bei invalidem JSON: `ArgumentException`. Leerer String wird intern zu `"{}"` normalisiert (Default-Configuration-Konvention).

**Fixierte Realfakten aus Schritt 6 (verbindlich ab Schritt 7):**

**(a) `IRunService` in `Geef.Atelier.Application/Runs/`:**
```csharp
public interface IRunService {
    Task<Guid> SubmitRunAsync(string briefingText, string configJson, CancellationToken ct = default);
    Task<RunEntity?> GetRunAsync(Guid runId, CancellationToken ct = default);
    Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, CancellationToken ct = default);
    Task<bool> CancelRunAsync(Guid runId, CancellationToken ct = default);
}
```
- `RunService` als `internal sealed class` mit Primary Constructor, Scoped-Lifetime.
- `CancelRunAsync` gibt `bool` zurück: `true` bei erfolgreichem Cancel-Request, `false` für drei Fälle (terminal, nicht gefunden, bereits angefragt). UI kann via `GetRunAsync` zwischen den Fällen unterscheiden.

**(b) `IRunRepository` in `Geef.Atelier.Core/Persistence/`:**
- `GetByIdAsync`, `ListAsync(StatusFilter?, limit)`, `RequestCancellationAsync` (atomar, bool-Return).
- Implementierung `RunRepository` in Infrastructure als `internal sealed`.
- `RequestCancellationAsync` via `ExecuteUpdateAsync` mit `WHERE Id=? AND Status IN(Pending,Running) AND !CancellationRequested` — atomar idempotent.

**(c) `RunEntity.CancellationRequested` Boolean-Flag:**
- `public bool CancellationRequested { get; init; }` mit Default `false`.
- EF-Migration `20260510202104_Step06Cancellation`: `ALTER TABLE "Runs" ADD COLUMN "CancellationRequested" boolean NOT NULL DEFAULT false;`
- Wird vom Cancellation-Watcher pro Run gepollt.

**(d) Cancellation-Watcher in `RunOrchestratorService`:**
- `WatchCancellationAsync(Guid runId, CancellationTokenSource cts)` als pro-Run-Task gestartet parallel zur Pipeline.
- Pollt DB im eigenen `CreateAsyncScope` mit `OrchestratorOptions.CancellationPollingInterval` (Default 1s, Test 200ms).
- Bei `CancellationRequested = true` → `cts.Cancel()` → Pipeline empfängt `OperationCanceledException`.
- Zweiter Catch-Arm: `catch (OperationCanceledException) when (cts.IsCancellationRequested)` → `OverrideToAbortedAsync("Cancelled by user")`.
- `finally`: `cts.Cancel()` (idempotent), `await watcherTask` (try/catch), `_runCts.TryRemove`, `cts.Dispose()`, `_slots.Release()`, `_runTasks.TryRemove` (letzter Schritt — Drain-Semantik aus D-016).

**(e) OCE-Catch-Filter-Reihenfolge:**
- Erst Service-Stop-Arm (`when stoppingToken.IsCancellationRequested`)
- Dann User-Cancel-Arm (`when cts.IsCancellationRequested`)
- Bei beiden true gewinnt Service-Stop — semantisch korrekt, weil forcierter Stop Priorität hat.

**(f) `OverrideToAbortedAsync`-Race-Behandlung:**
- Filter `r.Status IN(Running, Failed)` — schließt `Completed` aus.
- Wenn Pipeline schon `Completed` ist bevor Cancel ankommt: bleibt `Completed`. `CancellationRequested=true` bleibt ohne Effekt. Akzeptiert.

**(g) `OrchestratorOptions`-Erweiterung:**
- `CancellationPollingInterval: TimeSpan` (Default 1s).
- In `appsettings.json` Sektion `"Orchestrator"`: `"CancellationPollingInterval": "00:00:01"`.

**(h) Application-DI-Registrierung:**
- `AddAtelierApplication()` Extension in `Geef.Atelier.Application/Runs/ApplicationServiceExtensions.cs`.
- Registriert `IRunService` als Scoped (wegen DbContext-Abhängigkeit über Repository).
- In `Program.cs` neben `AddAtelierPersistence()`.

**(i) Test-Patterns für Application-Tests (R2-Lehre):**
- `BuildProvider()` liefert `ServiceProvider`, jeder Test öffnet `await using var provider = BuildProvider()` + `await using var scope = provider.CreateAsyncScope()`. Beide werden via IAsyncDisposable diszipliniert disposed — verhindert Connection-Leaks.
- Mid-Flight-Cancel-Tests verwenden Polling-Loop (deadline 15s) statt hardkodierter `Task.Delay`. Kein Flakey-Risk.
- `gate.Release(int.MaxValue)` als Cleanup nach Polling-Verifikation.

**R2-MAJOR-Fixes vor Phase 4:**

**(MAJOR-1) ServiceProvider-Disposal:** `RunServiceValidatesInputsTests.BuildService()` gab `IRunService` aus einem nie-disponierten Provider zurück → 5 geleaste Npgsql-Connections.
**Fix:** Provider und Scope explizit disponieren via `await using`-Pattern.

**(MAJOR-2) Test-Race-Condition:** Hardkodiertes `Task.Delay(400)` vor `gate.Release` in `RunServiceCancelsRunningRunTests` — flakey wenn Watcher langsamer als 400ms ist.
**Fix:** Polling-Loop bis Status nicht mehr Running, dann Gate-Release als Cleanup. `gate.WaitAsync(ct)` wirft OCE bei CTS-Cancel ohnehin — kein Release nötig für Abbruch-Pfad.

**Workflow-Update in `02-architecture.md`:**
Architecture-Doc Z.64 ("`IRunService`-Vertrag in Core") war ursprüngliche Brainstorming-Annahme. Beim Bau pragmatisch korrigiert: `IRunService` lebt in Application, nicht Core — Application-Verträge gehören in den Application-Layer. Doc entsprechend aktualisiert.

**AC9-Status (Real-OpenRouter-Test):** ⏭ Skip (vierter Skip in Folge: Schritte 3, 4, 5, 6). **Eskaliert für Schritt 7:** Vor Schritt 9 (MCP) muss AC9 mindestens einmal grün laufen, sonst baut MCP auf ungetesteter End-to-End-Pipeline-Kette. Im Step-7-Prompt als Hard-AC verankert.

**Empfehlungen für Schritt 7 (UI):**
- M1-Merge (jetzt mit Schritt 6) zuerst nach main, dann Schritt 7 in main bauen.
- Drei Pages: `/new` (Submit), `/runs` (Liste), `/runs/{id}` (Detail mit Live-Status).
- SignalR via `IHubContext<RunHub>` direkt aus `PostgresEventSink` — kein zusätzliches Polling für UI-Updates.
- Cancel-Button-UX: optimistisches Update, Server-Confirm via `GetRunAsync`-Status-Check.
- UI-Komponenten in `Components/UI/` — Workflow-CRITICAL bei direkten HTML-Elementen in Pages.

**M1-Merge-Status:** Läuft gerade. Nach Abschluss enthält main Schritte 1–5 + M1 + Schritt 6.

---

### D-020: Schritt 7 abgeschlossen — Blazor-UI mit SignalR und Playwright-E2E

**Datum:** 11. Mai 2026
**Bericht:** [reports/step-07-report.md](reports/step-07-report.md)
**Branch:** main (Single-Maintainer-Konvention)
**Reviewer-Iterationen:** R1: 1 (0 Findings), R2: 2 (CRITICAL SignalRRunNotifier try/catch behoben), R3: CONDITIONAL PASS (5/5 Determinismus), R4: 1 (0 CRITICAL/MAJOR), R5: 1 (PASS, 4 Flows).
**Tests:** 55/55 grün (31 alt + 4 bUnit + 4 Playwright E2E + 16 pers/orch/app).

**Fixierte Realfakten:**

(a) **Drei Pages** (`/new`, `/runs`, `/runs/{id}`) — Routing über Blazor `@page`-Direktive, kein Controller.

(b) **RunHub** (`Web/Hubs/RunHub.cs`) — zwei SignalR-Groups:
- `run-{runId}` für Detail-Page (Join/Leave pro Page-Lifecycle)
- `all-runs` für Runs-Listen-Page (Join/Leave pro Page-Lifecycle)
- Vier Methoden: `JoinRunGroupAsync`, `LeaveRunGroupAsync`, `JoinAllRunsGroupAsync`, `LeaveAllRunsGroupAsync`

(c) **`IRunNotifier`** in Core (`Core/Notifications/`), **`SignalRRunNotifier`** in Web (`Web/Notifications/`), Singleton-Lifetime. `IHubContext<RunHub>` direkt injiziert.

(d) **`PostgresEventSink`-Konstruktor** jetzt vierstellig: `(Guid atelierRunId, IServiceScopeFactory scopeFactory, IRunNotifier notifier, ILogger logger)`. Notifier-Aufruf nach jedem Persist in eigenem `try/catch` (best-effort, Warning-Log bei Fehler).

(e) **9 UI-Komponenten** in `Components/UI/`: StatusBadge, SeverityBadge, RunCard, IterationPanel, FindingItem, RunHeader, SubmitForm, EmptyState, CancelButton. Alle mit scoped `.razor.css`.

(f) **bUnit-Tests** in `Web/Components/` (4 Stück) + **Playwright-E2E** in `Web/E2E/` (4 Stück) + `WebTestHost`-Hybrid (wraps `WebApplicationFactory<Program>` + echte Kestrel-Adresse via `IServerAddressesFeature`).

(g) **SignalR-Variante α** (Browser-HubConnection) gewählt. `WithAutomaticReconnect()` + `Reconnected`-Handler re-joinst Groups.

(h) **Hub-Event-Granularität A** — `"RunUpdated"` (Detail) und `"AnyRunUpdated"` (Liste) transportieren nur `Guid runId`. UI fetcht via `IRunService`.

(i) **Cancel-Button-UX:** Optimistisch (sofortiger Disabled-State). Re-Fetch via `"RunUpdated"`-Event korrigiert falls nötig.

(j) **Listen-Live-Update:** `AnyRunUpdated`-Group → Re-Fetch der letzten 20 Runs + `StateHasChanged`. Throttling ausstehend (Skeleton-Akzeptanz).

(k) **Architect-Invocation:** Entscheidungen im Plan-Mode fixiert (Level-2-Equivalent), keine separate CLI-Invocation während Execution.

(l) **AC8 (OpenRouter-Real-Test):** ✅ Grün. Latenz 5–12s, Tokens 174–523 pro Run. Key in `appsettings.Development.json` (nicht im Repo).

(m) **Hard-Rule UI-Komponenten-Library:** R4: 0 CRITICAL/MAJOR. Alle semantischen UI-Elemente sind Komponenten; Layout-`div`s in Pages erlaubt.