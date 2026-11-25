# Decisions Log

*[Deutsch](05-decisions-log_de.md) · **English***

*Last updated: 2026-11-24 (D-070 addendum: conservative length check and upload answers; D-069 added: stage 1 direct-path engine; D-070 added: stage 1 orders, sources and display state; D-067 added: stage 0 safety net; D-068 added: direction of the rebuild with order, direct path, questions, sources and memory; before that 2026-11-23, D-066 extended incl. /12 explicit model_domain and a single assignment rule: codex always-latest aliases, nightly host sync, review hardening; before that 2026-11-22, D-066 added: model matrix by role and domain, Claude/OpenAI only via the cli-proxy; before that 2026-11-12, D-065 added: image generation in the cli-proxy; before that 2026-08-04, D-064 added: dynamic model catalog for CLI providers, incl. the guard rule and the post-deploy checklist)*

Chronological log of all decisions from the brainstorming.

## 10 May 2026 — first brainstorming

### D-001 to D-009 (condensed)
Generic use case, fire-and-forget, Blazor Server, Postgres, MCP as a second interface, Geef.Atelier, walking skeleton, canonical `geef_workflow.md`.

### D-010: Step 1 — solution setup
Geef.Sdk 1.0.0-ci.1, `.slnx`, UI component library, auto-migration with try-catch.

### D-011: architect workflow + Atelier fallback
(A) Phase 1.4 with a fallback sequence. (B) Atelier level 4: the executor writes it itself.

### D-012: Step 2 — SDK reality facts
Six corrections: SDK `FindingSeverity { Info, Warning, Error, Critical }`; `DefaultConvergencePolicy`; `UseMiddleware<T>()`; only `EvaluationApprovedEvent`/`RejectedEvent`; `IterationHistory` workaround; `using SdkGeef = Geef.Sdk.Geef;`.

### D-013: Step 3 — Anthropic client (replaced by M1)
Reality facts about the original Anthropic layer. The concepts (tool use, defensive JSON, API key per request, resilience) remain valid in M1.

### D-014: production domain for step 10
`geef.stefan-bechtel.de`, IP `95.216.100.213`, Traefik with TLS.

### D-015: Step 4 — event sink and persistence
`IRunPersistenceService`, `PostgresEventSink` with an injected `Guid runId`, severity mapping via `ToAtelierSeverity()`, token tracking via a context key, critical-abort from `PipelineFailedEvent.History` (SDK decompilation), `_lastExecutionContext` as `volatile`, `IServiceScopeFactory.CreateAsyncScope()` per event.

### D-016: Step 5 — RunOrchestratorService
Atomic Pending→Running claim, `SemaphoreSlim` + `ConcurrentDictionary<Guid, Task>` + `WhenAll` drain, `OverrideToAbortedAsync` with `CancellationToken.None`, `_runCts` dictionary for cancellation reaction. `OrchestratorOptions` in Core.

### D-017: provider-strategy switch (M1 trigger)
Switch from Anthropic-specific to OpenAI-API-compatible via OpenRouter. Per-actor model mapping.

### D-018: migration M1 completed
Branch `feature/openai-compatible-providers`. `ILlmClient`, `OpenAiCompatibleClient`, `LlmOptions` with per-actor mapping. ToolChoice as a string convention, string keys for actor lookup, no `BaseAddress` on the HttpClient, `anthropic-version` header removed. Workflow deviation: no formal R1–R5 passes.

### D-019: Step 6 — IRunService application layer + cancellation
Variant β (own Application project without an Infrastructure dep, IRunRepository in Core). Cancellation watcher pattern A (per-run task), DB flag `RunEntity.CancellationRequested` with migration `Step06Cancellation`. OCE catch-filter order: first service stop, then user cancel. R2 fixes: ServiceProvider disposal, polling loop instead of Task.Delay.

### D-020: Step 7 completed — Blazor UI, SignalR, AC8 green

**Date:** 11 May 2026
**Report:** [reports/step-07-report.md](reports/step-07-report.md)
**Branch:** `main` — M1 + step 6 + step 7 all in main (push range `28daafb..ad90f65`).
**Reviewer iterations:** 2 (iteration 1 with 1 R2-CRITICAL, iteration 2 green).
**Tests:** 55/55 green. **12 conventional commits.**

**Architect consultation:** form: **plan-phase integration** (no separate architect invocation, all decisions fixed in the plan document). Establishes itself as the standard form for steps 5–7. Answers to the six focal points from the step-7 prompt:

| Focal point | Decision |
|---|---|
| (F1) SignalR mechanics | Variant α (browser HubConnection) — MCP-consistent, Playwright-testable |
| (F2) Hub event granularity | Only `RunId` without a payload — the UI re-fetches via `IRunService` |
| (F3) CSS strategy | Scoped `.razor.css` per component — consistent with `MainLayout.razor.css` and `ReconnectModal.razor.css` |
| (F4) Form validation | `EditForm` + `DataAnnotationsValidator` (standard Blazor) |
| (F5) Test host setup | Hybrid: `WebApplicationFactory<Program>` + Kestrel on port 0 (Playwright needs a real HTTP listener) |
| (F6) Notifier layer | `IRunNotifier` in Core, `SignalRRunNotifier` in Web as a singleton (sink tests via `NoOpRunNotifier`, no SignalR mocks) |

**Fixed reality facts from step 7 (binding from step 8):**

**(a) `IRunNotifier` in `Geef.Atelier.Core/Notifications/`:**
- Frontend-agnostic contract. The Infrastructure sink consumes it without a Web dep.
- Method: `NotifyRunUpdatedAsync(Guid runId, CancellationToken ct)`.

**(b) `RunHub` in `Geef.Atelier.Web/Hubs/`:**
- Two groups: `run-{runId}` (for the detail page) and `all-runs` (for the list page).
- Constant `AllRunsGroup` for a type-safe reference.
- Four methods: `JoinRunGroupAsync`, `LeaveRunGroupAsync`, `JoinAllRunsGroupAsync`, `LeaveAllRunsGroupAsync`.
- Endpoint: `app.MapHub<RunHub>("/hubs/runs")`.

**(c) `SignalRRunNotifier` in `Geef.Atelier.Web/Notifications/`:**
- `internal sealed`, singleton lifetime.
- Injects `IHubContext<RunHub>`.
- Sends `"RunUpdated"` to the `run-{runId}` group **and** `"AnyRunUpdated"` to the `all-runs` group.
- **Best-effort:** both `SendAsync` calls individually wrapped in `try { } catch { }` (R2-CRITICAL fix). Double fail-safe together with the sink wrapper.

**(d) `PostgresEventSink` constructor extension:**
- New parameter `IRunNotifier notifier` as the third parameter (after `scopeFactory`, before `logger`).
- After a successful persist: `await notifier.NotifyRunUpdatedAsync(atelierRunId, ct)` in its own `try/catch` with a warning log.
- Sink tests use `NoOpRunNotifier` (no SignalR mock needed).

**(e) Pages with hub lifecycle (`IAsyncDisposable`):**
- `/new` (`New.razor`): `EditForm` with `DataAnnotationsValidator`, submit → `IRunService.SubmitRunAsync` → redirect to `/runs/{id}`.
- `/runs` (`Runs.razor`): lists 20 runs, status-filter buttons, `JoinAllRunsGroupAsync` for live updates of the list.
- `/runs/{RunId:guid}` (`RunDetail.razor`): full details, `JoinRunGroupAsync(runId)` for live detail updates.
- All pages: `WithAutomaticReconnect()` + a `Reconnected` handler re-joins the group and re-fetches the state.

**(f) Nine UI components in `Components/UI/`:**
- `StatusBadge`, `SeverityBadge`, `RunCard`, `IterationPanel`, `FindingItem`, `RunHeader`, `SubmitForm`, `EmptyState`, `CancelButton`.
- All with a `.razor.css` file (scoped CSS).
- Reusable: `StatusBadge` and `SeverityBadge` are used multiple times in `RunCard`, `RunHeader`, `FindingItem`.

**(g) Atelier interpretation of the "no HTML in pages" rule (R4-MINOR precedent):**
- The workflow hard rule requires UI logic in `Components/UI/`. But: trivial page controls (a simple `<button>` with an `onclick` handler, a `<div>` container) remain allowed in pages.
- Example from step 7: 6 inline filter buttons in `Runs.razor` were not extracted into a `FilterBar` component. Rationale: extraction adds more complexity than benefit at this triviality. R4 flagged it as MINOR, deliberately left unfixed.
- **Atelier interpretation:** "UI component" means reusable UI **logic**, not every HTML element. If an element occurs in only one place, has no own state and contains no 3+ lines of HTML logic, it may stay in the page.

**(h) Test infrastructure:**
- **bUnit** (`Microsoft.AspNetCore.Components.Testing`) for component unit tests: 4 new tests (`StatusBadgeTests`, `SeverityBadgeTests`, `RunCardTests`, `SubmitFormTests`).
- **Playwright** (`Microsoft.Playwright`) for E2E tests: 4 flow tests (`SubmitFlowTests`, `ListFlowTests`, `LiveUpdateFlowTests`, `CancelFlowTests`).
- `PlaywrightCollection` + `PlaywrightFixture`: `[Collection("Playwright")]` with a shared Chromium browser, Docker flags `--no-sandbox`, `--disable-setuid-sandbox`, `--disable-dev-shm-usage`, `--shm-size=2gb`.
- `WebTestHost`: `WebApplicationFactory<Program>` + `UseKestrel()` + port 0; `BaseUrl` from `IServerAddressesFeature`; overrides `ILlmClient → GatedFakeLlmClient`, connection string → `PostgresFixture`, `MaxConcurrentRuns = 10`.

**(i) Cancel-race solution (step-7 insight):**
- In `CancelFlowTests`, releasing the gate after a cancel click is **counterproductive** — it would let `FakeLlmClient` (synchronous `Task.FromResult`) race to the Completed state in < 200ms before the watcher CTS can cancel.
- The `OverrideToAbortedAsync` filter `Status IN (Running, Failed)` excludes `Completed` — race lost.
- **Solution:** keep the gate closed. `SemaphoreSlim.WaitAsync(cancelledToken)` throws `OperationCanceledException` immediately, even without a permit.
- Insight for future steps: cancellation tests with mock LLMs must artificially slow the pipeline, otherwise the race is lost.

**(j) `Program.cs` and `appsettings.json`:**
- `builder.Services.AddSignalR();`
- `builder.Services.AddSingleton<IRunNotifier, SignalRRunNotifier>();`
- `app.MapHub<RunHub>("/hubs/runs");`
- No new `appsettings.json` sections for UI/SignalR.

**Workflow interpretation: AC9 (`geef_architecture.md` existence) as "N/A":**
The report rates AC9 as "N/A" with the rationale "architecture decisions documented in the plan; R4: 0 CRITICAL/MAJOR". `geef_workflow.md` itself requires `geef_architecture.md` as a mandatory artifact (hard rule from D-011(A)). **In practice, plan-phase integration establishes itself** as an equivalent fulfillment — the plan contains the architectural decisions, R4 checks compliance against it. A workflow update would be consistent but is the maintainer's call.

**AC8 FINALLY green (real-OpenRouter test):**
- **Latency:** 5–12 seconds for the full pipeline (1 iteration, executor + 2 reviewers).
- **Token usage (R5 live runs):** 349 tokens (run 1), 174 tokens (run 2), 523 tokens (a separate test run mentioned in the report).
- **Cost implication:** with Claude Opus 4.7 via OpenRouter roughly 1–2 cents per skeleton run. Cost tracking in step 10 less dramatic than initially feared.
- **Docker setup:** `Llm__ApiKey` injected as an env var via the `-e` flag; `appsettings.Development.json` (gitignored since commit `28daafb`) used locally.

**Workflow observation — `appsettings.Development.json` gitignored:**
Since commit `28daafb`, `appsettings.Development.json` is no longer tracked. Rationale: it contains the OpenRouter bearer key. A safe pattern for local development. The production setup uses env vars (`Llm__ApiKey`). If a later maintainer asks why the file is missing: that is security discipline, not forgetfulness.

**Recommendations for step 8 (cookie auth — from report section 8):**
1. Login page `Components/Pages/Login.razor` with `HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ...)`.
2. User credentials via `ATELIER_USER` + `ATELIER_PASSWORD_HASH` environment variables. BCrypt hash recommended.
3. `AddAuthentication(...).AddCookie(...)` with a 30-day cookie lifetime.
4. `[Authorize]` on `RunHub` and pages.
5. `<AuthorizeView>` for conditional UI, `CascadingAuthenticationState` in `App.razor`.
6. E2E tests: `WebTestHost` can disable or mock the auth middleware — new auth-flow tests separately.

---

### D-021: Step 8 completed — cookie auth, login/logout, 71/71 tests green

**Date:** 11 May 2026
**Report:** [reports/step-08-report.md](reports/step-08-report.md)
**Branch:** `main` directly
**Reviewer iterations:** 4 (R1–R5 all completed with 0 findings)
**Tests:** 71/71 green. **13 conventional commits.**

**Architect consultation:** form: **plan-phase integration** (six focal points fixed in the plan document; no separate architect subagent).

**Fixed reality facts from step 8 (binding from step 9):**

**(a) Cookie-auth configuration:**
- Cookie name: `Atelier.Auth`; `HttpOnly=true`; `SameSite=Strict` (prod), `Lax` (test env); `SecurePolicy`: `SameAsRequest` (dev), `Always` (prod); `ExpireTimeSpan=30d`, `SlidingExpiration=true`; `LoginPath="/login"`.

**(b) Layer placement:**
- `IUserAuthenticator` interface in `Geef.Atelier.Application/Auth/` (not Infrastructure — auth is application logic).
- `AtelierUserOptions` in `Geef.Atelier.Core/Configuration/` (POCO without deps).
- `AtelierUserAuthenticator` (`internal sealed`) in Application.
- `ApplicationAuthExtensions.AddAtelierAuth()` binds options + registers a scoped `IUserAuthenticator`.

**(c) Login page as static SSR:**
- `Login.razor` without `@rendermode` (static SSR mandatory — `HttpContext.SignInAsync` needs an HTTP request context, not a WebSocket).
- `@formname="login-form"` on `<form method="post">` — mandatory for Blazor static SSR form routing (without it leads to HTTP 400 "POST does not specify which form").
- `OnInitializedAsync` checks `HttpContext.Request.Method == "POST"`.

**(d) Logout via `POST /auth/logout`:**
- Minimal-API endpoint in `AuthEndpoints.cs`; `.RequireAuthorization()`.
- `UserMenu` component (`AuthorizeView`, `<form method="post" action="/auth/logout">` + `<AntiforgeryToken />`).
- `SignOutAsync` in a `try/catch` (the test env has no cookie-auth handler in the authenticated=true variant → ignore `InvalidOperationException`).

**(e) `[Authorize]` on pages — Index stays anonymous:**
- `@attribute [Authorize]` on: `New.razor`, `Runs.razor`, `RunDetail.razor`.
- `Index.razor` stays anonymous — a welcome page with quick links to protected pages; `AuthorizeRouteView` redirects when auth is needed.

**(f) RunHub WITHOUT `[Authorize]` — an architectural trade-off (deviation from D-020 recommendation #4):**
- Blazor Server's `HubConnectionBuilder` creates server-side SignalR connections. Browser auth cookies are **not** forwarded.
- With `[Authorize]` on `RunHub`: the SSR pre-render phase receives 401, Blazor circuit initialization fails.
- Mitigation: all subscribing pages carry `@attribute [Authorize]` → unauthenticated users cannot reach the pages (and thus the hub).
- R2 and R4 accepted this trade-off explicitly.

**(g) `TestAuthenticationHandler` and `WebTestHost` extension:**
- `TestAuthenticationHandler` in `tests/Geef.Atelier.Tests/Web/E2E/`, `internal sealed`.
- `WebTestHost.StartAsync(bool authenticated = true)` — `true`: test handler (all requests pre-authenticated), `false`: real cookie auth with a BCrypt-wf=4 hash.
- **Security rule:** the handler must never be referenced in `Program.cs` or `Geef.Atelier.Web.csproj`.

**(h) `tools/HashPassword/` mini CLI:**
- `BCrypt.Net.BCrypt.HashPassword(args[0], workFactor: 11)`.
- Included as a solution project in `Geef.Atelier.slnx`.
- Dev default hash in `docker-compose.dev.yml`: corresponds to `"DevPassword!"` (local development only).

**(i) `UseForwardedHeaders` before `UseAuthentication`:**
- Mandatory for Traefik TLS termination in production. `KnownIPNetworks.Clear()` for Docker-network invariance.

**(j) `/health` with `AllowAnonymous()`:**
- The health probe stays unauthenticated. Reverse-proxy routing and container-lifecycle management require anonymous access.

**(k) BCrypt work factor 11:**
- Production: wf=11 (~80ms, single-user → acceptable latency). Tests: wf=4 (fast, deterministic).

**(l) Lazy-fail on missing configuration:**
- The service starts without `ATELIER_USER`/`ATELIER_PASSWORD_HASH`. Login returns `false` without a crash. The health check stays green. An init warning without PII is logged.

**(m) UI component library extended:**
- New in `Components/UI/`: `LoginForm.razor`, `UserMenu.razor`, `RedirectToLogin.razor` — all with scoped `.razor.css`.
- `EmptyLayout.razor` new in `Components/Layout/` — a minimal wrapper without NavMenu for the login page.

**(n) `no-store` cache-control middleware:**
- `ctx.Response.Headers.CacheControl = "no-store, no-cache"` for authenticated responses — prevents a browser-back-button cache leak.

**(o) `AddCascadingAuthenticationState()` instead of `<CascadingAuthenticationState>` in Routes.razor:**
- `.NET 8+` service registration replaces the component wrapper. Both together lead to double registration and an `IComponentRenderMode` conflict.

**Recommendations for step 9 (MCP server):**
1. Multi-auth scheme: `AddAuthentication().AddCookie().AddScheme<BearerTokenHandler>(...)` — cookie for the UI, bearer for MCP coexist.
2. `ITokenValidator` interface in `Geef.Atelier.Application/Auth/` (symmetric to `IUserAuthenticator`).
3. `ATELIER_MCP_TOKEN` as an env var, `MCP-TOKEN` header in requests.
4. The MCP server project needs `[Authorize(AuthenticationSchemes = "Bearer")]` on its endpoints.
5. No bearer-token access to UI Blazor routes needed — clear separation.

---

## D-022 — Step 9: MCP server with bearer-token auth (2026-05-11)

**Context:** a second frontend for external AI agents (Claude Desktop, Claude Code) over the Model Context Protocol.

**Decisions:**
- (a) MCP library: `ModelContextProtocol.AspNetCore` 1.3.0 (officially Anthropic+Microsoft, GA, 9M downloads). A custom build was rejected.
- (b) Transport: Streamable HTTP (Stateless=true), no SSE legacy, no WebSocket. Stdio as future work.
- (c) Endpoint position: in the web host under `/mcp` (Mcp = class library, hosted in the web process).
- (d) `ITokenValidator` in `Geef.Atelier.Application/Auth/`, `AtelierMcpOptions` in `Core/Configuration/`.
- (e) Multi-auth: default scheme=cookie, an explicit `McpPolicy` with `AuthenticationSchemes=["Bearer"]`.
- (f) `BearerTokenHandler` in `Geef.Atelier.Web/Auth/` (internal sealed), returns `NoResult()` on a missing header.
- (g) `RunEntity.CreatedByUser` nullable, migration `Step09AuditTrail` (ADD COLUMN text nullable).
- (h) `IRunService.SubmitRunAsync(briefing, configJson, createdByUser=null, ct)` — optional default-null parameter (backward compat).
- (i) The UI sets `createdByUser=Identity.Name`, MCP sets `"mcp-client"` (a static identifier).
- (j) `GetRunDetailsAsync` (from D-019) reused for the `get_run_details` tool — no new method.
- (k) Constant-time token compare: `CryptographicOperations.FixedTimeEquals` with a length short-circuit before the comparison.
- (l) MCP-SDK version pinned to `[1.3.0,2.0.0)` in `Directory.Packages.props`.
- (m) Mcp project SDK switch: `Microsoft.NET.Sdk.Web` → `Microsoft.NET.Sdk` (class library); Program.cs + appsettings* deleted.
- (n) MCP endpoint setup always active in `WebTestHost` (no conflict with cookie tests).
- (o) Stdio transport documented as future work (after the skeleton).

**Result:** 85/85 tests green. R1 PASS, R2 APPROVED, R3 PASS, R4 COMPLIANT, R5 curl-verified.

---

## D-023 — Step 10: production deploy with Traefik (2026-05-11)

**Context:** the last walking-skeleton step. App code unchanged; only deployment configuration.

**Decisions:**
- (a) External Traefik network: `proxy` (server convention, verified).
- (b) Cert-resolver name: `le` (HTTP challenge via the `web` entry point); not `letsencrypt`.
- (c) Entry point for the HTTPS router: `websecure`; HTTP→HTTPS redirect global in traefik.yml.
- (d) No app-side HTTP redirect router — Traefik does it globally.
- (e) `chain@file` middleware: secure-headers + compression + rate-limit (server convention).
- (f) Postgres in the same compose file (server convention: own-Postgres-per-app).
- (g) `.env` file gitignored, automatically generated via `openssl rand` + `tools/HashPassword`.
- (h) `pull_policy: never` + `com.centurylinklabs.watchtower.enable=false` for the local build.
- (i) Auto-migration on startup stays (D-010); `Step09AuditTrail` is additive (a nullable column).
- (j) `Cookie.Domain` unset — auto-detect more robust for subdomains.
- (k) Direct port exposure (8080) removed; no `ports:` in the production compose.
- (l) `tools/HashPassword` for the BCrypt hash with workFactor 11.
- (m) `traefik.docker.network=proxy` label required when the container is in multiple networks.

**Result:** walking skeleton complete. `geef.stefan-bechtel.de` reachable in production.

---

## D-024 — post-skeleton step 1: Postgres backup strategy (2026-05-11)

**Context:** after walking-skeleton completion: the first post-skeleton feature step for data protection. No code intervention — a pure compose-extension step.

**Decisions:**
- (a) Backup image: `prodrigestivill/postgres-backup-local:16` (community standard, healthcheck + retention policy out of the box, PG16-compatible).
- (b) Network: only `geef-atelier-network` (no `proxy` — the backup needs no external access).
- (c) Watchtower disable: `com.centurylinklabs.watchtower.enable=false` (server convention from D-023).
- (d) Retention: 7 daily, 4 weekly, 6 monthly snapshots — appropriate for single-user with a < 100 MB DB.
- (e) Schedule: `0 3 * * *` (03:00 UTC, minimal server load).
- (f) Backup volume: `geef-atelier-backups` (named volume, isolated from `geef_atelier_postgres_data`).
- (g) Restore script: `scripts/restore-backup.sh` — stops `web`, restores via psql, restarts `web`.
- (h) No off-site backup in this step — a volume backup protects against a container crash, not a server failure. Off-site documented as the next post-skeleton step.
- (i) No new `.env` variables — the backup uses the existing `POSTGRES_*` credentials.
- (j) Server precedent: no other app stack on this server uses a backup service — Geef.Atelier sets the pattern.

**Result:** three containers healthy (web, postgres, postgres-backup). The first manual backup trigger and test restore verified successfully. 85/85 tests still green.

---

## D-025 — post-skeleton step 2: reviewer calibration (2026-05-12)

**Context:** the first real-world briefing (the Hadwiger–Nelson problem) exposed a faulty severity classification: the `KlarheitReviewer` produced critical findings for factually correct content ("correct, but...") → the pipeline aborted (AbortOnCritical=true from D-012). Three goals: (A) sharpen the reviewer prompts, (B) make the convergence policy more robust, (C) improve the executor's iteration-2+ behaviour.

**Decisions:**
- (a) 4-level severity taxonomy: Critical/Major/Minor/Info as the Atelier standard; `docs/06-reviewer-calibration.md` is the normative reference document.
- (b) Tool-schema values changed: `["critical", "major", "minor", "info"]` instead of `["info", "warning", "error", "critical"]`. Backward compatibility in `LlmReviewer.MapSeverity()` for `"error"` and `"warning"`.
- (c) The anti-pattern rule "correct, but" ≠ critical added explicitly to both reviewer prompts.
- (d) The Hadwiger–Nelson problem as a negative example in both reviewer prompts — a concrete LLM anchor against misclassification.
- (e) `ConvergenceOptions` as a new config class in `Geef.Atelier.Infrastructure.Configuration`; default `AbortOnCritical=false` — the pipeline iterates 3 times instead of aborting after the first critical.
- (f) The executor prompt sharpens iteration-2+ behaviour: numbered findings with a severity tag, an explicit requirement "concrete, visible change per finding".
- (g) Reviewer prompts grew from ~4 to ~65 lines — a token-cost increase of ~5–10% per reviewer call accepted (irrelevant at <5 cents/run).
- (h) Stagnation threshold stays 3 (no intervention) — the pipeline aborts on persisting findings after 3 iterations.
- (i) Cross-reviewer voting (B2) rejected — too complex for this step; B1+B4 (configurable) is enough.
- (j) Hadwiger–Nelson replay as a `[Fact]` test with skip-if-no-ApiKey in `AtelierPipelineRunsAgainstOpenRouterTests` — long-term regression safeguard.

**Result:** 96 tests (before: 85). 3 new test classes (SeverityClassification, ConvergencePolicyConfig, OvereagerCriticalAbort). `dotnet build` 0/0. Reviewer calibration raised to the Atelier standard.

---

## D-026 — post-skeleton step 3: design translation (2026-05-12)

**Context:** the walking skeleton + PS-1 (backup) + PS-2 (reviewer calibration) are done. A professional HTML/CSS/JSX prototype (`docs/design/atelier-mockups/`) with three palettes (Vellum/Noir/Petrol), its own typography (Newsreader/Geist/JetBrains Mono), 14+ hairline icons and five screens existed. Goal: port the visual and structural language into the Blazor UI without new backend features.

**Decisions:**
- (a) Three themes via `html.palette-{vellum|noir|petrol}` (CSS class). Default Vellum (prompt requirement) — the mockup default was Noir; an explicit divergence.
- (b) Theme cookie `Atelier.Theme`, 1 year, `HttpOnly=false` (JS interop must read/set it), SameSite=Strict, logout does NOT delete.
- (c) Theme-switch mechanics: JS interop primary (`window.atelier.setTheme`), the server endpoint `/settings/theme` as a fallback and Playwright test hook. Both implemented in parallel.
- (d) `<html>` class Razor-server-side via `IHttpContextAccessor` — no flash of wrong theme.
- (e) The Bootstrap stylesheet fully removed; `wwwroot/atelier.css` as the global stylesheet (ported 1:1 from the mockup + @font-face).
- (f) Self-hosted fonts (Newsreader, Geist, JetBrains Mono) in `wwwroot/fonts/` — GDPR-compliant, no external network request.
- (g) ReviewerDisplay helper mapping (β variant): code classes `BriefingTreueReviewer`/`KlarheitReviewer` unchanged; the UI shows `BriefingFidelity`/`Clarity`. No persistence breaking changes.
- (h) PressStageMapper: a conservative heuristic — all running runs show stage 0 (draft) active; more complex event-based stage detection as an optional refinement.
- (i) FindingResolutionInferrer: heuristic cross-iteration diff via a `Severity|ReviewerName|Message[..60]` signature. Bug fix in PS-3: `IndexOutOfRangeException` on an empty iteration list fixed.
- (j) Mock stubs consistent via the `.coming-soon` class: cost display, export button, profile menu item, welcome stats.
- (k) E2E selectors deliberately stable: `input#username`, `button.btn-login`, `.user-name`, `.btn-logout`, `textarea#briefing`, `button.btn-submit`, `[data-status]`. New components with `data-testid`.
- (l) The tweaks panel and the style guide from the mockup not adopted into production.
- (m) StatusBadge: `data-status` attribute restored (E2E tests expect it); the CSS-class rework (`badge-*` → `status pending`) required a test adjustment.
- (n) WebTestHost in tests: `AddHttpContextAccessor()` added, `[Collection("Postgres")]` for theme-cookie tests.

**Result:** 106 tests total (105 passed, 1 skipped ThemeSwitcher E2E). `dotnet build` 0/0. Five screens visually reworked, three themes functional, self-hosted fonts, 16 icon components, Bootstrap removed.

---

## D-027 — post-skeleton step 4: CLI provider adapter (2026-05-13)

**Context:** all three provider calls ran via OpenRouter (pay-as-you-go) so far. On the Atelier server (Hetzner, `95.216.100.213`), `claude` (Claude Code CLI, subscription) and `codex` (OpenAI Codex CLI, subscription) are installed — subscription capacity without token billing. Goal: a second provider path via a new side container that exposes these CLIs as OpenAI-compatible HTTP.

**Decisions:**

- **(a) Tech stack CLI proxy:** Python 3.12 + FastAPI. Rationale: compact code for an HTTP subprocess wrapper, Pydantic for schema validation, pytest for tests. The Dockerfile installs Node.js + CLI packages (`@anthropic-ai/claude-code`, `@openai/codex`) on top.
- **(b) CLI proxy interface:** OpenAI-compatible (`POST /v1/chat/completions`, `GET /v1/models`, `GET /health`). `OpenAiCompatibleClient` stays unchanged — it simply uses an alternative endpoint. No new `CliLlmClient` needed.
- **(c) `LlmOptions` rebuild (multi-provider):** the old flat schema (`ApiKey`, `DefaultModel`, `Actors{Model}`) replaced by a `Providers` dict (name → endpoint + ApiKey) and an `Actors` dict (name → provider + model + MaxTokens). Hard cut, no backward compat (single-maintainer project). Env var: `Llm__Providers__openrouter__ApiKey`.
- **(d) `ILlmClientResolver` interface:** `(ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName)` in `Geef.Atelier.Infrastructure.Llm`. The resolver caches `OpenAiCompatibleClient` instances per provider in a `ConcurrentDictionary` (one `HttpClient` instance per provider, not per actor).
- **(e) `OpenAiCompatibleClient` multi-instance:** the constructor now takes `(HttpClient, string endpoint, string apiKey)` directly — no more `IOptions<LlmOptions>`. A single named `HttpClient` ("llm") is reused for all providers (`IHttpClientFactory`); endpoint and ApiKey are injected per call via the `Authorization` header.
- **(f) Model routing in the CLI proxy:** `claude-*` / `anthropic/claude-*` → claude CLI; `gpt-*` / `o*` / `openai/*` → codex CLI. Unknown → claude (fallback). The provider prefix is stripped before the CLI call.
- **(g) Tool-use mapping:** schema embedding as a system-prompt addendum → the CLI returns plaintext → `tool_use_parser.extract_json()` extracts JSON (incl. Markdown-fence stripping, balanced-brace scan) → a `tool_calls` response. On a JSON parse failure: plaintext back as `finish_reason="stop"` (the downstream `LlmReviewer` can handle this via D-013(e)).
- **(h) Auth strategy CLI proxy:** named volume `geef-atelier-cli-auth:/auth`, split into `/auth/claude` and `/auth/codex`. A one-time manual login via `docker exec -it geef-atelier-cli-proxy claude auth login`. Tokens persist across container restarts. Secrets never in source control, logs or reports.
- **(i) Concurrency:** `asyncio.Semaphore` per CLI, default 2 (`CLAUDE_MAX_CONCURRENT=2`, `CODEX_MAX_CONCURRENT=2`). A queue on overrun, no error.
- **(j) Side-container network:** `geef-atelier-network` (internal), no `proxy` (no Traefik). Hostname within the compose stack: `cli-proxy`. Reachable from `web` via `http://cli-proxy:8090/v1`.
- **(k) Renamed env var:** `LLM_API_KEY` → `LLM_OPENROUTER_API_KEY` in `.env` + docker-compose. Clearer semantics in the multi-provider context.
- **(l) Default configuration:** all three actors stay on `openrouter` (backward sanity). The `cli` provider is pre-configured in `appsettings.json` but no actor routed to it. Switch via env override without a code change.
- **(m) `web` depends-on `cli-proxy`:** health-check dependent (service_healthy). Prevents starting before the CLI proxy is ready.

**Tests:**
- Python: 21 pytest tests (openai_format, tool_use_parser, claude_adapter_mock, codex_adapter_mock, concurrency) — all green.
- C#: LlmOptionsMultiProviderTests, LlmClientResolverTests, OpenAiCompatibleClientTests (adjusted), TestLlmClientResolver (new). `dotnet test` 113 passed, 1 skipped — all green.

**Result:** `dotnet build` 0/0. 113 C# tests green. 21 Python tests green. CLI-proxy image buildable. Backward sanity (pure OpenRouter) unchanged.

---

## D-028 — crew foundation: profiles, templates, snapshots (2026-05-13)

**Context:** the pipeline ran with a hard-coded three-member crew (`LlmExecutionStep` + `LlmReviewer(BriefingTreue)` + `LlmReviewer(Klarheit)`). This blocked the vision goal "text manufactory with different crews" and all subsequent steps (PS-6: UI crew selection, PS-7: advisor passes).

**Decisions:**

- **(a) Profiles as records:** `ReviewerProfile` and `ExecutorProfile` are sealed positional records in `Core/Domain/Crew/Profiles/`. Same structure (Name, DisplayName, Description, SystemPrompt, Provider, Model, MaxTokens, IsSystem). EF Core maps them as entities with Name as the PK.

- **(b) System profiles as code constants:** defined in `SystemCrew.cs` (read-only). Not in the DB. Custom profiles in the DB with the auto-prefix `"custom-"`. Update/delete of system profiles throws `InvalidOperationException`. Model pluralism: DefaultExecutor on `anthropic/claude-opus-4.7` (continuity), reviewers on foreign models (`google/gemini-2.5-flash`, `openai/gpt-5.5-mini`) per vision principle 3 + the CLAUDE.md convention.

- **(c) CrewTemplate:** composes executor + reviewers + EvaluationStrategy + an optional ConvergenceOverride + empty AdvisorProfileNames (PS-7 preparation). System template `"klassik"` as a code constant, reproduces PS-2 behaviour exactly.

- **(d) CrewSnapshot fully embedded:** reproducibility guaranteed. JSONB column on `Runs`. SchemaVersion=1. Serialized with `JsonNamingPolicy.CamelCase`. Defensive deserialization in the OrchestratorService with a fallback to the klassik constants.

- **(e) EvaluationStrategies:** all four strategies (`Parallel`, `Sequential`, `FailFast`, `Priority`) exist as SDK classes in `Geef.Sdk.Policies` — no Atelier-own code needed. `EvaluationStrategyMapper` maps the domain enum to the SDK classes.

- **(f) `ILlmClientResolver.ForProfile`:** a second method added; uses the same provider cache as `ForActor`. `ForActor` stays for backward compatibility.

- **(g) `IRunService.SubmitRunAsync` extended:** new parameters `crewTemplateName` + `customCrew`. Null/empty → default `"klassik"`. `customCrew` overrides `crewTemplateName`. The snapshot is built and persisted on submit.

- **(h) MCP tools:** `list_crew_templates` + `list_reviewer_profiles` new. `submit_request` extended with `crew_template` + `custom_crew` (JSON string, ignored on a parse error).

- **(i) Migration Step10CrewSystem:** the gap after Step09AuditTrail closed. New tables `ReviewerProfiles`, `ExecutorProfiles`, `CrewTemplates`. `Runs` extended with `CrewTemplateName` + `CrewSnapshot`. UPDATE historical runs to `"klassik"`. UPDATE `Findings.ReviewerName` (`BriefingTreueReviewer` → `briefing-fidelity`, `KlarheitReviewer` → `clarity`).

- **(j) Reviewer slugs:** `"briefing-fidelity"` and `"clarity"` replace the old class names in `FindingEntity.ReviewerName`. `ReviewerDisplay.ToDisplay()` contains both variants as a fallback.

- **(k) `LlmReviewer`, `LlmExecutionStep`, `AtelierSystemPrompts` deleted:** replaced by `ProfileBasedReviewer`/`ProfileBasedExecutor` + `Core/Domain/Crew/SystemPrompts.cs`.

- **(l) AdvisorProfile stub:** fully defined with the `AdvisorMode` enum (Strategic, Critical, DevilsAdvocate, DomainExpert). No DB table in PS-5. PS-7 uses the schema without a breaking change.

- **(m) SystemPrompts in Core:** the long system-prompt strings semantically belong to the system profiles → they now live in `Core/Domain/Crew/SystemPrompts.cs` (domain layer, no infrastructure dependency).

**Tests:** `dotnet build` 0/0 warnings. 154 C# tests green (41 new), 1 E2E skip unchanged.

**Result:** the pipeline builds itself dynamically per run from the persisted CrewSnapshot. System defaults versioned in code, user-custom in the DB. Reviewer-name migration clean. MCP tools functional. PS-6 (UI crew selection) and PS-7 (advisor passes) can follow without schema breaks.

---

## D-029 — PS-6 crew UI: architecture decisions

Date: 2026-05-13

| Decision | Result |
|---|---|
| (a) `CrewSnapshot.Deserialize` as a static domain helper | Consolidates the deserialization logic previously duplicated inline in `RunOrchestratorService`. UI components and the service consume the same helper. |
| (b) `IProviderCatalog` service in the application layer | Instead of injecting `IOptions<LlmOptions>` directly into Razor (a layer violation): a narrow interface in Application, the implementation in Infrastructure wraps `IOptions<LlmOptions>`. |
| (c) Routing constraint `[a-z0-9\-]+` max 64 characters | Blazor route parameter for profile/template names. DataAnnotations `RegularExpression` in form validation enforces the same pattern. |
| (d) Interactive-server render mode for all CRUD editor pages | Up/down buttons in the ReviewerPicker, state management in the delete modal need server interactivity without a page reload. |
| (e) Top-level NavMenu link "Crew" as the fourth entry | NavMenu had only 3 items, no style-guide link present. Top-level is the natural choice. |
| (f) Generic `ProfileEditorForm` for reviewer + executor | Both records have an identical schema — code reuse via one parameterized form component. |
| (g) Generic `Modal` component + `DeleteConfirmationModal` as a wrapper | No browser `confirm`. The modal is reusable for other confirmation flows. |
| (h) Up/down arrow buttons instead of drag-drop in the ReviewerPicker | No JS interop needed, sufficient for 2–5 reviewers. |
| (i) System profile/template protection: UI + service | The UI renders disabled buttons + a duplicate action; the service additionally throws `InvalidOperationException` (defense in depth). |
| (j) Custom auto-prefix live preview in the editor | The UI shows "Will be saved as: custom-XXX" before saving. The service layer is idempotent. |
| (k) `CrewSummary` click-to-expand instead of a separate modal | Space-saving, no extra click for closing needed. |
| (l) `CrewBadge` as a subtle text badge without an icon | A small visual hierarchy below StatusBadge — an icon would be overload. |
| (m) AdvisorProfile fields omitted in PS-6 | PS-7 brings the advisor UI. The schema is there but not yet functional in PS-6. |


## D-030 — bugfix: run status on an LLM-provider error

Date: 2026-05-13

| Decision | Result |
|---|---|
| (a) Outer catch in `RunOrchestratorService`, not in the SDK layer | `ProcessRunAsync` is the only place that knows the run context and has write rights on `RunEntity`. Adjusting the SDK layer for error handling would be a layer violation. |
| (b) `MarkRunFailedAsync` as a separate helper analogous to `OverrideToAbortedAsync` | A consistent pattern: every terminal-state transition has its own helper. No generic helper, to avoid confusion. |
| (c) `SanitizeErrorMessage` walks the `InnerException` chain | The Geef SDK wraps `HttpRequestException` in its own exception — direct pattern matching on the outermost exception type is not enough. |
| (d) No exception types explicitly in the `catch` signature | The `catch (Exception ex)` stays generic since the SDK could use arbitrary wrapper types. The sanitize logic encapsulates the type differentiation. |
| (e) `TaskCanceledException` in the sanitizer = "timed out" | `cts.IsCancellationRequested`-based cancellation is caught by earlier `catch` blocks. A `TaskCanceledException` that reaches the generic block is exclusively a provider timeout. |
| (f) No auto-retry | Transient-error retry (HTTP 429/503) stays a separate step with `Polly`. This bugfix only persists the error state correctly. |

---

## D-031 — PS-7: advisor passes (2026-05-13)

**Context:** advisor passes make it possible to consultatively bring in LLM actors before the executor pass (BeforeFirstExecution, BeforeEveryExecution) or after a convergence failure (OnConvergenceFailure). Their output is injected as a marked context block into the run context without modifying the Geef SDK core.

| Crux | Decision |
|---|---|
| (a) Decorator pattern instead of an SDK hook | The Geef SDK does export `Geef.Sdk.Advisors.IAdvisor`, but for layer-separation reasons an own `AdvisorAwareExecutor` decorator is placed around `IExecutionStep`. The decorator sits in the Infrastructure layer and needs no SDK-internal knowledge. Changes to the SDK interface would not break Atelier. |
| (b) Advisor output as `AtelierContextKeys.AdvisorBlock` | The advisor output is written as a single, clearly named text block (`[ADVISOR: <name>]\n<text>`) into `IRunContext` via `AtelierContextKeys.AdvisorBlock`. Executor and reviewer system prompts can reference this block. No structured findings — advisors deliver flowing advice, not severity-classified findings. |
| (c) Advisor failure → the run fails (exception bubbling) | Advisor LLM calls are not best-effort. An exception in `ProfileBasedAdvisor` bubbles up through `AdvisorAwareExecutor` and aborts the run with status `Failed`. Rationale: a failed advisor may have corrupted the executor context — silently continuing would be more dangerous than a transparent abort. |
| (d) Advisor order significant (analogous to reviewers) | Multiple advisors of one trigger run sequentially in list order. Each later advisor sees the already accumulated `AdvisorBlock` of the predecessors. Analogous to the `Sequential` EvaluationStrategy for reviewers: the order in the CrewTemplate is semantic. |
| (e) OnConvergenceFailure trigger via single-retry cap | `RunOrchestratorService.TryConvergenceFailureRetryAsync` catches `ConvergenceFailedException` and starts a single repeat run with `OnConvergenceFailure` advisors in the context. The `RunEntity.AdvisorRetryAttempted` flag (migration Step11) prevents infinite loops: a second `ConvergenceFailedException` after the retry escalates directly to `Failed`. Multi-retry with a configured retry count is documented as future work (PS-8 or an own step). |

---

## D-032 — refactor: CLI provider split (2026-05-13)

**Context:** PS-4 created a single `cli` provider with internal model-name routing (claude prefix → claude CLI, gpt-/o* prefix → codex CLI). In the PS-6 crew UI it became visible that this hidden routing mechanism is a UX weakness: the user sees only "cli" in the provider dropdown and must guess from the model name which CLI is actually used. A misspelled model name can silently route to the wrong CLI.

| Crux | Decision |
|---|---|
| (a) Legacy endpoint in the cli-proxy: remove or keep | **Keep with a deprecation warning.** The legacy endpoint `/v1/chat/completions` is retained and logs a WARNING-level log on every call. Rationale: minimal risk for migration edge cases (if a profile still had `cli` as provider after the DB migration, it would keep working instead of failing completely). Planned removal after 2–3 Atelier versions. |
| (b) Two explicit endpoints: `/v1/claude/chat/completions` + `/v1/codex/chat/completions` | Deterministic routing without a model-name heuristic. The provider name alone decides the CLI choice. The Atelier configuration separates the two providers in `appsettings.json` with different endpoint paths. |
| (c) `IProviderCatalog` API extension with `ProviderInfo` | The old method `ListProviderNames() → IReadOnlyList<string>` is replaced by `ListProviders() → IReadOnlyList<ProviderInfo>`. `ProviderInfo` is a sealed record with `Name` (DB key) and `DisplayName` (UI label). No backward compat (single-maintainer project, the only caller is `ProfileEditorForm`). |
| (d) `ProviderCatalog` implementation: hard-coded DisplayNames instead of DB data | DisplayNames for the three known providers (`openrouter`, `claude-cli`, `codex-cli`) are stored as a dictionary constant in the code. Unknown provider names (future extensions) fall back to `Name == DisplayName`. |
| (e) CrewSnapshot migration strategy: two-pass SQL | The DB migration `Step12CliProviderSplit` migrates the profile tables (ReviewerProfiles, ExecutorProfiles, AdvisorProfiles) via a direct `CASE`-`UPDATE` on the `"Model"` column — always correct. For `Runs.CrewSnapshot` JSONB: two-pass SQL string replace (codex pattern first, then claude-cli fallback). Limitation: mixed-CLI snapshots (one executor claude, one reviewer codex in the same run) are not migrated correctly — in practice such snapshots do not exist since all system actors use `openrouter` and custom CLI profiles are new in the project. |

**Tests:** `dotnet build` 0/0. 246 C# tests green (7 new), 1 E2E skip. Python pytest 30/30 green (9 new).

---

## D-033 — feature: model-catalog dropdown (2026-05-13)

**Context:** the profile editor (PS-6) used a free-text input for the model field. This led to a bug with a non-existent model (`openai/gpt-5.5-mini`), which now correctly lands as `Failed` (D-030) but is better solved by prevention: a dropdown with valid model IDs prevents typos.

| Crux | Decision |
|---|---|
| (a) CLI listing command | Neither the `claude` nor the `codex` CLI has a `--list-models` command. Only option (b) — static lists in the adapters. No "hybrid" attempt needed. |
| (b) Recommended lists | Hard-coded in `StaticModelFallback.cs` (Core layer). A recommendation is an Atelier opinion, not a provider property. A maintainer duty on every model release. |
| (c) Cache sharing | Single-instance setup: `IMemoryCache` with a 24h TTL is sufficient. No Redis or distributed cache needed. |
| (d) CLI-provider model sources | `claude-cli` backend: static list in `claude_adapter.STATIC_MODELS`. `codex-cli` backend: static list in `codex_adapter.STATIC_MODELS`. Both are exposed via the new `/v1/claude/models` and `/v1/codex/models` endpoints in the cli-proxy. OpenRouter: a live API call against `https://openrouter.ai/api/v1/models`. |
| (e) Fallback strategy | API call fails → `StaticModelFallback.For(providerName)`. An `IsUsingFallback()` flag is exposed; the UI shows a warning banner when the fallback is active. |
| (f) Custom-model escape hatch | Exists as a "Custom model name…" option in the dropdown. When saving with a non-catalogued model: a confirmation modal ("Save anyway?"). Prevents typos but allows deliberate custom use. |

**Tests:** 256 C# tests (8 new `ModelCatalogTests`), 43 Python tests (13 new `test_models_endpoints.py`), 1 E2E skip, 0 failures.

---

## D-034 — feature: grounding visualization (2026-05-13)

**Context:** the GEEF grounding phase exists fully in code (`BriefingGroundingStep`, `AdvisorContextGroundingStep`) but is invisible in the UI. RunDetail jumps directly from the briefing to the first iteration. Additionally, pre-execution advisors (trigger `BeforeFirstExecution`) are rendered as an iteration-1 contribution — conceptually they belong to the grounding phase since they run once per run.

| Crux | Decision |
|---|---|
| (a) Trigger lookup strategy: DB column vs. snapshot deserialization | **Snapshot deserialization.** The `AdvisorConsultation` entity has no `Trigger` field. The trigger is looked up at render time from `CrewSnapshot.Advisors` via an `AdvisorProfileName` match. No DB migration needed. Fallback on a lookup miss (e.g. historical runs without a matching advisor profile in the snapshot): the consultation stays in the iteration bucket, no data loss. |
| (b) Press-visualization layout: four equal stages vs. a two-part display | **Two-part** (grounding | iteration loop). Grounding as a standalone column left of the iteration block, separated by a CSS divider. Makes it semantically clear that grounding runs once and the iteration stages are a loop. Four equal stages would obscure the difference. |
| (c) Grouping logic location: page-internal vs. an application-layer ViewModel | **Application-layer ViewModel** (`RunWithGroundingViewModel`). `IRunService.GetRunWithGroundingAsync` encapsulates the grouping logic fully: testable without the Blazor stack, reusable (possibly for a future MCP tool), clean layer separation. `RunDetail.razor` is thereby purely declarative without grouping logic. |

**Tests:** 273 C# tests (19 new: `RunWithGroundingViewModelTests`, `GroundingSectionTests`, `PressVisualizationWithGroundingTests`), 1 E2E skip, 0 failures. Python tests unchanged (43 green).

---

## D-035 — grounding-provider foundation + Tavily web search (2026-05-13)

**Context:** the first real web-search provider based on the advisor-passes architecture (PS-7 mirrored). Goal: a generic `IGroundingProvider` abstraction that also holds for a future `VectorStoreGroundingProvider` without a refactor.

### Architect decisions (four cruxes):

| Question | Decision | Rationale |
|---|---|---|
| (a) ProviderType discriminator: enum vs. string | **String** (`"tavily"`, `"vector-store"`, …). An open system via an `IGroundingProviderFactory` DI lookup. An enum would require a Core change per new provider. | Provider-agnostic — an AC17 prerequisite. |
| (b) Cost tracking: sync in the pipeline vs. lazy | **Sync in `TavilyGroundingProvider.EnrichAsync`**. An `IServiceScopeFactory` scope pattern (identical to PS-7 `AdvisorAwareExecutor`). No separate post-run job needed, no lost costs on a crash. | Captive-dependency fix: singleton provider, scoped repository. |
| (c) QueryExtraction: briefing prefix vs. standalone query extraction | **Briefing text directly as the query**. Query extraction (an own LLM call before Tavily) is PS-8 scope. Sufficient for phase 1 since the Tavily synthesized answer also yields sensible results for long briefings. | Scope boundary kept clear; foundation-first. |
| (d) Grounding-context position: before vs. after the advisor block | **Before the advisor block** in `ProfileBasedExecutor`. Web facts should already be visible to advisors when their output is produced (e.g. `briefing-clarifier` can include web-researched facts in its questions). GroundingContext → AdvisorBlock → UserPrompt. | The advisor-before-executor ordering from PS-7 stays consistent. |

**Foundation check (AC17):** `VectorStoreGroundingProvider` can dock on via an `IGroundingProvider` implementation + DI registration, without changing a line of existing code. `ProviderSettings: Dictionary<string,string>` carries arbitrary provider configuration. `GroundingProviderProfile.ProviderType` is a string. `IGroundingProviderFactory.Create(type)` resolves by discriminator.

**Tests:** 304 C# tests (31 new: SystemCrewGroundingConstantsTests, CrewServiceGroundingProviderCrudTests, TavilyGroundingProviderTests, MultiProviderGroundingStepTests, KlassikTemplateGroundingRegressionTests), 1 E2E skip, 0 failures. Python tests unchanged (43 green).

**Migration:** `Step13GroundingProviders` — three tables/columns: `GroundingProviderProfiles`, `GroundingConsultations` (with a cascade-delete FK on Runs), a `GroundingProviderNames` JSONB column on `CrewTemplates`.

**Deployment note:** `TAVILY_API_KEY` must be set in `.env` (optional — an empty key registers the provider but throws `InvalidOperationException` at runtime with a clear message, no app crash at startup). No key in logs.

---

## D-036 — feature: vector-store grounding provider (phase 2 RAG) (2026-05-14)

**Context:** the Tavily step (D-035) created the `IGroundingProvider` foundation with reserved `SourceCitation` fields `DocumentReference` and `RelevanceScore`. This step activates the second provider: semantic search over uploaded documents (Markdown, text) instead of web search. A full RAG setup (phase 2).

### Architect decisions (five cruxes):

| Question | Decision | Rationale |
|---|---|---|
| (a) Postgres image: `pgvector/pgvector:pg16` | **Accepted.** `gen_random_uuid()` is PG16 core, encoding/locale identical to `postgres:16-alpine`. The volume is retained on the image switch. | Official pgvector image, no vendor lock. |
| (b) Embedding-model default | **`openai/text-embedding-3-small` (1536 dim, ~$0.02/1M tokens via OpenRouter).** The cheapest capable OpenAI embedding model, via OpenRouter without a separate API key. `allow_fallbacks: true` for availability. | No new keys needed (LLM_OPENROUTER_API_KEY reused). |
| (c) Chunking strategy | **Self-built `RecursiveCharacterTextSplitter`** (LangChain-compatible). Separators: `"\n\n"`, `"\n"`, `". "`, `" "`, `""`. ~4 chars/token. No external library. | `TreatWarningsAsErrors=true` + LangChain.NET is unstable. The own impl. is fully testable. |
| (d) `Pgvector.EntityFrameworkCore 0.3.0` compatibility | **INCOMPATIBLE with EF Core 10.** Targets net8.0, requires Npgsql.EF ≥9.0.1. LINQ distance operators do not work. **Fallback: raw Npgsql ADO.NET for all vector operations.** A `float[]` ValueConverter (culture-invariant) for EF column mapping. | Same interface `IVectorSearchRepository`, different impl. No external API change. |
| (e) HttpClient sharing for embeddings | **An own `HttpClient<OpenRouterEmbeddingProvider>`** via `EmbeddingsServiceExtensions`. The same `LLM_OPENROUTER_API_KEY` from `LlmOptions`. | Cleaner scope, no circular dependencies. |

### Implementation highlights:

- **VectorSearchRepository:** a raw NpgsqlCommand with a `@vec::vector` cast (named `@param` syntax instead of positional `$N`, to avoid Npgsql auto-prepare cache conflicts), the `<=>` cosine-distance operator, `&&` array overlap for the tag filter (OR semantics: at least one tag must match)
- **float[] ValueConverter:** `string.Join(",", floats)` → `[f1,...fn]` (InvariantCulture both directions) — the pgvector literal format
- **HNSW index:** `CREATE INDEX USING hnsw ("Embedding" vector_cosine_ops)` — ANN for cosine similarity
- **Tag filter:** `&&` (array overlap, OR) instead of `@>` (array contains all, AND) — correct "at least one" semantics
- **Foundation check (AC15):** the `IGroundingProvider` contract from D-035 reused without a refactor — `VectorStoreGroundingProvider` is drop-in next to `TavilyGroundingProvider`

### New tables (migration `Step14VectorStore`):

```sql
"KnowledgeDocuments"     -- document metadata + tags (text[]) + GIN index
"KnowledgeDocumentChunks" -- chunks + "Embedding" vector(1536) + HNSW index
CREATE EXTENSION IF NOT EXISTS vector;
```

### Tests:

400 C# tests (40 new: Domain/Application, Embeddings, Repositories, Services, Provider, UI/bUnit, Pipeline/Regression), 1 E2E skip, 0 failures.

### Deployment:

- Backup: `backup/before-pgvector-migration-20260514-120931.dump` (34K, 48 TOC entries, PG16)
- Postgres image switched to `pgvector/pgvector:pg16` (PG 16.13, Debian)
- `vector` extension 0.8.2 installed
- Web container rebuilt with `--no-cache`
- PR #6 merged (SHA: `b659912`), branch `feat/vector-store-grounding` deleted
- In production at `https://geef.stefan-bechtel.de/crew/knowledge`

### Deliberately NOT in this step:

PDF support, a background job for indexing, multi-modal embeddings, OR tag filter (UI), hybrid search, re-ranking, an embedding-model-switch UI with auto-re-index, document versioning, OpenAI-direct integration.

---

## D-037 — feature: run attachments — direct document upload at the briefing (2026-05-14)

**Context:** D-036 (vector-store grounding) is ideal for **persistent** domain sources (brand guidelines, style guides). For ad-hoc use ("summarize this report") the upload→template→select→brief workflow is too heavyweight. Run attachments implement the ChatGPT/Claude standard pattern: attach files directly at the briefing, indexed run-locally, automatically active as a grounding provider.

### Architect decisions (three cruxes):

| Question | Decision | Rationale |
|---|---|---|
| (a) Schema strategy: extend `KnowledgeDocuments` vs. a separate table | **Extension.** `Scope integer NOT NULL DEFAULT 0` + `RunId uuid NULL` + FK on `Runs.Id`. | One search logic, no JOIN, FK directly on Runs, the scope filter transparent for `VectorSearchRepository`. A separate table would have duplicated the UI/search logic. |
| (b) Run-persist + attachment-upload sequence | **Two-phase: Status=Pending → attachments → snapshot patch → queue.** `RunPersistenceService` gets `UpdateSnapshotAsync` + `MarkRunFailedAsync`. | The FK takes hold after run-create, the pipeline starts only after attachment upload. Failure → `MarkRunFailedAsync` = no orphaned Pending run. Structurally analogous to D-030 `MarkRunFailedAsync` in the orchestrator. |
| (c) Multi-provider precedence | **`RunAttachmentsProfile` is `Prepend`ed before all other providers (specific > general).** `MultiProviderGroundingStep` respects the snapshot order — no change needed. | Custom template with `knowledge-base-default` + attachments: run-local sources first, then the global KB. Analogous to D-031 advisor order. |

### Implementation highlights:

- **`KnowledgeScope` enum** (`Global=0`, `RunLocal=1`) — type-safe, default 0 = backward compat for existing docs
- **`SubmitRunRequest` record** — `IRunService.SubmitRunAsync` switched from positional args to a record (breaks the caller signature cleanly instead of parameter chaos)
- **`RunAttachmentInput` record** with `byte[]` instead of `Stream` — lifetime-safe across async boundaries
- **`VectorSearchRepository`** raw SQL: `WHERE (@scopeFilter IS NULL OR d."Scope" = @scopeFilter) AND (@runIdFilter IS NULL OR d."RunId" = @runIdFilter)` — typed `NpgsqlParameter` with an explicit `NpgsqlDbType` for nullable parameters
- **Blazor Server + drag-and-drop:** `DragEventArgs` does not serialize a `Files` reference over SignalR — drag-and-drop removed, the UI switched to "click to browse"
- **`to_regclass` vs. `::regclass` in the migration:** the former returns NULL for non-existent tables, the latter throws an exception — important for idempotent FK guards
- **Tag dedup in `PromoteToGlobalAsync`:** `.Union()` instead of spread syntax (the spread produces duplicates)
- **`ListAsync` scope filter:** `KnowledgeDocuments` in the global KB view + the MCP tool explicitly pass `KnowledgeScope.Global` — run-local attachments do not bleed through

### Migration `Step15RunAttachments`:

```sql
ALTER TABLE "KnowledgeDocuments" ADD COLUMN "Scope" integer NOT NULL DEFAULT 0;
ALTER TABLE "KnowledgeDocuments" ADD COLUMN "RunId" uuid NULL;
-- FK via a PL/pgSQL block (to_regclass guard, PostgreSQL 16 has no ADD CONSTRAINT IF NOT EXISTS)
FK REFERENCES "Runs"("Id") ON DELETE CASCADE;
CREATE INDEX "IX_KnowledgeDocuments_RunId" ON "KnowledgeDocuments"("RunId") WHERE "RunId" IS NOT NULL;
CREATE INDEX "IX_KnowledgeDocuments_Scope" ON "KnowledgeDocuments"("Scope");
INSERT INTO "GroundingProviderProfiles" ... ('run-attachments', 'vector-store', ...) ON CONFLICT DO NOTHING;
```

### Tests:

494 C# tests (94 new), 1 E2E skip, 0 failures. New test classes: `KnowledgeDocumentScopeTests`, `SystemCrewRunAttachmentsProfileTests`, `SubmitRunRequestTests`, `Step15RunAttachmentsMigrationTests`, `KnowledgeDocumentRepositoryScopeTests`, `VectorSearchRepositoryScopeTests`, `RunDeleteCascadesAttachmentsTests`, `KnowledgeServiceUploadRunAttachmentTests`, `RunServiceAttachmentTests`, `SubmitRequestToolAttachmentTests`, `AtelierPipelineFactoryWithRunAttachmentsTests`, `KlassikWithAttachmentsTests`, `MultiProviderOrderingTests`, `FileDropZoneTests`, `RunAttachmentsListTests`, `PromoteAttachmentModalTests`.

### Deployment:

- Backup: `backup/before-run-attachments-migration-20260514-165517.dump` (39K, 60 TOC entries)
- Web container rebuilt with `--no-cache`
- PR #7 merged (SHA: `f6832f9`), branch `feat/run-attachments` deleted
- In production at `https://geef.stefan-bechtel.de/new` (FileDropZone visible)

### Deliberately NOT in this step:

PDF support, a background job for attachment indexing, image attachments, an auto-cleanup retention policy, attachment editing (upload-only), hybrid search (pure vector as in D-036).

---

## D-038 — Template Studio: meta-AI for template creation (2026-05-14)

A new page `/crew/studio`: the user describes a task in natural language, a meta-AI (Claude Sonnet 4.5 via OpenRouter) analyzes the task with tool use (`submit_template_proposal`), compares it with existing templates and profiles, and either proposes an existing template or creates a new one with matching custom profiles. The user reviews and edits in a 5-step wizard (Input → Analyzing → Review → Edit → Confirmation) **before** anything is saved. All created records are afterwards normally editable via the existing editors (`/crew/templates/{name}`, `/crew/profiles/.../{name}`).

### Architecture decisions:

| Area | Decision |
|---|---|
| **Meta-LLM** | `anthropic/claude-opus-4-7` via OpenRouter (upgraded from `claude-sonnet-4-5`, see D-043/9); configurable via `appsettings.json:TemplateStudio:Model` |
| **Structured output** | OpenAI tool use (`submit_template_proposal` tool with a full JSON schema); no free-text parsing |
| **Edit-before-save mandatory** | The wizard allows no skip from Review → Confirmation; hallucination protection via mandatory review |
| **Subsequent editability** | Studio creates only `custom-` records via `ICrewService.CreateCustom…`; system profiles are only referenced, never modified |
| **Profile similarity check** | On-the-fly embedding cosine similarity via `IEmbeddingProvider`; threshold 0.85 → a proposed profile marked as a duplicate and not created |
| **System-profile protection** | Guard in `TemplateStudioService.ValidateNotSystemProfiles` + the existing `CrewService.Update` check |
| **Model-availability validation** | `IModelCatalog.ListModelsAsync` per provider; missing models → warning, no failure |
| **Provider-availability check** | `IProviderCatalog.ListProviders`; missing API keys → warning in `MaterializationResult.Warnings` |
| **Audit trail** | New table `TemplateStudioAnalyses` (Step17 migration); the JSONB column `AnalysisResultJson` contains the complete `TemplateStudioAnalysis` record |
| **Cost tracking** | `IPricingCatalog.CalculateCostEur` computes the cost of the meta-LLM call; persisted in `TemplateStudioAnalyses.CostEur` |
| **Multi-step wizard** | A Blazor state machine client-side in `TemplateStudio.razor`; no backend state needed |
| **Few-shot examples** | 3 examples in the system prompt: (1) klassik template match > 0.85, (2) a new template with existing profiles, (3) a new template with a new domain-specific reviewer |
| **Custom-profile naming** | `custom-` prefix automatic via `ICrewService.CreateCustom…`; conflicts caught via the EF DB unique constraint |
| **String-format protection** | `template.Replace("{0}", context)` instead of `string.Format` (the prompt contains `{klassik: 0.95}` literals that trigger `FormatException`) |

### Implementation:

- **Core:** `Domain/Crew/TemplateStudio/` — 6 new records/enums (TemplateStudioAnalysis, ProposedTemplate, ProposedProfile, TemplateMatch, StudioRecommendation, ProposedProfileType)
- **Persistence:** `Core/Persistence/TemplateStudio/ITemplateStudioAnalysisRepository`, Infrastructure entity/configuration/repository; migration `Step17TemplateStudio` (manual as raw SQL, since `dotnet ef` crashes due to a pgvector ValueComparer bug)
- **Application:** `ITemplateStudioService`, `MaterializationRequest/Result`, `TemplateStudioOptions`
- **Infrastructure:** `TemplateProposalTool` (JSON schema), `TemplateStudioPrompts` (meta prompt + 3 few-shot examples), `ProfileSimilarityService` (cosine), `TemplateStudioService` (full implementation), `TemplateStudioServiceExtensions` (DI)
- **Web:** `TemplateStudio.razor` (`/crew/studio`), `StudioTaskInputStep`, `StudioAnalyzingStep`, `StudioReviewStep`, `StudioEditStep`, `StudioConfirmationStep`, NavMenu entry "Crew Studio", `New.razor` with `[SupplyParameterFromQuery]`

### Migration `Step17TemplateStudio`:

```sql
CREATE TABLE "TemplateStudioAnalyses" (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "TaskDescription" text NOT NULL,
    "AnalysisResultJson" jsonb NOT NULL DEFAULT '{}'::jsonb,
    "InputTokens" integer NOT NULL,
    "OutputTokens" integer NOT NULL,
    "CostEur" numeric(10,6) NULL,
    "MaterializedTemplateName" text NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX "IX_TemplateStudioAnalyses_CreatedAt" ON "TemplateStudioAnalyses"("CreatedAt" DESC);
```

### Tests:

562 existing C# tests unchanged green + 43 new tests. New test classes: `TemplateStudioAnalysisTests`, `TemplateProposalToolTests`, `ProfileSimilarityServiceTests`, `TemplateStudioServiceAnalyzeTests`, `TemplateStudioServiceMaterializeTests`, `TemplateStudioAnalysisRepositoryTests`, `Step17TemplateStudioMigrationTests`, `StudioTaskInputStepTests`, `StudioReviewStepTests`, `StudioEditStepTests`, `StudioConfirmationStepTests`.

### Deliberately NOT in this step:

An MCP-tool extension for Studio, auto-run after materialization, Studio iterations ("think again"), learning effects from the audit trail, a custom-executor proposal, cost budgets, bulk import of templates.

---

## D-039: Studio extensions (14 May 2026)

**Feature:** an MCP API for Template Studio + an analysis-history component + a welcome-stats extension.

### Decisions:

**1. MCP tool design:** two separate tools instead of one combined — `analyze_template_proposal` (analysis + persistence) and `materialize_template_proposal` (materialization after user review). The split enables an asynchronous workflow: analyze → review → materialize with optional edits in between. The `AnalysisId` from step 1 links both calls.

**2. `TemplateStudioHistoryItem` as a Core record:** a lightweight projection for history queries in the `Core.Persistence` namespace, to avoid the `Infrastructure → Application` layering. The `ReasoningSummary` is deserialized from the `AnalysisResultJson` JSONB (no own DB field) — defensible for max. 10 entries per page.

**3. `StudioAnalysesPage` and `StudioAnalysisHistoryEntry` in the application layer:** analogous to the HasMore pagination pattern of all other list endpoints. The UI component injects `ITemplateStudioService` directly — no separate controller layer.

**4. Studio costs separate from run costs:** `WelcomeStats` gets `StudioAnalysesThisMonth` and `StudioCostThisMonth` as its own fields, not aggregated into `TotalCostThisMonth`. Studio analyses are configuration costs, not execution costs — the user should see both dimensions separately.

**5. `StudioAnalysisHistoryList` component without a StateContainer:** local state in the component itself (`_analyses`, `_currentPage`, `_expandedId`). A re-analyze callback via `EventCallback<string>` — the parent (`TemplateStudio.razor`) sets `_taskDescription` and thereby jumps back into input mode.

### No schema change:

All tables (in particular `TemplateStudioAnalyses`) exist since Step17 — no new migration step. `TemplateStudioHistoryItem` is only a code abstraction, not a DB entity.

### Tests:

625 existing C# tests unchanged green + 40 new tests. New test classes: `AnalyzeTemplateProposalToolTests` (9), `MaterializeTemplateProposalToolTests` (9), `TemplateStudioServiceListRecentAnalysesTests` (7), `StudioAnalysisHistoryListTests` (13), `WelcomeStatsTests` (4). The orchestrator-timing tests (2 of them) were already pre-existing flaky — they pass in isolation, fail under the full suite due to thread-timing sensitivity (not a new problem).

### Deliberately NOT in this step:

Auto-run after materialization, Studio iterations, cost-budget alerts, bulk export of analysis histories, email notification after a completed analysis.

---

## D-040: grounding-provider-profile CRUD-UI catch-up (2026-05-15)

**Context:** the spec for this catch-up step assumed the grounding-provider CRUD UI was "completely missing". After code exploration it turned out that `GroundingProvidersIndex.razor` and `GroundingProviderEditor.razor` were already fully implemented — incl. system/custom split, Tavily and vector-store fields, DataAnnotations validation, the delete modal and all 5 `ICrewService` grounding methods. The pages already followed the reviewer/executor/advisor pattern exactly. The actual gap was not in the CRUD implementation but in: (a) a deviating route schema (the spec wanted `/grounding-providers`, `/create`, `/edit/{name}`, `/view/{name}`), (b) missing gap features and (c) a missing dashboard entry and tests.

**Decisions:**

**D-040/1 — route schema:** spec routes adopted (`/crew/profiles/grounding-providers`, `/create`, `/edit/{name}`, `/view/{name}`). The existing routes (`/crew/profiles/grounding`, `/new`, `/{name}`) did match the real reviewer/executor/advisor pattern — but the user decision favored spec conformance. The asymmetry to the sibling pages (which use `/new` and `/{name}`) is addressed in a follow-up step by aligning the other three profile types (recommendation: unify reviewer/executor/advisor onto a consistent schema).

**D-040/2 — a separate view page for system profiles:** a new `GroundingProviderView.razor` with `@page "/crew/profiles/grounding-providers/view/{name}"` instead of an inline read-only banner in the editor. Enables a clear system presentation without an editable form. Reviewer/executor/advisor keep the inline-banner pattern — a possible task for a follow-up harmonization step.

**D-040/3 — vector-store scope selector:** a `Scope` field (global/run-local/both) exposed in the editor. Existing custom vector-store profiles (created before this step) had no `Scope` key in `ProviderSettings` — `From()` defaults to `"both"` to preserve the previous unfiltered behaviour. New profiles: default `"global"`. Backend mapping: `"both"` → `null` filter → no restriction (works without a backend change, confirmed by `VectorStoreGroundingProvider.cs:44-49`).

**D-040/4 — ProviderType immutable on edit:** the `InputSelect` for ProviderType gets `disabled="@(!IsNew)"`. Type-specific settings (Tavily vs. vector-store) are not migratable between types — the user must delete the old profile and create a new one.

**D-040/5 — delete-cascade behaviour (verified):** no cascade delete from `CrewTemplates` on profile deletion. `CrewTemplate.GroundingProviderNames` is a JSONB string array without an FK relationship to `GroundingProviderProfiles`. Deleted profiles leave dangling names in templates; at runtime `GetGroundingProviderProfileAsync` resolves to `null`. Identical to the behaviour on reviewer/advisor profile deletion. `DeleteConfirmationModal` requires the exact name input as a safety layer. Follow-up-step recommendation: template-reference listing in the delete modal.

**D-040/6 — NavMenu entry:** the only grounding-providers NavLink in the NavMenu. Reviewer/executor/advisor have no NavMenu entries (reachable only via the `/crew` dashboard). This asymmetry is deliberate (spec AC #12 explicit). Follow-up-step recommendation: a crew-profile section in the NavMenu or treat all four types equally.

**Lesson (origin of the gap):** the grounding-provider CRUD pages were correctly co-implemented at the Tavily step (D-035) — but without a CrewIndex dashboard entry, without bUnit UI tests and without a route-schema alignment to the spec. No page code was forgotten; the organizational gap was in missing route conventions and test coverage.

---

## 16 May 2026 — MCP OAuth 2.1 authorization server (PS-9 extension)

### D-041: self-hosted OAuth 2.1 AS for Claude Desktop / Claude.ai custom connectors

**Date:** 16 May 2026
**Report:** [reports/feature-mcp-oauth-report.md](reports/feature-mcp-oauth-report.md)
**Branch:** `feat/mcp-oauth` → PR against `main`
**Reviewer iterations:** 9 (iteration 9: all five R1–R5 with 0 findings)
**Tests:** 803 green (+ 1 known E2E-flake skip), 92 new OAuth tests. **14 conventional commits.**

**Context:** Claude Code CLI works with a static bearer token (`ATELIER_MCP_TOKEN`). Claude Desktop custom connectors and Claude.ai web custom connectors speak OAuth exclusively — a static bearer token is not enough there. Goal: a self-hosted minimal OAuth 2.1 authorization server that runs alongside the static auth.

**Core decisions:**

**D-041/1 — self-hosted AS instead of an external IdP:** the OAuth 2.1 AS implemented directly in Geef.Atelier (no Keycloak, Auth0 etc.). Rationale: single-user context, no deployment overhead, full control over the token lifecycle. Scope exclusively `mcp:full` (no multi-scope design).

**D-041/2 — opaque tokens + SHA-256 instead of JWT:** tokens are cryptographically random 32-byte strings (Base64Url), stored in the DB exclusively as a SHA-256 hash (`RandomNumberGenerator.GetBytes(32)`). Advantage: no JWKS endpoint, no asymmetric-crypto infrastructure, token revocation simple (mark the hash in the DB), no token size in the HTTP header.

**D-041/3 — PKCE strictly S256, no `plain`:** `code_challenge_method=plain` is rejected. The authorization server enforces S256 via a `string.Equals(..., StringComparison.Ordinal)` check. Public clients (no client secrets) — `token_endpoint_auth_method=none`.

**D-041/4 — loopback special rule (RFC 8252):** redirect URIs are compared exactly. Exception: `http://127.0.0.1` URIs allow any port (RFC 8252 §7.3). Never applied to other hosts. `localhost` is not treated as loopback (only `127.0.0.1`).

**D-041/5 — refresh-token rotation + reuse detection:** every token refresh issues a new pair (access + refresh) and invalidates the old refresh token atomically via SQL `UPDATE WHERE UsedAt IS NULL`. Reuse of an already-consumed refresh token (a theft indicator per RFC 6819) triggers immediate revocation of all of the user's active tokens (`RevokeAllUserTokensAsync`).

**D-041/6 — endpoint mapping Minimal-API + Razor consent:** the machine OAuth endpoints as Minimal-API extensions (`OAuthEndpoints.cs`, `WellKnownEndpoints.cs`) — no MVC stack. `GET /oauth/authorize` as a Razor page (`OAuthAuthorize.razor`, `@attribute [Authorize]`, static SSR) — uses the existing cookie auth + return-URL mechanism of `Login.razor` without additional plumbing.

**D-041/7 — `ITokenValidator` evolved to `TokenValidationOutcome`:** the interface result extended from `bool` to `TokenValidationOutcome { IsValid, Kind, Subject, ClientId, Scope }`. `StaticTokenValidator` → `Kind="static-bearer"` (behaviour identical). `OAuthAccessTokenValidator` new. `CompositeTokenValidator` as a new `ITokenValidator` registration: static first, then OAuth. `BearerTokenHandler` builds claims from the outcome. Backward compat: the static path bit-identical to before.

**D-041/8 — backward compat Claude Code CLI:** `ATELIER_MCP_TOKEN` still fully functional. `CompositeTokenValidator` checks the static token first — Claude Code CLI requests never pass through the OAuth path. Both auth paths coexist without a configuration change.

**D-041/9 — audit log + cleanup background service:** all relevant OAuth operations write `OAuthAuditLogEntry` (5 tables, migration `Step19McpOAuth`). `OAuthCleanupBackgroundService` deletes expired auth codes and access/refresh tokens; the audit log stays permanent (forensics).

**Accepted deviations from the blueprint (documented in `geef_architecture.md`):**
- `token_type_hint` in revocation: SHOULD per RFC 7009, not implemented (accepted)
- `scopes_supported` in the metadata endpoint: string instead of array (accepted, since only one scope)
- TOCTOU window between `FindByXxxAsync` and `ConsumeAsync`: not an exploitable security problem (atomic `ConsumeAsync` implementation via `UPDATE WHERE UsedAt IS NULL`; accepted)

---

## D-042 — run-visibility isolation (user run isolation)

*Date: 17 May 2026 | Report: [reports/feature-run-user-isolation-report.md](reports/feature-run-user-isolation-report.md)*

Run visibility restricted to one's own account. Admin override via explicit toggles ("show all users", "show system-wide statistics"). Foundation: OAuth (Step19) + multi-user (Step20).

**D-042/1 — username as the isolation key:** `Runs.CreatedByUser` (string) retained as the user identifier. No switch to a UserId FK — the username is stable since Step20, a migration would be purely cosmetic with no security gain.

**D-042/2 — null semantics for admin bypass:** `requestingUsername = null` means system-wide in all service and repository methods. The caller decides (not the service) based on the user claims whether admin mode applies. Avoids code duplication between Web and MCP.

**D-042/3 — 403 for the web UI, a generic message for MCP:** RunDetail shows an explicit 403 page ("No access — this run belongs to another user") — clear UX. MCP tools return `null` (= "Run not found or access denied") — prevents a run-existence leak via the API.

**D-042/4 — static-bearer-token → admin mapping:** runs via `ATELIER_MCP_TOKEN` (Claude Code CLI) are attributed to the configured admin user (new: `ATELIER_MCP__STATIC_TOKEN_USER`, default: `ATELIER_USER`). No backfill of existing "mcp-client" runs. Security fix: the timing leak (length pre-check before `FixedTimeEquals`) removed.

**D-042/5 — welcome-stats default for the admin: own stats:** the admin sees their own stats by default; system-wide via a toggle. Prevents unintended privacy implications in a multi-user setup.

**D-042/6 — no cascade delete on user deletion:** runs remain in the DB when a user is deleted. The admin sees orphaned runs. Runs are historical value data — a destructive auto-delete is unacceptable.

**D-042/7 — index `IX_Runs_CreatedByUser`:** migration Step21 adds an index on `Runs.CreatedByUser`. Filter performance as the run count grows.

---

## D-043 — Template Studio Complete Edit (vollständiger Edit-Flow)

*Date: 18 May 2026 | Report: [reports/feature-studio-complete-edit-report.md](reports/feature-studio-complete-edit-report.md)*

Full field parity in the Studio Edit-Step so the Studio workflow requires no post-editing in the separate `/crew/profiles/…` editors. Provider/Model/MaxTokens, Advisor Mode/Trigger, Grounding settings, Executor-as-new-profile, UseExisting/CreateNew toggle, Field-Helps, and Reasoning display all added. Hybrid meta-LLM (proposes with Reasoning fields, defaults fill gaps).

**D-043/1 — Lean Extension: flat domain model, additive nullable Reasoning fields.** `ProposedProfile` and `ProposedTemplate` remain flat records with string-name references. Added only additive nullable fields: `ModelReasoning?`, `SystemPromptReasoning?`, `OverallReasoning?`, `ModeReasoning?`, `TriggerReasoning?` on `ProposedProfile`; `EvaluationStrategyReasoning?` on `ProposedTemplate`. Backwards-compat: old `AnalysisResultJson` (pre-Reasoning) still deserializes with all new fields defaulting to null. No wrapper records, no DB migration.

**D-043/2 — Lean Reuse: existing components unchanged.** `ModelSelector.razor`, `ReviewerPicker`, `AdvisorPicker`, `GroundingProviderPicker`, `ProfileEditorForm.razor`, and all live editor pages are reused without modification. Studio gets its own `StudioProfileSlot.razor` (Picker↔Inline toggle, no per-slot Submit, continuous binding) that embeds `ModelSelector` and Field-Helps. The two live editors are unchanged → no regression risk.

**D-043/3 — UseExisting vs. CreateNew = UI-State only.** `StudioSlotMode` enum (`UseExisting`/`CreateNew`) lives in `StudioEditStep.razor` as `SlotState` private class. CreateNew → Draft lands in `FinalNewProfiles` + name in `FinalTemplate.*Names`. UseExisting → name points to existing, no entry in `FinalNewProfiles`. Domain API (`MaterializationRequest`) unchanged.

**D-043/4 — Field-Help resource: central, German, spec-verbatim.** *(Language superseded by D-062 — field-helps are now English.)* `StudioFieldHelps.cs` (static constants) holds all Field-Help texts. `FieldHelp.razor` renders inline hints below each field. Every Studio edit field (template and all profile types) has a FieldHelp. No inline help strings inside components.

**D-043/5 — Defaults in appsettings.json `TemplateStudio:Defaults`.** New `StudioDefaults` subclass of `TemplateStudioOptions` (Reviewer/Executor/Advisor/GroundingProvider/EvaluationStrategy defaults). Values calibrated against existing System profiles. LLM-null fields filled from defaults in `TemplateStudioService.ApplyDefaults`.

**D-043/6 — Additive Tool/MCP schema, backwards-compat.** `TemplateProposalTool.cs` schema: `profile_type` enum extended with `"executor"`; optional Reasoning properties added. `ProposedTemplateDto`/`ProposedProfileDto` extended with nullable Reasoning fields (default null). Old MCP inputs without Reasoning or without executor type continue to work.

**D-043/7 — Atomic materialization via IAtomicTransactionFactory.** `TemplateStudioService.MaterializeAsync` uses `IAtomicTransactionFactory` (testable abstraction over EF Core `AtelierDbContext` transaction). Full validate → begin → create profiles → create template → commit; explicit `RollbackAsync` on any error. `CreateProfileAsync` extended with Executor branch. `MarkMaterializedAsync` called inside the transaction (correct: rolled back if CommitAsync fails). `GetDefaultMaxTokens` returns `null` for GroundingProvider (no LLM MaxTokens). `ValidateAvailabilityAsync` skips GroundingProvider profiles (they use Tavily/VectorStore, not LLM models).

**D-043/8 — Accepted architecture deviations.** Slot-state and `BuildRequest()` live in `StudioEditStep`, not `TemplateStudio.razor` (architecturally cleaner encapsulation). `bool ShowValidation` instead of `ValidationMessages` store (simpler, correct). `GroundingProviderProvider = "openrouter"` default — semantically the grounding profiles inherit this but the value is unused in `CreateProfileAsync` for grounding (no LLM call). `IAtomicTransactionFactory` abstraction instead of direct `AppDbContext` injection (architecturally superior). These deviations were reviewed and accepted in the evaluation.

**D-043/9 — Post-deploy corrections (2026-05-18, after browser smoke).** Three quality issues surfaced when the deployed Studio was exercised and were fixed in follow-up commits:
- **Generated profile prompts now follow the full Atelier profile anatomy.** The meta-prompt no longer caps system prompts at "100 words"; it mandates the same structure as the hand-authored system profiles in `SystemPrompts.cs` — for reviewers: role line, `submit_review` instruction, the 4-tier severity taxonomy with concrete domain examples, the ANTI-PATTERN calibration block, a domain focus checklist, and the "Respond in the language of the user briefing" line; for advisors: role line, "strategic guidance only", a numbered 2–5 observation list, conciseness rule, iteration-variance rule for `BeforeEveryExecution`. A full worked reviewer+advisor example was added to the few-shot. The advisor-discouraging principle was reversed: advisors are now actively encouraged whenever the task benefits from upfront/per-iteration guidance.
- **Meta-LLM upgraded** from `anthropic/claude-sonnet-4-5` to `anthropic/claude-opus-4-7`; `TemplateStudio:MaxTokens` raised 4096 → 8192 (a single tool call now carries several fully structured prompts).
- **MaxTokens floor.** `StudioDefaults.MinMaxTokens = 10000` clamps every generating profile up to that floor in `ApplyDefaults`, even when the meta-LLM proposes a smaller value (GroundingProvider stays `null`). Studio defaults raised: Reviewer/Advisor `MaxTokens` 2048 → 16384, Executor 4096 → 60000. The same too-low default affected all `MaxTokens: null` system profiles via `LlmOptions.DefaultMaxTokens`, raised 4096 → 16384; explicit actor configs `BriefingTreueReviewer`/`KlarheitReviewer` 2048 → 16384, `Executor` 8192 → 60000. `GroundingQueryExtractor` deliberately stays at 256 (it extracts only a short search query).

---

## D-044 — Finalizer-Foundation: Fünfter Profil-Typ, vollständige System-Library

*Date: 19 May 2026 | Report: [reports/feature-finalizer-foundation-report.md](reports/feature-finalizer-foundation-report.md)*

Adds a complete Finalizer phase as the fifth profile type (FileExport, MetadataEnrich, ExternalSink, Transform) with 17 system profiles, sortable pipeline chain in templates, Run Artifacts storage + download endpoint, full CRUD UI, Studio integration, and MCP tools. Completes the „F" in the GEEF acronym.

**D-044/1 — Flat domain model, GroundingProvider pattern.** `FinalizerProfile` is a flat record with `Dictionary<string,string>` Settings (JSONB) and `IsSystem`. No wrapper domain model. Typed settings records (`FileExportSettings`, `MetadataEnrichSettings`, `WebhookSinkSettings`, `EmailSinkSettings`, `TransformSettings`) wrap the dict in all executor implementations. Rationale: consistency with existing GroundingProvider pattern; avoids introducing a second abstraction level.

**D-044/2 — Separate `FinalizationActorCosts` table (not `IterationActorCosts` extension).** A new `FinalizationActorCosts` table (Id, RunId FK CASCADE, ActorName, ModelName, InputTokens, OutputTokens, CostEur, CreatedAt) replaces extending `IterationActorCosts`. `IterationActorCosts.IterationId` is a required FK — adding a nullable FK would introduce fragility and semantic pollution. Finalizer costs aggregate to `Runs.FinalizerCostEur`. Rationale: cleaner separation, no FK migration on the existing iteration table.

**D-044/3 — Partial-success contract (no new RunStatus enum value).** Finalizer errors produce an `ArtifactType.Status` artifact and set `Runs.FinalizerErrorMessage`. The Run status remains `Completed`. No `CompletedWithFinalizerErrors` enum value is introduced (avoided enum proliferation and downstream serialization migration). The error is visible in the RunDetail UI via the artifacts section.

**D-044/4 — Pipeline insertion: reload FinalText from DB.** `ExecuteFinalizationAsync` is inserted in `RunOrchestratorService` after `FinalizeRunCostsAsync`. It reloads the Run entity from the database because `PostgresEventSink` writes `FinalText` directly on `PipelineCompletedEvent` — the orchestrator does not hold it in memory. Discovered during grounding (spec pseudocode was incorrect).

**D-044/5 — MaxAttempts path covers advisor-retry failure.** When `RunFinalizersOnMaxAttempts=true`, finalizers run not only when the main pipeline fails to converge but also when the `OnConvergenceFailure` advisor retry also fails. The outer `catch (ConvergenceFailedException)` now wraps `TryConvergenceFailureRetryAsync` in a nested try-catch, setting `retryAlsoFailed=true` and proceeding to `ExecuteFinalizationOnMaxAttemptsAsync`. Found during Evaluation (R1 reviewer).

**D-044/6 — System profile names: deliberate deviation from spec draft.** The spec listed names such as `add-frontmatter`, `send-webhook`, `de-hedging`, `length-adjust-500`. The implementation uses more descriptive, internally consistent names: `add-front-matter`, `webhook-sink`, `tone-formalization`, `tone-casual`, `executive-summary`, `key-takeaways`, `glossary`, `add-reading-level`. The Table of Contents profile (`add-toc`) was not implemented; `add-reading-level` was substituted. Rationale: the implemented names are clearer to end users, internally consistent (all lowercase-hyphenated), and the system is self-documenting. No existing data depends on these names at the time of introduction. Acknowledged per D-044 (found by R1 reviewer).

**D-044/7 — ExportPath containment + GUID-based file paths (path-traversal prevention).** Download endpoint validates that the resolved `Path.GetFullPath(StorageUri)` starts with `Path.GetFullPath(ExportPath)`. File paths are constructed as `{ExportPath}/{runId:N}/{filename}` where `runId` comes from the GUID-typed route parameter and `filename` from a DB-stored value (never from user input). Defense-in-depth: both the GUID route constraint and the `Path.GetFullPath` containment check must pass.

**D-044/8 — Webhook auth-header stored plaintext.** The webhook authentication header is stored as plaintext in the `FinalizerProfiles` JSONB column and embedded in `CrewSnapshot` on every run. It is never logged or included in Status artifacts. Encryption at rest (e.g. `IDataProtectionProvider`) was considered but deferred: the auth header is a low-sensitivity bearer token (webhook endpoint security), not a user credential, and the production database is on a private network. UI hints corrected from "verschlüsselt gespeichert" to "im Klartext in der Datenbank gespeichert" (found by R2 reviewer).

**D-044/9 — MaxFileSizeBytes enforced before write.** `FileExportFinalizerExecutor` checks `bytes.Length > _options.MaxFileSizeBytes` after conversion but before writing to disk. Produces a `Status` artifact on violation. Default: 50 MB. Found by R2 reviewer (was defined in options but not enforced in first pass).

---

## D-045 — Model-driven web search on the CLI providers

*Date: 19 May 2026*

The `claude-cli` and `codex-cli` providers now run with web search enabled, so an actor can autonomously fetch current web information during generation. `claude` is invoked with `--allowedTools "WebSearch,WebFetch"` (web tools only — no Bash/Edit/Write, no full permission bypass); `codex` with the global `--search` flag placed **before** the `exec` subcommand (`codex exec` rejects it as a subcommand argument — discovered during live testing, the flag is global). Cost is subscription-covered (no per-search billing).

**Deliberate tradeoff:** sources the model searches are consumed internally and are **not** captured into `GroundingConsultation` / RunDetail — CLI web search has no citation or observability trail. This was accepted in favor of a minimal two-line adapter change over a Tavily-as-MCP-tool pipeline. The Tavily grounding provider remains the path for explicit, deterministic, cost-tracked, citable pre-briefing enrichment; the two are complementary. OpenRouter-routed actors (single-shot `OpenAiCompatibleClient`) cannot do agentic web search. Safety unchanged: codex `--search` has no per-call approval, claude allowlists only web tools — no new interaction/permission-prompt risk.

---

## D-046 — Run Resume: continue from where you left off

*Date: 19 May 2026 | PR #18 merged to `main`*

When a run ends in `Aborted` or `Failed` status, the user can restart it with one click. Two modes are available: **Seed mode** (the last completed iteration's `ArtifactText` is injected as a seed draft into iteration 1 of the new run — the executor refines rather than starting from scratch) and **Clean mode** (identical briefing and crew, fresh pipeline). An optional `MaxIterationsOverride` lets the user adjust the convergence limit for the resumed run.

**D-046/1 — Seed draft via a new grounding step, not a mid-pipeline checkpoint.** The Geef SDK pipeline has no resume-from-checkpoint mechanism. Instead, `SeedDraftGroundingStep` (implementing `IGroundingStep`) is used as the grounding step for resumed runs. It sets two context keys: `AtelierContextKeys.GroundedBrief` (the briefing text) and `AtelierContextKeys.SeedDraft` (the artifact text of the last completed iteration). On iteration 1, `ProfileBasedExecutor` reads the `SeedDraft` key and uses a "revise this interrupted draft" prompt instead of "write from scratch". On iteration 2+, the key is ignored. Rationale: minimal SDK coupling, no new abstraction beyond the existing `IGroundingStep` contract.

**D-046/2 — `AtelierPipelineFactory.BuildWithSeedDraft` mirrors `BuildWithAdvisorContext`.** A new static factory method with the same optional parameters (loggerFactory, additionalSinks, groundingProviderFactory, pricingCatalog, costAccumulator) but substituting `SeedDraftGroundingStep` for the normal briefing grounding step. The orchestrator dispatches to this path when `run.SeedDraftText is not null`. Advisor passes and cost tracking work identically to the normal path.

**D-046/3 — `MaxIterationsOverride` patched into `CrewSnapshot.ConvergenceOverride`.** The parent run's `CrewSnapshot` JSON is deserialized, the `ConvergenceOverride.MaxIterations` field is updated with the override value (or a new `ConvergenceOverride` record is created if `null`), and the patched snapshot is re-serialized for the resumed run. Rationale: convergence settings live in `CrewSnapshot`, not in `ConfigJson` — reusing the snapshot deserialization path keeps all crew logic in one place.

**D-046/4 — Owner check + non-resumable status guard in `RunService.ResumeRunAsync`.** The service rejects resume attempts if: (a) the parent run is not found, (b) the requesting user is not the owner (unless `requestingUsername` is `null` for admin bypass), or (c) the run's status is not `Aborted` or `Failed`. The check uses the same `null`-username semantics as D-042/2 (admin bypass for MCP).

**D-046/5 — `ResumeRunDialog` as a modal Blazor component.** `[EditorRequired, Parameter] Guid ParentRunId`, `bool HasIterations` (controls which mode is pre-selected), `int DefaultMaxIterations`, `EventCallback<ResumeOptions> OnConfirm`, `EventCallback OnCancel`. Seed mode is pre-selected when `HasIterations=true`; Clean mode otherwise. `MaxIterationsOverride` is `null` when the field is `0` or empty (falls back to the run's own convergence policy). `data-testid` attributes on all interactive elements for bUnit tests.

**D-046/6 — Migration `Step23RunResume`.** Two new nullable columns on `Runs`: `ParentRunId uuid NULL` (FK to `Runs.Id` with `ON DELETE SET NULL`) and `SeedDraftText text NULL`. Index `IX_Runs_ParentRunId` for parent→children lookups.

**Tests:** 1021 (1017 green + 4 known Playwright flakes). New test classes: `RunServiceResumeTests` (12 tests), `SeedDraftGroundingStepTests` (3 tests), `ProfileBasedExecutorSeedDraftTests` (3 tests), `ResumeRunDialogTests` (10 tests).

---

## D-047 — Custom Providers: CRUD-Entity für HTTP- und CLI-Provider

*Datum: 19. Mai 2026*

Provider werden zur sechsten CRUD-Entity in Geef.Atelier — anlegbar, editierbar, deaktivierbar und löschbar über die UI. Zwei Typen: **HTTP** (OpenAI-kompatibler Endpoint mit konfigurierbarem Auth-Header, optionalem models-Endpoint, Manual-Model-List und Cost-Tracking-Feldern) und **CLI** (Subprocess via cli-proxy mit vier CLI-Kinds: Claude, Codex, Gemini, Generic). 11 eingebaute System-Provider (8 HTTP + 3 CLI) ersetzen das bisherige statische `ProviderCatalog`-Dictionary. Der `IProviderService` (DB-basiert) ist ab sofort die primäre Quelle; `IProviderCatalog` bleibt als dünner Wrapper für bestehende Callsites (Legacy-Shim, nicht neu verwenden). Migration `Step24CustomProviders`.

**D-047/1 — `anthropic-direct` wird nicht als HTTP-Provider bereitgestellt.** Die Anthropic-API nutzt ein anderes Request-Format als die OpenAI-kompatible REST-Schicht. Ein generischer HTTP-Provider würde `system`, `thinking`-Blocks und die `anthropic-version`-Header-Konvention nicht korrekt abbilden. Anthropic bleibt ausschließlich als `claude-cli`-System-Provider erreichbar. Rationale: korrekte Ergebnisse vor Vollständigkeit.

**D-047/2 — `opencode-cli` wird nicht als eingebauter CLI-Provider geliefert.** Der Installationsmechanismus von OpenCode im Docker-Container war zum Zeitpunkt der Implementierung nicht stabil verifiziert. Nutzer können OpenCode als Custom-CLI-Provider mit `cli_kind=generic` selbst konfigurieren, sobald der Binary lokal verfügbar ist. Eine zukünftige Entscheidung kann OpenCode als System-Provider nachrüsten, sobald ein reproduzierbares Container-Build-Verfahren vorliegt.

**D-047/3 — Settings werden als `Dictionary<string, JsonElement>` (JSONB) gespeichert; typisierte Wrapper-Records lesen daraus.** `HttpProviderSettings.FromSettings()` und `CliProviderSettings.FromSettings()` sind pure Factory-Methoden ohne Seiteneffekte. Das JSONB-Format erlaubt additive Erweiterungen ohne Migrations-Churn; der Code bleibt typsicher durch die Records. Der `Provider`-Domain-Record selbst ist persistenz-frei (kein EF-Attribut).

**D-047/4 — System-Provider sind unveränderlich und nicht löschbar; Custom-Provider benötigen das Präfix `custom-`.** `ProviderService.EnsureCustomPrefix()` erzwingt den Präfix beim Anlegen. System-Provider werden durch `SystemProviders.IsSystemProviderName()` erkannt und von allen Mutationsoperationen ausgeschlossen. Das Präfix verhindert Namenskollisionen mit zukünftigen System-Providern und macht Custom-Einträge im Dropdown auf einen Blick erkennbar.

**D-047/5 — Delete ist nur erlaubt, wenn kein Profil den Provider referenziert (Cascade-Schutz).** `IProviderRepository.IsReferencedByAnyProfileAsync()` prüft per Raw-SQL `COUNT` über vier Profil-Tabellen (ReviewerProfiles, ExecutorProfiles, AdvisorProfiles, FinalizerProfiles — letztere via JSONB `->>'provider'` auf die `Settings`-Spalte). Soft-Disable via `SetActiveAsync` ist immer möglich; inaktive Provider erscheinen nicht in neuen Profil-Formularen, bestehende Profile (eingefroren im `CrewSnapshot`) bleiben funktionsfähig.

**D-047/6 — `LlmClientResolver` cached `Provider`-Lookups per Name via `ConcurrentDictionary<string, Lazy<Provider?>>` mit `LazyThreadSafetyMode.ExecutionAndPublication`.** Die `Lazy`-Wrapper garantieren, dass die asynchrone DB-Initialisierung pro Provider-Name exakt einmal ausgeführt wird, auch bei parallelen Anfragen. Fallback-Reihenfolge: DB-Provider → `LlmOptions.ProvidersFallback` (appsettings) → Exception. Der Resolver kann den Cache per Provider invalidieren, wenn `IProviderService` einen Update-Event signalisiert.

**D-047/7 — ModelCatalog: Cache-TTL von 24h auf 1h gesenkt; pro-Provider-Quellenauswahl.** HTTP-Provider mit `models_endpoint` rufen live ab; ohne `models_endpoint` wird die statische `manual_model_list` aus den Settings geliefert; CLI-Provider liefern ihre statische `models`-Liste aus den Settings. `IsUsingFallback()` signalisiert veraltete Cache-Zustände an die UI (Stale-Warning-Banner in `ModelSelector.razor`).

**D-047/8 — CLI-Proxy erhält einen generischen Endpoint `/v1/cli/{provider_name}/...` und einen internen Backend-Sync.** `ProviderConfigSync` lädt alle 60 s die aktiven CLI-Provider-Konfigurationen vom Backend-Endpoint `GET /api/internal/providers/cli` (abgesichert mit `X-Internal-Token`-Header). Der Endpoint ist mit `.AllowAnonymous()` registriert und prüft das Token manuell, damit keine Cookie-Auth-Middleware greift. Legacy-Endpoints `/v1/claude/...` und `/v1/codex/...` bleiben als Shims bestehen und routen auf den neuen generischen Endpoint.

**D-047/9 — `GeminiAdapter` und `GenericAdapter` als neue CLI-Adapter-Klassen mit abstrakter `CliAdapter`-Basisklasse.** Die abstrakte Basisklasse erzwingt `execute()`, `list_models()` und `health_check()` (Default: `shutil.which`). `GeminiAdapter` setzt `env["HOME"] = auth_volume` für den Token-Speicher und parst JSON mit Text-Fallback. `GenericAdapter` verwendet `prompt_args_template` (Substitution: `{prompt}`, `{model}`), `stdin_mode` und `output_format` (text/openai-json/jsonl). OpenCode ist bewusst ausgelassen (D-047/2).

**D-047/10 — `ProviderCatalog` (Legacy-Shim) verwendet `volatile CachedResult?` mit 5-Minuten-TTL.** Der Shim vermeidet Thread-Pool-Hunger durch synchrones Warten auf DB-Calls: nur bei Cache-Miss wird `.GetAwaiter().GetResult()` aufgerufen; danach antwortet er aus dem Speicher. `record CachedResult(IReadOnlyList<ProviderInfo> Items, DateTimeOffset Expiry)` ist unveränderlich. Der Shim ist als `[Obsolete]` markiert — alle neuen Callsites sollen `IProviderService` direkt nutzen.

**Tests:** 1065 gesamt (1061 grün + 4 bekannte Flakes unverändert). Neue .NET-Testklassen: `ProviderTests` (12), `ProviderServiceTests` (15), `ProviderRepositoryTests` (9), `ProvidersIndexTests` (8). Neue Python-Tests: `test_gemini_adapter.py` (7), `test_generic_adapter.py` (8), `test_provider_sync.py` (7) — 22 neue Python-Tests, 67 gesamt grün.

---

## D-048 — Workshop Dashboard: 13-Widget Live Dashboard as Entry Screen

*Date: 19 May 2026*

The primitive home screen (`/`) — hero + 4 stat tiles + recent runs list — is replaced by a full 13-widget Workshop Dashboard, ported 1:1 from a React prototype (`docs/design/dashboard-prototype/`, 6 JSX files). All 13 widgets are Blazor Server components with live SignalR updates, three-theme design system (Vellum/Noir/Petrol), and an admin scope toggle (My/All Workshops).

**Widgets:** WelcomeStrip (streak, CraftMark, today count), LivePressStatus (active runs rail with phase mapping), LedgerStats (cost + run counts, trend arrows), ActivityCalendar (365-day heatmap lvl-0..4), CrewDna (template distribution), CostForgeSankey (provider×role cost flows), IterationSweetSpot (histogram), ManuscriptsGallery (last 12 completed), TokenStream (sparkline by role), CriticsBenchMatrix (reviewer pass rates), ProviderBench (per-provider stats), KnowledgeBaseWidget (docs + chunks + top files), DayBookStream (live activity feed, 7-source UNION ALL).

**D-048/1 — `RunStatus` stored as string — raw SQL inserts use `'Completed'`, not integer `2`.** `RunConfiguration` has `HasConversion<string>()` — the DB column stores the enum name as text (`'Completed'`, `'Pending'`, etc.). All raw-SQL test inserts must use the string form. Inserting integer `2` stores the text `"2"` which EF's `WHERE Status = 'Completed'` never matches. Discovered and fixed during test stabilization.

**D-048/2 — `IDashboardService` registration moved from `LlmServiceExtensions` to `AddAtelierPersistence`.** The E2E test host uses a fake `ILlmClientResolver` and never calls `AddLlmClient()`. `SignalRRunNotifier` injects `IDashboardService` — leaving the registration inside LLM extensions caused DI validation failures for all E2E tests. Moving it to `AddAtelierPersistence()` (which the test host always calls via `builder.Services.AddAtelierPersistence()`) resolves the issue without modifying the test host.

**D-048/3 — `DashboardRepository` date parameters require explicit UTC offset.** The server runs at UTC+2. `DateTimeOffset.UtcNow.Date` returns `DateTime` with `Kind=Unspecified`; Npgsql converts it using the local timezone (UTC+2 offset), producing a parameter Npgsql rejects for `timestamptz` columns. Fixed by constructing `new DateTimeOffset(utcNow.UtcDateTime.Date, TimeSpan.Zero)` everywhere a date-boundary is needed.

**D-048/4 — CSS authored from scratch; no `atelier-dashboard.css` existed in the prototype.** The React prototype had no CSS file — only JSX `className` attributes and one HTML reference file. `wwwroot/atelier-dashboard.css` was authored from the JSX class names combined with the existing CSS variable system in `wwwroot/atelier.css`. All layout classes are global (no scoped Blazor CSS). Responsive breakpoints: ≤768 px collapses multi-column layouts to single column.

**D-048/5 — Naming conflicts between Blazor component classes and Core domain records.** Three components (`WelcomeStrip.razor`, `LedgerStats.razor`, `CriticsBenchMatrix.razor`) share their class name with identically-named Core domain records. Resolution: `[Parameter]` property declarations use fully-qualified type names (`Geef.Atelier.Core.Domain.Dashboard.WelcomeStrip`) instead of file renames, preserving the component naming convention.

**D-048/6 — Day-Book UNION ALL (7 sources) cached, no materialized view.** The Day-Book stream queries 7 sources in a single async UNION (completed runs, failed/aborted runs, knowledge documents, providers, OAuth clients, materialized templates, admin user events). Results are cached for 45 s in `DashboardService`. A PostgreSQL materialized view was explicitly excluded per plan constraints to avoid background-refresh complexity.

**D-048/7 — Migration Step25: `WordCount` on `Runs`, `ProviderName` on actor cost tables, performance indexes.** `Runs.WordCount` (int, nullable) backfilled via `regexp_split_to_array`. `IterationActorCosts.ProviderName` and `FinalizationActorCosts.ProviderName` backfilled via JSONB lateral join against `CrewSnapshot` (exact match), falling back to `split_part(ModelName,'/',1)` with provider-family heuristics. Index `IX_Runs_CreatedAt` (BTREE) added for heatmap and ledger range queries.

**Tests:** 1079 total (1074 green + 4 known flakes unchanged). New: `DashboardRepositoryTests` (6), `DashboardPerformanceTests` (2), `Step25MigrationTests` (4), plus 46 bUnit widget component tests.

---

## D-049 — LLM-Binding: Explicit Provider+Model Selection for All AI-Using Profile Slots

*Date: 20 May 2026*

Step 1 of 3 in the Grounding Improvement Series. Introduces a reusable `LlmBinding` value object and makes the provider/model choice explicit at every profile slot that calls an LLM — specifically Transform Finalizers (previously showing plain text inputs) and the Template Studio meta-LLM (previously using a hardcoded provider). Lays the foundation for Step 2 (Grounding Refinement) and Step 3 (AI-assisted Grounding Types) which will consume `LlmBinding` from their respective profile slots.

**Phase 1.2 Befund (blocking prerequisite, ermittelt):** `TransformFinalizerExecutor` already read Provider/Model/MaxTokens from `TransformSettings` via `llmClientResolver.ForProfile(settings.Provider, settings.Model, settings.MaxTokens)` — not hardcoded. All 6 System-Transform-Finalizer used `codex-cli` / `gpt-5.5` / `8192` MaxTokens explicitly in `SystemCrew.cs`. Template Studio: provider hardcoded as `"openrouter"` in `TemplateStudioService.cs:34`, model/maxTokens from `TemplateStudioOptions`. `FinalizationActorCost.ProviderName` column existed since Step25 but was not being written by the executor.

**D-049/1 — `LlmBinding` is a Core sealed record in `Geef.Atelier.Core.Domain.Llm`, not in Infrastructure.** Fields: `Provider`, `Model`, `MaxTokens`, `double? Temperature` (optional, null = provider default). Exposed as `TransformSettings.Binding` (computed property) and `TransformSettings.WithBinding(LlmBinding)`. This keeps the domain model clean and makes `LlmBinding` directly consumable by Step 2/3 without Infrastructure coupling.

**D-049/2 — `TransformSettings` retains flat JSONB keys rather than switching to nested object.** The existing `Dictionary<string,string>` format (keys `Provider`, `Model`, `MaxTokens`, `SystemPrompt`) is preserved. A nested `{"llmBinding": {...}}` would break all existing CrewSnapshots. `LlmBinding` is exposed as a computed property only. `Temperature` is added as a new optional flat key, written only when non-null (InvariantCulture dot-separator for round-trip safety).

**D-049/3 — No data migration required.** System Transform Finalizers already carry explicit Provider/Model/MaxTokens values in their Settings dict (seeded in Step22). Custom Finalizers without Temperature get `null` via `TransformSettings.From()` fallback — provider-default behavior. `CrewSnapshot` backwards-compatibility is automatic: old snapshots without Temperature parse to `null` via the trailing-optional `From()` logic.

**D-049/4 — `TransformFinalizerExecutor` validates provider liveness before the LLM call.** Uses `IServiceScopeFactory` (constructor-injected) to resolve `IProviderService` per request (executor is singleton; `IProviderService` is scoped). If the provider is missing or inactive: returns a `Status`-type `RunArtifact` with a clear error message; the Run itself is NOT aborted (partial-success contract from D-044). `ProviderName` is now correctly written to `FinalizationActorCost`.

**D-049/5 — `FinalizerEditor.razor` Transform section replaces plain `InputText` with Provider dropdown + `ModelSelector`.** Provider dropdown uses `<optgroup label="HTTP">` / `<optgroup label="CLI">` from `IProviderService.ListAsync(includeInactive: false)`. `ModelSelector` (reused from D-047) handles live model loading per provider. Temperature is an optional `<input type="number" step="0.1" min="0" max="2">` field. System-Transform-Finalizer slots are read-only (`CanEdit = IsNew || !profile.IsSystem`). New field-help constants in `FinalizerFieldHelps.cs`.

**D-049/6 — Template Studio meta-LLM provider is now configurable via appsettings.** `TemplateStudioOptions` gains `Provider` (default `"openrouter"`) so deployers can route the Studio's analysis call to any active provider without code changes. Existing behavior is preserved by default.

**D-049/7 — Studio meta-prompt extended to suggest cost-effective models for Transform Finalizers.** The system prompt now includes an explicit instruction: when proposing a Transform Finalizer, include `Provider`, `Model`, and `MaxTokens` in `finalizerSettings` and choose a cost-effective model (e.g., `gpt-4o-mini`, `gemini-flash`) since tone/style transforms do not require top-tier models. MCP `materialize_template_proposal` passes `finalizerSettings` keys through 1:1 — no schema changes needed.

**D-049/8 — `Executor/Reviewer/Advisor` profiles are NOT refactored to use `LlmBinding`.** These profiles have established `Provider`/`Model`/`MaxTokens` fields with 1074+ tests depending on them. Merging them into `LlmBinding` is a separate cleanup step with its own risk profile. Conceptually the same; physically separate — no persistence breaking change.

**Tests:** 1095 total (1089 green + 4 known flakes unchanged, 1 skipped). New: `LlmBindingTests` (5), `TransformSettingsTests` (12), `TransformFinalizerExecutor` inactive-provider test (+1), bUnit `FinalizerEditorTests` IProviderService registration fix (5 previously broken tests now green). Total new passing tests: +15.

---

## D-050 — Grounding-Refinement: optionaler KI-Filter nach Provider-Fetch (2026-05-20)

**Kontext:** Grounding-Provider lieferten Rohdaten ungefiltert ans Briefing. Irrelevante Web-Treffer, redundante Chunks und verrauschte Snippets zwangen den Executor zur manuellen Auswahl — Token-Verschwendung, Qualitätseinbußen.

**Entscheidungen:**
- **Pro-Provider-Refinement** (Option A): jeder Provider bekommt optional einen eigenen Refiner — Web-Refinement (Noise) und RAG-Refinement (Redundanz) sind unterschiedliche Aufgaben. Kein globaler Cross-Provider-Pass (separates optionales Feature für später).
- **Flache Settings-Keys** im bestehenden `GroundingProviderProfile.ProviderSettings`-Dict (`refinementProvider`, `refinementModel`, `refinementMaxTokens`, `refinementTemperature`, `refinementMode`, `refinementInstructions`) — backwards-kompatibel, keine Snapshot-Migration nötig.
- **`LlmBinding` aus D-049 wiederverwendet** — kein neues Binding-Konzept. `RefinementBinding: LlmBinding?`-Property auf `GroundingProviderProfile`.
- **`GroundingResult.ConsultationId: Guid?`** — Provider setzen dieses Feld nach Persistenz; der Orchestrator aktualisiert die gespeicherte Consultation mit dem `RefinementOutcome` (UPDATE-Pattern).
- **Filter- und Synthesize-Modus** mit Tool-Use-Schema (`submit_refinement`). Filter: pro-Quelle keep/drop mit Begründung. Synthesize: kohärenter Text mit `[n]`-Referenzen + Original-Quellen-Liste erhalten. Attribution in beiden Modi vollständig.
- **Graceful Degradation**: Provider inaktiv oder LLM-Fehler → Rohergebnisse durchgereicht, `WasSkipped=true`, Run NICHT abgebrochen. Konsistent mit D-044 (partial-success-Vertrag).
- **Hard-Cap 20 Quellen** an den Refiner; Überschuss ungefiltert beibehalten.
- **`GroundingActorCosts`-Tabelle** (Migration Step27) analog zu `FinalizationActorCosts` (D-044) — Refiner-Call-Kosten tracken.
- **`tavily-refined` System-Provider** als sofort nutzbares Demo-Profil (Tavily Advanced + Filter-Refinement, günstigstes Modell).
- **Grounding-Visualisierung** erweitert: Raw-Count → Refined-Count Badge, verworfene Quellen einklappbar mit Begründung, Synthesize-Text-Block, Skip-Hinweis bei Fehler.

**Nicht in diesem Step:** Globaler Cross-Provider-Pass, Refinement-Caching, neue Grounding-Provider-Typen (Step 3), Refinement für Executor/Reviewer/Advisor, Embeddings-basierte Vorfilterung.

**Fundament für:** D-051 (neue Grounding-Typen: url-fetch, news-search, academic-search nutzen denselben Refiner).

## D-051 — Grounding-Typen erweitern: Static-Context, URL-Fetch, News-Search

**Datum:** 2026-05-20  
**Status:** Umgesetzt in PR #23 (feat/grounding-types)

### Kontext

Das Grounding-System kannte nach D-050 nur zwei Provider-Typen: `tavily` (allgemeine Websuche) und `vector-store` (RAG). Reale Nutzungsszenarien brauchen aber: kuratierten Fixtext (Style-Guide, Glossar), gezieltes URL-Fetching bekannter Quellen und zeitkritische Newssuche.

### Entscheidung

Drei neue `IGroundingProvider`-Typen implementiert, die demselben Factory/DI/ConsultationId-Pattern wie die bestehenden Provider folgen:

1. **`static-context`** — kuratierter Text, immer unverändert injiziert, keine externe API, kein LLM.
2. **`url-fetch`** — HTTP-Fetch bekannter URLs, HTML-Bereinigung via AngleSharp, **SSRF-Guard als eigene Infrastructure-Komponente** (`IUrlSafetyValidator`).
3. **`news-search`** — Tavily-API mit `topic=news` + `days`-Parameter, `SourceCitation.PublishedDate` neu.

### Knackpunkte und Entscheidungen

1. **HTML-Cleaning:** HtmlAgilityPack 1.12.4 (MIT) + eigene Readability-Heuristik (script/style/nav/header/footer/aside entfernen). AngleSharp wurde erwogen (MIT, ~1 MB), verursachte aber einen `MissingMethodException`-Konflikt mit bUnit (das AngleSharp intern nutzt) im Test-AppDomain — daher HAP als Drop-in. Der Wrapper heißt weiterhin `AngleSharpHtmlContentExtractor` (Name aus Plan beibehalten, minimaler Diff). Refiner aus D-050 übernimmt Restbereinigung.

2. **SSRF-Guard (sicherheitskritisch):** `UrlSafetyValidator` prüft Schema (nur http/https), löst DNS auf (alle IPs, nicht nur erste), prüft IPv4- und IPv6-Private-Ranges inkl. Cloud-Metadata (169.254.0.0/16). Redirect-Kette manuell mit max. 3 Hops, jede Hop-IP erneut geprüft. `SocketsHttpHandler` mit `AllowAutoRedirect = false`.

3. **`urls`-Speicherung:** Newline-getrennte Zeichenkette im Settings-Dict — robust gegen Kommas in Query-Strings, konsistent mit anderen Textareas.

4. **`static-context content`:** Soft-Limit 50.000 Zeichen mit UI-Warnung; Hard-Cap 200.000 Zeichen server-side.

5. **Dashboard-Stage-Label:** `GroundingActorCosts` fließt als dritte Source in Cost-Forge und Provider-Bench ein; Stage-Label `"Refiner"` für Grounding-Kosten (konsistent mit Iteration/Finalizer-Pattern).

### Konsequenzen

- Refiner aus D-050 ist typ-agnostisch — alle drei neuen Typen erhalten automatisch Refinement wenn konfiguriert.
- `SourceCitation.PublishedDate: DateTimeOffset?` als trailing-optional hinzugefügt (backwards-compat).
- `GroundingProviderTypes`-Konstanten-Klasse eingeführt — kein Magic-String mehr in Providers.
- System-Profil `tavily-news` in `SystemCrew` für sofortige Nutzbarkeit.
- SSRF-Guard als wiederverwendbare Komponente (`IUrlSafetyValidator`) für zukünftigen `rest-api`-Provider (Step 4).

---

## D-052 — Grounding-Providers: academic-search and rest-api (2026-05-22)

**Status:** Implemented in feat/grounding-academic-restapi

### Context

After D-051 (url-fetch, news-search, static-context), the two remaining grounding scenarios described in the feature spec were: scientific paper retrieval from academic APIs, and arbitrary REST endpoint grounding. Both were deferred to a separate step.

### Decisions

**1. `academic-search` provider — three source adapters behind `IAcademicSource`:**
- `arxiv`: free, Atom/XML, no API key, CS/Physics/Math/Biology coverage.
- `semantic-scholar`: JSON API, optional `x-api-key` for higher rate-limits. Exponential-backoff retry on HTTP 429 (3 attempts, 2^attempt seconds).
- `openalex`: JSON API, polite-pool User-Agent, inverted-index abstract reconstruction, exponential-backoff retry on 429.
- Source selected by `source` settings key (defaults to `semantic-scholar`). Unknown source falls back to `semantic-scholar`.
- `AcademicSearchOptions(MaxPapers, DateFrom, Fields, ApiKey)` forwarded to adapter; API key resolved from ENV var at runtime (`apiKeyEnv` setting stores the variable NAME, not the value).
- No new NuGet dependencies: Atom XML via `System.Xml.Linq.XDocument`, JSON via `System.Text.Json`.

**2. `rest-api` provider — security-critical ordering:**
- Template substitution (`{briefing}` → `Uri.EscapeDataString`) happens BEFORE SSRF validation — a crafted briefing cannot inject an internal URL.
- `IUrlSafetyValidator` from D-051 reused — zero duplication.
- Redirect following disabled (`SocketsHttpHandler { AllowAutoRedirect = false }`).
- `authHeaderEnv` stores the ENV var name; value resolved at runtime, never logged.
- JSONPath extraction via `JsonPathNavigator` (new, System.Text.Json only, no Newtonsoft/JsonPath.Net dependency).
- POST body: `{briefing}` JSON-escaped (not URL-encoded) to safely embed inside JSON payloads.
- `maxItems` default 10 (safety cap, independent of JSONPath result count).

**3. System profile `academic-default`:**
- Semantic Scholar + openrouter/gemini-2.0-flash-lite filter-refinement, seeded via `Step28AcademicRestApiGrounding` migration.
- Registered in `SystemCrew.GroundingProviderProfiles`.

**4. `JsonPathNavigator` — lightweight internal JSONPath:**
- Supports `$`, `$.field`, `$.a.b`, `$.a[*]`, `$.a[*].b`, `$.a[0]` (negative index supported).
- Zero dependencies; ~120 LOC static class.
- Not adding `JsonPath.Net` or `Newtonsoft.Json` — they introduced assembly-version conflicts in prior steps.

**5. TemplateStudio meta-prompt updated:**
- `academic-search` guidance: propose for scholarly/academic briefings; when not to use.
- `rest-api` guidance: conservative — only propose with a concrete identified API endpoint.
- `grounding_provider_type` allowed-values list extended.

**What is NOT in this step:** WebSocket grounding, streaming providers, authenticated academic sources with per-institution licensing, REST API POST-based authentication flows (OAuth), Grounding-Provider bUnit UI tests (deferred).

**Tests:** 1334 total (1324 green + pre-existing flakes unchanged). New: 92 tests — `GroundingProviderProfileAcademicRestTests` (27), `JsonPathNavigatorTests` (18), `ArxivSourceTests` (17), `AcademicSearchGroundingProviderTests` (17), `RestApiGroundingProviderTests` (9), `AcademicRateLimitRetryTests` (4).

---

## D-053 — Public Landing Page: Static SSR Entry Screen at `/` (2026-05-22)

*Date: 22 May 2026*

The route `/` was previously the authenticated Workshop Dashboard (`[Authorize]`, InteractiveServer). Anonymous visitors saw only a login redirect and never knew what Geef.Atelier was. A React prototype landing page was delivered (`docs/design/landing-prototype/`) using the same design language and CSS token system as the dashboard. This decision ports it to Blazor Server and restructures the routing/auth split.

**D-053/1 — `/` = public landing page; `/workshop` = authenticated dashboard.** `Landing.razor`: `@page "/"`, `[AllowAnonymous]`, `@layout Layout.LandingLayout`, no `@rendermode` (Static SSR — no SignalR connection for anonymous visitors). `Index.razor` moved to `@page "/workshop"`, keeping `[Authorize]` and `@rendermode InteractiveServer` unchanged. No other dashboard code changed; SignalR hub groups are keyed by username/role, not route.

**D-053/2 — Server-side redirect for authenticated users landing on `/`.** Static SSR `OnInitializedAsync` with `[CascadingParameter] Task<AuthenticationState>` and `NavigationManager.NavigateTo("/workshop")` produces a server-side 302 before any HTML is written — zero client-side flash. This is the same pattern used in `RedirectToLogin.razor`. `FakeNavigationManager.Uri` captures it in bUnit tests with `SetAuthorized()`.

**D-053/3 — `LandingLayout.razor` — bare layout, no wrapper element.** A new `LandingLayout.razor` renders only `@Body` without any wrapping `<main>` or other container element. Using `EmptyLayout` was rejected because it wraps output in `<main class="empty-layout">` with flex constraints that conflict with the landing page's full-width section design.

**D-053/4 — All existing `/`-anchored hrefs migrated to `/workshop`.** Three locations: `Brand.razor:1` (`href="/workshop"`), `NavMenu.razor:6` (`href="/workshop"`, `Match="NavLinkMatch.All"` preserved), `ConnectedClients.razor:14` (back-link). `<base href="/">` in `App.razor` unchanged.

**D-053/5 — Post-login redirect default changed from `/runs` to `/workshop`.** `Login.razor` refactored: `ResolvePostLogin(string? returnUrl)` is now a testable `internal static` helper (Reflection test: `ResolvePostLogin(null) == "/workshop"`). `ReturnUrl` from query string still honored — deep-links survive login.

**D-053/6 — Conditional asset injection in `App.razor`.** Landing CSS (`atelier-landing.css`) and JS (`js/landing.js`) are injected only when `Http.HttpContext?.Request.Path.Value == "/"`. Body gets `class="landing-body"`, html element gets `landing-root` class appended — server-side, no FOUC. All 27 CSS tokens referenced by `atelier-landing.css` already existed across all three palettes (vellum/noir/petrol) in `atelier.css`; no new tokens needed.

**D-053/7 — `landing.js` is plain JS (no Blazor interop).** Self-invoking IIFE. `IntersectionObserver` drives scroll-reveal (`.lp-reveal` → `.in`). GEEF flow choreography uses a second observer (threshold 0.32) + `setTimeout` sequence G→E→E-loop→F, toggling `is-active`/`has-passed` per phase. `prefers-reduced-motion`: sets `body[data-static="1"]` and skips all timers; CSS `@media` stops keyframe animations. No JS Interop registration — Static SSR pages don't establish a Blazor circuit.

**D-053/8 — `LandingIllustrations.razor` uses a `Name` parameter + `@switch` block.** First attempt used static `RenderFragment` properties with `__builder =>` lambdas in `@code` — this does not work cleanly in Razor. The switch pattern on a `[Parameter, EditorRequired] string Name` allows consuming components to render any of the 12 SVGs without a shared static reference. All animations classes (`press-anim`, `platen`, `paper-in`, `paper-out`, `stamp-glow`, `loop-path`) ported 1:1.

**D-053/9 — Nine stub pages, no content.** Footer links must not 404. `/pricing`, `/docs`, `/self-host`, `/status`, `/changelog`, `/contact`, `/imprint`, `/privacy`, `/terms` — each an `[AllowAnonymous]` Static SSR page using `LandingLayout` and a shared `ComingSoon.razor` component (parameter `Title`). `/contact` shows only "Coming soon" per `contact-protection.md` — no contact data exposed.

**D-053/10 — Hero headline fixed to `manufactory` variant.** "A manufactory for the written word." is a constant in `LandingHero.razor`. The alternative (`"atelier"`) was not ported.

**Tests:** 1382 total. New: 48 bUnit tests — `LandingPageTests` (35: Nav, Hero, GeefFlow 4-phase + IterationLoop + Iter-badge, Crew, Proof, Capabilities, Closing, Footer), `LandingRoutingTests` (13: anonymous render, authenticated redirect, Reflection route/attribute checks, `ResolvePostLogin` unit, ComingSoon, stub page attributes).

---

## D-054 — Continuous Learning Loop: Gated Self-Improvement (2026-05-22)

*Date: 22 May 2026*

Every Atelier run implicitly produces knowledge (reviewer findings, convergence trajectory, iteration deltas) that was previously discarded. This decision closes a continuous improvement loop: an opt-in `learning-extractor` finalizer extracts structured facts from a completed run, fires a dedicated, tightly calibrated **learning-evaluation** crew run as a quality gate (fire-and-forget), and only converged learnings land — domain-tagged — in a **separate store from the curated knowledge base**. A domain-aware grounding provider re-injects them weighted into later runs.

The naive approach (unfiltered self-RAG) was deliberately rejected: it leads to model collapse. **Three protective mechanisms are constitutive:** (1) separate namespace (`LearningEntries` table, `learning://` citation scheme), (2) the Learning-Run as auditable gate, (3) structured facts not free-form LLM prose. Added: a domain-aware retriever as the single most effective protection against the relevance trap.

**D-054/1 — `RunKind` enum: `Standard = 0 / Learning = 1`.** A Learning-Run is a normal run with crew `learning-evaluation` + `Kind = Learning`. No orchestrator dispatch change — `RunKind` is metadata that only gates the two finalizers. Backwards-compatible: existing runs default to `Standard`; `Kind` column added with `DEFAULT 0` in migration Step30. `SubmitRunRequest.Kind` and `IRunPersistenceService.CreateRunAsync` extended with an optional parameter.

**D-054/2 — Recursion stop is safety-critical and implemented as two guards.** (a) `LearningExtractFinalizerExecutor`: `if (run.Kind == RunKind.Learning) return Ok;` — extractor refuses to run inside a Learning-Run, preventing extraction of an extraction. (b) `LearningPublishFinalizerExecutor`: `if (run.Kind != RunKind.Learning) return Ok;` — publisher refuses to run in standard runs. Both guards read `run.Kind` via `IRunRepository.GetByIdAsync`. A dedicated unit test (`LearningRunDoesNotTriggerLearningRunTests`) proves the invariant is respected.

**D-054/3 — `LearningEntry` record with structured facts.** `(Guid Id, string Text, Guid SourceRunId, Guid? LearningRunId, string Domain, LearningStatus Status, string StructuredFactsJson, string OwnerUsername, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt)`. Status: `Proposed → Approved | Rejected`. Embedding stored separately as a pgvector column (not in the record constructor).

**D-054/4 — pgvector raw-SQL pattern (same as D-036 VectorSearchRepository).** `Pgvector.EFCore` 0.3.0 is incompatible with Npgsql 10.x. Vector stored as `vector(1536)` column with a string value converter in EF; INSERT and cosine-similarity search use raw ADO.NET (`NpgsqlParameter` with `NpgsqlDbType.Vector`). HNSW index with `vector_cosine_ops`.

**D-054/5 — `LearningExtractFinalizerExecutor` threshold + fire-and-forget.** Threshold: `iterationCount ≥ minIterations (default 2) OR any finding with Severity ≤ Major`. Structured facts assembled from: briefing essence, template name (= domain), iteration count, findings (Severity + Reviewer + Message), deltas between iterations. LLM call (configurable binding) with strict anti-hallucination prompt (only from facts). `LearningEntry` written with `Status = Proposed`. `SubmitRunAsync(Kind = RunKind.Learning)` called fire-and-forget in a try/catch — a submit failure does not fail the original run.

**D-054/6 — Learning-Evaluation crew: multi-model pluralism with `AbortOnCritical = true`.** Three strict reviewers to reduce correlated blind spots: `learning-factual-grounding` (openrouter/gpt-4.1 — hallucination / unsupported claim = Critical), `learning-value` (openrouter/gemini-2.5-pro — trivial/banal = Critical), `learning-generalizability` (claude-cli/claude-opus-4.7, ≠ executor — single-case-only = Critical). `ConvergenceOverride(MaxIterations: 2, AbortOnCritical: true)`. Finalizer chain: `learning-publisher` with `RunFinalizersOnMaxAttempts: true`. No grounding, no advisors.

**D-054/7 — `LearningPublishFinalizerExecutor`: gate output.** Finds the candidate `LearningEntry` with `LearningRunId == context.RunId` (set during fire-and-forget submit). Convergence → compute embedding via `IEmbeddingProvider`, `UpdateStatusAsync(Approved, UtcNow)`, write embedding. Non-convergence → `UpdateStatusAsync(Rejected, null)`, nothing written to the vector store.

**D-054/8 — Domain-aware retriever: `LearningRetrievalGroundingProvider`.** Query embedding from briefing; `SearchApprovedAsync(queryEmbedding, topK × 4)` fetches extra candidates; domain-boost re-ranking in memory: `finalScore = similarity × (sameDomain ? sameDomainBoost : crossDomainPenalty)`. Defaults: `sameDomainBoost = 1.0`, `crossDomainPenalty = 0.5`, `maxLearnings = 4`. After sort + cap: `SourceCitation(DocumentReference: $"learning://{id}")`. Curated knowledge (`vector-store` provider) is listed before `learning-retrieval` in crew templates — curated knowledge outranks learnings by default.

**D-054/9 — Management UI `/crew/learnings`.** Lists all learning entries with status badge (Vorgeschlagen / Genehmigt / Abgelehnt), domain, text preview, dates. Filters: status dropdown + domain text input. Actions: Approve / Reject / Delete (DeleteConfirmationModal). Expand-on-click detail: full text, source run link, learning run link, approved-at timestamp, structured facts (collapsible JSON). Owned by username; `ILearningService` enforces owner-check for all mutations.

**D-054/10 — MCP tool `list_learnings`.** Filters: `status_filter` (Proposed / Approved / Rejected), `domain_filter`. Returns id, text (truncated to 300 chars), domain, status, source run ID, learning run ID, owner, created-at, approved-at. Does not expose raw `StructuredFactsJson` (too verbose for MCP context).

**What is NOT in this step:** unfiltered auto-write (always through the gate); writing to the curated KB; analytics/insights dashboard; write-side learning dedup; learning expiry; cross-user/global learnings; multi-hop learning; backfill of historical runs.

**Tests:** 1439 total. New: ~57 tests — `LearningRunDoesNotTriggerLearningRunTests` (3), `RunKindTests` (5), `LearningStatusTests` (3), `LearningEntryTests` (4), `LearningExtractThresholdTests` (6), `LearningExtractFireAndForgetTests` (4), `LearningPublisherApprovedTests` (5), `LearningPublisherRejectedTests` (3), `LearningRetrieverDomainBoostTests` (8), `LearningRetrieverCapTests` (3), `LearningsManagementTests` (bUnit, 13).

---

## 23 May 2026 — Bugfix Learning-Retrieval-Provider (D-055)

### D-055: Bugfix — Continuous-Learning-Loop vollständig schließen

*Branch: `fix/learning-retrieval-provider` / PR #30*

Der in D-054 gebaute Loop war produktiv wirkungslos: `learning-retriever-default` erschien nicht in der Provider-Liste. Diagnose ergab zwei unabhängige Ursachen:

**Ursache 1 — lautloses Wegfiltern (Hauptursache):** `ListGroundingProviderProfilesAsync` gibt `[.. SystemCrew-Konstanten, .. DB-Zeilen-mit-IsSystem==false]` zurück. `learning-retriever-default` hatte `IsSystem=true` in der DB, fehlte aber in `SystemCrew.GroundingProviderProfiles` — es fiel durch beide Siebe ohne Fehlermeldung.

**Ursache 2 — fehlende DI-Registrierung:** `LearningRetrievalGroundingProvider` war nicht in `GroundingServiceExtensions.AddGroundingProviders` registriert. Die Factory hätte bei Laufzeit `InvalidOperationException` geworfen.

**Fix:**
- `SystemCrew.cs`: `LearningRetrieverDefaultProfile` als Code-Konstante + Eintrag in `GroundingProviderProfiles`-Dictionary.
- `GroundingServiceExtensions.cs`: `services.AddSingleton<IGroundingProvider, LearningRetrievalGroundingProvider>()` ergänzt.
- `CrewService.cs`: `ILogger<CrewService>` + Warning-Log wenn DB-System-Profile nicht in `SystemCrew` tracking → verhindert Wiederholung der Fehlerklasse.
- **Keine neue Migration** — das Profil existierte korrekt in der DB (Step30 lief).

**Mitbehobene D-054-Fehler:** `learning-extractor` war fälschlicherweise in alle vier Standard-Templates (Klassik/Juristisch/Akademisch/Marketing) eingetragen; entfernt (Finalizer ist opt-in). Regressions-Test-Guard `SystemTemplates_HaveNoFinalizers` war deshalb rot. `FinalizerProfiles`-Count-Test auf 19 aktualisiert.

**Folgefix 1 — Finalizer-Executor nicht in DI registriert:** Beim ersten echten Loop-Test: `LearningExtractFinalizerExecutor` und `LearningPublishFinalizerExecutor` fehlten in `FinalizerServiceExtensions.AddFinalizers()` — exakt dasselbe Muster wie die Grounding-Provider-Lücke. Fix: beide Executoren in `FinalizerServiceExtensions.cs` ergänzt.

**Folgefix 2 — EF Core kann `vector(1536)` nicht via ValueConverter schreiben:** Beim ersten LearningEntry-INSERT: Postgres `42804: column "Embedding" is of type vector but expression is of type character varying`. `Pgvector.EFCore` 0.3.0 ist mit Npgsql 10.x inkompatibel — der string-ValueConverter funktioniert nicht. Da Embedding ausschließlich via Raw-SQL (`LearningRepository.SetEmbeddingAsync`) gesetzt wird, ist `builder.Ignore(e => e.Embedding)` in `LearningEntryConfiguration.cs` die korrekte Lösung. EF Core berührt die Spalte nie.

**Real-Tests (alle nach Folgefix 1+2):** A (Provider sichtbar, `list_grounding_provider_profiles` zeigt `learning-retriever-default`) ✅ · B (Loop schließt sich: Standard-Run → `learning-extractor` fires → Learning-Run `Kind=1` → `learning-publisher` → `LearningEntry.Status=Approved` + Embedding) ✅ · C (neuer Run mit `learning-retriever-default` zieht Learning als Citation) ✅ · E (Backwards-Compat: alle 1528 Vor-Bugfix-Tests grün) ✅.

**Tests:** 1541 grün. New: 20 Regressions-Tests — `LearningRetrievalProviderResolutionTests` (8) + `LearningRetrievalProviderListingTests` (5) + `LearningFinalizerExecutorRegistrationTests` (7) — hätten alle drei Ursachen verhindert.

**Commits:** `70bfe3e` (Merge PR #30 fix/learning-retrieval-provider → main) · `504fafa` (Folgefix 1: Finalizer-Executor DI) · `7319d59` (Folgefix 2: EF Embedding ignorieren). Deploy: 23. Mai 2026, ~12:45 UTC. Health-Endpoint bestätigt `Healthy`.

## 23. Mai 2026 — Learning-Loop in alle Standard-Templates (D-057)

### D-057: learning-retriever-default + learning-extractor als Standard in allen Templates

*Branch: `main` / Commits `562d235` + `e935307`*

Alle vier System-Templates (`klassik`, `juristisch`, `akademisch`, `marketing`) erhalten `learning-retriever-default` als dritten Standard-Grounding-Provider (nach `tavily-basic` + `run-attachments`) und `learning-extractor` als einzigen Standard-Finalizer. Das `learning-evaluation`-Template bleibt unverändert (Rekursionsstopp).

**Begründung:** Der Learning-Loop war technisch einsatzbereit, aber ohne Default-Verdrahtung in keinem einzigen Run aktiv. Jeder Standard-Run erzeugt implizit Wissen; der Extractor-Finalizer ist per `run.Kind == Learning`-Guard selbst-schützend. Neue Runs profitieren sofort von domänen-gewichteten Learnings ohne manuellen Template-Edit.

**Mitbehobene Bugs:** `Docs.razor` hatte fälschlicherweise `[Authorize]` statt `[AllowAnonymous]` (Regression aus Step31-Commit c02231a). `TransformSettings`-Default-MaxTokens in Tests war noch auf 4096 statt 60000.

**Geänderte Tests:** `FinalizerRegressionTests` (umbenannt + neue Assertion), `KlassikTemplateGroundingRegressionTests` (Count 2→3), `KlassikUnchangedRegressionTests` (Count 2→3), `SystemCrewDomainTemplatesTests` (3× Count 2→3), `SettingsRecordTests` + `TransformSettingsTests` (MaxTokens-Default 4096→60000).

**Deploy:** 23. Mai 2026, ~14:30 UTC. Health-Endpoint bestätigt `Healthy`.

## 24. Mai 2026 — CLI-Modellnamen-Normalisierung, Pflicht-Findings, JSON-Retry (D-058)

### D-058: Robustheitsfixes — Modellnamen, Reviewer-Qualität, CLI-Proxy-Stabilität

*Commits: `4cf9023` (cli-proxy JSON-Retry) · `7f38c9a` (ModelNameNormalizer) · `fb3c5fa` (Mandatory Findings)*

Three independent reliability fixes shipped together after production run analysis revealed all three failure modes in live runs.

**Fix 1 — cli-proxy JSON retry:** When a CLI adapter returns plain text instead of the expected JSON tool-call output, the proxy now retries once with an explicit "respond with ONLY a valid JSON object" suffix. Previously, the C# `ProfileBasedReviewer` would receive `finish_reason='stop'` and mark the reviewer as `Failed`, causing an iteration with no findings at all. New: `_needs_json_retry()` helper + retry branch in both `_call_claude` and `_call_codex`. 8 new Python tests.

**Fix 2 — canonical CLI model names:** Model names in `SystemCrew`, `SystemProviders`, and custom DB profiles were stored in inconsistent formats (`anthropic/claude-opus-4.7`, `claude-haiku-4.5`, `claude-opus-4-7` all occurred). New `ModelNameNormalizer.Normalize(provider, model)` in `Geef.Atelier.Infrastructure.Llm`:
- Strips vendor prefix (`anthropic/`, `openai/`, `google/`) for all CLI providers.
- Replaces dots with dashes for `claude-cli` and `gemini-cli` (canonical: `claude-opus-4-7`).
- **Codex/OpenAI keep dots** — `gpt-5.5` is the canonical GPT name.
- `LlmClientResolver.ForProfile` normalizes at the call site before forwarding.
- All system profile constants updated to canonical names. 16 new tests, 6 existing tests updated.

**Fix 3 — mandatory reviewer findings:** Root cause of "4 reviewers, 0 findings" in run `76ae5590`: all reviewer prompts explicitly stated *"No findings means approved=true with an empty findings array"* — permission to be lazy. Fixed at two levels:
- System prompts: sentence removed, replaced with *"You MUST always provide at least one finding — even on fully compliant text, use 'info' severity."*
- Code guard (`ProfileBasedReviewer.ReviewAsync`): if `approved=true && findings.Count==0`, retries once with explicit reminder. 6 new tests.

**Key invariant:** `approved=true` with `info` findings is still an approval toward convergence. Mandatory findings only guarantees MARGIN NOTES are populated; it does not force rejections.

**Tests:** 1555 grün (+23 neue; 7 pre-existing Failures unverändert). Deploy: 24. Mai 2026.

---

## 4. Juni 2026 — Auto-Crew: Crew-Komposition als GEEF-Run (D-059)

### D-059: Auto-Crew — vollständige Crew-Komposition als eigener GEEF-Run

*Branch: `feature/auto-crew-composition`*

Das bisherige Template-Studio (D-038) delegierte die Crew-Komposition an einen einzigen Meta-LLM-Aufruf (Single-Pass). Dieser Ansatz erzeugt unvollständige Crews ohne Selbstkorrektur: der LLM-Call kann scheitern, ungültige Profil-Namen erzeugen oder strukturell inkohärente Specs zurückliefern — ohne dass eine Überprüfungsschleife greift.

**Entscheidung:** Crew-Komposition wird selbst ein vollwertiger GEEF-Run (`RunKind.CrewComposition = 2`) mit der Crew `crew-composer`. GEEF-on-GEEF: Draft → Critique → Refine → Converge → Materialize.

**D-059/1 — `RunKind.CrewComposition = 2`.** Das `RunKind`-Enum erhält den Wert `CrewComposition = 2` (neben `Standard = 0` und `Learning = 1`). Ein Kompositions-Run ist ein regulärer GEEF-Run mit Crew `crew-composer` und `Kind = CrewComposition`. Der Orchestrator dispatcht alle drei Arten identisch; `Kind` gatet die Rekursionswächter und steuert den Finalizer-Zweig.

**D-059/2 — Crew `crew-composer` (5 Reviewer, 1 Advisor, Crew-Catalog-Grounding, Finalizer `crew-materializer`).** Die Kompositions-Crew besteht aus:
- 1 deterministischer Reviewer: `CrewSpecValidatorReviewer` — prüft das JSON-Spec in der Schleife (kein LLM-Call); ein strukturell ungültiges Spec erzeugt ein Critical-Finding und stoppt die Iteration sofort.
- 4 LLM-Reviewer mit Modell-Pluralismus: `crew-diversity-reviewer` (prüft Reviewer-Vielfalt), `crew-prompt-quality-reviewer` (bewertet System-Prompt-Qualität), `crew-grounding-fit-reviewer` (Grounding-Provider-Eignung), `crew-finalizer-fit-reviewer` (Finalizer-Eignung).
- 1 Advisor: `crew-catalog-advisor` (BeforeFirstExecution, liest aktuelle Crew-Katalog-Daten als Kontext).
- Grounding: `crew-catalog` — stellt den vollständigen Katalog aller verfügbaren Profile als Grounding-Kontext bereit (crew-level Dedup via pgvector in `CrewTemplateEmbeddings`).
- Finalizer: `crew-materializer` (`FinalizerType.CrewMaterialize = 6`), ausgeführt von `CrewMaterializeFinalizerExecutor`.
- `ConvergenceOverride{MaxIterations = 4, AbortOnCritical = false, StagnationThreshold = 3}` — vier Iterationen genügen für gut strukturierte Kompositions-Aufgaben; AbortOnCritical deaktiviert, damit Validator-Findings korrigierbar sind.

**D-059/3 — `CrewComposerExecutor` erzwingt Tool-Call `submit_crew_spec`.** Der Executor für Kompositions-Runs ist ein spezialisierter `CrewComposerExecutor`, der via `tool_choice` den Tool-Call `submit_crew_spec` erzwingt. Kein Freitext-Output — nur strukturiertes JSON-Spec.

**D-059/4 — `CrewSpecArtifact`-Schema (Kompositions-Spec-JSON).** Das produzierte Spec folgt einem definierten Schema:
```json
{
  "mode": "existing-template | composed | new",
  "reuse": "<template-name> | null",
  "executor": { "name": "...", "provider": "...", "model": "...", "systemPrompt": "...", "maxTokens": null },
  "reviewers": [{ "name": "...", "provider": "...", "model": "...", "systemPrompt": "...", "focus": "..." }],
  "advisors":  [{ "name": "...", "mode": "...", "trigger": "...", "systemPrompt": "..." }],
  "grounding": ["..."],
  "finalizers": ["..."],
  "evaluationStrategy": "Parallel | Sequential | FailFast | Priority",
  "convergenceOverride": null
}
```
- Modus `existing-template`: vorhandenes Template wiederverwenden (kein Schreiben in die DB).
- Modus `composed`: neues Template aus Spec zusammensetzen und materialisieren.
- Modus `new`: komplett neues Template anlegen.

**D-059/5 — `FinalizerType.CrewMaterialize = 6` / `CrewMaterializeFinalizerExecutor`.** Der Finalizer übernimmt nach Konvergenz des Kompositions-Runs folgende Schritte:
1. **Parse** — liest das `CrewSpecArtifact`-JSON aus dem finalen Iterations-Text.
2. **Validate** — strukturelle Prüfung (RequiredFields, Reviewer-Count ≥ 1, gültige Provider-Namen).
3. **Dedup** — Cosine-Similarity-Prüfung gegen `CrewTemplateEmbeddings` (Schwelle 0,90): zu ähnliche Templates werden nicht doppelt angelegt; stattdessen Modus-Downgrade auf `existing-template`.
4. **Materialize** — legt Profile (Executor, Reviewer, Advisor, GroundingProvider, Finalizer) und Template in der DB an (atomar, analog `TemplateStudioService.MaterializeAsync`).
5. **Embed** — berechnet ein Embedding des neuen Templates und schreibt es in `CrewTemplateEmbeddings`.
6. **Chain Task-Run** — startet optional (`ChainToTaskRun = true`) einen neuen Standard-Run mit der materialisierten Crew und dem ursprünglichen Briefing.

**D-059/6 — Neue DB-Tabelle `CrewTemplateEmbeddings` (pgvector).** Speichert Embeddings materialisierter Crew-Templates für zwei Zwecke: (a) Crew-Catalog-Grounding im Kompositions-Run (semantische Ähnlichkeitssuche über vorhandene Templates), (b) Dedup-Check im Materializer (verhindert funktional identische Templates). Embedding-Dimension: 1536 (analog `LearningEntries`). HNSW-Index mit `vector_cosine_ops`. Raw-ADO.NET-Pattern (identisch D-036/D-054 wegen Pgvector.EFCore-Inkompatibilität mit Npgsql 10.x). Migration: `Step35AutoCrew`.

**D-059/7 — `ParentCompositionRunId` auf `Runs` (Migration Step35).** Neue Spalte `ParentCompositionRunId uuid FK→Runs nullable` auf der `Runs`-Tabelle: verbindet einen Task-Run mit dem Kompositions-Run, der seine Crew erzeugt hat (Audit-Trail). Kein Cascade-Delete. Der Orchestrator setzt diese Spalte beim Chaining automatisch.

**D-059/8 — Studio-Page umgewidmet: eigenständige Komposition (`ChainToTaskRun = false`).** Die bestehende `/crew/studio`-Page übernimmt die Rolle des Kompositions-Einstiegspunkts. Standardmäßig läuft der Kompositions-Run eigenständig ohne direktes Chaining auf einen Task-Run (`ChainToTaskRun = false`); der Nutzer kann das materialisierte Template danach manuell für neue Runs auswählen.

**D-059/9 — Rekursionswächter und Sicherheitsgrenzen.**
- `LearningExtractFinalizerExecutor`: `if (run.Kind == RunKind.CrewComposition) return Ok;` — verhindert, dass ein Kompositions-Run Learnings extrahiert.
- `CrewComposerExecutor`: lehnt Runs mit `Kind != CrewComposition` ab.
- Kein verschachteltes Auto-Compose: `CrewMaterializeFinalizerExecutor` startet niemals einen weiteren Kompositions-Run (guard: `ChainToTaskRun` darf nur einen `RunKind.Standard`-Run starten).

---

## 10. Juni 2026 — Tool System: Central ToolDefinition, Agentic Loop, Auto-Crew Integration, MCP Client (D-060)

### D-060: Tool System — complete implementation across Phases A, B, C

*Branch: `feat/tool-system` → merged to main 2026-06-10*

Atelier actors previously operated exclusively from model knowledge. A reviewer could not verify a factual claim, an executor could not fetch live data. External context entered only via Grounding (Push, eager, once). This feature introduces **agentic tool use (Pull)**: an actor calls tools *during its own turn*, decides when and how often, and uses the results directly in its output.

**D-060/1 — `ToolDefinition` as single source of truth.** A new Core domain record `ToolDefinition` (Name, DisplayName, Description, ToolType, Settings `Dictionary<string,string>`, SecretRef, LlmSchema `JsonElement`, AccessClass, IsSystem) is the sole definition for any tool capability. The same record drives both Pull (agentic LLM tool use) and Push (grounding context injection). Nine type constants cover all existing capability categories plus the new `mcp-tool` type. Two access classes: `ReadOnly = 0` (default), `Mutating = 1` (operator opt-in required).

**D-060/2 — Secret isolation.** `SecretRef` stores only the ENV-var key name; the value is resolved at execution time via `Environment.GetEnvironmentVariable`. It is never stored in the database, logs, `CrewSnapshot`, `ToolInvocation`, or any UI field.

**D-060/3 — `IToolExecutor` — unified execution layer.** A single `ExecuteAsync(ToolDefinition, inputJson, ctx, ct)` dispatches to the appropriate executor for all 9 tool types. Every call appends a `ToolInvocation` audit record (RunId, IterationNumber, ActorType, ActorName, ToolName, InputJson, OutputExcerpt ≤500 chars, CostEur, DurationMs, Sequence, Outcome). This is the single execution point for both Pull (called from `IToolUseRunner`) and Push (called from `ToolBackedGroundingProvider`).

**D-060/4 — Multi-turn LLM client.** `LlmRequest` gained an optional `Messages` list (replaces system+user when set). `LlmResponse` now carries all `ToolCalls`. `OpenAiMessageFormat` serialises full message history including `assistant tool_calls` and `tool` result messages. Existing single-shot paths are unchanged.

**D-060/5 — `IToolUseRunner` — the agentic loop.** The loop (build history → LLM call with tools → execute tool calls → append results → repeat) is provider-agnostic. Per-turn cap default: **5 tool calls**. Per-tool timeout default: **30 s**. Loop ends on plain-text response, on receiving the mandatory final tool (`submit_review`), or on cap. Cap-reached state is audited as `ToolInvocationOutcome.CapReached`.

**D-060/6 — CLI proxy agentic extension.** When the .NET loop sends a call with tools but without forced `tool_choice`, the proxy injects an agentic protocol addendum (respond with exactly one `{"tool_call": …}` JSON or plain final text). The forced single-tool path (`submit_review`) is unchanged.

**D-060/7 — Provider capability detection.** `SupportsAgenticTools(providerName)` returns `true` for HTTP providers, `false` for `generic`/disabled. Graceful degradation: when a tool-bound profile runs on a non-capable provider, both the run log and a UI badge make this visible — no silent failure.

**D-060/8 — Tool binding on all actor types.** `ExecutorProfile`, `ReviewerProfile`, `AdvisorProfile`, `FinalizerProfile` (Transform only) each gained `IReadOnlyList<string> ToolNames`. If non-empty and provider supports agentic tools, the actor uses the loop. Migrations Step38 (profile ToolNames columns) and Step39 (grounding profile ToolName column).

**D-060/9 — Grounding rebuild on central tools.** All grounding providers now reference a `ToolDefinition` by name. The 8 type-specific provider classes were refactored: raw capability logic moved into reusable executors. A `ToolBackedGroundingProvider` wraps any tool type for eager Push. System tools and grounding profiles are defined as code constants in `SystemTools` / `SystemCrew`; Migration Step40 seeds them idempotently on startup.

**D-060/10 — `CrewSnapshot` v2 + `ToolInvocationsBlock`.** Schema version incremented. Each actor profile in the snapshot now embeds its bound `ToolDefinition`s (fully dereferenced, no secrets). New UI component `ToolInvocationsBlock.razor` on the run detail page: shows per-iteration, per-actor tool calls with input, output, cost, duration — providing provenance for reviewer findings.

**D-060/11 — `/tools` CRUD UI + `ToolPicker`.** New pages for listing, creating, editing, and viewing tool definitions. Danger badge for `Mutating` tools. `ToolPicker.razor` is reused across all actor editors and grounding provider editors. Capability warning shown for non-capable providers.

**D-060/12 — Phase B: Auto-Crew tool binding.** `CrewPartSpec.ToolNames` added. `ToolCatalogGroundingProvider` (type `"tool-catalog"`) injects all tools as a Markdown table into composition runs, preventing name hallucination. `CrewComposerToolBindingProfile` (6th composer reviewer) evaluates tool binding decisions (necessity, access class, role fit, count, catalog membership). `CrewSpecValidator` Step 8 deterministically checks: provider capability (8a), tool existence (8b), Mutating blocked without opt-in (8c).

**D-060/13 — Phase C: MCP Client.** `McpServerConfig` domain record + persistence (Migration Step41). `AtelierMcpClientFactory`: SSRF check via `IUrlSafetyValidator`, optional AUTH from ENV var, `HttpClientTransport` → `McpClient.CreateAsync`. `IMcpToolDiscoveryService` calls `tools/list` and maps `McpClientTool` to `ToolDefinition` candidates (type=mcp-tool, `mcpServerId`/`mcpOriginalName` settings, `JsonSchema` as LlmSchema, ReadOnly default). `/mcp-servers` CRUD UI with discovery + import flow. `ToolExecutor` mcp-tool dispatch: look up `McpServerConfig` by `ServerId`, connect, `CallToolAsync` with parsed JSON arguments.

**D-060/14 — Mutating opt-in.** `CrewSpecArtifact.AllowMutatingTools` (`bool`, default `false`). The validator blocks any Mutating tool unless this flag is explicitly set in the spec. The UI marks Mutating tools with a red danger badge in both editor and detail view.

**Tests:** 1788 passing, 0 regressions. 5 DB migrations (Step37–Step41), all additive (no data loss on forward migration).

---

## D-061 — Specialization Packs: generic actors + reusable scoped specialization layers

**Context:** Actors with task-baked system prompts leak their old task when reused in another crew
(silent reviewer corruption). Full concept: [`prompts/konzept-spezialisierungs-packs.md`](prompts/konzept-spezialisierungs-packs.md),
implementation guide: [`11-specialization-packs.md`](11-specialization-packs.md).

**D-061/1 — Generic role + `{specialization}` slot.** Actors carry only a generic role prompt with a `{specialization}` slot. `PromptComposer.Compose` (pure) replaces the slot with the ordered, `\n\n`-joined pack texts (fallback: append under `## Specialization`); precedence is last-in-sequence-wins.

**D-061/2 — `SpecializationPack` entity.** New scoped entity (`Scope` General/DomainScoped/TaskBound, `ApplicableActorTypes`, `OwningCrewId`, `Archived`, `LastUsedAt`). System packs are code constants (`SystemPacks`) concatenated ahead of custom DB rows (`specialization_packs`, Migration Step42) — mirroring the actor-profile pattern.

**D-061/3 — System-actor rebuild.** The six domain-specialized system reviewers (legal/academic/marketing) were replaced by two generic reviewer roles (`domain-terminology-reviewer`, `substantive-rigor-reviewer`, each carrying the shared severity taxonomy + slot). Domain deltas moved into six DomainScoped system packs + two General example packs; the `juristisch`/`akademisch`/`marketing` templates bind them so the composed prompt stays behaviour-equivalent.

**D-061/4 — Per-crew binding + composition at snapshot-build time.** `CrewTemplate`/`CrewSpec` gained `ActorPackBindings` (jsonb, keyed `"<actorType>:<profileName>"`). `CrewSnapshotBuilder.ApplyPacksAsync` composes each actor's effective prompt into the embedded profile's `SystemPrompt` (factory + `ProfileBased*` unchanged) and records provenance in `CrewSnapshot` v3 `PromptCompositions`. Composition applies to executor/reviewer/advisor (the actors with a prompt).

**D-061/5 — Scope enforcement.** `ListForBindingAsync` excludes foreign TaskBound + archived packs; the crew-coherence check hard-blocks a custom template binding a foreign TaskBound pack; `CrewSpecValidator` Step 9 + the pack-catalog grounding + the prompt-quality/reuse-correctness composer reviewers enforce scope in the composer.

**D-061/6 — Composer two-level composition.** `CrewPartSpec.PackNames` + `CrewSpecArtifact.NewPacks`; `submit_crew_spec` schema gained `pack_names` per actor + top-level `packs`. **The parser keeps `tool_names`/`pack_names` on `reuse` references** — binding packs to reused generic reviewers is the primary use case. `CrewMaterializer` creates new packs (TaskBound → owned by the new crew) and materializes the bindings.

**D-061/7 — Lifecycle.** TaskBound packs cascade-delete with their crew. Promote/Clone-to-Generalize are gated by an LLM generality review (`IPackGeneralityReviewer`, fail-closed). `PackArchivalBackgroundService` auto-archives unused, unreferenced custom General/DomainScoped packs after `PackGc.RetentionDays` (default 90).

**D-061/8 — Audit UI.** `ActorPromptCompositionBlock.razor` on the run-detail page shows the role prompt, bound packs (with scope + order), and the composed effective prompt per actor.

**D-061/9 — Clean reset, keep accounts + auth (operator policy).** Migration Step42 keeps only user accounts and auth/credentials/config (`Users`, OAuth, `mcp_server_configs`, `Providers`, settings) plus the DB-seeded system catalogue (system tools/finalizers/grounding, preserved via `WHERE "IsSystem" = false` deletes), and wipes everything else for a fresh start: run history, custom profiles/templates/packs, embeddings, studio analyses, the knowledge base, learnings, and custom tools. Improved prompts come from code constants (no DB reseed). `pg_dump` backup before deploy.

**Tests:** specialization/composition/parser/validator/lifecycle units added; no new regressions (pre-existing DB/E2E/contention failures unchanged). Migration Step42 (additive schema + scoped custom-data reseed).

## D-062 — Single-language UI: English-only until real i18n

**Context:** The web UI had drifted into a German/English mix (page text English, field-helps and some labels German). Mixing languages in a single-language product is a defect, not a feature.

**Decision.** Until the Atelier is genuinely multilingual, the **entire web UI is English-only — no language mixing**. This reverses the earlier "Atelier UI = German" / "field-helps = German" convention (D-043/4 and the per-feature build-prompt notes). Converted: public legal pages (imprint/privacy/terms), all field-help resource files (`LearningFieldHelps`, `StudioFieldHelps`, `ProviderFieldHelps`, `FinalizerFieldHelps`, `GroundingFieldHelps`), nav/menu labels, validation/toast strings, the system-crew template DisplayNames (`Classic`/`Legal`/`Academic` — internal names `klassik`/`juristisch`/`akademisch` unchanged) and descriptions, and the `SiteSettings` default placeholders. Code/XML-doc stays English; docs/reports/commits stay German (unchanged).

**Scope note:** Historical build-prompts under `docs/prompts/` and completion reports under `docs/reports/` keep their original "UI-Strings = Deutsch" wording — they are a record of what was decided then, not living guidance.

## D-063 — Better use of the now-OpenAI-conform cli-proxy (structured outputs, token breakdown, gateway auth)

**Context.** The `cli-proxy` was upgraded to be broadly OpenAI-conform (streaming, real token usage incl. cached/reasoning, OpenAI error envelope, server-validated `response_format`, parallel tool calls, opt-in Bearer auth) and made reachable portfolio-wide on the shared `proxy` network (see [`docs/cli-proxy-openai-gateway.md`](../../../docs/cli-proxy-openai-gateway.md)). Geef.Atelier only consumed the old subset (non-streaming, tool-hack JSON, zero usage). This decision adopts the high-value, low-risk parts. Streaming was deliberately **not** adopted: the executor runs in document_mode (the proxy buffers — no token deltas) and reviewers/composers emit JSON (not streamed to UIs), so the UX gain does not justify the interface churn.

**D-063/1 — Real token usage + cache-aware cost.** `LlmTokenUsage` gained `CachedInputTokens`/`ReasoningTokens`, parsed from `prompt_tokens_details`/`completion_tokens_details` in `OpenAiMessageFormat`. `IPricingCatalog.CalculateCostEur` gained an optional `cachedInputTokens` param; `PricingCatalog` bills the cached subset at `PricingOptions.CachedInputDiscountFactor` (default 0.1). Cached tokens are threaded through all actor cost sites (executor, reviewer, advisor, composer, grounding-refiner, transform-finalizer, studio). The proxy already returns real counts since the gateway deploy, so per-run `TotalCostEur` is now real (was 0) and cache-discounted.

**D-063/2 — Structured outputs via `response_format` (capability-gated, tool-hack fallback).** New `LlmResponseFormat` on `LlmRequest` (json_object / json_schema), serialized by `OpenAiCompatibleClient`. New provider capability `SupportsStructuredOutputs` (`ProviderExtensions` + `ProviderSettingsKeys`, default true for Http/Cli) and `ILlmClientResolver.SupportsStructuredOutputs(provider)` — returns **false** for the OpenAI **Responses** path (that client cannot send `response_format`) so it keeps tool-calling. New `StructuredOutput` helper builds either path and extracts JSON uniformly (`ToolArgumentsJson ?? Text`). Adopted at **CrewComposerExecutor** and **TemplateStudioService** (clean pass-through sites). **ProfileBasedReviewer** (agentic tool-loop path) and **GroundingRefiner** (two-mode) deliberately stay on tool-calling — `response_format` does not fit a multi-turn loop and the benefit there is marginal. Non-CLI providers and OpenAI-Responses transparently fall back to the proven forced-tool JSON.

**D-063/3 — Gateway Bearer auth.** The cli-proxy is now shared, so opt-in auth (`CLI_PROXY_API_KEYS`) is enabled via a shared secret. `LlmClientResolver` overrides the `claude-cli`/`codex-cli` ApiKey with the `CLI_PROXY_API_KEY` env var when set (no hardcoded secret in appsettings; the .NET client already sends `Authorization: Bearer`). Wired through committed `docker-compose.yml` + gitignored `.env`/override; empty → open (back-compat).

**D-063/4 — Token breakdown persistence + display (Step43).** The cached-input / reasoning token subsets are now persisted per actor on `IterationActorCosts` (nullable `CachedInputTokens`/`ReasoningTokens`) and aggregated per run on `Runs` (same two nullable columns), both added by migration **Step43TokenBreakdown** (additive, `ADD COLUMN IF NOT EXISTS`). `ICostAccumulator.RecordActorCost` / `PendingActorCost` carry the two values (optional, default 0); all six call sites (executor, reviewer ×2, advisor, composer, ToolUseRunner) pass `TokenUsage.CachedInputTokens/ReasoningTokens`. The orchestrator sums them onto the run (null when zero). RunDetail surfaces the run-level breakdown as dedicated "Cached input" / "Reasoning" meta rows, shown only when those values are present.

**Tests:** Pricing/ProviderExtensions/CrewComposer/TemplateStudio units green (93/93 in the targeted run) + a new `RunCostAccumulator` token-breakdown unit; test fakes updated for the new interface members. The TemplateStudio "no structured output" contract is preserved via an `IsJsonObject` pre-check (clean `InvalidOperationException` instead of a raw `JsonReaderException`). Migration Step43 is additive.

## D-064 — Dynamic model catalog for CLI providers (2026-08-04)

**Context.** The model picker served CLI providers a list hardcoded in C#: `ModelCatalog` branched
on `ProviderType.Cli` **before** the network path and returned `settings.models`. After Claude Opus 5
shipped, the best available model was therefore missing from every picker — and the same staleness
would have repeated on every future release. The `cli-proxy` could already answer the question; the
Atelier simply never asked it.

**D-064/1 — One provider-keyed endpoint instead of a `cli_kind` table in C#.** The Atelier calls only
`GET /v1/cli/{providerName}/models`. Which CLI kind has a live source is a property of the gateway
and is reported by it through `x_source` (`live` / `static` / `degraded`) rather than maintained as a
constant list in C#, so custom CLI providers work automatically. The HTTP call lives solely in
`CliProxyModelSource` behind `ICliModelSource`, on its own `HttpClient` **without** a resilience
handler — a failing model lookup must not open the shared circuit breaker of real LLM traffic.

**D-064/2 — The proxy reports provenance and never caches a failure.** Previously
`return concrete or list(STATIC_MODELS)` turned a total probe failure — an expired claude OAuth
session, which already happened on 2026-07-22 — into an **HTTP 200 carrying the frozen list**, cached
for 24 h and indistinguishable from success. A failure is now reported as `degraded` and **not**
cached; the alias probe runs under the shared semaphore and reaps its subprocess on timeout;
`?refresh=1` forces a re-resolution, throttled to one real probe run per minute; and a background
task warms both caches at start-up and re-resolves them every 12 h **with** a cache bust. Without
that bust the 12 h sweep would have been talking to itself: it would read its own 24 h cache and
never discover a new model.

**D-064/3 — `*-latest` aliases as the binding, concrete id in the snapshot.** Profiles and system
crews point at `claude-opus-latest` and therefore follow every release. The `CrewSnapshot` holds the
**resolved concrete id**, resolved at the one place every run snapshot passes through
(`CrewService.ApplyPacksAsync`). Without that resolution the change would have traded maintenance
effort for an irreversible loss: a resumed run would silently execute on a different model than its
parent, and cost rows would average model generations together. `ResolveModelAsync` deliberately
**never** triggers a lookup — it runs on the run-start path — and trusts the mapping only until its
**own expiry stamp**. The criterion "is a list cached?" would be wrong and has been removed outright;
the reasoning is in `geef_architecture.md` section 9.3 and in the amendment below.

**D-064/4 — Live and curated lists are merged, not replaced.** The composer filters
`PreferredComposerModels` against the catalog. If the gateway's answer replaced the list, a curated
model could silently drop out of the prompt and auto-composition would pick a weaker one — no error,
no log line. Merging prevents that, keeps the recommended group non-empty, and stops existing
profiles from falling back into the free-text field.

**D-064/5 — Plurality check.** An alias and a concrete id name the same model. A crew with executor
`claude-opus-latest` and reviewer `claude-opus-5` passed every check and was still a monoculture
reviewing itself. Step 10 of `CrewSpecValidator` compares after resolution and reports the case as a
**non-critical** issue — including for reused reviewers, which are the common and therefore likelier
shape.

**D-064/6 — Migration Step45.** Lifts only `IsSystem = true` profiles on `claude-cli` from frozen
Claude ids onto the family aliases, family-preserving through explicit `IN` lists (including the
dotted forms — Step30 seeds `claude-haiku-4.5`). User profiles and profiles on other providers are
untouched: a system profile reaching Claude through OpenRouter must keep its OpenRouter id, for which
no alias form exists. `Down()` is a documented no-op. **Accepted side effect:** `IsSystem = true`
means "created by the system", not "never changed by the operator" — anyone who deliberately pinned a
system profile to an older generation is lifted and can only undo that manually.

**Deliberate deviations from `geef_architecture.md`.** `HasLiveEndpoint` is implemented as
`HasLiveSource` (the provider-keyed endpoint no longer has an "endpoint" layer). `StaticByDesign` now
also covers HTTP providers with a manual model list — factually right, but an extension beyond the
original scope, visible as a quiet note where there previously was no marking at all. The note text
therefore reads "this provider" rather than "this CLI".

**Lesson for future amendments.** Amendment §8 was implemented as a diff rather than re-derived:
each point was correct on its own, and every reviewer finding sat **between** the points — the cache
bust against the timeout budget, the alias map without a lifecycle of its own, and test doubles whose
pass-through behaviour is precisely the null effect of the newly introduced rules. An amendment
should name the tensions between its own points explicitly.

**Deploy order: `cli-proxy` first, then `web`.** Starting the Atelier against a proxy without
D-064/2 makes every CLI provider falsely report "provider unreachable".

**Open follow-ups.** (1) Read `x-cli-proxy-provider`/`x-cli-proxy-failover` so a run written by codex
through failover is not booked as `claude-cli` (pre-dates this change). (2) A persisted "last known
good list" as a fourth fallback layer. (3) The bUnit 2.x jump — bUnit 1.38.5 is binary-incompatible
with AngleSharp >= 1.5, which is why `GHSA-pgww-w46g-26qg` is suppressed for the test project rather
than fixed (AngleSharp is never in the production graph). (4) Mirror the OpenRouter entries of
`PreferredComposerModels.Reviewers` into `StaticModelFallback`, so no preferred reviewer can silently
drop out of the composer prompt there either.

**Amendment after reviewer iteration 2 — the guard rule, and further deviations.**

Iteration 2 produced four findings of the same shape: *every new guard keyed off evidence that only
the success path produces.* The cause is that this system uses **four** encodings for "no real
answer" — `null` from the transport, a success-shaped answer carrying a degradation marker (HTTP 200
with `x_source: degraded`), the ordinary return of a constant instead of a fetch (missing API key),
and the **absence** of state (no timestamp, no map) — and none of them was written down anywhere.
The table is now **section 9 of `geef_architecture.md`**, together with the rule: *a guard must never
rest on evidence only the success path produces* — check the **encoding**, not the **value shape**.

Concretely changed as a result: the alias map carries its **own expiry stamp** instead of relying on
"is a list cached?". That earlier criterion was wrong at its core: `IMemoryCache` is keyed per
provider, not per generation, and the failure branch writes **its own** entry under the same key —
the guard asked "is there *a* list?" where the contract needs "is *the* list still valid?". The two
coincide on the success path and diverge exactly in the failure case. Anyone later tempted to
"simplify" that guard has to refute this paragraph first. Likewise: the throttle for forced
re-resolution keys off the **attempt**, not the success (otherwise it does not throttle at all during
an outage), and the internal 12 h sweep is **exempt** from it — it is the only path that still
discovers a new model while something is broken.

A missing `OPENROUTER_API_KEY` reports `static`, **not** `degraded`: an absent capability is not an
outage, and `degraded` would put a permanent red "provider unreachable" banner on every codex picker
the moment the key is legitimately removed.

Further recorded deviations from section 8: `WarmUpTimeoutSeconds` is 240 rather than the 60 named in
8.4 (the reasoning there — "the proxy warms itself" — does not hold, because the nightly sweep sends
`?refresh=1` and thereby bypasses exactly that self-warmed cache); newly added are
`RefreshTimeoutSeconds`/`FetchBudget.Refresh` as a separate budget for the refresh button, and
`PreferredComposerModels.SecondaryDefault` as a named binding for auto-composed reviewers and
advisors. The `HttpClient` cap is derived from the budgets so that
`Interactive < Refresh < WarmUp < cap` holds — a transport cap below a budget makes that budget
ineffective without the configuration showing it.

Deliberately not closed: `RunOrchestratorService.ResolveSnapshot` reconstructs a snapshot for
pre-PS-5 runs directly from `SystemCrew` constants and therefore bypasses alias resolution. That
snapshot is never persisted; touching a defensive legacy path is not worth the risk.

**Amendment after reviewer iteration 3 — accepted trade-offs and follow-ups.**

*Cold-start window.* For roughly 60 seconds between web start-up and the first warm-up the alias
mapping is empty. A run starting inside that window writes the **unresolved alias** into its
snapshot. That is the deliberate flip side of `ResolveModelAsync` not triggering a lookup — the
alternative would be a network round-trip on the run-start path, before every run. The run itself is
fine (the CLI resolves the alias anyway); only the provenance of that one snapshot is blurrier.
Closing it needs the persisted "last known good list" from follow-up (2), not a lookup here.

*Throttle.* Both adapters throttle under the same contract, each with its own per-provider
timestamp: it applies only to **forced** re-resolutions, an ordinary call after a failure still
probes, and the internal 12 h sweep is exempt. The timestamp records the **attempt**, not the
success — a failed lookup writes no cache entry, so a success-based throttle would fail to throttle
in exactly the outage it exists for. The reason for the strictness is the claude side: one probe
spawns three real CLI subprocesses on a subscription shared with Hermes and the DMS worker. For
codex it is a single HTTP request, so the throttle there is cheap — but above all symmetric.

*Inline comments.* `geef_architecture.md` section 2.9 explicitly grants the existing comments in
`ModelCatalog.cs` legacy status ("do not multiply; replace inside blocks you touch"). Touched blocks
were lifted to XML doc; the remaining lines are untouched legacy. The section markers in the tests
keep the four error encodings from section 9.1 visible next to the assertion and are left there on
purpose.

*Recorded as follow-ups, deliberately out of scope here:*
1. `codex-cli`: the **recommended** flag stays curated while availability maintains itself, so a new
   GPT generation appears in the list but not automatically as the preselection. Belongs with
   follow-up (4) of the main entry.
2. A failed refresh discards the last known good **list** (the alias mapping is protected, the list
   is not). Impact is limited because `MergeWithKnownIds` keeps every curated id; the clean fix is
   the persisted last-known-good from section 8.9.
3. The refresh button can still report "provider unreachable" incorrectly when the gateway's CLI
   slots are busy with real runs. Much less likely after the throttle and the separate refresh
   budget, but not excluded.
4. The alias expiry test only covers the side **before** the stamp. The negative half needs an
   injectable clock in the `ModelCatalog` constructor; the value does not justify that contract
   change before this deploy.

*Post-deploy checklist (plan points 20-22), after `cli-proxy` then `web`:*
- **20** Live UI through the Playwright MCP: open the model picker; `claude-opus-latest` and the
  concrete Opus id must be visible, console clean.
- **21** `get_provider_models("claude-cli")` through the MCP: contains the concrete current Opus id.
- **22** `curl -s localhost:8090/v1/claude/models` — the answer must **not** be exactly
  `STATIC_MODELS` and must report `x_source: "live"`. This is the only direct test against the
  failure mode where a failed probe looks like a success.

*Deviation from section 3.6 (model DTO).* The architecture calls for one shared internal DTO pair
across both lookup paths. Two exist: `ModelCatalog` keeps `ModelListResponse`/`ModelEntry` for the
HTTP path, and `CliProxyModelSource` has its own `CliModelsResponse`/`CliModelEntry`. Reason: the CLI
answer carries `x_source` and `x_aliases`, two fields that have no place in the HTTP schema — real
reuse would have meant a base type plus an extension, i.e. more parts for two deserialisation shapes
that evolve independently. Kept deliberately and recorded here rather than left silent.

## D-065 — Image generation in the cli-proxy (2026-11-12)

**Decision.** The proxy offers `POST /v1/codex/images/generations` (OpenAI Images API) backed by the
codex CLI's `$imagegen` skill. One image per request, returned as a base64 PNG.

**D-065/1 — A dedicated endpoint instead of image output in chat.** `chat/completions` has no image
output; a home-grown format would not be understood by any client. The Images endpoint fits the existing
base URL (`/v1/codex`) and ready-made clients (OpenAI SDK, Open WebUI).

**D-065/2 — No failover, no breaker.** claude cannot generate images, so a handover could only go to
itself. Image faults deliberately do not feed `failover.record_failure`: a refused image prompt says
nothing about whether text requests work, and must not colour `/health` or the cliproxy-watch.
Limit → 429, anything else → 502.

**D-065/3 — Honest parameters.** `n>1`, `response_format=url` and `output_format≠png` are rejected with
400, as is `background=transparent` (images are always opaque; silently returning a different image
would be dishonest). `size` is only an aspect-ratio request inside the prompt (codex has no size
switch); the response reports the real size read from the PNG header. `usage` is omitted because the
agent turn's tokens are not the image model's. Measured 2026-11-12: landscape 1672 × 941, 37 s.
When codex exits normally without an image it is usually a refused prompt: 400 `image_generation_failed`
(dedicated `NoImageError`, never classified by wording, never 502, so nobody retries the prompt and
burns quota).

**D-065/4 — File instead of event stream.** The image is not in the JSONL stream but under
`$CODEX_HOME/generated_images/<thread_id>/`. The proxy reads it there and always deletes the directory
(`finally`); the `thread_id` is accepted only as a UUID because it becomes part of a deleted path.
Leftovers after a timeout, cancellation or restart are swept by the next job (older than 1 h, UUID
directories only); a cancelled request kills the codex process.

**D-065/5 — Quota protection.** Images draw on the ChatGPT Plus window that BookForge shares. Hence one
image job at a time (`CODEX_IMAGE_MAX_CONCURRENT`, capped at `CODEX_MAX_CONCURRENT − 1`, which also holds one codex slot), a 300 s timeout and
a daily cap of 50 per UTC day (`IMAGE_DAILY_MAX`, in memory, 0 = off).

**Not included:** `/images/edits`, several images per request, URLs instead of base64, a self-test in
the nightly self-update (it would cost quota every night).

## D-066 — Model matrix: model by role and domain, Claude/OpenAI only via the cli-proxy (2026-11-22)

**Why.** Crew composition worked but picked models arbitrarily:

- The executor was almost always Opus.
- Reviewers rotated through a fixed ranking (`PreferredComposerModels`).
- Several profiles paid per token via OpenRouter for Claude/OpenAI models the subscription cli-proxy serves.
- Many bindings pointed at retired generations: `gpt-5.6-*`, Grok, DeepSeek v4 and Gemini 3.x.
- The cli-proxy codex catalog was derived from OpenRouter and listed API-only `-pro`/`:batch` ids.

Research: `reports/2026-11-22-modellrecherche.md`. Design: `docs/superpowers/specs/2026-11-22-modellmatrix-design.md`.

**Decisions:**

- **Fixed rule (variant A, operator decision).** `Core/Domain/Crew/Models/ModelMatrix` is the single source of concrete model ids. (slot × domain) gives an ordered candidate list. The matrix table is in the German log D-066/2.
- **Managed profiles.** Executor, reviewer and advisor profiles carry `ModelSlot` and an optional `ModelDomain`.
  - Slot set ⇒ managed: the stored binding is display only, and the model is assigned per run at snapshot-build time (`IModelAssignmentService.ApplyAsync`).
  - Slot null ⇒ manual.
  - Grounding refiners and Transform finalizers use a settings marker instead.
- **Domain belongs to the crew** (`CrewTemplate.Domain`); the profile domain is an override.
- **Assignment rules:**
  - Live availability is checked; an unavailable catalog never blocks a run.
  - Reviewers and advisors avoid the executor's (alias-resolved) model and share it only with a plurality warning when no other candidate is usable.
  - Reviewers prefer another vendor family.
  - Same-slot reviewers rotate through a ring of the first 3 permitted candidates, formed per choice (D-066/12).
  - Paid candidates are used outside Legal/Academic only from 3 same-slot reviewers on, with at most 2 paid reviewers per crew.
- **Composer.** It chooses `model_slot` and `domain`, never provider/model (superseded by D-066/12: the matrix row comes from the explicit `model_domain`; `domain` is only a naming/pack hint). Validator step 11 checks slots and the CLI route. Step 10 checks only manual reused reviewers.
- **Bugs fixed along the way:** inline Transform finalizers lost prompt and model; inline grounding lost its settings and refiner.
- **Migration Step46:** new columns, crew domains, role classification of all known custom profiles, CLI rewrite for the manual rest, managed refiners, cleared legacy Studio default. It is idempotent, and a tolerant enum converter reads unknown values as manual.
- **Learning extractor and Template Studio defaults** come from the matrix (no paid routes).
- **cli-proxy:** codex models are read from the CLI's own `models_cache.json`. The Atelier client logs `x-cli-proxy-failover` warnings.
- **Host codex default is `gpt-6.2-sol`, not Astra** (shared quota with BookForge, larger context). Addendum 2026-11-23: new models are only offered to current CLI versions; the host CLI (0.151) rejected `gpt-6.2-*` until it was updated to 0.171.0, the container's version.

- **Always-current models without code changes (2026-11-23):** the cli-proxy exposes codex always-latest aliases `gpt-<line>-latest`, resolved to the newest listed model of the line in `models_cache.json`. They are listed first with `x_aliases` and translated to the concrete id before `-m`. Matrix, fallbacks, seeds and Step46 use `gpt-sol-latest`/`gpt-astra-latest`; run snapshots keep the concrete id. The nightly `scripts/cli-proxy-selfupdate.sh` also brings the host claude/codex CLIs to the container's tested version and moves the host codex default to the newest Sol, each step smoke-tested, rolled back on failure and reported.
- **Review hardening (2026-11-23):**
  - Availability is checked against the ids the live catalog actually reports. A provider without its API key counts as unavailable, and a paid fallback respects the cap.
  - Composer settings are whitelisted; inline `rest-api` grounding and inline `ExternalSink` finalizers are critical.
  - `model_slot` accepts a single name only.
  - Step 10 compares manual pairs only; manual reviewers on a managed executor's model are warned at snapshot build.
  - LearningExtract bindings and the Studio default are CLI-normalized.
  - Step46 also covers system Transform finalizers, system refiners, custom finalizer bindings and bare o-series ids.
  - After review round 2:
    - **Aliases:** codex alias maps marked `x_aliases_complete` replace the known mapping (also on a static start).
    - **Retired ids:** one retired-codex rule, parity-tested between `CliRoute` and the Step46 SQL; correctly routed newer ids stay.
    - **Fallback:** it honours usability and the paid cap.
    - **Finalizer type:** it is parsed once; unknown types are critical.
    - **Failover warning:** only on a CLI family change.
    - **Law areas:** an explicit list instead of `*recht`.
    - **Host sync:** it runs on every exit path, with an honest smoke test, atomic TOML writes, rollback alarms, no key in `argv` and script tests.
- **Structural changes after review round 3 (D-066/12):**
  - **Domain:** the composer states the matrix row explicitly as `model_domain` (keyword heuristic removed). A missing or invalid value is a warning; the crew then uses the free `domain` string if it is an exact domain name (old artifacts), else General.
  - **Assignment:** one rule (`ModelChooser`) with hard rules H1/H2 that are never relaxed and ordered soft rules. The ring is formed per choice from the currently permitted candidates (incl. the paid budget); the used-model memory runs per slot; the last resort is always unpaid and carries plurality/repeat warnings. An exhaustive invariant test (~645k combinations, checked against the state at each choice) is mutation-checked.
  - **Deliberate behaviour change:** once the paid budget is exhausted, the next reviewer gets an unused model instead of a repeat (five Academic DomainReviewers: Neon, Astra, Fable, Neon, Sonnet instead of …, Astra); the last resort takes the first unpaid candidate instead of the first candidate.
  - **Dedup:** a similar template is only reused when its domain equals the effective `model_domain`.
  - **Catalog:** a missing API key always reports the provider as unusable, even when the lookup runs out of budget; the cli-proxy serves the curated codex list once `models_cache.json` disappears, so `gpt-sol-latest` stays resolvable.
  - **`url-fetch`:** composer `urls` are no longer allowed, and inline `url-fetch` is dropped with a warning. DNS pinning stays an open hardening task.
  - **Other fixes:** strict enum names; model id before provider in `CliRoute`; a stricter container smoke test; rollback failures exit with 2.
- **Deploy order:** `cli-proxy` before `web`. Afterwards `GET /v1/cli/codex-cli/models` must show `x_source: live`, `x_aliases_complete: true` and `gpt-sol-latest` in `x_aliases`.

**Rejected:** `gemini-cli` as third family (inactive); a codex budget per crew; a run-time candidate chain; a family mapping in the proxy failover.

## D-067 — Stage 0: safety net (2026-11-24)

**Context.** An analysis on 2026-11-24 found that attached files, web search and learnings had had no
effect in production since grounding was rebuilt on central tools (D-060), without any visible error. The system
grounding profiles pointed at tools that were not wired in `ToolExecutor` or read a key name the container
does not set; the error ended as empty context. There was no test over the real wiring, runs did not report
problems they worked around, and several places silently fell back to substitute behaviour: to the classic
crew on an unreadable snapshot, to approving everything for a crew without reviewers, to starting despite a
failed migration. Stage 0 adds no new features for the user; it makes such failures visible.
It is the precondition for the rebuild in D-068.

**D-067/1 — End-to-end harness over the real wiring.** `tests/Geef.Atelier.Tests/EndToEnd` starts the
application through `WebApplicationFactory<Program>` against real Postgres (Testcontainers). Only the HTTP
counterparts (cli-proxy, Tavily, embeddings) are replaced, and their request bodies are recorded. The tests
prove that attachment and web-search content reach the executor prompt, and they fail when the grounding
repair is reverted. Every new pipeline path gets a test here from entry point to result. Description:
[02-architecture](02-architecture.md#testing-and-measuring-d-067).

**D-067/2 — Warnings at the run.** Whatever a run works around (skipped source, failed reviewer, deviating
model assignment, failover, finalizer problem) is stored as a warning in the new table `RunWarnings`
(`IRunWarningSink`, its own short-lived database scope per write, never decisive for the run). Shown on the
run detail page and in the MCP field `warnings` of `get_run_status` and `get_run_result`. The texts are
short sentences for the user; technical details belong in the log.

**D-067/3 — `Outcome` as a display signal, `RunStatus` unchanged.** `Runs.Outcome` (`Converged`,
`DeliveredWithOpenPoints`, `Failed`, `Aborted`) and `Runs.DeliveredIteration` state what a run delivered.
`Outcome` means "finished and stored, including finalizers". `RunStatus` stays as decided: a run without
convergence ends `Failed` even when it delivers a text (status decision of 2026-07-17). `Outcome` is
returned over MCP (`get_run_status`, `get_run_result`); the web interface does not read it, its "Warnings"
block chooses its wording from `Status` and whether a text exists. `DeliveredWithOpenPoints` also covers a
run stopped by a critical finding (status `Aborted`) that has a text. On failure, the findings of the last
iteration are stored too.

**D-067/4 — Fail loud instead of silent.** A failed migration aborts the start; the health check
`pending-migrations` reports pending migrations. An unusable crew snapshot fails the run with a message
(`CrewSnapshot.DeserializeForRun`) instead of falling back to the classic crew. A crew without reviewers
fails at start; `AutoApproveReviewer` is removed, and creating or saving a crew template without a reviewer
is refused (`CrewTemplateRules.EnsureHasReviewer`, also in the template editor). A run without a snapshot is
not resumed but rejected immediately with a message (`RunNotResumableException`). An unknown grounding type
is skipped with a warning. Error texts at the run and warnings are fixed, plain sentences; technical details
go only to the log.

**D-067/5 — Grounding bridge until the rebuild.** The five system grounding profiles no longer have a
`ToolName` and run through the provider-type path again; a run-time rule (`GroundingPathSelection`) also
covers older snapshots, and migration Step47 cleans stored rows. The agentic tool path was never wired into
the pipeline; it stays frozen and its removal is planned (D-068). A run with attachments becomes ready only
after its attachments are uploaded (snapshot afterwards, time limit `Orchestrator:PreparationTimeout`,
default 15 minutes); a resumed run finds the attachments of its predecessor.

**D-067/6 — Allow list for environment variables.** User-editable configuration may only reference
environment variables on an allow list (`IEnvReferencePolicy`: built-in list, prefix `ATELIER_SECRET_*`,
additions via `EnvReference:Allowed`). Names not on the list are rejected on save and never read at run
time. The editors for providers, tools, MCP servers and grounding are admin-only; Template Studio no longer
takes over key references.

**D-067/7 — Hub for signed-in users only.** `RunHub` requires sign-in, derives group names on the server
from the identity and ends when the sign-in expires (`MapRunHub`). The Blazor pages get live updates through
the in-process `IRunUpdateFeed` with the same access rules and no longer open a `HubConnection`. List
updates go only to owners and administrators. No external client of the hub is known; `RunHub` and the hub
part of `LiveRunNotifier` stay, protected this way, until their removal in stage 8 (D-068).

**D-067/8 — Learning switch off.** `Learning:Enabled` (default `false`) pauses the learning loop: no
extraction, no gate run, no embedding, empty retrieval. Crews with learning parts stay valid. Following
D-068 the loop will be replaced by a per-user memory.

**D-067/9 — Nightly update builds only from the committed state.** The nightly cli-proxy self-update builds
only from the committed state; with uncommitted changes it pauses and reports this, also with `FORCE=1`.

**D-067/10 — Mini run after every deploy.** `scripts/atelier-smoke.sh` signs in, submits a short job over
MCP, waits until the run's `outcome` is set and only then reads the result (exit code 0 only for
`Converged` or `DeliveredWithOpenPoints` with a text). Setup: [`ops/atelier-smoke.md`](ops/atelier-smoke.md).

**D-067/11 — Watcher.** `scripts/atelier-watch.sh` reports stuck runs, an unhealthy cli-proxy and unhealthy
containers through the server's existing alert channel, only for new or changed problems and once when
they clear; if its state cannot be stored, it keeps it in a fallback location and never floods the
channel. Setup: [`ops/atelier-watch.md`](ops/atelier-watch.md).

**D-067/12 — Measuring tool.** `tools/Eval` compares the Atelier with other paths on a fixed task set by
blind pairwise comparison; real tasks stay outside Git ([`tools/Eval/README.md`](../tools/Eval/README.md)).

**Consequences.**

- Problems become visible at the run instead of silently lowering quality; some runs that used to run
  silently with substitute behaviour now fail with a message.
- Runs that ended before Step47 have `Outcome = NULL`; there is no backfill.
- Migration Step47 is additive and idempotent (`RunWarnings`, `Runs.Outcome`, `Runs.DeliveredIteration`,
  `Runs.ReportJson`, clearing `ToolName` on five system profiles).
- The documentation describes the tool path as frozen ([10-tool-system](10-tool-system.md)).

**Way back.**

- Step47 removes nothing; `Down` only reverts the new parts. The new MCP fields are optional; older clients
  ignore them.
- Learning can be switched on again with `Learning:Enabled = true`.
- Missing names on the allow list are added via `EnvReference:Allowed` or the prefix `ATELIER_SECRET_*`,
  without a new build.
- Smoke run, watcher and the lock in the nightly update are scripts outside the application and can be
  reverted individually.
- There is no way back to the tool path for the system profiles or to silent fallbacks; removing exactly
  these states was the point of this stage.

## D-068 — Direction of the rebuild: order, direct path, questions, sources, memory (2026-11-24)

**Status:** decided, being implemented. A direction decision; the individual stages get their own entries.

**Goal.** A user, including a layperson, types one sentence, optionally adds files, and within minutes gets
a result they prefer to a direct model call. The result invents nothing, keeps hard requirements and states
its assumptions.

**Rationale.** Measurements on eleven real jobs showed: asking questions beats merely assuming 9 : 1, an
additional review round does not improve the verdict, and neither does preparing the job without new
information.

**Direction.**

- **Question rule.** For every gap in a job, this order applies: take it from the job and the files → take
  it from the profile and memory → research it → assume it reasonably and mark it as an assumption → ask
  only when the user can know the answer and it changes the result substantially. At most three questions,
  short, with ready-made answers to click, always skippable. Questions are asked only **before** the run,
  never during it; without questions the run starts at once. The code enforces the rule, not the goodwill of
  a model.
- **Order as the unit users see.** Users see orders: clarification, sources, versions, result. Runs remain
  the record underneath.
- **Direct path as the default.** Clarify, research if needed, one writing call with all sources in full
  text, checks and at most one correction. The existing loop remains selectable as "Extra careful".
- **Sources in full text.** Files go into the prompt in full instead of through an excerpt search;
  researched quotes are checked word for word against the retrieved text.
- **Memory.** Profile, remembered answers and corrections per user, without embeddings; it replaces the
  learning loop.
- **Removal in stages.** Agentic tool path, MCP client, Template Studio, crew composer, learning gate and
  embeddings are removed step by step. The SignalR hub `RunHub` (no client inside the application, no known
  external client) goes in stage 8 at the latest, together with the hub part of `LiveRunNotifier`; until
  then it stays restricted to signed-in users. Tables are dropped only one deploy after the code is gone;
  old snapshots stay readable.

**Unchanged.** The Geef SDK, the status decision of 2026-07-17, additive and idempotent migrations, the
single-language English UI (D-062; questions and result appear in the language of the job).

## D-069 — Stage 1: direct-path engine (2026-11-24)

**Status:** implemented (package P2 of stage 1), acceptance on a real run after integration.

**Decision.** The direct path is a system crew `direct` that exists only in code (`SystemCrew.CodeOnly*`, not in
template lists, the classic form or the composer catalogue). It runs on the existing engine; the Geef SDK is
unchanged.

- **One assembler per kind of pipeline.** The orchestrator builds every pipeline through an `IPipelineAssembler`
  (composition 100, direct 90, resume 50, classic 0). The three existing paths behave as before; the only addition
  is the hook `OnRunFinishedAsync`, called after the text is stored and before the outcome is recorded, so that an
  order never shows as finished without its report.
- **Sources.** `OrderGroundingStep` loads the files of the order and renders them as a source block
  (`<sources>`/`<source>` with page markers, defused tags). Above `Sources:FullTextMaxChars` a document map with
  exact line numbers comes first; the mode follows `SourceBudgetPlanner.Classify`, the same rule as on the start page
  and in the order service. The block goes to the writer as background document, which the cli-proxy stores
  as `context.md` above 50 kB. A file with the role "template to revise" becomes the starting text in `draft.md`
  instead. In the factory the new parameter `customGroundingStep` replaces the briefing step.
- **Writer.** One call in document mode, without failover (`x-cli-proxy-failover: off`), because another CLI cannot
  edit the same files. An empty or unchanged answer (also the proxy's `empty_result`) is repeated once; then the run
  ends with "The writer returned no text. Please try again." (a correction pass keeps the previous version).
  `backend_unavailable` ends the run at once with "The writing service is busy. Please try again in a few minutes."
  The prompt asks to read `context.md` completely, in sections, and to ignore instructions inside the files.
- **Read evidence.** The input tokens of the first answer are compared with the size of the files
  (`ReadEvidenceCheck`: agent overhead plus characters / 1.9 for German). Below 60 % the warning "Some of your files
  may not have been read in full." and a report note appear. Not a hard failure.
- **Checks without a model.** `RequestConstraintExtractor` reads length, language, a ban on placeholders,
  forbidden forms and terms from the request, conservatively: whatever refers to a part of the text, a file or
  something unclear becomes a note instead of a check. `DeterministicChecksReviewer` reports violations as
  `Rejected`; at most one correction (`MaxIterations = 2`), after which the SDK rule delivers the better version.
  Placeholders are allowed and shown as "Fill in before sending" unless the request forbids them.
  Rules after the first review: a text is "empty" only without a single word (a slogan or subject line is fine); a
  length is checked only when the request states it; a remark to the reader before the text ("Here's what I
  learned:") is a note, never blocking; numbered lines count as a list only from two in a row and never when they
  start with a date ("24. November 2026"); "no subheadings" allows a title on the first line.
  Rules after the second review (precision before recall): a negation only acts inside its own clause (clauses end
  at punctuation, dashes and "und/and/aber/but/oder/or/sondern/while/obwohl …"), so "Write more than 100 words and
  don't use emojis" keeps the lower limit. A wording with one direction ("höchstens", "at most", "less than",
  "mindestens", "at least", "nicht weniger als") is never turned around; a negation before it in its clause leaves
  the length unchecked. "more than/mehr als/exceed" becomes an upper limit only with a negation right before it (at
  most one word in between: "must not be more than", "nie mehr als") or with the fixed "… sollen es nicht sein"
  after it; any other negation in its clause leaves the length unchecked. A length becomes a check only when every
  length in the request is clear: one unclear length, another count in the same clause (pages, minutes) or a length
  for a part of the text leaves only a note. "more than A and not more than B" gives the range A–B. The tolerance
  rounds the upper bound down and the lower bound up ("at most 5 words" accepts 5). Separators such as "* * *" are
  not lists. A possible remark to the reader does not pass but never blocks; it is shown to the person as a hint
  apart from the open checks.
- **Report.** `Runs.ReportJson` (`RunReport`, schema 1) with constraints, check results of the delivered version,
  placeholders, sources, read evidence and notes.

**Reason.** The pre-test on reading `context.md` showed that the agent often searches instead of reading; it reads
in full reliably only up to about 150,000 characters. The codex document mode yields no text in the container.
Hence read evidence, an explicit reading instruction, no failover and an own empty-text check.

## D-070 — Stage 1: orders, sources and what the person sees (2026-11-24)

**Status:** implemented (packages P1, P3, P4 of stage 1 plus the fixes after the first review), acceptance on a real
run after integration.

**Decision.**

- **The order is the unit the person sees.** New tables `Orders`, `Sources`, `OrderSources` and `RunSourceUsages`
  (migration Step48, hand-written, additive and idempotent). Runs keep their role as the technical log; `Runs.OrderId`
  and `Runs.OrderVersion` link them to their order (version 1..n per order, a composition carries the version of its
  writing run). Step48 links existing runs to orders: one order per chain from a run without parent run; learning runs
  stay without order. The old ways (`/new`, `submit_request`, resume) create or extend orders as well; a failure to
  link is logged and never stops the run.
- **Display state only from the order and its current run.** Eight states (`preparing`, `needs_input`, `queued`,
  `working`, `delivered`, `delivered_with_open_points`, `failed`, `stopped`), derived by `OrderStateResolver` from
  `Orders.State` and the current run (highest standard version). An order with text is never shown as failed. A run
  that reached its final status but has no outcome yet counts as `working` for up to 30 seconds (the report and outcome
  are stored right after the status), and so does a finished composition until its writing run exists. A run ended by
  a critical finding that still has a text is `delivered_with_open_points`, not `stopped`.
- **Files.** One upload per file (`POST /api/sources`) answers with a receipt (pages, characters, quality, warnings,
  size class). Limits: up to 150,000 characters the writer gets everything in full text, up to 600,000 with a map of
  the files, above that the person chooses pages of PDF files (`Sources:FullTextMaxChars`, `Sources:MapMaxChars`; the
  first plan said 300,000/900,000, the pre-test showed that full reading is reliable only up to about 150,000). The
  start page counts chosen pages through the order service with the same rules as the start
  (`IOrderService.CheckSourcesAsync`) and keeps "Start" locked while a choice is missing, wrong or still too long; a
  file without pages that is too long on its own asks for a shorter version. Originals are kept in their own storage
  volume and removed by a background job when no order uses them any more.
- **MCP.** `submit_order`, `get_order_status` (long poll) and `get_order_result`; `submit_request` stays as a shell
  over orders. With the static token an order needs a user name (`AtelierMcp:StaticTokenUser` or
  `AtelierUser:Username`), otherwise `submit_order`/`submit_request` refuse.
- **Wiring without silent fallbacks.** The order service, the assemblers, the writer of the direct path and the
  orchestrator require their dependencies; a missing registration fails at start instead of running with defaults.

**Reason.** The person thinks in requests and texts, not in runs and crews. A single place that derives what is shown
keeps web and MCP consistent, and the limits follow what the writer reliably reads.

### D-069 addendum — safe intake of uploaded files (2026-11-24)

**Decision.** PDF and Word files are read in a separate process of the same application
(`--extract-source`, branched in `Program.cs` before the host is built) with a hard total time limit (kill of the
whole process tree), a managed heap limit (`DOTNET_GCHeapHardLimit`, 1 GiB) plus a memory watchdog, a bounded
answer, a cleared environment and a private temporary working directory. Every crash, kill or limit ends as an
unreadable receipt with a plain sentence. Uploads stream into a private temporary file instead of memory; a global
limit of uploads (8) and of extractions (2) with a waiting time, a per-user quota of files no order uses (50 files,
500 MB), a unique index on owner and hash (in Step48, which was not deployed yet) and an orphan scan in the cleanup
complete it. Inside the extractors: XML nesting limit 100 without DTD before the Word document model, iterative walk,
`MaxUncompressedBytes` 64 MiB with the ratio check from 64 KiB, and a rough unpacked-size check of PDF streams
(256 MiB). Page-marker look-alikes are neutralised after line wrapping and in all non-PDF files. A source deleted
between planning and storing an order ends as "One of the files was not found. Please add it again."

**Reason.** The security review of stage 1 showed that a 3 KB Word file ended the whole web process (stack overflow),
a 400 KB PDF used 4.7 GB, and the old time limit did not stop the work. Only a process boundary makes stack overflow,
memory and CPU limits enforceable; the checks in the extractors keep the common cases fast.

### D-069 addendum, second round — limits of uploads and extracted text (2026-11-24)

**Decision.** (1) One source holds at most `Sources:MaxExtractedChars` (3 million characters; a book of 1,000 pages has
about 2 million). The extraction process stops as soon as the text passes the limit (Word while scanning the XML
parts, before the document model; PDF at the page that passes it), text files are counted before decoding, and the
web process checks again before it copies the text. (2) The answer of the extraction process is limited to 16 MiB
(corrected to 20 MiB in the D-070 addendum: six bytes per UTF-16 unit, plus room for the JSON) and is
parsed while it streams in. (3) The quota of files no order uses also counts characters (20 million per person) and is
checked again inside the inserting transaction under a lock per person (`pg_advisory_xact_lock`). (4) The upload
endpoint reads the multipart body as a stream without a form buffer and takes the person's upload place (at most two
at the same time, otherwise HTTP 429) and the global upload slot before it reads the body; a request ends after
5 minutes (HTTP 408) or when it sends slower than 4 KiB/s; pasted text takes no slot. (5) Line ends are unified before
page-marker look-alikes are neutralised, so a lone `\r` cannot forge a page. (6) The compose file limits the web
container to 6 GiB (`mem_limit: 6g`).

**Reason.** The second security review showed that a 399 KB Word file (compression ratio 140, within every archive
limit) produced 58.7 million characters and more than 1 GB in the web process per upload, that the quota counted only
file bytes and could be passed by parallel uploads, and that the request body was buffered before any slot was taken.
A text limit at the source closes the first point where the text is still in the limited child process; the lock
makes the quota exact; streaming makes the slots effective. The memory limit is the second layer: the web process
usually stays below 1 GB, each extraction process may use 1 GiB of managed heap and at most two run at the same time,
the rest is headroom; the limit keeps one service from pushing a shared host into swapping or the out-of-memory
killer.

### D-070 addendum — length check deliberately conservative; upload answers (2026-11-24)

**Decision.** (1) The length check without a model blocks only when the request is clear and the text is far off;
otherwise it leaves a note. A length becomes a blocking rule only when the whole request (without quotes, code and
attached text) has exactly one length or a clear pair, no other count, no reference to an earlier text or a revision,
a limit word bound to the number, no foreign preposition between the instruction verb and the length in the same
clause and no compound such as "300-word post" (`LengthRuleGate`). The direction rules of the second round apply on
top. (2) A correction is asked for only when the text misses the limit by more than 25 %; a smaller miss is an open
point. (3) When the length is the only blocking problem, the instruction ends with "Keep everything else unchanged."
(4) When all upload slots stay busy, the upload answers 503 with `Retry-After: 30` and the receipt; the quota message
no longer names a way the page does not offer; a Word or text file that is too long asks for a shorter version
instead of a page selection. (5) The answer limit of the extraction process is 20 MiB (six bytes per UTF-16 unit:
emoji and format characters are escaped), correcting point (2) of the second round.

**Reason.** Every review round found a new class of false blocks in the word-based reading of lengths (an upper limit
read as a lower one, negations in a later clause, the limit of one tweet read as the limit of three, numbers of a
study or a book, the length of the previous version, lengths inside a quoted e-mail). A false block is expensive: the
correction forces the writer against the person's wish, up to several thousand words. Patching single sentences did
not converge, so the rule is now structural and favours precision over recall, and the false-block rate is measured on
synthetic requests labelled by a separate generator before the extractor saw them (development set, held-out set and
a second set in a hasty typing style). The 25 % threshold limits the damage of any remaining misreading to texts that
are far off.


## D-071 — Stage 2: clarify before the run (2026-11-25)

**Status:** implemented (stage 2: clarifier, gate, worker, measuring tool, order service, profile, web and MCP), shipped with
`Orders:IntakeMode = Shadow`. `On` follows in a separate commit once the acceptance gates of the corpus measurement and
the shadow operation are met.

**Decision.** Before the first run a **clarifier** analyses the order in exactly one structured call. A **gate in code**
brings its proposals back to the question rule. The person gets at most three short questions with clickable answers,
or none; every failure starts the order with the request as written.

- **Clarifier** (`LlmClarifier`): slot `ModelSlot.Intake` (Sonnet, fallback Sol), `response_format` json_schema
  `clarify_order`, `reasoning_effort: low`, hard limit `Intake:Timeout` (40 s), failover allowed. It sees the request,
  the start and end of every file (2,000 + 1,000 characters, at most 12,000 together and five files) and the profile.
  Tags in the material are defused; the material is something to analyse, never an instruction. No second attempt in
  the application: the proxy already repeats an invalid answer once.
- **Gate** (`QuestionGate`, pure function, every rule tested): profile before question (language, sender, signature,
  form of address); anything researchable is never asked; questions the person cannot answer or that change little
  become assumptions; a revision never asks about length, tone or form of address and asks at most one question; the
  length only from the fixed template (German, English; otherwise an assumption) and only when the request names none
  (an amount with a unit of length counts wherever it stands, e.g. "two paragraphs", "keep it to three sentences",
  "a few lines", "a 2-page report", and so do tweet, SMS, one-pager and one-liner; deliberately broad, "ten pages are
  missing" counts too, because a false hit only lets the writer choose while a miss would ask for something already
  written; this detection never feeds a length check of the text); a model question becomes the template only under
  format, scope or other and only when it asks for the length of the text itself ("How long should the post be?", "Do you want a
  short or a long version?"), never a question about a duration, a validity or an amount ("How long have you worked there?",
  "How long should the reply window be?");
  question ≤ 120 characters, 2–4 options ≤ 60 characters, no filler answers ("other", "egal"), no blocked words
  (`JargonBlocklist`, including plurals), no other language; one topic, one question; length first; at most three.
  Every dropped question becomes an assumption, except when its topic is asked anyway (a dropped duplicate never sets a
  guess beside the answer; narrow topics also take no assumption from another gap; facts and other keep theirs, from a dropped duplicate the one
  the model gave). The gate
  assigns all ids.
- **Flow** (`OrderIntakeService`, `OrderIntakeWorker`): atomic claim, at most two clarifications at a time, a wake-up
  signal after submitting. Zero questions start the run in the same step (`Clarified`); otherwise the order waits with
  its questions stored. Stuck clarifications start without questions after 60 s in the queue or two attempts,
  unanswered questions after 24 h with the assumptions. Claims from before the process start count as abandoned at once.
- **Shadow:** in `Shadow` mode the order runs as in stage 1; the worker also clarifies while the live queue is empty and
  stores the result only in `OrderIntakeShadows` (at most 50 per UTC day).
- **Answered length:** the answer to the template question becomes a length check "about n words" (±20 %) without the
  text parser (`IntakeConstraints.WithAnswers` in `OrderGroundingStep`). Free text is never parsed.
- **Measurement:** `tools/Eval/geef_eval/intake_corpus.py` generates and labels a synthetic corpus (exit code 4 while
  entries are missing or unlabelled), `tools/IntakeEval run` measures clarifier and gate on it, `IntakeEval evaluate` and
  `IntakeCorpusTests` replay the stored raw answers without a model and give every gate a verdict (passed, failed, not
  evaluable). No gate passes without data: missing entries, wrong corpus sizes, unlabelled entries or an empty held-out set
  or second corpus make every gate not evaluable, and so does a result for another input (every line carries a hash of
  request, files, profile, clarifier prompt and schema; `run` never skips such entries); the failure rate counts every
  attempt, the latency every answer and time-out (fast failures never improve it); an answer over the production
  limit or one that cannot be replayed is a failure. `IntakeEval shadow` judges gates a, f and g from a read-only CSV
  export of `OrderIntakeShadows` (see `tools/IntakeEval/README.md`).

**Rationale.** In the measurement of 2026-11-24 an agent with free tool access asked questions in every case and took
too long doing so. One structured call on a small model plus a gate in code make the question rule verifiable instead of
leaving it to the model. Assuming is cheaper than a needless question, so the gate is strict and every rejection only
costs one more assumption.

**Further decisions of stage 2 (order service, profile, web, MCP; review round 1).**

- **Briefing** (`Intake:BriefMode`, default `AppendAnswers`): the request stays first and unchanged; after a separator
  follow the answers of the person (answers of the AI client are marked as such, also the answer to the revision guess),
  the corrected assumptions, the assumptions shown and not corrected as non-binding defaults (the heading says that the
  answers and corrections of the person always take precedence and the assumptions apply only where neither they nor the
  request or the files say otherwise) and the profile. Assumptions never become a blocking check. `PreparedBrief` adds what the
  clarifier understood. A start before the request was read has no assumptions and no text claims any.
- **Answers** (`answer_order`, state `needs_input`): origin `user` or `client`, never `memory`; a question is answered by
  an option, own words (only where allowed) or "decide for me", never by two of them; an empty answer is refused;
  assumptions take only a correction as free text. Open questions count as "decide for me". Without any answer the order
  counts as skipped, also for "Just start" in the web, which sends everything the person already did. Answers that arrive
  after the start are reported as not used.
- **Revision:** a file becomes the text to revise only when the request uses a revision verb **and** the clarifier judges
  it a revision, also with a single file. Without the clarifier's judgement (failure, off, skip while reading) no file
  changes its role, as in stage 1. The guess is shown in web and MCP (`revisionFile`) and can be turned down.
- **Profile** (`UserProfiles`, migration `Step49Intake`): one row per user name, edited only by the person under "About me".
  It goes to the clarifier and to writer and reviewers. It is deleted with the account, moved when the account is
  renamed, and a new account never finds an old profile. Account and profile change in one database transaction
  (`IAccountStore`); the runs that stay under a deleted or given-up name lose the profile block in their stored briefing
  (a note takes its place). The tokens of an account are revoked before anything changes, also on a rename. Run pages and `get_run_details` show the profile block only to
  the owner; everybody else, administrators included, sees a note instead.
- **Outside services:** web, news and academic search, the embeddings of the learning search, the query shaping and the
  refinement get only the request as written, never answers, assumptions or the profile. Learning summaries also use only
  the request.
- **Gate, content:** texts are normalized first (NFKC, format characters removed, odd dots read as dots). Questions,
  options, assumptions and the summary are dropped when they contain a link, a web or mail address, an IP address with a
  path or a Markdown link; a bare name such as `Booking.com` or `Node.js` is fine only when it stands literally in the
  request of the person, never because of a file. They are also dropped when they mention a password, PIN, TAN, key,
  token, card or account number in any form (also inside compound words; lower-case "pin" after an owner word) unless the
  request itself treats the same kind (access data, or payment and account data; a request with an IBAN allows no
  password); a
  request for the secret itself is always dropped. Only the element is dropped, never the order.
  The MCP instructions describe these fields as derived from request and files, not as instructions.
- **Robustness:** a failing claim gives its slot back; an order that keeps failing in a sweep is left out for 15 minutes,
  doubled with every further failure up to 24 hours, so that no number of broken orders starves the younger ones; "Stop" tries again (up to three times) when the state changed at the same moment.
- **MCP host start:** the host does not start when a tool parameter is neither a client value, an SDK value nor a
  registered service (`McpToolBindingCheck`), so that a missing registration can never let a client fill a service.
- **Known limit for stage 7:** `IMemoryResolution` applies "memory before question" only to the four profile topics
  (language, sender, signature, form of address); `IntakeRecord.FromMemory` is stored but not read. Remembered answers of
  stage 7 need both to be extended.
- **Known limit, accounts:** runs and orders hang on the user name (older than stage 2). A new account with the name of a
  deleted or renamed one sees those old runs and orders with request, answers and result; only the profile block is
  removed from their briefings. Profile values in the stored clarifier record (`FromMemory`) stay; they are shown nowhere.

## D-072 — Clarifier switched on (`IntakeMode=On`) after measurement (2026-11-25)

**Decision:** `Orders:IntakeMode = On` ships. The code default stays `Shadow`.

**Measurement** (synthetic corpus: 120 main and 40 second entries, generated by one model and labelled blind by another;
input hash per result line): on the held-out set and the second corpus the gates a–f pass. 160 of 160 calls succeeded,
median about 6 seconds, no question on complete requests, no question on a forbidden topic, on average at most 0.25
questions per request. The warning threshold e′ is missed (6–27 % of the gaps labelled necessary are asked); this is
intended, when in doubt the writer assumes or leaves a placeholder.

**Three corrections from the measurement:**
1. The schema no longer limits text lengths. The gateway rejected the whole answer as soon as one assumption was too long
   (5 of 160 calls, mostly revisions). The parser caps the fields itself.
2. The gate removes options that only announce to hand the information over later ("I'll add it", "placeholder"); with
   fewer than two left the question is dropped. Derived from the development set.
3. The prompt demands that every option can be put into the text as it is, and forbids questions after which the same point
   would still need a placeholder, as well as questions about wording or praise.

**Question review instead of gate g:** gate g (one week or 30 real orders in shadow mode) cannot be reached soon with the
small number of real orders. It is replaced by an independent review of all 27 questions asked on the corpus, each beside its
request (bar: at most 10 % bad). First round: 3 of 29 bad, 7 borderline → do not switch on, correction 3. Second round with a
fresh reviewer: 20 good, 6 borderline, 1 bad (3.7 %) → switch on. The margin is small. The measurement after the corrections
uses the same corpus; the held-out set is therefore no longer untouched for future prompt changes.

**Follow-up:** after the first 30 clarified real orders their questions are reviewed again. Way back without a new image: set
`Orders__IntakeMode=Off` in the environment of the web service; the worker still finishes orders left in `Analyzing` or `NeedsInput`.

## D-073 — Stage 3: honest writing (2026-11-25, draft)

**Status:** implemented (writer notes, brief split, fact check, recalculation of the totals in the files, "What was checked",
report schema 2, migration Step50, web and MCP). Shipped with `Direct:FactCheckMode = Report`, **although gate V2 did not
pass** – as a documented deviation (section "Pre-tests"). `On` for everybody only after gate Q4 and the conditions below,
in a commit of its own.

**Decision.** The direct path says what it assumed, checked and changed, and claims nothing it could not check.

- **Brief split** (`Intake:ContentAssumptions = OpenPoints`): the clarifier's assumptions about facts of the case (`Facts`,
  `Date`, `Sender`, `Signature`, `Other`) no longer reach the writer as instructions but under "Not given by the person
  (never state these as facts …)". Assumptions about the form stay instructions. Without a shown card the heading no longer
  says "shown to the person" (side finding, no switch).
- **Block at the end of the file** (`Direct:WriterNotes`): the writer appends `<!-- atelier-notes {JSON} -->` with its own
  assumptions, open points and quotes from the files. The code removes it before checks, storage, export and display
  (`WriterNotesFormat.Split`); sources are defused so that a file can never end the text early.
- **Fact check for every standard order** (`SourceFactCheckReviewer`, profile `source-fact-check`, slot `FactReviewer`,
  today Codex Astra): the template `direct` now has two reviewers (`deterministic-checks`, `source-fact-check`). There is no
  selection by difficulty, length or daily quota. "Not checked" only comes from the switch `Off`, from `Shadow` or from a
  failure of the fallback model too; every reason is said honestly under "What was checked".
  - Three questions: facts the material does not back (including invented ways to reply), broken instructions, errors of
    the material itself (contradictions, wrong totals, replaced or dropped information).
  - Material is the request, the answers of the person and of the client, the profile and the files (for a revision the
    text to revise as `S0`), **never** assumptions or notes. Above 150,000 characters (material and text) it checks without
    the content of the files and says so.
  - A finding only counts with a word-for-word quote the code verifies (`FactFindingFilter`). Where an instruction comes
    from and whether a finding blocks follow from where the quote stands, never from the model: request, the person's
    answers and the profile block, the client's answers are hints, contradictions in the files never block and are always
    shown.
  - Round 2 only looks at its own findings again: what is still there word for word stays open without a call; for the
    rest exactly one `recheck_facts` call.
  - Print mode with `response_format`, proxy failover off. When the model cannot be reached (unavailable, limit, time
    limit), **one** attempt follows with the next candidate of the slot that is neither the writer's model nor from the
    provider that failed nor paid per token (today Sonnet). If that fails too, the SDK isolates the reviewer, the existing
    warning appears, the text is delivered, the report says "could not be completed".
  - Modes (`Direct:FactCheckMode`): `Off` makes no call; `Shadow` checks and stores, shows nothing; `Report` checks and shows,
    never changes the text (findings `Warning`); `On` also corrects once (blocking → `Error`). `Direct:FactCheckModeOverrides`
    sets the mode per user (measuring user `On`), only through the environment.
- **Recalculation of the totals in the files without a model** (`Direct:SourceArithmetic`): totals and calculations in the
  files are recalculated; the person learns about a mismatch with both figures and which one the text uses. Never
  blocking, never silent. A wrong written calculation in the text itself blocks.
- **"What was checked"** always stands right below the text: lines only for checks that ran, what was changed after the
  check, whether the person needs to look at something, and the fixed end "They do not prove that everything in the text
  is right." Only `OrderCheckLines` builds the sentences; web and MCP (`checks`) say the same, the display rules (order of
  the passages, three quotes per file) live only in `OrderResultRules`. The page never shows the model's own words, only
  fixed sentences and verbatim quotes. Without files the fact line says "compared with your request and your answers",
  never "and your files".
- **Report schema 2** (`Runs.ReportJson`, additive): notes of the delivered round, only quotes found word for word in their
  file (the rest is counted), state and scope of the fact check with calls and model, totals with their use in the text,
  `Corrections` (blocking in round 1, gone in the delivered version). Old reports (schema 1) stay readable and show "Facts
  were not checked."
- **Migration Step50** (nullable, additive, idempotent): `Findings.Category`, `Code`, `Quote`; `Runs.FactCheckMode`. No data
  migration, no index (there is no cap that would need to count).
- **Delivered version:** no own selection; the SDK rule (fewest rejections, newest on a tie) fits two rounds and two
  reviewers (test `DirectDeliveryRuleTests`).

**Measurement and gates:** V1 (block), V2 (calibration of the fact check, held-out set), V3 (recalculation) before the merges;
then gates Q1–Q8 with the measuring user on production. Whoever changes prompt or schema of the fact check measures again
(`tools/FactCheckEval`).

**Cost:** per direct-path order one Codex call per checked wording: one without a correction, two when any check (facts,
length, language …) led to a correction, in `Report` as in `On` (since the first review round every changed wording is
checked in full); when Codex is down one Sonnet call instead.
If the quota is not enough in the long run, the operating switch decides, never a selection in code.

**Way back:** switches through the environment without a new image (`Direct__FactCheckMode=Off`, `Direct__WriterNotes=false`,
`Intake__ContentAssumptions=Defaults`, `Direct__SourceArithmetic=false`). Code: previous web image; Step50 stays (nullable, the
code of stage 2 reads and writes around it), no `Down()` in production. Before that no direct-path run may be `Pending` or
`Running`: stage-3 snapshots contain the reviewer `source-fact-check`, which the code of stage 2 does not know.

**Pre-tests (2026-11-25):**
- **V1 (block at the end of the file):** result A, 15 of 15 runs without damage → `Direct:WriterNotes = true`.
- **V3 (recalculation):** passed → `Direct:SourceArithmetic = true`.
- **V2 (calibration of the fact check): not passed.** Validity is **83.3 %** (10 of 12 calls for 10 texts; required
  ≥ 95 %, below 90 % the gate is formally not even evaluable), **p90 latency 164 s** (required ≤ 120 s). The cause are
  hanging Codex calls: **3 of 31** calls over both sets did not answer before the limit of that time (180 s); two of the
  three texts (`fc-06/base`, `fc-14/base`) were repeated and then answered within seconds, the third (`fc-15/base`, development
  set) was never repeated. The cause of the hang is **unknown**; with this sample the true rate lies roughly
  between 3 % and 25 %. The quality criteria passed: 17 of 17 hits, 0.20 blocking findings per clean text (required
  ≤ 0.25), 80 % of the clean texts without a blocking finding (required ≥ 75 %), no reported source conflicts without a faulty
  source, the mandatory parents' evening case passed. These figures rest on **10 synthetic texts**; 0.20 is one finding.
  The development set is **incomplete**: 19 of its 22 texts were called (`fc-15/planted`, `fc-16/base` and `fc-16/planted`
  never ran, both bases have faulty files), 18 answered; the 0.38 below and any recount rest on this subset.
- **Deviation:** `Report` (checks and shows, never changes the text) is shipped anyway, on the decision of an independent
  adviser. This softens the gate; it is acceptable because only operating criteria failed, `Report` changes no text and the
  failure path is honest ("The fact check could not be completed."). `Shadow` would not help (the hang happens there too),
  nor would a repeat run (with about 10 % hangs a run of ten passes by chance with about 35 % probability). The operator can
  switch back to `Shadow` or `Off`.
- **Contract branches not executed (also a deviation):** neither "check parser and schema" (validity) nor the probe with
  `reasoning_effort: low` or a lower material limit (latency) were run: no answer arrived in the hangs, so parser and schema
  are not involved, and the successful calls were fast (median about 14 s, longest 86 s).
- `FactCheckCorpusTests` pins the result: never "passed", the shortfall only in validity and latency, the quality figures of
  both sets as a lower bound (hits of questions a and b, blocking findings on clean texts and beyond the planted mistakes,
  question c, conflicts without a faulty file) and the number of kept findings exactly; a filter that loses a hit, drops
  source conflicts or adds a false alarm turns it red (checked by mutation: a filter dropping every source conflict fails it).

**Time limits and hangs (conditions of the adviser):**
- Time limit of the checking model **90 s** (`Direct:FactCheckTimeout`, before 180 s), of the fallback model **60 s**
  (`Direct:FactCheckFallbackTimeout`); both configurable, a test pins the shipped values. With 90 s, "p90 ≤ 120 s" holds
  **by construction** – that is **no evidence** of a faster check.
- On a hang: no second Codex attempt (shared quota), exactly one attempt with the fallback model, otherwise "check not
  completed". An aborted check starts **no correction round** (a failed reviewer does not block convergence; end-to-end
  test).
- The Atelier sends its limit to the gateway (header `x-cli-proxy-timeout-seconds`, 5 s below). The gateway ends the CLI
  process with its child processes before the Atelier gives up, and also when a caller disconnects. Before, an aborted
  Codex process ran on until the gateway's own cap and used quota of another production system. Such a time-out opens no
  breaker and starts no failover; the Atelier never retries requests with this limit at the HTTP level.
- Every hang is logged as a structured line of its own (call, model, duration, limit, run, round, fallback yes/no), without
  content.
- **Deliberate deviation from the adviser:** he advised storing Codex sessions to find the cause. That is not done: the
  sessions would contain the users' requests, files and texts, and the gateway is stateless on purpose (Codex runs with
  `--ephemeral`). The cause is searched through the log lines (clustering by model, duration, time of day) and, if needed,
  a separate test with synthetic texts.
- **Effect on waiting time:** about one order in ten gets the fallback checker or no check and waits up to 150 s longer
  (90 s + 60 s).

**First review round (2026-11-25):**
- "What was checked" names a check as met only when it ran and passed; a check that ran and failed is named as not met.
- Every changed wording is checked in full (any reason, also in `Report`); the recheck no longer replaces a full check. A
  model that hung is not called again in the same run. A delivered text no check saw says so ("The facts of this version of
  the text were not checked …", MCP `version_not_checked`); earlier findings appear only when their passage still stands in
  the text.
- The content guard never drops a finding: an unsafe reason is replaced by a fixed sentence, the quote of the text stays;
  unsafe material passages and unsafe notes of the writer are replaced or counted visibly ("1 more note … could not be
  shown").
- Recalculation of the files stays silent on deductions, payments made, balances, taxes, amounts per person, quantities and
  totals that only match their last items; the sentence promises only what was recalculated. V3 still passes.
- Large files: the sentence says the facts were not checked, only the instructions.
- Contradictions name where each side stands (request, answers, About me details, a file).

**Third review round (2026-11-25):**
- The recalculation never claims that calculations or files are right. It reports only clear mismatches, asking the person to
  check them, and otherwise says at most that it looked for clearly wrong calculations or totals and found none, adding that
  not every calculation can be checked this way. The distinction "all checked" against "partly checked" is gone; earlier
  reports read the same way.
- In a later clause after a total only words of shares (pro, je, jeweils, per, each) are harmless; words of deductions,
  discounts, subsidies, down payments and taxes make the total unclear there too, and it is not mentioned.
- An earlier finding is dropped only when a kept passage of the same kind contains it wholly; contradictions count as the same
  only at word boundaries and with the same files. A contradiction about a wrong total is dropped only when its passages
  differ in nothing but figures and total words. When an earlier version is delivered, findings of later rounds are added.
- When an account is deleted or renamed, the stored start input in the events (`PipelineStartedEvent.Input`) loses the About
  me block too, as does every other event text with the block or the details; runs cleaned before are cleaned again.

**Conditions before `On`** (in addition to gate Q4):
- Cause of the hangs found, **or** at most 1 hang in at least 60 real calls.
- The fallback model measured against the same quality criteria (it checks about one order in ten).
- Development set relabelled and false alarms counted again (0.38 per clean text there, above 0.25; in `On` false alarms
  change texts).
- Total waiting time including the correction round measured.

## D-074 — Stage 4: research (2026-11-25)

**Status:** implemented (core, service with Tavily and persistence, direct path, result on the web and over MCP, measuring tools
VT/VR and R3). Pre-tests VT and VR **passed**. Shipped with `Research:Mode = Off` (code and `appsettings.json`); the measuring
user gets `On` only through the environment (`Research__ModeOverrides__<user>=On`, no user name in the repository; test
`ShippedResearchSettingsTests`). `On` for everybody only after gate R3, in a commit of its own.

**Decision.** What the result shows as "looked up" is a passage that the code found word for word in the fetched page text,
with page and date – nothing more. Better to look up less than to show something unchecked.

- **When:** only the first run of a clarified order on the direct path (version 1, no text to revise, clarification `Ok` with
  research topics, switch `On` globally or for the person who ordered), never a revision, never "Extra careful", never
  `submit_request` without clarification. **Never with an attached file** (review round 1, structural instead of a word filter):
  when the order has any file – whatever its role, a text to revise included – nothing goes to Tavily or to the passage picker.
  The status `FilesAttached` (`files_attached`) is stored once with the sentence "No web search was done because you attached
  files, so nothing from them can reach a search engine." on the web and over MCP; every later run shows it and never uses stored
  entries for such an order. Researchable gaps stay as without research ("not given", not assumed); no text claims anything was
  looked up. Without research topics there is no status. The research is stored at the order (`Orders.ResearchJson`); every later run uses it
  unchanged and never searches again. Every research that ran is stored, a failed one included (conditional write; whoever
  loses reads the stored one).
- **Origin rule for topics** (`ResearchQueryGate`, no word list): Tavily gets at most four topics of the clarifier, never the
  request, answers or profile (there are never files at this point, see above). The rule checks against the **values** of the
  profile in the briefing, without the fixed field names and without the formality words (`ProfilePrompt.Values`). A topic with a
  word from the profile that is not exactly so in the person's request is withheld; so are variants (accents, ß/ss, spelled-out
  umlauts), shared word starts of five characters or more, camel-case and letter/digit parts (`ArztbriefSchmidt`), the genitive
  in -s (`Wolfs`), words with invisible characters (soft hyphen, zero width – removed as in `QuoteVerifier`) and numbers of four
  digits or more or with a dot or comma between digits, across spellings and without closing zeros (`1.250,00` = `1250`); and
  links, addresses, access or account data (`IntakeContentGuard`). **Language rule:** when the profile has free text or "Never do
  this" and the language of a topic is certainly another than the order's (function words or ä/ö/ü/ß of one language only), the
  topic is withheld. The rule only errs on the safe side; a withheld topic is assumed as before and reported visibly. **Known
  limits, on purpose:** translations without a language signal (topics of bare terms such as "breast cancer sick leave"),
  synonyms, the stem of a compound ("Insolvenz" from "Privatinsolvenz"), two-letter names, names written apart or together
  without capitals, homoglyphs; if the person changes the profile between clarification and start, the profile of the briefing
  counts.
- **Tavily:** exactly one search per topic (`basic`, three results, raw content on, Tavily answer off), up to four in parallel,
  20 s, **never repeated**. Only the raw page text is used, never Tavily's snippet or answer. A longer raw text is cut to 6,000
  characters and then **back to the last safe sentence or line end** (a dot after a digit does not count; without a sentence
  end at the last space not between digits; otherwise the page is left out). Results with user information in the address or an
  address over 500 characters are dropped. The Atelier server fetches none of the addresses itself. **Daily limit:** every search
  is reserved atomically before it starts (advisory lock, `Orders.ResearchAt` = time of the reservation): at most 50 orders per
  UTC day in total and 20 per account (`DailyOrderCapPerUser`); cancelled searches count, a repeat of the same order counts once.
  `FilesAttached`, "withheld" and "daily limit" reserve nothing. Remaining: deleting an order lowers the count (each attempt costs
  a whole order).
- **Picking passages:** one call in slot `GroundingRefiner` (Sonnet today), schema `pick_passages` without length limits, proxy
  failover off, 60 s, no second try. It sees **only** the topics sent and the pages (tags defused), nothing of the order, and
  picks passages; there is no statement of the model's own.
- **Dossier = verbatim passages** (`DossierBuilder`): every proposal is searched in the raw text (identical, or normalised only
  in white space and quotes, never approximate); stored is the page's original passage with its position and the SHA-256 of
  the raw text. An entry never starts or ends inside a word or a number (`1,5`, `1.250`). Dropped and counted: not on the page,
  too short (fewer than five words), unsafe (links, addresses, access or account data), duplicate (a later, longer passage
  replaces an earlier one it holds), over the limits (three per page, four per topic, twelve in total), unknown page, over 600
  characters (never cut). An unsafe page title is replaced by the host (also when old entries are shown or rendered); in the
  material of writer and fact check the address stands without query and fragment.
- **In the run:** the passages go as source `W1` ("Looked up on the web") after the files to writer and fact check, never to the
  recalculation of totals. The writer's system prompt and the fact check's prompts, schemas and version stay unchanged (hash
  tests; measurement V2 stays valid); the writer's user prompt gets one fixed sentence only when `W1` is there and never counts
  `W1` as an attached file (without files it gets an introduction of its own). An instruction from `W1` never blocks. A
  `superseded` finding whose material quote comes from `W1` never blocks, is never called "your files" and never goes into the
  correction as "the files say"; it is shown as a hint with the sentence "The pages looked up say otherwise – please check which
  is right." (web: `data-kind="differs-from-looked-up"`, MCP: `kind = differs_from_looked_up` with `note`), also from earlier
  reports; prompt and schema of the fact check stay unchanged. Research that cannot be stored is not used: the text is written
  without `W1` (status `Failed`, code `not_stored`, warning). A topic that is only the name of a kind of gap ("Facts") is never
  searched. The report lists `W1` with `lookedUp` (schema stays 2).
- **What the person sees:** section "Looked up" with passage, page (a link only for `http`/`https`, `nofollow noopener
  noreferrer`) and date under the fixed subline "… not that it is true or that the text uses it"; every non-success (nothing
  found, failure, daily limit, withheld, files attached) as a fixed sentence; the fact line names "the pages looked up" only when
  they were in the material; origin in a contradiction is "the pages looked up", never "your file", and the heading of the
  problems in the material then drops "in what you gave". Web and MCP (`get_order_result.lookedUp`, marked as content of other
  websites) say the same; the failure sentence appears only once. The result text (`MarkdownView`) shows links only for
  `http`, `https` and `mailto` and loads no images (only their alternative text stays).
- **Never stops the run:** every failure ends as a status with the warning `grounding.research_failed`; the text is written
  without `W1`. The grounding step waits at most 120 s, also when the service does not stop on its own. One log line per research names counts, status, duration and model,
  never topics, passages or addresses. Raw texts are not stored; only for the acceptance `Research:AuditDirectory` (empty by
  default) writes raw text and entries for users with an override (mode 0600); the directory is deleted afterwards.
- **Migration Step51** (nullable, additive, idempotent): `Orders.ResearchJson`, `Orders.ResearchAt`. No index, no data
  migration, no `Sources` row for the dossier.

**Deviations from the overall plan:** a dossier entry is the verbatim page passage, not a model "statement" (not checkable); no
second way through the CLI's web search (no raw text); no fetching of addresses; research only in version 1; `On` for everybody
only after the paired measurement R3.

**Switches:** `Research:Mode` (`Off|On`, shipped `Off`), `Research:ModeOverrides` (user → mode, environment only),
`Research:DailyOrderCap` (50), `Research:DailyOrderCapPerUser` (20), `Research:AuditDirectory` (empty). Limits: `MaxSearches` 4, `ResultsPerSearch` 3,
`RawCharsPerPage` 6,000, `SearchTimeout` 20 s, `CondenseTimeout` 60 s, `MaxEntries` 12. Whoever changes prompt or schema of the
passage picker measures VR again (`tools/ResearchEval`); the fact check prompt stays unchanged (test).

**Pre-tests (2026-11-25, synthetic corpus, in the afternoon between 15:06 and 15:09 UTC, 0 Codex calls):**
- **VT (Tavily probe): passed.** 3 of 3 requests with 2xx, 6 of 9 results with raw text, median 1.5 s. The answer carries no
  usage figure (only query, response time, request id): **Tavily credit use unknown**; 0.001 USD per `basic` search according to
  the configuration.
- **VR (dossier, held-out set of 9 orders): passed.**
  - K1 verbatim: **0 violations** over all 95 stored entries (development and held-out), independent Python check
    (`research_verify.py`, case-sensitive) and own recheck.
  - Validity 8 of 8 picker answers (required ≥ 95 %); yield 7 of 9 orders with at least one entry (77.8 %, required ≥ 70 %);
    relevance 34 of 35 entries help with their topic (97.1 %, required ≥ 80 %, blind Opus juror); median 5.0 s per order for
    search and picking (required ≤ 60 s).
  - Reported: 17 topics sent, 1 withheld, 17 searches without failure, 42 pages with raw text, 41 proposals, 35 entries;
    dropped 1 over the limit, 5 unsafe (12.2 %). Development set (no verdict): 12 of 12 with entries, 28 searches, 60 entries.
  - Limits: 9 orders are a plausibility gate, K1 is hard. The corpus has hardly any files (1 of 36 orders with topics, not in
    the held-out set); the origin rule acted once there, its effect is shown by tests (property test, end-to-end
    counter-checks). The language rule and "no search with a file" are not part of the measuring tool; both can only send less.
  - **Effect of review round 1 on VR (read only, no new measurement):** 92 of the 120 stored pages were cut at 6,000 characters;
    the new cut would take 1 to 470 characters off their end. **None of the 95 entries lies in that rest**, no VR address has
    user information or over 500 characters. `ResearchEval evaluate` with the new code on the same files (origin rule and
    `DossierBuilder` recomputed, the new check with word boundaries finds 0 violations): **still passed**, all figures equal. The
    passage picker saw the slightly longer page end in VR, so its input is not bit-identical with production; the result stays
    meaningful because no passage came from the removed part.

**Gates after the deploy (fixed beforehand, none passes without data):**
- **R2 (acceptance measurement):** 5 orders of the measuring user **without files** (3 web, 2 MCP), fact check `Report`: (a) each
  with research status ≠ `NotNeeded` and ≠ `FilesAttached`, (b) 0 questions about researched topics, (c) at most 1
  `unsupported` finding against a spot covered by `W1`, (d) median ≤ 60 s, (e) 0 note markers in the text. (a), (c) and (e) are
  checked by `tools/Eval/geef_eval/research_r2.py` on the stored `get_order_result` answers (c: the quote holds a number or a
  capitalised name found in an entry – errs on the strict side, in German every noun counts); (b) and (d) are **evaluated by
  hand**. Not evaluable below 4 orders with text. Failing (c) → research stays `Off` for everybody.
- **R3 (effect, `tools/Eval/geef_eval/research_pairs.py`):** 8 untouched development orders without files, two measuring users
  (with/without research, both without fact check), blind Sonnet juror, per order two kinds of calls in two swapped passes each:
  `rank` **without** passages decides wins and placeholders; `facts` with the numbered passages of arm on lists invented facts
  and names for each the passage that covers it word for word. Gate: (a) wins with ≥ wins without + 2, (b) sum of `erfunden`
  with ≤ without, (c) placeholders for researchable facts with < without (per order the mean of both passes). **Validity (fixed
  beforehand):** at least 7 of 8 pairs technically complete and at least **6 of 8 valid** – only pairs whose arm on has `found`
  with at least one entry; `failed`, `daily_limit`, `all_withheld`, `nothing_found`, `files_attached` or no research make a pair
  invalid; otherwise `not_evaluable`. The status per pair is in the output. **Invented facts are counted twice:** strictly (every
  fact) and with the verbatim exception (without those a passage covers word for word). Every fact that is left out only by the
  exception is listed and checked by hand against the intent before any `On`; while such a case lacks a valid decision in the
  confirmation file (`--confirmations`, per case `not_invented` or `invented`), the strict count applies and the gate is
  `not_evaluable`. Cap 32 juror verdicts (16 `rank` + 16 `facts`). After a configuration halt `run` submits nothing more.
  Passed → `On` for everybody; otherwise it stays `Off` and the measuring user keeps `On`.

**Counter-checks at integration:** over the real wiring neither a profile name nor an answer of the person reaches Tavily or
the passage picker; with an attached file no request goes out in any run (not even a translated file word or the file name);
address and title of a page stay content in the prompt. This removes the file parts of security findings M1/M2 of review
round 1 and the file-name finding of the integration. Remaining, on purpose: words of the person's request may go into a topic
(the person wrote them); the profile of the briefing counts; a page's address (without query and fragment) is in the writer's
prompt and might appear in the text as a source.

**Cost:** per researched order one Sonnet call and at most four Tavily searches (22.5 % of the corpus orders had research
topics); daily limit ≤ 200 searches, ≤ 80 per account.

**Way back:** `Research__Mode=Off` without a new image; stored research stays and is shown only for runs that used it. Code:
previous web image; Step51 is nullable, the code of stage 3 ignores the columns; no `Down()` in production. The template
`direct` does not change; running runs may stay.
