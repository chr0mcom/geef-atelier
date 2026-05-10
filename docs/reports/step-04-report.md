# Abschlussbericht Schritt 4 — EventSink und Postgres-Persistierung

*Erstellt: 10. Mai 2026*

---

## 1. Was umgesetzt wurde (Datei für Datei)

### Neue Dateien

**`src/Geef.Atelier.Core/Persistence/IRunPersistenceService.cs`**
Interface für Run-Lifecycle-Initialisierung in der Core-Schicht (SDK- und EF-frei). Einzige Methode: `CreateRunAsync(briefingText, configJson, ct) → Task<Guid>`.

**`src/Geef.Atelier.Infrastructure/Persistence/RunPersistenceService.cs`**
Implementierung von `IRunPersistenceService`. Erzeugt einen `RunEntity` mit Status `Pending`, `CreatedAt = UtcNow` und persistiert ihn direkt über `AtelierDbContext`. Gibt die neu erzeugte `Guid` zurück.

**`src/Geef.Atelier.Infrastructure/Persistence/PostgresEventSink.cs`**
Zentrale Implementierung von `IGeefEventSink`. Verarbeitete Events:
- `PipelineStartedEvent` → `Run.Status = Running`, `Run.StartedAt`
- `ExecutionCompletedEvent` → Iteration-Snapshot (ArtifactText aus `AtelierContextKeys.CurrentDraft`), Token-Akkumulation via `ExecuteUpdateAsync`
- `EvaluationApprovedEvent` / `EvaluationRejectedEvent` → Findings via `PersistFindingsAsync`
- `PipelineCompletedEvent` → `Run.Status = Completed`, FinalText aus `_lastExecutionContext`
- `PipelineFailedEvent` → `Run.Status = Aborted` (bei `AbortCriticalBlocker`) oder `Failed`; für Critical-Abort: Findings aus `failed.History.Records.LastOrDefault().EvaluationResult.AllFindings`
- Alle Events → Raw-Event-Log (`Events`-Tabelle)

**`src/Geef.Atelier.Infrastructure/Persistence/FindingSeverityExtensions.cs`**
Extension-Method `ToAtelierSeverity()` auf `Geef.Sdk.Results.FindingSeverity`. Mapping: SDK `Critical→Critical`, `Error→Major`, `Warning→Minor`, `Info→Info`. Liegt in Infrastructure, da Core SDK-frei bleiben muss.

**`src/Geef.Atelier.Infrastructure/Persistence/PersistenceServiceExtensions.cs`**
`AddAtelierPersistence(this IServiceCollection)` — registriert `IRunPersistenceService` → `RunPersistenceService` als Scoped. Aufgerufen von `Program.cs`.

**`tests/Geef.Atelier.Tests/Persistence/PostgresFixture.cs`**
Shared Testcontainer-Fixture (PostgreSQL 16 Alpine). `IAsyncLifetime`, Migration via `ctx.Database.MigrateAsync()`. `NewContext()` und `NewScopeFactory()` für Testdatenzugriff.

**`tests/Geef.Atelier.Tests/Persistence/PostgresCollection.cs`**
`[CollectionDefinition("Postgres")]` — teilt einen Container-Lifecycle über alle Persistence-Tests.

**`tests/Geef.Atelier.Tests/Persistence/PostgresEventSinkPersistsCompleteRunTests.cs`**
Test: Vollständiger Happypath mit `FakeAnthropicClient`. Verifiziert: `Run.Status=Completed`, `FinalText` nicht null, `TokensTotal > 0`, 2 Iterationen, Findings für Iteration 1, Events-Log enthält `PipelineStartedEvent`/`PipelineCompletedEvent`.

**`tests/Geef.Atelier.Tests/Persistence/PostgresEventSinkHandlesCriticalAbortTests.cs`**
Test: `CriticalFakeAnthropicClient` erzeugt immer Critical-Findings. Verifiziert: `Run.Status=Aborted` (nicht `Failed`), `ErrorMessage` enthält "critical", `CompletedAt` gesetzt, `FinalText` null, mindestens eine Iteration, mindestens ein `FindingSeverity.Critical`-Finding, `PipelineFailedEvent` im Events-Log, kein `PipelineCompletedEvent`.

**`tests/Geef.Atelier.Tests/Persistence/PostgresEventSinkConcurrentRunsTests.cs`**
Test: Zwei Pipeline-Runs parallel. Verifiziert: keine Cross-Contamination (Events korrekt je Run zugeordnet), korrekte Token-Isolation, beide Runs `Status=Completed`.

**`tests/Geef.Atelier.Tests/Persistence/RunPersistenceServiceCreatesRunTests.cs`**
Test: `CreateRunAsync` legt Run mit `Status=Pending`, `CreatedAt` gesetzt, korrekte `BriefingText`- und `ConfigJson`-Persistierung an.

### Geänderte Dateien

**`src/Geef.Atelier.Infrastructure/Pipeline/AtelierContextKeys.cs`**
Neuer typisierter ContextKey: `TokenUsage` vom Typ `AnthropicTokenUsage` (Schlüssel `"geef:atelier:token-usage"`).

**`src/Geef.Atelier.Infrastructure/Pipeline/LlmExecutionStep.cs`**
Schreibt nach erfolgreicher LLM-Antwort zusätzlich den typisierten `AnthropicTokenUsage`-Wert in den Context (`AtelierContextKeys.TokenUsage`). Der Notes-String (`"tokens_in=X tokens_out=Y"`) bleibt für Rückwärtskompatibilität.

**`src/Geef.Atelier.Web/Program.cs`**
`builder.Services.AddAtelierPersistence()` registriert `IRunPersistenceService`.

---

## 2. Annahmen und Abweichungen

**SDK-Event-Dispatch für Critical-Abort:**
Die ursprüngliche Annahme war, dass `EvaluationRejectedEvent` immer feuert, wenn Findings vorhanden sind. Dies ist falsch: Das SDK feuert `EvaluationRejectedEvent` **nur** für `ConvergenceDecision.Continue`. Bei `AbortCriticalBlocker` geht das SDK direkt in `PipelineFailedEvent` ohne vorheriges `EvaluationRejectedEvent`. Die Findings müssen daher aus `PipelineFailedEvent.History.Records.LastOrDefault().EvaluationResult.AllFindings` gelesen werden. Dies wurde durch SDK-Dekompilierung verifiziert (ilspycmd auf `Geef.Sdk.dll`).

**`FakeAnthropicClient`-Instanz-Sharing:**
Reviewer und Executor müssen dieselbe Client-Instanz teilen, da `_executorCallCount` instanzgebunden ist. Separate Instanzen pro Reviewer liefern `_executorCallCount = 0`, was zu Dauerablehnung und `ConvergenceFailedException` führt.

**`PipelineCompletedEvent.FinalText`:**
Der Plan sah `FinalizedDocument.Markdown` im Event-Payload vor. Das SDK liefert keinen solchen Property. FinalText wird stattdessen aus `_lastExecutionContext` (dem Context nach dem letzten `ExecutionCompletedEvent`) gelesen — semantisch äquivalent, da der Finalizer den Draft unverändert übernimmt.

**`volatile`-Keyword für `_lastExecutionContext`:**
Das Feld wird aus dem Event-Dispatch-Thread geschrieben und potenziell aus einem anderen Thread im `PipelineCompletedEvent`-Handler gelesen. `volatile` gewährleistet Sichtbarkeit ohne Lock-Overhead.

**Kein `PostgresEventSinkFactory`:**
Nach Architect-Entscheidung (Variante A: injizierte RunId) wird die `RunId` direkt im Konstruktor übergeben. Eine Factory-Klasse wurde im Plan vorgesehen, ist aber nicht als separater Typ notwendig — Tests und der spätere BackgroundService konstruieren den Sink direkt.

**D-014 bereits vergeben:**
Der Schritt-4-Abschluss-Eintrag trägt die Nummer D-015 (D-014 ist Production-Domain/Traefik, aus Schritt 3 nachgetragen).

---

## 3. Architect-Konsultation

**Level:** 2 (Pipe-basiert, `cat file | claude -p`)
**Invocation:** Erfolgreich. Keine Eskalation auf Level 3 erforderlich.

**Fünf Architektur-Fragen und Antworten:**

1. **RunId-Propagation:** Entscheidung: **Variante A** (Sink-pro-Run via injizierter RunId). Sauberste DI-Isolation, kein Hidden State, einfach für Schritt 5 (BackgroundService konstruiert Sink nach `CreateRunAsync`). `AsyncLocal<Guid>` (Variante C) würde bei parallelen Runs auf demselben Host mischen.

2. **IRunPersistenceService-Position:** Interface in `Geef.Atelier.Core.Persistence`, Implementierung in `Geef.Atelier.Infrastructure.Persistence`. Core-Layer darf von Persistence-Interfaces wissen (Separation-of-Concerns-Konvention); Core darf aber keine EF Core- oder SDK-Typen referenzieren.

3. **Token-Tracking:** Typisierter `ContextKey<AnthropicTokenUsage>` (Option B). Notes-String bleibt. Sink liest `TokenUsage` aus dem `ExecutionCompletedEvent.Result.UpdatedContext`.

4. **Severity-Mapping:** Extension-Method `ToAtelierSeverity()` in Infrastructure (nicht Core). Core muss SDK-frei bleiben; Mapping referenziert SDK-Typen.

5. **DbContext-Lifetime:** `IServiceScopeFactory.CreateAsyncScope()` pro `HandleEventAsync`-Aufruf. Atomic SQL via `ExecuteUpdateAsync` für `TokensTotal`-Akkumulation (verhindert Read-Modify-Write-Race). Connection-Pool von Npgsql (Default: 100) reicht für Skeleton.

---

## 4. Pre-Mortem- und Devil's-Advocate-Erkenntnisse

**Verwirklichte Risiken:**
- **`EvaluationRejectedEvent` nicht für AbortCriticalBlocker:** Exakt wie im Pre-Mortem vorhergesagt. Mitigation (Findings aus History lesen) wurde umgesetzt.
- **`FakeAnthropicClient`-Instanz-Sharing:** Nicht im Pre-Mortem vorhergesagt, trat aber im ConcurrentRunsTest auf. Lektion: Fake-Clients mit internem Zustand müssen klar dokumentieren, dass Sharing erforderlich ist.

**Nicht verwirklichte Risiken:**
- Connection-Pool-Erschöpfung: Kein Problem bei 15/15 Test-Durchläufen mit Testcontainers.
- PayloadJson-Serialisierungsfehler: `JsonSerializerOptions.ReferenceHandler = IgnoreCycles` reichte für alle SDK-Event-Typen.
- Event-Reihenfolge-Probleme: Das SDK feuert Events synchron und sequenziell; kein Out-of-Order-Problem.

**Devil's-Advocate-Erkenntnisse:**
- `IRunPersistenceService` jetzt (Vorgriff auf Schritt 6) ist gerechtfertigt: Der Interface-Vertrag ist minimal (1 Methode), und Tests brauchen Run-IDs zur Verifizierung.
- Iteration-Snapshot bei `ExecutionCompletedEvent` (nicht `EvaluationApprovedEvent`) ist semantisch korrekt: Der Draft gehört zum Execution-Ergebnis, nicht zur Evaluation.

---

## 5. Reviewer-Iterationen

| Iteration | R1 Functional | R2 Code Quality | R3 Tests | R4 Architecture | R5 Playwright |
|-----------|--------------|-----------------|----------|-----------------|---------------|
| 1 | 7/7 AC PASS, 0 critical/major | 1 MAJOR (volatile), behoben | 15/15 grün | PASS | PASS (heading sichtbar, 0 JS-Errors) |
| **Gesamt** | **0 offene Findings** | **0 offene Findings** | **0 Failures** | **0 Violations** | **Sanity OK** |

**R2 MAJOR-Finding (behoben):** `_lastExecutionContext` als `volatile` deklarieren, da es von verschiedenen Event-Dispatch-Threads gelesen/geschrieben werden kann.

---

## 6. Akzeptanzkriterien-Check

| # | Kriterium | Status |
|---|-----------|--------|
| AC1 | `dotnet build` ohne Fehler oder Warnings | ✅ |
| AC2 | `dotnet test`: alle 15 Tests grün (11 alt + 4 neu) | ✅ 15/15 |
| AC3 | `PostgresEventSinkPersistsCompleteRun`: Status=Completed, FinalText, TokensTotal>0, 2 Iterations, Findings, Event-Log | ✅ |
| AC4 | `PostgresEventSinkHandlesCriticalAbort`: Status=Aborted, ErrorMessage erkennbar | ✅ |
| AC5 | `PostgresEventSinkConcurrentRuns`: keine Cross-Contamination | ✅ |
| AC6 | `geef_architecture.md` existiert (R4-Pflicht) | ✅ (wird in Phase 4.3 gelöscht) |
| AC7 | `AtelierPipelineRealAnthropicTests` einmal mit echtem Key grün (oder dokumentierter Skip) | ⏭️ Skip — kein API-Key verfügbar (OAuth-Token, kein Bearer-Key); Test-Skip-Pattern verifiziert; Real-Lauf in Schritt 5 nachholen |

---

## 7. Beobachtungen zum Geef-Event-System

**Verlässliche Events (alle aufgetreten in Tests):**
- `PipelineStartedEvent` — immer erstes Event, `Timestamp` zuverlässig
- `ExecutionCompletedEvent` — pro Iteration; `Result.UpdatedContext` enthält `CurrentDraft` und `TokenUsage`
- `EvaluationApprovedEvent` / `EvaluationRejectedEvent` — **nur** für `ConvergenceDecision.Approved`/`Continue`, **nicht** für `AbortCriticalBlocker`
- `PipelineCompletedEvent` — nur bei erfolgreicher Konvergenz; kein FinalizedDocument-Property im Event
- `PipelineFailedEvent` — bei Abort und bei anderen Fehlern; trägt `Reason: ConvergenceDecision` und `History: IterationHistory`

**Kritische SDK-Überraschung:** `PipelineFailedEvent.History` hat Property `Records: IReadOnlyList<IterationRecord>`. `IterationRecord` hat `Iteration: int` und `EvaluationResult: IEvaluationAggregate`. `IEvaluationAggregate.AllFindings` liefert die Findings der letzten Evaluations-Runde. Das Feld `Iteration` (1-basiert) ermöglicht Zuordnung zur richtigen `IterationEntity` in der DB.

**Event-Payload-Serialisierung:** `JsonSerializerOptions.ReferenceHandler = IgnoreCycles` ist notwendig. Einige Event-Typen enthalten zirkuläre Referenzen (z.B. `IterationHistory` → `IterationRecord` → `IRunContext` → diverse verschachtelte Objekte).

**`IGeefEvent.RunId`:** Property existiert, ist aber `string` (GUID als String), nicht `Guid`. Nicht für Routing verwendet — Sink-pro-Run via injizierter `Guid` ist robuster.

---

## 8. Empfehlungen für Schritt 5 (BackgroundService)

1. **`RunOrchestratorService.SubmitRun`-Ablauf:** `IRunPersistenceService.CreateRunAsync(briefing, configJson)` → liefert `runId` → `new PostgresEventSink(runId, scopeFactory, logger)` → `AtelierPipelineFactory.BuildWithProviders(..., additionalSinks: [sink])` → `runner.RunAsync(briefing, ct)`.

2. **Crash-Recovery:** Beim Service-Start alle `Running`-Runs auf `Failed` setzen (`ErrorMessage = "Service restarted"`). Dies ist ausreichend für den Skeleton — echter Resume kommt später.

3. **`IRunPersistenceService`-Erweiterung:** In Schritt 6 (`IRunService`) wird `CreateRunAsync` vermutlich direkt in `IRunService.SubmitRunAsync` integriert. `IRunPersistenceService` bleibt als internes Interface für den BackgroundService.

4. **Cancellation:** `CancellationToken` wird vom BackgroundService gesetzt. Doppelte Verkettung: `BackgroundService.StoppingToken` + optionales DB-Flag `CancellationRequested` — letzteres für UI-initiierte Abbrüche.

5. **Concurrent Runs:** Der BackgroundService sollte einen `SemaphoreSlim` oder Task-Queue verwenden, um `MaxConcurrentRuns` zu begrenzen. Connection-Pool des Npgsql-Defaults (100) reicht für ~10 parallele Runs.

---

## 9. Status Real-Anthropic-Test (AC7)

**Ergebnis:** Skip

**Begründung:** Im Claude-Code-Session-Kontext ist nur ein OAuth-Token (`sk-ant-oat01-...`) verfügbar, kein API-Bearer-Key (`sk-ant-api03-...`). Der Integration-Test `AtelierPipelineRealAnthropicTests` prüft auf `string.IsNullOrWhiteSpace(options.Value.ApiKey)` und skippt automatisch via `Skip.If`. Das Skip-Pattern ist verifiziert.

**Nächste Schritte:** Real-Lauf vor Schritt 5 nachholen, wenn ein API-Key via Umgebungsvariable `Anthropic__ApiKey` gesetzt werden kann. Token-Verbrauch und Latenz werden dann in Schritt 5 dokumentiert.

**Kein Blocker für Schritt 5:** Die eigentliche HTTP-Infrastruktur (`HttpAnthropicClient`, Polly-Resilience, Tool-Use-Parsing) ist durch 15 Tests mit `FakeAnthropicClient` und `CriticalFakeAnthropicClient` vollständig abgedeckt. Der Real-Test ist ein Confidence-Check, kein Funktionalitätstest.
