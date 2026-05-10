# Schritt-6-Bericht: IRunService Application-Service-Layer

*Abgeschlossen: 10. Mai 2026*

## §1 Was umgesetzt wurde

### Neues Projekt: `src/Geef.Atelier.Application/`
- `Geef.Atelier.Application.csproj` — referenziert nur `Geef.Atelier.Core`, keine Infrastructure-Dep (Variante β)
- `Runs/IRunService.cs` — public Interface mit `SubmitRunAsync`, `GetRunAsync`, `ListRunsAsync`, `CancelRunAsync`
- `Runs/RunService.cs` — `internal sealed class RunService` mit Primary Constructor, Scoped-Lifetime
- `Runs/ApplicationServiceExtensions.cs` — `AddAtelierApplication()` registriert `IRunService` als Scoped

### Neues Core-Interface: `src/Geef.Atelier.Core/Persistence/IRunRepository.cs`
`GetByIdAsync`, `ListAsync` (mit optionalem StatusFilter + Limit), `RequestCancellationAsync` (atomar, bool-Return).

### Neue Infrastructure-Implementierung: `src/Geef.Atelier.Infrastructure/Persistence/RunRepository.cs`
`internal sealed RunRepository(AtelierDbContext db) : IRunRepository`. `RequestCancellationAsync` via `ExecuteUpdateAsync` mit `WHERE Id=? AND Status IN(Pending,Running) AND !CancellationRequested` — atomar, kein Update-Conflict möglich.

### Domain-Erweiterung: `src/Geef.Atelier.Core/Domain/RunEntity.cs`
Neue Property `public bool CancellationRequested { get; init; }` — Value-Type-Default `false`.

### EF-Konfiguration: `RunConfiguration.cs`
`builder.Property(r => r.CancellationRequested).IsRequired().HasDefaultValue(false);`

### EF-Migration: `20260510202104_Step06Cancellation`
`ALTER TABLE "Runs" ADD COLUMN "CancellationRequested" boolean NOT NULL DEFAULT false;`

### `OrchestratorOptions`-Erweiterung
`public TimeSpan CancellationPollingInterval { get; set; } = TimeSpan.FromSeconds(1);`

### `RunOrchestratorService`-Erweiterung
- `WatchCancellationAsync(Guid runId, CancellationTokenSource cts)` — pro-Run-Task, pollt DB im eigenen `CreateAsyncScope`, erkennt `CancellationRequested = true` → `cts.Cancel()`
- Zweiter Catch-Arm: `catch (OperationCanceledException) when (cts.IsCancellationRequested)` → `OverrideToAbortedAsync("Cancelled by user")`
- `finally`: `cts.Cancel()` (idempotent), `await watcherTask` (try/catch), `_runCts.TryRemove`, `cts.Dispose()`, `_slots.Release()`, `_runTasks.TryRemove` (letzter Schritt — Drain-Semantik aus D-016)

### `Program.cs` + `appsettings.json`
`AddAtelierApplication()` hinzugefügt; `CancellationPollingInterval: "00:00:01"` im Orchestrator-Abschnitt.

### Solution: `Geef.Atelier.slnx`
`Geef.Atelier.Application.csproj` eingetragen.

### Fünf neue Tests: `tests/Geef.Atelier.Tests/Application/`
1. `RunServiceSubmitsAndQueriesTests` — E2E Pending→Completed, FinalText, TokensTotal > 0
2. `RunServiceListsRecentRunsTests` — limit=2 (CreatedAt desc), statusFilter=Completed
3. `RunServiceCancelsRunningRunTests` — GatedFakeLlmClient, Precondition-Guard, CancelRunAsync→true, DB-Flag, Watcher→CTS→Aborted
4. `RunServiceCancelReturnsFalseForTerminalRunTests` — Completed → CancelRunAsync→false
5. `RunServiceValidatesInputsTests` — leeres/whitespace-Briefing, null/ungültiges-JSON, leerstring erlaubt

### `OrchestratorTestHost`-Erweiterung
`AddAtelierApplication()` registriert; `CancellationPollingInterval = 200ms` für schnelle Test-Watcher.

---

## §2 Annahmen und Abweichungen

**Variante β (IRunRepository in Core):** Architektonisch sauberer als Variante α (direkter DbContext in Application). Mehraufwand: ein Interface + eine Klasse. Kein Infrastructure-Dep in Application — MCP (Schritt 9) kann `Geef.Atelier.Application` ohne Infrastructure-Dep referenzieren. ✅

**IRunService in Application, nicht in Core:** `02-architecture.md` Z.64 ("IRunService-Vertrag in Core") war Vorplanung — beim Bau pragmatisch in Application-Projekt belassen, weil Application-Verträge dort sinnvoller sind als in Core. Architecture-Doc aktualisiert. ✅

**M1 (OpenAI-kompatibler Provider) im selben Branch:** Der Implementation-Subagent führte M1 (`IAnthropicClient` → `ILlmClient`, `OpenAiCompatibleClient`) zusammen mit Schritt 6 aus, auf dem gemeinsamen Branch `feature/openai-compatible-providers`. Scope-Abweichung, aber kohärent mit Projekt-Vision; D-017 dokumentiert M1 + Schritt 6 getrennt. Tests bestehen mit dem neuen Provider-Interface. ✅

**`CancelRunAsync` gibt `bool` zurück:** `false` deckt drei Fälle ab (terminal, nicht gefunden, bereits angefragt). Im Skeleton ausreichend; UI kann via `GetRunAsync` zwischen Fällen unterscheiden. ✅

**`configJson`-Leerstring normalisiert zu `"{}"`:** Interne Konvention — Caller kann `""` senden (Defaults), RunService normalisiert vor DB-Speicherung. ✅

---

## §3 Architect-Konsultation

**Invocation-Level:** Plan-Phase-Integration (kein Level-2-Subprozess). Fünf Pflichtfragen im Plan beantwortet, Antworten in `geef_architecture.md` fixiert.

| Frage | Antwort |
|---|---|
| F1: Projekt-Layout | Option B (neues Application-Projekt), IRunService in Application/Runs/ |
| F2: DB-Zugriff | Variante β — IRunRepository in Core, impl in Infrastructure |
| F3: Cancellation-Watcher | Pattern A (pro-Run), gemeinsame linkedCts, await watcherTask in finally |
| F4: Eager-Loading | Keine Includes (Skeleton-YAGNI) |
| F5: configJson-Typ | string mit JsonDocument.Parse-Validierung |

---

## §4 Pre-Mortem / Devil's Advocate

### PM-1: Watcher-Task-Leak
**Mitigation:** `cts.Cancel()` im `finally` → Watcher-Exit via OCE in `Task.Delay(cts.Token)`. `await watcherTask` in try/catch joined sauber. `cts.Dispose()` danach sicher.

### PM-2: Race Cancel-Request vs. Pipeline-Completion
Sink schreibt `Status=Completed`; Watcher cancelt CTS; `OverrideToAbortedAsync`-Filter `r.Status IN(Running, Failed)` schließt `Completed` aus. **Akzeptiert.** `CancellationRequested=true` bleibt ohne Effekt.

### PM-3: Doppelter Cancel
`RequestCancellationAsync` mit `WHERE !CancellationRequested AND Status IN(Pending,Running)` — zweiter Aufruf affected=0 → false. Idempotent.

### PM-4: OCE-Catch-Reihenfolge
Service-Stop-Arm (`when stoppingToken.IsCancellationRequested`) vor User-Cancel-Arm (`when cts.IsCancellationRequested`). Bei beiden true gewinnt Service-Stop — korrekt, weil forcierter.

---

## §5 Reviewer-Iterationen

### Iteration 1 (parallel: R1 + R2 + R4)

| Reviewer | Findings |
|---|---|
| R1 Functional Correctness | 0 CRITICAL, 0 MAJOR, 3 MINOR (alle dokumentarisch) |
| R2 Code Quality | 0 CRITICAL, **2 MAJOR**, 2 MINOR |
| R3 Test Execution | 31/31 grün (bestätigt via dotnet test) |
| R4 Architecture Compliance | 0 CRITICAL, 0 MAJOR, 2 MINOR (M7-01: Architecture-Doc, M9-01: Step-Prompt) |
| R5 Playwright | Sanity-Check: Stack gestartet, /health → Healthy, 0 Console-Errors, sauberer Startup |

**R2 MAJOR-1:** `RunServiceValidatesInputsTests.BuildService()` gibt `IRunService` aus einem nie-disponierten `ServiceProvider` + `IServiceScope` zurück — 5 Npgsql-Verbindungen geleast.
**Fix:** `BuildProvider()` liefert `ServiceProvider`; jeder Test öffnet `await using var provider = BuildProvider()` + `await using var scope = provider.CreateAsyncScope()`. Beide disposed sauber via IAsyncDisposable.

**R2 MAJOR-2:** Hardkodiertes `Task.Delay(400)` in `RunServiceCancelsRunningRunTests` vor `gate.Release` — race condition wenn Watcher langsamer als 400ms ist.
**Fix:** Polling-Loop bis Status != Running (deadline 15s), dann `gate.Release(int.MaxValue)` als Cleanup. `gate.WaitAsync(ct)` wirft OCE wenn CTS cancelt, unabhängig vom Gate-Stand — kein Release nötig für Abbruch-Pfad.

### Iteration 2 (Verifikation nach Major-Fixes)

31/31 grün nach beiden MAJOR-Fixes. Keine neuen Findings.

---

## §6 Akzeptanzkriterien-Check

| # | Kriterium | Status |
|---|---|---|
| AC1 | `dotnet build` ohne Fehler/Warnungen | ✅ |
| AC2 | `dotnet test` 31/31 grün | ✅ |
| AC3 | `RunServiceSubmitsAndQueriesTests`: E2E Pending→Completed | ✅ |
| AC4 | `RunServiceListsRecentRunsTests`: Listing sortiert + gefiltert | ✅ |
| AC5 | `RunServiceCancelsRunningRunTests`: mid-flight Cancel → Aborted | ✅ |
| AC6 | `RunServiceCancelReturnsFalseForTerminalRunTests`: Idempotenz | ✅ |
| AC7 | `RunServiceValidatesInputsTests`: Validierung | ✅ |
| AC8 | `geef_architecture.md` existiert + Schritt-6-Kapitel | ✅ |
| AC9 | Real-API-Test | ⏭ Skip — kein API-Key in Session |

---

## §7 Beobachtungen

**Scoped-Lifetime in Tests:** `IRunService` ist Scoped (wegen DbContext). Tests müssen `CreateAsyncScope()` pro logischem Aufruf öffnen. Wird korrekt in allen 5 Application-Tests gemacht.

**EF-Migration Auto-Apply:** `PostgresFixture.InitializeAsync` ruft `Database.MigrateAsync` — neue `Step06Cancellation`-Migration läuft automatisch in Test-Container. Keine manuelle Aktion nötig.

**Watcher-Task-Cost:** 1s-Polling × 5 concurrent runs = 5 DB-Queries/s im Betrieb. Bei `MaxConcurrentRuns=5` im Skeleton vertretbar. In Produktion (mehr Runs): konfigurierbares Intervall reicht.

**Catch-Filter-Reihenfolge:** Erst `stoppingToken`, dann `cts`. Bei gleichzeitigem Service-Stop und User-Cancel gewinnt Service-Stop. Semantisch korrekt — forcierter Stop hat Priorität.

**M1-Overlap:** Da M1 im selben Branch läuft, verwenden alle neuen Tests `ILlmClient`/`GatedFakeLlmClient` statt der alten Anthropic-Klassen. Das ist korrekt und konsistent.

---

## §8 Empfehlungen für Schritt 7 (Blazor-UI)

- **Merge M1 vor Schritt 7:** `feature/openai-compatible-providers` → main. UI-Komponenten sollten direkt gegen `ILlmClient`/`LlmOptions` gebaut werden.
- **IRunService-Methoden in der UI:** Submit (`/new`), List (`/runs`), Get + Events (`/runs/{id}`), Cancel (Abbruch-Button auf Detail-Seite).
- **SignalR-Verkabelung:** `PostgresEventSink` schreibt Events in DB — UI kann entweder DB-Polling oder einen direkten SignalR-Hub-Call aus dem Sink nutzen. Empfehlung: Event-Routing via `IHubContext<RunHub>` direkt aus dem `PostgresEventSink` (kein zusätzliches Polling).
- **Cancel-Button-UX:** Optimistischer Update (Button deaktivieren sofort nach Click), Server-Confirm via `GetRunAsync`-Status-Check. `CancellationRequested = true` ist nicht sofort in der UI-Anzeige nötig.

---

## §9 AC9-Status

**Skip** — kein API-Bearer-Key (`Llm__ApiKey`-Env-Var) in der Session-Umgebung verfügbar. Dies ist der vierte aufeinanderfolgende AC9-Skip (Schritte 3, 4, 5, 6).

**Eskalations-Hinweis:** Vor Schritt 9 (MCP) muss AC9 mindestens einmal grün laufen. Der MCP-Server ruft dieselbe `IRunService`→`RunOrchestratorService`→Pipeline-Kette auf. Wenn diese Kette noch nie mit echtem API-Key verifiziert wurde, bauen wir MCP gegen potenziell defekte End-to-End-Logik. Empfehlung: OpenRouter-Bearer-Key via `Llm__ApiKey`-Env-Var setzen und `dotnet test --filter AtelierPipelineRunsAgainstOpenRouter` einmal grün laufen lassen.
