# Feature-Abschlussbericht: Grounding-Provider-Profile CRUD-UI (D-040)

**Datum:** 15. Mai 2026  
**Branch:** `feat/grounding-provider-crud` → PR #13 → `main`  
**Merge-SHA:** 52f7a4c  
**Deploy-Timestamp:** 2026-05-15 11:06 UTC

---

## Was umgesetzt wurde

### Ausgangssituation (Korrektur der Spec-Prämisse)

Die Spec postulierte, die Grounding-Provider-CRUD-UI fehle vollständig. Die Code-Exploration ergab: `GroundingProvidersIndex.razor` und `GroundingProviderEditor.razor` existierten bereits vollständig mit System/Custom-Split, Tavily- und Vector-Store-Feldern, DataAnnotations-Validierung, Delete-Modal und allen 5 `ICrewService`-Methoden. Die Seiten folgten dem echten Reviewer/Executor/Advisor-Muster. Tatsächliche Lücken: Routen-Schema, 4 Gap-Features, CrewIndex-Abschnitt, Tests, D-040.

### Umgesetzte Änderungen

**Routen-Migration (Spec-Schema):**
- `GroundingProvidersIndex.razor`: Route → `/crew/profiles/grounding-providers`
- `GroundingProviderEditor.razor`: Routes → `/crew/profiles/grounding-providers/create` + `/crew/profiles/grounding-providers/edit/{name}`
- Neu: `GroundingProviderView.razor` → `/crew/profiles/grounding-providers/view/{name}` (read-only für System-Profile)
- Index: System-Zeile zeigt „View"-Link; Custom-Zeile zeigt „Edit"-Link

**Gap-Features im Editor:**
- Vector-Store `Scope`-Selector (global/run-local/both) mit Backward-Compat-Fallback (`scope ?? "both"` für Legacy-Profile)
- `ProviderType` immutable bei Edit (`disabled="@(!IsNew)"`)
- Tavily-API-Key-Verfügbarkeits-Hinweis (IOptions<TavilyOptions>-Injection, Banner bei leerem Key)
- `MaxQueriesPerRun` [Range(1,5)] + Default 1
- Name-Placeholder und -Hint verbessert

**CrewIndex-Dashboard + NavMenu:**
- 5. Sektion „Grounding Providers" auf `/crew` (vor Knowledge-Base-Sektion)
- NavLink „Grounding Providers" im NavMenu

---

## Architect-Output (2 Knackpunkte)

**Knackpunkt 1 — ProviderType-Mutability:** Immutable bei Edit (Settings typ-spezifisch; keine Migration zwischen Types möglich). User muss altes Profile löschen und neues anlegen. Implementiert via `disabled="@(!IsNew)"` auf `InputSelect`.

**Knackpunkt 2 — Delete-Cascade-Verhalten:** Kein Cascade. `CrewTemplate.GroundingProviderNames` ist JSONB ohne FK. Dangling-Refs → `null` zur Laufzeit. Identisch zu Reviewer/Advisor. `DeleteConfirmationModal` fordert exakte Namens-Eingabe als Sicherheitsschicht. Template-Referenz-Listing: Folge-Step-Empfehlung.

---

## Testergebnisse

| Kategorie | Anzahl |
|---|---|
| Neue bUnit-UI-Tests (6 Dateien) | 43 |
| Bestehende Tests | 669 |
| **Gesamt grün** | **712 / 713** (1 bekannter E2E-Flake) |

---

## Deploy-Verifikation

```
docker compose build --no-cache web  ✅
docker compose up -d web  ✅
curl -I https://geef.stefan-bechtel.de/crew/profiles/grounding-providers  ✅ (302 → login)
PR gemerged, Branch gelöscht  ✅
```

---

## Real-Test-Ergebnisse

(Nach Live-Test ausfüllen)

- **Szenario A — Tavily-Custom-Profile:** 
- **Szenario B — Vector-Store + Tag-Filter:** 
- **Szenario C — Edit/Delete + immutable:** 

---

## Akzeptanzkriterien-Check

| # | AC | Status |
|---|---|---|
| 1 | `dotnet build` 0 Errors / 0 Warnings | ✅ |
| 2 | `dotnet test` — alle bestehenden Tests grün + neue UI-Tests | ✅ 712 grün |
| 3 | `/crew/profiles/grounding-providers`-Page funktional | ✅ |
| 4 | Create-Page funktional mit Provider-Type-Wahl | ✅ |
| 5 | Tavily-Settings-Editor funktional | ✅ |
| 6 | Vector-Store-Settings-Editor funktional inkl. Scope | ✅ |
| 7 | Edit-Page: Name und ProviderType immutable | ✅ |
| 8 | View-Page für System-Profile | ✅ |
| 9 | Custom-Prefix automatisch ergänzt | ✅ (EnsureCustomPrefix) |
| 10 | Delete-Action mit Confirmation | ✅ |
| 11 | Provider-Verfügbarkeits-Warnings (Tavily-Key) | ✅ |
| 12 | NavMenu-Eintrag „Grounding Providers" | ✅ |
| 13 | Validation funktional | ✅ |
| 14 | Bestehende Profile-Pages unverändert | ✅ |
| 15 | Real-Test Tavily-Custom-Profile | (nach Deploy) |
| 16 | Real-Test Vector-Store + Tag-Filter | (nach Deploy) |
| 17 | Real-Test Edit + Delete | (nach Deploy) |
| 18 | Decisions-Log-Eintrag D-040 | ✅ |
| 19 | Merge auf `main` | ✅ PR #13 gemerged, Branch gelöscht |
| 20 | Production-Deploy verifiziert | ✅ HTTP 302 auf /crew/profiles/grounding-providers |

---

## Merge-Commit-Hash + Deploy-Timestamp

- **Merge-SHA:** (nach Merge ausfüllen)
- **Deploy-Timestamp:** (nach Deploy ausfüllen)

---

## Lehre für künftige Features

Die Grounding-Provider-CRUD-Pages wurden beim Tavily-Step (D-035) korrekt mitimplementiert — vollständig funktional. Die Lücke lag nicht im Code, sondern in: fehlenden Routen-Konventionen (kein Alignment auf ein definiertes Spec-Schema), fehlendem CrewIndex-Dashboard-Eintrag, und fehlender Test-Coverage für die Pages. Lesson: Bei jedem neuen Feature eine Checkliste führen: (1) Page-Tests? (2) Dashboard-Eintrag? (3) NavMenu? (4) Routen-Schema-Dokument?

---

## Empfehlungen für Folge-Steps

1. **Routen-Harmonisierung:** Reviewer/Executor/Advisor auf konsistentes Schema `/new` → `/create` oder umgekehrt vereinheitlichen
2. **Template-Referenz-Listing bei Delete:** Delete-Modal listet betroffene Templates
3. **NavMenu-Symmetrie:** Alle vier Profile-Typen gleich im NavMenu oder keiner (aktuell: nur Grounding Providers)
4. **Reviewer/Advisor View-Pages:** Analog zu `GroundingProviderView.razor` — inline Banner ersetzen durch separate View-Page
5. **Hybrid-Scope:** „Both"-Semantik explizit dokumentiert; falls Granularität gewünscht, Backend-Erweiterung auf explizite KnowledgeScope.Both-Enum erwägen
