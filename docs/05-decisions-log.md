# Decisions Log

*[Deutsch](05-decisions-log_de.md) · **English***

*Last updated: 2026-05-20 (D-051 added: Grounding-Typen erweitern — Static-Context, URL-Fetch, News-Search)*

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

**D-043/4 — Field-Help resource: central, German, spec-verbatim.** `StudioFieldHelps.cs` (static constants) holds all Field-Help texts in German. `FieldHelp.razor` renders inline hints below each field. Every Studio edit field (template and all profile types) has a FieldHelp. No inline help strings inside components.

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
