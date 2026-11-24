# Endpoint reference

*[Deutsch](09-endpoint-reference_de.md) · **English***

*Last updated: 2026-11-24 (stage 1: order tools `submit_order`, `get_order_status`, `get_order_result`, `submit_request` as a shell with `orderId`, server instructions, `list_learnings` admin only; D-067: `warnings` and `outcome` in `get_run_status`/`get_run_result`, reading the result after `outcome`; `/hubs/runs` stays until stage 8; `/hubs/runs` only for signed-in users; admin-only editors and `/access-denied`; learning paused by default; before that 2026-05-22, D-054: `list_learnings` tool added; `providerType` updated to include `learning-retrieval`)*

All externally reachable HTTP endpoints of Geef.Atelier — MCP, OAuth 2.1
and the web-UI/account endpoints. Base URL: `https://geef.stefan-bechtel.de`.

---

## MCP endpoint

| Endpoint | Method | Auth |
|----------|---------|------|
| `/mcp` | POST | Bearer token (static or OAuth) |

The actual MCP endpoint. Clients send their JSON-RPC requests (tool calls) here. Supports the Streamable-HTTP transport (`Stateless=true`).

**Auth options:**

- **Claude Code CLI:** `Authorization: Bearer <ATELIER_MCP_TOKEN>` (static token from `.env`)
- **Claude Desktop / Claude.ai:** OAuth 2.1 access token (bearer), issued after the OAuth flow described below

If no token or an invalid token is sent, the server responds with `401 Unauthorized` and the header:
```
WWW-Authenticate: Bearer resource_metadata="https://geef.stefan-bechtel.de/.well-known/oauth-protected-resource"
```
Through this, OAuth-capable clients discover the authorization server automatically.

### MCP tools

The MCP server exposes the following tools callable via the `/mcp` endpoint. Run-related tools enforce the same run-user isolation as the web UI (D-042): non-admin users can only access their own runs.

#### Orders: `submit_order`, `get_order_status`, `get_order_result`

Since stage 1 an **order** is the unit the person sees; runs stay the technical log underneath (one order can have several runs). Orders are owner-checked like runs: non-admins only see their own orders, an unknown or foreign order gives the error `Order not found.` Parameter and field names are camelCase.

**`submit_order` input:**
```json
{
  "request": "<string, required: the person's request word for word>",
  "attachments": "<string, optional: JSON array [{\"filename\":\"offer.pdf\",\"contentType\":\"application/pdf\",\"contentBase64\":\"...\"}]>",
  "extraCareful": false
}
```

Supported `contentType` values: `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`, `text/markdown` (also `text/x-markdown`), `text/plain` (parameters such as `; charset=utf-8` are ignored). `extraCareful = true` starts the slower mode with several review rounds; otherwise the direct path is used.

**`submit_order` output:**
```json
{
  "orderId": "<uuid>",
  "state": "queued",
  "runId": "<uuid|null>",
  "message": "<plain English: what to do next>",
  "sources": [ { "fileName": "offer.pdf", "pages": 80, "characters": 160485, "quality": "good|partial|poor", "warnings": ["<string>"] } ]
}
```

`state` can already be `needs_input` (with `runId = null`) once orders can ask questions (stage 2); clients that do not know it should call `get_order_status`.

**Attachment checks.** Before a file is read, the server rejects what is obviously not the file itself: invalid Base64, empty content, text with fewer than 10 visible characters, short texts that only refer to a file (for example "see attached full version"), text that is itself Base64 (encoded twice), and content whose magic bytes do not match `contentType` (a "PDF" without `%PDF-`, a `.docx` without ZIP header, a PDF or ZIP declared as text). A file that the intake cannot use (for example a scan without text) is rejected with its reason. In every case nothing is started, files stored by this call are released again, and the error names the right way: pass the complete original file as Base64 with the matching `contentType`; if the file is too large to pass as an argument, start the order in the web app and add the file there.

**`get_order_status` input:** `{ "orderId": "<uuid>", "waitSeconds": 25 }`

`waitSeconds` is clamped to `0 … Orders:MaxWaitSeconds` (default 50); `0` answers at once. The call waits until the display state changes or the order is finished, at the latest after `waitSeconds`. When the client goes away, the wait ends at once.

**`get_order_status` output:**
```json
{
  "orderId": "<uuid>",
  "state": "preparing|needs_input|queued|working|delivered|delivered_with_open_points|failed|stopped",
  "done": false,
  "step": "Writing",
  "elapsedSeconds": 42,
  "version": 1,
  "runId": "<uuid|null>",
  "warnings": [ { "code": "<string>", "message": "<string>" } ],
  "nextCheckSeconds": 10,
  "outcome": "Converged|DeliveredWithOpenPoints|Failed|Aborted|null"
}
```

`nextCheckSeconds` is `0` when `done` is true, otherwise `10`. `outcome` is the outcome of the current run once it has ended.

**`get_order_result` input:** `{ "orderId": "<uuid>" }`

**`get_order_result` output:**
```json
{
  "orderId": "<uuid>",
  "state": "delivered_with_open_points",
  "text": "<string|null>",
  "wordCount": 180,
  "version": 1,
  "placeholders": ["[delivery date]"],
  "openChecks": ["<plain English: a check the text did not pass>"],
  "sourcesUsed": [ { "fileName": "offer.pdf", "mode": "full_text|full_text_with_map|excluded", "characters": 160485 } ],
  "warnings": [ { "code": "<string>", "message": "<string>" } ],
  "note": "<string|null>"
}
```

`text` is also set for `delivered_with_open_points` (the best text with the open checks) and for a stopped order that already had a text; it is `null` while the order is working or when no text was written (`failed`). `note` says in plain English what to tell the person.

All three answers only ever grow by appended fields; clients ignore fields they do not know.

#### `submit_request` (shell over orders)

The older entry point keeps its answer but now creates an order without questions.

**Input:** `briefingText` (required), `configJson` (optional; only checked for valid JSON, it no longer changes anything), `crewTemplate` (optional), `customCrew` (optional CrewSpec JSON with at least `executorProfileName`), `attachments` (as for `submit_order`).

**Output:**
```json
{ "runId": "<uuid>", "status": "Pending", "errorMessage": null, "orderId": "<uuid>" }
```

Without `crewTemplate` and `customCrew` the order uses `Orders:LegacySubmitDefaultMode` (default: direct path; `Careful` switches back to the classic template). With either of them the order is `Careful` and uses that template or crew. Invalid `customCrew` JSON is an error (before stage 1 it was silently ignored). `get_run_status`/`get_run_result` keep working with `runId`; `orderId` works with the order tools. `submit_auto_crew_request` keeps its behaviour; its runs are attached to orders as well.

#### Server instructions and tool errors

During `initialize` the server sends instructions: pass the person's request word for word, files only as attachments with their original content (never placeholders), orders typically take 1 to 10 minutes, wait with `get_order_status` and `waitSeconds` instead of an own sleep loop, give the text as it is, invent nothing.

A tool that refuses a call answers with `isError: true` and the text `An error occurred invoking '<tool>': <message>`; the message is plain English and says how to fix the call.

#### `list_run_artifacts`

Lists all artifacts produced by a run.

**Input:**
```json
{ "run_id": "<uuid>" }
```

**Output:** Array of artifact objects:
```json
[
  {
    "artifact_id": "<uuid>",
    "finalizer_profile_name": "<string>",
    "artifact_type": "File|Url|Status",
    "filename": "<string|null>",
    "content_type": "<string|null>",
    "size_bytes": 12345,
    "storage_uri": "<string>",
    "status_message": "<string|null>",
    "created_at": "<ISO8601>"
  }
]
```

Returns an empty array if the run has no artifacts. Auth: owner check (same run isolation as other run tools).

---

#### `list_grounding_provider_profiles`

Lists all grounding-provider profiles (system + custom).

**Input:** `{ "includeSystem": bool (default true) }`

**Output:** Array of grounding-provider profile objects:
```json
[
  {
    "name": "tavily-refined",
    "displayName": "Tavily Refined",
    "description": "...",
    "providerType": "tavily",
    "maxQueriesPerRun": 3,
    "isSystem": true,
    "refinementEnabled": true,
    "refinementMode": "filter"
  },
  {
    "name": "tavily-news",
    "displayName": "Tavily News",
    "description": "...",
    "providerType": "news-search",
    "maxQueriesPerRun": 1,
    "isSystem": true,
    "refinementEnabled": true,
    "refinementMode": "filter"
  },
  {
    "name": "custom-my-provider",
    "displayName": "My Provider",
    "description": "...",
    "providerType": "vector-store",
    "maxQueriesPerRun": null,
    "isSystem": false,
    "refinementEnabled": false,
    "refinementMode": null
  }
]
```

| Field | Type | Description |
|---|---|---|
| `name` | string | Profile identifier |
| `displayName` | string | Human-readable name |
| `description` | string | Purpose description |
| `providerType` | string | `"tavily"`, `"vector-store"`, `"static-context"`, `"url-fetch"`, `"news-search"`, `"academic-search"`, `"rest-api"`, or `"learning-retrieval"` |
| `maxQueriesPerRun` | int? | Max queries per run (null = unlimited) |
| `isSystem` | boolean | Built-in system profile |
| `refinementEnabled` | boolean | Whether AI refinement is configured |
| `refinementMode` | string \| null | `"filter"` or `"synthesize"` (null when not enabled) |

---

#### `download_run_artifact`

Downloads the binary content of a `File`-type artifact, returned as Base64.

**Input:**
```json
{ "run_id": "<uuid>", "artifact_id": "<uuid>" }
```

**Output:**
```json
{
  "artifact_id": "<uuid>",
  "filename": "<string>",
  "content_type": "<string>",
  "size_bytes": 12345,
  "content_base64": "<base64-encoded file content>"
}
```

Only works for `ArtifactType.File` artifacts. Reads the file from the `ExportPath` on disk and returns the raw bytes as Base64. Auth: owner check.

---

#### `list_learnings`

**Administrators only** (since stage 1): other users get the error "Only administrators can list learning entries, because they contain material from every user." Lists learning entries of all users from the learning store (D-054). Since D-067 the learning loop is paused by default (`Learning:Enabled = false`); the tool still lists the existing entries, but no new ones are added while the switch is off.

**Input:**
```json
{
  "status_filter": "Proposed|Approved|Rejected",
  "domain_filter": "<string>"
}
```

Both parameters are optional. Omit `status_filter` to return all statuses; omit `domain_filter` to return all domains.

**Output:** Array of learning entry objects:
```json
[
  {
    "id": "<uuid>",
    "text": "<string, truncated to 300 chars>",
    "domain": "<string>",
    "status": "Proposed|Approved|Rejected",
    "source_run_id": "<uuid>",
    "learning_run_id": "<uuid|null>",
    "owner_username": "<string>",
    "created_at": "<ISO8601>",
    "approved_at": "<ISO8601|null>"
  }
]
```

| Field | Type | Description |
|---|---|---|
| `id` | uuid | Learning entry identifier |
| `text` | string | The condensed learning text, truncated to 300 characters |
| `domain` | string | Template name of the source run (used for domain-boost retrieval) |
| `status` | string | `"Proposed"`, `"Approved"`, or `"Rejected"` |
| `source_run_id` | uuid | The standard run that triggered the extraction |
| `learning_run_id` | uuid \| null | The learning-evaluation run that processed this entry (null while Proposed) |
| `owner_username` | string | Inherited from the source run's creating user |
| `created_at` | ISO8601 | Extraction timestamp |
| `approved_at` | ISO8601 \| null | Approval timestamp (null while Proposed or Rejected) |

**Note:** `structured_facts_json` is intentionally not exposed — the raw fact JSON is too verbose for MCP context and is only accessible via the web UI. Auth: administrators only, because the list contains entries of every user.

#### Run status and result: `warnings` and `outcome`

Since D-067, `get_run_status` and `get_run_result` return two additional fields. Both are additive;
older clients can ignore them. Field names are camelCase.

**Output of `get_run_status`:**
```json
{
  "runId": "<uuid>",
  "status": "Pending|Running|Completed|Failed|Aborted",
  "errorMessage": "<string|null>",
  "warnings": [ { "code": "<string>", "message": "<string>" } ],
  "outcome": "Converged|DeliveredWithOpenPoints|Failed|Aborted|null"
}
```

`get_run_result` returns the same fields plus `finalText`.

| Field | Type | Description |
|---|---|---|
| `warnings` | array | Problems the run worked around instead of stopping (skipped sources, failed reviewers, model failover, finalizer problems). Empty when there were none. `code` is stable and machine-readable (e.g. `grounding.provider_skipped`); `message` is a short plain-English sentence whose wording may change. |
| `outcome` | string \| null | What the run delivered once it is finished **and stored, including all finalizers**. `null` while the run is still working, **also when `status` already looks terminal** (the status is set before the finalizers run), and for runs that ended before Step47. |

**Reading the result:** `status` keeps its meaning. A run that stops without convergence still has
`status = "Failed"` but delivers its best draft with `outcome = "DeliveredWithOpenPoints"` and a
`finalText`; the same outcome with `status = "Aborted"` means a critical reviewer finding stopped a run
that already had a text. A client waits until `outcome` is set (not just a terminal `status`) and only then
reads `finalText`; before that the text can still be changed by a finalizer.

`outcome = "Failed"` or `"Aborted"` does not mean that there is no text: a run cancelled after its text was
stored keeps it (a run stopped during the finalizers can even keep `status = "Completed"` with
`outcome = "Aborted"`), and a run whose result could not be stored completely is `Failed` with the warning
`run.result_not_saved`. Check `finalText` in the result; with `Failed` it is not guaranteed to be complete.
Details: [02-architecture](02-architecture.md#run-outcome-and-runstatus).

---

## OAuth endpoints

### Discovery

#### `GET /.well-known/oauth-authorization-server`

**Auth:** None  
**RFC:** 8414

Returns the server metadata as JSON. Clients use this endpoint to automatically discover all other OAuth endpoints.

```json
{
  "issuer": "https://geef.stefan-bechtel.de",
  "authorization_endpoint": "https://geef.stefan-bechtel.de/oauth/authorize",
  "token_endpoint": "https://geef.stefan-bechtel.de/oauth/token",
  "registration_endpoint": "https://geef.stefan-bechtel.de/oauth/register",
  "revocation_endpoint": "https://geef.stefan-bechtel.de/oauth/revoke",
  "response_types_supported": ["code"],
  "grant_types_supported": ["authorization_code", "refresh_token"],
  "code_challenge_methods_supported": ["S256"],
  "token_endpoint_auth_methods_supported": ["none"],
  "scopes_supported": ["mcp:full"]
}
```

---

#### `GET /.well-known/oauth-protected-resource`

**Auth:** None  
**RFC:** Draft (MCP resource metadata)

Returns metadata about the protected resource (the MCP server).

```json
{
  "resource": "https://geef.stefan-bechtel.de/mcp",
  "authorization_servers": ["https://geef.stefan-bechtel.de"],
  "bearer_methods_supported": ["header"],
  "scopes_supported": ["mcp:full"]
}
```

---

### Client registration

#### `POST /oauth/register`

**Auth:** None (or an optional `Authorization: Bearer <REGISTRATION_TOKEN>` if configured)  
**RFC:** 7591 — Dynamic Client Registration

Registers a new OAuth client. Typically called automatically by the MCP client.

**Request (JSON):**
```json
{
  "client_name": "My Client",
  "redirect_uris": ["https://example.com/callback"],
  "client_id": "my-client-id",
  "logo_uri": null,
  "client_uri": null
}
```

`client_id` is optional — if omitted, the server generates a UUID. `client_name` and `redirect_uris` are mandatory fields.

**Response (201):**
```json
{
  "client_id": "my-client-id",
  "client_id_issued_at": 1747390000,
  "redirect_uris": ["https://example.com/callback"],
  "client_name": "My Client",
  "token_endpoint_auth_method": "none",
  "grant_types": ["authorization_code", "refresh_token"],
  "response_types": ["code"]
}
```

---

### Authorization-code flow

#### `GET /oauth/authorize`

**Auth:** Cookie (Geef.Atelier login — redirected to `/login` if there is no session)

Starts the authorization-code flow. Shows the logged-in user the consent page with the client name and the requested permission.

**Mandatory query parameters:**

| Parameter | Description |
|-----------|-------------|
| `response_type` | Must be `code` |
| `client_id` | Registered client ID |
| `redirect_uri` | Must exactly match a registered URI |
| `code_challenge` | PKCE challenge (Base64Url-encoded SHA-256 hash of the verifier) |
| `code_challenge_method` | Must be `S256` (`plain` is rejected) |

**Optional parameters:** `scope`, `state`

Example URL as Claude Desktop calls it:
```
https://geef.stefan-bechtel.de/oauth/authorize
  ?response_type=code
  &client_id=claude-ai
  &redirect_uri=https%3A%2F%2Fclaude.ai%2Fapi%2Fmcp%2Fauth_callback
  &code_challenge=Oaa_K782ehJ6ZNf-INVXFk1mEKtzQz7xOERSXZUiGXA
  &code_challenge_method=S256
  &state=<random-state>
  &scope=mcp%3Afull
```

The `GET /oauth/authorize` page is a server-rendered Blazor consent page
(`[Authorize]` cookie). The approve/deny decision is sent via form POST to
`/oauth/consent` (see below) — not to `/oauth/authorize` itself.

---

#### `POST /oauth/consent`

**Auth:** Cookie (Geef.Atelier login) + anti-forgery token  
**Content-Type:** `application/x-www-form-urlencoded`

Submit target of the consent page. Processes the user's approval/denial,
on approval creates the authorization code and performs the redirect.

**On approval:** redirect to `redirect_uri?code=<auth_code>&state=<state>`  
**On denial:** redirect to `redirect_uri?error=access_denied&state=<state>`  
**On an invalid request:** error page (no redirect — protects against open redirect)

---

### Token endpoint

#### `POST /oauth/token`

**Auth:** None (public clients — authentication via PKCE instead of a client secret)  
**Content-Type:** `application/x-www-form-urlencoded`

Exchanges an authorization code for tokens or renews via a refresh token.

**Grant: `authorization_code`**

```
grant_type=authorization_code
&code=<auth_code>
&client_id=<client_id>
&redirect_uri=<redirect_uri>
&code_verifier=<pkce_verifier>
```

**Grant: `refresh_token`**

```
grant_type=refresh_token
&refresh_token=<refresh_token>
&client_id=<client_id>
```

**Response (200):**
```json
{
  "access_token": "<opaque_token>",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "<opaque_token>",
  "scope": "mcp:full"
}
```

Response headers: `Cache-Control: no-store`, `Pragma: no-cache` (RFC 6749 §5.1).

**Error behaviour:**
- Invalid or consumed code → `400 invalid_grant`
- Refresh token already used → `400 invalid_grant` **+ immediate revocation of all of the user's tokens** (theft detection per RFC 6819)

---

### Revocation

#### `POST /oauth/revoke`

**Auth:** None  
**Content-Type:** `application/x-www-form-urlencoded`  
**RFC:** 7009

Revokes an access token or refresh token. Always returns `200 OK` (even if the token is unknown).

```
token=<token>
&client_id=<client_id>
```

---

## Web-UI & account endpoints

The web interface is Blazor Server (cookie auth). A selection of the relevant,
externally reachable routes:

| Endpoint | Method | Auth | Purpose |
|----------|---------|------|-------|
| `/` | GET | Cookie | Home page / Atelier overview |
| `/health` | GET | None | Health check (`Healthy`) — for the reverse proxy/container lifecycle; includes the database and the `pending-migrations` check (D-067) |
| `/login` | GET/POST | None | Login page (static SSR) |
| `/auth/logout` | POST | Cookie | Logout (anti-forgery, Minimal API) |
| `/settings/theme` | POST | None | Theme-switch fallback (no-JS), redirect to the referer |
| `/hubs/runs` | WS | Cookie | SignalR hub for live run updates, signed-in users only; group names are derived on the server and the connection closes when the sign-in expires (D-067). The application's own pages use the in-process `IRunUpdateFeed` instead; no external client is known, and the hub is removed in stage 8 (D-068). |
| `/access-denied` | GET | Cookie | Shown to signed-in users who open a page they have no access to (D-067) |
| `/admin/users` | GET | Cookie (admin) | User management |
| `/admin/oauth-clients` | GET | Cookie (admin) | OAuth client management |
| `/account/connected-clients` | GET | Cookie | Self-service for one's own connected OAuth clients |
| `/crew`, `/crew/templates`, `/crew/profiles/*`, `/crew/studio`, `/crew/knowledge` | GET | Cookie | Crew/template/profile/Studio/knowledge-base management |
| `/start` | GET | Cookie | Start page for a new order: request, files with receipts, "Extra careful" (D-070) |
| `/work`, `/orders` | GET | Cookie | "My work": the person's orders, newest activity first (admins can show all users) |
| `/orders/{id}` | GET | Cookie | Order page: progress, text, open points, "How it was made", versions |
| `/runs`, `/runs/{id}`, `/new` | GET | Cookie | Run list, run detail, new job |
| `/crew/providers/*`, `/tools/*`, `/mcp-servers/*`, `/crew/profiles/grounding-providers/create`, `/crew/profiles/grounding-providers/edit/{name}` | GET | Cookie (admin) | Provider, tool, MCP server and grounding editors — admin-only since D-067 because they reference secrets by name |

Run-related pages are subject to run-user isolation (D-042): each user sees
only their own runs; the admin can see system-wide via an explicit toggle.

---

## Sources and order export endpoints (D-070)

All three require the cookie sign-in; API calls without it get `401`/`403` instead of a redirect to the login page.
Writes need the antiforgery token in the `RequestVerificationToken` header and, when the browser sends one, an
`Origin` equal to the host (otherwise `400`).

| Endpoint | Method | Answer |
|---|---|---|
| `/api/sources` | POST (multipart, exactly one file) | `201` with the receipt and `Location: /api/sources/{id}`; `200` with `duplicate: true` when the same person uploaded the same file before; `422` with `usable: false`, `sourceId: null` and `reason` for a file without readable text; `400`/`413` with `{ "reason": "…" }` |
| `/api/sources/{id}` | GET | The receipt of an own source, otherwise `404` |
| `/api/sources/{id}` | DELETE | `204`; `409` with a reason while an order uses the file; `404` for unknown or foreign sources |
| `/orders/{id}/export/{format}` | GET | The text of the order as `docx`, `pdf` or `md` (optional `?version=n`); `404` for unknown or foreign orders or an order without text, `400` for an unknown format |

The receipt is JSON in camelCase with enum values as camelCase strings, for example:

```json
{
  "sourceId": "<uuid|null>", "fileName": "offer.pdf", "kind": "pdf", "sizeBytes": 2202009,
  "pageCount": 80, "charCount": 160485, "wordCount": 24100, "quality": "good|partial|poor|unreadable",
  "usable": true, "duplicate": false, "budgetClass": "fullText|withMap|tooLarge",
  "preview": "<beginning of the text>", "warnings": [ { "code": "pdf.pages_without_text", "message": "…", "pages": [3, 7] } ],
  "reason": null, "estimatedTokens": 84466
}
```

Orders themselves are started from the start page through `IOrderService` inside the Blazor circuit; there is no
separate HTTP endpoint for submitting an order.

---

## Artifact download endpoint

### `GET /runs/{runId:guid}/artifacts/{artifactId:guid}/download`

**Auth:** `.RequireAuthorization()` — requires an active session (cookie or bearer token)

Downloads an artifact file that was produced by a finalizer during a run. Only artifacts of type `File` can be downloaded; `Url` and `Status` artifacts return 404.

**Owner check:** Non-admin users may only download artifacts belonging to their own runs. The check is performed via `IRunService.GetRunAsync` with the requesting username. Admin users bypass the owner check.

**Security note:** A path containment guard (`Path.GetFullPath` comparison) prevents directory traversal attacks on the server-side file path.

**Success response:** `200 OK` — file stream with `Content-Disposition: attachment` (filename from the artifact record).

**Error responses:**

| Status | Condition |
|--------|-----------|
| `401 Unauthorized` | Not authenticated |
| `403 Forbidden` | Authenticated but not the run owner (and not admin) |
| `404 Not Found` | `runId` or `artifactId` not found |
| `404 Not Found` | Artifact exists but is type `Url` or `Status` (not `File`) |
| `404 Not Found` | File not found on disk |

---

## Transform-Finalizer LLM-Binding

Custom Transform-Finalizer profiles can configure the LLM that powers the transformation. When creating or editing a finalizer profile via `materialize_template_proposal` or the UI, the `settings` object for Transform-type finalizers may include these optional keys:

| Key | Type | Description |
|-----|------|-------------|
| `Provider` | string | Provider name (e.g., `"openrouter"`, `"claude-cli"`). Must match an active provider. |
| `Model` | string | Model identifier (e.g., `"openai/gpt-4o-mini"`, `"anthropic/claude-opus-4.7"`). |
| `MaxTokens` | string | Maximum output tokens. Must be ≥ 10000. |
| `SystemPrompt` | string | The transformation instruction (e.g., "Rewrite this in a formal tone"). |
| `Temperature` | string (optional) | Temperature for sampling (0.0 to 2.0). If omitted, the provider default is used. |

System Transform-Finalizer profiles are read-only; clone them as a custom profile to override LLM bindings.

---

## Token design

| Property | Value |
|-------------|------|
| Format | Opaque — 32 bytes of random data, Base64Url-encoded |
| Storage | Only the SHA-256 hash in the database |
| Generation | `RandomNumberGenerator.GetBytes(32)` |
| Comparison | `CryptographicOperations.FixedTimeEquals` |
| Access-token lifetime | 1 hour |
| Refresh-token lifetime | 30 days, rotated on every refresh |
| Scope | Only `mcp:full` (full access to the MCP server) |

---

## Full flow (summary)

```
Client                          Geef.Atelier                    Browser/user
  │                                 │                                 │
  │── GET /.well-known/oauth-... ──>│                                 │
  │<── server metadata ─────────────│                                 │
  │                                 │                                 │
  │── POST /oauth/register ────────>│                                 │
  │<── client_id ───────────────────│                                 │
  │                                 │                                 │
  │── open browser with GET ────────────────────────────────────────>│
  │   /oauth/authorize?...          │                                 │
  │                                 │<── login (if needed) ───────────│
  │                                 │<── consent "grant access" ──────│
  │                                 │── redirect ?code=... ──────────>│
  │<── code (via redirect_uri) ─────────────────────────────────────│
  │                                 │                                 │
  │── POST /oauth/token ───────────>│                                 │
  │   (code + code_verifier)        │                                 │
  │<── access_token + refresh_token─│                                 │
  │                                 │                                 │
  │── POST /mcp ───────────────────>│                                 │
  │   Authorization: Bearer <token> │                                 │
  │<── tool response ───────────────│                                 │
```
