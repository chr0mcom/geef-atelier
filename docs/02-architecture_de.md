# Architektur

*[English](02-architecture.md) · **Deutsch***

*Letzte Aktualisierung: 2026-05-19 (Datenmodell, LLM-/Auth-Schicht und MCP-Auth auf aktuellen Stand gebracht: Crew-Profil-System, OAuth 2.1, Multi-User, Run-User-Isolation, Finalizer-Foundation Step22, Run-Resume Step23)*

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
└── tests/
    └── Geef.Atelier.Tests/          // xUnit
```

**Begründung der Aufteilung:**

- **Core** ist LLM-frei und persistenz-frei — enthält nur Records, Interfaces, Domain-Logik. Damit testbar ohne Infrastruktur.
- **Infrastructure** kapselt alle externen Abhängigkeiten (Postgres, LLM-APIs, Geef SDK). Provider-Implementierungen leben hier, weil sie LLM-Clients und Repositories brauchen.
- **Web** hostet die UI, den `BackgroundService` und die `IRunService`-Implementierung. Letztere könnte später in ein eigenes Projekt wandern, ist aber im Skeleton hier am praktischsten.
- **Mcp** ist eine **Class Library** (kein eigener Host). Sie enthält alle MCP-Tool-Definitionen. Der MCP-Endpoint lebt im `Web`-Projekt (Pfad `/mcp`), das `Geef.Atelier.Mcp` referenziert und die Tools im selben DI-Container registriert. Vorteile: kein zweiter Host-Prozess, `IRunService` und alle Singletons (SignalR, DbContext) werden direkt geteilt, kein HTTP-Hop zwischen MCP und Application Layer.

## Datenmodell

Stand Mai 2026 umfasst das Schema **27 Tabellen**, hand-geschriebene Migrationen
`InitialCreate` + `Step06`/`Step09`–`Step23`. Gruppiert:

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

Die vier Run-Kern-Tabellen sind nachfolgend im Detail dokumentiert; die übrigen
Gruppen sind in den jeweiligen Feature-Abschnitten bzw. im [Decisions-Log](05-decisions-log_de.md)
(D-028 ff.) beschrieben. `Runs` trägt zusätzlich Spalten aus späteren Migrationen
(`CreatedByUser`, `CostTotal`, `CrewTemplateName`, `CrewSnapshot`, `AdvisorRetryAttempted`,
`FinalizerCostEur`, `FinalizerErrorMessage`, `ParentRunId`, `SeedDraftText`).

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
| FinalizerType | varchar(50) | `FileExport` / `MetadataEnrich` / `ExternalSink` / `Transform` |
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
| Finalize | `IFinalizerExecutor`-Kette | Läuft nach der Konvergenz. Iteriert `snapshot.Finalizers` der Reihe nach. Jeder `IFinalizerExecutor` erzeugt null oder ein `RunArtifact` (File, Url oder Status). Transform-Finalizer können `currentText` aktualisieren. `RunFinalizersOnMaxAttempts=true` lässt Finalizer auch bei Convergence-Failure laufen. Partial-Success-Vertrag: ein fehlschlagender Finalizer schreibt ein Status-Artifact und die Kette läuft weiter; der Run-Status bleibt Completed. |
| Convergence-Failure | `TryConvergenceFailureRetryAsync` | Aktiviert OnConvergenceFailure-Advisors, Single-Retry (AdvisorRetryAttempted-Cap) |

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

## UI-Architektur (Schritt 7)

### Pages

Drei Blazor Server Pages in `src/Geef.Atelier.Web/Components/Pages/`:

| Route | Komponente | Funktion |
|---|---|---|
| `/new` | `New.razor` | Submit-Formular. `EditForm` + `DataAnnotationsValidator`. Redirect zu `/runs/{id}` nach Submit. |
| `/runs` | `Runs.razor` | Run-Liste. Status-Filter via Query-Parameter. `HubConnection` auf `all-runs`-Group. Live-Update via `AnyRunUpdated`-Event. |
| `/runs/{id}` | `RunDetail.razor` | Run-Detail. `HubConnection` auf `run-{id}`-Group. Live-Update via `RunUpdated`-Event. Cancel-Button für Pending/Running. |

### SignalR-Hub (`RunHub`)

`src/Geef.Atelier.Web/Hubs/RunHub.cs` — gemappt auf `/hubs/runs`.

Zwei Groups:
- `run-{runId}` — Detail-Page-Subscriber. Sendet `"RunUpdated"` nach jedem Persist-Event.
- `all-runs` — Runs-Listen-Page-Subscriber. Sendet `"AnyRunUpdated"` nach jedem Persist-Event.

Browser-Clients verwenden `HubConnectionBuilder.WithUrl("/hubs/runs").WithAutomaticReconnect()`. Reconnect-Handler re-joinst die Group. Pages implementieren `IAsyncDisposable` mit `Leave`-Aufruf + Hub-Dispose.

### `IRunNotifier` / `SignalRRunNotifier`

`IRunNotifier` lebt in Core (`Core/Notifications/`). `PostgresEventSink` (Infrastructure) konsumiert den Vertrag — ohne Web-Dependency. `SignalRRunNotifier` lebt in Web, injiziert `IHubContext<RunHub>`, Singleton-Lifetime. Notifier-Aufrufe sind best-effort (`try/catch`).

**Sequenz User-Submit → Live-View:**

```
Browser /new  →  IRunService.SubmitRunAsync  →  RunEntity (Pending) in DB
                                                ↓
                                      RunOrchestratorService (BackgroundService)
                                        pollt Pending, setzt Running-Claim
                                                ↓
                                      Geef-SDK-Pipeline (Grounding → Execution → Evaluation → Finalize)
                                                ↓
                                      PostgresEventSink
                                        (a) schreibt Event in DB
                                        (b) IRunNotifier.NotifyRunUpdatedAsync
                                                ↓
                                      SignalRRunNotifier → IHubContext<RunHub>
                                        → run-{id}-Group: "RunUpdated"
                                        → all-runs-Group: "AnyRunUpdated"
                                                ↓
                                      Browser HubConnection.On("RunUpdated")
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

**RunHub ohne `[Authorize]` (architektonischer Trade-off):**

`RunHub` hat kein `[Authorize]`-Attribut. Begründung: Blazor Server's `HubConnectionBuilder` erzeugt server-seitige SignalR-Verbindungen, die Browser-Cookies nicht weiterleiten. Mit `[Authorize]` auf dem Hub würde das SSR-Pre-Render-Phase 401 erhalten und Blazor Circuit-Initialisierung schlägt fehl. Mitigation: Alle subscribenden Pages (`/new`, `/runs`, `/runs/{id}`) tragen `@attribute [Authorize]` — unauthentifizierte User können die Pages nicht laden, also auch keine Hub-Verbindung aufbauen.

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

## Observability

Geef bringt vieles eingebaut mit:
- `IGeefEventSink` für strukturierte Events → Custom-Sink schreibt in DB und SignalR
- `System.Diagnostics.ActivitySource("Geef.Sdk")` für Distributed Tracing → kann später an OpenTelemetry-Collector angeschlossen werden
- Middleware-Pipeline für Cross-Cutting (Timeout, ExceptionHandling, Tracing) — alle eingebauten Middleware werden im Skeleton genutzt

Logging via `Microsoft.Extensions.Logging` mit Console-Sink im Skeleton; strukturierte Logs (Serilog mit Postgres-Sink) sind eine spätere Option.
