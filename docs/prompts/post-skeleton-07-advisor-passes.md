# Claude-Code-Prompt: Post-Skeleton Schritt 7 — Advisor-Pässe

*Aktiviert die Advisor-Schicht, deren Schema in PS-5 als Stub vorbereitet wurde. Backend (Profile, Pipeline-Integration, Persistierung) + UI (Verwaltung, Anzeige) in einem Step, weil eng zusammengehörend. Läuft parallel zum Run-Status-Bug-Fix.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. PS-5 hat das Schema für `AdvisorProfile` als Stub-Klasse mit `AdvisorMode`-Enum vorbereitet. `CrewSnapshot` hat ein leeres `Advisors`-Feld. `CrewTemplate` hat eine leere `AdvisorProfileNames`-Liste. Aber: keine Persistierung, keine Pipeline-Integration, keine UI, kein funktionaler Effekt.

Deine Aufgabe ist **PS-7**: die Advisor-Schicht vollständig aktivieren. Advisor-Profile werden CRUD-fähig (analog zu Reviewer-/Executor-Profilen), CrewTemplates können sie referenzieren, und Pipeline ruft Advisor-Pässe als Pre-Executor-Hooks auf, deren Output an den Executor weitergegeben wird. UI bekommt Verwaltungs-Seiten und Anzeige-Komponenten.

Das ist der finale Vision-Schritt aus D-001 ("Text-Manufaktur mit Crew") — nach Reviewer-Spezialisierung (PS-5) und Crew-UI (PS-6) jetzt die strategische Beratungsschicht. Der konzeptionelle Hintergrund steht im Anthropic-Advisor-Artikel (`docs/Vom_Berater_im_Hintergrund.pdf`).

**Parallel-Hinweis:** Bug-Fix für Run-Status-bei-Provider-Error läuft parallel und berührt denselben `RunOrchestratorService`. Halte deine Pipeline-Erweiterungen **orthogonal zum Error-Handling** — füge Advisor-Aufrufe als zusätzliche Pipeline-Schritte ein, ohne den Error-Handling-Code zu verändern. Falls Bug-Fix vor diesem Step gemerged wird: rebase auf main, übernimm den verbesserten Error-Handling-Pfad für deine Advisor-Pipeline-Erweiterungen automatisch.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors. **Plan-Phase-Integration** als Architect-Form.

**Phase 1.2 ist recherche-lastig:** Bietet das Geef-SDK einen `IAdvisor`-Vertrag oder einen Pre-Executor-Hook? Falls nicht, muss Atelier eine eigene Pipeline-Wrapper-Schicht bauen. Plus: Wie sieht das Pipeline-Building im SDK heute aus — können wir vor `IExecutor` zusätzliche Steps einschieben, oder müssen wir den Executor selbst dekorieren?

**Branch-Empfehlung:** `feat/advisor-passes`. PR gegen `main` (Workflow-Empfehlung aus M1: PRs für Steps mit Migrations).

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`CLAUDE.md`** im Repo-Root.
3. **`docs/01-vision-and-scope.md`** — Vision-Kontext.
4. **`docs/02-architecture.md`** — Schichtenbild mit Crew-Sektion aus PS-5.
5. **`docs/08-crew-system.md`** — die Crew-System-Doku.
6. **`docs/05-decisions-log.md`**, alle Einträge bis D-029 (oder D-030 falls Bug-Fix schon gemerged).
7. **`docs/Vom_Berater_im_Hintergrund.pdf`** — der **konzeptionelle Pflicht-Input**. Besonders die Sektionen zu Advisor-Modes, Risiken (Authority Bias), und SDK-Empfehlungen.
8. **`docs/reports/post-skeleton-05-crew-foundation-report.md`** — was im PS-5-Stub schon vorgesehen ist.
9. **`docs/reports/post-skeleton-06-crew-ui-report.md`** — die UI-Patterns aus PS-6 (Modal, ProfileEditorForm, etc.).
10. **Aktueller Code:**
    - `src/Geef.Atelier.Core/Domain/Crew/Profiles/AdvisorProfile.cs` (PS-5-Stub)
    - `src/Geef.Atelier.Core/Domain/Crew/CrewSnapshot.cs` (mit leerem `Advisors`-Feld)
    - `src/Geef.Atelier.Core/Domain/Crew/CrewTemplate.cs` (mit leerer `AdvisorProfileNames`-Liste)
    - `src/Geef.Atelier.Application/Crew/CrewSnapshotBuilder.cs` — wie die Snapshots gebaut werden
    - `src/Geef.Atelier.Infrastructure/Pipeline/AtelierPipelineFactory.cs` — wo die Advisor-Hooks rein müssen
    - `src/Geef.Atelier.Infrastructure/Pipeline/ProfileBasedReviewer.cs` und `ProfileBasedExecutor.cs` — als Referenz für `ProfileBasedAdvisor`
    - `src/Geef.Atelier.Mcp/Tools/SubmitRequestTool.cs` — wo Advisor-Crews durchgereicht werden
    - `src/Geef.Atelier.Web/Components/UI/ProfileEditorForm.razor` — wird für Advisor wiederverwendet
    - `src/Geef.Atelier.Web/Components/Pages/Crew/` — wo die neuen Advisor-Pages hin müssen

## Verbindliche Entscheidungen

Diese Entscheidungen sind direkt fixiert — Architect bestätigt im Plan, weicht nur mit klarem Grund ab:

| Entscheidung | Konkret |
|---|---|
| **AdvisorTrigger** | Pro AdvisorProfile konfigurierbar: `BeforeFirstExecution` (Default), `BeforeEveryExecution`, `OnConvergenceFailure`. In das bestehende `AdvisorProfile`-Schema ergänzen. |
| **Advisor-Output-Form** | Freitext (kein Tool-Call). Wird dem Executor-Prompt als zusätzliche System-Message oder als gekennzeichneter Kontext-Block vorangestellt. |
| **Advisor-Output-Persistierung** | Neue Tabelle `AdvisorConsultations` mit Spalten `Id, RunId, IterationNumber, AdvisorProfileName, Output, CreatedAt`. FK auf `Runs.Id`. |
| **System-Advisor-Profile** | Zwei als Code-Konstanten: `briefing-clarifier` (Mode: Strategic, Trigger: BeforeFirstExecution) und `devils-advocate` (Mode: Devil, Trigger: BeforeEveryExecution). Beide IsSystem. |
| **Klassik-Template-Verhalten** | Klassik-Template bleibt **ohne Advisor** (leere AdvisorProfileNames-Liste). Verhaltens-Regression-Schutz wie in PS-5. |
| **Pipeline-Integration** | `ProfileBasedAdvisor` als neue Pipeline-Step-Klasse. Wird vor Executor aufgerufen, basierend auf `AdvisorTrigger`. Implementation: entweder eigene SDK-Hook (falls verfügbar) oder Atelier-spezifischer Pipeline-Wrapper. |
| **Custom-Auto-Prefix** | Custom-Advisor-Profile bekommen `custom-`-Prefix wie ReviewerProfile/ExecutorProfile (gleiche Service-Logik wiederverwenden). |
| **UI-Verwaltung** | `/crew/profiles/advisors` (Liste + Editor). Editor nutzt erweiterte `ProfileEditorForm` mit zusätzlichen Feldern für `AdvisorMode` und `AdvisorTrigger`. |
| **CrewTemplate-Editor-Erweiterung** | Neuer Picker-Block "Advisors" zwischen Executor und Reviewers, analog zu Reviewer-Picker (Up/Down-Reordering). |
| **CrewSummary-Erweiterung** | Advisor-Liste als zusätzliche Sektion im expanded-State. Display: "Advisors: <name1>, <name2>". |
| **RunDetail-Erweiterung** | Advisor-Consultations werden pro Iteration angezeigt — eine kleine Sektion "Advisors consulted before this iteration" mit Profile-Name + Output-Snippet (Click-to-Expand für vollen Text). |
| **MCP-Tool-Erweiterungen** | `list_advisor_profiles` als neues Tool. `submit_request` accepts `custom_crew.advisor_profile_names`. |
| **Migration** | `Step11AdvisorSystem` (oder nächste freie Nummer). Tabellen: `AdvisorProfiles`, `AdvisorConsultations`. `CrewTemplate.AdvisorProfileNames` ist schon JSONB aus PS-5, keine Schema-Änderung dort. |
| **Hadwiger-Nelson-Regression-Test** | Run mit Klassik-Template darf **kein** Advisor aufrufen. AdvisorConsultations-Tabelle bleibt leer für diesen Run. |
| **Real-Custom-Crew-Test (AC analog zu PS-5/PS-6)** | Pflicht: Real-Test mit briefing-clarifier-Advisor in einer Custom-Crew. Verifikation, dass Advisor-Output den nachfolgenden Executor-Pass tatsächlich beeinflusst. |

## Konkrete Anforderungen

### 1. Domain-Layer-Erweiterung (Core)

`src/Geef.Atelier.Core/Domain/Crew/Profiles/AdvisorProfile.cs` ist PS-5-Stub. Erweitere/aktualisiere:

```csharp
public sealed record AdvisorProfile(
    string Name,
    string DisplayName,
    string Description,
    string SystemPrompt,
    string Provider,
    string Model,
    int? MaxTokens,
    AdvisorMode Mode,
    AdvisorTrigger Trigger,
    bool IsSystem
);

public enum AdvisorMode
{
    Strategic,      // strategische Beratung vor Executor
    Critical,       // kritische Analyse (devil's advocate light)
    Devil,          // explizite Kontra-Position
    DomainExpert    // Domänen-Wissen einbringen
}

public enum AdvisorTrigger
{
    BeforeFirstExecution,    // nur vor Iteration 1
    BeforeEveryExecution,    // vor jeder Iteration
    OnConvergenceFailure     // nur wenn Pipeline ohne Konvergenz scheitert (Retry-Hint)
}
```

`CrewSnapshot.Advisors` ist schon als Liste vorgesehen — befülle sie im `CrewSnapshotBuilder`.

### 2. System-Advisor-Profile

In `src/Geef.Atelier.Core/Domain/Crew/SystemCrew.cs` ergänzen:

**`BriefingClarifierProfile`:**
- Name: `briefing-clarifier`
- DisplayName: `Briefing Clarifier`
- Description: `Strategic consultant. Analyzes briefings for unclear constraints, missing context, or unrealistic scope before the Executor begins.`
- Provider: `openrouter`
- Model: `google/gemini-2.5-flash` (günstig, schnell)
- Mode: `Strategic`
- Trigger: `BeforeFirstExecution`
- SystemPrompt: kurzer, fokussierter Prompt der den Advisor instruiert, max 3-5 strategische Beobachtungen zu liefern, nicht den Executor-Job zu machen

**`DevilsAdvocateProfile`:**
- Name: `devils-advocate`
- DisplayName: `Devil's Advocate`
- Description: `Adversarial perspective. After each iteration, challenges the strongest claims of the artifact to surface weak assumptions.`
- Provider: `openrouter`
- Model: `openai/gpt-4o-mini`
- Mode: `Devil`
- Trigger: `BeforeEveryExecution`
- SystemPrompt: instruiert den Advisor, in 2-4 Sätzen die schwächsten Annahmen zu identifizieren, nicht ad-hominem zu werden, konstruktiv zu bleiben

System-Advisor sind read-only (analog zu System-Reviewer/Executor).

### 3. Persistence-Layer

**DB-Migration `Step11AdvisorSystem`:**

```sql
CREATE TABLE "AdvisorProfiles" (
    "Name" text PRIMARY KEY,
    "DisplayName" text NOT NULL,
    "Description" text NOT NULL,
    "SystemPrompt" text NOT NULL,
    "Provider" text NOT NULL,
    "Model" text NOT NULL,
    "MaxTokens" integer NULL,
    "Mode" text NOT NULL,
    "Trigger" text NOT NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT now(),
    "UpdatedAt" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE "AdvisorConsultations" (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "RunId" uuid NOT NULL REFERENCES "Runs"("Id") ON DELETE CASCADE,
    "IterationNumber" integer NOT NULL,
    "AdvisorProfileName" text NOT NULL,
    "Output" text NOT NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX "IX_AdvisorConsultations_RunId" ON "AdvisorConsultations"("RunId");
```

Kein UPDATE-Statement für historische Daten nötig — alte Runs haben einfach keine AdvisorConsultations.

**Repository-Interface in Core:**
- `IAdvisorProfileRepository` analog zu `IReviewerProfileRepository`
- `IAdvisorConsultationRepository` für Consultation-Persistierung (CreateAsync + GetByRunIdAsync)

Implementations in Infrastructure als `internal sealed`.

### 4. Application-Layer-Erweiterung

**`ICrewService` ergänzen:**
- `ListAdvisorProfilesAsync(ct)` (System + Custom merged)
- `GetAdvisorProfileAsync(name, ct)`
- `CreateCustomAdvisorProfileAsync(...)` (mit Auto-Prefix)
- `UpdateCustomAdvisorProfileAsync(...)` (wirft bei System-Profile)
- `DeleteCustomAdvisorProfileAsync(...)` (wirft bei System-Profile)

**`CrewSnapshotBuilder` erweitern** — der Builder dereferenziert jetzt auch Advisor-Profile-Namen zu vollen Advisor-Records im Snapshot.

**`IRunService.SubmitRunAsync` Änderung:** keine API-Signatur-Änderung — Advisor-Konfiguration kommt über das Template oder Custom-Crew (das hat schon AdvisorProfileNames-Liste).

### 5. Pipeline-Layer

**`ProfileBasedAdvisor`** als neue Pipeline-Step-Klasse in `src/Geef.Atelier.Infrastructure/Pipeline/`:

```csharp
internal sealed class ProfileBasedAdvisor
{
    public ProfileBasedAdvisor(AdvisorProfile profile, ILlmClientResolver resolver,
                                IAdvisorConsultationRepository consultations, ...)
    {
        // ...
    }

    public async Task<AdvisorConsultation> ConsultAsync(
        Guid runId,
        int iterationNumber,
        string briefingText,
        IReadOnlyList<Finding>? previousFindings,
        CancellationToken ct)
    {
        // 1. LlmClient via resolver.ForProfile(profile.Provider, profile.Model, profile.MaxTokens)
        // 2. Build prompt: profile.SystemPrompt + briefing + (optional previous findings)
        // 3. Call LlmClient.CompleteAsync (no tool-use, plain text response)
        // 4. Sanitize and persist via consultations.CreateAsync
        // 5. Return AdvisorConsultation record
    }
}
```

**`AtelierPipelineFactory` umbauen:**
- Nimmt jetzt zusätzlich `IAdvisorConsultationRepository` und `IList<AdvisorProfile>` (aus dem CrewSnapshot)
- Vor jedem Executor-Pass: filter Advisors nach `AdvisorTrigger`, ruft sie sequenziell auf, sammelt Outputs
- Outputs werden dem Executor-Prompt vorangestellt als gekennzeichneter Kontext-Block

**Executor-Prompt-Erweiterung:**

Wenn Advisor-Outputs vorliegen:

```
[Advisor consultations for this iteration]

## briefing-clarifier (Strategic)
{output}

## devils-advocate (Devil)
{output}

[End of advisor consultations]

[existing executor prompt continues...]
```

Architect entscheidet exakte Form (System-Message vs. User-Message vs. einer Mischung).

**Falls SDK keinen Pre-Executor-Hook bietet:** Atelier-spezifischer Pipeline-Wrapper, der den `IExecutor` dekoriert — vor dem Executor-Call ruft er Advisor-Pässe auf. Architect prüft in Phase 1.2.

### 6. MCP-Layer

**Neues Tool `list_advisor_profiles`** analog zu `list_reviewer_profiles` aus PS-5.

**`submit_request`-Tool:** `custom_crew.advisor_profile_names` wird schon akzeptiert (Schema von PS-5). Keine Schema-Änderung nötig.

### 7. UI-Layer

**Neue Pages unter `/crew/profiles/advisors/`:**

- `AdvisorProfilesIndex.razor` — Liste analog zu Reviewer/Executor
- `AdvisorProfileEditor.razor` — Editor, **nutzt `ProfileEditorForm` mit zwei neuen Feldern**: `Mode` (Radio-Group mit Beschreibungen pro AdvisorMode), `Trigger` (Radio-Group)

**`ProfileEditorForm` erweitern** — neue optionale Parameter für Advisor-spezifische Felder:
- `ShowAdvisorFields: bool` (default false)
- Wenn true: rendert die zusätzlichen `Mode`- und `Trigger`-Eingaben

**`CrewTemplateEditor` erweitern** — neuer Picker-Block "Advisors" zwischen Executor und Reviewers, analog zum bestehenden ReviewerPicker. Up/Down-Reordering, gleiche UX.

**`CrewSummary` erweitern** — wenn `snapshot.Advisors` nicht leer:
```
[expanded state shows:]
Executor: ...
Reviewers: ...
Advisors: briefing-clarifier (Strategic, Before First) · devils-advocate (Devil, Before Every)
Strategy: ...
```

**`RunDetail.razor` erweitern** — pro Iteration eine kleine Sektion "Advisor consultations" über den Findings:
- Liste der Advisor-Profile-Namen mit kleinem Mode-Icon/Badge
- Click-to-Expand zeigt den vollständigen Advisor-Output
- Default-State: collapsed mit einer "X advisor consulted" Zeile

**`NavMenu`:** `/crew` ist schon Top-Level-Link aus PS-6 — keine Änderung. Innerhalb `/crew` führt der Index-Page-Link "Profiles → Advisors" zu den neuen Pages.

**`ReviewerDisplay`-Helper erweitern** um `GetAdvisorDisplay`, `GetAdvisorModeDisplay`, `GetAdvisorTriggerDisplay`.

### 8. Tests

**Domain-Tests:**
- `AdvisorProfileEqualityTests`
- `SystemCrewAdvisorConstantsTests` — Briefing-Clarifier + Devils-Advocate korrekt definiert

**Repository-Tests:**
- `AdvisorProfileRepositoryTests` — analog zu Reviewer
- `AdvisorConsultationRepositoryTests` — CreateAsync + GetByRunIdAsync

**Application-Tests:**
- `CrewServiceAdvisorCrudTests`
- `CrewSnapshotBuilderAdvisorTests` — Snapshot enthält dereferenzierte Advisor-Profile

**Pipeline-Tests:**
- `ProfileBasedAdvisorTests` — Advisor ruft LLM mit korrektem Prompt, persistiert Consultation
- `AtelierPipelineFactoryWithAdvisorsTests` — alle drei AdvisorTrigger-Modi getestet
- `HadwigerNelsonReplayWithKlassikTests` — kein Advisor aufgerufen, AdvisorConsultations-Tabelle bleibt leer für Klassik-Run

**MCP-Tests:**
- `ListAdvisorProfilesToolTests`

**UI-Tests (bUnit):**
- `AdvisorProfilesIndexTests`
- `AdvisorProfileEditorTests` (mit Mode/Trigger-Felder)
- `CrewTemplateEditorWithAdvisorPickerTests`
- `CrewSummaryWithAdvisorsTests`
- `RunDetailAdvisorConsultationsTests`

**Bestehende 192 Tests müssen grün bleiben.**

### 9. Real-Pipeline-Verifikation (Pflicht-AC)

**Auf `https://geef.stefan-bechtel.de/`:**

1. `/crew/profiles/advisors` öffnen → `briefing-clarifier` und `devils-advocate` als System-Advisor sichtbar
2. `/crew/templates/new` → Neues Custom-Template "klassik-mit-clarifier" mit Klassik-Crew + briefing-clarifier-Advisor
3. `/new` → Briefing mit der Custom-Crew submitten
4. Run beobachten → RunDetail zeigt Advisor-Consultation vor Iteration 1
5. Vergleichen: gleiches Briefing mit Klassik vs. mit klassik-mit-clarifier — beeinflusst der Advisor-Output den Executor?
6. Dokumentieren im Bericht: konkret was der Advisor sagte, was der Executor anders machte

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` 0 Errors, 0 Warnings.
2. `dotnet test` — alle bestehenden Tests grün (192 nach M1 oder mehr nach Bug-Fix-Merge) + neue Advisor-Tests.
3. **DB-Migration `Step11AdvisorSystem`** läuft sauber. AdvisorProfiles + AdvisorConsultations-Tabellen vorhanden.
4. **System-Advisor verfügbar** — `briefing-clarifier` und `devils-advocate` über `ICrewService.ListAdvisorProfilesAsync()` abrufbar.
5. **Custom-Advisor via UI anlegbar** — Auto-Prefix funktioniert, System-Advisor read-only.
6. **CrewTemplate kann Advisor referenzieren** — Editor zeigt Advisor-Picker, Snapshot enthält dereferenzierte Advisor-Profile.
7. **Pipeline ruft Advisor basierend auf Trigger** — alle drei Trigger-Modi getestet (mind. Mock-Level).
8. **AdvisorConsultations werden persistiert** — pro Iteration eine Zeile pro aufgerufenem Advisor.
9. **RunDetail zeigt Advisor-Consultations** — Click-to-Expand für vollen Output.
10. **Hadwiger-Nelson-Replay mit Klassik** — keine AdvisorConsultations für Klassik-Run (Verhaltens-Regression-Schutz).
11. **Real-Test mit Custom-Crew + briefing-clarifier** durchgeführt und im Bericht dokumentiert (Lehre aus PS-5/PS-6).
12. **MCP-Tool `list_advisor_profiles`** funktional.
13. **Decisions-Log-Eintrag** (D-031 oder nächste freie Nummer) mit Architect-Entscheidungen.

## Was du in diesem Schritt NICHT tust

- **Keine Eskalationsleitern** — rekursive Advisor-Calls (Advisor ruft selbst Advisor) sind explorativ, nicht in PS-7.
- **Keine sokratischen Advisor** — Frage-Antwort-Pattern aus dem PDF, nicht in PS-7.
- **Keine temporalen Advisor** — Zukunfts-Perspektive, nicht in PS-7.
- **Kein Multi-Advisor-Voting** — Advisor-Outputs werden alle dem Executor übergeben, keine Aggregation.
- **Kein Advisor-Cost-Tracking** — separater Step.
- **Keine Auto-Failover** wenn Advisor-Call scheitert — schlägt der Advisor fehl, scheitert der Run (oder wird im Bug-Fix-Step adressiert).
- **Keine LLM-Provider-Änderungen** — `LlmOptions`, `ILlmClientResolver` bleiben unverändert.
- **Keine Reviewer-Logic-Änderungen** — PS-2 Severity-Kalibrierung bleibt.

## Architect-Konsultation (Phase 1.4) — vier echte Knackpunkte

1. **SDK-Hook-Verfügbarkeit:** Bietet das Geef-SDK einen Pre-Executor-Hook oder einen `IAdvisor`-Vertrag? Wenn ja: nutzen. Wenn nein: Atelier baut einen Pipeline-Wrapper, der vor dem Executor-Call die Advisor-Pässe auslöst. Recherche-Ergebnisse im Plan dokumentieren.

2. **Advisor-Output-Übergabe an Executor:** System-Message vs. zusätzliche User-Message vs. Kontext-Block im bestehenden Prompt. Empfehlung: **gekennzeichneter Kontext-Block im bestehenden System-Prompt** mit klarer "Advisor consultations"-Header — vermeidet Konversations-Geschichte-Probleme bei multi-Iteration-Runs. Architect bestätigt nach SDK-Realfakten-Prüfung.

3. **Advisor-Failure-Verhalten:** Was wenn ein Advisor-Call HTTP-Error wirft (insbesondere vor dem Bug-Fix-Merge)? Empfehlung: **Run schlägt fehl**, weil Advisor-Konsultation explizit konfiguriert wurde — sie zu ignorieren wäre eine stille Verfälschung. Mit dem Bug-Fix-Merge bekommt diese Failure auch korrekt `Failed`-Status (siehe Parallel-Hinweis).

4. **Advisor-Reihenfolge:** Falls mehrere Advisor mit gleichem Trigger laufen, ist die Reihenfolge in `CrewSnapshot.Advisors` signifikant. Empfehlung: **ja, signifikant** — analog zu Reviewer-Reihenfolge in PS-5. Im UI durch den Picker bewahrt (Up/Down-Buttons).

`geef_architecture.md` prüft Konsistenz mit erweitertem Schichtenbild.

## Reviewer-Hinweise

- **R1 (Functional Correctness):** Alle 13 ACs prüfen. Besonders 10 (Klassik-Regression), 11 (Real-Test), 6 (Snapshot-Befüllung).
- **R2 (Code Quality):** `ProfileBasedAdvisor` analog strukturiert zu `ProfileBasedReviewer`. Service-Layer-Logik wiederverwendet für Custom-Prefix.
- **R3 (Test Execution):** Alle Tests grün. Mock-LLM-Tests für die drei Trigger-Modi.
- **R4 (Architecture Compliance):** Layer-Trennung weiter sauber. Pipeline-Wrapper-Strategie im Bericht dokumentiert falls eigene Implementation nötig.
- **R5 (Live UI):** Screenshot der neuen Pages in allen drei Themes. RunDetail mit Advisor-Consultations im Real-Test.

## Persistenter Abschlussbericht

Bericht nach `docs/reports/post-skeleton-07-advisor-passes-report.md`. Inhalt:

1. **Was wurde umgesetzt** — Schicht für Schicht.
2. **SDK-Recherche-Ergebnisse** — was unterstützt das SDK, was haben wir selbst gebaut.
3. **Architect-Output** — die vier Knackpunkte plus implizite Entscheidungen aus den Klärungs-Antworten.
4. **Pre-Mortem & Devil's Advocate** — Authority-Bias-Risiko (Executor folgt Advisor blind?), Advisor-Output-Längen-Probleme, Migration-Edge-Cases.
5. **Reviewer-Iterationen** — Tabelle.
6. **Akzeptanzkriterien-Check** — Tabelle mit allen 13 ACs.
7. **Real-Test-Ergebnis** — konkret was der Advisor sagte, wie der Executor reagierte. Mit/ohne-Advisor-Vergleich.
8. **Beobachtungen** — wie aufwändig waren die Pipeline-Umbauten? Welche SDK-Stellen waren überraschend.
9. **Empfehlungen für nächste Steps** — Authority-Bias-Mitigation? Advisor-Cost-Tracking? Multi-Advisor-Voting als Vision-Erweiterung?
10. **Parallel-Step-Koordination** — wie wurde der Merge mit dem Bug-Fix-Step koordiniert?

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- **Niemals Secrets** in source control, Logs oder Bericht.
- Custom-Advisor-Namen im Bericht **anonymisieren**, falls sensitive Domain-Bezüge.
- UI-Strings: **Englisch** (konsistent mit PS-3/PS-6).

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension). Erwarteter Aufwand: 2-3 Arbeitstage.

---

**Nach erfolgreichem Abschluss:** Atelier hat die vollständige Vision-Crew: Executor, Reviewer, Advisor. Strategische Beratung vor dem Schreiben, kritische Prüfung danach. Das Vision-Ziel aus D-001 ist erreicht. Folge-Steps: Domänen-spezifische Templates ("Juristisch", "Akademisch"), Cost-Tracking, Welcome-Stats, oder explorative Advisor-Varianten aus dem PDF (Sokratisch, Temporal, Eskalationsleitern).