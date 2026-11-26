# Architektur

*[English](02-architecture.md) · **Deutsch***

*Letzte Aktualisierung: 2026-11-26 (Verweis auf die Schalter der Auftragsfunktionen und die Seite `/how-it-works`; zuvor 2026-11-24, D-067: Etappe 0 Sicherheitsnetz — Warnungen am Lauf, `Runs.Outcome`, lautes Verhalten bei Start und Snapshot, Vorbereitungs-Zeitlimit, Live-Updates im Prozess und Hub nur für Angemeldete, Konfigurationsschalter, Ende-zu-Ende-Gerüst; zweite Prüfrunde: als umgesetzt beschrieben, `LiveRunNotifier`, `CircuitDataGate`, Schwärzen von Geheimnissen, Outcome-Tabelle und Variablen für den Snapshot-Test; zuvor 2026-11-22, D-066: Modellmatrix — Spalten `ModelSlot`/`ModelDomain`/`Domain` (Step46), Zuweisung beim Snapshot-Bau; zuvor 2026-08-04, D-064: dynamischer Modell-Katalog für CLI-Provider über den cli-proxy, dreiwertige Herkunft, Alias-Auflösung zum Snapshot-Bauzeitpunkt)*

## Schichtenbild

```
┌────────────────────────────────────────────────────────────────┐
│                    Frontends (zwei Adapter)                    │
│  ┌──────────────────────────────┐  ┌─────────────────────────┐ │
│  │  Web-UI (Blazor Server)      │  │  MCP-Server             │ │
│  │  - /new, /runs, /runs/{id}   │  │  - submit_request       │ │
│  │  - SignalR Live-Stream       │  │  - get_run_status       │ │
│  │  - Cookie-Auth               │  │  - get_run_result       │ │
│  └──────────────┬───────────────┘  └─────────────┬───────────┘ │
│                 │                                │             │
└─────────────────┼────────────────────────────────┼─────────────┘
                  │                                │
                  ▼                                ▼
┌────────────────────────────────────────────────────────────────┐
│              Application Service Layer  (IRunService)          │
│  - SubmitRunAsync(briefing, sources, options) -> RunId         │
│  - GetRunStatusAsync(runId) -> RunStatus                       │
│  - GetRunResultAsync(runId) -> Result                          │
│  - ListRunsAsync(filter) -> Summaries                          │
│  - CancelRunAsync(runId)                                       │
└──────────────────────────────┬─────────────────────────────────┘
                               │
                               ▼
┌────────────────────────────────────────────────────────────────┐
│                     Background Orchestrator                    │
│  - BackgroundService pollt Pending-Runs                        │
│  - Baut Geef-Pipeline aus Run-Konfiguration                    │
│  - Führt PipelineRunner.RunAsync() aus                         │
│  - Custom IGeefEventSink schreibt Events in DB + SignalR       │
└──────────────────────────────┬─────────────────────────────────┘
                               │
                               ▼
┌────────────────────────────────────────────────────────────────┐
│                       Geef SDK Pipeline                        │
│   Grounding → Execution → Evaluation (Loop) → Finalize         │
│                                                                 │
│   Provider-Implementierungen leben in Infrastructure:           │
│   - BriefingGroundingStep                                       │
│   - LlmExecutionStep      (Multi-Provider-fähig)                │
│   - LlmReviewer           (Multi-Provider-fähig, getaggt)       │
│   - MarkdownFinalizer                                           │
└──────────────────────────────┬─────────────────────────────────┘
                               │
                               ▼
┌────────────────────────────────────────────────────────────────┐
│                          Persistenz                            │
│   Postgres via EF Core                                         │
│   Tabellen: Runs, Iterations, Findings, Events                 │
└────────────────────────────────────────────────────────────────┘
```

## Solution-Struktur

```
Geef.Atelier.slnx
├── src/
│   ├── Geef.Atelier.Core/           // Domain-Records, Interfaces (IRunRepository,
│   │                                // IRunPersistenceService), Pipeline-Konfig-Records
│   ├── Geef.Atelier.Application/    // IRunService-Vertrag + RunService-Implementierung,
│   │                                // ApplicationServiceExtensions (AddAtelierApplication)
│   ├── Geef.Atelier.Infrastructure/ // EF Core, LLM-Clients (OpenAiCompatibleClient),
│   │                                // EventSink, Provider-Implementierungen, Repositories,
│   │                                // Finalizers/ (4 Executor-Implementierungen),
│   │                                // FormatConverters/ (Markdig, QuestPDF, OpenXml, PlaintextStripper)
│   ├── Geef.Atelier.Web/            // Blazor Server: UI + BackgroundService
│   │                                // (RunOrchestratorService), DI-Composition
│   └── Geef.Atelier.Mcp/            // Class Library: MCP-Tool-Definitionen,
   │                                // gehostet im Web-Projekt (shared DI, shared Container)
├── tests/
│   └── Geef.Atelier.Tests/          // xUnit; EndToEnd/ = Ende-zu-Ende-Gerüst (D-067)
└── tools/
    └── Eval/                        // Python-Messwerkzeug, blinder Paarvergleich (D-067)
```

**Begründung der Aufteilung:**

- **Core** ist LLM-frei und persistenz-frei — enthält nur Records, Interfaces, Domain-Logik. Damit testbar ohne Infrastruktur.
- **Infrastructure** kapselt alle externen Abhängigkeiten (Postgres, LLM-APIs, Geef SDK). Provider-Implementierungen leben hier, weil sie LLM-Clients und Repositories brauchen.
- **Web** hostet die UI, den `BackgroundService` und die `IRunService`-Implementierung. Letztere könnte später in ein eigenes Projekt wandern, ist aber im Skeleton hier am praktischsten.
- **Mcp** ist eine **Class Library** (kein eigener Host). Sie enthält alle MCP-Tool-Definitionen. Der MCP-Endpoint lebt im `Web`-Projekt (Pfad `/mcp`), das `Geef.Atelier.Mcp` referenziert und die Tools im selben DI-Container registriert. Vorteile: kein zweiter Host-Prozess, `IRunService` und alle Singletons (SignalR, DbContext) werden direkt geteilt, kein HTTP-Hop zwischen MCP und Application Layer.

## Datenmodell

Stand Mai 2026 umfasst das Schema **28 Tabellen**, hand-geschriebene Migrationen
`InitialCreate` + `Step06`/`Step09`–`Step30`. Gruppiert:

| Gruppe | Tabellen | Eingeführt |
|---|---|---|
| Run-Kern | `Runs`, `Iterations`, `Findings`, `Events` | InitialCreate |
| Crew/Profile | `ReviewerProfiles`, `ExecutorProfiles`, `CrewTemplates` | Step10 |
| Advisor | `AdvisorProfiles`, `AdvisorConsultations` | Step11 |
| Grounding | `GroundingProviderProfiles`, `GroundingConsultations` | Step13 |
| Vector-Store/RAG | `KnowledgeDocuments`, `KnowledgeDocumentChunks` | Step14 |
| Cost-Tracking | `IterationActorCosts` | Step16 |
| Template Studio | `TemplateStudioAnalyses` | Step17 |
| Multi-User | `Users` | Step20 |
| OAuth 2.1 | `OAuthClients`, `OAuthAuthorizationCodes`, `OAuthAccessTokens`, `OAuthRefreshTokens`, `OAuthAuditLog` | Step19 |
| Finalizer | `FinalizerProfiles`, `RunArtifacts`, `FinalizationActorCosts` | Step22 |
| Learning | `LearningEntries` | Step30 |

Die vier Run-Kern-Tabellen sind nachfolgend im Detail dokumentiert; die übrigen
Gruppen sind in den jeweiligen Feature-Abschnitten bzw. im [Decisions-Log](05-decisions-log_de.md)
(D-028 ff.) beschrieben. `Runs` trägt zusätzlich Spalten aus späteren Migrationen
(`CreatedByUser`, `CostTotal`, `CrewTemplateName`, `CrewSnapshot`, `AdvisorRetryAttempted`,
`FinalizerCostEur`, `FinalizerErrorMessage`, `ParentRunId`, `SeedDraftText`, `Kind`, `ParentCompositionRunId`,
`Outcome`, `DeliveredIteration`, `ReportJson`, `OrderId`, `OrderVersion`). Migration Step47 (D-067) ergänzt die
Tabelle `RunWarnings` und die Spalten `Outcome`, `DeliveredIteration` und `ReportJson`; Step48 (D-069) ergänzt
`OrderId` und `OrderVersion`.

### Runs

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | uuid (PK) | |
| CreatedAt | timestamptz | |
| StartedAt | timestamptz | nullable |
| CompletedAt | timestamptz | nullable |
| Status | varchar(50) | Pending / Running / Completed / Failed / Aborted |
| BriefingText | text | |
| ConfigJson | jsonb | Modell-Auswahl, Budget — als Snapshot bei Erstellung |
| FinalText | text | nullable, gesetzt wenn Status=Completed |
| ErrorMessage | text | nullable, gesetzt wenn Status=Failed |
| TokensTotal | int | accumuliert über alle LLM-Calls |
| CostTotal | numeric(10,4) | accumuliert |
| CancellationRequested | bool | true wenn User den Run abbrechen möchte |
| CrewTemplateName | varchar(100) | nullable; Name des Templates (z.B. `"klassik"`). Null = Custom-Crew-Submit. |
| CrewSnapshot | jsonb | nullable; vollständig eingebetteter CrewSnapshot zum Zeitpunkt des Submits. |
| AdvisorRetryAttempted | bool | nullable; true wenn OnConvergenceFailure-Retry bereits durchgeführt wurde (Single-Retry-Cap). |
| CreatedByUser | text | nullable; Username des erstellenden Nutzers (Run-User-Isolation, D-042). Index `IX_Runs_CreatedByUser` (Step21). |
| FinalizerCostEur | numeric(10,6) | nullable; akkumulierte Kosten aller Transform-Finalizer-LLM-Calls (Step22). |
| FinalizerErrorMessage | text | nullable; Fehlermeldung, wenn die Finalizer-Kette teilweise oder vollständig fehlschlug (Step22). |
| ParentRunId | uuid (FK→Runs) | nullable; selbstreferenziell, kein Cascade — gesetzt, wenn dieser Run aus einem anderen Run fortgesetzt wurde (Step23). |
| SeedDraftText | text | nullable; letzter Artifact-Text aus der Abschluss-Iteration des Parent-Runs, der als Seed für Iteration 1 dieses Runs verwendet wurde (Step23). |
| Kind | int | `0 = Standard` (Standard), `1 = Learning` — steuert die beiden Learning-Finalizer; bestehende Rows defaulten auf Standard (Step30). |
| Outcome | int | nullable; was der Lauf geliefert hat: `0 = Converged`, `1 = DeliveredWithOpenPoints`, `2 = Failed`, `3 = Aborted` (Step47). `NULL`, solange der Lauf aktiv ist, und bei Läufen, die vor Step47 endeten. Siehe [Lauf-Ergebnis](#lauf-ergebnis-outcome-und-runstatus). |
| DeliveredIteration | int | nullable; Iteration, deren Text ausgeliefert wurde (Step47). |
| ReportJson | jsonb | nullable; Ergebnisbericht des Direktwegs (`RunReport`, Schema 1: Vorgaben, Prüfergebnisse des ausgelieferten Textes, Platzhalter, genutzte Dateien, Lesebeleg, Vermerke), geschrieben vor dem Ausgang (Step47). `NULL` bei Läufen der anderen Wege. |
| OrderId | uuid (FK→Orders) | nullable; Auftrag, zu dem der Lauf gehört, `ON DELETE CASCADE` (Step48). `NULL` bei Läufen ohne Auftrag. Index `IX_Runs_OrderId_OrderVersion`. |
| OrderVersion | int | nullable; Fassung des Auftrags, die der Lauf liefert (Step48). Je Auftrag eindeutig für Standardläufe (`UX_Runs_OrderId_OrderVersion_Standard`, `Kind = 0`). |

### RunWarnings (Step47)

Probleme, die ein Lauf umgangen hat, statt anzuhalten (D-067). Werden mit dem Lauf gelöscht (`ON DELETE CASCADE`).

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | uuid (PK) | |
| RunId | uuid (FK→Runs) | Index `IX_RunWarnings_RunId` |
| Iteration | int | nullable; Iteration, in der das Problem auftrat |
| Source | varchar(100) | `grounding`, `review`, `model-assignment`, `llm`, `finalizer` oder `run` (Konstanten in `RunWarningSources`) |
| Code | varchar(100) | stabiler maschinenlesbarer Code, z. B. `grounding.provider_skipped`, `grounding.scope_unknown`, `review.reviewer_failed`, `llm.failover`, `finalizer.failed`, `run.result_not_saved`, `run.restarted_while_finishing` (Konstanten in `RunWarningCodes`) |
| Message | text | kurze, einfache englische Erklärung für den Anwender |
| CreatedAt | timestamptz | |

### Iterations

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | uuid (PK) | |
| RunId | uuid (FK) | |
| IterationNumber | int | 1-basiert |
| ArtifactText | text | Snapshot des Textes nach dieser Iteration |
| CreatedAt | timestamptz | |

### Findings

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | uuid (PK) | |
| IterationId | uuid (FK) | |
| ReviewerName | varchar(200) | |
| Severity | enum | Critical / Major / Minor / Info |
| Message | text | |
| CreatedAt | timestamptz | |

### Events

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | bigint (PK, identity) | |
| RunId | uuid (FK) | |
| EventType | varchar(100) | aus Geef-EventSink-Vokabular |
| PayloadJson | jsonb | |
| CreatedAt | timestamptz | |

**Indices:**
- `Runs.Status` (für Background-Polling)
- `Events.RunId` (für Detail-View)
- `Iterations.RunId` (für Detail-View)

### FinalizerProfiles (Step22)

| Spalte | Typ | Bemerkung |
|---|---|---|
| Name | varchar(100) (PK) | |
| DisplayName | varchar(200) | |
| Description | text | nullable |
| FinalizerType | int | `0 = FileExport` / `1 = MetadataEnrich` / `2 = ExternalSink` / `3 = Transform` / `4 = LearningExtract` / `5 = LearningPublish` |
| Settings | jsonb | typspezifische Konfiguration |
| IsSystem | bool | true für eingebaute System-Profile |
| CreatedAt | timestamptz | |
| UpdatedAt | timestamptz | |

### RunArtifacts (Step22)

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | uuid (PK) | |
| RunId | uuid (FK→Runs ON DELETE CASCADE) | |
| FinalizerProfileName | varchar(100) | Name des Finalizer-Profils, das dieses Artifact erzeugt hat |
| ArtifactType | enum | `File` / `Url` / `Status` |
| Filename | text | nullable; Dateiname für File-Artifacts |
| ContentType | text | nullable; MIME-Typ für File-Artifacts |
| SizeBytes | bigint | nullable; Byte-Größe für File-Artifacts |
| StorageUri | text | Pfad oder URL zum gespeicherten Artifact |
| StatusMessage | text | nullable; lesbare Status- oder Fehlermeldung |
| CreatedAt | timestamptz | |

### FinalizationActorCosts (Step22)

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | uuid (PK) | |
| RunId | uuid (FK→Runs ON DELETE CASCADE) | |
| ActorName | varchar(200) | Name des Transform-Finalizer-Akteurs |
| ModelName | varchar(200) | verwendetes LLM-Modell |
| InputTokens | int | |
| OutputTokens | int | |
| CostEur | numeric(10,6) | nullable |
| CreatedAt | timestamptz | |

### Neue Spalten auf bestehenden Tabellen (Step22 — Finalizer-Foundation)

| Tabelle | Spalte | Typ | Bemerkung |
|---|---|---|---|
| `CrewTemplates` | `FinalizerProfileNames` | jsonb | geordnetes Array von Finalizer-Profil-Name-Strings |
| `CrewTemplates` | `RunFinalizersOnMaxAttempts` | bool | Standard false; wenn true, laufen Finalizer auch bei Convergence-Failure |

### Neue Spalten auf bestehenden Tabellen (Step23 — Run-Resume)

Siehe `Runs`-Tabelle oben (`ParentRunId`, `SeedDraftText`).

### FinalizerProfiles — Typwerte (Step22 + Step30)

| `FinalizerType`-Wert | Bedeutung | Eingeführt |
|---|---|---|
| `0` — `FileExport` | Schreibt den Abschlusstext in eine Datei im konfigurierten Format (md, html, pdf, docx, txt, json) | Step22 |
| `1` — `MetadataEnrich` | Reichert Run-Metadaten an (Tags, Titelextraktion, …) | Step22 |
| `2` — `ExternalSink` | Schiebt den Abschlusstext an einen externen Service-Endpunkt | Step22 |
| `3` — `Transform` | Wendet einen LLM-Transformations-Pass auf den Text an (Zusammenfassung, Übersetzung, …) | Step22 |
| `4` — `LearningExtract` | Extrahiert strukturierte Learnings aus einem abgeschlossenen Run und stößt fire-and-forget einen Learning-Evaluation-Gate-Run an (opt-in; an kein Standard-Template angehängt) | Step30 |
| `5` — `LearningPublish` | Veröffentlicht genehmigte Learning-Kandidaten im Learning-Store oder markiert abgelehnte. Läuft innerhalb der `learning-evaluation`-Crew | Step30 |

### LearningEntries (Step30)

Der Learning-Store. Physisch getrennt von der kuratierten Wissensbasis (`KnowledgeDocuments`) — ein konstitutiver Schutzmechanismus gegen Model Collapse.

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | uuid (PK) | |
| Text | text | Der kondensierte, gate-genehmigte Lerntext |
| SourceRunId | uuid (FK→Runs ON DELETE SET NULL) | Der Standard-Run, der die Extraktion ausgelöst hat |
| LearningRunId | uuid (FK→Runs ON DELETE SET NULL) | nullable; der Learning-Evaluation-Run, der diesen Eintrag genehmigt/abgelehnt hat |
| Domain | text | Template-Name des Quell-Runs — für Domänen-Boost-Retrieval |
| Status | int | `0 = Proposed`, `1 = Approved`, `2 = Rejected` |
| StructuredFactsJson | text | Maschinen-lesbares JSON der rohen Run-Fakten, vom Extraktor zusammengestellt |
| OwnerUsername | text | Geerbt vom `CreatedByUser` des Quell-Runs |
| CreatedAt | timestamptz | |
| ApprovedAt | timestamptz | nullable; gesetzt, wenn der Publisher den Eintrag als Approved markiert |
| Embedding | vector(1536) | nullable; berechnet von `IEmbeddingProvider` zum Genehmigungszeitpunkt; als pgvector gespeichert |

**Indizes:**
- `IX_LearningEntries_Domain_Status` — Composite-BTree für gefilterte Listen-Queries
- `IX_LearningEntries_Embedding_HNSW` — HNSW-Index mit `vector_cosine_ops` für Cosine-Similarity-Retrieval

**pgvector Raw-SQL-Pattern:** `Pgvector.EFCore` 0.3.0 ist mit Npgsql 10.x inkompatibel — ein string-Value-Converter führt beim INSERT zu `42804: column is of type vector but expression is of type character varying`. Die `Embedding`-Spalte wird daher **von EF Core ignoriert** (`builder.Ignore(e => e.Embedding)` in `LearningEntryConfiguration`); alle Insert- und Cosine-Search-Operationen nutzen raw ADO.NET mit `NpgsqlParameter(NpgsqlDbType.Vector)` — identisch mit dem `VectorSearchRepository`-Muster (D-036). Behoben in D-055 Folgefix 2.

### Neue Spalten auf bestehenden Tabellen (Step30 — Continuous Learning)

| Tabelle | Spalte | Typ | Bemerkung |
|---|---|---|---|
| `Runs` | `Kind` | int | `0 = Standard` (Standard), `1 = Learning` |

## Crew-System (PS-5)

Jeder Run verwendet eine **Crew** aus Executor + Reviewers. Profile sind wiederverwendbare Konfigurationsbausteine.

### Neue Tabellen (Migration Step10 + Step11)

| Tabelle | Migration | Inhalt |
|---|---|---|
| `ReviewerProfiles` | Step10 | Custom Reviewer-Profile (System-Profile leben als Code-Konstanten in `SystemCrew`). |
| `ExecutorProfiles` | Step10 | Custom Executor-Profile. |
| `CrewTemplates` | Step10 | Custom Crew-Templates. |
| `AdvisorProfiles` | Step11 | Custom Advisor-Profile. |
| `AdvisorConsultations` | Step11 | Persistierte Advisor-Outputs pro Iteration. |
| Spalten `ModelSlot`/`ModelDomain` (Executor/Reviewer/Advisor), `Domain` (CrewTemplates) | Step46 | Modellmatrix (D-066): Rolle und Gebiet; verwaltete Profile bekommen ihr Modell beim Snapshot-Bau aus `ModelMatrix` (`IModelAssignmentService`, Application). Nullable `varchar(40)`, toleranter Enum-Konverter. |

### ProfileBasedReviewer / ProfileBasedExecutor

Ersetzen die alten `LlmReviewer` / `LlmExecutionStep`. Verwenden `ILlmClientResolver.ForProfile(provider, model, maxTokens?)` statt Actor-basierter Auflösung.

### EvaluationStrategies

Alle vier Strategien via Geef-SDK: `Parallel`, `Sequential`, `FailFast`, `PriorityOrdered`.

Weitere Details: [`08-crew-system.md`](08-crew-system_de.md).

## Advisor-Pipeline-Schicht (PS-7)

Advisors werden als Decorator um `IExecutionStep` realisiert. Der `AdvisorAwareExecutor` schiebt sich transparent vor jeden Executor-Aufruf, ohne das Geef-SDK zu modifizieren (D-031(a)).

### Decorator-Kette

```
AtelierPipelineFactory
  └── AdvisorAwareExecutor (IExecutionStep-Decorator)
        1. Filtert Advisors nach Trigger (BeforeFirst / BeforeEvery)
        2. ProfileBasedAdvisor: LLM-Call (plain text), persistiert AdvisorConsultation
        3. Schreibt Output → context[AtelierContextKeys.AdvisorBlock]
        4. Delegiert an ProfileBasedExecutor (echter Execution-Step)
```

### AtelierContextKeys.AdvisorBlock

Der Advisor-Output landet als einzelner Text-Block im `IRunContext`. Format:

```
[ADVISOR: briefing-clarifier]
<Advisor-Output-Text>

[ADVISOR: devils-advocate]
<Advisor-Output-Text>
```

Executor-System-Prompt kann diesen Block explizit referenzieren. Mehrere Advisors akkumulieren sequenziell (D-031(d)).

### Convergence-Failure-Retry

```
ConvergenceFailedException
  → RunOrchestratorService.TryConvergenceFailureRetryAsync
      ├── RunEntity.AdvisorRetryAttempted == true → Status = Failed (kein zweiter Retry)
      └── AdvisorRetryAttempted = true → OnConvergenceFailure-Advisors aktiviert → Pipeline-Retry
```

`RunEntity.AdvisorRetryAttempted` (Migration Step11) ist der Single-Retry-Cap (D-031(e)).

### DB-Erweiterungen (Migration Step11AdvisorSystem)

| Neu | Inhalt |
|---|---|
| `AdvisorProfiles` | Custom Advisor-Profile |
| `AdvisorConsultations` | Persistierter Advisor-Output pro Iteration (RunId, IterationNumber, AdvisorName, OutputText) |
| `Runs.AdvisorRetryAttempted` | bool nullable — Retry-Cap-Flag |

Weitere Details: [`08-crew-system.md`](08-crew-system_de.md) → Sektion "Advisor-Pässe (PS-7)".

## Mapping auf GEEF-Provider (PS-7-Stand)

| GEEF-Phase | Provider-Implementierung | Verhalten |
|---|---|---|
| Grounding | `BriefingGroundingStep` | Schreibt das Briefing in den Context, keine externen Quellen |
| Pre-Execution | `AdvisorAwareExecutor` (Decorator) | Konsultiert BeforeFirst/BeforeEvery-Advisors; schreibt AdvisorBlock in Context |
| Execution | `ProfileBasedExecutor` | LLM-Call mit Profil-SystemPrompt + PreviousFindings + AdvisorBlock; Modell aus `ExecutorProfile` |
| Evaluation | `ProfileBasedReviewer` × N | N Reviewer aus `CrewSnapshot.Reviewers`; Strategie konfigurierbar |
| Finalize | `IFinalizerExecutor`-Kette | Läuft nach der Konvergenz. Iteriert `snapshot.Finalizers` der Reihe nach. Jeder `IFinalizerExecutor` erzeugt null oder ein `RunArtifact` (File, Url oder Status). Transform-Finalizer können `currentText` aktualisieren. `RunFinalizersOnMaxAttempts=true` lässt Finalizer auch bei Convergence-Failure laufen. Partial-Success-Vertrag: ein fehlschlagender Finalizer schreibt ein Status-Artifact und die Kette läuft weiter; der Run-Status bleibt Completed. Learning-Finalizer (`LearningExtract`, `LearningPublish`) tragen je einen Rekursions-Guard: Der Extraktor überspringt Learning-Runs, der Publisher überspringt Standard-Runs. |
| Convergence-Failure | `TryConvergenceFailureRetryAsync` | Aktiviert OnConvergenceFailure-Advisors, Single-Retry (AdvisorRetryAttempted-Cap) |

**Grounding-Provider-Typen (ProviderType-String):**

| `ProviderType` | Implementierung | Bemerkung |
|---|---|---|
| `tavily` | `TavilyGroundingProvider` | Tavily-Websuche |
| `vector-store` | `VectorStoreGroundingProvider` | Semantische Suche über kuratierte `KnowledgeDocuments` (pgvector) |
| `static-context` | `StaticContextGroundingProvider` | Verbatim-Fixtext; Style-Guides, Glossare |
| `url-fetch` | `UrlFetchGroundingProvider` | Fetcht eine URL mit SSRF-Guard |
| `news-search` | `TavilyNewsGroundingProvider` | Tavily-Newssuche mit Datumsfilter |
| `academic-search` | `AcademicSearchGroundingProvider` | arXiv / Semantic Scholar / OpenAlex-Adapter |
| `rest-api` | `RestApiGroundingProvider` | Generischer authentifizierter REST-Endpunkt mit JSONPath-Extraktion |
| `learning-retrieval` | `LearningRetrievalGroundingProvider` | Cosine-Similarity-Suche über `Approved`-`LearningEntries`; Domänen-Boost-Ranking (`finalScore = similarity × sameDomainBoost \| crossDomainPenalty`); Citations nutzen `learning://{id}`-Schema; in Templates nach kuratierten Wissens-Providern platzieren |

**Convergence-Policy:** `DefaultConvergencePolicy` aus `ConvergenceOptions`, überschreibbar per `ConvergencePolicyOverride` im CrewTemplate.

**Evaluation-Strategy:** `Parallel` (Standard). Alle vier Strategien wählbar per Template.

## LLM-Provider-Schicht (umgesetzt in Migration M1 und CLI-Provider-Split, D-017/D-032)

Die LLM-Schicht ist **OpenAI-API-konform** implementiert. Drei konfigurierte Provider (Stand CLI-Provider-Split):

| Provider-Name | Endpoint | Abrechnung |
|---|---|---|
| `openrouter` | `https://openrouter.ai/api/v1` | Pay-per-Token |
| `claude-cli` | `http://cli-proxy:8090/v1/claude` | Claude Subscription |
| `codex-cli`  | `http://cli-proxy:8090/v1/codex`  | Codex Subscription |

Der `cli-proxy`-Side-Container (FastAPI, Python) stellt zwei explizite Endpunkte bereit, die direkt an die jeweilige CLI routen — ohne Model-Name-Heuristik. Ein Legacy-Endpunkt `/v1/chat/completions` bleibt für Backward-Kompatibilität erhalten und loggt eine Deprecation-Warning.

### Abstraktion

```csharp
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}
```

`OpenAiCompatibleClient` ist die einzige Implementierung im Skeleton. Weitere OpenAI-kompatible Endpoints (OpenAI direkt, lokales Ollama, Together AI) sind durch Anpassen von `LlmOptions.Endpoint` ansprechbar — ohne Code-Änderung.

### Provider-Konfiguration und Modell-Wahl

> **Hinweis:** Das ursprüngliche flache `Llm.Actors`-Schema (ein fester Modell-Eintrag
> je Akteur in `appsettings.json`) ist seit dem Crew-System (D-028) abgelöst. Modell-
> und Provider-Wahl sind heute **datengetrieben** Teil der Reviewer-/Executor-/Advisor-
> **Profile** (siehe [`08-crew-system.md`](08-crew-system_de.md)), nicht der App-Konfiguration.

`appsettings.json` konfiguriert nur noch die **Provider-Endpunkte** (Multi-Provider,
D-027/D-032):

```json
{
  "Llm": {
    "Providers": {
      "openrouter": { "Endpoint": "https://openrouter.ai/api/v1", "ApiKey": "" },
      "claude-cli": { "Endpoint": "http://cli-proxy:8090/v1/claude", "ApiKey": "" },
      "codex-cli":  { "Endpoint": "http://cli-proxy:8090/v1/codex",  "ApiKey": "" }
    }
  }
}
```

API-Key-Override via Environment-Variable, z.B. `Llm__Providers__openrouter__ApiKey`
bzw. `LLM_OPENROUTER_API_KEY` (Env-Fallback). Welcher Akteur welchen Provider und
welches Modell nutzt, bestimmt das jeweilige Profil im `CrewSnapshot` des Runs
(`ILlmClientResolver.ForProfile`). Der Leitstern **Modell-Pluralismus** wird damit
pro Crew/Template ausgespielt: Reviewer laufen bewusst auf Fremd-Modellen relativ zum
Executor (Default-System-Crew: Executor `claude-cli`, Reviewer überwiegend `codex-cli`).

### Token-Tracking

`LlmTokenUsage` (`InputTokens`, `OutputTokens`) wird pro Iteration vom `ProfileBasedExecutor`/`ProfileBasedReviewer` in den `IRunContext` gesetzt und von `PostgresEventSink` in `Runs.TokensTotal` akkumuliert (Wire-Namen `prompt_tokens`/`completion_tokens` der OpenAI-API). Seit dem Cost-Tracking (Step16) werden zusätzlich pro Akteur und Iteration die Kosten in `IterationActorCosts` persistiert und in `Runs.CostTotal` aggregiert.

## Schutzmechanismen am Lauf (D-067)

Etappe 0 („Sicherheitsnetz“) macht Probleme sichtbar, statt sie still durchzulassen. Hintergrund:
[D-067](05-decisions-log_de.md).

### Lauf-Ergebnis (`Outcome`) und `RunStatus`

`Runs.Status` behält seine Bedeutung. Insbesondere endet ein Lauf, der ohne Konvergenz stoppt, weiterhin
als `Failed`, auch wenn er einen Text liefert (Statusentscheidung vom 2026-07-17: Best-Effort-Läufe
behalten `Failed`/`Aborted`). `Runs.Outcome` ist das zusätzliche Anzeigesignal dafür, was der Lauf
geliefert hat:

| `Outcome` | Bedeutung |
|---|---|
| `Converged` | Alle Reviewer haben zugestimmt; der konvergierte Text und die Ergebnisse der Finalizer sind gespeichert. |
| `DeliveredWithOpenPoints` | Der Lauf stoppte ohne volle Konvergenz und lieferte seinen besten verfügbaren Entwurf. Status `Failed` (nicht konvergiert) oder `Aborted`, wenn ein kritischer Reviewer-Befund einen Lauf stoppte, der schon einen Text hat; der kritische Befund bleibt offen und steht bei den Befunden. |
| `Failed` | Der Lauf endete ohne brauchbaren Text, oder sein Ergebnis konnte nicht vollständig gespeichert werden (Warnung `run.result_not_saved`). Ein Text kann trotzdem vorliegen; vollständig ist er dann nicht sicher. |
| `Aborted` | Vom Anwender abgebrochen oder vom Dienst gestoppt, auch während der Finalizer. Ein vor dem Stopp gespeicherter Text bleibt lesbar (`FinalText`). |

`Outcome` bedeutet **„fertig abgelegt, inklusive Finalizer“**: Es wird erst gesetzt, wenn der endgültige
Text (gegebenenfalls von einem Finalizer umgewandelt) in der Datenbank steht. Ein Client, der ein
`Outcome` sieht, kann also den endgültigen Text lesen. Der erste Wert gewinnt; spätere Fehler
überschreiben ihn nicht. `DeliveredIteration` nennt die Iteration, deren Text ausgeliefert wurde. Läufe,
die vor Step47 endeten, behalten `NULL`. Scheitert ein Lauf, werden auch die Befunde der letzten Iteration
gespeichert, sodass Detailseite und MCP zeigen, warum er stoppte.

`Status` und `Outcome` können kurz auseinanderliegen: Der Event-Sink setzt `Completed` (oder `Failed`) und
den Text vor den Finalizern, `Outcome` folgt erst danach. Clients warten deshalb auf `Outcome`, nicht auf
einen Endstatus; `scripts/atelier-smoke.sh` und der Arm `atelier` von `tools/Eval` tun das. Wird der Dienst
während der Finalizer hart beendet, setzt der Wiederanlauf beim nächsten Start für Läufe mit bereits
terminalem Status und leerem `Outcome` ein Ergebnis, sodass kein Client endlos wartet: mit Text `Converged`
(Status `Completed`) oder `DeliveredWithOpenPoints`, ohne Text `Failed`, dazu die Warnung
`run.restarted_while_finishing`. Weil ältere Läufe (vor Step47) ebenfalls kein `Outcome` haben, werden nur
Läufe repariert, die nach dem frühesten Lauf mit `Outcome` endeten; solange kein Lauf ein `Outcome` hat,
ändert sich nichts. Die Reparatur läuft nach dem Zurücksetzen hängengebliebener `Running`-Läufe und für sich;
ein Fehler darin verhindert dieses Zurücksetzen nie.

Ein Abbruch oder Dienststopp während der Finalizer endet mit `Outcome = Aborted`, ohne Warnungen „Finalizer
fehlgeschlagen“. Maßgeblich ist dabei `Outcome`: Der Status eines konvergierten Laufs bleibt `Completed`, ein
Lauf mit `Failed` (nicht konvergiert) wird `Aborted`. Die Ergebnisse der Finalizer, die vor dem Stopp fertig
waren, bleiben erhalten (geänderter Text, ihre Artefakte, Kosten); die übrigen Finalizer laufen nicht mehr.

Nur MCP liefert `Outcome` (`get_run_status`, `get_run_result`). Die Weboberfläche liest es nicht: Der Block
„Warnings“ auf der Detailseite (`RunWarningsBlock`) wählt seinen Einleitungssatz nach `Status` und danach,
ob ein Text vorliegt.

### Warnungen am Lauf

Ein Lauf hält jedes Problem, das er umgeht, als Warnung in `RunWarnings` fest, zum Beispiel:

- eine übersprungene oder fehlgeschlagene Grounding-Quelle (Anhänge, Websuche, Wissensbasis),
- einen Reviewer, der in einer Iteration ausfiel,
- ein Modell, das nicht wie geplant zugewiesen werden konnte,
- einen Wechsel zu einer anderen Modellfamilie im LLM-Client (Failover),
- einen Finalizer, der fehlschlug, übersprungen oder nicht ausgeführt wurde.

`IRunWarningSink` (Vertrag in Core, Implementierung `RunWarningSink` in Infrastructure) schreibt jede
Warnung in einem eigenen kurzlebigen Datenbank-Scope; ein fehlgeschlagener Schreibvorgang wird geloggt
und hält den Lauf nie an. Überall, wo sie gebraucht wird (Run-Service, Orchestrator, Pipeline-Fabrik,
Grounding-Schritt), ist sie eine Pflicht-Abhängigkeit; eine fehlende Registrierung scheitert also beim
Start, statt Warnungen still zu verlieren. Dasselbe gilt für die Grounding-Provider-Fabrik im Orchestrator;
die Pipeline-Fabrik baut eine Crew mit Quellen nicht, wenn keine Grounding-Provider-Fabrik übergeben wird. Der
LLM-Client meldet Failover über den ambienten
`RunWarningScope`, den der Orchestrator je Lauf öffnet.

Warnungen erscheinen auf der Detailseite im Block „Warnings“ (nur wenn vorhanden) und in den MCP-Feldern
`warnings` und `outcome` von `get_run_status` und `get_run_result`
([09-endpoint-reference](09-endpoint-reference_de.md#lauf-status-und-ergebnis-warnings-und-outcome)).
Warnungstexte und die am Lauf gespeicherten Fehlertexte (`ErrorMessage`, Statusmeldungen) sind feste,
kurze, einfache Sätze für den Anwender, bei Bedarf mit Anzeigenamen; technische Einzelheiten wie
Ausnahmetexte, Hostnamen, Schlüsselnamen oder Fehlertexte vorgelagerter Dienste gehen nur ins Log. Der Grund,
aus dem ein Lauf vor dem Bestehen aller Prüfungen endete, ist ein fester Satz je Abbruchgrund
(`RunStopMessageExtensions`); auch die Überspring-Gründe der Quellen-Sortierung (`GroundingRefiner`) sind feste
Sätze. Bevor
etwas Technisches geloggt oder gespeichert wird, ersetzt `SecretRedactionExtensions` (Core,
`RedactSecrets`, `ToRedactedLogText`) Bruchstücke, die nach Zugangsdaten aussehen (bekannte
Schlüsselformate, Bearer-Tokens, `name=wert`-Paare mit geheim klingendem Namen, lange undurchsichtige
Zeichenfolgen), durch `[redacted]`. Ein Name gilt als geheim klingend, wenn sein letzter Teil ein Wort wie
`key`, `token`, `secret` oder `password` ist (auch `OPENAI_API_KEY`, `x-api-key`, `apiKey`); sein Wert wird nur
entfernt, wenn er nach einem Geheimnis aussieht (mindestens 12 Zeichen ohne Leerzeichen mit Buchstabe und
Ziffer) oder ein URL-Parameter ist. Gewöhnliche Sätze wie „Key: Strategic goals“ oder „Basic research“ bleiben
unverändert.

### Laut statt still

| Situation | Verhalten seit D-067 |
|---|---|
| Eine Datenbank-Migration scheitert beim Start | Der Start bricht mit einem kritischen Logeintrag ab (`MigrateDatabaseOrFailAsync`). Der Health-Check `pending-migrations` meldet offene Migrationen. |
| Der Crew-Snapshot eines Laufs fehlt, ist unlesbar, leer, hat eine unbekannte Version oder keine Executor-, Reviewer- oder Advisor-Liste | `CrewSnapshot.DeserializeForRun` wirft `InvalidCrewSnapshotException`; der Lauf endet `Failed` mit verständlicher Meldung. Einen Rückfall auf die Klassik-Crew gibt es nicht mehr; das tolerante `Deserialize` dient nur der Anzeige. |
| Eine Crew hat keinen Reviewer | `AtelierPipelineFactory` baut die Pipeline nicht; der Lauf endet `Failed` mit Meldung. Der stille Rückfall `AutoApproveReviewer` ist entfernt. |
| Ein Grounding-Profil hat einen unbekannten Provider-Typ | Es wird mit einer Warnung übersprungen, statt den Lauf abstürzen zu lassen. |
| Ein Lauf ohne brauchbaren Crew-Snapshot wird fortgesetzt | `RunService.ResumeRunAsync` lehnt sofort mit `RunNotResumableException` ab (ein einfacher Satz für den Anwender), statt einen Lauf anzulegen, der bis zum Vorbereitungs-Zeitlimit wartet. Die Detailseite blendet „Resume“ für solche Läufe aus und zeigt die Meldung. |

### Vorbereitungs-Zeitlimit für Läufe mit Anhängen

Ein Lauf mit Anhängen wird zuerst **ohne** Crew-Snapshot angelegt; der Snapshot wird erst geschrieben, wenn
alle Anhänge hochgeladen und indiziert sind. Der Orchestrator holt nur `Pending`-Läufe **mit** Snapshot ab;
ein Lauf kann also nicht mehr vor seinen Anhängen starten. Ein `Pending`-Lauf, der nach
`Orchestrator:PreparationTimeout` (Standard 15 Minuten) noch keinen Snapshot hat, wird mit der Bitte um
erneutes Einreichen auf `Failed` gesetzt; ein Neustart des Dienstes räumt ebenso auf. Ein fortgesetzter
Lauf findet die Anhänge des Laufs, den er fortsetzt (Abstammung über `ParentRunId`/
`ParentCompositionRunId`). Ein Lauf ohne Anhänge löst keinen Embedding-Aufruf aus.

## Aufträge und Quellen (D-069, D-070)

| Tabelle (Step48) | Inhalt |
|---|---|
| `Orders` | Die Anfrage der Person im Wortlaut, Modus (`Direct`/`Careful`), Eingang (`Web`/`Mcp`/`Legacy`), Zustand (`Analyzing`, `NeedsInput`, `Ready`, `Cancelled`), Besitzer |
| `Sources` | Eine hochgeladene Datei: extrahierter Text mit Seitenmarken (`[[page N]]`), Zählwerte, Lesequalität, Warnungen, Pfad des aufbewahrten Originals, Ablaufzeit, solange kein Auftrag sie nutzt |
| `OrderSources` | Welche Quelle zu welchem Auftrag gehört, mit Rolle, Position und Seitenauswahl (`1-40,55`) |
| `RunSourceUsages` | Welche Datei in welchen Lauf ging, in welchem Modus (`FullText`, `FullTextWithMap`) und mit wie vielen Zeichen |

`Runs.OrderId`/`Runs.OrderVersion` verbinden einen Lauf mit seinem Auftrag. `OrderService` (Application) prüft und
legt einen Auftrag mit seinem ersten Lauf in einer Transaktion an; `OrderStateResolver` (Core) leitet den
Anzeigezustand nur aus `Orders.State` und dem aktuellen Lauf ab. Die Größengrenzen kommen aus
`SourceBudgetPlanner.Classify`, das Startseite, Auftragsdienst (`CheckSourcesAsync`, `SubmitAsync`) und der
Quellenblock des Schreibers (`SourceBlockRenderer`) teilen; `OrderSourcePrompts` baut die Quellen für Direktweg und
sorgfältigen Modus gleich.

Der Orchestrator wählt je Lauf einen `IPipelineAssembler` (Komposition 100, Direktweg 90, Fortsetzung 50,
Klassik 0). Der Direktweg (`DirectPipelineAssembler`) nutzt die Code-Vorlage `direct` (`SystemCrew.CodeOnly*`; die
Namen sind wie Systemnamen reserviert): `OrderGroundingStep`, `DirectWriterExecutor`,
`DeterministicChecksReviewer`, höchstens zwei Durchgänge, Bericht in `Runs.ReportJson`. Einzelheiten: D-069 und
D-070 im [Decisions-Log](05-decisions-log_de.md).

## Konfigurationsschalter (D-067)

| Schlüssel | Standard | Wirkung |
|---|---|---|
| `Learning:Enabled` | `false` | Schaltet die Learning-Schleife (D-054) ein oder aus. Ist sie aus, kehren die Finalizer `learning-extractor` und `learning-publisher` sofort zurück (kein LLM-Aufruf, kein Eintrag, kein `learning-evaluation`-Lauf, kein Embedding), das Grounding `learning-retrieval` liefert leeren Kontext ohne Embedding, und `/crew/learnings` zeigt, dass Learning pausiert ist. Crews mit diesen Teilen bleiben gültig. |
| `EnvReference:Allowed` | leere Liste | Zusätzliche Umgebungsvariablen-Namen oder Präfixe (mit `*` am Ende), die Konfiguration nennen darf, zusätzlich zur eingebauten Standardliste und zum Präfix `ATELIER_SECRET_*`. Einzelheiten: [10-tool-system](10-tool-system_de.md#secrets-und-die-allowlist-für-umgebungsvariablen). |
| `Orchestrator:PreparationTimeout` | `00:15:00` | Wie lange ein `Pending`-Lauf ohne Crew-Snapshot (Anhänge noch in Vorbereitung) bleiben darf, bevor er auf `Failed` gesetzt wird. |
| `Ui:WorkshopForAdminsOnly` | `true` | Solange an, sehen nur Admins die Einstiege zu den Werkstatt-Seiten: das Menü „Workshop“, den Link zum alten Formular auf der Startseite und „Technical details“ auf der Auftragsseite; die Kontoseiten führen alle anderen zurück zu „My work“. Seiten, Routen und Rechte bleiben unverändert (D-077, Etappe 8a). Rückweg: `Ui__WorkshopForAdminsOnly=false` in der Umgebung. |

Die Schalter der Auftragsfunktionen (Fragen, Ändern eines Textes, Recherche, Faktenprüfung, Upload-Ticket, ältere
MCP-Werkzeuge), ihre ausgelieferten Werte und ihre Ausnahmen je Person stehen im [Leitfaden für die
Administration](14-admin-guide_de.md#funktionen-schalten). Die öffentliche Seite `/how-it-works` liest dieselben Schalter und
Ausnahmen (`Web/Display/HowItWorksFeatures.cs`).

`Learning:Enabled` wird genau einmal registriert (`AddLearningSwitch`, Infrastructure); alle Leser
(Finalizer, Retrieval-Provider, Learnings-Seite) lesen den Schalter über `IOptionsMonitor`, eine Änderung
erreicht also alle gleich.

Fehlende Grounding-Schlüssel (`Tavily:ApiKey`, der OpenRouter-Schlüssel für Embeddings) werden beim Start
mit Namen geloggt (`GroundingKeyGuard`); Werte werden nie geloggt.

## Testen und Messen (D-067)

### Ende-zu-Ende-Gerüst (`tests/Geef.Atelier.Tests/EndToEnd/`)

`AtelierEndToEndHost` startet die **echte** Anwendung über `WebApplicationFactory<Program>` gegen eine
eigene Datenbank je Test auf dem Testcontainers-Postgres der `PostgresFixture`. Migration, DI-Verdrahtung,
Orchestrator, Grounding, Pipeline, Event-Sink und MCP-Endpunkt sind die der Produktion. Ersetzt wird nur
externes HTTP:

- `ScriptedLlmEndpoint` vertritt den cli-proxy (OpenAI-Format) und antwortet je Rolle (Executor, Reviewer,
  Refiner, Query-Extraktor); `LlmTrafficRecorder` zeichnet jeden Anfragekörper auf, auch die
  Dokumentmodus-Felder `document` und `context_document`.
- `FakeTavilyEndpoint`, `FakeEmbeddingsEndpoint` und `FakeCliProxyModelsEndpoint` liefern Websuche,
  deterministische Embeddings und die Modellliste.
- Alle übrigen benannten HTTP-Clients sind gesperrt und werden protokolliert; kein Test kann versehentlich
  ins Netz.

Hilfen: `SubmitAsync` (echter `IRunService`), `WaitForTerminalAsync` (wartet auf einen Endstatus mit
`Outcome`), `QueryAsync`, `CallMcpToolAsync` (echter `/mcp`-Endpunkt mit Bearer-Token).
`HeldOrchestratorStarter` startet den echten Orchestrator erst nach dem Einreichen.

**Was es beweist:** Ein Auftrag läuft über die echte Verdrahtung vom Einreichen (Web oder MCP) bis zum
abgelegten Ergebnis, und der Inhalt von Anhängen und Websuche kommt tatsächlich im Executor-Prompt an. Die
Tests in `PromptWiringEndToEndTests` schlagen fehl, wenn man die Grounding-Reparatur aus D-067 zurücknimmt;
das wurde durch Zurücknehmen geprüft. Sie sind gewöhnliche Regressionstests ohne eigenes Merkmal.

```bash
export MSBUILDNODEREUSE=0 DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet test tests/Geef.Atelier.Tests/Geef.Atelier.Tests.csproj --filter "FullyQualifiedName~Geef.Atelier.Tests.EndToEnd"
dotnet test tests/Geef.Atelier.Tests/Geef.Atelier.Tests.csproj --filter "FullyQualifiedName~PromptWiringEndToEndTests"
```

Die Tests brauchen Docker (Testcontainers) und laufen in einer eigenen Collection ohne Parallelität. Ein
neuer Pipeline-Weg bekommt hier seinen eigenen Ende-zu-Ende-Test, vom Einstieg bis zum abgelegten Ergebnis.

### Test gegen die Produktions-Snapshots

`ProductionSnapshotFixtureTests` liest jeden exportierten Crew-Snapshot der Produktion über das strenge
Lesen zur Laufzeit (`CrewSnapshot.DeserializeForRun`) und prüft Schema-Version, Reviewer und den
Grounding-Weg der Systemprofile. Die Exporte enthalten echte Aufträge und gehören **nie** ins Repository;
ohne die Variablen wird der Test übersprungen.

| Variable | Bedeutung |
|---|---|
| `ATELIER_SNAPSHOT_FIXTURES` | Verzeichnis mit einem exportierten Snapshot je Lauf (`*.json`). |
| `ATELIER_SNAPSHOT_FIXTURES_MIN` | Mindestzahl der Dateien im Export (Standard 1). Auf die Zahl der exportierten Läufe setzen, damit ein unvollständiger Export scheitert, statt still durchzugehen. |

Vor einem Deploy, der das Lesen der Snapshots berührt, exportiert der Betreiber die aktuellen Snapshots,
setzt beide Variablen und führt den Test aus (`--filter "FullyQualifiedName~ProductionSnapshotFixtureTests"`).
Verzeichnis und Anzahl stehen nicht im Repository.

### Messwerkzeug `tools/Eval`

Ein Python-Werkzeug, das das Atelier auf einem festen Aufgabenset per blindem Paarvergleich mit anderen
Wegen vergleicht (zum Beispiel einem rohen Einzelaufruf eines Modells). Aufruf, Umgebungsvariablen,
Aufgabenformat und Datenschutzregeln: [`tools/Eval/README.md`](../tools/Eval/README.md). Echte Aufträge
bleiben außerhalb von Git.

### Betriebsskripte

`scripts/atelier-smoke.sh` (Mini-Lauf über MCP nach jedem Deploy) und `scripts/atelier-watch.sh` (Alarm bei
hängenden Läufen und ungesundem cli-proxy) sind in [`ops/atelier-smoke.md`](ops/atelier-smoke.md) und
[`ops/atelier-watch.md`](ops/atelier-watch.md) beschrieben.

## UI-Architektur (Schritt 7)

### Pages

Drei Blazor Server Pages in `src/Geef.Atelier.Web/Components/Pages/`:

| Route | Komponente | Funktion |
|---|---|---|
| `/new` | `New.razor` | Submit-Formular. `EditForm` + `DataAnnotationsValidator`. Redirect zu `/runs/{id}` nach Submit. |
| `/runs` | `Runs.razor` | Run-Liste. Status-Filter via Query-Parameter. Live-Update über `IRunUpdateFeed.WatchRunList` (eigene Läufe; alle Läufe für Administratoren). |
| `/runs/{id}` | `RunDetail.razor` | Run-Detail. Live-Update über `IRunUpdateFeed.WatchRunAsync` (nur Besitzer oder Administrator). Cancel-Button für Pending/Running. Zeigt einen Block „Warnings“, wenn der Lauf Warnungen hat (D-067). |

### Live-Updates: `IRunUpdateFeed` und `RunHub` (D-067)

Die Blazor-Seiten (`Runs`, `RunDetail`, das Dashboard auf `Index`) **öffnen keine `HubConnection` mehr**.
Vor D-067 verbanden sie sich serverseitig mit der öffentlichen URL des eigenen Hubs, ohne das Cookie des
Nutzers, und wählten ihre Gruppennamen selbst. Jetzt abonnieren sie den prozessinternen
`IRunUpdateFeed` (`Web/Notifications/`, Implementierung `InProcessRunUpdateFeed`, registriert über
`AddRunLiveUpdates()`):

- Das Thema leitet der Server aus dem angemeldeten Nutzer ab (`RunUpdateTopics`); der Aufrufer wählt es
  nie. Ein Lauf-Thema bekommt nur der Besitzer des Laufs oder ein Administrator; Listen- und
  Dashboard-Thema enthalten die eigenen Läufe, für Administratoren alle.
- Jeder Abonnent hat eine eigene Warteschlange; Handler laufen nacheinander, gleiche wartende Meldungen
  werden zusammengefasst, und ein fehlerhafter Abonnent blockiert die anderen nicht. Der Publisher wartet
  nie auf Abonnenten.
- Über den Zugriff wird beim Abonnieren entschieden. Ein Abonnement gilt so lange wie der Circuit der
  Seite, und wie bei jeder Blazor-Server-Seite behält der Circuit die Anmeldung, mit der er geöffnet wurde;
  eine erneute Prüfung bei Ablauf der Anmeldung ist nicht gebaut (geringes Risiko: Das Lauf-Thema trägt nur
  eine ID, das Dashboard-Thema über alle Nutzer gibt es nur für Administratoren).
- Die Detailseite lädt direkt nach dem Abonnieren einmal neu, damit zwischen erstem Laden und Abonnement
  keine Meldung verloren geht.

**`CircuitDataGate`** (`Web/Components/`, je Circuit als Scoped registriert über `AddRunLiveUpdates()`):
Alle Komponenten eines Circuits teilen sich einen `DbContext`, der nur eine Operation gleichzeitig erlaubt.
Ein Nachladen durch ein Live-Update und eine Nutzeraktion (Filter, Löschen, Fortsetzen, Abbrechen) könnten
sich sonst überschneiden. Regel: **Jeder Datenbankzugriff einer Seite mit Live-Updates läuft über
`DataGate.RunAsync(...)`** (heute `Runs`, `RunDetail`, `Index` und `CancelButton`). Aufrufe dürfen **nicht
verschachtelt** werden: Eine Aktion im Tor ruft nie eine andere Aktion im Tor auf (das Tor ist ein
`SemaphoreSlim(1,1)` und würde auf sich selbst warten); Rückrufe wie `OnCancelled` oder ein Nachladen
laufen nach dem Block im Tor.

`RunHub` (`Web/Hubs/RunHub.cs`, über `MapRunHub()` auf `/hubs/runs` gemappt) hat in der Anwendung keinen
Client mehr, und ein externer Client ist nicht bekannt. Er bleibt **bis zum Rückbau Schnitt 2**, der auf seinen Auslöser in
[D-077](05-decisions-log_de.md#d-077--abschluss-des-umbaus-2026-11-26) wartet ([D-068](05-decisions-log_de.md#d-068--richtung-umbau-auftrag-direktweg-rückfragen-quellen-gedächtnis-2026-11-24))
und ist bis dahin abgesichert:

- **Nur für Angemeldete:** `[Authorize]` am Hub; `MapRunHub()` (`LiveUpdateServiceExtensions`) mappt ihn mit
  `RequireAuthorization()` und `CloseOnAuthenticationExpiration`. Anonyme Verbindungen werden abgewiesen,
  und eine Verbindung wird geschlossen, sobald die Anmeldung abläuft, mit der sie geöffnet wurde.
- **Serverseitig abgeleitete Gruppen:** Gruppennamen kommen aus `Context.User`, nie aus einem Parameter.
  Einer Lauf-Gruppe tritt nur bei, wem der Lauf gehört (oder ein Administrator); sonst wirft der Hub eine
  `HubException`. Die Listen- und Dashboard-Gruppen über alle Nutzer gibt es nur für Administratoren. Die
  früheren Methoden mit Benutzernamen als Parameter sind entfernt.

### `IRunNotifier` / `LiveRunNotifier`

`IRunNotifier` lebt in Core (`Core/Notifications/`). `PostgresEventSink` (Infrastructure) konsumiert den Vertrag — ohne Web-Dependency. `LiveRunNotifier` (`Web/Notifications/`) lebt in Web, Singleton-Lifetime, und veröffentlicht jede Meldung zweimal: in den `IRunUpdateFeed` für die Blazor-Seiten und in die `RunHub`-Gruppen (bis zum Rückbau des Hubs mit Schnitt 2, D-077). Den Besitzer des Laufs ermittelt er selbst (`IRunRepository`, zwischengespeichert) und schickt Listen-Updates nur an Besitzer und Administratoren, nie an alle. Notifier-Aufrufe sind best-effort (`try/catch`).

**Sequenz User-Submit → Live-View:**

```
Browser /new  →  IRunService.SubmitRunAsync  →  RunEntity (Pending) in DB
                                                ↓
                                      RunOrchestratorService (BackgroundService)
                                        pollt Pending-Läufe mit Snapshot, setzt Running-Claim
                                                ↓
                                      Geef-SDK-Pipeline (Grounding → Execution → Evaluation → Finalize)
                                                ↓
                                      PostgresEventSink
                                        (a) schreibt Event in DB
                                        (b) IRunNotifier.NotifyRunUpdatedAsync
                                                ↓
                                      LiveRunNotifier
                                        → IRunUpdateFeed: Lauf-Thema, Listen-Thema des Besitzers, Admin-Listen-Thema
                                        → RunHub-Gruppen (bis zum Rückbau, D-077)
                                                ↓
                                      Seiten-Callback (WatchRunAsync / WatchRunList)
                                        → IRunService.GetRunAsync → StateHasChanged
```

### UI-Komponenten-Library (`Components/UI/`)

9 Komponenten, alle mit scoped `.razor.css`:
`StatusBadge`, `SeverityBadge`, `RunCard`, `IterationPanel`, `FindingItem`, `RunHeader`, `SubmitForm`, `EmptyState`, `CancelButton`.

**Workflow-Regel:** Semantische UI-Elemente (Buttons, Forms, Badges, Listen) sind Komponenten. Layout-`div`-Tags in Pages erlaubt.

### PS-6 Crew-Verwaltungs-Pages

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
| `/crew/learnings` | `LearningsIndex` | Learning-Einträge aus dem Learning-Store auflisten, filtern, genehmigen, ablehnen und löschen (D-054) |

Neue UI-Komponenten (PS-6): `CrewBadge`, `CrewSelector`, `CrewSummary`, `ReviewerPicker`, `ProfileEditorForm`, `Modal`, `DeleteConfirmationModal`.

## Frontend-Stack-Entscheidung

**Blazor Server.** Begründung: derselbe .NET-Stack wie Geef SDK, kein Kontextwechsel; SignalR ist eingebaut und beliefert den Live-Status quasi gratis; Single-User heißt keine Skalierungs-Sorgen; lokale UI-Latenz ist dank Server-Hosting und Reverse-Proxy unkritisch. Falls später ein Wechsel zu Blazor WebAssembly oder React+API nötig wird, bleibt das Backend (`IRunService`, MCP-Server, Pipeline) unverändert.

## Auth-Strategie (umgesetzt in Schritt 8, siehe D-021)

### Web-UI — Cookie-Auth

> **Multi-User seit Step20 (D-041-Umfeld):** Ursprünglich Single-User aus
> Environment-Variablen; inzwischen **DB-basierte Mehrbenutzerverwaltung**
> (Tabelle `Users`, BCrypt). Der Admin-Account wird beim Start aus
> `ATELIER_USER`/`ATELIER_PASSWORD_HASH` geseedet/synchronisiert; weitere
> Konten verwaltet der Admin unter `/admin/users` (`IUserAdminService`).
> `IUserAuthenticator` liefert seitdem ein `AtelierUser?` (statt nur `bool`).
> Die Cookie-Konfiguration unten gilt unverändert.

BCrypt-Hash (work factor 11) wird via `tools/HashPassword/` erzeugt.

**Cookie-Konfiguration:**

| Option | Wert |
|---|---|
| Cookie-Name | `Atelier.Auth` |
| `HttpOnly` | `true` |
| `SameSite` | `Strict` (Produktion) / `Lax` (Test-Env) |
| `SecurePolicy` | `SameAsRequest` (Dev) / `Always` (Prod) |
| `ExpireTimeSpan` | 30 Tage |
| `SlidingExpiration` | `true` |
| `LoginPath` | `/login` |

**Login-Flow (Static SSR):**

```
Anonymer Browser → /runs → [Authorize] → RedirectToLogin
  → NavigationManager.NavigateTo("/login?ReturnUrl=%2Fruns")
  → Login.razor (Static SSR, kein @rendermode)
  → POST /login (Blazor Static SSR Form-Handler, @formname="login-form")
  → IUserAuthenticator.ValidateCredentialsAsync (BCrypt.Verify)
  → HttpContext.SignInAsync → Cookie gesetzt → Redirect zu /runs
```

**Wichtig: Login-Page muss Static SSR bleiben.** `@rendermode InteractiveServer` würde die Form-POST im WebSocket-Kontext abwickeln ohne `HttpContext` → `SignInAsync` wäre nicht aufrufbar. Das `@formname="login-form"`-Attribut auf dem `<form>`-Element ist Pflicht für Blazor Static SSR Form-Routing.

**Logout:** `POST /auth/logout` (Minimal API) mit `<AntiforgeryToken />` in der `UserMenu`-Komponente. GET-Logout wäre ein CSRF-Angriffspunkt.

**`IUserAuthenticator`-Schicht:**

```
Geef.Atelier.Core/Configuration/AtelierUserOptions.cs   → POCO für Username/PasswordHash
Geef.Atelier.Application/Auth/IUserAuthenticator.cs     → Interface (Application, nicht Infrastructure)
Geef.Atelier.Application/Auth/AtelierUserAuthenticator.cs → BCrypt.Verify + CryptographicOperations.FixedTimeEquals
Geef.Atelier.Application/Auth/ApplicationAuthExtensions.cs → AddAtelierAuth(IServiceCollection, IConfiguration)
```

`AtelierUserAuthenticator` ist `internal sealed`. Env-Var-Fallback (`ATELIER_USER`/`ATELIER_PASSWORD_HASH`) wird in `ApplicationAuthExtensions` aufgelöst — docker-compose-User müssen nicht die ASP.NET-Core-Doppelunderstrich-Konvention kennen.

**Timing-Schutz:** `FixedTimeEquals` für Username-Vergleich, `BCrypt.Verify` aufgerufen auch bei falschem Username (konstante Timing-Eigenschaft). Kein Username/Password-Hash wird in Logs geschrieben — nur `"Login attempt rejected"` (ohne PII).

**Lazy-Fail bei fehlender Konfiguration:** Service startet auch ohne Env-Vars, Login gibt `false` zurück. Health-Check bleibt anonym (`.AllowAnonymous()` auf `MapHealthChecks`). Init-Warning-Log beim ersten fehlkonfigurierten Login-Versuch.

**`ForwardedHeaders` vor `UseAuthentication`:**

```csharp
app.UseForwardedHeaders();   // ZUERST — damit Request.IsHttps korrekt ist
app.UseAuthentication();
app.UseAuthorization();
```

Traefik terminiert TLS und leitet HTTP weiter. Ohne `UseForwardedHeaders` würde `SecurePolicy.Always` in Produktion Cookies blockieren. `KnownIPNetworks.Clear()` öffnet für alle Proxy-IPs (Docker-Netzwerk-invariant).

**RunHub verlangt Anmeldung (seit D-067):**

Bis D-067 hatte `RunHub` kein `[Authorize]`, weil sich die Blazor-Seiten serverseitig ohne Browser-Cookie mit
ihm verbanden. Die Seiten nutzen jetzt den prozessinternen `IRunUpdateFeed`, deshalb verlangt der Hub eine
Anmeldung (`[Authorize]`, `MapRunHub()` mit `RequireAuthorization()` und `CloseOnAuthenticationExpiration`);
siehe [Live-Updates](#live-updates-irunupdatefeed-und-runhub-d-067).

### Test-Auth-Bypass

`TestAuthenticationHandler` (in `tests/Geef.Atelier.Tests/Web/E2E/`, `internal sealed`) markiert jeden Request als pre-authenticated mit `ClaimTypes.Name = "test-user"`. `WebTestHost.StartAsync(authenticated: true/false)` — `true` aktiviert den Test-Handler, `false` startet echte Cookie-Auth mit BCrypt-wf=4-Hash für LoginFlow/LogoutFlow-Tests. **Der Handler darf nie in `Program.cs` oder dem Web-Projekt referenziert werden.**

### MCP-Server — Bearer-Token / Multi-Auth (umgesetzt in Schritt 9, siehe D-022)

**Multi-Auth-Schema-Setup:** Die Anwendung nutzt zwei parallele Authentication Schemes.

| Scheme | Name | Zweck |
|---|---|---|
| Cookie | `CookieAuthenticationDefaults.AuthenticationScheme` | Web-UI, Default-Scheme |
| Bearer | `"Bearer"` | MCP-Endpoint `/mcp`, explizit via `McpPolicy` |

**Default-Scheme:** Cookie (alle Blazor-Routen, `[Authorize]` ohne Argument).

**MCP-Endpoint:** Ist explizit mit `RequireAuthorization("McpPolicy")` geschützt. Die `McpPolicy` setzt das Authentication-Scheme auf `"Bearer"`, sodass der MCP-Pfad nie Cookie-Auth versucht.

**`ITokenValidator` / `BearerTokenHandler` (Stand nach D-041 OAuth 2.1):**

`ITokenValidator.ValidateTokenAsync` liefert seit D-041 ein reiches Ergebnis
`TokenValidationOutcome { IsValid, Kind, Subject, ClientId, Scope }` (nicht mehr nur `bool`).

```
Geef.Atelier.Application/Auth/ITokenValidator.cs           → Interface (Application Layer)
Geef.Atelier.Application/Auth/StaticTokenValidator.cs      → Statisches ATELIER_MCP_TOKEN
                                                              (FixedTimeEquals); Kind="static-bearer"
Geef.Atelier.Application/Auth/OAuthAccessTokenValidator.cs → OAuth-Access-Token via DB-Lookup
                                                              (SHA-256-Hash); Subject = OAuth-Nutzer
Geef.Atelier.Application/Auth/CompositeTokenValidator.cs   → registriert als ITokenValidator:
                                                              prüft statisch, dann OAuth
Geef.Atelier.Web/Auth/BearerTokenHandler.cs                → AuthenticationHandler; baut Claims
                                                              aus dem Outcome (Name/NameIdentifier/Role)
```

`BearerTokenHandler` mappt das Outcome auf Claims: `ClaimTypes.Name` ← `Subject`,
`ClaimTypes.NameIdentifier` ← `ClientId ?? Subject`, und für statisches Bearer-Token
`ClaimTypes.Role = "admin"`. Damit greift die Run-User-Isolation (D-042) auch über MCP:
OAuth-Runs gehören dem autorisierenden Nutzer, statische-Token-Runs dem Admin.
`ICurrentUserService`/`HttpContextCurrentUserService` exponieren `Username`/`IsAdmin`
für Service- und MCP-Schicht.

**OAuth 2.1 ist seit D-041 vollständig implementiert** (kein „nach dem Skeleton“ mehr):
self-hosted Authorization Server mit Pflicht-PKCE/S256, Opaque-Tokens (nur SHA-256 in DB),
Refresh-Rotation + Reuse-Detection. Endpunkt- und Flow-Details siehe
[`04-mcp-integration.md`](04-mcp-integration_de.md) und [`09-endpoint-reference.md`](09-endpoint-reference_de.md);
Begründungen im [Decisions-Log](05-decisions-log_de.md) D-041. Beide Auth-Pfade
(statisches Bearer-Token für Claude Code CLI, OAuth 2.1 für Claude Desktop/Claude.ai)
koexistieren ohne Konfigurationsänderung.

## Production-Deployment

### Traefik-Flow

```
Browser → HTTPS:443 → Traefik (TLS via Let's Encrypt, cert-resolver 'le') → HTTP:8080 → geef-atelier-web container → ASP.NET
```

Traefik terminiert TLS und leitet HTTP (Port 8080) an den Container weiter. Der Container selbst exponiert keinen Port nach außen (`ports:` entfällt im Production-Compose).

### TLS und Traefik-Konfiguration

| Parameter | Wert |
|---|---|
| Externes Netzwerk | `proxy` (Server-Konvention) |
| Cert-Resolver | `le` (HTTP-Challenge via `web`-Entry-Point) |
| Entry-Point (HTTPS) | `websecure` |
| HTTP→HTTPS-Redirect | Global in `traefik.yml` (kein App-seitiger Redirect-Router) |
| Middleware-Chain | `chain@file` (secure-headers + compression + rate-limit, Server-Konvention) |
| `traefik.docker.network` | `proxy` (Pflicht wenn Container in mehreren Netzwerken) |

### Cookie-SecurePolicy in Production

`ASPNETCORE_ENVIRONMENT=Production` aktiviert `CookieSecurePolicy.Always`. Die `ForwardedHeaders`-Middleware (vor `UseAuthentication` gesetzt, siehe Auth-Strategie-Abschnitt) liest `X-Forwarded-Proto=https` von Traefik und stellt sicher, dass `Request.IsHttps == true` — ohne dies würde `SecurePolicy.Always` Cookies blockieren, weil der Container HTTP sieht, nicht HTTPS.

### Multi-Auth über HTTPS

Cookie-Auth für die Web-UI und Bearer-Auth für den MCP-Endpoint (`/mcp`) funktionieren identisch über HTTPS: TLS wird bei Traefik terminiert, der Container empfängt HTTP. Die Auth-Schicht der Anwendung sieht keinen Unterschied zum Dev-Betrieb — lediglich `SecurePolicy.Always` und `SameSite=Strict` sind in Production aktiv.

### Postgres-Strategie (Server-Konvention)

Jede Anwendung auf diesem Server betreibt einen eigenen Postgres-Container im selben Compose-File (`own-Postgres-per-App`-Pattern). Kein geteilter Datenbank-Host — Isolation und einfaches Backup pro App.

### Backup-Strategie (Post-Skeleton Schritt 1)

Der `postgres-backup`-Service (`prodrigestivill/postgres-backup-local:16`) läuft als dritter Container im Production-Stack:

- **Schedule:** täglich 03:00 UTC (`0 3 * * *`)
- **Retention:** 7 Tages-, 4 Wochen-, 6 Monats-Snapshots
- **Volume:** `geef-atelier-backups` (Named Volume, unabhängig vom DB-Volume)
- **Format:** `.sql.gz` (gzip-komprimiertes pg_dump SQL, Compression Level 6)
- **Restore:** `scripts/restore-backup.sh <datei.sql.gz>` (stoppt `web`, restored, startet `web` neu)

Kein App-Code-Eingriff — reiner Compose-Service. Backup-Container braucht nur das interne `geef-atelier-network` (kein `proxy`).

### Reviewer-Kalibrierung

Severity-Taxonomie, Anti-Pattern-Regel und Convergence-Policy-Strategie sind in [`docs/06-reviewer-calibration.md`](06-reviewer-calibration_de.md) beschrieben. Neue Reviewer-Rollen müssen den dort definierten Standard übernehmen.

### Deployment-Ablauf

1. `.env`-File generieren via `openssl rand` + `tools/HashPassword` (gitignored, nie ins Repo).
2. `docker build --no-cache -t geef-atelier .` im `build/`-Verzeichnis.
3. `docker compose -f docker-compose.prod.yml up -d` startet App + Postgres; Auto-Migration on Startup (D-010) läuft beim ersten Start.
4. Traefik erkennt den Container via Docker-Labels, stellt Let's-Encrypt-Zertifikat aus.
5. Nach jedem Deploy den Mini-Lauf `scripts/atelier-smoke.sh` ausführen ([`ops/atelier-smoke.md`](ops/atelier-smoke.md)); eine gescheiterte Migration hält den Start bereits an (D-067).

Das nächtliche Selbst-Update des cli-proxy (`scripts/cli-proxy-selfupdate.sh`) baut nur aus dem committeten Stand; bei nicht committeten Änderungen unter `cli-proxy/` setzt es aus und schickt einen Alarm, statt zu bauen (D-067).

### Modell-Katalog für CLI-Provider (D-064)

CLI-Provider führen ihre Modell-Liste nicht im Atelier. `ModelCatalog` liest sie über
`ICliModelSource` / `CliProxyModelSource` aus dem cli-proxy, und zwar über den einen
provider-gekeyten Endpunkt `GET /v1/cli/{providerName}/models`. Das Gateway meldet die Herkunft —
`x_source` ist `live`, `static` (keine Live-Quelle vorgesehen, z. B. gemini) oder `degraded`
(Live-Quelle vorhanden, aber nicht erreichbar) — und für claude zusätzlich eine `x_aliases`-Abbildung
von `claude-<familie>-latest` auf die konkrete ID, auf die der Alias gerade zeigt.

Daraus folgen drei Eigenschaften des umgebenden Codes:

* **Die Herkunft ist dreiwertig, kein Boolean.** `ModelCatalogSource` unterscheidet `Live`,
  `StaticByDesign` und `Fallback`, damit die Auswahl bei einem Provider ohne Live-Quelle wahrhaftig
  bleibt, statt dauerhaft „unerreichbar" zu behaupten.
* **Die Live-Liste wird mit der kuratierten vereinigt**, nie ersetzt, damit ein bevorzugtes
  Composer-Modell nicht lautlos aus genau dem Katalog fällt, gegen den es gefiltert wird.
* **Profile halten `*-latest`-Aliase, Run-Snapshots halten konkrete IDs.** Der Alias wird einmal pro
  Snapshot in `CrewService.ApplyPacksAsync` aufgelöst — neue Läufe folgen damit neuen Releases,
  während ein fortgesetzter Lauf auf dem Modell seines Elternlaufs bleibt.

Caching: 24 h nach einem erfolgreichen CLI-Abruf (deckungsgleich mit dem Cache des Gateways), 1 h für
HTTP-Provider, 5 Minuten nach jedem Fehlschlag. `ModelCatalogWarmUpService` wärmt kurz nach dem Start
und danach nächtlich jeden aktiven CLI-Provider; `ModelCatalogOptions.CliLiveCatalogEnabled` ist der
Not-Aus zurück auf die konfigurierten Listen, ohne Redeploy.

## Observability

Geef bringt vieles eingebaut mit:
- `IGeefEventSink` für strukturierte Events → Custom-Sink schreibt in DB und SignalR
- `System.Diagnostics.ActivitySource("Geef.Sdk")` für Distributed Tracing → kann später an OpenTelemetry-Collector angeschlossen werden
- Middleware-Pipeline für Cross-Cutting (Timeout, ExceptionHandling, Tracing) — alle eingebauten Middleware werden im Skeleton genutzt

Logging via `Microsoft.Extensions.Logging` mit Console-Sink im Skeleton; strukturierte Logs (Serilog mit Postgres-Sink) sind eine spätere Option.
