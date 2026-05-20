# MCP integration

*[Deutsch](04-mcp-integration_de.md) · **English***

*Last updated: 2026-05-20 (D-051: neue providerType-Werte static-context, url-fetch, news-search in list_grounding_provider_profiles + materialize_template_proposal dokumentiert)*

## Why MCP

Geef.Atelier should not only be usable via the web UI but also consumable by AI agents. The typical use case: a Claude (or another MCP-capable client) works on a more complex task and needs a particularly carefully produced text for one sub-step. Instead of generating that text inline — where the calling Claude has neither multiple iterations nor a reviewer crew available — it delegates the job to Geef.Atelier, fetches the result later and continues working.

MCP (Model Context Protocol) is the standard that makes such delegations run cleanly: tools with JSON-schema-defined inputs/outputs, uniform auth, uniform transport.

## Architectural consequence

The service has **two frontends**: the web UI and the MCP server. Both use the same application service layer (`IRunService`). The pipeline logic, the persistence, the event sink — all of it is frontend-agnostic.

```
Web UI ──┐
         ├──> IRunService ──> background orchestrator ──> Geef pipeline
MCP   ───┘
```

It follows that everything the user can do in the UI should also be doable via MCP — except purely UI-specific things (the live-stream display).

## Tools the MCP server offers

### `submit_request`

Queues a new job.

**Input:**
```json
{
  "briefing": "string (required) — Beschreibung der Aufgabe und des gewünschten Ergebnisses",
  "options": {
    "executor_model": "string (optional) — z.B. 'claude-opus-4-7'",
    "reviewer_models": ["array (optional) — Liste von Modellen für die Reviewer"],
    "max_iterations": "int (optional, default 3)"
  }
}
```

**Output:**
```json
{
  "run_id": "uuid",
  "status": "Pending"
}
```

### `get_run_status`

Returns the current status of a run.

**Input:** `{ "run_id": "uuid" }`

**Output:**
```json
{
  "run_id": "uuid",
  "status": "Pending | Running | Completed | Failed | Aborted",
  "current_phase": "Grounding | Execution | Evaluation | Finalize | null",
  "current_iteration": 2,
  "tokens_used": 12345,
  "cost_total": 0.234,
  "started_at": "2026-05-10T12:00:00Z",
  "completed_at": null
}
```

### `get_run_result`

Returns the finished result. Only when status=Completed.

**Input:** `{ "run_id": "uuid" }`

**Output (when Completed):**
```json
{
  "run_id": "uuid",
  "final_text": "string",
  "tokens_used": 12345,
  "cost_total": 0.234,
  "iteration_count": 2
}
```

**Output (for other statuses):** an error with the current status so the client knows whether to wait or not.

### `list_runs`

Lists existing runs.

**Input:**
```json
{
  "limit": "int (optional, default 20)",
  "status_filter": "string (optional)"
}
```

**Output:** array of run summaries (Id, Status, CreatedAt, BriefingPreview).

### `get_run_details`

Returns the complete trail of a run.

**Input:** `{ "run_id": "uuid" }`

**Output:** run data plus all iterations (with artifact text), all findings, optionally the last N events.

### `cancel_run`

Cancels a running run. Returns `true` if the cancellation was triggered; `false` if the run was already terminal (Completed, Failed, Aborted) or does not exist.

**Input:** `{ "run_id": "uuid" }`

**Output:** `bool` (`true` | `false`)

### Further tools (crew, knowledge base, Template Studio)

Besides the six run tools the server offers nine more — **15 MCP tools** in total:

| Tool | Purpose |
|---|---|
| `list_crew_templates` | List crew templates (system + custom) |
| `list_reviewer_profiles` | List reviewer profiles (system + custom) |
| `list_advisor_profiles` | List advisor profiles (system + custom) |
| `list_grounding_provider_profiles` | List grounding-provider profiles (incl. `refinementEnabled` / `refinementMode`) |
| `list_knowledge_documents` | List global knowledge-base documents |
| `analyze_template_proposal` | Analyze a task description, produce a template proposal (persisted) |
| `materialize_template_proposal` | Materialize a reviewed proposal as a custom template + profiles |
| `list_run_artifacts` | List all RunArtifacts produced by finalizers for a completed run |
| `download_run_artifact` | Download the binary content of a File artifact as Base64 |

Full parameter/schema details: [`09-endpoint-reference.md`](09-endpoint-reference.md).

#### `list_grounding_provider_profiles`

**Input:** `{ "includeSystem": bool (default true) }`

**Output:** Array of grounding-provider profile objects. Each profile includes:

| Field | Type | Description |
|---|---|---|
| `name` | string | Profile identifier |
| `displayName` | string | Human-readable name |
| `description` | string | Purpose description |
| `providerType` | string | `"tavily"`, `"vector-store"`, `"static-context"`, `"url-fetch"`, or `"news-search"` |
| `maxQueriesPerRun` | int? | Maximum queries this provider may issue per run |
| `isSystem` | boolean | Whether this is a built-in system profile |
| `refinementEnabled` | boolean | Whether KI-Refinement is configured for this provider |
| `refinementMode` | string \| null | `"filter"` or `"synthesize"` (null when `refinementEnabled` is false) |

#### `analyze_template_proposal`

Runs a meta-LLM that analyzes the task description and produces a structured `TemplateStudioAnalysis` persisted in the DB.

**Input:** `{ "task_description": "string" }`

**Output:** `TemplateStudioAnalysis` — includes `id` (UUID, needed for `materialize_template_proposal`), a `proposed_template` (DisplayName, Description, EvaluationStrategy, optional `evaluation_strategy_reasoning`, optional `finalizer_profile_names` array, optional `finalizer_reasoning`), and two lists:

- `proposed_new_profiles` — crew/advisor/grounding/executor profiles. Each carries: `profile_type` (`"reviewer"` | `"advisor"` | `"grounding_provider"` | `"executor"`), Name, DisplayName, Description, Provider, Model, MaxTokens, SystemPrompt, plus type-specific optional fields (ReviewerFocus, AdvisorMode, AdvisorTrigger, GroundingProviderType, GroundingProviderSettings) and optional LLM reasoning fields (`model_reasoning`, `system_prompt_reasoning`, `overall_reasoning`, `mode_reasoning`, `trigger_reasoning`).
- `proposed_new_finalizer_profiles` — finalizer profile proposals. Each carries: `name`, `display_name`, `description`, `finalizer_type` (`"FileExport"` | `"MetadataEnrich"` | `"ExternalSink"` | `"Transform"`), `settings` (object), `finalizer_type_reasoning` (optional string).

Missing fields are filled from `appsettings TemplateStudio:Defaults` server-side.

Backwards-compatible: old inputs without finalizer fields continue to work, defaulting to no finalizers.

#### `materialize_template_proposal`

Atomically writes all new profiles and the crew template to the DB in a single transaction.

**Input:**
```json
{
  "analysis_id": "uuid",
  "final_template": {
    "display_name": "...", "description": "...", "evaluation_strategy": "Sequential",
    "executor_profile_name": "...", "reviewer_profile_names": ["..."],
    "advisor_profile_names": ["..."], "grounding_provider_profile_names": ["..."],
    "finalizer_profile_names": ["..."],
    "finalizer_reasoning": "optional string"
  },
  "final_new_profiles": [
    {
      "profile_type": "reviewer", "name": "custom-my-reviewer",
      "display_name": "...", "system_prompt": "...", "provider": "openrouter",
      "model": "openai/gpt-4o-mini", "max_tokens": 16384
    }
  ],
  "final_new_finalizer_profiles": [
    {
      "name": "custom-my-exporter", "display_name": "...", "description": "...",
      "finalizer_type": "FileExport", "settings": {}
    },
    {
      "name": "custom-my-transform", "display_name": "Mein Transform", "description": "...",
      "finalizer_type": "Transform",
      "settings": {
        "Provider": "openrouter",
        "Model": "openai/gpt-4o-mini",
        "MaxTokens": "8192",
        "SystemPrompt": "...",
        "Temperature": "0.5"
      }
    }
  ]
}
```

`final_new_profiles` contains only profiles the user chose to create fresh (CreateNew mode). Profiles in UseExisting mode appear only by name in `final_template.*_profile_names`. `final_new_finalizer_profiles` follows the same pattern for finalizer profiles. A `max_tokens` below the hard floor (`StudioDefaults.MinMaxTokens = 10000`) is clamped up server-side; omitting it applies the `TemplateStudio:Defaults` value (Reviewer/Advisor 16384, Executor 60000). Omitting `finalizer_profile_names` or `final_new_finalizer_profiles` is backwards-compatible (no finalizers attached).

**Grounding-Provider-Typen:** `providerType` (bzw. `GroundingProviderType` im Proposal) kann folgende Werte haben: `"tavily"`, `"vector-store"`, `"static-context"`, `"url-fetch"`, `"news-search"`. Jeder Typ erwartet typ-spezifische Settings-Keys (siehe `08-crew-system.md` Provider-Typen-Tabelle).

**Grounding-Provider-Refinement keys in `groundingProviderSettings`:** The `GroundingProviderSettings` dict for a `grounding_provider` profile accepts all KI-Refinement keys (`refinementProvider`, `refinementModel`, `refinementMaxTokens`, `refinementTemperature`, `refinementMode`, `refinementInstructions`) as flat string entries. All keys are optional and backwards-compatible — profiles without these keys behave exactly as before (no refinement pass).

**Output:** `{ "created_template_name": "custom-..." }` — the name of the materialized template, ready to pass to `submit_request` as `crew_template`.

#### `list_run_artifacts`

Lists all RunArtifacts produced by finalizers for a completed run.

**Input:** `{ "run_id": "uuid (required)" }`

**Auth:** owner isolation — non-admins can only see artifacts of their own runs.

**Output:** array of artifact objects:
```json
[
  {
    "artifact_id": "uuid",
    "finalizer_profile_name": "string",
    "artifact_type": "File | Url | Status",
    "filename": "string or null",
    "content_type": "string or null",
    "size_bytes": 12345,
    "storage_uri": "string — file path (File), URL (Url), or 'error'/'info' (Status)",
    "status_message": "string or null",
    "created_at": "2026-05-19T10:00:00Z"
  }
]
```

#### `download_run_artifact`

Downloads the binary content of a File artifact as Base64. Only works for `artifact_type: "File"` — returns an error for Url and Status types. Useful for AI agents that need to retrieve generated PDFs, DOCX, HTML, etc. programmatically.

**Input:** `{ "run_id": "uuid (required)", "artifact_id": "uuid (required)" }`

**Auth:** owner isolation.

**Output:**
```json
{
  "artifact_id": "uuid",
  "filename": "string",
  "content_type": "string",
  "size_bytes": 12345,
  "content_base64": "string"
}
```

### Run visibility over MCP (D-042)

Since run-user isolation, runs are visible per user separately — including over MCP:
runs submitted/queried via OAuth belong to the authorizing user; requests with the
static `ATELIER_MCP_TOKEN` (Claude Code CLI) are attributed to the admin.
`list_runs`/`get_run_*` return only the respective user's runs (no
run-existence leak); if the caller is admin, they can see system-wide with the
`list_runs` parameter `includeAllUsers=true` (no effect for non-admins).

## SDK

**`ModelContextProtocol.AspNetCore` v1.3.0** — the official Anthropic+Microsoft C# SDK ([modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)). The tools are defined as `[McpServerTool]`-annotated methods in `Geef.Atelier.Mcp` (class library) and registered in the web host via `AddMcpServer().WithToolsFromAssembly()`.

## Transport

**Streamable HTTP** (stateless, `Stateless=true`) is the actively used transport. Advantage over the older SSE transport: bidirectional over a single connection, easier to route through reverse proxies, simpler auth handling.

Endpoint: `POST https://atelier.example.com/mcp` (the path `/mcp` is fixed in the web host's `MapMcp()` call).

## Auth

Two parallel auth paths — both active, no config switch needed:

### Path A: Static bearer token (Claude Code CLI)

**Bearer token** in the `Authorization` header. Token from the environment variable `ATELIER_MCP_TOKEN`. No rotation, no refresh. Sufficient for single-user CLI operation.

```
Authorization: Bearer <ATELIER_MCP_TOKEN>
```

### Path B: OAuth 2.1 (Claude Desktop / Claude.ai custom connectors)

Self-hosted OAuth 2.1 authorization server, implemented directly in Geef.Atelier. Supports the full authorization-code flow with mandatory PKCE/S256.

**Relevant specifications:** RFC 8414 (metadata), RFC 7591 (dynamic client registration), RFC 7636 (PKCE), RFC 7009 (revocation), RFC 8252 (loopback).

**Endpoints:**

| Endpoint | Method | Purpose |
|----------|---------|-------|
| `/.well-known/oauth-authorization-server` | GET | RFC 8414 server metadata |
| `/.well-known/oauth-protected-resource` | GET | MCP resource metadata |
| `/oauth/register` | POST | RFC 7591 dynamic client registration |
| `/oauth/authorize` | GET | Consent page (Blazor, `[Authorize]` cookie — redirects to `/login` if there is no session) |
| `/oauth/consent` | POST | Approve/deny submit of the consent page → redirect to the `redirect_uri` |
| `/oauth/token` | POST | Token endpoint (authorization_code + refresh_token) |
| `/oauth/revoke` | POST | RFC 7009 token revocation |
| `/account/connected-clients` | GET | Self-service for connected clients (user UI) |
| `/admin/oauth-clients` | GET | OAuth client management (admin only) |

**Flow:**

```
1. Client → GET /.well-known/oauth-authorization-server  (discovery)
2. Client → POST /oauth/register                         (dynamic client registration)
3. Client → GET /oauth/authorize?...&code_challenge=...  (→ browser login + consent)
4. User approves → browser redirect back with ?code=...
5. Client → POST /oauth/token (code + code_verifier)     (token exchange)
6. Client → MCP request with Bearer <access_token>
7. Client → POST /oauth/token (refresh_token)            (refresh rotation, optional)
8. Client → POST /oauth/revoke                           (revocation, optional)
```

**Token design:** opaque tokens (32-byte random string, Base64Url). Only the SHA-256 hash in the DB. Access token: 1 hour. Refresh token: 30 days, rotated on every refresh.

**Security:**
- All secret comparisons via `CryptographicOperations.FixedTimeEquals`
- Token generation exclusively `RandomNumberGenerator.GetBytes(32)`
- PKCE S256 enforced — `plain` rejected
- Refresh-reuse detection: a consumed refresh token → immediate revocation of all the user's tokens

### Compatibility

`CompositeTokenValidator` checks both paths — the static token first. Claude Code CLI requests with `ATELIER_MCP_TOKEN` never reach the OAuth path. Both paths coexist without a configuration change.

## Relationship to the web UI

Both frontends call the same `IRunService`. Consequences:

- A run started via MCP appears immediately in the web UI (same DB).
- A run started via the UI can be queried via MCP.
- Status updates of a run started via MCP are visible live in the UI (SignalR stream).
- Cancellation works from both sides.

This is a deliberate design goal: a job is a job, regardless of the entry path.

## Hosting

In the skeleton the MCP server runs **as part of the same ASP.NET application** as the web UI — the same container, the same process, its own path prefix (`/mcp`). This saves deployment effort. Should the need arise later (e.g. different scaling requirements), the MCP server can be split into its own container without changing any domain logic.

## Discovery and configuration

### Claude Code CLI (static token)

```json
{
  "mcpServers": {
    "geef-atelier": {
      "url": "https://geef.stefan-bechtel.de/mcp",
      "transport": "streamable-http",
      "auth": {
        "type": "bearer",
        "token": "<ATELIER_MCP_TOKEN>"
      }
    }
  }
}
```

### Claude Desktop / Claude.ai custom connector (OAuth)

Enter the URL `https://geef.stefan-bechtel.de/mcp` — the client detects `WWW-Authenticate: Bearer resource_metadata=".../.well-known/oauth-protected-resource"` and starts the OAuth flow automatically (dynamic client registration → browser login → consent → token exchange).

## Not in scope

- Rate limiting (single-user, no need)
- Multiple scopes / fine-grained permissions (only `mcp:full`)
- JWTs / OpenID Connect (opaque tokens + DB lookup is sufficient)
- Multi-tenant
