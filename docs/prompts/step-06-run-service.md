# Claude-Code-Prompt: Schritt 6 — IRunService (Application-Service-Layer)

*Diese Datei ist als Eingabe für Claude Code gedacht.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Schritte 1–5 sind abgeschlossen: Solution + DB + Pipeline + Anthropic-Provider + Persistierung + BackgroundService laufen. Deine Aufgabe ist **Schritt 6 von 10**: die **`IRunService`-Application-Service-Schicht** — die saubere Schnittstelle, die später sowohl von der Web-UI (Schritt 7) als auch vom MCP-Server (Schritt 9) konsumiert wird.

Was sich ändert: Eine neue Schicht (vermutlich neues Projekt `Geef.Atelier.Application`) wird eingezogen. `IRunService` mit vier Methoden: Run absetzen, abfragen, listen, abbrechen. **Cancellation wird in diesem Schritt erstmals echt implementiert** — mit DB-Flag-Migration und Orchestrator-Erweiterung. Was bleibt unverändert: Pipeline-Provider, Anthropic-Client, EventSink-Logik, Run-Persistence-Service.

UI kommt in Schritt 7, Auth in Schritt 8, MCP in Schritt 9. In Schritt 6 reden wir noch nicht mit Frontends — Tests rufen `IRunService` direkt auf.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors, alle Hard Rules.

**Phase-1.4-Hinweis:** Level 2 (`cat /tmp/prompt.txt | claude -p`) hat in den letzten drei Schritten zuverlässig funktioniert. Alternativ Plan-Phase-Integration wie in Schritt 5 (Architect-Antworten als verbindliche Realfakten direkt im Plan-Dokument fixiert, ohne separaten Aufruf) — das ist auch valid laut dem Schritt-5-Bericht. Wähle was effizienter ist.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

Vor Beginn der Implementierung lies in dieser Reihenfolge:

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`CLAUDE.md`** im Repo-Root.
3. **`docs/02-architecture.md`**, besonders das Schichtenbild und der `IRunService`-Abschnitt im Application-Layer.
4. **`docs/03-walking-skeleton-plan.md`**, Abschnitt "Schritt 6".
5. **`docs/05-decisions-log.md`**, alle Einträge **D-010 bis D-016** — besonders D-016 mit den Schritt-5-Realfakten und den **Empfehlungen für Schritt 6**.
6. **`docs/reports/step-05-report.md`**, besonders **Sektion 8 (Empfehlungen für Schritt 6)** mit der bereits skizzierten Interface-Spezifikation.
7. **Aktueller Code im Repo:**
   - `src/Geef.Atelier.Core/Persistence/IRunPersistenceService.cs` — wird vom neuen `IRunService` aufgerufen
   - `src/Geef.Atelier.Web/Services/RunOrchestratorService.cs` — muss für Cancellation erweitert werden (CTS-Dictionary ist bereits da)
   - `src/Geef.Atelier.Infrastructure/Persistence/AtelierDbContext.cs` — wird für DB-Flag erweitert
   - `src/Geef.Atelier.Core/Domain/RunEntity.cs` — bekommt ein neues Property

## In Schritten 1–5 etablierte Realfakten (verbindlich)

Aus D-010, D-012, D-013, D-015, D-016. Zentrale Punkte für Schritt 6:

**Schichten-Disziplin:**
- `Core` ist SDK- und EF-frei. Nur Domain-Records, Interfaces, Konfigurations-POCOs.
- `Infrastructure` enthält EF-Implementierungen und SDK-Adapter.
- `Web` ist Hosting-Layer (Blazor + BackgroundService + DI-Composition).
- **Application** als neuer Layer (vom Architect zu validieren): zwischen Web/MCP und Infrastructure/Core. Implementiert Application-Services, die Frontends konsumieren.

**Bestehende Verträge:**
- `IRunPersistenceService.CreateRunAsync(briefingText, configJson, ct) → Task<Guid>` — bleibt internes Infrastructure-Interface, wird von `IRunService` aufgerufen.
- `RunOrchestratorService` pollt `RunEntity.Status=Pending`. Hat `ConcurrentDictionary<Guid, CancellationTokenSource> _runCts` für in-flight Runs.
- `PostgresEventSink` setzt Status-Übergänge: `Running → Completed/Failed/Aborted`. **Wichtig:** Status `Pending → Running` wird vom Orchestrator atomar gesetzt, *nicht* vom Sink.
- Cancellation in Schritt 5: nur `StoppingToken`. Kein DB-Flag bisher.

## Konkrete technische Anforderungen für Schritt 6

### Neues Projekt: `Geef.Atelier.Application`

**Empfehlung:** Eigenes Projekt, nicht `IRunService` in `Web` ablegen. Begründung:
- MCP-Server (Schritt 9) konsumiert `IRunService` ebenfalls. Wenn `IRunService` in `Web` liegt, müsste `Geef.Atelier.Mcp` von `Geef.Atelier.Web` abhängen — ungesunde Frontend↔Frontend-Kopplung.
- Saubere Layer-Trennung: `Web` und `Mcp` sind Frontend-Adapter, `Application` ist Domain-Logik-Aggregation, `Infrastructure` ist Persistence/Adapter, `Core` ist Domain.

**Architect validiert.** Falls Architect einen guten Grund für Web-Inline-Variante hat, akzeptabel — der Prompt schreibt Option B nicht erzwingend vor, sondern als Default. Im Bericht klar dokumentieren welche Variante gewählt wurde und warum.

**Projektstruktur (bei Option B):**
```
src/Geef.Atelier.Application/
├── Geef.Atelier.Application.csproj    // referenziert Core; KEIN Verweis auf Infrastructure!
└── Runs/
    ├── IRunService.cs
    └── RunService.cs                   // Implementierung
```

`IRunService` darf nur `Core`-Typen (Entities, Records) und Standard-.NET-Typen verwenden. Implementierung ruft `IRunPersistenceService` (Core-Interface) auf und liest direkt aus `AtelierDbContext` für Queries.

**Aber Moment:** Wenn `Application` direkt `AtelierDbContext` verwendet, hängt es von `Infrastructure` ab. Das ist nicht ideal — `Application` sollte gegen Abstraktionen reden, nicht gegen EF Core.

**Vorschlag:** Repository-Pattern. `IRunRepository` (in Core) mit Read-Methoden (`GetByIdAsync`, `ListAsync`, `SetCancellationRequestedAsync`); Implementierung (`AtelierDbContext`-basiert) in Infrastructure. `RunService` ruft sowohl `IRunPersistenceService` (Create) als auch `IRunRepository` (Read/Update) auf.

**Aber:** Das wird viel Plumbing für eine kleine App. Architect entscheidet — wenn er `Application → Infrastructure → Core` als pragmatisch akzeptabel sieht (Onion-Architecture-Pragmatismus), ist das OK fürs Skeleton. Aber dann muss `Application` Infrastructure als ProjectReference haben.

### `IRunService`-Interface

Gemäß Sektion 8 des Schritt-5-Berichts:

```csharp
public interface IRunService
{
    Task<Guid> SubmitRunAsync(
        string briefingText,
        string configJson,
        CancellationToken cancellationToken = default);

    Task<RunEntity?> GetRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RunEntity>> ListRunsAsync(
        int limit = 20,
        RunStatus? statusFilter = null,
        CancellationToken cancellationToken = default);

    Task<bool> CancelRunAsync(
        Guid runId,
        CancellationToken cancellationToken = default);
}
```

**Method-Semantik:**

- **`SubmitRunAsync`** ruft intern `IRunPersistenceService.CreateRunAsync` und gibt die neue `runId` zurück. Validierungen: `briefingText` non-empty, `configJson` valides JSON (oder leer string für "Defaults"). Wirft `ArgumentException` bei invalidem Input. Architect entscheidet ob `configJson` `string` oder typisierter Wrapper sein soll — für Skeleton reicht `string`.

- **`GetRunAsync`** liest den `RunEntity` aus der DB inklusive Iterations und Findings (Architect entscheidet: Eager Loading via `.Include()` oder zwei Queries). Gibt `null` zurück wenn nicht gefunden.

- **`ListRunsAsync`** liest die letzten N Runs nach `CreatedAt desc`, optional gefiltert nach Status. Iterations und Findings *nicht* mitladen — die Listenansicht braucht das nicht.

- **`CancelRunAsync`** setzt `RunEntity.CancellationRequested = true`. Gibt `true` zurück wenn Status `Pending` oder `Running` war (Cancellation möglich), `false` wenn der Run bereits terminal ist (`Completed/Failed/Aborted`). Atomar via `ExecuteUpdateAsync`.

### DB-Schema-Erweiterung — neue Migration

Neue EF-Migration `Step06Cancellation` mit:
- `RunEntity.CancellationRequested boolean default false NOT NULL`
- Indizierung **nicht nötig** — wird nur pro-Run gelesen, kein Bulk-Filter.

`RunEntity` Domain-Record entsprechend erweitern.

### `RunOrchestratorService`-Erweiterung — Cancellation-Reaktion

Der Orchestrator muss in `ProcessRunAsync` periodisch das `CancellationRequested`-Flag aus der DB lesen und bei `true` die per-Run-CTS signalisieren. Das CTS-Dictionary (`_runCts`) ist bereits aus Schritt 5 vorbereitet.

Konkret: parallel zur Pipeline läuft ein zweiter `Task`, der alle ~1s die Run-Row liest und bei `CancellationRequested=true` die zugehörige CTS cancelt. Die Pipeline empfängt das via verkettetem `CancellationToken`. Bei Cancellation läuft das Catch-Block-Logik aus Schritt 5 — Status wird auf `Aborted` gesetzt.

**Achtung:** Der Cancellation-Watcher-Task muss sauber beendet werden wenn die Pipeline durchläuft (kein Leak). Pattern: gemeinsame `linkedCts`, die beim Pipeline-Ende oder beim Cancellation-Event canceled wird.

**Architect klärt die Implementierungsform:**
- Pattern A: Watcher-Task pro Run-Pipeline-Aufruf, gemeinsame `LinkedCancellationTokenSource`.
- Pattern B: Zentraler Watcher im Orchestrator-Service, der alle Running-Runs auf einmal beobachtet (eine Query statt N).

Pattern B ist effizienter bei vielen parallelen Runs, Pattern A ist einfacher. Für Skeleton mit `MaxConcurrentRuns=5` reicht Pattern A.

### Tests

**Neue Test-Familie `Geef.Atelier.Tests/Application/`:**

**A) `RunServiceSubmitsAndQueries`:**
1. `SubmitRunAsync(briefing, configJson)` → liefert `runId`.
2. `GetRunAsync(runId)` → liefert `RunEntity` mit `Status=Pending`, `BriefingText` korrekt.
3. Service starten (via `OrchestratorTestHost` aus Schritt 5), warten bis Status=Completed.
4. `GetRunAsync(runId)` erneut → liefert Run mit Iterations und Findings.

**B) `RunServiceListsRecentRuns`:**
1. Drei Runs absetzen mit `SubmitRunAsync`.
2. `ListRunsAsync(limit=2)` → liefert die zwei neuesten, sortiert nach `CreatedAt desc`.
3. `ListRunsAsync(statusFilter=Completed)` → liefert nur Completed-Runs.

**C) `RunServiceCancelsRunningRun`:**
1. Run via `SubmitRunAsync` absetzen.
2. Orchestrator startet, `GatedFakeAnthropicClient` hält die Pipeline.
3. `CancelRunAsync(runId)` aufrufen → `true` zurück, `CancellationRequested=true` in DB.
4. Gate öffnen, Pipeline läuft kurz weiter und sieht Cancellation.
5. Warten bis `Status=Aborted`, `ErrorMessage` zeigt Cancellation-Grund.

**D) `RunServiceCancelReturnsFalseForTerminalRun`:**
1. Run absetzen, durchlaufen lassen bis `Status=Completed`.
2. `CancelRunAsync(runId)` → `false` zurück. `CancellationRequested` bleibt `false`.

**E) `RunServiceValidatesInputs`:**
1. `SubmitRunAsync("", configJson)` → wirft `ArgumentException`.
2. `SubmitRunAsync(briefing, "not-json")` → wirft `ArgumentException` (oder ähnliche klare Exception).

**Bestehende Tests:** Alle 19 Tests aus Schritten 1–5 müssen weiter grün bleiben. Insbesondere `RunOrchestratorRespectsConcurrencyLimit` und `RunOrchestratorHonorsStoppingToken` — letzteres ist jetzt mit dem neuen Cancellation-Mechanismus verschränkt und könnte fragil werden, wenn der Watcher-Task nicht sauber sterben kann.

### DI-Registrierung (in `Program.cs`)

```csharp
builder.Services.AddScoped<IRunService, RunService>();
// Plus ggf. IRunRepository falls Architect dafür stimmt
```

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` ohne Fehler oder Warnings.
2. `dotnet test` (mit Docker-Daemon für Testcontainers): alle Tests grün — 19 bestehende + neue Application-Tests.
3. `RunServiceSubmitsAndQueries` zeigt End-to-End-Flow via `IRunService`.
4. `RunServiceListsRecentRuns` zeigt Listing mit Filter.
5. `RunServiceCancelsRunningRun` zeigt funktionierende Cancellation: DB-Flag wird gesetzt, Orchestrator reagiert, Run endet als `Aborted`.
6. `RunServiceCancelReturnsFalseForTerminalRun` zeigt Idempotenz.
7. `RunServiceValidatesInputs` zeigt Input-Validierung.
8. `geef_architecture.md` existiert (R4-Pflicht).

**AC9 (verschoben aus AC8): Real-Anthropic-Test.**
Wenn ein API-Bearer-Key (`sk-ant-api03-...`) als Environment-Variable `Anthropic__ApiKey` zur Test-Zeit verfügbar ist, läuft `AtelierPipelineRealAnthropicTests` mindestens einmal grün. Skip mit klarer Doku falls nicht. **Empfehlung:** Wenn AC9 in Schritt 6 wieder skipped wird, sollte der User vor Schritt 9 (MCP) explizit über die Verifikation entscheiden — sonst riskieren wir, dass MCP gegen ein funktionsuntüchtiges Backend gebaut wird.

## Was du in diesem Schritt NICHT tust

- **Keine UI** — Schritt 7.
- **Keine Auth** — Schritt 8.
- **Kein MCP** — Schritt 9.
- **Keine Provider-Änderungen** — `LlmExecutionStep`, `LlmReviewer`, etc. bleiben unverändert.
- **Keine `Cost`-Berechnung** — `CostTotal` bleibt 0.
- **Kein Pagination-Pattern** — `ListRunsAsync` hat nur `limit`, keine Cursor/Offset. Echte Pagination kommt mit UI-Schritt 7 falls nötig.
- **Kein Streaming/Live-Status** — `GetRunAsync` ist Snapshot. Live-Updates via SignalR sind Schritt-7-Material.

## Architect-Konsultation (Phase 1.4) — fünf Schwerpunkte

1. **Projekt-Layout:** `Application`-Layer als eigenes Projekt (Option B) oder `IRunService` in `Web` (Option A)? Begründung mit Blick auf MCP-Schritt 9.
2. **Repository-Pattern oder direkter `AtelierDbContext`-Zugriff aus Application?** Mit Repository: sauberer aber mehr Plumbing. Ohne: pragmatischer aber `Application → Infrastructure`-Kopplung.
3. **Cancellation-Watcher-Pattern:** Pro-Run-Watcher (Pattern A) oder zentraler Watcher (Pattern B)?
4. **DB-Polling-Intervall für Cancellation-Watcher:** 500ms? 1s? Konfigurierbar?
5. **`configJson` als Typ:** `string` (flexibel, später typisierbar) oder schon jetzt `RunConfiguration`-Record (typsicher)?

`geef_architecture.md` prüft Konsistenz mit `docs/02-architecture.md` und allen verbindlichen Realfakten aus D-010 bis D-016.

## Persistenter Abschlussbericht für den Brainstorming-Chat

Bericht nach `docs/reports/step-06-report.md`, gleicher Aufbau wie Schritte 1–5. Wichtig in diesem Schritt:

1. **Was wurde umgesetzt** — Datei-für-Datei, inklusive ob `Application`-Projekt angelegt wurde.
2. **Annahmen und Abweichungen** — vor allem zum Application-Layer-Layout, Repository-Entscheidung, Cancellation-Watcher-Form.
3. **Architect-Output** — alle fünf Pflichtfragen.
4. **Pre-Mortem & Devil's Advocate** — speziell zu: Watcher-Task-Leaks, Race zwischen Cancel-Request und Pipeline-Abschluss, Idempotenz bei mehrfachem `CancelRunAsync`.
5. **Reviewer-Iterationen** — Tabelle.
6. **Akzeptanzkriterien-Check** — Tabelle, inklusive AC9.
7. **Beobachtungen** — wie greift `IRunService` mit `RunOrchestratorService` zusammen? Konsistenz mit `IRunPersistenceService`?
8. **Empfehlungen für Schritt 7 (UI)** — welche `IRunService`-Methoden wird die UI direkt aufrufen? Welche SignalR-Verkabelung erwartet?
9. **Status AC9** — Real-Anthropic-Test.

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- API-Key niemals in source control, niemals in Logs, niemals im Bericht.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.