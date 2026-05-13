# Claude-Code-Prompt: CLI-Provider-Split (Refactor)

*Refactor des einzelnen `cli`-Providers aus PS-4 in zwei explizite Provider `claude-cli` und `codex-cli`. UI wird transparenter, Routing wird deterministisch.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. PS-4 hat einen einzelnen `cli`-Provider angelegt, der via Side-Container (cli-proxy) intern entscheidet, welche der beiden CLIs (`claude` oder `codex`) aufgerufen wird — anhand des Modell-Namens (`claude-*` → claude CLI, `gpt-*`/`o*` → codex CLI).

Dieses Design hat eine **UX-Schwäche**, die im Crew-UI aus PS-6 deutlich wurde: der User sieht im Provider-Dropdown nur "cli" und weiß nicht, welche Subscription tatsächlich genutzt wird, bis das Model-Feld ausgefüllt ist. Falls jemand einen ungültigen Modell-Namen einträgt, läuft das Routing möglicherweise in die falsche CLI — ohne sichtbares Feedback.

Deine Aufgabe ist der **Provider-Split**: der einzelne `cli`-Provider wird in zwei explizite Provider `claude-cli` und `codex-cli` aufgeteilt. Der cli-proxy Side-Container bekommt zwei explizite Endpoint-Pfade (`/v1/claude/chat/completions` und `/v1/codex/chat/completions`), die direkt zur jeweiligen CLI routen — keine Model-Name-Heuristik mehr.

Klein abgegrenzt: keine neuen Features, keine UI-Komponenten-Änderungen außer die Provider-Catalog-Liste, keine Domain-Modell-Änderungen. Reiner Refactor mit Migration für bestehende Custom-Profile.

## Vorgehen

**Du folgst dem Workflow in `/srv/docker/docs/geef-workflow.md`**, in **komprimierter Form** weil kleiner Refactor:
- Phase 1.1 + 1.2: Stand erfassen
- Phase 1.4 Architect: Migrations-Strategie für bestehende Custom-Profile
- Phase 2: Implementation
- Phase 3: R1 + R3 + R4 reichen (R2 leichter Pass, R5 nur Browser-Verifikation des Provider-Dropdowns)
- Phase 4: Bericht, Commit

**Branch-Empfehlung:** `refactor/cli-provider-split` mit PR (Migration drin).

## Pflicht-Lektüre fürs Grounding

1. **`docs/reports/post-skeleton-04-cli-adapter-report.md`** — Architektur der CLI-Schicht, Side-Container-Aufbau
2. **`cli-proxy/`** — der Python-Side-Container
   - `src/main.py` — FastAPI-Routes
   - `src/claude_adapter.py` und `src/codex_adapter.py` — die CLI-Wrapper
   - `tests/test_*.py` — bestehende Tests
3. **`src/Geef.Atelier.Infrastructure/Llm/LlmOptions.cs`** — die Provider-Konfiguration
4. **`src/Geef.Atelier.Application/Crew/IProviderCatalog.cs`** und Implementation — die UI-API für verfügbare Provider
5. **`appsettings.json`** und **`.env.example`** — wo die Provider-Konfiguration heute steht
6. **`docs/05-decisions-log.md`** D-018, D-027 (PS-4-Entscheidungen)

## Konkrete Anforderungen

### 1. Side-Container-Erweiterung (cli-proxy)

Zwei neue explizite Endpoint-Pfade hinzufügen:

```python
# main.py
@app.post("/v1/claude/chat/completions")
async def claude_completions(request: ChatCompletionRequest):
    # Direkt claude_adapter.complete(...) aufrufen
    # KEINE Model-Name-Heuristik, KEIN Codex-Fallback
    return await claude_adapter.complete(request)

@app.post("/v1/codex/chat/completions")
async def codex_completions(request: ChatCompletionRequest):
    # Direkt codex_adapter.complete(...) aufrufen
    return await codex_adapter.complete(request)
```

**Bestehender `/v1/chat/completions`-Endpoint:** Architect entscheidet:
- **(a)** Entfernen — Breaking-Change, aber sauber
- **(b)** Erhalten als Legacy-Fallback mit Deprecation-Warning im Log

Empfehlung: **(b) erhalten** für die nächsten 2-3 Atelier-Versionen, dann entfernen. Falls aus Versehen ein Custom-Profile noch `cli` als Provider mit alter Endpoint-URL hat, läuft es weiter — aber mit Log-Warning.

**Model-Routing-Logik im Legacy-Endpoint bleibt bestehen** (so wie heute), aber wirft eine Deprecation-Warning ins Log: *"DEPRECATED: /v1/chat/completions endpoint with model-name routing. Use /v1/claude/chat/completions or /v1/codex/chat/completions instead."*

**Tests im cli-proxy** erweitern:
- `test_claude_endpoint_direct.py` — `/v1/claude/...` ruft immer claude_adapter, ignoriert Modell-Name
- `test_codex_endpoint_direct.py` — analog
- `test_legacy_endpoint_warns.py` — Legacy-Endpoint funktioniert, aber loggt Warning

### 2. Atelier-Konfiguration

**`appsettings.json`** anpassen:

```json
{
  "Llm": {
    "Providers": {
      "openrouter": {
        "Endpoint": "https://openrouter.ai/api/v1",
        "ApiKey": ""
      },
      "claude-cli": {
        "Endpoint": "http://atelier-cli-proxy:8090/v1/claude",
        "ApiKey": ""
      },
      "codex-cli": {
        "Endpoint": "http://atelier-cli-proxy:8090/v1/codex",
        "ApiKey": ""
      }
    },
    "Actors": {
      // bestehende System-Akteure unverändert (nutzen openrouter)
    }
  }
}
```

**`.env.example`** entsprechend ergänzen falls Endpoints dort konfiguriert werden — Endpoint-Strings ändern sich nicht (gleicher Host:Port, nur Pfad-Suffix), nur die Provider-Namen in den Keys.

### 3. Datenbank-Migration für bestehende Custom-Profile

**Migration `Step12CliProviderSplit`**:

```sql
-- Custom-ReviewerProfile mit cli-Provider migrieren
UPDATE "ReviewerProfiles"
SET "Provider" = CASE
    WHEN "Model" ~* '^(claude|anthropic/)' THEN 'claude-cli'
    WHEN "Model" ~* '^(gpt|o[0-9]|openai/)' THEN 'codex-cli'
    ELSE 'claude-cli'  -- Fallback: claude (mit Log-Warning aus Code)
END
WHERE "Provider" = 'cli';

-- Custom-ExecutorProfile analog
UPDATE "ExecutorProfiles"
SET "Provider" = CASE
    WHEN "Model" ~* '^(claude|anthropic/)' THEN 'claude-cli'
    WHEN "Model" ~* '^(gpt|o[0-9]|openai/)' THEN 'codex-cli'
    ELSE 'claude-cli'
END
WHERE "Provider" = 'cli';

-- Custom-AdvisorProfile analog
UPDATE "AdvisorProfiles"
SET "Provider" = CASE
    WHEN "Model" ~* '^(claude|anthropic/)' THEN 'claude-cli'
    WHEN "Model" ~* '^(gpt|o[0-9]|openai/)' THEN 'codex-cli'
    ELSE 'claude-cli'
END
WHERE "Provider" = 'cli';
```

**Wichtig:** Auch der `CrewSnapshot`-JSON-Inhalt in `Runs.CrewSnapshot` enthält Provider-Namen. Eine zweite Migrations-Phase aktualisiert historische Snapshots:

```sql
-- CrewSnapshot in historischen Runs aktualisieren
-- Provider-Strings in JSON ersetzen (Postgres jsonb-Funktionen)
UPDATE "Runs"
SET "CrewSnapshot" = REPLACE("CrewSnapshot"::text, '"provider":"cli"', '"provider":"claude-cli"')::jsonb
WHERE "CrewSnapshot"::text LIKE '%"provider":"cli"%'
  AND "CrewSnapshot"::text ~* '"model":"(claude|anthropic/)';

UPDATE "Runs"
SET "CrewSnapshot" = REPLACE("CrewSnapshot"::text, '"provider":"cli"', '"provider":"codex-cli"')::jsonb
WHERE "CrewSnapshot"::text LIKE '%"provider":"cli"%'
  AND "CrewSnapshot"::text ~* '"model":"(gpt|o[0-9]|openai/)';
```

Architect prüft die exakte JSONB-Form der CrewSnapshots und passt die Replace-Logik an die tatsächliche Struktur an. Falls die JSONB-Manipulation komplex wird, alternative: C#-basierter Migration-Code im `Step12CliProviderSplit.Up()`.

### 4. `IProviderCatalog`-Erweiterung

Der bestehende `IProviderCatalog` liefert die Liste der verfügbaren Provider-Namen. Nach dem Split:

```csharp
public sealed class ProviderCatalog : IProviderCatalog
{
    public IReadOnlyList<ProviderInfo> ListProviders() => new[]
    {
        new ProviderInfo("openrouter", "OpenRouter (HTTP, pay-per-token)"),
        new ProviderInfo("claude-cli", "Claude (Subscription CLI)"),
        new ProviderInfo("codex-cli",  "Codex (Subscription CLI)")
    };
}

public sealed record ProviderInfo(string Name, string DisplayName);
```

Architect prüft die genaue API des bestehenden `IProviderCatalog` — wenn dieser bisher nur `string[]` zurückgab, ist die Erweiterung um Display-Name eine kleine API-Änderung (UI-Komponenten müssen folgen).

### 5. UI-Anpassung

**Provider-Dropdown im `ProfileEditorForm`:** Statt Provider-Name nur als String anzeigen, jetzt mit Display-Name:

```razor
<select @bind="Profile.Provider">
    @foreach (var p in _providers)
    {
        <option value="@p.Name">@p.DisplayName</option>
    }
</select>
```

Optional: kleine Hilfe-Hint unter dem Dropdown: *"OpenRouter wird pro Token abgerechnet. Claude-CLI und Codex-CLI nutzen lokale Subscription."*

**Keine weiteren UI-Änderungen** — die `CrewSummary`, `RunDetail`-Anzeigen zeigen bereits den Provider-Namen wie er in der DB steht, das funktioniert nach Migration automatisch.

### 6. Tests

**C#-Tests:**
- `ProviderCatalogTests` — listet die drei erwarteten Provider mit Display-Namen
- `Step12CliProviderSplitMigrationTests` — historische Profile mit `cli`-Provider werden korrekt umgemappt (Test mit synthetischen DB-Daten)
- `OpenAiCompatibleClientForClaudeCliTests` — Client mit `http://...claude`-Endpoint funktioniert
- `OpenAiCompatibleClientForCodexCliTests` — analog

**Python-Tests im cli-proxy:**
- `test_claude_endpoint_routes_to_claude` — Request an `/v1/claude/...` mit beliebigem Modell-Namen ruft claude_adapter
- `test_codex_endpoint_routes_to_codex` — analog
- `test_legacy_endpoint_still_works` — Backward-Compat verifizieren
- `test_legacy_endpoint_logs_deprecation` — Warning-Log bei Legacy-Aufruf

**Regression-Tests:**
- Bestehende `LlmOptionsTests`, `LlmClientResolverTests` etc. nach Provider-Namen-Update grün halten
- Bestehende `appsettings.json`-Parsing-Tests grün

### 7. Dokumentation

- **`docs/02-architecture.md`** LLM-Provider-Sektion aktualisieren: drei Provider statt zwei.
- **`docs/05-decisions-log.md`** neuer Eintrag **D-032** (oder nächste freie Nummer) für den Split.
- **`cli-proxy/README.md`** Endpoint-Sektion erweitern: die drei Routen dokumentiert.

## Akzeptanzkriterien

1. `dotnet build` 0 Errors, 0 Warnings.
2. `dotnet test` — alle bestehenden Tests grün (239 nach PS-7 + neue Tests).
3. **Python `pytest`** im cli-proxy grün, neue Tests bestätigen Endpoint-Trennung.
4. **`/v1/claude/chat/completions`** routet **immer** zu claude CLI, unabhängig vom Modell-Namen.
5. **`/v1/codex/chat/completions`** routet **immer** zu codex CLI.
6. **Legacy `/v1/chat/completions`** funktioniert weiterhin mit Model-Name-Routing, loggt Deprecation-Warning.
7. **DB-Migration `Step12CliProviderSplit`** läuft sauber. Custom-Profile mit `cli`-Provider werden korrekt zu `claude-cli` oder `codex-cli` zugeordnet basierend auf Modell-Namen.
8. **CrewSnapshot in historischen Runs** aktualisiert oder Migrations-Strategie für CrewSnapshot dokumentiert.
9. **UI Provider-Dropdown** zeigt drei explizite Optionen mit Display-Namen.
10. **Real-Test:** Custom-Profile mit `claude-cli` anlegen, Run einreichen, verifizieren dass claude CLI im cli-proxy-Log aufgerufen wurde (nicht codex). Analog für `codex-cli`.
11. **Decisions-Log-Eintrag** mit Architect-Entscheidungen (Legacy-Endpoint-Behandlung, Fallback-Logik).

## Was du in diesem Schritt NICHT tust

- **Keine neuen Features** — reiner Refactor + Migration.
- **Keine Domain-Modell-Änderungen** — `ReviewerProfile.Provider` bleibt `string`.
- **Keine Pipeline-Logik-Änderungen** — `ILlmClientResolver` arbeitet weiter mit Provider-Namen-Lookup.
- **Keine MCP-Tool-Änderungen** außer `list_reviewer_profiles` etc. liefern jetzt korrekte Provider-Namen.
- **Keine neuen LLM-Provider** außer der Split — wenn später noch eine dritte CLI dazukommt, eigener Step.
- **Keine UI-Komponenten-Refactors** — nur Provider-Dropdown-Inhalt.

## Architect-Konsultation (Phase 1.4) — zwei Knackpunkte

1. **Legacy-Endpoint im cli-proxy:** Entfernen (Breaking-Change, sauberer) oder Erhalten mit Deprecation-Warning. **Empfehlung: erhalten**, weil minimaler Aufwand und schützt vor möglichen Bugs in der Migration (falls ein Profile nach Migration noch `cli` als Provider hat, würde es weiter funktionieren statt komplett zu scheitern). Architect bestätigt.

2. **CrewSnapshot-JSON-Migration:** SQL-basierte String-Replace-Migration vs. C#-basierte deserialize-modify-serialize-Migration im `Up()`. **Empfehlung: SQL-Replace** ist schneller und kommt mit weniger C#-Code aus, aber empfindlich gegen JSONB-Format-Änderungen. Architect prüft die tatsächliche CrewSnapshot-JSON-Struktur und entscheidet.

## Reviewer-Hinweise

- **R1 (Functional Correctness):** Alle 11 ACs prüfen. Besonders 7 (Migration) und 10 (Real-Test mit beiden CLIs).
- **R2 (Code Quality):** Sauberer Refactor, keine Duplikation im Side-Container (claude- und codex-Endpunkte teilen sich Request-Parsing-Code).
- **R3 (Test Execution):** Alle Tests grün, neue Tests für beide Endpunkte + Migration.
- **R4 (Architecture Compliance):** Layer-Trennung bleibt, keine neuen Abhängigkeiten zwischen Core und Infrastructure.
- **R5 (Live UI):** Provider-Dropdown im Browser zeigt drei Optionen mit Display-Namen.

## Persistenter Abschlussbericht

Bericht nach `docs/reports/refactor-cli-provider-split-report.md` (kein PS-Nummer — Refactor). Inhalt:

1. **Was wurde umgesetzt** — cli-proxy, Atelier-Config, Migration, UI
2. **Architect-Entscheidungen** zu den zwei Knackpunkten
3. **Migration-Verifikation** — welche Custom-Profile wurden umgemappt, Edge-Cases
4. **Real-Test-Ergebnis** — beide CLIs explicit über die neuen Endpunkte angesprochen, cli-proxy-Logs bestätigen
5. **Akzeptanzkriterien-Check** — Tabelle mit allen 11 ACs
6. **Empfehlungen** — Legacy-Endpoint-Removal-Termin (z.B. nach 2 Atelier-Versionen)

## Production-Deploy

Im Bericht als Anleitung für Stefan:

```bash
# Auf dem Hetzner-Server
cd /srv/docker/websites/geef_atelier
git pull --ff-only

# cli-proxy neu bauen (zwei neue Routen)
docker compose build --no-cache cli-proxy

# Web neu bauen (Provider-Catalog + Migration)
docker compose build --no-cache web

# Stack neu starten — Migration läuft automatisch
docker compose up -d

# Verifikation
curl http://localhost:8090/v1/claude/chat/completions  # erwartet 405 oder 400, NICHT 404
curl http://localhost:8090/v1/codex/chat/completions   # analog
```

Im UI verifizieren: `/crew/profiles/reviewers` → vorhandene Profile haben jetzt `claude-cli` oder `codex-cli` als Provider (statt `cli`).

## Konventionen

- C#-Code, Kommentare, XML-Doc: **Englisch**.
- Python-Code im cli-proxy: **Englisch**, PEP-8.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits (z.B. `refactor: split cli provider into claude-cli and codex-cli`, `feat(cli-proxy): explicit endpoints per cli`, `chore(db): migrate custom profiles from cli to claude-cli/codex-cli`).
- **Niemals Secrets** in source control, Logs oder Bericht.

Erwarteter Aufwand: 1-2 Arbeitstage.

---

**Nach erfolgreichem Abschluss:** Provider-Schicht ist deterministisch — Provider-Name allein entscheidet die CLI-Wahl, kein verstecktes Model-Routing mehr. UI zeigt drei klare Optionen. Foundation für künftige Domain-Templates (PS-9?), die gezielt `claude-cli` für Code-Tasks und `codex-cli` für andere Tasks nutzen können.