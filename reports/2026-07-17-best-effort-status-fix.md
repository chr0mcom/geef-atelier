# Best-Effort-Statusfix + Step44-Backfill (2026-07-17)

## Problem

Nicht-konvergierte Best-Effort-Runs wurden als `Completed` persistiert: Das Geef-SDK publiziert bei Nicht-Konvergenz zuerst `PipelineFailedEvent` (Sink setzt korrekt `Failed`/`Aborted` + `ErrorMessage`), der Best-Effort-Zweig danach zusätzlich `PipelineCompletedEvent(Success: false)` — und der `PostgresEventSink` ignorierte das `Success`-Flag und überschrieb den Status pauschal mit `Completed`. Folge: Der Run wirkte erfolgreich (Badge „Completed" + rotem Fehlerbanner darunter), war aber nicht resumefähig (Resume-Gate verlangt `Aborted or Failed`), und der `crew-materializer`-Finalizer des Composer-Flows lief nie. Auslöser war Kompositions-Run `c8dbd1a7`, dessen WEG-Template nie materialisiert wurde.

## Fix

1. **`PostgresEventSink`**: `PipelineCompletedEvent` setzt `Completed` nur noch bei `Success == true`. Bei `Success == false` läuft zuerst ein idempotenter, auf `Status == Running` geguardeter Terminal-Fallback (`Failed` + `CompletedAt` + „Pipeline stopped without full convergence" — fängt transient verlorene `PipelineFailedEvent`-Writes ab, überschreibt nie ein persistiertes `Failed`/`Aborted`), danach werden nur `FinalText`/`WordCount` (bester Entwurf) geschrieben. Zusätzlich ist `PersistRawEventAsync` jetzt isoliert (try/catch + `ChangeTracker.Clear()`), damit ein Audit-Journal-Fehler das Run-State-Handling keines Events mehr unterdrücken kann.
2. **`RunDetail.razor`**: Manuscript-Block (Final-Text) rendert auch für `Failed`/`Aborted`-Runs mit nicht-leerem `FinalText` — der Best-Effort-Entwurf bleibt sichtbar, Resume-/Delete-Buttons existierten für diese Stati bereits.
3. **Migration `Step44BestEffortRunStatusBackfill`** (data-only, No-op-Down): backfillt die 9 historischen Falsch-Completed-Runs (`Completed + ErrorMessage IS NOT NULL` → `Aborted` bei `LIKE 'Aborted%'`, sonst `Failed`; Aborted-Sweep zwingend zuerst). Dry-Run gegen die Live-DB: exakt 9 Treffer, 0 False Positives.
4. **Doku**: XML-Doc von `SurfaceBestEffortDraftAsync` korrigiert.

## Tests

15 fix-relevante Tests grün: 4 Sink-Integrationstests (Failed-max-iterations, Aborted-critical-abort, Stranded-Running-Fallback, Aborted-Preservation), 1 Migrations-Mapping-Test (führt die echten `UpOperations`-SQLs aus), 4+4 bUnit (`RunDetailManuscriptVisibilityTests` neu, `RunDetailArtifactsTests` nach Stub-Extraktion nach `Fakes/RunDetailTestDoubles.cs`), 2 bestehende Sink-Regressions-Suiten. Gefilterte Suite (Persistence/Pipeline/Orchestrator/Web.Components): 592/597 — die 5 Fails sind Baseline-identische Altlasten (git-stash-verifiziert).

## Auswirkungen

- Nicht-konvergierte Runs erscheinen künftig ehrlich als `Failed`/`Aborted` **mit** sichtbarem Best-Effort-Entwurf und Resume-Button.
- Dashboard-Aggregate (Run-Counts, Konvergenz-Rate, Kosten-Trends) verlieren rückwirkend die 9 backfillten Runs aus den Completed-Zahlen — beabsichtigte Ehrlichkeits-Korrektur.
- `LearningPublishFinalizerExecutor` publiziert Learnings nur noch aus wirklich konvergierten Evaluation-Runs.
- Rollback-Netz: `pg_dump`-Snapshot der `Runs`-Tabelle unter `/srv/backup/geef-atelier-runs-before-step44-20260717.dump`.

## Prozess

GEEF-Workflow vollständig: Architekt-Blueprint, Advisor-Skips dokumentiert, 4 Review-Iterationen (2× MAJOR von codex/gpt-5.6-sol in Iteration 2/3 — Lost-Write-Stranding und Fallback-Priorisierung — beide behoben), finale Konvergenz aller 4 Reviewer mit 0 Findings; Pre-Deploy-Advisor 🟢 GRÜN mit Snapshot-Auflage.
