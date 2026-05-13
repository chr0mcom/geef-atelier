# Claude-Code-Prompt: Post-Skeleton Schritt 6 — UI für Crew-Komposition

*Dieser Prompt baut die UI-Schicht über die PS-5-Crew-Foundation. Crew-Template-Auswahl auf NewRun, Crew-Anzeige auf RunDetail, vollständige Profile- und Template-Verwaltungs-Seiten mit CRUD. Das Atelier-Design aus PS-3 bleibt durchgehend erhalten.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. PS-3 (Design-Translation) und PS-5 (Crew-Foundation) sind durch. Die Backend-Schicht für dynamische Crew-Komposition steht vollständig — `ICrewService`, `ProfileBasedReviewer`/`ProfileBasedExecutor`, `CrewSnapshot`, System-Profile + Klassik-Template, MCP-Tools. Aber die UI weiß davon nichts: NewRun-Page submits noch ohne Crew-Wahl, RunDetail zeigt die verwendete Crew nicht an, es gibt keine Profile-/Template-Verwaltung.

Deine Aufgabe ist **PS-6**: die UI-Schicht über die PS-5-Foundation bauen. Vier zusammenhängende UI-Erweiterungen — Crew-Auswahl auf NewRun, Crew-Anzeige auf RunDetail, vollständige Profile-Verwaltung (Reviewer + Executor), vollständige Template-Verwaltung.

Das Atelier-Design aus PS-3 bleibt durchgehend erhalten: dieselben Color-Tokens, dieselbe Typografie, dieselben Icons, dasselbe Theme-System (Vellum/Noir/Petrol). Neue Komponenten fügen sich in die Atelier-Bibliothek ein.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors. **Plan-Phase-Integration** als Architect-Form. R5 (Live UI) ist diesmal **substantiell** — neue Pages, neue Forms, alle drei Themes verifizieren.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`CLAUDE.md`** im Repo-Root.
3. **`docs/01-vision-and-scope.md`** — der Vision-Kontext.
4. **`docs/02-architecture.md`** — Schichtenbild, jetzt mit Crew-Sektion.
5. **`docs/08-crew-system.md`** — die Crew-System-Doku aus PS-5.
6. **`docs/05-decisions-log.md`**, alle Einträge bis D-028.
7. **`docs/reports/post-skeleton-03-design-translation-report.md`** — UI-Foundation und etablierte Patterns.
8. **`docs/reports/post-skeleton-05-crew-foundation-report.md`** — Backend-API und MCP-Tools.
9. **Aktueller Code:**
   - `src/Geef.Atelier.Application/Crew/ICrewService.cs` und Implementation — das ist die UI-API
   - `src/Geef.Atelier.Core/Domain/Crew/` — alle Domain-Records (Profile, Template, Snapshot, Spec)
   - `src/Geef.Atelier.Web/Components/UI/` — bestehende UI-Bibliothek aus PS-3
   - `src/Geef.Atelier.Web/Components/Pages/` — Login, Index, New, Runs, RunDetail
   - `src/Geef.Atelier.Web/Components/Layout/MainLayout.razor` und `NavMenu.razor`
   - `src/Geef.Atelier.Web/wwwroot/atelier.css` — Design-Tokens, neue CSS-Klassen werden hier ergänzt
   - `src/Geef.Atelier.Web/Display/ReviewerDisplay.cs` — wird erweitert
10. **MCP-Tools aus PS-5:** `ListCrewTemplatesTool`, `ListReviewerProfilesTool`, `SubmitRequestTool` mit Crew-Parametern — als Referenz wie das Domain-Layer angesprochen wird.

## Verbindliche Entscheidungen

Diese Entscheidungen sind im Brainstorming fixiert — Architect bestätigt sie im Plan, weicht nur mit klarem Grund ab:

| Entscheidung | Konkret |
|---|---|
| **Design-Konsistenz** | Atelier-CSS-Tokens, Schriften, Icons aus PS-3. Drei Themes funktionieren in allen neuen Komponenten. |
| **Routing-Struktur** | `/crew` als Crew-Landing, `/crew/templates`, `/crew/templates/new`, `/crew/templates/{name}`, `/crew/profiles/reviewers`, `/crew/profiles/reviewers/new`, `/crew/profiles/reviewers/{name}`, `/crew/profiles/executors` analog. |
| **NavMenu-Erweiterung** | Neuer Top-Level-Link **"Crew"** zwischen "New briefing" und "Style guide" (falls vorhanden). Oder im UserMenu — Architect entscheidet basierend auf NavMenu-Sättigung. |
| **CrewSelector auf NewRun** | Dropdown mit Display-Name + Description-Snippet pro Option. Default: `"klassik"`. Plus dezenter Link "Manage crews →" der zu `/crew/templates` führt. |
| **Custom-Crew auf NewRun** | **Nicht** in PS-6. Inline-Ad-hoc-Crew-Builder ist UX-komplex, kommt später. NewRun nur Template-Auswahl. |
| **CrewSummary auf RunDetail** | Im Run-Header als dezent Zeile *"Crew: Klassik · 1 Executor + 2 Reviewers"* mit Click-to-Expand. Expandiert zeigt: Executor-Profile (Name + Modell), Reviewer-Liste (Name + Modell), EvaluationStrategy, ggf. ConvergencePolicy-Override. |
| **Crew-Badge auf RunRow** | Dezenter Text-Badge mit Template-Namen (z.B. `klassik`, `custom-juristisch`). Bei Custom-Crew ohne Template-Name: `custom`. |
| **System-Profile/Template read-only** | Edit-Buttons disabled mit Tooltip "System profile — duplicate to customize". Action **"Duplicate as custom"** prominent als primärer Button. |
| **Custom-Auto-Prefix** | Service-Layer setzt automatisch `custom-` (PS-5-Logik). UI zeigt vor dem Speichern den finalen Namen ("Wird gespeichert als: custom-juristisch"). |
| **Form-Validierung** | DataAnnotations für UI-Form-Logic + Service-Layer-Validation für Business-Rules (Konflikt, Provider-Existenz). Beide-Ebenen-Strategie konsistent mit der bestehenden NewRun-Form. |
| **Delete-Confirmation** | Blazor-internes Modal (kein Browser-Confirm). Eingabefeld zur Bestätigung: User muss den Profile/Template-Namen tippen, um Delete zu aktivieren. |
| **Reviewer-Reihenfolge im Template-Editor** | Up/Down-Pfeil-Buttons (keine Drag-Drop). Einfach, ohne JS-Interop, ausreichend für 2-5 Reviewer. |
| **Provider-Dropdown im Profile-Editor** | Liste aus den konfigurierten `LlmOptions.Providers`-Keys (aktuell `openrouter` und `cli`). Wenn neuer Provider in Zukunft dazukommt, wird er automatisch auswählbar. |
| **Model-Eingabe im Profile-Editor** | Free-text TextBox in PS-6. Spätere Erweiterung: Dropdown mit bekannten Modellen pro Provider (separater Step). |
| **EvaluationStrategy-Auswahl** | Radio-Group mit Beschreibungs-Text pro Option (Parallel/Sequential/FailFast/Priority). Default: Parallel. Beschreibungen aus dem GEEF-Artikel. |
| **ConvergencePolicy-Override** | Collapse-Section "Advanced", standardmäßig geschlossen. Drinnen vier optionale Felder (MaxIterations, AbortOnCritical, DetectRegression, StagnationThreshold). Leer → kein Override, nutzt globalen Default aus PS-2. |
| **AC für Real-UI-Test** | Pflicht-AC: mindestens ein Real-Run mit User-erstellter Custom-Crew auf Production verifiziert. Lehre aus PS-5 (AC11). |
| **`data-testid`-Pattern** | Für alle neuen Komponenten konsistent mit PS-3-Praxis. |

## Konkrete Anforderungen

### 1. NewRun-Page-Erweiterung

**`New.razor`** bekommt einen neuen Form-Abschnitt zwischen Briefing-Textarea und Submit-Button:

```
[Briefing-Textarea]
[Character/Word-Counter]
[Crew-Selector]               ← NEU
[Submit-Button]
```

**`CrewSelector`-Komponente** (`Components/UI/CrewSelector.razor`):
- Dropdown mit `ICrewService.ListCrewTemplatesAsync()` als Datenquelle
- Pro Option: Display-Name + Description-Snippet (max 80 Zeichen)
- System-Badge falls `IsSystem == true`
- Default-Auswahl: `"klassik"`
- Plus dezenter Link "Manage crews →" rechts neben dem Dropdown, führt zu `/crew/templates`
- `data-testid="crew-selector"` auf dem Dropdown

**Submit-Logik:** `crewTemplateName` wird aus der Selector-Auswahl an `IRunService.SubmitRunAsync(...)` übergeben.

### 2. RunDetail-Page-Erweiterung

**Run-Header** bekommt eine dezent Crew-Zeile direkt unter den Meta-Daten (Submitted/Started/Completed/Tokens):

```
Status: Completed · Submitted 2 hours ago · 18,724 tokens · $ —
Crew: Klassik · 1 Executor + 2 Reviewers ▾    ← NEU, klickbar
```

**`CrewSummary`-Komponente** (`Components/UI/CrewSummary.razor`):
- Collapsed-Zeile mit Template-Namen + Crew-Größe
- Click-to-Expand zeigt:
  - **Executor:** Display-Name · Provider/Model
  - **Reviewers** (in Snapshot-Reihenfolge): Display-Name · Provider/Model · Severity-Indikator falls Findings vorhanden
  - **EvaluationStrategy:** Display-Name + Mini-Beschreibung
  - **ConvergencePolicy-Override** falls vorhanden: Liste der Override-Werte
- Falls `RunEntity.CrewTemplateName == null` (Custom-Crew via API): `"Custom crew"` als Label
- `data-testid="crew-summary"` auf dem Container

**Daten:** `RunEntity.CrewSnapshot` wird in der Page deserialisiert (oder via neuem `IRunService`-Helper-Methode bereitgestellt).

### 3. Runs-Liste-Erweiterung

**`RunRow`-Komponente** bekommt einen dezent Crew-Badge:

```
[Status-Glyph] [Briefing-Snippet]                    [Tokens] [Created]
                                                              [klassik]   ← NEU dezent
```

- Text-Badge mit `RunEntity.CrewTemplateName` (oder `"custom"` wenn null)
- Kleiner als Status-Badge, andere visuelle Hierarchie
- `data-testid="crew-badge"` auf dem Element

### 4. Crew-Landing-Page (`/crew`)

**`CrewIndex.razor`** als Übersicht mit drei Sektionen:

- **Crew Templates** — Mini-Liste der letzten 3-5 Templates mit Link "Alle anzeigen →"
- **Reviewer Profiles** — analog
- **Executor Profiles** — analog

Jede Sektion hat eine "Neu anlegen"-Action.

### 5. Crew-Template-Verwaltung

**`/crew/templates`** — Liste aller Templates (System + Custom):

- Tabellen-Layout: Display-Name, Description, Executor, Reviewer-Count, Strategy, System-Badge
- System-Templates: Zeile mit dezent abgesetztem Hintergrund + "System"-Pill
- Custom-Templates: Zeile normal + Edit-Icon + Delete-Icon
- Floating "Neues Template"-Button oben rechts
- `data-testid="template-list"`

**`/crew/templates/new`** und **`/crew/templates/{name}`** — Editor:

**`CrewTemplateEditor`-Komponente** (`Components/UI/CrewTemplateEditor.razor`):
- Felder: Name (Auto-Prefix-Preview), DisplayName, Description (Textarea)
- **Executor-Wahl:** Dropdown aus `ICrewService.ListExecutorProfilesAsync()`
- **Reviewer-Liste:** Picker mit:
  - Verfügbare Profile (linke Liste)
  - Ausgewählte Profile (rechte Liste, Reihenfolge editierbar)
  - Up/Down-Pfeil-Buttons für Reihenfolge
- **EvaluationStrategy:** Radio-Group (Parallel/Sequential/FailFast/Priority) mit Beschreibungs-Text pro Option
- **ConvergencePolicy-Override:** Collapse "Advanced"
  - MaxIterations (Number, optional)
  - AbortOnCritical (Checkbox, optional)
  - DetectRegression (Checkbox, optional)
  - StagnationThreshold (Number, optional)
- **Save-Button** + **Cancel-Button**
- Bei Edit eines System-Templates: alle Felder disabled, prominenter Button "Duplicate as custom"
- `data-testid="template-editor"` auf Container, `data-testid="save-template"` auf Save

**Duplicate-as-Custom-Logik:**
- Erstellt neue Custom-Template mit Prefix
- Display-Name wird automatisch zu `"{Original} (Copy)"`
- User wird auf den neuen Editor weitergeleitet zum Anpassen

**Delete-Modal:**
- Modaler Dialog
- Warnung: "Templates werden in historischen Runs als Snapshot bewahrt — der Delete betrifft nur zukünftige Submits."
- Eingabefeld: User muss Template-Namen tippen, um Delete zu aktivieren
- `data-testid="delete-confirm"`

### 6. Reviewer-Profile-Verwaltung

**`/crew/profiles/reviewers`** — analog zur Template-Liste.

**`/crew/profiles/reviewers/new`** und **`/crew/profiles/reviewers/{name}`** — Editor:

**`ProfileEditor`-Komponente** (`Components/UI/ProfileEditor.razor`, generisch für Reviewer + Executor):
- Felder: Name (Auto-Prefix-Preview), DisplayName, Description
- **SystemPrompt** — großzügige Textarea (mindestens 15 Zeilen, Monospace-Font, JetBrains Mono), max 8000 Zeichen Counter
- **Provider** — Dropdown aus `LlmOptions.Providers`-Keys
- **Model** — TextBox (free-text in PS-6)
- **MaxTokens** — Number, optional (Default: Provider-Default)
- Save + Cancel
- Bei Edit eines System-Profile: alle disabled, "Duplicate as custom"-Action

### 7. Executor-Profile-Verwaltung

**`/crew/profiles/executors`** — analog. Verwendet dieselbe `ProfileEditor`-Komponente mit `ProfileKind: Executor`-Parameter (unterscheidet sich nur in den Service-Methoden, die aufgerufen werden).

### 8. NavMenu-Erweiterung

**`NavMenu.razor`** bekommt einen neuen Link **"Crew"** zwischen "New briefing" und ggf. anderen Top-Level-Links. Aktiver Zustand: alle `/crew*`-Routen.

### 9. ReviewerDisplay-Erweiterung

**`Display/ReviewerDisplay.cs`** wird um zusätzliche Methoden erweitert:
- `GetExecutorDisplay(string profileName)` — analog zu existierender Reviewer-Mapping-Logik
- `GetTemplateDisplay(string templateName)` — `"klassik"` → `"Klassik"`, etc.

System-Templates und System-Profile haben jetzt feste Display-Namen aus den `SystemCrew`-Konstanten — die werden bevorzugt vor dem `ReviewerDisplay`-Mapping.

### 10. Tests

**bUnit-Komponenten-Tests:**
- `CrewSelectorTests` — Dropdown-Befüllung, Default-Auswahl, Selektion
- `CrewSummaryTests` — Collapsed/Expanded-State, Custom-Crew-Label
- `ProfileEditorTests` — Form-Validierung, System-Profile-Disabled-State
- `CrewTemplateEditorTests` — Reviewer-Picker, Reihenfolge-Buttons, Strategy-Auswahl

**E2E-Playwright-Tests:**
- `CrewSelectorE2ETests` — NewRun mit verschiedenen Templates submitten
- `CustomProfileCrudE2ETests` — Anlegen, Editieren, Löschen eines Custom-Profile
- `CustomTemplateCrudE2ETests` — analog
- `DuplicateAsCustomE2ETests` — System-Profile duplizieren und modifizieren

**Bestehende Tests müssen grün bleiben** (154 nach PS-5). NewRun-, Runs-List-, RunDetail-Tests müssen mit den neuen Komponenten umgehen — minimal angepasste Selektoren erwartet.

### 11. Real-UI-Verifikation

**Pflicht für AC-Erfüllung (Lehre aus PS-5-AC11):**

Auf `https://geef.stefan-bechtel.de/`:
1. Login → `/crew/profiles/reviewers/new` → Custom-Reviewer-Profile mit alternativem Modell anlegen
2. `/crew/templates/new` → Custom-Template mit Klassik-Executor + Custom-Reviewer + bestehendem `clarity`-Reviewer
3. `/new` → Briefing submitten mit Custom-Template
4. Run beobachten → Custom-Crew wird in Summary korrekt angezeigt → Pipeline läuft mit den richtigen Modellen
5. Resultat dokumentieren im Bericht: Welche Modelle, welche Convergence, welche Token-Verbrauch

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` 0 Errors, 0 Warnings.
2. `dotnet test` — alle bestehenden Tests grün (154 nach PS-5 + neue Crew-UI-Tests).
3. **NewRun-Page** zeigt CrewSelector mit Default `"klassik"`. Submit übergibt `crewTemplateName` korrekt.
4. **RunDetail-Page** zeigt CrewSummary in allen Run-Status (Pending/Running/Completed/Failed/Aborted). Click-to-Expand funktional.
5. **Runs-Liste** zeigt CrewBadge in jeder RunRow.
6. **Crew-Verwaltungs-Seiten** funktional:
   - Template-Liste, Template-Editor (New + Edit + Duplicate-as-Custom + Delete)
   - Reviewer-Profile-Liste, Editor analog
   - Executor-Profile-Liste, Editor analog
7. **System-Profile/Template-Schutz** — Edit-Versuche disabled, "Duplicate as custom" verfügbar.
8. **Form-Validierung** funktional auf allen Editoren.
9. **Delete-Confirmation-Modal** mit Name-Eingabe-Bestätigung funktional.
10. **Drei Themes (Vellum/Noir/Petrol)** funktionieren in allen neuen Komponenten — visuelle Verifikation.
11. **NavMenu** zeigt "Crew"-Link, aktiver Zustand korrekt für alle `/crew*`-Routen.
12. **Real-Custom-Crew-Test auf Production** durchgeführt und im Bericht dokumentiert (Lehre aus PS-5-AC11).
13. **Decisions-Log-Eintrag** (D-029 oder nächste freie Nummer) mit Architect-Entscheidungen.

## Was du in diesem Schritt NICHT tust

- **Kein Ad-hoc-Custom-Crew-Builder auf NewRun** — Inline-Crew-Komposition ohne Template ist UX-komplex, separater Step.
- **Keine Advisor-UI** — PS-7.
- **Keine Backend-Änderungen** — `ICrewService`, `IRunService`, MCP-Tools, Pipeline-Layer bleiben unverändert. Nur Display-Helpers (`ReviewerDisplay`) werden minimal erweitert.
- **Keine Cost-Tracking-Aktivierung** — bleibt Stub `$ —` aus PS-3.
- **Keine Welcome-Stats-Aktivierung** — bleibt Stub aus PS-3.
- **Keine Domain-Modell-Änderungen** — `RunEntity`, `CrewTemplate`, `CrewSnapshot` bleiben unverändert.
- **Kein Model-Dropdown im Profile-Editor** — Free-text in PS-6, Dropdown in späterem Step.
- **Keine Audit-Trail-Erweiterung** für Profile-Edits — wer hat wann was geändert, ist Single-User-Atelier irrelevant.

## Architect-Konsultation (Phase 1.4) — drei echte Knackpunkte

(Diesmal kurz, weil die Klärungs-Antworten viel fixiert haben.)

1. **CrewSnapshot-Deserialization auf der RunDetail-Page:** Die `CrewSummary`-Komponente braucht den deserialisierten `CrewSnapshot`. Optionen: (a) `IRunService` bekommt eine Helper-Methode `GetRunWithCrewAsync(runId)` die den Snapshot bereits deserialisiert, (b) die Razor-Page deserialisiert selbst aus `RunEntity.CrewSnapshot` (JSON-String). Empfehlung: **(a)** — saubere Schichtung. Architect bestätigt.

2. **Routing-Konflikte:** `/crew/profiles/reviewers/{name}` — der Name kann `custom-` oder beliebige Strings enthalten. Welche Constraints? Empfehlung: Pattern `[a-z0-9\-]+`, max 64 Zeichen. Architect bestätigt.

3. **Form-Submit-Strategy:** Static SSR mit `@formname` (wie LoginForm aus Schritt 8), oder Interactive-Server für die Editor-Forms? Empfehlung: **Interactive-Server** für die Editor-Forms (Reviewer-Reihenfolge-Buttons brauchen sowieso Interaktivität), Static SSR nur für Login und Submit. Architect bestätigt.

`geef_architecture.md` prüft Konsistenz mit der UI-Schicht-Doku.

## Reviewer-Hinweise

- **R1 (Functional Correctness):** Alle 13 ACs prüfen. Besonders 3-6 (UI-Funktionalität), 12 (Real-Custom-Crew-Test).
- **R2 (Code Quality):** Komponenten-Aufteilung sinnvoll. Generische `ProfileEditor` für Reviewer + Executor sauber implementiert. Scoped CSS pro Komponente. `data-testid` konsistent.
- **R3 (Test Execution):** Alle Tests grün, neue bUnit-Tests + E2E-Tests dokumentiert.
- **R4 (Architecture Compliance):** "Keine HTML in Pages außer trivialen Steuerelementen" weiter. Crew-UI-Komponenten in `Components/UI/`, Pages bleiben dünn.
- **R5 (Live UI):** Screenshot-Verifikation pro neue Komponente in **allen drei Themes**. Real-Custom-Crew-Test auf Production-URL dokumentiert.

## Persistenter Abschlussbericht

Bericht nach `docs/reports/post-skeleton-06-crew-ui-report.md`. Inhalt:

1. **Was wurde umgesetzt** — Screen für Screen.
2. **Architect-Output** — die drei Knackpunkte plus alle implizit getroffenen Entscheidungen.
3. **Neue Komponenten-Inventar** — Liste mit Zweck pro Komponente.
4. **Real-Custom-Crew-Test-Ergebnis** — welche Crew, welche Modelle, welche Beobachtungen. Falls Konvergenz besser/schlechter als Klassik — auch dokumentieren.
5. **Pre-Mortem & Devil's Advocate** — Validation-Edge-Cases, System-Profile-Schutz-Lücken, Theme-Konsistenz-Brüche, E2E-Flakiness.
6. **Screenshot-Vergleiche** — neue Komponenten in allen drei Themes.
7. **Reviewer-Iterationen** — Tabelle.
8. **Akzeptanzkriterien-Check** — Tabelle mit allen 13 ACs.
9. **Beobachtungen** — wie aufwändig waren die Editoren? Welche Form-Validierungs-Probleme tauchten auf?
10. **Vorbereitung für PS-7** — was hat sich an UI-Anchors etabliert, wovon PS-7 (Advisor) profitiert?

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- UI-Strings: **Englisch** (konsistent mit PS-3).
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- **Niemals Secrets** in source control, Logs oder Bericht.
- Custom-Profile-Namen werden im Bericht **anonymisiert**, falls sie sensitive Domain-Bezüge enthalten.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.

---

**Nach erfolgreichem Abschluss:** Der Atelier-Nutzer kann seine Crew per UI komponieren und verwalten. Custom-Reviewer und -Executor-Profile sind anlegbar, Templates können System-Crews als Ausgangspunkt nutzen ("Duplicate as custom"). PS-7 baut darauf Advisor-Profile + Advisor-Pässe auf, die genauso über die etablierten Crew-Mechaniken verwaltbar werden.