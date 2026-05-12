# Claude-Code-Prompt: Post-Skeleton Schritt 3 — Design-Translation (Mockups → Blazor)

*Dieser Prompt überträgt ein hochwertiges UI-Design aus Mockups in die Blazor-Server-App, inklusive eines Theme-Switchers mit drei Paletten und Cookie-Persistierung.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Das Walking-Skeleton steht produktiv, PS-1 (Backup) und PS-2 (Reviewer-Kalibrierung) sind durch. Ein professionell gestaltetes UI-Design ist von einer separaten Design-Session (Claude Design) als HTML/CSS/React-Prototyp produziert worden — drei Paletten, eigene Typografie, eigene Hairline-Icons, mehrere designreiche Komponenten, eine durchgängige Atelier-Atmosphäre. Das Design ist gut.

Deine Aufgabe ist **PS-3: Design-Translation**. Übertrage die visuelle und strukturelle Sprache des Mockups in die bestehende Blazor-Server-App. Keine neuen Backend-Features (außer einer Mini-Settings-Mechanik für das Theme). Was funktional real ist, bleibt verkabelt. Was im Mockup nur Vision ist, kommt als sichtbarer-aber-statischer Stub mit klarer "Coming soon"-Markierung. Was Prototyping-Beiwerk ist, bleibt draußen — mit einer Ausnahme: der Theme-Switcher wird vom Tweaks-Panel-Pattern in eine dauerhafte UI-Funktion überführt.

Visuelle Treue ist hoch zu halten. Das Design soll erkennbar in Blazor wiederfinden, nicht eine 60%-Annäherung. Wenn dabei strukturelle Anpassungen an der bestehenden `Components/UI/`-Library nötig werden — die sind erlaubt und erwartet.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors. **Plan-Phase-Integration** als Architect-Form. R5 (Live UI) ist diesmal **substantiell**: Screenshot-Vergleich Mockup vs. Live-Implementation pro Screen, plus alle drei Themes verifiziert.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

**Mockup-Material** (an dich übergeben in `docs/design/atelier-mockups/`):
1. `Geef_Atelier.html` — HTML-Einstiegspunkt
2. `atelier.css` (~1800 Zeilen) — vollständiges Design-System mit drei Paletten, OKLCH-Farbtokens, Typografie, allen Komponenten-Styles. **Die Theme-Definitionen unter `html.palette-noir`, `html.palette-vellum`, `html.palette-petrol` sind die Basis für deinen Theme-Switcher.**
3. `atelier-app.jsx` — App-Shell, Routing, NavMenu, UserMenu. **Das Tweaks-Panel zeigt, wie der Theme-Wechsel mechanisch funktioniert** (`applyPalette()`-Funktion ändert die CSS-Klasse am `<html>`).
4. `atelier-screens.jsx` — alle fünf Hauptscreens: LoginScreen, WelcomeScreen, NewRunScreen, RunsListScreen, RunDetailScreen
5. `atelier-icons.jsx` — 14 Hairline-Icons als inline SVG. **`IconPalette` ist relevant für das Theme-Switcher-UI.**
6. `atelier-style-guide.jsx` — Style Guide, **nicht in Production übernehmen**
7. `atelier-data.jsx` — Sample-Daten und kleinere Helper
8. `tweaks-panel.jsx` — Mockup-spezifisches Panel, **nicht in Production übernehmen**, aber als Referenz für die Theme-Wechsel-Mechanik lesen

Lies das Mockup-Material vollständig, bevor du irgendetwas anfasst. Das Design ist konsistent — du musst die Sprache erst verstehen, dann übersetzen.

**Existierender Repo-Code:**
1. **`/srv/docker/docs/geef-workflow.md`**, **`CLAUDE.md`**, **`docs/02-architecture.md`**, **`docs/05-decisions-log.md`** D-010 bis D-024.
2. **Bestehende UI-Schicht:**
   - `src/Geef.Atelier.Web/Components/Pages/` — Login, Index, New, Runs, RunDetail
   - `src/Geef.Atelier.Web/Components/UI/` — die 9-12 bestehenden Komponenten
   - `src/Geef.Atelier.Web/Components/Layout/` — MainLayout, EmptyLayout, NavMenu
   - `src/Geef.Atelier.Web/wwwroot/` — Assets-Strategie
3. **Auth-Cookie-Pattern aus Schritt 8** (`AddCookie()` in `Program.cs`) — als Vorbild für den Theme-Cookie.
4. **`docs/reports/post-skeleton-02-reviewer-calibration-report.md`** für aktuellen Reviewer-Stand.

## Was ist real, was ist Mock, was ist neu

**Diese Mapping-Tabelle ist verbindlich.**

### Funktional verkabelt (bestehendes Backend nutzen)

| Mockup-Element | Backend-Anschluss |
|---|---|
| **Login-Form** (Username + Passphrase) | `IUserAuthenticator`, Cookie-Auth |
| **NewRun-Form** mit Briefing-Textarea + Config-JSON | `IRunService.SubmitRunAsync(briefing, config, createdByUser)` |
| **`⌘ + Enter` Submit-Shortcut** | Client-side Keyboard-Handler, ruft denselben Submit-Pfad |
| **Runs-Liste mit Status-Filter-Pills** | `IRunService.ListRunsAsync(limit, statusFilter)` |
| **Status-Badges** (Pending/Running/Completed/Failed/Aborted) | `RunEntity.Status` |
| **Live-Updates auf Runs-Liste** | SignalR `all-runs`-Group, `IRunNotifier` |
| **Run-Header** (Status, Briefing, Submitted/Started/Completed/Tokens) | `RunEntity` Felder |
| **Cancel-Button** für `running`-Runs | `IRunService.CancelRunAsync(runId)` |
| **Press-Visualization** (Executor → Reviewers → Executor) | Mapping auf aktiven Pipeline-Stage; lässt sich aus letztem Event ableiten |
| **Iteration-Panels** mit Artifact + Findings + collapsible | `IterationEntity` + `FindingEntity[]` |
| **Severity-Badges** (Critical/Major/Minor/Info) | `FindingEntity.Severity` |
| **Final Manuscript** (Newsreader-Serif) | `RunEntity.FinalText` bei `Completed`-Runs |
| **Copy-Button am Manuscript** | Browser-Clipboard-API |
| **Reconnect-Banner** | SignalR-Connection-State |
| **NavMenu mit Brand "geef.atelier"** | Static layout |
| **UserMenu mit Logout** | `/auth/logout`-Endpoint |
| **Globaler "1 run underway"-Indicator** | Counter aus `ListRunsAsync(statusFilter: Running)` + SignalR-Update |
| **Theme-Switcher** (3 Themes, Default Vellum, persistiert) | **NEU** — Mini-Settings-Endpoint + Cookie, siehe Sektion unten |

### Mock-Stub (visuell sichtbar, "Coming soon"-Hinweis, später aktivierbar)

| Mockup-Element | Stub-Strategie |
|---|---|
| **WelcomeScreen-Stats** ("47 runs this month", etc.) | Dezent statisch oder "—" mit Caption "Statistiken kommen bald". Empfehlung: dezent statisch, kein Fake-Wert. |
| **"Three runs underway since this morning"** | Wenn dynamisch trivial: verkabeln. Sonst statisch. |
| **Profile-Item im UserMenu** | Item sichtbar, klick zeigt Toast oder ist disabled mit Tooltip. |
| **Cost-Anzeige** (`$0.142`) | `RunEntity.CostTotal` ist `0`. Anzeige: "—" oder "Coming soon". |
| **Export-Button** im Manuscript | Button sichtbar, klick zeigt "Export folgt" oder disabled. |
| **"Resolved in next iteration"-Marker** | Trivial inferierbar in Razor (Cross-Iteration-Diff). Versuch's wenn machbar, weil großer visueller Mehrwert. Sonst statischer Hinweis. |
| **Crew-Liste auf NewRun-Page** | Hardcoded statisch, ist okay. |
| **Colophon im Login** ("EST. 2026 · ZÜRICH · VOL. III · NO. 047") | Pure Atmosphäre — übernehmen, statisch. |

### Nicht in Production übernehmen

- **Tweaks Panel** (`tweaks-panel.jsx`) — Prototyping-Tool. Die Theme-Wechsel-Funktion wird stattdessen in den UserMenu integriert (siehe nächster Abschnitt).
- **Style Guide-Page** (`/guide`-Route) — nur fürs Design-System-Showcasing
- **Hardcoded Mock-Daten** (`isolde.geef`, `R-2026-0184`, etc.) — Username kommt aus Auth-State

## Theme-Switcher — neue Mini-Funktion

**Anforderung:** Der Nutzer kann zwischen drei Themes wählen: **Vellum** (default, warm parchment, light), **Noir** (dark anthracite mit aged gold), **Petrol** (cool deep teal). Die Wahl wird persistiert und gilt über Sessions und Geräte-Neustarts hinweg.

### Mechanik

**Persistierung via Cookie**, analog zum bestehenden Auth-Cookie aus Schritt 8:

- **Cookie-Name:** `Atelier.Theme`
- **Werte:** `vellum`, `noir`, `petrol`
- **Default:** `vellum` (wenn kein Cookie vorhanden)
- **Eigenschaften:** Path=`/`, MaxAge=1 Jahr (oder analog zur Auth-Cookie-Lifetime), SameSite=Strict, HttpOnly=false (kein Sicherheitsrisiko, JS darf lesen falls nötig), Secure=Production-only

**Server-Side-Rendering:** Beim Page-Render liest das Layout (`MainLayout.razor` oder `App.razor`) den Cookie und setzt die korrekte CSS-Klasse direkt auf das `<html>`- oder `<body>`-Element (`palette-vellum`, `palette-noir`, `palette-petrol`). Damit gibt es keinen Flash-of-Wrong-Theme beim Laden.

**Wechsel-Mechanik:**
1. Im UserMenu klickt der User auf ein Theme
2. POST an `/settings/theme?value=<vellum|noir|petrol>` (kleiner MVC-Endpoint analog zu `/auth/logout` aus Schritt 8)
3. Endpoint setzt Cookie `Atelier.Theme=<value>`, redirected zur Referer-URL
4. Beim nächsten Render zeigt der Server die neue Palette

**Alternative ohne Server-Round-Trip:** JS-Interop könnte das `<html>`-Class direkt umschalten *und* den Cookie via `document.cookie` setzen — kein Server-Hit nötig. Das vermeidet den Page-Reload. Empfehlung: **diese Variante**, weil eleganter. Architect kann den Server-Round-Trip-Weg wählen wenn er einfacher findet.

### UI-Platzierung

**Empfehlung:** Im **UserMenu** als Auswahl-Items. Der UserMenu (rechts oben in der NavBar) bekommt eine neue Sektion oberhalb des Logout-Items:

```
+----------------------------+
| 👤 Profile (coming soon)   |
+----------------------------+
| 🎨 Theme                   |
|   ● Vellum                 |  (active)
|   ○ Noir                   |
|   ○ Petrol                 |
+----------------------------+
| ⬅ Sign out                 |
+----------------------------+
```

`IconPalette` aus den Mockup-Icons als Section-Icon. Aktive Theme via Filled-Dot oder Checkmark markiert.

**Alternative:** Standalone Palette-Icon-Button neben dem UserMenu, klick öffnet eigenes Popover. Architect kann das wählen, wenn UserMenu zu voll wird.

### Akzeptanzkriterien für den Theme-Switcher (zusätzlich zu den allgemeinen ACs unten)

- Default-Theme bei erstem Page-Load ist **Vellum**.
- Theme-Wechsel via UserMenu funktioniert ohne sichtbaren Page-Reload (oder mit minimalem, falls Server-Round-Trip-Variante).
- Nach Browser-Neustart bleibt das gewählte Theme aktiv.
- Logout löscht den Theme-Cookie **nicht** — beim nächsten Login gilt weiter das gewählte Theme.
- Alle Screens und Komponenten funktionieren visuell sauber in allen drei Themes (kein Hardcoded-Color, alles aus CSS-Custom-Properties).
- R5 verifiziert alle drei Themes pro Hauptscreen.

## Klärungsfragen für die Architect-Phase (Phase 1.4) — sechs Schwerpunkte

1. **Theme-Wechsel-Mechanik:** Cookie + Server-Round-Trip vs. JS-Interop direkt mit Cookie-Set via JS. Empfehlung: **JS-Interop** für eleganteren UX (kein Reload), Server-Round-Trip als Fallback. Architect bestätigt.

2. **Reviewer-Klassen-Namen:** Code hat `BriefingTreueReviewer` und `KlarheitReviewer` (Deutsch). Mockup hat `BriefingFidelityReviewer` und `ClarityReviewer` (Englisch). Drei Optionen:
   - (α) Code-Klassen umbenennen (Breaking-Change)
   - (β) UI-Übersetzungs-Layer (Display-Mapping in Helper-Klasse)
   - (γ) UI in Deutsch belassen
   
   Empfehlung: **(β)** — UI-Display-Mapping in Helper-Klasse, Code-Klassen bleiben unverändert.

3. **UI-Sprache insgesamt:** Bisher Englisch. Bleibt Englisch, konsistent mit Mockup. Architect bestätigt.

4. **Komponenten-Struktur:** Bestehende UI-Komponenten werden ersetzt (nicht ergänzt), weil das Design eine andere visuelle Sprache spricht. Tests müssen entsprechend angepasst werden.

5. **CSS-Strategie:** Hybrid empfohlen — globale CSS-Custom-Properties (Color-Tokens für alle drei Paletten, Fonts, Spacing) in zentralem Stylesheet, scoped `.razor.css` pro Komponente nutzen diese Tokens.

6. **Fonts: Self-Host oder CDN?** Mockup nutzt Google Fonts CDN. Drei Optionen: CDN (einfach, DSGVO-fragil), Self-Host (sauber, DSGVO-konform), Bunny Fonts (DSGVO-konformer CDN). Empfehlung: **Self-Host** — DSGVO-konform auf deutscher Domain, alle drei Fonts (Newsreader, Geist, JetBrains Mono) haben OFL-Lizenz.

## Konkrete Anforderungen

### Screens (in dieser Reihenfolge empfohlen)

1. **Design-System-Foundation** zuerst: globale CSS mit allen drei Paletten-Definitionen, Schriften (self-hosted oder CDN je nach F6), Layout-Grid, Body-Grain, Background-Gradients. Plus die Atomic-Komponenten: `Brand`, `StatusBadge`, `SeverityBadge`, `Button` (alle Varianten), Hairline-Icons.

2. **Layout-Chrome:** MainLayout mit NavMenu, UserMenu mit Theme-Switcher, globalem Running-Indicator.

3. **Theme-Persistierung implementieren** und initial mit Vellum als Default verifizieren.

4. **LoginScreen** — Two-Pane mit Colophon links, Login-Card rechts. Cookie-Auth bleibt unverändert. Bei Erfolg Redirect zur Welcome-Page.

5. **RunDetail** (das Herzstück) — Run-Header, Press-Visualization, Iteration-Panels mit Findings-Counter-Pills, Final Manuscript mit Drop-Cap. SignalR-Live-Updates in allen dynamischen Stellen.

6. **RunsList** — Workbench mit RunRows, Status-Filter-Pills mit Counts, Progress-Pips, Floating "New briefing"-Button.

7. **NewRun** — Großzügige Briefing-Textarea, Char/Word-Counter, Config-JSON-Toggle, "Hand to the crew"-Button.

8. **WelcomeScreen** — Hero-Headline, CTA-Card, Stat-Tiles (Mock), Recent-Work-Liste.

9. **Empty/Error/Loading-States** — Empty workbench, Reconnect-Banner, Error-Banner.

### Bestehende Pages → neue Komponenten-Library

Die bestehenden `Pages/`-Razor-Komponenten werden neu strukturiert. Routing bleibt:
- `/login` → `Login.razor` (mit `@layout EmptyLayout`)
- `/` → `Welcome.razor`
- `/new` → `New.razor`
- `/runs` → `Runs.razor`
- `/runs/{RunId:guid}` → `RunDetail.razor`

Plus neuer Mini-Endpoint:
- `/settings/theme` → POST-Handler in `Endpoints/SettingsEndpoints.cs` (oder ähnlich), setzt Cookie

### Funktionale Erhaltung — verbindlich

Alle 96 bestehenden Tests müssen grün bleiben. Insbesondere:
- Authentifizierungs-E2E-Tests (`LoginFlowTests`, `LogoutFlowTests`)
- Submit/List/Detail-E2E-Tests
- Live-Update-E2E-Tests (`LiveUpdateFlowTests` — bekannt flaky aus PS-2, soll **nicht schlechter** werden)
- Cancel-Flow-Tests

**Neue Tests** für Theme-Switcher:
- `ThemeCookieDefaultsToVellumTests` — kein Cookie gesetzt → `<html class="palette-vellum">`
- `ThemeCookieIsRespectedTests` — Cookie `Atelier.Theme=noir` → `<html class="palette-noir">`
- `ThemeSwitcherE2ETests` (Playwright) — User klickt Vellum → Noir → Petrol → Vellum, Browser-Refresh, Theme bleibt

**Falls Playwright-Selectoren brechen** wegen CSS-Klassen-Änderungen: anpassen. `data-testid` wenn sinnvoll.

### Mockup-Treue im Bericht

Pro Screen einen **Screenshot-Vergleich Mockup ↔ Live-Implementation**, plus **alle drei Themes** für mindestens RunDetail und Welcome. Mockup-Screenshots aus dem HTML-Prototyp, Live-Screenshots via Playwright.

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` 0 Errors, 0 Warnings.
2. `dotnet test` — alle 96 bestehenden Tests grün, plus die neuen Theme-Tests.
3. **Visuelle Treue ≥ 90%** zum Mockup, dokumentiert via Screenshot-Vergleich pro Screen.
4. **Funktionale Erhaltung 100%** — Login, Submit, List, Detail, Live-Updates, Cancel, Logout funktionieren identisch zum Stand vor PS-3.
5. **Theme-Switcher voll funktional** — Default Vellum, Wechsel via UserMenu, Persistenz via Cookie über Browser-Neustart hinweg, alle drei Themes funktional in allen Screens.
6. **Mock-Hints klar sichtbar** — Stats auf WelcomeScreen, Cost-Anzeige, Export-Button mit "Coming soon"-Indikation.
7. **Tweaks Panel und Style Guide-Page nicht in Production-Build**.
8. **Fonts gemäß Architect-Empfehlung** (Self-Host, kein externer Font-Request im Network-Tab, falls F6=β).
9. **R5 auf `https://geef.stefan-bechtel.de/`**: alle Hauptscreens visuell verifiziert in **allen drei Themes**, keine Console-Errors, keine Layout-Brüche bei normaler Browser-Größe (1280-1920px).
10. **README aktualisiert** — kurze Notiz zur neuen UI, Theme-Switcher-Hinweis, Mock-Komponenten-Liste als TODO-Sektion.
11. **Decisions-Log-Eintrag** (D-025 oder nächste freie Nummer) mit Architect-Entscheidungen zu den sechs Schwerpunkten + Theme-Switcher-Mechanik-Wahl.

## Was du in diesem Schritt NICHT tust

- **Keine neuen Backend-Features** (außer dem Theme-Cookie-Mini-Endpoint) — keine neuen Service-Methoden, keine neuen Migrations.
- **Keine Domain-Modell-Änderungen** — `RunEntity`, `IterationEntity`, `FindingEntity` bleiben unverändert.
- **Keine Pipeline-Logik-Änderungen** — Reviewer, Executor, Convergence bleiben wie nach PS-2.
- **Keine MCP-Schicht-Änderungen** — `Geef.Atelier.Mcp` bleibt unangefasst.
- **Keine Cost-Tracking-Implementation** — Cost bleibt `0` bzw. "—".
- **Keine Profile-Page-Implementation** — Profile bleibt Stub.
- **Keine WelcomeScreen-Stats-Aggregation** — Stats bleiben Hardcoded/Stub.
- **Keine Auth-Mechanik-Änderungen** — Cookie-Setup, Bearer-Token, Logout bleiben wie etabliert.

## Reviewer-Hinweise

- **R1 (Functional Correctness):** Alle 11 ACs prüfen, besonders 4 (funktionale Erhaltung) und 5 (Theme-Switcher). Live-Test auf der Production-URL.
- **R2 (Code Quality):** Saubere CSS-Strukturierung, keine Inline-Styles, sinnvolle Komponenten-Aufteilung. HTML-Semantik (Heading-Levels, ARIA). Theme-Switcher-Code idiomatisch.
- **R3 (Test Execution):** Alle Tests grün, neue Theme-Tests dokumentiert.
- **R4 (Architecture Compliance):** "Keine HTML in Pages"-Regel weiter. Triviale Page-Steuerelemente dürfen bleiben (D-020(g)).
- **R5 (Live UI):** Screenshot-Vergleich pro Screen, **alle drei Themes**. Theme-Persistierung durch Browser-Neustart verifizieren. Console-Errors zählen.

## Persistenter Abschlussbericht

Bericht nach `docs/reports/post-skeleton-03-design-translation-report.md`. Inhalt:

1. **Was wurde umgesetzt** — Screen für Screen.
2. **Architect-Output** — Antworten auf die sechs Schwerpunkte plus Theme-Wechsel-Mechanik-Entscheidung.
3. **Komponenten-Mapping-Tabelle** — alte vs. neue Komponenten.
4. **Mock-vs-Real-Inventory** — finale Liste was als Stub umgesetzt wurde.
5. **Screenshot-Vergleiche** — pro Screen Mockup ↔ Live, plus alle drei Themes.
6. **Theme-Switcher-Verifikation** — UX-Flow, Persistierungs-Test, Cross-Theme-Visual-Test-Ergebnisse.
7. **Reviewer-Iterationen** — Tabelle.
8. **Akzeptanzkriterien-Check** — Tabelle mit allen 11 ACs.
9. **Beobachtungen** — was war einfacher als gedacht, was war schwieriger.
10. **TODO-Liste für nachträgliche Backend-Aktivierung** — alle Mock-Stubs aufgelistet als Grundlage für die nächste Roadmap-Diskussion.

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- UI-Strings: **Englisch**.
- Keine Mockup-spezifischen Helper (Tweaks-Panel-Logik, Style Guide) im Production-Build.
- Theme-Cookie-Behandlung darf nicht den Auth-Flow stören.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.
