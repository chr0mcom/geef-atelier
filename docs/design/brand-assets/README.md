# Brand Assets — Geef.Atelier

Source-of-Truth für alle Brand-Assets. 42 PNGs aus der initialen Brand-Generierungs-Session (Mai 2026).

## Asset-Gruppen

### Master-Mark (Quill)

Freigestellte Feder/Quill in drei Tintefarb-Varianten (1254×1254 px, transparenter Hintergrund):

| Datei | Variante | Verwendung |
|---|---|---|
| `quill-dark.png` | Dunkle Tinte | Vellum-Palette (heller Hintergrund) |
| `quill-light.png` | Helle Tinte | Noir-Palette (dunkler Hintergrund) |
| `quill-sand.png` | Sand-Tinte | Petrol-Palette (dunkler Hintergrund) |
| `quill-master.png` | Master | Referenz, nicht direkt verwendet |

### App-Icons (1024×1024 px, mit Hintergrund)

| Datei | Palette |
|---|---|
| `icon-vellum.png` | Vellum (hell, warm) |
| `icon-noir.png` | Noir (dunkel, amber) |
| `icon-petrol.png` | Petrol (dunkel, cyan) |

### Favicon-Sets

Drei Sets in den Größen 16/24/32/48/64/96/128/192/256/512 px:

| Prefix | Beschreibung |
|---|---|
| `favicon-vellum-*.png` | Vellum-Palette — **Default** |
| `favicon-noir-*.png` | Noir-Palette — Dark-Mode-Hint im Browser |
| `favicon-transparent-*.png` | Transparenter Hintergrund |

Direkt adressierte Größen (ohne Prefix) sind Duplikate des Vellum-Sets:

| Datei | Quelle |
|---|---|
| `favicon-16.png` | = favicon-vellum-16.png |
| `favicon-32.png` | = favicon-vellum-32.png |
| `favicon-192.png` | Genutzt als Apple-Touch-Icon |
| `favicon-512.png` | PWA-Icon |

## Theme-Mapping

| Palette | brand-mark | hero-mark |
|---|---|---|
| `palette-vellum` | `mark-dark.png` (= quill-dark) | `mark-dark.png` |
| `palette-noir` | `mark-light.png` (= quill-light) | `mark-light.png` |
| `palette-petrol` | `mark-sand.png` (= quill-sand) | `mark-sand.png` |

## Production-Asset-Ablage

Production-Assets liegen in `src/Geef.Atelier.Web/wwwroot/`:

```
wwwroot/
├── favicon-16.png          (vellum-16)
├── favicon-32.png          (vellum-32)
├── favicon-192.png
├── favicon-512.png
├── favicon-noir-16.png
├── favicon-noir-32.png
├── apple-touch-icon.png    (= favicon-192.png)
├── site.webmanifest
└── img/brand/
    ├── mark-dark.png       (= quill-dark.png)
    ├── mark-light.png      (= quill-light.png)
    ├── mark-sand.png       (= quill-sand.png)
    ├── icon-vellum.png
    ├── icon-noir.png
    └── icon-petrol.png
```

## TODOs

- **Hairline-Verstärkung ≤32px**: Aktuelles Favicon-Set bei kleinen Größen (16/24/32px) ggf. zu fein. Nach Production-Verifikation entscheiden ob nachgeschärft werden soll.
- **Stempel-Asset**: War im ursprünglichen Mockup geplant, aber nicht im generierten Set enthalten. Für späteren Design-Sprint vorgemerkt.
- **Watermark im Manuscript**: Quill-Mark als subtiles Wasserzeichen auf dem Manuskript-Paper wäre stimmig. Scope-Drift für diesen Step — separater TODO.
- **Wordmark/Schriftzug**: Programmatische Wordmark aus Fonts; keine separate Schriftzug-Grafik generiert.
