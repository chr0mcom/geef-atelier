# Feature-Bericht: Model-Catalog-Dropdown

*Datum: 2026-05-13 · Branch: `feat/model-catalog-dropdown`*

---

## 1. Was wurde umgesetzt

### cli-proxy (Python/FastAPI)
- Neue statische Listen in `claude_adapter.STATIC_MODELS` und `codex_adapter.STATIC_MODELS`
- `list_models()` in beiden Adaptern (gibt statische Listen zurück)
- Zwei neue Endpoints:
  - `GET /v1/claude/models` → OpenAI-kompatible Response für Claude-CLI-Modelle
  - `GET /v1/codex/models` → OpenAI-kompatible Response für Codex-CLI-Modelle
- Legacy `GET /v1/models` nutzt jetzt die Adapter-Listen statt hardcodierter Inline-Liste

### Atelier-Backend (C#)

**Core:**
- `ModelInfo.cs` — Record mit `Id`, `DisplayName`, `Description`, `IsRecommended`, `Label`-Property
- `StaticModelFallback.cs` — statische Listen für OpenRouter, claude-cli, codex-cli; `For(providerName)` Dispatcher

**Application:**
- `IModelCatalog.cs` — Interface mit `ListModelsAsync`, `RefreshAsync`, `IsUsingFallback`

**Infrastructure:**
- `ModelCatalog.cs` — Implementation mit `IMemoryCache` (24h TTL), HTTP-Fetch gegen `{endpoint}/models`, SemaphoreSlim-per-Provider gegen Thundering-Herd, statischer Fallback bei Fehler
- `LlmServiceExtensions.cs` — `services.AddMemoryCache()` + `services.AddSingleton<IModelCatalog, ModelCatalog>()`

**Web:**
- `ModelSelector.razor` + `.css` — Searchable Dropdown mit optgroups (Recommended / Other / Advanced), Refresh-Button, Loading-State, Fallback-Banner, Custom-Model-Escape-Hatch (Textfeld via "Custom model name…" + Zurück-Button)
- `ProfileEditorForm.razor` — Model-`InputText` ersetzt durch `<ModelSelector>`, Custom-Model-Confirmation-Modal via `<Modal>` wenn Submit mit unbekanntem Modell

---

## 2. CLI-Recherche-Ergebnis

| CLI | List-Models-Befehl | Entscheidung |
|---|---|---|
| `claude` | Kein `--list-models`-Flag. Nur `--model <name>` zum Setzen. | Statische Liste in `claude_adapter.STATIC_MODELS` |
| `codex` | Keine `models`-Subkommando. Nur `-m <model>`. | Statische Liste in `codex_adapter.STATIC_MODELS` |

Das "Hybrid"-Pattern (CLI versuchen, fallback auf statisch) ist nicht nötig — statische Listen sind der einzige Weg. Der cli-proxy exponiert sie als HTTP-Endpoints, damit der Atelier-Backend-`ModelCatalog` alle drei Provider-Typen uniform über dieselbe HTTP+Cache-Pipeline abfragt.

---

## 3. Architect-Output (Phase 1.4 Knackpunkte)

1. **CLI-Listing:** Weder `claude` noch `codex` unterstützt Model-Listing. Nur statische Listen.
2. **Recommended-Lists:** Hardcoded in `StaticModelFallback.cs` (Core). Recommendation ist Atelier-Meinung, keine Provider-Eigenschaft. Maintainer-Pflicht bei Modell-Release.
3. **Cache-Sharing:** Single-Instanz → `IMemoryCache` ausreichend. Kein Redis.

---

## 4. Cache-Verhalten-Verifikation

| Szenario | Verhalten | Test |
|---|---|---|
| Erster Aufruf, Endpoint erreichbar | HTTP-Fetch, Cache-Set (24h), `IsUsingFallback=false` | `ListModelsAsync_ReturnsApiModels_WhenEndpointSucceeds` |
| Zweiter Aufruf (Cache warm) | Cache-Hit, kein HTTP-Call | `ListModelsAsync_UsesCacheOnSecondCall` (CallCount=1) |
| Endpoint nicht erreichbar, Cache leer | Statische Fallback-Liste, `IsUsingFallback=true` | `ListModelsAsync_UsesFallback_WhenEndpointFails` |
| `RefreshAsync` | Cache invalidieren, neuer HTTP-Call | `RefreshAsync_BypassesCache` (CallCount=2) |
| Unbekannter Provider | Fallback (StaticModelFallback.For gibt empty zurück) | `ListModelsAsync_UsesFallback_WhenProviderUnknown` |

---

## 5. Real-Test-Ergebnis

**AC10 (Real-Test: neues Custom-Profile mit Dropdown-Auswahl, Run einreichen):** Nicht als automatisierter Test ausgeführt — erfordert Live-Deployment. Die Integration-Tests (`ModelCatalogTests`) verifizieren das Verhalten mit echten HTTP-Responses gegen den Fake-Handler. Ein Playwright-E2E-Test für den UI-Flow (ProfileEditor → ModelSelector → Run-Einreichung) wäre der nächste Polish-Schritt.

---

## 6. Akzeptanzkriterien-Check

| AC | Beschreibung | Status |
|---|---|---|
| AC1 | `dotnet build` 0 Errors, 0 Warnings | ✅ |
| AC2 | `dotnet test` alle bestehenden + neue Tests grün | ✅ 256 Passed, 1 Skipped |
| AC3 | Python `pytest` grün | ✅ 43 Passed |
| AC4 | `/v1/claude/models` und `/v1/codex/models` funktional | ✅ Tests + statische Listen |
| AC5 | `IModelCatalog.ListModelsAsync` mit 24h-Cache | ✅ |
| AC6 | Cache-Failure-Fallback | ✅ `ModelCatalogTests` verifiziert |
| AC7 | `ModelSelector`-Komponente: Provider-Wechsel, Custom-Option, Refresh | ✅ Component implementiert |
| AC8 | Custom-Model-Submit-Warning | ✅ Modal in `ProfileEditorForm` |
| AC9 | R5 Live-Test: ProfileEditor in allen drei Themes | ⚠️ Nicht deployt (kein Live-Test) |
| AC10 | Real-Test: Dropdown-Auswahl → Run → Modell genutzt | ⚠️ Erfordert Deploy |
| AC11 | Decisions-Log-Eintrag D-033 | ✅ |

---

## 7. Empfehlungen

**Cost-Info im Dropdown (nächster Polish-Step):**
OpenRouter liefert `pricing`-Felder in der `/models`-Response (`prompt`, `completion` in USD/Token). `ModelInfo` um `PricePerMillionTokens?` erweitern, im Dropdown als kleiner Hinweis anzeigen. Separater Step — kein Impact auf diesen MVP.

**bUnit-Test für ModelSelector:**
Der `ModelSelector`-Komponenten-Test ist noch nicht implementiert (Plan: `ModelSelectorComponentTests`). Wäre sinnvoll als nächster Test-Polish-Step, besonders für den Provider-Wechsel-Case und den Custom-Model-Flow.

**Auto-Refresh beim Container-Restart:**
`IModelCatalog` nutzt `IMemoryCache` — beim Container-Neustart ist der Cache leer. Erster Zugriff nach Restart triggert automatisch einen Fetch. Kein Problem, da der Fallback greift wenn der Fetch fehlschlägt.

**Static-Model-Listen pflegen:**
Bei jedem neuen Anthropic/OpenAI-Modell-Release: `StaticModelFallback.cs` + `claude_adapter.STATIC_MODELS` + `codex_adapter.STATIC_MODELS` aktualisieren. Maintainer-Verantwortung — im Bericht dokumentiert.
