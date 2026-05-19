# Geef.Atelier — Project documentation

*[Deutsch](README_de.md) · **English***

This is the continuously maintained documentation for **Geef.Atelier** — a text manufactory built on the [Geef SDK](https://github.com/chr0mcom/geef) that puts its GEEF pipeline (Grounding / Execution / Evaluation / Finalize) into productive use to produce high-quality texts of any kind.

The documentation evolves alongside the brainstorming and is updated after every decision round. When a topic is not yet worked out, that is stated explicitly in the respective document.

## Document structure

| File | Content | Type |
|---|---|---|
| [01-vision-and-scope.md](01-vision-and-scope.md) | What the project is, what it is not, for whom, in what environment | Living |
| [02-architecture.md](02-architecture.md) | Layers, components, data flow, DB schema | Living |
| [03-walking-skeleton-plan.md](03-walking-skeleton-plan.md) | The original 10-step build plan (historical; skeleton completed) | Historical |
| [04-mcp-integration.md](04-mcp-integration.md) | MCP server: tools, auth (bearer + OAuth 2.1), transport | Living |
| [05-decisions-log.md](05-decisions-log.md) | Chronological, append-only log of all architecture decisions (D-001 ff.) | Historical |
| [06-reviewer-calibration.md](06-reviewer-calibration.md) | Severity taxonomy and convergence-policy standard for reviewers | Living |
| [07-design-system.md](07-design-system.md) | Theme system, typography, component inventory (as of design translation PS-3) | Living |
| [08-crew-system.md](08-crew-system.md) | Crew/profile/template system, system profiles, advisor passes | Living |
| [09-endpoint-reference.md](09-endpoint-reference.md) | Reference of all externally reachable HTTP endpoints (MCP, OAuth, Web/Account) | Living |

> **Internal working directories (not in the public repository):**
> `docs/prompts/` (step-specific build prompts) and `docs/reports/` (persistent
> completion reports per build step) are deliberately excluded via `.gitignore` and
> exist only in the local working copy. References to `reports/…` or `prompts/…`
> in the historical documents serve provenance and are not resolvable on GitHub —
> this is intentional.

## Binding development workflow

The cross-cutting process rules live in the parent infrastructure repository under `/srv/docker/docs/`, foremost **`geef-workflow.md`** — the canonical instruction to Claude Code on *how* things are built (outside this repository, hence not linked here). It defines the four phases (Grounding / Execution / Evaluation / Finalize), the five reviewers and the advisor consultations. All build prompts in `docs/prompts/` reference it and only add the step-specific scope. The other files in that folder (e.g. `docker-deployment.md`, `contact-protection.md`) are likewise binding and must be observed.

## Status

- **Phase:** Walking skeleton completed; in production at `https://geef.stefan-bechtel.de`. Numerous post-skeleton extensions shipped (crew system, advisor passes, grounding/vector-store RAG, run attachments, cost tracking, Template Studio, domain templates, MCP OAuth 2.1, multi-user, run-user isolation).
- **Last updated:** 19 May 2026
- **Current state & next steps:** see the [project README](../README.md) (implementation status) and the [decisions log](05-decisions-log.md) (chronological decision history, most recently D-044).

## Conventions

- Documentation is bilingual: the English file (`X.md`) is the primary version, the German original is preserved as `X_de.md`; both are kept in sync
- Code, code comments and XML-doc comments in English (matching the Geef SDK)
- Decisions are recorded in the decisions log before they flow into other documents
- Living document: no version numbers, instead a "Last updated" field per file
