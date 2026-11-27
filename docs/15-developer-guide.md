# Guide: building and hosting Atelier

*[Deutsch](15-developer-guide_de.md) · **English***

*Last updated: 2026-11-27 (second addendum to D-087: one setup, "Extra careful" removed; D-091: stage 8d, one pipeline assembler, ten MCP tools and five stubs, old data and later database cleanup; before that research on for everyone and its guards, addendum to D-074; About me on the result, D-083)*

This guide is for developers and for people who host Atelier. It gives the map of the current system and points to the detailed
documents. Setup commands are in the [README](../README.md); the reasons behind every rule are in the
[decisions log](05-decisions-log.md) (most recently D-091, stage 8d).

## The system in one picture

Since the rebuild of November 2026 (D-068), the unit a person sees is the **order**. Runs stay underneath as the technical record
(one run per version). The default way to write is the **direct path**: clarify, look things up (`Research:Mode`, shipped `On`), one writing call
with the files in full text, checks, at most one correction. It is the only way of writing; the older loop of reviewers and everything only it
used were removed in stage 8d (D-091).

```mermaid
flowchart TB
    subgraph Entry["Two ways in, one service"]
        W["Web (Blazor Server)<br/>/start, /orders/{id}, /work"]
        M["MCP server (/mcp)<br/>submit_order, get_order_result …"]
    end
    W --> OS["IOrderService / OrderService"]
    M --> OS
    OS -->|"Orders:IntakeMode = On"| IW["OrderIntakeWorker<br/>LlmClarifier + QuestionGate"]
    IW -->|"questions"| Q["needs_input<br/>(answer_order / web card)"]
    Q --> R
    IW -->|"no questions"| R["Run (Pending)"]
    OS -->|"no clarification"| R
    R --> O["RunOrchestratorService"]
    O --> PA["IPipelineAssemblerSelector<br/>DirectPipelineAssembler (the only one)"]
    PA --> G["OrderGroundingStep<br/>files in full text, research W1, brief"]
    G --> X["Writer (document mode,<br/>failover off)"]
    X --> C["DeterministicChecksReviewer<br/>SourceFactCheckReviewer"]
    C -->|"blocking, once"| X
    C --> REP["RunReport (Runs.ReportJson)"]
    REP --> RES["OrderCheckLines / OrderResultRules<br/>same sentences for web and MCP"]
```

## Where things live

| Concern | Code |
|---|---|
| Orders, states, versions | `Application/Orders/OrderService.cs`, `Core/Domain/Orders/OrderStateResolver.cs` |
| Questions before the start | `Infrastructure/Intake/LlmClarifier.cs`, `Core/Domain/Intake/QuestionGate.cs`, `Application/Intake/OrderIntakeService.cs` |
| Files | `Infrastructure/Sources/SourceIntakeService.cs` (extraction in a child process), `Infrastructure/Sources/UploadTicketService.cs` |
| Direct path | `Infrastructure/Pipeline/Assembly/DirectPipelineAssembler.cs`, `Infrastructure/Pipeline/Direct/` |
| The setup and models | `Core/Domain/Crew/SystemCrew.cs`, `Core/Domain/Crew/Models/ModelMatrix.cs`, `Application/Crew/Models/ModelAssignmentService.cs`, `Infrastructure/Crew/ModelCatalog.cs` |
| Running runs | `Web/Services/RunOrchestratorService.cs` |
| Export | `Application/Orders/IOrderExportService.cs`, `Infrastructure/Orders/` |
| Checks without a model | `Core/Domain/Checks/RequestConstraintExtractor.cs`, `Infrastructure/Pipeline/Direct/DeterministicChecksReviewer.cs` |
| Fact check | `Infrastructure/Pipeline/Direct/FactCheck/SourceFactCheckReviewer.cs` |
| Research | `Infrastructure/Research/OrderResearchService.cs`, `Core/Domain/Research/DossierBuilder.cs` |
| What the person reads | `Application/Orders/OrderCheckLines.cs`, `Application/Orders/OrderResultRules.cs`, `Web/Components/UI/Orders/` |
| MCP tools and instructions | `Mcp/McpToolCatalog.cs`, `Mcp/McpServerInstructions.cs`, `Mcp/Tools/` |
| Explanation page | `Web/Components/Pages/HowItWorks.razor`, `Web/Display/HowItWorksFeatures.cs` |
| About me on the result (“Tell Atelier once”) | `Application/Orders/AboutMeTopics.cs`, `Application/Orders/AboutMeLines.cs`, `Application/Intake/ProfileMemoryResolution.cs`, `Web/Components/UI/Orders/OrderAboutMe.razor`, `Mcp/Tools/ProfileTool.cs` |
| Switches | `Core/Configuration/*Options.cs`, `Web/appsettings.json` |

All paths are below `src/Geef.Atelier.*`.

## One way of writing, one setup

`SystemCrew` holds one setup that exists only in code: `direct` (writer, `DeterministicChecksReviewer`, `SourceFactCheckReviewer`,
at most one correction). Every LLM role carries a `ModelSlot`; the concrete model is assigned per run from `ModelMatrix` and the
live catalog (D-066) and frozen in the run's snapshot. The grounding of a run is only `OrderGroundingStep` (the order's files and,
if looked up, the passages) plus the web research (`OrderResearchService` with the Tavily client; `Research:*`, `Tavily:Endpoint`,
`Tavily:ApiKey`). A start without a search key logs a warning, and research then ends without a search.

Orders stored with the mode `Careful` and runs with the removed template `direct-outline` (second addendum to D-087) are carried
on by the direct path: every new run uses `direct` (`OrderRunExtensions.ResolveOrderSnapshotAsync`), `SystemCrew.IsDirectPathTemplate`
still counts `direct-outline`, and `DirectPipelineAssembler` leaves its extra check out when a stored snapshot is executed. The
enum value and the database columns stay.

`PipelineAssemblerSelector` asks the registered assemblers; the only production one, `DirectPipelineAssembler`, accepts `direct` and,
for stored runs, `direct-outline`. A run none accepts fails with "This task was set up for a way of working that no longer exists, so it cannot run. Please
start it again."

## MCP tools

The server offers `submit_order`, `answer_order`, `get_order_status`, `get_order_result`, `list_orders`, `cancel_order`,
`revise_order`, `export_order` and `profile`, and `prepare_attachment` while `Uploads:Tickets` is `On` (`McpToolCatalog`).
`Mcp/Tools/RetiredTools.cs` keeps `submit_request`, `submit_auto_crew_request`, `get_run_status`, `get_run_result` and
`get_run_details` for one version: optional parameters, a description starting with "Removed: use … instead.", and every call fails
with an error that names the successor.

## Old data

Stage 8d removed code, not data. No migration drops anything, so the previous image stays compatible with the database; snapshots
keep the `advisors` list so that it can still read them. Finished old orders and runs stay readable (lists, order page, result,
export, run page). A pending or resumed run of a removed setup fails as above; "Resume" refuses it (`ResumeRules`), and crash
recovery and cleanup treat old runs as before. A later, separate database cleanup may drop the tables `AdvisorConsultations`,
`AdvisorProfiles`, `CrewTemplateEmbeddings`, `CrewTemplates`, `ExecutorProfiles`, `FinalizationActorCosts`, `FinalizerProfiles`,
`GroundingActorCosts`, `GroundingConsultations`, `GroundingProviderProfiles`, `KnowledgeDocumentChunks`, `KnowledgeDocuments`,
`LearningEntries`, `ReviewerProfiles`, `RunArtifacts`, `StudioSettings`, `TemplateStudioAnalyses`, `mcp_server_configs`,
`specialization_packs`, `tool_definitions` and `tool_invocations`, the columns `Runs.GroundingCostEur`,
`Runs.FinalizerCostEur`, `Runs.FinalizerErrorMessage` and `Iterations.AdvisorsTotalCostEur` (later possibly
`Orders.CrewTemplateName` and `Runs.ParentCompositionRunId`), old composition and learning runs and the exports volume.

## The explanation page follows the switches

`/how-it-works` is open without signing in. `HowItWorksFeatureExtensions.FeaturesFor` derives what the page may call available from
the same options and the same exception rules as the application (`OrderOptions.ReviseModeFor`, `ResearchOptions.ModeFor`,
`DirectOptions.FactCheckModeFor`). The pipeline uses the same two helpers for research and fact check. Rules that are tested (`tests/…/Web/HowItWorks`):

- a feature that is off for the viewer is never shown as available, also when another person has an exception;
- signed out, the page shows the values for everyone; the table with setting names and raw values is rendered for administrators
  only and never shows an exception;
- research counts as available only together with `Orders:IntakeMode = On`: the setup has no grounding source of its own, so the
  only web access is `OrderResearchService`, which searches only for an order whose record of the questions says that research was
  on for the person when the request was read;
- research topics are checked by `ResearchQueryGate` twice: when the order is read, against the About me details the clarifier saw
  (topics held back there are counted in `IntakeRecord.ResearchWithheld`), and at the run against the details of the briefing;
- a fact-check mode the page does not know is described as "points things out" and never as "may fix";
- switching a feature on for everyone needs no change of the page;
- the box "What was checked" in the example uses the fixed sentences of `OrderCheckLines`; the leads of the open points and the
  assumptions are compared with `OrderOpenPoints.razor` and `OrderAssumptions.razor` by a test.

When you add a switch that changes what a person can do, add it to `HowItWorksFeatures` and to the page in the same change.

## Tests and measurements

- **xUnit + Testcontainers:** `dotnet test` needs a Docker daemon. Prefer real Postgres for anything that touches EF queries or
  migrations.
- **End-to-end over the real wiring:** `tests/Geef.Atelier.Tests/EndToEnd` starts the application against Postgres and replaces
  only the HTTP counterparts (D-067). Every new pipeline path gets a test here; data written by older versions has its own tests
  (`LegacyDataEndToEndTests`).
- **Browser:** Playwright tests in `tests/Geef.Atelier.Tests/Web/E2E` (`WebTestHost`); with `ATELIER_SCREENSHOT_DIR` some of them
  store screenshots.
- **Measuring tools:** `tools/Eval` (blind pairwise comparison), `tools/IntakeEval` (clarifier), `tools/FactCheckEval` (fact check),
  `tools/ResearchEval` (research). Changing the prompt or schema of the clarifier, the fact check or the passage picker makes the
  stored measurement invalid; for the clarifier and the fact check, tests with input hashes turn red until it is measured again.
- **Drift guards for the documentation:** the tool table in [13-ai-assistant-guide.md](13-ai-assistant-guide.md) and its German
  version is compared with the tools the server offers, without the stubs; every guide must exist in both languages and be listed for `/docs`. Every document
  served under `/docs` is rendered in a test: Mermaid blocks and HTML comments are left out (GitHub draws the diagrams), links to
  allow-listed documents go to `/docs/…`, links to other files of the repository go to GitHub, and links into the unpublished
  working folders become plain text.

## Rules that keep it honest

- Never claim more than was checked; never lose a finding silently. "What was checked" consists of fixed sentences and verbatim
  quotes only, never a model's own words about its work (D-073).
- Fail loud: a missing registration fails at start, a skipped source or failed reviewer becomes a run warning (D-067).
- Migrations are hand-written, additive and idempotent and need `[DbContext]` and `[Migration]`, otherwise EF skips them silently.
- The repository is public: no secrets, no operating details, no account names, no real orders in tests or examples.
- UI texts are English only (D-062); documentation is bilingual (English primary, German `_de.md`).
- About me is strictly per account: the result fields `TellOnce` and `FromAboutMe` are filled only for the owner of the order, the
  web component is rendered only for the owner, and `IProfileService.SavePartAsync` and `profile` write only into the profile of
  the signed-in user, and only into empty fields, in one locked statement (`IUserProfileRepository.FillEmptyAsync`); name, role and
  organization are one line without special characters, and no signature line may start with a field name of `ProfilePrompt.Labels`. The topics come from the stored reading of the request (`IntakeRecord`) and the profile block of the stored
  briefing, never from words in the text; the clarifier prompt and the briefing of orders without such a topic stay unchanged (D-083).

## Hosting

- Docker Compose with the web service, Postgres, the backup container and the CLI gateway `cli-proxy`; see the README for the first
  setup, backups and restore. The Postgres image is still `pgvector`, because older migrations create the `vector` extension.
- Deploy order when both change: first `cli-proxy`, then `web` (D-064, D-066).
- Deploy only when no run is `Pending` or `Running` (D-077).
- After every deploy run `scripts/atelier-smoke.sh` ([ops/atelier-smoke.md](ops/atelier-smoke.md)); `scripts/atelier-watch.sh`
  reports stuck runs and unhealthy services ([ops/atelier-watch.md](ops/atelier-watch.md)).
- Switches: see the [admin guide](14-admin-guide.md#switching-features).
