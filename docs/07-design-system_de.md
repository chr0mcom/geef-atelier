# 07 — Design-System

*[English](07-design-system.md) · **Deutsch***

*Letzte Aktualisierung: 2026-05-17 (Mock-Stub-Status auf aktuellen Feature-Stand gebracht; Tokens/Themes/Typografie unverändert seit PS-3-Design-Translation)*

Das Atelier-Design-System ist aus dem Mockup (`docs/design/atelier-mockups/`) in die Blazor-Server-App übertragen worden (PS-3). Dieser Leitfaden beschreibt Tokens, Themes, Typografie, Komponenten-Inventar und Mock-Stubs für zukünftige Entwicklung.

## Theme-System

### Paletten

Drei CSS-Paletten via `html.palette-{name}`-Klasse:

| Name | Klasse | Charakter |
|------|--------|-----------|
| Vellum | `palette-vellum` | Hell, Pergament-Töne — **Production-Default** |
| Noir | `palette-noir` | Dunkel, Tinte auf Papier |
| Petrol | `palette-petrol` | Kühles Teal-Dunkelblau |

**Default:** `palette-vellum` (gesetzt via `App.razor` aus Cookie `Atelier.Theme`; ohne Cookie default Vellum).

### Theme-Cookie

| Eigenschaft | Wert |
|------------|------|
| Name | `Atelier.Theme` |
| Werte | `vellum`, `noir`, `petrol` |
| MaxAge | 365 Tage |
| HttpOnly | `false` (JS-Interop liest/setzt) |
| SameSite | Strict |
| Secure | Nur in Production |
| Logout | Löscht Cookie NICHT |

### Theme-Wechsel-Mechanik

Primär: `window.atelier.setTheme(name)` (JS-Interop) — wechselt sofort ohne Page-Reload, setzt Cookie.

Fallback: `POST /settings/theme` (Form-POST) — für No-JS-Szenarios, Redirect zum Referer.

### Server-seitiges Rendering

`App.razor` liest `Atelier.Theme`-Cookie via `IHttpContextAccessor` und rendert `<html class="palette-@Theme">` server-seitig — kein Flash-of-Wrong-Theme.

## Typografie

Drei selbst-gehostete Variable-Fonts in `wwwroot/fonts/` (OFL-lizenziert):

| Familie | CSS-Variable | Verwendung | Gewichts-Range |
|---------|-------------|-----------|---------------|
| Newsreader | `var(--font-display)` | Überschriften, Manuscript | 200–800 |
| Geist | `var(--font-ui)` | UI-Text, Labels | 100–900 |
| JetBrains Mono | `var(--font-mono)` | Code, IDs, Mono-Tags | 100–800 |

Keine externen Font-Requests — DSGVO-konform.

## Severity-Mapping

Aus D-025 (PS-2) übernommen; UI-Darstellung in PS-3 implementiert:

| Atelier-Enum | CSS-Klasse | Shape-Mark |
|-------------|-----------|-----------|
| Critical | `severity critical` | Dreieck |
| Major | `severity major` | Raute |
| Minor | `severity minor` | Kreis |
| Info | `severity info` | Querbalken |

## Status-Badge

| RunStatus | CSS-Klasse | Glyph-Shape | data-status |
|-----------|-----------|------------|------------|
| Pending | `status pending` | Ring (border) | `Pending` |
| Running | `status running` | Puls-Animation | `Running` |
| Completed | `status completed` | Häkchen | `Completed` |
| Failed | `status failed` | X | `Failed` |
| Aborted | `status aborted` | Diagonal | `Aborted` |

## Reviewer-Display-Mapping

Code-Klassen bleiben unverändert (keine Breaking Changes in Persistenz/Schema):

| Code-Name | UI-Anzeige |
|-----------|-----------|
| `BriefingTreueReviewer` | `BriefingFidelity` |
| `KlarheitReviewer` | `Clarity` |
| Alle anderen | Unverändert (Fallback) |

Implementierung: `Geef.Atelier.Web.Display.ReviewerDisplay.ToDisplay(name)`.

## Komponenten-Inventar

### Neue Komponenten (PS-3)

| Datei | Beschreibung |
|-------|-------------|
| `Brand.razor` | Crest + Wordmark (geef.atelier) |
| `ThemeSwitcher.razor` | Drei Theme-Buttons mit data-testid |
| `GlobalPulse.razor` | Laufende-Runs-Anzeige (Stub) |
| `ReconnectBanner.razor` | Blazor-Error-UI mit Reconnect-Text |
| `RunRow.razor` | Grid-Zeile in der Runs-Liste |
| `FilterPill.razor` | Status-Filter-Button mit Count |
| `ProgressPip.razor` | Iterations-Fortschritts-Pip |
| `Press.razor` | Pipeline-Stage-Visualisierung (3 Seals + 2 Ribbons) |
| `Manuscript.razor` | Finales Manuskript mit Copy-Button |
| `Icons/Icon*.razor` | 16 hairline SVG-Icons |

### Aktualisierte Komponenten

| Datei | Änderung |
|-------|---------|
| `StatusBadge.razor` | Shape-Glyph, data-status Attribut |
| `SeverityBadge.razor` | Shape-Mark via Clip-Path |
| `IterationPanel.razor` | Collapse, Severity-Pills, Evolution-Footer |
| `FindingItem.razor` | ReviewerDisplay-Mapping, Resolved-Badge |
| `MainLayout.razor` | Sticky Nav, kein Sidebar |
| `NavMenu.razor` | Flat Links, kein Bootstrap |
| `UserMenu.razor` | Dropdown mit ThemeSwitcher |

## Mock-Stubs (.coming-soon)

Diese Liste war zur PS-3-Design-Translation der Stand der noch nicht angebundenen
UI-Elemente. Aktueller Status (Mai 2026):

| Element | Damaliger Stub | Heutiger Stand |
|---------|----------------|----------------|
| Welcome-Stats | `—` in Stat-Tiles | ✅ Implementiert (`IRunService.GetWelcomeStatsAsync`, pro Nutzer isoliert) |
| Cost-Anzeige (RunDetail) | `$ —` | ✅ Implementiert (Cost-Tracking, `Runs.CostTotal` / `IterationActorCosts`) |
| Account-Funktionen | Disabled | ✅ Implementiert: Multi-User (`/admin/users`), verbundene Clients (`/account/connected-clients`) |
| „Profile"-Eintrag im `UserMenu` | Disabled | ⏳ Weiterhin Stub (`.coming-soon`) — der separate Profil-Menüpunkt selbst ist noch nicht angebunden |
| Export-Button (Manuscript) | Disabled | ⏳ Weiterhin Stub — DOCX/PDF-*Export* bleibt out of scope (siehe [01-vision-and-scope.md](01-vision-and-scope_de.md)) |

Die CSS-Klasse `.coming-soon` (gedämpfte Farbe, Tooltip) wird noch an zwei Stellen
verwendet: dem Export-Button (`Manuscript.razor`) und dem „Profile"-Eintrag im
`UserMenu.razor`.

## CSS-Architektur

- **Globales Stylesheet:** `wwwroot/atelier.css` — umfangreiches globales Stylesheet (CSS Custom Properties, drei Paletten, alle Komponenten-Basics); wächst mit neuen Features mit
- **Scoped CSS:** `.razor.css` pro Komponente für Layout-Overrides
- **Bootstrap:** vollständig entfernt in PS-3
- **Kein Inline-Styling** in Komponenten (außer minimal in Pages)

## Landing-Page (D-053)

**Route:** `/` — `Landing.razor`, `[AllowAnonymous]`, Static SSR (kein `@rendermode`), `LandingLayout.razor`.

**CSS:** `wwwroot/atelier-landing.css` — 1:1-Port des React-Prototyp-`landing.css`. Alle 27 referenzierten CSS-Tokens existierten bereits in allen drei Paletten (`atelier.css`); keine Tokens hinzugefügt.

**JS:** `wwwroot/js/landing.js` — reines IIFE, kein Blazor-Interop. `IntersectionObserver` für Scroll-Reveal (`.lp-reveal` → `.in`). GEEF-Flow-Choreografie: zweiter Observer + `setTimeout`-Sequenz G→E→E-Schleife→F via `is-active`/`has-passed`-Phasen-Klassen. `prefers-reduced-motion` setzt `body[data-static="1"]`, CSS stoppt alle Keyframe-Animationen.

**Komponenten:** `Components/UI/Landing/` — neun Sektions-Komponenten (`LandingNav`, `LandingHero`, `LandingTurn`, `LandingGeefFlow`, `LandingCrew`, `LandingProof`, `LandingCapabilities`, `LandingClosing`, `LandingFooter`) und `LandingIllustrations.razor` (12 Inline-SVGs via `Name`-Parameter + `@switch`).

**Stub-Seiten:** `Components/Pages/Public/` — neun `[AllowAnonymous]`-Static-SSR-Seiten mit `LandingLayout` und `ComingSoon.razor` (`[Parameter, EditorRequired] string Title`): `/pricing`, `/docs`, `/self-host`, `/status`, `/changelog`, `/contact`, `/imprint`, `/privacy`, `/terms`.

**CSS-Klassen-Präfixe:** `lp-nav`, `lp-hero`, `lp-turn`, `lp-reveal`, `lp-caps`, `lp-close`, `lp-footer`, `lp-stub`. GEEF-Flow: `geef-flow`, `geef-phase`. Crew: `crew-sheet`. Proof: `run-mock`. Illustrationen: `press-anim`, `platen`, `paper-in`, `paper-out`, `stamp-glow`, `loop-path`.

**Asset-Einbindung:** `App.razor` injiziert `atelier-landing.css`, `js/landing.js`, `landing-root` (html) und `landing-body` (body) nur bedingt für `Path == "/"` — kein Overhead für authentifizierte App-Routen.
