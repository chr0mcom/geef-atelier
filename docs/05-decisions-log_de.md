# Decisions Log

*[English](05-decisions-log.md) · **Deutsch***

*Letzte Aktualisierung: 2026-05-19 (D-046 ergänzt: Run-Resume)*

Chronologisches Protokoll aller Entscheidungen aus dem Brainstorming.

## 10. Mai 2026 — Erstes Brainstorming

### D-001 bis D-009 (kondensiert)
Use-Case generisch, Fire-and-Forget, Blazor Server, Postgres, MCP zweite Schnittstelle, Geef.Atelier, Walking Skeleton, kanonische `geef_workflow.md`.

### D-010: Schritt 1 — Solution-Setup
Geef.Sdk 1.0.0-ci.1, `.slnx`, UI-Component-Library, Auto-Migration mit try-catch.

### D-011: Architect-Workflow + Atelier-Fallback
(A) Phase 1.4 mit Fallback-Sequence. (B) Atelier-Level-4: Executor schreibt selbst.

### D-012: Schritt 2 — SDK-Realfakten
Sechs Korrekturen: SDK-`FindingSeverity { Info, Warning, Error, Critical }`; `DefaultConvergencePolicy`; `UseMiddleware<T>()`; nur `EvaluationApprovedEvent`/`RejectedEvent`; `IterationHistory`-Workaround; `using SdkGeef = Geef.Sdk.Geef;`.

### D-013: Schritt 3 — Anthropic-Client (durch M1 ersetzt)
Realfakten zur ursprünglichen Anthropic-Schicht. Konzepte (Tool-Use, defensive JSON, API-Key per Request, Resilience) bleiben in M1 gültig.

### D-014: Production-Domain für Schritt 10
`geef.stefan-bechtel.de`, IP `95.216.100.213`, Traefik mit TLS.

### D-015: Schritt 4 — EventSink und Persistierung
`IRunPersistenceService`, `PostgresEventSink` mit injizierter `Guid runId`, Severity-Mapping via `ToAtelierSeverity()`, Token-Tracking via Context-Key, Critical-Abort aus `PipelineFailedEvent.History` (SDK-Dekompilierung), `_lastExecutionContext` als `volatile`, `IServiceScopeFactory.CreateAsyncScope()` pro Event.

### D-016: Schritt 5 — RunOrchestratorService
Atomarer Pending→Running-Claim, `SemaphoreSlim` + `ConcurrentDictionary<Guid, Task>` + `WhenAll`-Drain, `OverrideToAbortedAsync` mit `CancellationToken.None`, `_runCts`-Dictionary für Cancellation-Reaktion. `OrchestratorOptions` in Core.

### D-017: Provider-Strategie-Wechsel (M1 Auslöser)
Wechsel von Anthropic-spezifisch auf OpenAI-API-konform via OpenRouter. Pro-Akteur-Modell-Mapping.

### D-018: Migration M1 abgeschlossen
Branch `feature/openai-compatible-providers`. `ILlmClient`, `OpenAiCompatibleClient`, `LlmOptions` mit Pro-Akteur-Mapping. ToolChoice als String-Convention, String-Keys für Actor-Lookup, kein `BaseAddress` am HttpClient, `anthropic-version`-Header entfernt. Workflow-Abweichung: keine formalen R1–R5-Pässe.

### D-019: Schritt 6 — IRunService Application-Layer + Cancellation
Variante β (eigenes Application-Projekt ohne Infrastructure-Dep, IRunRepository in Core). Cancellation-Watcher Pattern A (pro-Run-Task), DB-Flag `RunEntity.CancellationRequested` mit Migration `Step06Cancellation`. OCE-Catch-Filter-Reihenfolge: erst Service-Stop, dann User-Cancel. R2-Fixes: ServiceProvider-Disposal, Polling-Loop statt Task.Delay.

### D-020: Schritt 7 abgeschlossen — Blazor-UI, SignalR, AC8 grün

**Datum:** 11. Mai 2026
**Bericht:** [reports/step-07-report.md](reports/step-07-report.md)
**Branch:** `main` — M1+Schritt-6+Schritt-7 alle in main (Push-Range `28daafb..ad90f65`).
**Reviewer-Iterationen:** 2 (Iteration 1 mit 1 R2-CRITICAL, Iteration 2 grün).
**Tests:** 55/55 grün. **12 Conventional-Commits.**

**Architect-Konsultation:** Form: **Plan-Phase-Integration** (keine separate Architect-Invocation, alle Entscheidungen im Plan-Dokument fixiert). Etabliert sich als Standard-Form für Schritte 5–7. Antworten auf die sechs Schwerpunkte aus dem Step-7-Prompt:

| Schwerpunkt | Entscheidung |
|---|---|
| (F1) SignalR-Mechanik | Variante α (Browser-HubConnection) — MCP-konsistent, Playwright-testbar |
| (F2) Hub-Event-Granularität | Nur `RunId` ohne Payload — UI re-fetcht via `IRunService` |
| (F3) CSS-Strategie | Scoped `.razor.css` pro Komponente — konsistent mit `MainLayout.razor.css` und `ReconnectModal.razor.css` |
| (F4) Form-Validierung | `EditForm` + `DataAnnotationsValidator` (Standard-Blazor) |
| (F5) Test-Host-Setup | Hybrid: `WebApplicationFactory<Program>` + Kestrel auf Port 0 (für Playwright braucht echten HTTP-Listener) |
| (F6) Notifier-Schicht | `IRunNotifier` in Core, `SignalRRunNotifier` in Web als Singleton (Sink-Tests via `NoOpRunNotifier`, keine SignalR-Mocks) |

**Fixierte Realfakten aus Schritt 7 (verbindlich ab Schritt 8):**

**(a) `IRunNotifier` in `Geef.Atelier.Core/Notifications/`:**
- Frontend-agnostischer Vertrag. Infrastructure-Sink konsumiert ohne Web-Dep.
- Methode: `NotifyRunUpdatedAsync(Guid runId, CancellationToken ct)`.

**(b) `RunHub` in `Geef.Atelier.Web/Hubs/`:**
- Zwei Groups: `run-{runId}` (für Detail-Page) und `all-runs` (für Listen-Page).
- Konstante `AllRunsGroup` für typsichere Referenz.
- Vier Methoden: `JoinRunGroupAsync`, `LeaveRunGroupAsync`, `JoinAllRunsGroupAsync`, `LeaveAllRunsGroupAsync`.
- Endpoint: `app.MapHub<RunHub>("/hubs/runs")`.

**(c) `SignalRRunNotifier` in `Geef.Atelier.Web/Notifications/`:**
- `internal sealed`, Singleton-Lifetime.
- Injiziert `IHubContext<RunHub>`.
- Sendet `"RunUpdated"` an `run-{runId}`-Group **und** `"AnyRunUpdated"` an `all-runs`-Group.
- **Best-effort:** Beide `SendAsync`-Aufrufe individuell in `try { } catch { }` gewrappt (R2-CRITICAL-Fix). Doppelter Fail-Safe zusammen mit Sink-Wrapper.

**(d) `PostgresEventSink`-Konstruktor-Erweiterung:**
- Neuer Parameter `IRunNotifier notifier` als dritter Parameter (nach `scopeFactory`, vor `logger`).
- Nach erfolgreichem Persist: `await notifier.NotifyRunUpdatedAsync(atelierRunId, ct)` in eigenem `try/catch` mit Warning-Log.
- Sink-Tests verwenden `NoOpRunNotifier` (kein SignalR-Mock notwendig).

**(e) Pages mit Hub-Lifecycle (`IAsyncDisposable`):**
- `/new` (`New.razor`): `EditForm` mit `DataAnnotationsValidator`, Submit → `IRunService.SubmitRunAsync` → Redirect zu `/runs/{id}`.
- `/runs` (`Runs.razor`): Listet 20 Runs, Status-Filter-Buttons, `JoinAllRunsGroupAsync` für Live-Updates der Liste.
- `/runs/{RunId:guid}` (`RunDetail.razor`): Vollständige Details, `JoinRunGroupAsync(runId)` für Live-Detail-Updates.
- Alle Pages: `WithAutomaticReconnect()` + `Reconnected`-Handler re-joinst die Group und re-fetcht den State.

**(f) Neun UI-Komponenten in `Components/UI/`:**
- `StatusBadge`, `SeverityBadge`, `RunCard`, `IterationPanel`, `FindingItem`, `RunHeader`, `SubmitForm`, `EmptyState`, `CancelButton`.
- Alle mit `.razor.css`-Datei (scoped CSS).
- Wiederverwendbar: `StatusBadge` und `SeverityBadge` werden in `RunCard`, `RunHeader`, `FindingItem` mehrfach genutzt.

**(g) Atelier-Auslegung der "keine HTML in Pages"-Regel (R4-MINOR-Präzedenzfall):**
- Workflow-Hard-Rule fordert UI-Logik in `Components/UI/`. Aber: triviale Page-Steuerelemente (einfache `<button>` mit `onclick`-Handler, `<div>`-Container) bleiben in Pages erlaubt.
- Beispiel aus Schritt 7: 6 Inline-Filter-Buttons in `Runs.razor` wurden nicht in eine `FilterBar`-Komponente extrahiert. Begründung: Extraktion bringt mehr Komplexität als Nutzen bei dieser Trivialität. R4 markierte als MINOR, blieb bewusst unbehoben.
- **Atelier-Interpretation:** "UI-Komponente" meint wiederverwendbare UI-**Logik**, nicht jedes HTML-Element. Wenn ein Element nur an einer Stelle vorkommt, keinen eigenen State hat und keine 3+ Lines of HTML-Logik enthält, darf es in der Page bleiben.

**(h) Test-Infrastruktur:**
- **bUnit** (`Microsoft.AspNetCore.Components.Testing`) für Komponenten-Unit-Tests: 4 neue Tests (`StatusBadgeTests`, `SeverityBadgeTests`, `RunCardTests`, `SubmitFormTests`).
- **Playwright** (`Microsoft.Playwright`) für E2E-Tests: 4 Flow-Tests (`SubmitFlowTests`, `ListFlowTests`, `LiveUpdateFlowTests`, `CancelFlowTests`).
- `PlaywrightCollection` + `PlaywrightFixture`: `[Collection("Playwright")]` mit shared Chromium-Browser, Docker-Flags `--no-sandbox`, `--disable-setuid-sandbox`, `--disable-dev-shm-usage`, `--shm-size=2gb`.
- `WebTestHost`: `WebApplicationFactory<Program>` + `UseKestrel()` + Port 0; `BaseUrl` aus `IServerAddressesFeature`; überschreibt `ILlmClient → GatedFakeLlmClient`, Connection-String → `PostgresFixture`, `MaxConcurrentRuns = 10`.

**(i) Cancel-Race-Lösung (Schritt-7-Erkenntnis):**
- Bei `CancelFlowTests` ist Gate-Release nach Cancel-Click **kontraproduktiv** — würde `FakeLlmClient` (synchrones `Task.FromResult`) in < 200ms zur Completed-State rasen lassen, bevor Watcher CTS canceln kann.
- `OverrideToAbortedAsync`-Filter `Status IN (Running, Failed)` schließt `Completed` aus — Race verloren.
- **Lösung:** Gate geschlossen lassen. `SemaphoreSlim.WaitAsync(cancelledToken)` wirft `OperationCanceledException` sofort, auch ohne Permit.
- Erkenntnis für künftige Schritte: Cancellation-Tests mit Mock-LLMs müssen die Pipeline künstlich verlangsamen, sonst gewinnt der Race.

**(j) `Program.cs` und `appsettings.json`:**
- `builder.Services.AddSignalR();`
- `builder.Services.AddSingleton<IRunNotifier, SignalRRunNotifier>();`
- `app.MapHub<RunHub>("/hubs/runs");`
- Keine neuen `appsettings.json`-Sektionen für UI/SignalR.

**Workflow-Auslegung: AC9 (`geef_architecture.md`-Existenz) als "N/A":**
Bericht stuft AC9 als "N/A" ein mit Begründung "Architektur-Entscheidungen im Plan dokumentiert; R4: 0 CRITICAL/MAJOR". Der `geef_workflow.md` selbst fordert `geef_architecture.md` als Pflicht-Artefakt (Hard Rule aus D-011(A)). **Praktisch etabliert sich Plan-Phase-Integration** als äquivalente Erfüllung — der Plan enthält die architektonischen Festlegungen, R4 prüft Compliance gegen sie. Workflow-Aktualisierung wäre konsequent, ist aber Maintainer-Sache.

**AC8 ENDLICH grün (Real-OpenRouter-Test):**
- **Latenz:** 5–12 Sekunden für vollständige Pipeline (1 Iteration, Executor + 2 Reviewer).
- **Token-Verbrauch (R5-Live-Runs):** 349 Tokens (Run 1), 174 Tokens (Run 2), 523 Tokens (separater Test-Run im Bericht erwähnt).
- **Kosten-Implikation:** Bei Claude Opus 4.7 via OpenRouter grob 1–2 Cent pro Skeleton-Run. Cost-Tracking in Schritt 10 weniger dramatisch als initial befürchtet.
- **Docker-Setup:** `Llm__ApiKey` als Env-Var via `-e`-Flag injiziert; `appsettings.Development.json` (gitignored seit Commit `28daafb`) lokal verwendet.

**Workflow-Beobachtung — `appsettings.Development.json` gitignored:**
Seit Commit `28daafb` wird `appsettings.Development.json` nicht mehr getrackt. Begründung: enthält OpenRouter-Bearer-Key. Sicheres Pattern für lokale Entwicklung. Production-Setup nutzt Env-Vars (`Llm__ApiKey`). Falls späterer Maintainer fragt warum die Datei fehlt: das ist Sicherheitsdisziplin, nicht Vergesslichkeit.

**Empfehlungen für Schritt 8 (Cookie-Auth — aus Bericht-Sektion 8):**
1. Login-Page `Components/Pages/Login.razor` mit `HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ...)`.
2. User-Credentials via `ATELIER_USER` + `ATELIER_PASSWORD_HASH` Environment-Variablen. BCrypt-Hash empfohlen.
3. `AddAuthentication(...).AddCookie(...)` mit 30-Tage-Cookie-Lifetime.
4. `[Authorize]` auf `RunHub` und Pages.
5. `<AuthorizeView>` für conditional UI, `CascadingAuthenticationState` in `App.razor`.
6. E2E-Tests: `WebTestHost` kann Auth-Middleware deaktivieren oder mocken — neue Auth-Flow-Tests separat.

---

### D-021: Schritt 8 abgeschlossen — Cookie-Auth, Login/Logout, 71/71 Tests grün

**Datum:** 11. Mai 2026
**Bericht:** [reports/step-08-report.md](reports/step-08-report.md)
**Branch:** `main` direkt
**Reviewer-Iterationen:** 4 (R1–R5 alle mit 0 Findings abgeschlossen)
**Tests:** 71/71 grün. **13 Conventional-Commits.**

**Architect-Konsultation:** Form: **Plan-Phase-Integration** (sechs Schwerpunkte im Plan-Dokument fixiert; kein separater Architect-Subagent).

**Fixierte Realfakten aus Schritt 8 (verbindlich ab Schritt 9):**

**(a) Cookie-Auth-Konfiguration:**
- Cookie-Name: `Atelier.Auth`; `HttpOnly=true`; `SameSite=Strict` (Prod), `Lax` (Test-Env); `SecurePolicy`: `SameAsRequest` (Dev), `Always` (Prod); `ExpireTimeSpan=30d`, `SlidingExpiration=true`; `LoginPath="/login"`.

**(b) Schicht-Platzierung:**
- `IUserAuthenticator`-Interface in `Geef.Atelier.Application/Auth/` (nicht Infrastructure — Auth ist Anwendungslogik).
- `AtelierUserOptions` in `Geef.Atelier.Core/Configuration/` (POCO ohne Deps).
- `AtelierUserAuthenticator` (`internal sealed`) in Application.
- `ApplicationAuthExtensions.AddAtelierAuth()` bindet Options + registriert Scoped `IUserAuthenticator`.

**(c) Login-Page als Static SSR:**
- `Login.razor` ohne `@rendermode` (Static SSR Pflicht — `HttpContext.SignInAsync` braucht HTTP-Request-Kontext, nicht WebSocket).
- `@formname="login-form"` auf `<form method="post">` — Pflicht für Blazor Static SSR Form-Routing (ohne führt zu HTTP 400 "POST does not specify which form").
- `OnInitializedAsync` prüft `HttpContext.Request.Method == "POST"`.

**(d) Logout via `POST /auth/logout`:**
- Minimal-API-Endpoint in `AuthEndpoints.cs`; `.RequireAuthorization()`.
- `UserMenu`-Komponente (`AuthorizeView`, `<form method="post" action="/auth/logout">` + `<AntiforgeryToken />`).
- `SignOutAsync` in `try/catch` (Test-Env hat keinen Cookie-Auth-Handler in der authenticated=true-Variante → `InvalidOperationException` ignorieren).

**(e) `[Authorize]` auf Pages — Index bleibt anonym:**
- `@attribute [Authorize]` auf: `New.razor`, `Runs.razor`, `RunDetail.razor`.
- `Index.razor` bleibt anonym — Welcome-Page mit Quick-Links zu geschützten Pages; `AuthorizeRouteView` redirected bei Auth-Bedarf.

**(f) RunHub OHNE `[Authorize]` — architektonischer Trade-off (Abweichung von D-020-Empfehlung #4):**
- Blazor Server's `HubConnectionBuilder` erzeugt server-seitige SignalR-Verbindungen. Browser-Auth-Cookies werden **nicht** weitergeleitet.
- Mit `[Authorize]` auf `RunHub`: SSR-Pre-Render-Phase erhält 401, Blazor Circuit-Initialisierung schlägt fehl.
- Mitigation: Alle subscribenden Pages tragen `@attribute [Authorize]` → unauthentifizierte User können Pages (und damit den Hub) nicht erreichen.
- R2 und R4 akzeptierten diesen Trade-off explizit.

**(g) `TestAuthenticationHandler` und `WebTestHost`-Erweiterung:**
- `TestAuthenticationHandler` in `tests/Geef.Atelier.Tests/Web/E2E/`, `internal sealed`.
- `WebTestHost.StartAsync(bool authenticated = true)` — `true`: Test-Handler (alle Requests pre-authenticated), `false`: echte Cookie-Auth mit BCrypt-wf=4-Hash.
- **Sicherheitsregel:** Handler darf nie in `Program.cs` oder `Geef.Atelier.Web.csproj` referenziert werden.

**(h) `tools/HashPassword/` Mini-CLI:**
- `BCrypt.Net.BCrypt.HashPassword(args[0], workFactor: 11)`.
- Eingebunden als Solution-Projekt in `Geef.Atelier.slnx`.
- Dev-Default-Hash in `docker-compose.dev.yml`: entspricht `"DevPassword!"` (nur für lokale Entwicklung).

**(i) `UseForwardedHeaders` vor `UseAuthentication`:**
- Pflicht für Traefik-TLS-Termination in Produktion. `KnownIPNetworks.Clear()` für Docker-Netzwerk-Invarianz.

**(j) `/health` mit `AllowAnonymous()`:**
- Health-Probe bleibt unauthentifiziert. Reverse-Proxy-Routing und Container-Lifecycle-Management erfordern anonymen Zugriff.

**(k) BCrypt work factor 11:**
- Produktion: wf=11 (~80ms, Single-User → akzeptable Latenz). Tests: wf=4 (schnell, deterministisch).

**(l) Lazy-Fail bei fehlender Konfiguration:**
- Service startet ohne `ATELIER_USER`/`ATELIER_PASSWORD_HASH`. Login gibt `false` zurück ohne Crash. Health-Check bleibt grün. Init-Warning ohne PII geloggt.

**(m) UI-Komponenten-Library erweitert:**
- Neu in `Components/UI/`: `LoginForm.razor`, `UserMenu.razor`, `RedirectToLogin.razor` — alle mit scoped `.razor.css`.
- `EmptyLayout.razor` neu in `Components/Layout/` — minimaler Wrapper ohne NavMenu für Login-Page.

**(n) `no-store` Cache-Control-Middleware:**
- `ctx.Response.Headers.CacheControl = "no-store, no-cache"` für authentifizierte Responses — verhindert Browser-Back-Button-Cache-Leck.

**(o) `AddCascadingAuthenticationState()` statt `<CascadingAuthenticationState>` in Routes.razor:**
- `.NET 8+` Service-Registration ersetzt den Component-Wrapper. Beide zusammen führen zu doppelter Registrierung und `IComponentRenderMode`-Konflikt.

**Empfehlungen für Schritt 9 (MCP-Server):**
1. Multi-Auth-Schema: `AddAuthentication().AddCookie().AddScheme<BearerTokenHandler>(...)` — Cookie für UI, Bearer für MCP koexistieren.
2. `ITokenValidator`-Interface in `Geef.Atelier.Application/Auth/` (symmetrisch zu `IUserAuthenticator`).
3. `ATELIER_MCP_TOKEN` als Env-Var, `MCP-TOKEN`-Header in Requests.
4. MCP-Server-Projekt braucht `[Authorize(AuthenticationSchemes = "Bearer")]` auf seinen Endpoints.
5. Kein Bearer-Token-Zugriff auf UI-Blazor-Routen nötig — klare Separation.

---

## D-022 — Schritt 9: MCP-Server mit Bearer-Token-Auth (2026-05-11)

**Kontext:** Zweites Frontend für externe KI-Agenten (Claude Desktop, Claude Code) über Model Context Protocol.

**Entscheidungen:**
- (a) MCP-Library: `ModelContextProtocol.AspNetCore` 1.3.0 (offiziell Anthropic+Microsoft, GA, 9M Downloads). Eigenbau verworfen.
- (b) Transport: Streamable HTTP (Stateless=true), kein SSE-Legacy, kein WebSocket. Stdio als Future Work.
- (c) Endpoint-Position: Im Web-Host unter `/mcp` (Mcp = Class Library, gehostet im Web-Prozess).
- (d) `ITokenValidator` in `Geef.Atelier.Application/Auth/`, `AtelierMcpOptions` in `Core/Configuration/`.
- (e) Multi-Auth: Default-Scheme=Cookie, explizite `McpPolicy` mit `AuthenticationSchemes=["Bearer"]`.
- (f) `BearerTokenHandler` in `Geef.Atelier.Web/Auth/` (internal sealed), gibt `NoResult()` bei fehlendem Header.
- (g) `RunEntity.CreatedByUser` nullable, Migration `Step09AuditTrail` (ADD COLUMN text nullable).
- (h) `IRunService.SubmitRunAsync(briefing, configJson, createdByUser=null, ct)` — optionaler Default-null-Parameter (Backward-Compat).
- (i) UI setzt `createdByUser=Identity.Name`, MCP setzt `"mcp-client"` (statischer Bezeichner).
- (j) `GetRunDetailsAsync` (aus D-019) für `get_run_details`-Tool wiederverwendet — keine neue Methode.
- (k) Constant-Time-Token-Compare: `CryptographicOperations.FixedTimeEquals` mit Längen-Kurzschluss vor dem Vergleich.
- (l) MCP-SDK-Version gepinnt auf `[1.3.0,2.0.0)` in `Directory.Packages.props`.
- (m) Mcp-Projekt-SDK-Wechsel: `Microsoft.NET.Sdk.Web` → `Microsoft.NET.Sdk` (Class Library); Program.cs + appsettings* gelöscht.
- (n) MCP-Endpoint-Setup im `WebTestHost` immer aktiv (kein Konflikt mit Cookie-Tests).
- (o) Stdio-Transport als Future Work dokumentiert (nach Skeleton).

**Ergebnis:** 85/85 Tests grün. R1 PASS, R2 APPROVED, R3 PASS, R4 COMPLIANT, R5 curl-verifiziert.

---

## D-023 — Schritt 10: Production-Deploy mit Traefik (2026-05-11)

**Kontext:** Letzter Walking-Skeleton-Schritt. App-Code unverändert; nur Deployment-Konfiguration.

**Entscheidungen:**
- (a) Externes Traefik-Netzwerk: `proxy` (Server-Konvention, verifiziert).
- (b) Cert-Resolver-Name: `le` (HTTP-Challenge via `web`-Entry-Point); nicht `letsencrypt`.
- (c) Entry-Point für HTTPS-Router: `websecure`; HTTP→HTTPS-Redirect global in traefik.yml.
- (d) Kein App-seitiger HTTP-Redirect-Router — Traefik macht das global.
- (e) `chain@file`-Middleware: secure-headers + compression + rate-limit (Server-Konvention).
- (f) Postgres im selben Compose-File (Server-Konvention: own-Postgres-per-App).
- (g) `.env`-File gitignored, automatisch generiert via `openssl rand` + `tools/HashPassword`.
- (h) `pull_policy: never` + `com.centurylinklabs.watchtower.enable=false` für lokalen Build.
- (i) Auto-Migration on Startup bleibt (D-010); `Step09AuditTrail` ist additiv (nullable Spalte).
- (j) `Cookie.Domain` unset — Auto-Detect robuster für Subdomains.
- (k) Direkt-Port-Exposure (8080) entfernt; kein `ports:` im Production-Compose.
- (l) `tools/HashPassword` für BCrypt-Hash mit workFactor 11.
- (m) `traefik.docker.network=proxy` Label erforderlich wenn Container in mehreren Netzwerken.

**Ergebnis:** Walking Skeleton komplett. `geef.stefan-bechtel.de` produktiv erreichbar.

---

## D-024 — Post-Skeleton Schritt 1: Postgres-Backup-Strategie (2026-05-11)

**Kontext:** Nach Walking-Skeleton-Abschluss: erster Post-Skeleton-Feature-Schritt zur Datensicherung. Kein Code-Eingriff — reiner Compose-Erweiterungsschritt.

**Entscheidungen:**
- (a) Backup-Image: `prodrigestivill/postgres-backup-local:16` (Community-Standard, Healthcheck + Retention-Policy out-of-the-box, PG16-kompatibel).
- (b) Netzwerk: nur `geef-atelier-network` (kein `proxy` — Backup braucht keinen externen Zugang).
- (c) Watchtower-Disable: `com.centurylinklabs.watchtower.enable=false` (Server-Konvention aus D-023).
- (d) Retention: 7 Tages-, 4 Wochen-, 6 Monats-Snapshots — angemessen für Single-User mit < 100 MB DB.
- (e) Schedule: `0 3 * * *` (03:00 UTC, minimale Server-Auslastung).
- (f) Backup-Volume: `geef-atelier-backups` (Named Volume, isoliert von `geef_atelier_postgres_data`).
- (g) Restore-Skript: `scripts/restore-backup.sh` — stoppt `web`, restored via psql, startet `web` neu.
- (h) Kein Off-Site-Backup in diesem Schritt — Volume-Backup schützt gegen Container-Crash, nicht gegen Server-Ausfall. Off-Site als nächster Post-Skeleton-Schritt dokumentiert.
- (i) Keine neuen `.env`-Variablen — Backup nutzt bestehende `POSTGRES_*`-Credentials.
- (j) Server-Präzedenz: kein anderer App-Stack auf diesem Server nutzt einen Backup-Service — Geef.Atelier setzt den Pattern.

**Ergebnis:** Drei Container healthy (web, postgres, postgres-backup). Erster manueller Backup-Trigger und Test-Restore erfolgreich verifiziert. 85/85 Tests weiter grün.

---

## D-025 — Post-Skeleton Schritt 2: Reviewer-Kalibrierung (2026-05-12)

**Kontext:** Erstes Real-World-Briefing (Hadwiger-Nelson-Problem) deckte fehlerhafte Severity-Klassifikation auf: Der `KlarheitReviewer` produzierte Critical-Findings für faktisch korrekte Inhalte ("stimmt zwar, aber...") → Pipeline brach ab (AbortOnCritical=true aus D-012). Drei Ziele: (A) Reviewer-Prompts schärfen, (B) Convergence-Policy robuster machen, (C) Executor-Iteration-2+-Verhalten verbessern.

**Entscheidungen:**
- (a) Severity-Taxonomie 4-stufig: Critical/Major/Minor/Info als Atelier-Standard; `docs/06-reviewer-calibration.md` ist normatives Referenzdokument.
- (b) Tool-Schema-Werte umgestellt: `["critical", "major", "minor", "info"]` statt `["info", "warning", "error", "critical"]`. Backwards-Kompat in `LlmReviewer.MapSeverity()` für `"error"` und `"warning"`.
- (c) Anti-Pattern-Regel "stimmt zwar" ≠ Critical explizit in beide Reviewer-Prompts aufgenommen.
- (d) Hadwiger-Nelson-Problem als Negativbeispiel in beiden Reviewer-Prompts — konkreter LLM-Anker gegen Fehlklassifikation.
- (e) `ConvergenceOptions` als neue Config-Klasse in `Geef.Atelier.Infrastructure.Configuration`; Default `AbortOnCritical=false` — Pipeline iteriert 3 Mal statt nach erstem Critical abzubrechen.
- (f) Executor-Prompt schärft Iteration-2+-Verhalten: nummerierte Findings mit Severity-Tag, explizite Anforderung "concrete, visible change per finding".
- (g) Reviewer-Prompts gewachsen von ~4 auf ~65 Zeilen — Token-Cost-Anstieg ~5-10% pro Reviewer-Call akzeptiert (bei <5 Cent/Run irrelevant).
- (h) Stagnation-Threshold bleibt 3 (kein Eingriff) — Pipeline bricht bei persistierenden Findings nach 3 Iterationen ab.
- (i) Cross-Reviewer-Voting (B2) verworfen — zu komplex für diesen Schritt; B1+B4 (konfigurierbar) reicht.
- (j) Hadwiger-Nelson-Replay als `[Fact]`-Test mit Skip-If-No-ApiKey in `AtelierPipelineRunsAgainstOpenRouterTests` — langfristige Regression-Absicherung.

**Ergebnis:** 96 Tests (vorher: 85). 3 neue Test-Klassen (SeverityClassification, ConvergencePolicyConfig, OvereagerCriticalAbort). `dotnet build` 0/0. Reviewer-Kalibrierung auf Atelier-Standard angehoben.

---

## D-026 — Post-Skeleton Schritt 3: Design-Translation (2026-05-12)

**Kontext:** Walking Skeleton + PS-1 (Backup) + PS-2 (Reviewer-Kalibrierung) sind durch. Ein professioneller HTML/CSS/JSX-Prototyp (`docs/design/atelier-mockups/`) mit drei Paletten (Vellum/Noir/Petrol), eigener Typografie (Newsreader/Geist/JetBrains Mono), 14+ Hairline-Icons und fünf Screens lag vor. Ziel: visuelle und strukturelle Sprache ins Blazor-UI übertragen ohne neue Backend-Features.

**Entscheidungen:**
- (a) Drei Themes via `html.palette-{vellum|noir|petrol}` (CSS-Klasse). Default Vellum (Prompt-Anforderung) — Mockup-Default war Noir; explizite Divergenz.
- (b) Theme-Cookie `Atelier.Theme`, 1 Jahr, `HttpOnly=false` (JS-Interop muss lesen/setzen), SameSite=Strict, Logout löscht NICHT.
- (c) Theme-Wechsel-Mechanik: JS-Interop primär (`window.atelier.setTheme`), Server-Endpoint `/settings/theme` als Fallback und Playwright-Test-Hook. Beide parallel implementiert.
- (d) `<html>`-Klasse Razor-server-side via `IHttpContextAccessor` — kein Flash-of-Wrong-Theme.
- (e) Bootstrap-Stylesheet vollständig entfernt; `wwwroot/atelier.css` als globales Stylesheet (1:1 portiert aus Mockup + @font-face).
- (f) Self-hosted Fonts (Newsreader, Geist, JetBrains Mono) in `wwwroot/fonts/` — DSGVO-konform, kein externer Network-Request.
- (g) ReviewerDisplay-Helper-Mapping (β-Variante): Code-Klassen `BriefingTreueReviewer`/`KlarheitReviewer` unverändert; UI zeigt `BriefingFidelity`/`Clarity`. Keine Persistenz-Breaking-Changes.
- (h) PressStageMapper: konservative Heuristik — alle Running-Runs zeigen Stage 0 (Draft) active; komplexere Event-basierte Stage-Erkennung als optionale Verfeinerung.
- (i) FindingResolutionInferrer: heuristisches Cross-Iteration-Diff via `Severity|ReviewerName|Message[..60]`-Signatur. Bug-Fix in PS-3: `IndexOutOfRangeException` bei leerer Iterations-Liste behoben.
- (j) Mock-Stubs konsistent via `.coming-soon`-Klasse: Cost-Anzeige, Export-Button, Profile-MenuItem, Welcome-Stats.
- (k) E2E-Selektoren bewusst stabil: `input#username`, `button.btn-login`, `.user-name`, `.btn-logout`, `textarea#briefing`, `button.btn-submit`, `[data-status]`. Neue Komponenten mit `data-testid`.
- (l) Tweaks-Panel und Style-Guide aus Mockup nicht in Production übernommen.
- (m) StatusBadge: `data-status`-Attribut wiederhergestellt (E2E-Tests erwarten es); CSS-Klassen-Umbau (`badge-*` → `status pending`) erforderte Test-Anpassung.
- (n) WebTestHost in Tests: `AddHttpContextAccessor()` ergänzt, `[Collection("Postgres")]` für Theme-Cookie-Tests.

**Ergebnis:** 106 Tests gesamt (105 passed, 1 skipped ThemeSwitcher-E2E). `dotnet build` 0/0. Fünf Screens visuell überarbeitet, drei Themes funktional, self-hosted Fonts, 16 Icon-Komponenten, Bootstrap entfernt.

---

## D-027 — Post-Skeleton Schritt 4: CLI-Provider-Adapter (2026-05-13)

**Kontext:** Alle drei Provider-Aufrufe liefen bisher über OpenRouter (Pay-as-you-go). Auf dem Atelier-Server (Hetzner, `95.216.100.213`) sind `claude` (Claude Code CLI, Subscription) und `codex` (OpenAI Codex CLI, Subscription) installiert — Subscription-Kapazität ohne Token-Abrechnung. Ziel: zweiter Provider-Pfad via neuem Side-Container, der diese CLIs als OpenAI-kompatibles HTTP exposed.

**Entscheidungen:**

- **(a) Tech-Stack CLI-Proxy:** Python 3.12 + FastAPI. Begründung: kompakter Code für HTTP-Subprocess-Wrapper, Pydantic für Schema-Validierung, pytest für Tests. Dockerfile installiert Node.js + CLI-Packages (`@anthropic-ai/claude-code`, `@openai/codex`) on top.
- **(b) Schnittstelle CLI-Proxy:** OpenAI-kompatibel (`POST /v1/chat/completions`, `GET /v1/models`, `GET /health`). `OpenAiCompatibleClient` bleibt unverändert — nutzt einfach alternativen Endpoint. Kein neuer `CliLlmClient` nötig.
- **(c) `LlmOptions`-Umbau (Multi-Provider):** Altes flaches Schema (`ApiKey`, `DefaultModel`, `Actors{Model}`) ersetzt durch `Providers`-Dict (name → Endpoint + ApiKey) und `Actors`-Dict (name → Provider + Model + MaxTokens). Harter Cut, kein Backward-Kompat (Single-Maintainer-Projekt). Env-Var: `Llm__Providers__openrouter__ApiKey`.
- **(d) `ILlmClientResolver`-Interface:** `(ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName)` in `Geef.Atelier.Infrastructure.Llm`. Resolver cached `OpenAiCompatibleClient`-Instances pro Provider in `ConcurrentDictionary` (eine `HttpClient`-Instanz pro Provider, nicht pro Akteur).
- **(e) `OpenAiCompatibleClient`-Multi-Instance:** Konstruktor übernimmt jetzt `(HttpClient, string endpoint, string apiKey)` direkt — keine `IOptions<LlmOptions>` mehr. Einziger named `HttpClient` ("llm") wird für alle Provider wiederverwendet (`IHttpClientFactory`); Endpoint und ApiKey werden per Call via `Authorization`-Header injiziert.
- **(f) Model-Routing im CLI-Proxy:** `claude-*` / `anthropic/claude-*` → claude CLI; `gpt-*` / `o*` / `openai/*` → codex CLI. Unbekannte → claude (Fallback). Provider-Prefix wird vor CLI-Aufruf gestripped.
- **(g) Tool-Use-Mapping:** Schema-Embedding als System-Prompt-Addendum → CLI liefert Plaintext → `tool_use_parser.extract_json()` extrahiert JSON (inkl. Markdown-Fence-Stripping, Balanced-Brace-Scan) → `tool_calls`-Response. Bei JSON-Parse-Failure: Plaintext als `finish_reason="stop"` zurück (downstream `LlmReviewer` kann damit umgehen via D-013(e)).
- **(h) Auth-Strategie CLI-Proxy:** Named Volume `geef-atelier-cli-auth:/auth`, unterteilt in `/auth/claude` und `/auth/codex`. Einmaliger manueller Login via `docker exec -it geef-atelier-cli-proxy claude auth login`. Tokens persistieren über Container-Restarts. Secrets niemals in Source-Control, Logs oder Berichten.
- **(i) Concurrency:** `asyncio.Semaphore` pro CLI, Default 2 (`CLAUDE_MAX_CONCURRENT=2`, `CODEX_MAX_CONCURRENT=2`). Warteschlange bei Überschreitung, kein Fehler.
- **(j) Side-Container-Netzwerk:** `geef-atelier-network` (intern), kein `proxy` (kein Traefik). Hostname innerhalb Compose-Stack: `cli-proxy`. Erreichbar von `web` via `http://cli-proxy:8090/v1`.
- **(k) Umbenannte Env-Var:** `LLM_API_KEY` → `LLM_OPENROUTER_API_KEY` in `.env` + docker-compose. Klarere Semantik im Multi-Provider-Kontext.
- **(l) Default-Konfiguration:** Alle drei Akteure bleiben auf `openrouter` (Backward-Sanity). `cli`-Provider in `appsettings.json` vorkonfiguriert, aber kein Akteur darauf geroutet. Umschalten per Env-Override ohne Code-Änderung.
- **(m) `web`-depends-on `cli-proxy`:** Health-Check-abhängig (service_healthy). Verhindert Start vor CLI-Proxy-Bereitschaft.

**Tests:**
- Python: 21 pytest-Tests (openai_format, tool_use_parser, claude_adapter_mock, codex_adapter_mock, concurrency) — alle grün.
- C#: LlmOptionsMultiProviderTests, LlmClientResolverTests, OpenAiCompatibleClientTests (angepasst), TestLlmClientResolver (neu). `dotnet test` 113 passed, 1 skipped — alle grün.

**Ergebnis:** `dotnet build` 0/0. 113 C#-Tests grün. 21 Python-Tests grün. CLI-Proxy-Image bauffähig. Backward-Sanity (pure OpenRouter) unverändert.

---

## D-028 — Crew-Foundation: Profile, Templates, Snapshots (2026-05-13)

**Kontext:** Die Pipeline lief mit einer fest hartkodierte Dreier-Crew (`LlmExecutionStep` + `LlmReviewer(BriefingTreue)` + `LlmReviewer(Klarheit)`). Das blockierte das Vision-Ziel "Text-Manufaktur mit verschiedenen Crews" und alle nachfolgenden Schritte (PS-6: UI-Crew-Auswahl, PS-7: Advisor-Pässe).

**Entscheidungen:**

- **(a) Profile als Records:** `ReviewerProfile` und `ExecutorProfile` sind sealed positional records in `Core/Domain/Crew/Profiles/`. Gleiche Struktur (Name, DisplayName, Description, SystemPrompt, Provider, Model, MaxTokens, IsSystem). EF Core mappt sie als Entities mit Name als PK.

- **(b) System-Profile als Code-Konstanten:** Definiert in `SystemCrew.cs` (read-only). Nicht in DB. Custom-Profile in DB mit Auto-Prefix `"custom-"`. Update/Delete von System-Profilen wirft `InvalidOperationException`. Modell-Pluralismus: DefaultExecutor auf `anthropic/claude-opus-4.7` (Kontinuität), Reviewer auf Außen-Modelle (`google/gemini-2.5-flash`, `openai/gpt-5.5-mini`) gemäß Vision-Leitstern 3 + CLAUDE.md-Konvention.

- **(c) CrewTemplate:** Komponiert Executor + Reviewers + EvaluationStrategy + optionalen ConvergenceOverride + leere AdvisorProfileNames (PS-7-Vorbereitung). System-Template `"klassik"` als Code-Konstante, reproduziert PS-2-Verhalten exakt.

- **(d) CrewSnapshot vollständig eingebettet:** Reproduzierbarkeit garantiert. JSONB-Spalte auf `Runs`. SchemaVersion=1. Serialisiert mit `JsonNamingPolicy.CamelCase`. Defensive Deserialisierung im OrchestratorService mit Fallback auf Klassik-Konstanten.

- **(e) EvaluationStrategies:** Alle vier Strategien (`Parallel`, `Sequential`, `FailFast`, `Priority`) als SDK-Klassen in `Geef.Sdk.Policies` vorhanden — kein Atelier-eigener Code nötig. `EvaluationStrategyMapper` mappt Domain-Enum auf SDK-Klassen.

- **(f) `ILlmClientResolver.ForProfile`:** Zweite Methode ergänzt; nutzt denselben Provider-Cache wie `ForActor`. `ForActor` bleibt für Backward-Kompatibilität.

- **(g) `IRunService.SubmitRunAsync` erweitert:** Neue Parameter `crewTemplateName` + `customCrew`. Null/leer → Default `"klassik"`. `customCrew` überschreibt `crewTemplateName`. Snapshot wird beim Submit gebaut und persistiert.

- **(h) MCP-Tools:** `list_crew_templates` + `list_reviewer_profiles` neu. `submit_request` erweitert um `crew_template` + `custom_crew` (JSON-String, bei ParseFehler ignoriert).

- **(i) Migration Step10CrewSystem:** Lücke nach Step09AuditTrail geschlossen. Neue Tabellen `ReviewerProfiles`, `ExecutorProfiles`, `CrewTemplates`. `Runs` um `CrewTemplateName` + `CrewSnapshot` erweitert. UPDATE historische Runs zu `"klassik"`. UPDATE `Findings.ReviewerName` (`BriefingTreueReviewer` → `briefing-fidelity`, `KlarheitReviewer` → `clarity`).

- **(j) Reviewer-Slugs:** `"briefing-fidelity"` und `"clarity"` ersetzen alte Klassennamen in `FindingEntity.ReviewerName`. `ReviewerDisplay.ToDisplay()` enthält beide Varianten als Fallback.

- **(k) `LlmReviewer`, `LlmExecutionStep`, `AtelierSystemPrompts` gelöscht:** Ersetzt durch `ProfileBasedReviewer`/`ProfileBasedExecutor` + `Core/Domain/Crew/SystemPrompts.cs`.

- **(l) AdvisorProfile-Stub:** Vollständig definiert mit `AdvisorMode`-Enum (Strategic, Critical, DevilsAdvocate, DomainExpert). Keine DB-Tabelle in PS-5. PS-7 nutzt Schema ohne Breaking-Change.

- **(m) SystemPrompts in Core:** Die langen System-Prompt-Strings gehören semantisch zu den System-Profilen → liegen jetzt in `Core/Domain/Crew/SystemPrompts.cs` (Domain-Layer, keine Infrastruktur-Abhängigkeit).

**Tests:** `dotnet build` 0/0 Warnings. 154 C#-Tests grün (41 neue), 1 E2E-Skip unverändert.

**Ergebnis:** Pipeline baut sich pro Run dynamisch aus dem persistierten CrewSnapshot. System-Defaults im Code versioniert, User-Custom in DB. Reviewer-Name-Migration sauber. MCP-Tools funktional. PS-6 (UI-Crew-Auswahl) und PS-7 (Advisor-Pässe) können ohne Schema-Brüche folgen.

---

## D-029 — PS-6 Crew-UI: Architektur-Entscheidungen

Datum: 2026-05-13

| Entscheidung | Ergebnis |
|---|---|
| (a) `CrewSnapshot.Deserialize` als statischer Domain-Helper | Konsolidiert die bisher inline in `RunOrchestratorService` duplizierte Deserialisierungs-Logik. UI-Komponenten und Service konsumieren denselben Helper. |
| (b) `IProviderCatalog`-Service in Application-Schicht | Statt direktem `IOptions<LlmOptions>`-Inject in Razor (Layer-Verletzung): schmale Interface in Application, Implementierung in Infrastructure wraps `IOptions<LlmOptions>`. |
| (c) Routing-Constraint `[a-z0-9\-]+` max 64 Zeichen | Blazor-Route-Parameter für Profile-/Template-Namen. DataAnnotations-`RegularExpression` in Form-Validierung erzwingt dasselbe Pattern. |
| (d) Interactive-Server-Render-Mode für alle CRUD-Editor-Pages | Up/Down-Buttons im ReviewerPicker, State-Verwaltung im Delete-Modal brauchen Server-Interaktivität ohne Page-Reload. |
| (e) Top-Level-NavMenu-Link „Crew" als vierter Eintrag | NavMenu hatte nur 3 Items, kein Style-Guide-Link vorhanden. Top-Level ist die natürliche Wahl. |
| (f) Generische `ProfileEditorForm` für Reviewer + Executor | Beide Records haben identisches Schema — Code-Reuse via einem parametrisierten Form-Komponenten. |
| (g) Generische `Modal`-Komponente + `DeleteConfirmationModal` als Wrapper | Kein Browser-`confirm`. Modal ist wiederverwendbar für andere Bestätigungs-Flows. |
| (h) Up/Down-Pfeil-Buttons statt Drag-Drop im ReviewerPicker | Kein JS-Interop nötig, ausreichend für 2-5 Reviewer. |
| (i) System-Profile/Template-Schutz: UI + Service | UI rendert disabled-Buttons + Duplicate-Action; Service wirft zusätzlich `InvalidOperationException` (Defense in Depth). |
| (j) Custom-Auto-Prefix Live-Preview im Editor | UI zeigt „Wird gespeichert als: custom-XXX" vor dem Speichern. Service-Layer ist idempotent. |
| (k) `CrewSummary` Click-to-Expand statt separatem Modal | Platzsparend, kein zusätzlicher Klick für Schließen nötig. |
| (l) `CrewBadge` als dezenter Text-Badge ohne Icon | Kleine visuelle Hierarchie unterhalb StatusBadge — Icon wäre Überfrachtung. |
| (m) AdvisorProfile-Felder aus PS-6 ausgespart | PS-7 bringt Advisor-UI. Schema steht, aber in PS-6 noch nicht funktional. |


## D-030 — Bugfix: Run-Status bei LLM-Provider-Fehler

Datum: 2026-05-13

| Entscheidung | Ergebnis |
|---|---|
| (a) Outer-Catch im `RunOrchestratorService`, nicht im SDK-Layer | `ProcessRunAsync` ist der einzige Ort, der den Run-Kontext kennt und Schreibrechte auf `RunEntity` hat. SDK-Layer für Error-Handling anzupassen wäre Schicht-Verletzung. |
| (b) `MarkRunFailedAsync` als separater Helper analog zu `OverrideToAbortedAsync` | Konsistentes Muster: jede Terminal-State-Transition hat ihren eigenen Helper. Kein generischer Helper, um Verwechslungen zu vermeiden. |
| (c) `SanitizeErrorMessage` walked die `InnerException`-Kette | Das Geef SDK wraps `HttpRequestException` in seiner eigenen Exception — direktes Pattern-Matching auf dem äußersten Exception-Typ reicht nicht. |
| (d) Keine Exception-Typen explizit in der `catch`-Signatur | Der `catch (Exception ex)` bleibt generisch, da das SDK beliebige Wrapper-Typen verwenden könnte. Die Sanitize-Logik kapselt die Typ-Differenzierung. |
| (e) `TaskCanceledException` im Sanitizer = "timed out" | `cts.IsCancellationRequested`-basierte Cancellation wird durch frühere `catch`-Blöcke abgefangen. Eine `TaskCanceledException`, die den generischen Block erreicht, ist ausschließlich ein Provider-Timeout. |
| (f) Kein Auto-Retry | Transient-Error-Retry (HTTP 429/503) bleibt separater Step mit `Polly`. Dieser Bugfix nur Error-State korrekt persistieren. |

---

## D-031 — PS-7: Advisor-Pässe (2026-05-13)

**Kontext:** Advisor-Pässe ermöglichen es, vor dem Executor-Pass (BeforeFirstExecution, BeforeEveryExecution) oder nach einem Convergence-Failure (OnConvergenceFailure) LLM-Akteure konsultativ einzuschalten. Ihr Output wird als gekennzeichneter Kontext-Block in den Run-Kontext injiziert, ohne den Geef-SDK-Kern zu modifizieren.

| Knackpunkt | Entscheidung |
|---|---|
| (a) Decorator-Pattern statt SDK-Hook | Das Geef-SDK exportiert zwar `Geef.Sdk.Advisors.IAdvisor`, aber aus Layer-Trennung-Gründen wird ein eigener `AdvisorAwareExecutor`-Decorator um `IExecutionStep` gelegt. Der Decorator ist im Infrastructure-Layer angesiedelt und braucht kein SDK-Intern-Wissen. Änderungen am SDK-Interface würden Atelier nicht brechen. |
| (b) Advisor-Output als `AtelierContextKeys.AdvisorBlock` | Der Advisor-Output wird als einzelner, klar bezeichneter Text-Block (`[ADVISOR: <name>]\n<text>`) in `IRunContext` via `AtelierContextKeys.AdvisorBlock` geschrieben. Executor- und Reviewer-System-Prompts können diesen Block referenzieren. Keine strukturierten Findings — Advisors liefern fließenden Rat, keine Severity-klassifizierten Befunde. |
| (c) Advisor-Failure → Run schlägt fehl (Exception bubbling) | Advisor-LLM-Calls sind nicht best-effort. Eine Exception im `ProfileBasedAdvisor` bubbled durch den `AdvisorAwareExecutor` hoch und bricht den Run mit Status `Failed` ab. Begründung: ein fehlgeschlagener Advisor hat möglicherweise den Executor-Context korrumpiert — stiller Weiterlauf wäre gefährlicher als transparenter Abbruch. |
| (d) Advisor-Reihenfolge signifikant (analog Reviewer) | Mehrere Advisors eines Triggers werden in Listen-Reihenfolge sequenziell ausgeführt. Jeder spätere Advisor sieht den bereits akkumulierten `AdvisorBlock` der Vorgänger. Analog zur `Sequential`-EvaluationStrategy bei Reviewern: Reihenfolge im CrewTemplate ist semantisch. |
| (e) OnConvergenceFailure-Trigger via Single-Retry-Cap | `RunOrchestratorService.TryConvergenceFailureRetryAsync` fängt `ConvergenceFailedException` und startet einen einmaligen Wiederholungsdurchlauf mit `OnConvergenceFailure`-Advisors im Kontext. `RunEntity.AdvisorRetryAttempted`-Flag (Migration Step11) verhindert Endlos-Schleifen: ein zweites `ConvergenceFailedException` nach dem Retry eskaliert direkt zu `Failed`. Multi-Retry mit konfigurierter Wiederholungsanzahl ist als Future Work dokumentiert (PS-8 oder eigener Schritt). |

---

## D-032 — Refactor: CLI-Provider-Split (2026-05-13)

**Kontext:** PS-4 hat einen einzelnen `cli`-Provider mit internem Model-Name-Routing angelegt (claude-Präfix → claude CLI, gpt-/o*-Präfix → codex CLI). Im PS-6 Crew-UI wurde sichtbar, dass dieser versteckte Routing-Mechanismus eine UX-Schwäche ist: der User sieht im Provider-Dropdown nur "cli" und muss anhand des Modell-Namens raten, welche CLI tatsächlich genutzt wird. Ein falsch geschriebener Modell-Name kann silently in die falsche CLI routen.

| Knackpunkt | Entscheidung |
|---|---|
| (a) Legacy-Endpoint im cli-proxy: Entfernen oder Behalten | **Behalten mit Deprecation-Warning.** Der Legacy-Endpoint `/v1/chat/completions` bleibt erhalten und loggt bei jedem Aufruf ein WARNING-Level-Log. Begründung: minimales Risiko bei Migration-Edge-Cases (falls ein Profil nach DB-Migration noch `cli` als Provider hätte, würde es weiter funktionieren statt komplett zu scheitern). Geplante Entfernung nach 2-3 Atelier-Versionen. |
| (b) Zwei explizite Endpoints: `/v1/claude/chat/completions` + `/v1/codex/chat/completions` | Deterministisches Routing ohne Model-Name-Heuristik. Provider-Name allein entscheidet die CLI-Wahl. Atelier-Konfiguration trennt die zwei Provider in `appsettings.json` mit unterschiedlichen Endpoint-Pfaden. |
| (c) `IProviderCatalog`-API-Erweiterung um `ProviderInfo` | Die alte Methode `ListProviderNames() → IReadOnlyList<string>` wird ersetzt durch `ListProviders() → IReadOnlyList<ProviderInfo>`. `ProviderInfo` ist ein sealed record mit `Name` (DB-Key) und `DisplayName` (UI-Label). Kein Backward-Kompat (Single-Maintainer-Projekt, einziger Caller ist `ProfileEditorForm`). |
| (d) `ProviderCatalog`-Implementierung: Hardcodierte DisplayNames statt DB-Daten | DisplayNames für die drei bekannten Provider (`openrouter`, `claude-cli`, `codex-cli`) sind als Dictionary-Konstante im Code hinterlegt. Unbekannte Provider-Namen (zukünftige Erweiterungen) fallen auf `Name == DisplayName` zurück. |
| (e) CrewSnapshot-Migrations-Strategie: Two-Pass SQL | DB-Migration `Step12CliProviderSplit` migriert Profile-Tabellen (ReviewerProfiles, ExecutorProfiles, AdvisorProfiles) via direktem `CASE`-`UPDATE` auf der `"Model"`-Spalte — immer korrekt. Für `Runs.CrewSnapshot`-JSONB: Two-Pass SQL-String-Replace (Codex-Pattern zuerst, dann claude-cli-Fallback). Limitation: Mixed-CLI-Snapshots (ein Executor claude, ein Reviewer codex im selben Run) werden nicht korrekt migriert — in der Praxis existieren solche Snapshots nicht, da alle System-Akteure `openrouter` nutzen und custom CLI-Profile im Projekt neu sind. |

**Tests:** `dotnet build` 0/0. 246 C#-Tests grün (7 neue), 1 E2E-Skip. Python pytest 30/30 grün (9 neue).

---

## D-033 — Feature: Model-Catalog-Dropdown (2026-05-13)

**Kontext:** Der Profile-Editor (PS-6) nutzte ein Free-Text-Input für das Modell-Feld. Das führte zu einem Bug mit nicht-existentem Modell (`openai/gpt-5.5-mini`), der jetzt korrekt als `Failed` landet (D-030), aber besser durch Prävention gelöst wird: ein Dropdown mit validen Modell-IDs verhindert Tippfehler.

| Knackpunkt | Entscheidung |
|---|---|
| (a) CLI-Listing-Befehl | Weder `claude` noch `codex` CLI hat einen `--list-models`-Befehl. Nur Option (b) — statische Listen in den Adaptern. Kein "Hybrid"-Versuch nötig. |
| (b) Recommended-Listen | Hardcoded in `StaticModelFallback.cs` (Core-Layer). Recommendation ist eine Atelier-Meinung, keine Provider-Eigenschaft. Maintainer-Pflicht bei jedem Modell-Release. |
| (c) Cache-Sharing | Single-Instanz-Setup: `IMemoryCache` mit 24h-TTL ausreichend. Kein Redis oder distributed cache nötig. |
| (d) CLI-Provider-Modellquellen | `claude-cli`-Backend: statische Liste in `claude_adapter.STATIC_MODELS`. `codex-cli`-Backend: statische Liste in `codex_adapter.STATIC_MODELS`. Beide werden über die neuen `/v1/claude/models` und `/v1/codex/models` Endpoints im cli-proxy exponiert. OpenRouter: live API-Aufruf gegen `https://openrouter.ai/api/v1/models`. |
| (e) Fallback-Strategie | API-Aufruf schlägt fehl → `StaticModelFallback.For(providerName)`. `IsUsingFallback()`-Flag wird exponiert; UI zeigt Warning-Banner wenn Fallback aktiv. |
| (f) Custom-Model-Escape-Hatch | Besteht als "Custom model name…" Option im Dropdown. Beim Speichern mit nicht-katalogisiertem Modell: Bestätigungs-Modal ("Save anyway?"). Verhindert Tippfehler, erlaubt aber bewusste Custom-Nutzung. |

**Tests:** 256 C#-Tests (8 neu `ModelCatalogTests`), 43 Python-Tests (13 neu `test_models_endpoints.py`), 1 E2E-Skip, 0 Failures.

---

## D-034 — Feature: Grounding-Visualization (2026-05-13)

**Kontext:** Die GEEF-Grounding-Phase existiert vollständig im Code (`BriefingGroundingStep`, `AdvisorContextGroundingStep`), ist aber in der UI unsichtbar. RunDetail springt direkt von Briefing zur ersten Iteration. Zusätzlich werden Pre-Execution-Advisors (Trigger `BeforeFirstExecution`) als Iteration-1-Beitrag gerendert — konzeptionell gehören sie zur Grounding-Phase, da sie einmalig pro Run laufen.

| Knackpunkt | Entscheidung |
|---|---|
| (a) Trigger-Lookup-Strategie: DB-Spalte vs. Snapshot-Deserialisierung | **Snapshot-Deserialisierung.** `AdvisorConsultation`-Entity hat kein `Trigger`-Feld. Der Trigger wird zur Render-Zeit aus `CrewSnapshot.Advisors` per `AdvisorProfileName`-Match nachgeschlagen. Keine DB-Migration nötig. Fallback bei Lookup-Miss (z.B. historische Runs ohne passendes Advisor-Profil im Snapshot): Consultation bleibt im Iteration-Bucket, kein Datenverlust. |
| (b) Press-Visualization-Layout: Vier gleichberechtigte Stages vs. zweiteilige Anzeige | **Zweiteilig** (Grounding | Iteration-Loop). Grounding als eigenständige Spalte links des Iteration-Blocks, getrennt durch CSS-Divider. Macht semantisch klar, dass Grounding einmalig läuft und die Iteration-Stages ein Loop sind. Vier gleichberechtigte Stages würden den Unterschied verschleiern. |
| (c) Grouping-Logik-Location: Page-intern vs. Application-Layer ViewModel | **Application-Layer ViewModel** (`RunWithGroundingViewModel`). `IRunService.GetRunWithGroundingAsync` kapselt die Grouping-Logik vollständig: testbar ohne Blazor-Stack, wiederverwendbar (ggf. für zukünftiges MCP-Tool), saubere Layer-Trennung. `RunDetail.razor` ist damit rein deklarativ ohne Grouping-Logik. |

**Tests:** 273 C#-Tests (19 neu: `RunWithGroundingViewModelTests`, `GroundingSectionTests`, `PressVisualizationWithGroundingTests`), 1 E2E-Skip, 0 Failures. Python-Tests unverändert (43 grün).

---

## D-035 — Grounding-Provider-Foundation + Tavily Web-Search (2026-05-13)

**Kontext:** Erster echter Web-Search-Provider auf Basis der Advisor-Pässe-Architektur (PS-7 gespiegelt). Ziel: generische `IGroundingProvider`-Abstraktion, die auch für einen zukünftigen `VectorStoreGroundingProvider` ohne Refactor gilt.

### Architect-Entscheidungen (vier Knackpunkte):

| Frage | Entscheidung | Begründung |
|---|---|---|
| (a) ProviderType-Discriminator: Enum vs. String | **String** (`"tavily"`, `"vector-store"`, …). Offenes System via `IGroundingProviderFactory`-DI-Lookup. Enum würde einen Core-Change pro neuem Provider erfordern. | Provider-agnostisch — AC17-Voraussetzung. |
| (b) Cost-Tracking: Sync in Pipeline vs. Lazy | **Sync in `TavilyGroundingProvider.EnrichAsync`**. `IServiceScopeFactory`-Scope-Pattern (identisch zu PS-7 `AdvisorAwareExecutor`). Kein separater Post-Run-Job nötig, keine verlorenen Kosten bei Absturz. | Captive-Dependency-Fix: Singleton Provider, Scoped Repository. |
| (c) QueryExtraction: Briefing-Prefix vs. eigenständige Query-Extraktion | **Briefing-Text direkt als Query**. Query-Extraktion (eigener LLM-Call vor Tavily) ist PS-8-Scope. Für Phase 1 ausreichend, da Tavily-Synthesized-Answer auch bei langen Briefings sinnvolle Ergebnisse liefert. | Scope-Grenze klar gehalten; Foundation-First. |
| (d) Grounding-Context-Position: Vor vs. Nach Advisor-Block | **Vor Advisor-Block** in `ProfileBasedExecutor`. Web-Fakten sollen für Advisors bereits sichtbar sein, wenn deren Output entsteht (z.B. `briefing-clarifier` kann web-recherchierte Fakten in seine Fragen einbeziehen). GroundingContext → AdvisorBlock → UserPrompt. |  Advisor-before-Executor-Ordering aus PS-7 bleibt konsistent. |

**Foundation-Check (AC17):** `VectorStoreGroundingProvider` kann durch `IGroundingProvider`-Implementation + DI-Registrierung andocken, ohne eine Zeile bestehenden Codes zu ändern. `ProviderSettings: Dictionary<string,string>` trägt beliebige Provider-Konfiguration. `GroundingProviderProfile.ProviderType` ist string. `IGroundingProviderFactory.Create(type)` resolved per Discriminator.

**Tests:** 304 C#-Tests (31 neu: SystemCrewGroundingConstantsTests, CrewServiceGroundingProviderCrudTests, TavilyGroundingProviderTests, MultiProviderGroundingStepTests, KlassikTemplateGroundingRegressionTests), 1 E2E-Skip, 0 Failures. Python-Tests unverändert (43 grün).

**Migration:** `Step13GroundingProviders` — drei Tabellen/Spalten: `GroundingProviderProfiles`, `GroundingConsultations` (mit Cascade-Delete FK auf Runs), `GroundingProviderNames`-JSONB-Spalte auf `CrewTemplates`.

**Deployment-Note:** `TAVILY_API_KEY` muss in `.env` gesetzt werden (optional — leerer Key registriert Provider, wirft aber zur Laufzeit `InvalidOperationException` mit klarer Message, kein App-Crash beim Start). Kein Key in Logs.

---

## D-036 — Feature: Vector-Store-Grounding-Provider (Phase 2 RAG) (2026-05-14)

**Kontext:** Tavily-Step (D-035) hat die `IGroundingProvider`-Foundation mit reservierten `SourceCitation`-Feldern `DocumentReference` und `RelevanceScore` angelegt. Dieser Step aktiviert den zweiten Provider: semantische Suche über hochgeladene Dokumente (Markdown, Text) statt Web-Search. Vollständiges RAG-Setup (Phase 2).

### Architect-Entscheidungen (fünf Knackpunkte):

| Frage | Entscheidung | Begründung |
|---|---|---|
| (a) Postgres-Image: `pgvector/pgvector:pg16` | **Akzeptiert.** `gen_random_uuid()` ist PG16-Core, Encoding/Locale identisch zu `postgres:16-alpine`. Volume bleibt beim Image-Wechsel erhalten. | Offizielles pgvector-Image, kein Vendor-Lock. |
| (b) Embedding-Modell-Default | **`openai/text-embedding-3-small` (1536 dim, ~$0.02/1M Tokens via OpenRouter).** Günstigstes leistungsfähiges OpenAI-Embedding-Modell, via OpenRouter ohne separaten API-Key. `allow_fallbacks: true` für Verfügbarkeit. | Keine neuen Keys nötig (LLM_OPENROUTER_API_KEY wiederverwendet). |
| (c) Chunking-Strategie | **Selbstgebauter `RecursiveCharacterTextSplitter`** (LangChain-kompatibel). Separatoren: `"\n\n"`, `"\n"`, `". "`, `" "`, `""`. ~4 chars/token. Keine externe Library. | `TreatWarningsAsErrors=true` + LangChain.NET ist instabil. Eigene Impl. vollständig testbar. |
| (d) `Pgvector.EntityFrameworkCore 0.3.0` Kompatibilität | **INKOMPATIBEL mit EF Core 10.** Targets net8.0, requires Npgsql.EF ≥9.0.1. LINQ-Distance-Operatoren funktionieren nicht. **Fallback: Raw Npgsql ADO.NET für alle Vector-Operationen.** `float[]` ValueConverter (culture-invariant) für EF-Column-Mapping. | Gleiches Interface `IVectorSearchRepository`, andere Impl. Keine API-Änderung nach außen. |
| (e) HttpClient-Sharing für Embeddings | **Eigener `HttpClient<OpenRouterEmbeddingProvider>`** via `EmbeddingsServiceExtensions`. Selber `LLM_OPENROUTER_API_KEY` aus `LlmOptions`. | Cleaner Scope, keine zirkulären Abhängigkeiten. |

### Implementierungs-Highlights:

- **VectorSearchRepository:** Raw NpgsqlCommand mit `@vec::vector`-Cast (named `@param`-Syntax statt positional `$N`, um Npgsql auto-prepare-Cache-Konflikte zu vermeiden), `<=>` Cosine-Distance-Operator, `&&` Array-Overlap für Tag-Filter (OR-Semantik: mindestens ein Tag muss matchen)
- **float[] ValueConverter:** `string.Join(",", floats)` → `[f1,...fn]` (InvariantCulture beide Richtungen) — pgvector-Literal-Format
- **HNSW-Index:** `CREATE INDEX USING hnsw ("Embedding" vector_cosine_ops)` — ANN für Cosine-Similarity
- **Tag-Filter:** `&&` (Array-Overlap, OR) statt `@>` (Array-Contains-All, AND) — korrekte "mindestens einer"-Semantik
- **Foundation-Check (AC15):** `IGroundingProvider`-Vertrag aus D-035 ohne Refactor wiederverwendet — `VectorStoreGroundingProvider` ist drop-in neben `TavilyGroundingProvider`

### Neue Tabellen (Migration `Step14VectorStore`):

```sql
"KnowledgeDocuments"     -- Dokument-Metadaten + Tags (text[]) + GIN-Index
"KnowledgeDocumentChunks" -- Chunks + "Embedding" vector(1536) + HNSW-Index
CREATE EXTENSION IF NOT EXISTS vector;
```

### Tests:

400 C#-Tests (40 neu: Domain/Application, Embeddings, Repositories, Services, Provider, UI/bUnit, Pipeline/Regression), 1 E2E-Skip, 0 Failures.

### Deployment:

- Backup: `backup/before-pgvector-migration-20260514-120931.dump` (34K, 48 TOC-Entries, PG16)
- Postgres-Image gewechselt auf `pgvector/pgvector:pg16` (PG 16.13, Debian)
- `vector`-Extension 0.8.2 installiert
- Web-Container mit `--no-cache` neu gebaut
- PR #6 gemerged (SHA: `b659912`), Branch `feat/vector-store-grounding` gelöscht
- Produktiv unter `https://geef.stefan-bechtel.de/crew/knowledge`

### Bewusst NICHT in diesem Step:

PDF-Support, Background-Job für Indexing, Multi-Modal-Embeddings, OR-Tag-Filter (UI), Hybrid-Search, Re-Ranking, Embedding-Modell-Wechsel-UI mit Auto-Re-Index, Document-Versionierung, OpenAI-direkt-Integration.

---

## D-037 — Feature: Run-Attachments — Direkter Document-Upload beim Briefing (2026-05-14)

**Kontext:** D-036 (Vector-Store-Grounding) ist ideal für **persistente** Domain-Quellen (Brand-Guidelines, Style-Guides). Für Ad-hoc-Verwendung ("Fasse diesen Bericht zusammen") ist der Upload→Template→Select→Brief-Workflow zu schwergewichtig. Run-Attachments implementiert das ChatGPT/Claude-Standard-Pattern: Dateien direkt beim Briefing anhängen, run-lokal indexiert, automatisch als Grounding-Provider aktiv.

### Architect-Entscheidungen (drei Knackpunkte):

| Frage | Entscheidung | Begründung |
|---|---|---|
| (a) Schema-Strategie: Erweiterung `KnowledgeDocuments` vs. separate Tabelle | **Erweiterung.** `Scope integer NOT NULL DEFAULT 0` + `RunId uuid NULL` + FK auf `Runs.Id`. | Eine Such-Logik, kein JOIN, FK direkt auf Runs, Scope-Filter transparent für `VectorSearchRepository`. Separate Tabelle hätte UI/Such-Logik dupliziert. |
| (b) Run-Persist + Attachment-Upload Sequenz | **Two-Phase: Status=Pending → Attachments → Snapshot-Patch → Queue.** `RunPersistenceService` erhält `UpdateSnapshotAsync` + `MarkRunFailedAsync`. | FK greift nach Run-Create, Pipeline startet erst nach Attachment-Upload. Failure → `MarkRunFailedAsync` = kein orphaned Pending-Run. Strukturell analog zu D-030 `MarkRunFailedAsync` im Orchestrator. |
| (c) Multi-Provider-Vorrang | **`RunAttachmentsProfile` wird per `Prepend` vor alle anderen Provider gehängt (specific > general).** `MultiProviderGroundingStep` respektiert Snapshot-Reihenfolge — keine Änderung nötig. | Custom-Template mit `knowledge-base-default` + Attachments: run-lokale Quellen zuerst, dann globale KB. Analog zu D-031 Advisor-Reihenfolge. |

### Implementierungs-Highlights:

- **`KnowledgeScope`-Enum** (`Global=0`, `RunLocal=1`) — typsicher, default 0 = Backward-Compat für bestehende Docs
- **`SubmitRunRequest`-Record** — `IRunService.SubmitRunAsync` von positional Args auf Record umgestellt (bricht Aufrufer-Signatur sauber statt Parameter-Chaos)
- **`RunAttachmentInput`-Record** mit `byte[]` statt `Stream` — Lifetime-Safe über async Boundaries
- **`VectorSearchRepository`** Raw-SQL: `WHERE (@scopeFilter IS NULL OR d."Scope" = @scopeFilter) AND (@runIdFilter IS NULL OR d."RunId" = @runIdFilter)` — typed `NpgsqlParameter` mit explizitem `NpgsqlDbType` für nullable Parameter
- **Blazor Server + Drag-and-Drop:** `DragEventArgs` serialisiert keine `Files`-Referenz über SignalR — Drag-and-Drop entfernt, UI auf "Click to browse" umgestellt
- **`to_regclass` vs. `::regclass` in Migration:** Ersteres gibt NULL für nicht-existente Tabellen, Letzteres wirft Exception — wichtig für idempotente FK-Guards
- **Tag-Dedup in `PromoteToGlobalAsync`:** `.Union()` statt Spread-Syntax (Spread produziert Duplikate)
- **`ListAsync`-Scope-Filter:** `KnowledgeDocuments` in globaler KB-Ansicht + MCP-Tool passieren explizit `KnowledgeScope.Global` — Run-lokale Attachments bluten nicht durch

### Migration `Step15RunAttachments`:

```sql
ALTER TABLE "KnowledgeDocuments" ADD COLUMN "Scope" integer NOT NULL DEFAULT 0;
ALTER TABLE "KnowledgeDocuments" ADD COLUMN "RunId" uuid NULL;
-- FK via PL/pgSQL-Block (to_regclass-Guard, PostgreSQL 16 kein ADD CONSTRAINT IF NOT EXISTS)
FK REFERENCES "Runs"("Id") ON DELETE CASCADE;
CREATE INDEX "IX_KnowledgeDocuments_RunId" ON "KnowledgeDocuments"("RunId") WHERE "RunId" IS NOT NULL;
CREATE INDEX "IX_KnowledgeDocuments_Scope" ON "KnowledgeDocuments"("Scope");
INSERT INTO "GroundingProviderProfiles" ... ('run-attachments', 'vector-store', ...) ON CONFLICT DO NOTHING;
```

### Tests:

494 C#-Tests (94 neu), 1 E2E-Skip, 0 Failures. Neue Test-Klassen: `KnowledgeDocumentScopeTests`, `SystemCrewRunAttachmentsProfileTests`, `SubmitRunRequestTests`, `Step15RunAttachmentsMigrationTests`, `KnowledgeDocumentRepositoryScopeTests`, `VectorSearchRepositoryScopeTests`, `RunDeleteCascadesAttachmentsTests`, `KnowledgeServiceUploadRunAttachmentTests`, `RunServiceAttachmentTests`, `SubmitRequestToolAttachmentTests`, `AtelierPipelineFactoryWithRunAttachmentsTests`, `KlassikWithAttachmentsTests`, `MultiProviderOrderingTests`, `FileDropZoneTests`, `RunAttachmentsListTests`, `PromoteAttachmentModalTests`.

### Deployment:

- Backup: `backup/before-run-attachments-migration-20260514-165517.dump` (39K, 60 TOC-Entries)
- Web-Container mit `--no-cache` neu gebaut
- PR #7 gemerged (SHA: `f6832f9`), Branch `feat/run-attachments` gelöscht
- Produktiv unter `https://geef.stefan-bechtel.de/new` (FileDropZone sichtbar)

### Bewusst NICHT in diesem Step:

PDF-Support, Background-Job für Attachment-Indexing, Image-Attachments, Auto-Cleanup-Retention-Policy, Attachment-Editierung (Upload-only), Hybrid-Search (pure Vector wie D-036).

---

## D-038 — Template Studio: Meta-KI für Template-Erstellung (2026-05-14)

Neue Seite `/crew/studio`: User beschreibt eine Aufgabe in natürlicher Sprache, eine Meta-KI (Claude Sonnet 4.5 via OpenRouter) analysiert die Aufgabe mit Tool-Use (`submit_template_proposal`), vergleicht sie mit existierenden Templates und Profilen, und schlägt entweder ein bestehendes Template vor oder erstellt ein neues mit passenden Custom-Profilen. User reviewt und editiert im 5-Schritt-Wizard (Input → Analyzing → Review → Edit → Confirmation) **bevor** gespeichert wird. Alle erstellten Records sind danach durch die existierenden Editoren (`/crew/templates/{name}`, `/crew/profiles/.../{name}`) normal bearbeitbar.

### Architektur-Entscheidungen:

| Bereich | Entscheidung |
|---|---|
| **Meta-LLM** | `anthropic/claude-opus-4-7` via OpenRouter (von `claude-sonnet-4-5` hochgestuft, siehe D-043/9); konfigurierbar über `appsettings.json:TemplateStudio:Model` |
| **Strukturiertes Output** | OpenAI Tool-Use (`submit_template_proposal`-Tool mit vollständigem JSON-Schema); kein Freitext-Parsing |
| **Edit-vor-Save-Pflicht** | Wizard erlaubt keinen Skip von Review → Confirmation; Halluzinationsschutz durch Pflicht-Review |
| **Nachträgliche Bearbeitbarkeit** | Studio erstellt nur `custom-`-Records via `ICrewService.CreateCustom…`; System-Profile werden nur referenziert, niemals modifiziert |
| **Profile-Similarity-Check** | On-the-fly Embedding-Cosine-Similarity via `IEmbeddingProvider`; Schwellwert 0.85 → vorgeschlagenes Profil als Duplikat markiert und nicht angelegt |
| **System-Profile-Schutz** | Guard in `TemplateStudioService.ValidateNotSystemProfiles` + bestehender `CrewService.Update`-Check |
| **Modell-Verfügbarkeits-Validation** | `IModelCatalog.ListModelsAsync` pro Provider; fehlende Modelle → Warning, kein Failure |
| **Provider-Verfügbarkeits-Check** | `IProviderCatalog.ListProviders`; fehlende API-Keys → Warning im `MaterializationResult.Warnings` |
| **Audit-Trail** | Neue Tabelle `TemplateStudioAnalyses` (Step17-Migration); JSONB-Spalte `AnalysisResultJson` enthält kompletten `TemplateStudioAnalysis`-Record |
| **Cost-Tracking** | `IPricingCatalog.CalculateCostEur` berechnet Kosten des Meta-LLM-Calls; persistiert in `TemplateStudioAnalyses.CostEur` |
| **Multi-Step-Wizard** | Blazor-State-Machine client-side im `TemplateStudio.razor`; kein Backend-State nötig |
| **Few-Shot-Examples** | 3 Beispiele im System-Prompt: (1) Klassik-Template-Match > 0.85, (2) Neues Template mit existing Profilen, (3) Neues Template mit neuem domänenspezifischen Reviewer |
| **Custom-Profile-Naming** | `custom-`-Prefix automatisch via `ICrewService.CreateCustom…`; Konflikte über EF-DB-Unique-Constraint gefangen |
| **String-Format-Schutz** | `template.Replace("{0}", context)` statt `string.Format` (Prompt enthält `{klassik: 0.95}`-Literals die `FormatException` auslösen) |

### Implementierung:

- **Core:** `Domain/Crew/TemplateStudio/` — 6 neue Records/Enums (TemplateStudioAnalysis, ProposedTemplate, ProposedProfile, TemplateMatch, StudioRecommendation, ProposedProfileType)
- **Persistence:** `Core/Persistence/TemplateStudio/ITemplateStudioAnalysisRepository`, Infrastructure-Entity/-Configuration/-Repository; Migration `Step17TemplateStudio` (manuell als raw SQL, da `dotnet ef` wegen pgvector-ValueComparer-Bug abstürzt)
- **Application:** `ITemplateStudioService`, `MaterializationRequest/Result`, `TemplateStudioOptions`
- **Infrastructure:** `TemplateProposalTool` (JSON-Schema), `TemplateStudioPrompts` (Meta-Prompt + 3 Few-Shot-Examples), `ProfileSimilarityService` (Cosine), `TemplateStudioService` (vollständige Implementation), `TemplateStudioServiceExtensions` (DI)
- **Web:** `TemplateStudio.razor` (`/crew/studio`), `StudioTaskInputStep`, `StudioAnalyzingStep`, `StudioReviewStep`, `StudioEditStep`, `StudioConfirmationStep`, NavMenu-Eintrag "Crew Studio", `New.razor` mit `[SupplyParameterFromQuery]`

### Migration `Step17TemplateStudio`:

```sql
CREATE TABLE "TemplateStudioAnalyses" (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "TaskDescription" text NOT NULL,
    "AnalysisResultJson" jsonb NOT NULL DEFAULT '{}'::jsonb,
    "InputTokens" integer NOT NULL,
    "OutputTokens" integer NOT NULL,
    "CostEur" numeric(10,6) NULL,
    "MaterializedTemplateName" text NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX "IX_TemplateStudioAnalyses_CreatedAt" ON "TemplateStudioAnalyses"("CreatedAt" DESC);
```

### Tests:

562 bestehende C#-Tests unverändert grün + 43 neue Tests. Neue Test-Klassen: `TemplateStudioAnalysisTests`, `TemplateProposalToolTests`, `ProfileSimilarityServiceTests`, `TemplateStudioServiceAnalyzeTests`, `TemplateStudioServiceMaterializeTests`, `TemplateStudioAnalysisRepositoryTests`, `Step17TemplateStudioMigrationTests`, `StudioTaskInputStepTests`, `StudioReviewStepTests`, `StudioEditStepTests`, `StudioConfirmationStepTests`.

### Bewusst NICHT in diesem Step:

MCP-Tool-Erweiterung für Studio, Auto-Run nach Materialization, Studio-Iterationen ("Lass nochmal nachdenken"), Lerneffekte aus Audit-Trail, Custom-Executor-Vorschlag, Cost-Budgets, Bulk-Import von Templates.

---

## D-039: Studio-Extensions (14. Mai 2026)

**Feature:** MCP-API für Template Studio + Analysis-History-Komponente + Welcome-Stats-Erweiterung.

### Entscheidungen:

**1. MCP-Tool-Design:** Zwei separate Tools statt einem kombiniertem — `analyze_template_proposal` (Analyse + Persistierung) und `materialize_template_proposal` (Materialisierung nach User-Review). Trennung ermöglicht asynchronen Workflow: Analyse → Review → Materialisierung mit optionalen Edits dazwischen. Die `AnalysisId` aus Schritt 1 verknüpft beide Aufrufe.

**2. `TemplateStudioHistoryItem` als Core-Record:** Leichtgewichtige Projektion für History-Queries im `Core.Persistence`-Namespace, um die Schicht `Infrastructure → Application` zu vermeiden. Der `ReasoningSummary` wird aus dem `AnalysisResultJson`-JSONB deserialisiert (kein eigenes DB-Feld) — vertretbar für max. 10 Einträge per Page.

**3. `StudioAnalysesPage` und `StudioAnalysisHistoryEntry` im Application-Layer:** Analog zum HasMore-Pagination-Pattern aller anderen List-Endpunkte. Die UI-Komponente injiziert `ITemplateStudioService` direkt — kein separates Controller-Layer.

**4. Studio-Kosten separat von Run-Kosten:** `WelcomeStats` bekommt `StudioAnalysesThisMonth` und `StudioCostThisMonth` als eigene Felder, nicht in `TotalCostThisMonth` aggregiert. Studio-Analysen sind Konfigurationskosten, keine Ausführungskosten — User soll beide Dimensionen separat sehen.

**5. `StudioAnalysisHistoryList`-Komponente ohne StateContainer:** Lokaler State in der Komponente selbst (`_analyses`, `_currentPage`, `_expandedId`). Re-Analyze-Callback via `EventCallback<string>` — Parent (`TemplateStudio.razor`) setzt `_taskDescription` und springt damit zurück in Eingabe-Mode.

### Kein Schema-Change:

Alle Tabellen (insbesondere `TemplateStudioAnalyses`) existieren seit Step17 — kein neuer Migration-Step. `TemplateStudioHistoryItem` ist nur eine Code-Abstraktion, keine DB-Entität.

### Tests:

625 bestehende C#-Tests unverändert grün + 40 neue Tests. Neue Test-Klassen: `AnalyzeTemplateProposalToolTests` (9), `MaterializeTemplateProposalToolTests` (9), `TemplateStudioServiceListRecentAnalysesTests` (7), `StudioAnalysisHistoryListTests` (13), `WelcomeStatsTests` (4). Orchestrator-Timing-Tests (2 Stück) waren bereits pre-existing flaky — bestehen isoliert, schlagen unter Full-Suite durch Thread-Timing-Sensitivität fehl (kein neues Problem).

### Bewusst NICHT in diesem Step:

Auto-Run nach Materialization, Studio-Iterationen, Cost-Budget-Alerts, Bulk-Export von Analyse-Historien, E-Mail-Notification nach abgeschlossener Analyse.

---

## D-040: Grounding-Provider-Profile CRUD-UI Catch-Up (2026-05-15)

**Kontext:** Die Spec für diesen Catch-Up-Step ging davon aus, die Grounding-Provider-CRUD-UI „fehlt komplett". Nach Code-Exploration stellte sich heraus, dass `GroundingProvidersIndex.razor` und `GroundingProviderEditor.razor` bereits vollständig implementiert waren — inkl. System/Custom-Split, Tavily- und Vector-Store-Felder, DataAnnotations-Validierung, Delete-Modal und allen 5 `ICrewService`-Grounding-Methoden. Die Seiten folgten bereits exakt dem Reviewer/Executor/Advisor-Muster. Die eigentliche Lücke lag nicht in der CRUD-Implementierung, sondern in: (a) einem abweichenden Routen-Schema (Spec wollte `/grounding-providers`, `/create`, `/edit/{name}`, `/view/{name}`), (b) fehlenden Gap-Features und (c) fehlendem Dashboard-Eintrag und Tests.

**Entscheidungen:**

**D-040/1 — Routen-Schema:** Spec-Routen übernommen (`/crew/profiles/grounding-providers`, `/create`, `/edit/{name}`, `/view/{name}`). Die bestehenden Routen (`/crew/profiles/grounding`, `/new`, `/{name}`) matchten zwar das echte Reviewer/Executor/Advisor-Muster — aber die User-Entscheidung favorisierte Spec-Konformität. Asymmetrie zu den Geschwister-Seiten (die `/new` und `/{name}` verwenden) wird in einem Folge-Step durch Angleichung der anderen drei Profile-Typen adressiert (Empfehlung: Reviewer/Executor/Advisor auf konsistentes Schema vereinheitlichen).

**D-040/2 — Separate View-Page für System-Profile:** Neues `GroundingProviderView.razor` mit `@page "/crew/profiles/grounding-providers/view/{name}"` statt inline read-only Banner im Editor. Ermöglicht klare System-Präsentation ohne editierbares Formular. Reviewer/Executor/Advisor behalten das Inline-Banner-Pattern — mögliche Aufgabe für Folge-Step-Harmonisierung.

**D-040/3 — Vector-Store Scope-Selector:** `Scope`-Feld (global/run-local/both) im Editor exponiert. Bestehende Custom-Vector-Store-Profile (vor diesem Step erstellt) hatten keinen `Scope`-Schlüssel in `ProviderSettings` — `From()` defaultet auf `"both"` zur Wahrung des bisherigen ungefilterten Verhaltens. Neue Profile: Default `"global"`. Backend-Mapping: `"both"` → `null`-Filter → keine Einschränkung (funktioniert ohne Backend-Änderung, bestätigt durch `VectorStoreGroundingProvider.cs:44-49`).

**D-040/4 — ProviderType immutable bei Edit:** `InputSelect` für ProviderType bekommt `disabled="@(!IsNew)"`. Typ-spezifische Settings (Tavily vs. Vector-Store) sind nicht zwischen Types migrierbar — User muss altes Profil löschen und neues anlegen.

**D-040/5 — Delete-Cascade-Verhalten (verifiziert):** Kein Cascade-Delete aus `CrewTemplates` bei Profil-Löschung. `CrewTemplate.GroundingProviderNames` ist JSONB-String-Array ohne FK-Beziehung zu `GroundingProviderProfiles`. Gelöschte Profile hinterlassen Dangling-Namen in Templates; zur Laufzeit löst `GetGroundingProviderProfileAsync` → `null` auf. Identisch zum Verhalten bei Reviewer/Advisor-Profil-Löschung. `DeleteConfirmationModal` fordert exakte Namens-Eingabe als Sicherheitsschicht. Folge-Step-Empfehlung: Template-Referenz-Listing im Delete-Modal.

**D-040/6 — NavMenu-Eintrag:** Einziger Grounding-Providers-NavLink im NavMenu. Reviewer/Executor/Advisor haben keine NavMenu-Einträge (nur via `/crew`-Dashboard erreichbar). Diese Asymmetrie ist bewusst (Spec-AC #12 explizit). Empfehlung Folge-Step: Crew-Profile-Sektion im NavMenu oder alle vier Typen gleich behandeln.

**Lehre (Ursprung der Lücke):** Die Grounding-Provider-CRUD-Pages wurden beim Tavily-Step (D-035) korrekt mitimplementiert — aber ohne CrewIndex-Dashboard-Eintrag, ohne bUnit-UI-Tests und ohne Routen-Schema-Alignment auf die Spec. Kein Page-Code wurde vergessen; die organisatorische Lücke lag in fehlenden Routen-Konventionen und Test-Coverage.

---

## 16. Mai 2026 — MCP OAuth 2.1 Authorization Server (PS-9-Erweiterung)

### D-041: Self-hosted OAuth 2.1 AS für Claude Desktop / Claude.ai Custom Connectors

**Datum:** 16. Mai 2026
**Bericht:** [reports/feature-mcp-oauth-report.md](reports/feature-mcp-oauth-report.md)
**Branch:** `feat/mcp-oauth` → PR gegen `main`
**Reviewer-Iterationen:** 9 (Iteration 9: alle fünf R1–R5 mit 0 Findings)
**Tests:** 803 grün (+ 1 bekannter E2E-Flake-Skip), 92 neue OAuth-Tests. **14 Conventional-Commits.**

**Kontext:** Claude Code CLI funktioniert mit statischem Bearer-Token (`ATELIER_MCP_TOKEN`). Claude Desktop Custom Connectors und Claude.ai Web Custom Connectors sprechen ausschließlich OAuth — statisches Bearer-Token reicht dort nicht. Ziel: self-hosted minimaler OAuth-2.1-Authorization-Server, der neben dem statischen Auth weiterläuft.

**Kernentscheidungen:**

**D-041/1 — Self-hosted AS statt externem IdP:** OAuth-2.1-AS direkt in Geef.Atelier implementiert (kein Keycloak, Auth0 etc.). Begründung: Single-User-Kontext, kein Deployment-Overhead, volle Kontrolle über Token-Lebenszyklus. Scope ausschließlich `mcp:full` (kein Multi-Scope-Design).

**D-041/2 — Opaque Tokens + SHA-256 statt JWT:** Tokens sind kryptografisch zufällige 32-Byte-Strings (Base64Url), in der DB ausschließlich als SHA-256-Hash gespeichert (`RandomNumberGenerator.GetBytes(32)`). Vorteil: kein JWKS-Endpoint, keine asymmetrische Kryptographie-Infrastruktur, Token-Revocation einfach (Hash in DB markieren), keine Tokengröße im HTTP-Header.

**D-041/3 — PKCE strikt S256, kein `plain`:** `code_challenge_method=plain` wird abgelehnt. Authorization-Server erzwingt S256 per `string.Equals(..., StringComparison.Ordinal)`-Check. Public Clients (keine Client-Secrets) — `token_endpoint_auth_method=none`.

**D-041/4 — Loopback-Sonderregel (RFC 8252):** Redirect-URIs werden exakt verglichen. Ausnahme: `http://127.0.0.1`-URIs erlauben beliebigen Port (RFC 8252 §7.3). Nie auf andere Hosts angewandt. `localhost` wird nicht als Loopback behandelt (nur `127.0.0.1`).

**D-041/5 — Refresh-Token-Rotation + Reuse-Detection:** Jeder Token-Refresh gibt ein neues Paar (Access + Refresh) aus und invalidiert den alten Refresh-Token atomisch via SQL `UPDATE WHERE UsedAt IS NULL`. Erneuter Einsatz eines bereits verbrauchten Refresh-Tokens (Diebstahl-Indikator per RFC 6819) löst sofortige Revocation aller aktiven Tokens des Users aus (`RevokeAllUserTokensAsync`).

**D-041/6 — Endpoint-Mapping Minimal-API + Razor-Consent:** Maschinelle OAuth-Endpoints als Minimal-API-Extensions (`OAuthEndpoints.cs`, `WellKnownEndpoints.cs`) — kein MVC-Stack. `GET /oauth/authorize` als Razor-Page (`OAuthAuthorize.razor`, `@attribute [Authorize]`, Static SSR) — nutzt bestehende Cookie-Auth + Return-URL-Mechanismus von `Login.razor` ohne zusätzliches Plumbing.

**D-041/7 — `ITokenValidator` evolviert zu `TokenValidationOutcome`:** Interface-Result erweitert von `bool` auf `TokenValidationOutcome { IsValid, Kind, Subject, ClientId, Scope }`. `StaticTokenValidator` → `Kind="static-bearer"` (Verhalten identisch). `OAuthAccessTokenValidator` neu. `CompositeTokenValidator` als neue `ITokenValidator`-Registrierung: erst statisch, dann OAuth. `BearerTokenHandler` baut Claims aus Outcome. Backwards-Compat: statischer Pfad bit-identisch zu vorher.

**D-041/8 — Backwards-Compat Claude Code CLI:** `ATELIER_MCP_TOKEN` weiterhin voll funktional. `CompositeTokenValidator` prüft statisches Token zuerst — Claude Code CLI-Requests passieren den OAuth-Pfad nie. Beide Auth-Pfade koexistieren ohne Konfigurationsänderung.

**D-041/9 — Audit-Log + Cleanup-BackgroundService:** Alle relevanten OAuth-Operationen schreiben `OAuthAuditLogEntry` (5 Tabellen, Migration `Step19McpOAuth`). `OAuthCleanupBackgroundService` löscht abgelaufene Auth-Codes und Access-/Refresh-Tokens; Audit-Log bleibt permanent (Forensik).

**Akzeptierte Abweichungen vom Blueprint (dokumentiert in `geef_architecture.md`):**
- `token_type_hint` in Revocation: SHOULD laut RFC 7009, nicht implementiert (akzeptiert)
- `scopes_supported` im Metadata-Endpoint: String statt Array (akzeptiert, da nur ein Scope)
- TOCTOU-Fenster zwischen `FindByXxxAsync` und `ConsumeAsync`: kein exploitbares Sicherheitsproblem (atomare `ConsumeAsync`-Implementierung via `UPDATE WHERE UsedAt IS NULL`; akzeptiert)

---

## D-042 — Run-Sichtbarkeits-Isolation (User Run Isolation)

*Datum: 17. Mai 2026 | Bericht: [reports/feature-run-user-isolation-report.md](reports/feature-run-user-isolation-report.md)*

Run-Sichtbarkeit auf eigenen Account eingeschränkt. Admin-Override via explizite Toggles ("Alle Benutzer anzeigen", "System-weite Statistiken anzeigen"). Grundlage: OAuth (Step19) + Multi-User (Step20).

**D-042/1 — Username als Isolation-Schlüssel:** `Runs.CreatedByUser` (String) als User-Identifier beibehalten. Kein Wechsel auf UserId-FK — Username ist seit Step20 stabil, Migration wäre rein kosmetisch ohne Sicherheitsgewinn.

**D-042/2 — null-Semantik für Admin-Bypass:** `requestingUsername = null` bedeutet systemweit in allen Service- und Repository-Methoden. Caller entscheidet (nicht der Service) anhand der User-Claims ob Admin-Mode. Vermeidet Code-Duplikation zwischen Web und MCP.

**D-042/3 — 403 für Web-UI, generische Meldung für MCP:** RunDetail zeigt explizite 403-Seite ("Kein Zugriff — dieser Run gehört einem anderen Benutzer") — klare UX. MCP-Tools geben `null` zurück (= "Run not found or access denied") — verhindert Run-Existenz-Leak via API.

**D-042/4 — Static-Bearer-Token → Admin-Mapping:** Runs via `ATELIER_MCP_TOKEN` (Claude Code CLI) werden dem konfigurierten Admin-User zugeordnet (neu: `ATELIER_MCP__STATIC_TOKEN_USER`, Default: `ATELIER_USER`). Kein Backfill bestehender "mcp-client"-Runs. Sicherheits-Fix: Timing-Leak (Length-Pre-Check vor `FixedTimeEquals`) entfernt.

**D-042/5 — Welcome-Stats-Default für Admin: eigene Stats:** Admin sieht standardmäßig eigene Stats; system-weite via Toggle. Verhindert unbeabsichtigte Privacy-Implikationen bei Multi-User-Setup.

**D-042/6 — Kein Cascade-Delete bei User-Löschung:** Runs bleiben in der DB erhalten wenn User gelöscht wird. Admin sieht verwaiste Runs. Runs sind historische Wertdaten — destruktiver Auto-Delete inakzeptabel.

**D-042/7 — Index `IX_Runs_CreatedByUser`:** Migration Step21 fügt Index auf `Runs.CreatedByUser` hinzu. Filter-Performance bei wachsender Run-Zahl.

---

## D-043 — Template Studio Complete Edit (vollständiger Edit-Flow)

*(2026-05-18 — Branch `feat/studio-complete-edit`, PR #16, Merge-SHA `ee317e7`)*

Das Template Studio (`/crew/studio`) hatte bisher nur minimale Edit-Felder im `StudioEditStep` (Template-DisplayName, je Profil nur SystemPrompt). Provider, Modell, MaxTokens, Advisor-Mode/Trigger, Grounding-Settings, UseExisting/CreateNew-Toggle pro Slot und Field-Helps fehlten — Nutzer mussten nach einer Studio-Analyse in die separaten `/crew/profiles/…`-Editoren wechseln. D-043 liefert vollständige Feld-Parität ohne Nachbearbeitung.

**D-043/1 — Lean Extension: flaches Domain-Modell, additive nullable Reasoning-Felder.** `ProposedProfile` und `ProposedTemplate` bleiben flache Records mit String-Namen-Referenzen. Additive nullable Felder ergänzt: `ModelReasoning?`, `SystemPromptReasoning?`, `OverallReasoning?`, `ModeReasoning?`, `TriggerReasoning?` auf `ProposedProfile`; `EvaluationStrategyReasoning?` auf `ProposedTemplate`. Backwards-Compat: alte `AnalysisResultJson` (ohne Reasoning) deserialisiert weiterhin korrekt — neue Felder defaulten auf `null`. Keine Wrapper-Records, keine DB-Migration.

**D-043/2 — Lean Reuse: existierende Komponenten unverändert.** `ModelSelector.razor`, `ReviewerPicker`, `AdvisorPicker`, `GroundingProviderPicker`, `ProfileEditorForm.razor` und alle Live-Editor-Pages bleiben unverändert (kein Regressionsrisiko). Das Studio erhält eine eigene `StudioProfileSlot.razor` (Picker↔Inline-Toggle, kein per-Slot-Submit, kontinuierliches Binding), die `ModelSelector` und Field-Helps einbettet.

**D-043/3 — UseExisting vs. CreateNew = UI-State only.** `StudioSlotMode`-Enum (`UseExisting`/`CreateNew`) lebt in `StudioEditStep.razor` als private `SlotState`-Klasse. CreateNew → Draft landet in `FinalNewProfiles` + Name in `FinalTemplate.*Names`. UseExisting → Name zeigt auf existierendes Profil, kein Eintrag in `FinalNewProfiles`. Domain-API (`MaterializationRequest`) unverändert.

**D-043/4 — Field-Help-Resource: zentral, Deutsch, Spec-verbatim.** `StudioFieldHelps.cs` (statische Konstanten) hält alle Field-Help-Texte auf Deutsch. `FieldHelp.razor` rendert Inline-Hinweise unterhalb jedes Feldes. Jedes Studio-Edit-Feld (Template und alle Profil-Typen) hat einen FieldHelp. Keine Inline-Help-Strings in Komponenten.

**D-043/5 — Defaults in `appsettings.json TemplateStudio:Defaults`.** Neue `StudioDefaults`-Unterklasse von `TemplateStudioOptions` (Reviewer/Executor/Advisor/GroundingProvider/EvaluationStrategy-Defaults). Werte gegen existierende System-Profile kalibriert. LLM-null-Felder werden in `TemplateStudioService.ApplyDefaults` aus Defaults befüllt.

**D-043/6 — Additives Tool-/MCP-Schema, Backwards-Compat.** `TemplateProposalTool.cs`-Schema: `profile_type`-Enum um `"executor"` erweitert; optionale Reasoning-Properties ergänzt. `ProposedTemplateDto`/`ProposedProfileDto` um nullable Reasoning-Felder erweitert (Default `null`). Alte MCP-Inputs ohne Reasoning oder ohne Executor-Type funktionieren weiterhin.

**D-043/7 — Atomare Materialisierung via `IAtomicTransactionFactory`.** `TemplateStudioService.MaterializeAsync` nutzt `IAtomicTransactionFactory` (testbare Abstraktion über EF-Core-`AtelierDbContext`-Transaktion). Ablauf: vollständige Validierung → Begin → Profile anlegen → Template anlegen → Commit; explizites `RollbackAsync` bei jedem Fehler. `CreateProfileAsync` um Executor-Zweig erweitert. `MarkMaterializedAsync` innerhalb der Transaktion (korrekt: wird bei fehlgeschlagenem Commit zurückgerollt). `GetDefaultMaxTokens` gibt `null` für GroundingProvider zurück (kein LLM-MaxTokens). `ValidateAvailabilityAsync` überspringt GroundingProvider-Profile (nutzen Tavily/VectorStore, keine LLM-Modelle).

**D-043/8 — Akzeptierte Architektur-Abweichungen.** Slot-State und `BuildRequest()` leben in `StudioEditStep`, nicht in `TemplateStudio.razor` (architektonisch sauberere Kapselung). `bool ShowValidation` statt `ValidationMessages`-Store (einfacher, korrekt). `GroundingProviderProvider = "openrouter"` als Default — semantisch übernehmen Grounding-Profile diesen Wert, er wird in `CreateProfileAsync` für Grounding nicht genutzt (kein LLM-Aufruf). `IAtomicTransactionFactory`-Abstraktion statt direkter `AppDbContext`-Injection (architektonisch überlegen). Diese Abweichungen wurden im Eval-Loop geprüft und akzeptiert.

**D-043/9 — Post-Deploy-Korrekturen (2026-05-18, nach Browser-Smoke).** Drei Qualitätsmängel traten beim Testen des deployten Studios auf und wurden in Folge-Commits behoben:
- **Generierte Profil-Prompts folgen jetzt der vollständigen Atelier-Profil-Anatomie.** Der Meta-Prompt limitiert System-Prompts nicht mehr auf „100 Wörter"; er schreibt dieselbe Struktur vor wie die handgeschriebenen System-Profile in `SystemPrompts.cs` — Reviewer: Rollen-Zeile, `submit_review`-Anweisung, 4-stufige Severity-Taxonomie mit konkreten domänenspezifischen Beispielen, ANTI-PATTERN-Kalibrierung, Domänen-Fokus-Checkliste, „Respond in the language of the user briefing"; Advisor: Rollen-Zeile, „strategic guidance only", nummerierte 2–5-Beobachtungsliste, Kürze-Regel, Iterations-Varianz-Regel bei `BeforeEveryExecution`. Ein vollständiges Reviewer+Advisor-Beispiel wurde dem Few-Shot ergänzt. Das Advisor-abratende Prinzip wurde umgekehrt: Advisors werden jetzt aktiv ermutigt, wann immer die Aufgabe von Vorab-/Pro-Iteration-Beratung profitiert.
- **Meta-LLM hochgestuft** von `anthropic/claude-sonnet-4-5` auf `anthropic/claude-opus-4-7`; `TemplateStudio:MaxTokens` 4096 → 8192 (ein Tool-Call trägt jetzt mehrere vollständig strukturierte Prompts).
- **MaxTokens-Floor.** `StudioDefaults.MinMaxTokens = 10000` zieht in `ApplyDefaults` jedes generierende Profil auf diesen Floor hoch, auch wenn die Meta-LLM einen kleineren Wert vorschlägt (GroundingProvider bleibt `null`). Studio-Defaults erhöht: Reviewer/Advisor `MaxTokens` 2048 → 16384, Executor 4096 → 60000. Derselbe Zu-niedrig-Default betraf über `LlmOptions.DefaultMaxTokens` alle `MaxTokens: null`-System-Profile, erhöht 4096 → 16384; explizite Actor-Configs `BriefingTreueReviewer`/`KlarheitReviewer` 2048 → 16384, `Executor` 8192 → 60000. `GroundingQueryExtractor` bleibt bewusst bei 256 (extrahiert nur eine kurze Suchanfrage).

---

## D-044 — Finalizer-Foundation: Fünfter Profil-Typ, vollständige System-Library

*Datum: 19. Mai 2026 | Bericht: [reports/feature-finalizer-foundation-report.md](reports/feature-finalizer-foundation-report.md)*

Ergänzt eine vollständige Finalizer-Phase als fünften Profil-Typ (FileExport, MetadataEnrich, ExternalSink, Transform) mit 17 System-Profilen, sortierbarer Pipeline-Kette in Templates, Run-Artifacts-Speicherung + Download-Endpoint, vollständiger CRUD-UI, Studio-Integration und MCP-Tools. Vervollständigt das „F" im GEEF-Akronym.

**D-044/1 — Flaches Domain-Modell, GroundingProvider-Pattern.** `FinalizerProfile` ist ein flacher Record mit `Dictionary<string,string>` Settings (JSONB) und `IsSystem`. Kein Wrapper-Domain-Modell. Typisierte Settings-Records (`FileExportSettings`, `MetadataEnrichSettings`, `WebhookSinkSettings`, `EmailSinkSettings`, `TransformSettings`) kapseln das Dict in allen Executor-Implementierungen. Begründung: Konsistenz mit dem bestehenden GroundingProvider-Pattern; vermeidet eine zweite Abstraktionsebene.

**D-044/2 — Separate `FinalizationActorCosts`-Tabelle (keine `IterationActorCosts`-Erweiterung).** Eine neue `FinalizationActorCosts`-Tabelle (Id, RunId FK CASCADE, ActorName, ModelName, InputTokens, OutputTokens, CostEur, CreatedAt) ersetzt die Erweiterung von `IterationActorCosts`. `IterationActorCosts.IterationId` ist ein Pflicht-FK — ein nullable FK würde Fragilität und semantische Verschmutzung einführen. Finalizer-Kosten aggregieren zu `Runs.FinalizerCostEur`. Begründung: sauberere Trennung, keine FK-Migration auf der bestehenden Iteration-Tabelle.

**D-044/3 — Partial-Success-Vertrag (kein neuer RunStatus-Enum-Wert).** Finalizer-Fehler erzeugen ein `ArtifactType.Status`-Artifact und setzen `Runs.FinalizerErrorMessage`. Der Run-Status bleibt `Completed`. Kein `CompletedWithFinalizerErrors`-Enum-Wert wird eingeführt (vermeidet Enum-Proliferation und nachgelagerte Serialisierungs-Migration). Der Fehler ist in der RunDetail-UI über die Artifacts-Sektion sichtbar.

**D-044/4 — Pipeline-Einfügung: FinalText aus der DB nachladen.** `ExecuteFinalizationAsync` wird im `RunOrchestratorService` nach `FinalizeRunCostsAsync` eingefügt. Es lädt die Run-Entity aus der Datenbank neu, weil `PostgresEventSink` den `FinalText` direkt beim `PipelineCompletedEvent` schreibt — der Orchestrator hält ihn nicht im Speicher. Beim Grounding entdeckt (Spec-Pseudocode war falsch).

**D-044/5 — MaxAttempts-Pfad deckt Advisor-Retry-Fehlschlag ab.** Wenn `RunFinalizersOnMaxAttempts=true`, laufen Finalizer nicht nur, wenn die Haupt-Pipeline nicht konvergiert, sondern auch, wenn der `OnConvergenceFailure`-Advisor-Retry ebenfalls fehlschlägt. Das äußere `catch (ConvergenceFailedException)` umschließt `TryConvergenceFailureRetryAsync` jetzt mit einem verschachtelten try-catch, setzt `retryAlsoFailed=true` und fährt mit `ExecuteFinalizationOnMaxAttemptsAsync` fort. In der Evaluation gefunden (R1-Reviewer).

**D-044/6 — System-Profil-Namen: bewusste Abweichung vom Spec-Entwurf.** Die Spec listete Namen wie `add-frontmatter`, `send-webhook`, `de-hedging`, `length-adjust-500`. Die Implementierung nutzt aussagekräftigere, intern konsistente Namen: `add-front-matter`, `webhook-sink`, `tone-formalization`, `tone-casual`, `executive-summary`, `key-takeaways`, `glossary`, `add-reading-level`. Das Table-of-Contents-Profil (`add-toc`) wurde nicht implementiert; `add-reading-level` wurde stattdessen ergänzt. Begründung: die implementierten Namen sind für Endnutzer klarer, intern konsistent (durchgängig lowercase-hyphenated), und das System ist selbstdokumentierend. Zum Einführungszeitpunkt hängen keine bestehenden Daten von diesen Namen ab. Anerkannt per D-044 (vom R1-Reviewer gefunden).

**D-044/7 — ExportPath-Containment + GUID-basierte Dateipfade (Path-Traversal-Prävention).** Der Download-Endpoint validiert, dass der aufgelöste `Path.GetFullPath(StorageUri)` mit `Path.GetFullPath(ExportPath)` beginnt. Dateipfade werden als `{ExportPath}/{runId:N}/{filename}` konstruiert, wobei `runId` aus dem GUID-typisierten Routen-Parameter stammt und `filename` aus einem DB-gespeicherten Wert (niemals aus Nutzereingabe). Defense-in-Depth: sowohl die GUID-Routen-Constraint als auch der `Path.GetFullPath`-Containment-Check müssen bestehen.

**D-044/8 — Webhook-Auth-Header im Klartext gespeichert.** Der Webhook-Authentifizierungs-Header wird als Klartext in der `FinalizerProfiles`-JSONB-Spalte gespeichert und bei jedem Run in den `CrewSnapshot` eingebettet. Er wird niemals geloggt oder in Status-Artifacts aufgenommen. Verschlüsselung at-rest (z.B. `IDataProtectionProvider`) wurde erwogen, aber zurückgestellt: der Auth-Header ist ein Bearer-Token niedriger Sensitivität (Webhook-Endpoint-Sicherheit), kein Nutzer-Credential, und die Produktions-Datenbank liegt in einem privaten Netzwerk. UI-Hinweise von „verschlüsselt gespeichert" zu „im Klartext in der Datenbank gespeichert" korrigiert (vom R2-Reviewer gefunden).

**D-044/9 — MaxFileSizeBytes vor dem Schreiben erzwungen.** `FileExportFinalizerExecutor` prüft `bytes.Length > _options.MaxFileSizeBytes` nach der Konvertierung, aber vor dem Schreiben auf die Platte. Erzeugt bei Verletzung ein `Status`-Artifact. Default: 50 MB. Vom R2-Reviewer gefunden (war in den Options definiert, aber im ersten Durchlauf nicht erzwungen).

---

## D-045 — Modell-gesteuerte Websuche auf den CLI-Providern

*Datum: 19. Mai 2026*

Die Provider `claude-cli` und `codex-cli` laufen jetzt mit aktivierter Websuche, sodass ein Akteur während der Generierung selbst aktuelle Web-Informationen holen kann. `claude` wird mit `--allowedTools "WebSearch,WebFetch"` aufgerufen (nur Web-Tools — kein Bash/Edit/Write, kein voller Permission-Bypass); `codex` mit dem globalen `--search`-Flag **vor** dem `exec`-Subcommand (`codex exec` lehnt es als Subcommand-Argument ab — im Live-Test entdeckt, das Flag ist global). Kosten sind abo-gedeckt (keine Per-Search-Abrechnung).

**Bewusster Trade-off:** Vom Modell gesuchte Quellen werden intern verarbeitet und **nicht** in `GroundingConsultation` / RunDetail erfasst — die CLI-Websuche hat keine Citation- oder Nachvollziehbarkeits-Spur. Das wurde zugunsten einer minimalen Zwei-Zeilen-Adapter-Änderung gegenüber einer Tavily-als-MCP-Tool-Pipeline akzeptiert. Der Tavily-Grounding-Provider bleibt der Pfad für explizite, deterministische, kostengetrackte, zitierbare Vor-Briefing-Anreicherung; beide ergänzen sich. OpenRouter-geroutete Akteure (single-shot `OpenAiCompatibleClient`) können keine agentische Websuche durchführen. Sicherheit unverändert: codex `--search` ohne Per-Call-Approval, claude allowlistet nur Web-Tools — kein neues Interaktions-/Permission-Prompt-Risiko.

---

## D-046 — Run-Resume: Fortsetzen wo aufgehört

*Datum: 19. Mai 2026 | PR #18 auf `main` gemerged*

Wenn ein Run mit Status `Aborted` oder `Failed` endet, kann der Nutzer ihn jetzt mit einem Klick neu starten. Zwei Modi stehen zur Verfügung: **Seed-Modus** (`ArtifactText` der letzten abgeschlossenen Iteration wird als Seed-Draft in Iteration 1 des neuen Runs injiziert — der Executor überarbeitet statt von Scratch zu beginnen) und **Clean-Modus** (identisches Briefing und Crew, frische Pipeline). Ein optionaler `MaxIterationsOverride` erlaubt die Anpassung der Konvergenz-Grenze für den fortgesetzten Run.

**D-046/1 — Seed-Draft via neuen Grounding-Step, kein Mid-Pipeline-Checkpoint.** Das Geef-SDK hat keinen Resume-from-Checkpoint-Mechanismus. Stattdessen wird `SeedDraftGroundingStep` (implementiert `IGroundingStep`) als Grounding-Step für fortgesetzte Runs genutzt. Er setzt zwei Context-Keys: `AtelierContextKeys.GroundedBrief` (Briefing-Text) und `AtelierContextKeys.SeedDraft` (ArtifactText der letzten abgeschlossenen Iteration). In Iteration 1 liest `ProfileBasedExecutor` den `SeedDraft`-Key und nutzt einen „überarbeite diesen unterbrochenen Draft"-Prompt statt „schreibe von Scratch". Ab Iteration 2 wird der Key ignoriert. Begründung: minimale SDK-Kopplung, keine neue Abstraktion über den bestehenden `IGroundingStep`-Vertrag hinaus.

**D-046/2 — `AtelierPipelineFactory.BuildWithSeedDraft` spiegelt `BuildWithAdvisorContext`.** Eine neue statische Factory-Methode mit denselben optionalen Parametern (loggerFactory, additionalSinks, groundingProviderFactory, pricingCatalog, costAccumulator), aber `SeedDraftGroundingStep` statt des normalen Briefing-Grounding-Steps. Der Orchestrator dispatcht auf diesen Pfad, wenn `run.SeedDraftText is not null`. Advisor-Pässe und Cost-Tracking funktionieren identisch zum normalen Pfad.

**D-046/3 — `MaxIterationsOverride` in `CrewSnapshot.ConvergenceOverride` gepatcht.** Das `CrewSnapshot`-JSON des Parent-Runs wird deserialisiert, das `ConvergenceOverride.MaxIterations`-Feld mit dem Override-Wert aktualisiert (oder ein neuer `ConvergenceOverride`-Record erstellt, falls `null`), und der gepatchte Snapshot für den fortgesetzten Run re-serialisiert. Begründung: Konvergenz-Settings leben in `CrewSnapshot`, nicht in `ConfigJson` — der Snapshot-Deserialisierungs-Pfad hält alle Crew-Logik an einem Ort.

**D-046/4 — Owner-Check + Non-Resumable-Status-Guard in `RunService.ResumeRunAsync`.** Der Service lehnt Resume-Versuche ab, wenn: (a) der Parent-Run nicht gefunden wird, (b) der anfragende Nutzer nicht der Besitzer ist (außer `requestingUsername` ist `null` für Admin-Bypass), oder (c) der Run-Status nicht `Aborted` oder `Failed` ist. Der Check nutzt dieselbe `null`-Username-Semantik wie D-042/2 (Admin-Bypass für MCP).

**D-046/5 — `ResumeRunDialog` als modales Blazor-Komponent.** `[EditorRequired, Parameter] Guid ParentRunId`, `bool HasIterations` (steuert Vorauswahl des Modus), `int DefaultMaxIterations`, `EventCallback<ResumeOptions> OnConfirm`, `EventCallback OnCancel`. Seed-Modus ist vorausgewählt wenn `HasIterations=true`; Clean-Modus sonst. `MaxIterationsOverride` ist `null` wenn das Feld `0` oder leer ist (Fallback auf die Konvergenz-Policy des Runs). `data-testid`-Attribute auf allen interaktiven Elementen für bUnit-Tests.

**D-046/6 — Migration `Step23RunResume`.** Zwei neue nullable Spalten auf `Runs`: `ParentRunId uuid NULL` (FK auf `Runs.Id` mit `ON DELETE SET NULL`) und `SeedDraftText text NULL`. Index `IX_Runs_ParentRunId` für Parent→Children-Lookups.

**Tests:** 1021 (1017 grün + 4 bekannte Playwright-Flakes). Neue Test-Klassen: `RunServiceResumeTests` (12 Tests), `SeedDraftGroundingStepTests` (3 Tests), `ProfileBasedExecutorSeedDraftTests` (3 Tests), `ResumeRunDialogTests` (10 Tests).
