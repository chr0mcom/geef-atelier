# Decisions Log

*Letzte Aktualisierung: 2026-05-13 (PS-7 Advisor-Pässe: D-031 ergänzt)*

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
