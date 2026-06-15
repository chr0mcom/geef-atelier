# Geef.Atelier

*[Deutsch](README_de.md) · **English***

Text-generation pipeline platform built on the [Geef SDK](https://github.com/chr0mcom/geef). Multiple models collaborate in configurable crews — the executor writes, the reviewers assess, and the pipeline iterates until convergence.

## Implementation status

**Step 1 ✅** Solution structure, Postgres persistence, health check, Docker Compose.

**Step 2 ✅** In-memory Geef pipeline with stub providers — no LLM, no DB access. Convergence loop, event sink, all four provider contracts implemented and covered by xUnit tests.

**Step 3 ✅** Real Anthropic API calls — `IAnthropicClient`, `LlmExecutionStep`, two `LlmReviewer` (tool use). Polly resilience.

**Step 4 ✅** Postgres persistence — `PostgresEventSink` writes every pipeline run with its iterations, findings, token usage and event log to the DB.

**Step 5 ✅** BackgroundService orchestration — `RunOrchestratorService` polls for pending runs, sets an atomic claim, runs the Geef pipeline concurrently, and recovers crashed runs on startup.

**Step 6 ✅** Application service layer — `IRunService` (Submit/Get/List/Cancel), `IRunRepository`, cancellation watcher.

**Step 7 ✅** Blazor UI — three pages (`/new`, `/runs`, `/runs/{id}`), SignalR hub with live status, 9 UI components, bUnit and Playwright tests.

**Step 8 ✅** Cookie auth — single-user login, `[Authorize]` on pages, login/logout, `TestAuthenticationHandler` for E2E tests.

**Step 9+ ✅** Crew system, advisor passes, Template Studio (with full-field edit, D-043), domain templates, grounding-provider CRUD, vector-store RAG, PDF support, cost tracking.

**MCP OAuth ✅** Self-hosted OAuth 2.1 authorization server (RFC 8414/7591/7636/7009/8252) — authorization-code flow with mandatory PKCE/S256, opaque tokens, refresh rotation, reuse detection.

**Multi-user ✅** DB-based user management with BCrypt, admin UI at `/admin/users`, startup seeding of the admin account from env vars.

**Run-user isolation ✅** Each user sees only their own runs; admin override via explicit toggles. MCP runs are attributed to the authorizing OAuth user, Claude Code CLI runs (static token) to the admin (D-042).

**Finalizer pipeline ✅** 5th profile type — FinalizerProfile with four types (FileExport, MetadataEnrich, ExternalSink, Transform). 17 system profiles. RunArtifact entity (File with download, Url, Status). Download endpoint with owner check. FinalizerPicker + RunFinalizersOnMaxAttempts in CrewTemplateEditor. RunDetail artifact table. Studio integration. 2 new MCP tools (D-044, 2026-05-19).

**Run-Resume ✅** Resume a failed or aborted run from where it left off — Seed mode (continue from last draft) or Clean mode (fresh start with same briefing). "Fortsetzen" button in RunDetail, ResumeRunDialog with MaxIterations override. ParentRunId link shown in RunDetail (PR #18, 2026-05-19).

**Grounding Refinement ✅** Optional AI filter pass after every grounding provider fetch — Filter mode (keep/drop per source with reasoning) and Synthesize mode (merge all sources into a coherent text with `[n]` citations). Configurable per provider via `LlmBinding` (D-049/D-050, PR #22, 2026-05-20).

**Grounding Types ✅** Three new grounding-provider types alongside the existing `tavily` and `vector-store` providers: `static-context` (curated fixed text — style guides, glossaries, brand voice), `url-fetch` (fetch specific URLs with SSRF guard blocking all private/cloud-metadata IPs), and `news-search` (Tavily news topic with date filter). New system profile `tavily-news`. Dashboard cost aggregation now includes Grounding Refiner costs (D-051, PR #23, 2026-05-20).

**Tool System ✅** Central `ToolDefinition` catalogue + agentic tool-use (Pull, `IToolUseRunner`) for executor/reviewer/advisor, grounding rebuilt on the same tools (Push), provider capability detection, `/tools` CRUD + `ToolPicker`, `ToolInvocationsBlock` audit, Auto-Crew tool binding, and an MCP client (`/mcp-servers` discovery/import). See [docs/10-tool-system.md](docs/10-tool-system.md) (D-060).

**Specialization Packs ✅** Generic actors (role prompt + `{specialization}` slot) + reusable scoped `SpecializationPack`s bound per crew. Effective prompt composed at snapshot-build time (CrewSnapshot v3 + provenance), audit UI shows it. The six domain-specialized system reviewers became two generic roles + six DomainScoped packs. `/packs` CRUD, scope enforcement, composer integration (`pack_names`/`packs` + pack-catalog grounding), lifecycle (cascade-delete, promote/demote/clone with LLM generality review, auto-GC). See [docs/11-specialization-packs.md](docs/11-specialization-packs.md) (D-061).

Currently: **over 1800 tests** (green; pre-existing Testcontainers/E2E env flakes excluded).

Full scope: [docs/01-vision-and-scope.md](docs/01-vision-and-scope.md)

---

## Local startup

```bash
# Start app + Postgres
docker compose -f docker-compose.dev.yml up --build -d

# Health check
curl http://localhost:8080/health   # → Healthy

# Stop the stack
docker compose -f docker-compose.dev.yml down
```

## Auth setup

### Users

The app supports multiple user accounts. The first admin account is set automatically from env vars and is created or synchronized on startup:

```bash
# Generate a BCrypt hash for a password (work factor 11)
dotnet run --project tools/HashPassword -- "YourPassword"
# Output: $2a$11$...
```

```env
ATELIER_USER=stefan
ATELIER_PASSWORD_HASH=$2a$11$...
```

Further users can be created in the admin panel at `/admin/users` (visible only to the admin account).

**Dev defaults** (local development only, in `docker-compose.dev.yml`):
- Username: `admin`
- Password: `DevPassword!`

### MCP token (Claude Code CLI)

```bash
# Generate a random token
openssl rand -hex 32
```

```env
ATELIER_MCP_TOKEN=<hex-token>
```

---

## Tests

```bash
# Requires a running Docker daemon (Testcontainers)
dotnet test
```

## Run a migration manually

```bash
dotnet ef database update \
  --project src/Geef.Atelier.Infrastructure \
  --startup-project src/Geef.Atelier.Web
```

---

## MCP server

The MCP server is served at `/mcp`. Two auth paths are available in parallel:

### Path A: Static bearer token (Claude Code CLI)

```json
{
  "mcpServers": {
    "geef-atelier": {
      "url": "https://<your-domain>/mcp",
      "transport": "streamable-http",
      "auth": {
        "type": "bearer",
        "token": "<ATELIER_MCP_TOKEN>"
      }
    }
  }
}
```

### Path B: OAuth 2.1 (Claude Desktop / Claude.ai custom connector)

Enter the URL `https://<your-domain>/mcp` in the client — the client detects the `WWW-Authenticate` header carrying the resource-metadata URL and starts the OAuth flow automatically (dynamic client registration → browser login → consent page → token exchange).

Prerequisite: the OAuth client must be registered in the admin panel at `/admin/oauth-clients`, or the client registers itself via dynamic client registration (`POST /oauth/register`).

### MCP tools

**Run management:**
- `submit_request` — submit a new run (optional: `crew_template`, `custom_crew`)
- `get_run_status` — query the status of a run
- `get_run_result` — retrieve the result of a completed run
- `list_runs` — list recent runs
- `get_run_details` — retrieve detailed information including iterations
- `cancel_run` — cancel a running run

**Crew system:**
- `list_crew_templates` — available crew templates (system + custom)
- `list_reviewer_profiles` — available reviewer profiles (system + custom)
- `list_advisor_profiles` — available advisor profiles (system + custom)
- `list_grounding_provider_profiles` — available grounding-provider profiles (system + custom)

**Knowledge base & Template Studio:**
- `list_knowledge_documents` — list global knowledge-base documents
- `analyze_template_proposal` — analyze a task description, produce a template proposal (persisted)
- `materialize_template_proposal` — materialize a reviewed proposal as a custom template + profiles

**Artifacts:**
- `list_run_artifacts` — list RunArtifacts from a completed run
- `download_run_artifact` — download artifact content (Base64 for files, URL for external sinks)

15 MCP tools in total. Full endpoint documentation: [docs/09-endpoint-reference.md](docs/09-endpoint-reference.md)

---

## Crew system

Every run uses a configurable crew of an executor (writes the draft) and reviewers (assess the draft). The default template `"klassik"` reproduces the original behaviour with two reviewers running in parallel.

**Available evaluation strategies:** `Parallel` (default), `Sequential`, `FailFast`, `Priority`.

**System profiles** are versioned in code and read-only. **Custom profiles** can be created via `ICrewService` or MCP and are stored in the DB.

Every run stores a fully embedded **CrewSnapshot** in the DB — so the run stays reproducible even if profiles are changed later.

### Crew management in the UI

| Page | URL |
|-------|-----|
| Crew overview | `/crew` |
| Template list | `/crew/templates` |
| Create/edit template | `/crew/templates/new`, `/crew/templates/{name}` |
| Reviewer profiles | `/crew/profiles/reviewers` |
| Executor profiles | `/crew/profiles/executors` |
| Grounding providers | `/crew/profiles/grounding-providers` |
| Finalizer profiles | `/crew/profiles/finalizers` |
| Template Studio (AI-assisted template creation) | `/crew/studio` |

Details: [docs/08-crew-system.md](docs/08-crew-system.md)

---

## Production deployment

Prerequisites: Traefik is running on the server, the domain's DNS points to the server.

### Initial setup

**1. Generate secrets:**

```bash
# BCrypt hash for the UI password (work factor 11)
dotnet run --project tools/HashPassword -- "YourPassword"

# MCP token (64 hex characters)
openssl rand -hex 32

# Postgres password
openssl rand -base64 24
```

**2. Create the `.env` file** (never commit to Git — it is gitignored):

```env
POSTGRES_DB=geef_atelier
POSTGRES_USER=geef_atelier
POSTGRES_PASSWORD=<generated-password>

ATELIER_USER=<admin-username>
ATELIER_PASSWORD_HASH=<bcrypt-hash>
ATELIER_MCP_TOKEN=<hex-token>
ATELIER_DOMAIN=<your-domain>

LLM_OPENROUTER_API_KEY=<openrouter-api-key>

# Tavily web search (https://tavily.com) — optional; required for tavily, news-search, and tavily-news grounding profiles
TAVILY_API_KEY=
```

**3. Start the stack:**

```bash
docker compose up -d --build
```

Migrations run automatically on startup. Health check: `https://<your-domain>/health`.

### Restart / update

```bash
docker compose build --no-cache web && docker compose up -d web
docker compose logs -f web
```

### Important URLs

| URL | Description |
|-----|--------------|
| `https://<your-domain>/` | Web UI (cookie auth) |
| `https://<your-domain>/health` | Health check |
| `https://<your-domain>/mcp` | MCP endpoint (bearer / OAuth 2.1) |
| `https://<your-domain>/admin/users` | User management (admin only) |
| `https://<your-domain>/admin/oauth-clients` | OAuth client management (admin only) |
| `https://<your-domain>/.well-known/oauth-authorization-server` | OAuth server metadata |

---

## Backup & restore

The `postgres-backup` container creates daily backups automatically.

- **Schedule:** daily at 03:00 UTC
- **Retention:** 7 daily, 4 weekly, 6 monthly snapshots
- **Location:** Docker volume `geef-atelier-backups`
- **Format:** `.sql.gz` (gzip-compressed pg_dump)

```bash
# Trigger a backup manually
docker compose exec postgres-backup /backup.sh

# Inspect backups
docker compose exec postgres-backup ls -lh /backups/last/

# Copy a backup out of the volume
docker cp geef-atelier-postgres-backup:/backups/last/<file>.sql.gz ./
```

### Restore

```bash
docker compose stop web
./scripts/restore-backup.sh <path-to-backup-file.sql.gz>
curl https://<your-domain>/health
```

> **Note:** `scripts/restore-backup.sh` overwrites all existing data. Take a fresh copy before restoring.

---

## Project structure

```
src/
  Geef.Atelier.Core/           Domain entities, interfaces — no external dependencies
  Geef.Atelier.Application/    IRunService, IOAuthService, IUserAdminService, etc.
  Geef.Atelier.Infrastructure/ EF Core, Npgsql, LLM clients, Geef.Sdk provider impl.
  Geef.Atelier.Web/            Blazor Server, BackgroundService, endpoints, MCP server
  Geef.Atelier.Mcp/            MCP tool definitions (class library)
tests/
  Geef.Atelier.Tests/          xUnit + Testcontainers + bUnit
tools/
  HashPassword/                BCrypt hash generator CLI
docs/
  reports/                     Completion reports per build step
```

## Stack

- .NET 10 / Blazor Server / Minimal API
- PostgreSQL 16 + pgvector via `Npgsql.EntityFrameworkCore.PostgreSQL`
- [Geef.Sdk 1.0.0-ci.1](https://www.nuget.org/packages/Geef.Sdk/) (prerelease)
- Docker / Traefik
