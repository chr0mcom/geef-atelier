# Tool System

*[Deutsch](10-tool-system_de.md) · **English***

*Last updated: 2026-06-10 (D-060, Phases A–C complete)*

---

## Overview

The Tool System introduces **agentic tool use** for all Atelier actors (Executor, Reviewer, Advisor, Finalizer). Instead of working only from model knowledge, an actor can now *call tools during its own turn* — look up a web source, query the knowledge base, fetch a URL, or invoke an MCP server — and use the results in its output.

A single central record, `ToolDefinition`, is the sole source of truth for every capability. The same definition powers both **Pull** (agentic: the LLM calls the tool when it decides to) and **Push** (grounding: eager context injection before the pipeline starts).

---

## Key Concepts

### `ToolDefinition`

Located in `Core/Domain/Tools/ToolDefinition.cs`.

```csharp
public sealed record ToolDefinition(
    string Name,           // Kebab-case identifier, e.g. "web-search"
    string DisplayName,    // UI label
    string Description,    // Passed to the LLM in the tool list
    string ToolType,       // Discriminator — see ToolType constants
    IReadOnlyDictionary<string, string> Settings,
    string? SecretRef,     // ENV-var key name — never the secret value
    JsonElement LlmSchema, // JSON Schema for the LLM input object
    ToolAccessClass AccessClass, // ReadOnly | Mutating
    bool IsSystem          // Built-in tools are read-only
)
```

**Name rules:** lowercase kebab-case, `[a-z0-9]` start/end, regex `^[a-z0-9]([a-z0-9\-]*[a-z0-9])?$`. Custom tools may use any valid name; system tools have well-known names (see `SystemTools`).

**Secrets:** `SecretRef` stores only the name of an environment variable (e.g. `"TAVILY_API_KEY"`). The value is resolved at execution time via `Environment.GetEnvironmentVariable`. It is never stored in the database, logs, snapshots, or the UI.

### Tool Types (`ToolType` constants)

| Constant | Value | Description |
|---|---|---|
| `WebSearch` | `web-search` | Tavily web search |
| `KnowledgeBase` | `knowledge-base` | pgvector semantic search |
| `UrlFetch` | `url-fetch` | HTTP GET with SSRF guard |
| `NewsSearch` | `news-search` | News API search |
| `AcademicSearch` | `academic-search` | arXiv / SemanticScholar / OpenAlex |
| `RestApi` | `rest-api` | Generic HTTP call with JSONPath extraction |
| `StaticContext` | `static-context` | Returns configured static text |
| `LearningRetrieval` | `learning-retrieval` | Atelier learning-loop retrieval |
| `McpTool` | `mcp-tool` | Discovered from an MCP server |

### Access Class (`ToolAccessClass`)

- **`ReadOnly = 0`** — tool only reads data; default for all tools including MCP-discovered tools.
- **`Mutating = 1`** — tool writes, modifies, or deletes data. Blocked in crew specs unless the operator explicitly sets `allow_mutating_tools: true` in the spec.

### Settings Keys (`ToolDefinitionSettingsKeys`)

| Key | Used by |
|---|---|
| `apiKey` | Legacy; prefer `SecretRef` |
| `baseUrl` | Web search, REST API |
| `maxResults` | Web search, news, academic |
| `endpoint` | REST API, MCP |
| `collectionName` | Knowledge base |
| `includeDomains` / `excludeDomains` | Web search domain filter |
| `newsLanguage` | News search (BCP-47) |
| `academicSource` | `arXiv`, `SemanticScholar`, `OpenAlex` |
| `jsonPathExpression` | REST API response extraction |
| `httpMethod` | REST API (`GET`, `POST`, …) |
| `staticContent` | Static context inline text |
| `refinementBinding` / `refinementMode` / `refinementInstructions` | Grounding refinement pass |
| `domainBoost` | Learning retrieval domain ranking |
| `mcpServerId` | MCP tool — GUID of the `McpServerConfig` |
| `mcpOriginalName` | MCP tool — original name as advertised by the server |

---

## Architecture

```
                    ToolDefinition (single source of truth)
                         │
          ┌──────────────┴────────────────┐
          ▼ PULL (agentic, during actor turn)  ▼ PUSH (eager, before pipeline)
     Executor / Reviewer / Advisor             GroundingProvider
     → IToolUseRunner (multi-turn loop)        → MultiProviderGroundingStep
          └──────────────┬─────────────────────────────┘
                         ▼  one execution layer, two consumers
                 IToolExecutor.ExecuteAsync(tool, inputJson, ctx, ct)
                    ├── web-search     → Tavily HTTP
                    ├── knowledge-base → pgvector
                    ├── url-fetch      → HttpClient + SSRF
                    ├── news-search    → news API
                    ├── academic-search→ arXiv/SS/OAlex
                    ├── rest-api       → HttpClient + JSONPath
                    ├── static-context → inline text
                    ├── learning-retrieval → LearningRepository
                    └── mcp-tool       → AtelierMcpClientFactory → McpClient
```

---

## Phase A — Foundation + Agentic Tools + Grounding Rebuild

### A-T1 — `ToolDefinition` Domain

`Core/Domain/Tools/`:
- `ToolDefinition` — primary constructor record (see above)
- `ToolType` — 9 type constants
- `ToolAccessClass` — `ReadOnly = 0`, `Mutating = 1`
- `ToolInvocation` — audit record per tool call (appended on each `ExecuteAsync`)

`ToolInvocation` fields:
```
Id, RunId, IterationNumber, ActorType, ActorName,
ToolName, ToolType, InputJson (raw), OutputExcerpt (≤500 chars),
CostEur?, DurationMs, Sequence, Outcome (Success/Failed/CapReached/Blocked),
CreatedAt
```
Secrets are never written to `InputJson` or `OutputExcerpt`.

### A-T2 — Persistence

- `IToolDefinitionRepository` — CRUD: `GetAllAsync`, `GetByNameAsync`, `GetSystemAsync`, `GetCustomAsync`, `SaveAsync`, `DeleteAsync`
- `IToolInvocationRepository` — append-only: `AddAsync`, `GetByRunAsync`
- EF configurations: `ToolDefinitionConfiguration` (JSONB for Settings and LlmSchema), `ToolInvocationConfiguration`
- DB table: `tool_definitions`, `tool_invocations` (Migration Step37)

### A-T3 — `IToolExecutor` + `IToolSchemaProvider`

**`IToolExecutor`** (`Application/Tools/`):

```csharp
Task<ToolExecutionResult> ExecuteAsync(
    ToolDefinition tool,
    string inputJson,
    ToolInvocationContext ctx,
    CancellationToken ct = default);
```

`ToolInvocationContext` carries `RunId`, `IterationNumber`, `ActorType`, `ActorName`, `Sequence`. Every call automatically appends a `ToolInvocation` audit record.

**`IToolSchemaProvider`** generates the `LlmTool` object (name + description + input schema) needed to register a tool with the LLM. The schema is taken from `ToolDefinition.LlmSchema` (or auto-generated per type if the stored schema is empty).

### A-T4 — Multi-Turn LLM Client

`LlmRequest` was extended with an optional `Messages` list (`IReadOnlyList<LlmMessage>`) that replaces the single system+user prompt when set. `LlmResponse` now carries all `ToolCalls` (not just the first one). `OpenAiMessageFormat` serialises full message history including `assistant` `tool_calls` and `tool` result messages.

### A-T5 — `IToolUseRunner` — the Agentic Loop

`Infrastructure/Pipeline/ToolUseRunner.cs`:

```
1. Build initial history (system + user prompt)
2. Call LLM with full history + bound LlmTools
3. Response has tool_calls?
   → For each tool_call:
       - Execute via IToolExecutor (with per-tool timeout, default 30 s)
       - Append tool-result message to history
   → Increment call counter; if counter ≥ maxToolCalls → abort (CapReached)
   → Go to step 2
4. Response is plain text (no tool_calls) → loop ends, return text
5. Required final tool (e.g. submit_review) received → loop ends
```

The loop is fully provider-agnostic. HTTP providers and CLI providers are driven the same way. Per-turn cap default: **5 tool calls**. Per-tool timeout default: **30 s**.

Every tool call is recorded as a `ToolInvocation`. LLM round-trip costs are tracked via `ICostAccumulator`; tool costs (e.g. Tavily credits) are captured in `ToolExecutionResult.CostEur`.

### A-T6 — CLI Proxy: Agentic Loop Participation

`cli-proxy/src/tool_use_parser.py` was extended with `build_agentic_tool_prompt()`. When the .NET loop sends a call with tools but without a forced `tool_choice`, the proxy injects a protocol addendum instructing the model to respond with exactly one JSON `{"tool_call": {"name": …, "arguments": {…}}}` or plain final text. The parser routes per-turn: if a `tool_call` JSON is detected, it builds a `tool_calls` response; otherwise it returns the text as `stop`.

The forced single-tool path (`submit_review` via `tool_choice`) is unchanged.

### A-T7 — Provider Capability Detection

`ILlmClientResolver.SupportsAgenticTools(providerName)` returns `true` for HTTP providers (OpenAI-compatible, OpenRouter, Anthropic, custom) and `false` for `generic` or explicitly-disabled providers. This is used in:
- `ProfileBasedReviewer` / `ProfileBasedExecutor` / `ProfileBasedAdvisor` — decide whether to use the tool loop or fall back to single-shot
- `CrewSpecValidator` step 8a — rejects specs that bind tools to a non-capable provider
- `/tools` editor — shows a capability warning badge

Degradation is **visible**, never silent: the run log and the UI badge both show "provider does not support agentic tools".

### A-T8 — Tool Binding on Actor Profiles

`ExecutorProfile`, `ReviewerProfile`, `AdvisorProfile`, and `FinalizerProfile` (Transform type only) each gained:
```csharp
IReadOnlyList<string> ToolNames { get; init; } // names from ToolDefinition catalog
```

If `ToolNames` is non-empty and the provider supports agentic tools, the actor runs via `IToolUseRunner` instead of single-shot `ILlmClient.CompleteAsync`. For reviewers, `submit_review` remains the mandatory final tool call; the loop ends when it arrives.

Migration Step38 adds the `ToolNames` JSON column to `ExecutorProfiles`, `ReviewerProfiles`, `AdvisorProfiles`, `FinalizerProfiles`.

### A-T9 — Grounding Rebuild on Central Tools

All grounding providers now reference a `ToolDefinition` by name (`ToolName` on `GroundingProviderProfile`). The 8 type-specific grounding provider classes were refactored: raw capability logic moved into reusable executors (e.g. `TavilySearchClient`, `VectorSearchExecutor`). A new `ToolBackedGroundingProvider` wraps any tool type for eager Push invocation.

System grounding provider profiles are defined in `SystemCrew` (code constants), referencing system `ToolDefinition`s (also code constants in `SystemTools`). Migration Step40 seeds the system tools into the database on first start.

Migration Step39 adds the `ToolName` column to `GroundingProviderProfiles`.

### A-T10 — Snapshot + Audit UI

`CrewSnapshot` schema version 2: each actor profile in the snapshot now includes its bound `ToolDefinition`s (fully dereferenced — name, type, description, settings, AccessClass — but never secrets). Runs remain reproducible regardless of later catalog changes.

New UI component `ToolInvocationsBlock.razor` on the run detail page: per iteration and actor, shows which tool was called, with what input, what the (truncated) output was, cost, and duration. Provides provenance for reviewer findings.

### A-T11 — `/tools` CRUD UI + `ToolPicker`

New pages: `/tools` (list), `/tools/create`, `/tools/edit/{name}`, `/tools/view/{name}`.

`ToolEditor.razor` fields:
- Name (kebab-case, validated)
- Display name, description
- Tool type (dropdown)
- Settings (type-specific: API key / base URL / endpoint / collection / domains / language / static content / etc.)
- SecretRef (ENV-var key, never the value — field help clearly states this)
- AccessClass (ReadOnly default; Mutating shows a red danger badge)
- LlmSchema (JSON editor for the input schema description)

`ToolPicker.razor` is reused across all actor editors (`ExecutorProfileEditor`, `ReviewerProfileEditor`, `AdvisorProfileEditor`, `FinalizerEditor`) and in `GroundingProviderEditor` (where exactly one tool is referenced). The picker shows a capability warning when the actor's provider does not support agentic tools.

---

## Phase B — Auto-Crew Composer Integration

### B-T1 — `CrewPartSpec.ToolNames`

`CrewSpecArtifact` → `CrewPartSpec.ToolNames` (`IReadOnlyList<string>?`): optional list of tool names to bind to the composed actor.

`CrewSpecParser` deserialises the `tool_names` JSON array. `CrewMaterializer` passes `ToolNames` through to the materialised profile. `CrewSpecTool` JSON schema includes `tool_names` as an optional array of strings for executor, reviewers, advisors, and finalizers.

### B-T2 — Tool Catalog Grounding Provider

`Infrastructure/Grounding/ToolCatalogGroundingProvider.cs` — type discriminator `"tool-catalog"`.

On each grounding invocation, it fetches all `ToolDefinition`s (via scoped `IToolDefinitionRepository`), formats them as a Markdown table (name, type, description, access class), and injects it as context into the composition run. The composer meta-LLM can therefore reference existing tools by name instead of hallucinating names.

Registered as the third grounding provider in `SystemCrew.CrewComposerTemplate.GroundingProviderNames`.

### B-T3 — `CrewComposerToolBindingProfile` Reviewer

System reviewer `"crew-composer-tool-binding"` (provider: `openrouter`, model: `google/gemini-3.5-flash`) checks tool binding decisions in composed specs:
- **Necessity**: is the tool actually needed for this actor's role?
- **Access class**: Mutating tools blocked in Phase B (critical finding)
- **Role fit**: static-context as Pull on a reviewer is a design smell (major)
- **Count**: more than 3 tools per actor raises a minor finding
- **Catalog membership**: tool names not in the catalog are critical

This is the 6th reviewer on the `crew-composer` crew.

### B-T4 — Deterministic Validator: Step 8

`CrewSpecValidator.ValidateToolBindingsAsync` runs after all profile validation steps:

| Check | Severity | Condition |
|---|---|---|
| 8a — provider capability | Critical | actor has `tool_names` but `!SupportsAgenticTools(provider)` |
| 8b — tool existence | Critical | tool name not found in `IToolDefinitionRepository` |
| 8c — Mutating blocked | Critical | `tool.AccessClass == Mutating && !spec.AllowMutatingTools` |

All unique tool names are batch-fetched in one DB query to avoid N+1.

---

## Phase C — MCP Client

### C-T1 — `McpServerConfig` + `AtelierMcpClientFactory`

`Core/Domain/Mcp/McpServerConfig.cs`:
```csharp
public sealed record McpServerConfig
{
    public Guid Id { get; init; }
    public string Name { get; init; }       // display name
    public string Url { get; init; }        // MCP server endpoint URL
    public string? AuthHeaderEnv { get; init; } // ENV-var name for the Bearer token
    public bool IsActive { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
```

`AuthHeaderEnv` stores only the environment variable name. At connection time, `AtelierMcpClientFactory` reads the value from the environment and injects it as `Authorization: Bearer <token>`.

`IAtelierMcpClientFactory.ConnectAsync(config, ct)`:
1. Validates the URL (`Uri.TryCreate`, absolute)
2. Passes the URL through `IUrlSafetyValidator` (SSRF guard — blocks private IPs, localhost, internal domains)
3. Creates an `HttpClient` from the `"mcp-client"` named factory
4. Optionally adds the `Authorization` header from the ENV var
5. Creates `HttpClientTransport` → `McpClient.CreateAsync`

Returns a connected `McpClient` implementing `IAsyncDisposable`.

DB table: `mcp_server_configs` (Migration Step41).

### C-T2 — Tool Discovery + Catalog Import

`IMcpToolDiscoveryService.DiscoverAsync(config, ct)` connects to the MCP server, calls `tools/list`, and maps each `McpClientTool` to a `ToolDefinition` candidate:

| `ToolDefinition` field | Source |
|---|---|
| `Name` | Sanitised from `McpClientTool.Name` (lowercase kebab-case, `[a-z0-9-]` only, max 64 chars) |
| `DisplayName` | `McpClientTool.Title ?? McpClientTool.Name` |
| `Description` | `McpClientTool.Description` |
| `ToolType` | `"mcp-tool"` |
| `Settings["mcpServerId"]` | `config.Id.ToString()` |
| `Settings["mcpOriginalName"]` | `McpClientTool.Name` (original, unsanitised) |
| `LlmSchema` | `McpClientTool.JsonSchema` (the `JsonElement` from the server's tool description) |
| `AccessClass` | `ReadOnly` (conservative default) |
| `IsSystem` | `false` |

Returned definitions are candidates only — not yet persisted. The UI's "Import" button calls `IToolDefinitionService.SaveAsync`.

**`/mcp-servers` UI** — new Blazor pages:
- `/mcp-servers` — table of all server configs; per row: "Edit", "Delete", "Discover Tools" button
- `/mcp-servers/create` and `/mcp-servers/edit/{Id:guid}` — CRUD form (Name, URL, AuthHeaderEnv, IsActive)
- "Discover Tools" triggers `IMcpToolDiscoveryService.DiscoverAsync` and shows results inline; already-imported tools are marked; "Import" calls `SaveAsync`

### C-T3 — `mcp-tool` Execution + Mutating Opt-in

**`ToolExecutor` mcp-tool branch:**
1. Read `Settings["mcpServerId"]` → load `McpServerConfig` from DB
2. Guard: server must be active
3. Read `Settings["mcpOriginalName"]` (falls back to `tool.Name`)
4. Parse `inputJson` into a `Dictionary<string, JsonElement>` of arguments
5. `await using var client = await mcpClientFactory.ConnectAsync(serverConfig, ct)`
6. `var response = await client.CallToolAsync(originalName, args, ct)`
7. Concatenate all non-empty `response.Content[].Text` entries, wrap in `[MCP Tool Result]` / `[End of MCP tool result]` delimiters

Connection errors and tool execution failures are caught and returned as `ToolExecutionResult` with a non-null `Error` (not thrown). Every call is recorded in `tool_invocations`.

**Mutating Opt-in** — `CrewSpecArtifact.AllowMutatingTools` (default: `false`):

```json
{
  "allow_mutating_tools": true,
  "executor": { "tool_names": ["mcp-write-tool"] }
}
```

When `allow_mutating_tools` is absent or `false`, the validator blocks any Mutating-class tool binding with a critical issue. When `true`, Mutating tools are permitted. The UI shows a red danger badge on any `ToolDefinition` with `AccessClass = Mutating` in both the editor and detail view.

---

## Database Changes

| Migration | Step | What changed |
|---|---|---|
| `Step37ToolSystem` | 37 | New tables: `tool_definitions` (JSONB Settings, LlmSchema), `tool_invocations` |
| `Step38ToolNamesOnProfiles` | 38 | New JSON column `ToolNames` on `ExecutorProfiles`, `ReviewerProfiles`, `AdvisorProfiles`, `FinalizerProfiles` |
| `Step39ToolNameOnGroundingProfiles` | 39 | New column `ToolName` on `GroundingProviderProfiles` |
| `Step40SystemToolsSeed` | 40 | Idempotent upsert of 9 system `ToolDefinition`s on startup |
| `Step41McpServerConfigs` | 41 | New table: `mcp_server_configs` |

**No existing data was lost.** All migrations only add new tables and columns. The `DOWN` rollback paths are the only places that contain `DROP` or `ALTER … DROP COLUMN`.

---

## Security Notes

- **Secrets** — `SecretRef` is an ENV-var key name only. The actual API key/token is never persisted in the DB, the `CrewSnapshot`, logs, or any UI field.
- **SSRF** — `IUrlSafetyValidator` blocks private IP ranges, `localhost`, link-local, and internal hostnames before any HTTP call originating from a `ToolDefinition` (url-fetch, rest-api, mcp-tool).
- **Mutating access** — `ToolAccessClass.Mutating` is blocked by default. The operator must explicitly set `allow_mutating_tools: true` per spec. The UI highlights Mutating tools with a red danger badge.
- **Per-turn cap** — the agentic loop never runs indefinitely. Default cap: 5 tool calls per actor turn. Cap-reached state is audited as `ToolInvocationOutcome.CapReached`.
- **Per-tool timeout** — default 30 seconds per tool call. Timeout is audited.

---

## Running Locally

```bash
# Start the database
cd /srv/docker/websites/geef_atelier
docker compose -f docker-compose.dev.yml up -d postgres

# Run the web app (hot-reload)
dotnet watch --project src/Geef.Atelier.Web

# Seed system tools (automatic on startup via Step40 migration)
# Navigate to /tools to see the 9 system tool definitions

# Add an MCP server (manual)
# Navigate to /mcp-servers → "Neuen Server hinzufügen"
# Enter URL, optionally set AuthHeaderEnv to an ENV-var name
# Click "Tools entdecken" to import discovered tools
```

---

## File Index

| Layer | Path | Description |
|---|---|---|
| Core | `Core/Domain/Tools/ToolDefinition.cs` | Central record + settings keys |
| Core | `Core/Domain/Tools/ToolType.cs` | 9 type constants |
| Core | `Core/Domain/Tools/ToolAccessClass.cs` | ReadOnly / Mutating |
| Core | `Core/Domain/Tools/ToolInvocation.cs` | Audit record |
| Core | `Core/Persistence/Tools/IToolDefinitionRepository.cs` | CRUD interface |
| Core | `Core/Persistence/Tools/IToolInvocationRepository.cs` | Append-only interface |
| Core | `Core/Domain/Mcp/McpServerConfig.cs` | MCP server connection config |
| Core | `Core/Persistence/Mcp/IMcpServerConfigRepository.cs` | CRUD interface |
| Application | `Application/Tools/IToolExecutor.cs` | Unified execution interface |
| Application | `Application/Tools/IToolSchemaProvider.cs` | LLM schema generation |
| Infrastructure | `Infrastructure/Tools/ToolExecutor.cs` | Dispatcher to all 9 executors |
| Infrastructure | `Infrastructure/Pipeline/ToolUseRunner.cs` | Agentic multi-turn loop |
| Infrastructure | `Infrastructure/Mcp/IAtelierMcpClientFactory.cs` | MCP client factory interface |
| Infrastructure | `Infrastructure/Mcp/AtelierMcpClientFactory.cs` | SSRF + auth + McpClient |
| Infrastructure | `Infrastructure/Mcp/IMcpToolDiscoveryService.cs` | tools/list → ToolDefinition |
| Infrastructure | `Infrastructure/Mcp/McpToolDiscoveryService.cs` | Discovery implementation |
| Infrastructure | `Infrastructure/Grounding/ToolCatalogGroundingProvider.cs` | B-T2 catalog grounding |
| Infrastructure | `Infrastructure/Composition/CrewSpecValidator.cs` | Step 8 tool binding checks |
| Infrastructure | `Infrastructure/Persistence/Migrations/` | Step37–Step41 |
| Web | `Web/Components/Pages/ToolsIndex.razor` | `/tools` list |
| Web | `Web/Components/Pages/ToolEditor.razor` | Create / edit tool |
| Web | `Web/Components/Pages/ToolView.razor` | Read-only detail view |
| Web | `Web/Components/Pages/McpServersIndex.razor` | `/mcp-servers` list + discovery |
| Web | `Web/Components/Pages/McpServerEditor.razor` | Add / edit MCP server |
| Web | `Web/Components/UI/ToolPicker.razor` | Reusable actor tool picker |
| Web | `Web/Components/UI/ToolInvocationsBlock.razor` | Run detail audit block |
| Core | `Core/Domain/Crew/Composition/CrewSpecArtifact.cs` | `AllowMutatingTools` flag |
| Infrastructure | `Infrastructure/Composition/CrewSpecParser.cs` | `allow_mutating_tools` parsing |
| Infrastructure | `Infrastructure/Composition/CrewSpecTool.cs` | Composer JSON schema |
