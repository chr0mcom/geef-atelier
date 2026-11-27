# 07 — Design-System

*[English](07-design-system.md) · **Deutsch***

*Letzte Aktualisierung: 2026-11-27 (D-091: mit dem Rückbau entfallene Teile gestrichen, Namen entfernter Aufstellungen; davor 2026-05-17, Mock-Stub-Status auf damaligen Stand gebracht; Tokens/Themes/Typografie unverändert seit PS-3-Design-Translation)*

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
| `requirements-check` (entfernt) | `Request check` |
| Alle anderen | Unverändert (Fallback) |

Entfernte eingebaute Aufstellungen behalten auf der technischen Laufseite ihren früheren Namen (`GetTemplateDisplay`: `klassik` →
`Classic`, `juristisch` → `Legal`, `akademisch` → `Academic`, `marketing` → `Marketing`, `crew-composer` → `Crew Composer`,
`direct-outline` → `Extra careful`).

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
| Welcome-Stats | `—` in Stat-Tiles | Entfallen mit dem Dashboard (D-091) |
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

## Landing-Page (D-053, überarbeitet mit D-084)

**Route:** `/` — `Landing.razor`, `[AllowAnonymous]`, Static SSR (kein `@rendermode`), `LandingLayout.razor`. Sie liest dieselben
Einstellungen wie „How it works“ über `HowItWorksFeatures` (`FeaturesFor(viewer)`) und reicht das Ergebnis an die Abschnitte weiter:
Rückfragen, Ändern, Webrecherche und Faktenprüfung werden nur beschrieben, wenn sie für die betrachtende Person an sind, und jede
Zahl stammt aus den Einstellungen.

**Abschnitte (Reihenfolge):** `LandingNav` (angemeldet: My work / Write something), `LandingHero`, `LandingExample` (erfundenes
Beispiel als Korrekturbogen mit Randnotizen, gebaut aus den festen Sätzen von `OrderCheckLines` und den Einleitungen von
`HowItWorks`), `LandingSteps` (vier oder fünf Schritte des Standardwegs, „one more try, at most“),
`LandingCapabilities` (sechs Punkte und „What Atelier does not do“), `LandingAudiences` (#for-whom), `LandingPrivacy`,
`LandingFilm` (Video unverändert, bis ein neues vorliegt; der Begleittext sagt, dass es eine frühere Version zeigt), `LandingClosing`,
`LandingFooter`. Inline-SVGs in `LandingIllustrations.razor` über `Name`.

**CSS:** `wwwroot/atelier-landing.css`, nur Tokens der drei Paletten. Präfixe: `lp-*`, `ex-*`/`proof-*` (Beispiel), `lp-flow*`
(Schritte), `cap`, `aud`. Neben Presse und Film nur eine orchestrierte Bewegung (die Schritte).

**JS:** `wwwroot/js/landing.js` — reines IIFE. Schritte: ein Durchlauf über `.lp-flow-step` (`is-active`/`has-passed`), der Bogen
`.lp-flow-retry` erscheint, solange der Prüfschritt aktiv ist. `prefers-reduced-motion` markiert alle Schritte sofort.
Scroll-Einblendung nur noch für den Film.

**Tests:** `Web/HowItWorks/LandingWordsTests` (Wörter, Namen, Versprechen), `LandingSwitchesTests` (Schalterzustände, Zahlen,
Überschriftenfolge), `LandingLinksTests` (jeder Link löst auf), `Web/E2E/LandingFlowTests` (hell/dunkel × Desktop/Telefon: kein
seitliches Scrollen, kein Layoutsprung, Kontrast jedes Textes, Tastatur, Schritte).

**Stub-Seiten:** `Components/Pages/Public/` — `[AllowAnonymous]`-Static-SSR-Seiten mit `LandingLayout`: `/pricing`, `/docs`,
`/self-host`, `/status`, `/changelog`, `/contact`, `/imprint`, `/privacy`, `/terms`.

**Asset-Einbindung:** `App.razor` bindet `atelier-landing.css` für die öffentlichen Seiten und `js/landing.js` nur für `Path == "/"` ein.
