# Claude-Code-Prompt: Schritt 3 — Anthropic-Client und echte Provider

*Diese Datei ist als Eingabe für Claude Code gedacht.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Schritt 1 (Solution-Setup) und Schritt 2 (Pipeline-Skelett mit Stub-Providern) sind abgeschlossen. Deine Aufgabe ist **Schritt 3 von 10**: die **Stub-Provider durch echte Anthropic-API-Aufrufe ersetzen**.

Was sich ändert: `StubExecutionStep` und `StubReviewer` werden durch `LlmExecutionStep` und `LlmReviewer` ersetzt, die Anthropic via HTTP ansprechen. Was bleibt unverändert: die Pipeline-Struktur, `BriefingGroundingStep`, `MarkdownFinalizer`, `AtelierContextKeys`, das Domain-Modell, alle Schritt-1-2-Tests.

DB-Persistierung kommt erst in Schritt 4. Background-Service in Schritt 5. UI in Schritt 7. MCP in Schritt 9. Diese Disziplin halten — wir tauschen jetzt nur die Provider gegen echte LLM-Calls aus.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors, alle Hard Rules. Bei Konflikten zwischen diesem Prompt und dem Workflow gilt der Workflow.

### Atelier-spezifische Phase-1.4-Konvention (Level-4-Fallback)

Falls die Workflow-Levels 1–3 alle scheitern (in Schritt 2 war das der Fall), aktiviere den Atelier-Level-4-Fallback gemäß D-011(B):
1. Pflicht-Header in `geef_architecture.md`: `> ⚠️ Architect-Fallback: Levels 1–3 failed (see report). Executor-authored — verify against existing architecture docs.`
2. Explizite Diff-Sektion gegen `docs/02-architecture.md`.
3. Dokumentation aller Level-Fehler im Phase-4-Bericht.

In Schritt 2 war Level 1 ein Permissions-Problem. Probiere Level 1 mit `--dangerously-skip-permissions` als ersten Versuch — falls das durchläuft, dokumentiere es und nutze es. Falls nicht, Level 3 oder Atelier-Level-4.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

Vor Beginn der Implementierung lies in dieser Reihenfolge:

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`CLAUDE.md`** im Repo-Root.
3. **`docs/02-architecture.md`** — Layer-Architektur und Mapping auf GEEF.
4. **`docs/03-walking-skeleton-plan.md`**, Abschnitt "Schritt 3" — der Scope.
5. **`docs/05-decisions-log.md`**, Einträge **D-010, D-011, D-012** — Realfakten und SDK-Korrekturen.
6. **`docs/reports/step-02-report.md`** — kompletter Schritt-2-Bericht. **Besonders Sektion 7 (Beobachtungen zum SDK) und Sektion 8 (Empfehlungen für Schritt 3).**
7. **Aktueller Code im Repo:** Lies `src/Geef.Atelier.Infrastructure/Pipeline/` komplett — du baust direkt darauf auf. Schau dir an, wie `StubExecutionStep`, `StubReviewer`, `StubPipelineFactory`, `AtelierContextKeys` jetzt aussehen.
8. **Anthropic Messages API:** Offizielle Doku zu `/v1/messages`, Tool Use, JSON Schema-basierter Output. Endpunkt-Form, Authentication-Header, Response-Format, Token-Counting.

## Verbindliche SDK-Realfakten (aus Schritt 2 fixiert)

Diese sind verbindlich. Brainstorming-Doku-Annahmen, die diesen widersprechen, sind ungültig:

- **Severity-Enum:** `Geef.Sdk.Results.FindingSeverity` mit Werten `{ Info, Warning, Error, Critical }`. Voll-qualifizieren wegen Namespace-Konflikt mit Atelier-eigenem `FindingSeverity`.
- **Convergence-Policy:** `DefaultConvergencePolicy { MaxIterations, AbortOnCritical, DetectRegression, StagnationThreshold }`. **Achtung:** `AbortOnCritical = true` ist gesetzt — ein echter Reviewer mit Critical-Finding stoppt die Pipeline hart.
- **Middleware:** `UseMiddleware<TMiddleware>()` oder `UseMiddleware(IGeefMiddleware)` — generisch, nicht parameterlos.
- **Events:** `EvaluationApprovedEvent`, `EvaluationRejectedEvent` — keine PhaseStarted/Completed.
- **PreviousFindings-Access:** Über `GeefKeys.IterationHistory`, nicht direkt `GeefKeys.PreviousFindings` (Symbols-Workaround). **In Schritt 3 prüfen:** ist mit `Geef.Sdk 1.0.0` (stable) der direkte Zugriff jetzt möglich? Falls ja, refactoren — falls nein, Workaround beibehalten.
- **Namespace-Alias:** `using SdkGeef = Geef.Sdk.Geef;` für statische Builder-Methoden.
- **Logger:** `loggerFactory.CreateLogger("Geef.Atelier.Pipeline")` (String-Overload), nicht `CreateLogger<TStaticClass>()`.
- **Finding.Metadata:** `IReadOnlyDictionary<string, object>` — bei Erzeugung als `new Dictionary<string, object>()`.
- **Finalizer-Result:** `IFinalizeResult<T>` benötigt explizit gesetzten `FinalContext`.

## In Schritt 1+2 etablierte Realfakten

- **NuGet:** `Geef.Sdk 1.0.0-ci.1`. Falls `1.0.0` stable während der Arbeit verfügbar ist, darfst du updaten — und prüfe dabei den `PreviousFindings`-Punkt oben.
- **Solution-Format:** `.slnx`. Build-Properties zentral in `Directory.Build.props`. `TreatWarningsAsErrors=true`, `CS1591` global suppressed.
- **Provider-Sichtbarkeit:** `internal sealed`, mit `<InternalsVisibleTo Include="Geef.Atelier.Tests" />` in `Geef.Atelier.Infrastructure.csproj` — bleibt bestehen.
- **Context-Keys:** `AtelierContextKeys` in `src/Geef.Atelier.Infrastructure/Pipeline/` mit `geef:atelier:`-Präfix. Erweiterbar, aber bestehende Keys nicht umbenennen.
- **Pipeline-Konstruktion:** `StubPipelineFactory` ist die aktuelle Form. **Umbenennen** auf `AtelierPipelineFactory` (das "Stub" gehört zu den Providern, nicht zum Builder). Methode bleibt `Build(IServiceProvider services)`. DI-Container-Migration kommt in Schritt 6.
- **Migration-Strategie, Health-Check, UI-Component-Library:** alle unverändert.
- **Domain:** `FinalizedDocument` bleibt mit `Markdown` und `IterationCount` — **Token-Verbrauch kommt nicht hier rein**. Token-Tracking läuft via EventSink (in Schritt 4 persistiert).

## Modell-Wahl für Schritt 3

**Beide Provider (Executor und Reviewer) nutzen `claude-opus-4-7`.** Bewusst gleiches Modell — der "Außenperspektive durch anderes Modell"-Aspekt aus der Vision (siehe `01-vision-and-scope.md`, Leitstern "Modell-Pluralismus") kommt erst nach dem Skeleton, zusammen mit dem Multi-Provider-Adapter (OpenAI, OpenRouter). Schritt 3 hat einen anderen Fokus: **dass Anthropic-Calls überhaupt sauber durch die Pipeline laufen**, nicht dass die Outputs maximal divergent sind.

Konfigurations-Default ist also für beide:
```json
"Anthropic": {
  "ExecutorModel": "claude-opus-4-7",
  "ReviewerModel": "claude-opus-4-7"
}
```

## API-Key-Bereitstellung

**Wichtig — zwei klar getrennte Pfade:**

### Pfad A: Production-Code (im Repo)

Der Code liest den API-Key nach Standard-ASP.NET-Core-Pattern aus `IConfiguration`. Production setzt die Environment-Variable `Anthropic__ApiKey` — analog zu `ConnectionStrings__Postgres` aus Schritt 1. Im `appsettings.json` steht der Schlüssel mit leerem Wert (`""`) als Default; die echte Belegung erfolgt ausschließlich über Environment.

Der `HttpAnthropicClient` validiert beim ersten Aufruf, dass der Key konfiguriert ist, und wirft sonst eine klare `InvalidOperationException` mit dem Hinweis, welche Environment-Variable zu setzen ist. **Kein API-Key-Material kommt in Source Control oder in Logs.**

### Pfad B: Schritt-3-Entwicklung und Tests (du, Claude Code)

Für die Integration-Tests in diesem Schritt brauchst du einen echten Anthropic-API-Key. **Du stellst diesen Key selbst aus deinen eigenen verfügbaren Credentials/Settings bereit** — du musst dafür nicht den Brainstorming-Chat-User um Bereitstellung bitten und du legst keinen Key im Repo ab.

Konkret heißt das: vor dem Aufruf von `dotnet test` exportierst du den dir verfügbaren Key in die Test-Shell-Umgebung:

```bash
export Anthropic__ApiKey="<dein verfügbarer Key>"
dotnet test
```

Oder direkt inline:
```bash
Anthropic__ApiKey="<key>" dotnet test
```

Beides geht. Wichtig:
- **Kein `git add`** auf irgendeine Datei, die den Key enthält. Nicht in `appsettings.Development.json`, nicht in `.env`, nicht in `launchSettings.json`. Wenn du Test-Skripte schreibst, die den Key referenzieren: lies aus der Shell-Env-Variable, schreibe nicht.
- **Im Bericht** erwähnst du, dass der Integration-Test mit einem zur Build-Zeit verfügbaren Key gelaufen ist — aber **nicht den Key selbst**.
- Falls du keinen Key zur Verfügung hast: dann die Integration-Tests skippen lassen (siehe Test-Sektion C unten) und im Bericht klar dokumentieren — Mock-Tests müssen trotzdem grün sein.

## Konkrete technische Anforderungen für Schritt 3

### `IAnthropicClient` (in `src/Geef.Atelier.Infrastructure/Llm/`)

Neuer Namespace `Geef.Atelier.Infrastructure.Llm`. Schnittstelle:

```csharp
public interface IAnthropicClient
{
    Task<AnthropicResponse> CompleteAsync(
        AnthropicRequest request,
        CancellationToken cancellationToken);
}

public sealed record AnthropicRequest
{
    public required string Model { get; init; }
    public required string SystemPrompt { get; init; }
    public required string UserPrompt { get; init; }
    public int MaxTokens { get; init; } = 4096;
    public IReadOnlyList<AnthropicTool>? Tools { get; init; }
    public string? ToolChoice { get; init; }  // z.B. "required" oder spezifischer Tool-Name
}

public sealed record AnthropicResponse
{
    public required string Text { get; init; }
    public IReadOnlyDictionary<string, object>? ToolInput { get; init; }
    public required AnthropicTokenUsage TokenUsage { get; init; }
    public required string StopReason { get; init; }
}

public sealed record AnthropicTokenUsage
{
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
}

public sealed record AnthropicTool
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required object InputSchema { get; init; }  // JSON Schema als anonymes Objekt
}
```

**Implementierung:** `internal sealed class HttpAnthropicClient : IAnthropicClient`
- HTTP gegen `https://api.anthropic.com/v1/messages`.
- Header: `x-api-key`, `anthropic-version: 2023-06-01`, `content-type: application/json`.
- API-Key aus `IConfiguration["Anthropic:ApiKey"]`. Bei leerem Wert: klare `InvalidOperationException` beim ersten Aufruf.
- Polly-Resilience oder einfaches Retry: **vom Architect entscheiden lassen** — defaultmäßig 3 Retries mit exponentialem Backoff bei 429/503. Bei 429: `retry-after` Header ehren falls vorhanden.
- `HttpClient` via `IHttpClientFactory`, registriert in DI mit Named Client `"anthropic"`.

### `LlmExecutionStep` ersetzt `StubExecutionStep`

Schreibt die echte Generierung. Wichtige Punkte:
- Liest Briefing (`AtelierContextKeys.GroundedBrief` oder wie auch immer der Schritt-2-Key heißt), aktuelle Iteration via `GeefKeys.CurrentIteration`, vorherige Findings via `GeefKeys.IterationHistory.Records[^1].EvaluationResult.AllFindings` (Iteration ≥ 2).
- System-Prompt baut sich zusammen aus: Atelier-Standard-System-Prompt (in Konstanten-Klasse) + Briefing als Kontext.
- User-Prompt:
  - Iteration 1: `"Schreibe einen Text gemäß dem Briefing oben."`
  - Iteration ≥ 2: `"Hier ist dein vorheriger Entwurf: ...\n\nDie Reviewer haben folgende Findings gemeldet:\n- {finding1}\n- {finding2}\n\nSchreibe den Text neu, adressiere alle Findings."` Vorheriger Entwurf kommt aus dem Context (z.B. `AtelierContextKeys.LatestDraft`).
- Modell: `claude-opus-4-7` aus `IConfiguration["Anthropic:ExecutorModel"]`.
- Schreibt das Ergebnis in den Context unter dem Schritt-2-etablierten Key (z.B. `LatestDraft`).
- Token-Verbrauch via Custom-Event ins EventSink schicken (vorbereitend für Schritt 4) — bzw. an einen `ITokenAccumulator`-Service den der Architect mag definieren.

### `LlmReviewer` ersetzt `StubReviewer` × 2

Zwei eigenständige Reviewer mit unterschiedlichen System-Prompts:
- **`BriefingTreueReviewer`** — prüft, ob der Text das Briefing adressiert.
- **`KlarheitReviewer`** — prüft Verständlichkeit, Argumentation, Sprache.

Beide nutzen Modell `claude-opus-4-7` aus `IConfiguration["Anthropic:ReviewerModel"]`.

**Strukturierter Output:** Nutze Anthropic Tool Use mit einem `submit_review`-Tool:
```jsonc
{
  "name": "submit_review",
  "description": "Submit your review findings...",
  "input_schema": {
    "type": "object",
    "properties": {
      "approved": { "type": "boolean" },
      "findings": {
        "type": "array",
        "items": {
          "type": "object",
          "properties": {
            "severity": { "type": "string", "enum": ["info", "warning", "error", "critical"] },
            "message": { "type": "string" }
          },
          "required": ["severity", "message"]
        }
      }
    },
    "required": ["approved", "findings"]
  }
}
```
Mit `tool_choice: {"type": "tool", "name": "submit_review"}` — erzwingt strukturierten Output.

Severity-Mapping: lowercase im JSON → `Geef.Sdk.Results.FindingSeverity` Enum-Wert.

### Pipeline-Builder-Anpassung

`StubPipelineFactory` umbenennen auf `AtelierPipelineFactory`. Provider-Liste:
- `BriefingGroundingStep` (unverändert)
- `LlmExecutionStep` (neu, ersetzt Stub)
- `BriefingTreueReviewer` (neu, ersetzt einen Stub)
- `KlarheitReviewer` (neu, ersetzt anderen Stub)
- `MarkdownFinalizer` (unverändert)

Convergence-Policy unverändert (`DefaultConvergencePolicy` mit `MaxIterations = 3` etc.). EventSink unverändert.

### Tests (in `tests/Geef.Atelier.Tests/`)

Drei Test-Kategorien:

**A) Mock-basierte Tests** (default — `dotnet test` muss ohne API-Key laufen können):
- `AtelierPipelineConvergesWithMockClient` — Fake `IAnthropicClient` der deterministisch antwortet (Iter 1: Reviewer findet was, Iter 2: Reviewer approved). Verifiziert dieselben Akzeptanzkriterien wie Stub-Tests aus Schritt 2 plus den neuen Provider-Code.
- `AtelierPipelineEmitsExpectedEvents` — wie in Schritt 2, aber mit dem neuen Provider-Setup.
- **`AtelierPipelineAbortsOnCriticalFinding`** (Pflicht aus Empfehlung 6 des Schritt-2-Berichts): Mock-Reviewer der ein `Critical`-Finding zurückgibt. Pipeline muss `AbortOnCritical = true` ehren und stoppen. Test verifiziert: kein Final-Output, ein `PipelineAbortedEvent` (oder wie das SDK es nennt — im SDK lesen), Iterations-Count entspricht der Iteration in der das Critical kam.

**B) Integration-Test** (markiert mit `[Trait("Type", "Integration")]` oder `Skip`-Attribut bei fehlendem Key):
- `AtelierPipelineRunsAgainstRealAnthropic` — echter HTTP-Call gegen Anthropic mit einem trivialen Briefing (`"Schreibe einen kurzen Text über Walking-Skeleton-Pattern."`). Prüft: läuft durch (kann 2 oder 3 Iterationen nehmen), produziert nicht-leeren Output, Token-Verbrauch ist nicht null. Liest API-Key aus `Anthropic__ApiKey` (oder `IConfiguration["Anthropic:ApiKey"]` über `WebApplicationFactory`/`HostBuilder` Test-Setup). Bei fehlendem Key: Skip via xUnit `Skip.If` o.ä.

**C) Bestehende Tests:**
Alle 7 Tests aus Schritt 1 + 2 müssen weiterhin grün bleiben. Falls Provider-Umbenennung Test-Fixtures bricht, anpassen — aber nicht weglassen.

### HTTP-Client-Konfiguration (in `Program.cs` der Web-App)

```csharp
builder.Services.AddHttpClient("anthropic", client =>
{
    client.BaseAddress = new Uri("https://api.anthropic.com/");
    client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
});
builder.Services.AddSingleton<IAnthropicClient, HttpAnthropicClient>();
```

Konfiguration via `appsettings.json` Sektion `"Anthropic"`:
```json
{
  "Anthropic": {
    "ExecutorModel": "claude-opus-4-7",
    "ReviewerModel": "claude-opus-4-7",
    "ApiKey": ""
  }
}
```

`ApiKey: ""` bewusst leer in Source Control. Production: `Anthropic__ApiKey` als Environment-Variable. Schritt-3-Tests: du setzt die Variable selbst aus deinen Credentials (siehe oben).

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` ohne Fehler oder Warnings.
2. `dotnet test` (ohne `Anthropic__ApiKey`): alle Mock-Tests grün, Integration-Test Skip.
3. `dotnet test` (mit gesetztem `Anthropic__ApiKey`): alle Tests grün, inklusive `AtelierPipelineRunsAgainstRealAnthropic`.
4. `AtelierPipelineConvergesWithMockClient` läuft in 2 Iterationen durch.
5. `AtelierPipelineAbortsOnCriticalFinding` zeigt Pipeline-Abbruch bei Critical-Finding.
6. `AtelierPipelineRunsAgainstRealAnthropic` produziert nicht-leeren Output mit Token-Verbrauch > 0.
7. `geef_architecture.md` existiert (R4-Pflicht).

## Was du in diesem Schritt NICHT tust

- **Keine DB-Persistierung** — kommt in Schritt 4. EventSink bleibt `LoggingEventSink`.
- **Kein BackgroundService** — Tests rufen den Runner direkt auf. Schritt 5.
- **Keine UI-Anbindung** — Schritt 7.
- **Kein `IRunService`** — Schritt 6.
- **Kein MCP** — Schritt 9.
- **Kein Multi-Provider** (OpenAI, OpenRouter) — kommt nach Skeleton.
- **Kein Modell-Pluralismus** zwischen Executor und Reviewer — beide Opus 4.7. Cross-Modell + Cross-Provider kommt nach Skeleton.
- **Keine Token-/Cost-Persistierung** — nur Logging via EventSink.
- **Keine Erweiterung von `FinalizedDocument`** — Markdown + IterationCount reichen.
- **Kein API-Key in Source Control** — auch nicht in `appsettings.Development.json`, `.env`-Files, oder Test-Skripten.

## Architect-Konsultation (Phase 1.4) — fünf Schwerpunkte

Der Architect bekommt diese Schwerpunkte:

1. **Anthropic-Client-DI:** `HttpAnthropicClient` als Singleton oder Scoped? `IHttpClientFactory` Named oder Typed Client? Welcher Namespace passt am besten zu `Llm/`?
2. **API-Key-Handling:** Direkt aus `IConfiguration` oder via `IOptions<AnthropicOptions>` Pattern? Wo erfolgt die "Key fehlt"-Validierung — Startup oder Lazy beim ersten Call?
3. **Resilience:** Polly oder native HttpClient-Retries? Bei 429 spezifisch handhaben (`retry-after`-Header). Welche Timeouts (Default vs. Reviewer-Calls)?
4. **Reviewer-Output-Schema:** Tool Use mit `tool_choice: required` reicht, oder zusätzlich strikte JSON-Schema-Validierung clientseitig? Wie handhaben wir den seltenen Fall, dass Anthropic trotz Tool-Use den Tool nicht ruft?
5. **PreviousFindings-Workaround revisitieren:** Falls `Geef.Sdk 1.0.0` stable inzwischen released und in `Directory.Packages.props` updatebar ist — kann der direkte Zugriff jetzt funktionieren?

`geef_architecture.md` prüft Konsistenz mit `docs/02-architecture.md` und allen verbindlichen Realfakten aus D-010+D-012.

## Persistenter Abschlussbericht für den Brainstorming-Chat

Bericht nach `docs/reports/step-03-report.md`, gleicher Aufbau wie Schritt-1- und Schritt-2-Bericht. Besonders relevant in diesem Schritt:

1. **Was wurde umgesetzt** — Datei-für-Datei.
2. **Annahmen und Abweichungen** — vor allem zur Anthropic-API-Form (welche genau ist die richtige `anthropic-version`? Welcher Reviewer-Output-Workaround falls Tool-Use mal nicht greift?).
3. **Architect-Output** — welcher Invocation-Level, ob Atelier-Level-4-Fallback, was waren die Architect-Entscheidungen zu DI/Resilience/Schema/Key-Handling?
4. **Pre-Mortem & Devil's Advocate** — speziell zu API-Key-Leaks (kein Key in Logs! kein Key in Test-Output!), Retry-Storms, Token-Cost-Runaway.
5. **Reviewer-Iterationen** — Tabelle.
6. **Akzeptanzkriterien-Check** — Tabelle.
7. **Beobachtungen zur Anthropic-API** — Reviewer-Tool-Use-Verhalten, Latenz, Token-Verbrauch bei trivialen Briefings, Retry-Verhalten in der Praxis. **Kein Key, keine Token, keine kompletten API-Responses im Bericht.**
8. **Empfehlungen für Schritt 4** — was muss beim Persistierungs-Schritt mit Tokens/Iterations/Findings beachtet werden? Welche Event-Daten sind verlässlich?
9. **PreviousFindings-Status** — ob mit `Geef.Sdk 1.0.0` stable jetzt direkt nutzbar oder weiter Workaround.

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- **Keine Geef-API erfinden, keine Anthropic-API erfinden** — beides aus offizieller Quelle lesen.
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- API-Key niemals in source control, niemals in Logs, niemals im Bericht.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.