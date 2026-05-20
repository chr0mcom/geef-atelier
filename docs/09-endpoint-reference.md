# Endpoint reference

*[Deutsch](09-endpoint-reference_de.md) · **English***

*Last updated: 2026-05-20 (D-051: `providerType` values updated to include `static-context`, `url-fetch`, `news-search`)*

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
| `providerType` | string | `"tavily"`, `"vector-store"`, `"static-context"`, `"url-fetch"`, or `"news-search"` |
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
| `/health` | GET | None | Health check (`Healthy`) — for the reverse proxy/container lifecycle |
| `/login` | GET/POST | None | Login page (static SSR) |
| `/auth/logout` | POST | Cookie | Logout (anti-forgery, Minimal API) |
| `/settings/theme` | POST | None | Theme-switch fallback (no-JS), redirect to the referer |
| `/hubs/runs` | WS | — | SignalR hub for live run updates |
| `/admin/users` | GET | Cookie (admin) | User management |
| `/admin/oauth-clients` | GET | Cookie (admin) | OAuth client management |
| `/account/connected-clients` | GET | Cookie | Self-service for one's own connected OAuth clients |
| `/crew`, `/crew/templates`, `/crew/profiles/*`, `/crew/studio`, `/crew/knowledge` | GET | Cookie | Crew/template/profile/Studio/knowledge-base management |
| `/runs`, `/runs/{id}`, `/new` | GET | Cookie | Run list, run detail, new job |

Run-related pages are subject to run-user isolation (D-042): each user sees
only their own runs; the admin can see system-wide via an explicit toggle.

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
