# Post-Skeleton Schritt 7 — Advisor-Pässe: Abschlussbericht

Datum: 2026-05-13  
Autor: Claude Code (claude-sonnet-4-6) im Auftrag von Stefan Bechtel

---

## 1. Was wurde umgesetzt

PS-7 fügt dem Atelier ein vollständiges Advisor-System hinzu: konfigurierbare LLM-Profile, die vor oder nach GEEF-Iterationen konsultiert werden und deren Output in den Kontext der nächsten Iteration einfließt. Die Implementierung folgt streng der Schichtenarchitektur.

### Domain (Core)

| Datei | Inhalt |
|---|---|
| `Core/Domain/Crew/Advisors/AdvisorTrigger.cs` | Enum: `BeforeFirstExecution`, `BeforeEveryExecution`, `OnConvergenceFailure` |
| `Core/Domain/Crew/Advisors/AdvisorConsultation.cs` | Record: `Id`, `RunId`, `IterationNumber`, `AdvisorProfileName`, `Output`, `CreatedAt` |
| `Core/Domain/Crew/Advisors/AdvisorProfile.cs` (geändert) | `Trigger`-Feld ergänzt (war zuvor ohne Trigger-Semantik) |
| `Core/Domain/Crew/SystemCrew.cs` (geändert) | `BriefingClarifierProfile`, `DevilsAdvocateProfile`, `AdvisorProfiles`-Dict, `IsSystemAdvisorName()` |

### Persistence

| Datei | Inhalt |
|---|---|
| `Core/Persistence/Crew/IAdvisorProfileRepository.cs` | CRUD-Interface für Advisor-Profile |
| `Core/Persistence/Crew/IAdvisorConsultationRepository.cs` | Insert + GetByRunId |
| `Infrastructure/Persistence/Repositories/AdvisorProfileRepository.cs` | EF-Core-Implementierung |
| `Infrastructure/Persistence/Repositories/AdvisorConsultationRepository.cs` | EF-Core-Implementierung |
| `Infrastructure/Persistence/Configurations/AdvisorProfileConfiguration.cs` | Fluent-API-Konfiguration |
| `Infrastructure/Persistence/Configurations/AdvisorConsultationConfiguration.cs` | Fluent-API-Konfiguration |
| `Infrastructure/Persistence/AtelierDbContext.cs` (geändert) | +2 DbSets: `AdvisorProfiles`, `AdvisorConsultations` |
| `Infrastructure/Persistence/Entities/RunEntity.cs` (geändert) | +`AdvisorRetryAttempted bool` (Single-Retry-Cap-Marker) |
| Migration `20260513113803_Step11AdvisorSystem` | Neue Tabellen, neues Feld |

### Application

| Datei | Inhalt |
|---|---|
| `Application/Crew/CrewSpec.cs` (geändert) | +`AdvisorProfileNames` (string-Array) |
| `Application/Crew/ICrewService.cs` (geändert) | +5 Methoden: Create/Update/Delete/Get/List für Advisor-Profile |
| `Application/Crew/CrewService.cs` (geändert) | Implementierung der 5 Methoden |
| `Application/Crew/CrewSnapshotBuilder.cs` (geändert) | `ResolveAdvisorsAsync` statt `Array.Empty` |

### Pipeline (Infrastructure)

| Datei | Inhalt |
|---|---|
| `Infrastructure/Pipeline/ProfileBasedAdvisor.cs` | LLM-Aufruf (plain text), `AdvisorConsultation` persistieren |
| `Infrastructure/Pipeline/AdvisorAwareExecutor.cs` | Decorator für `IExecutionStep`: Pre-Execution-Filter nach Trigger, Context-Injektion |
| `Infrastructure/Pipeline/AtelierContextKeys.cs` | `AdvisorBlock` ContextKey (Prefix: `atelier:`) |
| `Infrastructure/Pipeline/AdvisorContextGroundingStep.cs` | Grounding-Step für `BuildWithAdvisorContext` |
| `Infrastructure/Pipeline/AtelierPipelineFactory.cs` (geändert) | Decorator-Wiring + `BuildWithAdvisorContext`-Methode |
| `Infrastructure/Pipeline/ProfileBasedExecutor.cs` (geändert) | Liest `AtelierContextKeys.AdvisorBlock`, prepend in System-Prompt |
| `Web/Services/RunOrchestratorService.cs` (geändert) | `TryConvergenceFailureRetryAsync` nach Pipeline-Aufruf, vor Status-Update |

### MCP

| Datei | Inhalt |
|---|---|
| `Mcp/Dtos/AdvisorProfileDto.cs` | DTO für MCP-Antworten |
| `Mcp/Tools/ListAdvisorProfilesTool.cs` | `list_advisor_profiles`-Tool |

### UI (Web)

| Datei | Inhalt |
|---|---|
| `Web/Components/UI/AdvisorPicker.razor` + `.razor.css` | Multi-Select-Komponente für Advisor-Profile im Template-Editor |
| `Web/Components/UI/AdvisorConsultationsBlock.razor` + `.razor.css` | Zeigt Consultations auf RunDetail (aufklappbar pro Consultation) |
| `Web/Components/Pages/AdvisorProfilesIndex.razor` | Übersichtsliste aller Advisor-Profile |
| `Web/Components/Pages/AdvisorProfileEditor.razor` | Create/Edit mit Trigger-Auswahl, Modell-Auswahl, Prompt-Feld |
| `Web/Display/ReviewerDisplay.cs` (geändert) | +3 Advisor-Helper-Methoden: Trigger-Label, Icon, Zusammenfassung |
| `Web/Components/UI/ProfileEditorForm.razor` (geändert) | `ShowAdvisorFields`, Mode/Trigger Radio-Groups |
| `Web/Components/UI/CrewSummary.razor` (geändert) | Advisor-Sektion im expanded-State |
| `Web/Components/Pages/CrewIndex.razor` (geändert) | Advisor-Profile-Sektion |
| `Web/Components/Pages/CrewTemplateEditor.razor` (geändert) | `AdvisorPicker` eingebunden |
| `Web/Components/Pages/RunDetail.razor` (geändert) | `AdvisorConsultationsBlock` + Recovery-Sektion (ConvergenceFailure) |

---

## 2. SDK-Recherche-Ergebnisse

### Was im Geef-SDK vorhanden ist

| SDK-Element | Befund |
|---|---|
| `Geef.Sdk.Exceptions.ConvergenceFailedException` | Vorhanden und explizit dokumentiert. Wird vom SDK geworfen, wenn die Evaluation-Strategie nach max. Iterationen kein konvergentes Ergebnis liefert. Ermöglicht saubere `catch`-Behandlung im Orchestrator. |
| `IExecutionStep` | Das korrekte Interface für den Decorator-Einstiegspunkt. Das SDK unterscheidet zwischen `IExecutionStep` (eine Iteration) und dem übergeordneten Pipeline-Executor. `AdvisorAwareExecutor` wrапpt `IExecutionStep`, nicht `IExecutor`. |
| `Geef.Sdk.Advisors.IAdvisor` | Im SDK nativ vorhanden. Wurde in der Recherche entdeckt, aber **bewusst nicht genutzt** (siehe unten). |

### Warum das native `IAdvisor` nicht genutzt wird

Das SDK-native `IAdvisor`-Interface würde eine Abhängigkeit von `Geef.Sdk` im Application- oder Domain-Layer erzwingen. Da Geef.Atelier die Schichtenarchitektur (Core ist LLM- und SDK-frei) als Hard Rule definiert hat, wurde stattdessen das Decorator-Pattern auf Atelier-Infrastruktur-Ebene gewählt:

- `AdvisorAwareExecutor` implementiert `IExecutionStep` (SDK-Interface, nur in Infrastructure erlaubt)
- Die eigentliche Advisor-Logik (`ProfileBasedAdvisor`) kennt kein SDK, nur `ILlmClientResolver` und `IAdvisorConsultationRepository`
- Das Ergebnis ist funktional äquivalent zu `IAdvisor`, aber ohne SDK-Kopplung in tieferen Schichten

Diese Entscheidung ist im Decisions-Log (D-030) festgehalten.

---

## 3. Architect-Output (D-030): Fünf Knackpunkte

### (a) Decorator vs. SDK-Hook

Das SDK stellt `IAdvisor` bereit. Atelier nutzt stattdessen einen `IExecutionStep`-Decorator (`AdvisorAwareExecutor`), der `ProfileBasedExecutor` umhüllt. Begründung: `IAdvisor` wäre nur sauber nutzbar, wenn die Implementierung in Infrastructure liegt und dort den vollen SDK-Kontrakt erfüllt — das ist möglich, aber erzeugt eine enge SDK-Kopplung. Der Decorator ist dünner und hält alle Advisor-Logik explizit in Atelier-Kontrolle.

### (b) Context-Block via AtelierContextKeys.AdvisorBlock

Advisor-Output wird nicht per Parameter übergeben, sondern über den GEEF-Kontext. `AdvisorAwareExecutor` ruft alle relevanten Advisors auf, konkateniert deren Outputs zu einem Block und setzt `context.Set(AtelierContextKeys.AdvisorBlock, block)`. `ProfileBasedExecutor` liest diesen Key und prepended den Block vor dem eigentlichen System-Prompt. Der ContextKey folgt der `atelier:`-Präfix-Konvention (nicht `geef:`, da Atelier-intern).

### (c) Advisor-Failure → Run fehlgeschlagen

Wenn ein Advisor-LLM-Call fehlschlägt (Netzwerk, Modell nicht verfügbar, Token-Limit), bubbled die Exception durch `AdvisorAwareExecutor` in den `RunOrchestratorService`. Dort wird der Run als `Failed` markiert. Es gibt keine Fallback-Logik (Advisor-Fehler sind keine Soft-Fehler): ein fehlgeschlagener Advisor bedeutet, dass der Run nicht korrekt vorbereitet wurde — lieber explizit scheitern als stille Fehlkonfiguration.

### (d) Reihenfolge signifikant

Die Reihenfolge der `AdvisorProfileNames` in `CrewSpec` ist semantisch bedeutsam: Advisors werden in Listenreihenfolge konsultiert, und jeder spätere Advisor erhält den gleichen ursprünglichen Kontext (nicht den Output des vorherigen). Der konkatenierte Block enthält alle Outputs in der definierten Reihenfolge. Für den Executor ist die Position im Block relevant (frühere Advisors stehen weiter oben im Prompt).

### (e) OnConvergenceFailure — Single-Retry-Cap

`ConvergenceFailedException` wird im `RunOrchestratorService` gefangen. `TryConvergenceFailureRetryAsync` prüft zunächst, ob `RunEntity.AdvisorRetryAttempted` gesetzt ist. Wenn ja: kein weiterer Retry, Run schlägt fehl (Exception bubbled). Wenn nein: `AdvisorRetryAttempted = true` persistieren, Advisors mit `OnConvergenceFailure`-Trigger konsultieren, Run neu starten. Dieser Mechanismus verhindert Endlos-Loops durch die Datenbankmarke — auch bei Container-Neustart wird der Retry-Status nicht verloren.

---

## 4. Pre-Mortem & Devil's Advocate

### Authority-Bias-Risiko

**Risiko:** Ein Advisor mit hoher Autorität (z. B. "Senior Architect") könnte den Executor so stark beeinflussen, dass dieser keine eigenständigen Schlussfolgerungen mehr zieht — der Advisor dominiert statt berät.

**Mitigation:** Prompt-Engineering in `BriefingClarifierProfile` und `DevilsAdvocateProfile` ist explizit formuliert, um den Executor zur eigenen Beurteilung anzuhalten ("Prüfe kritisch, ob dieser Hinweis auf deine Aufgabe zutrifft"). Monitoring der Consultation-Outputs auf inhaltliche Vielfalt ist als Empfehlung festgehalten (siehe Abschnitt 9).

### Advisor-Output-Länge

**Risiko:** Ein Advisor-LLM liefert sehr langen Output → der konkatenierte Block überschreitet das Kontext-Fenster des Executors.

**Mitigation:** `ProfileBasedAdvisor` nutzt `MaxTokens`-Cap beim LLM-Call. Advisor-Profile sollten mit deutlich niedrigerem `MaxTokens` als Executor-Profile konfiguriert werden. Diese Empfehlung ist in der UI (`AdvisorProfileEditor`) durch einen Hinweistext sichtbar.

### Endlos-Loop-Risiko

**Risiko:** `OnConvergenceFailure` löst Retry aus → Retry scheitert ebenfalls → erneuter Retry → Loop.

**Mitigation:** `AdvisorRetryAttempted`-Spalte in `RunEntity` (persistiert in DB). Nach dem ersten Retry wird die Spalte auf `true` gesetzt. Jeder weitere `ConvergenceFailedException`-Catch prüft diese Spalte und scheitert sofort. Kein In-Memory-Zähler, kein Race-Condition-Risiko.

### Bug-Fix-Koordination

PS-7 und ein parallel laufender Bug-Fix-Step berühren beide `RunOrchestratorService.cs`. Koordinationspunkt: PS-7 fügt `TryConvergenceFailureRetryAsync` **nach** dem Pipeline-Aufruf, **vor** dem Status-Update ein. Der Bug-Fix-Step behandelt den Error-Handling-Pfad (Provider-Fehler, Timeout). Diese Pfade sind orthogonal: der ConvergenceFailure-Pfad betritt `TryConvergenceFailureRetryAsync` nur bei expliziter `ConvergenceFailedException`, der Error-Pfad nur bei allen anderen Exceptions. Merge-Konflikt möglich, aber inhaltlich keine Überschneidung.

---

## 5. Akzeptanzkriterien-Check

| AC | Beschreibung | Status |
|---|---|---|
| AC1 | `AdvisorProfile`-Record mit Name, SystemPrompt, Modell, Trigger, IsSystem | ✅ Implementiert |
| AC2 | `AdvisorTrigger`-Enum: `BeforeFirstExecution`, `BeforeEveryExecution`, `OnConvergenceFailure` | ✅ Implementiert |
| AC3 | `AdvisorConsultation`-Record persistiert (RunId, Iteration, Name, Output, Timestamp) | ✅ Implementiert |
| AC4 | `IAdvisorProfileRepository` + `IAdvisorConsultationRepository` im Core | ✅ Implementiert |
| AC5 | EF-Core-Konfiguration + Migration für beide Tabellen | ✅ Migration `20260513113803_Step11AdvisorSystem` |
| AC6 | `ProfileBasedAdvisor` ruft LLM auf, persistiert Consultation | ✅ Implementiert |
| AC7 | `AdvisorAwareExecutor` als `IExecutionStep`-Decorator, filtert nach Trigger | ✅ Implementiert |
| AC8 | Context-Injektion via `AtelierContextKeys.AdvisorBlock`, Executor liest und prepended | ✅ Implementiert |
| AC9 | `SystemCrew` enthält `BriefingClarifierProfile` + `DevilsAdvocateProfile` | ✅ Implementiert |
| AC10 | `CrewSpec.AdvisorProfileNames` + `CrewSnapshotBuilder.ResolveAdvisorsAsync` | ✅ Implementiert |
| AC11 | CRUD-Methoden in `ICrewService` + `CrewService` | ✅ 5 Methoden |
| AC12 | UI: `AdvisorProfilesIndex`, `AdvisorProfileEditor`, `AdvisorPicker`, `AdvisorConsultationsBlock` | ✅ 4 neue Komponenten |
| AC13 | MCP: `list_advisor_profiles`-Tool + `AdvisorProfileDto` | ✅ Implementiert |
| AC14 | Single-Retry-Cap: `AdvisorRetryAttempted`-Spalte + `TryConvergenceFailureRetryAsync` | ✅ Implementiert |

Alle 14 Akzeptanzkriterien erfüllt.

---

## 6. Test-Ergebnisse

### Neue Tests (PS-7)

| Testdatei | Neue Tests | Schwerpunkt |
|---|---|---|
| `AdvisorTriggerTests.cs` | 4 | Enum-Werte, Serialisierung |
| `AdvisorConsultationTests.cs` | 5 | Record-Konstruktion, Immutabilität |
| `AdvisorProfileTests.cs` | 5 | Profile-Validierung, System-Flag |
| `ProfileBasedAdvisorTests.cs` | 6 | LLM-Aufruf via `SystemPromptRoutingClient`-Pattern, Consultation-Persistierung |
| `AdvisorAwareExecutorTests.cs` | 6 | Trigger-Filterung (BeforeFirst/BeforeEvery/OnConvergence), Context-Injektion |
| `CrewServiceAdvisorTests.cs` | 4 | CRUD-Methoden, Repository-Interaktion |
| `TryConvergenceFailureRetryAsyncTests.cs` | 3 | Single-Retry-Cap, AdvisorRetryAttempted-Flag |

**Gesamtbilanz:**

| Metrik | Wert |
|---|---|
| Neue Tests (PS-7) | +33 |
| Tests vor PS-7 | 206 |
| Tests nach PS-7 | 239 |
| Failures | 0 |
| Errors | 0 |
| Warnings | 0 |

`dotnet test` — alle 239 Tests grün, 0 Errors, 0 Warnings (TreatWarningsAsErrors=true).

---

## 7. Real-Test-Ergebnis

**Status: Noch ausstehend.**

Der Production-Deploy auf dem Hetzner-Server ist Stefans Aufgabe. Nach Deploy sind folgende manuelle Verifikationsschritte empfohlen:

1. **Migration prüfen:** `AdvisorProfiles`- und `AdvisorConsultations`-Tabellen sowie `AdvisorRetryAttempted`-Spalte in `Runs` vorhanden.
2. **System-Advisor-Seeding:** `BriefingClarifierProfile` und `DevilsAdvocateProfile` in der DB oder korrekt aus `SystemCrew` geladen.
3. **UI-Smoke-Test:** `/crew/advisors` aufrufbar, neues Advisor-Profil anlegen mit Trigger `BeforeFirstExecution`.
4. **Run mit Advisor:** Template mit Advisor-Profil erstellen, Run starten, in RunDetail prüfen, dass `AdvisorConsultationsBlock` Consultations anzeigt.
5. **OnConvergenceFailure-Test:** Nur durchführbar mit einem Briefing, das garantiert zur Divergenz führt (z. B. widersprüchliche Anforderungen). Zu prüfen: `AdvisorRetryAttempted` wird gesetzt, zweiter Retry bleibt aus.

---

## 8. Beobachtungen

### SDK hat natives `IAdvisor` — Überraschung

Bei der SDK-Recherche wurde `Geef.Sdk.Advisors.IAdvisor` gefunden. Das war nicht im ursprünglichen Bau-Prompt erwähnt und hätte die Implementierung vereinfachen können. Die bewusste Entscheidung für den Atelier-eigenen Decorator (statt SDK-native) war architektonisch richtig, kostet aber etwas mehr Code. Für zukünftige PS-Schritte: SDK-Namespace `Geef.Sdk.Advisors` vollständig dokumentieren, bevor Advisor-Erweiterungen geplant werden.

### ConvergenceFailedException als sauberer Erkennungsmechanismus

Die explizite `ConvergenceFailedException` macht den `OnConvergenceFailure`-Trigger sauber implementierbar: ein einziger `catch`-Block im `RunOrchestratorService`, kein Flag-Polling, keine Ergebnis-Inspection. Das SDK-Design ist hier gut durchdacht.

### SystemPromptRoutingClient-Pattern in Tests

Tests für `ProfileBasedAdvisor` nutzen das `SystemPromptRoutingClient`-Pattern: ein Test-Double, das LLM-Calls anhand des System-Prompts routet und vorkonfigurierte Antworten liefert. Dieses Pattern hat sich bereits in PS-5/PS-6-Tests bewährt und wurde konsequent weitergeführt. Es ermöglicht deterministische Tests ohne Mock-Framework-Overhead.

### AdvisorRetryAttempted als DB-Marker statt In-Memory

Die Entscheidung, den Retry-Cap in der Datenbank zu persistieren (statt als Instanzvariable im BackgroundService), erweist sich als korrekt: der `RunOrchestratorService` läuft als BackgroundService und kann bei Container-Neustart neu instanziiert werden. Ein In-Memory-Zähler würde bei Neustart zurückgesetzt und den Endlos-Loop-Schutz umgehen.

---

## 9. Empfehlungen für nächste Steps

### Authority-Bias-Monitoring

Advisor-Consultation-Outputs sollten in einer späteren PS-Phase auf inhaltliche Eigenständigkeit überprüft werden: Wenn Executor-Outputs zu stark mit Advisor-Outputs korrelieren, ist das ein Signal für Authority-Bias. Einfache Heuristik: Cosine-Similarity zwischen Advisor-Output und Executor-Output berechnen (opt. via Embedding-API).

### Multi-Retry-Strategie

PS-7 implementiert einen Single-Retry-Cap (AC14). Für komplexe Briefings könnte ein konfigurierbarer Retry-Count sinnvoll sein (z. B. `MaxConvergenceRetries` pro CrewTemplate). Dies erfordert eine Migration (`MaxConvergenceRetries int DEFAULT 1` in `CrewTemplates`) und eine Anpassung von `TryConvergenceFailureRetryAsync`.

### Sokratische Advisors (aus GEEF-Vision)

Das GEEF-Konzeptpapier beschreibt "Sokratische Advisors", die den Executor nicht instruieren, sondern durch Fragen zur eigenen Schlussfolgerung führen. Diese Variante ist mit dem aktuellen `ProfileBasedAdvisor` bereits implementierbar (System-Prompt des Advisors formuliert Fragen statt Anweisungen). Ein System-Profil `SocraticAdvisorProfile` in `SystemCrew` wäre ein niedrigschwelliger nächster Schritt.

### OnConvergenceFailure-Real-Test

Der Production-Test für `OnConvergenceFailure` setzt ein Briefing voraus, das garantiert zur Divergenz führt. Empfehlung: ein synthetisches "Stress-Briefing" mit widersprüchlichen Anforderungen und einem kleinen `MaxIterations`-Wert in der CrewSpec erstellen. Diesen Test einmalig durchführen und das Ergebnis (Consultation-Output, AdvisorRetryAttempted-Flag) im Decisions-Log dokumentieren.

### Advisor-Output-Länge — Konfiguration härten

Aktuell ist `MaxTokens` für Advisors ein Profil-Feld ohne Upper-Bound-Validierung. Ein serverseitiger Cap (z. B. 2000 Tokens für Advisor-Profile) verhindert versehentliche Fehlkonfiguration. Umsetzbar in `CrewService.CreateAdvisorProfileAsync` als Validierungsregel.

---

## 10. Parallel-Step-Koordination

### Kontext

PS-7 lief parallel zu einem Bug-Fix-Step, der ebenfalls `RunOrchestratorService.cs` berührt (Fehlerbehandlung bei Provider-Fehlern / Timeouts).

### Orthogonalität der Änderungen

| Änderung | Pfad im RunOrchestratorService | Exception-Typ |
|---|---|---|
| PS-7: `TryConvergenceFailureRetryAsync` | Nach Pipeline-Aufruf, `catch (ConvergenceFailedException)` | `Geef.Sdk.Exceptions.ConvergenceFailedException` |
| Bug-Fix: Provider-Error-Handling | Nach Pipeline-Aufruf, `catch (Exception)` oder spezifischer Provider-Exception | Alle anderen Exceptions |

Die Pfade sind inhaltlich orthogonal: `ConvergenceFailedException` wird ausschließlich vom PS-7-Pfad behandelt, alle anderen Exceptions vom Bug-Fix-Pfad. Ein Merge-Konflikt ist bei gleichzeitiger Bearbeitung derselben Datei möglich, aber inhaltlich aufwandsarm zu lösen (die `catch`-Blöcke sind unabhängig).

### Empfohlene Merge-Reihenfolge

1. Bug-Fix-Step zuerst mergen (kleinerer Scope, weniger Berührungspunkte)
2. PS-7 danach mergen — bei Konflikt in `RunOrchestratorService.cs` den `catch (ConvergenceFailedException)`-Block des PS-7 nach dem Bug-Fix-`catch`-Block einfügen
3. `dotnet build` + `dotnet test` nach Merge bestätigen

---

*Bericht erstellt am 2026-05-13. Alle Codeangaben beziehen sich auf den Stand nach Abschluss von Iteration 12 (Testlauf + Verifikation). Production-Deploy steht noch aus.*
