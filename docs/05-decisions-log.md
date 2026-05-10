# Decisions Log

*Letzte Aktualisierung: 10. Mai 2026 (Migration M1 abgeschlossen, Merge in main offen)*

Chronologisches Protokoll aller Entscheidungen aus dem Brainstorming.

## 10. Mai 2026 — Erstes Brainstorming

### D-001 bis D-009 (kondensiert)

Use-Case-Fokus generisch (D-001), Fire-and-Forget (D-002), Blazor Server (D-003), Postgres (D-004), MCP zweite Schnittstelle (D-005), Projekt-Name Geef.Atelier (D-006), Konventionen formalisiert via D-009, Walking Skeleton zuerst (D-008), kanonische `geef_workflow.md` (D-009).

### D-010: Schritt 1 abgeschlossen — Realitäts-Abgleich

Realfakten: Geef.Sdk 1.0.0-ci.1, `.slnx`-Format, `Directory.Build.props`, UI-Component-Library unter `src/Geef.Atelier.Web/Components/UI/`, Auto-Migration mit try-catch.

### D-011: Architect-Workflow-Update + Atelier-Fallback

(A) Generisches Workflow-Update: Phase 1.4 mit Fallback-Sequence, R4 prüft `geef_architecture.md`-Existenz. (B) Atelier-Level-4: Executor schreibt selbst.

### D-012: Schritt 2 abgeschlossen — SDK-Realfakten

Sechs Korrekturen: `FindingSeverity { Info, Warning, Error, Critical }`; `DefaultConvergencePolicy`; `UseMiddleware<T>()` generisch; nur `EvaluationApprovedEvent`/`RejectedEvent`; `PreviousFindings` via `IterationHistory`; `using SdkGeef = Geef.Sdk.Geef;`.

### D-013: Schritt 3 abgeschlossen — Anthropic-Client (in M1 ersetzt durch ILlmClient)

Realfakten zur ursprünglichen Anthropic-Schicht. **Status durch M1 (siehe D-018):** Komplett ersetzt durch provider-agnostische `ILlmClient`-Schicht. Inhaltliche Konzepte (Tool-Use-Pattern, defensive JSON-Deserialisierung, API-Key per Request statt DefaultHeaders, Resilience-Strategie) bleiben gültig — nur die Adapter-Implementierung ändert sich.

### D-014: Production-Domain und Traefik-Routing für Schritt 10

Domain `geef.stefan-bechtel.de`, IP `95.216.100.213`, Traefik mit TLS auf Server aktiv. Wird in Schritt 10 verkabelt.

### D-015: Schritt 4 abgeschlossen — EventSink und Persistierung

Realfakten: `IRunPersistenceService` in Core, `PostgresEventSink` mit injizierter `Guid runId`, Severity-Mapping via `ToAtelierSeverity()`, Token-Tracking via typisierter `ContextKey<LlmTokenUsage>` (in M1 von `AnthropicTokenUsage` umbenannt — Inhalt identisch), Critical-Abort-Findings aus `PipelineFailedEvent.History.Records[^1].EvaluationResult.AllFindings` (SDK-Dekompilierung verifiziert), `_lastExecutionContext` als `volatile`, `JsonSerializerOptions.ReferenceHandler = IgnoreCycles`, `IServiceScopeFactory.CreateAsyncScope()` pro Event.

### D-016: Schritt 5 abgeschlossen — RunOrchestratorService

Realfakten: Atomarer Pending→Running-Claim, `SemaphoreSlim` + `ConcurrentDictionary<Guid, Task>` + `WhenAll`-Drain, `OverrideToAbortedAsync` mit `CancellationToken.None`, `PipelineStartedEvent`-Handler nur idempotent `StartedAt`. `_runCts`-Dictionary für spätere Cancellation-Reaktion. `OrchestratorOptions` in Core. `GatedFakeLlmClient` für deterministische Concurrency-Tests. Cancellation-Strategie: nur StoppingToken (Option γ); `CancelRunAsync`-API-Implementierung verschoben auf Schritt 6.

### D-017: Provider-Strategie-Wechsel auf OpenAI-konforme APIs (Migration M1 — Auslöser)

**Status:** ✅ Abgeschlossen (Branch `feature/openai-compatible-providers`, kein Merge in main bisher — wartet auf Maintainer-Entscheidung).

**Auslöser:**
- OAuth-Token (`sk-ant-oat01-*`) wurde von Anthropic im Februar 2026 für die Messages-API deaktiviert.
- API-Bearer-Key (`sk-ant-api03-*`) erfordert separaten Pay-as-you-go-Account auf der Anthropic-Console — vermeidbar.
- OpenAI-konforme APIs (insbesondere OpenRouter) bieten Anthropic Claude **plus** OpenAI GPT, Google Gemini, Meta Llama, Mistral, etc. über einen einzigen Bearer-Key.

**Entscheidung:** Wechsel der LLM-Schicht von Anthropic-spezifisch auf OpenAI-API-konform. Default-Endpoint: OpenRouter. Pro-Akteur-Modell-Mapping. Modell-Pluralismus aus Vision sofort verfügbar (vorgezogen aus "nach Skeleton").

**Branch-Strategie:** Nicht in main automatisch mergen, nur pushen. Empfehlung: Merge vor Schritt 7.

(Detail-Realfakten siehe D-018, der den M1-Abschluss dokumentiert.)

### D-018: Migration M1 abgeschlossen — Realfakten und Workflow-Abweichung

**Datum:** 10. Mai 2026
**Bericht:** [reports/migration-01-report.md](reports/migration-01-report.md) (auf Branch `feature/openai-compatible-providers`)
**Branch:** `feature/openai-compatible-providers` — gepusht, **nicht** in main gemerged.
**Tests:** 31/31 grün (alle Tests, inklusive Postgres/Orchestrator-Testcontainer; davon 9 Unit-Tests ohne Docker).
**Commits:** 4 Conventional-Commits + 1 nachgereichter `docs(reports)`-Commit für den Bericht selbst.

**Architect-Konsultation — Antworten auf die sechs Schwerpunkte aus dem M1-Prompt:**

**(F1) ToolChoice-Repräsentation:** String-Convention `"function:submit_review"` (intern in `OpenAiMessageFormat.BuildToolChoice()` zu OpenAI-Object serialisiert). Begründung: Nur eine genutzte Variante in der Pipeline; typsichere Discriminated Union wäre überdimensionierte Abstraktion für ein statisches Muster.

**(F2) Pro-Akteur-Lookup-Pattern:** `string actorName` als Dictionary-Key in `LlmOptions.Actors`, **nicht** als Enum. `LlmActor`-Enum existiert weiterhin als Typen-Dokumentation, wird aber nicht für den Lookup genutzt. Begründung: `IReviewer.Name` aus dem Geef-SDK ist bereits `string`; Enum als Key hätte zwei Quellen der Wahrheit erzeugt (`actor.ToString()` für `IReviewer.Name`-Getter). Lookup-Form: `options.Value.Actors.GetValueOrDefault(name)`.

**(F3) `OpenAiMessageFormat`-Position:** Internal static class neben `OpenAiCompatibleClient`, exakt analog zur Schritt-3-Konvention mit `AnthropicMessageFormat`. Saubere Kapselung pro Adapter — wenn später ein zweiter Adapter (z.B. `AnthropicNativeClient` mit Bearer-Key) ergänzt würde, hätte er eigene `AnthropicMessageFormat`-Klasse, keine Kollision.

**(F4) Endpoint-Override-Pfad:** `LlmOptions.Endpoint` als konfigurierbare String-Property mit Default `"https://openrouter.ai/api/v1"`. Override via `Llm__Endpoint`-Env-Var. **`HttpClient.BaseAddress` wird NICHT gesetzt** — `OpenAiCompatibleClient` baut Full-URL pro Request via `$"{options.Value.Endpoint.TrimEnd('/')}/chat/completions"`. Abweichung vom Schritt-3-Pattern, aber bewusst: Endpoint dynamisch aus Config kommt, nicht statisch.

**(F5) `02-architecture.md`-Update-Form:** Vollständige Ersetzung der "Multi-Provider-LLM-Abstraktion (geplant)"-Sektion durch eine "LLM-Provider-Schicht (umgesetzt in M1)"-Sektion. Begründung: Der ursprüngliche Abschnitt enthielt eine falsche `LlmRequest`-Signatur (mit `Provider`-Parameter); inkrementelles Update mit Verweis hätte Lesende verwirrt. D-017-Verweis ist im neuen Abschnitt für historischen Kontext enthalten.

**(F6) `anthropic-version`-Header-Erbe:** Vollständig entfernt. Kein Provider-spezifisches Header-Framework. Begründung: OpenRouter setzt Provider-Header selbst beim Weiterleiten. Future-Proofing durch das `ILlmClient`-Interface ist ausreichend — ein späterer `AnthropicNativeClient` wäre ein neuer Implementer mit eigenen Headern.

**Fixierte Realfakten aus M1 (verbindlich ab Schritt 7, nach Merge):**

**(a) `ILlmClient` als public interface in `Geef.Atelier.Infrastructure.Llm`:**
- `Task<LlmResponse> CompleteAsync(LlmRequest, CancellationToken)`
- Co-Located Records `LlmRequest`, `LlmResponse`, `LlmTokenUsage`, `LlmTool` in derselben Datei (analog zu Schritt-3-Konvention mit `IAnthropicClient`).

**(b) `LlmResponse`-Form mit separatem `ToolName` und `ToolArgumentsJson`:**
- `ToolName: string?` — Name des aufgerufenen Tools (z.B. `"submit_review"`), `null` bei Plain-Text-Response.
- `ToolArgumentsJson: string?` — Raw-JSON-String der Tool-Arguments, kein `JsonElement`-Coupling im Interface (analog zu D-013(a)).
- `FinishReason: string` — z.B. `"stop"`, `"tool_calls"`, `"length"`. Reviewer-Code prüft auf `"tool_calls"` (OpenAI-Standard, nicht Anthropic-`"tool_use"`).
- Diese Property-Trennung ist eine **leichte Verbesserung** gegenüber dem M1-Prompt-Vorschlag, der nur eine kombinierte Tool-Repräsentation nannte.

**(c) `OpenAiCompatibleClient` (internal sealed):**
- Typed HttpClient via `AddHttpClient<ILlmClient, OpenAiCompatibleClient>()`.
- API-Key per Request gesetzt (`Authorization: Bearer ...`), **nicht** in `DefaultRequestHeaders` — analog zu D-013(b), verhindert Key-Leak im HttpClient-Singleton.
- Lazy-Validation des `ApiKey` beim ersten Aufruf: `InvalidOperationException` mit klarer Botschaft.
- Kein `BaseAddress`; Full-URL aus `LlmOptions.Endpoint` pro Request gebaut.
- Timeout 120 Sekunden (wie aus Schritt 3).

**(d) `LlmOptions` mit Pro-Akteur-Mapping:**
```csharp
public sealed class LlmOptions {
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = "";
    public string DefaultModel { get; set; } = "anthropic/claude-opus-4.7";
    public int DefaultMaxTokens { get; set; } = 4096;
    public Dictionary<string, ActorConfig> Actors { get; set; } = new();
    public sealed class ActorConfig {
        public string Model { get; set; } = "";
        public int? MaxTokens { get; set; }
    }
}
```
Sektion `"Llm"` in `appsettings.json` mit drei Default-Akteur-Einträgen (`Executor`, `BriefingTreueReviewer`, `KlarheitReviewer`) — alle initial auf `anthropic/claude-opus-4.7`.

**(e) `LlmServiceExtensions.AddLlmClient(IConfiguration)`:**
- Registriert `LlmOptions` aus `"Llm"`-Sektion.
- Setzt Analytics-Header in `DefaultRequestHeaders`: `HTTP-Referer: https://geef.stefan-bechtel.de`, `X-Title: Geef.Atelier`.
- Gibt `IHttpClientBuilder` für Resilience-Chaining zurück.
- In `Program.cs`: `builder.Services.AddLlmClient(builder.Configuration).AddStandardResilienceHandler();`

**(f) `OpenAiMessageFormat` (internal static):**
- `BuildRequestBody(LlmRequest)` — serialisiert zu OpenAI-Chat-Completions-Request-Form.
- `BuildToolChoice(string)` — wandelt Atelier-String `"function:submit_review"` zu OpenAI-Object `{"type":"function","function":{"name":"submit_review"}}`.
- `DeserializeResponse(string)` — parst zu `LlmResponse` mit defensiver `?? throw new JsonException(...)`-Strategie (Pattern aus D-013).
- Token-Mapping: OpenAI-Wire `prompt_tokens`/`completion_tokens` → `LlmTokenUsage.InputTokens`/`OutputTokens` (Property-Namen bleiben für minimale Disruption in `PostgresEventSink`).

**(g) `LlmExecutionStep` und `LlmReviewer` umgestellt:**
- Konstruktor-Typen: `ILlmClient`/`IOptions<LlmOptions>`.
- Pro-Akteur-Modell-Lookup: `options.Value.Actors.GetValueOrDefault("Executor")` (oder Reviewer-Name); Fallback auf `DefaultModel`.
- `LlmReviewer` ToolChoice-String: `"function:submit_review"` (statt Schritt-3 `"tool:submit_review"`).
- Reviewer prüft `FinishReason == "tool_calls"` und `ToolName == "submit_review"`; defensiver Fallback bei fehlendem Tool-Call: `ReviewDecision.Failed` mit `SuggestedRetryHint` (Pattern aus D-013(e) bleibt).

**(h) `LlmActor`-Enum existiert, aber nicht als Lookup-Key:**
```csharp
public enum LlmActor { Executor, BriefingTreueReviewer, KlarheitReviewer }
```
Dient als Typen-Dokumentation und Referenz. String-Keys für `LlmOptions.Actors`-Dictionary verwenden konventionell dieselben Namen (`"Executor"`, etc.).

**(i) Test-Infrastruktur:**
- `FakeLlmClient`, `CriticalFakeLlmClient`, `GatedFakeLlmClient` (mechanische Renames + LlmResponse-Anpassung).
- Erkennungslogik aus D-013(g) bleibt: `request.Tools == null` → Executor-Call, sonst Reviewer-Call.
- Neue `OpenAiCompatibleClientTests` (3 Unit-Tests): ToolCall-Response-Parsing, PlainText-Response, EmptyApiKey-Guard.
- `AtelierPipelineRunsAgainstOpenRouterTests` ersetzt `AtelierPipelineRealAnthropicTests` — Skip via Early-Return bei fehlendem `Llm__ApiKey`.
- `CountingEventSink.TotalEvents`-Property neu hinzugefügt für Integration-Test-Assertion.

**(j) `RunOrchestratorService` minimal angepasst:**
- Konstruktor-Parameter-Typen: `ILlmClient` statt `IAnthropicClient`, `IOptions<LlmOptions>` statt `IOptions<AnthropicOptions>`.
- `AtelierPipelineFactory.Build`-Aufruf mit neuen Typen.
- Keine logischen Änderungen — Orchestrator-Verhalten aus D-016 bleibt vollständig gültig.

**Workflow-Abweichung — bewusst dokumentiert:**

M1 wurde als **parallelisierter Subagent-Auftrag ohne formalen Geef-Workflow-Reviewer-Pass** ausgeführt. Der Bericht sagt explizit: *"Die Reviewer-Pässe wurden durch die Subagent-eigenen Self-Reviews und die Build-/Test-Verifikation ersetzt."*

Konsequenzen:
- **R1 (Functional Correctness)**: Durch Self-Review + 31/31 Tests indirekt abgedeckt — vertretbar.
- **R2 (Code Quality via codex+gpt-5.4)**: **Nicht ausgeführt.** Diese Lücke ist relevant — R2 hat in den vergangenen Schritten konsistent subtile Threading-, Defensiv-Code- und Race-Condition-Issues gefunden, die R1 (Claude-basiert) übersehen hat. Cross-Provider-Reviewer-Effekt fehlt für M1.
- **R3 (Test Execution)**: Durch `dotnet test` mit Docker (31/31 grün) abgedeckt — vertretbar.
- **R4 (Architecture Compliance)**: `geef_architecture.md` existiert (AC7), aber kein dedicated Compliance-Review.
- **R5 (Live UI Sanity Check via Playwright)**: Bei reinem LLM-Layer-Refactor ohne UI-Änderungen irrelevant — Skip begründet.

**Empfohlene Nachholarbeit:** Nach dem Merge in main einen **R2-Pass auf den finalen post-Merge-Stand** ausführen. Dauert ~10 Minuten, fängt potenzielle Race-Conditions im neuen `OpenAiCompatibleClient` und defensive JSON-Deserialisierungs-Lücken im neuen `OpenAiMessageFormat` ab.

**AC9-Status (Real-OpenRouter-Test):** ⏭ Skip — kein `Llm__ApiKey` in Ausführungsumgebung. Skip-Mechanismus korrekt implementiert (Early-Return). **Dringende Empfehlung:** Vor Schritt-7-Beginn einmal manuell mit echtem OpenRouter-Bearer-Key ausführen, um zu verifizieren:
- `anthropic/claude-opus-4.7` ist der stabile Modellname auf OpenRouter (Alternative: `anthropic/claude-opus-4-5`).
- `finish_reason: "tool_calls"` wird konsistent geliefert.
- Tool-Use-Verhalten ist mit dem `submit_review`-Schema kompatibel.
- Cold-Start-Latenz und Token-Verbrauch im realistischen Bereich.

**Merge-Status:** Branch gepusht, Merge in main offen — wartet auf:
1. Schritt-6-Abschluss in main, dann
2. Rebase von M1 auf aktualisierten main (Konflikte: `Program.cs`, `appsettings.json`, `RunOrchestratorService.cs`).

Detail-Plan siehe Merge-Coordination-Notiz [snippets/m1-merge-coordination.md](snippets/m1-merge-coordination.md).