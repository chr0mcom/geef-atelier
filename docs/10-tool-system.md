# Tool System

*[Deutsch](10-tool-system_de.md) · **English***

*Last updated: 2026-11-27 (D-091: stage 8d, the tool system was removed; this page is now a short note; before that 2026-11-24, D-067: actual state after stage 0; 2026-06-10, D-060)*

---

The tool system of D-060 (central tool catalogue, `/tools` pages, tool definitions, invocations and bindings on profiles,
MCP server configurations for tools, the agentic tool loop and grounding through tools) was **removed in stage 8d (D-091)**.
Its MCP client had already gone in stage 8c (D-089). The agentic path was never wired into the pipeline (D-067).

Nothing replaces tool use by the actors. The only access to the web is the research in the grounding step of the direct path
([08-crew-system.md](08-crew-system.md#research-in-the-grounding-step-d-074)). The database tables and the old runs were removed with the database cleanup (D-092).

The earlier description is in the git history of this file.

## Secrets and the environment-variable allow list

This rule came with D-067 and still applies, now to the provider settings: where a provider names an environment variable (API
key variable, endpoint override variable), only names on the allow list are saved or read (`IEnvReferencePolicy`). A name that
is not allowed is refused on save, and at run time its value is not read.

The list is the built-in default (`EnvReferenceOptions.DefaultAllowed`: the usual provider key names, `OLLAMA_ENDPOINT`,
`TAVILY_API_KEY` and the prefix `ATELIER_SECRET_*`) plus the entries of `EnvReference:Allowed` (exact names or prefixes ending
in `*`; matching is case-sensitive). Infrastructure secrets such as the database password are deliberately not on it.
