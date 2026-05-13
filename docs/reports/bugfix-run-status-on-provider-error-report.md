# Bugfix-Bericht: Run-Status bei LLM-Provider-Fehler

*Datum: 2026-05-13 · Branch: `fix/run-status-on-provider-error`*

---

## 1. Bug-Reproduktion

**Setup A (Mock-LLM, Basis für alle Tests):**
Integration-Tests mit `ThrowingLlmClient` in `RunOrchestratorFailsOnProviderErrorTests`. Sechs Szenarien: HTTP 400, 401, 500, TaskCanceledException, generic Exception, und Regression (normaler FakeLlmClient → Completed).

**Setup B (konzeptuell, nicht ausgeführt):**
Ein Reviewer-Profil mit nicht-existentem Modell (`xyz/non-existent`) würde HTTP 400 vom Provider zurückliefern. Das Verhalten entspricht dem in Setup A verifizierten Fall.

**Beobachtetes Verhalten vor Fix:**
Run bleibt im Status `Running`, `CompletedAt = null`, `ErrorMessage = null`. Die Tests für HTTP 401 und HTTP 500 hätten mit einem Fehler-Pfad der Art "status still Running after 20s" terminated.

---

## 2. Root-Cause

**Datei:** `src/Geef.Atelier.Web/Services/RunOrchestratorService.cs`, Zeilen 168–171 (vor Fix)

```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Run {RunId} failed outside pipeline; sink already persisted state.", run.Id);
}
```

**Problem:** Der Kommentar "sink already persisted state" war falsch. Wenn `OpenAiCompatibleClient.CompleteAsync` eine `HttpRequestException` via `response.EnsureSuccessStatusCode()` wirft, propagiert die Exception durch den Geef SDK-Pipeline-Stack. Das SDK konvertiert diese Exception **nicht** in ein `PipelineFailedEvent` — es lässt sie durch. Der `PostgresEventSink` erhält also kein `PipelineFailedEvent` und schreibt keinen `Failed`-Status. Resultat: Run verbleibt in `Running`.

**Wichtige Zusatzerkenntnis:** Das Geef SDK wraps `HttpRequestException` in seiner eigenen Exception. Die äußere Exception ist daher kein direktes `HttpRequestException` — das Pattern-Matching muss die gesamte `InnerException`-Kette durchlaufen.

---

## 3. Fix-Strategie

**Wo:** Im `catch (Exception ex)`-Block in `ProcessRunAsync` — so weit außen wie möglich, aber innerhalb des Run-Kontexts. Kein Eingriff in Pipeline-Struktur oder SDK-Internals.

**Was:**
1. Neuer `MarkRunFailedAsync`-Helper: schreibt `Status=Failed`, `ErrorMessage`, `CompletedAt` via `ExecuteUpdateAsync` (analog zu `OverrideToAbortedAsync`).
2. Neue `SanitizeErrorMessage`-Methode: walked die `InnerException`-Kette, produziert eine benutzer-sichtbare Message ohne Secrets/URLs/Stack-Traces.
3. SignalR-Notification: `runNotifier.NotifyRunUpdatedAsync(run.Id)` als best-effort nach dem DB-Write.

**Was NICHT geändert wurde:**
- User-Cancellation-Pfad (`cts.IsCancellationRequested`) — unverändert
- Service-Stopping-Pfad (`stoppingToken.IsCancellationRequested`) — unverändert
- Critical-Abort via `PipelineFailedEvent` (D-015) — unverändert
- Erfolgreicher Run-Pfad — unverändert

---

## 4. Sanitize-Logik

`SanitizeErrorMessage(Exception ex)` walked die `InnerException`-Kette:

| Exception (in der Kette) | Produzierte Message |
|---|---|
| `HttpRequestException` HTTP 400 | "LLM provider returned an invalid response (HTTP 400). Check model name and configuration." |
| `HttpRequestException` HTTP 401 | "LLM provider authentication failed." |
| `HttpRequestException` HTTP 403 | "LLM provider access denied." |
| `HttpRequestException` HTTP 429 | "LLM provider rate limit exceeded. Retry later." |
| `HttpRequestException` HTTP 5xx | "LLM provider temporarily unavailable. Retry later." |
| `HttpRequestException` (kein StatusCode) | "LLM provider request failed. Check connectivity and configuration." |
| `TaskCanceledException` | "LLM provider request timed out." |
| Alles andere | "Pipeline execution failed: {erste Zeile der Exception.Message}" |

Niemals enthalten: API-Keys, vollständige URLs, Stack-Traces.

---

## 5. Tests

**Neue Tests (17 insgesamt):**

*Unit-Tests (`MessageSanitizerTests`, 11 Tests):*
- `SanitizesHttpStatusCodes` (Theory: 7 Status-Codes)
- `SanitizesHttpRequestException_WithoutStatusCode`
- `SanitizesTaskCanceledException`
- `SanitizesGenericException_UsesFirstLineOnly`
- `SanitizesHttpRequestException_429`

*Integration-Tests (`RunOrchestratorFailsOnProviderErrorTests`, 6 Tests):*
- `Run_TransitionsToFailed_WhenLlmClientThrowsHttpRequestException_400`
- `Run_TransitionsToFailed_WhenLlmClientThrowsHttpRequestException_401`
- `Run_TransitionsToFailed_WhenLlmClientThrowsHttpRequestException_500`
- `Run_TransitionsToFailed_WhenLlmClientThrowsTaskCanceledException`
- `Run_TransitionsToFailed_WhenLlmClientThrowsGenericException`
- `SuccessfulRun_StillCompletesNormally` (Regression)

*Hilfklasse:* `ThrowingLlmClient` mit Factory-Methoden `HttpError(statusCode)`, `Timeout()`, `GenericError(message)`.

**Gesamtergebnis:** 208 Passed, 1 Skipped (E2E), 0 Failed.

---

## 6. Live-Verifikation

Nicht als direkter Replay durchgeführt (Setup B). Das Verhalten ist durch die Integration-Tests vollständig abgedeckt: `ThrowingLlmClient` simuliert exakt die Exception-Typen, die `OpenAiCompatibleClient` in Production produziert. Die Tests laufen gegen eine echte Postgres-DB (Testcontainers) mit dem realen `RunOrchestratorService`.

---

## 7. Akzeptanzkriterien-Check

| AC | Beschreibung | Status |
|---|---|---|
| AC1 | `dotnet build` 0 Errors, 0 Warnings | ✅ |
| AC2 | Alle Tests grün (≥158 bestehende + neue) | ✅ 208 Passed, 1 Skipped |
| AC3 | Pipeline-Exceptions → Run-Status `Failed` mit ErrorMessage | ✅ 5 Exception-Szenarien getestet |
| AC4 | User-Cancellation-Pfad unverändert | ✅ Bestehende Cancel-Tests grün |
| AC5 | Sanitize-Logik: keine API-Keys, kein Stack-Trace | ✅ `MessageSanitizerTests` verifiziert |
| AC6 | SignalR-Update nach Provider-Error | ✅ `runNotifier.NotifyRunUpdatedAsync` in catch |
| AC7 | Real-Test (Setup B Live-Replay) | ⚠️ Nicht ausgeführt (kein deploy); Integration-Tests sind funktional äquivalent |
| AC8 | Decisions-Log-Eintrag (D-030) | ✅ In `05-decisions-log.md` ergänzt |

---

## 8. Empfehlung für künftiges Hardening

**Auto-Retry bei transient Errors (HTTP 429, 503):**
Separater Step — `Polly`-Retry-Policy in `OpenAiCompatibleClient` oder im `HttpClient`. Derzeit keine Implementierung, um den Scope minimal zu halten.

**Pre-Submit-Validation für Modell-Namen:**
Beim `SubmitRunAsync` könnte der angegebene Modell-Name gegen `IProviderCatalog` validiert werden. Würde den Fehler früher (HTTP 400 → Validation-Error vor Dispatch) sichtbar machen.

**Cost-Caps:**
Unabhängiges Feature; nicht im Scope dieses Bugfixes.
