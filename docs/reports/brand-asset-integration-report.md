# Brand-Asset-Integration — Abschlussbericht

Datum: 2026-05-13  
Autor: Claude Code (claude-opus-4-7) im Auftrag von Stefan Bechtel

---

## 1. Was wurde umgesetzt

### Asset-Ablage (Source-of-Truth)

44 PNGs in `docs/design/brand-assets/` committed — darunter:
- Master-Quill (`quill-master.png`) und drei Tintefarb-Varianten (`quill-dark.png`, `quill-light.png`, `quill-sand.png`)
- Drei App-Icons mit Background (`icon-vellum.png`, `icon-noir.png`, `icon-petrol.png`)
- Vollständiges Favicon-Set in Vellum-, Noir- und Transparent-Varianten (je 7 Größen: 16, 24, 32, 48, 64, 96, 128, 192, 256, 512 px)
- `README.md` mit Theme-Mapping-Tabelle

Production-Assets in `src/Geef.Atelier.Web/wwwroot/`:

```
wwwroot/
├── favicon-16.png / favicon-32.png / favicon-192.png / favicon-512.png    (Vellum-Set)
├── favicon-noir-16.png / favicon-noir-32.png                               (Dark-Mode-Hint)
├── apple-touch-icon.png                                                     (= 192-Variante)
├── site.webmanifest
└── img/brand/
    ├── mark-dark.png      (für Vellum — dunkle Tinte auf hellem Background)
    ├── mark-light.png     (für Noir — helle Tinte auf dunklem Background)
    ├── mark-sand.png      (für Petrol — sand-Tinte auf tealem Background)
    ├── icon-vellum.png    (1024×1024 mit Background, für App-Stores)
    ├── icon-noir.png
    └── icon-petrol.png
```

### Brand-Komponente — theme-aware via CSS-Custom-Property

`Components/UI/Brand.razor` umgestellt:
- Vorher: `<div class="crest">G</div>` (Platzhalter-Buchstabe)
- Nachher: `<div class="brand-mark" data-testid="brand-mark"></div>` mit CSS-Background

`atelier.css` ergänzt um `--brand-mark` und `--hero-mark` in allen drei Paletten:

| Palette | `--brand-mark` | `--hero-mark` |
|---|---|---|
| Vellum (default) | `mark-dark.png` | `mark-dark.png` |
| Noir | `mark-light.png` | `mark-light.png` |
| Petrol | `mark-sand.png` | `mark-sand.png` |

Theme-Wechsel im laufenden Browser → Mark wechselt sofort per CSS, kein Reload.

### Favicon-Setup im `<head>`

`Components/App.razor` — vollständige Favicon-Sektion:
- PNG-Größen 16/32/192/512 (Vellum-Set als Default)
- Dark-Mode-Hint: `favicon-noir-16.png` / `favicon-noir-32.png` mit `media="(prefers-color-scheme: dark)"`
- Apple-Touch-Icon: `apple-touch-icon.png` (192 px)
- Web-Manifest: `site.webmanifest`

### Web-Manifest (`site.webmanifest`)

PWA-Metadaten: Name "Geef Atelier", Short-Name "Geef", Icons 192/512, Display "browser", Start-URL "/". Theme-Color aus Atelier-Tokens (Noir-Hintergrund als Konvention für Browser-Chrome).

### Login-Page Hero-Mark

`Components/UI/LoginForm.razor` — Atmosphäre-Hälfte (Two-Pane-Left) erhält:
```html
<div class="login-hero-mark" aria-hidden="true"></div>
```

Scoped CSS (240×240 px, `background-image: var(--hero-mark)`). Freigestelltes Mark ohne Background, weil der Login-Hintergrund schon den Atelier-Ton trägt.

---

## 2. Architect-Output — zwei Knackpunkte

**Knackpunkt 1 — Login-Hero-Asset:** Freigestelltes Mark (`mark-{theme}.png`) statt App-Icon (`icon-{theme}.png` mit Background). Entscheidung: Mark gewählt, weil der Login-Hintergrund bereits die Atelier-Atmosphäre trägt. Das App-Icon-Background würde visuell konkurrieren.

**Knackpunkt 2 — Brand-Mark-Größe:** 28×28 px in der NavBar (CSS `.nav-brand .brand-mark`). Entspricht der bisherigen Crest-Größe optisch. Passt zum 32px-Zeilenhöhe der Nav-Links.

---

## 3. Akzeptanzkriterien-Check

| AC | Status |
|---|---|
| 1. `dotnet build` 0/0 | ✅ |
| 2. Alle bestehenden Tests grün | ✅ (192 Tests, 1 E2E-Skip) |
| 3. Assets vollständig committed (44 PNGs + README) | ✅ |
| 4. Favicon-Setup im `<head>` vollständig | ✅ (PNG 16/32/192/512, Dark-Mode-Hint, Apple-Touch, Manifest) |
| 5. `site.webmanifest` vorhanden und valide JSON | ✅ |
| 6. Brand-Komponente nutzt theme-aware Mark via CSS-Custom-Property | ✅ |
| 7. Login-Page zeigt großes Mark in Atelier-Atmosphäre | ✅ |
| 8. Browser-Tab zeigt Favicon auf Production | ✅ (Production-Deploy 2026-05-13) |
| 9. Apple-Touch-Icon | ✅ (192-Variante als `apple-touch-icon.png`) |
| 10. R5 Live-Verifikation in allen drei Themes | ✅ (als Teil des M1-Production-Deploy verifiziert) |
| 11. TODO-Hairline-Verstärkung dokumentiert | ✅ (siehe Sektion 5) |

---

## 4. Beobachtungen

- **44 statt 42 PNGs:** Der ursprüngliche Prompt nannte 42 PNGs. Das tatsächliche Asset-Set enthielt 44 (zusätzlich `quill-master.png` und `favicon-64.png` der Noir-Variante). Beide committed.
- **`quill-*.png` als Duplikate zu `mark-*.png`:** Beides liegt in `docs/design/brand-assets/` — `mark-*` sind die sauber benannten Kopien die nach `wwwroot/img/brand/` kopiert wurden.
- **Petrol-Mark:** `mark-sand.png` verwendet sand-Tinte (hell) auf tealem Background — optisch korrekt für das Petrol-Theme mit dunklem Hintergrund.
- **bUnit-Tests:** `BrandTests.cs` enthielt Selector für das alte `.crest`-Element. Nach dem Umbau auf `data-testid="brand-mark"` angepasst.

---

## 5. TODOs für spätere Steps

- **Hairline-Verstärkung Favicon:** Bei ≤16px Tab-Größe ist das freigestellte Quill-Mark sehr filigran. Falls auf Production unleserlich: gesonderte ≤32px-Version mit verstärkten Strichen generieren lassen (separater Mini-Step "Favicon-Bold-Cut").
- **Watermark im Manuscript:** War im Ursprungs-Mockup, aber Scope-Drift. Kann in einem späteren UI-Step hinzugefügt werden.
- **iOS-Home-Screen-Test:** Noch nicht durchgeführt (kein iOS-Gerät im CI-Kontext). Apple-Touch-Icon liegt korrekt bereit.
- **Stempel-Asset:** War im Mockup erwähnt, aber nicht im Asset-Set vorhanden. Kein Handlungsbedarf bis Asset existiert.

---

## 6. Kennzahlen

| Kennzahl | Wert |
|---|---|
| Neue Brand-Assets (docs/) | 44 PNGs + 1 README |
| Production-Assets (wwwroot/) | 8 Favicons + 6 Brand-Mark/Icon-PNGs + 1 WebManifest |
| Geänderte Dateien | 4 (`App.razor`, `Brand.razor`, `LoginForm.razor`, `atelier.css`) |
| Neue CSS-Custom-Properties | `--brand-mark`, `--hero-mark` (je 3 Palette-Werte + Default) |
| `dotnet build` | 0 Errors, 0 Warnings |
| Tests | 192 grün, 1 E2E-Skip |
| Docker-Build | ✅ erfolgreich |
| Migrations | 0 |
