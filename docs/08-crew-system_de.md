# 08 — Crew-System (PS-5)

*[English](08-crew-system.md) · **Deutsch***

Letzte Aktualisierung: 2026-05-18 (Template-Studio-Abschnitt + Routing-Map-Eintrag für D-043 ergänzt)

## Überblick

Das Crew-System ersetzt die in PS-2 hartkodierte Dreier-Crew (Executor + BriefingTreueReviewer + KlarheitReviewer) durch ein konfigurierbares Profil- und Template-System. Jeder Run erhält beim Einreichen einen vollständig eingebetteten **CrewSnapshot**, der die Reproduzierbarkeit des Runs auch dann garantiert, wenn Profile später geändert oder gelöscht werden.

## Kernbegriffe

| Begriff | Bedeutung |
|---|---|
| **ExecutorProfile** | LLM-Akteur der den Draft erstellt. Trägt System-Prompt, Provider, Modell, MaxTokens. |
| **ReviewerProfile** | LLM-Akteur der den Draft bewertet. Gleiche Felder. `Priority` für sequenzielle Strategien via `IReviewer.Priority`. |
| **CrewTemplate** | Komponiert Executor + Reviewers + EvaluationStrategy + optionalen ConvergenceOverride + Advisor-Profile. |
| **CrewSnapshot** | Vollständig eingebettete Kopie des CrewTemplates (inkl. aller Profil-Daten) zum Zeitpunkt der Run-Einreichung. Persistiert als JSONB auf `Runs.CrewSnapshot`. |
| **AdvisorProfile** | LLM-Akteur für konsultative Pässe vor oder nach der Execution. Trägt `AdvisorMode` + `AdvisorTrigger`. Funktional ab PS-7. |

## EvaluationStrategies

| Enum-Wert | SDK-Klasse | Verhalten |
|---|---|---|
| `Parallel` | `ParallelEvaluationStrategy` | Alle Reviewer parallel, alle Findings gesammelt. Standard. |
| `Sequential` | `SequentialEvaluationStrategy` | Reviewer nacheinander in Listen-Reihenfolge, alle abwarten. |
| `FailFast` | `FailFastEvaluationStrategy` | Wie Sequential, Abbruch nach erstem Critical-Finding. |
| `Priority` | `PriorityOrderedEvaluationStrategy` | Reviewer in `Priority`-Reihenfolge (nicht Listenreihenfolge). |

**Hinweis:** Bei `Parallel` ist die Reihenfolge in `ReviewerProfileNames` nur dokumentatorisch. Bei `Sequential` und `Priority` ist sie signifikant.

## System-Profile (Code-Konstanten)

Definiert in `Geef.Atelier.Core.Domain.Crew.SystemCrew` (read-only, versioniert mit dem Code):

Provider/Modelle Stand Mai 2026 (nach der Umstellung auf die Subscription-CLIs,
D-027/D-032): Executor und Anthropic-Reviewer laufen über `claude-cli`, die übrigen
Reviewer über `codex-cli`. Modell-Pluralismus bleibt gewahrt (Reviewer ≠ Executor-Modell).

| Name | Typ | Provider / Modell |
|---|---|---|
| `default-executor` | ExecutorProfile | `claude-cli` / `anthropic/claude-opus-4.7` |
| `briefing-fidelity` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `clarity` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `legal-jargon-precision` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `legal-clause-risk` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `academic-citation-readiness` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `academic-argumentation-rigor` | ReviewerProfile | `claude-cli` / `anthropic/claude-opus-4.7` |
| `marketing-audience-clarity` | ReviewerProfile | `codex-cli` / `gpt-5.5` |
| `marketing-conversion-strength` | ReviewerProfile | `codex-cli` / `gpt-5.5` |

**System-Templates** (vier): `klassik` (Evaluation `Parallel`, keine Advisors —
reproduziert das ursprüngliche PS-2-Verhalten) sowie die Domain-Templates
`juristisch` (`Sequential`, Advisor `legal-domain-expert`),
`akademisch` (`Sequential`, Advisor `academic-rigor-advisor`) und
`marketing` (`Parallel`, keine Advisors).

## Custom-Profile

- Werden in der DB (`ReviewerProfiles`, `ExecutorProfiles`, `CrewTemplates`) gespeichert.
- Name erhält automatisch den Prefix `"custom-"` (idempotent, kein Doppelpräfix).
- System-Profile sind read-only: Update/Delete wirft `InvalidOperationException("System profile is read-only — copy it as a custom variant.")`.
- API: `ICrewService.CreateCustomReviewerProfileAsync(profile)`.

## CrewSnapshot-Format (SchemaVersion 1)

> Das folgende Beispiel zeigt die **Struktur**. Die `provider`/`model`-Werte sind
> illustrativ — die aktuell gültigen System-Werte stehen in der Tabelle
> „System-Profile" oben; ein realer Snapshot enthält die zum Submit-Zeitpunkt
> gültigen Werte.

```json
{
  "schemaVersion": 1,
  "templateName": "klassik",
  "executor": {
    "name": "default-executor",
    "displayName": "Default Executor",
    "systemPrompt": "...",
    "provider": "openrouter",
    "model": "anthropic/claude-opus-4.7",
    "maxTokens": null,
    "isSystem": true
  },
  "reviewers": [
    { "name": "briefing-fidelity", "provider": "openrouter", "model": "google/gemini-2.5-flash", ... },
    { "name": "clarity",           "provider": "openrouter", "model": "openai/gpt-5.5-mini",    ... }
  ],
  "evaluationStrategy": "Parallel",
  "convergenceOverride": null,
  "advisors": []
}
```

Serialisiert mit `JsonNamingPolicy.CamelCase`. Gespeichert auf `Runs.CrewSnapshot` (JSONB).

## Advisor-Pässe (PS-7)

Advisors sind konsultative LLM-Akteure, die zu definierten Zeitpunkten in der Pipeline ausgeführt werden. Ihr Output fließt als gekennzeichneter Kontext-Block in den Run — der Executor und nachfolgende Reviewer sehen ihn, ohne dass das Geef-SDK-Kern modifiziert werden muss.

### AdvisorProfile-Schema

```csharp
public sealed record AdvisorProfile(
    string Name, string DisplayName, string Description,
    string SystemPrompt, string Provider, string Model, int? MaxTokens,
    AdvisorMode Mode, AdvisorTrigger Trigger, bool IsSystem);

public enum AdvisorMode    { Strategic, Critical, DevilsAdvocate, DomainExpert }
public enum AdvisorTrigger { BeforeFirstExecution, BeforeEveryExecution, OnConvergenceFailure }
```

### Trigger-Typen

| Trigger | Bedeutung |
|---|---|
| `BeforeFirstExecution` | Advisor wird einmalig vor Iteration 1 konsultiert. Geeignet für strategische Briefing-Analyse. |
| `BeforeEveryExecution` | Advisor wird vor jeder Iteration konsultiert. Geeignet für kritische Gegenstimmen. |
| `OnConvergenceFailure` | Advisor wird nur bei Convergence-Failure konsultiert; danach folgt ein einmaliger Retry-Durchlauf. |

### System-Advisors

Provider/Modell Stand Mai 2026: alle System-Advisors laufen über
`claude-cli` / `anthropic/claude-opus-4.7`.

| Name | Mode | Trigger | Zweck |
|---|---|---|---|
| `briefing-clarifier` | Strategic | BeforeFirstExecution | Analysiert das Briefing vor dem ersten Executor-Pass und liefert strukturierte Klärungshinweise. |
| `devils-advocate` | DevilsAdvocate | BeforeEveryExecution | Hinterfragt vor jeder Iteration die geplante Executor-Richtung kritisch, um Fehler durch blinden Fortschritt zu vermeiden. |
| `legal-domain-expert` | DomainExpert | BeforeFirstExecution | Domänen-Input für juristische Texte (Template `juristisch`). |
| `academic-rigor-advisor` | Critical | BeforeEveryExecution | Wissenschaftliche Strenge/Argumentationsqualität (Template `akademisch`). |

### Pipeline-Integration via Decorator

Der `AdvisorAwareExecutor` (in `Infrastructure/Pipeline/`) dekoriert `IExecutionStep` und schiebt sich transparent vor jeden Executor-Aufruf:

```
AdvisorAwareExecutor.ExecuteAsync(context)
  1. Filtert Advisors nach aktivem Trigger (BeforeFirst nur bei Iteration 1, BeforeEvery immer)
  2. Ruft ProfileBasedAdvisor für jeden passenden Advisor sequenziell auf
  3. Schreibt Output als "[ADVISOR: <name>]\n<text>" in context[AtelierContextKeys.AdvisorBlock]
  4. Persistiert AdvisorConsultation-Record (Tabelle AdvisorConsultations)
  5. Delegiert an den echten IExecutionStep
```

`AtelierPipelineFactory.BuildWithAdvisorContext(snapshot, context)` wired den Decorator und stellt sicher, dass der Advisor-Block im `IRunContext` propagiert wird.

### Advisor-Failure-Verhalten

Advisor-LLM-Calls sind nicht best-effort. Eine Exception in `ProfileBasedAdvisor` bubbled durch `AdvisorAwareExecutor` und bricht den Run mit `Status=Failed` ab (D-031(c)). Stiller Weiterlauf würde einen möglicherweise korrumpierten Kontext maskieren.

### Convergence-Failure-Retry-Mechanismus

```
Pipeline → ConvergenceFailedException
  → RunOrchestratorService.TryConvergenceFailureRetryAsync
      1. Prüft RunEntity.AdvisorRetryAttempted — true → eskaliert zu Failed (kein zweiter Retry)
      2. Setzt AdvisorRetryAttempted = true in DB
      3. Aktiviert OnConvergenceFailure-Advisors im nächsten Run-Kontext
      4. Startet Pipeline-Durchlauf erneut (einmalig)
      5. Zweites ConvergenceFailedException → Failed (kein weiterer Retry)
```

**Single-Retry-Cap:** `RunEntity.AdvisorRetryAttempted` (Migration Step11) verhindert Endlos-Schleifen. Multi-Retry mit konfigurierbarer Wiederholungsanzahl ist als Future Work dokumentiert.

### DB-Tabellen (Migration Step11AdvisorSystem)

| Tabelle | Inhalt |
|---|---|
| `AdvisorProfiles` | Custom Advisor-Profile (System-Advisors leben als Code-Konstanten in `SystemCrew`). |
| `AdvisorConsultations` | Persistierte Advisor-Outputs pro Iteration und Advisor (RunId, IterationNumber, AdvisorName, OutputText, CreatedAt). |

Spalte `RunEntity.AdvisorRetryAttempted` (bool, nullable) auf `Runs`-Tabelle.

### UI-Komponenten (PS-7)

| Komponente | Zweck |
|---|---|
| `AdvisorPicker` | Available/Selected-Liste analog `ReviewerPicker`, mit Trigger-Anzeige |
| `AdvisorConsultationsBlock` | Klappsection auf RunDetail-Page: zeigt alle Consultations pro Iteration |
| `AdvisorProfilesIndex` | Liste aller Advisor-Profile (System + Custom) unter `/crew/profiles/advisors` |
| `AdvisorProfileEditor` | CRUD-Editor für Custom Advisor-Profile |

`ProfileEditorForm` wurde um `ShowAdvisorFields` + Mode/Trigger Radio-Groups erweitert (wiederverwendbar für Reviewer, Executor und Advisor).

### MCP-Tool

`list_advisor_profiles` — listet alle Advisor-Profile (System + Custom).

## API-Pfade

### Template-basierter Submit (Standard)

```csharp
await runService.SubmitRunAsync(
    briefingText: "...",
    configJson:   "{}",
    crewTemplateName: "klassik");  // null → Standard "klassik"
```

### Custom-Crew-Submit

```csharp
var spec = new CrewSpec(
    ExecutorProfileName:  "custom-my-executor",
    ReviewerProfileNames: ["briefing-fidelity", "custom-my-reviewer"],
    EvaluationStrategy:   EvaluationStrategy.Sequential,
    ConvergenceOverride:  new ConvergencePolicyOverride(MaxIterations: 3, null, null, null));

await runService.SubmitRunAsync("...", "{}", customCrew: spec);
```

### MCP-Tools

- `list_crew_templates` — listet alle Templates (System + Custom).
- `list_reviewer_profiles` — listet alle Reviewer-Profile (System + Custom).
- `list_advisor_profiles` — listet alle Advisor-Profile (System + Custom).
- `list_grounding_provider_profiles` — listet alle Grounding-Provider-Profile.
- `submit_request` — erweitert um `crew_template` und `custom_crew` (JSON-String).

Vollständige Tool-Liste (13 Tools): siehe [09-endpoint-reference.md](09-endpoint-reference_de.md) und die [Projekt-README](../README_de.md).

## Reviewer-Name-Migration

| Alt (pre-PS-5) | Neu (PS-5) |
|---|---|
| `BriefingTreueReviewer` | `briefing-fidelity` |
| `KlarheitReviewer` | `clarity` |

Migration Step10 benennt historische `Findings.ReviewerName`-Werte um. `ReviewerDisplay.ToDisplay()` enthält beide Varianten als Fallback.

## Systemtrennung (Namespace)

- `Core/Domain/Crew/` — alle Domain-Records (keine Infrastruktur-Abhängigkeit).
- `Core/Domain/Crew/SystemPrompts.cs` — System-Prompt-Texte (lang, gehören semantisch zu System-Profilen).
- `Infrastructure/Pipeline/ProfileBasedReviewer.cs` / `ProfileBasedExecutor.cs` — Geef-SDK-Adapter.
- `Application/Crew/CrewService.cs` + `CrewSnapshotBuilder.cs` — orchestriert Repo-Lookups + Snapshot-Konstruktion.

## PS-6 — UI-Pfade und Konventionen

### Routing-Map

| URL | Komponente | Beschreibung |
|---|---|---|
| `/crew` | `CrewIndex` | Landing-Page mit Überblick über Templates + Profile |
| `/crew/templates` | `CrewTemplatesIndex` | Liste aller Templates (System + Custom) |
| `/crew/templates/new` | `CrewTemplateEditor` | Neues Template anlegen |
| `/crew/templates/{name}` | `CrewTemplateEditor` | Template bearbeiten / System-Template duplizieren |
| `/crew/profiles/reviewers` | `ReviewerProfilesIndex` | Liste aller Reviewer-Profile |
| `/crew/profiles/reviewers/new` | `ReviewerProfileEditor` | Neues Reviewer-Profil anlegen |
| `/crew/profiles/reviewers/{name}` | `ReviewerProfileEditor` | Reviewer-Profil bearbeiten |
| `/crew/profiles/executors` | `ExecutorProfilesIndex` | Liste aller Executor-Profile |
| `/crew/profiles/executors/new` | `ExecutorProfileEditor` | Neues Executor-Profil anlegen |
| `/crew/profiles/executors/{name}` | `ExecutorProfileEditor` | Executor-Profil bearbeiten |
| `/crew/studio` | `TemplateStudio` | KI-gestützter Template-Wizard (Analyse → Review → Edit → Materialisierung) |

### UI-Komponenten

| Komponente | Ort | Zweck |
|---|---|---|
| `CrewBadge` | `Components/UI/` | Dezenter Text-Badge mit Template-Namen in RunRow |
| `CrewSelector` | `Components/UI/` | Dropdown zur Template-Auswahl auf der NewRun-Page |
| `CrewSummary` | `Components/UI/` | Click-to-Expand Crew-Übersicht auf RunDetail-Page |
| `ReviewerPicker` | `Components/UI/` | Available/Selected-Liste mit Up/Down-Reordering |
| `ProfileEditorForm` | `Components/UI/` | Generisches Form für Reviewer- und Executor-Profile |
| `Modal` | `Components/UI/` | Generische Modal-Komponente mit Backdrop |
| `DeleteConfirmationModal` | `Components/UI/` | Bestätigungs-Modal: User muss Namen tippen |

### Name-Constraints

Pattern `^[a-z0-9\-]+$`, max 64 Zeichen — gilt für alle Profile- und Template-Namen (Custom-Prefix exkl.). Form-Validierung via `DataAnnotations.RegularExpression`. Service-Layer ist idempotent bzgl. `custom-`-Prefix.

## Template Studio (D-043)

Das Template Studio unter `/crew/studio` ist ein KI-gestützter Wizard, der für eine beschriebene Aufgabe eine vollständige Crew-Konfiguration vorschlägt. Der Nutzer kann jeden Vorschlag im Edit-Step prüfen und bearbeiten, bevor er in die DB materialisiert wird.

### Wizard-Schritte

| Schritt | Komponente | Beschreibung |
|---|---|---|
| TaskInput | `StudioTaskInputStep` | Freitext-Aufgabenbeschreibung; löst LLM-Analyse aus |
| Analyzing | `StudioAnalyzingStep` | Lade-Indikator während das Meta-LLM läuft |
| Review | `StudioReviewStep` | Zeigt den KI-Vorschlag; Option, ein bestehendes Template zu verwenden |
| Edit | `StudioEditStep` | Vollständiger Editor für das vorgeschlagene Template und alle Profile |
| Confirmation | `StudioConfirmationStep` | Zeigt Materialisierungs-Ergebnis; startet einen Run |

### StudioEditStep — Feld-Parität (D-043)

Der Edit-Step exponiert das vollständige Feld-Set für das Template und jeden Profil-Slot:

**Template-Felder:** DisplayName, Description, EvaluationStrategy (Dropdown), EvaluationStrategyReasoning (read-only, vom LLM)

**Pro Profil-Slot (Executor / Reviewer × N / Advisor × N / GroundingProvider × N):**
- **UseExisting / CreateNew Toggle** — bestehendes Profil per Name wählen oder neues Profil inline konfigurieren
- **CreateNew-Felder:** Name (kebab-case), DisplayName, Description, Provider, Modell (`ModelSelector`), MaxTokens, System-Prompt
- **Reviewer-spezifisch:** ReviewerFocus (optional)
- **Advisor-spezifisch:** AdvisorMode (Strategic / Critical / DevilsAdvocate), AdvisorTrigger (BeforeFirstExecution / BeforeEveryExecution / OnConvergenceFailure)
- **GroundingProvider-spezifisch:** GroundingProviderType (Tavily / VectorStore), Typ-spezifische Einstellungen (API-Key oder Collection-Name)
- **Reasoning-Anzeige:** LLM-Begründungen pro Feld, read-only (aus `analyze_template_proposal`)
- **Field-Helps:** Deutsche Inline-Hinweise für jedes Feld (`StudioFieldHelps.cs`)

### Schlüssel-Komponenten

| Komponente | Zweck |
|---|---|
| `StudioProfileSlot.razor` | UseExisting/CreateNew-Toggle + vollständiges Inline-Profil-Form; bettet `ModelSelector` ein |
| `FieldHelp.razor` | Inline-Hinweis unterhalb jedes Feldes |
| `StudioFieldHelps.cs` | Zentrale deutsche Field-Help-Text-Konstanten |

### Materialisierung (atomar, D-043/7)

`TemplateStudioService.MaterializeAsync` kapselt alle DB-Schreibvorgänge in einer einzelnen EF-Core-Transaktion (`IAtomicTransactionFactory`). Ablauf: Validierung → Begin → Profile anlegen (Executor, Reviewer, Advisor, GroundingProvider) → Template anlegen → Commit. Explizites Rollback bei jedem Fehler — kein Halb-materialisierter Zustand. `MarkMaterializedAsync` (markiert den Analyse-Datensatz als verbraucht) läuft innerhalb der Transaktion.
