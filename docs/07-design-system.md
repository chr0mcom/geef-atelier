# 07 — Design-System

*Letzte Aktualisierung: 2026-05-12 (PS-3: Design-Translation)*

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

Folgende UI-Elemente zeigen "Coming soon" und sind noch nicht mit echtem Backend verbunden:

| Element | Sichtbar als | Backend-Voraussetzung |
|---------|-------------|----------------------|
| Welcome-Stats | `—` in Stat-Tiles | Aggregations-Queries in `IRunService` |
| Cost-Anzeige (RunDetail) | `$ —` | `RunEntity.CostTotal` > 0 (PS-Roadmap: Cost-Tracking) |
| Export-Button (Manuscript) | Disabled | Export-Service (DOCX/PDF) |
| Profile-MenuItem | Disabled | Multi-User-Auth |

CSS-Klasse: `.coming-soon` (gedämpfte Farbe, Tooltip).

## CSS-Architektur

- **Globales Stylesheet:** `wwwroot/atelier.css` (1835 Zeilen) — CSS Custom Properties, drei Paletten, alle Komponenten-Basics
- **Scoped CSS:** `.razor.css` pro Komponente für Layout-Overrides
- **Bootstrap:** vollständig entfernt in PS-3
- **Kein Inline-Styling** in Komponenten (außer minimal in Pages)
