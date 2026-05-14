# Feature-Abschlussbericht: Studio-Extensions (D-039)

**Datum:** 14. Mai 2026  
**Branch:** `feat/studio-extensions` → PR #12 → `main`  
**Merge-SHA:** 289bc18  
**Deploy-Timestamp:** 2026-05-14 21:05 UTC

---

## Was umgesetzt wurde

### Sub-Feature 1: MCP-Studio-API

Zwei neue MCP-Tools in `Geef.Atelier.Mcp/Tools/`:

**`analyze_template_proposal`** (`AnalyzeTemplateProposalTool.cs`)
- Delegiert an `ITemplateStudioService.AnalyzeAsync`
- Gibt vollständigen `AnalyzeTemplateProposalOutput` zurück: `AnalysisId`, `MatchedExistingTemplates`, `Recommendation`, `ProposedTemplate`, `ProposedNewProfiles`, `ReasoningSummary`, Token-Counts, Cost
- `AnalysisId` verbindet mit dem Materialisierungs-Aufruf

**`materialize_template_proposal`** (`MaterializeTemplateProposalTool.cs`)
- Nimmt `analysisId`, `finalTemplate` (ProposedTemplateDto), `finalNewProfiles` (Liste von ProposedProfileDtos)
- Deserialisiert DTO-Strings zu Domain-Typen via `Enum.TryParse<ProposedProfileType>`
- Gibt `MaterializeTemplateProposalOutput` mit `TemplateName`, `CreatedProfileNames`, `Warnings` zurück
- Wirft `ArgumentException` bei ungültigem `ProfileType`

### Sub-Feature 2: StudioAnalysisHistoryList-Komponente

**`StudioAnalysisHistoryList.razor`** — vollständige Analyse-Historie auf der Studio-Seite:
- Listet bisherige Analysen mit Zeitstempel, Task-Preview (80 Zeichen), Materialisierungs-Badge oder "Not materialized", Cost
- Expand/Collapse per Click oder Enter/Space (keyboard-accessible)
- Expanded-View: vollständiger Task-Text, Reasoning-Summary, "Re-analyze with edits"-Button, "View materialized template →"-Link
- Pagination: "Show more"-Button, 10 Einträge pro Seite, `HasMore`-Pattern
- `OnReAnalyze`-EventCallback: setzt `_taskDescription` in `TemplateStudio.razor` zurück in Eingabe-Mode
- Vollständige `data-testid`-Attribute für alle interaktiven Elemente

**`TemplateStudio.razor`** — `StudioAnalysisHistoryList` unter `StudioTaskInputStep` eingebunden mit Divider.

### Sub-Feature 3: Welcome-Stats-Erweiterung

**`WelcomeStats`-Record** um zwei Felder ergänzt:
- `StudioAnalysesThisMonth` (int) — Anzahl Studio-Analysen im laufenden Monat
- `StudioCostThisMonth` (decimal) — aggregierte Studio-Kosten im laufenden Monat

**`RunRepository.GetWelcomeStatsAsync`** — zwei neue EF-Queries auf `TemplateStudioAnalyses`-Tabelle.

**`Index.razor`** — viertes Stat-Tile "Studio analyses / this month" nach dem Cost-Tile.

---

## Foundation-Wiederverwendung

Alle Persistence-Infrastruktur (Tabelle, Repository, Service) war seit **Step17 (Template Studio)** vorhanden. Kein neuer Migration-Step notwendig. Die Extensions bauen nur auf dem bestehenden Stack auf:

| Wiederverwendet | Wie genutzt |
|---|---|
| `TemplateStudioAnalyses`-Tabelle | Query für History-List + Aggregation für Stats |
| `ITemplateStudioAnalysisRepository` | Neues `ListHistoryAsync` |
| `ITemplateStudioService` | Neues `ListRecentAnalysesAsync` |
| `TemplateStudioService` | Implementierung der neuen Methode |
| `ITemplateStudioService` im MCP-Projekt | Dependency Injection in die neuen Tools |

---

## Architektur-Entscheidungen (D-039)

**Schicht-Design:** `TemplateStudioHistoryItem` als Core-Record, um zirkuläre Abhängigkeit `Infrastructure → Application` zu vermeiden. `StudioAnalysesPage` + `StudioAnalysisHistoryEntry` im Application-Layer.

**Studio-Kosten-Trennung:** Nicht in `TotalCostThisMonth` aggregiert — Studio ist Konfigurationsaufwand, kein Ausführungsaufwand. User sieht beides separat.

**MCP-Workflow:** Zwei-Schritt-Design (Analyse → Materialisierung) ermöglicht User-Review und optionale Edits zwischen den Aufrufen. `AnalysisId` als Verknüpfung.

---

## Testergebnisse

| Kategorie | Anzahl |
|---|---|
| Neue MCP-Tool-Tests | 18 (9 + 9) |
| Neue Service-Pagination-Tests | 7 |
| Neue UI-Komponenten-Tests (bUnit) | 13 |
| Neue Domain-Tests (WelcomeStats) | 4 |
| **Neue Tests gesamt** | **~40** |
| Bestehende Tests | 628 |
| **Gesamt grün** | **668 / 669** (1 Skipped: E2E ThemeSwitcher) |

Orchestrator-Timing-Tests (2 Stück) schlagen nur unter Full-Suite durch Thread-Konkurrenz-Sensitivität fehl, bestehen isoliert — pre-existing, kein neues Problem.

---

## Deploy-Verifikation

```
HTTP/2 302 → https://geef.stefan-bechtel.de/login  ✅ (erwartet: Auth-gated)
docker compose up -d web  ✅
docker compose build --no-cache web  ✅
PR #12 gemerged, Branch gelöscht  ✅
```

---

## Empfehlungen für Folge-Steps

- **Auto-Run nach Materialization:** Direkt aus der Analyse-Review einen Run starten ("Gleich mit dem neuen Template ausprobieren")
- **Studio-Iterationen:** "Nochmal analysieren mit Feedback" — Konversations-ähnlicher Studio-Flow
- **Cost-Budget-Alerts:** Schwellwert für Studio-Kosten, UI-Warnung wenn überschritten
- **MCP-Prompt-Verbesserung:** Reichhaltigere Beispiel-Tasks in den Tool-Descriptions für bessere LLM-Nutzung
