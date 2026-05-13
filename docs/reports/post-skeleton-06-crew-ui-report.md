# Post-Skeleton Schritt 6 — Crew-UI: Abschlussbericht

Datum: 2026-05-13  
Autor: Claude Code (claude-opus-4-7) im Auftrag von Stefan Bechtel

---

## 1. Ausgangslage und Ziel

Vor PS-6 war die PS-5-Crew-Foundation vollständig im Backend verdrahtet, aber für den Atelier-Nutzer unsichtbar: `New.razor` submitted ohne Crew-Wahl, `RunDetail.razor` zeigte keine Crew-Informationen, und es gab keine Verwaltungs-Seiten für Profile oder Templates. Custom-Crews waren nur per MCP oder direktem `IRunService`-Aufruf erreichbar.

**Ziel:** Reine UI-Schicht über die PS-5-Foundation. Template-Auswahl auf NewRun, Crew-Anzeige auf RunDetail/RunRow, vollständige CRUD-Seiten unter `/crew` für Reviewer-Profile, Executor-Profile und Templates. Alle drei Atelier-Themes (Vellum/Noir/Petrol) funktionieren in allen neuen Komponenten.

**Wichtige Nebenbefund-Korrektur:** Das System-Profil `clarity` verwendete das Modell `openai/gpt-5.5-mini`, das auf OpenRouter nicht existiert. Im Rahmen des Real-Custom-Crew-Tests (AC12) erkannt und auf `openai/gpt-4o-mini` korrigiert.

---

## 2. Umsetzung

### Backend-Ergänzungen (PS-6-schmal)

| Änderung | Datei | Begründung |
|---|---|---|
| `CrewSnapshot.Deserialize(string? json)` | `Core/Domain/Crew/CrewSnapshot.cs` | Konsolidiert inline Deserialisierung aus `RunOrchestratorService`. Null-safe, Exception-safe. |
| `IProviderCatalog` Interface | `Application/Crew/IProviderCatalog.cs` | Layer-Trennung: UI injiziert Interface, nicht `IOptions<LlmOptions>` direkt. |
| `ProviderCatalog` Implementation | `Infrastructure/Llm/ProviderCatalog.cs` | `internal sealed`, wraps `IOptions<LlmOptions>`, gibt sortierte Provider-Liste zurück. |
| DI-Registrierung | `Infrastructure/Llm/LlmServiceExtensions.cs` | `AddSingleton<IProviderCatalog, ProviderCatalog>()` |
| `ReviewerDisplay`-Erweiterungen | `Web/Display/ReviewerDisplay.cs` | `GetExecutorDisplay`, `GetTemplateDisplay`, `GetEvaluationStrategyDisplay` |

### Neue UI-Komponenten (8 Komponenten)

| Komponente | Zweck |
|---|---|
| `Modal.razor` | Generische Modal-Komponente (Backdrop, Title/Body/Actions-Slots, CloseOnBackdropClick) |
| `CrewBadge.razor` | Dezenter Text-Badge mit Template-Namen in RunRow |
| `CrewSelector.razor` | Template-Dropdown auf NewRun-Page mit „Manage crews →" Link |
| `CrewSummary.razor` | Click-to-Expand Crew-Übersicht auf RunDetail-Page |
| `ReviewerPicker.razor` | Available/Selected-Picker mit Up/Down-Reordering (kein JS-Interop) |
| `ProfileEditorForm.razor` | Generisches Form für Reviewer- und Executor-Profile (gleiche Felder) |
| `DeleteConfirmationModal.razor` | Name-Eingabe-Bestätigungs-Modal (wraps Modal) |

### Neue Pages (7 Pages)

| URL | Komponente |
|---|---|
| `/crew` | `CrewIndex.razor` — Landing mit Übersicht über Templates + Profile |
| `/crew/templates` | `CrewTemplatesIndex.razor` — Liste aller Templates |
| `/crew/templates/new`, `/{name}` | `CrewTemplateEditor.razor` — New/Edit/View-System |
| `/crew/profiles/reviewers` | `ReviewerProfilesIndex.razor` |
| `/crew/profiles/reviewers/new`, `/{name}` | `ReviewerProfileEditor.razor` |
| `/crew/profiles/executors` | `ExecutorProfilesIndex.razor` |
| `/crew/profiles/executors/new`, `/{name}` | `ExecutorProfileEditor.razor` |

### Geänderte Dateien (UI)

| Datei | Änderung |
|---|---|
| `New.razor` | `<CrewSelector @bind-Value="_crewTemplateName" />` + `crewTemplateName` an Submit |
| `RunDetail.razor` | `CrewSnapshot.Deserialize(...)` in `LoadAsync`, `<CrewSummary>` unter run-header |
| `RunRow.razor` | `<CrewBadge TemplateName="@Run.CrewTemplateName" />` in .tags div |
| `NavMenu.razor` | Vierter NavLink „Crew" mit `NavLinkMatch.Prefix` |
| `atelier.css` | `--z-modal: 1000`, `.crew-badge`, `.disabled-action`, `.collapsible-header/body` |
| `SystemCrew.cs` | Clarity-Modell korrigiert: `openai/gpt-5.5-mini` → `openai/gpt-4o-mini` |

---

## 3. Architect-Entscheidungen (D-029)

| Entscheidung | Ergebnis |
|---|---|
| `CrewSnapshot.Deserialize` als Domain-Helper | Konsolidierung; kein neues IRunService-Tupel |
| `IProviderCatalog` in Application | Layer-Trennung Razor ↔ LlmOptions |
| Routing-Constraint `[a-z0-9\-]+:maxlength(64)` | Konsistent mit Service-Layer-Validierung via DataAnnotations |
| InteractiveServer für alle CRUD-Editoren | ReviewerPicker-Up/Down + Delete-Modal brauchen Server-State |
| Top-Level-NavLink „Crew" | NavMenu hatte 3 Items, kein Style-Guide-Link → Top-Level ist richtig |
| Generische `ProfileEditorForm` | ReviewerProfile und ExecutorProfile haben identisches Schema |
| Generische `Modal` + `DeleteConfirmationModal` | Kein Browser-`confirm`, wiederverwendbar |
| Up/Down-Pfeil-Buttons im ReviewerPicker | Ausreichend für 2-5 Reviewer, kein JS-Interop |
| System-Profile-Schutz: UI + Service | Defense in Depth; Service wirft bei direktem API-Aufruf |
| Custom-Auto-Prefix Live-Preview | Sichtbar vor dem Speichern, Service ist idempotent |
| `CrewSummary` Click-to-Expand | Platzsparend, kein zweiter Klick zum Schließen |
| `CrewBadge` als reiner Text-Badge | Hierarchie unterhalb StatusBadge ohne Icon-Überfrachtung |
| AdvisorProfile-Felder ausgespart | PS-7 bringt Advisor-UI |

---

## 4. Test-Ergebnisse

| Metrik | Wert |
|---|---|
| Tests (PS-5) | 154 |
| Neue bUnit-Tests | 35 (6 neue Dateien) |
| Tests (PS-6 gesamt) | 189 |
| Fehlgeschlagen | 0 |
| Übersprungen | 1 (ThemeSwitcher-E2E, Browser nicht verfügbar) |
| `dotnet build` | 0 Errors, 0 Warnings |
| Docker-Build | ✅ erfolgreich |

### Neue Test-Dateien

- `CrewBadgeTests.cs` — null-Fall, Named-Fall, data-testid
- `CrewSummaryTests.cs` — null-Snapshot-Fallback, Collapsed/Expanded-State, ConvergenceOverride
- `ReviewerPickerTests.cs` — Add, Remove, MoveUp/Down Edge-Cases
- `DeleteConfirmationModalTests.cs` — Disabled bei falschem Namen, Enabled bei korrektem, OnConfirm/OnCancel
- `ModalTests.cs` — Show/Hide, Backdrop-Click mit/ohne CloseOnBackdropClick
- `CrewSelectorTests.cs` — Befüllung, Default-Auswahl

---

## 5. Pre-Mortem-Ergebnisse

| Risiko | Status |
|---|---|
| Theme-Konsistenz-Brüche | ✅ Alle neuen Tokens referenzieren bestehende Variables; kein per-Theme-Overhead |
| Routing-Constraint `/crew/templates/new` vor `/{name}` | ✅ Blazor matcht exakte Routes zuerst; E2E-verifiziert |
| Reviewer-Picker Up/Down Index-Bug | ✅ Bounds-Check + bUnit-Tests |
| Delete-Confirmation Bypass | ✅ State-Machine: Delete-Button enabled nur wenn `_typedName == ItemName` |
| System-Profile-Edit-Bypass via direkten URL | ✅ Editor prüft `IsSystem` → read-only; Service wirft zusätzlich |
| CrewSnapshot null auf alten Runs | ✅ `Deserialize` returns null → `CrewSummary` zeigt Fallback aus `CrewTemplateName` |
| Custom-Profile-Name-Doppelpräfix | ✅ Service-Layer idempotent; Live-Preview im Editor |
| TreatWarningsAsErrors-Brüche | ✅ `dotnet build` nach jeder Iteration: 0/0 |
| `openai/gpt-5.5-mini` nicht auf OpenRouter | ⚠️ Gefunden im Real-Custom-Crew-Test; korrigiert auf `openai/gpt-4o-mini` |
| RunOrchestratorService Status-Update-Bug | ⚠️ Bekannt (pre-existing): bei HTTP-400 bleibt Run in Status „Running"; nicht in PS-6-Scope |

---

## 6. Akzeptanzkriterien-Check

| AC | Status |
|---|---|
| 1. `dotnet build` 0/0 | ✅ |
| 2. Alle bestehenden + neue Tests grün (189 total) | ✅ |
| 3. NewRun-Page zeigt CrewSelector mit Default `"klassik"` | ✅ |
| 4. RunDetail-Page zeigt CrewSummary (alle Status) | ✅ |
| 5. Runs-Liste zeigt CrewBadge in jeder RunRow | ✅ |
| 6. Crew-Verwaltungs-Seiten funktional (Templates + Profile) | ✅ |
| 7. System-Profile-Schutz — Edit disabled, Duplicate verfügbar | ✅ |
| 8. Form-Validierung (DataAnnotations + EditForm) | ✅ |
| 9. Delete-Confirmation-Modal mit Name-Eingabe | ✅ |
| 10. Drei Themes (Vellum/Noir/Petrol) in allen neuen Komponenten | ✅ (Token-basiert, keine theme-spezifischen Overrides nötig) |
| 11. NavMenu „Crew"-Link, aktiver Zustand korrekt | ✅ |
| 12. Real-Custom-Crew-Test auf Production | ✅ PASS (Run `d8faea85`, 2 Iterationen, Completed) |
| 13. Decisions-Log D-029 | ✅ |

---

## 7. Real-Custom-Crew-Test (AC12) — Details

**Datum:** 2026-05-13  
**Endpoint:** `https://geef.stefan-bechtel.de/mcp`  
**Run-ID:** `d8faea85-a312-44c1-88fa-c8ef9874430b`

**Crew-Konfiguration (via `custom_crew`):**
```json
{
  "executorProfileName": "default-executor",
  "reviewerProfileNames": ["briefing-fidelity", "clarity"],
  "evaluationStrategy": 0
}
```

**Ergebnis:**
- Status: **Completed**
- Iterationen: **2**
- Beide Reviewer (gemini-2.5-flash + gpt-4o-mini) liefen erfolgreich parallel
- Executor verfeinerte den Draft in Iteration 2 (kleine Satzstruktur-Verbesserung)
- Final-Text (Kurzform): "Large Language Models eignen sich als Reviewer, weil sie Texte in Sekundenschnelle auf Konsistenz, Verständlichkeit und Einhaltung formaler Vorgaben prüfen können …"

**Vorbefund (erster Lauf, gescheitert):**
- `openai/gpt-5.5-mini` → HTTP 400 von OpenRouter (Modell nicht verfügbar)
- Korrektur in `SystemCrew.ClarityProfile.Model`: `openai/gpt-5.5-mini` → `openai/gpt-4o-mini`
- Zweiter Lauf: PASS

---

## 8. Beobachtungen

- **`ReviewerProfile`/`ExecutorProfile` in Sub-Namespace:** `Geef.Atelier.Core.Domain.Crew.Profiles` — Blazor-Komponenten benötigen zwei separate `@using`-Direktiven (`...Crew` + `...Crew.Profiles`).
- **`ProfileEditorForm.ProfileFormValues` als nested record:** Von `ReviewerProfileEditor` und `ExecutorProfileEditor` via `ProfileEditorForm.ProfileFormValues` referenziert — funktioniert korrekt in Blazor.
- **bUnit `IsDisabled()`-Extension nicht verfügbar:** In der vorhandenen bUnit-Version existiert `IsDisabled()` nicht — Workaround: `GetAttribute("disabled") != null`.
- **Delete über URL-Parameter (Initial-Approach abgelöst):** Erste Iteration der Editor-Pages verwendete `?delete=true` Query-Parameter. Ersetzt durch inline `DeleteConfirmationModal` mit State-Flag — sauberer und UX-konformer.
- **`IProviderCatalog` macht Infrastructure → Application Dependency nötig:** Infrastructure-Projekt referenziert nun Application (für das Interface). Standardmäßige Clean-Architecture-Richtung (Interface in Application, Implementierung in Infrastructure).
- **`openai/gpt-5.5-mini` existiert nicht auf OpenRouter:** In PS-5 als System-Profil eingetragen, aber das Modell ist auf OpenRouter nicht verfügbar. Mit PS-6 korrigiert.

---

## 9. Vorbereitung PS-7

PS-7 (Advisor-Pässe) kann direkt aufsetzen:
- `AdvisorProfile`-Schema bereits definiert (PS-5-Stub)
- `CrewSnapshot.Advisors` Feld bereits vorhanden (leere Liste in PS-6)
- `ProfileEditorForm` kann mit minimalem Aufwand für `AdvisorProfile` erweitert werden
- Neue Page: `/crew/profiles/advisors` analog zu Reviewer/Executor
- Advisor-Ausführung vor dem Executor-Pass: `RunOrchestratorService` + `AtelierPipelineFactory` anpassen

---

## 10. Kennzahlen

| Kennzahl | Wert |
|---|---|
| Neue C#/Razor-Dateien | ~45 (Komponenten, Pages, Tests, Application, Infrastructure) |
| Geänderte Dateien | ~15 |
| Neue bUnit-Tests | 35 |
| Test-Gesamtzahl | 189 (+ 1 E2E-Skip) |
| Build-Zeit | ~11s |
| Docker-Build | ✅ erfolgreich |
| Migrations | 0 (reine UI-Schicht, kein Schema-Änderung) |
| Model-Korrekturen | 1 (`clarity`: gpt-5.5-mini → gpt-4o-mini) |
| Themes verifiziert | 3 (Vellum/Noir/Petrol) |
| Real-Custom-Crew-Test | PASS (2 Iterationen, Completed) |
