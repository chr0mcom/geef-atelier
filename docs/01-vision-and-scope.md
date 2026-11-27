# Vision and Scope

*[Deutsch](01-vision-and-scope_de.md) · **English***

*Last updated: 2026-11-27 (D-091: stage 8d, scope matches the current product: standard way only, no team building, no learning, no knowledge base; before that 2026-11-26: guides per audience and the page "How it works"; D-077; D-068: at most three short questions with click answers before the run, never during it; 22 May 2026, D-054)*

## Vision

**Geef.Atelier** is a text manufactory: it turns a person's request and files into a finished text and says honestly what was
checked and what is still open. A request becomes an **order**; at most three short questions come before the start; one
writing call gets the files in full text; automatic checks and a fact check by another model follow, with at most one
correction. There is no second, slower way. The guides [12](12-user-guide.md) to [15](15-developer-guide.md) describe the
product for each audience.

The original vision was a machine that composes a crew of AI roles for each piece of work. That way of working, together with
the learning loop and the knowledge base, was removed in stage 8d (D-091); the git history and the
[decisions log](05-decisions-log.md) keep its description.

The project is the productive application of the [Geef SDK](https://github.com/chr0mcom/geef): it uses the GEEF pipeline
pattern (Grounding / Execution / Evaluation / Finalize), its convergence policies, evaluation strategies, event sink and
middleware.

## Guiding principles

1. **One clear way** — every order goes the same way. There are no teams, profiles or pipelines to set up.
2. **Honest transparency** — every result comes with a fixed account of what was checked, the open points and the
   assumptions; every run keeps its passes and findings.
3. **Model pluralism** — the checks deliberately use other models than the writer, to get a real outside view instead of
   self-confirmation.

## In scope

- Texts people need written: letters, emails, articles, summaries, reports and the like, from the request and the files
- Multi-user hosting on a self-owned Docker server: DB-based user management (BCrypt), per-user isolation of orders and runs, admin override via explicit toggles
- Web UI (Blazor Server) for orders: start page, "My work", order page, "About me"
- MCP server interface so that AI assistants (Claude Desktop, Claude Code, custom agents) can place orders and fetch results, with a static bearer token or the self-hosted OAuth 2.1
- LLM providers: the command-line tools Claude Code and Codex, signed in with own accounts, through a local proxy; further OpenAI-compatible providers such as OpenRouter exist, but writing runs through the proxy (D-066). The checks deliberately run on other models than the writer
- Files as sources: upload of PDF, Word, Markdown or plain text, also through an upload ticket from an AI assistant; the writer gets them in full text
- Web research for orders without files, where switched on (Tavily), shown as passages of other websites, not as checked facts
- Fire-and-forget workflow: place an order, check later, fetch the result. **Before** the writing, at most three short questions with ready-made answers to click are allowed, always skippable (D-068); no intervention while the text is written
- Changing a finished text as a new version of the same order, with comparison of the versions
- Download of every order's text as Word, PDF or Markdown, on the order page and over MCP (`export_order`, D-070, D-075)
- Persistent order and run history; continuing a failed or aborted run of the standard way (D-046)

## Out of scope (for now)

- True multi-tenancy / tenant isolation (the system is multi-user but single-tenant: one admin, shared configuration; only orders and runs are isolated per user)
- Public access without auth
- Human-in-the-loop while the text is written. Only **before** the writing may the Atelier ask at most three short questions with click answers, and only when the answer cannot be taken from the request, the files or the profile, cannot be researched and cannot reasonably be assumed, the user can know it, and it changes the result substantially (D-068)
- Building teams of AI roles, custom pipelines or tool use by models
- Learning across orders, a per-user memory beyond "About me", and a knowledge base (vector search)
- Finishing steps that transform a text or send it somewhere else
- Mobile apps or native clients
- Commercial hosting, billing, invoicing
- Domain-specific database connectors (for example legal databases)

## Target users

Several named user accounts, managed by an admin. The application is not multi-tenant (no separation of configuration between tenants), but each user sees only their own orders and runs; the admin can optionally view everything. Authentication is mandatory because the app is reachable over the public internet (server hosting).

## Hosting environment

- Docker containers on a self-owned server (Docker Compose): web app, cli-proxy, Postgres and a backup service
- A reverse proxy (Traefik / Nginx) handles TLS termination
- API keys, connection strings, auth secrets via environment variables / Docker secrets
- Persistent data in Postgres; uploaded files on a volume

## Success criterion for the skeleton

> **Historical.** This was the definition of the minimal goal at the start of the project; it was met in May 2026. It names
> two fixed reviewers, which is not how the product works today (see [Vision](#vision)).

The walking skeleton (see [03-walking-skeleton-plan.md](03-walking-skeleton-plan.md)) was successful when:

1. A job with a plain text briefing could be started via the web UI.
2. The pipeline ran with Anthropic as executor and two fixed reviewers (e.g. with an OpenAI model) without the user having to intervene.
3. A live status was visible in the UI during the run (current phase, iteration, findings).
4. After completion the final text was shown in the UI.
5. The same workflow also worked via the MCP server (submit a job, query status, fetch the result via MCP tools).
6. The whole application was deployable as a Docker container and connected to Postgres.
