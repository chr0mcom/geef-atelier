# Schritt-3-Bericht: Anthropic-Client und echte Provider

*Abgeschlossen: 10. Mai 2026*

---

## 1. Was umgesetzt wurde

Schritt 3 ersetzt die beiden Stub-Provider aus Schritt 2 (`StubExecutionStep`, zwei `StubReviewer`) durch echte Anthropic-API-Aufrufe:

**Neue Infrastruktur (`src/Geef.Atelier.Infrastructure/Llm/`):**
- `IAnthropicClient` — public interface mit `CompleteAsync(AnthropicRequest, CancellationToken) → AnthropicResponse`
- `HttpAnthropicClient` — internal typed HTTP-Client gegen `/v1/messages`
- `AnthropicOptions` — `IOptions<T>`-Pattern mit `ApiKey`, `ExecutorModel`, `ReviewerModel`
- `AnthropicMessageFormat` — internal static; Request-Serialisierung + Response-Deserialisierung
- `AnthropicServiceExtensions` — `AddAnthropicClient(IConfiguration)` gibt `IHttpClientBuilder` zurück

**Neue Provider (`src/Geef.Atelier.Infrastructure/Pipeline/`):**
- `LlmExecutionStep` — ruft Anthropic mit Executor-System-Prompt; ab Iteration 2 mit PreviousFindings
- `LlmReviewer` — parametrisiert mit Name + SystemPrompt; erzwingt Tool-Use via `tool_choice: "tool:submit_review"`
- `ReviewerToolDefinition` — statische `AnthropicTool`-Definition mit JSON-Schema für `submit_review`
- `AtelierSystemPrompts` — drei System-Prompts (Executor, BriefingTreue, Klarheit)
- `AtelierPipelineFactory` — ersetzt `StubPipelineFactory`; `Build(...)` für Production, `BuildWithProviders(...)` als Test-Hook

**DI-Registrierung (`src/Geef.Atelier.Web/Program.cs`):**
```csharp
builder.Services.AddAnthropicClient(builder.Configuration).AddStandardResilienceHandler();
```

**Tests (`tests/Geef.Atelier.Tests/`):**
- `FakeAnthropicClient` + `CriticalFakeAnthropicClient` — deterministisch, kein echtes HTTP
- `AtelierPipelineConvergenceTests` — 2-Iterationen-Convergence mit Mock
- `AtelierPipelineEventTests` — Event-Count-Assertions (identisch zu Schritt-2-Erwartungen)
- `AtelierPipelineCriticalAbortTests` — `ConvergenceFailedException` bei Critical-Finding
- `AtelierPipelineRealAnthropicTests` — Skip-If-No-Key-Integration-Test

---

## 2. Annahmen, Abweichungen und Korrekturen gegenüber dem Bau-Prompt

| # | Bau-Prompt-Annahme | Realität |
|---|---|---|
| 1 | `ToolInputJson` als `IReadOnlyDictionary<string,object>` | Geändert zu `string?` (raw JSON) — vermeidet `JsonElement`-Coupling im public Interface; `LlmReviewer` parst selbst via `JsonDocument` |
| 2 | Named HttpClient `"anthropic"` | Typed Client `AddHttpClient<IAnthropicClient, HttpAnthropicClient>()` — einfacher, kein Factory-Overhead |
| 3 | `Infrastructure.csproj` ohne `Microsoft.Extensions.Http` | Paket benötigt für `IHttpClientBuilder` in `AnthropicServiceExtensions`; hinzugefügt |
| 4 | `Assert.Skip(...)` in xUnit 2.9 | API nicht vorhanden; einfacher Early-Return-Pattern stattdessen |
| 5 | `result.Success == false` bei Critical-Abort | SDK wirft `ConvergenceFailedException` — `Assert.ThrowsAsync` statt `Assert.False` |
| 6 | Integration-Test via `WebApplicationFactory` | Manueller `ServiceCollection` + `ConfigurationBuilder.AddInMemoryCollection` — einfacher, keine Blazor-Test-Host-Abhängigkeit |

---

## 3. Architect-Konsultation

**Level:** Level 2 (`cat file | claude -p`) — erfolgreich.

Level 1 (`claude -p --dangerously-skip-permissions --input-file`) wurde aufgrund des in D-012 dokumentierten Musters direkt übersprungen. Level 2 produzierte `geef_architecture.md` mit Antworten auf alle fünf Pflichtfragen:

1. **Anthropic-Client-DI:** Typed Client (nicht Named); Singleton-safe über `IHttpClientFactory`-Mechanismus. Namespace `Geef.Atelier.Infrastructure.Llm` korrekt.
2. **API-Key-Handling:** `IOptions<AnthropicOptions>`, Lazy-Validation beim ersten Call. Startup-Validation bewusst nicht, da keine Validator-Dependencies gewünscht.
3. **Resilience:** `AddStandardResilienceHandler()` reicht für Skeleton; Custom-Pipeline (retry-after-Honor) ab Schritt 4+.
4. **Reviewer-Output-Schema:** Tool Use mit `tool_choice: "tool:submit_review"` — bei fehlendem Tool-Call `ReviewDecision.Failed` mit `SuggestedRetryHint`.
5. **PreviousFindings-Workaround:** Beibehalten; kein SDK-Bump in Schritt 3.

---

## 4. Pre-Mortem / Devil's Advocate — Erkenntnisse

**Adressierte Risiken:**

- **API-Key-Leak in Logs:** Key wird per Request gesetzt (`httpRequest.Headers.Add`), nicht in `DefaultRequestHeaders` — verhindert Persistierung im `HttpClient`-Singleton. Standard-Resilience-Logger logt keine Request-Header.
- **Tool-Use-Misfire:** `LlmReviewer` gibt `ReviewDecision.Failed` mit `SuggestedRetryHint` zurück; Convergence-Policy kann nächste Iteration versuchen.
- **JSON-Schema-Mismatch:** `MapSeverity` ist `ToLowerInvariant()`-basiert mit Default `Warning` — unbekannte Werte sind sicher.
- **Critical-Abort-Event-Typ:** Durch `AtelierPipelineCriticalAbortTests` verifiziert: `ConvergenceFailedException`, `PipelineFailedEvent` = 1, `PipelineCompletedEvent` = 0.

**Devil's-Advocate — Entscheidungen bestätigt:**

- Polly bleibt (kein Try-Catch-Loop): Retry-Storm-Schutz durch `AddStandardResilienceHandler` eingebaut (Rate-Limit-Backoff).
- Tool Use bleibt (kein JSON-Mode): Structured Outputs via Tool Use sind stabiler; JSON-Mode erzwingt kein Schema.
- `ToolInputJson` als `string?`: Sauberste Option — kein `System.Text.Json`-Leak in public Interface, kein `JsonElement`-Gleichheitsproblem in Tests.

---

## 5. Reviewer-Iterationen

**Iteration 1 — alle 5 Reviewer:**

| Reviewer | Ergebnis | Findings |
|---|---|---|
| R1 Functional Correctness | APPROVED_WITH_WARNINGS | 1 MINOR (PreviousFindings-Workaround dokumentiert) |
| R2 Code Quality | APPROVED_WITH_WARNINGS | 2 MAJOR (defensive JSON-Deserialisierung) — behoben vor Phase 4 |
| R3 Test Execution | APPROVED | 11/11 grün (mit Docker-Socket für Testcontainers) |
| R4 Architecture Compliance | APPROVED_WITH_WARNINGS | 1 MINOR (Layer-Reinheit korrekt) |
| R5 Live UI Sanity | APPROVED | Heading sichtbar, 0 Console-Errors, Screenshot gespeichert |

R2-MAJOR-Findings behoben:
1. `AnthropicMessageFormat.DeserializeResponse`: `?? throw new JsonException(...)` statt `!`-Null-Forgiving
2. `LlmReviewer.ParseToolInput`: `TryGetProperty` statt `GetProperty` — gibt `ReviewDecision.Failed` bei malformiertem Tool-Input zurück

---

## 6. Akzeptanzkriterien-Check

| # | Kriterium | Status |
|---|---|---|
| 1 | `IAnthropicClient` mit `CompleteAsync`-Vertrag | ✅ |
| 2 | HTTP-Implementierung gegen `/v1/messages` mit Tool-Use-Support | ✅ |
| 3 | API-Key via `IOptions<AnthropicOptions>`, Lazy-Validation | ✅ |
| 4 | `LlmExecutionStep` mit PreviousFindings ab Iter 2 | ✅ |
| 5 | `LlmReviewer` mit Tool-Use und `ReviewDecision.Failed`-Fallback | ✅ |
| 6 | `AtelierPipelineFactory.BuildWithProviders` als Test-Hook | ✅ |
| 7 | 11/11 Tests grün; Integration-Test korrekt skippbar | ✅ |

---

## 7. Beobachtungen zur Anthropic-API (Integration-Test)

Der Integration-Test (`AtelierPipelineRealAnthropicTests`) wurde in dieser Session **nicht mit echtem API-Key** ausgeführt — kein Key verfügbar. Test läuft korrekt durch (Early-Return) und produziert "Passed" ohne API-Kosten.

Beobachtungen aus dem Mock-Test-Design (nicht aus echten Calls):
- Tool-Use-Response enthält `stop_reason: "tool_use"` — SDK-Vertrag korrekt implementiert
- `submit_review`-JSON-Schema erzwingt `{ approved: bool, findings: [{severity, message}] }` — passt zum Anthropic-Tool-Use-Format
- Token-Erfassung über `AnthropicTokenUsage { InputTokens, OutputTokens }` bereit für Schritt-4-Persistierung

---

## 8. Empfehlungen für Schritt 4 (Persistierung)

**Verlässliche Event-Daten für PostgresEventSink:**
- `ExecutionPhaseCompletedEvent` — enthält aktuellen Draft (via `AtelierContextKeys.CurrentDraft`)
- `EvaluationApprovedEvent` / `EvaluationRejectedEvent` — enthält alle Findings für die Iteration
- `PipelineCompletedEvent` — enthält `FinalizedDocument` mit Markdown
- `PipelineFailedEvent` — enthält Exception-Details für Failed-Runs

**Token-Tracking:**
- `LlmExecutionStep` schreibt Token-Infos in `ExecutionResult.Notes` (`tokens_in=X tokens_out=Y`)
- In Schritt 4: `Notes` parsen oder `ExecutionResult` erweitern mit `TokenUsage`-Property
- Alternativ: `AnthropicTokenUsage` in Kontext schreiben (`context.Set(AtelierContextKeys.TokenUsage, usage)`)

**PreviousFindings-Workaround:**
- Bleibt in Schritt 4; wenn SDK-Bump auf `1.0.0` stable folgt, `GeefKeys.PreviousFindings` direkt nutzen

**Schema-Stabilität:**
- `Finding.Fingerprint` = SHA-256-Hash der Message → Stagnation-Detection funktioniert korrekt über Iterationen
- `Finding.Category` = `"review"`, `ArtifactReference` = `"draft"` — konsistent für alle Reviewer

---

## 9. PreviousFindings-Status

Der Workaround `GeefKeys.IterationHistory.Records[^1].EvaluationResult.AllFindings` bleibt aktiv. Er ist funktional korrekt und in `LlmExecutionStep` isoliert — ein SDK-Bump würde nur eine Zeile ändern. Kein SDK-Bump in Schritt 3 (siehe D-013(j)).
