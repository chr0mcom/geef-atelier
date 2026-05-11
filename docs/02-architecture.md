# Architektur

*Letzte Aktualisierung: 2026-05-11 (S10: Production-Deploy-Sektion ergänzt)*

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
Geef.Atelier.sln
├── src/
│   ├── Geef.Atelier.Core/           // Domain-Records, Interfaces (IRunRepository,
│   │                                // IRunPersistenceService), Pipeline-Konfig-Records
│   ├── Geef.Atelier.Application/    // IRunService-Vertrag + RunService-Implementierung,
│   │                                // ApplicationServiceExtensions (AddAtelierApplication)
│   ├── Geef.Atelier.Infrastructure/ // EF Core, LLM-Clients (OpenAiCompatibleClient),
│   │                                // EventSink, Provider-Implementierungen, Repositories
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

## Datenmodell (Skeleton-Stand)

Vier Tabellen reichen am Anfang. Spätere Erweiterungen (Sources, AdvisorConsultations, AdvisorProvenance, ReviewerProfiles, CrewTemplates) kommen mit den entsprechenden Features.

### Runs

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | uuid (PK) | |
| CreatedAt | timestamptz | |
| StartedAt | timestamptz | nullable |
| CompletedAt | timestamptz | nullable |
| Status | enum | Pending / Running / Completed / Failed / Aborted |
| BriefingText | text | |
| ConfigJson | jsonb | Modell-Auswahl, Crew, Budget — als Snapshot bei Erstellung |
| FinalText | text | nullable, gesetzt wenn Status=Completed |
| ErrorMessage | text | nullable, gesetzt wenn Status=Failed |
| TokensTotal | int | accumuliert über alle LLM-Calls |
| CostTotal | numeric(10,4) | accumuliert |

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

## Mapping auf GEEF-Provider (Skeleton)

| GEEF-Phase | Provider-Implementierung | Skeleton-Verhalten |
|---|---|---|
| Grounding | `BriefingGroundingStep` | Schreibt das Briefing in den Context, keine externen Quellen |
| Execution | `LlmExecutionStep` | OpenAI-kompatibler LLM-Call (via OpenRouter) mit Briefing + PreviousFindings, gibt neuen Text zurück |
| Evaluation | `LlmReviewer` × 2 | Zwei Reviewer (konfigurierbar pro Akteur), parallel ausgeführt |
| Finalize | `MarkdownFinalizer` | Wrappt finalen Text in `FinalizedDocument`-Record |

**Convergence-Policy im Skeleton:** `MaxIterationsPolicy(3)` — drei Iterationen Maximum, danach abbrechen. Mehr ist im Skeleton Overkill.

**Evaluation-Strategy im Skeleton:** `ParallelEvaluationStrategy` — beide Reviewer laufen gleichzeitig.

## LLM-Provider-Schicht (umgesetzt in Migration M1, siehe D-017)

Die LLM-Schicht ist **OpenAI-API-konform** implementiert. Default-Endpoint: **OpenRouter** (`https://openrouter.ai/api/v1`).

### Abstraktion

```csharp
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}
```

`OpenAiCompatibleClient` ist die einzige Implementierung im Skeleton. Weitere OpenAI-kompatible Endpoints (OpenAI direkt, lokales Ollama, Together AI) sind durch Anpassen von `LlmOptions.Endpoint` ansprechbar — ohne Code-Änderung.

### Pro-Akteur-Modell-Konfiguration

Jeder Pipeline-Akteur (Executor, BriefingTreueReviewer, KlarheitReviewer) hat eine eigene Modell-Konfiguration in `appsettings.json`:

```json
{
  "Llm": {
    "Endpoint": "https://openrouter.ai/api/v1",
    "ApiKey": "",
    "DefaultModel": "anthropic/claude-opus-4.7",
    "Actors": {
      "Executor":              { "Model": "anthropic/claude-opus-4.7", "MaxTokens": 8192 },
      "BriefingTreueReviewer": { "Model": "anthropic/claude-opus-4.7", "MaxTokens": 2048 },
      "KlarheitReviewer":      { "Model": "anthropic/claude-opus-4.7", "MaxTokens": 2048 }
    }
  }
}
```

Der Leitstern **Modell-Pluralismus** ist damit konfigurativ sofort verfügbar: Executor kann ein anderes Modell nutzen als die Reviewer. Beispiel: `anthropic/claude-opus-4.7` für den Executor, `openai/gpt-5` + `google/gemini-2.5-pro` für die Reviewer. API-Key-Override via Environment-Variable: `Llm__ApiKey`.

### Token-Tracking

`LlmTokenUsage` (`InputTokens`, `OutputTokens`) wird pro Iteration vom `LlmExecutionStep` in `AtelierContextKeys.TokenUsage` gesetzt und von `PostgresEventSink` in `Runs.TokensTotal` akkumuliert. Property-Namen identisch zu Schritt 3; nur der Wire-Name ändert sich (`prompt_tokens`/`completion_tokens` in der OpenAI-API).

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

## Frontend-Stack-Entscheidung

**Blazor Server.** Begründung: derselbe .NET-Stack wie Geef SDK, kein Kontextwechsel; SignalR ist eingebaut und beliefert den Live-Status quasi gratis; Single-User heißt keine Skalierungs-Sorgen; lokale UI-Latenz ist dank Server-Hosting und Reverse-Proxy unkritisch. Falls später ein Wechsel zu Blazor WebAssembly oder React+API nötig wird, bleibt das Backend (`IRunService`, MCP-Server, Pipeline) unverändert.

## Auth-Strategie (umgesetzt in Schritt 8, siehe D-021)

### Web-UI — Cookie-Auth

Ein fester User aus Environment-Variablen. BCrypt-Hash (work factor 11) via `tools/HashPassword/`.

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

**`ITokenValidator` / `BearerTokenHandler`:**

```
Geef.Atelier.Application/Auth/ITokenValidator.cs      → Interface (Application Layer)
Geef.Atelier.Application/Auth/AtelierTokenValidator.cs → Implementierung: konstanter Zeitvergleich vs. ATELIER_MCP_TOKEN
Geef.Atelier.Web/Auth/BearerTokenHandler.cs           → AuthenticationHandler<AuthenticationSchemeOptions>
                                                         liest Authorization-Header, delegiert an ITokenValidator
```

`ITokenValidator` lebt in Application (ohne Web-Dependency). `BearerTokenHandler` lebt in Web und ist der einzige Ort mit ASP.NET-Core-Auth-Primitiven im Bearer-Pfad. Token wird aus `ATELIER_MCP_TOKEN` gelesen; fehlt die Variable, schlägt jede Bearer-Anfrage fehl.

OAuth-2.0-Flow ist im MCP-Standard vorgesehen — kommt nach dem Skeleton, wenn echter Multi-Client-Bedarf entsteht.

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