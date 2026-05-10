# Architektur

*Letzte Aktualisierung: 10. Mai 2026*

## Schichtenbild

```
┌────────────────────────────────────────────────────────────────┐
│                    Frontends (zwei Adapter)                    │
│  ┌──────────────────────────────┐  ┌─────────────────────────┐ │
│  │  Web-UI (Blazor Server)      │  │  MCP-Server             │ │
│  │  - /new, /runs, /runs/{id}   │  │  - submit_request       │ │
│  │  - SignalR Live-Stream       │  │  - get_run_status       │ │
│  │  - Cookie-Auth               │  │  - get_run_result       │ │
│  └──────────────┬───────────────┘  └─────────────┬───────────┘ │
│                 │                                │             │
└─────────────────┼────────────────────────────────┼─────────────┘
                  │                                │
                  ▼                                ▼
┌────────────────────────────────────────────────────────────────┐
│              Application Service Layer  (IRunService)          │
│  - SubmitRunAsync(briefing, sources, options) -> RunId         │
│  - GetRunStatusAsync(runId) -> RunStatus                       │
│  - GetRunResultAsync(runId) -> Result                          │
│  - ListRunsAsync(filter) -> Summaries                          │
│  - CancelRunAsync(runId)                                       │
└──────────────────────────────┬─────────────────────────────────┘
                               │
                               ▼
┌────────────────────────────────────────────────────────────────┐
│                     Background Orchestrator                    │
│  - BackgroundService pollt Pending-Runs                        │
│  - Baut Geef-Pipeline aus Run-Konfiguration                    │
│  - Führt PipelineRunner.RunAsync() aus                         │
│  - Custom IGeefEventSink schreibt Events in DB + SignalR       │
└──────────────────────────────┬─────────────────────────────────┘
                               │
                               ▼
┌────────────────────────────────────────────────────────────────┐
│                       Geef SDK Pipeline                        │
│   Grounding → Execution → Evaluation (Loop) → Finalize         │
│                                                                 │
│   Provider-Implementierungen leben in Infrastructure:           │
│   - BriefingGroundingStep                                       │
│   - LlmExecutionStep      (Multi-Provider-fähig)                │
│   - LlmReviewer           (Multi-Provider-fähig, getaggt)       │
│   - MarkdownFinalizer                                           │
└──────────────────────────────┬─────────────────────────────────┘
                               │
                               ▼
┌────────────────────────────────────────────────────────────────┐
│                          Persistenz                            │
│   Postgres via EF Core                                         │
│   Tabellen: Runs, Iterations, Findings, Events                 │
└────────────────────────────────────────────────────────────────┘
```

## Solution-Struktur

```
Geef.Atelier.sln
├── src/
│   ├── Geef.Atelier.Core/           // Domain-Records, IRunService-Vertrag,
│   │                                // Pipeline-Konfigurations-Records
│   ├── Geef.Atelier.Infrastructure/ // EF Core, LLM-Clients, EventSink,
│   │                                // Provider-Implementierungen, Repositories
│   ├── Geef.Atelier.Web/            // Blazor Server: UI + BackgroundService
│   │                                // + Application-Service-Implementierung
│   └── Geef.Atelier.Mcp/            // MCP-Server (eigener Host, ruft IRunService)
└── tests/
    └── Geef.Atelier.Tests/          // xUnit
```

**Begründung der Aufteilung:**

- **Core** ist LLM-frei und persistenz-frei — enthält nur Records, Interfaces, Domain-Logik. Damit testbar ohne Infrastruktur.
- **Infrastructure** kapselt alle externen Abhängigkeiten (Postgres, LLM-APIs, Geef SDK). Provider-Implementierungen leben hier, weil sie LLM-Clients und Repositories brauchen.
- **Web** hostet die UI, den `BackgroundService` und die `IRunService`-Implementierung. Letztere könnte später in ein eigenes Projekt wandern, ist aber im Skeleton hier am praktischsten.
- **Mcp** ist ein eigenes ASP.NET-Core-Projekt, das den MCP-Server hostet und denselben `IRunService` aufruft wie die UI. Kann separat deployed werden, im Skeleton läuft es als Teil derselben Anwendung.

## Datenmodell (Skeleton-Stand)

Vier Tabellen reichen am Anfang. Spätere Erweiterungen (Sources, AdvisorConsultations, AdvisorProvenance, ReviewerProfiles, CrewTemplates) kommen mit den entsprechenden Features.

### Runs

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | uuid (PK) | |
| CreatedAt | timestamptz | |
| StartedAt | timestamptz | nullable |
| CompletedAt | timestamptz | nullable |
| Status | enum | Pending / Running / Completed / Failed / Aborted |
| BriefingText | text | |
| ConfigJson | jsonb | Modell-Auswahl, Crew, Budget — als Snapshot bei Erstellung |
| FinalText | text | nullable, gesetzt wenn Status=Completed |
| ErrorMessage | text | nullable, gesetzt wenn Status=Failed |
| TokensTotal | int | accumuliert über alle LLM-Calls |
| CostTotal | numeric(10,4) | accumuliert |

### Iterations

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | uuid (PK) | |
| RunId | uuid (FK) | |
| IterationNumber | int | 1-basiert |
| ArtifactText | text | Snapshot des Textes nach dieser Iteration |
| CreatedAt | timestamptz | |

### Findings

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | uuid (PK) | |
| IterationId | uuid (FK) | |
| ReviewerName | varchar(200) | |
| Severity | enum | Critical / Major / Minor / Info |
| Message | text | |
| CreatedAt | timestamptz | |

### Events

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | bigint (PK, identity) | |
| RunId | uuid (FK) | |
| EventType | varchar(100) | aus Geef-EventSink-Vokabular |
| PayloadJson | jsonb | |
| CreatedAt | timestamptz | |

**Indices:**
- `Runs.Status` (für Background-Polling)
- `Events.RunId` (für Detail-View)
- `Iterations.RunId` (für Detail-View)

## Mapping auf GEEF-Provider (Skeleton)

| GEEF-Phase | Provider-Implementierung | Skeleton-Verhalten |
|---|---|---|
| Grounding | `BriefingGroundingStep` | Schreibt das Briefing in den Context, keine externen Quellen |
| Execution | `LlmExecutionStep` | Anthropic-Call mit Briefing + PreviousFindings, gibt neuen Text zurück |
| Evaluation | `LlmReviewer` × 2 | Zwei Reviewer mit OpenAI-Modell, parallel ausgeführt |
| Finalize | `MarkdownFinalizer` | Wrappt finalen Text in `FinalizedDocument`-Record |

**Convergence-Policy im Skeleton:** `MaxIterationsPolicy(3)` — drei Iterationen Maximum, danach abbrechen. Mehr ist im Skeleton Overkill.

**Evaluation-Strategy im Skeleton:** `ParallelEvaluationStrategy` — beide Reviewer laufen gleichzeitig.

## Multi-Provider-LLM-Abstraktion (geplant, nicht im Skeleton)

Für späteren Multi-Provider-Support (Anthropic, OpenAI, OpenRouter) ist ein dünner Adapter geplant:

```csharp
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}

public sealed record LlmRequest(
    string Provider,        // "anthropic" | "openai" | "openrouter"
    string Model,
    string SystemPrompt,
    string UserPrompt,
    JsonSchema? ResponseSchema,
    LlmOptions Options);
```

Im Skeleton existiert nur `AnthropicLlmClient`. OpenAI und OpenRouter werden in einem späteren Schritt ergänzt. Die Reviewer im Skeleton dürfen ausnahmsweise auch Anthropic nutzen, *müssen aber ein anderes Modell* als der Executor verwenden, damit die "fremder Blick"-Eigenschaft erhalten bleibt.

## Frontend-Stack-Entscheidung

**Blazor Server.** Begründung: derselbe .NET-Stack wie Geef SDK, kein Kontextwechsel; SignalR ist eingebaut und beliefert den Live-Status quasi gratis; Single-User heißt keine Skalierungs-Sorgen; lokale UI-Latenz ist dank Server-Hosting und Reverse-Proxy unkritisch. Falls später ein Wechsel zu Blazor WebAssembly oder React+API nötig wird, bleibt das Backend (`IRunService`, MCP-Server, Pipeline) unverändert.

## Auth-Strategie

- **Web-UI:** Cookie-Auth mit einem festen User aus Environment-Variablen (`ATELIER_USER`, `ATELIER_PASSWORD_HASH`). Hash via ASP.NET Core Identity-Hasher oder bcrypt.
- **MCP-Server:** Bearer-Token in Header. Im Skeleton ein einzelnes Long-Lived-Token aus Environment-Variable (`ATELIER_MCP_TOKEN`). OAuth-2.0-Flow ist im MCP-Standard vorgesehen, kommt aber nach Skeleton.

## Observability

Geef bringt vieles eingebaut mit:
- `IGeefEventSink` für strukturierte Events → Custom-Sink schreibt in DB und SignalR
- `System.Diagnostics.ActivitySource("Geef.Sdk")` für Distributed Tracing → kann später an OpenTelemetry-Collector angeschlossen werden
- Middleware-Pipeline für Cross-Cutting (Timeout, ExceptionHandling, Tracing) — alle eingebauten Middleware werden im Skeleton genutzt

Logging via `Microsoft.Extensions.Logging` mit Console-Sink im Skeleton; strukturierte Logs (Serilog mit Postgres-Sink) sind eine spätere Option.