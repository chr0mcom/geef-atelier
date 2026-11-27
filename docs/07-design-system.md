# 07 — Design system

*[Deutsch](07-design-system_de.md) · **English***

*Last updated: 2026-11-27 (D-091: parts removed with the cleanup taken out, names of removed setups; before that 2026-05-17, mock-stub status brought up to the feature state of that time; tokens/themes/typography unchanged since the PS-3 design translation)*

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
| `deterministic-checks` | `Automatic checks` |
| `source-fact-check` | `Fact check` |
| All others | Unchanged (fallback) |

A setup without a display name shows its stored name (`GetTemplateDisplay`).

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
| Welcome stats | `—` in stat tiles | Removed with the dashboard (D-091) |
| Cost display (RunDetail) | `$ —` | ✅ Implemented (cost tracking, `Runs.TotalCostEur` / `IterationActorCosts`) |
| Account functions | Disabled | ✅ Implemented: multi-user (`/admin/users`), connected clients (`/account/connected-clients`) |
| "Profile" entry in `UserMenu` | Disabled | ⏳ Still a stub (`.coming-soon`) — the separate profile menu item itself is not yet wired up |
| Export button (Manuscript) | Disabled | ⏳ Still a stub — DOCX/PDF *export* remains out of scope (see [01-vision-and-scope.md](01-vision-and-scope.md)) |

The CSS class `.coming-soon` (dimmed color, tooltip) is still used in two places: the export button (`Manuscript.razor`) and the "Profile" entry in `UserMenu.razor`.

## CSS architecture

- **Global stylesheet:** `wwwroot/atelier.css` — a large global stylesheet (CSS custom properties, three palettes, all component basics); grows with new features
- **Scoped CSS:** `.razor.css` per component for layout overrides
- **Bootstrap:** fully removed in PS-3
- **No inline styling** in components (except minimally in pages)

## Landing page (D-053, reworked in D-084)

**Route:** `/` — `Landing.razor`, `[AllowAnonymous]`, Static SSR (no `@rendermode`), `LandingLayout.razor`. It reads the same
settings as "How it works" through `HowItWorksFeatures` (`FeaturesFor(viewer)`) and passes the result to the sections, so asking,
changing, web research and the fact check are only described when they are on for the viewer, and every number comes from the
settings.

**Sections (in order):** `LandingNav` (signed in: My work / Write something), `LandingHero`, `LandingExample` (made-up example as an
annotated proof sheet, built from the fixed sentences of `OrderCheckLines` and the leads of `HowItWorks`), `LandingSteps` (four or
five steps of the standard way, "one more try, at most"), `LandingCapabilities` (six points and "What
Atelier does not do"), `LandingAudiences` (#for-whom), `LandingPrivacy`, `LandingFilm` (video unchanged until a new one exists; the
text next to it says that it shows an earlier version), `LandingClosing`, `LandingFooter`. Inline SVGs in
`LandingIllustrations.razor` via `Name`.

**CSS:** `wwwroot/atelier-landing.css`, tokens of the three palettes only. Prefixes: `lp-*`, `ex-*`/`proof-*` (example),
`lp-flow*` (steps), `cap`, `aud`. Only one orchestrated motion (the steps) besides the press and the film.

**JS:** `wwwroot/js/landing.js` — plain IIFE. Steps: one pass over `.lp-flow-step` (`is-active`/`has-passed`), the arc
`.lp-flow-retry` shows while the check step is active. `prefers-reduced-motion` marks every step at once. Scroll reveal only for
the film.

**Tests:** `Web/HowItWorks/LandingWordsTests` (words, names, promises), `LandingSwitchesTests` (switch states, numbers, heading
order), `LandingLinksTests` (every link resolves), `Web/E2E/LandingFlowTests` (light/dark × desktop/phone: no sideways scrolling,
no layout shift, contrast of every text, keyboard, steps).

**Stub pages:** `Components/Pages/Public/` — `[AllowAnonymous]` Static SSR pages using `LandingLayout`: `/pricing`, `/docs`,
`/self-host`, `/status`, `/changelog`, `/contact`, `/imprint`, `/privacy`, `/terms`.

**Asset injection:** `App.razor` injects `atelier-landing.css` for the public pages and `js/landing.js` only for `Path == "/"`.
