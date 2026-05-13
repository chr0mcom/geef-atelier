# Claude-Code-Prompt: Brand-Asset-Integration

*Side-Step parallel zu PS-6. Integration der neu generierten Logo- und Favicon-Assets in die Atelier-App. Klein abgegrenzt, kein Backend, kein Domain-Eingriff — nur Asset-Ablage und UI-Verkabelung.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Eine externe Bildgenerierungs-Session hat ein vollständiges Brand-Asset-Set produziert: Master-Mark (eine stilisierte Feder/Quill) in drei Tintefarb-Varianten, App-Icons, Favicon-Sets in mehreren Größen und Theme-Varianten. Diese Assets liegen als ZIP vor und sollen in die App eingebaut werden.

Deine Aufgabe ist die **Brand-Asset-Integration** — ein klein abgegrenzter Side-Step parallel zur laufenden PS-6 (Crew-UI). Konkret: Assets in `wwwroot/` ablegen, Favicon-Setup im HTML-Head korrekt verkabeln, bestehende `Brand`-Komponente vom Platzhalter-"G"-Crest auf das echte Mark umstellen, Web-Manifest für PWA-Verhalten ergänzen.

Kein Backend-Eingriff, keine neuen Components außer minimaler Anpassung der bestehenden `Brand`. Theme-aware Logo via CSS-Custom-Properties.

## Vorgehen

**Du folgst dem Workflow in `/srv/docker/docs/geef-workflow.md`**, aber **in komprimierter Form**:
- Phase 1.1 (Task Comprehension): kurz, ist klein
- Phase 1.2 (Grounding): bestehende `Brand.razor`-Komponente und Layout-Headers lesen
- Phase 1.4 (Architect): zwei echte Klärungspunkte (siehe unten), sonst sind Entscheidungen fixiert
- Phase 2 (Execution): Implementation
- Phase 3 (Review): R1 + R5 reichen für diesen Side-Step — R2-R4 können auf das normale Maß reduziert werden, weil keine Backend-Logik oder Architektur-Eingriffe stattfinden
- Phase 4 (Finalize): Bericht, Commit, Push

## Verbindliche Entscheidungen

| Entscheidung | Konkret |
|---|---|
| **Asset-Ablage** | Source-of-Truth in `docs/design/brand-assets/` (alle 42 PNGs, committed). Production-Assets in `src/Geef.Atelier.Web/wwwroot/` an passenden Stellen. |
| **Theme-aware Mark via CSS** | CSS-Custom-Property `--brand-mark`, in jeder Palette-Klasse einen anderen `url()`-Wert. Kein JS, kein Razor-Conditional. |
| **Favicon-Strategie** | Vellum-Set als Default. Plus `media="(prefers-color-scheme: dark)"`-Hint für Noir-Variante in Browser-Dark-Mode. |
| **Apple-Touch-Icon** | `favicon-vellum-192.png` als Apple-Touch-Icon. 180×180-spezifische Variante nicht generieren — 192 wird sauber runterskaliert. |
| **Web-Manifest** | `site.webmanifest` mit Standard-PWA-Metadaten. Name "Geef Atelier", Short-Name "Geef", Icons 192/512, Theme-Color aus Atelier-Tokens. |
| **Brand-Komponente** | `Components/UI/Brand.razor` umstellen: `<div class="crest">G</div>` → `<div class="brand-mark"></div>` mit CSS-Background-Image. Wordmark-Teil (`geef.atelier`) bleibt unverändert. |
| **Login-Page-Hero-Logo** | Login-Page (`Login.razor`) Two-Pane-Left bekommt das Master-Quill prominent als Atelier-Atmosphäre. Aus dem `quill-{theme}.png`-Set. |
| **Watermark im Manuscript** | **Nicht** in diesem Step. Manuscript-Watermark wäre nice, aber Scope-Drift. Manueller TODO für später. |
| **Stempel-Verwendung** | Stempel-Asset war im Mockup, ist aber nicht im ZIP enthalten — wird nicht implementiert. |
| **Favicon-ICO-Legacy** | `favicon.ico` aus den PNGs generieren via Online-Tool oder `imagemagick`, oder weglassen (moderne Browser brauchen es nicht). Empfehlung: weglassen, weil `.ico` 2026 nicht mehr nötig ist und das Atelier nur moderne Browser unterstützen muss. |
| **Hairline-Verstärkung für ≤32px** | TODO für später dokumentieren. Aktuelles Set einbauen, auf Production verifizieren, dann ggf. nachschärfen lassen. |

## Konkrete Anforderungen

### 1. Asset-Ablage

**Source-of-Truth** in `docs/design/brand-assets/`: alle 42 PNGs aus dem ZIP, plus eine `README.md` die das Set kurz beschreibt (Master, Mark-Varianten, App-Icons, Favicon-Sets, Theme-Mapping-Tabelle).

**Production-Assets** in `src/Geef.Atelier.Web/wwwroot/`:

```
wwwroot/
├── favicon-16.png                  (kopiert aus favicon-vellum-16.png oder favicon-16.png)
├── favicon-32.png                  (analog)
├── favicon-192.png                 
├── favicon-512.png                 
├── favicon-noir-16.png             (für dark-mode Browser)
├── favicon-noir-32.png             (für dark-mode Browser)
├── apple-touch-icon.png            (= favicon-vellum-192.png, umkopiert)
├── site.webmanifest                (neu, siehe unten)
├── img/
│   └── brand/
│       ├── mark-dark.png           (= quill-dark.png, 1254×1254)
│       ├── mark-light.png          (= quill-light.png)
│       ├── mark-sand.png           (= quill-sand.png)
│       ├── icon-vellum.png         (1024×1024 mit Background)
│       ├── icon-noir.png           
│       └── icon-petrol.png         
```

**Wichtig:** Die `quill-*.png`-Dateien werden zu `mark-*.png` umbenannt beim Kopieren nach `wwwroot/img/brand/`. Das ist konsistenter mit dem Inhalt (alle drei sind dasselbe Mark in unterschiedlichen Tintefarben).

### 2. Favicon-Verkabelung in `App.razor`

Der `<head>`-Block bekommt eine ordentliche Favicon-Sektion:

```html
<!-- Modern PNG favicons -->
<link rel="icon" type="image/png" sizes="16x16" href="favicon-16.png" />
<link rel="icon" type="image/png" sizes="32x32" href="favicon-32.png" />
<link rel="icon" type="image/png" sizes="192x192" href="favicon-192.png" />
<link rel="icon" type="image/png" sizes="512x512" href="favicon-512.png" />

<!-- Dark mode hint -->
<link rel="icon" type="image/png" sizes="32x32"
      href="favicon-noir-32.png" media="(prefers-color-scheme: dark)" />

<!-- Apple Touch -->
<link rel="apple-touch-icon" sizes="192x192" href="apple-touch-icon.png" />

<!-- Web Manifest for PWA / Android home screen -->
<link rel="manifest" href="site.webmanifest" />
```

### 3. Web-Manifest

Neue Datei `wwwroot/site.webmanifest`:

```json
{
  "name": "Geef Atelier",
  "short_name": "Geef",
  "description": "Text manufactory with AI crew",
  "icons": [
    { "src": "/favicon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/favicon-512.png", "sizes": "512x512", "type": "image/png" }
  ],
  "theme_color": "#1a1410",
  "background_color": "#c8bca0",
  "display": "browser",
  "start_url": "/"
}
```

`theme_color` und `background_color` aus den Atelier-Tokens — Architect prüft die exakten Werte gegen `atelier.css`.

### 4. Brand-Komponente umstellen

`Components/UI/Brand.razor`:

**Aktuelle Struktur (aus PS-3):**
```razor
<div class="brand">
  <div class="crest">G</div>
  <div class="wordmark">
    geef<span class="dot">.</span><span class="atelier">atelier</span>
  </div>
</div>
```

**Neue Struktur:**
```razor
<div class="brand">
  <div class="brand-mark" data-testid="brand-mark"></div>
  <div class="wordmark">
    geef<span class="dot">.</span><span class="atelier">atelier</span>
  </div>
</div>
```

**Scoped CSS (`Brand.razor.css` oder ergänzt in `atelier.css`):**

```css
.brand-mark {
  width: 28px;          /* an aktuelle Crest-Größe angepasst */
  height: 28px;
  background-image: var(--brand-mark);
  background-size: contain;
  background-repeat: no-repeat;
  background-position: center;
}

html.palette-vellum { --brand-mark: url('/img/brand/mark-dark.png'); }
html.palette-noir   { --brand-mark: url('/img/brand/mark-light.png'); }
html.palette-petrol { --brand-mark: url('/img/brand/mark-sand.png'); }
```

Die alten `.crest`-CSS-Regeln werden aus `atelier.css` entfernt.

### 5. Login-Page-Hero-Logo

`Components/Pages/Login.razor` — die Two-Pane-Left mit Colophon/Atelier-Atmosphäre bekommt das große Master-Quill prominent.

**CSS-Pattern** analog zum brand-mark, aber größer:

```css
.login-hero-mark {
  width: 240px;
  height: 240px;
  background-image: var(--hero-mark);
  background-size: contain;
  background-repeat: no-repeat;
  background-position: center;
  margin-bottom: 32px;  /* an Login-Atmosphäre angepasst */
}

html.palette-vellum { --hero-mark: url('/img/brand/mark-dark.png'); }
html.palette-noir   { --hero-mark: url('/img/brand/mark-light.png'); }
html.palette-petrol { --hero-mark: url('/img/brand/mark-sand.png'); }
```

Falls die Login-Atmosphäre besser mit dem **App-Icon** (1024×1024 mit Background) statt dem freigestellten Mark wirkt — Architect entscheidet basierend auf der visuellen Tests. Beide Optionen sind okay.

### 6. Tests

**bUnit-Anpassung:**
- `BrandTests.razor.cs` (falls existent aus PS-3): den alten `.crest`-Selector durch `data-testid="brand-mark"` ersetzen
- Verifizieren dass die `.brand-mark`-Div im richtigen Layout-Slot rendert

**E2E-Anpassung:**
- Playwright-Tests die das alte "G"-Crest geprüft haben: Selector umstellen
- Neuer Test: Brand-Mark ist sichtbar nach Login auf allen Hauptscreens

**Visuelle Verifikation in allen drei Themes** ist Pflicht. Screenshot-Pärchen Vellum/Noir/Petrol von Login + RunDetail im Bericht.

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` 0 Errors, 0 Warnings.
2. `dotnet test` — alle bestehenden Tests grün, bUnit-Anpassungen wo nötig.
3. **Assets vollständig committed** — `docs/design/brand-assets/` mit allen 42 PNGs + README, plus Production-Assets in `wwwroot/`.
4. **Favicon-Setup im `<head>`** vollständig: PNG-Sizes 16/32/192/512, Dark-Mode-Hint, Apple-Touch, Web-Manifest.
5. **`site.webmanifest`** vorhanden und valide JSON. Browser-DevTools "Application → Manifest" zeigt korrekte Metadaten.
6. **Brand-Komponente nutzt theme-aware Mark** via CSS-Custom-Property. Theme-Wechsel im laufenden Browser zeigt sofortigen Logo-Wechsel ohne Reload.
7. **Login-Page zeigt großes Mark** prominent in der Atelier-Atmosphäre-Hälfte.
8. **Browser-Tab zeigt Favicon** auf Production. Bei 16px Tab-Größe noch erkennbar (oder TODO-Hairline-Verstärkung notiert).
9. **Apple-Touch-Icon funktional** — auf iOS "Zum Home-Bildschirm hinzufügen" zeigt das richtige Icon.
10. **R5 Live-Verifikation** auf `https://geef.stefan-bechtel.de/` in allen drei Themes. Screenshots im Bericht.
11. **TODO-Hairline-Verstärkung** dokumentiert für Folge-Step falls Favicon bei ≤32px unleserlich.

## Was du in diesem Schritt NICHT tust

- **Kein Watermark im Manuscript** — wäre nice, aber Scope-Drift.
- **Keine Backend-Änderungen** — `IRunService`, MCP, Pipeline alles unangetastet.
- **Keine Schriftzug-Generierung** — User generiert den Wordmark separat.
- **Keine Domain-Modell-Änderungen**.
- **Keine Anpassung der Crew-UI aus PS-6** — der läuft parallel.
- **Keine ICO-Datei** — `.ico` ist 2026 nicht mehr nötig.

## Architect-Konsultation (Phase 1.4) — zwei Knackpunkte

1. **Login-Hero-Asset:** freigestelltes Mark (`mark-{theme}.png`, vertikal-orientiert) vs. App-Icon (`icon-{theme}.png`, 1024×1024 mit Background). Empfehlung: **freigestelltes Mark**, weil der Login-Hintergrund schon den Atelier-Hintergrund hat — das App-Icon-Background würde mit dem Page-Background konkurrieren. Architect bestätigt nach visueller Prüfung.

2. **Brand-Mark-Größe in der NavBar:** aktuelle Crest-Größe ist vermutlich 24-32px. Empfehlung: **28px** als Default. Falls das Mark bei der Höhe optisch zu klein wirkt, Architect kalibriert mit visuellem Test.

## Reviewer-Hinweise

- **R1 (Functional Correctness):** Alle 11 ACs prüfen. Besonders 6 (theme-aware Brand) und 10 (Live-Verifikation).
- **R2 (Code Quality):** Saubere CSS-Custom-Property-Strategie, keine Inline-Styles, keine doppelten URL-Definitionen.
- **R3 (Test Execution):** Tests grün nach Selector-Anpassungen.
- **R4 (Architecture Compliance):** Keine Backend-Berührung. Asset-Pfade konsistent mit `wwwroot/`-Struktur.
- **R5 (Live UI):** Screenshot-Pärchen Vellum/Noir/Petrol von Login + Welcome + RunDetail. Browser-Tab-Favicon-Verifikation. iOS-Home-Screen-Test falls möglich.

## Persistenter Abschlussbericht

Bericht nach `docs/reports/brand-asset-integration-report.md` (kein PS-Nummer, weil Side-Step). Inhalt:

1. **Was wurde umgesetzt** — Asset-Ablage, Brand-Komponente, Favicon-Setup, Login-Hero.
2. **Architect-Output** — die zwei Knackpunkte.
3. **Screenshot-Verifikation** — alle drei Themes pro relevantem Screen.
4. **Akzeptanzkriterien-Check** — Tabelle.
5. **TODOs für später** — Hairline-Verstärkung falls nötig, Watermark im Manuscript, Stempel-Asset.

## Konventionen

- Code, Code-Kommentare: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits (z.B. `feat: add brand assets`, `feat: theme-aware brand mark`, `feat: web manifest`, `chore: favicon setup`).
- TreatWarningsAsErrors aus Schritt 1 respektieren.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension). Erwarteter Aufwand: 0.5-1 Arbeitstag.

---

**Nach erfolgreichem Abschluss:** Atelier hat eine vollständige visuelle Identität. Favicon im Browser-Tab, App-Icon auf iOS-Home-Screen, theme-aware Brand-Mark in der UI, Master-Quill als Login-Atmosphäre. PS-6 bleibt davon unberührt und kann parallel weiterlaufen.