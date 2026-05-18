# 08 — Crew system (PS-5)

*[Deutsch](08-crew-system_de.md) · **English***

Last updated: 2026-05-17 (system profiles/advisors/templates brought up to the current `SystemCrew` state: CLI providers, domain templates)

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
| `default-executor` | ExecutorProfile | `claude-cli` / `anthropic/claude-opus-4.7` |
| `briefing-fidelity` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `clarity` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `legal-jargon-precision` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `legal-clause-risk` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `academic-citation-readiness` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `academic-argumentation-rigor` | ReviewerProfile | `claude-cli` / `anthropic/claude-opus-4.7` |
| `marketing-audience-clarity` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `marketing-conversion-strength` | ReviewerProfile | `codex-cli` / `gpt-5.5` |

**System templates** (four): `klassik` (evaluation `Parallel`, no advisors —
reproduces the original PS-2 behaviour) plus the domain templates
`juristisch` (`Sequential`, advisor `legal-domain-expert`),
`akademisch` (`Sequential`, advisor `academic-rigor-advisor`) and
`marketing` (`Parallel`, no advisors).

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
`claude-cli` / `anthropic/claude-opus-4.7`.

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
- `submit_request` — extended with `crew_template` and `custom_crew` (JSON string).

Full tool list (13 tools): see [09-endpoint-reference.md](09-endpoint-reference.md) and the [project README](../README.md).

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

### StudioEditStep field parity (D-043)

The Edit step exposes the full field set for the template and every profile slot:

**Template fields:** DisplayName, Description, EvaluationStrategy (dropdown), EvaluationStrategyReasoning (read-only, from LLM)

**Per profile slot (Executor / Reviewer × N / Advisor × N / GroundingProvider × N):**
- **UseExisting / CreateNew toggle** — pick an existing profile by name, or configure a new one inline
- **CreateNew fields:** Name (kebab-case), DisplayName, Description, Provider, Model (`ModelSelector`), MaxTokens, SystemPrompt
- **Reviewer-specific:** ReviewerFocus (optional)
- **Advisor-specific:** AdvisorMode (Strategic / Critical / DevilsAdvocate), AdvisorTrigger (BeforeFirstExecution / BeforeEveryExecution / OnConvergenceFailure)
- **GroundingProvider-specific:** GroundingProviderType (Tavily / VectorStore), type-specific settings (API key or collection name)
- **Reasoning display:** LLM reasoning per field, read-only (from `analyze_template_proposal`)
- **Field-Helps:** inline German help texts for every field (`StudioFieldHelps.cs`)

### Key components

| Component | Purpose |
|---|---|
| `StudioProfileSlot.razor` | UseExisting/CreateNew toggle + full inline profile form; embeds `ModelSelector` |
| `FieldHelp.razor` | Inline hint rendered below every field |
| `StudioFieldHelps.cs` | Central German-language help-text constants |

### Materialization (atomic, D-043/7)

`TemplateStudioService.MaterializeAsync` wraps all DB writes in a single EF Core transaction (`IAtomicTransactionFactory`). Order: validate → begin → create profiles (Executor, Reviewer, Advisor, GroundingProvider) → create template → commit. Explicit rollback on any error — no half-materialized state. `MarkMaterializedAsync` (marks the analysis record as consumed) runs inside the transaction.
