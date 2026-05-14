# Claude-Code-Prompt: Grounding-Phase sichtbar machen

*UI-Erweiterung um die im Code bereits existierende GEEF-Grounding-Phase in der RunDetail-Visualisierung sichtbar zu machen. Plus konzeptionelle Konsolidierung: Pre-Execution-Advisors werden als Grounding-Beitrag visualisiert, nicht mehr als Iteration-Beitrag.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Im Code existiert die GEEF-Grounding-Phase vollständig: `BriefingGroundingStep` (aus Schritt 2, aktuell Pass-Through) und `AdvisorContextGroundingStep` (aus PS-7, setzt Advisor-Kontext). Die UI zeigt sie aber nicht — die RunDetail-Page springt direkt von Briefing zur ersten Iteration, Press-Visualization hat nur drei Stages (Executor → Reviewers → Executor).

Das ist ein **architektonisches UX-Problem**: die UI erzählt eine vereinfachte Geschichte, die Pipeline-Realität bleibt unsichtbar. Plus: Pre-Execution-Advisors (Trigger `BeforeFirstExecution`) gehören konzeptionell zur Grounding-Phase, werden aktuell aber wie Iteration-Beiträge präsentiert.

Deine Aufgabe ist die **Grounding-Visualisierung**: eine eigene "Grounding"-Sektion auf der RunDetail-Page, plus Press-Visualization-Erweiterung um eine vierte Stage. Advisor-Consultations werden nach Trigger gruppiert — `BeforeFirstExecution`-Advisors in die Grounding-Sektion, alle anderen bleiben in der jeweiligen Iteration.

Klein abgegrenzt: UI-Erweiterung + minimales Backend-Refactoring (Advisor-Grouping-Logik). Keine Domain-Modell-Änderungen, keine Migration, keine neue Funktionalität.

## Vorgehen

**Du folgst dem Workflow in `/srv/docker/docs/geef-workflow.md`** in komprimierter Form:
- Phase 1.1 + 1.2: aktuelle UI-Struktur und Grounding-Step-Code lesen
- Phase 1.4 Architect: zwei kleine Knackpunkte
- Phase 2: Implementation (UI + minimal Backend)
- Phase 3: R1 + R2 + R3 + R5 (R4 leichter Pass)
- Phase 4: Bericht
- **Phase 5: Merge & Deploy** (verbindlich, siehe unten)

**Branch:** `feat/grounding-visualization`, PR gegen `main`.

## Pflicht-Lektüre fürs Grounding

1. **`/srv/docker/docs/geef-workflow.md`**
2. **`CLAUDE.md`** im Repo-Root
3. **`docs/Vom_Prompt_zur_Pipeline.pdf`** — besonders Bild 6 (Grounding läuft nur **einmal** pro Run, **wird nicht wiederholt**)
4. **`docs/02-architecture.md`** — die Schichtenarchitektur
5. **`docs/reports/step-02-report.md`** — wie `BriefingGroundingStep` ursprünglich angelegt wurde
6. **`docs/reports/post-skeleton-07-advisor-passes-report.md`** — wie `AdvisorContextGroundingStep` und `AdvisorAwareExecutor` zusammenspielen
7. **Aktueller Code:**
   - `src/Geef.Atelier.Infrastructure/Pipeline/BriefingGroundingStep.cs` — was der Grounding-Step macht
   - `src/Geef.Atelier.Infrastructure/Pipeline/AdvisorContextGroundingStep.cs`
   - `src/Geef.Atelier.Infrastructure/Pipeline/AdvisorAwareExecutor.cs` — wie Trigger-Filterung läuft
   - `src/Geef.Atelier.Core/Domain/Crew/Advisors/AdvisorTrigger.cs` — die drei Trigger-Werte
   - `src/Geef.Atelier.Web/Components/Pages/RunDetail.razor` — die aktuell zu erweiternde Page
   - `src/Geef.Atelier.Web/Components/UI/AdvisorConsultationsBlock.razor` — wie Advisors aktuell gezeigt werden
   - `src/Geef.Atelier.Web/Components/UI/Press.razor` (oder wie auch immer die Press-Visualization-Komponente heißt) — die zu erweiternde Phasen-Anzeige
   - `src/Geef.Atelier.Application/Runs/IRunService.cs` — falls eine neue Helper-Methode für gruppierte Advisor-Consultations sinnvoll ist

## Konkrete Anforderungen

### 1. UI: Grounding-Sektion auf RunDetail

Vor dem ersten Iteration-Panel auf der RunDetail-Page eine neue Sektion einfügen:

```
═══════════════════════════════════════
🔍 Grounding
═══════════════════════════════════════
Briefing
   "Wie viele Farben sind mindestens notwendig, um eine Ebene
    einzufärben, wenn je zwei Punkte mit Abstand 1
    unterschiedlich gefärbt sein müssen?"

[Advisor consultations (1)]               ▾
  briefing-clarifier (Strategic)
    Output: "Briefing requests 700 words expository note..."
    [click to expand full output]

[Grounded Brief]                          ▾
  (currently identical to the original briefing — RAG will
   enrich this in a future step)
═══════════════════════════════════════
```

**Neue Komponente `GroundingSection.razor`** in `Components/UI/`:
- Inputs: `Briefing`, `GroundedBrief` (string, aktuell == Briefing), `BeforeFirstAdvisorConsultations` (Liste)
- Drei Sub-Sektionen: Briefing-Display, Advisor-Consultations (collapse), Grounded-Brief (collapse, mit Hint dass es aktuell identisch ist)
- `data-testid="grounding-section"` auf dem Container
- Atelier-Design-konform (Newsreader-Typografie für Texte, atelier-CSS-Tokens)

**`RunDetail.razor`** anpassen:
- Lädt die Run-Daten wie bisher
- Filtert AdvisorConsultations: nur die mit Trigger `BeforeFirstExecution` für Grounding-Sektion
- Übrige AdvisorConsultations bleiben bei der zugehörigen Iteration

### 2. Backend: Advisor-Grouping-Helper

In `IRunService` oder einem neuen `RunDetailViewModel`-Builder:

```csharp
public sealed record RunWithGroundingViewModel(
    RunDetails Run,
    IReadOnlyList<AdvisorConsultationView> BeforeFirstExecutionAdvisors,
    IReadOnlyList<IterationWithAdvisors> Iterations
);

public sealed record IterationWithAdvisors(
    IterationDetails Iteration,
    IReadOnlyList<AdvisorConsultationView> AdvisorsForThisIteration  // BeforeEveryExecution, OnConvergenceFailure
);
```

`IRunService.GetRunWithGroundingAsync(runId, ct)` liefert das ViewModel.

**Wichtig:** die Trigger-Information muss in `AdvisorConsultation` zugänglich sein. Falls die `AdvisorConsultations`-Tabelle nur `AdvisorProfileName` speichert (nicht den Trigger zum Zeitpunkt der Konsultation), wird der Trigger aus dem `CrewSnapshot` (deserialisiert) gelookup-t per Profile-Name. Architect prüft die Datenstruktur.

### 3. UI: Press-Visualization um Grounding erweitern

Die existierende Press-Visualization (Phasen-Anzeige) hat aktuell drei Stages: Executor → Reviewers → Executor.

**Neue Version:** vier Stages, mit Grounding **außerhalb der Iteration-Schleife** visualisiert:

```
[Grounding]    [Iteration N: Executor → Reviewers → (Executor revising)]
   ●               ○                 ○             ○
   ↑ done          ↑ active
```

- Grounding-Stage **vor** dem Iteration-Loop
- Visuelle Trennung (z.B. dünner Trennstrich) zwischen Grounding und Iteration
- Aktiv-Anzeige bei laufendem Run zeigt, dass Grounding bereits abgeschlossen ist sobald der Executor läuft

`Press.razor` (oder Äquivalent) wird entsprechend erweitert.

### 4. UI: AdvisorConsultationsBlock anpassen

Die bestehende `AdvisorConsultationsBlock.razor` (aus PS-7) wird aktuell pro Iteration gerendert mit allen Advisors. Nach diesem Step:

- In der Grounding-Sektion: nur die mit Trigger `BeforeFirstExecution`
- In jeder Iteration: nur die mit Trigger `BeforeEveryExecution` oder `OnConvergenceFailure`

Die Komponente bleibt strukturell gleich, bekommt aber einen Filter-Parameter `TriggerFilter` (Optional).

### 5. ReviewerDisplay-Erweiterung (optional)

Falls die UI Trigger-Labels anzeigt (z.B. "(Strategic, Before First Execution)"), die existierende `ReviewerDisplay`-Helper-Klasse bekommt einen `GetTriggerDisplay(AdvisorTrigger trigger)` — User-freundliche Labels:
- `BeforeFirstExecution` → "Pre-Run (Grounding)"
- `BeforeEveryExecution` → "Per-Iteration"
- `OnConvergenceFailure` → "On Convergence Failure (Recovery)"

### 6. Tests

**bUnit-Tests:**
- `GroundingSectionTests` — Briefing-Display, Advisor-Consultations-Rendering, Grounded-Brief-Hint
- `RunDetailWithGroundingTests` — RunDetail-Page rendert Grounding-Sektion vor Iterations
- `AdvisorConsultationsBlockFilterTests` — Trigger-Filter funktioniert
- `PressVisualizationWithGroundingTests` — vier Stages werden gerendert

**Application-Tests:**
- `RunWithGroundingViewModelTests` — Advisors werden korrekt nach Trigger gruppiert

**Regression-Tests:**
- Bestehende RunDetail-Tests bleiben grün (mit angepassten Selektoren falls nötig)

### 7. Real-UI-Test (Pflicht-AC)

Auf der Production-Instance `https://geef.stefan-bechtel.de/`:

1. Custom-Template anlegen mit `briefing-clarifier` (Trigger: `BeforeFirstExecution`) **und** `devils-advocate` (Trigger: `BeforeEveryExecution`)
2. Briefing einreichen
3. RunDetail beobachten:
   - Grounding-Sektion zeigt briefing-clarifier-Consultation
   - Iteration 1 zeigt devils-advocate-Consultation (BeforeEveryExecution für Iter 1)
   - Iteration 2 zeigt devils-advocate-Consultation (BeforeEveryExecution für Iter 2)
4. Theme-Wechsel in allen drei Themes (Vellum/Noir/Petrol): Grounding-Sektion visuell konsistent
5. Press-Visualization zeigt Grounding-Stage vor dem Iteration-Loop

Beobachtungen im Bericht.

## Akzeptanzkriterien

1. `dotnet build` 0 Errors, 0 Warnings.
2. `dotnet test` — alle bestehenden Tests grün + neue Tests.
3. **`GroundingSection`-Komponente** auf RunDetail sichtbar, mit Briefing, Advisor-Consultations (BeforeFirstExecution), Grounded-Brief.
4. **AdvisorConsultations werden nach Trigger gruppiert** — Pre-Run-Advisors in Grounding, andere in Iterations.
5. **Press-Visualization** zeigt vier Stages: Grounding (außerhalb der Iteration) + Executor → Reviewers → Executor (innerhalb).
6. **`RunWithGroundingViewModel`** im Application-Layer funktional.
7. **Real-UI-Test auf Production** durchgeführt und im Bericht dokumentiert (Lehre aus PS-5/PS-6).
8. **Drei Themes (Vellum/Noir/Petrol)** funktionieren in der neuen Komponente.
9. **Historische Runs mit alten Advisor-Trigger-Daten** rendern korrekt (Backward-Compat-Check).
10. **Decisions-Log-Eintrag** mit Architect-Entscheidungen.
11. **Merge auf `main` durchgeführt** (PR gemerged oder Direct-Push falls Refactor klein bleibt).
12. **Production-Deploy verifiziert** — Container neu gebaut, Health-Check grün, RunDetail-Page live mit neuer Grounding-Sektion.

## Phase 5 — Merge & Deploy (verbindlich)

Direkt nach Phase 4 (Bericht), bevor der Step als abgeschlossen gilt:

```bash
# Merge auf main
cd /srv/docker/websites/geef_atelier
git checkout main
git pull --ff-only
gh pr merge <PR-Number> --merge --delete-branch
# oder: Direct-Merge falls Branch nicht-konflikt
git checkout main
git merge --no-ff feat/grounding-visualization
git push origin main

# Deploy
docker compose build --no-cache web
docker compose up -d

# Health-Check
docker compose ps  # alle services healthy
curl -I https://geef.stefan-bechtel.de/  # HTTP 200/302
```

**Live-Verifikation:**
- RunDetail-Page öffnen, Grounding-Sektion sichtbar
- Theme-Wechsel testen in Vellum/Noir/Petrol
- Bei Issues: Direct-Fix-Commits oder klare Folge-Step-Notiz

Im Bericht festhalten: Merge-Commit-Hash, Deploy-Timestamp, Live-Verifikations-Ergebnis pro AC.

## Was du in diesem Step NICHT tust

- **Kein echtes RAG-Grounding** — Grounded Brief bleibt aktuell == Briefing. Echtes Briefing-Pre-Processing (Audience-Erkennung, Quellen-Recherche) ist eigener Step.
- **Keine neuen Advisor-Trigger** — die drei Werte aus PS-7 bleiben.
- **Keine Domain-Modell-Änderungen** — `AdvisorConsultation`-Entity bleibt unverändert.
- **Keine Migration** — Trigger-Information kommt aus dem persistierten CrewSnapshot.
- **Keine MCP-Tool-Änderungen** — Atelier-spezifische UI-Konsolidierung.
- **Keine `BriefingGroundingStep`-Erweiterung** — der bleibt Pass-Through.

## Architect-Konsultation (Phase 1.4) — zwei Knackpunkte

1. **Trigger-Information im AdvisorConsultation-Lookup:** Die Tabelle `AdvisorConsultations` enthält `AdvisorProfileName`, nicht direkt den Trigger. Optionen:
   - **(a)** Trigger zum Snapshot-Zeitpunkt aus `CrewSnapshot.Advisors` deserialisieren
   - **(b)** Trigger-Spalte zu `AdvisorConsultations` hinzufügen (DB-Migration nötig, denormalisiert)
   
   Empfehlung: **(a)** — keine Migration, Snapshot ist Source-of-Truth. Architect bestätigt nach Code-Inspektion.

2. **Press-Visualization-Strategie:** Vier separate Stages vs. zweiteilige Anzeige (Grounding | Iteration-Loop). Empfehlung: **zweiteilig** — visuell klarer dass Grounding einmalig läuft. Architect bestätigt.

## Reviewer-Hinweise

- **R1 (Functional Correctness):** Alle 12 ACs. Besonders 4 (Trigger-Gruppierung), 7 (Real-UI), 12 (Deploy).
- **R2 (Code Quality):** Saubere Komponenten-Trennung, ViewModel-Pattern konsequent.
- **R3 (Test Execution):** Alle Tests grün, neue bUnit-Tests für die neuen Komponenten.
- **R4 (Architecture Compliance):** ViewModel im Application-Layer, keine Layer-Verletzung.
- **R5 (Live UI):** Real-Test auf Production in allen drei Themes, Screenshots im Bericht.

## Persistenter Abschlussbericht

Bericht nach `docs/reports/feature-grounding-visualization-report.md`. Inhalt:

1. **Was wurde umgesetzt** — UI-Komponenten, Backend-ViewModel
2. **Architect-Output** — die zwei Knackpunkte
3. **Trigger-Lookup-Strategie** — wie der Trigger im UI bestimmt wird
4. **Real-UI-Test-Ergebnis** — Custom-Template-Test mit beiden Advisor-Triggern
5. **Akzeptanzkriterien-Check** — Tabelle mit allen 12 ACs
6. **Merge-Commit-Hash + Deploy-Timestamp** — verbindlich
7. **Live-Verifikations-Screenshots** — drei Themes
8. **Beobachtungen** — Architektur-Konsistenz erreicht?
9. **Empfehlungen** — echtes RAG-Grounding als Folge-Step? Briefing-Parsing für Audience-Hints?

## Konventionen

- C#-Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- UI-Strings: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- **Niemals Secrets** in source control, Logs oder Bericht.

Erwarteter Aufwand: 1 Arbeitstag inklusive Deploy.

---

**Nach erfolgreichem Abschluss:** Atelier-UI zeigt die GEEF-Architektur ehrlich. Grounding ist sichtbar — auch wenn aktuell minimal aktiv. Pre-Execution-Advisors stehen konzeptionell korrekt im Grounding-Block. Foundation für künftiges RAG-Grounding (Quellen-Recherche, Briefing-Anreicherung) ist da — die Grounding-Sektion erwartet einfach mehr Inhalt.