# Claude-Code-Prompt: Model-Catalog mit Dropdown-Auswahl

*Ersetzt das Free-Text-Model-Input im Profile-Editor durch ein searchable Dropdown, das die verfügbaren Modelle pro Provider dynamisch abfragt und cached. Baut auf dem CLI-Provider-Split auf.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Der Profile-Editor (PS-6) nutzt aktuell ein Free-Text-Input für das Model-Feld. Das führte bereits einmal zum `openai/gpt-5.5-mini`-Bug, wo ein nicht-existentes Modell konfiguriert wurde und der Run hängenblieb. Mit dem Bug-Fix wird das jetzt sauber zu `Failed` — aber die UX wäre besser, wenn Fehler-Modelle gar nicht erst eingegeben werden können.

Deine Aufgabe ist ein **Model-Catalog mit Dropdown-Auswahl**: Atelier fragt pro Provider die verfügbaren Modelle ab, cached die Listen, und zeigt sie im Profile-Editor als Dropdown statt Free-Text. Eine "Custom model"-Escape-Hatch bleibt für Edge-Cases.

## Vorgehen

**Du folgst dem Workflow in `/srv/docker/docs/geef-workflow.md`** in komprimierter Form:
- Phase 1.1 + 1.2: SDK/Provider-API-Recherche
- Phase 1.4 Architect: drei Knackpunkte (siehe unten)
- Phase 2: Implementation
- Phase 3: R1 + R2 + R3 + R5 (R4 leichter Pass)
- Phase 4: Bericht

**Branch:** `feat/model-catalog-dropdown` mit PR.

## Pflicht-Lektüre fürs Grounding

1. **`docs/reports/refactor-cli-provider-split-report.md`** — der Stand der CLI-Endpoints nach dem Split
2. **`cli-proxy/src/main.py`** — die FastAPI-Routes, wo die `/models`-Endpoints hinzukommen
3. **`cli-proxy/src/claude_adapter.py`** und **`codex_adapter.py`** — die CLI-Wrapper, müssen ggf. um `list_models()` ergänzt werden
4. **`src/Geef.Atelier.Application/Crew/IProviderCatalog.cs`** und Implementation — der Provider-Catalog aus PS-6, wird Vorbild für `IModelCatalog`
5. **`src/Geef.Atelier.Web/Components/UI/ProfileEditorForm.razor`** — wo das Model-Input umgebaut wird
6. **OpenRouter API-Doku:** `GET https://openrouter.ai/api/v1/models` Response-Schema
7. **CLI-Recherche:** `claude --help` und `codex --help` — gibt es überhaupt einen `--list-models`-Befehl, oder müssen wir die Modell-Listen anders abrufen?

## Konkrete Anforderungen

### 1. cli-proxy: neue `/models`-Endpoints

Zwei neue Routen im cli-proxy (Python/FastAPI):

```python
@app.get("/v1/claude/models")
async def claude_models():
    models = await claude_adapter.list_models()
    return {
        "object": "list",
        "data": [{"id": m, "object": "model"} for m in models]
    }

@app.get("/v1/codex/models")
async def codex_models():
    models = await codex_adapter.list_models()
    return {
        "object": "list",
        "data": [{"id": m, "object": "model"} for m in models]
    }
```

**`claude_adapter.list_models()` und `codex_adapter.list_models()`-Implementation:**

Phase 1.2-Recherche bestimmt die exakte Mechanik. Plausible Optionen:

- **(a) CLI-Befehl falls vorhanden:** `claude --list-models` (oder `claude models list`, `claude config show-models`, etc.)
- **(b) Statische Liste im Adapter:** wenn die CLI keinen Listing-Befehl hat, hardcoded Modell-Tags die in der Subscription verfügbar sind. Liste muss manuell gepflegt werden, aber das ist ja sowieso die Realität — die Atelier-Maintainer müssen wissen welche Modelle aktiv sind.
- **(c) Hybrid:** versuche CLI-Befehl, fall zurück auf statische Liste mit Warning-Log.

Empfehlung: **(c) Hybrid**. CLI-Befehl ist die Source-of-Truth wenn verfügbar, statische Liste ist robust gegen CLI-Updates.

**Statische Fallback-Liste** im cli-proxy als Konstante (pflegbar via Code-Update):

```python
# claude_adapter.py
STATIC_CLAUDE_MODELS = [
    "claude-sonnet-4-5",
    "claude-opus-4-7",
    "claude-haiku-4-5",
    # ...
]

# codex_adapter.py
STATIC_CODEX_MODELS = [
    "gpt-5.5",
    "gpt-5.5-mini",
    "o5-mini",
    "o4",
    # ...
]
```

Architect verifiziert die aktuellen Modell-Tags der CLIs in Phase 1.2 und pflegt die Liste.

**Health-Check-Erweiterung im cli-proxy:** `/health` kann zusätzlich melden ob die `/models`-Endpoints letzten erfolgreichen Refresh hatten.

### 2. Atelier-Backend: `IModelCatalog`-Service

Neuer Service in `src/Geef.Atelier.Application/Crew/IModelCatalog.cs`:

```csharp
public interface IModelCatalog
{
    /// <summary>Lists available models for a given provider. Cached for 24h.</summary>
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct);

    /// <summary>Invalidates cache and re-fetches.</summary>
    Task RefreshAsync(string providerName, CancellationToken ct);
}

public sealed record ModelInfo(
    string Id,                    // z.B. "claude-sonnet-4-5"
    string DisplayName,           // z.B. "Claude Sonnet 4.5" (optional, Fallback auf Id)
    string? Description,          // optional, z.B. "Fast, good for everyday tasks"
    bool IsRecommended            // für "Recommended" Sektion im Dropdown
);
```

**Implementation in `src/Geef.Atelier.Infrastructure/Crew/ModelCatalog.cs`:**

- Cache via `IMemoryCache` mit 24h TTL pro Provider-Key
- Bei `ListModelsAsync`:
  1. Cache-Lookup
  2. Wenn Cache-Hit und nicht expired: return
  3. Sonst: API-Call (HTTP zu Provider-Endpoint + `/models`)
  4. Parse OpenAI-Schema-konforme Response (`{ "object": "list", "data": [...] }`)
  5. Cache speichern, return
- Bei `RefreshAsync`: Cache invalidieren, dann `ListModelsAsync` aufrufen
- **Failure-Strategie:** wenn API-Call scheitert UND Cache leer ist → statische Fallback-Liste aus Code (`Geef.Atelier.Core/Domain/Crew/StaticModelFallback.cs`)

**Recommended-Modelle:** Eine kleine Logik markiert pro Provider die "empfohlenen" Modelle (z.B. claude-sonnet-4-5, claude-opus-4-7, claude-haiku-4-5 für claude-cli). Andere Modelle werden im UI unter "Other available" angezeigt. Die Recommended-Liste ist statisch in `Geef.Atelier.Core/Domain/Crew/ModelRecommendations.cs`.

### 3. Statische Fallback-Liste

`src/Geef.Atelier.Core/Domain/Crew/StaticModelFallback.cs`:

```csharp
public static class StaticModelFallback
{
    public static readonly IReadOnlyList<ModelInfo> ForOpenRouter = new[]
    {
        new ModelInfo("anthropic/claude-opus-4.7", "Claude Opus 4.7", "Best quality, Anthropic via OpenRouter", true),
        new ModelInfo("anthropic/claude-sonnet-4.5", "Claude Sonnet 4.5", "Fast, Anthropic via OpenRouter", true),
        new ModelInfo("google/gemini-2.5-flash", "Gemini 2.5 Flash", "Cheap, fast, Google via OpenRouter", true),
        new ModelInfo("openai/gpt-4o-mini", "GPT-4o Mini", "Cheap, OpenAI via OpenRouter", true),
        // weitere bekannte Modelle, weniger prominent
    };

    public static readonly IReadOnlyList<ModelInfo> ForClaudeCli = new[]
    {
        new ModelInfo("claude-sonnet-4-5", "Claude Sonnet 4.5", "Fast, good for everyday tasks", true),
        new ModelInfo("claude-opus-4-7", "Claude Opus 4.7", "Best quality", true),
        new ModelInfo("claude-haiku-4-5", "Claude Haiku 4.5", "Cheapest, basic tasks", true),
    };

    public static readonly IReadOnlyList<ModelInfo> ForCodexCli = new[]
    {
        // basierend auf Phase 1.2-Recherche
    };
}
```

**Wichtig:** diese statische Liste wird mit jedem Atelier-Update gepflegt. Maintainer-Verantwortung. Im Bericht eine Notiz: "Beim nächsten Modell-Release in Atelier-Update integrieren."

### 4. UI: Model-Dropdown im ProfileEditor

**`ProfileEditorForm.razor`** umbauen:

Statt:
```razor
<input @bind="Profile.Model" type="text" />
```

Jetzt:
```razor
<ModelSelector
    Provider="@Profile.Provider"
    @bind-Value="@Profile.Model"
    data-testid="model-selector" />
```

**Neue Komponente `Components/UI/ModelSelector.razor`:**

- Bei Provider-Wechsel: ruft `IModelCatalog.ListModelsAsync(provider)` neu auf, setzt Model zurück
- Searchable Dropdown (User kann tippen, filtert die Liste)
- Drei Sektionen:
  - **Recommended for this provider** (Modelle mit `IsRecommended=true`)
  - **Other available** (alle anderen aus dem Catalog)
  - **Custom model name (advanced)** — bei Klick öffnet sich ein Free-Text-Input
- Refresh-Button (kleines Icon) neben dem Dropdown: ruft `IModelCatalog.RefreshAsync(provider)`
- Loading-State während der erste Fetch läuft
- Error-State falls Catalog leer (statischer Fallback aktiv): kleines Banner *"Showing fallback list — provider unreachable. [Refresh]"*

**Custom-Model-Warning bei Submit:**

In `ProfileEditorForm` Submit-Handler: wenn `Profile.Model` nicht in der aktuellen Catalog-Liste ist, zeigt ein Modal *"Model '{name}' is not in the catalog. Submit anyway?"* mit Confirm/Cancel.

### 5. Tests

**C#-Tests:**
- `ModelCatalogTests` — Cache funktioniert (Cache-Hit, Cache-Expiry, Refresh)
- `ModelCatalogFailureFallbackTests` — wenn API-Call scheitert, fallback auf statische Liste
- `ModelInfoEqualityTests`
- `ModelSelectorComponentTests` (bUnit) — Provider-Change resettet Model, Custom-Option, Refresh-Button

**Python-Tests im cli-proxy:**
- `test_claude_models_endpoint` — Endpoint funktioniert, liefert OpenAI-konforme Response
- `test_codex_models_endpoint` — analog
- `test_models_fallback_to_static` — wenn CLI-Listing-Befehl nicht funktioniert, statische Liste aus dem Code

**Integration-Test (bUnit + Mock):**
- `ProfileEditorWithModelSelectorTests` — Profile-Edit mit Catalog funktioniert end-to-end

### 6. Provider-Details im Dropdown

Optional als Verbesserung: jedes Modell im Dropdown zeigt zusätzlich:
- Provider-spezifische Cost-Info (für OpenRouter: aus `pricing`-Feld der API-Response)
- Geschätzte Latenz (statisch in der Recommendations-Liste)
- Context-Window-Größe (aus der API-Response, falls verfügbar)

Empfehlung: in PS-1 dieses Step nur Modell-ID + DisplayName. Cost/Latenz/Context als optional in einem späteren Polish-Step.

### 7. Migration für historische Profile

Keine DB-Migration nötig. Historische Custom-Profile haben Modell-Namen als String — die bleiben gültig. Falls ein historisches Modell nicht mehr im aktuellen Catalog ist, würde der Dropdown beim Editieren das Modell unter "Custom" zeigen. User kann es behalten oder ändern.

## Akzeptanzkriterien

1. `dotnet build` 0 Errors, 0 Warnings.
2. `dotnet test` — alle bestehenden Tests grün + neue Catalog-Tests.
3. **Python `pytest`** im cli-proxy grün.
4. **`/v1/claude/models` und `/v1/codex/models`** im cli-proxy funktional, OpenAI-konforme Response.
5. **`IModelCatalog.ListModelsAsync`** mit 24h-Cache funktional.
6. **Cache-Failure-Fallback** auf statische Liste verifiziert.
7. **`ModelSelector`-Komponente** funktional: Provider-Wechsel resettet Model, Searchable, Custom-Option, Refresh-Button.
8. **Custom-Model-Submit-Warning** funktional.
9. **R5 Live-Test:** ProfileEditor in allen drei Themes, Dropdown zeigt Modelle, Refresh funktioniert.
10. **Real-Test:** neues Custom-Profile mit Dropdown-Auswahl anlegen, Run einreichen, verifizieren dass das ausgewählte Modell tatsächlich genutzt wird (cli-proxy-Log oder OpenRouter-Dashboard).
11. **Decisions-Log-Eintrag** mit Architect-Entscheidungen.

## Was du in diesem Step NICHT tust

- **Keine Provider-Catalog-Änderungen** — `IProviderCatalog` aus PS-6 bleibt unverändert.
- **Keine Cost-Tracking-Aktivierung** — Cost-Info im Dropdown ist optional, in PS-1 dieses Steps weglassen.
- **Keine Bulk-Migration historischer Profile** — historische Modell-Namen bleiben gültig.
- **Keine Auto-Validation-Pre-Submit** für Modell-Namen außerhalb des Editors (z.B. via MCP-API) — User kann via API beliebige Modell-Strings setzen, das ist ein anderer Step ("Pre-Submit-Validation").
- **Keine SignalR-Cache-Invalidation-Broadcast** — falls ein User refresht, sieht nur er die neue Liste. Andere Browser-Tabs warten auf eigenen Refresh oder TTL.

## Architect-Konsultation (Phase 1.4) — drei Knackpunkte

1. **CLI-Listing-Befehle:** Was funktioniert in der aktuellen `claude` und `codex` CLI? Phase 1.2-Recherche: ist `claude --list-models` ein echter Befehl? Falls nein: nutzen wir nur die statische Liste? Falls ja: parsen wir die Ausgabe robust?

2. **Recommended-Listen-Strategie:** Hardcoded in Atelier-Core vs. dynamisch über ein zusätzliches Provider-Metadata-Feld. **Empfehlung: hardcoded in Core**, weil Recommendation eine Atelier-Meinung ist, keine Provider-Eigenschaft. Architect bestätigt.

3. **Cache-Sharing zwischen Atelier-Instanzen:** Single-Maintainer-Setup hat nur eine Atelier-Instanz, also `IMemoryCache` ausreichend. Architect bestätigt.

## Reviewer-Hinweise

- **R1 (Functional Correctness):** Alle 11 ACs. Besonders 6 (Failure-Fallback) und 10 (Real-Test).
- **R2 (Code Quality):** Cache-Logik sauber, keine doppelten API-Calls bei concurrent Requests (Lock oder `Lazy<T>`-Pattern).
- **R3 (Test Execution):** Mock-HTTP-Tests für die drei Provider-APIs.
- **R4 (Architecture Compliance):** `IModelCatalog` in Application-Layer, Cache-Implementation in Infrastructure. Statische Fallback-Liste in Core (kein Infrastructure-Dep).
- **R5 (Live UI):** ProfileEditor in allen drei Themes mit funktionalem Dropdown.

## Persistenter Abschlussbericht

Bericht nach `docs/reports/feature-model-catalog-dropdown-report.md`. Inhalt:

1. **Was wurde umgesetzt** — cli-proxy, Atelier-Backend, UI
2. **CLI-Recherche-Ergebnis** — was unterstützen die CLIs für Model-Listing, was wurde statisch hinterlegt
3. **Architect-Output** — drei Knackpunkte
4. **Cache-Verhalten-Verifikation** — Cache-Hit, Cache-Expiry, Refresh, Fallback
5. **Real-Test-Ergebnis** — Profile mit Dropdown angelegt, Run mit dem Modell erfolgreich
6. **Akzeptanzkriterien-Check** — Tabelle
7. **Empfehlungen** — Cost-Info im Dropdown als Folge-Step? Auto-Refresh beim Container-Restart?

## Production-Deploy

```bash
cd /srv/docker/websites/geef_atelier
git pull --ff-only
docker compose build --no-cache cli-proxy web
docker compose up -d

# Verifikation
curl http://localhost:8090/v1/claude/models | head -50
curl http://localhost:8090/v1/codex/models | head -50
```

Im UI verifizieren: `/crew/profiles/reviewers/new` → Provider-Wechsel → Model-Dropdown lädt korrekte Liste.

## Konventionen

- C#: **Englisch**, Code-Kommentare, XML-Doc.
- Python: **Englisch**, PEP-8.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- **Niemals Secrets** in source control, Logs oder Bericht.

Erwarteter Aufwand: 1-2 Arbeitstage.

---

**Nach erfolgreichem Abschluss:** Profile-Editor lässt keine ungültigen Modell-Namen mehr zu (außer per Custom-Escape-Hatch mit Warning). Maintainer pflegt die statischen Fallback-Listen bei jedem Atelier-Release. Foundation für künftige Provider-spezifische Anreicherung (Cost-Info, Context-Window, etc.).