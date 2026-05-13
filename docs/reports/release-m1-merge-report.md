# Release M1 — Merge-Bericht

Datum: 2026-05-13  
Autor: Claude Code (claude-sonnet-4-6) im Auftrag von Stefan Bechtel

---

## 1. Was wurde gemerged

**PR #1 — "Release M1: Brand-Asset-Integration + PS-6 Crew-UI"**  
Branch: `feat/brand-asset-integration` → `main`  
Merge-Commit: `9e94384c0bb4669b5d156c889fbc968684af36d9`  
Zeitpunkt: 2026-05-13T11:02:54Z  
Strategie: **Merge-Commit** (kein Squash, kein Rebase)

**Commit-Statistik im Branch:**
| Liefereinheit | Commits |
|---|---|
| PS-5 Crew-Foundation (Vorarbeit) | ~12 Commits |
| Brand-Asset-Integration | 5 Commits |
| PS-6 Crew-UI | 8 Commits |
| PS-6 Dokumentation + Bericht | 1 Commit |
| Brand-Asset-Bericht (nachgezogen) | 1 Commit |
| **Gesamt (ohne Merge-Commit)** | ~27 Commits auf Branch |

### Liefereinheit A: Brand-Asset-Integration

- 44 PNGs in `docs/design/brand-assets/` (Source-of-Truth)
- 6 Production-Brand-Assets in `wwwroot/img/brand/` (mark-dark/light/sand, icon-vellum/noir/petrol)
- 8 Favicon-Dateien in `wwwroot/` (PNG 16/32/192/512, Noir-Dark-Mode-Hint, Apple-Touch)
- `site.webmanifest` (PWA-Metadaten)
- `Brand.razor` umgestellt auf theme-aware `--brand-mark` CSS-Custom-Property
- `LoginForm.razor` mit Hero-Mark (240×240 px, freigestelltes Quill)
- `App.razor` mit vollständiger Favicon-Sektion

### Liefereinheit B: PS-6 Crew-UI

- 8 neue UI-Komponenten (Modal, CrewBadge, CrewSelector, CrewSummary, ReviewerPicker, ProfileEditorForm, DeleteConfirmationModal)
- 7 neue Blazor-Pages unter `/crew` (CrewIndex, CrewTemplatesIndex, CrewTemplateEditor, ReviewerProfilesIndex, ReviewerProfileEditor, ExecutorProfilesIndex, ExecutorProfileEditor)
- 3 thin Backend-Ergänzungen: `CrewSnapshot.Deserialize()`, `IProviderCatalog`, `ReviewerDisplay`-Helper
- 35 neue bUnit-Tests (6 Test-Dateien)
- Bugfix: `SystemCrew.ClarityProfile.Model` — `openai/gpt-5.5-mini` → `openai/gpt-4o-mini`

---

## 2. Pre-Merge-Sanity-Resultate

| Prüfpunkt | Ergebnis |
|---|---|
| `git pull --ff-only` | ✅ Branch war aktuell |
| `dotnet build` | ✅ 0 Errors, 0 Warnings |
| `dotnet test` | ✅ 192 passed, 1 skipped (ThemeSwitcher-E2E) |
| `docker compose build --no-cache web` | ✅ Image `geef-atelier-web:latest` gebaut |
| `docs/reports/post-skeleton-06-crew-ui-report.md` | ✅ vorhanden |
| `docs/reports/brand-asset-integration-report.md` | ✅ erstellt + committed vor Merge |
| `docs/reports/post-skeleton-05-crew-foundation-report.md` | ✅ vorhanden |
| D-028 + D-029 in `docs/05-decisions-log.md` | ✅ beide vorhanden |

**Nachgezogen vor Merge:** `brand-asset-integration-report.md` fehlte — erstellt und in einem separaten Commit auf dem Branch committed (`0d73270`), dann gemergt.

---

## 3. PR-Operation

| Schritt | Details |
|---|---|
| PR-Titel aktualisiert | "feat: Brand-Asset-Integration (Side-Step)" → "Release M1: Brand-Asset-Integration + PS-6 Crew-UI" |
| PR-Beschreibung | Beide Liefereinheiten beschrieben, Tests, Migration, pre-existing Bug |
| Merge-Methode | Merge-Commit (nicht Squash) — Commit-Historie bleibt für Bisecting erhalten |
| Branch-Löschung | `feat/brand-asset-integration` remote + lokal gelöscht |
| Merge-Zeitpunkt | 2026-05-13T11:02:54Z |

---

## 4. Production-Deploy-Anleitung

**Das führt Stefan auf dem Hetzner-Server aus.** Claude Code führt niemals SSH auf Production aus.

### Pre-Deploy-Check

```bash
ssh hetzner  # oder direkte SSH auf den Server
cd /srv/docker/websites/geef_atelier

# Stand prüfen
git status
git pull --ff-only  # oder: git fetch && git reset --hard origin/main
```

### .env-Anpassung (einmalig, falls noch aus PS-4 ausstehend)

```bash
# Sicherheits-Backup
cp .env .env.backup-$(date +%Y%m%d)

# Env-Variable umbenennen
sed -i 's/^LLM_API_KEY=/LLM_OPENROUTER_API_KEY=/' .env

# Verify
grep -E '^LLM_(API_KEY|OPENROUTER_API_KEY)' .env
# Erwartet: LLM_OPENROUTER_API_KEY=<wert>  (kein LLM_API_KEY mehr)
```

### Build und Deploy

```bash
# Images neu bauen (--no-cache verhindert veraltete Layer)
docker compose build --no-cache cli-proxy web

# Stack neu starten (Migration läuft automatisch beim Container-Start)
docker compose up -d

# Status prüfen
docker compose ps
# Erwartet: web, postgres, postgres-backup, cli-proxy alle "Up (healthy)"
```

### Migration-Verifikation (optional)

Die `Step10CrewSystem`-Migration läuft automatisch. Zur Kontrolle:

```bash
# In Postgres direkt prüfen
docker exec geef-atelier-postgres psql -U geef_atelier -d geef_atelier -c \
  "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1 DESC LIMIT 5;"
# Step10CrewSystem sollte als applied erscheinen
```

### CLI-Authentifizierung (einmalig, falls noch aus PS-4 ausstehend)

```bash
# Claude CLI
docker exec -it geef-atelier-cli-proxy claude auth login
# Browser-Flow folgen; Token wird in /auth/ Volume persistiert

# Codex CLI (falls vorhanden)
docker exec -it geef-atelier-cli-proxy codex auth login
```

### Health-Checks

```bash
# Web-Endpoint
curl -I https://geef.stefan-bechtel.de/
# Erwartet: HTTP 302 → /login (oder HTTP 200 wenn eingeloggt)

# CLI-Proxy
docker exec geef-atelier-cli-proxy curl -s http://localhost:8090/health
# Erwartet: {"status": "healthy", "cli_status": {...}}
```

---

## 5. Live-Verifikations-Checkliste

**Das macht Stefan im Browser.**

### Brand-Assets (AC10 Brand-Asset-Integration)

- [ ] Login-Page: Master-Quill sichtbar in `palette-vellum` (Default)
- [ ] Theme via UserMenu auf Noir wechseln → Mark wechselt zu heller Tinte
- [ ] Theme auf Petrol → Mark wechselt zu sand-Tinte
- [ ] NavBar-Brand-Mark in allen drei Themes korrekt (28×28 px, kein Buchstabe "G")
- [ ] Browser-Tab zeigt Favicon (sichtbar bei 16-32px Tab-Größe)
- [ ] Bei Dark-Mode-Browser: Noir-Variante im Tab-Favicon
- [ ] `site.webmanifest`: Browser DevTools → Application → Manifest → Metadaten korrekt

### CLI-Proxy (AC4+6 PS-4, falls noch ausstehend)

- [ ] `docker compose ps` zeigt cli-proxy als healthy
- [ ] `/health`-Endpunkt: HTTP 200 mit `cli_status: { claude: "ready", ... }`

### PS-6 Crew-UI-Sanity

- [ ] `/crew` rendert ohne JavaScript-Fehler
- [ ] `/crew/templates` zeigt Klassik als System-Template (Edit/Delete disabled, Duplicate aktiv)
- [ ] `/crew/profiles/reviewers` zeigt `briefing-fidelity` und `clarity`
- [ ] Neues Custom-Reviewer-Profil anlegen → erscheint mit `custom-`-Prefix in der Liste
- [ ] Custom-Template anlegen → in CrewSelector auf `/new` auswählbar
- [ ] Briefing mit Custom-Template einreichen → Run startet, RunDetail zeigt CrewSummary
- [ ] Theme-Wechsel auf jeder Crew-Seite → alle neuen Komponenten rendern korrekt in Vellum/Noir/Petrol

### Favicon-Qualität

- [ ] Bei ≤16px Tab-Größe: Mark erkennbar? → Falls grauer Klecks: TODO-Hairline-Verstärkung notieren

---

## 6. Offene Punkte nach M1

### Stefans manuelle Schritte (Production-Server)

1. `git pull` auf dem Hetzner-Server
2. `.env`-Anpassung (`LLM_API_KEY` → `LLM_OPENROUTER_API_KEY`) falls noch nicht aus PS-4
3. `docker compose build --no-cache cli-proxy web && docker compose up -d`
4. CLI-Auth-Login falls noch nicht aus PS-4
5. Live-Verifikation der Checkliste aus Sektion 5

### Pre-existing Bug (nicht in M1 gefixed)

**RunOrchestratorService Status-Update-Bug:** Wenn ein Reviewer mit HTTP-400 antwortet, bleibt der Run im Status "Running" anstatt auf "Failed" gesetzt zu werden. Dies ist ein pre-existing Bug, der schon vor PS-6 bekannt war. Eigener Bug-Fix-Step nach M1.

### Mögliche Folge-Steps

- **PS-7 (Advisor-Pässe):** AdvisorProfile-Schema aus PS-5 aktivieren, `/crew/profiles/advisors`-Page, Advisor-Ausführung vor dem Executor-Pass.
- **Run-Status-Bug-Fix:** `RunOrchestratorService` bei HTTP-400 robust auf "Failed" setzen.
- **Favicon-Bold-Cut:** Falls Favicon bei ≤16px unleserlich — schärfere Variante generieren lassen (separater Mini-Step).

### Workflow-Empfehlung ab M1

Aus dem Architect-Knackpunkt im M1-Prompt: **PRs für Steps mit Migrations oder strukturellen Eingriffen** empfohlen. Side-Steps und Bugfixes können weiterhin als Direct-Push auf `main` laufen. Für PS-7 und größere Steps → neuer Feature-Branch + PR.

---

## 7. Kennzahlen

| Kennzahl | Wert |
|---|---|
| PR-Nummer | #1 |
| Merge-Commit | `9e94384c` |
| Branch | `feat/brand-asset-integration` (gelöscht nach Merge) |
| Commits im Branch | ~27 (exkl. Merge-Commit) |
| Tests (post-Merge main) | 192 grün, 1 E2E-Skip |
| Build | 0 Errors, 0 Warnings |
| Brand-Assets (docs/) | 44 PNGs + 1 README |
| Neue UI-Komponenten (PS-6) | 8 Komponenten + 7 Pages |
| Neue bUnit-Tests (PS-6) | 35 (6 Dateien) |
| Migrations | 1 (Step10CrewSystem, auto beim Container-Start) |
| Bugfix | `openai/gpt-5.5-mini` → `openai/gpt-4o-mini` |
