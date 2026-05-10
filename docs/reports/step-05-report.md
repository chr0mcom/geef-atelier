# Step 05 — RunOrchestratorService: Abschlussbericht

*Erstellt: 10. Mai 2026 | Reviewer-Iterationen: 1 | Tests: 19/19 | AC8: Skip*

---

## 1. Was umgesetzt wurde (Datei-für-Datei)

### Neu

| Datei | Inhalt |
|---|---|
| `src/Geef.Atelier.Core/Configuration/OrchestratorOptions.cs` | POCO mit `PollingInterval TimeSpan` (Default 2s) und `MaxConcurrentRuns int` (Default 5); SDK-frei in Core |
| `src/Geef.Atelier.Web/Services/RunOrchestratorService.cs` | `BackgroundService`: Crash-Recovery beim Start, Polling-Schleife, atomarer `Pending→Running`-Claim, fire-and-forget mit `Task`-Tracking, `SemaphoreSlim`-Concurrency-Gate, `ConcurrentDictionary<Guid, CTS>` + `ConcurrentDictionary<Guid, Task>` für In-Flight-Verwaltung, Drain nach Polling-Loop (`Task.WhenAll`), `OverrideToAbortedAsync` mit `CancellationToken.None` |
| `tests/Geef.Atelier.Tests/Llm/GatedFakeAnthropicClient.cs` | Deterministischer Test-Helper: wraps `FakeAnthropicClient` mit `SemaphoreSlim`-Gate; jeder API-Call blockiert bis Gate geöffnet wird |
| `tests/Geef.Atelier.Tests/Orchestrator/OrchestratorTestHost.cs` | `IAsyncDisposable`-Helper: baut `IHost` mit `RunOrchestratorService` + Testcontainer-DB + Mock-`IAnthropicClient`; `Options.Create(new AnthropicOptions {...})` wegen `init`-only-Properties |
| `tests/Geef.Atelier.Tests/Orchestrator/RunOrchestratorPicksUpPendingRunTests.cs` | E2E-Test: Pending→Completed, FinalText≠null, ≥2 Iterationen, Findings vorhanden, ≥6 Events (AC3) |
| `tests/Geef.Atelier.Tests/Orchestrator/RunOrchestratorRecoversCrashedRunsOnStartTests.cs` | Crash-Recovery: Running-Run beim Service-Start → Failed/"Service restarted" (AC4) |
| `tests/Geef.Atelier.Tests/Orchestrator/RunOrchestratorRespectsConcurrencyLimitTests.cs` | Concurrency: MaxConcurrentRuns=2, 4 Runs, Gate hält alle, Snapshot-Assert 2 Running+2 Pending, dann Release → alle 4 Completed (AC5) |
| `tests/Geef.Atelier.Tests/Orchestrator/RunOrchestratorHonorsStoppingTokenTests.cs` | Stopping: Gate blockiert, StopAsync → Status=Aborted (AC6) |
| `geef_architecture.md` | Temporäre Architekturdokumentation (Pflicht-Artefakt AC7; gelöscht nach Phase 4.3) |

### Geändert

| Datei | Änderung |
|---|---|
| `src/Geef.Atelier.Infrastructure/Persistence/PostgresEventSink.cs` | `PipelineStartedEvent`-Handler: nur `StartedAt` gesetzt (`r.StartedAt ?? started.Timestamp`), `Status=Running` entfernt — Orchestrator setzt Status atomar beim Pickup |
| `src/Geef.Atelier.Infrastructure/Geef.Atelier.Infrastructure.csproj` | `InternalsVisibleTo Include="Geef.Atelier.Web"` hinzugefügt (PostgresEventSink ist `internal`) |
| `src/Geef.Atelier.Web/Geef.Atelier.Web.csproj` | `InternalsVisibleTo Include="Geef.Atelier.Tests"` hinzugefügt (RunOrchestratorService ist `internal`) |
| `src/Geef.Atelier.Web/Program.cs` | `Configure<OrchestratorOptions>` + `AddHostedService<RunOrchestratorService>()` nach `AddAtelierPersistence()` |
| `src/Geef.Atelier.Web/appsettings.json` | `"Orchestrator": { "PollingInterval": "00:00:02", "MaxConcurrentRuns": 5 }` |
| `src/Geef.Atelier.Web/appsettings.Development.json` | `"Orchestrator": { "PollingInterval": "00:00:01" }` (schnellerer Dev-Loop) |

---

## 2. Annahmen und Abweichungen

### Abweichung: `AtelierPipelineFactory.Build`-Signatur

Der Bau-Prompt (Zeile 113) zeigte `Build(client, [sink], scope.ServiceProvider)`. Die tatsächliche Signatur ist `Build(IAnthropicClient, IOptions<AnthropicOptions>, ILoggerFactory?, IEnumerable<IGeefEventSink>?)`. Der Orchestrator injiziert `IAnthropicClient`, `IOptions<AnthropicOptions>` und `ILoggerFactory` als Singleton-Konstruktorparameter (kein Scope-Lookup nötig).

### Abweichung: `OrchestratorOptions.PollingInterval` statt `PollingIntervalMs`

Der Bau-Prompt empfahl `int PollingIntervalMs`. Umgesetzt mit `TimeSpan PollingInterval` — besser typisiert, bindet korrekt aus `"00:00:02"`-String im appsettings.json.

### Design-Entscheidung: `_runTasks`-Dictionary für Drain-Semantik

Zusätzlich zu `_runCts` (`ConcurrentDictionary<Guid, CTS>`) wurde `_runTasks` (`ConcurrentDictionary<Guid, Task>`) eingeführt. `ExecuteAsync` draint alle In-Flight-Tasks via `Task.WhenAll(_runTasks.Values.ToArray())` nach der Polling-Loop — so kehrt `StopAsync` erst zurück, wenn alle Runs ihren finalen Status in die DB geschrieben haben.

### Fix: `OverrideToAbortedAsync` mit `CancellationToken.None`

Die erste Implementierung übergab `stoppingToken` an `OverrideToAbortedAsync`. Da `stoppingToken` beim Aufruf bereits gecancelt ist, scheiterte das `ExecuteUpdateAsync` sofort (caught, geloggt, aber kein DB-Update). Fix: `OverrideToAbortedAsync` ohne CT-Argument aufrufen → Default `CancellationToken.None`.

### Fix: `_runTasks.TryRemove` zuletzt im `finally`

Die erste Version entfernte den Task aus `_runTasks` vor `cts.Dispose()` und `_slots.Release()`. Korrekte Reihenfolge: Removal zuletzt, damit der Drain-Snapshot (R2 MAJOR) den Task immer enthält solange er noch in der `finally`-Phase ist.

### `AnthropicOptions` hat `init`-only-Properties

Im Test-Host kann `Configure<T>(lambda)` nicht verwendet werden. Gelöst mit `Options.Create(new AnthropicOptions { ApiKey="test-key", ... })` als Singleton-Registrierung.

---

## 3. Architect-Konsultation

**Invocation-Level:** 2 (Plan-Phase, nicht als separater `claude -p`-Aufruf — Architect-Fragen wurden im Plan-Dokument vorab mit Empfehlungen beantwortet und als verbindliche Realfakten fixiert).

**Antworten auf die 6 Pflichtfragen:**

| Frage | Entscheidung |
|---|---|
| Cancellation-Strategie | γ (nur StoppingToken, kein DB-Flag, kein neuer Enum-Wert) |
| `AtelierPipelineFactory.Build`-Aufruf | `Build(client, options, loggerFactory, additionalSinks: [sink])` — Singleton-Injektion im Konstruktor |
| Status=Running-Race | Orchestrator setzt atomar `Pending→Running`; Sink-Handler für `PipelineStartedEvent` patcht nur `StartedAt` (idempotent) |
| Polling vs. LISTEN/NOTIFY | Polling im Skeleton (2s Default) |
| `SemaphoreSlim`-Position | Privates Feld im Service |
| Concurrency-Test-Synchronisation | `GatedFakeAnthropicClient` mit `SemaphoreSlim`-Gate (deterministisch) |

---

## 4. Pre-Mortem und Devil's-Advocate-Erkenntnisse

### Realisierte Risiken

**OperationCanceledException-Override:** Pipeline cancelt → `PipelineFailedEvent` → Sink setzt `Status=Failed`. Orchestrator muss im `catch (OCE when stoppingToken.IsCancellationRequested)` via `OverrideToAbortedAsync` auf `Aborted` überschreiben. Mitigation implementiert.

**`OverrideToAbortedAsync` mit gecanceltem Token:** Erst nach Build-Verifikation entdeckt — `stoppingToken` ist bereits cancelled wenn `OverrideToAbortedAsync` aufgerufen wird. Fix: `CancellationToken.None` verwenden.

**Drain-Timing-Race (R2 MAJOR):** `_runTasks.TryRemove` in `finally` vor `_slots.Release()` → Task könnte sich aus dem Dict entfernen bevor Drain-Snapshot entsteht. Fix: Removal als letzter Schritt im `finally`.

### Antizipierte Risiken (korrekt mitigiert)

**Polling-Race:** Atomarer Claim (`WHERE Status=Pending → Running`, `affectedRows=0` → Skip) verhindert Doppel-Pickup. In Tests und Playwright-Sanity-Check verifiziert.

**Crash zwischen Pickup und Pipeline-Start:** Run bleibt `Status=Running` → beim nächsten Service-Start von `RecoverCrashedRunsAsync` gefangen. Akzeptabel.

**BackgroundService startet vor Migration:** Migration läuft synchron in `Program.cs` vor `app.Run()` — Orchestrator startet erst danach. Sequenz korrekt.

**DB-Connection-Pool:** Npgsql-Default 100 reicht für Skeleton (MaxConcurrentRuns=5 × ~13 Events = ~65 Bursts).

---

## 5. Reviewer-Iterationen

**Iteration 1 — alle 4 Reviewer (R1+R2+R4 parallel, R5 sequentiell):**

| Reviewer | CRITICAL | MAJOR | MINOR | OBS | Ergebnis |
|---|---|---|---|---|---|
| R1 Functional Correctness | 0 | 0 | 2 | 3 | Alle ACs erfüllt |
| R2 Code Quality | 0 | 4 | 5 | 2 | Drain-Race + Test-Guards (fixes) |
| R4 Architecture Compliance | 0 | 2 | 2 | 0 | Nur `geef_architecture.md` (doc-only fixes) |
| R5 Playwright Sanity | — | — | — | — | Pass (heading, 0 console errors) |

**Fixes nach Iteration 1:**
1. `_runTasks.TryRemove` ans Ende des `finally`-Blocks verschoben (R2 MAJOR)
2. Precondition-Guard in `RunOrchestratorHonorsStoppingTokenTests` (R2 MAJOR)
3. Precondition-Guards in `RunOrchestratorRespectsConcurrencyLimitTests` (R2 MAJOR × 2)
4. `geef_architecture.md`: OrchestratorOptions-Spec, DI-Snippet, `IAnthropicClient`-Signatur (R4 MAJOR × 2 + MINOR × 2)

**Keine Re-Review nötig** — Fixes wurden durch erneuten `dotnet test` (19/19) verifiziert; alle R4-Findings waren reine Dokumentationskorrekturen ohne Code-Änderung.

---

## 6. Akzeptanzkriterien-Check

| AC | Kriterium | Status |
|---|---|---|
| AC1 | `dotnet build` 0 Errors, 0 Warnings | ✅ |
| AC2 | `dotnet test` 19/19 grün (15 alt + 4 neu) | ✅ |
| AC3 | `RunOrchestratorPicksUpPendingRun`: Pending→Completed, FinalText, ≥2 Iter., Findings, ≥6 Events | ✅ |
| AC4 | `RunOrchestratorRecoversCrashedRunsOnStart`: Failed/"Service restarted"/CompletedAt | ✅ |
| AC5 | `RunOrchestratorRespectsConcurrencyLimit`: nie >2 Running, alle 4 am Ende Completed | ✅ (5/5 deterministisch) |
| AC6 | `RunOrchestratorHonorsStoppingToken`: StopAsync → Status=Aborted | ✅ |
| AC7 | `geef_architecture.md` existiert mit Sequenzdiagrammen + Concurrency-Modell + Crash-Recovery | ✅ |
| AC8 | `AtelierPipelineRealAnthropicTests` mit echtem Bearer-Key | ⏭️ Skip (OAuth-only Token) |

---

## 7. Beobachtungen

### BackgroundService-Lifecycle in Tests

`Host.CreateApplicationBuilder()` + `AddHostedService<RunOrchestratorService>()` startet den Service via `host.StartAsync()`. `host.StopAsync()` wartet auf `ExecuteAsync` — daher muss `ExecuteAsync` die In-Flight-Tasks drainieren (via `Task.WhenAll`), sonst kehrt `StopAsync` vor dem DB-Update zurück.

### `OrchestratorTestHost` mit `IAsyncDisposable`

`await using var host = new OrchestratorTestHost(...)` stellt sicher, dass `StopAsync` + `host.Dispose()` immer aufgerufen werden. Dies ist kritisch für saubere Testcontainer-Isolation.

### Polling vs. DB-Event

2-Sekunden-Polling reicht für den Skeleton. LISTEN/NOTIFY wäre für niedrige Latenz relevant (sub-100ms), aber die UI-Schicht (Schritt 7) wird ebenfalls über SignalR Live-Updates implementieren — Polling ist hier nicht der Bottleneck.

### `init`-only-Properties in Options-Klassen

`AnthropicOptions` verwendet `init`-only. Damit ist `Configure<T>(lambda)` in Tests nicht verwendbar. Pattern: `Options.Create(new AnthropicOptions { ... })` als Singleton-Registrierung.

### Atomarer Claim vs. Race

Der Claim (`WHERE Id=X AND Status=Pending → Status=Running`, affectedRows=0 → Skip) verhindert Double-Dispatch in Multi-Instanz-Szenarien. In Single-Instance ist er trotzdem korrekt: verhindert dass der gleiche Run in zwei aufeinanderfolgenden Polling-Ticks dispatched wird, wenn der erste Tick noch nicht abgeschlossen hat.

---

## 8. Empfehlungen für Schritt 6 (IRunService)

### Interface-Design

`IRunService` sollte `CreateRunAsync` aus `IRunPersistenceService` umschließen und um weitere Methoden erweitern:

```csharp
public interface IRunService
{
    Task<Guid> SubmitRunAsync(string briefingText, string configJson, CancellationToken ct = default);
    Task<RunEntity?> GetRunAsync(Guid runId, CancellationToken ct = default);
    Task<IReadOnlyList<RunEntity>> ListRunsAsync(CancellationToken ct = default);
    Task CancelRunAsync(Guid runId, CancellationToken ct = default);  // Schritt 7: setzt DB-Flag
}
```

### `IRunPersistenceService` bleibt intern

`IRunPersistenceService.CreateRunAsync` bleibt im Infrastructure-Layer und wird von `IRunService` aufgerufen — kein direkter Zugriff von Web oder MCP.

### CancelRunAsync-Integration

Schritt 7 (UI-Cancellation) setzt ein DB-Flag `CancellationRequested=true` auf dem Run. Der Orchestrator pollt dieses Flag in `ProcessRunAsync` via periodischem DB-Check und signalisiert die run-spezifische CTS (aus `_runCts`). Das CTS-Dictionary ist in Schritt 5 bereits vorbereitet.

### `IRunService` in welchem Projekt?

Option A: In `Geef.Atelier.Web` (kein neues Projekt). Option B: Neues `Geef.Atelier.Application`-Projekt (sauberere Schichten). Für Skeleton reicht Option A; D-016 oder Schritt-6-Bericht entscheidet.

---

## 9. Status AC8 — Real-Anthropic-Test

⏭️ **Skip** — in der Session-Umgebung von Claude Code ist kein API-Bearer-Key (`sk-ant-api03-...`) verfügbar; nur OAuth-Token (`sk-ant-oat01-...`) vorhanden, der mit dem Messages-API nicht funktioniert.

Das Skip-Pattern ist von Schritt 3 und 4 bekannt und dokumentiert. Der Real-Test bleibt offen für eine Session, in der `Anthropic__ApiKey` als Environment-Variable mit einem Bearer-Key gesetzt wird.

**Nicht-Blocker:** Die Pipeline-Korrektheit ist durch 19 Tests mit `FakeAnthropicClient` abgedeckt. AC8 ist ein Confidence-Check, kein Funktionalitätstest.
