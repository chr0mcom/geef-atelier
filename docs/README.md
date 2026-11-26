# Geef.Atelier — Project documentation

*[Deutsch](README_de.md) · **English***

This is the continuously maintained documentation for **Geef.Atelier**: it turns a short request into a finished text and says what was checked, what is still open and what was assumed. It is built on the [Geef SDK](https://github.com/chr0mcom/geef).

**New here?** Start with the guide for your role:

| Guide | For |
|---|---|
| [12-user-guide.md](12-user-guide.md) | Everyone who writes with Atelier in the browser |
| [13-ai-assistant-guide.md](13-ai-assistant-guide.md) | People who use Atelier from an AI assistant (MCP) |
| [14-admin-guide.md](14-admin-guide.md) | Administrators: accounts, switches, the older workshop |
| [15-developer-guide.md](15-developer-guide.md) | Developers and hosts: map of the system, tests, rules |

The running application also explains itself on the public page `/how-it-works`, which follows the current switches.

The documentation evolves alongside the brainstorming and is updated after every decision round. When a topic is not yet worked out, that is stated explicitly in the respective document.

## Document structure

| File | Content | Type |
|---|---|---|
| [12-user-guide.md](12-user-guide.md) … [15-developer-guide.md](15-developer-guide.md) | Guides per audience (see above) | Living |
| [01-vision-and-scope.md](01-vision-and-scope.md) | What the project is, what it is not, for whom, in what environment | Living |
| [02-architecture.md](02-architecture.md) | Layers, components, data flow, DB schema; run safeguards, configuration switches, end-to-end harness (D-067) | Living |
| [03-walking-skeleton-plan.md](03-walking-skeleton-plan.md) | The original 10-step build plan (historical; skeleton completed) | Historical |
| [04-mcp-integration.md](04-mcp-integration.md) | MCP server: tools, auth (bearer + OAuth 2.1), transport | Living |
| [05-decisions-log.md](05-decisions-log.md) | Chronological, append-only log of all architecture decisions (D-001 ff.) | Historical |
| [06-reviewer-calibration.md](06-reviewer-calibration.md) | Severity taxonomy and convergence-policy standard for reviewers | Living |
| [07-design-system.md](07-design-system.md) | Theme system, typography, component inventory (as of design translation PS-3) | Living |
| [08-crew-system.md](08-crew-system.md) | Crew/profile/template system, system profiles, advisor passes – the older way ("Workshop", "Extra careful") | Living |
| [09-endpoint-reference.md](09-endpoint-reference.md) | Reference of all externally reachable HTTP endpoints (MCP, OAuth, Web/Account) | Living |
| [10-tool-system.md](10-tool-system.md) | Central tool catalogue + agentic tool-use (Pull) + grounding (Push) + MCP client (D-060); actual state: Pull never wired and frozen, environment-variable allow list (D-067) | Living |
| [11-specialization-packs.md](11-specialization-packs.md) | Generic actors + reusable scoped specialization packs (D-061) | Living |
| [ops/atelier-smoke.md](ops/atelier-smoke.md), [ops/atelier-watch.md](ops/atelier-watch.md) | Operations: mini run after deploy, watcher for stuck runs (German only) | Living |

> **Internal working directories (not in the public repository):**
> `docs/prompts/` (step-specific build prompts) and `docs/reports/` (persistent
> completion reports per build step) are deliberately excluded via `.gitignore` and
> exist only in the local working copy. References to `reports/…` or `prompts/…`
> in the historical documents serve provenance and are not resolvable on GitHub —
> this is intentional.

## Binding development workflow

The cross-cutting process rules live in the parent infrastructure repository under `/srv/docker/docs/`, foremost **`geef-workflow.md`** — the canonical instruction to Claude Code on *how* things are built (outside this repository, hence not linked here). It defines the four phases (Grounding / Execution / Evaluation / Finalize), the five reviewers and the advisor consultations. All build prompts in `docs/prompts/` reference it and only add the step-specific scope. The other files in that folder (e.g. `docker-deployment.md`, `contact-protection.md`) are likewise binding and must be observed.

## Status

- **Phase:** in production. Since the rebuild of November 2026 (D-068 to D-077) the default is the order with the direct path; the older crew system (with its extensions such as advisor passes, grounding, finalizers, Template Studio and the paused learning loop) stays available as "Workshop" and "Extra careful".
- **Last updated:** 2026-11-26
- **Current state & next steps:** see the [project README](../README.md) (implementation status) and the [decisions log](05-decisions-log.md) (chronological decision history, most recently D-077 "completion of the rebuild"). The rebuild following D-068 ends with stage 6 and the closing round 8a (D-077): stages 1b, 7, 8 (apart from 8a) and 9 are not built, and the older crew, tool and learning systems stay in place until the removal trigger in D-077 is met; documents mark parts that are frozen or due for removal.

## Conventions

- Documentation is bilingual: the English file (`X.md`) is the primary version, the German original is preserved as `X_de.md`; both are kept in sync
- Code, code comments and XML-doc comments in English (matching the Geef SDK)
- Decisions are recorded in the decisions log before they flow into other documents
- Living document: no version numbers, instead a "Last updated" field per file
