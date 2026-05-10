# Claude-Code-Prompt: Migration M1 — OpenAI-kompatible Provider

*Diese Datei ist als Eingabe für einen **parallel laufenden** Claude-Code-Prozess gedacht. Sie ersetzt nicht die regulären Step-Prompts (Schritt 6 läuft parallel in `main`).*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier** und arbeitest **parallel** zu einem anderen Claude-Code-Prozess, der gerade Schritt 6 (`IRunService`) in `main` umsetzt. Deine Aufgabe ist die **Provider-Migration M1**: die anthropic-spezifische LLM-Schicht durch eine **OpenAI-API-konforme** Schicht ersetzen, die als Default gegen **OpenRouter** spricht.

Was sich ändert: Die LLM-Adapter-Schicht (`Geef.Atelier.Infrastructure/Llm/`), der `LlmExecutionStep`, der `LlmReviewer` und ihre Konfiguration werden umgebaut. Pro Akteur (Executor, BriefingTreueReviewer, KlarheitReviewer) wird ein eigenes Modell konfigurierbar. Tool-Use wechselt auf OpenAI-`function`-Schema.

Was unverändert bleibt: **Alles oberhalb der LLM-Schicht** — Pipeline-Struktur, EventSink, Persistierung, Orchestrator, Domain-Modell, DB-Schema. Du fasst keinen Code an, der nicht zur LLM-Schicht gehört. Diese Disziplin ist absolut wichtig, weil parallel zu dir Schritt 6 in main läuft.

## Branch- und Merge-Strategie (verbindlich)

1. **Erstelle einen neuen Branch** `feature/openai-compatible-providers` von `main` (aktueller Stand: Schritt 5 abgeschlossen).
2. **Arbeite ausschließlich auf diesem Branch.** Nicht in `main`.
3. **Push am Ende** auf den Remote-Branch (`git push -u origin feature/openai-compatible-providers`).
4. **NICHT in main mergen.** Kein Pull-Request automatisch erstellen, kein Merge-Commit. Der Brainstorming-Maintainer entscheidet den Merge-Zeitpunkt manuell.
5. Falls beim Branch-Erstellen eine spätere Schritt-6-Änderung in `main` schon committed ist, ist das OK — dein Branch wird dann beim Merge etwas später trotzdem mit aktuellem `main` rebasen können.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors, Hard Rules. Behandle dies wie einen vollwertigen "Schritt", nur dass es ein Migrations-Track statt eines numerischen Schritts ist.

**Phase-1.4-Hinweis:** Level 2 (`cat /tmp/prompt.txt | claude -p`) hat in den letzten Schritten zuverlässig funktioniert. Plan-Phase-Integration (Architect-Antworten direkt im Plan-Dokument) ist auch valide.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

Vor Beginn der Implementierung lies in dieser Reihenfolge:

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`CLAUDE.md`** im Repo-Root.
3. **`docs/01-vision-and-scope.md`**, besonders die Leitsterne-Sektion mit "Modell-Pluralismus".
4. **`docs/02-architecture.md`**, besonders die Schichten-Sektion. Dieses Dokument **wirst du in deiner Migration aktualisieren** — die LLM-Provider-Sektion bekommt ein Update.
5. **`docs/05-decisions-log.md`**, besonders **D-013 (Schritt-3-Realfakten, die du teilweise ablöst)**, **D-015 (Schritt-4-Realfakten, die unverändert bleiben)**, **D-016 (Schritt-5-Realfakten, die unverändert bleiben)** und **D-017 (deine Mission)**.
6. **`docs/reports/step-03-report.md`** — der ursprüngliche Bericht zur Anthropic-Client-Implementierung. Du verstehst damit, was du ablöst.
7. **Aktueller Code im Branch (vor deinen Änderungen):**
   - `src/Geef.Atelier.Infrastructure/Llm/` komplett (alle Anthropic-Records und Client-Klassen)
   - `src/Geef.Atelier.Infrastructure/Pipeline/LlmExecutionStep.cs`
   - `src/Geef.Atelier.Infrastructure/Pipeline/LlmReviewer.cs`
   - `src/Geef.Atelier.Infrastructure/Pipeline/AtelierPipelineFactory.cs`
   - `src/Geef.Atelier.Infrastructure/Pipeline/AtelierContextKeys.cs` (für `TokenUsage`-Schlüssel)
   - `src/Geef.Atelier.Web/Program.cs` (DI-Registrierung der LLM-Schicht)
   - `src/Geef.Atelier.Web/appsettings.json` (Sektion `"Anthropic"`)
   - `tests/Geef.Atelier.Tests/` — alle `FakeAnthropicClient`-/`CriticalFakeAnthropicClient`-/`GatedFakeAnthropicClient`-Verwendungen
8. **OpenAI Chat Completions API Doku**: Endpoint-Form, Request/Response-Schema, Tool-Use-Pattern (`tools`-Array mit `type: "function"`, `tool_choice` als Object, `tool_calls` in Response mit `arguments` als JSON-String).
9. **OpenRouter-Doku** (`https://openrouter.ai/docs`): Modell-Namen-Konvention (`anthropic/claude-opus-4.7`, `openai/gpt-5`, `google/gemini-2.5-pro`), optionale Header (`HTTP-Referer`, `X-Title`), Pricing-Information, Tool-Use-Kompatibilität.

## Verbindliche Realfakten (unverändert aus Schritten 1–5)

Diese Punkte fasst du **nicht** an. Wenn du in deiner Migration auf eine dieser Strukturen stößt, ändere sie nicht:

- **Geef-SDK-Vokabular** aus D-012: `FindingSeverity { Info, Warning, Error, Critical }`, `DefaultConvergencePolicy`, `EvaluationApprovedEvent`/`RejectedEvent`, `using SdkGeef = Geef.Sdk.Geef;`, `IterationHistory` für PreviousFindings, `Finding.Metadata` als `IReadOnlyDictionary<string, object>`.
- **Tool-Use-Konzept aus D-013**: `submit_review` mit `approved`-Flag und `findings`-Array. Defensive `TryGetProperty`-Parsing. Fallback auf `ReviewDecision.Failed` mit `SuggestedRetryHint` wenn Tool-Call fehlt.
- **Severity-Mapping aus D-015**: `ToAtelierSeverity()` Extension. SDK-`Critical→Critical`, `Error→Major`, `Warning→Minor`, `Info→Info`.
- **Persistierung aus D-015**: `IRunPersistenceService`, `PostgresEventSink` mit injizierter `Guid runId`, `IServiceScopeFactory.CreateAsyncScope()` pro Event, atomare Token-Akkumulation via `ExecuteUpdateAsync`, `_lastExecutionContext` als `volatile`, `JsonSerializerOptions.ReferenceHandler = IgnoreCycles`.
- **Orchestrator aus D-016**: Atomarer Pending→Running-Claim, `SemaphoreSlim`, `ConcurrentDictionary<Guid, Task>`, `WhenAll`-Drain, `OverrideToAbortedAsync` mit `CancellationToken.None`.
- **Convergence-Verhalten**: `ConvergenceFailedException` mit `"AbortCriticalBlocker"` bei Critical-Abort. `PipelineFailedEvent.History.Records[^1].EvaluationResult.AllFindings` für Critical-Abort-Findings.
- **Resilience**: `AddStandardResilienceHandler()` an `IHttpClientBuilder` in `Program.cs`. Pattern bleibt; nur die Client-Klasse ändert sich.
- **Domain-Modell**: `RunEntity`, `IterationEntity`, `FindingEntity`, `EventEntity`, Atelier-`FindingSeverity`. Alles unverändert.

## Konkrete technische Anforderungen für M1

### Neue Datei-Struktur in `src/Geef.Atelier.Infrastructure/Llm/`

Aktuelle anthropic-spezifische Files werden ersetzt:

```
src/Geef.Atelier.Infrastructure/Llm/
├── ILlmClient.cs                       (neu, ersetzt IAnthropicClient)
├── LlmRequest.cs                       (neu, ersetzt AnthropicRequest)
├── LlmResponse.cs                      (neu, ersetzt AnthropicResponse)
├── LlmTool.cs                          (neu, ersetzt AnthropicTool)
├── LlmTokenUsage.cs                    (neu, ersetzt AnthropicTokenUsage)
├── OpenAiCompatibleClient.cs           (neu, ersetzt HttpAnthropicClient)
├── OpenAiMessageFormat.cs              (neu, ersetzt AnthropicMessageFormat)
├── LlmOptions.cs                       (neu, ersetzt AnthropicOptions, mit Pro-Akteur-Mapping)
├── LlmActor.cs                         (neu, Enum für Akteure)
└── LlmServiceExtensions.cs             (neu, ersetzt AnthropicServiceExtensions)
```

Alle Records `internal` oder `public` analog zu vorher (siehe Schritt-3-Realfakten in D-013(a)).

### `ILlmClient`-Interface

```csharp
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken);
}
```

Provider-agnostisch. Implementierungen können OpenRouter, OpenAI direkt, Together, etc. sein. Nur der Default-Adapter (`OpenAiCompatibleClient`) wird in M1 implementiert.

### `LlmRequest`-Record

```csharp
public sealed record LlmRequest
{
    public required string Model { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }
    public int MaxTokens { get; init; } = 4096;
    public IReadOnlyList<LlmTool>? Tools { get; init; }
    public string? ToolChoice { get; init; }   // siehe ToolChoice-Format unten
}
```

`ToolChoice`-Format (Architect entscheidet finale Form, Vorschlag):
- `null` oder fehlt → kein Tool-Zwang
- `"auto"` → Modell entscheidet
- `"required"` → ein Tool-Call ist Pflicht (irgendeines)
- `"function:submit_review"` → konkretes Tool wird erzwungen (Atelier-eigene String-Konvention, intern in OpenAI-Object-Form serialisiert)

### `LlmResponse`-Record

```csharp
public sealed record LlmResponse
{
    public required string Text { get; init; }
    public string? ToolName { get; init; }                  // Name des aufgerufenen Tools, falls Tool-Use
    public string? ToolArgumentsJson { get; init; }         // raw JSON-String der Tool-Arguments
    public required LlmTokenUsage TokenUsage { get; init; }
    public required string FinishReason { get; init; }      // "stop", "tool_calls", "length", ...
}
```

`ToolArgumentsJson` als `string?` analog zur Schritt-3-Entscheidung (D-013(a)) — keine `JsonElement`-Coupling im Interface.

### `LlmTool`-Record

```csharp
public sealed record LlmTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required JsonElement InputSchema { get; init; }   // JSON Schema
}
```

Analog zu `AnthropicTool` aus Schritt 3, nur umbenannt. Schema-Format ist identisch (JSON Schema), nur die Wrapper-Struktur in der Wire-Form ändert sich (OpenAI hat `{"type": "function", "function": {...}}`).

### `LlmTokenUsage`-Record

```csharp
public sealed record LlmTokenUsage
{
    public required int InputTokens { get; init; }      // mappt auf OpenAI prompt_tokens
    public required int OutputTokens { get; init; }     // mappt auf OpenAI completion_tokens
}
```

**Wichtig:** Property-Namen bleiben `InputTokens`/`OutputTokens` für minimale Disruption in `PostgresEventSink` und Token-Akkumulation. Nur die Deserialisierung mappt von OpenAI-Wire-Namen.

### `LlmActor`-Enum

```csharp
public enum LlmActor
{
    Executor,
    BriefingTreueReviewer,
    KlarheitReviewer
}
```

Wird genutzt für Pro-Akteur-Modell-Lookup in `LlmOptions`.

### `LlmOptions`-Konfiguration

```csharp
public sealed class LlmOptions
{
    public string Endpoint { get; set; } = "https://openrouter.ai/api/v1";
    public string ApiKey { get; set; } = "";
    public string DefaultModel { get; set; } = "anthropic/claude-opus-4.7";
    public int DefaultMaxTokens { get; set; } = 4096;

    /// <summary>
    /// Pro-Akteur-Modell-Mapping. Akteur-Name → Modell-ID.
    /// Wenn ein Akteur nicht im Mapping ist, wird DefaultModel verwendet.
    /// </summary>
    public Dictionary<string, ActorConfig> Actors { get; set; } = new();

    public sealed class ActorConfig
    {
        public string Model { get; set; } = "";
        public int? MaxTokens { get; set; }
    }
}
```

`appsettings.json`-Sektion:

```json
{
  "Llm": {
    "Endpoint": "https://openrouter.ai/api/v1",
    "ApiKey": "",
    "DefaultModel": "anthropic/claude-opus-4.7",
    "DefaultMaxTokens": 4096,
    "Actors": {
      "Executor": {
        "Model": "anthropic/claude-opus-4.7",
        "MaxTokens": 8192
      },
      "BriefingTreueReviewer": {
        "Model": "anthropic/claude-opus-4.7",
        "MaxTokens": 2048
      },
      "KlarheitReviewer": {
        "Model": "anthropic/claude-opus-4.7",
        "MaxTokens": 2048
      }
    }
  }
}
```

**Default-Werte alle auf `anthropic/claude-opus-4.7`** — bewusst gleiche Modelle für alle Akteure als Skeleton-Default. Cross-Modell-Setup (z.B. `openai/gpt-5` als Reviewer) ist konfigurativ jederzeit möglich, aber kein Default. **Modell-Pluralismus ist ab M1 konfigurativ verfügbar, ist aber nicht der Out-of-the-Box-Zustand.**

Environment-Variable-Override: `Llm__ApiKey` für den Bearer-Key.

### `OpenAiCompatibleClient`-Implementierung

`internal sealed class OpenAiCompatibleClient : ILlmClient`. Nutzt `HttpClient` via `IHttpClientFactory` (Typed Client).

**HTTP-Details:**
- Endpoint: `{LlmOptions.Endpoint}/chat/completions` (also `https://openrouter.ai/api/v1/chat/completions`)
- Header: `Authorization: Bearer {apiKey}` (analog zu D-013(b): per Request setzen, **nicht** in `DefaultRequestHeaders`)
- Optionale OpenRouter-Analytics-Header in `DefaultRequestHeaders`: `HTTP-Referer: https://geef.stefan-bechtel.de`, `X-Title: Geef.Atelier`
- Timeout: 120 Sekunden (wie in Schritt 3)
- Lazy-Validation des `ApiKey` beim ersten Aufruf

**Request-Body-Mapping:**

```jsonc
{
  "model": "<request.Model>",
  "messages": [
    { "role": "system", "content": "<request.SystemPrompt>" },
    { "role": "user",   "content": "<request.UserPrompt>" }
  ],
  "max_tokens": "<request.MaxTokens>",
  "tools": [
    {
      "type": "function",
      "function": {
        "name": "<tool.Name>",
        "description": "<tool.Description>",
        "parameters": "<tool.InputSchema (JsonElement)>"
      }
    }
  ],
  "tool_choice": {                              // wenn ToolChoice == "function:submit_review"
    "type": "function",
    "function": { "name": "submit_review" }
  }
}
```

**Response-Body-Parsing:**

```jsonc
{
  "choices": [{
    "message": {
      "role": "assistant",
      "content": null,
      "tool_calls": [{
        "id": "call_xyz",
        "type": "function",
        "function": {
          "name": "submit_review",
          "arguments": "{\"approved\":false,\"findings\":[...]}"   // <-- STRING, kein Object
        }
      }]
    },
    "finish_reason": "tool_calls"
  }],
  "usage": {
    "prompt_tokens": 123,
    "completion_tokens": 456,
    "total_tokens": 579
  }
}
```

→ `LlmResponse`:
- `Text` = `choices[0].message.content ?? ""`
- `ToolName` = `choices[0].message.tool_calls[0].function.name` (falls vorhanden)
- `ToolArgumentsJson` = `choices[0].message.tool_calls[0].function.arguments` (raw String, nicht weiter parsen)
- `TokenUsage.InputTokens` = `usage.prompt_tokens`
- `TokenUsage.OutputTokens` = `usage.completion_tokens`
- `FinishReason` = `choices[0].finish_reason`

**Defensive Deserialisierung** wie in Schritt 3 (D-013): `?? throw new JsonException(...)` statt `!`-Operator. `TryGetProperty` für optionale Felder.

### `LlmExecutionStep` (anpassen)

- Konstruktor: `ILlmClient` statt `IAnthropicClient`. `IOptions<LlmOptions>` statt `IOptions<AnthropicOptions>`.
- Modell-Lookup: `options.Value.Actors.GetValueOrDefault("Executor")?.Model ?? options.Value.DefaultModel`. Analog für `MaxTokens`.
- `LlmRequest` statt `AnthropicRequest`.
- `AtelierContextKeys.TokenUsage`-Typ ändert sich von `ContextKey<AnthropicTokenUsage>` zu `ContextKey<LlmTokenUsage>` — der Sink-Code in `PostgresEventSink` muss entsprechend mit-angepasst werden, aber **nur die Type-Reference**, nicht die Logik.
- Notes-String-Format bleibt: `"tokens_in=X tokens_out=Y"` (Rückwärtskompatibilität).

### `LlmReviewer` (anpassen)

- Konstruktor: `ILlmClient`, `IOptions<LlmOptions>`, `string actorName` (entweder `"BriefingTreueReviewer"` oder `"KlarheitReviewer"`).
- Modell-Lookup analog.
- Tool-Definition: `LlmTool` statt `AnthropicTool`.
- `tool_choice`-Wert: `"function:submit_review"` (Atelier-Konvention, in `OpenAiCompatibleClient` zu OpenAI-Object serialisiert).
- `ParseToolInput`: Liest `response.ToolArgumentsJson` (raw String) und parst zu `JsonDocument`. Defensive `TryGetProperty` wie in Schritt 3 (D-013(e)).
- Fallback bei `ToolName == null` oder fehlenden Properties: `ReviewDecision.Failed` mit `SuggestedRetryHint`.
- `ReviewerToolDefinition` (statisches Tool-Schema) wird zu `LlmTool` umgebaut. Schema-Inhalt identisch wie in Schritt 3.

### `AtelierPipelineFactory` (minimal anpassen)

- Signatur: `Build(ILlmClient, IOptions<LlmOptions>, ILoggerFactory?, IEnumerable<IGeefEventSink>?)` — statt `IAnthropicClient`/`IOptions<AnthropicOptions>`.
- Innere Provider-Konstruktion ändert sich entsprechend.
- `BuildWithProviders(...)` Test-Hook bleibt bestehen, nur Type-Parameter geändert.
- **Wichtig:** Die Schritt-5-Realfakten zur `Build(...)`-Aufruf-Stelle in `RunOrchestratorService` (siehe D-016(e)) gelten weiterhin — nur dass dort jetzt `ILlmClient` statt `IAnthropicClient` injiziert wird. Der Orchestrator-Code selbst muss minimal angepasst werden (Konstruktor-Parameter-Typ).

### `LlmServiceExtensions`

```csharp
public static IHttpClientBuilder AddLlmClient(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.Configure<LlmOptions>(configuration.GetSection("Llm"));

    return services.AddHttpClient<ILlmClient, OpenAiCompatibleClient>(client =>
    {
        // BaseAddress dynamisch aus IOptions im Client (DefaultRequestHeaders auch)
        client.Timeout = TimeSpan.FromSeconds(120);
    });
}
```

In `Program.cs`:
```csharp
builder.Services.AddLlmClient(builder.Configuration).AddStandardResilienceHandler();
```

### `RunOrchestratorService` (minimal anpassen)

Nur Konstruktor-Parameter-Typen: `ILlmClient` statt `IAnthropicClient`, `IOptions<LlmOptions>` statt `IOptions<AnthropicOptions>`. Sonst keine Änderungen am Orchestrator.

### Tests anpassen

**Umbenennungen** (mechanisch):
- `FakeAnthropicClient` → `FakeLlmClient`
- `CriticalFakeAnthropicClient` → `CriticalFakeLlmClient`
- `GatedFakeAnthropicClient` → `GatedFakeLlmClient`
- Alle Test-Helper, die `AnthropicOptions`/`AnthropicTokenUsage` nutzen, entsprechend.

**Inhaltliche Anpassung der Fakes:**
- `FakeLlmClient` muss `LlmRequest`/`LlmResponse` zurückgeben.
- Token-Usage-Setzen: `new LlmTokenUsage { InputTokens = 100, OutputTokens = 50 }` (statt Anthropic-Variante).
- Tool-Call-Mock: Wenn `request.Tools` gesetzt ist und `request.ToolChoice` auf `submit_review` zeigt, Response mit `ToolName = "submit_review"` und `ToolArgumentsJson = "{...}"` zurückgeben.
- Erkennungslogik aus D-013(g) bleibt: `request.Tools == null` → Executor-Call, sonst Reviewer-Call.

**Neue Integration-Tests:**

**`AtelierPipelineRunsAgainstOpenRouter`** (statt `AtelierPipelineRealAnthropicTests`):
- Echter HTTP-Call gegen OpenRouter mit dem in `Llm__ApiKey` gesetzten Bearer-Key.
- Briefing: `"Schreibe einen kurzen Text über Walking-Skeleton-Pattern."`
- Modell: Default `anthropic/claude-opus-4.7`.
- Verifiziert: Pipeline läuft durch (2–3 Iterationen), Output nicht-leer, Token-Verbrauch > 0.
- Bei fehlendem Key: Skip via Early-Return (analog zu Schritt 3).

**`OpenAiCompatibleClientHandlesToolCallResponse`** (Unit-Test):
- Mock `HttpMessageHandler`, der eine OpenAI-formatierte Tool-Call-Response zurückgibt.
- Verifiziert: `LlmResponse.ToolName == "submit_review"`, `ToolArgumentsJson` ist raw String, `TokenUsage` korrekt gemappt.

**`OpenAiCompatibleClientHandlesPlainTextResponse`** (Unit-Test):
- Mock-Handler mit Standard-Text-Response (kein Tool).
- Verifiziert: `Text` gefüllt, `ToolName == null`, `ToolArgumentsJson == null`, `FinishReason == "stop"`.

**`OpenAiCompatibleClientThrowsOnEmptyApiKey`** (Unit-Test):
- `LlmOptions` ohne `ApiKey`.
- Verifiziert: Erste `CompleteAsync`-Aufruf wirft `InvalidOperationException` mit klarer Botschaft.

**Bestehende Tests:** Alle 19 Tests aus Schritten 1–5 müssen nach der Migration weiter grün bleiben. Der mechanische Umbau (Type-Renames, Field-Renames) sollte das einfach durchziehen.

### `02-architecture.md` aktualisieren

Die LLM-Provider-Sektion in `docs/02-architecture.md` muss aktualisiert werden:
- "Anthropic-spezifischer Adapter" → "OpenAI-API-konformer Adapter (Default: OpenRouter)"
- Pro-Akteur-Modell-Konfiguration als neue Sektion
- Verweis auf D-017
- Hinweis dass weitere Endpoints (OpenAI direkt, lokales Ollama, Together, etc.) durch denselben Adapter-Code ansprechbar sind

Konkrete Wording-Updates entscheidet der Architect in Phase 1.4 (Diff im Plan-Dokument vorschlagen, dann committen).

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` ohne Fehler oder Warnings.
2. `dotnet test` (mit Docker-Daemon für Testcontainers, ohne `Llm__ApiKey`): alle Tests grün — 19 bestehende (mechanisch umbenannt) + neue OpenAI-Compatible-Client-Unit-Tests. Integration-Test Skip.
3. `dotnet test` (mit gesetztem `Llm__ApiKey`): zusätzlich `AtelierPipelineRunsAgainstOpenRouter` grün.
4. Provider-agnostisches Interface `ILlmClient` etabliert; keine `Anthropic*`-Type-Namen mehr im Code (außer in Migrations-Notes / Doku).
5. Pro-Akteur-Modell-Konfiguration funktioniert: Test mit Custom-Modell-Mapping in `LlmOptions` zeigt, dass Executor und Reviewer unterschiedliche Modelle anrufen können.
6. `02-architecture.md` reflektiert die neue LLM-Schicht.
7. `geef_architecture.md` existiert (R4-Pflicht, wird in Phase 4.3 gelöscht).
8. **Branch ist gepusht, aber NICHT in main gemerged.** Kein automatischer Pull-Request, kein Merge-Commit.

## Was du in dieser Migration NICHT tust

- **Keinen Code außerhalb der LLM-Schicht ändern.** Ausnahmen klar enumeriert oben (Type-Reference-Updates in `LlmExecutionStep`, `LlmReviewer`, `AtelierPipelineFactory`, `RunOrchestratorService`-Konstruktor-Parameter, Test-Helper-Renames). Alles andere bleibt unverändert.
- **Keinen IRunService / Application-Layer anlegen** — das ist Schritt 6 und läuft parallel in main.
- **Keine UI** — Schritt 7.
- **Keinen Multi-Endpoint-Support** — nur OpenRouter via `OpenAiCompatibleClient`. Andere Provider wären eine separate Migration M2 (oder ergänzen sich später durch Konfiguration).
- **Keine Streaming-Unterstützung** — `OpenAiCompatibleClient` macht synchrone `POST`-Requests, keine SSE/Streaming. Streaming wäre eine Erweiterung post-Skeleton.
- **Keine Cost-Berechnung** — OpenRouter liefert Pro-Modell-Pricing-Info via `/models`-Endpoint, das ist für später.
- **Kein Merge in main.** Nur Branch pushen.

## Architect-Konsultation (Phase 1.4) — sechs Schwerpunkte

1. **`ToolChoice`-Repräsentation:** String-Convention `"function:submit_review"` (Atelier-intern, im Client zu Object serialisiert) vs. struct/sealed-record für TypeSafe-Variante. Impact auf API-Sauberkeit.
2. **Pro-Akteur-Lookup-Pattern:** `string actorName` als Konstruktor-Parameter im `LlmReviewer` (aktuell vorgeschlagen) vs. `LlmActor`-Enum vs. dedicated `IActorModelResolver`-Service. Welches Pattern ist am cleansten für spätere Erweiterung (z.B. dynamische Akteure aus Crew-Composition)?
3. **`OpenAiMessageFormat`-Position:** internal static class neben `OpenAiCompatibleClient` (analog zu Schritt 3) vs. partial methods am Client selbst. Wartbarkeit bei späterem zweitem Adapter (z.B. Anthropic-Native für Bearer-Key-Setups).
4. **Endpoint-Override-Pfad:** `LlmOptions.Endpoint` als String konfigurierbar (Default OpenRouter) vs. nur Hardcoded-Default mit Override für Tests. Was ist clean genug für Skeleton?
5. **`02-architecture.md`-Update-Form:** Komplettumschreibung der LLM-Provider-Sektion vs. inkrementelles Update mit Verweis auf D-017. Welche Form passt besser zur Doku-Hierarchie?
6. **`anthropic-version`-Header-Erbe:** Bei Anthropic war der Header `anthropic-version: 2023-06-01` Pflicht. OpenAI hat keinen vergleichbaren Header. Aber wenn jemand später einen Anthropic-Native-Bearer-Key einsetzen will, müsste der Header zurück. Architectural-Future-Proofing-Frage.

`geef_architecture.md` prüft Konsistenz mit `docs/02-architecture.md` und allen verbindlichen Realfakten aus D-010 bis D-016 (D-017 ist deine Mission selbst).

## Persistenter Abschlussbericht

Bericht nach `docs/reports/migration-01-report.md`, gleicher Aufbau wie Schritt-Berichte. Wichtig:

1. **Was wurde umgesetzt** — Datei-für-Datei.
2. **Annahmen und Abweichungen** — vor allem zur OpenAI/OpenRouter-API-Form (welche genaue Tool-Choice-Object-Form? Welche Modelle haben getestet getestet? Welcher OpenRouter-Endpoint-Pfad?).
3. **Architect-Output** — alle sechs Pflichtfragen.
4. **Pre-Mortem & Devil's Advocate** — speziell zu: Modell-Verfügbarkeit auf OpenRouter (was wenn `anthropic/claude-opus-4.7` nicht ausgelistet?), Tool-Call-Verhalten-Variabilität zwischen Modellen (Claude vs. GPT vs. Gemini), Rate-Limits auf OpenRouter, Wire-Format-Edge-Cases (z.B. content als String vs. Array bei manchen Modellen).
5. **Reviewer-Iterationen** — Tabelle.
6. **Akzeptanzkriterien-Check** — Tabelle, inklusive AC für Branch-Push-ohne-Merge.
7. **Beobachtungen zu OpenRouter** — Latenz, Token-Verbrauch, Retry-Verhalten, Tool-Call-Verlässlichkeit beim Default-Modell.
8. **Empfehlungen für den Merge zurück in main** — was muss beim Merge mit Schritt-6-State beachtet werden (insbesondere `Program.cs`-Konflikt mit `IRunService`-Registrierung)? Welche Tests müssen nach dem Merge erneut laufen?
9. **Status echter Integration-Test** — lief er grün? Was waren echte Token-Verbräuche und Latenzen?

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- API-Key niemals in source control, niemals in Logs, niemals im Bericht.
- **Branch arbeiten, nicht main.** Nicht mergen, nur pushen.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.