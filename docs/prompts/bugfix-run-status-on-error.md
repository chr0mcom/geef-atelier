# Claude-Code-Prompt: Bug-Fix — Run-Status bei LLM-Provider-Fehler

*Pre-existing Bug aus PS-6 Bericht Sektion 5. Wenn ein Reviewer oder Executor mit HTTP-Fehler vom LLM-Provider antwortet, bleibt der Run-Status auf `Running` hängen statt auf `Failed` zu wechseln. Klein abgegrenzter Bug-Fix-Step, läuft parallel zu PS-7.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Bei AC12 in PS-6 (Real-Custom-Crew-Test) wurde ein pre-existing Bug entdeckt und dokumentiert: Wenn ein LLM-Provider HTTP 4xx oder 5xx zurückgibt (z.B. weil das Modell nicht existiert wie damals `openai/gpt-5.5-mini`), bleibt der zugehörige Run im Status `Running` hängen, statt auf `Failed` zu wechseln. Der RunOrchestratorService catched die Exception nicht robust genug.

Deine Aufgabe ist die **Reparatur dieses Bugs** mit minimal-invasivem Fix: Pipeline-Errors zu sauberen `Failed`-Status-Übergängen verarbeiten, ohne andere Verhaltensweisen zu verändern.

**Parallel-Hinweis:** PS-7 (Advisor-Pässe) läuft parallel und berührt denselben `RunOrchestratorService`. Halte deine Änderungen **orthogonal** zur Pipeline-Struktur — nur Error-Handling anfassen, keine Pipeline-Schritte einfügen oder umsortieren. Bei Merge-Konflikten: Bug-Fix wird zuerst gemerged.

## Vorgehen

Du folgst dem Workflow in `/srv/docker/docs/geef-workflow.md`, aber in **komprimierter Form** weil kleiner Bug-Fix:
- Phase 1.1 + 1.2: Bug reproduzieren, Root-Cause finden
- Phase 1.4 Architect: Fix-Strategie (Where to catch? Welche Exception-Typen?)
- Phase 2: Implementation + Tests
- Phase 3: R1 + R3 reichen (kein Architecture-Eingriff, kein UI-Eingriff)
- Phase 4: Bericht, Commit, optional PR

**Branch-Empfehlung:** `fix/run-status-on-provider-error`

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

1. **`docs/reports/post-skeleton-06-crew-ui-report.md`** Sektion 5 — Bug-Beschreibung
2. **`docs/reports/post-skeleton-06-crew-ui-report.md`** Sektion 7 — Reproduktions-Kontext (Run `d8faea85`, erster Lauf gescheitert mit `openai/gpt-5.5-mini`)
3. **`src/Geef.Atelier.Web/Background/RunOrchestratorService.cs`** — der zentrale Service mit der pending-poll + execute-Logik
4. **`src/Geef.Atelier.Infrastructure/Pipeline/AtelierPipelineFactory.cs`** — wie die Pipeline aufgebaut wird
5. **`src/Geef.Atelier.Infrastructure/Pipeline/ProfileBasedReviewer.cs`** und **`ProfileBasedExecutor.cs`** — wo HttpRequestException entstehen kann
6. **`src/Geef.Atelier.Infrastructure/Llm/OpenAiCompatibleClient.cs`** — Outbound-HTTP, Quelle der Provider-Exceptions
7. **`src/Geef.Atelier.Infrastructure/Persistence/RunPersistenceService.cs`** — wie Status-Updates persistiert werden
8. **`src/Geef.Atelier.Infrastructure/EventSink/PostgresEventSink.cs`** — Event-Pattern für Run-Status-Lifecycle
9. **D-015** im Decisions-Log — Critical-Abort via PipelineFailedEvent (das ist die etablierte Fehler-Senke)

## Reproduktion (Phase 1.2)

Den Bug nachstellen, um Root-Cause sicher zu identifizieren:

**Test-Setup A (Mock-LLM):**
- Unit-Test mit Mock-`ILlmClient`, der `HttpRequestException` mit Status 400 wirft
- Pipeline-Run starten
- Erwartung: Run-Status nach Pipeline-Lauf ist `Running` (Bug-Manifestation)
- Soll-Verhalten: Status ist `Failed` mit aussagekräftiger ErrorMessage

**Test-Setup B (Real-Replay):**
- In `appsettings.Development.json` (oder Test-Config) temporär `clarity`-Profile auf einen nicht-existenten Modell-Namen setzen (`xyz/non-existent-model`)
- Test-Briefing submitten
- Beobachten: Status bleibt `Running` hängen

Setup A reicht für die Verifikation. Setup B nur als Sanity-Check vor dem Merge.

## Vermutete Root-Cause (Architect verifiziert in Phase 1.4)

Aus dem Bericht-Hinweis + GEEF-SDK-Architektur-Wissen aus M1:

Wahrscheinlichste Stelle: Im `RunOrchestratorService` läuft die Pipeline-Execution in einem try-catch-Block, **aber die Exception wird vermutlich nur geloggt und nicht in einen Run-Status-Update umgesetzt**. Oder: das catch fängt nur bestimmte Exception-Typen (z.B. `OperationCanceledException`) und lässt andere durch — wodurch der Status-Update-Code nie erreicht wird.

Alternative Stelle: Die `OpenAiCompatibleClient.CompleteAsync`-Methode wirft die Exception **nach** einem `PipelineStartedEvent`, aber **vor** dem entsprechenden `PipelineFailedEvent` — und der Code zwischen den Events catched nichts.

Architect verifiziert genau wo, dokumentiert im Bericht.

## Konkrete Anforderungen

### 1. Fix-Strategie

**Empfehlung (Architect bestätigt nach Code-Diving):**

Im `RunOrchestratorService.RunPipelineAsync` (oder wie die Methode heißt) ein **outer try-catch** um den gesamten Pipeline-Execute-Block. Bei jeder Exception:

1. Run-Status auf `Failed` setzen
2. `ErrorMessage` auf eine sanitisierte Exception-Message setzen (keine Secrets!)
3. `PipelineFailedEvent` publishen für SignalR-Live-Update
4. Exception weiter loggen (existing Logger), aber **nicht** rethrowen

```csharp
try
{
    // existing pipeline execution
    await pipeline.RunAsync(...);
}
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    // existing cancellation path - leave untouched
    throw;
}
catch (Exception ex)
{
    _logger.LogError(ex, "Pipeline execution failed for run {RunId}", runId);
    await _runPersistence.MarkFailedAsync(runId, SanitizeMessage(ex), ct);
    await _runNotifier.NotifyRunFailedAsync(runId, ct);
}
```

**Sanitize-Logik:** Provider-spezifische Error-Messages können Keys, URLs mit Parametern, oder andere Sensitive-Details enthalten. Eine `SanitizeMessage`-Funktion produziert eine kurze, user-freundliche Message ohne API-Details. Z.B.:
- `HttpRequestException` mit Status 400 → `"LLM provider returned an invalid response (HTTP 400). Check model name and configuration."`
- `HttpRequestException` mit Status 401 → `"LLM provider authentication failed."`
- `HttpRequestException` mit Status 429 → `"LLM provider rate limit exceeded. Retry later."`
- `HttpRequestException` mit Status 5xx → `"LLM provider temporarily unavailable. Retry later."`
- `TaskCanceledException` (nicht user-cancelled) → `"LLM provider request timed out."`
- Generic `Exception` → `"Pipeline execution failed: {first line of message}"`

Die volle Exception bleibt im Logger-Output erhalten — nur die UI-sichtbare `ErrorMessage` wird sanitisiert.

### 2. Bestehende Verhalten erhalten

**Bleibt unverändert:**
- User-Cancellation-Pfad (`CancellationRequested`-Flag → CancellationToken-Path)
- Critical-Abort via `PipelineFailedEvent` aus dem SDK (D-015)
- Tool-Use-Parse-Errors aus dem CLI-Proxy → bleiben Findings, kein Run-Failed
- Successful-Run-Pfad bleibt komplett unverändert

**Wird neu gefangen:**
- Alle übrigen `Exception`-Typen, die bisher den Run im `Running`-Status hängen ließen

### 3. Tests

**Neue Unit-Tests:**
- `RunOrchestratorServiceTests.RunFailsWithFailedStatus_WhenLlmClientThrowsHttpRequestException`
- `RunOrchestratorServiceTests.RunFailsWithFailedStatus_WhenLlmClientThrowsTaskCanceledException` (non-user-cancellation)
- `RunOrchestratorServiceTests.RunFailsWithFailedStatus_WhenLlmClientThrowsGenericException`
- `RunOrchestratorServiceTests.UserCancellationStillBehavesAsCancelled` (regression-test)
- `RunOrchestratorServiceTests.SuccessfulRunStillCompletesNormally` (regression-test)

**Sanitize-Tests:**
- `MessageSanitizerTests.SanitizesHttpStatusCodes`
- `MessageSanitizerTests.RedactsApiKeysFromMessages` (falls die Exception-Message API-Keys enthalten könnte)

**Bestehende 192 Tests müssen grün bleiben.**

### 4. Live-Verifikation (Pflicht-AC)

**Replay des PS-6-Original-Bugs:**

Auf Production oder Dev-Setup:
1. Atelier-Config kurz ändern: ein Reviewer-Profil hat ein nicht-existentes Modell (z.B. `xyz/non-existent`)
2. Briefing submitten
3. **Vor Fix:** Status bleibt `Running` (Bug reproduziert)
4. **Nach Fix:** Status wechselt auf `Failed` mit Message "LLM provider returned an invalid response (HTTP 400)" innerhalb von ~5-10 Sekunden
5. UI zeigt den Failed-Status auf RunDetail-Page korrekt an (SignalR-Update)

Config-Change danach rückgängig machen.

## Akzeptanzkriterien

1. `dotnet build` 0 Errors, 0 Warnings.
2. `dotnet test` — alle bestehenden Tests grün (192 nach M1) + neue Bug-Fix-Tests.
3. **Bug ist gefixt:** Pipeline-Exceptions (HTTP 4xx/5xx, Timeout, Generic) führen zu Run-Status `Failed` mit aussagekräftiger ErrorMessage.
4. **User-Cancellation-Pfad unverändert** — bestehende Cancel-Flow-Tests grün.
5. **Sanitize-Logik:** ErrorMessage enthält keine API-Keys, kein vollständiger Stack-Trace, keine sensitive URLs.
6. **SignalR-Update funktioniert:** UI zeigt Failed-Status nach Provider-Error in Echtzeit.
7. **Real-Test:** Bug reproduziert + Fix verifiziert (Beobachtungen im Bericht).
8. **Decisions-Log-Eintrag** (D-030 oder nächste freie Nummer) mit Architect-Entscheidungen zur Fix-Strategie.

## Was du in diesem Step NICHT tust

- **Keine Pipeline-Struktur-Änderungen** — keine neuen Steps, kein Umsortieren. Nur Error-Handling.
- **Keine Pre-Submit-Validation** für Modell-Namen — separater Step ("LLM-Model-Validation") falls gewünscht.
- **Kein Auto-Retry** bei transient Errors — separater Step.
- **Keine Cost-Tracking-Anpassung**.
- **Keine UI-Anpassung** — Error-Message wird in der bestehenden ErrorBanner-Komponente angezeigt.
- **Keine Domain-Modell-Änderungen** — `RunEntity.Status` und `ErrorMessage` existieren bereits.

## Architect-Schwerpunkte (Phase 1.4) — zwei echte Fragen

1. **Wo genau das Outer-Catch?** Im `RunOrchestratorService` oder im `AtelierPipelineFactory` oder in der Service-Wrapper-Schicht? Architect findet die richtige Schicht basierend auf der Code-Realität. Faustregel: so weit außen wie möglich, aber innerhalb des Run-Kontexts.

2. **Welche Exception-Typen** explizit fangen vs. unter generic `Exception` zusammenfassen? Faustregel: `HttpRequestException`, `TaskCanceledException` (non-user-cancellation), `JsonException` als explizite Cases mit eigener Sanitize-Logik; alles andere als generic Fallback.

## Reviewer-Hinweise (reduziert)

- **R1 (Functional Correctness):** Alle 8 ACs prüfen, besonders Live-Reproduktion (AC7).
- **R2 (Code Quality):** Sanitize-Logik sauber, keine Code-Duplikation.
- **R3 (Test Execution):** Alle Tests grün, neue Tests dokumentieren spezifische Failure-Pfade.
- **R4 (Architecture Compliance):** Kein neuer Layer-Konflikt, Error-Handling im richtigen Service.

R5 (Live UI) entfällt — Bug-Fix hat keine UI-Komponenten-Änderung, Live-Sanity ist Teil von AC7.

## Persistenter Abschlussbericht

Bericht nach `docs/reports/bugfix-run-status-on-provider-error-report.md`. Inhalt:

1. **Bug-Reproduktion** — wie der Bug nachgestellt wurde (Setup A + B)
2. **Root-Cause** — wo genau die Exception verloren ging (mit Code-Zeile)
3. **Fix-Strategie** — Architect-Entscheidungen
4. **Sanitize-Logik** — was die Message-Sanitizer alles abdeckt
5. **Tests** — neue Tests + Regression-Tests
6. **Live-Verifikation** — Replay-Beobachtung
7. **Akzeptanzkriterien-Check** — Tabelle mit allen 8 ACs
8. **Empfehlung für künftige Hardening** — Auto-Retry? Pre-Submit-Validation? Beides separate Steps.

## Merge-Strategie

**Empfehlung:** Direct-Push auf `main` möglich (Bugfix-Charakter, kein PR-Workflow zwingend).

Falls Stefan PR-Workflow bevorzugt: Branch `fix/run-status-on-provider-error`, PR gegen `main`, Squash-Merge okay (kleiner Bug-Fix, fein-granulare Commits weniger relevant für künftige Bisecting).

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits (z.B. `fix: catch llm errors in orchestrator`, `test: add provider error coverage`, `feat: message sanitizer for run errors`).
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- **Niemals Secrets** in Logs oder Sanitize-Output.

Erwarteter Aufwand: 0.5-1 Arbeitstag.

---

**Nach erfolgreichem Abschluss:** Pipeline-Errors führen verlässlich zu sauberen Failed-States. Atelier ist robust gegen Provider-Misskonfigurationen. PS-7 kann ohne dieses Risiko parallel weiterlaufen.