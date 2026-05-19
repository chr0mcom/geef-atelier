# Vision and Scope

*[Deutsch](01-vision-and-scope_de.md) · **English***

*Last updated: 19 May 2026 (scope aligned with Finalizer-Foundation and Run-Resume)*

## Vision

**Geef.Atelier** is a text manufactory: an atelier of specialized AI roles (executor, reviewer, optionally advisor) staffed differently depending on the piece of work. The difference from a classic "GPT wrapper" is not the UX or the price, but that the machine *decides how it works before it works* (adaptive crew composition) and *makes transparent what it does while it works* (process visibility via an iteration and findings trail).

The project is the productive application of the [Geef SDK](https://github.com/chr0mcom/geef): it uses the GEEF pipeline pattern (Grounding / Execution / Evaluation / Finalize) and the extensions (convergence policies, evaluation strategies, advisor pattern, event sink, middleware) to produce texts of the highest quality.

## Guiding principles

1. **Adaptive crew composition** — the classifier recognizes the text type and assembles the matching crew from a tagged reviewer/advisor pool. Specialization happens through data (reviewer profiles, crew templates), not code branches.
2. **Process transparency** — every run leaves a complete trail: all iterations, all findings, all advisor consultations. Trust comes from traceability.
3. **Model pluralism** — reviewers deliberately use models other than the executor's, to create a genuine outside perspective instead of self-confirmation.

## In scope

- Generic pipeline for arbitrary text types (legal briefs, technical articles, marketing, academic texts, letters, poems, …)
- Multi-user hosting on a self-owned Docker server: DB-based user management (BCrypt), per-user isolated run visibility, admin override via explicit toggles
- Web UI (Blazor Server) for submitting jobs, live status, picking up results
- MCP server interface so external AI clients (Claude Desktop, Claude Code, custom agents) can submit jobs and fetch results — auth via static bearer token or self-hosted OAuth 2.1
- Multi-provider LLM support: OpenRouter (pay-per-token) plus subscription CLIs (Claude Code, Codex) via a local proxy; reviewers/advisors deliberately on foreign models
- Source ingestion in several forms: file upload (PDF, DOCX, TXT, MD), URLs, free-text briefing, style reference texts; semantic knowledge base (vector-store RAG) and run-local attachments
- Fire-and-forget workflow: start a job, check status later, fetch the result at the end — no human-in-the-loop interventions during the run
- Persistent run history with a complete iteration trail
- Run resumption: continue a failed or aborted run from its last draft text (seed mode) or with a fresh pipeline (clean mode) — D-046
- File export in formats md / html / pdf / docx / txt / json via the Finalizer pipeline (D-044); the five Finalizer profile types (FileExport, MetadataEnrich, ExternalSink, Transform) are implemented as part of the crew system

## Out of scope (for now)

- True multi-tenancy / tenant isolation (the system is multi-user but single-tenant: one admin, shared configuration; only run visibility is isolated per user)
- Public access without auth
- Human-in-the-loop between iterations (deliberately not implemented; fire-and-forget remains the model)
- Mobile apps or native clients
- Commercial hosting, billing, invoicing
- True memory-backed advisors with cross-run learning
- Domain-specific database connectors (e.g. legal databases such as dejure.org or Beck-Online) — optional later
- Direct "Export" button in the briefing UI: a one-click export button accessible from the run/briefing UI is not implemented. (File export in md/html/pdf/docx/txt/json *is* implemented, but only via the Finalizer pipeline configured in a crew template — see "In scope" above.)

## Target users

Several named user accounts, managed by an admin. The application is not multi-tenant (no tenant separation of configuration or crew data), but each user sees only their own runs; the admin can optionally view everything. Authentication is mandatory because the app is reachable over the public internet (server hosting).

## Hosting environment

- Docker container on a self-owned server
- Postgres is already present as a database service on the server — the project uses this existing Postgres instance, no separate DB container needed (except for local development)
- A reverse proxy (Traefik / Nginx) handles TLS termination
- API keys, connection strings, auth secrets via environment variables / Docker secrets
- Persistent data storage in Postgres (incl. pgvector for the semantic knowledge base); uploaded sources are kept indexed in the database

## Success criterion for the skeleton

> **Status:** This skeleton success criterion has been fully met since May 2026; the app runs in production. The extensions listed below as "afterwards" (source upload/RAG, crew composition, advisor passes) have since been shipped. The following list is retained as the original definition of the minimal goal.

The walking skeleton (see [03-walking-skeleton-plan.md](03-walking-skeleton-plan.md)) is successful when:

1. A job with a plain text briefing can be started via the web UI.
2. The pipeline runs with Anthropic as executor and two fixed reviewers (e.g. with an OpenAI model) without the user having to intervene.
3. A live status is visible in the UI during the run (current phase, iteration, findings).
4. After completion the final text is shown in the UI.
5. The same workflow also works via the MCP server (submit a job, query status, fetch the result via MCP tools).
6. The whole application is deployable as a Docker container and connects to the existing Postgres instance.

Everything else (source upload, classifier, dynamic crew, advisor, multi-format export) are extensions *after* the skeleton.
