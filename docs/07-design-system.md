# 07 — Design system

*[Deutsch](07-design-system_de.md) · **English***

*Last updated: 2026-05-17 (mock-stub status brought up to the current feature state; tokens/themes/typography unchanged since the PS-3 design translation)*

The Atelier design system was ported from the mockup (`docs/design/atelier-mockups/`) into the Blazor Server app (PS-3). This guide describes tokens, themes, typography, the component inventory and the mock stubs for future development.

## Theme system

### Palettes

Three CSS palettes via the `html.palette-{name}` class:

| Name | Class | Character |
|------|--------|-----------|
| Vellum | `palette-vellum` | Light, parchment tones — **production default** |
| Noir | `palette-noir` | Dark, ink on paper |
| Petrol | `palette-petrol` | Cool teal dark blue |

**Default:** `palette-vellum` (set via `App.razor` from the `Atelier.Theme` cookie; without a cookie it defaults to Vellum).

### Theme cookie

| Property | Value |
|------------|------|
| Name | `Atelier.Theme` |
| Values | `vellum`, `noir`, `petrol` |
| MaxAge | 365 days |
| HttpOnly | `false` (JS interop reads/sets it) |
| SameSite | Strict |
| Secure | Production only |
| Logout | Does NOT delete the cookie |

### Theme-switch mechanics

Primary: `window.atelier.setTheme(name)` (JS interop) — switches instantly without a page reload, sets the cookie.

Fallback: `POST /settings/theme` (form POST) — for no-JS scenarios, redirect to the referer.

### Server-side rendering

`App.razor` reads the `Atelier.Theme` cookie via `IHttpContextAccessor` and renders `<html class="palette-@Theme">` server-side — no flash of wrong theme.

## Typography

Three self-hosted variable fonts in `wwwroot/fonts/` (OFL-licensed):

| Family | CSS variable | Usage | Weight range |
|---------|-------------|-----------|---------------|
| Newsreader | `var(--font-display)` | Headings, manuscript | 200–800 |
| Geist | `var(--font-ui)` | UI text, labels | 100–900 |
| JetBrains Mono | `var(--font-mono)` | Code, IDs, mono tags | 100–800 |

No external font requests — GDPR-compliant.

## Severity mapping

Taken from D-025 (PS-2); UI rendering implemented in PS-3:

| Atelier enum | CSS class | Shape mark |
|-------------|-----------|-----------|
| Critical | `severity critical` | Triangle |
| Major | `severity major` | Diamond |
| Minor | `severity minor` | Circle |
| Info | `severity info` | Bar |

## Status badge

| RunStatus | CSS class | Glyph shape | data-status |
|-----------|-----------|------------|------------|
| Pending | `status pending` | Ring (border) | `Pending` |
| Running | `status running` | Pulse animation | `Running` |
| Completed | `status completed` | Checkmark | `Completed` |
| Failed | `status failed` | X | `Failed` |
| Aborted | `status aborted` | Diagonal | `Aborted` |

## Reviewer display mapping

Code class names stay unchanged (no breaking changes in persistence/schema):

| Code name | UI display |
|-----------|-----------|
| `BriefingTreueReviewer` | `BriefingFidelity` |
| `KlarheitReviewer` | `Clarity` |
| All others | Unchanged (fallback) |

Implementation: `Geef.Atelier.Web.Display.ReviewerDisplay.ToDisplay(name)`.

## Component inventory

### New components (PS-3)

| File | Description |
|-------|-------------|
| `Brand.razor` | Crest + wordmark (geef.atelier) |
| `ThemeSwitcher.razor` | Three theme buttons with data-testid |
| `GlobalPulse.razor` | Running-runs indicator (stub) |
| `ReconnectBanner.razor` | Blazor error UI with reconnect text |
| `RunRow.razor` | Grid row in the runs list |
| `FilterPill.razor` | Status filter button with count |
| `ProgressPip.razor` | Iteration-progress pip |
| `Press.razor` | Pipeline stage visualization (3 seals + 2 ribbons) |
| `Manuscript.razor` | Final manuscript with copy button |
| `Icons/Icon*.razor` | 16 hairline SVG icons |

### Updated components

| File | Change |
|-------|---------|
| `StatusBadge.razor` | Shape glyph, data-status attribute |
| `SeverityBadge.razor` | Shape mark via clip-path |
| `IterationPanel.razor` | Collapse, severity pills, evolution footer |
| `FindingItem.razor` | ReviewerDisplay mapping, resolved badge |
| `MainLayout.razor` | Sticky nav, no sidebar |
| `NavMenu.razor` | Flat links, no Bootstrap |
| `UserMenu.razor` | Dropdown with ThemeSwitcher |

## Mock stubs (.coming-soon)

This list was, at the PS-3 design translation, the state of the not-yet-connected UI elements. Current status (May 2026):

| Element | Stub back then | State today |
|---------|----------------|----------------|
| Welcome stats | `—` in stat tiles | ✅ Implemented (`IRunService.GetWelcomeStatsAsync`, per-user isolated) |
| Cost display (RunDetail) | `$ —` | ✅ Implemented (cost tracking, `Runs.CostTotal` / `IterationActorCosts`) |
| Account functions | Disabled | ✅ Implemented: multi-user (`/admin/users`), connected clients (`/account/connected-clients`) |
| "Profile" entry in `UserMenu` | Disabled | ⏳ Still a stub (`.coming-soon`) — the separate profile menu item itself is not yet wired up |
| Export button (Manuscript) | Disabled | ⏳ Still a stub — DOCX/PDF *export* remains out of scope (see [01-vision-and-scope.md](01-vision-and-scope.md)) |

The CSS class `.coming-soon` (dimmed color, tooltip) is still used in two places: the export button (`Manuscript.razor`) and the "Profile" entry in `UserMenu.razor`.

## CSS architecture

- **Global stylesheet:** `wwwroot/atelier.css` — a large global stylesheet (CSS custom properties, three palettes, all component basics); grows with new features
- **Scoped CSS:** `.razor.css` per component for layout overrides
- **Bootstrap:** fully removed in PS-3
- **No inline styling** in components (except minimally in pages)
