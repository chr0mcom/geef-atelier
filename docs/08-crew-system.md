# 08 — Crew system (PS-5)

*[Deutsch](08-crew-system_de.md) · **English***

Last updated: 2026-11-25 (D-074: research in the grounding step of the direct path; before that 2026-11-24, D-067: strict snapshot reading, at least one reviewer, outcome after finalizers, run warnings, system grounding back on the provider-type path, learning paused by default; second review round: reviewer check on save, vector scope `both`, `grounding.scope_unknown`, `grounding.tool_failed` reserved, finalizers and cancellation; before that 2026-11-23, D-066/12: explicit model_domain, one assignment rule; D-066/10–11: codex aliases, host sync, availability only as reported by the live catalog; before that 2026-11-22, D-066: model matrix — system profiles and model matrix section rewritten; before that 2026-06-04, D-059)

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

Defined in `Geef.Atelier.Core.Domain.Crew.SystemCrew` (read-only, versioned with the code). Since D-066 `SystemCrew` holds no model literals: every LLM role carries a `ModelSlot`, and its displayed binding is the matrix primary. The actual model is assigned per run (see **Model matrix (D-066)**).

| Name | Type | Role (`ModelSlot`) | Primary (General) |
|---|---|---|---|
| `default-executor` | Executor | Executor | `claude-cli` / `claude-opus-latest` (Technical: Fable) |
| `briefing-fidelity` | Reviewer | FactReviewer | `codex-cli` / `gpt-astra-latest` |
| `clarity` | Reviewer | StyleReviewer | `codex-cli` / `gpt-sol-latest` |
| `domain-terminology-reviewer` | Reviewer | StyleReviewer | `codex-cli` / `gpt-sol-latest` |
| `substantive-rigor-reviewer` | Reviewer | DomainReviewer | `codex-cli` / `gpt-astra-latest` (Legal/Academic: Gemini 4 Neon) |
| `crew-composer-executor` | Executor | Composer | `claude-cli` / `claude-opus-latest` |
| `crew-composer-completeness`, `crew-composer-tool-binding` | Reviewer | FactReviewer | ring: Astra, then Sonnet |
| `crew-composer-fit`, `crew-composer-reuse-correctness` | Reviewer | DomainReviewer | ring: Astra, then Fable |
| `crew-composer-prompt-quality` | Reviewer | StyleReviewer | `codex-cli` / `gpt-sol-latest` |
| `crew-spec-validator` | Reviewer | — (deterministic) | — |

**System templates** (four + composer), each with a crew domain (`CrewTemplate.Domain`):

- `klassik`: General, `Parallel`, no advisors.
- `juristisch`: Legal, `Sequential`, advisor `legal-domain-expert`.
- `akademisch`: Academic, `Sequential`, advisor `academic-rigor-advisor`.
- `marketing`: Marketing, `Parallel`.
- `crew-composer`: General.

All four standard templates ship with three default grounding providers (`tavily-basic`, `run-attachments`, `learning-retriever-default`) and one default finalizer (`learning-extractor`). The `learning-evaluation` template is the only exception — it carries no grounding providers and no `learning-extractor` (recursion stop). Since D-067 the learning parts run empty unless `Learning:Enabled` is set (see [Continuous Learning Loop](#continuous-learning-loop-d-054)).

## Model matrix (D-066)

A profile states its **role**; the crew states its **domain**; the system picks the model from `Core/Domain/Crew/Models/ModelMatrix`. Claude always runs via `claude-cli`, OpenAI always via `codex-cli`; other routes are rewritten on save.

- **Managed vs manual:** `ModelSlot` set ⇒ managed (the stored binding is display only); `ModelSlot = null` ⇒ manual. Grounding refiners and Transform finalizers use a settings marker instead.
- **Assignment at run start** (`IModelAssignmentService.ApplyAsync`):
  - The candidate must be reported by the live catalog (the picker list merged with static ids does not count); an unreachable catalog counts as available, an HTTP provider without its API key as unavailable.
  - Reviewers and advisors do not share the executor's model while a permitted alternative exists; otherwise only with a plurality warning.
  - The crew domain comes explicitly from the composer's `model_domain` (no keyword heuristic); all rules live in one function (`ModelChooser`) with hard rules that are never relaxed, guarded by the exhaustive `ModelAssignmentInvariantTests`.
  - Reviewers prefer another vendor family.
  - Same-slot reviewers rotate through a ring of the first 3 eligible candidates.
  - Paid candidates are used outside Legal/Academic only from 3 same-slot reviewers on, with at most 2 paid reviewers per crew.
  - Warnings are logged on skipped primaries and fallbacks.

- **Always current (D-066/10):** the matrix names family aliases (`claude-opus-latest`, `gpt-sol-latest`, `gpt-astra-latest`). The cli-proxy resolves the codex aliases to the newest listed model of the line, so a new release of an existing line needs no code change; run snapshots keep the concrete id. The nightly `scripts/cli-proxy-selfupdate.sh` keeps container and host CLIs current and moves the host codex default to the newest Sol.
- **Intake slot (D-071):** `ModelSlot.Intake` (Sonnet → Sol) clarifies an order before writing; it is assigned with `AssignAsync(ModelSlot.Intake, General)` and no profile may carry it (`AllowedSlots` unchanged, not in the composer catalog).
- **Composer settings** are whitelisted (`CrewPartSettingsPolicy`); inline `rest-api` grounding and inline `ExternalSink` finalizers are critical.

The table and rationale are in D-066 and `reports/2026-11-22-modellrecherche.md`.

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
    "model": "anthropic/claude-opus-4.8",
    "maxTokens": null,
    "isSystem": true
  },
  "reviewers": [
    { "name": "briefing-fidelity", "provider": "openrouter", "model": "google/gemini-3.5-flash", ... },
    { "name": "clarity",           "provider": "openrouter", "model": "openai/gpt-5.6-luna",    ... }
  ],
  "evaluationStrategy": "Parallel",
  "convergenceOverride": null,
  "advisors": []
}
```

Serialized with `JsonNamingPolicy.CamelCase`. Stored in `Runs.CrewSnapshot` (JSONB).

**Reading a snapshot for a run (D-067).** The orchestrator reads the snapshot strictly through
`CrewSnapshot.DeserializeForRun`. A missing, unreadable or empty snapshot, an unknown schema version, or a
snapshot without executor, reviewer or advisor list raises `InvalidCrewSnapshotException`, and the run ends
`Failed` with a clear message. The former silent fallback to the `klassik` crew is removed. The tolerant
`CrewSnapshot.Deserialize` remains for display only. A run with attachments is stored without a snapshot
until its attachments are ready ([02-architecture](02-architecture.md#preparation-timeout-for-runs-with-attachments)).

**At least one reviewer.** A crew without reviewers cannot run: `AtelierPipelineFactory` refuses to build
the pipeline and the run ends `Failed` with a message. The former `AutoApproveReviewer` that silently
approved every draft is removed. Creating or saving a crew template without a reviewer is refused
(`CrewTemplateRules.EnsureHasReviewer`, used by `CrewService` and the template editor); Template Studio and
the composer require a reviewer as well.

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

Models since D-066 come from the model matrix: `briefing-clarifier` and `crew-design-advisor` are StrategicAdvisor (Fable), `devils-advocate` is CriticalAdvisor (Fable), `legal-domain-expert` is DomainExpertAdvisor/Legal (Opus, excluded as executor model, so Astra at run time), `academic-rigor-advisor` is DomainExpertAdvisor/Academic (Astra).

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

Since D-067:

- `Runs.Outcome` is set only after the finalizers have finished and the (possibly transformed) final text is stored, so `Outcome` means "finished and stored, including all finalizers" ([02-architecture](02-architecture.md#run-outcome-and-runstatus)).
- A finalizer that fails, is skipped, or is not run (best-effort run without `RunFinalizersOnMaxAttempts`) becomes a run warning (`finalizer.failed`, `finalizer.skipped`, `finalizer.not_run`) instead of passing silently.
- A cancellation or service stop while the finalizers run is not a finalizer failure: the run ends with `Outcome = Aborted` and gets no `finalizer.failed` warning. The results of finalizers that finished before the stop are kept (changed text, artifacts, costs); the remaining ones do not run. The status of a converged run stays `Completed`; `Outcome` is what counts.
- If the service is stopped hard while the finalizers run, the next start gives the run an outcome and the warning `run.restarted_while_finishing` (source `run`).
- If the result cannot be stored completely, the run gets the warning `run.result_not_saved` (source `run`) and `Outcome = Failed`.

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
- **Modell:** Frei wählbar; für Tone-Transformationen reichen günstige Modelle (z.B. `gpt-5.6-luna`)
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

**Actual state (D-067):**

- The five system profiles `tavily-basic`, `tavily-refined`, `run-attachments`, `knowledge-base-default` and `academic-default` run through their type-specific provider listed below, not through the tool path, also for older snapshots that still carry a `ToolName`. Only custom profiles with a `ToolName` use the tool-backed provider ([10-tool-system](10-tool-system.md#a-t9--grounding-rebuild-on-central-tools)).
- A provider that is skipped or fails (missing key, unknown type, error) becomes a run warning (`grounding.provider_skipped`) instead of silently empty context. The code `grounding.tool_failed` is reserved and currently never written; failures of tool-backed sources also use `grounding.provider_skipped`.
- Key and header references (`authHeaderEnv`, `apiKeyEnv`) are only accepted and read for names on the allow list ([10-tool-system](10-tool-system.md#secrets-and-the-environment-variable-allow-list)); the grounding editor is admin-only.

### Provider Types

| Type | Implementation | Description | Settings |
|---|---|---|---|
| `tavily` | `TavilyGroundingProvider` | Web search via Tavily API (basic or advanced). API key per profile. | `Tier` (basic/advanced), `MaxResults`, `IncludeAnswer` |
| `vector-store` | `VectorStoreGroundingProvider` | Semantic search in a pgvector collection. Scope: `global` (shared documents only), `run-local` (attachments of the run and of the runs it continues, never those of other runs) or `both` (shared documents plus those attachments). A missing scope means `global`; an unknown value is also treated as `global` (fail-closed, never other runs' attachments) and the run gets the warning `grounding.scope_unknown` (D-067). `TagFilter` narrows the shared documents only; attachments have no tags and are never hidden by it. | `TopK`, `Scope`, `TagFilter` |
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
- **Field-Helps:** inline English help texts for every field (`StudioFieldHelps.cs`)

### Key components

| Component | Purpose |
|---|---|
| `StudioProfileSlot.razor` | UseExisting/CreateNew toggle + full inline profile form; embeds `ModelSelector` |
| `FieldHelp.razor` | Inline hint rendered below every field |
| `StudioFieldHelps.cs` | Central English help-text constants |

### Materialization (atomic, D-043/7)

`TemplateStudioService.MaterializeAsync` wraps all DB writes in a single EF Core transaction (`IAtomicTransactionFactory`) and returns a `MaterializationResult` (final template name, created profile names, warnings).

**Pre-transaction validation:**
- `ValidateNotSystemProfiles` — proposed profile names must not collide with system-reserved names.
- `ValidateReviewerCount` — the template must carry at least one reviewer.
- `ValidateAvailabilityAsync` — checks each generating profile's model against the provider catalogue; a mismatch produces a **non-blocking warning** (the model may simply be missing from the live catalogue), not an abort.

**Transaction order:** begin → create profiles (Executor, Reviewer, Advisor, GroundingProvider, Finalizer) via `CreateProfileAsync` → create template via `CreateTemplateAsync` → `MarkMaterializedAsync` (marks the analysis record as consumed) → commit. Any error triggers an explicit rollback — no half-materialized state.

**Name mapping:** `CreateCustom*Async` idempotently prefixes every new profile name with `custom-`. `MaterializeAsync` records the old→final mapping and `ApplyProfileNameMapping` rewrites all template references (executor, reviewers, advisors, grounding providers, finalizers) to the actually stored names before the template is created. Names already referring to existing profiles pass through unchanged. The evaluation strategy is normalized (`NormalizeEvaluationStrategy`, default `Sequential`).

Finalizer proposals appear in the Studio's LLM analysis output; `CreateProfileAsync` handles the finalizer branch; `StudioEditStep` exposes the finalizer slot section alongside the other profile slots.

Since D-067 the Studio removes `authHeaderEnv`/`apiKeyEnv` from proposed grounding profiles with a warning, and its prompt no longer offers these keys; secret references are set by administrators in the grounding editor only.

## Continuous Learning Loop (D-054)

> **Paused since D-067.** The loop runs only when `Learning:Enabled` is `true` (default `false`). While it
> is off, `learning-extractor` and `learning-publisher` return immediately (no LLM call, no entry, no
> `learning-evaluation` run, no embedding), `learning-retrieval` returns empty context, and
> `/crew/learnings` shows that learning is paused. Crews that contain these parts stay valid. The loop is
> to be replaced by a per-user memory (D-068).

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
| `learning-factual-grounding` | Reviewer | codex-cli/gpt-5.6-sol |
| `learning-value` | Reviewer | openrouter/gemini-3.1-pro-preview |
| `learning-generalizability` | Reviewer | claude-cli/claude-opus-4.8 |

## Auto-Crew: Composition-Run

A Composition-Run (`RunKind.CrewComposition = 2`) is a full GEEF-Run that uses the GEEF evaluation loop to compose a new crew. GEEF-on-GEEF: the meta-crew `crew-composer` goes through draft → critique → refine → converge, then materializes the result via the `crew-materializer` finalizer. See D-059 in the [decisions log](05-decisions-log.md).

### What a Composition-Run is

The Template Studio's single-pass meta-LLM call (D-038) produced crews without self-correction. A Composition-Run replaces that call with a proper GEEF pipeline: the `crew-composer` crew iteratively refines a `CrewSpecArtifact` JSON until convergence, then the `CrewMaterializeFinalizerExecutor` turns the converged spec into real DB records.

Entry point: `/crew/studio` (standalone composition, `ChainToTaskRun = false` by default). The user provides a task description; the studio submits a `RunKind.CrewComposition` run and shows progress via SignalR.

### The 5 reviewers of crew-composer

| Name | Type | Provider / Model | Role |
|---|---|---|---|
| `crew-spec-validator` | **Deterministic** (no LLM) | — | Validates the `CrewSpecArtifact` JSON schema in-loop; missing required fields or invalid structure → Critical finding, no LLM call needed |
| `crew-diversity-reviewer` | LLM | codex-cli / gpt-5.6-sol | Checks model pluralism: executor and reviewers must span ≥ 2 different providers |
| `crew-prompt-quality-reviewer` | LLM | claude-cli / claude-opus-4-8 | Evaluates the quality, specificity and role-clarity of every system prompt |
| `crew-grounding-fit-reviewer` | LLM | codex-cli / gpt-5.6-sol | Assesses whether the chosen grounding providers match the task domain |
| `crew-finalizer-fit-reviewer` | LLM | codex-cli / gpt-5.6-sol | Checks whether the selected finalizers suit the task's output requirements |

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

## Uploaded files: safe reading (D-069 addendum)

Files from `POST /api/sources` and from MCP attachments go through `SourceIntakeService`. PDF and Word files are read
by the same application in a **separate process**, so a hostile file can never take the web process down.

### Intake steps

1. **Upload place and slot.** Each person runs at most `Sources:MaxConcurrentUploadsPerUser` (2) uploads at the same
   time; a further one is refused at once with HTTP 429, `Retry-After: 5` and "You are already adding 2 files at the
   same time. Please wait until one is done, then try again." Across all people at most `Sources:MaxConcurrentIntakes`
   uploads run at the same time; a caller waits up to `Sources:ExtractionQueueTimeout`, otherwise the answer is HTTP 503
   with `Retry-After: 30` and the receipt "The service is busy with other uploads. Please try again in a minute." (the
   card offers "Try again"). Both are taken **before** the request body is
   read: `POST /api/sources` reads the multipart body as a stream (no form buffer) and takes the antiforgery token only
   from the header. Pasted text needs neither, because it is already in memory.
2. **Streaming and time.** The upload is written to `<StorageRoot>/.incoming/<id>/upload.part` (directory 0700, file
   0600), hashed on the way and capped at `Sources:MaxFileBytes`. Only the first 8 KiB stay in memory for type
   detection. The request ends after `Sources:UploadTimeout` (5 minutes, HTTP 408 "The upload took too long. Check your
   connection and try again.") and, behind Kestrel, when it sends slower than `Sources:MinUploadBytesPerSecond`
   (4 KiB/s after a grace period of 10 s); both free the place and the slot. A broken or interrupted body ends as 400
   "The upload could not be read."; only a file above the size limit gets 413.
3. **Reuse and quota.** An identical file of the same owner is reused (the unique index
   `UX_Sources_OwnerUsername_Sha256` makes parallel uploads of the same file end in one source). Files that no order
   uses count against `Sources:MaxUnattachedSourcesPerUser` (50), `Sources:MaxUnattachedBytesPerUser` (500 MB) and
   `Sources:MaxUnattachedCharsPerUser` (20 million characters of text). The quota is checked before reading and again
   inside the inserting transaction under a lock per person (`pg_advisory_xact_lock(hashtext(owner))`), so parallel
   uploads cannot pass it together.
4. **Type check.** The kind comes from the content. Before a ZIP archive is opened in the web process, its end record
   is read: more entries than `Sources:MaxArchiveEntries`, a central directory larger than 1 KiB per allowed entry or a
   ZIP64 directory (no Word file needs one) refuse the file without listing the archive. `%PDF-` within the first
   1,024 bytes counts as a PDF on purpose (readers accept the header anywhere there).
5. **Extraction.** Text and Markdown are read in the web process. PDF and Word take an extraction slot
   (`Sources:ExtractionMaxConcurrency`, default 2) and run as
   `dotnet <application assembly> --extract-source <pdf|docx> <file>`. `Program.cs` branches into this mode before any
   host is built: no DI, database, configuration or sign-in. The answer of the process is parsed while it streams in;
   it is never held as a whole byte array.
6. **Text limit.** One source holds at most `Sources:MaxExtractedChars` (3 million characters; a book of 1,000 pages has
   about 2 million). Word files are counted while their XML parts are scanned, before the document model is built, and
   again while the body is walked; PDF reading stops at the page that passes the limit; text files are counted before
   they are decoded; the web process checks the length again before it copies the text. The receipt says "This file
   contains far more text than we can use. Please upload a shorter version."
7. **Preparation in one pass.** Line ends become `\n` first (so a lone `\r` starts a line before anything looks at
   the lines), lines that look like `[[page N]]` are neutralised in text, Markdown and Word files, long lines are broken
   (every broken piece is neutralised as well) and characters and words are counted without page markers. Text that
   needs none of this is not copied. Only the PDF extractor writes real markers.
8. **Store.** The received file is moved into `<StorageRoot>/<owner hash>/<id>.<ext>`; the temporary directory is
   removed in every case.

### Limits of the extraction process

| Limit | Option | Default | Effect when hit |
|---|---|---|---|
| Total time | `ExtractionTimeout` | 60 s | whole process tree killed, "Reading this file took too long." |
| Managed memory | `ExtractionMemoryLimitBytes` | 1 GiB | `DOTNET_GCHeapHardLimit` in the child; a watchdog also kills it above the limit plus 256 MiB |
| Text | `MaxExtractedChars` | 3,000,000 characters | reading stops, "This file contains far more text than we can use. Please upload a shorter version." |
| Answer size | `ExtractionMaxOutputBytes` | 20 MiB | process killed, file unreadable; sized for the text limit at up to six bytes per UTF-16 unit (emoji and format characters are written as `\uXXXX`, letters of the basic plane are not escaped) plus room for the JSON |
| Environment | — | — | cleared; only `PATH`, `HOME`/`TMPDIR` (the private upload directory), GC settings and the extraction limits as JSON |
| Working directory | — | — | the private upload directory |

A crash (for example a stack overflow, exit code 128 and above), an out-of-memory exit or a kill ends as a receipt
with "This file is too complex to read. Please save it again or split it into smaller files." The web process only
logs kind, outcome, duration, exit code and peak memory, never file content or names.
`Sources:ExtractionMode = InProcess` exists only for tests of the extractors.

### Defences inside the extractors

- **Word:** archive check (entries, total size `MaxUncompressedBytes` = 64 MiB, compression ratio from 64 KiB entry
  size on), then every XML part is read once as a stream without DTD and with a nesting limit (`MaxXmlDepth` = 100)
  before the document model is built; the body is walked without recursion.
- **PDF:** before opening, the Flate streams that are not pictures are unpacked into nothing and added up; above
  `MaxPdfInflatedBytes` (256 MiB) the file is refused. Nested filters are left to the process limits.

### Cleanup

The hourly cleanup also deletes, after `Sources:OrphanFileAge` (24 h), left-over upload directories, `*.part` files
and stored files that no source refers to. It looks only at the root and the owner folders below it, skips links and
resolves every path inside the root. When at least 10 stored files are unknown and they are more than half of all old
stored files, the database most likely does not match the volume (restored from an older backup or started empty);
then it deletes no stored file and logs an error.

### Operations note

The compose file limits the memory of the web container to 6 GiB (`mem_limit: 6g`) as a second layer behind the
limits above: the web process usually stays below 1 GB, each extraction process may use 1 GiB of managed heap (plus
the watchdog allowance) and at most two run at the same time; the rest is headroom. The limit protects the other
services of a shared host. If the host has less memory to spare, lower `ExtractionMaxConcurrency` or
`ExtractionMemoryLimitBytes` together with the limit.

## Automatic length check of the direct path (D-070 addendum)

The length check without a model is deliberately conservative: it blocks only when the request is clear and the text
is far off; otherwise it leaves a note. `RequestConstraintExtractor` reads lengths only outside quotes, code and text
attached after a lead-in ("Answer this e-mail:"). `LengthRuleGate` then lets a length become a blocking check only
when the whole request has exactly one length in words or characters (or a clear pair such as "between 100 and 150
words"), no other count (pieces, "each/per/je", variants, pages, minutes), no word that refers to an earlier text or a
revision ("draft", "previous", "too long", "shorten", "summarize", "translate" …), a limit word bound to the number
("at most", "höchstens", "about" … or "with/of/in N words" right after the text to write), no foreign preposition
between the instruction verb and the length in the same clause ("a title for a book with at least 80,000 words") and
no compound such as "300-word post". Every other length gives the note "Your request mentions a length (…); it was
not checked automatically." Even a blocking length leads to a correction only when the text misses the limit by more
than 25 % ("at most 100 words" from 126 words, "at least 200 words" below 150); a smaller miss is an open point. When
the length is the only blocking problem, the instruction to the writer ends with "Keep everything else unchanged."
The rate of false blocks is measured on synthetic, independently labelled requests
(`tests/Geef.Atelier.Tests/Domain/Checks/Corpus/`, `LengthCorpusTests`).

## Template `direct`: two reviewers (D-073)

The code-only template `direct` (`SystemCrew.DirectTemplate`, not in template lists) runs the writer once and corrects at
most once (`MaxIterations: 2`). Since stage 3 it has two reviewers, both injected at pipeline build time by
`DirectPipelineAssembler`:

| Reviewer | Profile | Model | What it does |
|---|---|---|---|
| Automatic checks | `deterministic-checks` | none | `DraftCheckEvaluator`: not empty, no remark before the text, length, language, placeholders, forbidden patterns and words, required words, written calculations. Blocking failures → `Error`, the rest `Info`. |
| Fact check | `source-fact-check` | slot `FactReviewer` (today Codex Astra, never the writer's family) | `SourceFactCheckReviewer`: compares the facts of the text with the request, the answers, the profile and the files (`FactCheckPrompts`, version `fact-check-1`). |

The fact check runs for every order of the direct path; only `Direct:FactCheckMode = Off`, `Shadow` or a failure of the
model and its one fallback leave the facts unchecked, and "What was checked" says so.

- **Material** comes from the grounding step (`AtelierContextKeys.FactMaterial`): request, answers of the person and of
  the client, profile and files, for a revision the text to revise as `S0`. Assumptions and the writer's notes are never
  material. Above `Direct:FactCheckMaxMaterialChars` (150,000 characters of material and text) the check gets the file map
  instead of the content.
- **Filter of the code** (`FactFindingFilter`): a finding stays only with a word-for-word quote; whether it blocks follows from
  where its quotes stand (request, the person's answers and profile block; the client's answers are hints; contradictions
  in the files never block). A reason that names a link, an address or account or access details never drops the finding:
  the reason is replaced by a fixed sentence, the quote of the text stays (an invented IBAN is exactly what the person must
  see); unsafe passages of the material are replaced the same way. "Covered word for word" needs whole words.
- **Every wording is checked in full.** A text changed for any reason (fact finding, length, language …) gets a full
  `check_facts` again, with the same fallback rules; only a wording an earlier round already checked word for word takes over
  that round's findings without a call. `recheck_facts` is no longer called (prompt and schema stay unchanged in the code).
  A model that ran into its time limit is not called again in the same run. The report knows which wording was checked
  (`FactCheckRound.TextHash`); a delivered text no check saw is `VersionNotChecked` ("The facts of this version of the text
  were not checked …"), and earlier findings are shown only when their passage still stands in it.
- **No finding is lost between rounds** (`FactCarryOver`): after every full check, the findings of all earlier answered rounds
  whose passage still stands in the text word for word (whole words) stay open with their blocking effect, and every source
  conflict stays (a correction does not change the files). Only repetitions are dropped: a passage that lies wholly inside a
  kept passage of the same kind (for an instruction, against the same instruction), or a conflict whose two passages contain
  each other as whole words and that names the same files; a passage that only overlaps another is shown as well. No limit
  applies, ids are given anew. When an earlier version is delivered as the best one, the findings of the later answered rounds
  are added the same way (conflicts always, passages only when they stand in the delivered text, unlisted ones counted). So in `On` a blocking
  passage that the second check does not flag again still rejects. Flagged passages an earlier check could not list stay
  counted. Quotes are widened to whole words, so an unchanged passage never counts as "removed"; without a check of the
  delivered wording, "Removed or replaced" adds that the statement may still be in the text in other words.
- **Modes:** `Report` (shipped) → findings `Warning`, decision `Approved`, the text is never changed; `On` → blocking findings
  `Error` and `Rejected`, the writer gets fixed sentences; `Shadow` → `Info` with metadata `shadow`, nothing shown.
- **Fallback:** when the model cannot be reached, one more attempt with the next candidate of `FactReviewer` that is neither
  the writer's model nor from the provider that failed nor paid per token (`FactCheckModelFallback`, today Sonnet). The
  gateway's own failover stays off. Calls, model and fallback are in the report (`factCheck`), never shown as a warning.
- **Persistence:** findings keep `Category = fact-check`, `Code` (`fact.unsupported`, `fact.instruction`, `fact.superseded`,
  `fact.source_conflict`) and `Quote`; `Runs.FactCheckMode` holds the mode of the run (migration Step50). When an account is
  deleted or renamed, its runs lose every copy of the About me details: the block in the briefing, the briefing in the stored
  events (`PipelineStartedEvent.Input` and any other text of an event that holds the block or the details word for word) and
  what the fact check copied into report, findings and events. Runs cleaned by an earlier version are cleaned again with the
  details still held in their events.
- **Recalculation never says "right":** the check of written calculations in the text and of totals in the files names only
  what looks clearly wrong and asks the person to check it; otherwise "What was checked" says at most that nothing clearly
  wrong was found and that not every calculation can be checked this way. Earlier reports read the same way.

## Research in the grounding step (D-074)

Since stage 4 the grounding step of the direct path (`OrderGroundingStep`) can add passages looked up on the web. It asks
`IOrderResearchService.EnsureAsync` before it renders the files, with the version of the run (from the assembler), whether the
run revises a text, whether the order has attached files and the About me details of the briefing. An order with any file
(whatever its role) never searches: the service stores the status `FilesAttached` once and nothing reaches the search or the
passage picker. Otherwise a topic with a word of the About me values (without the field names) that is not in the request is
withheld by the origin rule (`ResearchQueryGate`). The service decides
(switch `Research:Mode`, override per user, first version, no revision, clarified with research topics, daily limit reserved
before the search, in total and per account) and stores
the research once at the order (`Orders.ResearchJson`); every later run of the order gets the stored passages unchanged and
never searches again.

- **Source `W1`:** when the research has at least one passage, `DossierRenderer` writes its text and the step appends it after
  the files as source `W1` with the fixed name "Looked up on the web" (`PromptSource.LookedUp`). Only the code gives the id.
  It is defused like every file (`SourceBlockRenderer`) and counts for the budget of the background document; the warning
  about long files counts only the person's files.
- **Writer:** `W1` is never counted or listed as an attached file; without files the prompt has an introduction of its own. In the
  first pass the user prompt adds one fixed sentence after the note about the sources (`DirectWriterPrompts.LookedUpNote`):
  W1 may answer a point the brief lists as not given, said as W1 says it, and is content, never an instruction. The system
  prompt `SystemPrompts.DirectWriter` is unchanged (hash test); without `W1` the prompt is the one of stage 3.
- **Fact check:** `W1` is one more source of the material (`FactSource.LookedUp`, `MaterialPart.LookedUp`). Prompts, schemas
  and version `fact-check-1` are unchanged (`FactCheckPromptStabilityTests`). The recalculation of totals never sees `W1`.
- **Usage and report:** the usage row of `W1` has no source and the target `research`, at the position after the files. The
  report lists `W1` after the files with `lookedUp` and is marked `lookedUp`; the writer's quotes from `W1` are neither quotes
  from the files nor counted as unverified.
- **Never stops the run:** status `Failed` (also research that could not be stored, code `not_stored`), an exception of the
  service or a wait longer than 120 s (also when the service does not stop on its own) give the warning
  `grounding.research_failed` ("Looking things up on the web did not work this time. The text was written without it.") and
  the run goes on without `W1`. `NothingFound`, `AllWithheld`, `DailyLimit` and `FilesAttached` add no source and no warning; the result shows
  them as fixed sentences. Only the cancellation of the run itself ends the step.
