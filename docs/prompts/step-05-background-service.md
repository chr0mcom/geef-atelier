# Claude-Code-Prompt: Schritt 5 — RunOrchestratorService (BackgroundService)

*Diese Datei ist als Eingabe für Claude Code gedacht.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Schritte 1–4 sind abgeschlossen: Solution + DB + Pipeline + Anthropic-Provider + Persistierung laufen. Deine Aufgabe ist **Schritt 5 von 10**: der **`RunOrchestratorService` als BackgroundService** — die erste echte Fire-and-Forget-Stufe.

Was sich ändert: Ein `BackgroundService` pollt Pending-Runs aus der DB, baut für jeden eine Pipeline-Instanz mit dem persistenten EventSink, führt sie aus, schreibt Status-Updates. Crash-Recovery beim Service-Start. Mehrere Runs können parallel laufen, mit konfigurierbarem Limit. Was bleibt unverändert: Pipeline-Provider, Anthropic-Client, EventSink-Logik, DB-Schema (außer ggf. Cancellation-Flag).

`IRunService` (saubere Application-Service-Schicht für UI und MCP) kommt erst in Schritt 6. UI in Schritt 7. MCP in Schritt 9. In Schritt 5 reden wir noch nicht mit User-Frontends — der Run wird über `IRunPersistenceService.CreateRunAsync` von Tests in die DB gestellt, der Service findet ihn dann.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors, alle Hard Rules.

**Phase-1.4-Hinweis:** Level 2 (`cat /tmp/prompt.txt | claude -p`) hat in Schritt 3 und 4 zuverlässig funktioniert. Probiere Level 1 erst, falls du eine bessere Lösung hast — sonst direkt Level 2. Atelier-Level-4-Fallback nur wenn auch Level 2 versagt.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

Vor Beginn der Implementierung lies in dieser Reihenfolge:

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`CLAUDE.md`** im Repo-Root.
3. **`docs/02-architecture.md`**, besonders das Schichtenbild und das BackgroundService-Schicht.
4. **`docs/03-walking-skeleton-plan.md`**, Abschnitt "Schritt 5".
5. **`docs/05-decisions-log.md`**, alle Einträge **D-010 bis D-015** — besonders D-015 mit den Schritt-4-Realfakten und den **Empfehlungen für Schritt 5** am Ende.
6. **`docs/reports/step-04-report.md`**, besonders **Sektion 8 (Empfehlungen für Schritt 5)** und **Sektion 9 (AC7-Status)**.
7. **Aktueller Code im Repo:**
   - `src/Geef.Atelier.Core/Persistence/IRunPersistenceService.cs` — der Vertrag, den der Orchestrator nutzt.
   - `src/Geef.Atelier.Infrastructure/Persistence/RunPersistenceService.cs` — die Implementierung.
   - `src/Geef.Atelier.Infrastructure/Persistence/PostgresEventSink.cs` — der Sink, den der Orchestrator pro Run instantiiert.
   - `src/Geef.Atelier.Infrastructure/Pipeline/AtelierPipelineFactory.cs` — `Build(...)` und `BuildWithProviders(...)`.
   - `src/Geef.Atelier.Web/Program.cs` — DI-Registrierung, dort kommt `AddHostedService<RunOrchestratorService>()` rein.
8. **.NET BackgroundService Patterns:** Doku zu `BackgroundService`, `StoppingToken`-Verkettung, `IServiceScopeFactory` für Pro-Iteration-Scopes, `SemaphoreSlim` für Concurrency-Limits.

## In Schritten 1–4 etablierte Realfakten (verbindlich)

Aus D-010, D-012, D-013, D-015. Zentrale Punkte für Schritt 5:

**SDK-Vokabular:**
- `Geef.Sdk.Results.FindingSeverity { Info, Warning, Error, Critical }`. Mapping zu Atelier via `ToAtelierSeverity()` Extension in Infrastructure.
- `ConvergenceFailedException` bei `AbortOnCritical=true`, Message enthält `"AbortCriticalBlocker"`.
- `PipelineFailedEvent.History.Records[^1]?.EvaluationResult.AllFindings` für Critical-Abort-Findings.
- `using SdkGeef = Geef.Sdk.Geef;` für statische Builder-Methoden.

**Atelier-Konventionen:**
- `internal sealed` für alle Provider; `<InternalsVisibleTo Include="Geef.Atelier.Tests" />`.
- `IRunPersistenceService.CreateRunAsync(briefingText, configJson, ct) → Task<Guid>` ist der einzige Weg, Runs anzulegen.
- `PostgresEventSink(Guid runId, IServiceScopeFactory scopeFactory, ILogger logger)` — RunId injiziert, kein hidden state.
- `AtelierPipelineFactory.Build(...)` für Production, `BuildWithProviders(..., additionalSinks: [...])` für Tests.
- Token-Tracking via `AtelierContextKeys.TokenUsage` (typisiert).
- Run-Status: `Pending → Running → Completed/Failed/Aborted`.

**Aus Schritt 4 explizit empfohlener Pipeline-Bau-Ablauf** (Bericht Sektion 8 Punkt 1):
```
IRunPersistenceService.CreateRunAsync(briefing, configJson)
  → runId
  → new PostgresEventSink(runId, scopeFactory, logger)
  → AtelierPipelineFactory.BuildWithProviders(..., additionalSinks: [sink])
  → runner.RunAsync(briefing, ct)
```

**Wichtig:** In Schritt 5 erstellt nicht der Orchestrator den Run-Record — der wird vorher von einem Test (oder später Schritt 6 via `IRunService`) angelegt mit `Status=Pending`. Der Orchestrator findet ihn, setzt ihn auf `Running`, baut die Pipeline um die existierende `RunId`, lässt sie laufen.

## Konkrete technische Anforderungen für Schritt 5

### `RunOrchestratorService` (in `src/Geef.Atelier.Web/Services/`)

`internal sealed class RunOrchestratorService : BackgroundService`. Lebt in `Web` (nicht Infrastructure), weil:
- Es ist Teil des Hosting-Lifecycle (`BackgroundService` ist eine Hosting-Abstraktion).
- DI-Registrierung erfolgt in `Web/Program.cs`.
- `Web` darf von `Infrastructure` abhängen, nicht umgekehrt — der Orchestrator nutzt sowohl `IRunPersistenceService` (aus Core) als auch `AtelierPipelineFactory` (aus Infrastructure).

**Konstruktor-Abhängigkeiten:**
- `IServiceScopeFactory` (für Per-Iteration-DbContext-Scopes)
- `IOptions<OrchestratorOptions>` (Polling-Intervall, Concurrency-Limit)
- `ILogger<RunOrchestratorService>`

**`OrchestratorOptions`** (in `src/Geef.Atelier.Core/Configuration/` oder analog):
```csharp
public sealed class OrchestratorOptions
{
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);
    public int MaxConcurrentRuns { get; set; } = 5;
}
```

Konfig-Sektion `"Orchestrator"` in `appsettings.json`.

**`ExecuteAsync`-Logik:**

1. **Crash-Recovery beim Start (vor der Polling-Loop):**
   - Alle `RunEntity` mit `Status=Running` auf `Status=Failed` setzen, `ErrorMessage="Service restarted"`, `CompletedAt=UtcNow`.
   - Atomar via `ExecuteUpdateAsync` (analog zu Token-Akkumulation in Schritt 4).
   - Logging: Anzahl der recovered Runs.

2. **Polling-Loop bis StoppingToken:**
   - Alle `PollingInterval` Pending-Runs holen (`Status=Pending`, sortiert nach `CreatedAt`, mit Limit ≤ verfügbarer Concurrency-Slots).
   - Pro Run: `SemaphoreSlim`-Slot reservieren, dann fire-and-forget `Task` starten der den Run ausführt.
   - Beim Run-Start: Slot reserviert; beim Run-Ende: Slot freigeben.

3. **`ProcessRunAsync(RunEntity run, CancellationToken stoppingToken)`:**
   - Run-Record auf `Status=Running` setzen, `StartedAt=UtcNow` — nicht via `PostgresEventSink`, sondern direkt vor Pipeline-Bau (sonst Race mit `PipelineStartedEvent`-Handler im Sink).
   - **Achtung:** Hier könnte der Sink den `Status=Running`-Update doppelt machen. Architect entscheidet — entweder Orchestrator setzt nicht, oder Sink prüft und überspringt redundante Updates.
   - Pipeline bauen via Schritt-4-Ablauf:
     ```
     using var scope = scopeFactory.CreateAsyncScope();
     var anthropicClient = scope.ServiceProvider.GetRequiredService<IAnthropicClient>();
     var sink = new PostgresEventSink(run.Id, scopeFactory, sinkLogger);
     var runner = AtelierPipelineFactory.Build(anthropicClient, [sink], scope.ServiceProvider);
     ```
   - Mit `try { await runner.RunAsync(run.BriefingText, stoppingToken); } catch { ... }`.
   - Exceptions: 
     - `OperationCanceledException` (StoppingToken) → Run auf `Status=Aborted` mit `ErrorMessage="Service stopping"`.
     - Andere Exceptions → bereits vom Sink via `PipelineFailedEvent` persistiert; nochmals catch-and-log defensiv.

4. **Concurrency-Limit via `SemaphoreSlim(MaxConcurrentRuns)`:**
   - Vor Run-Start: `await semaphore.WaitAsync(stoppingToken)`.
   - Nach Run-Ende (in `finally`): `semaphore.Release()`.
   - Wenn Limit erreicht ist, blockiert das nächste `WaitAsync` — also Polling sollte das auch berücksichtigen (nicht 100 Pending-Runs gleichzeitig holen, wenn `MaxConcurrent=5`).

### Cancellation — DB-Flag-Entscheidung (Architect)

Der Bericht-Sektion 8 empfiehlt: *"`BackgroundService.StoppingToken` + optionales DB-Flag `CancellationRequested`"*. In Schritt 4 wurde dieses Flag nicht in der Migration angelegt. Drei Optionen für Schritt 5:

- **Option α — Migration `Step05Cancellation`** mit neuer Spalte `CancellationRequested boolean default false`. Orchestrator pollt diese zusätzlich und cancelt laufende Runs via per-Run-`CancellationTokenSource`.
- **Option β — Neuer Enum-Wert `RunStatus.CancellationRequested`** (Status-Übergang `Running → CancellationRequested → Aborted`). Migration nötig (Enum-Werte als String).
- **Option γ — Verschieben auf Schritt 7 (UI)** — im Skeleton reicht `StoppingToken`, externe Cancellation kommt mit der UI.

Architect entscheidet. **Empfehlung von hier:** Option γ, weil im Skeleton-Test reicht `StoppingToken`, und die DB-Schema-Erweiterung kann mit der UI-Anforderung zusammen kommen — dann weiß man auch, welche User-Aktion die Cancellation triggert.

### Tests (in `tests/Geef.Atelier.Tests/`)

Vier neue Test-Familien:

**A) `RunOrchestratorPicksUpPendingRun`** — End-to-End-Test:
1. `IRunPersistenceService.CreateRunAsync(...)` → Run mit `Status=Pending`.
2. Service starten (z.B. via `Host.CreateApplicationBuilder` mit Test-DB und `FakeAnthropicClient`).
3. Warten (mit Timeout, max. 10 Sekunden) bis `Status=Completed`.
4. Verifizieren: `FinalText` nicht null, `TokensTotal > 0`, 2 Iterations, Findings vorhanden.

**B) `RunOrchestratorRecoversCrashedRunsOnStart`**:
1. Run direkt in DB anlegen mit `Status=Running` (Simulation eines Crashs).
2. Service starten.
3. Sofort verifizieren (vor erstem Polling-Tick): Run hat `Status=Failed`, `ErrorMessage="Service restarted"`, `CompletedAt` gesetzt.

**C) `RunOrchestratorRespectsConcurrencyLimit`**:
1. `MaxConcurrentRuns=2` konfigurieren.
2. Vier Pending-Runs anlegen.
3. Service starten.
4. Während die Pipeline läuft: prüfen dass nie mehr als 2 Runs gleichzeitig `Status=Running` sind.
5. Am Ende: alle vier `Status=Completed`.

   **Hinweis:** Dieser Test braucht entweder einen langsamen `FakeAnthropicClient` (mit eingebauter Latenz) oder ein Synchronisations-Mechanismus (z.B. `ManualResetEventSlim`-Gates), damit man die Mid-Flight-Concurrency observieren kann. Architect entscheidet.

**D) `RunOrchestratorHonorsStoppingToken`**:
1. Run anlegen.
2. Service starten.
3. Während Run läuft: Service-Host stoppen (`StopAsync` mit Cancellation).
4. Verifizieren: Run hat `Status=Aborted` (oder `Failed` mit erkennbarer Ursache, falls `OperationCanceledException` als Exception gewrappt wird), `ErrorMessage` enthält "stopping" oder "cancel".

**Bestehende Tests:** Alle 15 Tests aus Schritten 1–4 müssen weiter grün bleiben.

### DI-Registrierung (in `Program.cs`)

```csharp
builder.Services.Configure<OrchestratorOptions>(builder.Configuration.GetSection("Orchestrator"));
builder.Services.AddHostedService<RunOrchestratorService>();
```

`IServiceScopeFactory` ist automatisch via DI verfügbar — keine explizite Registrierung nötig.

### `appsettings.json` ergänzen

```json
{
  "Orchestrator": {
    "PollingInterval": "00:00:02",
    "MaxConcurrentRuns": 5
  }
}
```

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` ohne Fehler oder Warnings.
2. `dotnet test` (mit Docker-Daemon für Testcontainers): alle Tests grün — 15 bestehende + 4 neue Orchestrator-Tests.
3. `RunOrchestratorPicksUpPendingRun` zeigt End-to-End-Run-Verarbeitung ohne manuellen Trigger.
4. `RunOrchestratorRecoversCrashedRunsOnStart` zeigt Crash-Recovery.
5. `RunOrchestratorRespectsConcurrencyLimit` zeigt Concurrency-Limit-Respekt.
6. `RunOrchestratorHonorsStoppingToken` zeigt sauberen Service-Shutdown.
7. `geef_architecture.md` existiert (R4-Pflicht).

**AC8 (offen aus D-013/D-015): Real-Anthropic-Test.**
Wenn ein API-Bearer-Key (`sk-ant-api03-...`) als Environment-Variable `Anthropic__ApiKey` zur Test-Zeit verfügbar ist, läuft `AtelierPipelineRealAnthropicTests` mindestens einmal grün durch. Token-Verbrauch und Latenz im Bericht festhalten.
- Wenn kein Bearer-Key verfügbar ist (nur OAuth-Token wie in Schritt 4): Skip mit klarer Doku im Bericht. **Kein Blocker** — die HTTP-Infrastruktur ist durch FakeAnthropicClient-Tests abgedeckt.

## Was du in diesem Schritt NICHT tust

- **Kein `IRunService`** — Application-Service-Schicht für Frontends kommt in Schritt 6.
- **Keine UI** — Schritt 7.
- **Kein MCP** — Schritt 9.
- **Keine UI-initiierte Cancellation** — Skeleton hat nur `StoppingToken` (Empfehlung Option γ oben).
- **Kein PostgreSQL LISTEN/NOTIFY** — einfaches Polling reicht für Skeleton; LISTEN/NOTIFY wäre eleganter, aber nach-Skeleton-Optimierung.
- **Keine Cost-Berechnung** — `CostTotal` bleibt 0.
- **Keine Provider-Änderungen** — `LlmExecutionStep`, `LlmReviewer`, `BriefingGroundingStep`, `MarkdownFinalizer` bleiben unverändert.
- **Keine Sink-Änderungen** — `PostgresEventSink` bleibt wie aus Schritt 4. Falls der Architect für die Status-Doppel-Update-Frage (Orchestrator setzt `Running` + Sink setzt `Running` via `PipelineStartedEvent`) zur Lösung "Sink prüft idempotent" greift, ist das eine minimale Erweiterung — aber bevorzugt: Orchestrator setzt nicht, lässt das den Sink machen.

## Architect-Konsultation (Phase 1.4) — sechs Schwerpunkte

1. **Cancellation-Strategie:** Option α (DB-Spalte mit Migration), β (neuer Enum-Wert), oder γ (verschoben auf Schritt 7)? Empfehlung: γ.
2. **Run-Status-Update beim Run-Start:** Setzt der Orchestrator `Status=Running` direkt, oder lässt er den Sink (`PipelineStartedEvent`) das machen? Welche Reihenfolge ist robuster gegen Race-Conditions?
3. **Polling-vs-Notify:** Polling reicht? Oder sollte für die Pipeline-Test-Latenz `LISTEN/NOTIFY` schon im Skeleton rein? Empfehlung: Polling.
4. **`SemaphoreSlim`-Position:** Im Orchestrator selbst (Pro-Service-Instance) oder als injizierter Singleton-Service (austauschbar für Tests)?
5. **Pro-Run-`CancellationTokenSource`:** Brauchen wir das im Skeleton (für `StoppingToken`-Verkettung)? Oder reicht `stoppingToken` direkt durchgereicht?
6. **Concurrency-Test-Synchronisation:** Wie testet man "max 2 Runs gleichzeitig" sauber? Slow-Mock mit `Task.Delay` oder `ManualResetEventSlim`-Gates?

`geef_architecture.md` prüft Konsistenz mit `docs/02-architecture.md` und allen verbindlichen Realfakten aus D-010 bis D-015.

## Persistenter Abschlussbericht für den Brainstorming-Chat

Bericht nach `docs/reports/step-05-report.md`, gleicher Aufbau wie Schritte 1–4. Wichtig in diesem Schritt:

1. **Was wurde umgesetzt** — Datei-für-Datei.
2. **Annahmen und Abweichungen** — vor allem zu Polling-Intervall-Defaults, Concurrency-Test-Setup, Pro-Run-CancellationToken-Pattern.
3. **Architect-Output** — welcher Invocation-Level, Entscheidungen zu Cancellation/Run-Status/Polling/Semaphore.
4. **Pre-Mortem & Devil's Advocate** — speziell zu Race-Conditions (Orchestrator vs. Sink bei Status-Updates), Connection-Pool-Erschöpfung bei vielen parallelen Runs, Crash-Recovery-Korrektheit (passieren `Running`-Updates erst nach `RunAsync` startet, oder vorher?).
5. **Reviewer-Iterationen** — Tabelle.
6. **Akzeptanzkriterien-Check** — Tabelle, inklusive AC8 (Real-Anthropic-Test).
7. **Beobachtungen** — Polling-Performance, BackgroundService-Lifecycle in Tests, `Host.CreateApplicationBuilder` vs. `WebApplicationFactory`.
8. **Empfehlungen für Schritt 6 (`IRunService`)** — wie sieht die Application-Service-Schnittstelle aus, die jetzt vom Orchestrator + von Frontends gemeinsam genutzt wird?
9. **Status AC8** — Real-Anthropic-Test grün oder Skip mit Begründung.

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- API-Key niemals in source control, niemals in Logs, niemals im Bericht.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.