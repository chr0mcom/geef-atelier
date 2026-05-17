# Architecture

*[Deutsch](02-architecture_de.md) · **English***

*Last updated: 2026-05-17 (data model, LLM/auth layer and MCP auth brought up to date: crew-profile system, OAuth 2.1, multi-user, run-user isolation)*

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
│   │                                // event sink, provider implementations, repositories
│   ├── Geef.Atelier.Web/            // Blazor Server: UI + BackgroundService
│   │                                // (RunOrchestratorService), DI composition
│   └── Geef.Atelier.Mcp/            // Class library: MCP tool definitions,
   │                                // hosted in the Web project (shared DI, shared container)
└── tests/
    └── Geef.Atelier.Tests/          // xUnit
```

**Rationale for the split:**

- **Core** is LLM-free and persistence-free — it contains only records, interfaces, domain logic. Thus testable without infrastructure.
- **Infrastructure** encapsulates all external dependencies (Postgres, LLM APIs, Geef SDK). Provider implementations live here because they need LLM clients and repositories.
- **Web** hosts the UI, the `BackgroundService` and the `IRunService` implementation. The latter could later move into its own project but is most practical here in the skeleton.
- **Mcp** is a **class library** (no own host). It contains all MCP tool definitions. The MCP endpoint lives in the `Web` project (path `/mcp`), which references `Geef.Atelier.Mcp` and registers the tools in the same DI container. Advantages: no second host process, `IRunService` and all singletons (SignalR, DbContext) are shared directly, no HTTP hop between MCP and the application layer.

## Data model

As of May 2026 the schema comprises **21 tables**, with hand-written migrations
`InitialCreate` + `Step06`/`Step09`–`Step21`. Grouped:

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

The four run-core tables are documented in detail below; the other groups are
described in their respective feature sections or in the [decisions log](05-decisions-log.md)
(D-028 ff.). `Runs` additionally carries columns from later migrations
(`CreatedByUser`, `CostTotal`, `CrewTemplateName`, `CrewSnapshot`, `AdvisorRetryAttempted`).

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
| Finalize | `MarkdownFinalizer` | Wraps the final text in a `FinalizedDocument` record |
| Convergence failure | `TryConvergenceFailureRetryAsync` | Enables OnConvergenceFailure advisors, single retry (AdvisorRetryAttempted cap) |

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

## UI architecture (step 7)

### Pages

Three Blazor Server pages in `src/Geef.Atelier.Web/Components/Pages/`:

| Route | Component | Function |
|---|---|---|
| `/new` | `New.razor` | Submit form. `EditForm` + `DataAnnotationsValidator`. Redirect to `/runs/{id}` after submit. |
| `/runs` | `Runs.razor` | Run list. Status filter via query parameter. `HubConnection` on the `all-runs` group. Live update via the `AnyRunUpdated` event. |
| `/runs/{id}` | `RunDetail.razor` | Run detail. `HubConnection` on the `run-{id}` group. Live update via the `RunUpdated` event. Cancel button for Pending/Running. |

### SignalR hub (`RunHub`)

`src/Geef.Atelier.Web/Hubs/RunHub.cs` — mapped to `/hubs/runs`.

Two groups:
- `run-{runId}` — detail-page subscribers. Sends `"RunUpdated"` after every persist event.
- `all-runs` — runs-list-page subscribers. Sends `"AnyRunUpdated"` after every persist event.

Browser clients use `HubConnectionBuilder.WithUrl("/hubs/runs").WithAutomaticReconnect()`. The reconnect handler re-joins the group. Pages implement `IAsyncDisposable` with a `Leave` call + hub dispose.

### `IRunNotifier` / `SignalRRunNotifier`

`IRunNotifier` lives in Core (`Core/Notifications/`). `PostgresEventSink` (Infrastructure) consumes the contract — without a Web dependency. `SignalRRunNotifier` lives in Web, injects `IHubContext<RunHub>`, singleton lifetime. Notifier calls are best-effort (`try/catch`).

**Sequence user-submit → live view:**

```
Browser /new  →  IRunService.SubmitRunAsync  →  RunEntity (Pending) in DB
                                                ↓
                                      RunOrchestratorService (BackgroundService)
                                        polls Pending, sets the Running claim
                                                ↓
                                      Geef SDK pipeline (Grounding → Execution → Evaluation → Finalize)
                                                ↓
                                      PostgresEventSink
                                        (a) writes the event to the DB
                                        (b) IRunNotifier.NotifyRunUpdatedAsync
                                                ↓
                                      SignalRRunNotifier → IHubContext<RunHub>
                                        → run-{id} group: "RunUpdated"
                                        → all-runs group: "AnyRunUpdated"
                                                ↓
                                      Browser HubConnection.On("RunUpdated")
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

**RunHub without `[Authorize]` (an architectural trade-off):**

`RunHub` has no `[Authorize]` attribute. Rationale: Blazor Server's `HubConnectionBuilder` creates server-side SignalR connections that do not forward browser cookies. With `[Authorize]` on the hub, the SSR pre-render phase would receive 401 and Blazor circuit initialization would fail. Mitigation: all subscribing pages (`/new`, `/runs`, `/runs/{id}`) carry `@attribute [Authorize]` — unauthenticated users cannot load the pages, hence cannot open a hub connection either.

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

## Observability

Geef brings a lot built in:
- `IGeefEventSink` for structured events → the custom sink writes to the DB and SignalR
- `System.Diagnostics.ActivitySource("Geef.Sdk")` for distributed tracing → can later be connected to an OpenTelemetry collector
- A middleware pipeline for cross-cutting concerns (timeout, exception handling, tracing) — all built-in middleware is used in the skeleton

Logging via `Microsoft.Extensions.Logging` with a console sink in the skeleton; structured logs (Serilog with a Postgres sink) are a later option.
