# Architecture

*[Deutsch](02-architecture_de.md) · **English***

*Last updated: 2026-11-27 (second addendum to D-087: "Extra careful" removed, one setup; D-091: stage 8d, document brought to the current state: only the direct path runs, the removed parts are gone from the description; before that 2026-11-26: pointer to the switches of the order features and the page `/how-it-works`; 2026-11-24, D-067: run warnings, `Runs.Outcome`, fail-loud start and snapshot handling, preparation timeout, in-process live updates, end-to-end tests; 2026-11-22, D-066: model matrix; 2026-08-04, D-064: model catalog for CLI providers)*

Atelier turns a person's request and files into a finished text. The unit the person sees is the **order**; **runs** are the
technical record underneath (one run per version of an order). Since stage 8d (D-091) there is **one way of working, the direct
path**. The earlier ways (the crew review loop, composed teams, the learning loop, the
agentic tool path, the vector knowledge base, advisors, specialization packs, finishing steps and the workshop editors) have been
removed from the code; the git history and the [decisions log](05-decisions-log.md) keep their descriptions.

## Layer diagram

```
┌──────────────────────────────────────────────────────────────────┐
│                     Frontends (two adapters)                     │
│  ┌──────────────────────────────┐  ┌───────────────────────────┐ │
│  │  Web UI (Blazor Server)      │  │  MCP server (/mcp)        │ │
│  │  /start, /work, /orders/{id} │  │  submit_order,            │ │
│  │  /runs, /runs/{id}           │  │  get_order_status,        │ │
│  │  cookie auth                 │  │  get_order_result, ...    │ │
│  └──────────────┬───────────────┘  └─────────────┬─────────────┘ │
└─────────────────┼────────────────────────────────┼───────────────┘
                  ▼                                ▼
┌──────────────────────────────────────────────────────────────────┐
│  Application layer: IOrderService, IRunService, intake, sources, │
│  profile ("About me"), providers, auth and OAuth                 │
└────────────────────────────────┬─────────────────────────────────┘
                                 ▼
┌──────────────────────────────────────────────────────────────────┐
│  Background services: intake worker, run orchestrator,           │
│  source cleanup, OAuth cleanup, model catalog warm-up            │
└────────────────────────────────┬─────────────────────────────────┘
                                 ▼
┌──────────────────────────────────────────────────────────────────┐
│  Geef SDK pipeline of the direct path                            │
│  OrderGroundingStep → DirectWriterExecutor → checks (at most     │
│  two passes) → MarkdownFinalizer → report                        │
└────────────────────────────────┬─────────────────────────────────┘
                                 ▼
┌──────────────────────────────────────────────────────────────────┐
│  Persistence: Postgres via EF Core; uploaded files on a volume   │
└──────────────────────────────────────────────────────────────────┘
```

## Solution structure

```
Geef.Atelier.slnx
├── src/
│   ├── Geef.Atelier.Core/           // domain records, rules without LLM or database, configuration
│   │                                // classes, repository contracts
│   ├── Geef.Atelier.Application/    // services: orders, runs, intake, sources, profile, providers,
│   │                                // model assignment, auth, OAuth
│   ├── Geef.Atelier.Infrastructure/ // EF Core, LLM clients, model catalog, direct-path pipeline,
│   │                                // intake worker, research (web search), source extraction,
│   │                                // order export, event sink
│   ├── Geef.Atelier.Web/            // Blazor Server UI, HTTP endpoints, orchestrator, DI composition
│   └── Geef.Atelier.Mcp/            // class library: MCP tools, server instructions, skill;
│                                    // hosted in the Web project
├── tests/
│   └── Geef.Atelier.Tests/          // xUnit; EndToEnd/ = end-to-end tests
└── tools/                           // Eval (Python), IntakeEval, FactCheckEval, ResearchEval,
                                     // HashPassword
```

- **Core** needs no database and no LLM, so its rules are testable on their own.
- **Application** holds the use cases and depends only on Core.
- **Infrastructure** encapsulates the external dependencies (Postgres, LLM APIs, web search, Geef SDK).
- **Web** is the only host. **Mcp** is a class library without a host of its own: the endpoint `/mcp` lives in the Web
  project, shares its DI container and calls the same services, without an HTTP hop in between.

## Orders and sources (D-069, D-070)

| Table | Content |
|---|---|
| `Orders` | The person's request word for word, mode (`Direct`/`Careful`), entry (`Web`/`Mcp`/`Legacy`), state (`Analyzing`, `NeedsInput`, `Ready`, `Cancelled`), owner, the record of the questions and answers, the research result |
| `Sources` | One uploaded file: extracted text with page markers (`[[page N]]`), counts, quality, warnings, path of the kept original, expiry while no order uses it |
| `OrderSources` | Which source belongs to which order, with role, position and page selection (`1-40,55`) |
| `RunSourceUsages` | Which file went into which run, in which mode (`FullText`, `FullTextWithMap`) and with how many characters |

`Runs.OrderId`/`Runs.OrderVersion` link a run to its order. `OrderService` (Application) validates and creates an
order; `OrderStateResolver` (Core) derives the display state from `Orders.State` and the current run only. Size limits
come from `SourceBudgetPlanner.Classify`, which the start page, the order service and the source block of the writer
(`SourceBlockRenderer`) share. Text is extracted from uploads in a separate, memory-limited process
(`Sources:ExtractionMode = Isolated`).

**From request to run.** An order starts either directly with its first run, or, when questions are enabled for it
(`Orders:IntakeMode = On`), in state `Analyzing`: `OrderIntakeWorker` lets the clarifier (`LlmClarifier`) read the request,
`QuestionGate` keeps at most three short questions, and the order waits in `NeedsInput` until the person answers or skips
(or the wait of `Intake:NeedsInputAutoStartAfter` ends). The questions come only before writing, never during it.

**Changing a finished text** ("Change something", `revise_order`) adds a run as the next version. It is seeded with the text
of the shown version (`Runs.SeedDraftText`, `Runs.ParentRunId`) and carries the person's wish in `Runs.RevisionHint`.

## The direct path pipeline

One setup exists, only in code (`SystemCrew`): `direct` — writer, automatic checks without a model (`DeterministicChecksReviewer`)
and fact check (`SourceFactCheckReviewer`); at most two passes, that is at most one correction. Every new run of every order uses
it, whatever the stored mode of the order. Orders stored with the mode `Careful` and runs with the removed template
`direct-outline` still belong to the direct path (second addendum to D-087): they are read, continued and changed there; if a
stored `direct-outline` run is executed, its extra check `requirements-check` is left out.

`IPipelineAssembler` has a single production assembler, `DirectPipelineAssembler`; `PipelineAssemblerSelector` refuses any
run no assembler accepts (see [Old data](#old-data-runs-of-removed-ways-of-working)). `AtelierPipelineFactory` builds the
Geef pipeline from the run's snapshot:

1. **`OrderGroundingStep`** loads the order and its files, renders the files into the background document, reads the rules
   the request states, records the `RunSourceUsages`, prepares the material of the fact check and fixes the fact-check mode
   of the run (`Runs.FactCheckMode`). For the first version of an order of mode `Direct` without files it asks `OrderResearchService` for
   passages from the web (Tavily; switch `Research:Mode`). This order grounding step plus the web research is the only
   grounding there is.
2. **`DirectWriterExecutor`** writes the text. With a CLI provider it writes into a document (`draft.md`) and asks the
   gateway not to switch to another CLI. In a correction pass it gets only the blocking findings of the pass before. Notes
   of the writer (assumptions, open points) are split off the text. An empty or unchanged answer is never accepted.
3. **Checks** run after each pass. Which findings may start the correction depends on the check and on
   `Direct:FactCheckMode` (`Off`, `Shadow`, `Report`, `On`; D-073).
4. **`MarkdownFinalizer`** returns the text. When the checks do not pass within the allowed passes, the best-effort text
   of the SDK is delivered with its open points.
5. **Report:** `DirectReportBuilder` builds the `RunReport` (constraints, check results of the delivered text,
   placeholders, files used, read evidence, notes), stored in `Runs.ReportJson` before the outcome is recorded. The
   "What was checked" sentences of the web page and of `get_order_result` come from it.

**Convergence policy:** `DefaultConvergencePolicy` built from the `Convergence` section (`ConvergenceOptions`) and
overridden by the setup (both setups: two passes, no abort on a critical finding, no regression detection). A reviewer that
fails for technical reasons does not block; the run records a warning.

## Run orchestrator

`RunOrchestratorService` (hosted in Web) polls the database every `Orchestrator:PollingInterval` (2 s), runs at most
`Orchestrator:MaxConcurrentRuns` (5) runs at once and takes only `Pending` runs **with** a snapshot, claiming each with one
atomic update to `Running`. It assumes **a single instance**: the crash recovery at start resets every `Running` run.

For each run it reads the snapshot strictly, selects the assembler, runs the pipeline with `PostgresEventSink` (events,
iterations, findings, status and text; live updates through `IRunNotifier`), stores the costs, lets the assembler write the
report and then records the outcome. A watcher polls `Runs.CancellationRequested`; a cancellation or a service stop ends the
run as `Aborted`. An unexpected error ends it as `Failed` with one of a few fixed plain sentences; the details go to the log
only.

**Crash recovery at start:** every run still `Running` becomes `Failed` ("Service restarted"); `Pending` runs without a
snapshot that are older than this instance become `Failed`; runs with a terminal status but without `Outcome` (stopped while
their result was being stored) get an outcome and a warning (see below).

### Old data: runs of removed ways of working

The database keeps every table and column (no `DROP` migration), so the previous image can still read it. For that reason new
snapshots still carry an (empty) `advisors` list.

- Finished runs and orders of the removed ways stay readable: lists, "My work", order page, result, export and the technical
  run page, which shows the setup name and says that the setup belongs to a way of working that no longer exists.
- A pending run of a removed setup (old review loop, composed team, learning run) fails with "This task was set up for a way
  of working that no longer exists, so it cannot run. Please start it again."
- "Resume" is refused for such a run ("This task was set up for a way of working that no longer exists, so it cannot be
  continued. Please start it again.").
- A snapshot that names a check this version no longer has fails the run with a plain sentence as well.
- A waiting order stored with the mode `Careful` is not an old way in this sense: it runs on the direct path.
- Crash recovery and cleanup treat old runs like any other. `RunKind` keeps the values `Learning` and `CrewComposition` so
  that old rows can be read.

## Setups, snapshots and models

Each run stores a `CrewSnapshot` (JSON in `Runs.CrewSnapshot`): schema version, setup name, the full writer profile, the
check profiles, the evaluation strategy and the convergence override. It is built by `ICrewService` from the code-only
profiles: the model matrix (`ModelMatrix`, `IModelAssignmentService`, D-066) assigns a model to every role with a slot, and
model aliases are resolved to concrete ids. There are no user-editable profiles or templates any more. The request check has
no slot and keeps a fixed binding, on another model than the writer.

## Data model

Migrations are hand-written (`InitialCreate` and the `StepNN` series). The EF Core model maps these tables:

| Group | Tables |
|---|---|
| Orders and files | `Orders`, `Sources`, `OrderSources`, `RunSourceUsages`, `UploadTickets`, `SourceRefusals` |
| Runs | `Runs`, `Iterations`, `Findings`, `Events`, `RunWarnings`, `IterationActorCosts` |
| Intake and profile | `OrderIntakeShadows`, `UserProfiles` |
| Accounts and settings | `Users`, `Providers`, `SiteSettings` |
| OAuth 2.1 | `OAuthClients`, `OAuthAuthorizationCodes`, `OAuthAccessTokens`, `OAuthRefreshTokens`, `OAuthAuditLog` |

**Tables of removed parts.** They are no longer mapped but **stay in the database** until a separate cleanup step, which is
not part of stage 8d and may drop them: `AdvisorConsultations`, `AdvisorProfiles`, `CrewTemplateEmbeddings`,
`CrewTemplates`, `ExecutorProfiles`, `FinalizationActorCosts`, `FinalizerProfiles`, `GroundingActorCosts`,
`GroundingConsultations`, `GroundingProviderProfiles`, `KnowledgeDocumentChunks`, `KnowledgeDocuments`, `LearningEntries`,
`ReviewerProfiles`, `RunArtifacts`, `StudioSettings`, `TemplateStudioAnalyses`, `mcp_server_configs`,
`specialization_packs`, `tool_definitions`, `tool_invocations`. The same step may drop the unmapped columns
`Runs.GroundingCostEur`, `Runs.FinalizerCostEur`, `Runs.FinalizerErrorMessage` and `Iterations.AdvisorsTotalCostEur`
(and later possibly `Orders.CrewTemplateName` and `Runs.ParentCompositionRunId`), old composition and learning runs, and
the former exports volume. `Runs.AdvisorRetryAttempted` is not mapped either. Because some of these tables hold vector
columns, the compose files still use a Postgres image with `pgvector`.

### Runs

| Column | Type | Note |
|---|---|---|
| Id | uuid (PK) | |
| CreatedAt / StartedAt / CompletedAt | timestamptz | the last two nullable |
| Status | varchar(50) | Pending / Running / Completed / Failed / Aborted |
| BriefingText | text | the briefing the writer gets |
| ConfigJson | jsonb | configuration stored at creation time |
| FinalText | text | nullable; the delivered text |
| WordCount | int | nullable; words of `FinalText` |
| ErrorMessage | text | nullable; a fixed plain sentence for the user |
| TokensTotal | int | accumulated over the LLM calls |
| CostTotal, TotalCostEur, LlmCostEur | numeric | the last two nullable; cost tracking |
| CachedInputTokens, ReasoningTokens | int | nullable; subsets of the tokens |
| CancellationRequested | bool | set when the user wants to cancel |
| CreatedByUser | text | nullable; owner (run isolation, D-042) |
| CrewTemplateName | varchar(200) | nullable; setup name (`direct`, or a removed one on old runs, for example `direct-outline`) |
| CrewSnapshot | jsonb | nullable; the snapshot of the setup |
| Kind | int | `0 = Standard`; `1 = Learning` and `2 = CrewComposition` exist only on old rows |
| ParentRunId | uuid | nullable; the run this one resumes or changes |
| SeedDraftText | text | nullable; text the first pass starts from |
| RevisionHint | text | nullable; the change the person asked for |
| ParentCompositionRunId | uuid | nullable; only on old rows |
| Outcome | int | nullable; `0 = Converged`, `1 = DeliveredWithOpenPoints`, `2 = Failed`, `3 = Aborted`. See [Run outcome](#run-outcome-and-runstatus) |
| DeliveredIteration | int | nullable; the pass whose text was delivered |
| ReportJson | text | nullable; the `RunReport` of the direct path |
| OrderId | uuid (FK→Orders) | nullable; `ON DELETE CASCADE`. Index `IX_Runs_OrderId_OrderVersion` |
| OrderVersion | int | nullable; unique per order for standard runs (`UX_Runs_OrderId_OrderVersion_Standard`) |
| FactCheckMode | varchar(20) | nullable; the fact-check mode of the run |

### RunWarnings

Problems a run worked around instead of stopping (D-067). Deleted together with the run.

| Column | Type | Note |
|---|---|---|
| Id | uuid (PK) | |
| RunId | uuid (FK→Runs) | indexed |
| Iteration | int | nullable; pass in which the problem occurred |
| Source | varchar(100) | `grounding`, `review`, `model-assignment`, `llm`, `run`, `intake` or `fact-check` (`RunWarningSources`) |
| Code | varchar(100) | stable machine-readable code, e.g. `grounding.research_failed`, `grounding.source_warning`, `review.reviewer_failed`, `llm.failover`, `writer.partial_read`, `run.result_not_saved`, `run.restarted_while_finishing` (`RunWarningCodes`, `DirectWarningCodes`). Old rows may carry codes of removed parts; they keep their text and are still shown |
| Message | text | short plain-English explanation for the user |
| CreatedAt | timestamptz | |

### Iterations, Findings, Events

- `Iterations`: `RunId`, `IterationNumber`, `ArtifactText` (the text after the pass), cost and token columns of writer and
  checks.
- `Findings`: `IterationId`, `ReviewerName`, `Severity`, `Message`, optional `Category`, `Code` and `Quote`.
- `Events`: `RunId`, `EventType` (from the Geef event-sink vocabulary), `PayloadJson`.

## LLM provider layer

Providers are rows of the `Providers` table (system providers such as `openrouter`, `claude-cli` and `codex-cli` plus CLI
providers that an administrator adds); `Llm:ProvidersFallback` in the configuration is used for a name the provider service
does not know. `ILlmClientResolver` returns an `ILlmClient`: `OpenAiResponsesClient` for endpoints on `api.openai.com`,
otherwise `OpenAiCompatibleClient`. The `cli-proxy` side container (FastAPI, Python) offers the subscription CLIs behind
OpenAI-compatible endpoints, one per CLI (for example `http://cli-proxy:8090/v1/claude`). A call that must not move to
another CLI asks the gateway for `x-cli-proxy-failover: same-model` (D-090).

Checks deliberately run on other models than the writer. Token usage goes through the pipeline context into
`Runs.TokensTotal`; with `CostTracking:Enabled` the costs per actor and pass are stored in `IterationActorCosts` and summed
on the run. API keys come from environment variables; names that editable configuration may reference are limited by an
allow list (`EnvReference:Allowed` on top of a built-in list and the prefix `ATELIER_SECRET_*`).

## Run safeguards (D-067)

### Run outcome and `RunStatus`

`Runs.Status` keeps its meaning. A run that stops without all checks passing ends as `Failed` even when it delivers a text.
`Runs.Outcome` is the additional signal for what the run delivered:

| `Outcome` | Meaning |
|---|---|
| `Converged` | The checks passed; text and report are stored. |
| `DeliveredWithOpenPoints` | The run stopped with open points and delivered its best available draft. |
| `Failed` | The run ended without a usable text, or its result could not be stored completely (warning `run.result_not_saved`). A text may still be present; it is not guaranteed to be complete. |
| `Aborted` | Cancelled by the user or stopped by the service. A text stored before the stop stays readable. |

`Outcome` means **finished and stored**: it is set after the final text and the report are in the database. The first value
wins. `Status` and `Outcome` can briefly disagree, because the event sink sets the status first; clients therefore wait
until the order is done and an outcome is set (as `scripts/atelier-smoke.sh` does). If the service is stopped hard in
between, the crash recovery sets the outcome: with a text `Converged` (status `Completed`) or `DeliveredWithOpenPoints`,
without one `Failed`, plus the warning `run.restarted_while_finishing`. Runs from before the column existed keep `NULL`;
only runs that completed after the earliest run with an outcome are repaired.

### Warnings at the run

A run records every problem it works around in `RunWarnings`: for example a web research that failed, a warning from reading
a file, a check that could not be completed in a pass, a model that could not be assigned as planned or a failover in the
LLM client. `IRunWarningSink` writes each warning in its own short-lived database scope; a failed write is logged and never
stops the run. It is a required dependency, so a missing registration fails at start. The LLM client reports failovers
through the ambient `RunWarningScope`, which the orchestrator opens per run.

Warnings appear on the technical run page and in the `warnings` field of `get_order_status` and `get_order_result`.
Warning and error texts stored at a run are fixed, short, plain sentences; technical details go only to the log. Before
anything technical is logged, `SecretRedactionExtensions` (Core) replaces credential-like fragments with `[redacted]`.

### Fail loud instead of silent

| Situation | Behaviour |
|---|---|
| A database migration fails at start-up | The start aborts with a critical log entry (`MigrateDatabaseOrFailAsync`). The health check `pending-migrations` reports pending migrations. |
| A run's snapshot is missing, unreadable, empty, of an unknown version, or lacks writer, checks or the advisor list | `CrewSnapshot.DeserializeForRun` throws `InvalidCrewSnapshotException`; the run ends `Failed` with a clear message. There is no fallback setup; the tolerant `Deserialize` is for display only. |
| A snapshot has no checks, or names an unknown check | `AtelierPipelineFactory` refuses to build the pipeline; the run ends `Failed` with a message. |
| A run belongs to a removed way of working | No assembler accepts it; it fails with the sentence quoted under [Old data](#old-data-runs-of-removed-ways-of-working). |
| A run without a usable snapshot is resumed | `RunService.ResumeRunAsync` refuses with `RunNotResumableException` instead of creating a run that can never start. |
| The search key is missing | A warning with the key name at start (`ResearchKeyGuard`); values are never logged. |

### Preparation timeout for runs with attachments

Run attachments were removed in stage 8d (D-091); the files of an order are read before its run is created. The safeguard
itself remains: the orchestrator picks up only `Pending` runs **with** a snapshot, and a `Pending` run that still has none
after `Orchestrator:PreparationTimeout` (default 15 minutes) is set to `Failed` with a message asking the user to submit it
again; a restart cleans up the same way.

## MCP server

The MCP server (`Geef.Atelier.Mcp`) is served at `/mcp` over stateless Streamable HTTP with bearer authentication. It offers
ten tools: `submit_order`, `answer_order`, `get_order_status`, `get_order_result`, `list_orders`, `cancel_order`,
`revise_order`, `export_order`, `prepare_attachment` (only while `Uploads:Tickets` is `On`) and `profile`. The five names
`submit_request`, `submit_auto_crew_request`, `get_run_status`, `get_run_result` and `get_run_details` stay registered for
one version as stubs whose every call fails with an error that names the successor. The tools call `IOrderService`, the same
service as the web pages. `McpToolCatalog` registers the tools one by one, and `McpToolBindingCheck` stops the start when a
tool parameter that is a service has no registration. Details: [MCP integration](04-mcp-integration.md) and
[endpoint reference](09-endpoint-reference.md).

## Configuration

Settings come from `appsettings.json` and environment variables (`__` instead of `:`) and are read at start.

| Section | Content |
|---|---|
| `ConnectionStrings` | Postgres connection (`DefaultConnection`) |
| `Llm`, `CliProxy`, `ModelCatalog` | fallback providers, access to the CLI gateway, model lists |
| `Orchestrator` | polling, parallel runs, cancellation polling, preparation timeout |
| `Convergence` | defaults of the convergence policy |
| `Sources`, `Uploads` | limits and retention of files; upload tickets |
| `Orders` | request limits and waits, questions (`IntakeMode`), changing a text (`Revise`), `TellOnce` |
| `Intake` | the clarifier and its questions |
| `Direct` | writer notes, arithmetic of totals in files, fact check |
| `Research`, `Tavily` | web research and its limits; `Tavily:Endpoint` and `Tavily:ApiKey` |
| `Mcp` | `Skills` and `Elicitation` (D-088) |
| `Ui` | `WorkshopForAdminsOnly` |
| `AtelierUser`, `AtelierMcp`, `OAuth` | first administrator, static MCP token and its user, OAuth server |
| `Pricing`, `CostTracking` | price table and cost tracking |
| `EnvReference` | additional allowed environment-variable names |
| `Logging`, `AllowedHosts` | ASP.NET Core defaults |

The switches of the order features, their shipped values and their exceptions per person are listed in the
[admin guide](14-admin-guide.md#switching-features). The public page `/how-it-works` reads the same switches.

`Ui:WorkshopForAdminsOnly` (default `true`) hides the workshop menu from non-administrators; the menu holds only "Runs" and,
for administrators, "Providers". It changes what is shown, not access rights.

## Testing and measuring

### End-to-end tests (`tests/Geef.Atelier.Tests/EndToEnd/`)

`AtelierEndToEndHost` starts the **real** application through `WebApplicationFactory<Program>` against its own database per
test on the Testcontainers Postgres of `PostgresFixture`. Migration, DI wiring, intake, orchestrator, pipeline, event sink
and the MCP endpoint are the production code. Only external HTTP is replaced: `ScriptedLlmEndpoint` stands in for the
cli-proxy and answers per role, `LlmTrafficRecorder` records the request bodies, `FakeTavilyEndpoint` and
`FakeCliProxyModelsEndpoint` serve web search and model list, and every other named HTTP client is blocked.

```bash
export MSBUILDNODEREUSE=0 DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet test tests/Geef.Atelier.Tests/Geef.Atelier.Tests.csproj --filter "FullyQualifiedName~Geef.Atelier.Tests.EndToEnd"
```

The tests need Docker and run in their own collection without parallelism. A new path through the system should get its own
end-to-end test, from the entry point to the stored result.

### Test against exported snapshots

`ProductionSnapshotFixtureTests` reads exported snapshots through the strict run-time read and checks that each one either
runs on the direct path or is refused with the plain sentence above. The exports contain real orders and are **never** part
of the repository; without the variables the test is skipped.

| Variable | Meaning |
|---|---|
| `ATELIER_SNAPSHOT_FIXTURES` | Directory with one exported snapshot per run (`*.json`). |
| `ATELIER_SNAPSHOT_FIXTURES_MIN` | Minimum number of files the export must contain (default 1), so that an incomplete export fails. |

### Measuring tools and operations scripts

`tools/Eval` compares the Atelier with other ways of producing a text by blind pairwise comparison
([`tools/Eval/README.md`](../tools/Eval/README.md)); real tasks stay outside Git. `IntakeEval`, `FactCheckEval` and
`ResearchEval` measure the clarifier, the fact check and the research.

`scripts/atelier-smoke.sh` (a mini order through MCP after each deploy: `submit_order`, one skip of questions if asked,
`get_order_status` until done, `get_order_result`) and `scripts/atelier-watch.sh` (alarm for stuck runs and an unhealthy
cli-proxy) are described in [`ops/atelier-smoke.md`](ops/atelier-smoke.md) and [`ops/atelier-watch.md`](ops/atelier-watch.md).

## UI architecture

**Blazor Server.** The same .NET stack as the Geef SDK and live status without a separate client application.

### Main pages

| Route | Function |
|---|---|
| `/start` | New order: request, files with receipts |
| `/work`, `/orders` | "My work": the person's orders |
| `/orders/{id}` | Order page: questions, progress, text, open points, versions, changes, export |
| `/about-me` | "About me" (the profile used in texts) |
| `/runs`, `/runs/{id}` | Technical run list and run page |
| `/crew/providers…` | Provider management (administrators) |
| `/admin/users`, `/admin/oauth-clients`, `/admin/site-settings` | Administration |

The full list, including public pages, is in the [endpoint reference](09-endpoint-reference.md#web-ui--account-endpoints).

### Live updates

Pages subscribe to the in-process `IRunUpdateFeed` (`Web/Notifications/`, `InProcessRunUpdateFeed`, registered by
`AddRunLiveUpdates()`); there is no SignalR hub of the application's own.

- The topic is derived on the server from the signed-in user (`RunUpdateTopics`). A run or order topic is granted only to the
  owner or an administrator; list topics carry the user's own entries, or all entries for administrators.
- Every subscriber has its own queue; handlers run one after another, identical pending updates are merged, and publishing
  never waits for subscribers.
- Access is decided when subscribing; a subscription lasts as long as the page's circuit.

`LiveRunNotifier` implements the Core contracts `IRunNotifier` and `IOrderNotifier` and publishes into the feed; a run of an
order also updates the order topics. Notifications are best-effort.

**`CircuitDataGate`** (scoped per circuit): all components of a circuit share one `DbContext`, so every database access of a
page that receives live updates runs through `DataGate.RunAsync(...)`. Calls must not be nested.

## Auth strategy

### Web UI — cookie auth

> **Multi-user since Step20 (D-041 timeframe):** originally single-user from
> environment variables; now **DB-based multi-user management**
> (table `Users`, BCrypt). The admin account is seeded/synchronized at startup
> from `ATELIER_USER`/`ATELIER_PASSWORD_HASH`; further accounts are managed by
> the admin at `/admin/users` (`IUserAdminService`). `IUserAuthenticator` has
> since returned an `AtelierUser?` (instead of just `bool`). The cookie
> configuration below applies unchanged.

The BCrypt hash (work factor 11) is generated via `tools/HashPassword/`.

**Cookie configuration:**

| Option | Value |
|---|---|
| Cookie name | `Atelier.Auth` |
| `HttpOnly` | `true` |
| `SameSite` | `Strict` (production) / `Lax` (test env) |
| `SecurePolicy` | `SameAsRequest` (dev) / `Always` (prod) |
| `ExpireTimeSpan` | 30 days |
| `SlidingExpiration` | `true` |
| `LoginPath` | `/login` |

**Login flow (static SSR):**

```
Anonymous browser → /runs → [Authorize] → RedirectToLogin
  → NavigationManager.NavigateTo("/login?ReturnUrl=%2Fruns")
  → Login.razor (static SSR, no @rendermode)
  → POST /login (Blazor static SSR form handler, @formname="login-form")
  → IUserAuthenticator.ValidateCredentialsAsync (BCrypt.Verify)
  → HttpContext.SignInAsync → cookie set → redirect to /runs
```

**Important: the login page must stay static SSR.** `@rendermode InteractiveServer` would handle the form POST in a WebSocket context without an `HttpContext` → `SignInAsync` would not be callable. The `@formname="login-form"` attribute on the `<form>` element is mandatory for Blazor static SSR form routing.

**Logout:** `POST /auth/logout` (Minimal API) with `<AntiforgeryToken />` in the `UserMenu` component. A GET logout would be a CSRF attack vector.

**`IUserAuthenticator` layer:**

```
Geef.Atelier.Core/Configuration/AtelierUserOptions.cs   → POCO for username/password hash
Geef.Atelier.Application/Auth/IUserAuthenticator.cs     → interface (Application, not Infrastructure)
Geef.Atelier.Application/Auth/AtelierUserAuthenticator.cs → BCrypt.Verify + CryptographicOperations.FixedTimeEquals
Geef.Atelier.Application/Auth/ApplicationAuthExtensions.cs → AddAtelierAuth(IServiceCollection, IConfiguration)
```

`AtelierUserAuthenticator` is `internal sealed`. The env-var fallback (`ATELIER_USER`/`ATELIER_PASSWORD_HASH`) is resolved in `ApplicationAuthExtensions` — docker-compose users do not need to know the ASP.NET Core double-underscore convention.

**Timing protection:** `FixedTimeEquals` for the username comparison, `BCrypt.Verify` called even for a wrong username (constant-timing property). No username/password hash is written to logs — only `"Login attempt rejected"` (without PII).

**Lazy-fail on missing configuration:** the service starts even without env vars, login returns `false`. The health check stays anonymous (`.AllowAnonymous()` on `MapHealthChecks`). An init-warning log on the first misconfigured login attempt.

**`ForwardedHeaders` before `UseAuthentication`:**

```csharp
app.UseForwardedHeaders();   // FIRST — so that Request.IsHttps is correct
app.UseAuthentication();
app.UseAuthorization();
```

Traefik terminates TLS and forwards HTTP. Without `UseForwardedHeaders`, `SecurePolicy.Always` would block cookies in production. `KnownIPNetworks.Clear()` opens it for all proxy IPs (Docker-network invariant).

### Test auth bypass

`TestAuthenticationHandler` (in `tests/Geef.Atelier.Tests/Web/E2E/`, `internal sealed`) marks every request as pre-authenticated with `ClaimTypes.Name = "test-user"`. `WebTestHost.StartAsync(authenticated: true/false)` — `true` activates the test handler, `false` starts real cookie auth with a BCrypt-wf=4 hash for LoginFlow/LogoutFlow tests. **The handler must never be referenced in `Program.cs` or the Web project.**

### MCP server — bearer token / multi-auth (implemented in step 9, see D-022)

**Multi-auth scheme setup:** the application uses two parallel authentication schemes.

| Scheme | Name | Purpose |
|---|---|---|
| Cookie | `CookieAuthenticationDefaults.AuthenticationScheme` | Web UI, default scheme |
| Bearer | `"Bearer"` | MCP endpoint `/mcp`, explicitly via `McpPolicy` |

**Default scheme:** cookie (all Blazor routes, `[Authorize]` without an argument).

**MCP endpoint:** explicitly protected with `RequireAuthorization("McpPolicy")`. The `McpPolicy` sets the authentication scheme to `"Bearer"` so the MCP path never attempts cookie auth.

**`ITokenValidator` / `BearerTokenHandler` (state after D-041 OAuth 2.1):**

`ITokenValidator.ValidateTokenAsync` has returned, since D-041, a rich result
`TokenValidationOutcome { IsValid, Kind, Subject, ClientId, Scope }` (no longer just `bool`).

```
Geef.Atelier.Application/Auth/ITokenValidator.cs           → interface (application layer)
Geef.Atelier.Application/Auth/StaticTokenValidator.cs      → static ATELIER_MCP_TOKEN
                                                              (FixedTimeEquals); Kind="static-bearer"
Geef.Atelier.Application/Auth/OAuthAccessTokenValidator.cs → OAuth access token via DB lookup
                                                              (SHA-256 hash); Subject = OAuth user
Geef.Atelier.Application/Auth/CompositeTokenValidator.cs   → registered as ITokenValidator:
                                                              checks static, then OAuth
Geef.Atelier.Web/Auth/BearerTokenHandler.cs                → AuthenticationHandler; builds claims
                                                              from the outcome (Name/NameIdentifier/Role)
```

`BearerTokenHandler` maps the outcome onto claims: `ClaimTypes.Name` ← `Subject`,
`ClaimTypes.NameIdentifier` ← `ClientId ?? Subject`, and for the static bearer token
`ClaimTypes.Role = "admin"`. Run-user isolation (D-042) therefore also applies over MCP:
OAuth orders belong to the authorizing user, static-token orders to `AtelierMcp:StaticTokenUser` or the configured administrator.
`ICurrentUserService`/`HttpContextCurrentUserService` expose `Username`/`IsAdmin`
for the service and MCP layers.

**OAuth 2.1 has been fully implemented since D-041** (no more "after the skeleton"):
a self-hosted authorization server with mandatory PKCE/S256, opaque tokens (only SHA-256 in the DB),
refresh rotation + reuse detection. Endpoint and flow details see
[`04-mcp-integration.md`](04-mcp-integration.md) and [`09-endpoint-reference.md`](09-endpoint-reference.md);
rationale in the [decisions log](05-decisions-log.md) D-041. Both auth paths
(static bearer token for Claude Code CLI, OAuth 2.1 for Claude Desktop/Claude.ai)
coexist without a configuration change.

## Production deployment

### Traefik flow

```
Browser → HTTPS:443 → Traefik (TLS via Let's Encrypt, cert-resolver 'le') → HTTP:8080 → geef-atelier-web container → ASP.NET
```

Traefik terminates TLS and forwards HTTP (port 8080) to the container, which exposes no port to the outside.

| Parameter | Value |
|---|---|
| External network | `proxy` |
| Cert resolver | `le` (HTTP challenge via the `web` entry point) |
| Entry point (HTTPS) | `websecure` |
| HTTP→HTTPS redirect | Global in `traefik.yml` (no app-side redirect router) |
| Middleware chain | `chain@file` (secure headers, compression, rate limit) |
| `traefik.docker.network` | `proxy` (mandatory when a container is in multiple networks) |

`ASPNETCORE_ENVIRONMENT=Production` enables `CookieSecurePolicy.Always`. `ForwardedHeaders` (before `UseAuthentication`)
reads `X-Forwarded-Proto=https`; without it the cookies would be blocked. Cookie auth and bearer auth work the same way
behind the proxy.

### Postgres and backup

The compose file runs its own Postgres container (image `pgvector/pgvector:pg16`, see [Data model](#data-model)). The
`postgres-backup` service (`prodrigestivill/postgres-backup-local:16`) writes a gzip-compressed SQL dump daily at 03:00 UTC
into the volume `geef-atelier-backups` (7 daily, 4 weekly, 6 monthly). Restore: `scripts/restore-backup.sh <file.sql.gz>`.
Uploaded originals live on the volume `geef-atelier-sources`, outside the database dump.

### Reviewer calibration

[`06-reviewer-calibration.md`](06-reviewer-calibration.md) describes the severity taxonomy and rules for reviewer roles.

### Deployment procedure

1. Create the `.env` file (`openssl rand`, `tools/HashPassword`); it is gitignored and never goes into the repository.
2. Build the images and start the compose stack; the migration runs at start (D-010) and a failed migration stops the start
   (D-067).
3. Traefik detects the container through its labels and issues the certificate.
4. After every deploy, run `scripts/atelier-smoke.sh` ([`ops/atelier-smoke.md`](ops/atelier-smoke.md)).

The nightly cli-proxy self-update (`scripts/cli-proxy-selfupdate.sh`) builds only from the committed state; with
uncommitted changes under `cli-proxy/` it pauses and sends an alarm instead of building (D-067).

### Model catalog for CLI providers (D-064)

CLI providers do not carry their model list in the Atelier. `ModelCatalog` reads it from the cli-proxy through
`ICliModelSource` / `CliProxyModelSource` (`GET /v1/cli/{providerName}/models`). The gateway reports where the list came
from (`x_source`: `live`, `static` or `degraded`) and, for claude, an `x_aliases` map from `claude-<family>-latest` to the
concrete model.

* **Provenance is three-state.** `ModelCatalogSource` distinguishes `Live`, `StaticByDesign` and `Fallback`.
* **The live list is merged with the curated one**, never replaced.
* **The model matrix holds `*-latest` aliases, run snapshots hold concrete ids.** The alias is resolved once when the
  snapshot is built (`CrewSnapshotModelResolver`), so new runs follow new releases while a resumed run keeps the model of
  its parent.

Caching: 24 h for a successful CLI lookup, 1 h for HTTP providers, 5 minutes for any failure. `ModelCatalogWarmUpService`
warms every active CLI provider shortly after start and every night; `ModelCatalog:CliLiveCatalogEnabled` switches back to
the configured lists.

## Observability

- `PostgresEventSink` (an `IGeefEventSink`) writes pipeline events to the database and triggers live updates.
- `System.Diagnostics.ActivitySource("Geef.Sdk")` for distributed tracing; the pipeline uses the SDK's exception-handling
  and tracing middleware.
- Logging through `Microsoft.Extensions.Logging` to the console.
