# Architecture

*[Deutsch](02-architecture_de.md) · **English***

*Last updated: 2026-11-24 (D-067: stage 0 safety net — run warnings, `Runs.Outcome`, fail-loud start and snapshot handling, preparation timeout, live updates in process and the hub for signed-in users only, configuration switches, end-to-end harness; second review round: described as built, `LiveRunNotifier`, `CircuitDataGate`, secret redaction, outcome table and snapshot test variables; before that 2026-11-22, D-066: model matrix — columns `ModelSlot`/`ModelDomain`/`Domain` (Step46), assignment at snapshot build; before that 2026-08-04, D-064: dynamic model catalog for CLI providers via the cli-proxy, three-state provenance, alias resolution at snapshot build time)*

## Layer diagram

```
┌────────────────────────────────────────────────────────────────┐
│                    Frontends (two adapters)                    │
│  ┌──────────────────────────────┐  ┌─────────────────────────┐ │
│  │  Web UI (Blazor Server)      │  │  MCP server             │ │
│  │  - /new, /runs, /runs/{id}   │  │  - submit_request       │ │
│  │  - SignalR live stream       │  │  - get_run_status       │ │
│  │  - Cookie auth               │  │  - get_run_result       │ │
│  └──────────────┬───────────────┘  └─────────────┬───────────┘ │
│                 │                                │             │
└─────────────────┼────────────────────────────────┼─────────────┘
                  │                                │
                  ▼                                ▼
┌────────────────────────────────────────────────────────────────┐
│              Application service layer  (IRunService)          │
│  - SubmitRunAsync(briefing, sources, options) -> RunId         │
│  - GetRunStatusAsync(runId) -> RunStatus                       │
│  - GetRunResultAsync(runId) -> Result                          │
│  - ListRunsAsync(filter) -> Summaries                          │
│  - CancelRunAsync(runId)                                       │
└──────────────────────────────┬─────────────────────────────────┘
                               │
                               ▼
┌────────────────────────────────────────────────────────────────┐
│                     Background orchestrator                    │
│  - BackgroundService polls pending runs                        │
│  - Builds the Geef pipeline from the run configuration         │
│  - Runs PipelineRunner.RunAsync()                              │
│  - A custom IGeefEventSink writes events to DB + SignalR       │
└──────────────────────────────┬─────────────────────────────────┘
                               │
                               ▼
┌────────────────────────────────────────────────────────────────┐
│                       Geef SDK pipeline                        │
│   Grounding → Execution → Evaluation (loop) → Finalize         │
│                                                                 │
│   Provider implementations live in Infrastructure:              │
│   - BriefingGroundingStep                                       │
│   - LlmExecutionStep      (multi-provider capable)              │
│   - LlmReviewer           (multi-provider capable, tagged)      │
│   - MarkdownFinalizer                                           │
└──────────────────────────────┬─────────────────────────────────┘
                               │
                               ▼
┌────────────────────────────────────────────────────────────────┐
│                          Persistence                           │
│   Postgres via EF Core                                         │
│   Tables: Runs, Iterations, Findings, Events                   │
└────────────────────────────────────────────────────────────────┘
```

## Solution structure

```
Geef.Atelier.slnx
├── src/
│   ├── Geef.Atelier.Core/           // Domain records, interfaces (IRunRepository,
│   │                                // IRunPersistenceService), pipeline-config records
│   ├── Geef.Atelier.Application/    // IRunService contract + RunService implementation,
│   │                                // ApplicationServiceExtensions (AddAtelierApplication)
│   ├── Geef.Atelier.Infrastructure/ // EF Core, LLM clients (OpenAiCompatibleClient),
│   │                                // event sink, provider implementations, repositories,
│   │                                // Finalizers/ (4 executor implementations),
│   │                                // FormatConverters/ (Markdig, QuestPDF, OpenXml, PlaintextStripper)
│   ├── Geef.Atelier.Web/            // Blazor Server: UI + BackgroundService
│   │                                // (RunOrchestratorService), DI composition
│   └── Geef.Atelier.Mcp/            // Class library: MCP tool definitions,
   │                                // hosted in the Web project (shared DI, shared container)
├── tests/
│   └── Geef.Atelier.Tests/          // xUnit; EndToEnd/ = end-to-end harness (D-067)
└── tools/
    └── Eval/                        // Python measuring tool, blind pairwise comparison (D-067)
```

**Rationale for the split:**

- **Core** is LLM-free and persistence-free — it contains only records, interfaces, domain logic. Thus testable without infrastructure.
- **Infrastructure** encapsulates all external dependencies (Postgres, LLM APIs, Geef SDK). Provider implementations live here because they need LLM clients and repositories.
- **Web** hosts the UI, the `BackgroundService` and the `IRunService` implementation. The latter could later move into its own project but is most practical here in the skeleton.
- **Mcp** is a **class library** (no own host). It contains all MCP tool definitions. The MCP endpoint lives in the `Web` project (path `/mcp`), which references `Geef.Atelier.Mcp` and registers the tools in the same DI container. Advantages: no second host process, `IRunService` and all singletons (SignalR, DbContext) are shared directly, no HTTP hop between MCP and the application layer.

## Data model

As of May 2026 the schema comprises **28 tables**, with hand-written migrations
`InitialCreate` + `Step06`/`Step09`–`Step30`. Grouped:

| Group | Tables | Introduced |
|---|---|---|
| Run core | `Runs`, `Iterations`, `Findings`, `Events` | InitialCreate |
| Crew/profiles | `ReviewerProfiles`, `ExecutorProfiles`, `CrewTemplates` | Step10 |
| Advisor | `AdvisorProfiles`, `AdvisorConsultations` | Step11 |
| Grounding | `GroundingProviderProfiles`, `GroundingConsultations` | Step13 |
| Vector-store/RAG | `KnowledgeDocuments`, `KnowledgeDocumentChunks` | Step14 |
| Cost tracking | `IterationActorCosts` | Step16 |
| Template Studio | `TemplateStudioAnalyses` | Step17 |
| Multi-user | `Users` | Step20 |
| OAuth 2.1 | `OAuthClients`, `OAuthAuthorizationCodes`, `OAuthAccessTokens`, `OAuthRefreshTokens`, `OAuthAuditLog` | Step19 |
| Finalizer | `FinalizerProfiles`, `RunArtifacts`, `FinalizationActorCosts` | Step22 |
| Learning | `LearningEntries` | Step30 |

The four run-core tables are documented in detail below; the other groups are
described in their respective feature sections or in the [decisions log](05-decisions-log.md)
(D-028 ff.). `Runs` additionally carries columns from later migrations
(`CreatedByUser`, `CostTotal`, `CrewTemplateName`, `CrewSnapshot`, `AdvisorRetryAttempted`,
`FinalizerCostEur`, `FinalizerErrorMessage`, `ParentRunId`, `SeedDraftText`, `Kind`, `ParentCompositionRunId`,
`Outcome`, `DeliveredIteration`, `ReportJson`). Migration Step47 (D-067) adds the table `RunWarnings`
and the last three `Runs` columns.

### Runs

| Column | Type | Note |
|---|---|---|
| Id | uuid (PK) | |
| CreatedAt | timestamptz | |
| StartedAt | timestamptz | nullable |
| CompletedAt | timestamptz | nullable |
| Status | varchar(50) | Pending / Running / Completed / Failed / Aborted |
| BriefingText | text | |
| ConfigJson | jsonb | model selection, budget — a snapshot at creation time |
| FinalText | text | nullable, set when Status=Completed |
| ErrorMessage | text | nullable, set when Status=Failed |
| TokensTotal | int | accumulated over all LLM calls |
| CostTotal | numeric(10,4) | accumulated |
| CancellationRequested | bool | true when the user wants to cancel the run |
| CrewTemplateName | varchar(100) | nullable; name of the template (e.g. `"klassik"`). Null = custom-crew submit. |
| CrewSnapshot | jsonb | nullable; the fully embedded CrewSnapshot at submit time. |
| AdvisorRetryAttempted | bool | nullable; true when an OnConvergenceFailure retry has already been performed (single-retry cap). |
| CreatedByUser | text | nullable; username of the creating user (run-user isolation, D-042). Index `IX_Runs_CreatedByUser` (Step21). |
| FinalizerCostEur | numeric(10,6) | nullable; accumulated cost of all Transform-finalizer LLM calls (Step22). |
| FinalizerErrorMessage | text | nullable; error message when a finalizer chain partially or fully failed (Step22). |
| ParentRunId | uuid (FK→Runs) | nullable; self-referential, no cascade — set when this run was resumed from another run (Step23). |
| SeedDraftText | text | nullable; last artifact text copied from the parent run's final iteration, used to seed iteration 1 of this resumed run (Step23). |
| Kind | int | `0 = Standard` (default), `1 = Learning`, `2 = CrewComposition` — gates finalizers and recursion guards; existing rows default to Standard (Step30/Step35). |
| ParentCompositionRunId | uuid (FK→Runs) | nullable; self-referential, no cascade — set when this task-run was chained from a composition-run (Step35). Audit link from task-run back to the crew-composer run that created its crew. |
| Outcome | int | nullable; what the run delivered: `0 = Converged`, `1 = DeliveredWithOpenPoints`, `2 = Failed`, `3 = Aborted` (Step47). `NULL` while the run is active and for runs that ended before Step47. See [Run outcome](#run-outcome-and-runstatus). |
| DeliveredIteration | int | nullable; iteration whose text was delivered (Step47). |
| ReportJson | jsonb | nullable; reserved for the result report (Step47); not written yet. |

### RunWarnings (Step47)

Problems a run worked around instead of stopping (D-067). Deleted together with the run (`ON DELETE CASCADE`).

| Column | Type | Note |
|---|---|---|
| Id | uuid (PK) | |
| RunId | uuid (FK→Runs) | index `IX_RunWarnings_RunId` |
| Iteration | int | nullable; iteration in which the problem occurred |
| Source | varchar(100) | `grounding`, `review`, `model-assignment`, `llm`, `finalizer` or `run` (constants in `RunWarningSources`) |
| Code | varchar(100) | stable machine-readable code, e.g. `grounding.provider_skipped`, `grounding.scope_unknown`, `review.reviewer_failed`, `llm.failover`, `finalizer.failed`, `run.result_not_saved` (constants in `RunWarningCodes`) |
| Message | text | short plain-English explanation for the user |
| CreatedAt | timestamptz | |

### Iterations

| Column | Type | Note |
|---|---|---|
| Id | uuid (PK) | |
| RunId | uuid (FK) | |
| IterationNumber | int | 1-based |
| ArtifactText | text | snapshot of the text after this iteration |
| CreatedAt | timestamptz | |

### Findings

| Column | Type | Note |
|---|---|---|
| Id | uuid (PK) | |
| IterationId | uuid (FK) | |
| ReviewerName | varchar(200) | |
| Severity | enum | Critical / Major / Minor / Info |
| Message | text | |
| CreatedAt | timestamptz | |

### Events

| Column | Type | Note |
|---|---|---|
| Id | bigint (PK, identity) | |
| RunId | uuid (FK) | |
| EventType | varchar(100) | from the Geef event-sink vocabulary |
| PayloadJson | jsonb | |
| CreatedAt | timestamptz | |

**Indices:**
- `Runs.Status` (for background polling)
- `Events.RunId` (for the detail view)
- `Iterations.RunId` (for the detail view)

### FinalizerProfiles (Step22)

| Column | Type | Note |
|---|---|---|
| Name | varchar(100) (PK) | |
| DisplayName | varchar(200) | |
| Description | text | nullable |
| FinalizerType | varchar(50) | `FileExport` / `MetadataEnrich` / `ExternalSink` / `Transform` |
| Settings | jsonb | type-specific configuration |
| IsSystem | bool | true for built-in system profiles |
| CreatedAt | timestamptz | |
| UpdatedAt | timestamptz | |

### RunArtifacts (Step22)

| Column | Type | Note |
|---|---|---|
| Id | uuid (PK) | |
| RunId | uuid (FK→Runs ON DELETE CASCADE) | |
| FinalizerProfileName | varchar(100) | name of the finalizer profile that produced this artifact |
| ArtifactType | enum | `File` / `Url` / `Status` |
| Filename | text | nullable; original filename for File artifacts |
| ContentType | text | nullable; MIME type for File artifacts |
| SizeBytes | bigint | nullable; byte size for File artifacts |
| StorageUri | text | path or URL to the stored artifact |
| StatusMessage | text | nullable; human-readable status or error message |
| CreatedAt | timestamptz | |

### FinalizationActorCosts (Step22)

| Column | Type | Note |
|---|---|---|
| Id | uuid (PK) | |
| RunId | uuid (FK→Runs ON DELETE CASCADE) | |
| ActorName | varchar(200) | name of the Transform-finalizer actor |
| ModelName | varchar(200) | LLM model used |
| InputTokens | int | |
| OutputTokens | int | |
| CostEur | numeric(10,6) | nullable |
| CreatedAt | timestamptz | |

### New columns on existing tables (Step22 — Finalizer-Foundation)

| Table | Column | Type | Note |
|---|---|---|---|
| `CrewTemplates` | `FinalizerProfileNames` | jsonb | ordered array of finalizer profile name strings |
| `CrewTemplates` | `RunFinalizersOnMaxAttempts` | bool | default false; if true, finalizers run even when convergence fails |

### New columns on existing tables (Step23 — Run-Resume)

See `Runs` table above (`ParentRunId`, `SeedDraftText`).

### FinalizerProfiles — type values (Step22 + Step30 + Step35)

| `FinalizerType` value | Meaning | Introduced |
|---|---|---|
| `0` — `FileExport` | Writes the final text to a file in a configured format (md, html, pdf, docx, txt, json) | Step22 |
| `1` — `MetadataEnrich` | Enriches run metadata (tags, title extraction, …) | Step22 |
| `2` — `ExternalSink` | Pushes the final text to an external service endpoint | Step22 |
| `3` — `Transform` | Applies an LLM transformation pass to the text (summary, translation, …) | Step22 |
| `4` — `LearningExtract` | Extracts structured learnings from a completed run and fires a learning-evaluation gate run fire-and-forget (opt-in; not attached to standard templates) | Step30 |
| `5` — `LearningPublish` | Publishes approved learning candidates to the learning store, or marks rejected ones. Runs inside the `learning-evaluation` crew | Step30 |
| `6` — `CrewMaterialize` | Parses the converged `CrewSpecArtifact` JSON, validates it, deduplicates via `CrewTemplateEmbeddings` (threshold 0.90), materializes profiles + template to the DB, embeds the new template, and optionally chains a task-run. Runs only inside the `crew-composer` composition-run | Step35 |

### LearningEntries (Step30)

The learning store. Physically separate from the curated knowledge base (`KnowledgeDocuments`) — a constitutive protection mechanism against model collapse.

| Column | Type | Note |
|---|---|---|
| Id | uuid (PK) | |
| Text | text | The condensed, gate-approved learning text |
| SourceRunId | uuid (FK→Runs ON DELETE SET NULL) | The standard run that triggered the extraction |
| LearningRunId | uuid (FK→Runs ON DELETE SET NULL) | nullable; the learning-evaluation run that approved/rejected this entry |
| Domain | text | Template name of the source run — used for domain-boost retrieval |
| Status | int | `0 = Proposed`, `1 = Approved`, `2 = Rejected` |
| StructuredFactsJson | text | Machine-readable JSON of the raw run facts assembled by the extractor |
| OwnerUsername | text | Inherited from the source run's `CreatedByUser` |
| CreatedAt | timestamptz | |
| ApprovedAt | timestamptz | nullable; set when the publisher marks the entry as Approved |
| Embedding | vector(1536) | nullable; computed by `IEmbeddingProvider` at approval time; stored as pgvector type |

**Indices:**
- `IX_LearningEntries_Domain_Status` — composite btree for filtered list queries
- `IX_LearningEntries_Embedding_HNSW` — HNSW index with `vector_cosine_ops` for cosine-similarity retrieval

**pgvector raw-SQL pattern:** `Pgvector.EFCore` 0.3.0 is incompatible with Npgsql 10.x — a string value converter causes `42804: column is of type vector but expression is of type character varying` on INSERT. The `Embedding` column is therefore **ignored by EF Core** (`builder.Ignore(e => e.Embedding)` in `LearningEntryConfiguration`); all insert and cosine-search operations use raw ADO.NET with `NpgsqlParameter(NpgsqlDbType.Vector)` — identical to the `VectorSearchRepository` pattern (D-036). Fixed in D-055 Folgefix 2.

### New columns on existing tables (Step30 — Continuous Learning)

| Table | Column | Type | Note |
|---|---|---|---|
| `Runs` | `Kind` | int | `0 = Standard` (default), `1 = Learning` |

### CrewTemplateEmbeddings (Step35)

Stores embeddings of materialized crew templates. Used for two purposes: (a) crew-catalog grounding during a composition-run (semantic similarity search over existing templates), (b) dedup check in `CrewMaterializeFinalizerExecutor` (prevents functionally identical templates from being created twice).

| Column | Type | Note |
|---|---|---|
| Id | uuid (PK) | |
| TemplateName | varchar(100) (FK→CrewTemplates ON DELETE CASCADE) | |
| Embedding | vector(1536) | nullable; computed by `IEmbeddingProvider` at materialization time |
| CreatedAt | timestamptz | |

**Indices:**
- `IX_CrewTemplateEmbeddings_Embedding_HNSW` — HNSW index with `vector_cosine_ops` for cosine-similarity retrieval

**pgvector raw-SQL pattern:** same as `LearningEntries` (D-036/D-054) — `Embedding` ignored by EF Core, all insert/search via raw ADO.NET.

### New columns on existing tables (Step35 — Auto-Crew)

| Table | Column | Type | Note |
|---|---|---|---|
| `Runs` | `ParentCompositionRunId` | uuid FK→Runs nullable | links a task-run back to the composition-run that created its crew |

## Auto-Crew: Composition-Run pipeline (D-059)

A Composition-Run (`RunKind.CrewComposition = 2`) is a full GEEF-Run whose purpose is to compose a new crew. GEEF-on-GEEF: the meta-crew `crew-composer` runs through the standard draft → critique → refine → converge loop and materializes the result via `CrewMaterializeFinalizerExecutor`.

```
COMPOSITION-RUN  (Kind=CrewComposition, crew "crew-composer")
  ├─ Grounding:  crew-catalog (all available profiles as context)
  ├─ Advisor:    crew-catalog-advisor (BeforeFirstExecution)
  ├─ Executor:   CrewComposerExecutor (forces submit_crew_spec tool-call)
  ├─ Reviewers:  CrewSpecValidatorReviewer (deterministic, no LLM)
  │              + 4 LLM reviewers (diversity, prompt-quality, grounding-fit, finalizer-fit)
  ├─ ConvergenceOverride: MaxIterations=4, AbortOnCritical=false, StagnationThreshold=3
  └─ Finalizer:  crew-materializer (FinalizerType.CrewMaterialize=6)
       ├─ Parse → CrewSpecArtifact JSON
       ├─ Validate (required fields, reviewer count ≥ 1, provider names)
       ├─ Dedup via CrewTemplateEmbeddings (threshold 0.90)
       ├─ Materialize profiles + template (atomic transaction)
       ├─ Embed new template → CrewTemplateEmbeddings
       └─ Optionally chain task-run (ChainToTaskRun=true, RunKind.Standard only)
```

**Recursion guards:** `LearningExtractFinalizerExecutor` skips `Kind == CrewComposition`; `CrewMaterializeFinalizerExecutor` may only chain `RunKind.Standard` runs (no nested composition). See D-059 in the [decisions log](05-decisions-log.md).

## Crew system (PS-5)

Every run uses a **crew** of an executor + reviewers. Profiles are reusable configuration building blocks.

### New tables (migration Step10 + Step11)

| Table | Migration | Content |
|---|---|---|
| `ReviewerProfiles` | Step10 | Custom reviewer profiles (system profiles live as code constants in `SystemCrew`). |
| `ExecutorProfiles` | Step10 | Custom executor profiles. |
| `CrewTemplates` | Step10 | Custom crew templates. |
| `AdvisorProfiles` | Step11 | Custom advisor profiles. |
| `AdvisorConsultations` | Step11 | Persisted advisor outputs per iteration. |
| Columns `ModelSlot`/`ModelDomain` (executor/reviewer/advisor), `Domain` (CrewTemplates) | Step46 | Model matrix (D-066): role and domain; managed profiles get their model at snapshot-build time from `ModelMatrix` (`IModelAssignmentService`, Application). Nullable `varchar(40)`, tolerant enum converter. |

### ProfileBasedReviewer / ProfileBasedExecutor

Replace the old `LlmReviewer` / `LlmExecutionStep`. They use `ILlmClientResolver.ForProfile(provider, model, maxTokens?)` instead of actor-based resolution.

### EvaluationStrategies

All four strategies via the Geef SDK: `Parallel`, `Sequential`, `FailFast`, `PriorityOrdered`.

More details: [`08-crew-system.md`](08-crew-system.md).

## Advisor pipeline layer (PS-7)

Advisors are realized as a decorator around `IExecutionStep`. The `AdvisorAwareExecutor` slots transparently in front of every executor call without modifying the Geef SDK (D-031(a)).

### Decorator chain

```
AtelierPipelineFactory
  └── AdvisorAwareExecutor (IExecutionStep decorator)
        1. Filters advisors by trigger (BeforeFirst / BeforeEvery)
        2. ProfileBasedAdvisor: LLM call (plain text), persists AdvisorConsultation
        3. Writes the output → context[AtelierContextKeys.AdvisorBlock]
        4. Delegates to ProfileBasedExecutor (the real execution step)
```

### AtelierContextKeys.AdvisorBlock

The advisor output ends up as a single text block in the `IRunContext`. Format:

```
[ADVISOR: briefing-clarifier]
<advisor output text>

[ADVISOR: devils-advocate]
<advisor output text>
```

The executor system prompt can explicitly reference this block. Multiple advisors accumulate sequentially (D-031(d)).

### Convergence-failure retry

```
ConvergenceFailedException
  → RunOrchestratorService.TryConvergenceFailureRetryAsync
      ├── RunEntity.AdvisorRetryAttempted == true → Status = Failed (no second retry)
      └── AdvisorRetryAttempted = true → OnConvergenceFailure advisors enabled → pipeline retry
```

`RunEntity.AdvisorRetryAttempted` (migration Step11) is the single-retry cap (D-031(e)).

### DB extensions (migration Step11AdvisorSystem)

| New | Content |
|---|---|
| `AdvisorProfiles` | Custom advisor profiles |
| `AdvisorConsultations` | Persisted advisor output per iteration (RunId, IterationNumber, AdvisorName, OutputText) |
| `Runs.AdvisorRetryAttempted` | bool nullable — retry-cap flag |

More details: [`08-crew-system.md`](08-crew-system.md) → section "Advisor passes (PS-7)".

## Mapping to GEEF providers (PS-7 state)

| GEEF phase | Provider implementation | Behaviour |
|---|---|---|
| Grounding | `BriefingGroundingStep` | Writes the briefing into the context, no external sources |
| Pre-execution | `AdvisorAwareExecutor` (decorator) | Consults BeforeFirst/BeforeEvery advisors; writes the AdvisorBlock into the context |
| Execution | `ProfileBasedExecutor` | LLM call with the profile system prompt + PreviousFindings + AdvisorBlock; model from the `ExecutorProfile` |
| Evaluation | `ProfileBasedReviewer` × N | N reviewers from `CrewSnapshot.Reviewers`; strategy configurable |
| Finalize | `IFinalizerExecutor` chain | Runs after convergence. Iterates `snapshot.Finalizers` in order. Each `IFinalizerExecutor` produces zero or one `RunArtifact` (File, Url, or Status). Transform finalizers may update `currentText`. `RunFinalizersOnMaxAttempts=true` causes finalizers to run even on convergence failure. Partial-success contract: a failing finalizer records a Status artifact and the chain continues; run status stays Completed. Learning finalizers (`LearningExtract`, `LearningPublish`) each carry a recursion guard: the extractor skips Learning-Runs, the publisher skips Standard-Runs. |
| Convergence failure | `TryConvergenceFailureRetryAsync` | Enables OnConvergenceFailure advisors, single retry (AdvisorRetryAttempted cap) |

**Grounding provider types (ProviderType string):**

| `ProviderType` | Implementation | Note |
|---|---|---|
| `tavily` | `TavilyGroundingProvider` | Tavily web search |
| `vector-store` | `VectorStoreGroundingProvider` | Semantic search over curated `KnowledgeDocuments` (pgvector) |
| `static-context` | `StaticContextGroundingProvider` | Verbatim fixed text; style guides, glossaries |
| `url-fetch` | `UrlFetchGroundingProvider` | Fetches one URL with SSRF guard |
| `news-search` | `TavilyNewsGroundingProvider` | Tavily news search with date filter |
| `academic-search` | `AcademicSearchGroundingProvider` | arXiv / Semantic Scholar / OpenAlex adapters |
| `rest-api` | `RestApiGroundingProvider` | Generic authenticated REST endpoint with JSONPath extraction |
| `learning-retrieval` | `LearningRetrievalGroundingProvider` | Cosine-similarity search over `Approved` `LearningEntries`; domain-boost ranking (`finalScore = similarity × sameDomainBoost \| crossDomainPenalty`); citations use `learning://{id}` scheme; place after curated-knowledge providers in templates |

**Convergence policy:** `DefaultConvergencePolicy` from `ConvergenceOptions`, overridable via `ConvergencePolicyOverride` in the CrewTemplate.

**Evaluation strategy:** `Parallel` (default). All four strategies selectable per template.

## LLM provider layer (implemented in migration M1 and the CLI-provider split, D-017/D-032)

The LLM layer is implemented **OpenAI-API-compatible**. Three configured providers (as of the CLI-provider split):

| Provider name | Endpoint | Billing |
|---|---|---|
| `openrouter` | `https://openrouter.ai/api/v1` | Pay-per-token |
| `claude-cli` | `http://cli-proxy:8090/v1/claude` | Claude subscription |
| `codex-cli`  | `http://cli-proxy:8090/v1/codex`  | Codex subscription |

The `cli-proxy` side container (FastAPI, Python) exposes two explicit endpoints that route directly to the respective CLI — without a model-name heuristic. A legacy endpoint `/v1/chat/completions` is retained for backward compatibility and logs a deprecation warning.

### Abstraction

```csharp
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}
```

`OpenAiCompatibleClient` is the only implementation in the skeleton. Further OpenAI-compatible endpoints (OpenAI directly, local Ollama, Together AI) are addressable by adjusting `LlmOptions.Endpoint` — without a code change.

### Provider configuration and model choice

> **Note:** the original flat `Llm.Actors` schema (one fixed model entry per actor
> in `appsettings.json`) has been superseded since the crew system (D-028). Model
> and provider choice are today **data-driven** parts of the reviewer/executor/advisor
> **profiles** (see [`08-crew-system.md`](08-crew-system.md)), not of the app configuration.

`appsettings.json` only configures the **provider endpoints** (multi-provider,
D-027/D-032):

```json
{
  "Llm": {
    "Providers": {
      "openrouter": { "Endpoint": "https://openrouter.ai/api/v1", "ApiKey": "" },
      "claude-cli": { "Endpoint": "http://cli-proxy:8090/v1/claude", "ApiKey": "" },
      "codex-cli":  { "Endpoint": "http://cli-proxy:8090/v1/codex",  "ApiKey": "" }
    }
  }
}
```

API-key override via environment variable, e.g. `Llm__Providers__openrouter__ApiKey`
or `LLM_OPENROUTER_API_KEY` (env fallback). Which actor uses which provider and
which model is determined by the respective profile in the run's `CrewSnapshot`
(`ILlmClientResolver.ForProfile`). The guiding principle **model pluralism** is thus
played out per crew/template: reviewers deliberately run on foreign models relative
to the executor (default system crew: executor `claude-cli`, reviewers mostly `codex-cli`).

### Token tracking

`LlmTokenUsage` (`InputTokens`, `OutputTokens`) is set per iteration by `ProfileBasedExecutor`/`ProfileBasedReviewer` into the `IRunContext` and accumulated by `PostgresEventSink` into `Runs.TokensTotal` (wire names `prompt_tokens`/`completion_tokens` of the OpenAI API). Since cost tracking (Step16) the per-actor, per-iteration costs are additionally persisted in `IterationActorCosts` and aggregated into `Runs.CostTotal`.

## Run safeguards (D-067)

Stage 0 ("safety net") makes problems visible instead of letting them pass silently. Background:
[D-067](05-decisions-log.md).

### Run outcome and `RunStatus`

`Runs.Status` keeps its meaning. In particular, a run that stops without convergence still ends as
`Failed` even when it delivers a text (status decision of 2026-07-17: best-effort runs keep
`Failed`/`Aborted`). `Runs.Outcome` is the additional display signal for what the run delivered:

| `Outcome` | Meaning |
|---|---|
| `Converged` | All reviewers approved; the converged text and the results of the finalizers were stored. |
| `DeliveredWithOpenPoints` | The run stopped without full convergence and delivered its best available draft. Status `Failed` (not converged), or `Aborted` when a critical reviewer finding stopped a run that already has a text; the critical finding stays open and is listed with the findings. |
| `Failed` | The run ended without a usable text, or its result could not be stored completely (warning `run.result_not_saved`). A text may still be present; it is not guaranteed to be complete. |
| `Aborted` | Cancelled by the user or stopped by the service, also during the finalizers. A text stored before the stop stays readable (`FinalText`). |

`Outcome` means **"finished and stored, including all finalizers"**: it is set only once the final text
(possibly transformed by a finalizer) is in the database, so a client that sees an `Outcome` can read the
final text. The first value wins; later errors do not overwrite it. `DeliveredIteration` names the
iteration whose text was delivered. Runs that ended before Step47 keep `NULL`. When a run fails, the
findings of the last iteration are stored as well, so the run detail page and MCP show why it stopped.

`Status` and `Outcome` can briefly disagree: the event sink sets `Completed` (or `Failed`) and the text
before the finalizers run, `Outcome` follows only afterwards. Clients therefore wait for `Outcome`, not for
a terminal status; `scripts/atelier-smoke.sh` and the `atelier` arm of `tools/Eval` do so. If the service
is stopped hard during the finalizers, the crash recovery at the next start also sets an outcome for runs
whose status is already terminal but whose `Outcome` is still empty, so no client waits forever. A
cancellation or service stop during the finalizers ends as `Aborted` without "finalizer failed" warnings.

Only MCP returns `Outcome` (`get_run_status`, `get_run_result`). The web interface does not read it: the
"Warnings" block on the run detail page (`RunWarningsBlock`) chooses its intro sentence from `Status` and
whether a text exists.

### Warnings at the run

A run records every problem it works around as a warning in `RunWarnings`, for example:

- a grounding source that was skipped or failed (attachments, web search, knowledge base),
- a reviewer that failed in an iteration,
- a model that could not be assigned as planned,
- a failover to another model family in the LLM client,
- a finalizer that failed, was skipped or was not run.

`IRunWarningSink` (Core contract, Infrastructure implementation `RunWarningSink`) writes each warning
in its own short-lived database scope; a failed write is logged and never stops the run. It is a required
dependency everywhere it is used (run service, orchestrator, pipeline factory, grounding step), so a missing
registration fails at start instead of silently dropping warnings. The LLM client reports failovers through
the ambient `RunWarningScope`, which the orchestrator opens per run.

Warnings appear on the run detail page in a "Warnings" block (only when there are any) and in the MCP
fields `warnings` and `outcome` of `get_run_status` and `get_run_result`
([09-endpoint-reference](09-endpoint-reference.md#run-status-and-result-warnings-and-outcome)).
Warning texts and the error texts stored at the run (`ErrorMessage`, status messages) are fixed, short,
plain sentences for the user, with display names where needed; technical details such as exception texts,
host names, key names or upstream error bodies go only to the log. The reason a run stopped before all checks
passed is one fixed sentence per stop reason (`RunStopMessageExtensions`), and the skip reasons of source sorting
(`GroundingRefiner`) are fixed sentences too. Before anything technical is logged or
stored, `SecretRedactionExtensions` (Core, `RedactSecrets`, `ToRedactedLogText`) replaces credential-like
fragments (known key formats, bearer tokens, `name=value` pairs with a secret-sounding name, long opaque
tokens) with `[redacted]`. A name counts as secret-sounding when its last part is a word such as `key`, `token`,
`secret` or `password` (also `OPENAI_API_KEY`, `x-api-key`, `apiKey`); its value is removed only when it looks
like a secret (at least 12 characters without whitespace with a letter and a digit) or when it is a URL query
parameter. Ordinary sentences such as "Key: Strategic goals" or "Basic research" stay unchanged.

### Fail loud instead of silent

| Situation | Behaviour since D-067 |
|---|---|
| A database migration fails at start-up | The start aborts with a critical log entry (`MigrateDatabaseOrFailAsync`). The health check `pending-migrations` reports pending migrations. |
| A run's crew snapshot is missing, unreadable, empty, of an unknown version, or lacks executor, reviewer or advisor list | `CrewSnapshot.DeserializeForRun` throws `InvalidCrewSnapshotException`; the run ends `Failed` with a clear message. There is no fallback to the classic crew any more; the tolerant `Deserialize` is for display only. |
| A crew has no reviewer | `AtelierPipelineFactory` refuses to build the pipeline; the run ends `Failed` with a message. The silent `AutoApproveReviewer` fallback is removed. |
| A grounding profile has an unknown provider type | It is skipped with a warning instead of crashing the run. |
| A run without a usable crew snapshot is resumed | `RunService.ResumeRunAsync` rejects it immediately with `RunNotResumableException` (a plain sentence for the user) instead of creating a run that waits for the preparation timeout. The run detail page hides "Resume" for such runs and shows the message. |

### Preparation timeout for runs with attachments

A run with attachments is first stored **without** a crew snapshot; the snapshot is written only after all
attachments are uploaded and indexed. The orchestrator picks up only `Pending` runs **with** a snapshot,
so a run can no longer start before its attachments are ready. A `Pending` run that still has no snapshot
after `Orchestrator:PreparationTimeout` (default 15 minutes) is set to `Failed` with a message asking the
user to submit it again; a restart of the service cleans up the same way. A resumed run finds the
attachments of the run it continues (lineage via `ParentRunId`/`ParentCompositionRunId`). A run without
attachments triggers no embedding call.

## Configuration switches (D-067)

| Key | Default | Effect |
|---|---|---|
| `Learning:Enabled` | `false` | Switches the continuous learning loop (D-054) on or off. When off, the `learning-extractor` and `learning-publisher` finalizers return immediately (no LLM call, no entry, no `learning-evaluation` run, no embedding), `learning-retrieval` grounding returns empty context without an embedding, and `/crew/learnings` shows that learning is paused. Crews that contain these parts stay valid. |
| `EnvReference:Allowed` | empty list | Additional environment-variable names or prefixes (ending in `*`) that configuration may reference, on top of the built-in default list and the prefix `ATELIER_SECRET_*`. Details: [10-tool-system](10-tool-system.md#secrets-and-the-environment-variable-allow-list). |
| `Orchestrator:PreparationTimeout` | `00:15:00` | How long a `Pending` run may stay without a crew snapshot (attachments still being prepared) before it is failed. |

`Learning:Enabled` is registered exactly once (`AddLearningSwitch`, Infrastructure); every reader
(finalizers, the retrieval provider, the learnings page) reads it through `IOptionsMonitor`, so a change
reaches all of them alike.

Missing grounding keys (`Tavily:ApiKey`, the OpenRouter key used for embeddings) are logged by name at
start-up (`GroundingKeyGuard`); values are never logged.

## Testing and measuring (D-067)

### End-to-end harness (`tests/Geef.Atelier.Tests/EndToEnd/`)

`AtelierEndToEndHost` starts the **real** application through `WebApplicationFactory<Program>` against
its own database per test on the Testcontainers Postgres of `PostgresFixture`. Migration, DI wiring,
orchestrator, grounding, pipeline, event sink and the MCP endpoint are the production ones. Only external
HTTP is replaced:

- `ScriptedLlmEndpoint` stands in for the cli-proxy (OpenAI format) and answers per role (executor,
  reviewer, refiner, query extractor); `LlmTrafficRecorder` records every request body, including the
  document-mode fields `document` and `context_document`.
- `FakeTavilyEndpoint`, `FakeEmbeddingsEndpoint` and `FakeCliProxyModelsEndpoint` serve web search,
  deterministic embeddings and the model list.
- Every other named HTTP client is blocked and logged, so no test can reach the network by accident.

Helpers: `SubmitAsync` (real `IRunService`), `WaitForTerminalAsync` (waits for a terminal status with an
`Outcome`), `QueryAsync`, `CallMcpToolAsync` (real `/mcp` endpoint with a bearer token).
`HeldOrchestratorStarter` starts the real orchestrator only after submission.

**What it proves:** a request runs through the real wiring from submission (web or MCP) to the stored
result, and the content of attachments and web search actually reaches the executor prompt. The tests
in `PromptWiringEndToEndTests` fail when the grounding repair of D-067 is reverted; this was checked by
reverting it. They are ordinary regression tests without a special trait.

```bash
export MSBUILDNODEREUSE=0 DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet test tests/Geef.Atelier.Tests/Geef.Atelier.Tests.csproj --filter "FullyQualifiedName~Geef.Atelier.Tests.EndToEnd"
dotnet test tests/Geef.Atelier.Tests/Geef.Atelier.Tests.csproj --filter "FullyQualifiedName~PromptWiringEndToEndTests"
```

The tests need Docker (Testcontainers) and run in their own collection without parallelism. A new
pipeline path should get its own end-to-end test here, from the entry point to the stored result.

### Test against the production snapshots

`ProductionSnapshotFixtureTests` reads every exported production crew snapshot through the strict
run-time read (`CrewSnapshot.DeserializeForRun`) and checks schema version, reviewers and the grounding
path of the system profiles. The exports contain real orders and are **never** part of the repository;
without the variables the test is skipped.

| Variable | Meaning |
|---|---|
| `ATELIER_SNAPSHOT_FIXTURES` | Directory with one exported snapshot per run (`*.json`). |
| `ATELIER_SNAPSHOT_FIXTURES_MIN` | Minimum number of files the export must contain (default 1). Set it to the number of exported runs so that an incomplete export fails instead of passing quietly. |

Before a deploy that touches snapshot reading, the operator exports the current snapshots, sets both
variables and runs the test (`--filter "FullyQualifiedName~ProductionSnapshotFixtureTests"`). Directory and
count are not written down in the repository.

### Measuring tool `tools/Eval`

A Python tool that compares the Atelier with other paths (for example a raw single model call) on a fixed
task set by blind pairwise comparison. Usage, environment variables, task format and privacy rules:
[`tools/Eval/README.md`](../tools/Eval/README.md). Real tasks stay outside Git.

### Operations scripts

`scripts/atelier-smoke.sh` (a mini run through MCP after each deploy) and `scripts/atelier-watch.sh`
(alarm for stuck runs and an unhealthy cli-proxy) are described in [`ops/atelier-smoke.md`](ops/atelier-smoke.md)
and [`ops/atelier-watch.md`](ops/atelier-watch.md).

## UI architecture (step 7)

### Pages

Three Blazor Server pages in `src/Geef.Atelier.Web/Components/Pages/`:

| Route | Component | Function |
|---|---|---|
| `/new` | `New.razor` | Submit form. `EditForm` + `DataAnnotationsValidator`. Redirect to `/runs/{id}` after submit. |
| `/runs` | `Runs.razor` | Run list. Status filter via query parameter. Live update through `IRunUpdateFeed.WatchRunList` (own runs; all runs for administrators). |
| `/runs/{id}` | `RunDetail.razor` | Run detail. Live update through `IRunUpdateFeed.WatchRunAsync` (owner or administrator only). Cancel button for Pending/Running. Shows a "Warnings" block when the run has warnings (D-067). |

### Live updates: `IRunUpdateFeed` and `RunHub` (D-067)

The Blazor pages (`Runs`, `RunDetail`, the dashboard on `Index`) **no longer open a `HubConnection`**.
Before D-067 they connected server-side to the public URL of their own hub, without the user's cookie,
and chose their group names themselves. They now subscribe to the in-process
`IRunUpdateFeed` (`Web/Notifications/`, implementation `InProcessRunUpdateFeed`, registered by
`AddRunLiveUpdates()`):

- The topic is derived on the server from the signed-in user (`RunUpdateTopics`); the caller never
  chooses it. A run topic is only granted to the run's owner or an administrator; the list and dashboard
  topics carry the user's own runs, or all runs for administrators.
- Every subscriber has its own queue; handlers run one after another, identical pending updates are
  merged, and a failing subscriber does not block the others. Publishing never waits for subscribers.
- Access is decided when subscribing. A subscription lasts as long as the page's circuit, and like every
  Blazor Server page the circuit keeps the sign-in it was opened with; a re-check when the sign-in expires
  is not built (low risk: the run topic carries only an id, the all-users dashboard topic is for
  administrators only).
- The run detail page loads once more right after subscribing, so no update between the first load and the
  subscription is lost.

**`CircuitDataGate`** (`Web/Components/`, scoped per circuit, registered by `AddRunLiveUpdates()`): all
components of a circuit share one `DbContext`, which allows only one operation at a time. A live-update
reload and a user action (filter, delete, resume, cancel) could otherwise overlap. Rule: **every database
access of a page that receives live updates runs through `DataGate.RunAsync(...)`** (today `Runs`,
`RunDetail`, `Index` and `CancelButton`). Calls must **not be nested**: a gated action never calls another
gated action (the gate is a `SemaphoreSlim(1,1)` and would wait for itself); callbacks such as
`OnCancelled` or a reload run after the gated block.

`RunHub` (`Web/Hubs/RunHub.cs`, mapped to `/hubs/runs` by `MapRunHub()`) has no client inside the
application any more, and no external client is known. It stays **until its removal in stage 8**
([D-068](05-decisions-log.md#d-068--direction-of-the-rebuild-order-direct-path-questions-sources-memory-2026-11-24))
and is protected until then:

- **Signed-in users only:** `[Authorize]` on the hub; `MapRunHub()` (`LiveUpdateServiceExtensions`) maps it
  with `RequireAuthorization()` and `CloseOnAuthenticationExpiration`, so anonymous connections are
  rejected and a connection is closed when the sign-in it was opened with expires.
- **Server-side groups:** group names come from `Context.User`, never from a parameter. Joining a run
  group requires owning the run (or being an administrator); otherwise the hub throws a `HubException`.
  The all-users list and dashboard groups are for administrators only. The former methods that took a
  username are gone.

### `IRunNotifier` / `LiveRunNotifier`

`IRunNotifier` lives in Core (`Core/Notifications/`). `PostgresEventSink` (Infrastructure) consumes the contract — without a Web dependency. `LiveRunNotifier` (`Web/Notifications/`) lives in Web, singleton lifetime, and publishes every update twice: into `IRunUpdateFeed` for the Blazor pages and into the `RunHub` groups (until the hub is removed in stage 8). It looks up the run's owner itself (`IRunRepository`, cached) and sends list updates only to the owner and to administrators, never to everyone. Notifier calls are best-effort (`try/catch`).

**Sequence user-submit → live view:**

```
Browser /new  →  IRunService.SubmitRunAsync  →  RunEntity (Pending) in DB
                                                ↓
                                      RunOrchestratorService (BackgroundService)
                                        polls Pending runs with a snapshot, sets the Running claim
                                                ↓
                                      Geef SDK pipeline (Grounding → Execution → Evaluation → Finalize)
                                                ↓
                                      PostgresEventSink
                                        (a) writes the event to the DB
                                        (b) IRunNotifier.NotifyRunUpdatedAsync
                                                ↓
                                      LiveRunNotifier
                                        → IRunUpdateFeed: run topic, owner list topic, admin list topic
                                        → RunHub groups (until removal in stage 8)
                                                ↓
                                      Page callback (WatchRunAsync / WatchRunList)
                                        → IRunService.GetRunAsync → StateHasChanged
```

### UI component library (`Components/UI/`)

9 components, all with scoped `.razor.css`:
`StatusBadge`, `SeverityBadge`, `RunCard`, `IterationPanel`, `FindingItem`, `RunHeader`, `SubmitForm`, `EmptyState`, `CancelButton`.

**Workflow rule:** semantic UI elements (buttons, forms, badges, lists) are components. Layout `div` tags in pages are allowed.

### PS-6 crew-management pages

| URL | Component | Description |
|---|---|---|
| `/crew` | `CrewIndex` | Landing page with an overview of templates + profiles |
| `/crew/templates` | `CrewTemplatesIndex` | List of all templates (system + custom) |
| `/crew/templates/new` | `CrewTemplateEditor` | Create a new template |
| `/crew/templates/{name}` | `CrewTemplateEditor` | Edit a template / duplicate a system template |
| `/crew/profiles/reviewers` | `ReviewerProfilesIndex` | List of all reviewer profiles |
| `/crew/profiles/reviewers/new` | `ReviewerProfileEditor` | Create a new reviewer profile |
| `/crew/profiles/reviewers/{name}` | `ReviewerProfileEditor` | Edit a reviewer profile |
| `/crew/profiles/executors` | `ExecutorProfilesIndex` | List of all executor profiles |
| `/crew/profiles/executors/new` | `ExecutorProfileEditor` | Create a new executor profile |
| `/crew/profiles/executors/{name}` | `ExecutorProfileEditor` | Edit an executor profile |
| `/crew/learnings` | `LearningsIndex` | List, filter, approve, reject, and delete learning entries from the learning store (D-054) |

New UI components (PS-6): `CrewBadge`, `CrewSelector`, `CrewSummary`, `ReviewerPicker`, `ProfileEditorForm`, `Modal`, `DeleteConfirmationModal`.

## Frontend stack decision

**Blazor Server.** Rationale: the same .NET stack as the Geef SDK, no context switch; SignalR is built in and feeds the live status practically for free; single-user means no scaling worries; local UI latency is uncritical thanks to server hosting and the reverse proxy. Should a switch to Blazor WebAssembly or React+API become necessary later, the backend (`IRunService`, MCP server, pipeline) stays unchanged.

## Auth strategy (implemented in step 8, see D-021)

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

**RunHub requires sign-in (since D-067):**

Until D-067, `RunHub` had no `[Authorize]` because the Blazor pages connected to it server-side without the
browser cookie. The pages now use the in-process `IRunUpdateFeed`, so the hub requires sign-in
(`[Authorize]`, `MapRunHub()` with `RequireAuthorization()` and `CloseOnAuthenticationExpiration`); see
[Live updates](#live-updates-irunupdatefeed-and-runhub-d-067).

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
OAuth runs belong to the authorizing user, static-token runs to the admin.
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

Traefik terminates TLS and forwards HTTP (port 8080) to the container. The container itself exposes no port to the outside (`ports:` is omitted in the production compose).

### TLS and Traefik configuration

| Parameter | Value |
|---|---|
| External network | `proxy` (server convention) |
| Cert resolver | `le` (HTTP challenge via the `web` entry point) |
| Entry point (HTTPS) | `websecure` |
| HTTP→HTTPS redirect | Global in `traefik.yml` (no app-side redirect router) |
| Middleware chain | `chain@file` (secure-headers + compression + rate-limit, server convention) |
| `traefik.docker.network` | `proxy` (mandatory when a container is in multiple networks) |

### Cookie SecurePolicy in production

`ASPNETCORE_ENVIRONMENT=Production` enables `CookieSecurePolicy.Always`. The `ForwardedHeaders` middleware (placed before `UseAuthentication`, see the auth-strategy section) reads `X-Forwarded-Proto=https` from Traefik and ensures `Request.IsHttps == true` — without this, `SecurePolicy.Always` would block cookies because the container sees HTTP, not HTTPS.

### Multi-auth over HTTPS

Cookie auth for the web UI and bearer auth for the MCP endpoint (`/mcp`) work identically over HTTPS: TLS is terminated at Traefik, the container receives HTTP. The application's auth layer sees no difference from dev operation — only `SecurePolicy.Always` and `SameSite=Strict` are active in production.

### Postgres strategy (server convention)

Every application on this server runs its own Postgres container in the same compose file (the `own-Postgres-per-app` pattern). No shared database host — isolation and easy per-app backup.

### Backup strategy (post-skeleton step 1)

The `postgres-backup` service (`prodrigestivill/postgres-backup-local:16`) runs as a third container in the production stack:

- **Schedule:** daily at 03:00 UTC (`0 3 * * *`)
- **Retention:** 7 daily, 4 weekly, 6 monthly snapshots
- **Volume:** `geef-atelier-backups` (named volume, independent of the DB volume)
- **Format:** `.sql.gz` (gzip-compressed pg_dump SQL, compression level 6)
- **Restore:** `scripts/restore-backup.sh <file.sql.gz>` (stops `web`, restores, restarts `web`)

No app-code intervention — a pure compose service. The backup container only needs the internal `geef-atelier-network` (no `proxy`).

### Reviewer calibration

The severity taxonomy, the anti-pattern rule and the convergence-policy strategy are described in [`docs/06-reviewer-calibration.md`](06-reviewer-calibration.md). New reviewer roles must adopt the standard defined there.

### Deployment procedure

1. Generate the `.env` file via `openssl rand` + `tools/HashPassword` (gitignored, never into the repo).
2. `docker build --no-cache -t geef-atelier .` in the `build/` directory.
3. `docker compose -f docker-compose.prod.yml up -d` starts app + Postgres; auto-migration on startup (D-010) runs on first start.
4. Traefik detects the container via Docker labels, issues the Let's Encrypt certificate.
5. After every deploy, run the mini run `scripts/atelier-smoke.sh` ([`ops/atelier-smoke.md`](ops/atelier-smoke.md)); a failed migration already stops the start (D-067).

The nightly cli-proxy self-update (`scripts/cli-proxy-selfupdate.sh`) builds only from the committed state; with uncommitted changes under `cli-proxy/` it pauses and sends an alarm instead of building (D-067).

### Model catalog for CLI providers (D-064)

CLI providers do not carry their model list in the Atelier. `ModelCatalog` reads it from the
cli-proxy through `ICliModelSource` / `CliProxyModelSource`, which calls the single provider-keyed
endpoint `GET /v1/cli/{providerName}/models`. The gateway reports where the list came from —
`x_source` is `live`, `static` (no live source by design, e.g. gemini) or `degraded` (a live source
exists but could not be reached) — and, for claude, an `x_aliases` map from `claude-<family>-latest`
to the concrete model the alias currently resolves to.

Three consequences shape the surrounding code:

* **Provenance is three-state, not a boolean.** `ModelCatalogSource` distinguishes `Live`,
  `StaticByDesign` and `Fallback`, so the picker can stay truthful for a provider that never had a
  live source instead of permanently claiming it is unreachable.
* **The live list is merged with the curated one**, never replaced, so a preferred composer model
  cannot silently drop out of the catalog it is filtered against.
* **Profiles hold `*-latest` aliases, run snapshots hold concrete ids.** The alias is resolved once
  per snapshot in `CrewService.ApplyPacksAsync`, so new runs follow new releases while a resumed run
  stays on the model its parent used.

Caching: 24 h for a successful CLI lookup (matching the gateway's own cache), 1 h for HTTP providers,
5 minutes for any failure. `ModelCatalogWarmUpService` warms every active CLI provider shortly after
start-up and again nightly; `ModelCatalogOptions.CliLiveCatalogEnabled` is the kill switch back to
configured lists without a redeploy.

## Observability

Geef brings a lot built in:
- `IGeefEventSink` for structured events → the custom sink writes to the DB and SignalR
- `System.Diagnostics.ActivitySource("Geef.Sdk")` for distributed tracing → can later be connected to an OpenTelemetry collector
- A middleware pipeline for cross-cutting concerns (timeout, exception handling, tracing) — all built-in middleware is used in the skeleton

Logging via `Microsoft.Extensions.Logging` with a console sink in the skeleton; structured logs (Serilog with a Postgres sink) are a later option.
