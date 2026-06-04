# 08 — Crew system (PS-5)

*[Deutsch](08-crew-system_de.md) · **English***

Last updated: 2026-06-04 (D-059: Auto-Crew — Composition-Run section added)

## Overview

The crew system replaces the three-member crew hard-coded in PS-2 (executor + BriefingTreueReviewer + KlarheitReviewer) with a configurable profile and template system. On submission every run receives a fully embedded **CrewSnapshot** that guarantees the run's reproducibility even if profiles are changed or deleted later.

## Core concepts

| Term | Meaning |
|---|---|
| **ExecutorProfile** | The LLM actor that creates the draft. Carries system prompt, provider, model, MaxTokens. |
| **ReviewerProfile** | The LLM actor that assesses the draft. Same fields. `Priority` for sequential strategies via `IReviewer.Priority`. |
| **CrewTemplate** | Composes executor + reviewers + EvaluationStrategy + an optional ConvergenceOverride + advisor profiles. |
| **CrewSnapshot** | A fully embedded copy of the CrewTemplate (incl. all profile data) at run-submission time. Persisted as JSONB in `Runs.CrewSnapshot`. |
| **AdvisorProfile** | An LLM actor for consultative passes before or after execution. Carries `AdvisorMode` + `AdvisorTrigger`. Functional from PS-7. |
| **FinalizerProfile** | A post-processing actor that runs after the GEEF convergence loop. Carries `FinalizerType` + typed settings. Produces `RunArtifact` records. Functional from Step22 (D-044). |

## EvaluationStrategies

| Enum value | SDK class | Behaviour |
|---|---|---|
| `Parallel` | `ParallelEvaluationStrategy` | All reviewers in parallel, all findings collected. Default. |
| `Sequential` | `SequentialEvaluationStrategy` | Reviewers one after another in list order, all awaited. |
| `FailFast` | `FailFastEvaluationStrategy` | Like Sequential, aborts after the first critical finding. |
| `Priority` | `PriorityOrderedEvaluationStrategy` | Reviewers in `Priority` order (not list order). |

**Note:** with `Parallel` the order in `ReviewerProfileNames` is only documentary. With `Sequential` and `Priority` it is significant.

## System profiles (code constants)

Defined in `Geef.Atelier.Core.Domain.Crew.SystemCrew` (read-only, versioned with the code):

Providers/models as of May 2026 (after the switch to the subscription CLIs,
D-027/D-032): the executor and the Anthropic reviewer run via `claude-cli`, the other
reviewers via `codex-cli`. Model pluralism is preserved (reviewer ≠ executor model).

| Name | Type | Provider / Model |
|---|---|---|
| `default-executor` | ExecutorProfile | `claude-cli` / `claude-opus-4-7` |
| `briefing-fidelity` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `clarity` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `legal-jargon-precision` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `legal-clause-risk` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `academic-citation-readiness` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `academic-argumentation-rigor` | ReviewerProfile | `claude-cli` / `claude-opus-4-7` |
| `marketing-audience-clarity` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `marketing-conversion-strength` | ReviewerProfile | `codex-cli` / `gpt-5.5` |

**System templates** (four): `klassik` (evaluation `Parallel`, no advisors —
reproduces the original PS-2 behaviour) plus the domain templates
`juristisch` (`Sequential`, advisor `legal-domain-expert`),
`akademisch` (`Sequential`, advisor `academic-rigor-advisor`) and
`marketing` (`Parallel`, no advisors).

All four standard templates ship with three default grounding providers (`tavily-basic`, `run-attachments`, `learning-retriever-default`) and one default finalizer (`learning-extractor`). The `learning-evaluation` template is the only exception — it carries no grounding providers and no `learning-extractor` (recursion stop).

## Custom profiles

- Stored in the DB (`ReviewerProfiles`, `ExecutorProfiles`, `CrewTemplates`).
- The name automatically receives the prefix `"custom-"` (idempotent, no double prefix).
- System profiles are read-only: update/delete throws `InvalidOperationException("System profile is read-only — copy it as a custom variant.")`.
- API: `ICrewService.CreateCustomReviewerProfileAsync(profile)`.

## CrewSnapshot format (SchemaVersion 1)

> The following example shows the **structure**. The `provider`/`model` values are
> illustrative — the currently valid system values are in the "System profiles"
> table above; a real snapshot contains the values valid at submit time.

```json
{
  "schemaVersion": 1,
  "templateName": "klassik",
  "executor": {
    "name": "default-executor",
    "displayName": "Default Executor",
    "systemPrompt": "...",
    "provider": "openrouter",
    "model": "anthropic/claude-opus-4.7",
    "maxTokens": null,
    "isSystem": true
  },
  "reviewers": [
    { "name": "briefing-fidelity", "provider": "openrouter", "model": "google/gemini-2.5-flash", ... },
    { "name": "clarity",           "provider": "openrouter", "model": "openai/gpt-5.5-mini",    ... }
  ],
  "evaluationStrategy": "Parallel",
  "convergenceOverride": null,
  "advisors": []
}
```

Serialized with `JsonNamingPolicy.CamelCase`. Stored in `Runs.CrewSnapshot` (JSONB).

## Advisor passes (PS-7)

Advisors are consultative LLM actors run at defined points in the pipeline. Their output flows as a marked context block into the run — the executor and subsequent reviewers see it without the Geef SDK core having to be modified.

### AdvisorProfile schema

```csharp
public sealed record AdvisorProfile(
    string Name, string DisplayName, string Description,
    string SystemPrompt, string Provider, string Model, int? MaxTokens,
    AdvisorMode Mode, AdvisorTrigger Trigger, bool IsSystem);

public enum AdvisorMode    { Strategic, Critical, DevilsAdvocate, DomainExpert }
public enum AdvisorTrigger { BeforeFirstExecution, BeforeEveryExecution, OnConvergenceFailure }
```

### Trigger types

| Trigger | Meaning |
|---|---|
| `BeforeFirstExecution` | The advisor is consulted once before iteration 1. Suitable for strategic briefing analysis. |
| `BeforeEveryExecution` | The advisor is consulted before every iteration. Suitable for critical counter-voices. |
| `OnConvergenceFailure` | The advisor is consulted only on a convergence failure; a single retry run follows afterwards. |

### System advisors

Provider/model as of May 2026: all system advisors run via
`claude-cli` / `claude-opus-4-7`.

| Name | Mode | Trigger | Purpose |
|---|---|---|---|
| `briefing-clarifier` | Strategic | BeforeFirstExecution | Analyzes the briefing before the first executor pass and delivers structured clarification hints. |
| `devils-advocate` | DevilsAdvocate | BeforeEveryExecution | Critically questions the planned executor direction before every iteration, to avoid errors through blind progress. |
| `legal-domain-expert` | DomainExpert | BeforeFirstExecution | Domain input for legal texts (template `juristisch`). |
| `academic-rigor-advisor` | Critical | BeforeEveryExecution | Scientific rigor/argumentation quality (template `akademisch`). |

### Pipeline integration via decorator

The `AdvisorAwareExecutor` (in `Infrastructure/Pipeline/`) decorates `IExecutionStep` and slots transparently in front of every executor call:

```
AdvisorAwareExecutor.ExecuteAsync(context)
  1. Filters advisors by the active trigger (BeforeFirst only at iteration 1, BeforeEvery always)
  2. Calls ProfileBasedAdvisor sequentially for each matching advisor
  3. Writes the output as "[ADVISOR: <name>]\n<text>" into context[AtelierContextKeys.AdvisorBlock]
  4. Persists an AdvisorConsultation record (table AdvisorConsultations)
  5. Delegates to the real IExecutionStep
```

`AtelierPipelineFactory.BuildWithAdvisorContext(snapshot, context)` wires the decorator and ensures the advisor block is propagated in the `IRunContext`.

### Advisor-failure behaviour

Advisor LLM calls are not best-effort. An exception in `ProfileBasedAdvisor` bubbles through `AdvisorAwareExecutor` and aborts the run with `Status=Failed` (D-031(c)). Silently continuing would mask a possibly corrupted context.

### Convergence-failure retry mechanism

```
Pipeline → ConvergenceFailedException
  → RunOrchestratorService.TryConvergenceFailureRetryAsync
      1. Checks RunEntity.AdvisorRetryAttempted — true → escalates to Failed (no second retry)
      2. Sets AdvisorRetryAttempted = true in the DB
      3. Enables OnConvergenceFailure advisors in the next run context
      4. Restarts the pipeline run (once)
      5. A second ConvergenceFailedException → Failed (no further retry)
```

**Single-retry cap:** `RunEntity.AdvisorRetryAttempted` (migration Step11) prevents infinite loops. Multi-retry with a configurable retry count is documented as future work.

### DB tables (migration Step11AdvisorSystem)

| Table | Content |
|---|---|
| `AdvisorProfiles` | Custom advisor profiles (system advisors live as code constants in `SystemCrew`). |
| `AdvisorConsultations` | Persisted advisor outputs per iteration and advisor (RunId, IterationNumber, AdvisorName, OutputText, CreatedAt). |

Column `RunEntity.AdvisorRetryAttempted` (bool, nullable) on the `Runs` table.

### UI components (PS-7)

| Component | Purpose |
|---|---|
| `AdvisorPicker` | Available/selected list analogous to `ReviewerPicker`, with a trigger indicator |
| `AdvisorConsultationsBlock` | Collapsible section on the RunDetail page: shows all consultations per iteration |
| `AdvisorProfilesIndex` | List of all advisor profiles (system + custom) at `/crew/profiles/advisors` |
| `AdvisorProfileEditor` | CRUD editor for custom advisor profiles |

`ProfileEditorForm` was extended with `ShowAdvisorFields` + mode/trigger radio groups (reusable for reviewer, executor and advisor).

### MCP tool

`list_advisor_profiles` — lists all advisor profiles (system + custom).

## Finalizer Profiles (Step22 / D-044)

Finalizers are post-processing actors that run **after** the GEEF convergence loop has completed (or, optionally, when it fails). They transform or export the final draft and produce `RunArtifact` records.

### FinalizerProfile schema

```csharp
public sealed record FinalizerProfile(
    string Name, string DisplayName, string Description,
    FinalizerType FinalizerType, Dictionary<string, string> Settings,
    bool IsSystem, DateTime CreatedAt, DateTime UpdatedAt);

public enum FinalizerType
{
    FileExport    = 0,
    MetadataEnrich = 1,
    ExternalSink  = 2,
    Transform     = 3,
}
```

`FinalizerType` is **immutable after creation**. Typed settings records (`FileExportSettings`, `MetadataEnrichSettings`, `WebhookSinkSettings`, `EmailSinkSettings`, `TransformSettings`) wrap the `Dictionary<string,string> Settings` for type-safe access.

### Pipeline position

Finalizers run sequentially in the order defined by `CrewTemplate.FinalizerProfileNames` after the convergence loop exits. The flag `CrewTemplate.RunFinalizersOnMaxAttempts` controls whether finalizers also execute when convergence fails (max attempts exceeded).

### RunArtifact entity

Each finalizer execution records its output as a `RunArtifact`:

| Field | Type | Description |
|---|---|---|
| `Id` | Guid | Primary key |
| `RunId` | Guid | FK → `Runs` |
| `FinalizerProfileName` | string | Name of the finalizer that produced this artifact |
| `ArtifactType` | enum `{File, Url, Status}` | How the artifact is stored |
| `Filename` | string? | File name (for `File` artifacts) |
| `ContentType` | string? | MIME type |
| `SizeBytes` | long? | File size in bytes |
| `StorageUri` | string | Storage path or URL |
| `StatusMessage` | string? | Human-readable status (for `Status` artifacts) |
| `CreatedAt` | DateTime | Creation timestamp |

### System finalizer profiles (17)

| Name | Type | Description |
|---|---|---|
| `export-markdown` | FileExport | Exports the final draft as a Markdown file |
| `export-html` | FileExport | Exports the final draft as an HTML file |
| `export-pdf` | FileExport | Exports the final draft as a PDF file |
| `export-docx` | FileExport | Exports the final draft as a DOCX file |
| `export-txt` | FileExport | Exports the final draft as a plain-text file |
| `export-json` | FileExport | Exports the run result as a structured JSON file |
| `add-front-matter` | MetadataEnrich | Prepends YAML front-matter with run metadata |
| `add-word-count-footer` | MetadataEnrich | Appends a word-count footer to the draft |
| `add-reading-level` | MetadataEnrich | Appends a Flesch–Kincaid reading-level annotation |
| `webhook-sink` | ExternalSink | POSTs the artifact payload to a configured webhook URL |
| `email-sink` | ExternalSink | Sends the artifact as an e-mail attachment |
| `anti-ai-voice` | Transform | Rewrites the draft to reduce detectable AI phrasing |
| `tone-formalization` | Transform | Elevates the draft's register to formal/academic tone |
| `tone-casual` | Transform | Lowers the draft's register to conversational tone |
| `executive-summary` | Transform | Produces a concise executive-summary prepended to the draft |
| `key-takeaways` | Transform | Appends a bullet-point key-takeaways section |
| `glossary` | Transform | Appends a glossary of domain-specific terms |

### LLM-Binding bei Transform-Finalizern

Transform-Finalizer führen einen LLM-Call aus, um den finalen Draft zu transformieren. Das gebundene Modell kann pro Profil konfiguriert werden:

- **Anbieter:** Jeder aktive Custom- oder System-Provider (HTTP oder CLI)
- **Modell:** Frei wählbar; für Tone-Transformationen reichen günstige Modelle (z.B. `gpt-4o-mini`)
- **MaxTokens:** Maximale Ausgabelänge (Mindest-Floor von 10000 gilt)
- **Temperature** (optional): leer = Anbieter-Standard, 0.0 = deterministisch, 2.0 = sehr kreativ

**System-Transform-Finalizer** (`anti-ai-voice`, `tone-formalization`, `tone-casual`, `executive-summary`, `key-takeaways`, `glossary`) sind read-only. Ihre Binding-Einstellungen können durch Klonen als Custom-Profil überschrieben werden.

Das `LlmBinding`-Konzept wird in Step 2 (Grounding-Refinement) und Step 3 (KI-Grounding-Typen) wiederverwendet.

### DB tables (migration Step22)

| Table | Content |
|---|---|
| `FinalizerProfiles` | Custom finalizer profiles (system profiles live as code constants in `SystemCrew`). |
| `RunArtifacts` | One row per finalizer output per run (see RunArtifact entity above). |
| `FinalizationActorCosts` | Per-run, per-finalizer cost records for LLM-backed transforms. |

New columns added to existing tables:

| Table | Column | Type | Description |
|---|---|---|---|
| `CrewTemplates` | `FinalizerProfileNames` | JSONB | Ordered list of finalizer profile names |
| `CrewTemplates` | `RunFinalizersOnMaxAttempts` | boolean | Run finalizers even when convergence fails |
| `Runs` | `FinalizerCostEur` | numeric | Total finalizer LLM cost for this run |
| `Runs` | `FinalizerErrorMessage` | text | Error message if any finalizer failed |

### UI components (Step22)

| Component | Purpose |
|---|---|
| `FinalizerPicker` | Available/selected list for finalizer profiles in `CrewTemplateEditor` |
| `FinalizerProfilesIndex` | List of all finalizer profiles (system + custom) at `/crew/profiles/finalizers` |
| `FinalizerProfileEditor` | CRUD editor for custom finalizer profiles |
| `FinalizerProfileView` | Read-only view for system finalizer profiles |
| `RunArtifactsTable` | Collapsible artifacts section on the RunDetail page |

`CrewTemplateEditor` was extended with a `FinalizerPicker` and the `RunFinalizersOnMaxAttempts` toggle.

### MCP tools (Step22)

- `list_run_artifacts` — lists all artifacts produced for a given run.
- `download_run_artifact` — downloads a specific artifact (owner-check + path-containment enforced).

## API paths

### Template-based submit (default)

```csharp
await runService.SubmitRunAsync(
    briefingText: "...",
    configJson:   "{}",
    crewTemplateName: "klassik");  // null → default "klassik"
```

### Custom-crew submit

```csharp
var spec = new CrewSpec(
    ExecutorProfileName:  "custom-my-executor",
    ReviewerProfileNames: ["briefing-fidelity", "custom-my-reviewer"],
    EvaluationStrategy:   EvaluationStrategy.Sequential,
    ConvergenceOverride:  new ConvergencePolicyOverride(MaxIterations: 3, null, null, null));

await runService.SubmitRunAsync("...", "{}", customCrew: spec);
```

### MCP tools

- `list_crew_templates` — lists all templates (system + custom).
- `list_reviewer_profiles` — lists all reviewer profiles (system + custom).
- `list_advisor_profiles` — lists all advisor profiles (system + custom).
- `list_grounding_provider_profiles` — lists all grounding-provider profiles.
- `list_run_artifacts` — lists all artifacts produced for a given run.
- `download_run_artifact` — downloads a specific run artifact (owner-check + path-containment enforced).
- `submit_request` — extended with `crew_template` and `custom_crew` (JSON string).

Full tool list (15 tools): see [09-endpoint-reference.md](09-endpoint-reference.md) and the [project README](../README.md).

## Grounding Provider Profiles (D-036 / D-040 / D-051)

Grounding providers enrich the briefing with external context before the GEEF execution loop.

### Provider Types

| Type | Implementation | Description | Settings |
|---|---|---|---|
| `tavily` | `TavilyGroundingProvider` | Web search via Tavily API (basic or advanced). API key per profile. | `Tier` (basic/advanced), `MaxResults`, `IncludeAnswer` |
| `vector-store` | `VectorStoreGroundingProvider` | Semantic search in a pgvector collection. Scope: `global`, `run-local`, or `both`. | `TopK`, `Scope`, `TagFilter` |
| `static-context` | `StaticContextGroundingProvider` | Curated fixed text injected unchanged at every run. No external API. Ideal for style guides, glossaries, brand voice. | `label`, `content` (max 200,000 chars, soft limit 50,000) |
| `url-fetch` | `UrlFetchGroundingProvider` | Fetches specific URLs, cleans HTML via HtmlAgilityPack, returns text content. SSRF guard blocks private IPs. | `urls` (newline-separated), `maxContentPerUrl` (default 8000), `stripBoilerplate` (bool, default true) |
| `news-search` | `NewsSearchGroundingProvider` | Tavily API with `topic=news` + `days` filter. For time-critical topics. Attribution via `PublishedDate`. | `recencyDays` (default 7), `newsMaxResults` (default 5), `newsSearchDepth` (basic/advanced) |

### SSRF Protection (`url-fetch`)

The `UrlSafetyValidator` component runs before every HTTP request for the `url-fetch` provider:

- **Schema check:** Only `http` and `https`. All others (`file://`, `ftp://`, custom schemes) → blocked.
- **DNS resolution:** Every hostname is resolved via `Dns.GetHostAddressesAsync`; **all** resulting IPs are checked (not just the first).
- **IPv4 blocklist:** `10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`, `127.0.0.0/8`, `169.254.0.0/16` (link-local + cloud metadata), `0.0.0.0/8`, `100.64.0.0/10`, `224.0.0.0/4`
- **IPv6 blocklist:** `::1`, `fc00::/7`, `fe80::/10`, `ff00::/8`, `64:ff9b::/96`, IPv4-mapped IPv6 (unwrapped and re-checked)
- **Redirect chain:** Max 3 hops; each redirect target IP is re-validated against the full blocklist.
- **Timeout:** 10 seconds hard cap per request; response body capped at 5 MB.

Blocked or failed URLs are skipped (warning logged, noted in the source citation); the run itself is not aborted.

### AI Refinement

Every grounding provider can optionally be configured with an AI refinement pass. After the fetch, an LLM processes the raw results — if configured.

**Configuration** (flat keys in `ProviderSettings`):

| Key | Type | Description |
|---|---|---|
| `refinementProvider` | string | LLM provider (e.g. `openrouter`) |
| `refinementModel` | string | Model (e.g. `google/gemini-2.0-flash-lite`) |
| `refinementMaxTokens` | int | Max tokens for the refinement response |
| `refinementTemperature` | double? | Optional; empty = provider default |
| `refinementMode` | int | `0` = Filter, `1` = Synthesize |
| `refinementInstructions` | string? | Optional additional instructions |

**Modes:**
- **Filter** (default): Each source is kept or discarded individually. Attribution remains 1:1.
- **Synthesize:** All sources are merged into a coherent text (`[n]` references). Original sources are preserved as a reference appendix.

**Graceful degradation:** If the refinement provider is inactive or the LLM call fails, raw results are passed through unchanged. The run is not aborted. The grounding visualization shows a notice.

**System profile `tavily-news`:** New — Tavily news search (`topic=news`, `recencyDays=7`) with filter refinement via `google/gemini-2.0-flash-lite`. Suitable for time-critical topics.

**System profile `tavily-refined`:** Ready-to-use demo profile — Tavily Advanced with filter refinement via `google/gemini-2.0-flash-lite`.

## Reviewer-name migration

| Old (pre-PS-5) | New (PS-5) |
|---|---|
| `BriefingTreueReviewer` | `briefing-fidelity` |
| `KlarheitReviewer` | `clarity` |

Migration Step10 renames historical `Findings.ReviewerName` values. `ReviewerDisplay.ToDisplay()` contains both variants as a fallback.

## System separation (namespace)

- `Core/Domain/Crew/` — all domain records (no infrastructure dependency).
- `Core/Domain/Crew/SystemPrompts.cs` — system-prompt texts (long, semantically belong to the system profiles).
- `Infrastructure/Pipeline/ProfileBasedReviewer.cs` / `ProfileBasedExecutor.cs` — Geef SDK adapters.
- `Application/Crew/CrewService.cs` + `CrewSnapshotBuilder.cs` — orchestrates repo lookups + snapshot construction.

## PS-6 — UI paths and conventions

### Routing map

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
| `/crew/profiles/advisors` | `AdvisorProfilesIndex` | List of all advisor profiles (system + custom) |
| `/crew/profiles/advisors/new` | `AdvisorProfileEditor` | Create a new advisor profile |
| `/crew/profiles/advisors/{name}` | `AdvisorProfileEditor` | Edit an advisor profile |
| `/crew/profiles/grounding-providers` | `GroundingProviderIndex` | List of all grounding-provider profiles |
| `/crew/profiles/finalizers` | `FinalizerProfilesIndex` | List of all finalizer profiles (system + custom) |
| `/crew/profiles/finalizers/create` | `FinalizerProfileEditor` | Create a custom finalizer profile |
| `/crew/profiles/finalizers/edit/{name}` | `FinalizerProfileEditor` | Edit a custom finalizer profile |
| `/crew/profiles/finalizers/view/{name}` | `FinalizerProfileView` | View a system finalizer profile (read-only) |
| `/crew/studio` | `TemplateStudio` | AI-assisted template wizard (analyze → review → edit → materialize) |

### UI components

| Component | Location | Purpose |
|---|---|---|
| `CrewBadge` | `Components/UI/` | Subtle text badge with the template name in RunRow |
| `CrewSelector` | `Components/UI/` | Dropdown for template selection on the NewRun page |
| `CrewSummary` | `Components/UI/` | Click-to-expand crew overview on the RunDetail page |
| `ReviewerPicker` | `Components/UI/` | Available/selected list with up/down reordering |
| `ProfileEditorForm` | `Components/UI/` | Generic form for reviewer and executor profiles |
| `Modal` | `Components/UI/` | Generic modal component with a backdrop |
| `DeleteConfirmationModal` | `Components/UI/` | Confirmation modal: the user must type the name |

### Name constraints

Pattern `^[a-z0-9\-]+$`, max 64 characters — applies to all profile and template names (custom prefix excluded). Form validation via `DataAnnotations.RegularExpression`. The service layer is idempotent regarding the `custom-` prefix.

## Template Studio (D-043)

The Template Studio at `/crew/studio` is an AI-assisted wizard that proposes a complete crew configuration for a described task and lets the user review and edit every field before materializing to the DB.

### Wizard steps

| Step | Component | Description |
|---|---|---|
| TaskInput | `StudioTaskInputStep` | Free-text task description; triggers LLM analysis |
| Analyzing | `StudioAnalyzingStep` | Loading indicator while the meta-LLM runs |
| Review | `StudioReviewStep` | Shows the AI proposal; option to use an existing template instead |
| Edit | `StudioEditStep` | Full-field editor for the proposed template and all profiles |
| Confirmation | `StudioConfirmationStep` | Shows materialization result; launches a run |

### Analysis pipeline (`AnalyzeAsync`)

`TemplateStudioService.AnalyzeAsync` turns the free-text task description into a persisted `TemplateStudioAnalysis` that drives the Review and Edit steps. The pipeline:

1. **Model resolution** — picks the meta-LLM in the order: explicit per-analysis override → persisted Studio default (`StudioSettings`) → `appsettings` default (`TemplateStudioOptions`).
2. **Context assembly (parallel, budgeted)** — loads all crew lists (templates, executors, reviewers, advisors, grounding providers, finalizers) and fetches every provider's model catalogue **in parallel** under a 20-second budget. A slow or unreachable provider contributes no models instead of stalling the whole analysis; recommended models are listed first so the LLM picks valid IDs.
3. **Meta-LLM call (timeout-capped)** — calls the model with the `submit_template_proposal` tool forced via `tool_choice`. A hard cap (`TemplateStudioOptions.AnalysisTimeoutSeconds`) turns a stalled provider into a `TimeoutException` with a retry hint. A response without a tool call raises an `InvalidOperationException`.
4. **Proposal parsing** — `ParseProposal` reads the tool-call JSON into `MatchedExistingTemplates`, a `StudioRecommendation` (`use_existing` / `adapt_existing` / `create_new`), the `ProposedTemplate`, and the list of `ProposedProfile` records (each carrying per-field LLM reasoning).
5. **Defaults & clamping** — `ApplyDefaults` fills empty provider/model/MaxTokens per profile type from `StudioDefaults`. `ClampMaxTokens` enforces the `MinMaxTokens` floor for generating profiles (Executor, Reviewer, Advisor) and sets `null` for grounding/finalizer profiles (no own LLM generation).
6. **Deduplication** — `ProfileSimilarityService.FindSimilarAsync` drops proposed profiles that are too close to an existing one (similarity over name + prompt, `TemplateStudioOptions.SimilarityThreshold`).
7. **Cost & persistence** — input/output tokens are priced via `IPricingCatalog`; the full analysis (incl. cost in EUR) is stored through `ITemplateStudioAnalysisRepository` and returned. `ListRecentAnalysesAsync` exposes the history (see `StudioAnalysisHistoryList`).

### StudioEditStep field parity (D-043)

The Edit step exposes the full field set for the template and every profile slot:

**Template fields:** DisplayName, Description, EvaluationStrategy (dropdown), EvaluationStrategyReasoning (read-only, from LLM)

**Per profile slot (Executor / Reviewer × N / Advisor × N / GroundingProvider × N / Finalizer × N):**
- **UseExisting / CreateNew toggle** — pick an existing profile by name, or configure a new one inline
- **CreateNew fields:** Name (kebab-case), DisplayName, Description, Provider, Model (`ModelSelector`), MaxTokens, SystemPrompt
- **Reviewer-specific:** ReviewerFocus (optional)
- **Advisor-specific:** AdvisorMode (Strategic / Critical / DevilsAdvocate), AdvisorTrigger (BeforeFirstExecution / BeforeEveryExecution / OnConvergenceFailure)
- **GroundingProvider-specific:** GroundingProviderType (Tavily / VectorStore), type-specific settings (API key or collection name)
- **Finalizer-specific:** FinalizerType (FileExport / MetadataEnrich / ExternalSink / Transform), type-specific settings
- **Reasoning display:** LLM reasoning per field, read-only (from `analyze_template_proposal`)
- **Field-Helps:** inline German help texts for every field (`StudioFieldHelps.cs`)

### Key components

| Component | Purpose |
|---|---|
| `StudioProfileSlot.razor` | UseExisting/CreateNew toggle + full inline profile form; embeds `ModelSelector` |
| `FieldHelp.razor` | Inline hint rendered below every field |
| `StudioFieldHelps.cs` | Central German-language help-text constants |

### Materialization (atomic, D-043/7)

`TemplateStudioService.MaterializeAsync` wraps all DB writes in a single EF Core transaction (`IAtomicTransactionFactory`) and returns a `MaterializationResult` (final template name, created profile names, warnings).

**Pre-transaction validation:**
- `ValidateNotSystemProfiles` — proposed profile names must not collide with system-reserved names.
- `ValidateReviewerCount` — the template must carry at least one reviewer.
- `ValidateAvailabilityAsync` — checks each generating profile's model against the provider catalogue; a mismatch produces a **non-blocking warning** (the model may simply be missing from the live catalogue), not an abort.

**Transaction order:** begin → create profiles (Executor, Reviewer, Advisor, GroundingProvider, Finalizer) via `CreateProfileAsync` → create template via `CreateTemplateAsync` → `MarkMaterializedAsync` (marks the analysis record as consumed) → commit. Any error triggers an explicit rollback — no half-materialized state.

**Name mapping:** `CreateCustom*Async` idempotently prefixes every new profile name with `custom-`. `MaterializeAsync` records the old→final mapping and `ApplyProfileNameMapping` rewrites all template references (executor, reviewers, advisors, grounding providers, finalizers) to the actually stored names before the template is created. Names already referring to existing profiles pass through unchanged. The evaluation strategy is normalized (`NormalizeEvaluationStrategy`, default `Sequential`).

Finalizer proposals appear in the Studio's LLM analysis output; `CreateProfileAsync` handles the finalizer branch; `StudioEditStep` exposes the finalizer slot section alongside the other profile slots.

## Continuous Learning Loop (D-054)

### Architecture

```
NORMAL RUN  →  Finalizer "learning-extractor" (opt-in)
   ├─ Guard: run.Kind == Learning → return          (recursion stop #1)
   ├─ Threshold: iterationCount ≥ 2 OR Major+ finding
   ├─ Assembles structured facts → LearningEntry (Proposed)
   └─ FIRE-AND-FORGET: SubmitRunAsync(crew=learning-evaluation, Kind=Learning)

LEARNING-RUN  (Kind=Learning, crew "learning-evaluation")
   ├─ Executor condenses candidate
   ├─ 3 strict reviewers (AbortOnCritical=true, MaxIterations=2)
   └─ Finalizer "learning-publisher" (RunFinalizersOnMaxAttempts=true)
        ├─ Guard: run.Kind != Learning → return      (recursion stop #2)
        ├─ Convergence  → compute embedding, Approved, written to store
        └─ Non-conv.    → Rejected, nothing written

LATER RUN  →  Grounding "learning-retrieval"
   ├─ Embedding search over Approved learnings
   ├─ Domain-boost: finalScore = similarity × (sameDomain ? boost : penalty)
   ├─ Curated knowledge (vector-store) outranks learnings by provider order
   └─ SourceCitation: learning://{id}
```

### RunKind enum

`Standard = 0` (default) / `Learning = 1` / `CrewComposition = 2`. Carried in `RunEntity.Kind`, passed through `SubmitRunRequest.Kind` and `IRunPersistenceService.CreateRunAsync`. The orchestrator dispatches all three kinds identically; the kind gates finalizer guards and recursion stops.

### LearningEntry lifecycle

`Proposed` (extractor wrote it) → `Approved` (publisher + embedding) or `Rejected` (publisher, nothing stored). Manual override possible via `/crew/learnings` UI (owner-checked).

### System profiles added (Step30 migration)

| Name | Type | Notes |
|---|---|---|
| `learning-extractor` | Finalizer (LearningExtract) | Default in all four standard templates (D-057); not in `learning-evaluation` (recursion stop) |
| `learning-publisher` | Finalizer (LearningPublish) | Attached to `learning-evaluation` crew |
| `learning-evaluation` | CrewTemplate | AbortOnCritical=true, MaxIterations=2, 3 strict reviewers |
| `learning-retriever-default` | GroundingProvider (LearningRetrieval) | sameDomainBoost=1.0, crossDomainPenalty=0.5, maxLearnings=4 |
| `learning-factual-grounding` | Reviewer | openrouter/gpt-4.1 |
| `learning-value` | Reviewer | openrouter/gemini-2.5-pro |
| `learning-generalizability` | Reviewer | claude-cli/claude-opus-4.7 |

## Auto-Crew: Composition-Run

A Composition-Run (`RunKind.CrewComposition = 2`) is a full GEEF-Run that uses the GEEF evaluation loop to compose a new crew. GEEF-on-GEEF: the meta-crew `crew-composer` goes through draft → critique → refine → converge, then materializes the result via the `crew-materializer` finalizer. See D-059 in the [decisions log](05-decisions-log.md).

### What a Composition-Run is

The Template Studio's single-pass meta-LLM call (D-038) produced crews without self-correction. A Composition-Run replaces that call with a proper GEEF pipeline: the `crew-composer` crew iteratively refines a `CrewSpecArtifact` JSON until convergence, then the `CrewMaterializeFinalizerExecutor` turns the converged spec into real DB records.

Entry point: `/crew/studio` (standalone composition, `ChainToTaskRun = false` by default). The user provides a task description; the studio submits a `RunKind.CrewComposition` run and shows progress via SignalR.

### The 5 reviewers of crew-composer

| Name | Type | Provider / Model | Role |
|---|---|---|---|
| `crew-spec-validator` | **Deterministic** (no LLM) | — | Validates the `CrewSpecArtifact` JSON schema in-loop; missing required fields or invalid structure → Critical finding, no LLM call needed |
| `crew-diversity-reviewer` | LLM | codex-cli / gpt-5.5 | Checks model pluralism: executor and reviewers must span ≥ 2 different providers |
| `crew-prompt-quality-reviewer` | LLM | claude-cli / claude-opus-4-7 | Evaluates the quality, specificity and role-clarity of every system prompt |
| `crew-grounding-fit-reviewer` | LLM | codex-cli / gpt-5.5 | Assesses whether the chosen grounding providers match the task domain |
| `crew-finalizer-fit-reviewer` | LLM | codex-cli / gpt-5.5 | Checks whether the selected finalizers suit the task's output requirements |

The deterministic `CrewSpecValidatorReviewer` produces findings without any LLM call and is the primary structural gate. The four LLM reviewers run in `Parallel` strategy.

**ConvergenceOverride:** `MaxIterations = 4`, `AbortOnCritical = false` (so validator Critical findings are correctable, not fatal), `StagnationThreshold = 3`.

### CrewSpecArtifact schema

The `CrewComposerExecutor` forces the `submit_crew_spec` tool-call — no free-text output. The resulting JSON follows this schema:

```json
{
  "mode": "existing-template | composed | new",
  "reuse": "<template-name or null>",
  "executor": {
    "name": "...", "provider": "...", "model": "...",
    "systemPrompt": "...", "maxTokens": null
  },
  "reviewers": [
    { "name": "...", "provider": "...", "model": "...",
      "systemPrompt": "...", "focus": "..." }
  ],
  "advisors": [
    { "name": "...", "mode": "Strategic|Critical|DevilsAdvocate|DomainExpert",
      "trigger": "BeforeFirstExecution|BeforeEveryExecution|OnConvergenceFailure",
      "systemPrompt": "..." }
  ],
  "grounding": ["<profile-name>", "..."],
  "finalizers": ["<profile-name>", "..."],
  "evaluationStrategy": "Parallel | Sequential | FailFast | Priority",
  "convergenceOverride": null
}
```

**Mode semantics:**
- `existing-template` — reuse an existing template by name (`reuse` field); no DB writes.
- `composed` — assemble a new template from the `reuse` template as a base, with modifications.
- `new` — create a fully new template from the spec fields; all profiles must be fully specified.

The `reuse` field encodes the **dedup result**: when `CrewMaterializeFinalizerExecutor` finds a template with cosine similarity ≥ 0.90 in `CrewTemplateEmbeddings`, it downgrades the mode to `existing-template` and sets `reuse` to the matched template name.

### CrewMaterializeFinalizerExecutor steps

Runs after convergence of the composition-run (`FinalizerType.CrewMaterialize = 6`):

1. **Parse** — reads the `CrewSpecArtifact` JSON from the final iteration's `ArtifactText`.
2. **Validate** — structural check: required fields present, `reviewers` count ≥ 1, provider names are active in the system.
3. **Dedup** — computes an embedding of the spec, queries `CrewTemplateEmbeddings` for cosine similarity; if any existing template exceeds the 0.90 threshold, downgrades mode to `existing-template` (no new DB records).
4. **Materialize** — for `composed` / `new` modes: creates profiles (Executor, Reviewer × N, Advisor × N, GroundingProvider × N, Finalizer × N) and the crew template in a single EF Core transaction, exactly as `TemplateStudioService.MaterializeAsync` does.
5. **Embed** — computes an embedding of the new template and inserts it into `CrewTemplateEmbeddings` for future dedup.
6. **Chain task-run** — if `ChainToTaskRun = true` and the composition-run carries a seed briefing, submits a new `RunKind.Standard` run using the materialized crew and the original briefing text.

### ParentCompositionRunId audit link

When a task-run is chained from a composition-run, `Runs.ParentCompositionRunId` is set to the composition-run's ID. This provides a full audit trail: given any task-run, you can follow `ParentCompositionRunId` to inspect which composition-run created its crew and why.

### Guards

- `LearningExtractFinalizerExecutor`: `if (run.Kind == RunKind.CrewComposition) return Ok;` — no learnings extracted from composition-runs.
- `CrewMaterializeFinalizerExecutor`: the chained run is always `RunKind.Standard`; no nested composition is possible.
- `CrewComposerExecutor`: only active for `Kind == CrewComposition`; rejects other run kinds.
