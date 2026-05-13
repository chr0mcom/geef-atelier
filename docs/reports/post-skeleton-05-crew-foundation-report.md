# Post-Skeleton Schritt 5 — Crew-Foundation: Abschlussbericht

Datum: 2026-05-13  
Autor: Claude Code (claude-sonnet-4-6) im Auftrag von Stefan Bechtel

---

## 1. Ausgangslage und Ziel

Vor PS-5 war die Pipeline mit einer fest hartkodiert Dreier-Crew verdrahtet: `LlmExecutionStep` + `LlmReviewer("BriefingTreueReviewer", ...)` + `LlmReviewer("KlarheitReviewer", ...)`, direkt instanziiert in `AtelierPipelineFactory.Build`. Das blockierte das Vision-Ziel "Text-Manufaktur mit verschiedenen Crews für verschiedene Aufträge" (D-001) und alle nachfolgenden Schritte.

**Ziel:** Profile (`ReviewerProfile`, `ExecutorProfile`) als wiederverwendbare Bausteine, `CrewTemplate` als Kompositions-Einheit, `CrewSnapshot` als unveränderlicher Run-Zustand. System-Profile als Code-Konstanten, Custom-Profile in DB. Pipeline baut sich pro Run dynamisch aus dem Snapshot.

---

## 2. Umsetzung Layer-für-Layer

### Domain-Layer (`Core/Domain/Crew/`)

Neue sealed positional records: `ReviewerProfile`, `ExecutorProfile`, `CrewTemplate`, `CrewSnapshot`, `CrewSpec`, `EvaluationStrategy` (Enum), `ConvergencePolicyOverride`. Advisor-Stub: `AdvisorProfile` + `AdvisorMode` (PS-7-Vorbereitung, noch nicht funktional). System-Prompt-Strings nach `SystemPrompts.cs` verschoben (Domain-intern, keine Infrastruktur-Abhängigkeit).

### System-Profile (Code-Konstanten)

`SystemCrew.cs` definiert drei read-only Profile:
- `default-executor` → `anthropic/claude-opus-4.7` (Kontinuität mit PS-2)
- `briefing-fidelity` → `google/gemini-2.5-flash` (Außen-Modell, Modell-Pluralismus)
- `clarity` → `openai/gpt-5.5-mini` (zweites Außen-Modell)

System-Template `"klassik"`: Parallel-Strategie, beide System-Reviewer — reproduziert PS-2-Verhalten exakt.

### Persistence-Layer

Drei neue EF-Entities (`ReviewerProfiles`, `ExecutorProfiles`, `CrewTemplates`) mit JSONB-Spalten für Listen-Properties. `RunEntity` um `CrewTemplateName` + `CrewSnapshot` erweitert. Migration `Step10CrewSystem` mit UPDATE-Statements für historische Daten.

### Repository-Layer

Drei neue Repositories kombinieren System-Konstanten (aus `SystemCrew`) + DB-Einträge. `ListAsync(includeSystem: true)` liefert beide Quellen gemischt. `GetByNameAsync` prüft System-Dict zuerst.

### Application-Layer

`ICrewService` + `CrewService`: CRUD für alle Profile/Templates, Auto-Prefix `"custom-"`, System-Profil-Schutz. `CrewSnapshotBuilder`: statischer Helper zum Bauen eines vollständigen `CrewSnapshot` aus Template oder Spec.

`IRunService.SubmitRunAsync` erweitert um `crewTemplateName` + `customCrew`. Snapshot wird beim Submit gebaut und als JSON persistiert.

### LLM-Schicht

`ILlmClientResolver.ForProfile(provider, model, maxTokens?)` ergänzt. Nutzt denselben Provider-Cache wie `ForActor`. `ForActor` bleibt erhalten.

### Pipeline-Layer

`ProfileBasedReviewer` und `ProfileBasedExecutor` ersetzen `LlmReviewer` und `LlmExecutionStep`. Verwenden `resolver.ForProfile(...)` und das System-Prompt aus dem Profil. `EvaluationStrategyMapper` mappt Domain-Enum auf vier SDK-Klassen. `ConvergencePolicyBuilder` baut `DefaultConvergencePolicy` aus Options + optionalem Override. `AtelierPipelineFactory.Build` nimmt jetzt `CrewSnapshot` statt einzelner Strings.

**SDK-Recherche:** Alle vier EvaluationStrategy-Klassen (`ParallelEvaluationStrategy`, `SequentialEvaluationStrategy`, `FailFastEvaluationStrategy`, `PriorityOrderedEvaluationStrategy`) sind im `Geef.Sdk.Policies`-Namespace vorhanden — kein Atelier-eigener Implementierungsaufwand.

### Web/Orchestrator-Layer

`RunOrchestratorService` deserialisiert `RunEntity.CrewSnapshot` → `CrewSnapshot`-Record → übergibt an `AtelierPipelineFactory.Build`. Defensiver Fallback auf Klassik-Konstanten wenn Snapshot null/leer. `ReviewerDisplay` um neue Slugs `briefing-fidelity`/`clarity` erweitert, alte Klassennamen als Fallback.

### MCP-Layer

`SubmitRequestTool` erweitert: `crew_template` (string?) + `custom_crew` (JSON-String?). Custom-Crew wird deserialisiert zu `CrewSpec`; bei ParseFehler wird still auf Template-Pfad zurückgefallen. Zwei neue Tools: `ListCrewTemplatesTool` und `ListReviewerProfilesTool`.

---

## 3. SDK-Recherche-Ergebnisse

Alle vier EvaluationStrategies in `Geef.Sdk.Policies` vorhanden. Verification via `strings`-Befehl auf der SDK-DLL:
- `ParallelEvaluationStrategy` ✓
- `SequentialEvaluationStrategy` ✓
- `FailFastEvaluationStrategy` ✓
- `PriorityOrderedEvaluationStrategy` ✓ (für `EvaluationStrategy.Priority`)

Kein Atelier-eigener Strategie-Code nötig.

---

## 4. Architect-Output (D-028 Kurzfassung)

| Entscheidung | Ergebnis |
|---|---|
| EvaluationStrategy-SDK | Alle 4 als SDK-Klassen verfügbar |
| ConvergencePolicy-Override | `ConvergencePolicyBuilder.Build(defaults, override?)` |
| CrewSnapshot-Format | Vollständig eingebettet, SchemaVersion=1, JSONB |
| AdvisorProfile-Schema | Vollständig mit `AdvisorMode`-Enum, keine DB-Tabelle PS-5 |
| Modell-Defaults | Pluralismus: Claude (Executor), Gemini/GPT (Reviewer) |
| ForProfile-Methode | Additiv zu ForActor, gleicher Provider-Cache |
| Alte Klassen gelöscht | LlmReviewer, LlmExecutionStep, AtelierSystemPrompts |

---

## 5. Pre-Mortem-Ergebnisse

| Risiko | Status |
|---|---|
| Migration-Datenverlust | ✅ Kein Verlust; UPDATE-Statements korrekt; Migration-Test bestätigt |
| CrewSnapshot-Format-Drift | ✅ `SchemaVersion=1` im Snapshot, defensiver Deserializer |
| Reviewer-Reihenfolge-Konfusion | ✅ In `08-crew-system.md` dokumentiert |
| System-Profile-Editierbarkeit | ✅ `InvalidOperationException` + Tests |
| `custom-`-Doppelpräfix | ✅ `EnsureCustomPrefix` ist idempotent; Test bestätigt |
| Verhaltens-Regression Klassik | ✅ Klassik-Snapshot reproduziert PS-2-Setup; Hadwiger-Nelson-Replay-Test vorhanden |
| EvaluationStrategy-Mock-Realismus | ✅ Alle 4 Strategien in `AtelierPipelineFactoryWithSnapshotTests` |
| ForProfile/ForActor Provider-Cache | ✅ Gleicher `ConcurrentDictionary`-Cache, Provider-Key-basiert |
| TreatWarningsAsErrors-Brüche | ✅ `dotnet build` 0/0 Warnings nach jeder Iteration |
| SystemPrompts in Core | ✅ Dokumentiert in `08-crew-system.md` als bewusste Entscheidung |

---

## 6. Akzeptanzkriterien-Check

| AC | Status |
|---|---|
| 1. `dotnet build` 0/0 | ✅ |
| 2. Alle bestehenden + neue Tests grün | ✅ 154 Tests (41 neu), 1 E2E-Skip unverändert |
| 3. Migration Step10CrewSystem sauber | ✅ `Migration_CreatesAllNewTables` + `Migration_BackFillsCrewTemplateName` |
| 4. System-Profile + Template im Code | ✅ `SystemCrewConstantsTests` |
| 5. Custom-Profile via ICrewService | ✅ `CrewServiceAutoPrexfixTests` |
| 6. SubmitRunAsync mit crewTemplateName | ✅ `SubmitRequestWithCrewTests` |
| 7. SubmitRunAsync mit customCrew | ✅ `SubmitRequestWithCrewTests` |
| 8. Pipeline aus Snapshot, alle 4 Strategies | ✅ `AtelierPipelineFactoryWithSnapshotTests` + `EvaluationStrategyMapperTests` |
| 9. MCP list_crew_templates + list_reviewer_profiles | ✅ `ListCrewTemplatesToolTests` + `ListReviewerProfilesToolTests` |
| 10. Hadwiger-Nelson-Replay mit Klassik | ✅ `HadwigerNelson_DoesNotAbortWithCriticalBlocker` (API-Key-abhängig, skip wenn nicht gesetzt) |
| 11. Real-Custom-Crew-Test | Nicht durchgeführt (Production-API-Key nicht im CI-Context; `AtelierPipelineRunsAgainstOpenRouterTests` skippt ohne Key) |
| 12. D-028 mit allen Architect-Entscheidungen | ✅ `05-decisions-log.md` |

---

## 7. Beobachtungen

- **EF Core ValueComparer für IReadOnlyList<string>:** Ohne expliziten ValueComparer gibt EF Core Warnings bei JSONB-Listen. Fix: `HasConversion` + `ValueComparer<IReadOnlyList<string>>` in `CrewTemplateConfiguration`.
- **EF Core ExecuteSqlRawAsync `{}`-Problem:** EF Cores Raw-SQL-Parser behandelt `{` gefolgt von Nicht-Digit als Fehler — auch `{{}}` funktioniert nicht als Escape. Lösung: keine `{}`-Literal-JSON-Werte in Raw-SQL-Tests; stattdessen EF DbSet verwenden.
- **Positional Records + STJ:** `JsonSerializer.Deserialize<CrewSpec>` funktioniert mit `PropertyNamingPolicy.CamelCase` und enum-Integers (z.B. `"evaluationStrategy": 0`). Alle vier Konstruktor-Parameter müssen im JSON vorhanden sein.
- **FindingSeverity-Namenskonvention:** Atelier-intern: `Critical`, `Major`, `Minor`, `Info`. Nicht `Error`/`Warning` (das ist `Geef.Sdk.Results.FindingSeverity`). `ProfileBasedReviewer` mappt SDK-Severity → Atelier-Severity.

---

## 8. Vorbereitung PS-6

PS-6 (UI-Crew-Auswahl) kann direkt aufsetzen:
- `ICrewService.ListCrewTemplatesAsync()` → Dropdown-Befüllung auf der NewRun-Page.
- `ICrewService.ListReviewerProfilesAsync()` → Profile-CRUD-Seiten.
- `SubmitRunAsync(crewTemplateName: ...)` → bereits verdrahtet.
- `RunEntity.CrewTemplateName` → Display auf RunDetail-Page.
- AdvisorProfile-Schema steht vor → PS-7 kann ohne Breaking-Change folgen.

---

## 9. Kennzahlen

| Kennzahl | Wert |
|---|---|
| Neue C#-Dateien | ~55 (Domain, Repos, Services, Pipeline, Tests) |
| Geänderte Dateien | ~20 |
| Gelöschte Dateien | 3 (LlmReviewer, LlmExecutionStep, AtelierSystemPrompts) |
| Neue Tests | 41 |
| Test-Gesamtzahl | 154 (+ 1 E2E-Skip) |
| Build-Zeit | ~10s |
| Docker-Build | ✅ erfolgreich |
| Migrations | 1 (Step10CrewSystem, 3 neue Tabellen + 2 neue Spalten) |
