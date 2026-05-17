# Brand Assets — Geef.Atelier

*[Deutsch](README_de.md) · **English***

Source of truth for all brand assets. 43 PNGs from the initial brand-generation session (May 2026).

## Asset groups

### Master mark (quill)

A cut-out quill in three ink-color variants (1254×1254 px, transparent background):

| File | Variant | Usage |
|---|---|---|
| `quill-dark.png` | Dark ink | Vellum palette (light background) |
| `quill-light.png` | Light ink | Noir palette (dark background) |
| `quill-sand.png` | Sand ink | Petrol palette (dark background) |
| `quill-master.png` | Master | Reference, not used directly |

### App icons (1024×1024 px, with background)

| File | Palette |
|---|---|
| `icon-vellum.png` | Vellum (light, warm) |
| `icon-noir.png` | Noir (dark, amber) |
| `icon-petrol.png` | Petrol (dark, cyan) |

### Favicon sets

Three sets in the sizes 16/24/32/48/64/96/128/192/256/512 px:

| Prefix | Description |
|---|---|
| `favicon-vellum-*.png` | Vellum palette — **default** |
| `favicon-noir-*.png` | Noir palette — dark-mode hint in the browser |
| `favicon-transparent-*.png` | Transparent background |

Directly addressed sizes (without a prefix) are duplicates of the Vellum set:

| File | Source |
|---|---|
| `favicon-16.png` | = favicon-vellum-16.png |
| `favicon-32.png` | = favicon-vellum-32.png |
| `favicon-64.png` | directly addressed size (Vellum set) |
| `favicon-192.png` | Used as the Apple touch icon |
| `favicon-512.png` | PWA icon |

## Theme mapping

| Palette | brand-mark | hero-mark |
|---|---|---|
| `palette-vellum` | `mark-dark.png` (= quill-dark) | `mark-dark.png` |
| `palette-noir` | `mark-light.png` (= quill-light) | `mark-light.png` |
| `palette-petrol` | `mark-sand.png` (= quill-sand) | `mark-sand.png` |

## Production asset layout

Production assets live in `src/Geef.Atelier.Web/wwwroot/`:

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

- **Hairline reinforcement ≤32px**: the current favicon set may be too fine at small sizes (16/24/32px). Decide after production verification whether it should be sharpened.
- **Stamp asset**: was planned in the original mockup but not included in the generated set. Noted for a later design sprint.
- **Watermark in the manuscript**: a quill mark as a subtle watermark on the manuscript paper would be fitting. Scope drift for this step — a separate TODO.
- **Wordmark/lettering**: a programmatic wordmark from fonts; no separate lettering graphic was generated.
