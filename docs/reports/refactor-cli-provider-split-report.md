# CLI-Provider-Split — Abschlussbericht

Datum: 2026-05-13
Autor: Claude Code (claude-opus-4-7) im Auftrag von Stefan Bechtel

---

## 1. Was wurde umgesetzt

**Refactor: CLI-Provider-Split** — Der einzelne `cli`-Provider aus PS-4 wurde in zwei explizite Provider `claude-cli` und `codex-cli` aufgeteilt. Das versteckte Model-Name-Routing im cli-proxy entfällt für neue Verbindungen. Die User-Experience im Provider-Dropdown ist jetzt klar und deterministisch.

### cli-proxy (Python/FastAPI)

- **Zwei neue Endpoints:** `POST /v1/claude/chat/completions` und `POST /v1/codex/chat/completions` — routen direkt an die jeweilige CLI, unabhängig vom `model`-Feld.
- **Legacy-Endpoint `POST /v1/chat/completions`:** Erhalten, loggt bei jedem Aufruf ein WARNING-Level-Log mit Deprecation-Hinweis.
- **Neue Hilfsfunktionen:** `_call_claude()` und `_call_codex()` kapseln die adapter-spezifischen Aufrufe ohne Routing-Logik.
- **9 neue Python-Tests** in `tests/test_explicit_endpoints.py` (30 insgesamt grün).

### Atelier-Konfiguration

- `appsettings.json`: `"cli"` → `"claude-cli"` (Endpoint `/v1/claude`) + `"codex-cli"` (Endpoint `/v1/codex`).
- Alle System-Akteure (Executor, BriefingTreueReviewer, KlarheitReviewer) bleiben auf `openrouter` — unverändert.

### DB-Migration `Step12CliProviderSplit`

- Tabellen `ReviewerProfiles`, `ExecutorProfiles`, `AdvisorProfiles`: `Provider='cli'` → `claude-cli` oder `codex-cli` anhand Model-Regex.
- `Runs.CrewSnapshot`-JSONB: Two-Pass-SQL-Replace (Details siehe Abschnitt 3).
- Migration läuft beim Container-Start automatisch.

### `IProviderCatalog`-API-Erweiterung

- Neues sealed record `ProviderInfo(string Name, string DisplayName)`.
- Methode `ListProviderNames() → IReadOnlyList<string>` ersetzt durch `ListProviders() → IReadOnlyList<ProviderInfo>`.
- `ProviderCatalog` hardcodiert DisplayNames für die drei bekannten Provider; unbekannte Provider-Namen fallen auf `Name == DisplayName` zurück.

### UI: Provider-Dropdown in `ProfileEditorForm`

- `<option value="@p.Name">@p.DisplayName</option>` statt `<option value="@p">@p</option>`.
- Neuer Hilfstext unter dem Dropdown: "OpenRouter is billed per token. Claude-CLI and Codex-CLI use a local subscription."

### Tests

- **C#:** 246 Tests grün (239 PS-7-Stand + 5 neue `ProviderCatalogTests` + 2 neue `LlmClientResolverTests`).
- **Python:** 30 Tests grün (21 bestehende + 9 neue `test_explicit_endpoints`).
- Bestehende `LlmOptionsMultiProviderTests` + `LlmClientResolverTests` auf `claude-cli`/`codex-cli` aktualisiert.

---

## 2. Architect-Entscheidungen

### Entscheidung A: Legacy-Endpoint im cli-proxy

**Fragestellung:** Entfernen (Breaking-Change, sauber) oder Erhalten mit Deprecation-Warning?

**Entscheidung: Erhalten mit WARNING-Log** (analog Prompt-Empfehlung).

Begründung: Falls ein Custom-Profil nach der DB-Migration noch `"provider":"cli"` als Wert hätte (Edge-Case bei komplexen Snapshots), würde der Legacy-Endpoint als Fallback dienen statt komplett zu scheitern. Minimaler Aufwand, hohes Safety-Net. Geplante Entfernung nach 2-3 Atelier-Versionen.

### Entscheidung B: CrewSnapshot-JSON-Migration

**Fragestellung:** SQL-basierter String-Replace vs. C#-basierter Deserialisierungs-Migration?

**Entscheidung: Two-Pass SQL-String-Replace** (einfacher, weniger C#-Code).

Two-Pass-Strategie:
1. Pass A: Snapshots mit Codex-Model-Pattern (`gpt|o[0-9]|openai/`) → `"provider":"codex-cli"`
2. Pass B: Verbleibende Snapshots mit `"provider":"cli"` → `"provider":"claude-cli"` (inkl. Unbekannte)

**Limitation:** Mixed-CLI-Snapshots (Executor=claude, Reviewer=codex im selben Run) werden in Pass A inkorrekt als rein-codex behandelt. In der Praxis existieren solche Snapshots nicht, da alle System-Akteure `openrouter` nutzen und CLI-Custom-Profile im Projekt neu sind. Die Limitation ist in D-032 dokumentiert.

---

## 3. Migration-Verifikation

### Profil-Tabellen (immer korrekt)

`UPDATE "ReviewerProfiles" SET "Provider" = CASE WHEN "Model" ~* '^(gpt|o[0-9]|openai/)' THEN 'codex-cli' ELSE 'claude-cli' END WHERE "Provider" = 'cli'`

Analog für `ExecutorProfiles` und `AdvisorProfiles`. Der `CASE`-Ausdruck auf der direkten `"Model"`-Spalte ist korrekt für alle Einzel-Profile.

### CrewSnapshot in Runs

Two-Pass SQL mit `REPLACE("CrewSnapshot"::text, ...)::jsonb`. Limitation bei Mixed-Snapshots dokumentiert (D-032 (e)). Da alle System-Crew-Profiles `openrouter` nutzen, ist die Anzahl betroffener Runs in Production voraussichtlich null.

### Edge-Cases

| Edge-Case | Behandlung |
|---|---|
| Profil mit `provider=cli` und unbekanntem Modell | → `claude-cli` (Fallback, analog Legacy-Endpoint-Verhalten) |
| Mixed-CLI-Snapshot (Executor=claude, Reviewer=codex) | Limitation: Pass A ersetzt alle als codex-cli. Betrifft in Praxis keine Runs. |
| Profil mit `provider=openrouter` | Unberührt (WHERE-Klausel filtert nur `cli`) |
| System-Profile | Haben keinen DB-Eintrag — unberührt |

---

## 4. Real-Test-Ergebnis

**Hinweis:** Der Real-Test (AC10) mit beiden CLIs über explizite Endpunkte ist ein Production-Test, der auf dem Hetzner-Server von Stefan durchzuführen ist. Anleitung in Abschnitt 6 (Production-Deploy).

**Funktionstest (lokal, Unit-Level):**
- `test_routes_to_claude_adapter_regardless_of_model_name` ✅ — `/v1/claude/...` mit `model="gpt-4o"` ruft ausschließlich `claude_adapter`
- `test_routes_to_codex_adapter_regardless_of_model_name` ✅ — `/v1/codex/...` mit `model="claude-opus-4-5"` ruft ausschließlich `codex_adapter`
- `test_legacy_endpoint_logs_deprecation_warning` ✅ — WARNING-Log bei Legacy-Aufruf nachgewiesen

---

## 5. Akzeptanzkriterien-Check

| AC | Beschreibung | Status |
|---|---|---|
| 1 | `dotnet build` 0 Errors, 0 Warnings | ✅ |
| 2 | `dotnet test` — alle bestehenden Tests grün + neue Tests | ✅ 246 grün |
| 3 | Python `pytest` grün, neue Tests bestätigen Endpoint-Trennung | ✅ 30 grün |
| 4 | `/v1/claude/chat/completions` routet immer zu claude CLI | ✅ Test-nachgewiesen |
| 5 | `/v1/codex/chat/completions` routet immer zu codex CLI | ✅ Test-nachgewiesen |
| 6 | Legacy `/v1/chat/completions` funktioniert, loggt Deprecation-Warning | ✅ Test-nachgewiesen |
| 7 | DB-Migration `Step12CliProviderSplit` läuft sauber | ✅ SQL korrekt, Limitation dokumentiert |
| 8 | CrewSnapshot in historischen Runs aktualisiert / Strategie dokumentiert | ✅ Two-Pass SQL + Limitation D-032 |
| 9 | UI Provider-Dropdown zeigt drei explizite Optionen mit Display-Namen | ✅ ProfileEditorForm aktualisiert |
| 10 | Real-Test: beide CLIs über explizite Endpunkte — cli-proxy-Log bestätigt | Ausstehend (Production-Test) |
| 11 | Decisions-Log-Eintrag D-032 | ✅ |

---

## 6. Empfehlungen

### Legacy-Endpoint-Entfernung

Geplante Entfernung von `/v1/chat/completions` nach 2-3 Atelier-Versionen (ca. PS-9 oder PS-10). Vorher Verifikation, dass keine Custom-Profile noch `cli` als Provider-Wert haben (SELECT aus DB).

### Nächste Schritte

- **PS-8 (Cookie-Auth-Erweiterung):** Unabhängig vom CLI-Split.
- **OnConvergenceFailure-Multi-Retry:** Ausstehend seit PS-7, Single-Retry-Cap bleibt bis separater Step.
- **Legacy-Endpoint-Monitoring:** Wenn Deprecation-Warning im Production-Log nie auftaucht nach dem Deploy, kann der Legacy-Endpoint früher entfernt werden.

---

## 7. Production-Deploy-Anleitung

**Das führt Stefan auf dem Hetzner-Server aus.**

```bash
ssh hetzner
cd /srv/docker/websites/geef_atelier

# Stand prüfen
git status
git pull --ff-only

# cli-proxy neu bauen (zwei neue Routen)
docker compose build --no-cache cli-proxy

# Web neu bauen (Provider-Catalog + Migration)
docker compose build --no-cache web

# Stack neu starten — Migration Step12 läuft automatisch
docker compose up -d

# Verifikation: neue Endpunkte erreichbar (erwartet 400/422, NICHT 404)
curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:8090/v1/claude/chat/completions
# → 422 (fehlender Request-Body) oder 400 (Streaming not supported), NICHT 404

curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:8090/v1/codex/chat/completions
# → 422, NICHT 404

# Migration verifizieren (optional)
docker exec geef-atelier-postgres psql -U geef_atelier -d geef_atelier -c \
  "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1 DESC LIMIT 3;"
# Step12CliProviderSplit sollte als applied erscheinen

# Real-Test: Provider-Dropdown im Browser
# /crew/profiles/reviewers → vorhandene Profile zeigen jetzt "Claude (Subscription CLI)"
# oder "Codex (Subscription CLI)" statt "cli"
```

### Real-Test (AC10)

1. Im Browser `/crew/profiles/reviewers` öffnen — vorhandene Profile haben `claude-cli` oder `codex-cli` (statt `cli`)
2. Neues Custom-Reviewer-Profil anlegen mit Provider "Claude (Subscription CLI)"
3. Brief mit diesem Reviewer einreichen
4. `docker compose logs cli-proxy | grep "Dispatching"` → erwartet: `Dispatching to claude | model=...`
5. Neues Custom-Profile mit Provider "Codex (Subscription CLI)" anlegen
6. Brief einreichen
7. `docker compose logs cli-proxy | grep "Dispatching"` → erwartet: `Dispatching to codex | model=...`

---

## 8. Kennzahlen

| Kennzahl | Wert |
|---|---|
| Branch | `refactor/cli-provider-split` |
| C#-Tests | 246 grün (+7 neue) |
| Python-Tests | 30 grün (+9 neue) |
| Build | 0 Errors, 0 Warnings |
| Neue Python-Endpunkte | 2 (`/v1/claude/`, `/v1/codex/`) |
| Neue C#-Typen | `ProviderInfo` record |
| Migration | `Step12CliProviderSplit` (Daten-only, kein Schema-Change) |
| Geänderte Dateien | ~12 |
| Decisions-Log-Eintrag | D-032 |
