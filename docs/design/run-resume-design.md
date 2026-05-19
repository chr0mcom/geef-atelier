# Design: Run fortsetzen (Resume Run)

**Letzte Aktualisierung:** 2026-05-18
**Status:** Freigegeben — bereit zur Implementierung
**Ziel-Branch:** `feat/run-resume`

---

## Kontext und Motivation

Wenn ein laufender Run durch einen Deploy-Neustart (Status: `Aborted`) oder durch
Max-Iterations-Überschreitung (Status: `Failed` mit ConvergenceFailedException) endet, ist
der bereits produzierte Text verloren oder nicht weiter verarbeitbar — obwohl oft ein gut
brauchbarer Entwurf in der letzten `IterationEntity` steckt. Das Feature ermöglicht es,
einen solchen Run fortzusetzen, ohne von vorn beginnen zu müssen.

---

## Design-Entscheidungen (bestätigt)

| Entscheidung | Wert |
|---|---|
| Fortsetzen-Modi | „Mit letztem Entwurf" (Seed) + „Komplett neu" (Clean Retry) — User wählt pro Run |
| Angezeigte Status | `Aborted` und `Failed` |
| Run-Identität | Neuer Run, verknüpft mit `ParentRunId` → Original bleibt unverändert |
| Max-Iterations | User setzt Wert im Dialog (vorausgefüllt aus effektivem Wert des Eltern-Runs) |
| Migrations-Nummer | `Step23RunResume` (nächste freie nach Step22Finalizers) |

---

## A. Data Layer

### Migration `Step23RunResume`

```sql
ALTER TABLE "Runs" ADD "ParentRunId" uuid NULL;
ALTER TABLE "Runs" ADD "SeedDraftText" text NULL;
CREATE INDEX "IX_Runs_ParentRunId" ON "Runs"("ParentRunId");
```

### RunEntity — neue Properties

```csharp
/// <summary>
/// Set when this run was created as a resume of an existing run.
/// The parent run is never modified; its status stays Aborted or Failed.
/// </summary>
public Guid? ParentRunId { get; init; }

/// <summary>
/// The artifact text of the last completed iteration of the parent run,
/// injected as seed draft for the first pipeline iteration.
/// Null for clean-retry resumes and for runs that were not resumed.
/// </summary>
public string? SeedDraftText { get; init; }
```

### EF Core Konfiguration (RunEntityConfiguration)

```csharp
builder.Property(r => r.ParentRunId).IsRequired(false);
builder.Property(r => r.SeedDraftText).IsRequired(false);
builder.HasIndex(r => r.ParentRunId).HasDatabaseName("IX_Runs_ParentRunId");
```

---

## B. Application Layer

### ResumeOptions

```csharp
/// <summary>Parameters for resuming a previously aborted or failed run.</summary>
public sealed record ResumeOptions(
    Guid ParentRunId,

    /// <summary>
    /// True: inject the last iteration's ArtifactText as seed draft.
    /// False: start fresh with the same briefing (clean retry).
    /// </summary>
    bool UseSeedDraft,

    /// <summary>
    /// When non-null, overrides the convergence policy's MaxIterations for the resumed run.
    /// </summary>
    int? MaxIterationsOverride
);
```

### IRunService — neue Methode

```csharp
/// <summary>
/// Creates a new run that resumes a previously aborted or failed run.
/// Returns the ID of the newly created run.
/// Throws <see cref="InvalidOperationException"/> if the parent run does not exist,
/// does not belong to <paramref name="requestingUsername"/> (when non-null),
/// or is not in a resumable state (Aborted or Failed).
/// </summary>
Task<Guid> ResumeRunAsync(
    ResumeOptions options,
    string? requestingUsername,
    CancellationToken cancellationToken = default);
```

### RunService.ResumeRunAsync — Logik

1. Eltern-Run laden via `repository.GetByIdAsync`.
2. Guard: `Status` muss `Aborted` oder `Failed` sein — sonst `InvalidOperationException`.
3. Guard: Ownership-Check wie in allen anderen `IRunService`-Methoden.
4. Wenn `UseSeedDraft`: letzte `IterationEntity` laden (höchste `IterationNumber`),
   deren `ArtifactText` als `seedDraft` verwenden. Wenn keine Iterationen vorhanden:
   `UseSeedDraft` effektiv ignorieren (kein Seed, trotzdem fortsetzen).
5. `CrewSnapshot` für den neuen Run vorbereiten:
   - Eltern-Run `CrewSnapshot`-JSON via `CrewSnapshot.Deserialize` deserialisieren.
   - Wenn `MaxIterationsOverride` gesetzt: neuen Snapshot mit angepasster `ConvergenceOverride`
     erzeugen: `snapshot with { ConvergenceOverride = (snapshot.ConvergenceOverride ?? new()) with { MaxIterations = MaxIterationsOverride } }`.
     Dann erneut serialisieren.
   - Sonst: originales `CrewSnapshot`-JSON unverändert verwenden.
   - **Hinweis:** `ConfigJson` ist Audit-Metadata und wird vom Orchestrator nicht für die
     Pipeline-Konfiguration genutzt — MaxIterations muss im Snapshot stehen.
6. `persistence.CreateResumedRunAsync` aufrufen (neue Methode, s.u.) mit:
   - `briefingText` = Eltern-Run `BriefingText`
   - `configJson` = Eltern-Run `ConfigJson` (unverändert — nur Audit-Metadaten)
   - `createdByUser` = Eltern-Run `CreatedByUser`
   - `crewTemplateName` = Eltern-Run `CrewTemplateName`
   - `crewSnapshotJson` = (ggf. mit MaxIterations-Override modifiziertes) Snapshot-JSON
   - `parentRunId` = Eltern-Run `Id`
   - `seedDraftText` = `seedDraft` (oder null)
7. Neue RunId zurückgeben.

### IRunPersistenceService — neue Methode

```csharp
Task<Guid> CreateResumedRunAsync(
    string briefingText,
    string configJson,
    string? createdByUser,
    string? crewTemplateName,
    string? crewSnapshotJson,
    Guid parentRunId,
    string? seedDraftText,
    CancellationToken cancellationToken = default);
```

Die Implementierung in `RunPersistenceService` setzt alle bestehenden Felder wie
`CreateRunAsync`, ergänzt `ParentRunId` und `SeedDraftText`.

---

## C. Infrastructure / Pipeline Layer

### Neuer Context Key

```csharp
// In AtelierContextKeys:
/// <summary>
/// Injected by SeedDraftGroundingStep for resume runs. Contains the artifact text of the
/// last completed iteration of the parent run. ProfileBasedExecutor uses it on iteration 1
/// to prime the LLM with a prior draft rather than generating from scratch.
/// </summary>
public static readonly ContextKey<string> SeedDraft = new("geef:atelier:seed-draft");
```

### Neuer Grounding Step: SeedDraftGroundingStep

```csharp
internal sealed class SeedDraftGroundingStep(string seedDraftText) : IGroundingStep
{
    public Task<GroundingResult> RunAsync(string input, CancellationToken cancellationToken)
    {
        var context = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, input)
            .Set(AtelierContextKeys.SeedDraft, seedDraftText);

        return Task.FromResult(new GroundingResult { Context = context, Notes = [] });
    }
}
```

Identisches Pattern wie `AdvisorContextGroundingStep`.

### ProfileBasedExecutor — Iteration-1-Anpassung

Wenn `iter == 1` und `context.TryGet(AtelierContextKeys.SeedDraft, out var seedDraft)`:

```csharp
userPrompt = $"""
    Briefing:
    {brief}

    Previous draft (from an interrupted run — revise and improve it):
    {seedDraft}

    Revise the draft to better fulfill the briefing. Improve quality, address any
    weaknesses you can identify, and make the text more polished.
    """;
```

Andernfalls (kein Seed): bisherige Logik unverändert.

Der `SeedDraft`-Context-Key wird **nur** auf Iteration 1 ausgewertet. Ab Iteration 2
übernimmt die normale `CurrentDraft`/Findings-Logik.

### AtelierPipelineFactory — neuer Overload: BuildWithSeedDraft

```csharp
public static GeefPipelineRunner<FinalizedDocument> BuildWithSeedDraft(
    CrewSnapshot snapshot,
    ILlmClientResolver resolver,
    IOptions<ConvergenceOptions> convergenceOptions,
    string seedDraftText,
    IAdvisorConsultationRepository? consultationRepository = null,
    Guid runId = default,
    ILoggerFactory? loggerFactory = null,
    IEnumerable<IGeefEventSink>? additionalSinks = null,
    IGroundingProviderFactory? groundingProviderFactory = null,
    IPricingCatalog? pricingCatalog = null,
    ICostAccumulator? costAccumulator = null)
```

Identischer Aufbau wie `BuildWithAdvisorContext`, aber mit
`new SeedDraftGroundingStep(seedDraftText)` statt `BriefingGroundingStep` / `AdvisorContextGroundingStep`.

### RunOrchestratorService — Dispatch-Logik

In `ProcessRunAsync`, **vor** `AtelierPipelineFactory.Build`:

```csharp
var runner = run.SeedDraftText is not null
    ? AtelierPipelineFactory.BuildWithSeedDraft(
        snapshot, llmClientResolver, convergenceOptions, run.SeedDraftText,
        consultationRepository: consultations,
        runId: run.Id, loggerFactory: loggerFactory,
        additionalSinks: [sink],
        groundingProviderFactory: groundingProviderFactory,
        pricingCatalog: pricingCatalog, costAccumulator: accumulator)
    : AtelierPipelineFactory.Build(
        snapshot, llmClientResolver, convergenceOptions,
        consultationRepository: consultations,
        runId: run.Id, loggerFactory: loggerFactory,
        additionalSinks: [sink],
        groundingProviderFactory: groundingProviderFactory,
        pricingCatalog: pricingCatalog, costAccumulator: accumulator);
```

Kein Eingriff in `RecoverCrashedRunsAsync` — dort wird nur `Running→Failed` korrigiert.

---

## D. Web / UI Layer

### "Fortsetzen"-Button

Auf der **Run-Detailseite** (`RunDetail.razor` oder äquivalent): Button sichtbar, wenn
`run.Status == RunStatus.Aborted || run.Status == RunStatus.Failed`.

Button-Text: **„Fortsetzen"** (primär) mit Dropdown/Split für beide Modi, oder ein
einzelner Button der einen Dialog öffnet.

Empfehlung: **Ein Button → öffnet `ResumeRunDialog`-Modal** (einfachste, konsistenteste UI).

### ResumeRunDialog (Modal-Komponente)

Felder:

1. **Modus (RadioGroup):**
   - `Mit letztem Entwurf fortsetzen` (vorausgewählt, wenn Iterationen vorhanden)
   - `Komplett neu starten` (Clean Retry — selbe Briefing, keine Seedvorlage)

2. **Max Iterations (InputNumber):**
   - Vorausgefüllt mit dem effektiven MaxIterations-Wert des Eltern-Runs
     (aus `ConvergenceOverride.MaxIterations` wenn gesetzt, sonst globaler Default).
   - Validierung: min 1, max 30 (oder systemkonfigurierbar).

3. **Hinweis-Text** (informativ, wenn Modus = Seed):
   > „Iteration {N} des Eltern-Runs wird als Startpunkt verwendet."

4. **Buttons:** „Fortsetzen" (Primary) + „Abbrechen"

### Nach Bestätigung

1. POST an `IRunService.ResumeRunAsync` (via Blazor DI in der Page-Klasse).
2. Bei Erfolg: `NavigationManager.NavigateTo($"/runs/{newRunId}")`.
3. Bei Fehler (Run nicht resumable): Fehlermeldung inline im Dialog anzeigen.

### Eltern-Run-Link auf der neuen Run-Detailseite

Wenn `run.ParentRunId is not null`:
```
Fortgesetzt von Run [ParentRunId.ToString()[..8]] →
```
Als klickbarer Link auf den Eltern-Run.

---

## E. Tests

### Erwartetes Delta: ~25 neue Tests

**Domain/Application:**
- `ResumeRunAsync` — Happy Path (Seed + Clean): neuer Run wird mit korrekten Werten angelegt
- `ResumeRunAsync` — Guard: nicht-resumbarer Status → `InvalidOperationException`
- `ResumeRunAsync` — Guard: Ownership-Verletzung → null / Exception
- `ResumeRunAsync` — Seed ohne Iterationen: seedDraftText bleibt null (kein Fehler)
- `ResumeRunAsync` — MaxIterationsOverride in ConfigJson korrekt gesetzt

**Infrastructure/Pipeline:**
- `SeedDraftGroundingStep.RunAsync` — setzt beide ContextKeys korrekt
- `ProfileBasedExecutor` — Iteration 1 mit SeedDraft: korrekte Prompt-Struktur
- `ProfileBasedExecutor` — Iteration 1 ohne SeedDraft: bisherige Prompt-Struktur unverändert
- `ProfileBasedExecutor` — Iteration 2 mit SeedDraft im Context: SeedDraft wird ignoriert, normaler Flow

**UI (bUnit):**
- `ResumeRunDialog` — Modus-Radio wechselbar
- `ResumeRunDialog` — Validierung MaxIterations
- `ResumeRunDialog` — Submit löst korrekte ResumeOptions aus
- Detailseite — "Fortsetzen"-Button sichtbar bei Aborted/Failed
- Detailseite — "Fortsetzen"-Button nicht sichtbar bei Completed/Running/Pending
- Detailseite — ParentRunId-Link sichtbar wenn gesetzt

---

## F. Out of Scope

- Resume eines bereits resumten Runs (kein Limit auf Ketten-Tiefe — der neue Run kann selbst
  wieder resumt werden, `ParentRunId` zeigt immer auf den direkten Eltern-Run)
- Resume von `Completed`-Runs (explizit nicht vorgesehen)
- "Undo Resume" / Eltern-Run reaktivieren
- Mehrere parallele Resume-Runs desselben Eltern-Runs (technisch möglich, UI verhindert es nicht)

---

## Migrations-Verifizierung nach Deploy

```bash
docker exec geef-atelier-postgres psql -U geef_atelier -d geef_atelier -c \
  "SELECT column_name FROM information_schema.columns \
   WHERE table_name='Runs' AND column_name IN ('ParentRunId','SeedDraftText');"
# Erwartete Ausgabe: 2 Zeilen
```
