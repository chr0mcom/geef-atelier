# Claude-Code-Prompt: Post-Skeleton Schritt 5 — Reviewer-Profile + Crew-Templates

*Dieser Prompt baut die Foundation für dynamische Crew-Composition. Reviewer- und Executor-Profile werden als wiederverwendbare Konfigurationen mit System-Defaults im Code + User-Custom-Profilen in der DB definiert. CrewTemplates komponieren diese zu Domänen-spezifischen Setups. UI dafür kommt erst in PS-6.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Bisher arbeitet die Pipeline mit einer **fest hartkodierten Crew**: Executor + `BriefingTreueReviewer` + `KlarheitReviewer`. Das war für den Walking-Skeleton sinnvoll, ist aber die Differenzierungs-Sperre gegenüber dem Vision-Ziel "Text-Manufaktur mit verschiedenen Crews für verschiedene Aufträge" (D-001).

Deine Aufgabe ist **PS-5**: das System auf dynamische Crew-Komposition umstellen. Reviewer und Executor werden zu **profilierten, wiederverwendbaren Bausteinen**. CrewTemplates komponieren diese Bausteine zu domänen-spezifischen Setups. System-Profile/Templates leben als Code-Konstanten im Repo (versioniert mit Atelier-Updates), User-Custom-Profile in der DB (frei editierbar).

Dies ist eine substantielle Backend-Erweiterung — Domain-, Persistence-, Application-, Pipeline- und MCP-Schichten sind betroffen. **Keine UI** in diesem Step. UI für Crew-Auswahl kommt in PS-6, Advisor-Integration in PS-7.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors. **Plan-Phase-Integration** als Architect-Form.

**Phase 1.2 ist recherche-lastig:** Welche `EvaluationStrategy`-Implementierungen bietet das Geef-SDK? Wie wird die `ConvergencePolicy` konstruiert? Welche Hook-Points hat die Pipeline-Factory? Bevor du baust, dokumentiere die SDK-Realfakten im Plan.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`CLAUDE.md`** im Repo-Root.
3. **`docs/01-vision-and-scope.md`** — der Vision-Kontext, besonders Modell-Pluralismus und Crew-Differenzierung.
4. **`docs/02-architecture.md`** — Schichtenbild, Pipeline-Mapping. Eine neue Crew-Sektion wird in PS-5 hinzukommen.
5. **`docs/05-decisions-log.md`**, alle Einträge bis D-027.
6. **`docs/Vom_Prompt_zur_Pipeline.pdf`** — der GEEF-Artikel. **Besonders Bild 3** mit den vier Reviewer-Ablauf-Strategien (Parallel, Sequenziell, FailFast, Prioritätsgesteuert). Diese vier sind die Auswahl-Optionen für die `EvaluationStrategy`-Konfiguration pro CrewTemplate.
7. **`docs/Vom_Berater_im_Hintergrund.pdf`** — der Advisor-Artikel. **Nicht für PS-5 umsetzen** (kommt PS-7), aber als Kontext lesen, damit das `AdvisorProfile`-Stub-Schema sinnvoll dimensioniert ist.
8. **`docs/reports/post-skeleton-04-cli-adapter-report.md`** — der aktuelle Zustand der `LlmOptions` und `ILlmClientResolver`-Schicht. PS-5 baut darauf auf.
9. **Aktueller Code:**
   - `src/Geef.Atelier.Core/Domain/` — wo die neuen Entities landen
   - `src/Geef.Atelier.Application/Runs/` — wo `IRunService` erweitert wird
   - `src/Geef.Atelier.Infrastructure/Pipeline/` — `AtelierPipelineFactory`, `LlmExecutionStep`, `LlmReviewer`
   - `src/Geef.Atelier.Infrastructure/Llm/LlmOptions.cs` — die Pro-Akteur-Konfiguration aus PS-4, wird **nicht** ersetzt aber überlagert von der neuen Crew-Schicht
   - `src/Geef.Atelier.Infrastructure/Persistence/AtelierDbContext.cs` und Migrations
   - `src/Geef.Atelier.Mcp/Tools/SubmitRequest.cs` und Geschwister
10. **Geef-SDK-Inspektion:** `EvaluationStrategy`-Implementierungen suchen (`ParallelEvaluationStrategy`, ggf. `SequentialEvaluationStrategy`, `FailFastEvaluationStrategy`, `PriorityEvaluationStrategy`). Falls eine Strategie fehlt — Phase 1.4 entscheidet ob Atelier sie selbst implementiert oder die Option weglässt. `ConvergencePolicy`-Hooks für Custom-Konfiguration.

## Verbindliche Entscheidungen aus der Klärung

Diese Entscheidungen sind im Brainstorming fixiert worden — Architect bestätigt sie im Plan, weicht nur mit klarem Grund ab:

| Entscheidung | Konkret |
|---|---|
| **Severity-Logic** | Bleibt im Prompt-Text der Reviewer (PS-2-Stand). Keine separate Schema-Property. |
| **Executor-Profile** | Ja, Executor wird genauso profiliert wie Reviewer (`ExecutorProfile`-Klasse, analog zu `ReviewerProfile`). |
| **Convergence-Policy pro Template** | Ja, jedes CrewTemplate kann ConvergencePolicy-Felder überschreiben (MaxIterations, AbortOnCritical, etc.). |
| **EvaluationStrategy pro Template** | Ja, konfigurierbar: `parallel` (Default), `sequential`, `fail-fast`, `priority` — gemappt auf SDK-Klassen. |
| **Reviewer-Reihenfolge** | Reihenfolge in der `ReviewerProfiles[]`-Liste eines Templates ist **signifikant** für sequenzielle und prioritätsgesteuerte Strategien. |
| **Default-Template** | Nur **`"klassik"`** als System-Template. Weitere Templates entstehen organisch aus Real-Use-Cases (PS-6+). |
| **Namespace-Trennung** | System-Profile sind read-only (im Code). User-Custom-Profile bekommen automatisch `custom-`-Prefix beim Anlegen, damit kein Name-Konflikt mit System-Profilen möglich ist. User kann System-Profile **kopieren-und-modifizieren** als Custom-Variante. |
| **RunEntity-Erweiterung** | `CrewTemplateName: string?` und `CrewSnapshot: string?` (JSON). Migration setzt historische Runs auf `"klassik"`. |
| **API-Erweiterung** | `IRunService.SubmitRunAsync` bekommt `crewTemplateName: string?` und `customCrew: CrewSpec?`. Beide null → Default `"klassik"`. |
| **MCP-Tool-Erweiterungen** | `submit_request` bekommt `crew_template` + `custom_crew`-Parameter. Neue Tools: `list_crew_templates`, `list_reviewer_profiles`. |
| **Pipeline-Klassen** | Eine generic `ProfileBasedReviewer` ersetzt hartkodierte Reviewer-Klassen. Analog `ProfileBasedExecutor` für den Executor. |
| **Reviewer-Namen-Migration** | `BriefingTreueReviewer` → `"briefing-fidelity"`, `KlarheitReviewer` → `"clarity"`. DB-Migration benennt historische `FindingEntity.ReviewerName`-Werte um. UI bekommt Display-Mapping (in einer späteren Step). |
| **AdvisorProfile-Stub** | Schema vorbereitet für PS-7, aber leer-default. CrewTemplates haben `AdvisorProfiles[]`-Liste, die in PS-5 typischerweise leer ist. |

## Konkrete Anforderungen

### 1. Domain-Layer (Core)

Neue Entities in `src/Geef.Atelier.Core/Domain/Crew/`:

**`ReviewerProfile`:**
```csharp
public sealed record ReviewerProfile(
    string Name,              // eindeutig, kebab-case, z.B. "briefing-fidelity"
    string DisplayName,       // z.B. "Briefing-Fidelity Reviewer"
    string Description,       // 1-2 Sätze: wofür der Reviewer da ist
    string SystemPrompt,      // der eigentliche Reviewer-Charakter
    string Provider,          // Referenz auf LlmOptions.Providers-Key
    string Model,             // z.B. "google/gemini-2.5-flash"
    int? MaxTokens,           // optional, sonst Default
    bool IsSystem             // true für Code-Defaults, false für DB-Custom
);
```

**`ExecutorProfile`:** Identische Felder wie `ReviewerProfile`. Der einzige Unterschied ist die semantische Rolle. Falls Architect eine gemeinsame Base-Klasse `LlmActorProfile` sinnvoller findet — okay.

**`CrewTemplate`:**
```csharp
public sealed record CrewTemplate(
    string Name,                                 // z.B. "klassik"
    string DisplayName,                          // z.B. "Klassik"
    string Description,
    string ExecutorProfileName,                  // Referenz
    IReadOnlyList<string> ReviewerProfileNames,  // Referenzen, Reihenfolge signifikant
    EvaluationStrategy EvaluationStrategy,       // parallel/sequential/fail-fast/priority
    ConvergencePolicyOverride? ConvergenceOverride,  // optional, sonst Default-Policy
    IReadOnlyList<string> AdvisorProfileNames,   // leer für PS-5, PS-7-vorbereitet
    bool IsSystem
);

public enum EvaluationStrategy { Parallel, Sequential, FailFast, Priority }

public sealed record ConvergencePolicyOverride(
    int? MaxIterations,
    bool? AbortOnCritical,
    bool? DetectRegression,
    int? StagnationThreshold
);
```

**`CrewSpec`** als DTO für API-Custom-Crew-Pfad:
```csharp
public sealed record CrewSpec(
    string ExecutorProfileName,
    IReadOnlyList<string> ReviewerProfileNames,
    EvaluationStrategy EvaluationStrategy,
    ConvergencePolicyOverride? ConvergenceOverride
);
```

**`AdvisorProfile`-Stub** in `Crew/Advisors/AdvisorProfile.cs`: minimal-Schema-Vorlage für PS-7 (Architect entscheidet ob jetzt schon konkrete Felder oder reines Marker-Stub).

### 2. System-Profile + System-Template (Code-Konstanten)

In `src/Geef.Atelier.Core/Domain/Crew/SystemCrew.cs` (oder ähnlich):

**System-Reviewer-Profile:**
- `BriefingFidelityProfile` — System-Prompt aus dem aktuellen `BriefingTreueReviewer` mit PS-2-Severity-Kalibrierung
- `ClarityProfile` — analog aus aktuellem `KlarheitReviewer`

**System-Executor-Profile:**
- `DefaultExecutorProfile` — System-Prompt entspricht dem aktuellen Executor-Verhalten

**System-CrewTemplate:**
- `KlassikTemplate` — `ExecutorProfileName: "default-executor"`, `ReviewerProfileNames: ["briefing-fidelity", "clarity"]`, `EvaluationStrategy: Parallel`, `ConvergenceOverride: null` (nutzt SDK-Default)

Alle System-Konstanten sind `IsSystem = true` und werden zur Laufzeit als read-only behandelt.

### 3. Persistence-Layer

**DB-Migration `Step11CrewSystem`:**

```sql
-- Custom-Profile-Tabellen
CREATE TABLE "ReviewerProfiles" (
    "Name" text PRIMARY KEY,           -- mit "custom-" prefix
    "DisplayName" text NOT NULL,
    "Description" text NOT NULL,
    "SystemPrompt" text NOT NULL,
    "Provider" text NOT NULL,
    "Model" text NOT NULL,
    "MaxTokens" integer NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT now(),
    "UpdatedAt" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE "ExecutorProfiles" (...);  -- analog

CREATE TABLE "CrewTemplates" (
    "Name" text PRIMARY KEY,           -- mit "custom-" prefix
    "DisplayName" text NOT NULL,
    "Description" text NOT NULL,
    "ExecutorProfileName" text NOT NULL,
    "ReviewerProfileNames" jsonb NOT NULL,   -- string[]
    "EvaluationStrategy" text NOT NULL,
    "ConvergenceOverrideJson" jsonb NULL,
    "AdvisorProfileNames" jsonb NOT NULL DEFAULT '[]',
    "CreatedAt" timestamptz NOT NULL DEFAULT now(),
    "UpdatedAt" timestamptz NOT NULL DEFAULT now()
);

-- RunEntity-Erweiterung
ALTER TABLE "Runs" ADD COLUMN "CrewTemplateName" text NULL;
ALTER TABLE "Runs" ADD COLUMN "CrewSnapshot" jsonb NULL;
UPDATE "Runs" SET "CrewTemplateName" = 'klassik' WHERE "CrewTemplateName" IS NULL;

-- Reviewer-Namen-Migration in historischen Findings
UPDATE "Findings" SET "ReviewerName" = 'briefing-fidelity'
  WHERE "ReviewerName" = 'BriefingTreueReviewer';
UPDATE "Findings" SET "ReviewerName" = 'clarity'
  WHERE "ReviewerName" = 'KlarheitReviewer';
```

**Repository-Interfaces in Core:**
- `IReviewerProfileRepository` — CRUD für Custom-Profile + Lookup auf System-Profile
- `IExecutorProfileRepository` — analog
- `ICrewTemplateRepository` — CRUD für Custom-Templates + Lookup auf System-Templates

Implementations in Infrastructure als `internal sealed`-Repos.

### 4. Application-Layer

**`ICrewService` in `src/Geef.Atelier.Application/Crew/`:**
- `Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(ct)` — System + Custom kombiniert
- `Task<ReviewerProfile?> GetReviewerProfileAsync(string name, ct)`
- `Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile profile, ct)` — Auto-Prefix `custom-` wenn nicht vorhanden, Konflikt-Check mit System-Profil-Namen
- `Task UpdateCustomReviewerProfileAsync(string name, ReviewerProfile updated, ct)` — wirft bei System-Profile-Name
- `Task DeleteCustomReviewerProfileAsync(string name, ct)` — wirft bei System-Profile-Name
- Analog für `ExecutorProfile` und `CrewTemplate`

**`IRunService.SubmitRunAsync` erweitert:**
```csharp
Task<Guid> SubmitRunAsync(
    string briefingText,
    string configJson,
    string? createdByUser = null,
    string? crewTemplateName = null,    // NEU
    CrewSpec? customCrew = null,         // NEU
    CancellationToken ct = default);
```

**Logik:**
1. `customCrew` hat Vorrang vor `crewTemplateName`
2. Wenn beide null: Default-Template `"klassik"`
3. Aus dem aufgelösten Template/Spec wird ein **CrewSnapshot** erstellt (alle Profile vollständig dereferenziert) und in `RunEntity.CrewSnapshot` als JSON persistiert
4. `RunEntity.CrewTemplateName` bekommt den Template-Namen (oder null bei Custom-Crew)

### 5. Pipeline-Layer

**`ProfileBasedReviewer` als Ersatz für `LlmReviewer`:**
- Konstruktor: `(ReviewerProfile profile, ILlmClientResolver resolver, ...)`
- Nutzt `profile.Name` als `IReviewer.Name` (also den Profile-Namen, nicht die Klasse)
- Nutzt `profile.SystemPrompt` für den Reviewer-System-Prompt
- Nutzt `profile.Provider` + `profile.Model` über den Resolver
- ToolChoice-Convention `"function:submit_review"` bleibt (D-018(b))

**`ProfileBasedExecutor` analog für den Executor.**

**`AtelierPipelineFactory.Build`** wird umgestellt:
- Nimmt den Run-Snapshot (CrewSnapshot deserialisiert) als Konfigurationsinput
- Baut dynamisch `ProfileBasedExecutor` + N `ProfileBasedReviewer`
- Mappt `CrewSnapshot.EvaluationStrategy` auf SDK-Strategie-Klasse (Architect verifiziert SDK-API)
- Mappt `CrewSnapshot.ConvergenceOverride` auf SDK-Policy-Konstruktion
- Falls SDK eine Strategy-Klasse nicht bietet (z.B. `SequentialEvaluationStrategy`), implementiert Atelier sie als Custom-Klasse — Architect entscheidet ob das einen separaten Sub-Step braucht oder hier mit-erledigt wird

### 6. MCP-Layer

**`submit_request`-Tool erweitert:**
- Optional `crew_template: string?`
- Optional `custom_crew: object?` (deserialisiert zu `CrewSpec`)

**Neue MCP-Tools in `src/Geef.Atelier.Mcp/Tools/`:**
- `list_crew_templates` — Array of `{ name, display_name, description, executor_profile, reviewer_profiles[], evaluation_strategy, is_system }`
- `list_reviewer_profiles` — Array of `{ name, display_name, description, provider, model, is_system }`

Bestehende Tools (`get_run_*`, `cancel_run`, `list_runs`) müssen nicht angepasst werden — sie lesen `RunEntity` und das neue Crew-Feld wird automatisch mit ausgeliefert.

### 7. Tests

**Domain-Tests** (`tests/Geef.Atelier.Tests/Domain/Crew/`):
- `ReviewerProfileEqualityTests`
- `CrewTemplateValidationTests`
- `SystemCrewConstantsTests` — Klassik-Template referenziert vorhandene System-Profile

**Repository-Tests** (mit Testcontainer-Postgres):
- `ReviewerProfileRepositoryTests` — CRUD, Auto-Prefix, System-Profile-Lookup
- `CrewTemplateRepositoryTests` — analog

**Application-Tests:**
- `CrewServiceTests` — Konflikt-Verhalten bei System-Profile-Namen, Prefix-Auto-Apply
- `RunServiceWithCustomCrewTests` — Submit mit Custom-Crew baut korrekten Snapshot

**Pipeline-Tests:**
- `ProfileBasedReviewerTests` — Reviewer ruft LLM mit korrektem Profile-Prompt
- `AtelierPipelineFactoryWithCrewTests` — Pipeline wird dynamisch aus Snapshot gebaut, alle vier EvaluationStrategies mappen korrekt

**MCP-Tests:**
- `SubmitRequestWithCrewTemplateTests`
- `ListCrewTemplatesTests`
- `ListReviewerProfilesTests`

**Migration-Tests:**
- `Step11CrewSystemMigrationTests` — historische Daten werden korrekt migriert (Klassik-Default, Reviewer-Name-Mapping)

**Bestehende Tests müssen grün bleiben** (113 nach PS-4) — alle Pipeline-Tests, die heute hartkodierte Reviewer-Klassen verwenden, müssen umgestellt werden auf das Profile-Pattern. Das ist mechanisch.

### 8. Real-Pipeline-Verifikation

**Hadwiger-Nelson-Replay** mit explizitem `crewTemplateName: "klassik"` → muss identisch zum PS-2-Stand laufen (kein Verhaltens-Regress).

**Custom-Crew-Test** (manuell oder im Bericht):
- Custom-Profile mit GPT-5-Modell als zweiter Reviewer anlegen
- Submit mit Custom-Crew
- Verifikation: Crew-Snapshot zeigt korrekte Profile, Pipeline läuft mit zwei verschiedenen Modellen

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` 0 Errors, 0 Warnings.
2. `dotnet test` — alle bestehenden Tests grün (113 nach PS-4 + neue Crew-Tests).
3. **DB-Migration `Step11CrewSystem`** läuft sauber gegen Production-DB. Historische Runs haben `CrewTemplateName = 'klassik'`, historische Findings haben aktualisierte `ReviewerName`-Werte.
4. **System-Profile + System-Template im Code** verfügbar und über die Repositories abrufbar.
5. **Custom-Profile/Template via `ICrewService`** anlegbar, Auto-Prefix funktioniert, Konflikt-Schutz funktioniert.
6. **`IRunService.SubmitRunAsync` mit `crewTemplateName`** funktional. Default `"klassik"` wirkt bei null/leer.
7. **`IRunService.SubmitRunAsync` mit `customCrew`** funktional. Snapshot wird korrekt persistiert.
8. **Pipeline baut dynamisch aus Snapshot** — alle vier EvaluationStrategies getestet (mindestens Mock-Test, real wenn SDK das unterstützt).
9. **MCP `list_crew_templates` und `list_reviewer_profiles`** funktional. `submit_request` mit Crew-Parametern funktional.
10. **Hadwiger-Nelson-Replay mit Klassik-Template** läuft identisch zum PS-2-Stand (Verhaltens-Regression-Check).
11. **Real-Custom-Crew-Test** im Bericht dokumentiert (mindestens ein Run mit Custom-Profile durchgeführt).
12. **Decisions-Log-Eintrag** (D-028 oder nächste freie Nummer) mit Architect-Entscheidungen.

## Was du in diesem Schritt NICHT tust

- **Keine UI** — Crew-Auswahl auf NewRun-Page, Profile-CRUD-Seiten, Crew-Anzeige auf Detail-Page → PS-6.
- **Keine Advisor-Implementierung** — `AdvisorProfile`-Schema steht vor, aber kein funktionaler Advisor-Pass → PS-7.
- **Keine neuen Domänen-Templates** — nur `"klassik"`. Juristisch, Akademisch etc. entstehen organisch in späteren Steps.
- **Keine Cost-Tracking-Erweiterung** — bleibt aus.
- **Keine Reviewer-Prompts-Schärfung** — die aus PS-2 sind die System-Profile-Prompts. Werden nicht verändert.
- **Keine LLM-Provider-Schicht-Änderungen** — `LlmOptions`, `ILlmClientResolver` aus PS-4 bleiben unverändert.
- **Keine Convergence-Policy-globale-Änderungen** — die aus PS-2 bleibt. Templates können sie nur per-Run überschreiben.

## Architect-Konsultation (Phase 1.4) — vier echte Knackpunkte

(Diesmal kürzer als üblich, weil die Klärungs-Antworten viele Entscheidungen schon fixiert haben.)

1. **SDK-EvaluationStrategy-Mapping:** Welche der vier Strategien (Parallel/Sequenziell/FailFast/Prioritätsgesteuert) sind als SDK-Klassen verfügbar? Welche muss Atelier ggf. selbst implementieren? Falls Selbst-Implementation: in PS-5 mit-erledigen oder als Folge-Step?

2. **SDK-ConvergencePolicy-Konstruktion:** Wie wird die `DefaultConvergencePolicy` mit Override-Werten konstruiert? Reicht ein neuer Constructor-Aufruf pro Template-Use, oder muss eine eigene `AtelierConvergencePolicy` her? `IConvergencePolicy`-Hook-Verfügbarkeit prüfen.

3. **CrewSnapshot-JSON-Format:** Exakte Form. Eingebettetes vollständiges Profile-Objekt vs. nur Referenz-Namen + späteres Re-Resolving. Empfehlung: **vollständig eingebettet**, damit Reproduzierbarkeit garantiert ist (Profile können sich später ändern, der Run bleibt aussagekräftig). Architect bestätigt.

4. **AdvisorProfile-Schema-Tiefe:** Reines Marker-Stub vs. schon Felder (Name, SystemPrompt, Provider, Model, AdvisorMode-Hint) ähnlich `ReviewerProfile`. Empfehlung: **wie `ReviewerProfile`-Schema**, mit zusätzlicher `AdvisorMode`-Enum (z.B. `Strategic`, `Critical`, `Devil`, `Domain-Expert` — entsprechend dem Advisor-Artikel). PS-7 nutzt das dann ohne Schema-Erweiterung.

`geef_architecture.md` prüft Konsistenz mit dem neuen Schichtenbild.

## Reviewer-Hinweise

- **R1 (Functional Correctness):** Alle 12 ACs prüfen. Besonders 3 (Migration), 6 (Klassik-Default-Sanity), 10 (Verhaltens-Regression), 11 (Real-Custom-Crew).
- **R2 (Code Quality):** Saubere Profile-Vererbung wenn Base-Klasse `LlmActorProfile`. Repository-Pattern konsistent mit `IRunRepository` aus Schritt 6.
- **R3 (Test Execution):** Bestehende 113 Tests + neue Crew-Tests grün. Mock-LLM-Tests für die vier EvaluationStrategies.
- **R4 (Architecture Compliance):** Layer-Trennung weiter sauber. `Geef.Atelier.Core` enthält Domain-Entities ohne Infrastructure-Dep. `Geef.Atelier.Application/Crew/` ohne Infrastructure-Dep.
- **R5 (Live Sanity):** Hadwiger-Nelson-Replay + Custom-Crew-Test auf `https://geef.stefan-bechtel.de/`.

## Persistenter Abschlussbericht

Bericht nach `docs/reports/post-skeleton-05-crew-foundation-report.md`. Inhalt:

1. **Was wurde umgesetzt** — Schicht für Schicht.
2. **SDK-Recherche-Ergebnisse** — welche EvaluationStrategies/ConvergencePolicy-Hooks tatsächlich verfügbar sind.
3. **Architect-Output** — die vier Knackpunkte plus alle implizit getroffenen Entscheidungen aus den Klärungs-Antworten.
4. **Pre-Mortem & Devil's Advocate** — Migrations-Fehler, Profile-Namens-Konflikte, Snapshot-Reproduzierbarkeit, EvaluationStrategy-Mock-Realismus.
5. **Reviewer-Iterationen** — Tabelle.
6. **Akzeptanzkriterien-Check** — Tabelle mit allen 12 ACs.
7. **Beobachtungen** — wie aufwändig waren die Pipeline-Umbauten? Welche Stellen waren überraschend einfach/schwer?
8. **Real-Custom-Crew-Test-Ergebnis** — welche Crew wurde getestet, welcher Outcome.
9. **Vorbereitung für PS-6** — was muss die UI noch wissen, das hier nicht offensichtlich war?

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- **Niemals Secrets** in source control, Logs oder Bericht.
- Custom-Profile-Namen werden im Bericht **anonymisiert**, falls sie sensitive Domain-Bezüge enthalten.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.

---

**Nach erfolgreichem Abschluss:** Crew-Foundation steht. Jede Run weiß welche Crew sie genutzt hat, Custom-Crews sind über API/MCP anlegbar und nutzbar. PS-6 baut die UI darüber (Template-Auswahl auf NewRun, Profile-Verwaltungs-Seiten). PS-7 ergänzt Advisor-Pässe.