# Claude-Code-Prompt: Release M1 — Merge auf main + Production-Deploy

*Release-Step, kein Entwicklungs-Step. Mergt Branch `feat/brand-asset-integration` (mit Brand-Assets + PS-6) auf main, koordiniert Production-Deploy mit den ausstehenden CLI-Proxy-Schritten aus PS-4, und schließt die Live-Verifikations-Lücken für Brand-Assets.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Der `feat/brand-asset-integration`-Branch enthält jetzt zwei zusammengehörige Liefereinheiten: Brand-Asset-Integration und PS-6 Crew-UI. PR #1 ist gegen `main` offen. Deine Aufgabe ist **Release M1** — Branch-Inhalt sauber auf `main` mergen, Production-Server aktualisieren mit allen ausstehenden Operations-Schritten, Live-Verifikation der drei offenen ACs (Brand-Assets in 3 Themes, PS-4 CLI-Proxy-Auth, PS-5 Migration-Auto-Run).

Das ist **kein klassischer Entwicklungs-Step** — keine 4 Phasen, kein vollständiger 5-Reviewer-Pass. Stattdessen eine Release-Choreografie: Pre-Merge-Sanity, Merge-Operation, Deploy-Operation, Live-Verifikation, Sammelbericht.

**Was Claude Code macht:** Pre-Merge-Sanity, PR-Update, ggf. Merge per `gh` CLI, Sammelbericht.  
**Was Stefan macht:** finale PR-Approval, Production-Deploy-Befehle auf dem Hetzner-Server, R5-Live-Klicks.

Klare Trennung — Claude Code soll **niemals SSH auf den Production-Server** ausführen oder versuchen, dort Dinge auszuführen.

## Vorgehen

Vier kompakte Phasen, statt der üblichen vier Workflow-Phasen:

1. **Pre-Merge-Sanity** — lokal verifizieren dass alles bereit ist
2. **Merge-Operation** — PR-Beschreibung aktualisieren, Merge ausführen
3. **Production-Deploy-Anleitung bereitstellen** — als Bash-Snippets für Stefan
4. **Sammelbericht** — über Brand + PS-6 + Live-Verifikations-Status

## Pflicht-Lektüre fürs Grounding

1. **`docs/reports/post-skeleton-04-cli-adapter-report.md`** — Sektion 8 "TODO für Production-Deploy" mit den ausstehenden Bash-Schritten
2. **`docs/reports/post-skeleton-05-crew-foundation-report.md`** — Migration `Step10CrewSystem` Eigenschaften (läuft auto beim Container-Start)
3. **`docs/reports/post-skeleton-06-crew-ui-report.md`** — Sektion 5 Pre-Mortem-Punkt "RunOrchestratorService Status-Update-Bug" (pre-existing, nicht in M1)
4. **`docs/reports/brand-asset-integration-report.md`** — AC10 Live-Verifikation als noch offen
5. **`docker-compose.yml`** — aktueller Stand der Services (web, postgres, postgres-backup, cli-proxy)
6. **`.env.example`** falls vorhanden — welche Env-Vars haben sich geändert seit dem letzten Deploy

## Was im Branch enthalten ist

`feat/brand-asset-integration` umfasst zwei Liefereinheiten:

**Liefereinheit A: Brand-Asset-Integration** (5 Commits)
- 42 PNGs in `docs/design/brand-assets/`
- Production-Assets in `wwwroot/img/brand/` und `wwwroot/favicon-*.png`
- `Brand.razor` umgestellt auf `.brand-mark` mit CSS-Custom-Property
- `Login.razor` mit Hero-Mark
- `site.webmanifest`
- Favicon-Setup im `App.razor`-Head

**Liefereinheit B: PS-6 Crew-UI** (vermutlich 10-15 Commits)
- 8 neue UI-Komponenten (Modal, CrewBadge, CrewSelector, CrewSummary, ReviewerPicker, ProfileEditorForm, DeleteConfirmationModal)
- 7 neue Pages unter `/crew`
- `IProviderCatalog`-Service
- `CrewSnapshot.Deserialize`-Helper
- `ReviewerDisplay`-Erweiterungen
- 35 neue bUnit-Tests
- Bugfix: `clarity`-Profile-Modell `openai/gpt-5.5-mini` → `openai/gpt-4o-mini`

**Gesamtstand:** 189 Tests grün, 1 E2E-Skip, 0 Build-Warnings.

## Konkrete Schritte

### Phase 1 — Pre-Merge-Sanity (lokal)

Vor dem Merge final verifizieren:

```bash
# Branch aktuell holen
git fetch origin
git checkout feat/brand-asset-integration
git pull --ff-only

# Final-Build und Tests
dotnet build
dotnet test

# Docker-Build prüfen (alle drei Services)
docker compose build

# Berichte committed?
ls docs/reports/post-skeleton-06-crew-ui-report.md
ls docs/reports/brand-asset-integration-report.md
ls docs/reports/post-skeleton-05-crew-foundation-report.md
# (post-skeleton-05 sollte schon auf main sein, aber prüfen)

# Decisions-Log aktuell?
grep -E "D-0(28|29)" docs/05-decisions-log.md
# D-028 (PS-5) und D-029 (PS-6) müssen vorhanden sein
```

Wenn etwas fehlt: vor dem Merge ergänzen, **nicht** nach dem Merge.

### Phase 2 — PR #1 Beschreibung aktualisieren

Der ursprüngliche PR-Titel war vermutlich "Brand Asset Integration". Da jetzt auch PS-6 dabei ist, sollte der Titel/Beschreibung das reflektieren.

**Neuer PR-Titel:** `Release M1: Brand-Asset-Integration + PS-6 Crew-UI`

**Neue PR-Beschreibung** (Markdown, in `gh pr edit` oder GitHub-UI):

```markdown
## Release M1

Zwei zusammengehörige Liefereinheiten in einem Release.

### Liefereinheit A: Brand-Asset-Integration

- 42 PNGs Brand-Assets (Master-Mark in 3 Tintefarben, App-Icons, Favicons in 9 Größen × 3 Varianten)
- Theme-aware Brand-Mark via CSS-Custom-Property (Vellum/Noir/Petrol)
- Komplettes Favicon-Setup im HTML-Head, Apple-Touch-Icon, Web-Manifest
- Login-Page Hero-Mark
- Detail-Bericht: `docs/reports/brand-asset-integration-report.md`

### Liefereinheit B: PS-6 Crew-UI

- 8 neue UI-Komponenten + 7 neue Pages unter `/crew`
- CrewSelector auf NewRun-Page, CrewSummary auf RunDetail, CrewBadge in RunRow
- Vollständige CRUD-Seiten für Reviewer-Profile, Executor-Profile, Crew-Templates
- System-Profile-Schutz, "Duplicate as custom"-Pattern
- Bugfix: `clarity`-Profile auf `openai/gpt-4o-mini` (vorher nicht-existentes `openai/gpt-5.5-mini`)
- Detail-Bericht: `docs/reports/post-skeleton-06-crew-ui-report.md`

### Tests & Build

- 189 Tests grün, 1 E2E-Skip (ThemeSwitcher, Browser nicht im Testcontainer)
- `dotnet build` 0 Errors, 0 Warnings
- Docker-Build erfolgreich für alle Services

### Migration

`Step10CrewSystem` (aus PS-5, läuft auto beim Container-Start):
- Drei neue Tabellen (ReviewerProfiles, ExecutorProfiles, CrewTemplates)
- `Runs`-Tabelle erweitert um CrewTemplateName + CrewSnapshot
- Historische Runs auf `klassik` migriert
- Reviewer-Namen umbenannt (`BriefingTreueReviewer` → `briefing-fidelity` etc.)

### Production-Deploy

Siehe `docs/reports/release-m1-merge-report.md` Sektion "Production-Deploy-Anleitung" für die Bash-Schritte.

### Pre-existing Bugs außerhalb dieses Releases

- RunOrchestratorService: Run bleibt in "Running" bei HTTP-400 vom Reviewer (eigener Bug-Fix-Step folgt)

### Reviewer-Notiz

Squash-Merge **nicht** empfohlen — fein-granulare Commit-Historie wertvoll für Bisecting. Merge-Commit oder Rebase bevorzugt.
```

**Aktualisierung mit `gh` CLI** falls verfügbar:

```bash
gh pr edit 1 --title "Release M1: Brand-Asset-Integration + PS-6 Crew-UI" \
             --body "$(cat <<'EOF'
[Beschreibung wie oben]
EOF
)"
```

Falls `gh` nicht verfügbar: Markdown-Output in den Bericht legen, Stefan macht es manuell.

### Phase 3 — Merge ausführen

**Empfohlene Strategie: Merge-Commit** (nicht Squash, nicht Rebase).

Begründung: 15+ fein-granulare Commits im Branch repräsentieren die Schritt-für-Schritt-Implementierung. Squash würde alles in einen Klumpen-Commit pressen, was die Bisecting-Fähigkeit verliert. Rebase wäre auf öffentlichem Branch ggf. riskant.

**Mit `gh` CLI:**

```bash
gh pr merge 1 --merge --delete-branch
# --merge: Merge-Commit (nicht --squash, nicht --rebase)
# --delete-branch: Branch nach Merge löschen (lokal und remote)
```

**Alternativ über GitHub-UI:** "Create a merge commit" wählen, **nicht** "Squash and merge".

**Post-Merge-Cleanup lokal:**

```bash
git checkout main
git pull --ff-only
git branch -d feat/brand-asset-integration  # lokal löschen, falls nicht schon weg
```

### Phase 4 — Production-Deploy-Anleitung

**Das macht Stefan auf dem Hetzner-Server**, nicht Claude Code. Der Step liefert die Anleitung als kopierbare Bash-Snippets im Bericht.

**Pre-Deploy-Check:**

```bash
ssh hetzner  # oder direkt auf den Server SSH-en
cd /srv/docker/geef-atelier
git status
git pull --ff-only
```

**.env-Anpassung** (falls noch nicht aus PS-4 erledigt):

```bash
# Sicherheits-Backup
cp .env .env.backup-$(date +%Y%m%d)

# Variable umbenennen
sed -i 's/^LLM_API_KEY=/LLM_OPENROUTER_API_KEY=/' .env

# Verify
grep -E '^LLM_(API_KEY|OPENROUTER_API_KEY)' .env
# Sollte LLM_OPENROUTER_API_KEY=<wert> zeigen, kein LLM_API_KEY
```

**Build und Deploy:**

```bash
# Images neu bauen mit aktuellen Sources
docker compose build --no-cache cli-proxy web

# Stack neu starten
docker compose up -d

# Status prüfen
docker compose ps
# Erwartet: web, postgres, postgres-backup, cli-proxy alle "Up (healthy)"
```

**CLI-Auth-Login** (einmalig, falls noch nicht aus PS-4 erledigt):

```bash
docker exec -it geef-atelier-cli-proxy claude auth login
# Browser-Flow folgen, Token wird in /auth/ persistiert

docker exec -it geef-atelier-cli-proxy codex auth login
# Analog
```

**Migration läuft automatisch** beim Container-Start — keine manuelle Action nötig. Verifikation:

```bash
docker exec geef-atelier-web dotnet ef migrations list \
  --project src/Geef.Atelier.Web \
  --connection "Host=postgres;..."
# Sollte Step10CrewSystem als Applied zeigen

# Alternativ: in Postgres direkt prüfen
docker exec geef-atelier-postgres psql -U atelier -d atelier -c \
  "SELECT \"MigrationId\" FROM __EFMigrationsHistory ORDER BY 1 DESC LIMIT 5;"
```

**Health-Check:**

```bash
# CLI-Proxy
docker exec geef-atelier-cli-proxy curl -s http://localhost:8090/health

# Web
curl -I https://geef.stefan-bechtel.de/
# Erwartet HTTP 200 oder 302 → /login
```

### Phase 5 — Live-Verifikation (R5)

**Das macht Stefan im Browser**. Checkliste:

**AC10 (Brand-Assets) in allen drei Themes:**

- [ ] Login-Page: Master-Quill ist sichtbar in `palette-vellum` (Default)
- [ ] Theme via UserMenu auf Noir wechseln → Logo wechselt zu heller Tinte
- [ ] Theme auf Petrol → Logo zu sand-Tinte
- [ ] NavBar-Brand-Mark in allen drei Themes korrekt
- [ ] Favicon im Browser-Tab erkennbar (bei 16-32px)
- [ ] Bei Dark-Mode-Browser: Noir-Variante im Tab (sofern Browser-Feature unterstützt wird)

**AC4 PS-4 (CLI-Proxy-Health):**

- [ ] `/health`-Endpoint liefert HTTP 200 mit `cli_status: { claude: "ready", codex: "ready" }`

**PS-6 Crew-UI-Sanity (zusätzlich zu bereits passendem AC12):**

- [ ] `/crew` rendert ohne Errors
- [ ] `/crew/templates` zeigt mindestens "Klassik" als System-Template
- [ ] `/crew/profiles/reviewers` zeigt mindestens `briefing-fidelity` und `clarity`
- [ ] Custom-Profile anlegen → in der Liste sichtbar mit `custom-` Prefix
- [ ] Custom-Template mit dem neuen Profile anlegen → in der Liste sichtbar
- [ ] NewRun mit Custom-Template submitten → erfolgreich
- [ ] RunDetail zeigt CrewSummary mit korrekten Profile-Namen und Modellen

**Hairline-Verstärkungs-Test:**

- [ ] Browser-Tab-Favicon bei 16px im Tab — Mark erkennbar oder grauer Klecks?
  - Wenn erkennbar: ✅ fertig
  - Wenn Klecks: TODO-Folge-Step "Favicon-Bold-Cut" notieren

### Phase 6 — Sammelbericht

Bericht nach `docs/reports/release-m1-merge-report.md`. Inhalt:

1. **Was wurde gemerged** — Beide Liefereinheiten mit Commit-Counts
2. **Pre-Merge-Sanity-Resultate** — Build, Tests, Docker
3. **PR-Operation** — wann/wie gemerged, von wem
4. **Production-Deploy-Anleitung** — alle Bash-Snippets aus Phase 4 als kopierbare Blocks
5. **Live-Verifikations-Status** — Tabelle mit allen Checklist-Items
6. **Offene Punkte nach M1** — Stefans manuelle Schritte, hairline-Risk, pre-existing Bug
7. **Empfehlungen für nächsten Step** — PS-7 (Advisor) oder Bug-Fix-Step für Run-Status-Bug

## Akzeptanzkriterien

1. **Pre-Merge-Sanity grün** — `dotnet build`, `dotnet test`, `docker compose build` alle erfolgreich auf dem Branch.
2. **PR #1 aktualisiert** — Titel und Beschreibung reflektieren Brand + PS-6.
3. **PR #1 gemerged auf main** — via Merge-Commit (nicht Squash). Lokaler + Remote-Branch gelöscht.
4. **Production-Deploy-Anleitung im Bericht** — alle Bash-Snippets in copy-paste-fähigem Format.
5. **Stefans Production-Deploy-Schritte ausgeführt** — `.env`-Anpassung, Build, Restart, CLI-Auth, Health-Check.
6. **Live-Verifikation in allen drei Themes** — Brand-Mark wechselt korrekt, Favicons sichtbar, Crew-UI funktional.
7. **Pre-existing Bug dokumentiert** — Run-Status-bei-HTTP-400 als Folge-Step notiert.
8. **Sammelbericht committed** — auf main direkt nach Merge.

## Architect-Konsultation — zwei Knackpunkte

1. **GitHub-Workflow ab jetzt:** Bisher direct-push auf main (vermutlich), jetzt erste PR-Erfahrung. Soll dieser Workflow ab jetzt **etabliert** werden (alle Steps gehen über PRs)? Empfehlung: **Ja, für Steps mit Migrations oder strukturellen Eingriffen.** Side-Steps und Bugfixes können direct-push bleiben. Architect bestätigt im Bericht.

2. **`gh` CLI-Verfügbarkeit:** Falls `gh` nicht im Atelier-Container/Dev-Umgebung verfügbar ist, übergibt Claude Code die Operations an Stefan via Bericht-Snippets. Architect prüft Verfügbarkeit in Phase 1.

## Was du in diesem Step NICHT tust

- **Keinen pre-existing Bug fixen** — Run-Status-bei-HTTP-400 ist eigener Step nach M1.
- **Keine neuen Features** — reines Release.
- **Keine Hairline-Verstärkung am Favicon** — falls nötig, eigener Mini-Step nach Live-Test.
- **Keine Hetzner-SSH-Operationen** — Stefan macht das.
- **Keine Bericht-Generierung für PS-7** — kommt separat.

## Reviewer-Hinweise

Reduzierter Reviewer-Pass für Release-Step:

- **R1 (Functional Correctness):** Alle 8 ACs prüfen. Besonders 5 (Production-Deploy) und 6 (Live-Verifikation).
- **R3 (Test Execution):** Tests waren grün vor Merge — keine erneuten Tests nach Merge nötig, weil reine Merge-Operation.
- **R5 (Live Sanity):** Browser-Test auf Production in allen drei Themes — das ist der Hauptcheck.

R2 und R4 entfallen — kein Code-Quality- oder Architecture-Eingriff.

## Konventionen

- Bericht-Sprache: **Deutsch**.
- Commit-Messages für den Merge-Commit: deutsche Beschreibung im Stil `merge: M1 (Brand + PS-6) into main`.
- Production-Deploy-Snippets im Bericht: **Bash**, klar kommentiert auf Deutsch.
- **Niemals Secrets** im Bericht (kein API-Key, kein Hash, keine Tokens).

Wenn du soweit bist: starte mit Phase 1 (Pre-Merge-Sanity). Erwarteter Aufwand: 1-2 Stunden für Claude-Code-Anteile, plus Stefans Production-Deploy-Zeit.

---

**Nach erfolgreichem Abschluss:** `main` ist auf Stand mit Brand-Assets, PS-6 Crew-UI, und der bisherigen Roadmap-Investition. Production läuft mit aktivem CLI-Proxy (Subscription-Nutzung), neuer Crew-Verwaltung, neuem Atelier-Look. Atelier ist erstmals "Feature-vollständig" für das Vision-Ziel "Text-Manufaktur mit verschiedenen Crews". Nächster Schritt: PS-7 (Advisor-Pässe) oder Bug-Fix-Sprint für Run-Status-Edge-Cases.