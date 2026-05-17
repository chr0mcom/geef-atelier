# Walking Skeleton — build plan

*[Deutsch](03-walking-skeleton-plan_de.md) · **English***

*Last updated: 2026-05-17 (documentation cleanup: removed a duplicated Step-1 status field and an accidentally appended prompt fragment; added a pointer to the post-skeleton history. Substantively a historical build plan — dated status/PS blocks are deliberately not back-dated.)*

The walking skeleton is the smallest end-to-end functional version of Geef.Atelier: a job is submitted via the UI or via MCP, a real Geef pipeline runs (with real LLM calls), live status is visible, the result is displayed and persisted. Source upload, classifier, dynamic crew, advisor, multi-format export — everything else comes later.

## Strategy

Every step is individually verifiable. No step assumes everything before it is perfect. After each step the system should be in a testable state. Steps are implemented in order because each builds on its predecessors — but where needed it is permissible to stop, refactor or rethink before the next one starts.

---

## Parallel migration tracks

While the numbered steps are worked through sequentially, there are parallel migration tracks for structural rebuilds triggered by events outside the walking-skeleton plan. They run on their own branches and are not automatically merged into main — the brainstorming maintainer decides the merge timing.

### M1 — provider migration to OpenAI-compatible APIs

**Branch:** `feature/openai-compatible-providers`
**Status:** ✅ **Completed on 10 May 2026.** Branch `feature/openai-compatible-providers` pushed (4 commits + 1 follow-up report commit). 31/31 tests green (9 without Docker, 22 more via the Postgres/orchestrator test container). Architect answers given for all six focal points — notable decision: the `LlmActor` enum exists only as type documentation, lookup via string keys. Workflow deviation: no formal R1–R5 reviewer passes (replaced by subagent self-reviews + build/test) — R2 catch-up pass after merge recommended. Report: [reports/migration-01-report.md](reports/migration-01-report.md). Details see decisions log D-018.
**Merge status:** ✅ **Completed** (push range `28daafb..ad90f65`). main now contains steps 1–7 + M1 together. Branch `feature/openai-compatible-providers` can be deleted.
**Open before step 7:** run the real-OpenRouter integration test (`AtelierPipelineRunsAgainstOpenRouter`) once with a real bearer key — verifies model-ID stability, tool-use behaviour, latency.
**Trigger:** D-017 — the Anthropic OAuth token is not accepted by the Messages API; a pay-as-you-go bearer key is avoidable; the multi-provider advantage is immediately usable.
**Scope:** replaces the Anthropic-specific LLM layer with an OpenAI-API-compatible adapter (default: OpenRouter). Per-actor model configuration. The tool-use format switches to the OpenAI `function` schema.
**Not in scope:** pipeline structure, event sink, persistence, orchestrator, domain model.
**Recommended merge timing:** before step 7 (UI), so the UI is built directly against the new provider contracts.
**Prompt:** [prompts/migration-01-openai-compatible-providers.md](prompts/migration-01-openai-compatible-providers.md)

---

## The ten steps

### Step 1 — solution setup with Postgres and EF Core

**Goal:** a runnable solution with all projects, Postgres connection via Npgsql and EF Core, the first migration created, Docker Compose for local development.

**Scope:**
- Solution `Geef.Atelier.sln` with four projects (Core, Infrastructure, Web, Mcp) plus Tests
- Geef SDK referenced (NuGet if available, otherwise documented how it is wired in)
- DbContext with the four entities (Runs, Iterations, Findings, Events)
- Migration created, runnable against local Postgres
- `docker-compose.yml` for local development with app + Postgres
- Health-check endpoint
- README in the repo

**Acceptance criteria:**
- `dotnet build` without errors or warnings over skeleton code
- `dotnet ef database update` runs successfully against Postgres
- `docker compose up` starts app and DB; health check answers 200 OK
- Tests project contains at least one smoke test (DbContext loads, migration runs in the test DB)

**Status:** ✅ **Completed on 10 May 2026.** 1 reviewer iteration, all 5 reviewers passed (1 CRITICAL + 4 MAJOR findings, all fixed). 9 conventional commits. Report: [reports/step-01-report.md](reports/step-01-report.md). Details see decisions log D-010.

---

### Step 2 — pipeline skeleton with stub providers

**Goal:** the Geef pipeline runs with elaborate stub providers, without real LLM calls. Proves that the convergence loop and event sink work.

**Scope:**
- `BriefingGroundingStep` (stub: write the briefing into the context)
- `LlmExecutionStep` (stub: echo + iteration marker)
- Two `LlmReviewer` (stub: iteration 1 = findings, iteration 2+ = no findings)
- `MarkdownFinalizer`
- Pipeline-builder configuration with `MaxIterationsPolicy(3)` and `ParallelEvaluationStrategy`
- A simple console test that runs the pipeline once

**Acceptance criteria:**
- Pipeline runs 2 iterations and converges
- All Geef events are logged to the console
- The final output contains the expected marker

**Status:** ✅ **Completed on 10 May 2026.** 1 reviewer iteration, all 5 reviewers passed with 0 actionable findings. 7/7 tests green (5 step-1 tests + 2 new pipeline tests). 6 SDK-reality corrections vs. the build prompt (FindingSeverity enum, DefaultConvergencePolicy, UseMiddleware generic, evaluation event names, IterationHistory workaround, namespace alias). Report: [reports/step-02-report.md](reports/step-02-report.md). Details see decisions log D-012.

---

### Step 3 — Anthropic client and real providers

**Goal:** replace the stubs with real Anthropic API calls.

**Scope:**
- `IAnthropicClient` with `CompleteAsync(systemPrompt, userPrompt, options)`
- HTTP implementation against `/v1/messages`
- API key from `IConfiguration` (`ANTHROPIC_API_KEY`)
- `LlmExecutionStep` calls Anthropic with the executor system prompt + briefing + PreviousFindings
- `LlmReviewer` calls Anthropic with the reviewer system prompt + artifact; reviewer output as structured JSON
- Define `ReviewerResponseSchema` (findings: [{severity, message}])

**Acceptance criteria:**
- The pipeline runs with a real Anthropic model and converges (with a trivial briefing)
- Token usage is captured and reported in the final output
- Structured reviewer output is parsed correctly

**Status:** ✅ **Completed on 10 May 2026.** 1 reviewer iteration, 2 MAJOR findings fixed before phase 4 (defensive JSON deserialization). 11/11 tests green (4 new mock tests + 7 regression). Anthropic tool use with `submit_review`, `Microsoft.Extensions.Http.Resilience` via `AddStandardResilienceHandler`, `ConvergenceFailedException` for `AbortOnCritical=true` verified. 14 conventional commits. Report: [reports/step-03-report.md](reports/step-03-report.md). Details see decisions log D-013.

**Open:** the integration test `AtelierPipelineRealAnthropicTests` was not run with a real API key — catch up before step 5.

**Note:** the Anthropic-specific LLM layer established in step 3 is replaced by migration M1 (see above) with an OpenAI-API-compatible provider layer. The pipeline structure and convergence logic from step 3 remain unchanged; only the client adapter and the configuration records change. Details see D-017 in the decisions log.

---

### Step 4 — event sink and persistence

**Goal:** every run is stored in Postgres with all its iterations, findings and events.

**Scope:**
- `PostgresEventSink` (implementation of `IGeefEventSink`)
- Iteration snapshots are extracted and persisted at `ExecutionPhaseCompleted`
- Findings are persisted at `EvaluationPhaseCompleted`
- Token and cost accumulation per run

**Acceptance criteria:**
- After a pipeline run, the DB contains: one run, several iterations, findings (for the iterations in which any were found), and a complete event log
- No duplicate event, no lost iteration

**Status:** ✅ **Completed on 10 May 2026.** 1 reviewer iteration, 1 MAJOR finding (volatile annotation for `_lastExecutionContext`) fixed. 15/15 tests green (4 new persistence tests + 11 regression). PostgresEventSink with variant-A RunId propagation, IRunPersistenceService in Core, typed token tracking via `ContextKey<AnthropicTokenUsage>`, critical-abort findings from `PipelineFailedEvent.History` (SDK verified via decompilation). 13 conventional commits. Report: [reports/step-04-report.md](reports/step-04-report.md). Details see decisions log D-015.

**Open (deferred):** `AtelierPipelineRealAnthropicTests` with a real API bearer key — no key available in the session environment. Real run in step 5 or later, once a bearer key is provided.

---

### Step 5 — RunOrchestratorService

✅ **Completed on 10 May 2026.** 1 reviewer iteration, 6 findings (all fixed). Report: [docs/reports/step-05-report.md](reports/step-05-report.md). D-016.

**Goal:** asynchronous job processing via a `BackgroundService`. Jobs are written to the DB with status `Pending`; the service picks them up, runs the pipeline, writes the result back.

**Scope:**
- `RunOrchestratorService : BackgroundService`
- Polling interval (2 seconds default) for `Pending` runs; atomic `Pending→Running` claim
- `SemaphoreSlim` concurrency gate + task tracking (`_runTasks`) with drain on stop
- Crash recovery at service start: all `Running` runs → `Failed/"Service restarted"`
- Cancellation strategy γ: only `StoppingToken`; `OverrideToAbortedAsync` with `CancellationToken.None`
- `OrchestratorOptions` (PollingInterval, MaxConcurrentRuns) in `Core/Configuration/`
- `GatedFakeAnthropicClient` for deterministic concurrency tests

**Acceptance criteria:**
- ✅ Several runs processed automatically one after another (E2E Pending→Completed)
- ✅ App restart marks running runs as Failed/"Service restarted"
- ✅ Never more than MaxConcurrentRuns=2 runs simultaneously (5/5 deterministic)
- ✅ StopAsync mid-flight → status=Aborted
- ✅ 19/19 tests green; AC8 skipped (OAuth-only)

**Status:** ✅ **Completed on 10 May 2026.** 1 reviewer iteration; 4 MAJOR R2 (drain race, test-precondition guards) + 2 MAJOR R4 (doc updates) — all fixed. 19/19 tests green (4 new orchestrator tests + 15 regression), concurrency test 5/5 deterministic via `GatedFakeAnthropicClient`. Atomic Pending→Running claim, `SemaphoreSlim` + `ConcurrentDictionary<Guid, Task>` + `WhenAll` drain, crash recovery at service start, cancellation via option γ (only StoppingToken). 11 conventional commits. Report: [reports/step-05-report.md](reports/step-05-report.md). Details see decisions log D-016.

**Open (deferred):** AC8 (real-Anthropic test with a bearer key) — skipped a 3rd time due to an OAuth-only token in the session. `CancelRunAsync` as a stub implementation follows in step 6 together with the DB-flag migration.

---

### Step 6 — IRunService as the application service layer

**Goal:** a clean application-logic layer consumed by both frontends (web UI and MCP server).

**Scope:**
- `IRunService` interface in a new `Geef.Atelier.Application` project (option B, user confirmed)
- Methods: `SubmitRunAsync`, `GetRunAsync`, `ListRunsAsync`, `CancelRunAsync`
- `IRunRepository` in Core, `RunRepository` in Infrastructure (variant β — no infra dep in Application)
- `RunEntity.CancellationRequested` flag + EF migration `Step06Cancellation`
- Cancellation watcher in the orchestrator (pattern A, per-run, polls the DB every `CancellationPollingInterval`)
- DI registration `AddAtelierApplication()` + `AddAtelierApplication()` in Program.cs

**Acceptance criteria:**
- ✅ `SubmitRunAsync` + `GetRunAsync`: end-to-end Pending→Completed
- ✅ `ListRunsAsync`: sorted by `CreatedAt desc`, filterable by status
- ✅ `CancelRunAsync` mid-flight: DB flag → watcher → CTS → pipeline OCE → Aborted
- ✅ `CancelRunAsync` for a terminal run: false (idempotent)
- ✅ Input validation: empty/null briefing, null configJson, invalid JSON
- ✅ `dotnet test`: 31/31 green (5 new application tests + 26 regression)
- ✅ AC9: skipped — no live API key in the session (escalation note before step 9)

**Status:** ✅ **Completed on 10 May 2026.** 2 reviewer iterations, 2 R2-MAJOR findings (ServiceProvider disposal, test race) fixed. 31/31 tests green (5 new application tests). Variant β (application layer without an Infrastructure dep, IRunRepository in Core), cancellation watcher pattern A (per-run task), DB flag `RunEntity.CancellationRequested` with migration `Step06Cancellation`. 6 conventional commits. Report: [reports/step-06-report.md](reports/step-06-report.md). Details see decisions log D-019.
Details see decisions log D-017 (step-6 section)

---

### Step 7 — Blazor UI

**Status:** ✅ **Completed on 11 May 2026.** 2 reviewer iterations, 1 R2-CRITICAL (missing try/catch in `SignalRRunNotifier`) fixed — double fail-safe pattern established. 55/55 tests green (4 new bUnit + 4 new Playwright E2E + existing persistence/orchestrator/application). Three pages (`/new`, `/runs`, `/runs/{id}`), 9 UI components in `Components/UI/` with scoped CSS, SignalR hub `RunHub` with two groups (`run-{id}` + `all-runs`), `IRunNotifier` in Core and `SignalRRunNotifier` in Web as a singleton. **AC8 finally green:** OpenRouter real pipeline verified with 5–12s latency and 174–523 tokens per run. 12 conventional commits in `main`. Report: [reports/step-07-report.md](reports/step-07-report.md). Details see decisions log D-020.

**Workflow decision at this stage:** plan-phase integration establishes itself as the architect form (used since step 5); `geef_architecture.md` as a mandatory artifact is in practice equivalently replaced by plan documents — R4 checks architecture compliance against the plan. Atelier interpretation of the "no HTML in pages" rule: trivial page controls (simple `<button>`/`<div>` without state) may remain in pages, only reusable UI **logic** must live in `Components/UI/`.

---

### Step 8 — auth (cookie for UI, token for MCP preparation)

**Goal:** the application is no longer reachable unprotected on the internet.

**Prerequisite:** steps 1–7 + M1 in main. AC8 (real-OpenRouter test) green. Step 8 builds on the established UI layer (three pages + SignalR hub) and adds auth middleware + a login page. Single-user setup with cookie-based auth.

**Scope:**
- Cookie auth for the web UI; one user from environment variables
- Login page (static SSR), logout endpoint (`POST /auth/logout`)
- Bearer-token auth scheme prepared for the MCP server (activated in the next step)
- The health check stays unauthenticated

**Acceptance criteria:**
- ✅ Without login: redirect to the login page (`/login?ReturnUrl=…`)
- ✅ With login (admin/DevPassword! as the dev default): all UI routes reachable, logout button visible
- ✅ Wrong credentials: login fails, "Invalid credentials" banner, no cookie
- ✅ Logout → cookie deleted, subsequent auth routes redirect to /login again
- ✅ `/health` still anonymous (AllowAnonymous)
- ✅ `tools/HashPassword` CLI for BCrypt-hash generation
- ✅ 71/71 tests green (55 existing + 16 new)

**Status:** ✅ **Completed on 11 May 2026.** 4 reviewer iterations (R1–R5 all 0 findings). 71/71 tests green (55 regression + 4 application-auth + 6 bUnit + 6 Playwright E2E). Cookie auth: BCrypt wf=11, 30d SlidingExpiration, HttpOnly, SameSite=Strict, SecurePolicy dev/prod switch. Login as static SSR (`@formname` mandatory). Logout via `POST /auth/logout` with AntiforgeryToken. `TestAuthenticationHandler` in tests for bypass. Arch trade-off: RunHub without `[Authorize]` (Blazor Server server-side HubConnection cannot forward browser cookies — the SSR pre-render would receive 401). `ForwardedHeaders` middleware before `UseAuthentication` (Traefik-TLS preparation). 13 conventional commits. Report: [reports/step-08-report.md](reports/step-08-report.md). Details see decisions log D-021.

---

### Step 9 — MCP server ✅

**Goal:** a second frontend adapter alongside the web UI. External MCP clients (Claude Desktop, Claude Code, own agents) can submit jobs, query status, fetch results.

**Prerequisite:** steps 1–8 + M1 in main. App reachable at `95.216.100.213:8080` with cookie auth already. Step 9 adds a second auth path (bearer token) and a second frontend adapter (MCP) — the existing cookie UI stays unchanged.

**Architectural implication:** with step 9 the system has, for the first time, **two frontends running in parallel** over the same application service layer (`IRunService`). This is the actual litmus test for the layering discipline of the previous steps. If `IRunService` and its contracts are clean enough, MCP should require no pipeline/domain/orchestrator changes.

**Scope:**
- `Geef.Atelier.Mcp` as a **class library** (tool definitions), hosted in the Web project
- Use of `ModelContextProtocol.AspNetCore` v1.3.0 (the official Anthropic+Microsoft SDK)
- Transport: Streamable HTTP (Stateless=true), endpoint `/mcp`
- Tools:
  - `submit_request(briefing, options?)` → returns a new run ID
  - `get_run_status(run_id)` → current status with phase, iteration, tokens, cost
  - `get_run_result(run_id)` → final text (only when status=Completed)
  - `list_runs(limit?, status_filter?)` → list of recent runs
  - `get_run_details(run_id)` → full details with iterations and findings
  - `cancel_run(run_id)` → returns `bool`
- Multi-auth: cookie (UI, default scheme) + bearer (MCP, via `McpPolicy`)
- `ITokenValidator` in Application, `BearerTokenHandler` in Web
- All tools call `IRunService` (no direct DB access)

**Acceptance criteria:**
- The MCP inspector can connect and list all tools
- Submit a job via MCP, follow it live in parallel in the web UI
- Fetching the result via MCP matches the one in the UI

**Status:** ✅ **Completed on 11 May 2026.** 1 reviewer iteration, 0 critical/important findings (1 R2-minor about a dummy FixedTimeEquals fixed). 85/85 tests green (14 new: 3 StaticTokenValidator + 4 BearerHandler + 5 MCP unit + 2 MCP E2E). MCP library: `ModelContextProtocol.AspNetCore 1.3.0` (officially Microsoft+Anthropic). `Geef.Atelier.Mcp` as a class library (no second web host). Multi-auth: cookie default + bearer via `McpPolicy`, no cross-interference. Endpoint `/mcp` in the web host, `RunEntity.CreatedByUser` as audit-trail preparation. 14 conventional commits. Report: [reports/step-09-report.md](reports/step-09-report.md). Details see decisions log D-022 (written by the executor itself).

**Litmus test passed:** two frontends running in parallel (UI via cookie, MCP via bearer) use the same `IRunService` without any intervention in pipeline/domain/orchestrator code. The onion architecture holds.

**Known tech debt for post-skeleton:** `LiveUpdateFlowTests` (E2E) shows occasional timeouts under a full test run when resources are scarce. Stable when run individually. Not a blocker for step 10.

---

### Step 10 — Dockerfile and Compose setup for production

**Status:** ✅ Completed on 2026-05-11. 1 reviewer iteration, 0 findings. Report: docs/reports/step-10-report.md. D-023.

**Goal:** deployable on the target server.

**Prerequisite:** all 9 steps + M1 in main. The app container already runs at `95.216.100.213:8080` (direct port from step-8 R5). 85/85 tests green, all frontends (UI + MCP) functionally verified.

**Scope reduction vs. the original plan form:** step 10 is not a classic "initial deploy" — the container is already functional on the target server. Step 10 is **routing-and-TLS setup**: Traefik labels for `geef.stefan-bechtel.de`, a Let's Encrypt certificate, turning off direct port exposure, enabling cookie `SecurePolicy.Always`. Effort: significantly less than initially planned.

**Already prepared from the step-9 report's recommendations section:** the complete env-var list, a Traefik label template, the migration strategy (auto-on-startup stays), a SignalR-WebSocket routing note. In phase 1.4 the architect mainly checks alignment with the existing Traefik server convention in `/srv/CLAUDE.md` or `/srv/docker/docs/`.

**Scope:**
- Multi-stage `Dockerfile` (.NET 10 SDK build → ASP.NET Core 10 runtime)
- Non-root user in the container
- Healthcheck in the image
- `docker-compose.prod.yml` for production: only the app, the Postgres connection string points at the existing server Postgres instance
- Environment-variable documentation (`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `OPENROUTER_API_KEY`, `POSTGRES_CONNECTION_STRING`, `ATELIER_USER`, `ATELIER_PASSWORD_HASH`, `ATELIER_MCP_TOKEN`)
- The migration runs automatically at container start (or as an init container/migration job)

**Acceptance criteria:**
- The container builds and starts without manual intervention
- The app connects to the existing Postgres instance
- Health check and auth work behind the reverse proxy

**Note from the step-8 report:** the app already runs at `95.216.100.213:8080` (without Traefik routing) and was verified there by R5 for the auth flows. Step 10 is therefore no longer a classic "first deploy" but a **routing-and-domain setup**: Traefik labels for `geef.stefan-bechtel.de`, TLS termination, possibly production hardening (e.g. revisiting the migration strategy). The effort is considerably reduced because the container image is already functional.

---

## What is deliberately NOT in the skeleton

To keep the scope clear:

- **Source upload and RAG** (comes with pgvector in a later step)
- **Classifier and dynamic crew composition** (the crew is hard-wired in the skeleton)
- **Multi-provider adapter** for OpenAI and OpenRouter (the skeleton uses only Anthropic; reviewers can use the same provider API with a different model)
- **Advisor integration** (the skeleton does not yet use the Geef advisor pattern)
- **Real crash resume** with resumption at the last completed phase (the skeleton does naive Failed-marking)
- **Cost-budget caps** with abort on overrun
- **Export to DOCX/PDF** (the skeleton produces Markdown)
- **Crew templates and reviewer profiles** as versioned data (the skeleton has two hard-coded reviewers)
- **OAuth 2.0** for MCP (the skeleton uses a bearer token)

## Post-skeleton steps

### PS-1: Postgres backup ✅ (2026-05-11)
Automatic daily backup service (`prodrigestivill/postgres-backup-local:16`), retention 7/4/6, volume `geef-atelier-backups`, restore script `scripts/restore-backup.sh`. D-024. Report: `docs/reports/post-skeleton-01-postgres-backup-report.md`.

### PS-2: reviewer calibration ✅ (2026-05-12)
4-level severity taxonomy (critical/major/minor/info) with an anti-pattern rule + the Hadwiger–Nelson negative example in the reviewer prompts. `ConvergenceOptions` (`AbortOnCritical=false` default). Tool-schema update. Executor iteration-2+ sharpening. 11 new tests. D-025. Report: `docs/reports/post-skeleton-02-reviewer-calibration-report.md`.

> **Note:** this is the historical walking-skeleton build plan. All ten skeleton steps as well as the parallel migration track M1 are completed. The complete post-skeleton history — design system (PS-3), CLI-provider adapter/split (PS-4), crew system (PS-5/6), advisor passes (PS-7), grounding providers & vector-store RAG, run attachments, cost tracking, Template Studio, domain templates, MCP OAuth 2.1, multi-user and run-user isolation — is documented chronologically in the [decisions log](05-decisions-log.md) (entries D-024 ff.) and in overview form in the [project README](../README.md). The PS-1/PS-2 entries above are the original state of this plan and are deliberately not amended retroactively, as a historical note.
