# Geef.Atelier — Project documentation

*[Deutsch](README_de.md) · **English***

This is the continuously maintained documentation for **Geef.Atelier**: it turns a short request into a finished text and says what was checked, what is still open and what was assumed. It is built on the [Geef SDK](https://github.com/chr0mcom/geef).

> **Version 2 is a major update.** [What changed in version 2](16-version-2.md) · [version 1, with crews](https://github.com/chr0mcom/geef-atelier/tree/v1.0.0)

**New here?** Start with the guide for your role:

| Guide | For |
|---|---|
| [12-user-guide.md](12-user-guide.md) | Everyone who writes with Atelier in the browser |
| [13-ai-assistant-guide.md](13-ai-assistant-guide.md) | People who use Atelier from an AI assistant (MCP) |
| [14-admin-guide.md](14-admin-guide.md) | Administrators: accounts, switches, providers |
| [15-developer-guide.md](15-developer-guide.md) | Developers and hosts: map of the system, tests, rules |

The running application also explains itself on the public page `/how-it-works`, which follows the current switches.

The documentation evolves alongside the brainstorming and is updated after every decision round. When a topic is not yet worked out, that is stated explicitly in the respective document.

## Document structure

| File | Content | Type |
|---|---|---|
| [12-user-guide.md](12-user-guide.md) … [15-developer-guide.md](15-developer-guide.md) | Guides per audience (see above) | Living |
| [16-version-2.md](16-version-2.md) | What changed in version 2 (major update), how to move over, where version 1 is | Living |
| [01-vision-and-scope.md](01-vision-and-scope.md) | What the project is, what it is not, for whom, in what environment | Living |
| [02-architecture.md](02-architecture.md) | Layers, components, data flow, DB schema; run safeguards, configuration switches, end-to-end harness (D-067) | Living |
| [03-walking-skeleton-plan.md](03-walking-skeleton-plan.md) | The original 10-step build plan (historical; skeleton completed) | Historical |
| [04-mcp-integration.md](04-mcp-integration.md) | MCP server: tools, auth (bearer + OAuth 2.1), transport | Living |
| [05-decisions-log.md](05-decisions-log.md) | Chronological, append-only log of all architecture decisions (D-001 ff.) | Historical |
| [06-reviewer-calibration.md](06-reviewer-calibration.md) | Severity taxonomy and convergence policy; the parts about the old review loop are marked as historical (removed in D-091) | Living |
| [07-design-system.md](07-design-system.md) | Theme system, typography, component inventory (as of design translation PS-3) | Living |
| [08-crew-system.md](08-crew-system.md) | What remains of the crew system: the one setup in code, crew snapshot, model matrix, reviewers of the direct path; the removed parts are listed (D-091) | Living |
| [09-endpoint-reference.md](09-endpoint-reference.md) | Reference of all externally reachable HTTP endpoints (MCP, OAuth, Web/Account) | Living |
| [10-tool-system.md](10-tool-system.md) | Short note: the tool system was removed in stage 8d (D-091); the allow list for environment variables still applies | Note |
| [11-specialization-packs.md](11-specialization-packs.md) | Short note: specialization packs were removed in stage 8d (D-091) | Note |
| [ops/atelier-smoke.md](ops/atelier-smoke.md), [ops/atelier-watch.md](ops/atelier-watch.md) | Operations: mini run after deploy, watcher for stuck runs (German only) | Living |

> **Internal working directories (not in the public repository):**
> `docs/prompts/` (step-specific build prompts) and `docs/reports/` (persistent
> completion reports per build step) are deliberately excluded via `.gitignore` and
> exist only in the local working copy. The historical documents name some of these
> files (`reports/…`, `prompts/…`) to show where a statement comes from. They are
> plain text, not links, because they are not part of this repository.

## Development workflow

Changes are built in phases (grounding, execution, evaluation, finalize) with independent reviews; the build prompts and reports
of that process are the unpublished working folders named above.

## Status

- **Phase:** in production. Atelier writes every text on one way: the order with the direct path (D-068 to D-077). The variant "Extra careful" was removed after its measurement (second addendum to D-087). The older crew system and everything only it used (workshop, tools, learning, knowledge base, finishing steps, advisors, packs) were removed in stage 8d (D-091).
- **Last updated:** 2026-11-28
- **Current state & next steps:** see the [project README](../README.md) (implementation status) and the [decisions log](05-decisions-log.md) (chronological decision history, most recently D-092: the database was cleaned up into one baseline migration, after stage 8d, D-091, removed the rest of the older way of working). Documents mark what was removed.

## Conventions

- Documentation is bilingual: the English file (`X.md`) is the primary version, the German original is preserved as `X_de.md`; both are kept in sync
- Code, code comments and XML-doc comments in English (matching the Geef SDK)
- Decisions are recorded in the decisions log before they flow into other documents
- Living document: no version numbers, instead a "Last updated" field per file
