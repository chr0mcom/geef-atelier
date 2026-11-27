# Decisions Log

*[English](05-decisions-log.md) · **Deutsch***

*Letzte Aktualisierung: 2026-11-27 (Zusätze zu D-090 und D-092 nach der abschließenden Doku-Prüfung; D-092: Datenbank und Dateiablage aufgeräumt, eine Baseline-Migration; zweiter Zusatz zu D-087: „Extra careful“ entfällt; D-091: Rückbau, Schnitt 2, nur der Standardweg bleibt; zuvor D-090: Gateway-Failover nur auf dasselbe Modell; D-089: Rückbau, Schnitt 1; D-088: MCP-Skill und Rückfragen als Formular; D-085: ältere Prüfschleife, drei Schutzschalter; D-087: „Extra careful“ mit Gliederung hinter Schalter, Schleifen-Schalter je Konto, Messung vorbereitet; zuvor D-086: MCP-Server-Hinweise unter der Kürzungsgrenze, SDK exakt 2.4.0; zuvor 2026-11-26, D-083: Gedächtnis, kleinste Form, „Tell Atelier once“; D-077: Abschluss des Umbaus, Etappen 1b, 7, 8 und 9 nicht gebaut, Etappe 8a Werkstatt-Einstiege nur für Admins; D-076: Etappe 6, Dateien über MCP mit Upload-Ticket und Ablehnungszählung; zuvor 2026-11-24, Zusatz zu D-070: konservative Längenprüfung und Upload-Antworten; D-069 ergänzt: Etappe 1 Direktweg-Engine; D-070 ergänzt: Etappe 1 Aufträge, Quellen und Anzeigezustand; D-067 ergänzt: Etappe 0 Sicherheitsnetz; D-068 ergänzt: Richtung Umbau mit Auftrag, Direktweg, Rückfragen, Quellen und Gedächtnis; zuvor 2026-11-23, D-066/12 ergänzt: explizites `model_domain`, ein Zuweisungsprädikat; zuvor D-066/10–11: Codex-Always-latest-Aliase, nächtlicher Host-Abgleich, Härtung nach Review; zuvor 2026-11-22, D-066 ergänzt: Modellmatrix nach Rolle und Gebiet, Claude/OpenAI nur über den cli-proxy; zuvor 2026-11-12, D-065 ergänzt: Bilderzeugung im cli-proxy; zuvor 2026-08-04, D-064 ergänzt: dynamischer Modell-Katalog für CLI-Provider, inkl. Wächter-Regel und Post-Deploy-Checkliste)*

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
Die öffentliche Domain der Installation, hinter Traefik mit TLS.

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

**Kontext:** Alle drei Provider-Aufrufe liefen bisher über OpenRouter (Pay-as-you-go). Auf dem Atelier-Server sind `claude` (Claude Code CLI, Subscription) und `codex` (OpenAI Codex CLI, Subscription) installiert — Subscription-Kapazität ohne Token-Abrechnung. Ziel: zweiter Provider-Pfad via neuem Side-Container, der diese CLIs als OpenAI-kompatibles HTTP exposed.

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

- Vor der Migration wurde die Datenbank gesichert.
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

- Vor der Migration wurde die Datenbank gesichert.
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

**D-043/4 — Field-Help-Resource: zentral, Deutsch, Spec-verbatim.** *(Sprache überholt durch D-062 — Field-Helps sind jetzt englisch.)* `StudioFieldHelps.cs` (statische Konstanten) hält alle Field-Help-Texte. `FieldHelp.razor` rendert Inline-Hinweise unterhalb jedes Feldes. Jedes Studio-Edit-Feld (Template und alle Profil-Typen) hat einen FieldHelp. Keine Inline-Help-Strings in Komponenten.

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

---

## D-047 — Custom Providers: CRUD-Entity für HTTP- und CLI-Provider

*Datum: 19. Mai 2026*

Provider werden zur sechsten CRUD-Entity in Geef.Atelier — anlegbar, editierbar, deaktivierbar und löschbar über die UI. Zwei Typen: **HTTP** (OpenAI-kompatibler Endpoint mit konfigurierbarem Auth-Header, optionalem models-Endpoint, Manual-Model-List und Cost-Tracking-Feldern) und **CLI** (Subprocess via cli-proxy mit vier CLI-Kinds: Claude, Codex, Gemini, Generic). 11 eingebaute System-Provider (8 HTTP + 3 CLI) ersetzen das bisherige statische `ProviderCatalog`-Dictionary. Der `IProviderService` (DB-basiert) ist ab sofort die primäre Quelle; `IProviderCatalog` bleibt als dünner Wrapper für bestehende Callsites (Legacy-Shim, nicht neu verwenden). Migration `Step24CustomProviders`.

**D-047/1 — `anthropic-direct` wird nicht als HTTP-Provider bereitgestellt.** Die Anthropic-API nutzt ein anderes Request-Format als die OpenAI-kompatible REST-Schicht. Ein generischer HTTP-Provider würde `system`, `thinking`-Blocks und die `anthropic-version`-Header-Konvention nicht korrekt abbilden. Anthropic bleibt ausschließlich als `claude-cli`-System-Provider erreichbar. Rationale: korrekte Ergebnisse vor Vollständigkeit.

**D-047/2 — `opencode-cli` wird nicht als eingebauter CLI-Provider geliefert.** Der Installationsmechanismus von OpenCode im Docker-Container war zum Zeitpunkt der Implementierung nicht stabil verifiziert. Nutzer können OpenCode als Custom-CLI-Provider mit `cli_kind=generic` selbst konfigurieren, sobald der Binary lokal verfügbar ist. Eine zukünftige Entscheidung kann OpenCode als System-Provider nachrüsten, sobald ein reproduzierbares Container-Build-Verfahren vorliegt.

**D-047/3 — Settings werden als `Dictionary<string, JsonElement>` (JSONB) gespeichert; typisierte Wrapper-Records lesen daraus.** `HttpProviderSettings.FromSettings()` und `CliProviderSettings.FromSettings()` sind pure Factory-Methoden ohne Seiteneffekte. Das JSONB-Format erlaubt additive Erweiterungen ohne Migrations-Churn; der Code bleibt typsicher durch die Records. Der `Provider`-Domain-Record selbst ist persistenz-frei (kein EF-Attribut).

**D-047/4 — System-Provider sind unveränderlich und nicht löschbar; Custom-Provider benötigen das Präfix `custom-`.** `ProviderService.EnsureCustomPrefix()` erzwingt den Präfix beim Anlegen. System-Provider werden durch `SystemProviders.IsSystemProviderName()` erkannt und von allen Mutationsoperationen ausgeschlossen. Das Präfix verhindert Namenskollisionen mit zukünftigen System-Providern und macht Custom-Einträge im Dropdown auf einen Blick erkennbar.

**D-047/5 — Delete ist nur erlaubt, wenn kein Profil den Provider referenziert (Cascade-Schutz).** `IProviderRepository.IsReferencedByAnyProfileAsync()` prüft per Raw-SQL `COUNT` über vier Profil-Tabellen (ReviewerProfiles, ExecutorProfiles, AdvisorProfiles, FinalizerProfiles — letztere via JSONB `->>'provider'` auf die `Settings`-Spalte). Soft-Disable via `SetActiveAsync` ist immer möglich; inaktive Provider erscheinen nicht in neuen Profil-Formularen, bestehende Profile (eingefroren im `CrewSnapshot`) bleiben funktionsfähig.

**D-047/6 — `LlmClientResolver` cached `Provider`-Lookups per Name via `ConcurrentDictionary<string, Lazy<Provider?>>` mit `LazyThreadSafetyMode.ExecutionAndPublication`.** Die `Lazy`-Wrapper garantieren, dass die asynchrone DB-Initialisierung pro Provider-Name exakt einmal ausgeführt wird, auch bei parallelen Anfragen. Fallback-Reihenfolge: DB-Provider → `LlmOptions.ProvidersFallback` (appsettings) → Exception. Der Resolver kann den Cache per Provider invalidieren, wenn `IProviderService` einen Update-Event signalisiert.

**D-047/7 — ModelCatalog: Cache-TTL von 24h auf 1h gesenkt; pro-Provider-Quellenauswahl.** HTTP-Provider mit `models_endpoint` rufen live ab; ohne `models_endpoint` wird die statische `manual_model_list` aus den Settings geliefert; CLI-Provider liefern ihre statische `models`-Liste aus den Settings. `IsUsingFallback()` signalisiert veraltete Cache-Zustände an die UI (Stale-Warning-Banner in `ModelSelector.razor`).

**D-047/8 — CLI-Proxy erhält einen generischen Endpoint `/v1/cli/{provider_name}/...` und einen internen Backend-Sync.** `ProviderConfigSync` lädt alle 60 s die aktiven CLI-Provider-Konfigurationen vom Backend-Endpoint `GET /api/internal/providers/cli` (abgesichert mit `X-Internal-Token`-Header). Der Endpoint ist mit `.AllowAnonymous()` registriert und prüft das Token manuell, damit keine Cookie-Auth-Middleware greift. Legacy-Endpoints `/v1/claude/...` und `/v1/codex/...` bleiben als Shims bestehen und routen auf den neuen generischen Endpoint.

**D-047/9 — `GeminiAdapter` und `GenericAdapter` als neue CLI-Adapter-Klassen mit abstrakter `CliAdapter`-Basisklasse.** Die abstrakte Basisklasse erzwingt `execute()`, `list_models()` und `health_check()` (Default: `shutil.which`). `GeminiAdapter` setzt `env["HOME"] = auth_volume` für den Token-Speicher und parst JSON mit Text-Fallback. `GenericAdapter` verwendet `prompt_args_template` (Substitution: `{prompt}`, `{model}`), `stdin_mode` und `output_format` (text/openai-json/jsonl). OpenCode ist bewusst ausgelassen (D-047/2).

**D-047/10 — `ProviderCatalog` (Legacy-Shim) verwendet `volatile CachedResult?` mit 5-Minuten-TTL.** Der Shim vermeidet Thread-Pool-Hunger durch synchrones Warten auf DB-Calls: nur bei Cache-Miss wird `.GetAwaiter().GetResult()` aufgerufen; danach antwortet er aus dem Speicher. `record CachedResult(IReadOnlyList<ProviderInfo> Items, DateTimeOffset Expiry)` ist unveränderlich. Der Shim ist als `[Obsolete]` markiert — alle neuen Callsites sollen `IProviderService` direkt nutzen.

**Tests:** 1065 gesamt (1061 grün + 4 bekannte Flakes unverändert). Neue .NET-Testklassen: `ProviderTests` (12), `ProviderServiceTests` (15), `ProviderRepositoryTests` (9), `ProvidersIndexTests` (8). Neue Python-Tests: `test_gemini_adapter.py` (7), `test_generic_adapter.py` (8), `test_provider_sync.py` (7) — 22 neue Python-Tests, 67 gesamt grün.

---

## D-048 — Workshop-Dashboard: 13-Widget-Live-Dashboard als Einstiegsscreen

*Datum: 19. Mai 2026*

Der primitive Einstiegsscreen (`/`) — Hero + 4 Stat-Tiles + Recent-Runs-Liste — wird durch ein vollständiges 13-Widget-Workshop-Dashboard ersetzt, das 1:1 aus einem React-Prototypen portiert wurde (`docs/design/dashboard-prototype/`, 6 JSX-Dateien). Alle 13 Widgets sind Blazor-Server-Komponenten mit Live-SignalR-Updates, Drei-Theme-Design-System (Vellum/Noir/Petrol) und einem Admin-Scope-Toggle (My/All Workshops).

**Widgets:** WelcomeStrip (Streak, CraftMark, Heute-Zähler), LivePressStatus (aktive Runs-Rail mit Phasen-Mapping), LedgerStats (Kosten + Run-Zähler, Trend-Pfeile), ActivityCalendar (365-Tage-Heatmap lvl-0..4), CrewDna (Template-Verteilung), CostForgeSankey (Provider×Rolle-Kostenflüsse), IterationSweetSpot (Histogramm), ManuscriptsGallery (letzte 12 abgeschlossene), TokenStream (Sparkline nach Rolle), CriticsBenchMatrix (Reviewer-Pass-Raten), ProviderBench (Provider-Statistiken), KnowledgeBaseWidget (Docs + Chunks + Top-Dateien), DayBookStream (Live-Aktivitäts-Feed, 7-Quellen-UNION-ALL).

**D-048/1 — `RunStatus` wird als String gespeichert — Raw-SQL-Inserts verwenden `'Completed'`, nicht die Ganzzahl `2`.** `RunConfiguration` hat `HasConversion<string>()` — die DB-Spalte speichert den Enum-Namen als Text (`'Completed'`, `'Pending'` usw.). Alle Raw-SQL-Test-Inserts müssen die String-Form nutzen. Ein Insert mit Ganzzahl `2` speichert den Text `"2"`, der von EFs `WHERE Status = 'Completed'` nie gefunden wird. Entdeckt und behoben bei der Test-Stabilisierung.

**D-048/2 — `IDashboardService`-Registrierung von `LlmServiceExtensions` nach `AddAtelierPersistence` verschoben.** Der E2E-TestHost verwendet einen Fake-`ILlmClientResolver` und ruft `AddLlmClient()` nie auf. `SignalRRunNotifier` injiziert `IDashboardService` — die Registrierung in den LLM-Extensions verursachte DI-Validierungsfehler bei allen E2E-Tests. Die Verschiebung nach `AddAtelierPersistence()` (das der TestHost immer aufruft) löst das Problem ohne Änderung am TestHost.

**D-048/3 — `DashboardRepository`-Datums-Parameter benötigen expliziten UTC-Offset.** Der Server läuft auf UTC+2. `DateTimeOffset.UtcNow.Date` gibt `DateTime` mit `Kind=Unspecified` zurück; Npgsql konvertiert es mit der Lokalzeit (UTC+2-Offset), was für `timestamptz`-Spalten abgelehnt wird. Behoben durch explizites `new DateTimeOffset(utcNow.UtcDateTime.Date, TimeSpan.Zero)` überall wo Datumsgrenzen benötigt werden.

**D-048/4 — CSS von Grund auf neu geschrieben; kein `atelier-dashboard.css` im Prototyp vorhanden.** Der React-Prototyp enthielt keine CSS-Datei — nur JSX-`className`-Attribute und eine HTML-Referenzdatei. `wwwroot/atelier-dashboard.css` wurde aus den JSX-Klassennamen kombiniert mit dem bestehenden CSS-Variablen-System in `wwwroot/atelier.css` geschrieben. Alle Layout-Klassen sind global (kein Blazor-Scoped-CSS). Responsive Breakpoints: ≤768 px kollabiert Mehrspaltenlayouts auf eine Spalte.

**D-048/5 — Namenskonflikte zwischen Blazor-Komponentenklassen und Core-Domain-Records.** Drei Komponenten (`WelcomeStrip.razor`, `LedgerStats.razor`, `CriticsBenchMatrix.razor`) teilen ihren Klassennamen mit gleichnamigen Core-Domain-Records. Lösung: `[Parameter]`-Property-Deklarationen verwenden vollqualifizierte Typnamen (`Geef.Atelier.Core.Domain.Dashboard.WelcomeStrip`) statt Datei-Umbenennung, was die Komponenten-Namenskonvention beibehält.

**D-048/6 — Day-Book UNION ALL (7 Quellen) gecacht, keine Materialized View.** Der Day-Book-Stream fragt 7 Quellen in einem einzigen asynchronen UNION ab (abgeschlossene Runs, fehlgeschlagene/abgebrochene Runs, Knowledge-Dokumente, Provider, OAuth-Clients, materialisierte Templates, Admin-User-Events). Ergebnisse werden für 45 s im `DashboardService` gecacht. Eine PostgreSQL-Materialized-View wurde explizit ausgeschlossen (Plan-Constraint „was du NICHT tust").

**D-048/7 — Migration Step25: `WordCount` auf `Runs`, `ProviderName` auf Actor-Cost-Tabellen, Performance-Indizes.** `Runs.WordCount` (int, nullable) per `regexp_split_to_array` backgefüllt. `IterationActorCosts.ProviderName` und `FinalizationActorCosts.ProviderName` per JSONB-Lateral-Join gegen `CrewSnapshot` (Exakt-Match), Fallback auf `split_part(ModelName,'/',1)` mit Provider-Familie-Heuristiken. Index `IX_Runs_CreatedAt` (BTREE) für Heatmap- und Ledger-Range-Abfragen ergänzt.

**Tests:** 1079 gesamt (1074 grün + 4 bekannte Flakes unverändert). Neu: `DashboardRepositoryTests` (6), `DashboardPerformanceTests` (2), `Step25MigrationTests` (4), plus 46 bUnit-Widget-Komponenten-Tests.

---

## D-049 — LLM-Binding: Explizite Provider+Modell-Auswahl für alle KI-nutzenden Profil-Slots

*Datum: 20. Mai 2026*

Schritt 1 von 3 der Grounding-Verbesserungs-Serie. Führt einen wiederverwendbaren `LlmBinding`-Value-Object ein und macht die Provider-/Modell-Wahl bei allen Profil-Slots explizit, die ein LLM aufrufen — insbesondere Transform-Finalizer (bisher Plain-Text-Inputs) und das Template-Studio-Meta-LLM (bisher hartkodierter Provider). Legt das Fundament für Schritt 2 (Grounding-Refinement) und Schritt 3 (KI-gestützte Grounding-Typen), die `LlmBinding` aus ihren jeweiligen Profil-Slots verbrauchen.

**D-049/1 — `LlmBinding` ist ein Core-Sealed-Record in `Geef.Atelier.Core.Domain.Llm`, nicht in Infrastructure.** Felder: `Provider`, `Model`, `MaxTokens`, `double? Temperature` (optional, null = Anbieter-Standard). Exponiert als `TransformSettings.Binding` (computed property) und `TransformSettings.WithBinding(LlmBinding)`.

**D-049/2 — `TransformSettings` behält flache JSONB-Keys statt verschachteltem Objekt.** Das bestehende `Dictionary<string,string>`-Format (Keys `Provider`, `Model`, `MaxTokens`, `SystemPrompt`) bleibt erhalten — ein verschachteltes `{"llmBinding": {...}}` würde alle bestehenden CrewSnapshots brechen. `LlmBinding` ist nur als computed property exponiert. `Temperature` wird als neuer optionaler flacher Key ergänzt.

**D-049/3 — Keine Datenmigration erforderlich.** System-Transform-Finalizer tragen bereits explizite Provider/Modell/MaxTokens-Werte im Settings-Dict (in Step22 geseedet). Custom-Finalizer ohne Temperature erhalten `null` via `TransformSettings.From()`-Fallback.

**D-049/4 — `TransformFinalizerExecutor` prüft Provider-Liveness vor dem LLM-Call.** Nutzt `IServiceScopeFactory` (Constructor-injiziert), um `IProviderService` per Request aufzulösen. Wenn der Provider fehlt oder inaktiv ist: gibt ein `Status`-Typ-`RunArtifact` mit klarer Fehlermeldung zurück; der Run selbst wird NICHT abgebrochen (Partial-Success-Vertrag aus D-044).

**D-049/5 — `FinalizerEditor.razor` Transform-Sektion ersetzt Plain-`InputText` durch Provider-Dropdown + `ModelSelector`.** Provider-Dropdown verwendet `<optgroup label="HTTP">` / `<optgroup label="CLI">`. `ModelSelector` (aus D-047 wiederverwendet) übernimmt Live-Modell-Loading. Temperature ist ein optionales `<input type="number" step="0.1" min="0" max="2">`-Feld.

**D-049/6 — Template-Studio-Meta-LLM-Provider ist nun via appsettings konfigurierbar.** `TemplateStudioOptions` erhält `Provider` (Default `"openrouter"`).

**Tests:** 1095 gesamt (1089 grün + 4 bekannte Flakes + 1 Skip). Neu: `LlmBindingTests` (5), `TransformSettingsTests` (12), bUnit `FinalizerEditorTests` (+5 vorher defekte Tests jetzt grün).

---

## D-050 — Grounding-Refinement: optionaler KI-Filter nach Provider-Fetch

*Datum: 20. Mai 2026*

Schritt 2 von 3 der Grounding-Verbesserungs-Serie. Grounding-Provider lieferten Rohdaten ungefiltert ans Briefing. Irrelevante Web-Treffer, redundante Chunks und verrauschte Snippets zwangen den Executor zur manuellen Auswahl — Token-Verschwendung, Qualitätseinbußen.

**Entscheidungen:**
- **Pro-Provider-Refinement:** Jeder Provider bekommt optional einen eigenen Refiner — Web-Refinement (Noise) und RAG-Refinement (Redundanz) sind unterschiedliche Aufgaben. Kein globaler Cross-Provider-Pass.
- **Flache Settings-Keys** im bestehenden `GroundingProviderProfile.ProviderSettings`-Dict (`refinementProvider`, `refinementModel`, `refinementMaxTokens`, `refinementTemperature`, `refinementMode`, `refinementInstructions`) — backwards-kompatibel, keine Snapshot-Migration.
- **`LlmBinding` aus D-049 wiederverwendet** — kein neues Binding-Konzept. `RefinementBinding: LlmBinding?`-Property auf `GroundingProviderProfile`.
- **`GroundingResult.ConsultationId: Guid?`** — Provider setzen dieses Feld nach Persistenz; der Orchestrator aktualisiert die gespeicherte Consultation mit dem `RefinementOutcome`.
- **Filter- und Synthesize-Modus** mit Tool-Use-Schema (`submit_refinement`). Filter: pro-Quelle keep/drop mit Begründung. Synthesize: kohärenter Text mit `[n]`-Referenzen + Originalquellen-Liste.
- **Graceful Degradation**: Provider inaktiv oder LLM-Fehler → Rohergebnisse durchgereicht, `WasSkipped=true`, Run NICHT abgebrochen.
- **Hard-Cap 20 Quellen** an den Refiner; Überschuss ungefiltert beibehalten.
- **`GroundingActorCosts`-Tabelle** (Migration Step27) analog zu `FinalizationActorCosts` — Refiner-Kosten tracken.
- **`tavily-refined` System-Provider** als sofort nutzbares Demo-Profil (Tavily Advanced + Filter-Refinement).
- **Grounding-Visualisierung** erweitert: Raw-Count → Refined-Count Badge, verworfene Quellen einklappbar, Synthesize-Text-Block, Skip-Hinweis bei Fehler.

**Tests:** 1118 gesamt. Neu: ~23 Tests für Refinement-Logik (GroundingRefinerTests, MultiProviderGroundingStepRefinementTests, GroundingProviderEditorRefinementTests).

---

## D-051 — Grounding-Typen erweitern: Static-Context, URL-Fetch, News-Search

*Datum: 20. Mai 2026 | PR #23 (feat/grounding-types)*

Schritt 3 von 3 der Grounding-Verbesserungs-Serie. Das Grounding-System kannte nach D-050 nur zwei Provider-Typen: `tavily` und `vector-store`. Reale Nutzungsszenarien brauchen: kuratierten Fixtext, gezieltes URL-Fetching bekannter Quellen und zeitkritische Newssuche.

**Drei neue `IGroundingProvider`-Typen:**

1. **`static-context`** — kuratierter Text, immer unverändert injiziert, keine externe API, kein LLM. Soft-Limit 50.000 Zeichen (UI-Warnung), Hard-Cap 200.000 Zeichen server-side.
2. **`url-fetch`** — HTTP-Fetch bekannter URLs, HTML-Bereinigung via HtmlAgilityPack 1.12.4, **SSRF-Guard als eigene Infrastructure-Komponente** (`IUrlSafetyValidator`). AngleSharp wurde erwogen, verursachte aber einen bUnit-Assemblierungskonflikt — HAP als Drop-in.
3. **`news-search`** — Tavily-API mit `topic=news` + `days`-Parameter, `SourceCitation.PublishedDate: DateTimeOffset?` (trailing-optional, backwards-compat) neu.

**Knackpunkte:**

1. **HTML-Cleaning:** HtmlAgilityPack 1.12.4 (MIT). Boilerplate-Tags (script/style/nav/header/footer/aside) entfernen, OG-Title oder `<title>` extrahieren, Whitespace normalisieren. Refiner aus D-050 übernimmt Restbereinigung.
2. **SSRF-Guard (sicherheitskritisch):** `UrlSafetyValidator` prüft Schema (nur http/https), löst DNS auf (alle IPs), prüft IPv4- und IPv6-Private-Ranges inkl. Cloud-Metadata (169.254.0.0/16). Redirect-Kette manuell, max. 3 Hops, jede Hop-IP erneut geprüft. `SocketsHttpHandler` mit `AllowAutoRedirect = false`.
3. **`urls`-Speicherung:** Newline-getrennte Zeichenkette im Settings-Dict — robust gegen Kommas in Query-Strings.
4. **Dashboard-Stage-Label:** `GroundingActorCosts` fließt als dritte Source in Cost-Forge und Provider-Bench ein; Stage-Label `"Refiner"`.
5. **`GroundingProviderTypes`-Konstanten-Klasse** eingeführt — kein Magic-String mehr in Providers. System-Profil `tavily-news` in `SystemCrew` ergänzt.

**Konsequenzen:**
- Refiner aus D-050 ist typ-agnostisch — alle drei neuen Typen erhalten automatisch Refinement wenn konfiguriert.
- SSRF-Guard als wiederverwendbare Komponente (`IUrlSafetyValidator`) für zukünftigen `rest-api`-Provider (Step 4).

**Tests:** 1234 gesamt (1228 grün + 4 bekannte Flakes + 1 Skip + 1 Skip neu). Neu: ~115 neue Tests (SSRF-Validierung, Provider-Logik, UI-Komponenten, Dashboard-Cost-Aggregation).

---

## D-052 — Grounding-Provider: academic-search und rest-api (2026-05-22)

*Status: Umgesetzt in feat/grounding-academic-restapi*

### Kontext

Nach D-051 (url-fetch, news-search, static-context) fehlten noch zwei Grounding-Szenarien: wissenschaftliche Literatursuche über akademische APIs und generisches REST-Endpoint-Grounding. Beide wurden in einen eigenen Schritt (Grounding-Verbesserungs-Serie, Schritt 4) ausgelagert.

### Entscheidungen

**1. `academic-search`-Provider — drei Quell-Adapter hinter `IAcademicSource`:**
- `arxiv`: kostenlos, Atom/XML, kein API-Key, CS/Physik/Mathe/Biologie.
- `semantic-scholar`: JSON-API, optionaler `x-api-key` für höhere Rate-Limits. Exponentieller Backoff-Retry bei HTTP 429 (3 Versuche, 2^Versuch Sekunden).
- `openalex`: JSON-API, Polite-Pool-User-Agent, Inverted-Index-Abstract-Rekonstruktion, Backoff-Retry bei 429.
- Quelle per `source`-Settings-Key konfigurierbar (Standard: `semantic-scholar`). Unbekannte Quelle fällt auf `semantic-scholar` zurück.
- API-Key: `apiKeyEnv`-Key enthält den NAMEN der Umgebungsvariable, nicht den Wert — zur Laufzeit aufgelöst, nie geloggt.
- Keine neuen NuGet-Abhängigkeiten: Atom/XML via `System.Xml.Linq.XDocument`, JSON via `System.Text.Json`.

**2. `rest-api`-Provider — sicherheitskritische Reihenfolge:**
- Template-Substitution (`{briefing}` → `Uri.EscapeDataString`) wird VOR der SSRF-Validierung durchgeführt — ein manipuliertes Briefing kann keine interne URL einschleusen.
- `IUrlSafetyValidator` aus D-051 wiederverwendet — kein Code-Duplikat.
- Redirect-Folgen deaktiviert (`SocketsHttpHandler { AllowAutoRedirect = false }`).
- `authHeaderEnv` speichert nur den Variablennamen; Wert wird zur Laufzeit ausgelesen, nie geloggt.
- JSONPath-Extraktion via `JsonPathNavigator` (neu, nur `System.Text.Json`, keine Newtonsoft/JsonPath.Net-Abhängigkeit).
- POST-Body: `{briefing}` wird JSON-escaped (nicht URL-encoded) — sicheres Einbetten in JSON-Payloads.
- `maxItems` Standard 10 (Sicherheits-Cap, unabhängig von JSONPath-Ergebnismenge).

**3. System-Profil `academic-default`:**
- Semantic Scholar + openrouter/gemini-2.0-flash-lite Filter-Refinement, geseedet via Migration `Step28AcademicRestApiGrounding`.
- Registriert in `SystemCrew.GroundingProviderProfiles`.

**4. `JsonPathNavigator` — leichtgewichtiger interner JSONPath:**
- Unterstützt `$`, `$.field`, `$.a.b`, `$.a[*]`, `$.a[*].b`, `$.a[0]` (negativer Index unterstützt).
- Keine Abhängigkeiten; ~120 LOC statische Klasse.
- `JsonPath.Net` oder `Newtonsoft.Json` bewusst nicht eingebunden — verursachten in früheren Steps Assembly-Versionskonflikte.

**5. TemplateStudio-Meta-Prompt aktualisiert:**
- `academic-search`-Guidance: wann vorschlagen (akademische/wissenschaftliche Briefings), wann nicht.
- `rest-api`-Guidance: konservativ — nur vorschlagen, wenn ein konkreter API-Endpoint im Briefing klar identifizierbar ist.
- Erlaubte `grounding_provider_type`-Werte in Tool-Schema erweitert.

**Was NICHT in diesem Schritt:** WebSocket-Grounding, Streaming-Provider, authentifizierte akademische Quellen mit Institutions-Lizenzierung, REST-API-OAuth-Authentifizierungsflows, bUnit-UI-Tests für neue Provider-Sektionen (verschoben).

**Tests:** 1334 gesamt (1324 grün + bekannte Flakes unverändert). Neu: 92 Tests — `GroundingProviderProfileAcademicRestTests` (27), `JsonPathNavigatorTests` (18), `ArxivSourceTests` (17), `AcademicSearchGroundingProviderTests` (17), `RestApiGroundingProviderTests` (9), `AcademicRateLimitRetryTests` (4).

---

## D-053 — Öffentliche Landing-Page: Static-SSR-Startseite auf `/` (2026-05-22)

*Datum: 22. Mai 2026*

Die Route `/` war bisher das authentifizierte Workshop-Dashboard (`[Authorize]`, InteractiveServer). Anonyme Besucher sahen nur die Login-Weiterleitung und erfuhren nie, was Geef.Atelier ist. Eine Design-KI lieferte einen React-Prototyp einer Landing-Page (`docs/design/landing-prototype/`) in derselben Designsprache und mit demselben CSS-Token-System wie das Dashboard. Diese Entscheidung portiert den Prototyp nach Blazor Server und strukturiert die Routing-/Auth-Trennung neu.

**D-053/1 — `/` = öffentliche Landing-Page; `/workshop` = authentifiziertes Dashboard.** `Landing.razor`: `@page "/"`, `[AllowAnonymous]`, `@layout Layout.LandingLayout`, kein `@rendermode` (Static SSR — keine SignalR-Verbindung für anonyme Besucher). `Index.razor` auf `@page "/workshop"` verschoben, `[Authorize]` und `@rendermode InteractiveServer` unverändert. Kein sonstiger Dashboard-Code geändert; SignalR-Hub-Gruppen sind nach Benutzername/Rolle gegliedert, nicht nach Route.

**D-053/2 — Server-seitiger Redirect für eingeloggte Nutzer auf `/`.** Static-SSR-`OnInitializedAsync` mit `[CascadingParameter] Task<AuthenticationState>` und `NavigationManager.NavigateTo("/workshop")` erzeugt vor jeder HTML-Ausgabe einen serverseitigen 302 — kein clientseitiges Flackern. Dasselbe Muster nutzt bereits `RedirectToLogin.razor`. `FakeNavigationManager.Uri` erfasst den Redirect in bUnit-Tests mit `SetAuthorized()`.

**D-053/3 — `LandingLayout.razor` — reines Layout ohne Wrapper-Element.** `LandingLayout.razor` rendert nur `@Body` ohne umschließendes `<main>` oder andere Elemente. `EmptyLayout` wurde abgelehnt, weil es die Ausgabe in `<main class="empty-layout">` mit Flex-Einschränkungen einwickelt, die mit dem vollbreitigen Sektionsdesign der Landing-Page kollidieren.

**D-053/4 — Alle bestehenden `/`-Anker auf `/workshop` umgestellt.** Drei Stellen: `Brand.razor:1` (`href="/workshop"`), `NavMenu.razor:6` (`href="/workshop"`, `Match="NavLinkMatch.All"` beibehalten), `ConnectedClients.razor:14` (Zurück-Link). `<base href="/">` in `App.razor` unverändert.

**D-053/5 — Post-Login-Redirect-Standard von `/runs` auf `/workshop` geändert.** `Login.razor` refaktoriert: `ResolvePostLogin(string? returnUrl)` ist jetzt ein testbarer `internal static`-Helper (Reflection-Test: `ResolvePostLogin(null) == "/workshop"`). `ReturnUrl` aus dem Query-String wird weiterhin berücksichtigt — Deeplinks überleben den Login.

**D-053/6 — Bedingte Asset-Einbindung in `App.razor`.** Landing-CSS (`atelier-landing.css`) und JS (`js/landing.js`) werden nur eingebunden, wenn `Http.HttpContext?.Request.Path.Value == "/"`. `<body>` erhält `class="landing-body"`, `<html>` wird `landing-root` angehängt — serverseitig, kein FOUC. Alle 27 CSS-Tokens aus `atelier-landing.css` existierten bereits in allen drei Paletten (Vellum/Noir/Petrol) in `atelier.css`; keine neuen Tokens notwendig.

**D-053/7 — `landing.js` ist reines JavaScript (kein Blazor-Interop).** Self-invoking IIFE. `IntersectionObserver` steuert Scroll-Reveal (`.lp-reveal` → `.in`). GEEF-Flow-Choreografie nutzt einen zweiten Observer (Threshold 0,32) + `setTimeout`-Sequenz G→E→E-Schleife→F, schaltet `is-active`/`has-passed` pro Phase. `prefers-reduced-motion`: setzt `body[data-static="1"]` und überspringt alle Timer; CSS-`@media` stoppt Keyframe-Animationen. Kein JS-Interop-Register notwendig — Static-SSR-Seiten haben keinen Blazor-Circuit.

**D-053/8 — `LandingIllustrations.razor` nutzt `Name`-Parameter + `@switch`-Block.** Erster Ansatz mit statischen `RenderFragment`-Properties und `__builder =>`-Lambdas im `@code`-Block funktioniert in Razor nicht sauber. Das Switch-Muster auf `[Parameter, EditorRequired] string Name` erlaubt konsumierenden Komponenten, beliebige der 12 SVGs ohne statische Referenz zu rendern. Alle Animations-Klassen (`press-anim`, `platen`, `paper-in`, `paper-out`, `stamp-glow`, `loop-path`) 1:1 portiert.

**D-053/9 — Neun Stub-Seiten, kein Inhalt.** Footer-Links dürfen nicht mit 404 antworten. `/pricing`, `/docs`, `/self-host`, `/status`, `/changelog`, `/contact`, `/imprint`, `/privacy`, `/terms` — je eine `[AllowAnonymous]`-Static-SSR-Seite mit `LandingLayout` und der gemeinsamen `ComingSoon.razor`-Komponente (Parameter `Title`). `/contact` zeigt nur „Coming soon" gemäß `contact-protection.md` — keine Kontaktdaten.

**D-053/10 — Hero-Headline fest auf `manufactory`-Variante.** „A manufactory for the written word." ist eine Konstante in `LandingHero.razor`. Die Alternativvariante (`„atelier"`) wurde nicht portiert.

**Tests:** 1382 gesamt. Neu: 48 bUnit-Tests — `LandingPageTests` (35: Nav, Hero, GeefFlow 4 Phasen + IterationLoop + Iter-Badge, Crew, Proof, Capabilities, Closing, Footer), `LandingRoutingTests` (13: Anonym-Render, Auth-Redirect, Reflection Route/Attribute, `ResolvePostLogin`-Unit, ComingSoon, Stub-Seiten-Attribute).


---

## D-054 — Kontinuierlicher Lernzyklus: Qualitätsgegattertes Selbstverbesserungs-Loop (22. Mai 2026)

*Datum: 22. Mai 2026*

Jeder Atelier-Run produziert implizit Wissen (Reviewer-Findings, Konvergenzverlauf, Korrekturen zwischen Iterationen), das bisher verpuffte. Diese Entscheidung schließt einen kontinuierlichen Verbesserungs-Loop: Ein opt-in `learning-extractor`-Finalizer extrahiert strukturierte Fakten aus einem abgeschlossenen Run, stößt fire-and-forget einen eigenen, streng kalibrierten **Learning-Evaluation-Run** als Qualitäts-Gate an, und nur konvergierte Learnings landen — domänen-getaggt — in einem **von der kuratierten Knowledge-Base getrennten** Store. Ein domänen-bewusster Grounding-Provider zieht sie bei späteren Runs gewichtet wieder ein.

Der naive Ansatz (ungefiltertes Self-RAG) wurde bewusst verworfen: Er führt zu Model Collapse. **Drei Schutzmechanismen sind konstitutiv:** (1) getrennter Namespace (`LearningEntries`-Tabelle, `learning://`-Zitationsschema), (2) der Learning-Run als auditierbares Gate, (3) strukturierte Fakten statt freier KI-Prosa. Dazu ein domänen-bewusster Retriever als wirksamster Einzelschutz gegen die Relevanz-Falle.

**D-054/1 — `RunKind`-Enum: `Standard = 0 / Learning = 1`.** Ein Learning-Run ist ein normaler Run mit Crew `learning-evaluation` + `Kind = Learning`. Kein Orchestrator-Dispatch-Change — `RunKind` ist reine Metadaten, die nur die zwei Finalizer gaten. Rückwärtskompatibel: bestehende Runs defaulten auf `Standard`; `Kind`-Spalte in Migration Step30 mit `DEFAULT 0`. `SubmitRunRequest.Kind` und `IRunPersistenceService.CreateRunAsync` um optionalen Parameter erweitert.

**D-054/2 — Rekursions-Stopp ist sicherheitskritisch und als zwei Guards implementiert.** (a) `LearningExtractFinalizerExecutor`: `if (run.Kind == RunKind.Learning) return Ok;` — der Extractor verweigert die Ausführung innerhalb eines Learning-Runs. (b) `LearningPublishFinalizerExecutor`: `if (run.Kind != RunKind.Learning) return Ok;` — der Publisher verweigert die Ausführung in Standard-Runs. Beide Guards lesen `run.Kind` über `IRunRepository.GetByIdAsync`. Ein dedizierter Unit-Test (`LearningRunDoesNotTriggerLearningRunTests`) beweist die Invariante.

**D-054/3 — `LearningEntry`-Record mit strukturierten Fakten.** `(Guid Id, string Text, Guid SourceRunId, Guid? LearningRunId, string Domain, LearningStatus Status, string StructuredFactsJson, string OwnerUsername, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt)`. Status: `Proposed → Approved | Rejected`. Embedding separat als pgvector-Spalte (nicht im Record-Konstruktor).

**D-054/4 — pgvector Raw-SQL-Pattern (identisch mit D-036 VectorSearchRepository).** `Pgvector.EFCore` 0.3.0 inkompatibel mit Npgsql 10.x. Vector als `vector(1536)`-Spalte mit String-Konverter in EF; INSERT und Cosine-Similarity-Suche via rohem ADO.NET (`NpgsqlParameter` mit `NpgsqlDbType.Vector`). HNSW-Index mit `vector_cosine_ops`.

**D-054/5 — `LearningExtractFinalizerExecutor` Schwelle + Fire-and-Forget.** Schwelle: `iterationCount ≥ minIterations (Standard 2) ODER mindestens ein Finding mit Severity ≤ Major`. Strukturierte Fakten aus: Briefing-Essenz, Template-Name (= Domäne), Iterationszahl, Findings (Severity + Reviewer + Message), Deltas zwischen Iterationen. LLM-Call (konfigurierbares Binding) mit striktem Anti-Halluzinations-Prompt. `LearningEntry` mit `Status = Proposed` persistiert. `SubmitRunAsync(Kind = RunKind.Learning)` als Fire-and-Forget im try/catch — ein Submit-Fehler bricht den Ursprungs-Run nicht ab.

**D-054/6 — Learning-Evaluation-Crew: Multi-Modell-Pluralismus mit `AbortOnCritical = true`.** Drei strenge Reviewer zur Reduktion korrelierter blinder Flecken: `learning-factual-grounding` (openrouter/gpt-4.1), `learning-value` (openrouter/gemini-2.5-pro), `learning-generalizability` (claude-cli/claude-opus-4.7, ≠ Executor). `ConvergenceOverride(MaxIterations: 2, AbortOnCritical: true)`. Finalizer: `learning-publisher` mit `RunFinalizersOnMaxAttempts: true`.

**D-054/7 — `LearningPublishFinalizerExecutor`: Gate-Output.** Findet den Kandidaten-`LearningEntry` mit `LearningRunId == context.RunId`. Konvergenz → Embedding berechnen, `UpdateStatusAsync(Approved, UtcNow)`, Embedding schreiben. Nicht-Konvergenz → `UpdateStatusAsync(Rejected, null)`, nichts in den Vektor-Store.

**D-054/8 — Domänen-bewusster Retriever: `LearningRetrievalGroundingProvider`.** Query-Embedding aus Briefing; `SearchApprovedAsync` mit topK×4 Extra-Kandidaten; Domänen-Boost-Re-Ranking: `finalScore = similarity × (sameDomain ? sameDomainBoost : crossDomainPenalty)`. Standards: `sameDomainBoost = 1.0`, `crossDomainPenalty = 0.5`, `maxLearnings = 4`. Kuratiertes Wissen (`vector-store`) wird in Crew-Templates vor `learning-retrieval` gelistet — kuratiert schlägt Learnings.

**D-054/9 — Verwaltungs-UI `/crew/learnings`.** Liste mit Status-Badge, Domäne, Text-Vorschau, Datum. Filter: Status-Dropdown + Domäne-Text. Aktionen: Genehmigen / Ablehnen / Löschen (DeleteConfirmationModal). Expand-on-Click: Volltext, Quell-Run-Link, Learning-Run-Link, Genehmigungs-Zeitstempel, strukturierte Fakten (klappbares JSON). `ILearningService` erzwingt Owner-Check für alle Mutationen.

**D-054/10 — MCP-Tool `list_learnings`.** Filter: `status_filter` (Proposed / Approved / Rejected), `domain_filter`. Gibt Id, Text (gekürzt auf 300 Zeichen), Domäne, Status, Quell-Run-Id, Learning-Run-Id, Owner, Zeitstempel zurück. `StructuredFactsJson` nicht exponiert (zu verbose).

**Was bewusst NICHT in diesem Schritt:** kein ungefiltertes Auto-Schreiben; kein Schreiben in die kuratierte KB; kein Analytics-Dashboard; keine schreibseitige Learning-Dedup; kein Learning-Verfall; keine Cross-User-Learnings; kein Multi-Hop-Learning; kein Backfill historischer Runs.

**Tests:** 1439 gesamt. Neu: ca. 57 Tests — Anti-Rekursion, RunKind, Threshold, Fire-and-Forget, Publisher, Retriever-Domänen-Boost, UI (bUnit).

---

## 23. Mai 2026 — Bugfix Learning-Retrieval-Provider (D-055)

### D-055: Bugfix — Continuous-Learning-Loop vollständig schließen

*Branch: `fix/learning-retrieval-provider` / PR #30*

Der in D-054 gebaute Loop war produktiv wirkungslos: `learning-retriever-default` erschien nicht in der Provider-Liste. Diagnose ergab zwei unabhängige Ursachen:

**Ursache 1 — lautloses Wegfiltern (Hauptursache):** `ListGroundingProviderProfilesAsync` gibt `[.. SystemCrew-Konstanten, .. DB-Zeilen-mit-IsSystem==false]` zurück. `learning-retriever-default` hatte `IsSystem=true` in der DB, fehlte aber in `SystemCrew.GroundingProviderProfiles` — es fiel durch beide Siebe ohne Fehlermeldung.

**Ursache 2 — fehlende DI-Registrierung:** `LearningRetrievalGroundingProvider` war nicht in `GroundingServiceExtensions.AddGroundingProviders` registriert. Die Factory hätte bei Laufzeit `InvalidOperationException` geworfen.

**Fix:** `SystemCrew.cs` um `LearningRetrieverDefaultProfile` ergänzt; DI-Registrierung nachgezogen; `ILogger<CrewService>` + Warning-Log für unverfolgte System-Profile. Keine neue Migration — das DB-Profil existierte korrekt (Step30).

**Mitbehobene D-054-Fehler:** `learning-extractor` fälschlicherweise in alle Standard-Templates eingetragen → entfernt (opt-in). Finalizer-Count-Test auf 19 aktualisiert.

**Folgefix 1 — Finalizer-Executor nicht in DI registriert:** Beim ersten echten Loop-Test entdeckt: `LearningExtractFinalizerExecutor` und `LearningPublishFinalizerExecutor` fehlten in `FinalizerServiceExtensions.AddFinalizers()` — exakt dasselbe DI-Muster wie die Grounding-Provider-Lücke. Fix: beide Executoren in `FinalizerServiceExtensions.cs` ergänzt.

**Folgefix 2 — EF Core kann `vector(1536)` nicht via ValueConverter schreiben:** Beim ersten LearningEntry-INSERT warf Postgres `42804: column "Embedding" is of type vector but expression is of type character varying`. `Pgvector.EFCore` 0.3.0 ist mit Npgsql 10.x inkompatibel. Da das Embedding ausschließlich via Raw-SQL (`LearningRepository.SetEmbeddingAsync`) gesetzt wird, ist `builder.Ignore(e => e.Embedding)` in `LearningEntryConfiguration.cs` die korrekte Lösung — EF Core berührt die Spalte nie.

**Real-Tests (alle nach Folgefix 1+2):** A (Provider sichtbar, `list_grounding_provider_profiles` zeigt `learning-retriever-default`) ✅ · B (Loop schließt sich: Standard-Run → `learning-extractor` fires → Learning-Run `Kind=1` → `learning-publisher` → `LearningEntry.Status=Approved` + Embedding gesetzt) ✅ · C (neuer Run mit `learning-retriever-default` zieht Learning als Citation zurück) ✅ · E (Backwards-Compat: alle 1528 Vor-Bugfix-Tests unverändert grün) ✅.

**Tests:** 1541 grün. Neu: 20 Regressions-Tests — `LearningRetrievalProviderResolutionTests` (8) + `LearningRetrievalProviderListingTests` (5) + `LearningFinalizerExecutorRegistrationTests` (7) — hätten alle drei Ursachen abgefangen.

**Commits:** `70bfe3e` (Merge PR #30 fix/learning-retrieval-provider → main) · `504fafa` (Folgefix 1: Finalizer-Executor DI) · `7319d59` (Folgefix 2: EF Embedding ignorieren). Deploy: 23. Mai 2026, ~12:45 UTC.

## 23. Mai 2026 — Learning-Loop in alle Standard-Templates (D-057)

### D-057: learning-retriever-default + learning-extractor als Standard in allen Templates

*Branch: `main` / Commits `562d235` + `e935307`*

Alle vier System-Templates (`klassik`, `juristisch`, `akademisch`, `marketing`) erhalten `learning-retriever-default` als dritten Standard-Grounding-Provider (nach `tavily-basic` + `run-attachments`) und `learning-extractor` als einzigen Standard-Finalizer. Das `learning-evaluation`-Template bleibt unverändert (Rekursionsstopp).

**Begründung:** Der Learning-Loop war technisch einsatzbereit, aber ohne Default-Verdrahtung in keinem einzigen Run aktiv. Jeder Standard-Run erzeugt implizit Wissen; der Extractor-Finalizer ist per `run.Kind == Learning`-Guard selbst-schützend. Neue Runs profitieren sofort von domänen-gewichteten Learnings ohne manuellen Template-Edit.

**Mitbehobene Bugs:** `Docs.razor` hatte fälschlicherweise `[Authorize]` statt `[AllowAnonymous]` (Regression aus Step31-Commit c02231a). `TransformSettings`-Default-MaxTokens in Tests war noch auf 4096 statt 60000.

**Geänderte Tests:** `FinalizerRegressionTests` (umbenannt + neue Assertion), `KlassikTemplateGroundingRegressionTests` (Count 2→3), `KlassikUnchangedRegressionTests` (Count 2→3), `SystemCrewDomainTemplatesTests` (3× Count 2→3), `SettingsRecordTests` + `TransformSettingsTests` (MaxTokens-Default 4096→60000).

**Deploy:** 23. Mai 2026, ~14:30 UTC. Health-Endpoint bestätigt `Healthy`.

## 24. Mai 2026 — CLI-Modellnamen-Normalisierung, Pflicht-Findings, JSON-Retry (D-058)

### D-058: Robustheitsfixes — Modellnamen, Reviewer-Qualität, CLI-Proxy-Stabilität

*Commits: `4cf9023` (cli-proxy JSON-Retry) · `7f38c9a` (ModelNameNormalizer) · `fb3c5fa` (Pflicht-Findings)*

Drei unabhängige Stabilitätsfixes nach Analyse zweier produktiver Runs, die alle drei Fehlermuster aufdeckten.

**Fix 1 — cli-proxy JSON-Retry:** Wenn ein CLI-Adapter reinen Text statt der erwarteten JSON-Tool-Call-Ausgabe zurückgibt, wiederholt der Proxy den Aufruf einmalig mit einem expliziten „Antworte NUR mit einem gültigen JSON-Objekt"-Suffix. Vorher erhielt der C#-`ProfileBasedReviewer` `finish_reason='stop'` und markierte den Reviewer als `Failed` — die Iteration lief ohne Findings durch. Neu: `_needs_json_retry()`-Helper + Retry-Branch in `_call_claude` und `_call_codex`. 8 neue Python-Tests.

**Fix 2 — kanonische CLI-Modellnamen:** Modellnamen in `SystemCrew`, `SystemProviders` und Custom-Profilen in der DB lagen in inkonsistenten Formaten vor (`anthropic/claude-opus-4.7`, `claude-haiku-4.5`, `claude-opus-4-7` kamen alle vor). Neuer `ModelNameNormalizer.Normalize(provider, model)` in `Geef.Atelier.Infrastructure.Llm`:
- Entfernt Vendor-Präfix (`anthropic/`, `openai/`, `google/`) für alle CLI-Provider.
- Ersetzt Punkte durch Bindestriche für `claude-cli` und `gemini-cli` (kanonisch: `claude-opus-4-7`).
- **Codex/OpenAI behalten Punkte** — `gpt-5.5` ist der kanonische GPT-Name.
- `LlmClientResolver.ForProfile` normalisiert an der Call-Site vor der Weiterleitung.
- Alle System-Profil-Konstanten auf kanonische Namen umgestellt. 16 neue Tests, 6 bestehende aktualisiert.

**Fix 3 — Pflicht-Findings für Reviewer:** Ursache für „4 Reviewer, 0 Findings" in einem Run: alle Reviewer-Prompts enthielten explizit *„No findings means approved=true with an empty findings array"* — eine Erlaubnis zur Nachlässigkeit. Behoben auf zwei Ebenen:
- System-Prompts: Satz entfernt, ersetzt durch *„You MUST always provide at least one finding — even on fully compliant text, use 'info' severity."*
- Code-Guard (`ProfileBasedReviewer.ReviewAsync`): bei `approved=true && findings.Count==0` wird einmalig mit explizitem Hinweis wiederholt. 6 neue Tests.

**Schlüssel-Invariante:** `approved=true` mit `info`-Findings ist weiterhin eine normale Freigabe, die zur Konvergenz beiträgt. Pflicht-Findings garantiert nur, dass die MARGIN NOTES immer befüllt sind; Ablehnungen werden nicht erzwungen.

**Tests:** 1555 grün (+23 neue; 7 pre-existing Failures unverändert). Deploy: 24. Mai 2026.

---

## 10. Juni 2026 — Tool-System: Zentrale ToolDefinition, Agentic Loop, Auto-Crew-Integration, MCP-Client (D-060)

### D-060: Tool-System — vollständige Implementierung über Phasen A, B, C

*Branch: `feat/tool-system` → merge auf main 10. Juni 2026*

Atelier-Akteure arbeiteten bislang ausschließlich aus dem Modellwissen heraus: ein Reviewer konnte eine Tatsachenbehauptung nicht nachschlagen, ein Executor keinen Fakt live holen. Externer Kontext gelangte ausschließlich über Grounding (Push, eager, einmalig) in die Pipeline. Dieses Feature führt **agentic Tool-Use (Pull)** ein: ein Akteur ruft Werkzeuge *während seines eigenen Turns* auf, entscheidet selbst wann und wie oft, und nutzt die Ergebnisse direkt in seiner Ausgabe.

**D-060/1 — `ToolDefinition` als einzige Quelle der Wahrheit.** Ein neuer Core-Domain-Record `ToolDefinition` (Name, DisplayName, Description, ToolType, Settings `Dictionary<string,string>`, SecretRef, LlmSchema `JsonElement`, AccessClass, IsSystem) ist die alleinige Definition jeder Werkzeug-Fähigkeit. Dieselbe Definition treibt sowohl Pull (agentic LLM Tool-Use) als auch Push (Grounding-Kontexteinspeisung). Neun Typ-Konstanten decken alle bestehenden Fähigkeits-Kategorien plus den neuen Typ `mcp-tool` ab. Zwei Zugriffsklassen: `ReadOnly = 0` (Standard), `Mutating = 1` (expliziter Betreiber-Opt-in erforderlich).

**D-060/2 — Secret-Isolation.** `SecretRef` enthält ausschließlich den ENV-Variablen-Namen; der tatsächliche Wert wird erst zur Ausführungszeit per `Environment.GetEnvironmentVariable` aufgelöst. Niemals in DB, Logs, `CrewSnapshot`, `ToolInvocation` oder einem UI-Feld gespeichert.

**D-060/3 — `IToolExecutor` — einheitliche Ausführungsschicht.** Ein einziges `ExecuteAsync(ToolDefinition, inputJson, ctx, ct)` dispatcht für alle 9 Tool-Typen. Jeder Aufruf schreibt automatisch einen `ToolInvocation`-Audit-Record (RunId, IterationNumber, ActorType, ActorName, ToolName, InputJson, OutputExcerpt ≤500 Zeichen, CostEur, DurationMs, Sequence, Outcome). Dies ist der einzige Ausführungspunkt für sowohl Pull (aus `IToolUseRunner`) als auch Push (aus `ToolBackedGroundingProvider`).

**D-060/4 — Mehrfach-Turn-LLM-Client.** `LlmRequest` hat eine optionale `Messages`-Liste erhalten (ersetzt System+User wenn gesetzt). `LlmResponse` trägt jetzt alle `ToolCalls`. `OpenAiMessageFormat` serialisiert die vollständige Message-History inklusive `assistant tool_calls` und `tool`-Ergebnismeldungen. Bestehende Single-Shot-Pfade sind unverändert.

**D-060/5 — `IToolUseRunner` — der agentic Loop.** Der Loop (History aufbauen → LLM-Aufruf mit Tools → Tool-Calls ausführen → Ergebnisse anhängen → wiederholen) läuft vollständig provider-agnostisch. Per-Turn-Cap (Standard): **5 Tool-Aufrufe**. Per-Tool-Timeout (Standard): **30 s**. Loop endet bei reiner Text-Antwort, beim Empfang des Pflicht-End-Tools (`submit_review`) oder bei Cap. Cap-Erreicht-Status wird als `ToolInvocationOutcome.CapReached` auditiert.

**D-060/6 — CLI-Proxy agentic-Erweiterung.** Wenn der .NET-Loop einen Aufruf mit Tools, aber ohne erzwungenes `tool_choice` schickt, injiziert der Proxy einen agentic-Protokoll-Addendum (genau ein `{"tool_call": …}`-JSON oder reiner Abschlusstext antworten). Der erzwungene Einzeltool-Pfad (`submit_review`) bleibt unverändert.

**D-060/7 — Provider-Capability-Detection.** `SupportsAgenticTools(providerName)` gibt `true` für HTTP-Provider, `false` für `generic`/deaktiviert zurück. Graceful Degradation: läuft ein Tool-gebundenes Profil auf einem nicht-fähigen Provider, wird dies sowohl im Run-Log als auch per UI-Badge sichtbar gemacht — kein stilles Versagen.

**D-060/8 — Tool-Binding in alle Akteur-Typen.** `ExecutorProfile`, `ReviewerProfile`, `AdvisorProfile`, `FinalizerProfile` (nur Transform) haben jeweils `IReadOnlyList<string> ToolNames` erhalten. Bei nicht-leerer Liste und fähigem Provider nutzt der Akteur den Loop. Migrationen Step38 (ToolNames-Spalten in Profil-Tabellen) und Step39 (ToolName-Spalte in GroundingProviderProfiles).

**D-060/9 — Grounding-Neuaufbau auf zentrale Tools.** Alle Grounding-Provider referenzieren jetzt eine `ToolDefinition` über `ToolName`. Die 8 typ-spezifischen Provider-Klassen wurden refaktoriert; die rohe Fähigkeitslogik wanderte in wiederverwendbare Executors. Ein `ToolBackedGroundingProvider` kapselt jeden Typ für eager Push. System-Tools und -Grounding-Profile sind als Code-Konstanten definiert (`SystemTools` / `SystemCrew`); Migration Step40 seedet sie idempotent beim Start.

**D-060/10 — `CrewSnapshot` v2 + `ToolInvocationsBlock`.** Schema-Version erhöht. Jedes Akteur-Profil im Snapshot enthält jetzt seine gebundenen `ToolDefinition`s (vollständig dereferenziert, keine Secrets). Neue UI-Komponente `ToolInvocationsBlock.razor` auf der Run-Detailseite: zeigt pro Iteration und Akteur die Tool-Aufrufe mit Eingabe, Ausgabe, Kosten, Dauer — liefert Provenienz für Reviewer-Findings.

**D-060/11 — `/tools` CRUD-UI + `ToolPicker`.** Neue Seiten für Auflisten, Anlegen, Bearbeiten und Anzeigen von Tool-Definitionen. Roter Gefahren-Badge für `Mutating`-Tools. `ToolPicker.razor` ist in allen Akteur-Editoren und Grounding-Provider-Editoren wiederverwendbar. Capability-Warnung bei nicht-fähigen Providern.

**D-060/12 — Phase B: Auto-Crew Tool-Binding.** `CrewPartSpec.ToolNames` ergänzt. `ToolCatalogGroundingProvider` (Typ `"tool-catalog"`) speist alle Tools als Markdown-Tabelle in Kompositions-Runs ein und verhindert so Namens-Halluzinierungen. `CrewComposerToolBindingProfile` (6. Composer-Reviewer) bewertet Tool-Binding-Entscheidungen (Notwendigkeit, Zugriffsklasse, Rollen-Fit, Anzahl, Katalog-Mitgliedschaft). `CrewSpecValidator` Step 8 prüft deterministisch: Provider-Capability (8a), Tool-Existenz (8b), Mutating ohne Opt-in gesperrt (8c).

**D-060/13 — Phase C: MCP-Client.** `McpServerConfig`-Domain-Record + Persistenz (Migration Step41). `AtelierMcpClientFactory`: SSRF-Check via `IUrlSafetyValidator`, optionaler AUTH aus ENV-Variable, `HttpClientTransport` → `McpClient.CreateAsync`. `IMcpToolDiscoveryService` ruft `tools/list` ab und mappt `McpClientTool` auf `ToolDefinition`-Kandidaten (Typ=mcp-tool, Settings `mcpServerId`/`mcpOriginalName`, `JsonSchema` als LlmSchema, ReadOnly-Standard). `/mcp-servers`-CRUD-UI mit Discovery- und Import-Workflow. `ToolExecutor` mcp-tool-Dispatch: `McpServerConfig` über `ServerId` laden, verbinden, `CallToolAsync` mit geparsten JSON-Argumenten.

**D-060/14 — Mutating-Opt-in.** `CrewSpecArtifact.AllowMutatingTools` (`bool`, Standard `false`). Der Validator blockiert jedes Mutating-Tool ohne explizit gesetztes Flag in der Spec. Die UI markiert Mutating-Tools mit einem roten Gefahren-Badge in Editor und Detailansicht.

**Tests:** 1788 grün, 0 Regressions. 5 DB-Migrationen (Step37–Step41), alle additiv (kein Datenverlust beim Forward-Deploy).

---

## D-061 — Spezialisierungs-Packs: generische Akteure + wiederverwendbare gescopte Spezialisierungs-Schichten

**Kontext:** Akteure mit aufgabengebackenen System-Prompts lecken beim Wiederverwenden in einer anderen
Crew ihre alte Aufgabe (stille Reviewer-Korruption). Vollständiges Konzept:
[`prompts/konzept-spezialisierungs-packs.md`](prompts/konzept-spezialisierungs-packs.md),
Umsetzungsleitfaden: [`11-specialization-packs_de.md`](11-specialization-packs_de.md).

**D-061/1 — Generische Rolle + `{specialization}`-Slot.** Akteure tragen nur einen generischen Rollen-Prompt mit `{specialization}`-Slot. `PromptComposer.Compose` (rein) ersetzt den Slot durch die geordneten, mit `\n\n` verbundenen Pack-Texte (Fallback: anhängen unter `## Specialization`); Präzedenz last-in-sequence-wins.

**D-061/2 — `SpecializationPack`-Entität.** Neue gescopte Entität (`Scope` General/DomainScoped/TaskBound, `ApplicableActorTypes`, `OwningCrewId`, `Archived`, `LastUsedAt`). System-Packs sind Code-Konstanten (`SystemPacks`), den Custom-DB-Rows vorangestellt (`specialization_packs`, Migration Step42) — analog zum Akteur-Profil-Muster.

**D-061/3 — System-Akteur-Umbau.** Die sechs domänen-spezialisierten System-Reviewer (legal/academic/marketing) wurden durch zwei generische Reviewer-Rollen ersetzt (`domain-terminology-reviewer`, `substantive-rigor-reviewer`, je mit gemeinsamer Severity-Taxonomie + Slot). Domänen-Deltas wanderten in sechs DomainScoped-System-Packs + zwei General-Beispiel-Packs; die Templates `juristisch`/`akademisch`/`marketing` binden sie, sodass der komponierte Prompt verhaltensgleich bleibt.

**D-061/4 — Pro-Crew-Bindung + Komposition zur Snapshot-Bauzeit.** `CrewTemplate`/`CrewSpec` erhielten `ActorPackBindings` (jsonb, Key `"<actorType>:<profileName>"`). `CrewSnapshotBuilder.ApplyPacksAsync` komponiert den effektiven Prompt je Akteur in den `SystemPrompt` des eingebetteten Profils (Factory + `ProfileBased*` unverändert) und protokolliert die Provenienz in `CrewSnapshot` v3 `PromptCompositions`. Komposition gilt für Executor/Reviewer/Advisor (die Akteure mit Prompt).

**D-061/5 — Scope-Durchsetzung.** `ListForBindingAsync` schließt fremde TaskBound + archivierte Packs aus; der Crew-Kohärenz-Check blockiert hart ein Custom-Template, das ein fremdes TaskBound-Pack bindet; `CrewSpecValidator` Schritt 9 + das Pack-Katalog-Grounding + die Composer-Reviewer prompt-quality/reuse-correctness setzen den Scope im Composer durch.

**D-061/6 — Composer-Zwei-Ebenen-Komposition.** `CrewPartSpec.PackNames` + `CrewSpecArtifact.NewPacks`; `submit_crew_spec`-Schema erhielt `pack_names` je Akteur + top-level `packs`. **Der Parser behält `tool_names`/`pack_names` auf `reuse`-Referenzen** — das Binden von Packs an wiederverwendete generische Reviewer ist der Hauptanwendungsfall. `CrewMaterializer` legt neue Packs an (TaskBound → an die erzeugte Crew gebunden) und materialisiert die Bindungen.

**D-061/7 — Lebenszyklus.** TaskBound-Packs cascade-löschen mit ihrer Crew. Promote/Clone-to-Generalize sind durch ein LLM-Generalitäts-Review (`IPackGeneralityReviewer`, fail-closed) gated. `PackArchivalBackgroundService` auto-archiviert ungenutzte, unreferenzierte Custom-General/DomainScoped-Packs nach `PackGc.RetentionDays` (default 90).

**D-061/8 — Audit-UI.** `ActorPromptCompositionBlock.razor` auf der Run-Detailseite zeigt je Akteur den Rollen-Prompt, die gebundenen Packs (mit Scope + Reihenfolge) und den komponierten effektiven Prompt.

**D-061/9 — Saubere Neuaufsetzung, Konten + Auth bleiben (Betreiber-Policy).** Migration Step42 behält nur Benutzerkonten und Auth/Credentials/Config (`Users`, OAuth, `mcp_server_configs`, `Providers`, Settings) sowie den DB-geseedeten System-Katalog (System-Tools/-Finalizer/-Grounding, geschützt über `WHERE "IsSystem" = false`-DELETEs) und leert alles andere für einen Frischstart: Run-Historie, Custom-Profile/-Templates/-Packs, Embeddings, Studio-Analysen, Knowledge-Base, Learnings und Custom-Tools. Verbesserte Prompts kommen aus Code-Konstanten (kein DB-Reseed). `pg_dump`-Backup vor dem Deploy.

**Tests:** Specialization-/Composition-/Parser-/Validator-/Lebenszyklus-Units ergänzt; keine neuen Regressionen (vorbestehende DB-/E2E-/Contention-Fehler unverändert). Migration Step42 (additives Schema + gescopte Custom-Daten-Neuaufsetzung).

## D-062 — Einsprachige UI: durchgängig Englisch bis echte i18n

**Kontext:** Die Web-UI war in einen Deutsch/Englisch-Mix abgedriftet (Seitentexte englisch, Field-Helps und einige Labels deutsch). Sprachmischung in einem einsprachigen Produkt ist ein Defekt, kein Feature.

**Entscheidung.** Solange das Atelier nicht echt mehrsprachig ist, ist die **gesamte Web-UI durchgängig englisch — keine Sprachmischung**. Dies kehrt die frühere Konvention „Atelier-UI = Deutsch" / „Field-Helps = Deutsch" um (D-043/4 sowie die Notizen in den Feature-Bau-Prompts). Übersetzt: öffentliche Rechtsseiten (Impressum/Datenschutz/Nutzungsbedingungen), alle Field-Help-Resource-Dateien (`LearningFieldHelps`, `StudioFieldHelps`, `ProviderFieldHelps`, `FinalizerFieldHelps`, `GroundingFieldHelps`), Navigations-/Menü-Labels, Validierungs-/Toast-Strings, die System-Crew-Template-DisplayNames (`Classic`/`Legal`/`Academic` — interne Namen `klassik`/`juristisch`/`akademisch` unverändert) und ihre Beschreibungen sowie die `SiteSettings`-Default-Platzhalter. Code/XML-Doc bleibt englisch; Doku/Berichte/Commits bleiben deutsch (unverändert).

**Scope-Hinweis:** Die historischen Bau-Prompts unter `docs/prompts/` und Abschlussberichte unter `docs/reports/` behalten ihre ursprüngliche Formulierung „UI-Strings = Deutsch" — sie sind ein Protokoll dessen, was damals entschieden wurde, keine lebende Richtlinie.

## D-064 — Dynamischer Modell-Katalog für CLI-Provider (2026-08-04)

**Anlass.** Der Modell-Auswahldialog zeigte für CLI-Provider eine im C#-Quelltext einbetonierte
Liste: `ModelCatalog` verzweigte bei `ProviderType.Cli` **vor** dem Netzwerk-Pfad auf
`settings.models`. Nach dem Erscheinen von Claude Opus 5 fehlte damit das beste verfügbare Modell in
jeder Auswahl — und dieselbe Veraltung hätte sich bei jedem künftigen Release wiederholt. Der
`cli-proxy` konnte die Frage längst beantworten; das Atelier hat ihn nur nie gefragt.

**D-064/1 — Ein provider-gekeyter Endpunkt statt einer `cli_kind`-Tabelle im C#.** Das Atelier ruft
ausschließlich `GET /v1/cli/{providerName}/models`. Welche CLI-Art eine Live-Quelle hat, ist eine
Eigenschaft des Gateways und wird von ihm über `x_source` (`live` / `static` / `degraded`)
mitgeteilt, statt in C# als Konstantenliste gepflegt zu werden. Benutzerdefinierte CLI-Provider
funktionieren damit automatisch mit. Der HTTP-Aufruf lebt allein in `CliProxyModelSource`, hinter
`ICliModelSource`, mit eigenem `HttpClient` **ohne** Resilience-Handler — ein fehlschlagender
Modell-Abruf darf den geteilten Circuit-Breaker des echten LLM-Verkehrs nicht öffnen.

**D-064/2 — Der Proxy meldet Herkunft und cacht Fehlschläge nicht.** Zuvor verwandelte
`return concrete or list(STATIC_MODELS)` einen vollständigen Probe-Fehlschlag — etwa eine abgelaufene
claude-OAuth-Sitzung, am 2026-07-22 bereits eingetreten — in ein **HTTP 200 mit der eingefrorenen
Liste**, 24 h gecacht und vom Erfolgsfall nicht unterscheidbar. Jetzt wird ein Fehlschlag als
`degraded` ausgewiesen und **nicht** gecacht; die Alias-Probe läuft unter dem geteilten Semaphor und
beendet ihren Subprozess bei Timeout; `?refresh=1` erzwingt eine Neuauflösung, gedrosselt auf einen
echten Probe-Durchlauf pro Minute; ein Hintergrund-Task wärmt beide Caches beim Start und löst sie
alle 12 h **mit** Cache-Bust neu auf. Ohne diesen Bust wäre der 12-h-Lauf ein Selbstgespräch gewesen:
er hätte seinen eigenen 24-h-Cache gelesen und nie ein neues Modell entdeckt.

**D-064/3 — `*-latest`-Aliase als Vorgabe, konkrete ID im Snapshot.** Profile und System-Crews
zeigen auf `claude-opus-latest` und folgen damit automatisch jedem Release. Der `CrewSnapshot`
dagegen hält die **aufgelöste konkrete ID**, aufgelöst an der einen Stelle, an der jeder Run-Snapshot
entsteht (`CrewService.ApplyPacksAsync`). Ohne diese Auflösung hätte die Umstellung Wartungsaufwand
gegen einen irreversiblen Verlust getauscht: ein fortgesetzter Lauf wäre unbemerkt auf einem anderen
Modell gelaufen als sein Elternlauf, und die Kostenzeilen hätten Modellgenerationen zusammengemittelt.
`ResolveModelAsync` stößt bewusst **nie** einen Abruf an — es läuft auf dem Run-Start-Pfad — und
vertraut der Abbildung nur bis zu deren **eigenem Ablaufstempel**. Das Kriterium „liegt eine Liste im
Cache?" wäre falsch und ist ersatzlos entfallen; die Begründung steht in `geef_architecture.md`
Abschnitt 9.3 und im Nachtrag unten.

**D-064/4 — Live-Liste und kuratierte Liste werden vereinigt, nicht ersetzt.** Der Composer filtert
`PreferredComposerModels` gegen den Katalog. Ersetzte die Gateway-Antwort die Liste, könnte ein
kuratiertes Modell lautlos aus dem Prompt fallen und die Auto-Komposition ein schwächeres Modell
wählen — ohne Fehler, ohne Log. Die Vereinigung schließt das aus, hält die Empfehlungsgruppe
nicht-leer und bewahrt Bestandsprofile davor, in den Freitext-Modus zu kippen.

**D-064/5 — Pluralitätsprüfung.** Alias und konkrete ID benennen dasselbe Modell. Eine Crew mit
Executor `claude-opus-latest` und Reviewer `claude-opus-5` bestand zuvor jede Prüfung und war doch
eine Monokultur, die sich selbst reviewt. Schritt 10 des `CrewSpecValidator` vergleicht nach der
Auflösung und meldet den Fall als **nicht-kritischen** Hinweis — auch für wiederverwendete Reviewer,
die häufigere und damit wahrscheinlichere Konstellation.

**D-064/6 — Migration Step45.** Hebt ausschließlich `IsSystem = true`-Profile auf `claude-cli` von
eingefrorenen Claude-IDs auf die Familien-Aliase, familienerhaltend über explizite `IN`-Listen
(inklusive der gepunkteten Formen — Step30 seedet `claude-haiku-4.5`). Benutzerprofile und Profile
anderer Provider bleiben unangetastet: ein System-Profil, das Claude über OpenRouter erreicht, muss
seine OpenRouter-ID behalten, für die es keine Alias-Form gibt. `Down()` ist ein dokumentierter
No-op. **Nebenwirkung, bewusst in Kauf genommen:** `IsSystem = true` heißt „vom System angelegt",
nicht „vom Betreiber nie geändert" — wer ein System-Profil absichtlich auf eine ältere Generation
gesetzt hatte, wird angehoben und kann das nur manuell rückgängig machen.

**Bewusste Abweichungen von `geef_architecture.md`.** `HasLiveEndpoint` heißt in der Umsetzung
`HasLiveSource` (der provider-gekeyte Endpunkt hat keine „Endpoint"-Ebene mehr). `StaticByDesign`
gilt jetzt auch für HTTP-Provider mit manueller Modell-Liste — sachlich richtig, aber eine Ausweitung
über den ursprünglichen Scope hinaus, sichtbar als ruhiger Hinweis statt gar keiner Kennzeichnung.
Der Hinweistext lautet entsprechend „this provider" statt „this CLI".

**Erkenntnis für künftige Nachträge.** Der Nachtrag §8 wurde als Diff umgesetzt, nicht als
Neuherleitung: jeder Punkt für sich war korrekt, sämtliche Reviewer-Befunde saßen **zwischen** den
Punkten — der Cache-Bust gegen das Timeout-Budget, die Alias-Map ohne eigenen Lebenszyklus, und
Test-Doubles, deren Pass-Through-Verhalten genau die Null-Wirkung der neu eingeführten Regeln ist.
Ein Nachtrag sollte die Spannungen zwischen seinen eigenen Punkten ausdrücklich benennen.

**Deploy-Reihenfolge: erst `cli-proxy`, dann `web`.** Startet das Atelier gegen einen Proxy ohne
D-064/2, meldet jeder CLI-Provider fälschlich „provider unreachable".

**Offene Folgeaufgaben.** (1) `x-cli-proxy-provider`/`x-cli-proxy-failover` auslesen, damit ein per
Failover von codex geschriebener Lauf nicht als `claude-cli` verbucht wird (Bestand vor dieser
Aufgabe). (2) Persistierte „letzte bekannte gute Liste" als vierte Fallback-Ebene. (3) Sprung auf
bUnit 2.x — bUnit 1.38.5 ist binär inkompatibel zu AngleSharp ≥ 1.5, weshalb `GHSA-pgww-w46g-26qg`
für das Testprojekt unterdrückt statt behoben ist (AngleSharp liegt nie im Produktionsgraphen).
(4) OpenRouter-Einträge aus `PreferredComposerModels.Reviewers` in `StaticModelFallback` spiegeln,
damit auch dort kein bevorzugter Reviewer lautlos aus dem Composer-Prompt fallen kann.

**Nachtrag nach Reviewer-Iteration 2 — Wächter-Regel und weitere Abweichungen.**

Iteration 2 brachte vier Befunde derselben Bauart: *jeder neue Wächter stützte sich auf ein Indiz,
das ausschließlich der Erfolgspfad erzeugt.* Ursache ist, dass dieses System **vier** Kodierungen
für „keine echte Antwort" benutzt — `null` vom Transport, eine erfolgsförmige Antwort mit
Degradations-Marker (HTTP 200 + `x_source: degraded`), die normale Rückgabe einer Konstante statt
eines Abrufs (fehlender API-Key), und die **Abwesenheit** eines Zustands (kein Zeitstempel, keine
Map) — und keine davon irgendwo aufgeschrieben stand. Die Tabelle steht jetzt als **§9 in
`geef_architecture.md`**, zusammen mit der Regel: *ein Wächter darf sich nie auf ein Indiz stützen,
das nur der Erfolgspfad erzeugt* — also gegen die **Kodierung** prüfen, nicht gegen die **Wertform**.

Konkret daraus geändert: Die Alias-Map trägt einen **eigenen Ablaufstempel** statt sich auf „liegt
eine Liste im Cache?" zu verlassen. Das frühere Kriterium war falsch, und zwar im Kern: `IMemoryCache`
speichert provider-gekeyt, nicht generationsgekeyt, und der Fehlschlagzweig legt **selbst** einen
Eintrag unter denselben Schlüssel — der Wächter fragte „gibt es *eine* Liste?", während der Vertrag
„ist *die* Liste noch gültig?" braucht. Beides fällt in der Erfolgsspur zusammen und divergiert
exakt im Fehlerfall. Wer den Wächter später „vereinfachen" will, muss diesen Absatz widerlegen.
Ebenso: die Drossel für erzwungene Neuauflösung hängt am **Versuch**, nicht am Erfolg (sonst drosselt
sie in der Störung gar nicht), und der interne 12-h-Sweep ist von ihr **ausgenommen** — er ist der
einzige Weg, auf dem während einer Störung noch ein neues Modell entdeckt wird.

Ein fehlender `OPENROUTER_API_KEY` meldet `static`, **nicht** `degraded`: eine abwesende Fähigkeit
ist keine Störung, und `degraded` würde jedem codex-Picker dauerhaft einen roten
„provider unreachable"-Banner geben, sobald der Key legitim entfällt.

Weitere festgehaltene Abweichungen von §8: `WarmUpTimeoutSeconds` steht auf 240 statt der in §8.4
genannten 60 (die dortige Begründung „der Proxy wärmt sich selbst" trägt nicht, weil der Nachtlauf
`?refresh=1` sendet und damit genau den selbstgewärmten Cache umgeht); neu hinzugekommen sind
`RefreshTimeoutSeconds`/`FetchBudget.Refresh` als eigenes Budget für den ⟳-Knopf, und
`PreferredComposerModels.SecondaryDefault` als benannte Vorgabe für auto-komponierte Reviewer und
Advisors. Die Deckelung des `HttpClient` wird aus den Budgets berechnet, damit
`Interactive < Refresh < WarmUp < Deckel` gilt — eine Deckelung unterhalb eines Budgets macht dieses
Budget wirkungslos, ohne dass die Konfiguration das zeigt.

Bewusst nicht geschlossen: `RunOrchestratorService.ResolveSnapshot` rekonstruiert für Prä-PS-5-Runs
einen Snapshot direkt aus `SystemCrew`-Konstanten und umgeht damit die Alias-Auflösung. Der Snapshot
wird nicht persistiert; der Aufwand lohnt den Eingriff in einen defensiven Altpfad nicht.

**Nachtrag nach Reviewer-Iteration 3 — bewusst getragene Kehrseiten und Folgeaufgaben.**

*Kaltstartfenster.* In den rund 60 Sekunden zwischen Web-Start und dem ersten Warmlauf ist die
Alias-Abbildung leer. Ein Lauf, der genau in diesem Fenster startet, schreibt den **unaufgelösten
Alias** in seinen Snapshot. Das ist die bewusste Kehrseite davon, dass `ResolveModelAsync` keinen
Abruf auslöst (die Alternative wäre eine Netzwerk-Runde auf dem Run-Start-Pfad, vor jedem Lauf). Der
Lauf selbst funktioniert — die CLI löst den Alias ohnehin auf —, nur die Provenienz dieses einen
Snapshots ist unschärfer. Wer das schließen will, braucht die persistierte „letzte bekannte gute
Liste" aus Folgeaufgabe (2), nicht einen Abruf an dieser Stelle.

*Drossel.* Beide Adapter drosseln nach demselben Vertrag und mit eigenem Zeitstempel pro Provider:
sie greift nur bei **erzwungenen** Neuauflösungen, ein gewöhnlicher Aufruf nach einem Fehlschlag
probt weiterhin, und der interne 12-h-Sweep ist ausgenommen. Der Zeitstempel misst den **Versuch**,
nicht den Erfolg — ein fehlgeschlagener Abruf schreibt keinen Cache-Eintrag, eine erfolgsbasierte
Drossel würde also ausgerechnet in der Störung nicht drosseln. Anlass für die Härte ist die
claude-Seite: eine Probe startet drei echte CLI-Subprozesse auf einem geteilten Abo.
Für codex ist es ein HTTP-Request, die Drossel dort also günstig, aber vor
allem symmetrisch.

*Inline-Kommentare.* `geef_architecture.md` §2.9 gibt die vorhandenen Kommentare in
`ModelCatalog.cs` ausdrücklich als Altbestand frei („nicht vermehren; in angefassten Blöcken
ersetzen"). Angefasste Blöcke wurden auf XML-Doc gehoben; die verbliebenen Zeilen sind unveränderter
Altbestand. Die Abschnittsmarken in den Tests halten die vier Fehler-Kodierungen aus §9.1 an der
Assertion sichtbar und sind bewusst dort belassen.

*Als Folgeaufgaben festgehalten, bewusst nicht in dieser Aufgabe:*
1. `codex-cli`: die **Empfehlungs**-Markierung bleibt kuratiert, während die Verfügbarkeit sich
   selbst pflegt. Eine neue GPT-Generation erscheint also in der Liste, aber nicht automatisch als
   Vorauswahl. Gehört mit Folgeaufgabe (4) aus dem Haupteintrag zusammen.
2. Ein fehlgeschlagener Refresh verwirft die letzte bekannte gute **Liste** (die Alias-Abbildung ist
   geschützt, die Liste nicht). Wirkung begrenzt, weil `MergeWithKnownIds` alle kuratierten IDs
   hält; die saubere Lösung ist die persistierte Last-known-good aus §8.9.
3. Der ⟳-Knopf kann weiterhin fälschlich „provider unreachable" melden, wenn die CLI-Slots des
   Gateways gerade von echten Läufen belegt sind. Durch Drossel und eigenes Refresh-Budget deutlich
   unwahrscheinlicher als zuvor, aber nicht ausgeschlossen.
4. Der Ablauftest der Alias-Abbildung prüft nur die Seite **vor** dem Stempel. Die negative Hälfte
   verlangt eine injizierbare Uhr im `ModelCatalog`-Konstruktor; der Gegenwert rechtfertigt die
   Vertragsänderung vor diesem Deploy nicht.

*Post-Deploy-Checkliste (Plan-Punkte 20–22), nach `cli-proxy` → `web`:*
- **20** Live-UI über den Playwright-MCP: Modell-Auswahl öffnen, `claude-opus-latest` und die
  konkrete Opus-ID müssen sichtbar sein, Konsole fehlerfrei.
- **21** `get_provider_models("claude-cli")` über den MCP: enthält die konkrete aktuelle Opus-ID.
- **22** `curl -s localhost:8090/v1/claude/models` — die Antwort darf **nicht** exakt `STATIC_MODELS`
  sein und muss `x_source: "live"` melden. Das ist der einzige Direkttest gegen den Fehlermodus,
  bei dem eine gescheiterte Probe wie ein Erfolg aussieht.

*Abweichung von Abschnitt 3.6 (Modell-DTO).* Der Architekturteil verlangt ein gemeinsames internes
DTO-Paar für beide Abrufpfade. Umgesetzt sind zwei: `ModelCatalog` behält sein
`ModelListResponse`/`ModelEntry` für den HTTP-Pfad, `CliProxyModelSource` hat mit
`CliModelsResponse`/`CliModelEntry` ein eigenes. Grund: die CLI-Antwort trägt mit `x_source` und
`x_aliases` zwei Felder, die im HTTP-Schema nichts zu suchen haben — eine echte Wiederverwendung
wäre ein Basistyp plus Erweiterung gewesen, also mehr Bauteile für zwei Deserialisierungsformen, die
sich unabhängig voneinander entwickeln. Bewusst so belassen und hier festgehalten, statt still zu
bleiben.

## D-065 — Bilderzeugung im cli-proxy (2026-11-12)

**Entscheidung.** Der Proxy bietet `POST /v1/codex/images/generations` (OpenAI Images API) auf Basis des
`$imagegen`-Skills der codex-CLI. Ein Bild je Anfrage, Antwort als Base64-PNG.

**D-065/1 — Eigener Endpunkt statt Bildausgabe im Chat.** `chat/completions` kennt keine Bildausgabe;
ein Eigenformat würde kein Client verstehen. Der Images-Endpunkt passt zur bestehenden Base-URL
(`/v1/codex`) und zu fertigen Clients (OpenAI-SDK, Open WebUI).

**D-065/2 — Kein Failover, kein Breaker.** claude kann keine Bilder erzeugen, ein Umschalten gäbe es
nur auf sich selbst. Bildfehler fließen bewusst nicht in `failover.record_failure` ein: ein
abgelehnter Bildprompt sagt nichts darüber, ob Textanfragen funktionieren, und darf `/health` und
den cliproxy-watch nicht färben. Limit → 429, sonst 502.

**D-065/3 — Ehrliche Parameter.** `n>1`, `response_format=url` und `output_format≠png` werden mit 400
abgelehnt, ebenso `background=transparent` (die Bilder sind immer deckend; still ein anderes Bild zu
liefern wäre unehrlich). `size` ist nur ein Seitenverhältnis-Wunsch im Prompt (codex hat keinen
Größenschalter); die Antwort meldet die echte Größe aus dem PNG-Kopf. `usage` fehlt, weil die Tokens des
Agentenlaufs nicht die des Bildmodells sind. Messung 2026-11-12: Querformat 1672 × 941, 37 s.
Beendet codex normal ohne Bild, ist das meist eine Ablehnung des Prompts: 400 `image_generation_failed`
(eigene Ausnahme `NoImageError`, nie nach Wortlaut klassifiziert, nie 502, damit niemand den Prompt
wiederholt und Kontingent verbrennt).

**D-065/4 — Datei statt Ereignisstrom.** Das Bild steht nicht im JSONL-Strom, sondern unter
`$CODEX_HOME/generated_images/<thread_id>/`. Der Proxy liest es dort und löscht den Ordner immer wieder
(`finally`); die `thread_id` wird nur als UUID akzeptiert, weil sie Teil eines gelöschten Pfades wird.
Reste nach Zeitüberschreitung, Abbruch oder Neustart räumt der nächste Lauf ab (älter als 1 h, nur
UUID-Ordner); ein abgebrochener Request beendet den codex-Prozess.

**D-065/5 — Kontingentschutz.** Bilder zehren am codex-Kontingent, das andere Nutzer des Gateways mitnutzen. Daher
ein Bildlauf gleichzeitig (`CODEX_IMAGE_MAX_CONCURRENT`, auf `CODEX_MAX_CONCURRENT − 1` begrenzt, belegt zusätzlich einen Codex-Platz), Zeitlimit
300 s und Tagesdeckel 50 je UTC-Tag (`IMAGE_DAILY_MAX`, im Speicher, 0 = aus).

**Nicht enthalten:** `/images/edits`, mehrere Bilder je Anfrage, URLs statt Base64, Selbsttest im
nächtlichen Selfupdate (würde jede Nacht Kontingent kosten).

## D-066 — Modellmatrix: Modell nach Rolle und Gebiet, Claude/OpenAI nur über den cli-proxy (2026-11-22)

**Anlass.** Die Crew-Komposition funktionierte, wählte Modelle aber willkürlich:

- Der Executor lief fast immer auf Opus.
- Die Reviewer rotierten durch eine feste Rangliste (`PreferredComposerModels`).
- Mehrere Profile liefen bezahlt über OpenRouter (`anthropic/claude-opus-5`, `openai/gpt-5.6-sol`), obwohl der cli-proxy dieselben Familien über das Abo bedient.
- Viele Bindungen zeigten auf veraltete Generationen: `gpt-5.6-*`, Grok 4.5/4.6, DeepSeek v4 und Gemini 3.x.
- Der Codex-Katalog des cli-proxy wurde von OpenRouter abgeleitet und lieferte API-only-IDs (`-pro`, `:batch`) statt der Abo-Modelle.

Recherche: `reports/2026-11-22-modellrecherche.md`. Design: `docs/superpowers/specs/2026-11-22-modellmatrix-design.md`.

**D-066/1 — Feste Regel statt Empfehlung (Variante A, Betreiberentscheidung).** `Core/Domain/Crew/Models/ModelMatrix` ist die einzige Quelle für konkrete Modell-IDs. Gebiet × Rolle ergibt eine geordnete Kandidatenliste.

- **Rollen (`ModelSlot`):** Executor, FactReviewer, StyleReviewer, DomainReviewer, StrategicAdvisor, CriticalAdvisor, DomainExpertAdvisor, GroundingRefiner, TransformRewrite, TransformCondense, Composer, Helper.
- **Gebiete (`ModelDomain`):** General, Correspondence, Legal, Academic, Marketing, Speech, Technical.

Ein neues Modell ist damit eine Änderung an einer Stelle, bei Claude und Codex seit D-066/10 gar keine mehr (Always-latest-Aliase). Die Fallback-Listen und die eingefrorenen Literale von Step46 werden per Konsistenztest abgeglichen.

**D-066/2 — Matrix (Stand 2026-11-22).**

| Rolle | Kandidaten (Reihenfolge) |
|---|---|
| Executor | Opus → Sol; Technical: Fable → Opus → Astra |
| FactReviewer | Astra → Opus → Neon → Sonnet |
| StyleReviewer | Sol → Opus → Sonnet |
| DomainReviewer | Legal/Academic: Neon → Astra → Opus → Fable → Sonnet; übrige: Astra → Neon → Opus → Fable → Sonnet |
| StrategicAdvisor | Fable → Astra |
| CriticalAdvisor | Fable → Astra |
| DomainExpertAdvisor | Technical: Fable → Astra; Academic: Astra → Opus; übrige: Opus → Astra |
| GroundingRefiner | Sonnet → Sol |
| TransformRewrite | Sol → Sonnet |
| TransformCondense | Sonnet → Sol |
| Composer | Opus → Sonnet → Sol |
| Helper | Haiku → Sonnet |

Kürzel: Claude-Modelle laufen als `claude-cli/claude-<familie>-latest`. Sol/Astra sind `codex-cli/gpt-sol-latest|gpt-astra-latest` (seit D-066/10; aufgelöst am 2026-11-23 auf `gpt-6.2-sol|astra`). Neon ist `openrouter/google/gemini-4-neon` (bezahlt, 3 $/15 $ je MTok). Begründung je Zeile im Recherchebericht, Abschnitt 3. Luna, Terra, Grok und DeepSeek sind bewusst nicht enthalten.

**D-066/3 — Verwaltet oder manuell, explizit.**

- Executor-, Reviewer- und Advisor-Profile tragen `ModelSlot` und optional `ModelDomain` (neue Spalten). Ist `ModelSlot` gesetzt, ist das Profil verwaltet: Das gespeicherte Binding ist nur Anzeige, das echte Modell wird pro Run gesetzt. `ModelSlot = null` heißt manuell.
- Es gibt keine Heuristik „ist Kandidat, also verwaltet“; die frühere Fassung hätte manuelle Wahlen still überschrieben.
- Grounding-Refiner und Transform-Finalizer werden über einen Settings-Marker verwaltet: `modelSlot` bzw. `ModelSlot`.
- Alle Speicherpfade normalisieren über `ManagedBindingExtensions.ForSave`.

**D-066/4 — Gebiet an der Crew.**

- `CrewTemplate.Domain` (neue Spalte) wählt die Matrixzeile.
- Das Profil-Gebiet ist nur ein Override. Grund: Generische System-Reviewer werden von `juristisch`, `akademisch` und `marketing` gemeinsam genutzt.
- Der Composer liefert weiter einen freien `domain`-String, für Packs, Namen und Dedup unverändert. ~~`ModelDomainParser` bildet ihn per Wort-Token-Synonymen auf die Matrix ab.~~ Aufgehoben durch D-066/12: Das Matrix-Gebiet kommt ausdrücklich als `model_domain`.

**D-066/5 — Zuweisung genau einmal, beim Snapshot-Bau.** `IModelAssignmentService.ApplyAsync` läuft in `CrewService` zwischen den Packs und `CrewSnapshotModelResolver`. Es gelten diese Regeln:

- Kandidaten, die im Live-Katalog fehlen, entfallen. Ein ausgefallener Katalog zählt als verfügbar und blockiert keinen Run.
- Reviewer und Advisor laufen nie auf dem Modell des Executors. Der Vergleich erfolgt alias-aufgelöst; Claude-Familien-Aliase greifen auch ohne Katalog.
- Reviewer bevorzugen eine andere Familie als der Executor.
- Reviewer derselben Rolle bekommen reihum ein Modell aus den ersten 3 geeigneten Kandidaten (Ring).
- Bezahlte Kandidaten gibt es außerhalb von Legal/Academic erst ab 3 Reviewern der Rolle. Je Crew sind höchstens 2 bezahlte Reviewer erlaubt; manuelle zählen mit.
- Wenn ein Primärkandidat übersprungen wird oder ein Fallback greift, wird gewarnt.

Validator und Materializer rufen den Katalog nicht auf und bleiben deterministisch. Bestehende Run-Snapshots werden nie angefasst: Fortgesetzte Alt-Runs behalten ihre Alt-Bindungen, wegen der Reproduzierbarkeit.

**D-066/6 — Composer.**

- Der Composer wählt `model_slot` je Inline-Teil plus `domain`. `provider`/`model` werden für LLM-Rollen ignoriert; der Materializer schreibt eine Warnung.
- `ComposerModelCatalog` ersetzt den Live-Katalogblock.
- Validator Step 11 prüft: Ein unbekannter oder für den Teil unzulässiger Slot ist kritisch. Ein fehlender Slot wird mit dem Default belegt (Warnung). Ein Nicht-CLI-Weg für Claude/OpenAI wird gemeldet und umgeschrieben (Warnung).
- Step 10 (Plurality) prüft nur noch manuelle, wiederverwendete Reviewer.
- Mitbehoben: Inline-Transform-Finalizer verloren Prompt und Modell (`Settings: []`). Inline-Grounding verlor Settings und Refiner.

**D-066/7 — Bestand (Migration Step46).**

- Neue Spalten.
- Crew-Gebiet der 12 bekannten Templates.
- Rollen-Klassifikation aller 11 eigenen Executoren, 46 eigenen Reviewer und 3 Learning-Reviewer, mit Matrix-Primärmodell als Anzeige.
- CLI-Umschreibung für den manuellen Rest.
- Verwaltete Refiner.
- Leerung des veralteten Template-Studio-Defaults und Aktualisierung der CLI-Modelllisten.

Unbekannte Zeilen bekommen nie eine geratene Rolle. Ein toleranter Enum-Konverter liest unbekannte Werte als „manuell“ statt die Liste zu brechen. Vor dem Deploy wird ein `pg_dump` der betroffenen Tabellen gezogen.

Der Learning-Extractor lief bisher bezahlt (`openrouter/openai/gpt-5.6-sol`) und nutzt nun TransformCondense. Die Template-Studio-Defaults kommen aus der Matrix; die Provider/Model-Schlüssel wurden aus `appsettings.json` entfernt.

**D-066/8 — cli-proxy.**

- `/v1/codex/models` liest `$CODEX_HOME/models_cache.json` der codex-CLI: nur `visibility: list`, nach `priority` sortiert, nie `-pro`/`:batch`, erneutes Lesen bei geänderter mtime.
- Fehlt die Datei, gilt der kuratierte Fallback (`gpt-6.2-sol`, `gpt-6.2-astra`, `gpt-6.1-terra`, `x_source: static`). Ist sie kaputt, meldet der Proxy `degraded`.
- Die OpenRouter-Ableitung ist entfernt.
- Der Atelier-Client loggt `x-cli-proxy-failover` als Warnung, weil ein Failover einen Reviewer unbemerkt auf das Standardmodell der Partner-CLI legt.

**D-066/9 — Host-Default Codex = `gpt-6.2-sol`, nicht Astra.** Das weicht vom Wortlaut der Workflow-Regel „größtes Modell“ ab. Das Codex-Kontingent wird geteilt. Sol hat 500k Kontext, Astra 272k. Astra wird in der Matrix gezielt für Fakten genutzt. **Nachtrag 2026-11-23:** Der Server gibt neue Modelle nur an neue Client-Versionen frei. Die Host-CLI stand noch auf codex 0.151 und bekam `gpt-6.2-*` mit „not supported when using Codex with a ChatGPT account“ abgelehnt. Das hat codex auf dem Host bis zum Update lahmgelegt. Die Host-CLI ist jetzt auf 0.171.0 aktualisiert, dieselbe Version wie im cli-proxy-Container. Regel: Wer das Default-Modell ändert, prüft zuerst `codex --version` gegen den Container und danach `codex exec "OK" < /dev/null`.

**D-066/10 — Immer aktuelle Modelle ohne Codeänderung (Betreiberwunsch 2026-11-23).** Anlass: Die Host-CLI lehnte `gpt-6.2-sol` ab (siehe D-066/9), und die Matrix hätte ein `gpt-6.3-sol` erst nach einem Release genutzt. Drei Bausteine:

- **Codex-Aliase im cli-proxy:** Er bildet je Modelllinie einen Alias `gpt-<linie>-latest` (`gpt-sol-latest`, `gpt-astra-latest`, `gpt-terra-latest`, `gpt-luna-latest`). Der Alias zeigt auf das neueste gelistete Modell der Linie in `models_cache.json`, verglichen nach numerischer Version. Neue Linien bekommen den Alias automatisch.
  - `/v1/codex/models` und `/v1/cli/codex-cli/models` liefern die Aliase zuerst und dazu `x_aliases`.
  - Beim Aufruf wird der Alias vor `-m` in die konkrete ID übersetzt; die Antwort trägt die konkrete ID.
- **Matrix, Fallbacks, Seeds und Step46:** Sie nutzen `codex-cli/gpt-sol-latest` und `gpt-astra-latest`. Das Atelier löst den Alias wie bei Claude über `x_aliases` auf, Run-Snapshots halten die konkrete ID.
  - Ausgemusterte Codex-Generationen (gpt-4/5, Luna, `-mini`, o-Series) gehen auf den Alias ihrer Linie (Sol/Terra), sonst auf `gpt-astra-latest`.
  - Offene Gewichte (`gpt-oss`) sind kein Abo-Modell und bleiben unverändert.
- **Nächtlicher Host-Abgleich** in `scripts/cli-proxy-selfupdate.sh` (Cron 03:40): Die Host-CLIs claude und codex unter `~/.npm-global` werden auf die im Container getestete Version gebracht, nur vorwärts. Danach wird das Host-Standardmodell von codex auf die neueste Sol-Version gestellt.
  - Jeder Schritt wird mit einer echten Anfrage geprüft, bei Fehlschlag zurückgerollt und gemeldet.
  - Der Abgleich läuft auch dann, wenn der Container selbst aktuell ist. `HOST_ONLY=1` führt ihn einzeln aus.

**D-066/11 — Härtung nach dem GEEF-Review (2026-11-23).**

- **Verfügbarkeit ehrlich:** Die Zuweisung prüft gegen die Modell-IDs, die der Live-Katalog wirklich meldet (`IModelCatalog.ListReportedModelIdsAsync`), nicht gegen die mit der statischen Liste zusammengeführte Picker-Liste.
  - Ein HTTP-Provider ohne gesetzten API-Schlüssel zählt als nicht verfügbar.
  - Ein bezahlter Fallback hält die Obergrenze von 2 ein.
- **Composer-Settings nur per Whitelist** (`CrewPartSettingsPolicy`): Inline-Grounding und -Finalizer dürfen keine Ziele, Zugangsdaten, Variablennamen oder Modellbindungen setzen.
  - Inline-`rest-api`-Grounding und Inline-`ExternalSink`-Finalizer sind kritisch; dafür müssen freigegebene Profile wiederverwendet werden.
  - Anlass: Die neue `settings`-Durchreichung hätte sonst Webhook-Ziele bzw. REST-Header mit Umgebungsgeheimnissen aus Modelltext erlaubt.
- **Rollen-Parser:** `model_slot` nimmt nur einen einzelnen Namen an. Kombinationen wie `FactReviewer,StyleReviewer` und Nicht-String-Werte sind kritisch.
- **Plurality:** Validator Step 10 vergleicht nur noch zwei manuelle Bindungen (§7.2). Ein manueller Reviewer auf dem Modell eines verwalteten Executors wird beim Snapshot-Bau gewarnt.
- **Speicherpfade:** Auch LearningExtract-Bindungen werden CLI-normalisiert. Grounding-Marker sind nur `GroundingRefiner`. Die Template-Studio-Meta-Bindung wird beim Speichern normalisiert, und das Studio-Prompt-Beispiel setzt `ModelSlot` statt Provider/Model.
- **Step46 erweitert:**
  - System-Transform-Finalizer bekommen Marker und Matrixbindung, System-Grounding-Refiner werden verwaltet.
  - Eigene Transform- und LearningExtract-Bindungen folgen der CLI-Regel; nackte o-Series-IDs werden erkannt.
  - Je Klassifikationszeile steht eine Begründung im Code.
  - Die inaktive `gemini-cli`-Modellliste bleibt unverändert, weil der Provider abgelehnt ist (D-066 „Abgelehnt“).
- **Gebiets-Parser:** `weg` und `paper` greifen nur noch in Komposita (`weg-etv`, `research-paper`), `white-paper` zählt als Marketing. Neu ist die Endungs-Suche (`*recht` → `mietrecht`). `insurance` allein bleibt bewusst außerhalb von Legal: Kulanzanfragen sind Korrespondenz.
- **GroundingQueryExtractor:** Er nutzt die Matrixrolle Helper, wenn `Llm:Actors` ihn nicht explizit konfiguriert; der Schlüssel ist aus `appsettings.json` entfernt.
- **Nachgeschärft nach Review-Runde 2:**
  - **Codex-Aliase:** Der Proxy meldet `x_aliases_complete: true`. Das Atelier ersetzt dann die Abbildung, statt sie wie bei Claude zu mergen, auch bei statischem Start. Bei `degraded` bleibt die alte Abbildung stehen. Der Snapshot hält die ID, die der Proxy in diesem Moment aufrufen würde.
  - **Ausgemusterte Codex-IDs:** Es gibt eine Regel (`CliRoute.IsRetiredCodexModel`: gpt-4/5, o-Series, Luna, `-mini`). Step46 nutzt dieselbe Regel als eingefrorenes Regex-Literal, ein Paritätstest prüft C# gegen SQL. Eine richtig geroutete neuere ID wie `codex-cli/gpt-6.3-sol` bleibt unverändert. Die Ersatz-Aliase sind Konstanten in `ModelMatrix`.
  - **Fallback:** zwischenzeitlich gestuft; durch D-066/12 ersetzt (ein Prädikat ohne eigenen Fallback-Pfad). `IModelCatalog.ListReportedModelIdsAsync` hat keine Default-Implementierung mehr; unbekannt heißt `null`.
  - **Finalizer-Typ:** Validator, Policy und Materializer nutzen denselben geparsten Typ (getrimmt, ohne Groß-/Kleinschreibung). Ein unbekannter Typ ist kritisch. Die toten Provider/Model-Prüfungen im Validator sind entfernt.
  - **Failover-Warnung:** Sie kommt nur noch bei einem Wechsel der CLI-Familie, nicht bei `claude->claude-alt`.
  - **Gebiets-Parser:** zwischenzeitlich mit einer expliziten Liste der Rechtsgebiete; durch D-066/12 ersetzt (keine Heuristik mehr).
  - **Host-Abgleich:** Er läuft in jedem Ausstiegszweig. Der Smoke-Test prüft Exit-Code und letzte Antwort. `config.toml` wird per TOML geprüft und atomar geschrieben. Gescheiterte Rollbacks werden eigens gemeldet, der Gateway-Schlüssel steht nicht mehr in `argv`. Das Skript ist mit Fake-CLIs getestet.
  - **Paritätstest:** Er prüft `_FALLBACK_MODELS` des cli-proxy gegen die Atelier-Listen.
- **Deploy-Reihenfolge (zwingend):**
  1. erst `cli-proxy`, dann `web`. Ein alter Proxy kennt `gpt-sol-latest`/`gpt-astra-latest` nicht: Die Aufrufe scheitern und landen per Failover bei claude, die Zuweisung sieht Sol/Astra als nicht verfügbar.
  2. Post-Deploy-Prüfung: `GET /v1/cli/codex-cli/models` liefert `x_source: live`, `x_aliases_complete: true` und ein `x_aliases` mit `gpt-sol-latest`.
- **Bewusst so belassen:**
  - **Codex-Alias ohne Untergrenze:** Listet `models_cache.json` für eine Linie nur noch eine ausgemusterte Version, zeigt der Alias darauf. Die Liste führt der Server; eine fest kodierte Untergrenze widerspräche „ohne Codeänderung“. Der nächtliche Smoke-Test meldet den Fall.
  - **Learning-Extractor:** Er hält im Snapshot keine konkrete ID, weil er nach dem Lauf arbeitet; die Reproduzierbarkeit hängt nicht daran.
  - **Python-Code:** Die Konvention „keine Inline-Kommentare“ gilt nur für .NET. Neue Kommentare in D-066-Python-Code sind trotzdem als Docstrings formuliert.
  - **Webhook-Executor:** Seine Netzgrenze für vom Betreiber konfigurierte Senken bleibt unverändert; der Inline-Weg ist geschlossen.
  - **Mehrzeiliges TOML-`model`:** Der Host-Abgleich unterstützt keinen mehrzeiligen `model`-Wert. Er schreibt dann nichts, meldet das und endet mit Status ≠ 0 (getestet). Das Skript selbst schreibt nur einzeilig.
  - **`degraded`:** Das Atelier ersetzt die Alias-Abbildung bei `degraded` nicht und ist damit strenger als die Gateway-Doku, die es erlaubt.
  - **Vorrang:** Eine eindeutige Modell-ID entscheidet über die Familie, der Provider nur bei mehrdeutigen IDs (`CliRoute` und Step46).
- **Beobachtet, außerhalb von D-066:** Die claude-Alias-Probe (D-064) liefert `claude-fable-latest` nicht in `x_aliases`. Fable-Akteure halten im Snapshot deshalb den Alias statt der konkreten ID.
- **Nicht repariert, nur benannt:**
  - Crews aus dem Template-Studio haben kein Matrix-Gebiet (`CrewTemplate.Domain` bleibt leer, also General). Ein Gebietsfeld im Studio wäre eine neue Funktion; die Richtung ist die billigere, sichere.
  - Die Validator-Warnung zu `model_domain` läuft im Modus `existing-template` nicht, weil dort das vorhandene Template sein eigenes Gebiet mitbringt.
  - Die zwei eigenen Transform-Finalizer `custom-natural-german-voice-polish` und `custom-german-review-consolidation-transform` haben seit dem alten Materializer-Fehler leere Settings und werden still übersprungen.
  - Custom-Crews über MCP (`CrewSpec.Domain`) laufen mangels Befüllung mit `General`.

**D-066/12 — Strukturänderungen nach Review-Runde 3 (2026-11-23).** Anlass: Zwei Brennpunkte kamen dreimal in Folge als neue Instanz wieder.

1. Der Gebiets-Parser lieferte Falsch-Positive: `weg`/`paper` → `*recht` → `rechts*`.
2. Der Fallback der Zuweisung deckte einzelne Regel-Kombinationen nicht ab.

Der GEEF-Workflow sieht dann Anhalten vor. Der Betreiber hatte die Entscheidung an einen Fable-Berater delegiert. Dieser entschied: weitermachen, aber den Ansatz wechseln statt weiter zu patchen. Architektur: `reports/2026-11-23-modellmatrix-d066-architektur-und-verlauf.md` §8–§9.

- **Matrix-Gebiet explizit:** Der Composer setzt das Pflichtfeld `model_domain`. Die Enum-Werte entsprechen `ModelDomain`; ein Drift-Test gleicht Schema und Enum ab.
  - Aufgelöst wird an einer Stelle (`CrewSpecArtifact.EffectiveModelDomain`): exakter Name, sonst der freie `domain`-String als exakter Name, sonst General.
  - Fehlt `model_domain` oder ist es ungültig, gibt der Validator eine Warnung. Die Crew läuft dann auf dem freien `domain`-String, wenn er ein exakter Enum-Name ist (Kompatibilität mit alten Artefakten), sonst auf General.
  - Die Synonymtabelle ist gelöscht. Damit sind auch die Wortlisten aus D-066/11 hinfällig.
  - Bewusst akzeptiert: Eine Fehleinstufung durch den Composer kostet höchstens zwei bezahlte Reviewer.
- **Ein Zuweisungsprädikat** (`ModelChooser`, reine Funktion über die Katalog-Fakten Confirmed/Unknown/Missing/Unusable):
  - **Hart, nie gelockert:** H1 nicht unbenutzbar. H2: Bezahlt nur für Reviewer, höchstens 2 je Crew, außerhalb von Legal/Academic erst ab 3 Reviewern derselben Rolle.
  - **Weich, in fester Reihenfolge:** nicht im Live-Katalog, Executor-Modell, außerhalb des Rings, schon im Slot vergeben, gleiche Familie, Matrixposition.
  - **Ring:** wird je Wahl aus den Kandidaten gebildet, die die harten Regeln inklusive des aktuellen Bezahlkontingents erfüllen. Das Gedächtnis, welche Modelle schon vergeben sind, gilt je Slot über alle Profil-Gebiete hinweg. Reviewer werden in Index-Reihenfolge vergeben.
  - **Bewusste Verhaltensänderung:** Ist das Bezahlkontingent ausgeschöpft, bekommt der nächste Reviewer ein noch unbenutztes Modell statt einer Wiederholung (Beispiel: fünf Academic-DomainReviewer → Neon, Astra, Fable, Neon, Sonnet statt …, Astra). Die letzte Instanz nimmt den ersten unbezahlten Kandidaten statt des ersten.
  - **Letzte Instanz:** immer der erste unbezahlte Kandidat. Der eigene Fallback-Pfad ist entfallen.
  - **Absicherung:** ein erschöpfender Invarianten-Test I1–I5 über rund 645.000 Kombinationen. Er kreuzt Katalogzustand je Provider (inkl. fehlender Nicht-Erstmodelle) × Executor × Gebiet × Rolle (inkl. Helper) × 1–4 Reviewer × Profil-Overrides × eingeschobene zweite Rolle × manuelle bezahlte/unbezahlte Reviewer. Die Prüfungen beziehen sich auf den Zustand zum Zeitpunkt jeder Wahl: Ein unbenutzbarer Provider ist nur zulässig ohne erlaubte Alternative; die Plurality-Warnung ist Pflicht; eine Wiederholung ist nur zulässig ohne freien Kandidaten. Per Mutation geprüft; nach Iteration 4 verschärft, nachdem die erste Fassung Warnungen zu großzügig als Beleg akzeptiert hatte.
  - **Katalog:** Ein leeres HTTP-200 meldet `[]` (unbenutzbar), ein Transportfehler `null` (unbekannt).
- **Katalog (nach Runde 5):** Ein fehlender API-Schlüssel meldet den Provider immer als unbenutzbar, auch wenn der Listenabruf in den Zeitablauf läuft. Der cli-proxy liefert die kuratierte Codex-Liste, sobald `models_cache.json` fehlt; so bleibt `gpt-sol-latest` (Failover) auflösbar. Step46 trimmt auch Tabulatoren und Zeilenumbrüche wie C#.
- **Nach der Bestätigungsrunde 6:**
  - Das Template-Studio akzeptiert Transform-Vorschläge ohne Provider. Der Prompt landet in den Settings, eine sichtbare manuelle Modellwahl wird übernommen.
  - Der Codex-Failover startet ohne `-m` (CLI-Standard), wenn der Katalog keine Sol-Linie meldet.
  - Ein gedrosselter Refresh nach Cacheverlust liefert die kuratierte Liste.
  - Step46 trimmt den Vertikaltabulator korrekt (`\x0B` statt `\v`).
  - Der Host-Abgleich prüft die neue codex-CLI vor einem Rollback auch mit dem Zielmodell.
  - Der Invarianten-Test läuft in einer eigenen, nicht parallelen Test-Collection.
- **Deduplizierung:** Ein ähnliches vorhandenes Template wird nur übernommen, wenn sein Gebiet dem effektiven `model_domain` entspricht; sonst wird ein neues angelegt (Warnung).
- **Composer-`urls`:** `urls` steht nicht mehr in der Settings-Whitelist. Ein Inline-`url-fetch` wird mit Warnung verworfen, der Composer-Prompt nennt `url-fetch`, `rest-api` und `ExternalSink` „nur per Wiederverwendung“.
  - **Offen, eigene priorisierte Härtung:** DNS-Pinning (Verbindung an die geprüfte IP binden) für `url-fetch`, `rest-api` und den MCP-Client. Die Schwäche ist vorbestehend und betrifft auch das Template-Studio über MCP; das Pinning ist Voraussetzung für ein agentisches `url-fetch`.
- **Weitere Härtungen:**
  - Strenge Enum-Namen (`EnumNames.ParseExact`) für Parser und EF-Konverter, keine Zahlen, Vorzeichen oder Kombinationen.
  - Vorrang der Modell-ID vor dem Provider in `CliRoute.FamilyOf`; Step46 trimmt wie C#. Paritätsfälle mit Leerraum und verkehrten Bindungen sind ergänzt.
  - Container-Smoke im Selfupdate mit Exit-Code, Provider-Header, ohne Failover und mit exaktem „pong“. Ein gescheiterter Rollback endet mit Status 2.
  - Die Doku verspricht keinen HTTP-Default ohne `model` mehr.
- **Stoppschwelle:** Zeigt ein Reviewer bei grünem Invarianten-Test ein Gegenbeispiel zu I1–I5, oder wäre ein Befund nur mit einer neuen Wortliste bzw. Ausnahme zu beheben, wird angehalten und dem Betreiber vorgelegt.

**Abgelehnt.**

- `gemini-cli` als dritte Familie: im Atelier inaktiv, kein Login im Proxy.
- Ein Codex-Budget je Crew: eigenes Feature; die Atelier-Nutzung ist gering.
- Die Laufzeit-Kandidatenkette und ein Audit-Block „ModelAssignments“ im Snapshot: Folgepunkte.
- Ein Familien-Mapping im Proxy-Failover: berührt alle Portfolio-Konsumenten.

**Lastabschätzung.**

- `klassik`: 2 Codex-Aufrufe je Iteration (Astra, Sol).
- `juristisch`: je Iteration Astra + Sol + Neon (bezahlt), dazu einmalig der Advisor Astra.
- Composer: je Iteration Astra ×2 + Sol; Neon greift nicht.
- Advisor „Teufels Anwalt“: läuft auf Fable (Claude-Abo) statt Codex.

## D-067 — Etappe 0: Sicherheitsnetz (2026-11-24)

**Kontext.** Eine Analyse am 2026-11-24 ergab: Angehängte Dateien, Websuche und Learnings waren seit dem
Umbau des Groundings auf zentrale Tools (D-060) in der Produktion wirkungslos, ohne dass ein Fehler sichtbar wurde. Die
System-Grounding-Profile verwiesen auf Werkzeuge, die im `ToolExecutor` nicht verdrahtet waren oder einen
Schlüsselnamen lasen, den der Container nicht setzt; der Fehler endete als leerer Kontext. Es gab keinen
Test über die echte Verdrahtung, Läufe meldeten umgangene Probleme nicht, und mehrere Stellen fielen still
auf Ersatzverhalten zurück: auf die Klassik-Crew bei unlesbarem Snapshot, auf eine Freigabe ohne Reviewer
bei einer Crew ohne Reviewer, auf einen Start trotz gescheiterter Migration. Etappe 0 bringt dem Anwender
keine neuen Funktionen; sie macht solche Fehler sichtbar. Sie ist die Voraussetzung für den Umbau
nach D-068.

**D-067/1 — Ende-zu-Ende-Gerüst über die echte Verdrahtung.** `tests/Geef.Atelier.Tests/EndToEnd` startet
die Anwendung über `WebApplicationFactory<Program>` gegen echtes Postgres (Testcontainers). Ersetzt werden
nur die HTTP-Gegenstellen (cli-proxy, Tavily, Embeddings), deren Anfragekörper aufgezeichnet werden. Die
Tests weisen nach, dass Anhang und Websuche im Executor-Prompt ankommen, und schlagen fehl, wenn man die
Grounding-Reparatur zurücknimmt. Jeder neue Pipeline-Weg bekommt hier einen Test vom Einstieg bis zum
Ergebnis. Beschreibung: [02-architecture](02-architecture_de.md#testen-und-messen).

**D-067/2 — Warnungen am Lauf.** Was ein Lauf umgeht (übersprungene Quelle, ausgefallener Reviewer,
abweichende Modellzuweisung, Failover, Finalizer-Problem), wird als Warnung in der neuen Tabelle
`RunWarnings` gespeichert (`IRunWarningSink`, eigener kurzlebiger Datenbank-Scope je Schreibvorgang, nie
laufentscheidend). Anzeige auf der Detailseite und in den MCP-Feldern `warnings` von `get_run_status` und
`get_run_result`. Die Texte sind kurze Sätze für den Anwender; technische Einzelheiten gehören ins Log.

**D-067/3 — `Outcome` als Anzeigesignal, `RunStatus` unverändert.** `Runs.Outcome` (`Converged`,
`DeliveredWithOpenPoints`, `Failed`, `Aborted`) und `Runs.DeliveredIteration` sagen, was ein Lauf geliefert
hat. `Outcome` bedeutet „fertig abgelegt, inklusive Finalizer“. `RunStatus` bleibt wie entschieden: Ein Lauf
ohne Konvergenz endet `Failed`, auch wenn er einen Text liefert (Statusentscheidung vom 2026-07-17).
`Outcome` liefert MCP (`get_run_status`, `get_run_result`); die Weboberfläche liest es nicht, ihr Block
„Warnings“ wählt seinen Wortlaut nach `Status` und danach, ob ein Text vorliegt. `DeliveredWithOpenPoints`
umfasst auch einen Lauf, den ein kritischer Befund stoppte (Status `Aborted`) und der einen Text hat. Bei
einem Fehlschlag werden die Befunde der letzten Iteration mitgespeichert.

**D-067/4 — Laut statt still.** Eine gescheiterte Migration bricht den Start ab; der Health-Check
`pending-migrations` meldet offene Migrationen. Ein unbrauchbarer Crew-Snapshot lässt den Lauf mit Meldung
scheitern (`CrewSnapshot.DeserializeForRun`), statt auf die Klassik-Crew zu fallen. Eine Crew ohne Reviewer
scheitert beim Start; der `AutoApproveReviewer` ist entfernt, und eine Crew-Vorlage ohne Reviewer lässt sich
weder anlegen noch speichern (`CrewTemplateRules.EnsureHasReviewer`, auch im Vorlagen-Editor). Ein Lauf ohne
Snapshot wird nicht fortgesetzt, sondern sofort mit Meldung abgelehnt (`RunNotResumableException`). Ein
unbekannter Grounding-Typ wird als Warnung übersprungen. Fehlertexte am Lauf und Warnungen sind feste,
einfache Sätze; technische Einzelheiten gehen nur ins Log.

**D-067/5 — Grounding-Brücke bis zum Umbau.** Die fünf System-Grounding-Profile haben keinen `ToolName`
mehr und laufen wieder über den Provider-Typ-Weg; eine Laufzeitregel (`GroundingPathSelection`) gilt auch
für ältere Snapshots, Migration Step47 bereinigt gespeicherte Zeilen. Der agentische Tool-Pfad war nie mit
der Pipeline verdrahtet; er bleibt eingefroren, sein Rückbau ist vorgesehen (D-068). Ein Lauf mit Anhängen
wird erst nach dem Hochladen der Anhänge startbereit (Snapshot danach, Zeitlimit
`Orchestrator:PreparationTimeout`, Standard 15 Minuten); ein fortgesetzter Lauf findet die Anhänge seines
Vorgängers.

**D-067/6 — Allowlist für Umgebungsvariablen.** Konfiguration, die Nutzer bearbeiten können, darf nur
Umgebungsvariablen von einer Allowlist nennen (`IEnvReferencePolicy`: eingebaute Liste, Präfix
`ATELIER_SECRET_*`, Ergänzungen über `EnvReference:Allowed`). Nicht erlaubte Namen werden beim Speichern
abgelehnt und zur Laufzeit nie gelesen. Die Editoren für Provider, Tools, MCP-Server und Grounding sind nur
für Admins; das Template Studio übernimmt keine Schlüsselverweise mehr.

**D-067/7 — Hub nur für Angemeldete.** `RunHub` verlangt eine Anmeldung, bildet Gruppennamen serverseitig
aus der Identität und endet bei Ablauf der Anmeldung (`MapRunHub`). Die Blazor-Seiten bekommen Live-Updates
über den prozessinternen `IRunUpdateFeed` mit denselben Zugriffsregeln und öffnen keine `HubConnection`
mehr. Listen-Updates gehen nur an Besitzer und Admins. Ein externer Client des Hubs ist nicht bekannt;
`RunHub` und der Hub-Teil von `LiveRunNotifier` bleiben, so abgesichert, bis zum Rückbau in Etappe 8
(D-068).

**D-067/8 — Learning-Schalter aus.** `Learning:Enabled` (Standard `false`) hält die Learning-Schleife an:
kein Extrakt, kein Prüflauf, kein Embedding, leerer Abruf. Crews mit Learning-Teilen bleiben gültig. Die
Schleife wird nach D-068 durch ein Gedächtnis je Nutzer ersetzt.

**D-067/9 — Nacht-Update baut nur aus committetem Stand.** Das nächtliche Selbst-Update des cli-proxy baut
nur aus dem committeten Stand; bei nicht committeten Änderungen setzt es aus und meldet das, auch mit
`FORCE=1`.

**D-067/10 — Mini-Lauf nach jedem Deploy.** `scripts/atelier-smoke.sh` meldet sich an, reicht über MCP einen
kurzen Auftrag ein, wartet, bis das `outcome` des Laufs gesetzt ist, und liest erst dann das Ergebnis
(Exit-Code 0 nur bei `Converged` oder `DeliveredWithOpenPoints` mit Text). Einrichtung:
[`ops/atelier-smoke.md`](ops/atelier-smoke.md).

**D-067/11 — Wächter.** `scripts/atelier-watch.sh` meldet hängende Läufe, einen ungesunden cli-proxy und
ungesunde Container über den vorhandenen Meldekanal des Servers, nur bei neuen oder geänderten Problemen und
einmal bei Entwarnung; lässt sich sein Zustand nicht speichern, hält er ihn in einer Ersatzablage und flutet
den Kanal nie. Einrichtung: [`ops/atelier-watch.md`](ops/atelier-watch.md).

**D-067/12 — Messwerkzeug.** `tools/Eval` vergleicht das Atelier per blindem Paarvergleich mit anderen Wegen
auf einem festen Aufgabenset; echte Aufträge bleiben außerhalb von Git
([`tools/Eval/README.md`](../tools/Eval/README.md)).

**Folgen.**

- Probleme werden am Lauf sichtbar, statt die Qualität still zu senken; manche Läufe scheitern jetzt mit
  Meldung, die vorher still mit Ersatzverhalten liefen.
- Läufe, die vor Step47 endeten, haben `Outcome = NULL`; es gibt keine Nachberechnung.
- Migration Step47 ist additiv und idempotent (`RunWarnings`, `Runs.Outcome`, `Runs.DeliveredIteration`,
  `Runs.ReportJson`, Bereinigung von `ToolName` an fünf System-Profilen).
- Die Doku beschreibt den Tool-Pfad als eingefroren ([10-tool-system](10-tool-system_de.md)).

**Rückweg.**

- Step47 entfernt nichts; `Down` nimmt nur die neuen Teile zurück. Die neuen Felder in MCP sind optional,
  ältere Clients ignorieren sie.
- Learning lässt sich mit `Learning:Enabled = true` wieder einschalten.
- Fehlende Namen auf der Allowlist werden über `EnvReference:Allowed` oder das Präfix `ATELIER_SECRET_*`
  ergänzt, ohne neuen Build.
- Smoke-Lauf, Wächter und die Sperre im Nacht-Update sind Skripte außerhalb der Anwendung und lassen sich
  einzeln zurücknehmen.
- Zurück zum Tool-Pfad für die System-Profile oder zu stillen Rückfällen führt kein Weg; genau diese
  Zustände hat die Etappe beseitigt.

## D-068 — Richtung Umbau: Auftrag, Direktweg, Rückfragen, Quellen, Gedächtnis (2026-11-24)

**Status:** beschlossen; bis Etappe 6 umgesetzt, dann mit D-077 abgeschlossen (Etappen 1b, 7, 8 und 9 nicht gebaut, je mit Auslöser). Richtungsentscheidung; die einzelnen Etappen bekommen eigene Einträge.

**Ziel.** Ein Anwender, auch ein Laie, tippt einen Satz, gibt optional Dateien mit und bekommt in Minuten ein
Ergebnis, das er dem direkten Modellaufruf vorzieht. Das Ergebnis erfindet nichts, hält harte Vorgaben ein
und weist seine Annahmen aus.

**Begründung.** Messungen auf elf echten Aufträgen zeigten: Rückfragen schlagen bloßes Annehmen 9 : 1, eine
zusätzliche Prüfrunde hebt das Urteil nicht, und eine Aufbereitung des Auftrags ohne neue Information
ebenfalls nicht.

**Richtung.**

- **Fragenregel.** Für jede Lücke im Auftrag gilt diese Reihenfolge: aus Auftrag und Dateien nehmen → aus
  Profil und Gedächtnis nehmen → recherchieren → vernünftig annehmen und als Annahme ausweisen → nur dann
  fragen, wenn der Anwender die Antwort wissen kann und sie das Ergebnis wesentlich ändert. Höchstens drei
  Fragen, kurz, mit fertigen Antworten zum Anklicken, immer überspringbar. Gefragt wird nur **vor** dem Lauf,
  nie während; ohne Fragen startet der Lauf sofort. Die Regel setzt der Code durch, nicht das Wohlwollen
  eines Modells.
- **Auftrag als Anzeigeeinheit.** Anwender sehen Aufträge: Klärung, Quellen, Versionen, Ergebnis. Läufe
  bleiben das Protokoll darunter.
- **Direktweg als Standard.** Klären, bei Bedarf recherchieren, ein Schreibaufruf mit allen Quellen im
  Volltext, Prüfungen und höchstens eine Korrektur. Die bisherige Schleife bleibt als „Extra careful“
  wählbar.
- **Quellen im Volltext.** Dateien gehen vollständig in den Prompt statt über eine Ausschnittsuche;
  recherchierte Zitate werden wörtlich gegen den abgerufenen Text geprüft.
- **Gedächtnis.** Profil, gemerkte Antworten und Korrekturen je Nutzer, ohne Embeddings; es ersetzt die
  Learning-Schleife.
- **Rückbau in Etappen.** Agentischer Tool-Pfad, MCP-Client, Template Studio, Crew-Composer, Learning-Gate
  und Embeddings werden schrittweise entfernt. Der SignalR-Hub `RunHub` (kein Client in der Anwendung, kein
  bekannter externer Client) entfällt spätestens in Etappe 8, zusammen mit dem Hub-Teil von
  `LiveRunNotifier`; bis dahin bleibt er auf Angemeldete beschränkt. Tabellen werden erst ein Deploy nach
  dem Wegfall des Codes gelöscht; alte Snapshots bleiben lesbar.

**Unverändert.** Das Geef-SDK, die Statusentscheidung vom 2026-07-17, additive und idempotente Migrationen,
die einsprachige englische Oberfläche (D-062; Fragen und Ergebnis erscheinen in der Sprache des Auftrags).

## D-069 — Etappe 1: Direktweg-Engine (2026-11-24)

**Status:** umgesetzt (Paket P2 der Etappe 1), Abnahme am echten Lauf nach der Integration.

**Entscheidung.** Der Direktweg ist eine System-Crew `direct`, die nur im Code existiert
(`SystemCrew.CodeOnly*`, nicht in Template-Listen, dem alten Formular oder dem Composer-Katalog). Er läuft auf
dem bestehenden Motor; das Geef-SDK bleibt unverändert.

- **Aufbau je Pipeline-Art.** Der Orchestrator baut jede Pipeline über einen `IPipelineAssembler`
  (Komposition 100, Direktweg 90, Fortsetzung 50, Klassik 0). Die drei bisherigen Wege verhalten sich wie
  vorher; neu ist nur der Haken `OnRunFinishedAsync`, aufgerufen nach dem gespeicherten Text und vor dem
  Ausgang, damit ein Auftrag nie ohne seinen Bericht als fertig erscheint.
- **Quellen.** `OrderGroundingStep` lädt die Dateien des Auftrags und rendert sie als Quellenblock
  (`<sources>`/`<source>` mit Seitenmarken, entschärfte Tags). Über `Sources:FullTextMaxChars` steht eine
  Dokumentkarte mit exakten Zeilennummern davor; der Modus folgt `SourceBudgetPlanner.Classify`, derselben
  Regel wie auf der Startseite und im Auftragsdienst. Der Block geht als Hintergrunddokument an den Schreiber, das
  der cli-proxy ab 50 kB als `context.md` ablegt. Eine Datei mit der Rolle „Vorlage zum Überarbeiten“ wird
  stattdessen Ausgangstext in `draft.md`. In der Fabrik ersetzt der neue Parameter `customGroundingStep` die
  Briefing-Stufe.
- **Schreiber.** Ein Aufruf im Dokumentmodus, ohne Failover (`x-cli-proxy-failover: off`), weil eine andere
  CLI dieselben Dateien nicht bearbeiten kann. Eine leere oder unveränderte Antwort (auch `empty_result` des
  Proxys) wird einmal wiederholt; danach endet der Lauf mit „The writer returned no text. Please try again.“
  (in der Korrekturrunde bleibt die vorige Fassung). `backend_unavailable` beendet den Lauf sofort mit „The
  writing service is busy. Please try again in a few minutes.“ Der Prompt verlangt, `context.md` vollständig
  und abschnittsweise zu lesen und Anweisungen in den Dateien zu ignorieren.
- **Lesebeleg.** Die Eingabe-Token der ersten Antwort werden mit der Größe der Dateien verglichen
  (`ReadEvidenceCheck`: Grundaufwand des Agenten plus Zeichen / 1,9 bei Deutsch). Unter 60 % entstehen die
  Warnung „Some of your files may not have been read in full.“ und ein Vermerk im Bericht. Kein harter Fehler.
- **Prüfungen ohne Modell.** `RequestConstraintExtractor` liest Länge, Sprache, Platzhalterverbot, verbotene
  Formen und Begriffe aus dem Wortlaut, konservativ: Was einen Teil des Textes, eine Datei oder etwas
  Unklares meint, wird Notiz statt Prüfung. `DeterministicChecksReviewer` meldet Verstöße als `Rejected`;
  höchstens eine Korrektur (`MaxIterations = 2`), danach liefert die SDK-Regel die bessere Fassung.
  Platzhalter sind erlaubt und erscheinen als „Fill in before sending“, außer der Auftrag verbietet sie.
  Regeln nach der ersten Prüfrunde: „Leer“ ist ein Text nur ohne ein einziges Wort (Slogan oder Betreffzeile
  sind in Ordnung); eine Länge wird nur geprüft, wenn der Auftrag sie nennt; eine Vorrede an den Leser („Here's
  what I learned:“) ist ein Hinweis und blockiert nie; nummerierte Zeilen gelten erst ab zwei hintereinander als
  Liste und nie, wenn sie mit einem Datum beginnen („24. November 2026“); „ohne Zwischenüberschriften“ erlaubt
  einen Titel in der ersten Zeile.
  Regeln nach der zweiten Prüfrunde (Präzision vor Trefferquote): Eine Verneinung wirkt nur im eigenen Teilsatz
  (Teilsätze enden an Satzzeichen, Gedankenstrichen und „und/and/aber/but/oder/or/sondern/while/obwohl …“), daher
  bleibt bei „Write more than 100 words and don't use emojis“ die Untergrenze. Eine Wendung mit einer Richtung
  („höchstens“, „at most“, „weniger als“, „mindestens“, „at least“, „nicht weniger als“) wird nie umgedreht; eine
  Verneinung davor im selben Teilsatz lässt die Länge ungeprüft. „mehr als/more than/exceed“ wird nur zur
  Obergrenze, wenn eine Verneinung unmittelbar davor steht (höchstens ein Wort dazwischen: „must not be more than“,
  „nie mehr als“) oder die feste Nachstellung „… sollen es nicht sein“ folgt; jede andere Verneinung im Teilsatz
  lässt die Länge ungeprüft. Eine Länge wird nur dann Prüfung, wenn alle Längen des Auftrags klar sind: eine
  unklare Länge, eine andere Zählung im selben Teilsatz (Seiten, Minuten) oder eine Länge für einen Teil des Textes
  ergibt nur eine Notiz. „mehr als A und nicht mehr als B“ ergibt den Bereich A–B. Die Toleranz rundet die
  Obergrenze ab und die Untergrenze auf („höchstens 5 Wörter“ lässt 5 zu). Trennzeilen wie „* * *“ sind keine
  Listen. Eine mögliche Vorrede an den Leser besteht nicht, blockiert aber nie; sie erscheint als Hinweis getrennt
  von den offenen Prüfungen.
- **Bericht.** `Runs.ReportJson` (`RunReport`, Schema 1) mit Vorgaben, Prüfergebnissen der ausgelieferten
  Fassung, Platzhaltern, Quellen, Lesebeleg und Vermerken.

**Begründung.** Der Vorversuch zum Lesen von `context.md` zeigte: Der Agent sucht oft, statt zu lesen; ganz
gelesen wird verlässlich nur bis etwa 150.000 Zeichen. Der codex-Dokumentmodus liefert im Container keinen
Text. Deshalb Lesebeleg, ausdrückliche Leseanweisung, kein Failover und eigene Leertext-Prüfung.

## D-070 — Etappe 1: Aufträge, Quellen und was die Person sieht (2026-11-24)

**Status:** umgesetzt (Pakete P1, P3, P4 der Etappe 1 und die Korrekturen nach der ersten Prüfrunde), Abnahme am
echten Lauf nach der Integration.

**Entscheidung.**

- **Der Auftrag ist die Einheit, die die Person sieht.** Neue Tabellen `Orders`, `Sources`, `OrderSources` und
  `RunSourceUsages` (Migration Step48, handgeschrieben, additiv und idempotent). Läufe bleiben das technische
  Protokoll; `Runs.OrderId` und `Runs.OrderVersion` verbinden sie mit ihrem Auftrag (Version 1..n je Auftrag, eine
  Komposition trägt die Version ihres Schreiblaufs). Step48 verknüpft bestehende Läufe: ein Auftrag je Kette ab
  einem Lauf ohne Elternlauf; Lernläufe bleiben ohne Auftrag. Die alten Wege (`/new`, `submit_request`,
  Fortsetzen) legen ebenfalls Aufträge an oder erweitern sie; ein Fehler beim Verknüpfen wird geloggt und stoppt
  den Lauf nie.
- **Anzeigezustand nur aus Auftrag und aktuellem Lauf.** Acht Zustände (`preparing`, `needs_input`, `queued`,
  `working`, `delivered`, `delivered_with_open_points`, `failed`, `stopped`), abgeleitet von `OrderStateResolver`
  aus `Orders.State` und dem aktuellen Lauf (höchste Standardversion). Ein Auftrag mit Text erscheint nie als
  gescheitert. Ein Lauf, der seinen Endstatus hat, aber noch keinen Ausgang, gilt bis zu 30 Sekunden als
  `working` (Bericht und Ausgang werden direkt nach dem Status gespeichert), ebenso eine fertige Komposition, bis
  ihr Schreiblauf existiert. Ein durch einen kritischen Befund beendeter Lauf mit Text ist
  `delivered_with_open_points`, nicht `stopped`.
- **Dateien.** Ein Upload je Datei (`POST /api/sources`) antwortet mit einer Quittung (Seiten, Zeichen,
  Lesequalität, Warnungen, Größenklasse). Grenzen: bis 150.000 Zeichen bekommt der Schreiber alles im Volltext,
  bis 600.000 mit einer Karte der Dateien, darüber wählt die Person Seiten ihrer PDFs (`Sources:FullTextMaxChars`,
  `Sources:MapMaxChars`; der erste Plan nannte 300.000/900.000, der Vorversuch zeigte, dass vollständiges Lesen nur
  bis etwa 150.000 verlässlich ist). Die Startseite zählt gewählte Seiten über den Auftragsdienst mit denselben
  Regeln wie der Start (`IOrderService.CheckSourcesAsync`) und hält „Start“ gesperrt, solange eine Auswahl fehlt,
  falsch oder noch zu lang ist; eine Datei ohne Seiten, die allein zu lang ist, verlangt eine kürzere Fassung.
  Originale liegen in einem eigenen Speicher-Volume und werden von einem Hintergrunddienst entfernt, sobald kein
  Auftrag sie mehr nutzt.
- **MCP.** `submit_order`, `get_order_status` (Long-Poll) und `get_order_result`; `submit_request` bleibt als
  Hülle über Aufträgen. Mit dem statischen Token braucht ein Auftrag einen Benutzernamen
  (`AtelierMcp:StaticTokenUser` oder `AtelierUser:Username`), sonst lehnen `submit_order`/`submit_request` ab.
- **Verdrahtung ohne stillen Rückfall.** Auftragsdienst, Assembler, Schreiber des Direktwegs und Orchestrator
  verlangen ihre Abhängigkeiten; eine fehlende Registrierung scheitert beim Start, statt mit Standardwerten
  weiterzulaufen.

**Begründung.** Die Person denkt in Anfragen und Texten, nicht in Läufen und Crews. Eine einzige Stelle, die die
Anzeige ableitet, hält Web und MCP gleich, und die Grenzen folgen dem, was der Schreiber verlässlich liest.

### Zusatz zu D-069 — sichere Aufnahme hochgeladener Dateien (2026-11-24)

**Entscheidung.** PDF- und Word-Dateien werden in einem eigenen Prozess derselben Anwendung gelesen
(`--extract-source`, Verzweigung in `Program.cs` vor dem Aufbau des Hosts) mit harter Gesamtzeit (Abbruch des ganzen
Prozessbaums), Grenze für den verwalteten Speicher (`DOTNET_GCHeapHardLimit`, 1 GiB) plus Speicherwächter,
begrenzter Antwort, geleerter Umgebung und privatem temporären Arbeitsverzeichnis. Jeder Absturz, Abbruch oder jede
Grenze endet als Quittung „unlesbar“ mit einem einfachen Satz. Uploads werden in eine private temporäre Datei
gestreamt statt in den Speicher; dazu kommen eine globale Grenze für Uploads (8) und Extraktionen (2) mit Wartezeit,
ein Kontingent je Nutzer für Dateien ohne Auftrag (50 Dateien, 500 MB), ein eindeutiger Index auf Besitzer und Hash
(in Step48, das noch nicht deployt war) und ein Verwaisten-Scan im Aufräumdienst. In den Extraktoren: XML-Tiefenlimit
100 ohne DTD vor dem Word-Dokumentmodell, Durchlauf ohne Rekursion, `MaxUncompressedBytes` 64 MiB mit
Verhältnisprüfung ab 64 KiB und eine Grobprüfung der entpackten Größe von PDF-Strömen (256 MiB). Zeilen, die wie
Seitenmarken aussehen, werden nach dem Zeilenumbruch und in allen Nicht-PDF-Dateien entschärft. Eine Quelle, die
zwischen Planung und Speichern eines Auftrags gelöscht wird, endet als „One of the files was not found. Please add it
again.“

**Begründung.** Der Sicherheitsreview von Etappe 1 zeigte: Eine 3-KB-Word-Datei beendete den ganzen Webprozess (Stack
Overflow), eine 400-KB-PDF brauchte 4,7 GB, und der alte Zeitdeckel hielt die Arbeit nicht an. Erst eine
Prozessgrenze macht Grenzen für Stack, Speicher und CPU durchsetzbar; die Prüfungen in den Extraktoren halten die
häufigen Fälle schnell.

### Zusatz zu D-069, zweite Runde — Grenzen für Uploads und extrahierten Text (2026-11-24)

**Entscheidung.** (1) Eine Quelle hat höchstens `Sources:MaxExtractedChars` Zeichen (3 Mio.; ein Buch mit 1.000 Seiten
hat rund 2 Mio.). Der Extraktionsprozess hört auf, sobald der Text die Grenze überschreitet (Word schon beim Prüfen der
XML-Teile, vor dem Dokumentmodell; PDF bei der Seite, die sie überschreitet), Textdateien werden vor dem Dekodieren
gezählt, und der Webprozess prüft noch einmal, bevor er den Text kopiert. (2) Die Antwort des Extraktionsprozesses ist
auf 16 MiB begrenzt (im Zusatz zu D-070 auf 20 MiB korrigiert: sechs Byte je UTF-16-Einheit, plus Platz für das
JSON) und wird gelesen, während sie hereinkommt. (3) Das Kontingent für Dateien ohne Auftrag zählt
auch Zeichen (20 Mio. je Person) und wird in der einfügenden Transaktion unter einer Sperre je Person
(`pg_advisory_xact_lock`) noch einmal geprüft. (4) Der Upload-Endpunkt liest den Multipart-Body als Strom ohne
Formularpuffer und belegt den Upload-Platz der Person (höchstens zwei gleichzeitig, sonst HTTP 429) und den globalen
Upload-Platz, bevor er den Body liest; eine Anfrage endet nach 5 Minuten (HTTP 408) oder wenn sie langsamer als
4 KiB/s sendet; eingefügter Text belegt keinen Platz. (5) Zeilenenden werden vereinheitlicht, bevor Zeilen, die wie
Seitenmarken aussehen, entschärft werden; ein einzelnes `\r` kann also keine Seite fälschen. (6) Die Compose-Datei
begrenzt den Web-Container auf 6 GiB (`mem_limit: 6g`).

**Begründung.** Der zweite Sicherheitsreview zeigte: Eine 399-KB-Word-Datei (Kompressionsverhältnis 140, innerhalb
aller Archivgrenzen) lieferte 58,7 Mio. Zeichen und mehr als 1 GB im Webprozess je Upload, das Kontingent zählte nur
Dateibytes und ließ sich mit parallelen Uploads überschreiten, und der Request-Body wurde gepuffert, bevor ein Platz
belegt war. Eine Textgrenze an der Quelle schließt den ersten Punkt dort, wo der Text noch im begrenzten Kindprozess
liegt; die Sperre macht das Kontingent genau; das Streamen macht die Plätze wirksam. Das Speicherlimit ist die zweite
Schicht: Der Webprozess bleibt üblicherweise unter 1 GB, jeder Extraktionsprozess darf 1 GiB verwalteten Speicher
nutzen und höchstens zwei laufen gleichzeitig, der Rest ist Puffer; das Limit verhindert, dass ein Dienst einen
gemeinsam genutzten Host ins Auslagern oder zum OOM-Killer treibt.

### Zusatz zu D-070 — Längenprüfung bewusst konservativ; Antworten beim Upload (2026-11-24)

**Entscheidung.** (1) Die Längenprüfung ohne Modell blockiert nur bei eindeutiger Lage und grober Abweichung; sonst
bleibt es bei einer Notiz. Eine Länge wird nur dann zur blockierenden Regel, wenn der ganze Auftrag (ohne Zitate, Code
und angehängten Text) genau eine Längenangabe oder ein klares Paar enthält, keine andere Mengenangabe, keinen Rückbezug
auf einen früheren Text und keine Überarbeitung, ein an die Zahl gebundenes Grenzwort, keine fremde Präposition
zwischen Befehlsverb und Länge im selben Teilsatz und keine Zusammensetzung wie „300-Wörter-Text“ (`LengthRuleGate`).
Die Richtungsregeln der zweiten Runde gelten zusätzlich. (2) Eine Korrektur wird nur verlangt, wenn der Text die Grenze
um mehr als 25 % verfehlt; eine kleinere Abweichung ist ein offener Punkt. (3) Ist die Länge der einzige blockierende
Befund, endet die Anweisung mit „Keep everything else unchanged.“ (4) Sind alle Upload-Plätze belegt, antwortet der
Upload mit 503, `Retry-After: 30` und der Quittung; die Kontingent-Meldung nennt keinen Weg mehr, den die Seite nicht
bietet; eine zu lange Word- oder Textdatei verlangt eine kürzere Fassung statt einer Seitenauswahl. (5) Die
Antwortgrenze des Extraktionsprozesses beträgt 20 MiB (sechs Byte je UTF-16-Einheit, weil Emoji und Formatzeichen
maskiert werden); das korrigiert Punkt (2) der zweiten Runde.

**Begründung.** Jede Prüfrunde fand eine neue Klasse von Falsch-Blockierungen in der wortbasierten Lesart von Längen
(Obergrenze als Untergrenze, Verneinung im Folgesatzteil, die Grenze eines Tweets als Grenze von dreien, Zahlen einer
Studie oder eines Buchs, die Länge der vorigen Fassung, Längen in einer zitierten Mail). Eine Falsch-Blockierung ist
teuer: Die Korrekturrunde zwingt den Schreiber gegen den Wunsch der Person, bis zu mehreren tausend Wörtern. Das
Flicken einzelner Sätze konvergierte nicht; deshalb ist die Regel jetzt strukturell, Präzision geht vor Trefferquote,
und die Falsch-Blockier-Rate wird an synthetischen Aufträgen gemessen, die ein getrennter Generator vor dem Extraktor
beschriftet hat (Entwicklungsmenge, zurückgehaltene Menge und eine zweite Menge im Eiltipp-Stil). Die Schwelle von
25 % begrenzt den Schaden eines verbleibenden Lesefehlers auf Texte, die weit daneben liegen.


## D-071 — Etappe 2: Klären vor dem Lauf (2026-11-25)

**Status:** umgesetzt (Etappe 2: Klärer, Gate, Worker, Messwerkzeug, Auftragsdienst, Profil, Web und MCP), ausgeliefert mit
`Orders:IntakeMode = Shadow`. `On` folgt erst in einem eigenen Commit, wenn die Abnahmetore aus Korpusmessung
und Schattenbetrieb erfüllt sind.

**Entscheidung.** Vor dem ersten Lauf analysiert ein **Klärer** den Auftrag in genau einem strukturierten Aufruf.
Was er vorschlägt, setzt ein **Gate im Code** auf die Fragenregel zurück. Die Person bekommt höchstens drei kurze
Fragen mit Klickantworten oder gar keine; jeder Fehler startet den Auftrag mit dem Wortlaut.

- **Klärer** (`LlmClarifier`): Slot `ModelSlot.Intake` (Sonnet, Rückfall Sol), `response_format` json_schema
  `clarify_order`, `reasoning_effort: low`, harte Grenze `Intake:Timeout` (40 s), Failover erlaubt. Er sieht den
  Wortlaut, je Datei Anfang und Ende (2.000 + 1.000 Zeichen, zusammen höchstens 12.000, höchstens fünf Dateien)
  und das Profil. Tags im Material werden entschärft; das Material gilt als zu analysierender Stoff, nicht als
  Anweisung. Kein zweiter Versuch in der Anwendung: Der Proxy wiederholt eine ungültige Antwort schon einmal.
- **Gate** (`QuestionGate`, reine Funktion, jede Regel getestet): Profil vor Frage (Sprache, Absender, Signatur,
  Anrede); Recherchierbares wird nie gefragt; Fragen, die die Person nicht beantworten kann oder die wenig ändern,
  werden Annahmen; bei einer Überarbeitung nie Länge, Ton oder Anrede und höchstens eine Frage; die Länge nur aus
  der festen Vorlage (Deutsch, Englisch; sonst Annahme) und nur, wenn der Wortlaut keine nennt (eine Menge mit
  Längeneinheit zählt an jeder Stelle, etwa „zwei Absätze“, „maximal drei Sätze“, „ein paar Zeilen“, „a 2-page report“,
  dazu Tweet, SMS, One-Pager, Einzeiler und ein-/zweiseitig; bewusst breit, auch „im Buch fehlen zehn Seiten“ zählt, denn
  ein Fehltreffer lässt nur den Schreiber wählen, eine Lücke fragte nach schon Geschriebenem; diese Erkennung speist nie
  eine Längenprüfung des Textes); eine Modellfrage wird nur unter format, scope oder other und nur dann zur Vorlage, wenn sie
  ausdrücklich nach der Länge des Textes fragt („Wie lang soll der Beitrag werden?“, „Soll das Schreiben kurz oder lang
  sein?“), nie eine Frage nach einer Dauer, Gültigkeit oder Menge („Wie lange wohnst du schon dort?“, „Wie lange soll die
  Einladung gültig sein?“); Frage ≤ 120 Zeichen,
  2–4 Optionen ≤ 60 Zeichen, keine Füllantworten („egal“, „other“), keine Sperrwörter (`JargonBlocklist`, mit
  Pluralformen), keine fremde Sprache; ein Thema, eine Frage; Länge zuerst; höchstens drei. Jede verworfene Frage
  wird Annahme, außer ihr Thema wird ohnehin gefragt (eine verworfene Doppelfrage setzt nie eine Vermutung neben die
  Antwort; enge Themen nehmen auch aus anderen Lücken keine Annahme; Sachverhalte und Sonstiges behalten ihre, bei einer
  verworfenen Doppelfrage die vom Modell gelieferte). Alle
  Kennungen vergibt das Gate.
- **Ablauf** (`OrderIntakeService`, `OrderIntakeWorker`): atomarer Claim, höchstens zwei Klärungen gleichzeitig,
  ein Wecksignal nach dem Einreichen. Null Fragen starten den Lauf im selben Schritt (`Clarified`), sonst wartet der
  Auftrag mit gespeicherten Fragen. Hängende Klärungen starten nach 60 s Warteschlange oder zwei Versuchen ohne
  Fragen, unbeantwortete Fragen nach 24 h mit den Annahmen. Claims aus der Zeit vor dem Prozessstart gelten
  sofort als verwaist.
- **Schatten:** Im Modus `Shadow` läuft der Auftrag wie in Etappe 1; der Worker klärt zusätzlich, wenn die
  Live-Warteschlange leer ist, und speichert nur in `OrderIntakeShadows` (höchstens 50 je UTC-Tag).
- **Beantwortete Länge:** Die Antwort auf die Vorlagenfrage wird eine Längenprüfung „etwa n Wörter“ (±20 %), ohne
  Textparser (`IntakeConstraints.WithAnswers` in `OrderGroundingStep`). Freitext wird nie geparst.
- **Messung:** `tools/Eval/geef_eval/intake_corpus.py` erzeugt und beschriftet einen synthetischen Korpus
  (Rückgabewert 4, solange Einträge fehlen oder unbeschriftet sind), `tools/IntakeEval run` misst Klärer und Gate darauf,
  `IntakeEval evaluate` und `IntakeCorpusTests` spielen die gespeicherten Rohantworten ohne Modell erneut durch und geben
  jedem Tor ein Urteil (bestanden, nicht bestanden, nicht auswertbar). Kein Tor besteht ohne Daten: fehlende Einträge,
  falsche Korpusgrößen, unbeschriftete Einträge oder eine leere zurückgehaltene Menge bzw. ein leerer Zweitkorpus machen
  alle Tore nicht auswertbar, ebenso ein Ergebnis zu einer anderen Eingabe (jede Zeile trägt einen Hash über Auftrag,
  Dateien, Profil, Klärer-Prompt und Schema; `run` überspringt solche Einträge nicht); die Fehlerquote zählt jeden
  Versuch, die Latenz jede Antwort und Zeitüberschreitung (schnelle Fehler schönen sie nicht); eine Antwort über der Produktionsgrenze oder eine,
  die sich nicht nachspielen lässt, ist ein Fehler. `IntakeEval shadow` beurteilt die Tore a, f und g aus einem nur
  lesenden CSV-Export von `OrderIntakeShadows` (siehe `tools/IntakeEval/README.md`).

**Begründung.** In der Messung vom 2026-11-24 fragte ein Agent mit freiem Werkzeugzugang in jedem Fall und brauchte
dafür zu lange. Ein einzelner strukturierter Aufruf mit kleinem Modell und ein Gate im Code machen die Fragenregel
überprüfbar, statt sie dem Modell zu überlassen. Annehmen ist billiger als eine überflüssige Frage, deshalb ist das
Gate streng und jede Verwerfung nur eine Annahme mehr.

**Weitere Entscheidungen der Etappe 2 (Auftragsdienst, Profil, Web, MCP; Review-Runde 1).**

- **Brief** (`Intake:BriefMode`, Standard `AppendAnswers`): Der Wortlaut steht vorn und unverändert; nach einem Trenner
  folgen die Antworten der Person (Antworten des KI-Clients sind gekennzeichnet, auch die Antwort auf die
  Überarbeitungs-Vermutung), die korrigierten Annahmen, die angezeigten und nicht korrigierten Annahmen als unverbindliche
  Vorgaben (die Überschrift sagt, dass Antworten und Korrekturen der Person immer Vorrang haben und die Annahmen nur
  gelten, wo weder sie noch Wortlaut oder Dateien etwas anderes sagen) und das Profil. Annahmen werden nie zu einer
  blockierenden Prüfung. `PreparedBrief` ergänzt, was der Klärer verstanden hat. Ein Start vor dem Lesen des Auftrags hat
  keine Annahmen, und kein Text behauptet welche.
- **Antworten** (`answer_order`, Zustand `needs_input`): Herkunft `user` oder `client`, nie `memory`. Eine Frage wird mit
  einer Option, eigenen Worten (nur wo erlaubt) oder „entscheide du“ beantwortet, nie mit zweien davon; eine leere
  Antwort wird abgelehnt; Annahmen nehmen nur eine Korrektur als Freitext. Offene Fragen gelten als „entscheide du“. Ohne
  jede Antwort gilt der Auftrag als übersprungen, auch bei „Just start“ im Web, das alles mitschickt, was die Person schon
  getan hat. Antworten, die nach dem Start ankommen, werden als nicht verwendet gemeldet.
- **Überarbeitung:** Eine Datei wird nur dann zum Ausgangstext, wenn der Auftrag ein Überarbeitungsverb enthält **und**
  der Klärer eine Überarbeitung erkennt, auch bei genau einer Datei. Ohne Urteil des Klärers (Fehler, aus, Überspringen
  während des Lesens) ändert keine Datei ihre Rolle, wie in Etappe 1. Die Vermutung steht in Web und MCP
  (`revisionFile`) und kann abgelehnt werden.
- **Profil** (`UserProfiles`, Migration `Step49Intake`): eine Zeile je Benutzername, nur von der Person selbst unter
  „About me“ bearbeitet. Es geht an den Klärer sowie an Schreiber und Prüfer. Es wird mit dem Konto gelöscht, bei einer
  Umbenennung mitgezogen, und ein neues Konto findet nie ein altes Profil vor. Konto und Profil ändern sich in einer
  Datenbanktransaktion (`IAccountStore`); die Läufe, die unter einem gelöschten oder aufgegebenen Namen bleiben, verlieren
  den Profilblock im gespeicherten Brief (an seiner Stelle steht ein Hinweis). Die Tokens eines Kontos werden vor jeder
  Änderung widerrufen, auch bei einer Umbenennung. Laufseiten und `get_run_details` zeigen den
  Profilblock nur dem Besitzer; alle anderen, auch Admins, sehen stattdessen einen Hinweis.
- **Externe Dienste:** Web-, Nachrichten- und Wissenschaftssuche, die Embeddings der Learning-Suche, die Anfrageformung
  und die Verfeinerung bekommen nur den Wortlaut des Auftrags, nie Antworten, Annahmen oder das Profil. Auch
  Learning-Zusammenfassungen nutzen nur den Wortlaut.
- **Gate, Inhalt:** Texte werden zuerst normalisiert (NFKC, ohne Formatzeichen, ungewöhnliche Punkte als Punkt gelesen).
  Fragen, Optionen, Annahmen und die Zusammenfassung werden verworfen, wenn sie einen Link, eine Web- oder Mailadresse,
  eine IP-Adresse mit Pfad oder einen Markdown-Link enthalten; ein nackter Name wie `Booking.com` oder `Node.js` ist nur
  erlaubt, wenn er wörtlich im Auftragstext der Person steht, nie wegen einer Datei. Verworfen wird auch, was Passwort,
  PIN, TAN, Schlüssel, Token, Karten- oder Kontonummer in irgendeiner Form nennt (auch in Zusammensetzungen; „pin“ klein
  nach einem Besitzwort), außer der Auftrag selbst behandelt dieselbe Art (Zugangsdaten bzw. Zahlungs- und Kontodaten;
  ein Auftrag mit IBAN erlaubt kein Passwort); die Bitte um das Geheimnis selbst wird immer verworfen. Verworfen wird nur das
  Element, nie der Auftrag. Die MCP-Hinweise beschreiben diese Felder als aus Auftrag und Dateien abgeleitet, nicht als Anweisungen.
- **Robustheit:** Ein scheiternder Claim gibt seinen Platz zurück; ein Auftrag, der im Sammellauf scheitert, wird
  15 Minuten zurückgestellt, mit jedem weiteren Fehlschlag doppelt so lange bis höchstens 24 Stunden, sodass keine Zahl
  defekter Aufträge die jüngeren aushungert; „Stop“ versucht es erneut (bis zu dreimal), wenn sich
  der Zustand im selben Moment geändert hat.
- **Start des MCP-Hosts:** Der Host startet nicht, wenn ein Werkzeugparameter weder Client-Wert noch SDK-Wert noch
  registrierter Dienst ist (`McpToolBindingCheck`), damit eine fehlende Registrierung nie einen Dienst vom Client füllen
  lässt.
- **Bekannte Grenze für Etappe 7:** `IMemoryResolution` wendet „Gedächtnis vor Frage“ nur auf die vier Profilthemen an
  (Sprache, Absender, Signatur, Anrede); `IntakeRecord.FromMemory` wird gespeichert, aber nicht gelesen. Gemerkte
  Antworten aus Etappe 7 brauchen beides erweitert.
- **Bekannte Grenze, Konten:** Läufe und Aufträge hängen am Benutzernamen (älter als Etappe 2). Ein neues Konto mit dem
  Namen eines gelöschten oder umbenannten Kontos sieht diese alten Läufe und Aufträge mit Wortlaut, Antworten und
  Ergebnis; nur der Profilblock wird aus ihren Briefen entfernt. Profilwerte im gespeicherten Klärprotokoll
  (`FromMemory`) bleiben; sie werden nirgends angezeigt.

## D-072 — Klärer eingeschaltet (`IntakeMode=On`) nach Messung (2026-11-25)

**Entscheidung:** Ausgeliefert wird `Orders:IntakeMode = On`. Der Code-Standard bleibt `Shadow`.

**Messung** (synthetischer Korpus: 120 Haupt- und 40 Zweiteinträge, von einem Modell erzeugt, von einem anderen blind
beschriftet; Eingabe-Hash je Ergebniszeile): Auf der zurückgehaltenen Menge und dem Zweitkorpus bestehen die Tore a–f.
160 von 160 Aufrufen erfolgreich, Median rund 6 Sekunden, keine Frage bei vollständigen Aufträgen, keine Frage zu einem
verbotenen Thema, im Mittel höchstens 0,25 Fragen je Auftrag. Die Warnschwelle e′ ist unterschritten (6–27 % der als nötig
beschrifteten Lücken werden gefragt); das ist gewollt, im Zweifel nimmt der Schreiber an oder setzt einen Platzhalter.

**Drei Korrekturen aus der Messung:**
1. Das Schema begrenzt keine Textlängen mehr. Das Gateway verwarf die ganze Antwort, sobald eine Annahme zu lang war
   (5 von 160 Aufrufen, vor allem Überarbeitungen). Der Parser kürzt selbst.
2. Das Gate streicht Antworten, die nur ankündigen, die Angabe später zu liefern („Ich füge sie ein“, „Platzhalter“);
   bleiben weniger als zwei, entfällt die Frage. Abgeleitet aus der Entwicklungsmenge.
3. Der Prompt verlangt, dass jede Antwort so in den Text übernommen werden kann, und verbietet Fragen, nach deren
   Beantwortung für denselben Punkt trotzdem ein Platzhalter bliebe, sowie Fragen nach Formulierungen oder Lob.

**Sichtprüfung statt Tor g:** Tor g (eine Woche oder 30 echte Aufträge im Schattenbetrieb) ist bei der geringen Zahl echter
Aufträge nicht zeitnah erreichbar. An seine Stelle tritt eine unabhängige Prüfung aller 27 im Korpus gestellten Fragen im
Zusammenhang ihres Auftrags (Maßstab: höchstens 10 % schlecht). Erste Runde: 3 von 29 schlecht, 7 grenzwertig → nicht
einschalten, Korrektur 3. Zweite Runde mit frischem Prüfer: 20 gut, 6 grenzwertig, 1 schlecht (3,7 %) → einschalten.
Der Abstand ist knapp. Die zweite Messung nach den Korrekturen nutzt denselben Korpus; die zurückgehaltene Menge ist für
künftige Promptänderungen damit nicht mehr unberührt.

**Nachkontrolle:** Die Fragen echter Aufträge werden mit `tools/IntakeEval` und dem Auftragsbestand nach den ersten 30
geklärten Aufträgen erneut gesichtet. Rückweg ohne neues Image: `Orders__IntakeMode=Off` in der Umgebung des Webdienstes setzen; offene Aufträge im
Zustand `Analyzing` oder `NeedsInput` arbeitet der Worker weiter ab.

## D-073 — Etappe 3: Ehrlich schreiben (2026-11-25, Entwurf)

**Status:** umgesetzt (Schreiber-Notizen, Brief-Trennung, Faktenprüfer, Rechenprüfung der Quellen, „What was checked“,
Bericht Schema 2, Migration Step50, Web und MCP). Ausgeliefert wird `Direct:FactCheckMode = Report`, **obwohl Tor V2 nicht
bestanden ist** – als dokumentierte Abweichung (Abschnitt „Vorversuche“). `On` für alle erst nach Tor Q4 und den
Bedingungen unten, in einem eigenen Commit.

**Entscheidung.** Der Direktweg sagt, was er angenommen, geprüft und geändert hat, und behauptet nichts, was er nicht
prüfen konnte.

- **Brief-Trennung** (`Intake:ContentAssumptions = OpenPoints`): Annahmen des Klärers zu Sachverhalten (`Facts`, `Date`,
  `Sender`, `Signature`, `Other`) gehen nicht mehr als Vorgabe an den Schreiber, sondern unter „Not given by the person
  (never state these as facts …)“. Formannahmen bleiben Vorgaben. Ohne gezeigte Karte heißt die Überschrift nicht mehr
  „shown to the person“ (Nebenbefund, ohne Schalter).
- **Block am Dateiende** (`Direct:WriterNotes`): Der Schreiber hängt `<!-- atelier-notes {JSON} -->` mit eigenen Annahmen,
  offenen Punkten und wörtlichen Belegstellen an. Der Code trennt ihn vor Prüfung, Ablage, Export und Anzeige ab
  (`WriterNotesFormat.Split`); Quellen werden entschärft, damit eine Datei den Text nie vorzeitig beendet.
- **Faktenprüfer bei jedem Standardauftrag** (`SourceFactCheckReviewer`, Profil `source-fact-check`, Slot `FactReviewer`,
  heute Codex Astra): Die Vorlage `direct` hat jetzt zwei Prüfer (`deterministic-checks`, `source-fact-check`). Es gibt
  keine Auswahl nach Schwierigkeit, Länge oder Tageskontingent. „Nicht geprüft“ entsteht nur durch den Schalter `Off`,
  durch `Shadow` oder durch einen Ausfall auch des Ersatzmodells; jeder Grund steht ehrlich in „What was checked“.
  - Drei Fragen: nicht gedeckte Tatsachen (auch erfundene Rückmeldewege), verletzte Vorgaben, Fehler des Materials
    selbst (Widersprüche, Rechenfehler, verworfene oder ersetzte Angaben).
  - Material sind Wortlaut, Antworten von Person und Client, Profil und Dateien (bei einer Überarbeitung der Ausgangstext
    als `S0`), **nie** Annahmen oder Notizen. Über 150.000 Zeichen (Material und Text) prüft er ohne Dateiinhalt und sagt das.
  - Ein Befund zählt nur mit wörtlichem Zitat, das der Code nachprüft (`FactFindingFilter`). Herkunft einer Vorgabe und
    ob ein Befund blockiert, folgen aus der Fundstelle, nie aus dem Modell: Wortlaut, Antwort der Person und Profil
    blockieren, Antworten des Clients sind Hinweise, Widersprüche der Dateien blockieren nie und werden immer gezeigt.
  - Runde 2 prüft nur die eigenen Befunde nach: Was wörtlich noch dasteht, bleibt ohne Aufruf offen; für den Rest
    genau ein Aufruf `recheck_facts`.
  - Druckmodus mit `response_format`, Proxy-Failover aus. Kann das Modell nicht erreicht werden (nicht verfügbar, Limit,
    Zeitüberschreitung), folgt **ein** Versuch mit dem nächsten Kandidaten des Slots, der weder das Schreibermodell noch
    vom gescheiterten Anbieter noch bezahlt ist (heute Sonnet). Scheitert auch das, isoliert das SDK den Prüfer, die
    bestehende Warnung erscheint, der Text wird geliefert, der Bericht sagt „could not be completed“.
  - Stufen (`Direct:FactCheckMode`): `Off` ruft nicht; `Shadow` prüft und speichert, zeigt nichts; `Report` prüft und
    zeigt, ändert den Text nicht (Befunde `Warning`); `On` korrigiert zusätzlich einmal (blockierend → `Error`).
    `Direct:FactCheckModeOverrides` setzt den Modus je Nutzer (Messnutzer `On`), nur über die Umgebung.
- **Rechenprüfung der Quellen ohne Modell** (`Direct:SourceArithmetic`): Summen und Rechnungen in Dateien werden
  nachgerechnet; eine Abweichung erfährt die Person mit beiden Zahlen und welche der Text nutzt. Nie blockierend, nie
  still. Eine falsche ausgeschriebene Rechnung im Text selbst blockiert.
- **„What was checked“** steht immer direkt unter dem Text: Prüfzeilen nur für gelaufene Prüfungen, was nach der Prüfung
  geändert wurde, ob die Person etwas ansehen muss, und der feste Schluss „They do not prove that everything in the text
  is right.“ Die Sätze baut nur `OrderCheckLines`; Web und MCP (`checks`) sagen dasselbe, die Anzeigeregeln (Reihenfolge
  der Stellen, drei Zitate je Datei) stehen nur in `OrderResultRules`. Die Oberfläche zeigt nie den Modelltext des
  Prüfers, nur feste Sätze und wörtliche Zitate. Ohne Dateien heißt die Faktenzeile „compared with your request and your
  answers“, nie „and your files“.
- **Bericht Schema 2** (`Runs.ReportJson`, additiv): Notizen der ausgelieferten Runde, nur wörtlich in ihrer Datei
  gefundene Zitate (der Rest wird gezählt), Zustand und Umfang der Faktenprüfung samt Aufrufen und Modell, Rechenprüfung
  mit Textbezug, `Corrections` (in Runde 1 blockierend, in der gelieferten Fassung weg). Alte Berichte (Schema 1) bleiben
  lesbar und zeigen „Facts were not checked.“
- **Migration Step50** (nullable, additiv, idempotent): `Findings.Category`, `Code`, `Quote`; `Runs.FactCheckMode`.
  Keine Datenmigration, kein Index (es gibt keine Kappe, die zählen müsste).
- **Ausgelieferte Fassung:** keine eigene Auswahl; die SDK-Regel (wenigste Ablehnungen, bei Gleichstand die jüngste)
  ist bei zwei Runden und zwei Prüfern sachgerecht (Test `DirectDeliveryRuleTests`).

**Messung und Tore:** V1 (Block), V2 (Eichung des Prüfers, zurückgehaltene Menge), V3 (Rechenprüfung) vor den Merges;
danach die Tore Q1–Q8 mit dem Messnutzer über die Produktion. Wer Prompt oder Schema des Faktenprüfers ändert, misst neu
(`tools/FactCheckEval`).

**Kosten:** je Direktweg-Auftrag ein Codex-Aufruf je geprüfter Fassung: einer ohne Korrektur, zwei, wenn irgendeine
Prüfung (Tatsachen, Länge, Sprache …) eine Korrektur auslöste, in `Report` wie in `On` (seit der ersten Prüfrunde wird jede
geänderte Fassung voll geprüft); bei Codex-Ausfall ein Sonnet-Aufruf. Reicht das Kontingent dauerhaft nicht, entscheidet der Betriebsschalter, nie eine Auswahl im Code.

**Rückweg:** Schalter über die Umgebung ohne neues Image (`Direct__FactCheckMode=Off`, `Direct__WriterNotes=false`,
`Intake__ContentAssumptions=Defaults`, `Direct__SourceArithmetic=false`). Code: vorheriges Web-Image; Step50 bleibt (nullable,
der Code von Etappe 2 liest und schreibt unverändert), kein `Down()` in Produktion. Vorher darf kein Direktweg-Lauf
`Pending` oder `Running` sein: E3-Snapshots enthalten den Prüfer `source-fact-check`, den der Code von Etappe 2 nicht kennt.

**Vorversuche (2026-11-25):**
- **V1 (Block am Dateiende):** Ergebnis A, 15 von 15 Läufen ohne Beschädigung → `Direct:WriterNotes = true`.
- **V3 (Rechenprüfung):** bestanden → `Direct:SourceArithmetic = true`.
- **V2 (Eichung des Faktenprüfers): nicht bestanden.** Die Gültigkeit liegt bei **83,3 %** (10 von 12 Aufrufen für 10
  Texte; verlangt ≥ 95 %, unter 90 % gilt das Tor formal sogar als nicht auswertbar), das **p90 der Latenz bei 164 s**
  (verlangt ≤ 120 s). Ursache sind hängende Codex-Aufrufe: **3 von 31** Aufrufen über beide Mengen antworteten nicht vor
  der damaligen Grenze von 180 s; zwei der drei Texte (`fc-06/base`, `fc-14/base`) wurden wiederholt und antworteten dann in
  Sekunden, der dritte (`fc-15/base`, Entwicklungsmenge) wurde nie wiederholt. Die Ursache des Hängers ist
  **unbekannt**; die wahre Hängerquote liegt bei dieser Stichprobe etwa zwischen 3 % und 25 %. Die Qualitätskriterien sind
  bestanden: Treffer 17 von 17, 0,20 blockierende Befunde je sauberem Text (verlangt ≤ 0,25), 80 % saubere Texte ohne
  blockierenden Befund (verlangt ≥ 75 %), keine gemeldeten Quellenwidersprüche ohne fehlerhafte Quelle, Pflichtfall Elternabend
  bestanden. Diese Zahlen beruhen auf **10 synthetischen Texten**; 0,20 ist ein einziger Befund. Die Entwicklungsmenge ist
  **unvollständig**: 19 ihrer 22 Texte wurden aufgerufen (`fc-15/planted`, `fc-16/base` und `fc-16/planted` liefen nie,
  beide Grundlagen haben fehlerhafte Dateien), 18 beantwortet; die 0,38 unten und jede Neuzählung beruhen auf dieser Teilmenge.
- **Abweichung:** Ausgeliefert wird trotzdem `Report` (prüft und zeigt, ändert den Text nie), nach Entscheidung eines
  unabhängigen Beraters. Das ist eine Aufweichung des Tors, vertretbar, weil nur Betriebskriterien verfehlt sind, `Report`
  keinen Text ändert und der Ausfallpfad ehrlich ist („The fact check could not be completed.“). `Shadow` hülfe nicht
  (der Hänger tritt dort genauso auf), ein Wiederholungslauf auch nicht (bei rund 10 % Hängern besteht ein Zehnerlauf mit
  etwa 35 % Wahrscheinlichkeit zufällig). Der Betreiber kann per Schalter auf `Shadow` oder `Off` zurück.
- **Nicht ausgeführte Vertragszweige (ebenfalls Abweichung):** Weder „Parser und Schema prüfen“ (Gültigkeit) noch die
  Probe mit `reasoning_effort: low` bzw. die Senkung der Materialgrenze (Latenz) wurden ausgeführt: Bei den Hängern kam
  keine Antwort an, Parser und Schema sind nicht beteiligt, und die erfolgreichen Aufrufe waren schnell (Median rund 14 s,
  längster 86 s).
- `FactCheckCorpusTests` hält das Ergebnis fest: nie „bestanden“, die Verfehlung nur bei Gültigkeit und Latenz, die
  Qualitätswerte beider Mengen als Untergrenze (Treffer der Fragen a und b, blockierende Befunde auf sauberen Texten und
  jenseits der gepflanzten Fehler, Frage c, Widersprüche ohne fehlerhafte Datei) und die Zahl der behaltenen Befunde genau;
  ein Filter, der einen Treffer verliert, Quellenwidersprüche verwirft oder einen Fehlalarm hinzufügt, macht ihn rot (per
  Mutation geprüft: ein Filter, der jeden Quellenwiderspruch verwirft, lässt ihn scheitern).

**Zeitgrenzen und Hänger (Auflagen des Beraters):**
- Zeitgrenze des Prüfmodells **90 s** (`Direct:FactCheckTimeout`, vorher 180 s), des Ersatzmodells **60 s**
  (`Direct:FactCheckFallbackTimeout`); beide konfigurierbar, die ausgelieferten Werte hält ein Test fest. Mit 90 s ist
  „p90 ≤ 120 s“ **per Konstruktion** erfüllt – das ist **kein Nachweis** schnellerer Prüfung.
- Beim Hänger: kein zweiter Codex-Versuch (geteiltes Kontingent), genau ein Versuch mit dem Ersatzmodell, sonst „Prüfung
  nicht abgeschlossen“. Eine abgebrochene Prüfung löst **keine Korrekturrunde** aus (ein ausgefallener Prüfer blockiert
  die Konvergenz nicht; Ende-zu-Ende-Test).
- Das Atelier schickt dem Gateway seine Grenze mit (Header `x-cli-proxy-timeout-seconds`, 5 s darunter). Das Gateway
  beendet den CLI-Prozess samt Kindprozessen, bevor das Atelier aufgibt; ebenso, wenn ein Aufrufer die Verbindung trennt.
  Vorher lief ein abgebrochener Codex-Prozess bis zur Obergrenze des Gateways weiter und verbrauchte Kontingent eines
  anderen Produktivsystems. Eine solche Zeitüberschreitung öffnet keinen Breaker und löst keinen Failover aus; das
  Atelier wiederholt Anfragen mit dieser Grenze nie auf HTTP-Ebene.
- Jeder Hänger erscheint als eigene strukturierte Logzeile (Aufruf, Modell, Dauer, Grenze, Lauf, Runde, Ersatz ja/nein),
  ohne Inhalte.
- **Bewusste Abweichung vom Berater:** Er riet, Codex-Sitzungen zu speichern, um die Ursache zu finden. Das geschieht
  nicht: Die Sitzungen enthielten Aufträge, Dateien und Texte der Nutzer, und das Gateway ist bewusst zustandslos (Codex
  läuft mit `--ephemeral`). Die Ursache wird über die Logzeilen (Häufung nach Modell, Dauer, Tageszeit) und, falls nötig,
  einen eigenen Versuch mit synthetischen Texten gesucht.
- **Folgen für die Wartezeit:** Etwa jeder zehnte Auftrag bekommt den Ersatzprüfer oder keine Prüfung und wartet bis zu
  150 s länger (90 s + 60 s).

**Erste Prüfrunde (2026-11-25):**
- „What was checked“ nennt eine Prüfung nur als erfüllt, wenn sie lief und bestand; eine gelaufene, nicht bestandene
  Prüfung steht als nicht erfüllt da.
- Jede geänderte Fassung wird voll geprüft (aus jedem Grund, auch in `Report`); die Nachprüfung ersetzt keine volle Prüfung
  mehr. Ein Modell, das hing, wird im selben Lauf nicht erneut gerufen. Ein ausgelieferter Text, den keine Prüfung sah, sagt
  das („The facts of this version of the text were not checked …“, MCP `version_not_checked`); frühere Befunde erscheinen
  nur, wenn ihre Stelle noch im Text steht.
- Der Inhaltsschutz verwirft nie einen Befund: Eine unsichere Begründung wird durch einen festen Satz ersetzt, das Zitat aus
  dem Text bleibt; unsichere Materialstellen und unsichere Notizen des Schreibers werden ersetzt bzw. sichtbar gezählt
  („1 more note … could not be shown“).
- Die Rechenprüfung der Dateien schweigt bei Abzügen, Anzahlungen, Salden, Steuern, Beträgen pro Person, Mengenangaben und
  Summen, die nur zu ihren letzten Posten passen; der Satz verspricht nur das Nachgerechnete. V3 besteht weiter.
- Große Dateien: Der Satz sagt, dass Tatsachen nicht geprüft wurden, nur die Vorgaben.
- Widersprüche nennen die Herkunft beider Seiten (Auftrag, Antworten, About-me-Angaben, eine Datei).

**Dritte Prüfrunde (2026-11-25):**
- Die Rechenprüfung behauptet nie, dass Rechnungen oder Dateien stimmen. Sie meldet nur eindeutige Abweichungen mit der
  Bitte, sie zu prüfen, und sagt sonst höchstens, dass nach eindeutig falschen Rechnungen bzw. Summen gesucht und keine
  gefunden wurde, mit dem Zusatz, dass nicht jede Rechnung so prüfbar ist. Damit entfällt die Unterscheidung „alle geprüft“
  gegen „teilweise geprüft“; alte Berichte lesen sich genauso.
- Im Nachsatz hinter einer Summe sind nur Anteilswörter (pro, je, jeweils, per, each) unschädlich; Abzugs-, Rabatt-,
  Zuschuss-, Anzahlungs- und Steuerwörter machen die Summe auch dort unklar, sie bleibt dann unerwähnt.
- Ein früherer Befund entfällt nur, wenn eine behaltene Stelle gleicher Art ihn ganz enthält; Widersprüche gelten nur an
  Wortgrenzen und bei gleichen Dateien als dieselben. Ein Widerspruch zu einer falschen Summe entfällt nur, wenn sich seine
  Zitate außer in Beträgen und Summenwörtern in nichts unterscheiden. Wird eine frühere Fassung ausgeliefert, kommen Befunde
  späterer Runden dazu.
- Beim Löschen oder Umbenennen eines Kontos verliert auch der gespeicherte Startauftrag in den Ereignissen
  (`PipelineStartedEvent.Input`) den About-me-Block, ebenso jeder andere Ereignistext mit dem Block oder den Angaben; früher
  bereinigte Läufe werden nachbereinigt.

**Bedingungen vor `On`** (zusätzlich zu Tor Q4):
- Ursache der Hänger gefunden, **oder** höchstens 1 Hänger in mindestens 60 echten Aufrufen.
- Das Ersatzmodell an denselben Qualitätskriterien gemessen (es prüft etwa jeden zehnten Auftrag).
- Entwicklungsmenge neu beschriftet und Fehlalarme neu gezählt (dort 0,38 je sauberem Text, über 0,25; in `On` ändern
  Fehlalarme Texte).
- Gesamtwartezeit einschließlich Korrekturrunde gemessen.

## D-074 — Etappe 4: Recherche (2026-11-25)

**Status:** umgesetzt (Kern, Dienst mit Tavily und Persistenz, Einbau in den Direktweg, Ergebnis in Web und MCP, Messwerkzeuge
VT/VR und R3). Vorversuche VT und VR **bestanden**. Ausgeliefert mit `Research:Mode = Off` (Code und `appsettings.json`); der
Messnutzer bekommt `On` nur über die Umgebung (`Research__ModeOverrides__<nutzer>=On`, kein Nutzername im Repository; Test
`ShippedResearchSettingsTests`). `On` für alle erst nach Tor R3, in einem eigenen Commit.

**Entscheidung.** Was im Ergebnis als „nachgeschlagen“ steht, ist ein vom Code wörtlich im abgerufenen Seitentext gefundener
Ausschnitt mit Seite und Datum – nicht mehr. Lieber weniger nachschlagen als etwas Ungeprüftes zeigen.

- **Wann:** nur der erste Lauf eines geklärten Auftrags im Direktweg (Version 1, kein Ausgangstext, Klärung `Ok` mit
  Suchthemen, Schalter `On` global oder für den Auftraggeber), nie eine Überarbeitung, nie „Extra careful“, nie
  `submit_request` ohne Klärung. **Nie mit angehängter Datei** (Prüfrunde 1, strukturell statt Wortfilter): Hat der Auftrag
  irgendeine Datei – gleich welche Rolle, auch eine Überarbeitungsvorlage –, geht weder an Tavily noch an die Verdichtung
  etwas. Gespeichert wird einmal der Status `FilesAttached` (`files_attached`) mit dem Satz „No web search was done because you
  attached files, so nothing from them can reach a search engine.“ in Web und MCP; jeder Folgelauf zeigt ihn und nutzt für
  einen solchen Auftrag nie gespeicherte Einträge. Recherchierbare Lücken bleiben wie ohne Recherche („not given“, nicht
  angenommen), kein Text behauptet, es sei nachgeschlagen worden. Ohne Suchthemen gibt es keinen Status. Die Recherche liegt am Auftrag (`Orders.ResearchJson`); jeder spätere Lauf nutzt sie
  unverändert und sucht nie neu. Jede gelaufene Recherche wird gespeichert, auch eine gescheiterte (bedingtes Setzen, wer
  verliert, liest die gespeicherte).
- **Herkunftsregel für Suchthemen** (`ResearchQueryGate`, ohne Wortliste): Tavily bekommt höchstens vier Themen des Klärers, nie
  Wortlaut, Antworten oder Profil (Dateien gibt es dabei nie, siehe oben). Geprüft wird gegen die **Werte** des Profils im
  Briefing, ohne die festen Feldnamen und ohne die Anredeformel (`ProfilePrompt.Values`). Ein Thema mit einem Wort aus dem
  Profil, das nicht genau so im Wortlaut der Person steht, wird zurückgehalten; ebenso Varianten (Akzente, ß/ss,
  ausgeschriebene Umlaute), gemeinsame Wortanfänge ab fünf Zeichen, CamelCase- und Buchstabe/Ziffer-Teile
  (`ArztbriefSchmidt`), der Genitiv auf -s (`Wolfs`), Wörter mit unsichtbaren Zeichen (weiches Trennzeichen, Nullbreite – wie im
  `QuoteVerifier` entfernt) und Zahlen ab vier Ziffern oder mit Punkt/Komma zwischen Ziffern, über Schreibweisen hinweg und auch
  ohne Nachkommanullen (`1.250,00` = `1250`; auch Apostroph und schmales Leerzeichen zwischen Ziffern trennen: `4'850`,
  `4’850`); dazu Links, Adressen und Zugangs- oder Kontodaten (`IntakeContentGuard`). **Sprachregel:**
  Hat das Profil Freitext oder „Never do this“ und ist die Sprache eines Themas sicher eine andere als die des Auftrags
  (Funktionswörter oder ä/ö/ü/ß nur einer Sprache), wird es zurückgehalten. Die Regel irrt nur zur sicheren Seite; ein
  zurückgehaltenes Thema wird wie vorher angenommen und sichtbar gemeldet. **Bewusste Grenzen:** Übersetzungen ohne
  Sprachsignal (reine Fachwort-Themen wie „breast cancer sick leave“), Synonyme, Grundwörter von Komposita („Insolvenz“ aus
  „Privatinsolvenz“), Namen mit zwei Zeichen, getrennt/zusammen geschriebene Namen ohne Großbuchstaben, Homoglyphen; ändert die
  Person ihr Profil zwischen Klärung und Start, gilt das Profil des Briefings.
- **Tavily:** je Thema genau eine Suche (`basic`, drei Treffer, Rohtext an, Tavily-Antwort aus), bis zu vier parallel, 20 s,
  **nie wiederholt**. Verwendet wird nur der Rohtext der Seite, nie Tavilys Ausschnitt oder Antwort. Ein längerer Rohtext wird
  auf 6.000 Zeichen und dann **auf das letzte sichere Satz- oder Zeilenende zurück** gekürzt, **das mindestens die Hälfte
  erhält** (Prüfrunde 2: Punkt nach einer Ziffer, nach einem einzelnen Buchstaben oder einer bekannten Abkürzung wie `ca.`/`Dr.`/
  `z.B.` oder vor einem kleingeschriebenen Wort oder einer Ziffer zählt nicht, ebenso wenig ein Zeilenumbruch vor einer klein
  weitergehenden Zeile; `。！？` beenden einen Satz). Ohne solches Ende an der letzten Leerstelle, die nicht zwischen Ziffern
  steht, danach hinter dem letzten Komma, Semikolon, Doppelpunkt oder der letzten schließenden Klammer (auch in voller Breite),
  die nicht zwischen Ziffern stehen; erst ohne all das bleibt die Seite weg. Treffer mit
  Benutzerangaben in der Adresse oder über 500 Zeichen Adresse werden verworfen. Der Atelier-Server ruft keine der Adressen
  selbst ab. **Tagesgrenze:** Jede Suche wird vor dem Start atomar reserviert (Advisory-Lock, `Orders.ResearchAt` = Zeit der
  Reservierung): höchstens 50 Aufträge je UTC-Tag insgesamt und 20 je Konto (`DailyOrderCapPerUser`); abgebrochene Suchen
  zählen, eine Wiederholung desselben Auftrags zählt einmal. `FilesAttached`, „zurückgehalten“ und „Tagesgrenze“ reservieren
  nichts. Verbleibend: Das Löschen eines Auftrags senkt den Zähler (der Schaden ist je Versuch ein ganzer Auftrag).
- **Verdichtung:** ein Aufruf im Slot `GroundingRefiner` (heute Sonnet), Schema `pick_passages` ohne Längengrenzen, Proxy-Failover
  aus, 60 s, kein zweiter Versuch. Er sieht **nur** die gesendeten Themen und die Seiten (Tags entschärft), nie etwas vom
  Auftrag, und wählt Ausschnitte aus; eine eigene Aussage des Modells gibt es nicht.
- **Dossier = wörtliche Ausschnitte** (`DossierBuilder`): Jeder Vorschlag wird im Rohtext gesucht (wortgleich oder nur im
  Leerraum/Anführungszeichen normalisiert, nie näherungsweise); gespeichert wird der Originalausschnitt der Seite mit Lage und
  SHA-256 des Rohtexts. Ein Eintrag beginnt und endet nie in einem Wort oder einer Zahl (`1,5`, `1.250`, seit Prüfrunde 2 auch
  `1 500`, `4'850`, `1’250’000`: Leerzeichen oder Apostroph zwischen ein bis drei Ziffern und genau drei Ziffern gehören zur
  Zahl; die unabhängige Nachprüfung `research_verify.py` meldet so einen Schnitt ebenfalls). Verworfen und gezählt
  werden: nicht auf der Seite, zu kurz (unter fünf Wörtern), unsicher (Links, Adressen, Zugangs- oder Kontodaten), doppelt (ein
  späterer, längerer Ausschnitt ersetzt einen früheren, den er enthält), über den Grenzen (drei je Seite, vier je Thema, zwölf
  insgesamt), unbekannte Seite, über 600 Zeichen (nie gekürzt). Ein unsicherer Seitentitel wird durch den Host ersetzt (auch
  beim Anzeigen und Rendern alter Einträge); im Schreiber- und Prüfer-Material steht die Adresse ohne Query und Fragment.
- **Im Lauf:** Die Ausschnitte gehen als Quelle `W1` („Looked up on the web“) nach den Dateien an Schreiber und Faktenprüfer,
  nie an die Nachrechnung der Summen. System-Prompt des Schreibers sowie Prompts, Schemata und Version des Faktenprüfers bleiben
  unverändert (Hash-Tests; Messung V2 bleibt gültig); der Nutzerprompt des Schreibers bekommt nur mit `W1` einen festen Satz
  und zählt `W1` nie als angehängte Datei (ohne Dateien eine eigene Einleitung). Eine Vorgabe aus `W1` blockiert nie. Ein
  `superseded`-Befund, dessen Materialzitat aus `W1` stammt, blockiert nie, verdrängt nie einen anderen Befund an derselben
  Textstelle (er belegt keinen Bereich; Prüfrunde 2), wird nie „your files“ genannt und geht nie als „die
  Dateien sagen“ in den Korrektursatz; er erscheint als Hinweis mit dem Satz „The pages looked up say otherwise – please check
  which is right.“ (Web: `data-kind="differs-from-looked-up"`, MCP: `kind = differs_from_looked_up` mit `note`), auch aus älteren
  Berichten; Prüfer-Prompt und -Schema bleiben unverändert. Kann die Recherche nicht gespeichert werden, läuft der Text ohne
  `W1` weiter (Status `Failed`, Code `not_stored`, Warnung). Ein Thema, das nur der Name einer Lückenart ist („Facts“), wird nie
  gesucht. Der Bericht führt `W1` mit `lookedUp` (Schema bleibt 2).
- **Was die Person sieht:** Abschnitt „Looked up“ mit Ausschnitt, Seite (nur `http`/`https` als Link, `nofollow noopener
  noreferrer`) und Datum unter der festen Unterzeile „… not that it is true or that the text uses it“; jeder Nicht-Erfolg
  (nichts gefunden, Fehler, Tagesgrenze, zurückgehalten, Dateien angehängt) als fester Satz; der Satz zu zurückgehaltenen Themen
  nennt keine Ursache („… so that no private details could reach a search engine.“), weil ein Thema auch wegen eines Links, von
  Kontodaten oder seiner Sprache zurückgehalten wird und nur die Zahl gespeichert ist; die Faktenzeile nennt „the pages
  looked up“ nur, wenn sie im Material waren; Herkunft im Widerspruchssatz „the pages looked up“, nie „your file“, und die
  Überschrift der Materialprobleme lautet dann ohne „in what you gave“. Web und MCP (`get_order_result.lookedUp`, als Inhalt
  fremder Seiten gekennzeichnet) sagen dasselbe; der Fehlersatz erscheint nur einmal. Der Ergebnistext (`MarkdownView`) nutzt
  die eine Markdown-Konfiguration des Ateliers (`SafeMarkdown`, aus `main`): Erweiterungen einzeln gewählt, ohne „Generic
  Attributes“, `{…}` wird also nie ein Attribut (kein `on*`, `style`, `ping`) und ein Platzhalter wie `{Name}` bleibt sichtbar;
  eingebettetes HTML aus; Links nur für `http`, `https` und `mailto`; keine Bilder geladen (nur ihr Alternativtext bleibt).
- **Stoppt nie den Lauf:** Jeder Fehler endet als Status mit der Warnung `grounding.research_failed`; der Text entsteht ohne
  `W1`. Der Grounding-Schritt wartet höchstens 120 s, auch wenn der Dienst nicht von selbst abbricht. Eine Logzeile je Recherche nennt nur Zahlen, Status, Dauer und Modell,
  nie Themen, Ausschnitte oder Adressen. Rohtexte werden nicht gespeichert; nur zur Abnahme schreibt `Research:AuditDirectory`
  (Standard leer) für Nutzer mit Override Rohtext und Einträge (Rechte 0600), danach wird das Verzeichnis gelöscht.
- **Migration Step51** (nullable, additiv, idempotent): `Orders.ResearchJson`, `Orders.ResearchAt`. Kein Index, keine
  Datenmigration, keine `Sources`-Zeile für das Dossier.

**Abweichungen vom Gesamtplan:** Dossier-Eintrag ist der wörtliche Seitenausschnitt, keine Modell-„Aussage“ (nicht prüfbar);
kein Zweitweg über die Websuche der CLI (liefert keinen Rohtext); kein eigener Abruf von Adressen; Recherche nur in Version 1;
`On` für alle erst nach dem Paarvergleich R3.

**Schalter:** `Research:Mode` (`Off|On`, ausgeliefert `Off`), `Research:ModeOverrides` (Nutzer → Modus, nur über die Umgebung),
`Research:DailyOrderCap` (50), `Research:DailyOrderCapPerUser` (20), `Research:AuditDirectory` (leer). Grenzen: `MaxSearches` 4, `ResultsPerSearch` 3,
`RawCharsPerPage` 6.000, `SearchTimeout` 20 s, `CondenseTimeout` 60 s, `MaxEntries` 12. Wer Prompt oder Schema der Verdichtung
ändert, misst VR neu (`tools/ResearchEval`); der Prüfer-Prompt bleibt unverändert (Test).

**Vorversuche (2026-11-25, synthetischer Korpus, nachmittags zwischen 15:06 und 15:09 UTC, 0 Codex-Aufrufe):**
- **VT (Tavily-Probe): bestanden.** 3 von 3 Anfragen mit 2xx, 6 von 9 Treffern mit Rohtext, Median 1,5 s. Die Antwort enthält
  keine Verbrauchsangabe (nur Anfrage, Antwortzeit, Anfragekennung): **Kreditverbrauch von Tavily unbekannt**; laut
  Konfiguration 0,001 USD je `basic`-Suche.
- **VR (Dossier, zurückgehaltene Menge von 9 Aufträgen): bestanden.**
  - K1 wörtlich: **0 Verstöße** über alle 95 gespeicherten Einträge (Entwicklung und zurückgehalten), unabhängige
    Python-Nachprüfung (`research_verify.py`, Groß/Klein unterschieden) und eigene Nachrechnung.
  - Gültigkeit 8 von 8 Verdichtungsantworten (verlangt ≥ 95 %); Ausbeute 7 von 9 Aufträgen mit mindestens einem Eintrag (77,8 %,
    verlangt ≥ 70 %); Relevanz 34 von 35 Einträgen helfen beim Thema (97,1 %, verlangt ≥ 80 %, blinder Opus-Juror); Median
    5,0 s je Auftrag für Suche und Verdichtung (verlangt ≤ 60 s).
  - Berichtet: 17 Themen gesendet, 1 zurückgehalten, 17 Suchen ohne Fehlschlag, 42 Seiten mit Rohtext, 41 Vorschläge, 35
    Einträge; verworfen 1 über der Grenze, 5 unsicher (12,2 %). Entwicklungsmenge (ohne Urteil): 12 von 12 mit Einträgen, 28
    Suchen, 60 Einträge.
  - Grenzen: 9 Aufträge sind ein Plausibilitätstor, K1 ist hart. Der Korpus hat kaum Dateien (1 von 36 Aufträgen mit
    Themen, nicht in der zurückgehaltenen Menge); die Herkunftsregel wirkte dort nur einmal, ihre Wirkung belegen Tests
    (Eigenschaftstest, Ende-zu-Ende-Gegenproben). Die Sprachregel und die Regel „mit Datei keine Suche“ sind nicht Teil des
    Messwerkzeugs; beide können nur weniger senden.
  - **Folge der Prüfrunde 1 für VR (nur gelesen, keine neue Messung):** 92 der 120 gespeicherten Seiten waren auf 6.000 Zeichen
    gekürzt; der neue Schnitt nähme ihnen 1 bis 470 Zeichen am Ende. **Keiner der 95 Einträge liegt in diesem Rest**, keine
    VR-Adresse hat Benutzerangaben oder über 500 Zeichen. `ResearchEval evaluate` mit dem neuen Code auf denselben Dateien
    (Herkunftsregel und `DossierBuilder` werden nachgerechnet, die neue Nachprüfung mit Wortgrenzen ergibt 0 Verstöße):
    **weiter bestanden**, alle Zahlen gleich. Die Verdichtung sah in VR das etwas längere Seitenende; ihre Eingabe ist also nicht
    bitgleich mit dem Betrieb, das Ergebnis bleibt aussagekräftig, weil keine Fundstelle aus dem weggeschnittenen Teil stammt.
  - **Folge der Prüfrunde 2 für VR (nur gelesen, offline auf den gespeicherten Dateien, kein neuer Aufruf):** 101 der 120
    gespeicherten Seiten sind 6.000 UTF-16-Einheiten lang, also gekürzt (die 92 oben zählten Codepunkte). Der Schnitt mit
    Halbe-Länge-Regel, ohne Abkürzungen als Satzende und mit Komma-Rückfall nimmt ihnen 0 bis 470 Zeichen am Ende (Median 45 in
    der Entwicklungsmenge, 57 in der zurückgehaltenen; das unbekannte Zeichen nach Stelle 6.000 einmal als Buchstabe, einmal als
    Leerzeichen angenommen); **keine Seite wird leer oder behält weniger als die Hälfte, keiner der 95 Einträge liegt im
    weggeschnittenen Teil.** `research_verify.py` mit der Tausendertrenner-Regel: 0 Verstöße über 95 Einträge. `ResearchEval
    evaluate` mit dem neuen Code (gespeicherter und neu erzeugter Prüfbericht): **weiter bestanden**, alle Zahlen gleich.

**Tore nach dem Deploy (vorab festgelegt, keines besteht ohne Daten):**
- **R2 (Abnahme-Messung):** 5 Aufträge des Messnutzers **ohne Dateien** (3 Web, 2 MCP), Faktenprüfer `Report`: (a) jeder mit
  Recherche-Status ≠ `NotNeeded` und ≠ `FilesAttached`, (b) 0 Fragen zu recherchierten Themen, (c) höchstens 1
  `unsupported`-Befund gegen eine von `W1` gedeckte Stelle, (d) Median ≤ 60 s, (e) 0 Notizmarker im Text. (a), (c) und (e)
  prüft `tools/Eval/geef_eval/research_r2.py` auf den gespeicherten `get_order_result`-Antworten; (b) und (d) sind
  **Handauswertung**. **(c), vorab festgelegt (Prüfrunde 2):** Eine gemeldete Stelle zählt im Code, wenn ihr Zitat eine Zahl
  enthält (Datumsangaben über ihre Ziffern; `1 500`/`4'850` zusammengesetzt und in Teilen), die in Zitat oder Titel eines
  Eintrags desselben Auftrags steht; in einem englischen Text auch ein großgeschriebener Name, der nicht am Satzanfang steht. Im
  Deutschen ist jedes Substantiv großgeschrieben; eine Stelle, die nur ein großgeschriebenes Wort mit einem Eintrag teilt, ist
  deshalb ein **Fall für eine Person**: Sie steht im Bericht und wird in einer Bestätigungsdatei entschieden (`--confirmations`,
  je Fall `covered` oder `not_covered`, Zitat wörtlich wiederholt); `covered` zählt. Solange ein Fall offen ist oder die Datei
  einen Fehler hat, ist R2 `not_evaluable`. Ein Text gilt als deutsch, außer er enthält mehr englische als deutsche
  Funktionswörter. **Nicht auswertbar** unter 4 Aufträgen mit Text und unter **4 von 5 Aufträgen mit Status `found` und
  mindestens einem Eintrag**. Nicht bestanden bei (c) → Recherche bleibt für alle `Off`.
- **R3 (Wirkung, `tools/Eval/geef_eval/research_pairs.py`):** 8 unberührte Entwicklungsaufträge ohne Dateien, zwei Messnutzer
  (mit/ohne Recherche, beide ohne Faktenprüfung), blinder Sonnet-Juror, je Auftrag zwei Aufrufarten in je zwei vertauschten
  Durchgängen: `rank` **ohne** nachgeschlagene Ausschnitte entscheidet Siege und Platzhalter; `facts` mit den nummerierten
  Ausschnitten des Arms on listet erfundene Tatsachen und nennt je Tatsache den Ausschnitt, der sie wörtlich deckt. Tor: (a)
  Siege mit ≥ Siege ohne + 2, (b) Summe `erfunden` mit ≤ ohne, (c) Platzhalter für recherchierbare Fakten mit < ohne (je
  Auftrag Mittel der zwei Durchgänge). **Gültigkeit (vorab festgelegt):** mindestens 7 von 8 Paaren technisch vollständig und
  mindestens **6 von 8 gültig** – nur Paare, deren Arm on `found` mit mindestens einem Eintrag hat; `failed`, `daily_limit`,
  `all_withheld`, `nothing_found`, `files_attached` oder keine Recherche machen ein Paar ungültig; sonst `not_evaluable`. Der
  Status je Paar steht in der Ausgabe. **Erfundenes wird zweimal gezählt:** streng (jede Tatsache) und mit Wortlaut-Ausnahme
  (ohne die von einem Ausschnitt wörtlich gedeckten). Jede Tatsache, die nur durch die Ausnahme nicht zählt, wird gelistet und
  vor einem `On` von Hand gegen die Absicht geprüft; solange ein solcher Fall ohne gültige Entscheidung in der
  Bestätigungsdatei (`--confirmations`, je Fall `not_invented` oder `invented`) ist, zählt streng und das Tor ist
  `not_evaluable`. Kappe 32 Juror-Urteile (16 `rank` + 16 `facts`). Nach einem Konfigurationshalt reicht `run` nichts mehr ein.
  Bestanden → `On` für alle; sonst bleibt `Off`, der Messnutzer behält `On`.

**Gegenproben bei der Integration:** Über die echte Verdrahtung erreicht weder ein Profilname noch eine Antwort der Person
Tavily oder die Verdichtung; mit angehängter Datei geht in keinem Lauf eine Anfrage hinaus (auch nicht ein übersetztes
Dateiwort oder der Dateiname); Adresse und Titel einer Seite bleiben im Prompt Inhalt. Damit entfallen die Datei-Teile der
Sicherheitsbefunde M1/M2 der Prüfrunde 1 und der Dateiname-Fund der Integration. Verbleibend, bewusst: Wörter aus dem Wortlaut
der Person dürfen in ein Suchthema (sie hat sie selbst geschrieben); es gilt das Profil des Briefings; die Adresse einer Seite
(ohne Query und Fragment) steht im Schreiber-Prompt und könnte im Text als Quelle erscheinen.

**Kosten:** je recherchiertem Auftrag ein Sonnet-Aufruf und höchstens vier Tavily-Suchen (im Korpus hatten 22,5 % der Aufträge
Suchthemen); Tagesgrenze ≤ 200 Suchen, je Konto ≤ 80.

**Rückweg:** `Research__Mode=Off` ohne neues Image; gespeicherte Recherchen bleiben und erscheinen nur bei Läufen, die sie
nutzten. Code: vorheriges Web-Image; Step51 ist nullable, der Code von Etappe 3 ignoriert die Spalten; kein `Down()` in
Produktion. Die Vorlage `direct` ändert sich nicht; laufende Läufe dürfen bleiben.

**Messläufe R2 und R3 (2026-11-25, nach der Auslieferung):** **R2 bestanden:** fünf neue Aufträge, alle mit Fundstellen und
ohne Rückfrage; (a) und (e) im Code bestanden, (c) höchstens eine als ungedeckt gemeldete, aber nachgeschlagene Stelle (die
Handfälle entschied eine unabhängige Prüfung, alle „nicht gedeckt“), (d) Median der Recherche rund 5 s. **R3 nicht
auswertbar:** Von acht Paaren hatten nur vier eine Recherche mit Fundstellen (bei dreien erkannte der Klärer diesmal kein
Suchthema, bei einem fand die Suche nichts; verlangt sind sechs). Hinweis ohne Beweiskraft aus den vier Paaren: Siege mit
Recherche 3:1, fehlende Angaben 1 gegen 5, erfundene Angaben streng gezählt 30 gegen 12,5, ohne wörtlich Belegtes 12,5 gegen
11,5 (die 37 Fälle dafür sind nicht von Hand entschieden). **Folge:** `Research:Mode` bleibt `Off`. Ein neuer R3-Lauf braucht
eine neue, größere Auftragsmenge, bei der der Klärer verlässlich Suchthemen liefert.

**Beobachtungen aus der Abnahme:** Der Faktenprüfer meldet auch Sätze mit Platzhaltern und höfliche Schlusssätze als
ungedeckt; der Klärer bot einmal geratene Daten als Antworten an, obwohl sein Prompt das verbietet; Zitate zeigen
Formatzeichen der Quellseite.

### Zusatz zu D-074 — Messtor R3′ bestanden, Recherche für alle eingeschaltet (2026-11-27)

**Messung R3′ (Restplan, Paket 2):** neue, frei erfundene Aufträge ohne Dateien, geschrieben ohne Kenntnis des Tors; erst der
Arm mit Recherche, dann für dieselben Aufträge der Arm ohne, beide ohne Faktenprüfung. Kriterien, Schwelle T (Rauschen des
Jurors aus den gespeicherten R3-Daten) und Auswertung standen vor dem ersten Aufruf fest; „gedeckt“ entschied für Angaben mit
Ziffern der Code, für den Rest ein zweiter blinder Leser mit Kontrollfällen, keine Handentscheidung. **Ergebnis: bestanden,
alle drei Kriterien.** Texte mit Recherche gewannen deutlich öfter (a), ließen weniger nachschlagbare Lücken offen (c) und
enthielten je Text nicht mehr, sondern weniger Angaben ohne Beleg (b′) – auch ohne die Toleranz T.

**Folge:** `Research:Mode = On` in `appsettings.json` (Code-Standard bleibt `Off`), eigener Commit; Tagesgrenzen unverändert
(`Research:DailyOrderCap`, `Research:DailyOrderCapPerUser`). Ist eine Grenze erreicht, wird der Text ohne Nachschlagen
geschrieben, und das Ergebnis sagt es; der Auftrag scheitert nicht.

**Vor dem Einschalten am Code nachgeprüft (für jedes Konto):** keine Suche mit angehängten Dateien; keine bei „Extra careful“
(D-079; jetzt zusätzlich im Recherchedienst selbst gesperrt, nicht nur durch die Wahl des Ablaufs); an den Suchdienst gehen nur
die kurzen Suchthemen des Klärers (höchstens fünf, je höchstens 120 Zeichen, höchstens vier Suchen), nie Antworten, Dateiinhalte
oder bei einem Fehlschlag der Auftragstext, ein Wort aus About me nur, wenn es auch im Auftrag steht; gesucht wird nur, wenn die Rückfragen-Stufe lief
(ohne geklärten Datensatz keine Themen); eine Änderung („Change something“) sucht nicht neu, sie nutzt die gespeicherte
Recherche der ersten Version. **Gefundene Lücke, behoben:** Die Suchthemen wurden nur gegen die About-me-Angaben zum Start des
Laufs geprüft. Wer zwischen Rückfrage und Start eine Angabe änderte oder löschte, deren Wort der Klärer in ein Thema übernommen
hatte, hätte dieses Wort gesendet. Jetzt prüft schon das Lesen gegen die Angaben, die der Klärer sah; zurückgehaltene Themen
zählen weiter als zurückgehalten. Damit gilt der Satz „es gilt das Profil des Briefings“ oben nur noch zusätzlich.

**Nachbesserung nach der Datenschutz-Prüfung (vor dem Einschalten):** Eine unabhängige Prüfung zeigte, dass Namen, Anschriften,
Telefon- und Kontonummern, Geburtsdaten und Gesundheitsangaben aus dem Auftrag ungefiltert in ein Suchthema gelangen konnten und ein
Ergebnissatz das Gegenteil behauptete („… so that no private details could reach a search engine.“). Folgen: (1) Ein Code-Filter
ohne Modell versucht, Suchthemen mit Mailadresse, Telefonnummer (auch mit Klammern, Punkten oder Gedankenstrich), IBAN, langer oder
als Konto-, Kunden- oder Vertragsnummer bezeichneter Ziffernfolge, Geburtsdatum (nur mit einem Wort für Geburt), Straße mit
Hausnummer (auch „Hauptstr 7“, „Nr. 7“, „Am Markt 3“) oder Postleitzahl mit Ort zurückzuhalten, auch wenn der Auftrag sie nennt; er
erkennt nicht jede Schreibweise; Jahreszahlen, Zeiträume, Paragraphen, Beträge und Normnummern gehen
weiter durch, ebenso Rangplätze, Dauern („Kitaplatz 3 Jahre“), Listen von Jahren und historische Daten. Klärer-Prompt und
-Schema unverändert. (2) Die Texte sagen jetzt, dass die Suchwörter aus dem Auftrag stammen und
Namen oder andere persönliche Angaben darin landen können; die Sätze zu zurückgehaltenen Wörtern nennen Ursachen nur als Beispiele.
(3) Das Ergebnis zeigt der Person, der der Auftrag gehört, welche Suchwörter gesendet wurden (Web und MCP `lookedUp.searchWords`),
auch ohne Text und wenn die gezeigte Fassung die Stellen nicht nutzte, nie anderen, auch nicht der Administration. Alle Seiten
nutzen denselben Satz: Atelier versucht wegzulassen, was es erkennt, erkennt nicht jede Schreibweise, Namen und andere Angaben aus
dem Auftrag können in den Suchwörtern landen, Antworten und Dateien nie, About-me-Wörter nur, wenn sie auch im Auftrag stehen (eine
übersetzte oder umformulierte Angabe kann durchrutschen). (4) Gesucht wird nur, wenn beim Lesen des Auftrags die Recherche für die Person an war
und das im Datensatz steht (`IntakeRecord.ResearchAllowed`): Aufträge, die vor dem Einschalten gelesen wurden und noch warten,
suchen nie, auch nicht beim Fortsetzen. (5) Fremde Seiten: Stellen mit Steuer- oder Richtungszeichen werden verworfen, Seitentitel
gekürzt, Antworten des Suchdienstes über 4 MiB nicht gelesen. **Grenzen, bewusst:** Namen, Gesundheitsangaben und andere Angaben in
Worten erkennt der Filter nicht; eine Mailadresse mit Leerzeichen statt Zeichen („name beispiel example org“) ebenfalls nicht.

**Rückweg:** `Research__Mode=Off` (Umgebungsvariable, Neustart von `web`, kein neues Image). Ausnahmen je Konto bleiben über
`Research__ModeOverrides__<konto>` möglich. Ohne Suchschlüssel geht nichts an einen Suchdienst; wer Atelier selbst betreibt und
keinen Schlüssel setzt, schaltet `Research:Mode` aus, sonst meldet jedes recherchierbare Ergebnis, dass das Nachschlagen nicht
geklappt hat.

## D-075 — Etappe 5, Teil A: Befunde aus echten Läufen (2026-11-25)

**Status:** Von vier vorgesehenen Änderungen wird **eine** ausgeliefert: (c). (a), (b) und (d) wurden gebaut, gemessen und
geprüft und sind wieder entfernt, nicht nur abgeschaltet. Keine Migration, kein Schalter, keine zusätzlichen Modellaufrufe.
Prüfer-, Klärer- und Verdichter-Prompt samt Schema und der System-Prompt des Schreibers sind unverändert. Teil B
(Nachbessern, MCP-Vertrag) folgt als Zusatz zu diesem Eintrag.

**Leitsatz.** Was nicht nachweislich nützt, kommt wieder heraus. Fehlerrichtung: Eine Meldung, Rückfrage oder Annahme still
zu verlieren ist schlimmer als eine überflüssige Meldung oder eine Doppelung.

- **(c) Zitate unter „Looked up“ – ausgeliefert.** Die Ergebnisseite entfernt aus einem Zitat nur zwei Zeichenfolgen, die in
  echten Zitaten als Formatierung vorkamen: Fett-Paare `**` um Text (nicht, wenn außen Buchstabe, Ziffer, Schrägstrich oder
  Stern anliegt oder innen eine Klammer falsch herum steht) und den Backslash zwischen einer Ziffer und einem Punkt, auf den
  ein Leerzeichen oder das Ende folgt (gedacht für Listennummern wie `1\. `). Alles andere bleibt wörtlich, weil dort ein
  Zeichen Bedeutung tragen kann. Die Erkennung ist eine Näherung; in seltenen Fällen kann sie Zeichen entfernen, die gemeint
  waren. Kein Markdown-Parser (ein erster Entwurf
  mit Parser konnte bei tief verschachteltem fremdem Webtext eine Ausnahme werfen und den Sinn verändern). Nur die Anzeige:
  gespeicherter Wortlaut, Material von Schreiber und Prüfer und die MCP-Antwort bleiben unverändert.
- **(a) Hinweis statt „nicht gedeckt“ – entfernt.** Zwei strukturelle Regeln sollten Befunde an Sätzen mit Lücke und an kurzen
  Schlusssätzen zu nicht blockierenden Hinweisen machen. Im Nachspiel gespeicherter Prüferantworten war kein gepflanzter
  Fehler betroffen; über gespeicherte Antworten zu echten Aufträgen stuften die Regeln aber 5 Stellen um, von denen ein blinder
  Prüfer 3 als echte Aussagen bewertete (erlaubt: 0). Die Lückenregel wird in dieser Form nicht erneut vorgelegt. Eine
  Schlusssatzregel bräuchte: Absatz ohne Platzhalter, einen ehrlichen Satz (die Zeile stammt weder aus Auftrag noch
  Dateien), frische blind bestätigte Daten mit 0 Aussagen und eine eigene Entscheidung für den Korrekturmodus.
- **(b) Geratene Daten im Gate – entfernt.** Das Gate sollte Antwortoptionen mit erfundenem Datum oder Wochentag streichen.
  Eine erste Fassung verglich die Daten mit dem Material und verlor dabei nützliche Rückfragen (Bereiche, Aufzählungen,
  übliche Schreibweisen). Die vereinfachte Fassung griff nur noch, wenn nirgends etwas Datumsartiges stand, und war damit in
  den meisten realen Fällen wirkungslos; zudem scheiterte ihr Musteraufbau für zahlreiche Auftragssprachen mit einer
  Ausnahme. Belegt war ein einziger Anlassfall aus einem eigenen Messauftrag. Das Verbot geratener Daten bleibt Sache des
  Klärer-Prompts; ob er es einhält, zeigt die Sichtung echter Rückfragen (D-072).
- **(d) Fortgesetzte Annahmen einmal zeigen – entfernt.** Über alle gespeicherten Ergebnisse hätte die Regel 0 Annahmen
  zusammengelegt, brauchte aber mehrere Korrekturen an Randfällen, in denen eine Angabe verschwunden wäre.

**Lehre.** Bei allen drei entfernten Regeln lag der belegte Nutzen bei null oder bei einem Einzelfall, die Fehlerrichtung aber
beim stillen Verlieren. Vor dem Bau gehört deshalb die Frage, wie oft der Fall in gespeicherten Daten vorkommt.

**Reservierte Felder.** Einige Felder für Teil B (frühere Versionen eines Ergebnisses) sind schon angelegt; in diesem Stand
setzt sie niemand, sie werden nicht geschrieben und nicht ausgegeben.

**Rückweg:** Deploy A ist ein reines Web-Image ohne Migration; Rückweg ist das vorherige Image.

### Zusatz zu D-075 — Etappe 5, Teil B: Nachbessern und vollständiger MCP-Vertrag (2026-11-25)

**Status:** Teil B integriert, nicht ausgeliefert. Ausgeliefert wird mit `Orders:Revise = Off` (Override `On` nur für den
Messnutzer, nur per Umgebung); Tore N und M **stehen noch aus**. Erst wenn beide bestanden sind, folgt ein eigener Commit
`Orders:Revise = On`. Prüfer-, Klärer- und Verdichter-Prompt samt Schema und der System-Prompt des Schreibers bleiben bitgleich
(Hash-Tests grün).

**Leitsatz.** Eine neue Version ändert nur, was die Person verlangt, und nichts Bekanntes geht dabei verloren – weder der
bisherige Text noch ein offener Punkt, der weiter zutrifft.

**Gebaut**
- **Nachbessern** (`IOrderService.ReviseAsync`, Web „Change something“, MCP `revise_order`): Anweisung als Freitext (1–2.000
  Zeichen) und/oder Schnellknöpfe (Shorter, Longer, More formal, Friendlier, Simpler words). Bedingungen: Auftrag der Person
  selbst (nur die Besitzerin oder der Besitzer, auch über MCP; Administratoren und der statische Token ändern keine fremden
  Aufträge und bekommen dieselbe Antwort wie bei einem unbekannten Auftrag), nicht gestoppt, kein Lauf aktiv oder gerade endend, eine gezeigte Version mit Text, höchstens 10 Versionen, Schalter `On` für den
  Besitzer. Ein zweiter Aufruf während eine Version läuft, ändert nichts und antwortet „A change is already being made.“
  (keine Ausnahme). Keine Klärung, keine neue Recherche (nur Gespeichertes). Der neue Lauf entsteht in einer Transaktion
  (Auftrag gesperrt, Bedingungen erneut geprüft, max+1; eindeutiger Index als zweite Sicherung, beide einzeln per Mutation
  belegt) immer im Direktweg, auch für „Extra careful“-Aufträge, gesät mit dem **ausgelieferten** Text der gezeigten Version.
  Die Änderung steht im Brief als eigener Block vor dem Profilblock. Nur die neueste Zeile ist als „the change to make now“
  markiert; jede frühere als „earlier change: already made in the text – do not make it again, keep it“. Die Überschrift sagt,
  dass bei Widerspruch die neueste Änderung gilt und dass die Worte der Person hier den Blöcken „Assumptions …“ und „Not given by
  the person“ vorgehen. Der Faktenprüfer bekommt jede Änderung als Wort der Person mit einer kurzen Marke („newest change,
  version n“ bzw. „earlier change, version n; a newer change wins where they differ“) vor dem unveränderten Wortlaut; sein Prompt
  und Schema bleiben gleich. Die Marke ist nie ein Wort der Person: Das Material hält die Änderungen getrennt von ihrer Marke
  (`FactMaterial.Changes`), ausgeschrieben wird sie nur im Prompt des Prüfers, und an einer Stelle (Filter des Faktenprüfers)
  wird sie aus jedem Zitat des Prüfers entfernt, bevor es nachgeschlagen oder gespeichert wird – nur in einem
  Nachbesserungslauf, in dem Marken ausgeschrieben werden. Im ersten Lauf eines Auftrags gibt es keine Marke, und der Filter
  lässt jedes Zitat so, wie der Prüfer es gab, genau wie vor Etappe 5 (ein Zitat wie „Tarif Version 2) kostet …“ oder ein
  Vorgabenzitat „Entwurf Version 2)“ behält seine Wörter; Tests). Ein Zitat aus einer solchen Zeile
  zählt weiter als Antwort der Person und wird ohne Marke gespeichert; ein Zitat, das nur aus der Marke oder Teilen davon besteht,
  wird nicht als Vorgabe der Person anerkannt. In gespeicherten und gezeigten Zitaten (Web, MCP, Korrekturauftrag) steht die
  Marke deshalb nie. Ob das Modell die Reihenfolge so beachtet, ist **nicht gemessen** (Tor N).
- **Schreiber:** Nur im ersten Durchgang eines Nachbesserungslaufs ersetzt `RevisionInstruction` die bisherige Anweisung zum
  Ausgangstext (nur Nutzerprompt; System-Prompt bitgleich): nur die als „the change to make now“ markierte Zeile ausführen,
  frühere Änderungen beibehalten und nicht noch einmal ausführen, Sprache beibehalten, außer die Änderung verlangt eine andere,
  Länge ±15 %, außer die Änderung verlangt eine andere.
- **Prüfungen im Nachbesserungslauf:** Keine Regel aus dem ersten Auftrag blockiert (Länge, Sprache, Platzhalterverbot, Listen,
  Überschriften, Emojis, verbotene und verlangte Wörter), weil die Änderung sie aufheben darf. Hält die neue Fassung eine davon
  nicht ein, steht eine sichtbare Notiz da („Your first request asked for …; the new version does not keep to it: … Please check
  whether your change meant this.“). Was immer gilt (leerer Text, Vorrede, Anzeige der Platzhalter, Rechenprüfung,
  Faktenprüfung), bleibt wie es ist. „What was checked“ nennt eine eingehaltene Regel ausdrücklich als Regel des ersten Auftrags
  („Checked automatically against the rules of your first request (your change may have replaced them): …“), nie als „has the
  requested length“; ob die Änderung selbst eingehalten wurde, prüft keine Regel.
- **Vorgaben im Faktenprüfer beim Nachbessern** (zweite Korrekturrunde): Ein Befund über eine verletzte Vorgabe blockiert im
  Nachbesserungslauf nur, wenn sein Vorgabenzitat in der **neuesten** Änderung steht; steht es dort und zugleich früher, zählt
  es als neueste. Stammt es aus dem ersten Auftrag, aus Antworten der Person oder des Assistenten in der Klärung, aus den
  „About me“-Angaben oder aus einer früheren Änderung, bleibt der Befund sichtbar, blockiert nie (also auch nie im Modus `On`
  und nie im Korrekturauftrag) und trägt im Bericht `beforeLastChange`. Web zeigt ihn unter „Please also look at this:“ mit der
  Vorgabe („Asked for: …“ bzw. „Your About me details say: …“, für Nicht-Besitzer verborgen wie bisher) und dem Satz „This
  instruction applied before your latest change; your change may have replaced it.“; MCP liefert denselben Satz in
  `unbackedPassages[].note` mit `blocking = false`. Dasselbe gilt für Vorgaben-Befunde, die aus einer früheren Version
  übernommen werden: Sie blockieren nur, wenn ihre Vorgabe in der neuesten Änderung steht; ein so entschärfter Befund wird auch
  in späteren Versionen nicht wieder blockierend (auch nicht, wenn ein gleichlautender blockierender Befund mit ihm
  zusammengefasst wird). Unbelegte Stellen, ersetzte Angaben (auch aus Nachgeschlagenem) und Quellenwidersprüche bleiben, wie sie
  sind; eine Vorgabe aus Dateien oder Nachgeschlagenem wird wie bisher nicht als Vorgabe anerkannt. Im ersten Lauf eines
  Auftrags ändert sich nichts. Prüfer-Prompt und -Schema sind unverändert; ob das Modell solche Befunde meldet, ist nicht
  gemessen.
- **Nie verlieren über Versionen:** Offene Stellen der Vorversion, deren Zitat als ganze Wörter noch dasteht, und
  Quellenwidersprüche werden übernommen und tragen ihre Herkunft (`fromVersion`, Web „Found in version n.“); offene Punkte des
  Schreibers der Vorversion, die die neue nicht wiederholt, erscheinen unter „Noted for version n – your change may have settled
  it:“ (`earlierOpenPoints`). Ebenso gehen Annahmen des Schreibers der Vorversion mit, die die neue Fassung nicht wiederholt
  (als Annahme gekennzeichnet: Web „Assumed: …“, MCP `kind = assumption`, mit Ursprungsversion), und die Zahl der Notizen und
  geprüften Stellen der Vorversion, die nicht gezeigt werden konnten, als fester Satz. Hat die Basisversion keinen Bericht (z. B.
  „Extra careful“), steht dort der feste Satz „The checks of version n were not taken over, so anything they left open was not
  checked again.“; er zählt als offener Punkt. Diese festen Sätze von Atelier stehen im Web nicht unter „your change may have
  settled it“, sondern unter der eigenen, neutralen Einleitung „About version n:“; MCP liefert sie in `earlierOpenPoints` mit
  `kind = note`. Übernommen wird nur bei Nachbesserungsläufen (auch bei einem, den „Resume“ an einer Nachbesserung startet). Eigene Befunde einer Vorversion,
  deren Prüfung aus oder verborgen war, werden nicht übernommen; schon übernommene Befunde (mit Herkunft) bleiben über alle
  Versionen erhalten und sichtbar, auch wenn der Prüfmodus zwischendurch aus oder verborgen war (Befunde der verborgenen Prüfung
  selbst bleiben unsichtbar).
- **Annahmen ab Version 2:** Klärer-Annahmen und „nicht gegebene“ Fakten wurden für Version 1 notiert. Ab einer geänderten
  Version sagt die Seite das („Noted for version 1 – you had not said this then, so it was assumed. Your change may have settled
  it:“ bzw. „… you had not given these facts then …“), MCP liefert `assumptionsVersion` und einen Hinweis; nichts wird
  gelöscht. Annahmen des Schreibers der gezeigten Fassung behalten den bisherigen Satz.
- **Gezeigte Version:** neueste Version mit Text. Ergebnis, Export ohne Versionsangabe und Listen nehmen sie; der Zustand kommt
  weiter von der neuesten Version. Läuft sie noch: „A new version is being written. Version n stays here until it is ready.“;
  gescheitert oder gestoppt: „Version m could not be finished. This is version n.“ Warnungen einer neuen, nicht fertigen
  Fassung stehen in einem eigenen Block „Warnings for version m“ mit dem Satz, dass sie den gezeigten Text nicht betreffen und
  dieser unverändert ist.
- **„Ready – please check“ bei übernommenem blockierendem Punkt:** Trägt eine Nachbesserung eine blockierende Stelle der
  Vorversion offen weiter, wird sie wie bei einem eigenen blockierenden Befund als „Ready – please check“ gezeigt – überall gleich
  (Auftragsseite, „My work“, Dienst, `get_order_status`, `get_order_result`, `list_orders`). **Entschieden:** nur im Prüfmodus
  `On`; im Modus `Report` endet auch ein eigener blockierender Befund als „Ready“, also hier ebenso. Gelesen wird der
  gespeicherte Bericht; der gespeicherte Ausgang bleibt unverändert. **Basis ohne Bericht:** Hatte die Basisversion keinen
  lesbaren Bericht und wurde sie selbst als „Ready – please check“ gezeigt (z. B. „Extra careful“ nicht sauber beendet), wird
  auch die neue Version als „Ready – please check“ gezeigt, unabhängig vom Prüfmodus, weil von der Basis nichts nachgeprüft
  wurde. Die Quelle ist dieselbe wie oben (Feld `earlierNeedsCheck` im gespeicherten Bericht, gelesen beim Bestimmen des
  Zustands); eine spätere Version erbt es aus dem Bericht ihrer Basis. Ältere gespeicherte Läufe haben das Feld nicht und ändern
  ihre Anzeige nicht.
- **Web:** Karte „Change something“ unter dem Text (nur Besitzer, Auftrag fertig, kein Lauf aktiv, Schalter `On`) mit dem Satz
  „The writer is asked to make only this change. “Show changes” then shows what was changed.“ – geprüft wird nicht, dass sonst
  nichts geändert wurde. Antwortet der Dienst „A change is already being made.“, bleiben Text und Knöpfe der Karte erhalten. Nach
  einer gescheiterten oder gestoppten Änderung bleibt oben auch der Prüfhinweis der gezeigten Fassung, wenn sie offene Punkte
  hat. Abschnitt
  „Versions“ ab zwei Versionen mit „You asked: …“, Download je Version und „Show changes“ (Absatz-Diff als Klartext). Die frühere
  Fassung bleibt sichtbar, während die neue läuft und wenn sie scheitert. Die Laufseite zeigt bei einem gescheiterten oder
  gestoppten Direktweg-Lauf statt „Resume“ den Link „Change something“ – nur für den Besitzer, nur bei Schalter `On` und nur,
  wenn die Auftragsseite die Änderung anbieten kann; sonst bleibt „Resume“.
- **Fortsetzen (Altpfad):** „Resume“ sät mit dem ausgelieferten Text, wenn der Lauf einen hat, sonst wie bisher mit der letzten
  Iteration (Fehlerbehebung, kein Schalter).
- **Fortsetzen eines Nachbesserungslaufs:** War der gescheiterte oder gestoppte Lauf selbst eine Nachbesserung, startet
  „Resume“ (weiter angeboten für Administratoren und für die Besitzerin oder den Besitzer, solange der Schalter `Off` steht)
  wieder eine Nachbesserung: dieselbe Änderung an der jetzt gezeigten Version, gesät mit deren Text, mit dem Brief dieser
  Version plus Änderung als nächste Version und mit dieser Version als Grundlage des Berichts. Offene Stellen, offene Punkte,
  Annahmen und feste Sätze früherer Versionen werden deshalb übernommen, und die Regeln des ersten Auftrags blockieren nicht,
  wie bei jeder Nachbesserung. Angelegt wird unter denselben Bedingungen wie eine Nachbesserung (kein Lauf aktiv, gezeigte
  Version unverändert, Auftrag nicht gestoppt, höchstens 10 Versionen), sonst nennt „Resume“ den Grund; der Schalter
  `Orders:Revise` wird nicht gefragt, weil die Änderung schon angenommen war. Die Wahl, mit der letzten Iteration zu säen, gilt
  für einen solchen Lauf nicht. Abgelehnt wird „Resume“, ohne dass ein Lauf entsteht, wenn der Nachbesserungslauf schon einen
  eigenen Text hat (etwa einer, der mit Text als „nicht fertig“ endete: dieselbe Änderung käme ein zweites Mal auf einen Text, der
  sie schon enthält; die Laufseite zeigt dann den Satz statt des Knopfs) und wenn eine spätere Version des Auftrags schon einen Text
  hat (die ältere Änderung käme auf den neueren Text). Beide Sätze verweisen auf „Change something“ auf der Auftragsseite. „Resume“
  auf jedem anderen Lauf bleibt unverändert. Belegt durch Ende-zu-Ende-Tests über die echte Verdrahtung.
- **MCP:** neun Auftragswerkzeuge, einzeln registriert: `submit_order`, `answer_order`, `get_order_status`, `get_order_result`
  (neu `latestVersion`, `earlierOpenPoints` mit `kind` `openPoint`, `assumption` oder `note`, `unbackedPassages[].fromVersion`,
  `unbackedPassages[].note` auch für Vorgaben von vor der letzten Änderung, `assumptionsVersion`), `revise_order`
  (Beschreibung ohne Zusage, dass sonst nichts geändert wird), `export_order` (dieselbe Datei
  wie der Web-Download), `cancel_order`, `list_orders`, `profile` (nur lesend). Die bisherigen Werkzeuge kommen nur bei
  `Mcp:LegacyTools = true` dazu (ausgeliefert `true`); die fünf mit Nachfolger verweisen in ihrer Beschreibung darauf, ihre
  Antworten sind unverändert. Die längste Wartezeit in Werkzeugbeschreibung und Server-Hinweisen kommt aus
  `Orders:MaxWaitSeconds`, demselben Wert, der die Wartezeit begrenzt.

**Abweichungen und Präzisierungen gegenüber dem Vertrag:** Doppelaufruf antwortet ohne Ausnahme; der Schalter-Override richtet
sich nach dem Besitzer, nicht nach dem Aufrufer; `fromVersion` behält bei mehrfacher Übernahme die Version, in der der Befund
gefunden wurde; eigene Befunde einer ausgeschalteten oder verborgenen Prüfung der Vorversion werden nicht übernommen, schon
übernommene immer; `list_orders` nennt `version` und `wordCount` des gezeigten Texts, `state` der neuesten Fassung. Ändern darf
nur die Besitzerin oder der Besitzer (Web und MCP); exportieren und stoppen dürfen Administratoren (auch der statische Token)
über MCP wie bisher im Web. Die Längenregel war im Vertrag die einzige Notiz; jetzt sind alle Regeln des ersten Auftrags Notizen,
und „Keep the language of the text“ gilt nur, solange die Änderung keine andere Sprache verlangt (Entscheidung nach der
Prüfung).

**Grenzen der Übernahme und der entschärften Regeln (benannt, nicht gemessen):**
- Ob ein Befund gegen eine Vorgabe blockiert, hängt am Teil der Vorgabe, den der Prüfer zitiert. Zitiert er nur Wörter, die auch
  in der neuesten Änderung stehen (etwa „mention the price“ bei einem Auftrag „Do not mention the price.“ und einer Änderung
  „Please mention the price: 5 euros per family.“), gilt das Zitat als in der neuesten Änderung stehend, und der Befund
  blockiert; im Modus `On` kann die Korrektur die Änderung dann zurücknehmen. Erst mit dem vollen Zitat ist der Befund
  entschärft. Ob das Modell so zitiert, ist nicht gemessen; ein Widerspruchsfall gehört vor dem Einschalten für alle zur Messung.
- Eine offene Stelle der Vorversion wird nur übernommen, solange ihr zitierter Wortlaut unverändert im neuen Text steht. Formt
  die Änderung sie um („The fee is 30 euros.“ wird „The fee is 30 euros per person.“, fett oder „amounts to 30 euros“), fällt
  der frühere Befund weg, und nur die neue Prüfung der neuen Version kann ihn wieder melden; ob sie das tut, misst kein Tor. Vor
  dem Einschalten für alle werden in einem Ton-Fall die Stellen von Version 1 von Hand nachverfolgt.

**Bewusste Grenze:** Der Brief einer Nachbesserung ist der Brief der gezeigten Version plus Änderung. Er enthält deshalb die
„About me“-Angaben, die beim ersten Auftrag galten, auch wenn die Person sie inzwischen geändert hat; der Schreiber bekommt sie
beim Nachbessern erneut. Das ist so gewollt (der Text soll auf derselben Grundlage bleiben) und wird nicht geändert.

**Datenmodell:** Migration `Step52Revise`, additiv und idempotent: `Runs.RevisionHint text NULL` (`ADD COLUMN IF NOT EXISTS`).
Sonst nichts; der Bericht bleibt Schema 2 mit angehängten, optionalen Feldern (`earlierOpenPoints` mit `kind`, `fromVersion`,
`revision`, `beforeLastChange` an Befunden, `earlierNeedsCheck`; die beiden letzten werden nur geschrieben, wenn sie wahr sind).

**Schalter** (alle per Umgebung ohne neues Image; sie werden beim Start gelesen, ein Wechsel braucht einen Neustart des
Containers; `docker-compose.yml` zählt ihre Variablen einzeln auf und reicht diese nicht durch, also im `environment` des
Web-Dienstes in einer `docker-compose.override.yml` setzen):

| Schalter | Ausgeliefert | Wirkung „aus“ |
|---|---|---|
| `Orders:Revise` / `Orders:ReviseOverrides:<Benutzer>` | `Off`, Override nur für den Messnutzer | kein „Change something“, „Resume“ auf der Laufseite wie bisher; `revise_order` bleibt gelistet und antwortet „Changing a text is not available yet.“; gespeicherte Versionen bleiben sichtbar |
| `Orders:RevisionHintMaxChars`, `Orders:MaxVersionsPerOrder` | 2.000, 10 | – |
| `Mcp:LegacyTools` | `true` | nur die neun Auftragswerkzeuge (vorgesehen für Etappe 8) |
| `Orders:MaxWaitSeconds` | 50 | Grenze und Texte ändern sich zusammen |

**V-MCP bestanden:** Neun `get_order_status`-Aufrufe mit `waitSeconds = 50` aus Claude Code gegen die Produktion dauerten je
rund 50,0–50,2 s, ohne Client- oder Transportfehler. Die Wartezeit bleibt 50 s. **claude.ai ist nicht geprüft.**

**Tore N und M: ausstehend** (am echten Lauf nach Deploy B, Schwellen wie im Vertrag festgelegt; nach der Prüfung strenger
gefasst: N4 verlangt auch die `earlierOpenPoints` der Basis mit Ursprungsversion und die nicht wiederholten Annahmen des
Schreibers; der Stopp für N5 kommt zu acht echten Nachbesserungen hinzu und zählt nicht zu „mit Text“, N5 verlangt den Zustand
`stopped`; ein lokaler N1-Fall mit weniger als vier Absätzen ist nicht auswertbar; Tor M vergleicht den Export ohne
Versionsangabe mit dem Text von Version 2). Vor dem Einschalten für alle ist am echten Lauf außerdem zu beobachten, ob bei
einer Kette (Kürzung, danach lokale Änderung; zwei sich widersprechende Änderungen) die frühere Änderung nicht erneut ausgeführt
wird und der Faktenprüfer die neueste nicht blockiert – automatische Tests belegen, was beim Schreiber und beim Prüfer ankommt
und dass ein Vorgaben-Befund aus der Zeit vor der neuesten Änderung im Code nicht blockiert, nicht aber, was die Modelle daraus
machen. Ein Widerspruchsfall (erster Auftrag verbietet, was die Änderung verlangt) ist im Plan von Tor N nicht eigens
vorgeschrieben. Nicht am echten System
erzwingbare Fälle (Doppelaufruf, Kappe, gescheiterte neue Version durch Anbieterfehler, übernommener blockierender Punkt) sind
über Postgres- und Ende-zu-Ende-Tests belegt, darunter ein Test über den ganzen Ablauf v1 → v2 (MCP) → v3 (Web) gescheitert
bzw. gestoppt, der in allen Anzeigen denselben Zustand, dieselbe gezeigte Version und dieselben offenen Punkte prüft.

**Rückweg:** vorheriges Image (Stand Deploy A). Vorher darf kein Nachbesserungslauf `Pending|Running` sein. `Step52` ist
additiv; die Spalte bleibt, kein `Down()` in Produktion. Geprüft ohne Deploy mit dem Code von Teil A gegen eine Datenbank mit
`Step52` und gespeicherten Nachbesserungsläufen im neuen Berichtsformat: Start und Migration laufen ohne Fehler (keine offene
Migration), Auftragsseite, „My work“, Laufseiten, Ergebnis, Liste, Export und die MCP-Werkzeuge antworten ohne Absturz; der neue
Bericht wird gelesen, unbekannte Felder werden übergangen. **Was das alte Image dann anders zeigt:** Ist die neueste Version
gescheitert, gestoppt oder läuft sie, zeigt es wie vor Teil B keinen Text, und der Export ohne Versionsangabe findet keinen
(die frühere Version bleibt gespeichert und mit Versionsangabe exportierbar); Punkte der Vorversion (`earlierOpenPoints`)
und die Herkunft übernommener Stellen zeigt es nicht, eine Version mit übernommenem blockierendem Punkt oder mit
`earlierNeedsCheck` erscheint als „Ready“, und eine Stelle gegen eine Vorgabe von vor der letzten Änderung steht unter den
Hinweisen ohne den Satz dazu.
Nichts davon wird gelöscht; nach erneutem Deploy erscheint es wieder.

### Zusatz zu D-075 — Etappe 5: erste Messung (Tore M, V-MCP, N) und Festlegung von Tor N′ (2026-11-26)

**Status:** Tor M bestanden, V-MCP bestanden (siehe oben), **Tor N nicht bestanden**. `Orders:Revise` bleibt `Off` (Override
`On` nur für den Messnutzer). Die Vorbedingungen für die zweite und letzte Messung (Tor N′) sind gebaut und mit Tests belegt;
Deploy und Messung stehen aus. Prüfer-, Klärer- und Verdichter-Prompt samt Schema und der System-Prompt des Schreibers bleiben
bitgleich (Hash-Tests grün).

**Erstes Ergebnis Tor N: nicht bestanden – unverändert.** Laut Messwerkzeug verfehlt: N1 am Fall „Absatz streichen“ (Absatz-
und Wortschwelle) und N2 (zusammen elf „neue nicht belegte Stellen“ in den beiden Ton-Fällen, höchstens eine erlaubt); N3, N4
und N5 bestanden. Die alten Daten werden nicht neu bewertet, keine Schwelle wird an ihnen gelockert.

**Lesung des unabhängigen Beraters** (an den gespeicherten Texten, ohne Modellaufruf):
- **Beide Fehlschläge sind Fehler der Messanlage.** Der zu streichende Absatz war im Markdown ein Abschnitt aus vier Blöcken;
  schon die verlangte Streichung allein lässt höchstens zwei geänderte Blöcke und ±15 % Wörter nicht zu. Der Schreiber strich
  genau diesen Abschnitt und passte einen Halbsatz an, der sonst auf Gestrichenes gezeigt hätte. Die Kennzahl von N2 zählt jede
  beanstandete Stelle, deren Zitat nicht wörtlich in der Vorfassung steht; bei einer Tonänderung wird jeder Satz umformuliert,
  gezählt werden also Umformulierungen von Sätzen, die der Faktenprüfer schon vorher beanstandet hatte. Keine der elf Stellen ist
  in der Sache eine neue Aussage. Oberhalb von acht gelisteten Stellen ist die Kennzahl zudem blind. Leichter Produktbefund dabei:
  Ein wärmerer Ton verschob in einem Fall die Anrede (aus „Mieter“ wurden „Nachbarn“, obwohl die Verwaltung schreibt).
- **N4, Teilkriterium Platzhalter: Schein-Bestehen, in der Sache verfehlt.** Ein Platzhalter mit 94 Zeichen stand in drei
  Fassungen im Text und in keiner Liste „Fill these in before you send the text“. Produkt und Messwerkzeug erkannten Platzhalter
  nur bis 80 Zeichen, deshalb fiel es nicht auf. Der Fehler ist älter als das Nachbessern.
- **N3 „Länger“: ungesichert.** „Eine neue Stelle“ ist nur eine Untergrenze, weil zwei weitere beanstandete Stellen nicht in der
  Liste standen; im Text stehen zwei neue weiche Aussagen, darunter eine neue Zusage für den nächsten Anlass.
- **Basisaufnahmen als Kopien:** Die Ausgangsfassungen der Folgeänderungen wurden nachträglich als Kopien angelegt, um die Regel
  „jeder Schnappschuss in höchstens einem Fall“ zu erfüllen. In der Sache unschädlich, formal eine Umgehung der eigenen Regel; bei
  Tor N′ wird jede Ausgangsfassung unmittelbar vor ihrer Änderung aufgenommen.
- „Simpler words“ wurde nicht gemessen; die geöffnete Änderungsansicht ist in den Daten nicht belegt.

**Begründung des Messwechsels:** Die Kennzahl „neue `not_backed`“ zählt Umformulierungen und endet bei acht gelisteten Stellen;
Absatz- und Wortschwelle sind bei „Absatz streichen“ für einen fehlerfreien Schreiber unerreichbar. Ein neuer Anlauf nur am Satz
mit der alten Kennzahl wäre unabhängig vom Satz wieder verfehlt worden; den Laien das Ändern eines Textes wegen zweier Messfehler
vorzuenthalten, wäre nicht ehrlicher. Deshalb wird die eine Runde am Satz genutzt und genau einmal mit einer vorab festgelegten,
berichtigten Messanlage neu gemessen.

**Tor N′ – zweite und letzte Messung.** Die Festlegung gilt vor jedem neuen Datum und wird nach Beginn der Messung nicht mehr
geändert:
- **Daten:** drei frische Aufträge des Messnutzers (eine Einladung zu einer Vereinsversammlung und ein Bericht für eine
  Schulzeitung auf Deutsch, eine Rundmail zu einem Umzug auf Englisch), sieben Änderungen, fünf im Web mit echten Klicks und zwei
  über MCP: eine Angabe ersetzen, einen Satz ergänzen, einen Absatz streichen, zweimal Ton, Länger, Kürzer. Jede Art, die in der
  ersten Messung verfehlt wurde, ist wieder dabei. Vor jeder Änderung wird die Ausgangsfassung frisch aufgenommen.
- **Mechanisch für jeden Fall (N4′):** neue Fassung mit Text und der nächsten Versionsnummer; nichts Bekanntes der Vorfassung geht
  verloren; jeder Platzhalter des neuen Texts (eigene, vom Produkt unabhängige Regel bis 300 Zeichen) steht in der Liste; der
  Export ohne Versionsangabe enthält die neue Fassung.
- **Mechanisch je Art:** Schwellen auf Blöcke, Wortverhältnis (±15 %, Kürzer 40–85 %, Länger 115–160 %), Platzhalter und
  Ziffernfolgen; beim Streichen bilden die entfernten Blöcke einen zusammenhängenden Zielbereich (höchstens eine Anschlusskorrektur
  woanders), und das Wortverhältnis wird ohne diesen Bereich gerechnet. Oberfläche: Die geöffnete Änderungsansicht zeigt das alte
  Datum als entfernt und das neue als hinzugefügt (höchstens zwei geänderte Absätze); die Laufzeit beim Schreiben steht unter 1:00.
- **Inhaltlich: ein blinder Prüfer** (frischer Unteragent, nur das Lesen einer Eingabedatei, ohne Wissen über Produkt, Tor,
  Schwellen und Faktenprüfer) beurteilt alle sieben Paare aus Wunsch, Text vorher und Text nachher, dazu zwei von Hand gebaute
  Kontrollpaare (eine gepflanzte Aussage, eine saubere Einzeländerung). Gültig ist sein Urteil nur, wenn beide Kontrollpaare das
  Erwartete zeigen; Zitate werden gegen die Texte geprüft. Schwellen: Wunsch in allen Fällen erfüllt; keine wesentliche neue Aussage
  bei den lokalen Änderungen und bei Kürzer, bei den beiden Ton-Fällen zusammen und bei Länger je höchstens eine; keine weggefallene
  tragende Aussage, keine veränderte Angabe; außerhalb einer lokalen Stelle höchstens eine Änderung, und nur eine nötige.
- Die Zahlen des Faktenprüfers werden je Fassung berichtet, sind aber **kein Kriterium** mehr.
- Obergrenzen der Modellaufrufe (Klärer 4, Schreiber 12, Faktenprüfer und blinder Prüfer zusammen 12); nach einem technischen
  Fehlschlag darf dieselbe Änderung einmal neu gestartet werden.
- **Folgen:** bestanden → zusammen mit Tor M und nach V4/V5 eigener Commit `Orders:Revise = On`. Mechanisch N4′ verfehlt → Halt:
  Fehler beheben, nie für alle einschalten, bevor er am betroffenen Fall erneut nachgewiesen ist. Sonst verfehlt → `Orders:Revise`
  bleibt beim Messnutzer, keine weitere Runde am Satz und keine weitere Messung in Etappe 5; ein neuer Anlauf ist eine eigene Etappe.
  Nicht auswertbar → nur die fehlenden Fälle dürfen einmal unter derselben Festlegung nachgeholt werden. Kein Fall wird ausgetauscht,
  kein Urteil überstimmt, keine Schwelle angepasst.
- **Nicht gemessen:** der Knopf „Simpler words“ (kein Budget). Der Stopp (N5) wird aus der ersten Messung fortgeschrieben, weil
  keine Vorbedingung die Bestimmung des Zustands, der gezeigten Version oder den Stopp-Pfad ändert.

**Neuer Satz `RevisionInstruction`** (die eine Runde des Vertrags), Wortlaut:

> Change the text in draft.md only as the person asks in the line marked "the change to make now" under "Change requested by the
> person". The earlier changes listed there are already made in the text: keep them and do not make them again. Keep everything
> else as it is: the same facts, structure and wording. If the change leaves another sentence wrong or pointing at something that
> is no longer in the text, adjust that sentence as little as possible. A change of tone, style or length changes the wording, not
> the content: add no statement about what happened, what will happen, what is offered or what is expected that the text does not
> already make, and keep who writes and who is addressed. When the text has to get longer, expand on what it already says and do
> not invent detail to fill it. Keep the language of the text unless the change asks for another language. Keep the length within
> ±15 % unless the change asks for another length or cannot be made without changing it.

Für Anbieter ohne Dokumentmodus wie bisher mit „this text“ statt „the text in draft.md“. Die festen Sätze der Schnellknöpfe und
die Anweisung für einen gesäten ersten Durchgang bleiben unverändert (Tests).

**Vorbedingungen (Code mit Tests, ohne Modellaufruf):**
- **V1 Platzhalter:** erkannt bis 300 Zeichen auf einer Zeile im Produkt (Prüfung und Liste „Fill these in …“) und im
  Messwerkzeug. Der Filter des Faktenprüfers, der eine gemeldete Stelle innerhalb eines Platzhalters verwirft, bleibt bewusst bei
  200 Zeichen: Eine Stelle in einer längeren Klammer bleibt stehen, weil eine überflüssige Meldung besser ist als eine Aussage, die
  still verschwindet. Links, Verweise, Bilder, Fußnoten, Ankreuzfelder und Doppelklammern bleiben
  unabhängig von der Länge ausgenommen. Gespeicherte Ergebnisse ändern sich nicht, weil die Liste beim Lauf in den Bericht
  geschrieben wird; neue Läufe listen auch lange Platzhalter, und bei „keine Platzhalter“ im Auftrag blockieren sie wie kurze.
  Lange eckige Klammern mit anderem Inhalt (etwa eine redaktionelle Einfügung in einem Zitat) gelten wie bisher kurze als
  Platzhalter; in den gesicherten Texten früherer Läufe waren alle neu erkannten langen Klammern echte Platzhalter.
- **V2 Laufzeit:** Beim Nachbessern zählt die Anzeige (Web und `elapsedSeconds`) ab Beginn der laufenden Version, nicht ab Anlage
  des Auftrags.
- **V3:** der neue Satz oben.
- **V4 (vor „On für alle“):** Notizen früherer Versionen („Noted for version n …“, auch die für Version 1 notierten Annahmen und
  nicht gegebenen Fakten) stehen eingeklappt hinter einer Zeile mit ihrer Anzahl; nichts wird gelöscht. Die festen Sätze unter
  „About version n“ (was nicht gezeigt oder nicht übernommen wurde, darunter, dass die Prüfungen einer früheren Version nicht
  übernommen wurden, was der Grund für „Ready – please check“ sein kann) stehen immer offen und zählen in dieser Zeile nicht mit.
  Mit der Tastatur bedienbar, passt auf ein Telefon.
- **V5 (vor „On für alle“):** Annahmen des Schreibers einer geänderten Version stehen unter „The writer noted while making your
  change:“ statt unter „You did not say this, so it was assumed:“; MCP sagt in `note`, dass `writerAssumptions` dann Notizen zur
  Änderung sind.
- Empfohlen und umgesetzt: Der Link über dem Text heißt „See what changed and all versions“, wenn die Liste für die gezeigte
  Version „Show changes“ anbietet (eine frühere Version hat einen Text zum Vergleich), sonst „See all versions“, etwa nach einer
  ersten Version, die ohne Text endete. Nicht umgesetzt: doppelte Stelle zusammenführen, Wunsch neben dem Fortschritt zeigen.

**Eigener Befund außerhalb von Tor N: Grundrauschen des Faktenprüfers.** In den frischen ersten Fassungen der ersten Messung
beanstandete er je harmlosem Text 3 bis 9 Stellen, darunter Grußformel und Unterschrift („may not be backed“), und bei
unverändertem Text von Lauf zu Lauf andere Sätze. Als Messinstrument für neue Aussagen ist er deshalb ungeeignet; für die Person
bleibt es ein älterer, eigener Befund (Backlog). Prüfer-Prompt und -Schema bleiben in Etappe 5 unverändert.

### Zusatz zu D-075 — Ergebnis von Tor N′ (2026-11-26)

**Ausgeliefert** wurden vorher die Vorbedingungen V1–V5 und die Auflagen der Freigabe; Nachbessern blieb dabei ausgeschaltet,
eingeschaltet nur für das Messkonto. Gemessen genau nach der vorab festgelegten Anlage: 3 frische Aufträge, 7 Änderungen
(5 in der Weboberfläche mit echten Klicks, 2 über MCP), Aufnahmen unmittelbar vor und nach jeder Änderung, ein blinder Prüfer
mit zwei Kontrollpaaren. Aufrufe innerhalb der Obergrenzen, keine technischen Fehlschläge.

**Urteil: Tor N′ verfehlt.** Alle mechanischen Kriterien bestanden bei allen sieben Änderungen (neue Fassung, nichts Offenes
verloren, Platzhalter gelistet, Export, Schwellen je Art, Änderungsansicht, Laufzeitanzeige). Die Antwort des Prüfers war gültig
(beide Kontrollpaare wie erwartet); bei sechs von sieben Änderungen bestanden alle seine Schwellen. Verfehlt ist „Länger“
(Knopf): Wunsch nur **teilweise** erfüllt, weil die Verlängerung trotz „without adding new facts“ eine Tatsache über den
Anlass hinzufügte. Die Ergebnisseite führte den betreffenden Satz unter „may not be backed“, zusammen mit seinen Nachbarsätzen;
weitere ausschmückende Sätze (vom Prüfer als unwesentlich gewertet) meldete sie nicht in jedem Fall.

**Folge wie vorab festgelegt:** `Orders:Revise` bleibt `Off` und nur für das Messkonto eingeschaltet. Keine weitere Runde am Satz
und keine weitere Messung in Etappe 5. Ein neuer Anlauf ist eine eigene Etappe mit eigenem Vertrag; sein Ansatzpunkt ist das
Verlängern (etwa: den Knopf „Longer“ nicht anbieten, oder Verlängern nur mit neuen Angaben der Person), nicht das Nachbessern
insgesamt. Nicht gemessen blieben „Simpler words“; der Stopp gilt aus der ersten Messung fort.

**Stand der Etappe 5:** ausgeliefert sind die Zitat-Anzeige (Teil A), der vollständige MCP-Vertrag (Tor M und 50-Sekunden-Aufruf
bestanden) und Nachbessern hinter ausgeschaltetem Schalter. Nicht geprüft: claude.ai als MCP-Client.

## D-076 — Etappe 6: Dateien über MCP (Upload-Ticket) und Ablehnungszählung (2026-11-26)

**Status:** integriert, nicht ausgeliefert. Ausgeliefert wird mit `Uploads:Tickets = Off`; eingeschaltet wird erst nach dem
Deploy per Umgebung für die Tore T-A2 bis T-A5, und erst wenn alle bestanden sind, folgt ein eigener Commit
`Uploads:Tickets = On`. Das Sicherheits-Review (Tor T-A1) ist bestanden: 0 kritisch, 0 hoch, 1 mittel (ob ein Modell die
Quoting-Regel befolgt, ist ungemessen; das wird in T-A2 mit einem Dateinamen mit Apostroph und Leerzeichen gemessen), 8 niedrig
(Umgang unter „Korrekturrunde nach T-A1“ und „Entscheidungen“). Kein Prompt geändert
(Klärer, Prüfer, Verdichter samt Schema und System-Prompt des Schreibers bitgleich, Hash-Tests grün). Keine neuen Pakete.

**Leitsatz.** Eine Datei, die die Person ihrem KI-Assistenten gibt, kommt Byte für Byte an – oder der Assistent bekommt eine
Meldung, die den richtigen Weg nennt. Nichts wird abgetippt, nichts still verloren, und Ablehnungen werden gezählt, wo eine Person
zuzuordnen ist; welche Fälle nicht gezählt werden, steht unter „Zählung“.

**Bedarf.** Base64 in einem Werkzeugargument muss ein Modell Zeichen für Zeichen ausgeben; das bricht schon bei einigen Dutzend
Kilobyte ab, und claude.ai reicht Dateien aus dem Chat nicht an Connectoren weiter. Die bisherige Nutzung passt dazu. Abgelehnte
Dateien hinterließen
keine Zeile, MCP-Ablehnungen (JSON-RPC-Antwort mit Status 200) waren damit unsichtbar. Entwurfsregel: Der Token steht nie in
einer Adresse oder Query, weil Adressen in Logs landen können.

**Einteilung.** Gebaut: Upload-Ticket (`prepare_attachment`, Token-Upload, Upload-Seite, `uploadIds`), Ablehnungszählung,
Aufräumen. Später, jeweils mit Auslöser aus der Zählung (≥ 3 Ablehnungen von ≥ 2 Konten außerhalb der Messkonten in 60 Tagen):
E-Mail-Dateien (`eml`/`msg`; `.eml` dann über MimeKit aus MailKit), Tabellen (`xlsx`, dann über `DocumentFormat.OpenXml`;
`ExcelDataReader` gestrichen), Scans und Bilder (OCR, `pdf_no_text` oder Bild-Endungen; braucht eine asynchrone Quittung),
`add_text_document` (Ablehnungen `mcp_placeholder`/`mcp_base64_invalid`), Größendeckel für Base64-Anhänge
(`mcp_type_mismatch`/`unreadable`). Gestrichen: Bibliothek und „immer mitgeben“ (keine Wiederverwendung im Bestand; Stilvorlagen
kommen mit dem Gedächtnis), Rollen je Datei änderbar, Volume ins Backup (Originale werden nach der Extraktion nie gelesen, der Text
liegt in der gesicherten Datenbank), Pflicht-Prüfsumme im Ticket (die Antwort nennt `sha256`). Die Namensliste der Messkonten
für die Auslöser-Abfrage steht nur in der Betriebskonfiguration, nie im Repository.

**Regeln**
- **`prepare_attachment(fileName, sizeBytes?)`** (nur bei `On`): Name getrimmt, 1–255 Zeichen, nur Anzeige und Endung, über
  `SourceFileNames` bereinigt (je Unicode-Zeichen, also auch unsichtbare Tag-Zeichen ab U+E0000 und Richtungssteuerzeichen
  entfernt; Umlaute und Emojis bleiben), nie Teil eines Pfads. `sizeBytes` über `Sources:MaxFileBytes` wird sofort abgelehnt und
  als `too_large` auf dem Weg `mcp_ticket` gezählt, sonst ist er Obergrenze und Sollwert. Kontingent je Person: höchstens 10 offene Tickets und 50 in 24 Stunden (Zählen und Anlegen unter einer
  Sperre je Person). Antwort `{uploadId, curlCommand, uploadPage, tokenExpiresAt, pageExpiresAt, maxBytes, next}`. Der Token
  (32 Zufallsbytes, base64url) wird einmal ausgegeben und nur als SHA-256 gespeichert, gilt 15 Minuten und steht nur im Kopf
  `X-Upload-Token`. Der Befehl ist
  `curl --fail-with-body -sS --globoff -T '<path to the file>' -H 'X-Upload-Token: <token>' <OAuth:Issuer>/api/uploads/<uploadId>`;
  er enthält außer Token und Adresse nichts Veränderliches, insbesondere nie den Dateinamen. `--globoff` ist nötig, weil `curl`
  bei `-T` sonst `{}` und `[]` im Pfad als Muster auswertet. Beschreibung, `next` und Server-Hinweis verlangen, den Pfad in
  einfache Anführungszeichen zu setzen; enthält er ein Anführungszeichen, einen Zeilenumbruch oder ein anderes ungewöhnliches
  Zeichen, soll der Assistent den Namen nicht in einen Befehl schreiben, sondern in **einem** Shell-Befehl die Datei mit einem
  Muster nur aus Buchstaben, Ziffern und `/ . - _ *` (ein `*` statt jedes anderen Zeichens) unter einem schlichten Namen in ein
  frisches Verzeichnis kopieren, die Kopie hochladen und das Verzeichnis entfernen:
  `d=$(mktemp -d) && cp -- <pattern> "$d/upload" && <curlCommand>; rm -rf "$d"`. In `<curlCommand>` tritt `"$d/upload"` mit den
  doppelten Anführungszeichen an die Stelle von `'<path to the file>'`; in einfachen Anführungszeichen würde `$d` nicht ersetzt,
  und in einem zweiten Befehl wäre `$d` leer. Sonst den Link verwenden. Die Upload-Seite `/u/<uploadId>` enthält kein Geheimnis, gilt
  24 Stunden und nur nach Anmeldung als dieselbe Person.
- **Token-Upload `PUT /api/uploads/{id}`:** einziger Ausweis ist der Kopf; Cookies werden nicht gelesen, kein CORS, also kein
  CSRF. Anspruch in einer Anweisung (`Open → Receiving`); unbekannt, falscher Token, abgelaufen, benutzt und fremd antworten
  einheitlich `404`; ein laufender Upload desselben Tickets `409`. Aufnahme über dieselbe Erkennung, Extraktion im Kindprozess
  und dieselben Kontingente wie die Weboberfläche, Herkunft `McpAttachment`. Größe ≠ angekündigt → `size_mismatch`, nichts
  gespeichert. Brauchbar → `201` mit Quittung, `uploadId`, `sha256` und `next`. Die Antwort liest ein KI-Assistent; sie enthält
  deshalb weder den Anfang des Textes noch den Dateinamen.
- **Versuche:** Ein Versuch zählt nur, wenn die Datei wirklich geprüft und abgelehnt wurde; dann bleibt das Ticket offen bis
  3 Versuche, danach `Failed`, und der Code der letzten Ablehnung steht in `UploadTickets.LastRefusalCode`. Zu viele gleichzeitige
  Uploads (`user_busy`), Überlast (`busy`), Abbruch (`interrupted`), Speicherfehler (`save_failed`), nicht verfügbare Extraktion
  (`unavailable`), volles Kontingent (`quota`), Zeitgrenze, abgebrochene Anfrage und Serverfehler geben den Versuch zurück.
  `next` sagt nach einer geprüften Ablehnung, dass der Assistent der Person den Grund wörtlich sagen und dieselbe Datei nicht
  erneut hochladen soll, nach einer ungeprüften, dass es kein Problem der Datei war. Bei `quota` und `unavailable` sagt `next`
  zusätzlich, dass sofortiges Wiederholen nicht hilft, und was hilft (Kontingent: erst den Auftrag mit den schon angekommenen
  Dateien senden oder später, wenn nie benutzte Dateien automatisch gelöscht sind; nicht verfügbar: später, bei Dauer ein
  Administrator), jeweils mit neuem `prepare_attachment`, falls der Link bis dahin abgelaufen ist.
- **Zu lang für diesen Weg:** Eine Datei, die zu lang ist, um ganz verwendet zu werden (die Weboberfläche würde nach Seiten
  fragen), ist auf dem Ticketweg nicht brauchbar: `422` mit eigenem Satz (kürzere Datei, oder den Auftrag in der Web-App starten
  und dort Seiten wählen), Code `too_long_for_ticket`, nichts gespeichert (eine schon vorhandene gleiche Datei bleibt). Scheitert
  das Löschen der Kopie, endet der Satz nicht mit „Nothing was stored.“, sondern sagt, dass die Datei vorerst gespeichert bleiben
  kann und so keinem Auftrag hinzugefügt wird. Die Seite fragt nie nach Seiten.
- **Upload-Seite:** angemeldete Seite mit genau einem Ablagefeld; fremde und unbekannte Tickets sehen gleich aus („Not found“).
  `POST /api/uploads/{id}/page` prüft Cookie, Antiforgery-Kopf und `Origin` wie `/api/sources`, Herkunft `WebUpload`, ohne
  Größenvergleich (die Person wählt die Datei selbst). Ein Ticket, dessen Versuche alle durch abgelehnte Dateien verbraucht sind,
  heißt dort nicht „abgelaufen“, sondern hat einen eigenen Satz.
- **`submit_order(…, uploadIds?)`:** nur angekommene Dateien der angemeldeten Person, alles oder nichts; zusammen mit
  `attachments` höchstens 10 Dateien. „no longer stored“ nur für eine Datei, die angekommen war und gelöscht wurde. Für ein Ticket
  ohne brauchbare Datei: „No usable file arrived for upload {id}.“ mit dem Grund der letzten geprüften Ablehnung, wenn es einen
  gibt (aus dem gespeicherten Code, nie aus der Datei), sonst mit dem Rat, ein neues Ticket anzulegen; ist das Ticket noch offen,
  zusätzlich der Link. Sind mehrere Dateien zusammen zu lang, nennt die Ablehnung bei `On` keinen Seitenweg, sondern die Web-App.
  Bei `Off` fehlt `uploadIds` im Schema; ein trotzdem gesendeter Wert wird abgelehnt. Bei `On` widerspricht kein Text dem
  Dateiweg: Schritt 1 der Server-Hinweise und die Beschreibung von `submit_order` sagen, dass Dateien zuerst mit
  `prepare_attachment` hochgeladen werden; `attachments` nur für sehr kleine Textdateien. Bei `Off` bitgleich (Hash-Tests).
- **Zählung (`SourceRefusals`, ohne Schalter):** jede unbrauchbare Quittung der Aufnahme auf allen Wegen, die 413-Vorabprüfung
  von `/api/sources` und der Seite (nur mit gültigem Antiforgery-Kopf), jede Ablehnung der Anhangsprüfung von MCP und
  `size_mismatch`. Gespeichert werden Zeit, Person, Weg (`web`, `mcp_inline`, `mcp_ticket`, `upload_page`), fester Code, erkannte
  Art, Endung (`[a-z0-9]`, ≤ 8), angegebener Typ (nur `typ/untertyp`, ≤ 100) und Größe – nie Dateiname, Vorschau, Inhalt oder
  Hash. Zusätzlich zu den Codes des Vertrags `pdf_encrypted`, `unavailable` (Serverfehler, nicht die Schuld der Datei) und
  `too_long_for_ticket`; dazu die Ablehnung über `sizeBytes` bei `prepare_attachment`. **Nicht gezählt** werden: ein Token-Upload
  über der Anfragegrenze (`413` vor dem Anspruch, keine Person zuzuordnen), ein ungültiger Token (`404`), eine Datei, die die
  Upload-Seite schon im Browser abweist (über 25 MB, nie gesendet), ein Upload, der an der Zeitgrenze endet oder abgebrochen wird
  (keine Quittung), und technische `400` (veraltete Seite, kein Multipart). `too_large` ist damit untererfasst. Ein Zählfehler
  ändert nie die Antwort. Aufbewahrung 180 Tage. Auswertungsabfrage in `08-crew-system_de.md`.
- **Aufräumen:** hängende `Receiving` nach `Sources:UploadTimeout` + 1 Minute zurück auf `Open` bzw. `Failed` – im stündlichen
  Lauf und, wenn das Ticket vorher wieder benutzt wird (Seite, Token- oder Seiten-Upload), sofort bei diesem Zugriff mit derselben
  Frist; der Versuch bleibt dabei gezählt (eine Datei, die den Prozess abstürzen lässt, kann nicht endlos wiederholt werden).
  Tickets 7 Tage nach Ablauf der Seite gelöscht; Zählzeilen nach 180 Tagen.

**Bei der Integration geschlossen.** Die ersten vier Punkte sind je mit einem Test über die echte Verdrahtung belegt und per
Gegenprobe geprüft (alle vier Behebungen zurückgenommen: die Tests wurden rot); der fünfte nicht.
- **Dateinamen in der Shell.** Der erzeugte Befehl läuft unverändert mit dem echten `curl` gegen den echten Kestrel-Server, auch
  für Pfade mit Leerzeichen, Anführungszeichen, Umlauten, führendem Bindestrich, `$`, Backticks, Zeilenumbruch, `{}` und `[]`;
  kein Teil eines Namens wird ausgeführt. Ohne `--globoff` kamen Dateien mit `{}` oder `[]` nicht an.
- **Token in Logs.** Das MCP-SDK schreibt jede gesendete und empfangene Nachricht vollständig auf Trace – mit Token, Auftragstext
  und Base64-Dateien. `McpMessageLogGuard` verwirft Trace-Zeilen der SDK-Kategorien unabhängig von der Logging-Konfiguration.
  Belegt bei allen Kategorien auf Trace: Der Token steht in keiner Logzeile, keiner Antwort (auch nicht in Fehlerantworten),
  keiner Adresse und keiner Tabellenzeile.
- **Aufräumen gegen laufenden Upload.** Jeder Anspruch läuft in einer Anfrage mit Zeitgrenze `UploadTimeout` (Empfang,
  Warteschlange und Extraktion zusammen); ein hängender Upload endet damit lange vor der Frist des Aufräumens (Test mit
  gestocktem Rumpf). Zusätzlich schließen Abschluss und Freigabe nur den eigenen Anspruch (gleiches `ReceivingSince`): Ein Upload,
  den das Aufräumen doch zurückgesetzt hat, kann einen neueren Anspruch desselben Tickets weder mit seiner Datei abschließen noch
  wieder öffnen. Seine eigene Datei bleibt dann als ungebundene Quelle und wird nach 24 Stunden aufgeräumt.
- **Schalter aus.** Die Upload-Endpunkte werden nicht gemappt, und die Seite `/u/{id}` wird vor der Anmeldung aus der Anfrage
  genommen. Die Adressen antworten damit genau wie Adressen, die es nie gab (`GET` 404; auf `PUT` und `POST` an unbekannte
  Adressen antwortet die Anwendung wie bisher 405), angemeldet wie anonym. Vorher leitete `/u/{id}` zur Anmeldung um bzw. zeigte „Not found“
  mit Status 200. Werkzeugliste, Server-Hinweise und alle Anhang-Meldungen sind bei `Off` bitgleich zu Etappe 5 (Hash- und
  Wortlaut-Tests).
- **Messwerkzeug.** Eine einmalige, nicht versionierte Probe ließ `upload_check.py` gegen die echte Ausgabe des Testhosts laufen
  (echtes `curl`, echte Upload-Seite im Browser, echte Tabellen über die SQL-Abfragen des Werkzeugs): alle vier Tore bestanden.
  Das ist kein dauerhafter Test und war nicht Teil der Gegenprobe; die Mitschnitte von Claude Code waren aus den echten Antworten
  zusammengesetzt, das Modellverhalten misst erst T-A2. Dauerhaft belegt sind die SQL-Abfragen gegen ein Wegwerf-Postgres
  (`tests/test_upload_check_postgres.py`) und der Trockenfall aus synthetischen Daten.

**Korrekturrunde nach den Prüfberichten** (Sicherheit und Funktion, je mit Test, der ohne die Korrektur rot ist)
- Versuche zählen nur für geprüfte Dateien (siehe „Versuche“); belegt mit parallelen Uploads über Postgres und über die echte
  Verdrahtung.
- Pfad und Shell: die Quoting-Regel oben; ein Test führt den empfohlenen Kopierweg mit dem echten `sh` für feindliche Namen aus
  (`'`, `"`, `$( )`, Backticks, `;`, Zeilenumbruch, Emoji, Richtungssteuerzeichen): kein Namensteil wird ausgeführt, die Bytes
  kommen an; passt das Muster auf zwei Dateien, scheitert `cp`, und nichts wird hochgeladen.
- Antwort des Token-Uploads ohne Vorschau und Dateiname; `next` auch nach Ablehnung.
- Ehrliche Sätze in `submit_order` für nie angekommene Dateien; eigener Zustand der Seite für verbrauchte Tickets.
- Dateinamen über Unicode-Zeichen statt über UTF-16-Einheiten bereinigt.
- Zu lange Dateien auf dem Ticketweg abgelehnt; kein Text fordert ein Seitenfeld, das es dort nicht gibt.
- Texte unterscheiden Transportfehler (Link an die Person) von abgelehnter Datei (Grund wörtlich weitergeben, nicht erneut
  hochladen), auch bei `422` mit `curl --fail-with-body`.
- Hängendes `Receiving` wird beim nächsten Zugriff freigegeben.
- Zählweg `web` über die echte Verdrahtung belegt.
- `upload_check.py`: T-A3 (c) verlangt, dass das Seitenabbild des zweiten Kontos seine Adresse nennt und diese `/u/<Upload-Id>`
  ist; T-A4/T-A5 verlangen, dass die 422-Antworten die Upload-Id des Plans nennen und das Ticket einen geprüften, abgelehnten
  Versuch mit dem erwarteten Code zeigt. Kein Tor wurde lockerer.

**Korrekturrunde nach T-A1** (je mit Test, der ohne die Korrektur rot ist; per Gegenprobe geprüft)
- Kopierweg (B4): Kopie und Upload ausdrücklich in **einem** Shell-Befehl, mit vollständiger Vorlage und dem Platzhalter
  `"$d/upload"` in doppelten Anführungszeichen (siehe Regeln). Ein Test baut genau diesen Befehl aus der Beschreibung und führt ihn
  mit dem echten `curl` gegen den echten Kestrel-Server abwechselnd in `sh` und `bash` für feindliche Namen aus: die Bytes kommen
  an, kein Namensteil wird ausgeführt, das Verzeichnis ist danach weg. Ein zweiter Test zeigt, dass Kopie und Upload in zwei
  Befehlen oder `'$d/upload'` in einfachen Anführungszeichen nichts hochladen und das Ticket offen bleibt.
- Volles Kontingent und nicht verfügbare Extraktion (B3): eigener `next` ohne „run curlCommand again“ (siehe Versuche).
- „Nothing was stored.“ (B6) nur, wenn das Löschen der zu langen Datei gelang (siehe „Zu lang für diesen Weg“); belegt für ein
  Löschen, das mit einer Ausnahme scheitert, und für eines, das die Datei als benutzt meldet.
- Dieser Eintrag (B8): Ergebnis von T-A1 statt „steht aus“, keine Angaben zur Nutzung der Produktion.

**Schalter:** `Uploads:Tickets` (`Off|On`, ein globaler Schalter ohne Übersteuerung je Person, weil Werkzeugliste und
Server-Hinweise einmal beim Start gebaut werden). Ein Wechsel braucht das Neuerstellen des Containers.

**Step53** (`20261130120000_Step53Uploads`): zwei neue Tabellen `UploadTickets` (mit `LastRefusalCode`, nur ein Code) und
`SourceRefusals` samt Indizes, FK `SourceId → Sources` mit `ON DELETE SET NULL`; rein additiv und idempotent, `Sources`
unverändert, kein neuer `SourceOrigin`.

**Entscheidungen.** Ein Deploy für Ticket, Seite und Zählung; Token im Kopf; Token 15 Minuten, Seite 24 Stunden mit Anmeldung;
„einmalig“ heißt einmal erfolgreich, bis zu 3 geprüfte Versuche; `submit_order` alles oder nichts; ältere Werkzeuge
(`submit_request`, `submit_auto_crew_request`) behalten den bisherigen Ausweg, weil sie keine `uploadIds` annehmen.

Nach T-A1 bewusst nicht geändert:
- **Ungeprüfte Versuche und Zählzeilen ohne Deckel (B2).** Ein abgebrochener Upload gibt den Versuch zurück und schreibt eine
  Zählzeile; ebenso ein volles Kontingent, das erst nach der Extraktion auffällt. Begrenzt ist das durch zwei Plätze je Person,
  die Zeitgrenze je Anfrage und die Fristen (Token 15 Minuten, Seite 24 Stunden); der Web-Upload erlaubt angemeldeten Personen
  dasselbe, neu ist nur die Menge der Zählzeilen (auch bei `Off`). Zählzeilen je Konto und Tag werden beobachtet und bei Bedarf
  gedeckelt.
- **Was `submit_order` über eine angekommene Datei meldet (B5, Berichtigung des Vertrags).** Der Vertrag sagte, `submit_order`
  zeige Name, Größe und Vorschau. Tatsächlich nennt die Quittung je Datei den bereinigten Dateinamen, die Seitenzahl (nur PDF),
  die Zeichenzahl, die Lesequalität und die Warnungen – keine Größe, keine Vorschau, keine Prüfsumme. Auf dem Token-Weg ist der
  Name der, den der Assistent bei `prepare_attachment` selbst angegeben hat. Ein fremder Upload mit gestohlenem Token fällt also
  nur an Seiten, Zeichen oder Qualität auf und auf der Seite der Person („This file has already arrived“). Er braucht einen Token
  aus einem geteilten Verlauf binnen 15 Minuten; mit `sizeBytes` wird zusätzlich nur eine Datei genau dieser Größe angenommen.
- **`upload_check` bindet Zählzeilen nicht an das Messkonto (B7).** Kein Tor kann ohne echte Daten bestehen: Leere oder fehlende
  Belege ergeben „fehlt“, „nein“ oder „not_evaluable“. Die Lücke wirkt eher als falsches Scheitern: Eine Ablehnung eines anderen
  Kontos im Messfenster kann T-A4/T-A5 scheitern lassen; dann wird der Lauf wiederholt. Vor dem Lauf werden alte Ausgaben
  gelöscht, und alle Tore werden zusammen ausgewertet.

**Restrisiken**
- Den Pfad setzt der KI-Client in den Befehl ein. Der Befehl selbst enthält nichts Fremdes, und die Texte sagen, wie zu quoten
  ist und was bei ungewöhnlichen Namen zu tun ist; dass der empfohlene Weg sicher ist, belegt ein Test. Ob ein Modell den Texten
  folgt, ist noch ungemessen (T-A1, mittel); vor `On` wird es in T-A2 mit einem Dateinamen mit Apostroph und Leerzeichen
  gemessen. Ein Fehlgriff endet dort in einem Shell-Fehler. Unter Windows-PowerShell ist `curl` ein anderer Befehl; dann bleibt
  der Link.
- Der Token steht im Verlauf des Assistenten (gewollt: 15 Minuten, einmal erfolgreich, nur ins Konto der Person; ein zweiter
  Upload bekommt 404; was die Quittung in `submit_order` über die Datei nennt, steht unter „Entscheidungen“, B5).
- Ungeprüfte Versuche sind nicht begrenzt; ein Upload, der immer wieder an der Überlast scheitert, kann innerhalb der
  Token-Frist (15 Minuten) bzw. der Seitenfrist (24 Stunden) wiederholt werden. Die Kosten begrenzen die vorhandenen Plätze je
  Person und die Zeitgrenze je Anfrage.
- claude.ai ist nicht geprüft (Einladung an den Betreiber).

**Rückweg:** Schalter `Off` per Umgebung (sofort). Code: vorheriges Image; Step53 ist additiv, der alte Code liest `Sources`
unverändert. Über Tickets hochgeladene Quellen bleiben normale Quellen.

### Zusatz zu D-076 — Abnahme am echten System, Upload-Ticket eingeschaltet (2026-11-26)

Ausgeliefert mit `Uploads:Tickets=Off`, danach für die Messung per Umgebung eingeschaltet. Alle vier Tore, gemeinsam
ausgewertet mit `upload_check`, **bestanden**:
- **T-A2 (Claude Code, 2 Sitzungen):** PDF (24 Seiten) und DOCX über `prepare_attachment` und `curl` angekommen, Bytes
  gleich, kein Base64 im Mitschnitt, Auftrag verknüpft; beim PDF las der Schreiber den ganzen Text. Beim DOCX mit Apostroph und
  Leerzeichen im Namen wählte Claude Code selbst den empfohlenen Kopierweg in einem Befehl. Messbedingung: Shell-Befehle waren
  in diesen Sitzungen auf den von Atelier vorgeschlagenen Upload beschränkt; beim PDF versuchte Claude Code zuerst, die Datei
  selbst lokal auszulesen, was abgelehnt wurde, bevor es den Atelier-Weg nahm.
- **T-A3 (Upload-Seite):** angekommen und bytegleich; ein zweites Konto sah „Not Found“ und konnte die Upload-Id nicht
  verwenden.
- **T-A4 (Scan ohne Text):** auf beiden Wegen mit dem vorgesehenen Satz abgelehnt, nichts gespeichert, genau zwei Zählzeilen.
- **T-A5 (Zählung):** genau die erwarteten Zeilen, ohne Dateiname und Inhalt.

Danach `Uploads:Tickets=On` als Standard. Rückweg: Schalter `Off` oder das vorherige Image; Step53 ist rein additiv.
Nicht geprüft: claude.ai als Client.

## D-077 — Abschluss des Umbaus (2026-11-26)

**Status:** beschlossen. Der mit D-068 begonnene Umbau endet mit Etappe 6 (D-076) und einer kleinen Abschlussrunde, Etappe 8a.
Die Etappen 1b, 7, 8 (außer 8a) und 9 sind nicht gebaut; jede wartet auf den unten genannten Auslöser. Grundlage der
Einteilung ist eine unabhängige Prüfung des Plans gegen den Code und die bisherige Nutzung.

**Warum hier anhalten.** Der neue Weg wird bisher kaum genutzt. Jede weitere Etappe ließe sich deshalb im Wesentlichen nur an
Messkonten prüfen – die Lehre aus Etappe 5 (drei Regeln gebaut und wieder entfernt) und Etappe 6 (Bibliothek gestrichen). Der
nächste Gewinn kommt aus dem Benutzen, nicht aus mehr Bau. Der alte Weg (Crew-Composer, `submit_auto_crew_request`, die älteren
Werkzeuge, das alte Formular, „Extra careful“) wird nicht weggenommen, bevor der neue benutzt wurde. „Soweit möglich fertig“
heißt deshalb: sauberer Endstand, nicht jede Planzeile. Die Liste der Messkonten für die Auslöser steht nur in der Betriebskonfiguration, nie in diesem
Repository.

**Was läuft.** Die Etappen 0–6 wie in D-067 bis D-076 beschrieben: ein Satz als Auftrag, höchstens drei Rückfragen zum Anklicken
vor dem Lauf, Dateien ganz gelesen, Direktweg als Standard, Upload-Ticket über MCP. Gebaut, aber ausgeschaltet oder
eingeschränkt: Ändern eines Ergebnisses (`Orders:Revise = Off`), Internet-Recherche (`Research:Mode = Off`), Faktenprüfer nur
meldend (`Direct:FactCheckMode = Report`). Ein Auftrag aus claude.ai heraus ist nicht geprüft.

**Etappe 1b ist nicht gebaut.** Am Code geprüft am 2026-11-26; es fehlen:
- **Schleife:** kein `BestDraftSelector` – die sorgfältige Schleife liefert weiter die letzte Fassung; `ProfileBasedExecutor`
  führt nur einen „besten Entwurf“ als Maßstab, und das ist der längste Text (`LongestOf`). Der Schreiber bekommt weiter alle
  Befunde der Vorrunde (`ExtractPreviousFindings` liefert `AllFindings`), auch nicht blockierende und solche, die nur einen
  ausgefallenen Prüfer melden. Kein `DraftCollapseDetector`: Der Längenschutz bleibt (`IsRegression` – unter 70 % des bisher
  längsten Entwurfs, ab 2.000 Zeichen; ein nachdrücklicher zweiter Versuch, danach gewinnt der längste Text), Kürzen in der
  Schleife ist also weiter unmöglich. Keine `PlateauConvergencePolicy`: Es gibt keinen Stopp nach zwei Runden ohne Verbesserung
  (die Vorlage des Crew-Composers setzt `StagnationThreshold` gleich `MaxIterations`; sonst gilt der Standardwert 3 für
  gleichbleibende Befunde). Gebaut, aber schon in Etappe 0 (D-067): Ein Failover,
  den `OpenAiCompatibleClient` erkennt, wird zur Warnung am Lauf.
- **Rückbau Schnitt 1:** nichts entfernt. Agentischer Tool-Pfad, `/tools` und Tool-Bindungen; MCP-Client und `/mcp-servers`;
  Template Studio; die Grounding-Typen `rest-api`, `academic-search`, `news-search` und `url-fetch` samt
  `Infrastructure/Security/*`; die Finalizer `ExternalSink` und `MetadataEnrich`; `HttpProviderEditor`; die toten Konzepte
  `MaxQueriesPerRun` und `AdvisorRetryAttempted`; die Tabellen `ToolDefinitions`, `ToolInvocations`, `McpServerConfigs`,
  `TemplateStudioAnalyses` und `StudioSettings` – alles noch vorhanden. DNS-Pinning (D-066) bleibt eine offene Härtung.
- **Folge:** „Etappen 0–6 ausgeliefert“ stimmt nur ohne 1b. Die Frage von Etappe 9 („schlägt die *entschärfte* Schleife den
  Direktweg?“) ist so nicht stellbar; eine Messung würde die heutige Schleife vergleichen.

**Etappe 7 (Gedächtnis): gestrichen.** Kein Bedarf belegt; der Nachweis der Etappe („der zweite Auftrag fragt eine Frage
weniger“) wäre nur an einem Messkonto möglich. Gemerkte Regeln gingen in den Klärer-Prompt und machten die Messung
D-072 ungültig. Die Learnings werden nicht übernommen (sie passen zu keiner Textsorte des Direktwegs); die Tabelle bleibt.
**Auslöser** für einen Neuanlauf in kleinster Form („Remember this?“ nach einer Antwort): Ein Konto außerhalb der Messungen
bekommt zweimal eine Frage zum selben Thema, oder mindestens 10 beantwortete Fragen aus mindestens 2 solchen Konten in 60 Tagen
(Abfrage über `Orders.QuestionsAskedAt`/`AnsweredAt` und die Themen in `ClarifyJson`).

**Etappe 8: jetzt nur 8a** (siehe unten). Der Rest:
- **Gesperrt**, weil Teil des alten Wegs, der erst entfällt, wenn der neue benutzt wird: Crew-Composer, `submit_auto_crew_request`, die älteren
  MCP-Werkzeuge, das alte Formular; Werkzeug-Pfad, alte Grounding-Profile, Vektor-Wissensbasis, Embeddings (Anhänge und
  Websuche von „Extra careful“ und dem Composer laufen darüber).
- **Später, in einem Zug mit dem Rest:** Learning-Schleife, MCP-Client, Template Studio (Code) – tot, aber mit dem Schalter aus
  8a für Nicht-Admins unsichtbar; ein eigener Durchgang mit Review, Gesamtsuite und Deploy lohnt nicht.
- **Gestrichen bis zum Rückbau:** Einmal-Crews archivieren und die Spalten `OwnerUsername`/`Origin`; handgebaute Crews werden
  nie gelöscht.
- **Migrations-Baseline:** gesperrt, frühestens nach dem letzten `DROP`, sonst nie.
- **Später:** Orchestrator aus der Web-Schicht, Aufträge, die einen Deploy überleben, „Insights“, Doku in einer Sprache. Bis
  dahin gilt: Deploy nur, wenn kein Lauf `Pending` oder `Running` ist.
- **Auslöser für den Rückbau** (Schnitt 1 und 2 zusammen): Ein Konto außerhalb der Messungen hat mindestens 3 Aufträge über die
  neuen Werkzeuge oder die neue Seite gestellt, **und** in den 30 Tagen danach hat kein Konto außerhalb der Messungen einen Lauf
  über `submit_auto_crew_request`, `submit_request` oder „Extra careful“. Dann in dieser Reihenfolge: Navigation → Registrierung
  (die älteren Werkzeuge antworten eine Version lang mit Hinweis) → Code → Tests → Seeds; Crews nur archivieren; `DROP`
  frühestens einen Deploy **und** 30 Tage später, mit Dump und Image-Tag; die Baseline zuletzt.

**Etappe 9 („Extra careful“ neu): zurückgestellt.** Die Messung kostet mindestens zehn lange Läufe plus Blindvergleich auf
einem geteilten Kontingent, und ihr Ergebnis entschiede nur über den alten Weg – der vorerst ohnehin bleibt. **Auslöser:** Der
alte Weg wird weiter genutzt, *obwohl* die neuen Werkzeuge in den verwendeten KI-Clients angekommen sind (mindestens 3 Läufe eines Kontos außerhalb der Messungen in 30 Tagen). Dann entscheidet
eine einzige Nachtmessung am Bestand: fünf lange Aufgaben, höchstens zehn Läufe, Abbruch bei offenem Breaker. Verliert die
Schleife, greift der Rückbau aus Etappe 8; wird der alte Weg nicht mehr genutzt, entfällt er ohne Messung.

**Etappe 8a: Werkstatt-Einstiege nur für Admins.** Neuer Schalter `Ui:WorkshopForAdminsOnly` (ausgeliefert `true`). Solange er
an ist, sehen nur Admins das Menü „Workshop“, den Link „More options (choose a team yourself)“ auf der Startseite und „Technical
details“ auf der Auftragsseite; auf „Connected clients“ und „OAuth clients“ führt der Rückweg alle anderen zu „My work“ statt in
die ausgeblendete Werkstatt. Seiten, Routen, Rechte und Daten sind unverändert: Eine Werkstatt-Seite öffnet weiter, wenn ihre
Adresse eingegeben wird oder aus einem älteren Werkzeug kommt, und frühere Läufe stehen als Aufträge unter „My work“ (Step48).
Am Telefon nimmt das aufgeklappte Werkstatt-Menü jetzt die Breite der Linkzeile ein (vorher ragte es 8 px über den Rand).
Tests: bUnit für Menü, Startseite, Auftragsseite, Kontoseiten, den ausgelieferten Wert und die unveränderten Rechte der
Werkstatt-Seiten; Playwright am Telefon in einem dunklen und einem hellen Thema. **Rückweg:** `Ui__WorkshopForAdminsOnly=false`
unter `environment` des Webdienstes in der Compose-Datei bzw. Override setzen (eine `.env`-Datei allein genügt nicht) und den
Container neu erstellen.

**Harte Grenzen, auch später ohne Ausnahme bei autonomer Arbeit:** kein `DROP`, kein Löschen von Zeilen (Crews, Learnings,
Dokumente, Konten, OAuth-Clients echter Nutzer); `Mcp:LegacyTools` bleibt `true`; `submit_auto_crew_request` und
`submit_request` werden weder entfernt noch still auf den neuen Weg umgeleitet; keine Baseline; Klärer- und Prüfer-Prompt
unverändert; `Orders:Revise`, Recherche und Faktenprüfer werden nicht für alle eingeschaltet, solange ihr Tor fehlt.

**Falls später doch gebaut wird.** Näher am Nutzen als die Etappen 7–9 liegt, was gebaut, aber ausgeschaltet ist. Erste Wahl:
Ändern eines Ergebnisses ohne den Schnellknopf „Longer“ (der im Tor N′ eine Tatsache hinzufügte, D-075) als eigene kleine
Etappe mit eigenem Vertrag und einer Nachtmessung – ohne sie kann ein Laie an einem Ergebnis nichts ändern. Danach R3 der
Recherche mit größerer Menge (D-074). Beides nur nachts und mit Obergrenze.

## D-078 — Restplan: alles bauen und freischalten; Paket 1 „Ändern für alle“ (2026-11-26)

**Status:** beschlossen auf Auftrag des Betreibers. Paket 1 ist gebaut und wird ausgeschaltet ausgeliefert; eingeschaltet wird
nach Tor N″ mit eigenem Commit (Zusatz zu diesem Eintrag).

**Auftrag.** Der Betreiber hat beauftragt, die drei gebauten, aber ausgeschalteten Funktionen für alle einzuschalten – Ändern
eines Ergebnisses, Internet-Recherche, Korrektur durch den Faktenprüfer – und die ungebauten Teile zu bauen: Gedächtnis, ein
neues „Extra careful“ und den Rückbau des alten Wegs samt dem Teil Schleife von Etappe 1b. Ein Restplan legt fest, wie.

**Verhältnis zu D-077.** Für diese Punkte ersetzt der Restplan die Auslöser von D-077; insoweit sind dessen Grenzen aufgehoben.
Das betrifft: die Freischaltung von Ändern, Recherche und Faktenprüfer (jede erst nach ihrem eigenen Tor), Gedächtnis und
„Extra careful“ (in den unten genannten kleinsten Formen) sowie den Rückbau (`Mcp:LegacyTools`, die älteren Werkzeuge und
zuletzt das Löschen alter Tabellen, nur in den Stufen von Paket 8 und 9). Alle anderen Regeln von D-077 gelten weiter: keine
gelöschten Zeilen echter Nutzer, kein `DROP` außer in Paket 9, keine Migrations-Baseline, Klärer- und Prüfer-Prompt bitgleich,
außer wo ein Paket ausdrücklich eine neue Messung vorsieht (Paket 5), kein stilles Umleiten der älteren Einreich-Werkzeuge.

**Restplan in Kurzform (Reihenfolge):**
0. Vorbereitung: Sicherung, Abnahme des Wegs über claude.ai, ein gemeinsamer Messwächter für alle Messläufe.
1. **Ändern für alle** ohne Knopf „Longer“ (unten).
2. **Internet-Recherche für alle** nach einer größeren Messung (R3′) mit vorab bestimmter Toleranz für das Rauschen des Jurors;
   verfehlt sie, wird Recherche nur ein Häkchen „Look things up on the web“ (Standard aus).
3. **Faktenprüfer korrigiert den eindeutigen Fall:** Eine Zahl, die nirgends im Material steht, wird zur Lücke in eckigen
   Klammern; die übrigen Meldungen stehen eingeklappt hinter einer Zeile mit ihrer Anzahl; das Ersatzmodell korrigiert nie.
4. **Gedächtnis, kleinste Form:** Absender, Unterschrift, Anrede und Sprache einmal unter „About me“ angeben, mit einem Klick aus
   dem Ergebnis; MCP `profile` bekommt Schreiben für dieselben Felder; streng je Konto.
5. **Bedingt: leiserer Faktenprüfer** mit neuem Prüfauftrag, gemessen an einer zurückgehaltenen Hälfte; verfehlt er, bleibt der
   alte Prüfer mit der eingeklappten Anzeige aus Paket 3.
6. **Etappe 1b, Teil Schleife, kleinste Form:** beste statt letzter Fassung, an den Schreiber nur blockierende Befunde, Stopp nach
   zwei Runden ohne Verbesserung – je ein Schalter, belegt durch Nachspiel gespeicherter Läufe.
7. **Neues „Extra careful“:** Direktweg mit Gliederung gegen den Direktweg und gegen die entschärfte Schleife, blinder Juror;
   ohne Sieger entfällt das Häkchen.
8. **Rückbau des alten Wegs in vier umkehrbaren Stufen:** für Nicht-Admins abschalten, Übergang mit Team-Wahl für Admins im neuen
   Weg, toter Code, dann der Rest.
9. **Alte Tabellen löschen**, frühestens 30 Tage nach Stufe 8d und nach einem geprüften Rückspielen der Sicherung – der einzige
   nicht umkehrbare Schritt.

Jedes Paket hat einen eigenen Vertrag vor dem Bau (Kriterien, Trockenfall, Obergrenzen der Modellaufrufe; außerhalb dieses
Repositorys), einen eigenen Zweig, ein Review, einen Deploy und eine Abnahme am echten Lauf; Schwellen werden nach Beginn einer
Messung nicht geändert. Gestrichen: Migrations-Baseline, „Remember this?“, Stilvorlagen, Bibliothek, `DraftCollapseDetector`
(außer Paket 7 verlangt ihn).

**Paket 1 – Ändern für alle.** Tor N′ (D-075) scheiterte nur am Knopf „Longer“: Die Verlängerung fügte trotz „without adding new
facts“ eine Tatsache hinzu. Gebaut in kleinster Form:
- **Knopf „Longer“ entfällt** in Web und MCP. Angeboten werden Shorter, More formal, Friendlier, Simpler words. Eine Änderung mit
  „Longer“ lehnt der Dienst ab, ohne einen Lauf anzulegen: „To make the text longer, tell the writer what to add.“ `revise_order`
  mit `quick: ["longer"]` antwortet mit diesem Satz und dem Hinweis an den Client, die Person zu fragen und ihre Worte in `change`
  zu übergeben; ist Ändern für das Konto aus, kommt sofort „Changing a text is not available yet.“. Der Enum-Wert bleibt, weil
  gespeicherte Änderungen ihn nennen; sein fester Satz geht aber nie wieder an den Schreiber – er fehlt unter den früheren
  Änderungen einer neuen Fassung, und „Resume“ einer Änderung, die nur ihn verlangte, wird abgelehnt.
- **Hinweisfeld mit Beispiel:** „For example: The meeting is on 5 May, not 12 May. Or: Add that parking is free.“
- **Wer Verlängern als Freitext verlangt, bekommt es.** Statt einer Wortliste gilt eine strukturelle Regel: Hat eine geänderte
  Fassung mehr als 115 % der Wörter des Textes, den sie geändert hat (`Runs.SeedDraftText`, gezählt wie `Runs.WordCount`), steht
  im Web über dem Text und in MCP im neuen Feld `lengthNotice` von `get_order_result`: „This version is noticeably longer than the
  one before. Please check the new parts: the writer may have added things you did not ask for.“ Der Satz behauptet keine Herkunft,
  weil der Code nur die Länge misst (auch eine verlangte Ergänzung kann ihn auslösen). Die Regel wird bei jeder Anzeige aus den gespeicherten
  Texten berechnet, nicht gespeichert und nicht im Bericht; ältere gespeicherte Fassungen bekommen den Satz ebenso, alte
  Berichte bleiben unberührt. Erste Fassungen und ohne Änderungswunsch fortgesetzte Läufe bekommen ihn nie; eine fortgesetzte Änderung zählt wie eine Änderung.
- **Bitgleich** (Hash-Tests, festgehalten vor dem Bau): Satz `RevisionInstruction`, System-Prompt des Schreibers, Klärer-Prompt
  und -Schema, Prüfer-Prompt und -Schema, die festen Sätze aller Schnellknöpfe. Die sechs in N′ bestandenen Fälle gelten weiter.
- **Schalter:** `Orders:Revise` bleibt ausgeliefert `Off`. **Rückweg:** `Orders__Revise=Off`.

**Freischaltbedingung Tor N″** (schließt die in D-075 als „vor dem Einschalten zu beobachten“ genannten Lücken): zwei frische
Aufträge (deutsch, englisch), sechs Änderungen – eine Kette „Kürzen, danach ein Datum ersetzen“ (die Kürzung darf nicht erneut
ausgeführt werden), eine Kette „Satz gegen eine Vorgabe des ersten Auftrags ergänzen, danach seinen Wert wieder ersetzen“, „Simpler
words“ (nie gemessen) und „mach es länger“ als Freitext – sowie eine Probe `quick: longer` ohne Modellaufruf; ein blinder Prüfer
mit zwei Kontrollpaaren wie in N′. Alle Schwellen sind vorab fest und an den gespeicherten Aufnahmen von N′ als erreichbar bei
richtigem und reißend bei falschem Verhalten gezeigt. Folgen: Datenverlust oder ein fehlender fester Satz → Halt und Behebung;
eine verfehlte Kette → eine Reparatur (der Schreiber bekommt nur noch die neueste Änderung), danach Einschalten mit einem festen
Hinweis ab der dritten Fassung; „Simpler words“ verfehlt → auch dieser Knopf entfällt, der Rest wird eingeschaltet. Das Ergebnis
folgt als Zusatz.

**Zusatz (2026-11-26): Tor N″ bestanden.** Gemessen an zwei frischen Testaufträgen (Deutsch, Englisch) mit sechs Änderungen
samt beiden Ketten, „Simpler words“, „länger“ als Freitext und der Probe `quick: longer` (kein Modellaufruf). Alle Teile
bestanden; im Urteil des blinden Prüfers wurde nichts gestrichen. Beim Wunsch „länger“ kam ein zusätzlicher Satz mit eigenem
Inhalt dazu; genau dafür steht der feste Hinweis über dem Text. `Orders:Revise` steht ab jetzt auf `On`
(Rückweg: `Orders__Revise=Off`).

## D-079 — Keine Websuche bei Aufträgen mit der Standard-Crew; Suchanfrage scheitert geschlossen (2026-11-26)

**Status:** umgesetzt.

**Kontext.** Zugesagt ist: Die Websuche ist für alle aus (`Research:Mode = Off`), ein Auftrag mit Dateien sucht nie, und
Antworten und die Angaben unter „About me“ gehen nie in eine Suche. „Extra careful“ hielt das nicht ein. Es nutzt die
Standard-Crew (`klassik`), und diese trägt die Grounding-Quelle `tavily-refined`, die kein Schalter steuerte: Jeder solche
Auftrag schickte eine Suchanfrage an Tavily, auch mit angehängten Dateien. Die Anfrage bildete `LlmGroundingQueryExtractor` aus
dem Auftragsteil des Briefings (seit E2 Fix 1 ohne Antworten und Profil); schlug dieser Modellaufruf fehl oder kam nichts zurück,
ging der Anfang des Auftragstexts selbst (bis 400 Zeichen) an den Suchdienst.

**Entscheidung.**
- **Standardaufträge suchen nichts außerhalb.** Ein Lauf eines Auftrags mit der Standard-Crew – „Extra careful“ im Web oder über
  MCP, `submit_request` ohne `crewTemplate`/`customCrew` (bei `Orders:LegacySubmitDefaultMode = Careful`) – behält nur die
  Grounding-Quellen, die in Atelier bleiben: die Typen `vector-store`, `order-sources`, `learning-retrieval`, `static-context`,
  `crew-catalog`, `tool-catalog`, `pack-catalog`, und keine davon über den Tool-Pfad (eine Erlaubnisliste, ein neuer Typ bleibt
  also draußen, bis er aufgenommen wird). Web-, Nachrichten-, Wissenschafts-, REST-, URL- und Tool-Quellen fallen weg. Die Regel
  wendet der Orchestrator auf jeden Lauf an, bevor die Pipeline gebaut wird (`StandardOrderWebSearch.ForRunAsync`); sie gilt damit
  auch für fortgesetzte und wiederholte Läufe, deren gespeicherter Snapshot die Websuche noch nennt, und für einen fortgesetzten
  Lauf, der noch nicht mit seinem Auftrag verknüpft ist (dann zählt der Auftrag des Laufs, den er fortsetzt). Gespeicherte
  Snapshots werden nicht umgeschrieben.
- **Was als Standard gilt:** Der Snapshot ist `klassik`, der Auftrag nennt kein Template, und für den Auftrag wurde keine Crew
  zusammengestellt. Unverändert bleiben: ein bewusst genanntes Template (Werkstatt-Formular, `submit_request` mit `crewTemplate`,
  auch `klassik`), eine eigene Crew (`customCrew`), eine zusammengestellte Crew (`submit_auto_crew_request`, Werkstatt „auto“)
  und Läufe ohne Auftrag. Aufträge, die aus der Zeit vor den Aufträgen übernommen wurden, nennen kein Template; ihre Läufe gelten
  als Standardaufträge, weil nicht mehr bekannt ist, ob das Template gewählt wurde.
- **Suchanfrage scheitert geschlossen.** `IGroundingQueryExtractor` liefert `GroundingQuery.NotFormed`, wenn der Aufruf
  fehlschlägt oder keine Anfrage zurückkommt; der `tavily`-Provider schickt dann nichts und scheitert, die Quelle wird zur
  Laufwarnung `grounding.provider_skipped`. Der Extractor ist Pflicht; einen Weg ohne ihn gibt es nicht. Der Auftragstext ist nie
  die Ersatzanfrage. Unverändert und nur über eine bewusst gewählte Crew erreichbar: Ein Profil mit `ExtractQuery=false` sowie die
  Typen `news-search`, `academic-search`, `rest-api` und Tool-Quellen schicken den Auftragsteil des Briefings (nie Antworten oder
  Profil) unverändert.
- **Recherche des Direktwegs (`OrderResearchService`)** scheiterte schon geschlossen und bleibt unverändert: standardmäßig aus,
  nie mit Dateien, nie bei „Extra careful“, nur Themen des Klärers passieren das Tor, kein Rohtext.

**Ohne bewusste Wahl erreichbar?** Nicht über die Menüs. Das ältere Formular `/new` öffnet sich für alle Angemeldeten, die seine
Adresse eingeben (D-077, 8a), und hat `klassik` vorausgewählt; ein dort gestarteter Lauf nennt sein Template und behält die
Websuche. Dafür braucht es die Adresse, und es ist die Werkstatt; das gilt als Wahl.

**Tests.** Ende-zu-Ende über die echte Verdrahtung mit aufgezeichneten HTTP-Anfragen (`CarefulOrderWebSearchEndToEndTests`):
„Extra careful“ über MCP mit Angaben unter „About me“, ein sorgfältiger Web-Auftrag und `submit_request` ohne Template sowie ein
fortgesetzter Lauf mit gespeichertem Snapshot schicken nichts an den Suchdienst und rufen keine Anfrageformung auf, während die
Angaben unter „About me“ weiter beim Schreiber ankommen; `submit_request` mit `crewTemplate = klassik` sucht weiter, nur mit der
gebildeten Anfrage; ohne gebildete Anfrage sucht es nichts und speichert die Warnung. Ohne diese Änderung scheitern alle diese
Tests außer dem mit `crewTemplate`, der weiter sucht. Die Prüfung eines geklärten sorgfältigen Auftrags in `IntakePrivacyEndToEndTests` erwartet jetzt keine Suche.
Datenbanktests für die Regel (`StandardOrderWebSearchTests`), Unit-Tests für Extractor und Provider.

## D-080 — Erklärseite „How it works“ und Leitfäden je Zielgruppe (2026-11-26)

- **Seite `/how-it-works`**, ohne Anmeldung erreichbar: erklärt in einfachen englischen Wörtern (D-062), was Atelier tut, was
  geprüft wird und was nicht. Sie liest dieselben Schalter wie die Anwendung, einschließlich der Ausnahmen je Person, und
  beschreibt deshalb nie eine Funktion, die für die Person ausgeschaltet ist. Die Tabelle mit Schlüsselnamen und Werten sehen nur
  Administratoren.
- **Leitfäden** `docs/12` bis `docs/15` (Anwender, KI-Assistenten, Administration, Entwicklung), deutsch und englisch. Ein Test
  gleicht die Werkzeugliste im KI-Assistenten-Leitfaden mit dem Server ab, ein weiterer rendert alle in der App freigegebenen
  Dokumente und prüft jedes Linkziel.
- **Ehrlichkeit vor Werbung:** Aussagen nur, soweit der Code sie sicherstellt; keine Behauptungen über andere Produkte.
  Geprüft in zwei unabhängigen Runden.
- **Zur Aussage über claude.ai:** Seit dem 26.11. ist ein kompletter Weg über eine in claude.ai eingerichtete Verbindung erprobt,
  aufgerufen aus Claude Code: Auftrag mit Datei und Rückfrage, danach eine Änderung. Das Chatfenster von claude.ai selbst und
  Claude Desktop sind weiterhin nicht geprüft. Das ersetzt insoweit die früheren Vermerke „claude.ai nicht geprüft“.

## D-081 — Überspringen legt den Anwender nie fest (2026-11-26)

**Status:** umgesetzt; die Messung am echten System ist vorbereitet und steht noch aus.

**Regel.** Überspringt die Person eine Frage, startet sie ohne Antwort, läuft die Antwortzeit ab oder drückt sie „You decide“, darf
Atelier keine verbindliche Entscheidung für sie treffen, die sie nicht selbst gesagt hat: keine Zu- oder Absage, Bestellung,
Kündigung, Zahlung, Teilnahmebestätigung oder Zustimmung. Ein solcher Punkt bleibt im Text offen oder wird als Lücke markiert und am
Ergebnis genannt. Antworten der Person und Entscheidungen, die schon im Auftrag stehen, gelten wie bisher.

**Mechanismus vor dieser Änderung.** `QuestionGate` behält für eine gestellte Lücke keine Annahme. Eine übersprungene Frage stand
deshalb gar nicht im Auftrag an den Schreiber, und er wählte frei; „You decide“ kam als `Frage → left to you` an. Eine Frage, die
das Tor verwarf (zweite Frage zum Thema, über der Obergrenze, unbrauchbare Antworten), machte ihre Annahme oder „not specified – the
writer decides“ zur Annahme im Auftrag. Die Vorauswahl des Klärers (`defaultOptionId`) erreichte den Schreiber nie; sie wurde nur über
MCP weitergegeben.

**Entscheidung.**
- **Keine Vorauswahl.** `QuestionGate` setzt `DefaultOptionId` nie, und die MCP-Abbildung sendet immer `null`, auch für Datensätze von
  vor dieser Änderung.
- **Offene Fragen bekommen einen eigenen Block im Auftrag an den Schreiber.** Eine Frage des Klärers gilt als offen, wenn keine
  verwertbare Antwort der Person vorliegt (übersprungen, ohne Antworten gestartet, Zeit abgelaufen, „You decide“ oder weder bekannte
  Option noch eigene Worte; `OpenQuestions.IsLeftOpen`). Dazu wird eine Frage, die der Klärer unter den Themen `Purpose` oder `Other`
  stellen wollte und die das Tor als zweite Frage zum Thema, über der Obergrenze oder wegen ihrer Form verwirft, zurückgehalten
  (`QuestionGateResult.HeldBack`, gespeichert als `IntakeRecord.HeldBack`) und nie zur Annahme oder zur Zeile „the writer decides“.
  Beide Arten stehen wörtlich unter der Überschrift „Questions the person did not answer …“. Sie sagt zuerst, dass Auftrag,
  Antworten und Korrekturen der Person und spätere Änderungen vorgehen und befolgt werden, wo sie eine Frage klären; sonst
  entscheidet der Schreiber nie für die Person, was sie bindet, hält es offen (Eingang bestätigen, nachfragen, um Zeit bitten) oder
  setzt einen gut sichtbaren Platzhalter, nennt die offene Entscheidung unter den offenen Punkten, erfindet keine Fakten und wählt
  alles andere frei. Eine zurückgehaltene Frage, die wortgleich mit einer beantworteten ist (ohne Rücksicht auf Groß- und
  Kleinschreibung, Leerraum und Satzzeichen), entfällt aus Block und Ergebnis. Die feste Längenfrage öffnet den Block nie; „You
  decide“ erscheint dort weiter als „left to me“.
- **Start ohne Prüfung.** Ein Auftrag mit Rückfrage-Stufe, der startet, bevor sein Auftrag geprüft ist (Start während des Lesens,
  Klärer gescheitert oder zu langsam), bekommt dieselbe Regel als eine allgemeine Zeile („The request was not checked …“). Aufträge
  ohne Rückfrage-Stufe (`Orders:IntakeMode = Off` oder `Shadow`) bekommen sie nie.
- **Unverändert:** Aufträge an den Schreiber ohne offene oder zurückgehaltene Frage und ohne Start ohne Prüfung bleiben bitgleich;
  Datensätze ohne zurückgehaltene Frage werden gespeichert wie bisher. Prompts und Schemata von Klärer, Schreiber und Faktenprüfung
  sind unverändert (Prüfsummen-Tests).
- **Ergebnis.** Die Fragen werden aus dem Auftrag der gezeigten Fassung gelesen: im Web unter „Open points“ (bei einer geänderten
  Fassung mit der Version, vor der sie gestellt wurden), über MCP als `unansweredQuestions` (angehängt) mit dem Hinweis, sie zu zeigen
  und nicht selbst zu beantworten; Schritt 5 der MCP-Hinweise nennt sie. Die Karte sagt, dass der Schreiber bei übersprungenen Fragen
  angewiesen wird, nicht für die Person zu- oder abzusagen, und bittet, Annahmen und Text zu prüfen; „You decide“ erscheint als
  „left open“.
- **Recherche und Prüfungen** bekommen den Auftrag ohne diese Blöcke; nur die vollständigen Überschriften gelten als Blockanfang.
  Eine Änderung behält den Block.

**Grenzen.**
- Durchgesetzt ist nur, was dem Schreiber gesagt wird; ob er sich daran hält, misst das Messtor unten, garantiert ist es nicht.
- Eine Entscheidung, die der Klärer ohne Rückfrage in eine Annahme schreibt, ist nicht abgedeckt (braucht eine Klärer-Änderung und
  eine neue große Messung).
- Vor dieser Änderung gespeicherte Aufträge behalten ihren Auftrag an den Schreiber: Ändern oder Fortsetzen fügt keinen Block hinzu,
  und ihr Ergebnis zeigt keine `unansweredQuestions`. Beim Deploy wartende Aufträge behalten die Annahmen ihres gespeicherten
  Datensatzes.
- „Extra careful“ und `Direct:WriterNotes = false` haben keinen Kanal für offene Punkte; dort kann die Anweisung nur im Text sichtbar
  werden. Nicht gemessen.
- **Rückweg:** Ein älteres Image kennt die neuen Überschriften nicht. Steht ein solcher Block als erster Block im Auftrag an den
  Schreiber, hält das ältere Image beim Ändern oder Fortsetzen dieses Auftrags den ganzen Auftrag samt About-me-Angaben für die
  Anfrage. Nach einem Rückweg mit dieser Version geschriebene Aufträge nicht ändern oder fortsetzen.

**Tests.** Reine Tests von Tor und Auftragsaufbau (keine Vorauswahl, keine Annahme im Auftrag, „You decide“ und Teilantworten offen,
zurückgehaltene Fragen bei zweiter Frage, Obergrenze und unbrauchbaren Antworten, andere Themen unverändert, Start ohne Prüfung,
Antworten unverändert, Längenfrage, nur volle Überschrift), Parser und Änderung, Datenbank- und Ende-zu-Ende-Tests für Überspringen,
Antworten, automatischen Start und Klärer-Fehler, bUnit für „Open points“, MCP-Feld, Hinweis und `defaultOptionId`, Grundlinien der
MCP-Hinweise neu genommen. Alle Beispiele sind erfunden.

**Messung (vorbereitet, nicht ausgeführt).** Frische, erfundene Fälle auf Deutsch und Englisch, alle mit `skip=true` auf dem
Direktweg: Entscheidungsfälle, Kontrollfälle ohne Entscheidung und Fälle mit der Entscheidung im Auftrag; Schwelle 0 Festlegungen in
Entscheidungsfällen, Kontrollfälle nicht schlechter, höchstens 40 Modellaufrufe, blinder Prüfer ohne Werkzeuge in leerem Ordner. Nicht
gemessen werden „You decide“, Teilantworten, Zeitablauf, Änderungen und „Extra careful“. Vertrag und Werkzeug liegen außerhalb des
Repositorys.

### Zusatz zu D-081 — erste Messung nicht bestanden, Anweisung geschärft (2026-11-26)

**Ergebnis.** Die erste Messung verfehlte zwei Schwellen: keine Festlegung in Entscheidungsfällen (S1) und brauchbare
Entscheidungstexte (S4). Die Schwelle für Fälle mit der Entscheidung im Auftrag und die Kontrollfälle waren erfüllt, und der Prüfer
erkannte seinen Kontrolltext. Die durchgefallenen Texte hielten die Entscheidung widersprüchlich offen: Ein Platzhalter enthielt beide
Antworten, während ein anderer Satz die Teilnahme schon voraussetzte, oder der Text kündigte eine spätere Antwort an und ließ
zusätzlich einen Platzhalter für die Entscheidung.

**Entscheidung (nur Code und Überschrift, Klärer unverändert).** Die Regel in beiden Überschriften verlangt jetzt einen vollständigen,
so absendbaren Text, der jede Entscheidung auf **genau einem von zwei Wegen, nie beiden** offenhält: entweder noch nicht entscheiden
(Eingang bestätigen, nachfragen oder ankündigen, wann die Person antwortet; bevorzugt, wo ein Zwischenbescheid sinnvoll ist) oder
die Entscheidung in genau einen gut sichtbaren Platzhalter in eckigen Klammern legen, der alles enthält, was von ihr abhängt, mit
beiden möglichen Antworten. Kein Satz außerhalb des Platzhalters darf ein Ja oder Nein voraussetzen, auch nicht beiläufig (Vorfreude,
Dank für eine Zusage). Der Vorrang von Auftrag, Antworten, Korrekturen und Änderungen steht weiter vorn; beim Start ohne Prüfung steht
er jetzt ebenfalls zuerst. Mit den ersten Überschriften gespeicherte Aufträge bleiben lesbar (`OpenQuestionsHeadingFirstVersion`,
`NotCheckedHeadingFirstVersion`); alle vier Überschriften sind durch Prüfsummen-Tests festgehalten.

**Nächste Messung.** Neue erfundene Fälle in gleicher Zahl und Art, Schwellen unverändert; sie darf ohne Faktenprüfung laufen.

**Zusatz (2026-11-26): Messtor bestanden.** Zweiter Lauf mit neuen, frei erfundenen Testfällen (Deutsch und Englisch), alle mit
übersprungenen Rückfragen: keine Festlegung in den offenen Entscheidungsfällen, im Auftrag verlangte Entscheidungen umgesetzt,
Kontrollfälle brauchbar, Entscheidungstexte versendbar. Gemessen ist nur das Überspringen auf dem Standardweg.

## D-082 — Zahlen-Korrektur des Faktenprüfers: gebaut, nachgespielt, nicht eingeschaltet (2026-11-26)

- **Gebaut und nachgespielt** (ohne Modellaufrufe): eine enge Korrektur, die nur eine Zahl durch eine Lücke ersetzt, die nirgends
  im Material steht, samt einer Anzeige, die alle übrigen Meldungen eingeklappt hinter ihrer Anzahl zeigt.
- **Nicht eingeschaltet und nicht ausgeliefert.** Im Nachspiel zeigte sich kein Fall, in dem die Korrektur geholfen hätte; sie
  hätte eine richtige, allgemein bekannte Zahl entfernt. Die eingeklappte Anzeige hätte inhaltliche Meldungen verdeckt. Eine
  Ausnahme für Allgemeinwissen ließe sich ohne Modell nicht sauber fassen.
- `Direct:FactCheckMode` bleibt `Report`. Der Stand liegt unversendet in einem Archivzweig. Wiederaufnahme nur, wenn ein neues
  Nachspiel wiederholt fallbezogene, nicht allgemein bekannte erfundene Zahlen findet. Eine ruhigere Anzeige wird erst nach einer
  inhaltlichen Einordnung der Meldungen neu entschieden.

## D-083 — Gedächtnis, kleinste Form: „Tell Atelier once“ (2026-11-26)

**Status:** gebaut und nach einer unabhängigen Prüfung nachgeschärft; Abnahme am echten Lauf mit zwei Testkonten vorbereitet und noch
nicht ausgeführt.

**Anlass.** Rückfragen betrafen bisher nur Dinge des einen Auftrags; Absender, Anrede und Unterschrift wurden nie gefragt, aber in
vielen Aufträgen angenommen oder als Lücke gelassen, und kaum jemand hatte „About me“ ausgefüllt. Ein Gedächtnis hilft also genau
dort: einmal sagen, wer man ist.

**Entscheidung.**
- **Angebot am Ergebnis.** Hat ein Ergebnis eine nicht korrigierte Annahme oder eine offen gelassene Frage zu Absender, Unterschrift,
  Anrede oder Sprache und hat „About me“ dazu nichts, zeigt die Ergebnisseite der Person, der der Auftrag gehört, je Thema ein Feld
  mit „Save“. Gespeichert wird nur in Felder, die noch leer sind, in einem gesperrten Schritt; ein inzwischen belegtes Feld bleibt, und
  die Seite sagt „Already saved under About me“. Alle anderen Felder bleiben, auch wenn gleichzeitig gespeichert wird.
- **Ab dem nächsten Auftrag ohne Modell.** Der vorhandene Code beantwortet diese Themen aus dem Profil (`ProfileMemoryResolution`,
  `QuestionGate`): keine Annahme, keine Frage dazu; der Schreiber bekommt das Profil wie bisher im Profilblock. Der gezeigte Text und
  jede Änderung daran behalten die Angaben, mit denen er entstand (eine Änderung nutzt das Briefing der Fassung, die sie ändert) – auch
  gelöschte; das sagen Ergebnisseite und Handbuch.
- **Was der Schreiber bekam.** Das Ergebnis nennt die Themen, die der Schreiber mit dieser Fassung aus „About me“ bekam („With this
  request the writer got from About me: …“) – gesendet, nicht unbedingt verwendet –, mit Verweis zum Ändern und Löschen für die
  nächsten Aufträge.
- **Themen nur strukturell.** Sie kommen aus dem gespeicherten Lesen des Auftrags und dem Profilblock des gespeicherten Briefings,
  nie aus Wörtern im Text oder in Platzhaltern. Ein Platzhalter wie „[Ihr Name]“, den nur der Schreiber setzt, ohne dass das
  Lesen des Auftrags das Thema nannte, löst kein Angebot aus.
- **MCP.** `profile` füllt dieselben vier Felder (`name`, `signature`, `formality`, `language`) im eigenen Profil, nur wenn sie leer
  sind; ein belegtes Feld nennt die Antwort. Ohne Werte liest es nur. `get_order_result` liefert `aboutMeMissing` und `fromAboutMe`;
  die Server-Hinweise sagen, nur die eigenen Worte der Person zu speichern und nie selbst zu füllen.
- **Zeichenprüfung.** Name, Tätigkeit und Organisation sind eine Zeile ohne Steuerzeichen; eine Unterschrift darf Zeilenumbrüche
  haben, aber keine anderen Steuerzeichen und keine Zeile, die mit einem Feldnamen des Profilblocks und Doppelpunkt beginnt. Damit lässt
  sich über einen Wert kein weiteres Feld in den Profilblock einschleusen. Das gilt für Formular, Ergebnisseite und MCP.
- **Streng je Konto.** Beides steht nur für die Person, der der Auftrag gehört; die Administration sieht es an fremden Aufträgen nicht,
  und gespeichert wird nur ins eigene Profil, auch über MCP.
- **Schalter `Orders:TellOnce`** (`WithoutPromise` ausgeliefert, `On`, `Off`): `WithoutPromise` zeigt die Überschrift „Save it under
  About me for your next texts“; `On` zeigt „Tell Atelier once – it will not leave this open again“ und wird erst nach bestandener
  Abnahme gesetzt, weil das Versprechen auch den Text betrifft, den nur der echte Lauf belegt. `Off` zeigt nichts davon; Werkzeugliste
  und Server-Hinweise sind dann bitgleich zum Stand vor D-083 (`profile` nur lesend). Die Seite „How it works“ folgt dem Schalter.
- **Unverändert:** Klärer-Prompt und -Schema (Prüfsummen-Tests), das Briefing an den Schreiber und die Antworten von Aufträgen ohne ein
  solches Thema.

**Nicht gebaut:** eine Regel aus einer Korrektur (Auslöser: ein Konto außerhalb der Messungen stellt denselben Änderungswunsch zweimal),
Stilvorlagen (kein Fall), „Remember this?“, die Übernahme der früheren Lernnotizen und ein neues Laden des Profils bei einer Änderung.

**Abnahme (vorbereitet).** Testkonto A, zwei Aufträge derselben Textsorte ohne Faktenprüfung: Nach dem Speichern hat der zweite keine
Annahme und keine Lücke zum Thema, und die gespeicherte Unterschrift steht wörtlich im Text. Testkonto B sieht nichts von A. Bestanden →
`Orders:TellOnce = On`. Verfehlt der Schreiber das trotz Profil: eine Reparatur am Profilblock, ein Auftrag neu, höchstens zwei Runden;
danach bleibt `WithoutPromise`.

**Zusatz (2026-11-27): Abnahme bestanden.** Gemessen mit zwei Testkonten und frei erfundenen Aufträgen derselben Textsorte, ohne
Faktenprüfung: Nach dem Speichern hatte der zweite Auftrag von Konto A keine Lücke und keine Rückfrage zum gespeicherten Thema, die
gespeicherte Unterschrift stand wörtlich im Text, und Konto B sah nichts von Konto A (Profil leer, keine fremden Aufträge, fremdes
Ergebnis abgewiesen). **Folge:** `Orders:TellOnce = On` in `appsettings.json` (Code-Standard bleibt `WithoutPromise`), eigener
Commit. Die Überschrift ist enger gefasst: „Tell Atelier once – your next requests get it from About me“ statt „… it will not leave
this open again“, weil der Code nur zusichert, dass jeder neue Auftrag die Angabe bekommt (und ein zuerst gelesener Auftrag sie nicht
mehr annimmt oder erfragt; Aufträge, die das Lesen überspringen, werden nicht gelesen); der gezeigte Text und jede Änderung daran behalten
die Angaben, mit denen er entstand. Erklärseite und Handbuch sagen es ebenso. **Rückweg:** `Orders__TellOnce=WithoutPromise`
(Angebot ohne Zusage) oder `Orders__TellOnce=Off`.

## D-084 — Startseite sagt, was Atelier für alle tut (2026-11-26)

- **Inhalt:** Die Startseite (`/`) beschreibt jetzt den Standardweg statt der Werkstatt-Schleife: Auftrag in eigenen Worten
  (Dateien optional), höchstens die eingestellte Zahl an Rückfragen (überspringbar), ein Schreibdurchgang, Prüfungen ohne KI und ein
  Faktenprüfer, der meldet, höchstens ein weiterer Versuch, dann der Text mit „What was checked“, offenen Punkten und Annahmen;
  „Change something“ und Downloads. Neu: ein erfundenes Beispiel (Auftrag → Rückfrage → Text mit Lücken, Randnotizen und den
  Ergebniskästen), „Who it is for“ mit Einstiegen für Privat, Verein und Schule, kleines Büro, Nutzer von KI-Assistenten,
  Administratoren und Entwickler, „What Atelier does not do“, kurz „Where your words go“ und ein klarer Weg zum Start (angemeldet:
  „Write something“).
- **Werkstatt verkleinert** auf einen Abschnitt „For teams“: keine Modellnamen, Zahlen, Kosten oder Lauf-Kennungen mehr. „Extra
  careful“ ist als älterer Weg mit dem eingebauten Team beschrieben, offen für alle.
- **Dieselben Schalter wie „How it works“:** Die Seite liest `HowItWorksFeatures` für die betrachtende Person; nichts ist kopiert.
  Titel und Beschreibung stimmen für jeden Schalterstand.
- **Ehrlichkeit vor Werbung:** keine Aussagen über andere Produkte, keine Garantien, keine Zahlen, die nicht aus den Einstellungen
  kommen. Das Beispiel ist als erfunden gekennzeichnet. Tests prüfen verbotene Wörter und Namen, jeden Schalterzustand, dass jeder
  Link auflöst, und im Browser (hell/dunkel × Desktop/Telefon) seitliches Scrollen, Layoutsprung, Kontrast jedes Textes und die
  Tastatur.
- **Nebenbei behoben:** Die „Ghost“-Schaltflächen der öffentlichen Seiten wurden durch eine allgemeine Regel des
  Dashboard-Stylesheets mit 40 % Deckkraft gezeichnet (Kontrast unter dem Minimum); dem Kopfbereich nahm eine spezifischere Regel den
  seitlichen Abstand. Der Filmabschnitt ist unverändert.
- **Nach der unabhängigen Prüfung:** Die Seite „Host it yourself“ (`/self-host`) sagt jetzt dasselbe wie die Startseite: Aufträge,
  Texte und Dateien bleiben auf dem eigenen Server und gehen an die angebundenen KI-Dienste (vorher: „they never leave your
  servers“, dazu „briefings“, „workshop“, „GEEF loop“). Der Film bleibt unverändert, steht aber bis zum neuen Video hinter „For
  teams“. „What was checked“ sagt „starts with the text itself, not with a remark to the reader“ statt „starts directly“ (Web, MCP
  und Leitfäden). Die mitlaufende Navigation ist deckend. Die Wortliste läuft über jeden Schalterzustand.

## D-085 — Ältere Prüfschleife: drei Schutzschalter, ausgeliefert aus (2026-11-27)

- **Zweck:** Paket 6 des Restplans (Etappe 1b, Teil Schleife, kleinste Form). Die ältere Prüfschleife („Extra careful“,
  Werkstatt-Vorlagen, fortgesetzte Läufe) bekommt drei Schalter, damit sie in der Messung für das neue „Extra careful“ (Paket 7)
  fair antreten kann. Kein Umbau, keine Änderung am SDK, kein `DraftCollapseDetector`.
- **Schalter** (Abschnitt `Convergence`, je `Off`/`On`, im Code und in `appsettings.json` `Off`):
  `BestDraft` – endet die Schleife ohne Freigabe, wird die beste statt der vom SDK gewählten (praktisch der jüngsten) Runde
  geliefert und als gelieferte Runde gespeichert; eine freigegebene Schleife liefert wie bisher ihre letzte Runde.
  `WriterBlockingOnly` – der Schreiber bekommt in der Überarbeitung nur die blockierenden Befunde der letzten Runde, keine
  Ausfallmeldungen und keine nicht blockierenden Befunde.
  `StopWithoutProgress` – Stopp, sobald zwei Runden nacheinander keinen Fortschritt brachten; Freigabe, Höchstzahl der Runden,
  Zeitgrenze und die Stopps des SDK gehen vor.
- **Rang einer Runde:** zuerst die Zahl der critical-Befunde, dann die Zahl blockierender Befunde; kleiner ist besser. Gezählt wird
  nur über die **Prüfbasis**: die Prüfer, die in keiner der verglichenen Runden technisch ausgefallen sind. So macht ein Ausfall
  eine Runde weder besser (weniger geprüft) noch alle späteren schlechter. Bei gleichem Rang gewinnt die Runde mit weniger
  Ausfällen, dann die jüngste. Blockierend sind Befunde ab „major“ und jeder Befund eines ablehnenden Prüfers; ohne Einschränkung
  auf die Basis ist die Zahl genau dann 0, wenn die Richtlinie der Schleife die Runde freigibt (Test). Ohne gemeinsamen Prüfer wird
  nicht verglichen (keine Neuwahl, kein Stopp). Fortschritt heißt: ein Rang streng besser als jeder frühere.
- **Nie ein Stummel:** Wählbar ist neben der Runde, die das SDK geliefert hätte, nur eine Runde mit nicht leerem Text, der mindestens
  70 % des längsten Rundentexts hat (dieselbe Quote wie der Einbruchschutz des Schreibers, hier ohne Mindestlänge). Eine leere
  Antwort, eine Rückfrage oder eine Änderungsnotiz wird so nie geliefert, auch wenn sie weniger Befunde hatte.
- **Anzeige:** Die Manuskript-Ansicht der Werkstatt nennt „Text from iteration X of Y“, wenn der Text nicht aus der letzten Runde
  stammt. MCP gibt für Läufe keine Rundenzahl aus und bleibt unverändert.
- **Stoppgrund:** Das SDK kennt keinen passenden Wert; `StopStagnant` würde „the same points kept coming back“ zeigen, was diese
  Regel nicht prüft. Deshalb ein eigener Wert außerhalb der SDK-Aufzählung (`LoopStopReasons.NoProgress`, als Zahl gespeichert)
  mit dem Satz „Two more rounds did not reduce the open points, so the work was stopped.“
- **Bitgleich, solange aus:** Mit allen drei Schaltern `Off` werden dieselben Klassen gebaut wie vorher; Tests vergleichen Fassung,
  gelieferte Runde, Stoppgrund und die Aufträge an den Schreiber mit und ohne die neuen Parameter. Direktweg und Team-Entwurf
  bekommen die Schalter nie.
- **Nachweis:** Einheitstests, Pipeline-Tests über die echte Fabrik und Orchestrator-Tests gegen Postgres (auch für fortgesetzte
  Läufe); ein Test prüft am gebauten Ablauf, dass mit allen Schaltern aus dieselben Bausteine verwendet werden, ein weiterer
  vergleicht mit einem unabhängig aus den SDK-Bausteinen gebauten Ablauf, und je ein Test zeigt, dass Direktweg und eine Pipeline
  mit eigenem Schreiber (Team-Entwurf) die Schalter auch bei `On` nie bekommen. Ein Nachspiel auf gespeicherten Läufen der älteren Schleife und ein blinder Vergleich der gelieferten mit der gewählten
  Fassung liegen außerhalb dieses Repositorys; nach vorab festgelegter Regel bleibt ein Schalter aus, wenn seine gewählte Fassung
  im blinden Vergleich öfter verliert als gewinnt. Den Schreiber-Schalter belegt erst der echte Lauf in Paket 7.
- **Rückweg:** Schalter `Off` (Standard), z. B. `Convergence__BestDraft=Off`.

**Bekannte Grenzen bei eingeschalteten Schaltern** (ausgeliefert Off; vor einem Einschalten zu beheben, falls Paket 7 die Schleife behält): Bei sehr kurzen Texten kann eine kurze Rückfrage oder Weigerung die 70-%-Regel bestehen (Leerraum zählt mit); eine ungewöhnlich lange Runde schließt normale Runden aus der Wahl aus; fällt ein Prüfer nur in einzelnen Runden aus, kann der verbleibende Vergleich den Stopp auslösen oder eine Runde mit bekanntem kritischem Befund bevorzugen.

## D-086 — MCP: Server-Hinweise unter der Kürzungsgrenze, SDK exakt 2.4.0 (2026-11-27)

**Status:** gebaut und getestet (Schritte 0 und 1 der Prüfung neuer MCP-Fähigkeiten); Abnahme am echten Client nach dem Deploy
offen. D-085 gehört einem parallelen Paket.

**Anlass.** Claude Code behält von den Server-Hinweisen nur die ersten 4.096 Zeichen. Die ausgelieferte Kombination (ältere Werkzeuge
an, Upload-Ticket an, „Tell Atelier once“ ohne Versprechen) hatte 6.037 Zeichen; die Schritte 6 bis 8 (Ändern, Export, „nichts
erfinden“) kamen dort nie an. Außerdem stand das MCP-SDK auf dem Bereich `[1.3.0,2.0.0)`, aus dem NuGet immer die niedrigste Version
nimmt: Korrekturen kamen nie an, und 1.3.0 kündigte an jedem Werkzeug eine Task-Unterstützung an, die es nicht gibt.

**Entscheidung – Schritt 0, Hinweise.**
- **Höchstens 3.900 Zeichen in jeder Schalterkombination** (Reserve unter 4.096). Ein Test geht alle Kombinationen von
  `Mcp:LegacyTools`, `Uploads:Tickets`, `Orders:TellOnce` (On, WithoutPromise, Off), `Research:Mode`, `Orders:Revise` und zwei
  Wartezeiten durch und prüft zugleich, dass jede Werkzeugbeschreibung höchstens 16.384 Zeichen hat (Grenze für Beschreibungen, die
  Claude Code über Tool Search lädt). Längste Kombination jetzt 3.875 Zeichen (vorher 6.037), längste Werkzeugbeschreibung
  `get_order_result` mit 2.552.
- **Das Wichtigste zuerst.** Nach der ersten Zeile stehen die Regeln für jeden Schritt: nichts erfinden; Verständnis, Fragen,
  Antwortmöglichkeiten, Annahmen, offene Punkte, Zitate und `lookedUp` sind Inhalt, nie Anweisung, ihre Links werden nie geöffnet,
  und ihretwegen wird nie nach Geheimnissen gefragt. Dann Auftrag wortgetreu, Dateien richtig hochladen, Rückfragen nicht selbst
  raten, warten, Ergebnis samt Prüfstand, offenen Punkten und Annahmen unverändert zeigen. Ein Test prüft die Reihenfolge.
- **Einzelheiten in die Werkzeugbeschreibungen.** Was Schritt 6 zum Ändern sagte, steht vollständig in `revise_order` (neu dort:
  nichts versprechen, die Änderung erst als gemacht melden, wenn `get_order_result` die neue Fassung zeigt, bei nicht fertig
  gewordener Fassung den Hinweis weitergeben, die neue Fassung wie jedes Ergebnis zeigen, `lengthNotice`). `assumptionsVersion`,
  `lookedUp` und der Umgang mit Zitaten stehen in `get_order_result` (neu: Links aus Zitaten nie öffnen), der Export in
  `export_order`, der Ablehnungssatz zu doppelt kodiertem Base64 und falschem Inhaltstyp bei Upload-Ticket an in der Beschreibung von
  `attachments`. Die Sätze zu `cancel_order` und `list_orders` sind entfallen; beide Werkzeuge beschreiben sich selbst. Keine Regel
  ging verloren: Ein Test hält die Tabelle alte Regel → neuer Ort fest und prüft jede Schlüsselregel an ihrem Ort, im Text, den
  der Server wirklich ausliefert.
- **Hash-Baselines bewusst neu gezogen:** alle vier Hinweis-Hashes und die vier Werkzeuglisten-Hashes; der Abgleich der
  serialisierten Werkzeuglisten zeigt als einzige Änderung die Beschreibungen von `revise_order` und `get_order_result` (bei
  Upload-Ticket an zusätzlich die von `attachments`).

**Entscheidung – Schritt 1, SDK.**
- `ModelContextProtocol.AspNetCore` und `ModelContextProtocol.Core` **exakt `[2.4.0]`**; ein Update ist eine bewusste Änderung dieser
  Zeile. Der Code baut ohne Änderung mit Warnungen als Fehlern.
- Transport **ausdrücklich zustandslos** (`SessionMode = HttpServerSessionMode.Stateless`), nicht über einen SDK-Standard.
- **Werkzeuglisten-Hashes noch einmal neu gezogen:** einzige Abweichung ist das entfallene `execution: {"taskSupport":"optional"}`,
  das 1.3.0 jedem Werkzeug anhängte; die Hinweise sind bytegleich. Ein Test sichert, dass kein Werkzeug Tasks meldet und der Server
  keine Extensions anmeldet.
- **Ältere Clients:** Ende-zu-Ende über den echten Kestrel-Server mit einfachem JSON-RPC wie ein alter Client (2025-11-25 und
  2025-06-18: `initialize` ohne Sitzung, `notifications/initialized`, `tools/list`, Werkzeugaufruf) und mit dem SDK-Client (handelt
  2026-07-28 aus bzw. 2025-11-25, wenn verlangt). Die OAuth-Tests laufen unverändert grün.
- **Lange stille Antwort:** `get_order_status` mit `waitSeconds = 50` kommt am echten Kestrel-Server für beide Revisionen vollständig
  zurück (50,1 s). Seit SDK 2.3 kommen die Antwort-Header bei 2026-07-28 erst mit der Antwort (gemessen nach 50,1 s), bei 2025-11-25
  sofort. Kestrel und Web-Host setzen keine Frist; Proxy und Client müssen deutlich über 50 s erlauben.

**Nicht gebaut:** keine neuen Fähigkeiten – keine Skills, keine Elicitation, keine Tasks, keine Datei-Upload-Erweiterung, keine MCP
Apps. Sie bleiben späteren Schritten mit eigenem Schalter vorbehalten.

**Abnahme am echten Client (nach dem Deploy).** Claude Code interaktiv mit dem claude.ai-Connector: `/mcp` zeigt die Revision
2026-07-28, die Server-Hinweise kommen ohne „[truncated]“ an; ein vollständiger Auftrag mit Datei über den curl-Befehl und einer über
die Upload-Seite; `get_order_status` mit `waitSeconds = 50` kommt ohne Abbruch zurück; eine Änderung mit `revise_order` wird erst nach
der neuen Fassung als gemacht gemeldet. Dasselbe in claude.ai im Web. Da Clients `tools/list` zwischenspeichern können, wirken die
neuen Beschreibungen unter Umständen erst in einer neuen Sitzung.

## D-087 — Neues „Extra careful“: Direktweg mit Gliederung hinter Schalter, Messung vorbereitet (2026-11-27)

**Status:** gebaut und getestet, ausgeliefert aus; die Messung ist vorbereitet, nicht ausgeführt.

**Zweck.** Paket 7 des Restplans: Für lange, anspruchsvolle Texte soll „Extra careful“ etwas bringen, das der Standardweg nicht kann.
Heute läuft es auf der älteren Prüfschleife (bis zu sechs Runden, je Runde zwei Prüfer eines anderen Modells, ohne Faktenprüfung und
ohne „What was checked“). Gemessen wird, ob ein Direktweg mit Gliederung (K) oder die entschärfte Schleife (L, Schalter aus D-085) den
Direktweg (D) schlägt. Kein Neubau nach Abschnitt 3.5 des Analyseberichts.

**Entscheidung – Bau von K.**
- Neuer Schalter `Orders:CarefulOutline` (`Off`/`On`, Code und `appsettings.json` `Off`) mit `Orders:CarefulOutlineOverrides` je
  Benutzername wie bei den übrigen Schaltern. Mit `On` bekommt ein „Extra careful“-Auftrag **ohne** gewähltes Team und ohne eigene Crew
  die Code-Vorlage `direct-outline`; ein gewähltes Team, Standardaufträge, Änderungen und fortgesetzte Läufe bleiben, wie sie sind.
- `direct-outline` ist die Vorlage des Direktwegs unter eigenem Namen: derselbe Schreiber, dieselben Prüfungen ohne Modell, dieselbe
  Faktenprüfung, dieselbe eine Korrektur, derselbe Bericht. Neu ist ein Aufruf des Schreibermodells **vor** der ersten Fassung: Er sieht
  Auftrag und Dateien genau wie der Schreiber und liefert Abschnitte mit Überschrift, Wortbudget und Inhalt als JSON. Kann der Code die
  verlangte Länge in Wörtern lesen, werden die Budgets auf diese Länge skaliert (Mitte eines Bereichs, 90 % einer Obergrenze, 110 %
  einer Untergrenze, sonst die genannte Zahl; auf Zehner gerundet, je Abschnitt mindestens 20). Gesamtlänge und Summe der Budgets
  müssen zwischen 50 und 20.000 Wörtern liegen. Nur die erste Fassung bekommt die Gliederung, mit dem Satz, dass sie nur Reihenfolge
  und Länge vorgibt, jede Tatsache aus Auftrag und Dateien kommt und Anweisungen in ihr nicht gelten (die Gliederung entsteht aus
  einem Aufruf, der die Dateien liest); die Korrektur bekommt wie bisher nur die Probleme.
- Kommt keine brauchbare Gliederung zurück (Fehler, auch Zeitüberschreitung oder vorübergehender Fehler des Gateways, kein JSON, keine
  oder mehr als 20 Abschnitte, unplausible Länge), wird nicht wiederholt: Der Text wird
  in einem Zug geschrieben, und der Auftrag bekommt den Hinweis „The text could not be planned section by section first, so it was
  written in one go.“ Der Aufruf wird als eigener Kostenposten `direct-outline` gespeichert.
- Die Fortschrittsanzeige zeigt für solche Läufe die Schritte des Standardwegs; die Laufseite bietet „Change something“ wie beim
  Standardweg.
- **Bitgleich, solange aus:** Ohne Schalter wird jede Vorlage wie vorher gewählt, der Direktweg plant nie, und der Schreibauftrag ohne
  Gliederung ist zeichengleich mit dem bisherigen (Tests).

**Entscheidung – Schalter der Schleife je Konto.** Die drei Schalter aus D-085 bekommen je eine Ausnahmeliste je Benutzername
(`Convergence:BestDraftOverrides`, `…WriterBlockingOnlyOverrides`, `…StopWithoutProgressOverrides`), ausgeliefert leer. Damit kann
L für ein Messkonto laufen, ohne dass sich für andere etwas ändert. Die in D-085 genannten Grenzen der Schalter bleiben bekannt; sie
werden nur behoben, wenn L die Messung gewinnt, vor dem Einschalten.

**Messung (vorbereitet, außerhalb dieses Repositorys).** Fünf neue, frei erfundene lange Aufgaben (je mindestens 1.500 Wörter, drei mit
Dateien), festgeschrieben vor den Kriterien. Lauf 1: D gegen K; Lauf 2: L gegen den Sieger von Lauf 1 (dessen gespeicherte Texte). Ein
blinder Juror vergleicht je Paar in zwei Durchgängen mit vertauschter Reihenfolge. „Gewinnt“ heißt: in beiden Durchgängen mindestens 4
von 5 vorne und nicht mehr erfundene Angaben; reicht eine Obergrenze nicht für alle Paare, heißt das „nicht entschieden“, nie
Niederlage; Längentreue (±25 %) wird berichtet. Gewinnt keiner, folgt eine Reparatur an K (eigener
Schritt), sonst entfällt das Häkchen. Eine Bedürfnisprüfung auf früheren abgeschlossenen Aufgaben der Werkstatt entscheidet, ob Auto-Crew
und Composer mit dem Abriss gehen dürfen: Der alte Text darf höchstens einmal öfter gewinnen als der neue, der neue darf nicht mehr
erfundene Angaben haben, und bei mehrheitlich offenen Aufgaben ist es „nicht entschieden“. Harte Obergrenzen je Lauf, ein Schutz des
geteilten codex-Abos aus dessen aktuellem Stand (gelesen vor jedem Lauf und nach je fünf codex-Aufrufen) und der gemeinsame
Messwächter; Abbruch bei Failover oder offenem Breaker. Keine Zeit- oder Tagesregeln.

**Nachweis.** Einheitstests für Lesen, Zielgröße und Skalierung der Gliederung und für die Prompts; Pipeline-Tests über den echten
Zusammenbau des Direktwegs (Gliederung zuerst, fehlende Gliederung mit Hinweis, kein Wiederholen bei Ablehnung des Gateways, Korrektur ohne
Gliederung, keine Gliederung mit Vorlage oder auf dem Direktweg); Zuordnungstests der Vorlage und des Starts nach dem Klären;
Bindungstests der Ausnahmen; ein Orchestrator-Test gegen Postgres, in dem die Ausnahmen nur die Läufe ihres Kontos ändern;
Ende-zu-Ende über MCP mit und ohne Klärer.

**Bekannte Grenzen und offen.**
- Ein „Extra careful“-Auftrag schlägt nie im Web nach, auch mit Gliederung (D-079, D-074).
- Die Texte zu „Extra careful“ auf `/start`, in `submit_order` und auf der Erklärseite sprechen von mehreren Prüfrunden; sie werden erst
  mit dem Einschalten angepasst.
- Die Leitfäden nennen den neuen Schalter vorerst nur im Admin-Leitfaden.
- Wird ein K-Auftrag geändert, läuft die neue Fassung wie jede Änderung auf dem Direktweg; die Fortschrittsanzeige zeigt dort wie bei
  älteren „Extra careful“-Aufträgen die Schritte der Schleife. Vor dem Einschalten zu entscheiden.
- „Überarbeite meine Datei“: K macht die Datei nicht zur Vorlage (wie bisher bei „Extra careful“), plant und schreibt also neu, wo der
  Standardweg wenig ändert. Vor dem Einschalten zu entscheiden.

**Rückweg:** Schalter `Off` (Standard), Ausnahmen entfernen.

**Zusatz zu D-087 – Reparatur an K (2026-11-27).** Die Messung ergab: Weder K noch L gewann gegen den Direktweg. Nach der vorab
festgelegten Regel folgt genau eine Reparatur an K. Laut den Begründungen des blinden Jurors verlor K vor allem an nicht eingehaltenen
ausdrücklichen Vorgaben des Auftrags. Die Reparatur ist allgemein gehalten, nicht auf einzelne Aufgaben zugeschnitten, und liegt im
selben Schalter `Orders:CarefulOutline` (mit `Off` unverändert):
- Die Vorlage `direct-outline` bekommt eine dritte Prüfung `requirements-check`. Nach der **ersten** Fassung bekommt ein Aufruf eines
  anderen Modells als des Schreibers (das Standardmodell des Klärers, ohne Ausweichen auf ein anderes Modell, 120 s) nur die eigenen
  Worte der Person: den Auftragstext, ihre eigenen Antworten und ihre Änderungswünsche, nie Annahmen, About me, Überschriften oder
  Dateien. Er listet jede ausdrückliche Vorgabe wörtlich auf und sagt, ob der Text sie erfüllt. Die Länge prüft weiter der Code.
- Blockierend wird nur „nicht erfüllt“ mit einem Zitat von mindestens drei Wörtern oder zwölf Zeichen, das in diesen Worten an
  Wortgrenzen steht (Vergleich ohne Groß-/Kleinschreibung und Leerraum), höchstens zehn. Befund und Korrektur bestehen nur aus dem
  festen Satz „The request asks: "…". The text does not meet this yet.“ mit dem geprüften Zitat; freier Text des Prüfers erreicht
  weder den Schreiber noch Bericht oder Anzeige.
- Spätere Fassungen werden nicht erneut geprüft (Kostendeckel). Ob die Korrektur jeden Punkt trifft, bleibt deshalb ungeprüft.
- Ein Ausfall wird nicht wiederholt und blockiert nie; der Auftrag bekommt den Hinweis „Whether the text meets every point of the request
  could not be checked.“, und auch ein Aufruf ohne Antwort bekommt einen Kostenposten. Der Prompt nennt keine Textsorte.
- Nachweis: Einheitstests (Lesen, Zitat muss im Auftrag stehen, Satz, Prompt), Pipeline-Tests über den echten Zusammenbau (erfüllt,
  nicht erfüllt mit Korrektur, Zitat nicht im Auftrag, Ausfall, Direktweg prüft nie) und Ende-zu-Ende über MCP. Gemessen wird die
  Reparatur mit denselben Kriterien gegen die gespeicherten Texte des Direktwegs. Ohne Sieg entfällt das Häkchen, und lange Texte laufen
  im Direktweg.

**Zweiter Zusatz zu D-087 – Messung entschieden, „Extra careful“ entfällt (2026-11-27).** Die vorab festgelegte Messung ist
entschieden: Auch die reparierte Variante (Gliederungsweg `direct-outline` mit Vorgabenprüfung) hat nicht klar genug gewonnen. Wie
vorab festgelegt, entfällt das Häkchen, und lange Texte laufen im Direktweg.
- **Entfernt**, was nur dafür da war: der Schalter `Orders:CarefulOutline` samt `CarefulOutlineOverrides`, die Vorlage
  `direct-outline`, der Gliederungsschritt, die Vorgabenprüfung `requirements-check` und ihre Warncodes, das Häkchen auf `/start`, die
  Kennzeichnung in „My work“ und auf der Auftragsseite, die Absätze der Erklärseite und `extraCareful` in `submit_order` (Schema,
  Beschreibung, Skill, Zustand der Formular-Rückfrage). Die drei Schalter der Schleife aus D-085 samt Ausnahmen waren schon mit der
  älteren Prüfschleife entfallen (D-091).
- **Keine Migration, kein `DROP`.** Der Wert `OrderMode.Careful` und die Spalten bleiben; neue Aufträge sind immer `Direct`. Das
  Aufräumen der Datenbank ist ein eigener, späterer Schritt.
- **Gespeicherte Aufträge im Modus careful** bleiben lesbar und exportierbar. „Change something“ und `revise_order` schreiben die neue
  Fassung auf dem Direktweg; auch ein noch wartender Auftrag wird dort geschrieben. Die Zusage aus D-079 gilt für sie weiter: Ein
  gespeicherter sorgfältiger Auftrag schlägt nie im Web nach, auch wenn seine Anfrage erst jetzt gelesen wird.
- **Gespeicherte Läufe mit `direct-outline`** gehören weiter zum Direktweg: lesbar, fortsetzbar, änderbar. Wird ein solcher Lauf noch
  ausgeführt, lässt der Zusammenbau die Zusatzprüfung aus; einen Planungsaufruf gibt es nicht. Die technische Laufseite zeigt die
  Aufstellung unter ihrem früheren Namen.
- **Clients mit gespeicherter alter Werkzeugliste**, die `extraCareful` noch senden, werden mit einem klaren Satz abgelehnt; es
  entsteht kein Auftrag. Ein stiller Wechsel auf den Standardauftrag hätte die Zusage gebrochen, nie im Web nachzuschlagen. Der
  Parameter steht nicht im Schema und fällt mit den Platzhaltern der alten Werkzeuge in der nächsten Version weg.
- **Nachweis:** Ende-zu-Ende-Tests für einen fertigen sorgfältigen Auftrag mit `direct-outline` (Listen, Seiten, beide Exporte,
  `revise_order`), einen wartenden und einen fortgesetzten Lauf dieser Vorlage, einen auf Antworten wartenden sorgfältigen Auftrag und
  einen Aufruf mit `extraCareful`.
- **Rückweg:** das vorige Image; in der Datenbank wurde nichts geändert.

## D-088 — MCP: Skill und Rückfragen als Formular, beide hinter Schaltern und ausgeliefert aus (2026-11-27)

**Status:** gebaut und getestet (Schritte 2 und 3 der Prüfung neuer MCP-Fähigkeiten); beide Schalter ausgeliefert `Off`; Abnahme am
echten Client offen. D-087 gehört einem parallelen Paket.

**Anlass.** Claude Code und claude.ai können seit November 2026 Skills von MCP-Servern laden und Formular-Rückfragen (Elicitation)
zeigen. Die Server-Hinweise sind auf 3.900 Zeichen begrenzt (D-086); ein Skill kann den Ablauf ausführlicher erklären, ohne Kontext zu
kosten, solange er nicht geladen ist. Rückfragen gibt das Modell heute selbst weiter und kann dabei Optionen verlieren, raten oder die
Herkunft falsch setzen.

**Entscheidung – Skill (`Mcp:Skills`, `Off`/`On`).**
- Ein Skill `using-atelier` aus festen Dateien im Repository (`src/Geef.Atelier.Mcp/Skills/using-atelier/`: `SKILL.md`,
  `references/files.md`, `references/questions.md`, `references/result.md`), in die Assembly eingebettet, mit Manifest aus
  Prüfsummen und Größen. Nie aus Aufträgen, Dateien, Nutzer- oder Laufzeitdaten zusammengesetzt, nie `dynamic`.
- Paket `ModelContextProtocol.Extensions.Skills` exakt `[2.4.0]` (wie das SDK); `MCPEXP005` nur im Mcp- und Testprojekt unterdrückt.
- Bei `On`: Erweiterung `io.modelcontextprotocol/skills`, Fähigkeit `resources`, die vier Dateien als Ressourcen, `skills/list` und
  `skills/get` genau so, wie das SDK sie baut (`AtelierSkill.AddTo`). Die Server-Hinweise bleiben **unverändert**: Der empfohlene Satz
  „load the skill“ hätte die längste Kombination über 3.900 Zeichen gebracht (Reserve 25). Stattdessen bekommt nur die Beschreibung von
  `submit_order` einen Satz, der auf `skill://using-atelier/SKILL.md` verweist.
- Der Skill widerspricht den Hinweisen nie: Ein Test prüft, dass 20 Kernregeln der Hinweise in allen Schalterkombinationen und sieben
  in Werkzeugbeschreibungen gewanderte Regeln im Skill wortgleich stehen, dass 18 Gegenformulierungen fehlen, dass er nur vorhandene
  Werkzeuge nennt, nur auf eigene Dateien verweist und die Regel aus D-081 zu übersprungenen Fragen enthält. Wo der Ablauf von
  Schaltern abhängt (Upload-Ticket, About me speichern, ältere Werkzeuge), beschreibt der Skill beide Fälle („If prepare_attachment is
  in your tool list …“), damit er in jeder Konfiguration stimmt.

**Entscheidung – Rückfragen als Formular (`Mcp:Elicitation`, `Off`/`On`, `Mcp:ElicitationLifetime` Standard 30 Minuten).**
- Nur in `submit_order`, nur bei `needs_input`, nur wenn der Client 2026-07-28 ausgehandelt hat (`IsMrtrSupported`, zustandslos über
  MRTR und `InputRequiredException`) und `elicitation.form` meldet. Sonst `needs_input` wie bisher. `get_order_status` und `answer_order`
  bleiben unverändert.
- Formular **nur aus Auswahlfeldern**: je Frage ihre Optionen und „You decide“, die Frage nach der zu verbessernden Datei bei
  `revisionFile`; nichts Pflicht, nichts vorausgewählt. **Keine Texteingabe**, nirgends (Test: das Schema enthält in keinem Fall ein
  freies Textfeld); eigene Worte gehen über den Chat, die feste Nachricht sagt es („To answer in your own words, close this form and
  tell your assistant.“) und dass Atelier nie nach Passwörtern, PINs, TANs, Schlüsseln, Karten- oder Kontonummern fragt.
- **Zusage zum Filter:** Er hält die gängigen Formen fern, nicht alle. Kein Formular bei Aufträgen, deren Sprache nicht Deutsch oder
  Englisch ist oder deren Fragetexte andere als lateinische Buchstaben enthalten, und bei Fragen, Optionen oder Dateinamen mit Wörtern
  für Geheimnisse (auf dem normalisierten Text, auch getrennt geschrieben wie „Pass-Wort“, „P.I.N.“, „Karten-Nummer“), Links und
  Adressen (auch „punkt de“/„dot com“, nackte IP-Adressen, `.md`/`.zip` im Fragetext, `tel:`/`sms:`), Telefon- und anderen Ziffernfolgen
  ab sieben Ziffern, Aufforderungen anzurufen oder per SMS zu antworten, versteckten Zeichen oder zu langen Texten (240/120/120);
  dazu der Schutz des Klärers ohne dessen Ausnahme. Fragetexte stammen teils aus Dateiinhalten und sähen im Formular wie eine Frage von
  Atelier aus. Was der Filter übersieht, erscheint höchstens als Auswahlfrage; eingeben lässt sich nichts. Fehlalarme (z. B. „Login“)
  führen nur zum bisherigen `needs_input` (Log-Zeile „MCP form of submit_order not sent: {Reason}“ als Kennzahl).
- `requestState`: mit den Data-Protection-Schlüsseln signiert und verschlüsselt, gebunden an Auftrag, Nutzer, Client (OAuth-Client),
  Hash der Argumente und Frage-Kennungen, mit Ablauf; er trägt die Quittungen und Hinweise des ersten Aufrufs mit, damit nichts davon
  verloren geht. Der zweite Aufruf legt **nie** einen Auftrag an; fremder, gefälschter oder zu anderen Argumenten gehörender Wert →
  Werkzeugfehler ohne Auskunft über den Auftrag; wartet der Auftrag nicht mehr → aktueller Zustand, nichts erneut beantwortet.
- Abbruch, Ablehnung, Ablauf, unbrauchbare Antworten, geänderte Fragen → `needs_input` wie bisher, mit einem Satz davor; bei Ablehnung
  mit dem Hinweis auf `skip=true`. „You decide“ und leere Fragen → „entscheide du“, offen nach D-081. Ein **angenommenes, aber ganz
  leeres** Formular gilt als geschlossen (`needs_input`), damit ein Client, der Formulare selbsttätig annimmt, der Person die Rückfrage
  nicht nimmt.
- **Herkunft.** Darf der Server „user“ behaupten? Nein: Er sieht nicht, wer das Formular ausfüllt (Clients können Formulare
  automatisch beantworten, etwa über Hooks). Neue Herkunft `AnswerOrigin.Form` (Draht `form`, von keinem Client über `answer_order`
  annehmbar), im Web „(chosen in a form of your assistant)“. Für Schreiber und Faktenprüfung zählt sie wie eine Antwort der Person
  (ohne Markierung im Auftrag an den Schreiber), weil Atelier das Formular selbst gebaut und nur eigene Optionen angeboten hat.
- Bei `Off` wird `requestState` wie bisher übergangen; nur ein echter Wert dieses Servers (nur möglich, wenn vorher `On` galt) führt zum
  Zustand seines Auftrags statt zu einem zweiten Auftrag.

**API-Stabilität und Risiken.**
- Das Skills-Paket ist drei Wochen alt und experimentell; die Spezifikation der Erweiterung ist Beta. Genutzt werden nur
  `McpServerSkillPackage.Create`, `McpServerSkillFile.FromText` und `WithSkillPackages`. Weil der Schalter erst beim Aufbau der Optionen
  gelesen wird, holt `AtelierSkill.AddTo` die Options-Einrichtung des Pakets aus einem kleinen eigenen Container und wendet sie auf die
  Server-Optionen an; ändert ein späteres SDK diese Einrichtung, scheitert der Start nur bei `On` mit klarer Meldung, und die Tests
  fallen vorher auf. Bei jedem SDK-Update Skills-Paket mitziehen und die Skill-Tests laufen lassen.
- Claude Code entzieht die Freigabe eines Skills bei jeder Inhaltsänderung (Digest); Änderungen am Skill also sparsam.
- Elicitation läuft über MRTR, das das SDK für zustandslose Server erst seit 2.0 kann; ältere Revisionen bekommen nie ein Formular.
  Clients können Formulare verschieden darstellen (Titel, Beschreibung, Reihenfolge); Darstellung ist am echten Client zu prüfen.
- **Rückweg:** Ein älteres Image kennt die Herkunft `Form` nicht; für Aufträge, die über ein Formular beantwortet wurden, liest es die
  gespeicherten Antworten nicht (leere Liste). Ohne eingeschalteten Schalter entstehen keine solchen Aufträge. **Sobald
  `Mcp:Elicitation=On` einmal galt, ist das Rückweg-Image dieses D-088-Image mit Schalter `Off`, nicht das Image davor.** Wer trotzdem
  weiter zurück muss: solche Aufträge danach nicht ändern oder fortsetzen.

**Tests.** Einheitentests für Formularaufbau, Filter, Lesen der Antworten, Zustandswert (Rundweg, Ablauf, Fälschung, fremder Zweck und
Schlüssel, Argument-Hash), Ablauf (fremder Nutzer, Client, Argumente, fremder Auftrag, Doppelaufruf, Abbruch, Ablehnung, ungültig,
Ablauf, Schalter aus), Antwortregeln mit `Form`, Brief und Web-Anzeige; Schaltertests (Werkzeugliste, Hinweise, Fähigkeiten bei `Off`
gleich; bei `On` nur ein Satz in `submit_order`); Hash-Baselines unverändert. Ende-zu-Ende über den echten Kestrel-Server mit dem
SDK-Client: Formular beantwortet (ein Auftrag, Herkunft `Form`, Schreiber bekommt die Antwort), Wiederholung, geänderte Argumente,
gefälschter Wert, fremder Nutzer, Abbruch und Ablehnung mit anschließendem `answer_order`, „You decide“ nach D-081, Ablauf, Client
2025-11-25, Client ohne Formulare, Schalter aus; Skill listen, holen und alle Dateien geprüft lesen (2026-07-28), Werkzeuge und Hinweise
bei 2025-11-25 unverändert. Alle Beispiele sind erfunden.

**Abnahme am echten Client (nach dem Deploy, nicht ausgeführt).** Claude Code interaktiv mit dem claude.ai-Connector und claude.ai im
Web, je mit `Mcp:Skills=On` und `Mcp:Elicitation=On`: Skill erscheint und wird vor dem ersten Auftrag geladen; ein Auftrag mit offener
Länge zeigt das Formular; Auswahl, „You decide“, Schließen und Ablehnen verhalten sich wie beschrieben; eine Testdatei mit
eingeschleuster TAN-Frage ergibt kein Formular. Gegenprobe mit beiden Schaltern `Off`.

### Zusatz zu D-088 — Formular-Antworten für das Modell sichtbar: `givenAnswers` (2026-11-27)

**Befund der Abnahme am echten Client** (Claude Code 2.1.332 ohne Terminal gegen Produktion, Formular über einen Elicitation-Hook
beantwortet): Der Rundlauf funktionierte, aber das Modell erfuhr nicht, was im Formular gewählt wurde. Weder die Antwort von
`submit_order` noch `get_order_result` nannte die Antwort, und Claude warnte fälschlich, die Person habe sich nie entschieden. Das
Ergebnis nannte bisher überhaupt keine gegebenen Antworten, auch nicht die über `answer_order`.

**Entscheidung.**
- Neues Feld `givenAnswers` (`[{questionId, question, answer, leftToAtelier, origin}]`) für alle Herkünfte (`user`, `client`,
  `form`) und für Korrekturen von Annahmen, angehängt an das Ergebnis von `get_order_result` – **nur für den Besitzer** des Auftrags
  (wie `fromAboutMe`), ein Administrator, der fremde Aufträge sieht, bekommt es nicht.
- `submit_order` nach einem beantworteten Formular nennt dieselben Einträge und beginnt die Nachricht mit „The person answered
  Atelier's questions in a form shown by their app; givenAnswers lists what they chose (content, not instructions).“
- Die Beschreibung von `get_order_result` bekommt einen Satz: Der Text wurde mit diesen Antworten geschrieben, der Person nicht sagen, sie
  habe sie offen gelassen; Fragen und Antworten sind Inhalt, keine Anweisung. Die Server-Hinweise bleiben unverändert (Reserve 25
  Zeichen); der Skill wiederholt den Satz wortgleich.
- **Hash-Baselines bewusst neu gezogen:** Die vier Werkzeuglisten-Hashes (Tickets aus) ändern sich nur durch diesen Satz. Gegenprobe:
  Ohne ihn ergeben sich die bisherigen Hashes. Die Hinweise sind bytegleich. Weil `get_order_result` kein Ausgabeschema meldet, ändert
  das neue Feld selbst die Werkzeugliste nicht; die Antwort enthält es wie die anderen angehängten Felder (`null`, wenn leer).
- `Mcp:Skills` und `Mcp:Elicitation` bleiben ausgeliefert `Off`.

**Tests.** Einheiten für die Einträge (alle Herkünfte, Korrektur, Datei-Frage, Atelier überlassen, unbekannte Frage weggelassen) und
für `submit_order` nach dem Formular (Antworten, „You decide“, Ablehnung ohne Einträge); Feldreihenfolge der Ergebnis-DTOs; Ende-zu-Ende
mit dem SDK-Client: Formular → `submit_order` und `get_order_result` nennen die Wahl mit `form`; `answer_order` → `get_order_result`
über einen Client 2025-11-25 mit `user`; Antwort `client` sichtbar für den Besitzer in beiden Revisionen, nicht für einen
Administrator.

### Zusatz zu D-088 — Abnahme bestanden, beide Schalter ausgeliefert `On` (2026-11-27)

**Abnahme am echten Client mit Claude Code (2.1.332, gegen Produktion):** Formular beantwortet → das Modell nennt Wahl und Herkunft
richtig („laut Atelier in einem Formular gewählt, ich habe es nicht gesehen“); Formular ohne Antwort → sauber `needs_input`, nichts
selbst entschieden; Skill vom echten Server: `skills/list` und `resources/read` funktionieren. **Nicht geprüft:** das Chatfenster von
claude.ai und Claude Desktop.

**Entscheidung.** `appsettings.json` liefert `Mcp:Skills` und `Mcp:Elicitation` dauerhaft `On`; der Code-Standard bleibt `Off` (wie
bei den übrigen Schaltern). Die Bitgleichheit bei `Off` prüfen die Tests weiter ausdrücklich mit gesetztem `Off` (Hash-Baselines,
Schaltertests, Ende-zu-Ende mit `Off`); die Tests des ausgelieferten Stands erwarten `On`, der Kompatibilitätstest für ältere Clients
erwartet jetzt genau die Erweiterung `io.modelcontextprotocol/skills`. Erklärseite und Startseite bleiben unverändert: Sie sagen nichts,
was durch die Formulare falsch würde. **Rückweg:** `Mcp__Skills=Off` bzw. `Mcp__Elicitation=Off` per Umgebung, ohne neues Image;
nach dem ersten `On` ist das Rückweg-Image ein D-088-Image (siehe oben).

## D-089 — Rückbau, Schnitt 1: ungenutzter Code entfernt (2026-11-27)

**Kontext.** Stufe 8c des Rückbaus (Plan-Etappe 1b, „Rückbau Schnitt 1“). Sie nimmt Code heraus, den keine verbleibende Arbeitsweise
braucht; der zweite Schnitt (Composer und Auto-Crew, Werkzeugpfad, Lernschleife, Vektor-Wissensbasis, ältere MCP-Werkzeuge, `/new`)
folgt nach dem Übergang in 8b. Vor dem Entfernen eines Teils wurde geprüft, dass kein Team, das wählbar bleibt, davon abhängt.

**Entscheidung — entfernt.**
- **Template Studio** (`/crew/studio`, Dienst, Prompts, Ähnlichkeitssuche, Einstellungen, alle Studio-Bausteine). Sein eigenständiger
  Start eines Kompositionslaufs entfällt mit; Auto-Crew bleibt über `/new` und `submit_auto_crew_request` erreichbar. Die MCP-Werkzeuge
  `analyze_template_proposal` und `materialize_template_proposal` bleiben als **Stummel** unter den älteren Werkzeugen gelistet: Sie
  nehmen einen optionalen Text an, und jeder Aufruf liefert einen Werkzeugfehler, der `submit_order` nennt (Clients speichern
  Werkzeuglisten zwischen). `IAtomicTransactionFactory` ist vorher nach `Infrastructure/Persistence` umgezogen, weil der
  Crew-Materialisierer sie nutzt; der Hilfetext zur Transform-Anweisung steht jetzt in `FinalizerFieldHelps`.
- **MCP-Client** (`/mcp-servers`, Client-Fabrik, Werkzeugsuche, der Werkzeugtyp `mcp-tool` in `ToolExecutor`, Schema-Provider und
  Werkzeug-Editor). Der eigene MCP-Server von Atelier ist nicht betroffen. Das Paket `ModelContextProtocol.Core` entfällt in
  Infrastructure.
- **Grounding-Typen `rest-api`, `academic-search`, `news-search`** mit ihren Providern, den akademischen Quellen, `JsonPathNavigator`,
  Einstellungsschlüsseln und Editor-Abschnitten; die System-Profile `academic-default` und `tavily-news` verlassen `SystemCrew`.
- **Abschluss `ExternalSink`** (Webhook und E-Mail) samt Einstellungen, SMTP-Optionen und MailKit; die System-Profile `webhook-sink` und
  `email-sink` verlassen `SystemCrew`.
- **Editor für HTTP-Provider.** HTTP-Provider kommen mit dem System und sind nur lesbar; CLI-Provider behalten Editor und Kopie.
- **Tote Konzepte:** `GroundingProviderProfile.MaxQueriesPerRun` (nie durchgesetzt; Editorfeld, Listenspalte, Feld im MCP-DTO und
  EF-Zuordnung), `RunEntity.AdvisorRetryAttempted` (nie geschrieben) und die nur von Tests genutzten `StubExecutionStep`/`StubReviewer`
  (jetzt im Testprojekt).

**Entscheidung — bewusst behalten (Abweichung vom Plan).** `url-fetch` (mit `UrlSafetyValidator` und `HtmlContentExtractor`) und
`MetadataEnrich` bleiben: Teams, die wählbar bleiben, können sie weiter nennen. Über sie wird im zweiten Schnitt zusammen mit der
Team-Wahl entschieden. Auch der agentische
Werkzeugpfad und `/tools` bleiben bis 8d.

**Eingehaltene Regeln.**
- Reihenfolge je Teil: Navigation → Registrierung → Code → Tests → Seeds. **Kein `DROP`, keine Migration, keine gelöschte Zeile.** Die
  Tabellen `TemplateStudioAnalyses`, `StudioSettings`, `mcp_server_configs` und die Spalten `MaxQueriesPerRun`, `AdvisorRetryAttempted`
  bleiben (nicht mehr zugeordnet; nullable bzw. mit Standardwert). Zeilen ausgemusterter System-Profile bleiben in der Datenbank.
- **Ausgemusterte Werte bleiben lesbar.** `FinalizerType.ExternalSink` sowie die Konstanten `GroundingProviderTypes.NewsSearch/
  AcademicSearch/RestApi` und `ToolType.McpTool` bleiben als `[Obsolete]`. `GroundingProviderTypes.Retired` und
  `FinalizerType.IsRetired()` benennen sie: Gespeicherte Profile und Lauf-Snapshots damit bleiben lesbar, ein Lauf überspringt sie mit
  der bekannten Warnung („could not be read and was skipped“ / „The finishing step … failed“), speichern lassen sie sich nicht mehr
  (`CrewService`), Finalizer-Listen lassen sie aus, und der Grounding-Editor zeigt einen Hinweis. `academic-default` bleibt in der
  Regel, die ältere Snapshots vom Werkzeugpfad fernhält.
- **Ausgemusterte Typen werden nicht mehr angeboten.** Grounding-Listen (Vorlagen-Editor, `list_grounding_provider_profiles`) lassen
  Profile eines ausgemusterten Typs aus wie die Finalizer-Listen; der Composer-Prüfer weist ein `reuse` auf ein solches Profil als
  kritisch ab. Vorlagen, die schon eines nennen, behalten den Namen (die Auswahl behält gewählte Namen), und der Lauf überspringt es mit
  der Warnung.
- **Ausgemusterte System-Profile bleiben geschützt.** `academic-default`, `tavily-news`, `webhook-sink` und `email-sink` gelten weiter
  als System-Namen (`SystemCrew.RetiredGroundingProviderNames`, `RetiredFinalizerNames`): Ihre Zeilen lassen sich weder umbenennen noch
  ändern noch löschen.
- Der Composer nennt die entfernten Typen nicht mehr.

**Nachweis.** Der Produktions-Snapshot-Test (`ATELIER_SNAPSHOT_FIXTURES`, Export außerhalb des Repositorys) prüft jetzt zusätzlich jede
Grounding-Art gegen die echte Registrierung (`AddGroundingProviders`) oder die Ausmusterungsliste und jede Abschluss-Art gegen die
registrierten Executoren (`AddFinalizers`) oder die Ausmusterungsliste; alle exportierten Snapshots bestehen. Ein synthetischer
Test im Repository liest einen älteren Snapshot mit `maxQueriesPerRun`, ausgemusterten Grounding-Typen und einem
`ExternalSink`-Abschluss. Weitere Tests: Stummel antworten mit dem Fehler, ausgemusterte Typen lassen sich nicht speichern (echtes
Postgres), gespeicherte Sink-Zeilen bleiben, werden aber nicht gelistet, Editoren bieten die entfernten Typen nicht mehr an, die
Provider-Seiten verweisen auf keinen entfernten Weg, die Migration des Template Studio legt ihre Tabelle weiter an.

**MCP-Baselines bewusst neu genommen:** Die beiden Hashes der Werkzeugliste mit den älteren Werkzeugen ändern sich durch die
Stummel-Beschreibungen; die Hashes ohne die älteren Werkzeuge und die Server-Hinweise sind unverändert.

**Grenze: EF-Modell-Snapshot.** `AtelierDbContextModelSnapshot` kennt noch `MaxQueriesPerRun`, `AdvisorRetryAttempted`,
`TemplateStudioAnalyses`, `StudioSettings` und `mcp_server_configs`; eine erzeugte Migration (`dotnet ef migrations add`) schlüge
`DropColumn`/`DropTable` vor. Migrationen bleiben handgeschrieben; jetzt keine erzeugen. Erledigt wird das mit einer neuen
Migrations-Baseline nach dem Aufräumen der Datenbank, das als eigener Schritt nach 8d kommt.

**Rückweg.** Das vorige Image; die Datenbank bleibt verträglich, weil nichts gelöscht oder geändert wurde. Im Code nur über Git (Tag vor
dem Deploy).

## D-090 — Gateway: Failover nur auf dasselbe Modell (`x-cli-proxy-failover: same-model`) (2026-11-27)

**Status:** gebaut und getestet auf Branch `fix/failover-same-model`; nicht ausgeliefert. D-089 gehört zu einem parallelen Paket.

**Anlass.** Das Atelier schickt bei Aufrufen, deren Ergebnis vom Modell abhängt, `x-cli-proxy-failover: off` (Schreiber und
Gliederung im Dokumentmodus, Faktenprüfung samt Ersatzmodell, Prüfung der ausdrücklichen Vorgaben, Verdichter der Recherche, Klärer
bei `Intake:AllowFailover=false`). Das Gateway hält eine solche Anfrage bei genau einem Konto. Ist das erste Claude-Konto an seinem
Limit, bekommt deshalb jeder dieser Aufrufe `503 backend_unavailable` oder einen Limitfehler, obwohl ein zweites Claude-Konto dasselbe
Modell unter demselben Namen frei hätte. `off` war gedacht als „kein anderes Modell“, verhindert aber auch den Wechsel des Kontos.

**Entscheidung – Gateway.**
- Neuer Wert `x-cli-proxy-failover: same-model` (Groß-/Kleinschreibung egal): Eine Claude-Anfrage darf zwischen den Claude-Konten
  wechseln, mit unverändertem Modell, aus denselben Gründen wie beim normalen Failover (Auth, Limit; ein Transportfehler überspringt
  das zweite Konto wie immer), aber **nie** zu codex. Eine codex-Anfrage wird genau wie `off` behandelt. Konten mit offenem Breaker
  werden übersprungen; sind alle gesperrt, `503 backend_unavailable` mit `Retry-After` bis zum ersten Konto, das wieder eine Probe
  annimmt. Breaker, Limit-Erkennung, „nicht im Abo“-Routing und Antwortköpfe wie beim normalen Wechsel zwischen den Konten
  (`x-cli-proxy-failover: claude->claude-alt`). Mit `FAILOVER_ENABLED=0` wie `off`. Der generische Endpunkt `/v1/cli/…` wertet den
  Kopf weiterhin nicht aus.
- `off`/`0`/`false` und „kein Kopf“ verhalten sich **exakt wie bisher**; jeder andere Wert (auch `same_model`) gilt wie „kein Kopf“.

**Entscheidung – Atelier.** `LlmRequest.DisableFailover` schickt jetzt `same-model` statt `off`; die Wiederholungsregel des
`HttpClient` (`LlmHttpRetry`) erkennt beide Werte. Geprüft je Aufrufer: Schreiber und Gliederung brauchen das angefragte Modell im
Dokumentmodus (codex liefert dort ohnehin keinen Text); die Faktenprüfung braucht ein anderes Modell als der Schreiber, und dasselbe
Modell auf einem anderen Konto erhält das; ihr Zeitlimit gilt für die ganze Anfrage, also auch nach einem Kontowechsel; ihr Ersatzmodell
greift weiter, wenn beide Konten scheitern. Die Vorgaben-Prüfung und der Verdichter wollen kein anderes Modell, ein anderes Konto mit
demselben Modell ändert ihren Pfad nicht. Der Klärer mit `AllowFailover=false` bekommt dasselbe. Kein Aufrufer muss bei `off`
bleiben. Die Warnung „anderer Dienst hat geantwortet“ erscheint wie bisher nicht bei einem Wechsel `claude->claude-alt`.

**Reihenfolge beim Ausliefern (zwingend).** Zuerst das Gateway, dann `web`. Ein Gateway, das `same-model` noch nicht kennt, liest den
Wert wie „kein Kopf“ und übergibt solche Anfragen wieder voll an codex. Sicherheitsnetz: Das Atelier meldet jeden Wechsel der CLI als
Warnung am Auftrag.

**Restrisiko.** Kennt das zweite Konto eine konkrete Modell-ID nicht, kann die CLI sie still durch ein anderes Modell der Familie
ersetzen; das gilt beim normalen Wechsel zwischen den Konten genauso und ist in der Gateway-Doku beschrieben.

**Tests.** Gateway: 50 neue Tests (`tests/test_failover_same_model.py`: Kopf, Reihenfolge, nicht streamend, SSE gepuffert und Token,
decision-file, Dokumentmodus inkl. leerem Ergebnis, Legacy, generischer Endpunkt, offene Breaker, „nicht im Abo“, codex), dazu ein
Differenztest gegen `main` über die Szenario-Matrix für alle übrigen Kopfwerte. Atelier: Kopfwert in allen Aufrufer-Tests, beide Werte in
`LlmHttpRetryTests`.

**Rückweg.** Gateway-Image zurück genügt nicht allein, solange `web` `same-model` schickt (dann wieder volles Failover); also zuerst
`web` zurück, dann das Gateway.

### Zusatz zu D-090 — ausgeliefert (2026-11-27)

Gateway und Web-App wurden in der oben genannten Reihenfolge ausgeliefert (zuerst das Gateway). Die Statuszeile des Eintrags
beschreibt den Stand davor.

## D-091 — Rückbau, Schnitt 2: nur der Standardweg bleibt (2026-11-27)

**Kontext.** Stufe 8d des Rückbaus („Rückbau, Schnitt 2“). Sie ersetzt die Zwischenstufen 8a/8b: Die ältere Prüfschleife hat die
Messung gegen den Standardweg verloren, und die in 8b geplante Teamauswahl wird nicht gebaut. Ziel: eine aufgeräumte, kleinere Codebasis, in der nur bleibt, was der Standardweg braucht. Die Datenbank wird später in einem eigenen Schritt aufgeräumt, nicht hier.

**Entscheidung — entfernt.**
- **Composer und Auto-Crew** (Zusammenstellungsläufe, Materializer, Validator, Prompts, `submit_auto_crew_request`).
- **Der agentische Werkzeug-Pfad** (`/tools`, Werkzeugdefinitionen und -aufrufe, Werkzeugbindungen an Profilen und in Snapshots,
  MCP-Server-Konfigurationen).
- **Die Lernschleife** (Lernläufe, Learnings, `/learnings`, `Learning:*`).
- **Die Vektor-Wissensbasis und die Embeddings** (`/knowledge`, Abschnitte, pgvector-Pakete, Übernahme von Laufanhängen,
  `Embeddings:*`, `Knowledge:*`).
- **Die alten Grounding-Profile und -Typen**, einschließlich `url-fetch` mit der URL-Sicherheitsprüfung und der Vektor- und
  Anhangsquellen, die D-089 noch behalten hatte. Kein verbliebener Pfad braucht sie: Der Standardweg liest die Dateien des Auftrags in
  seinem eigenen Grounding-Schritt und erreicht das Web nur über den Recherchedienst (D-074). `Tavily` behält nur `Endpoint` und
  `ApiKey`.
- **Alle Abschlussschritte** (Finalizer), einschließlich `MetadataEnrich`, mit den Laufartefakten, ihrem Download-Endpunkt und
  `Finalizer:*`. Die Formatwandler ziehen zum Export der Aufträge um.
- **Advisor und Spezialisierungspakete.**
- **Die ältere Prüfschleife** mit ihren Vorlagen (`klassik`, `juristisch`, `akademisch`, `marketing`, jede eigene Vorlage), die
  Werkstatt und die Editoren für Teams und für Schreiber-, Prüfer-, Advisor-, Abschluss- und Quellenprofile, das Werkstatt-Dashboard mit
  seinem Live-Hub und die Schutzschalter aus D-085 (`Convergence:BestDraft`, `WriterBlockingOnly`, `StopWithoutProgress` samt
  Ausnahmen). Pipelines baut nur noch `DirectPipelineAssembler`; `AtelierPipelineFactory` nimmt die Schritte, die man ihm gibt.
- **Das alte Formular `/new`** und der Link „More options“ auf `/start`.
- **Die älteren MCP-Werkzeuge** und der Schalter `Mcp:LegacyTools`; `Orders:LegacySubmitDefaultMode` entfällt ebenfalls.
- Reste: der Löschschutz für Anbieter, der die alten Profiltabellen abfragte, die Warncodes alter Quellen und Abschlussschritte,
  Slot-Helfer, die nur die Werkstatt nutzte, das Dashboard-Stylesheet, MailKit-Audit-Ausnahmen, das Einmalwerkzeug `MaterializeRun`.

**Entscheidung — bleibt.** Die Auftrags- und Laufseiten des Standardwegs, Exporte, „My work“, „About me“, die Verwaltung (Nutzer,
OAuth-Clients, Seiteneinstellungen, Anbieter), Datenschutz und Rechtliches, Erklär- und Startseite, Uploads und Upload-Tickets, die
MCP-Werkzeuge des Standardwegs (`submit_order`, `answer_order`, `get_order_status`, `get_order_result`, `list_orders`, `cancel_order`,
`revise_order`, `export_order`, `prepare_attachment`, `profile`), die Modellmatrix und der Modellkatalog. Das Werkstatt-Menü
(`Ui:WorkshopForAdminsOnly`) enthält nur noch die technische Laufliste und, für Administratoren, die Anbieter.

**Stummel für eine Version.** `submit_request`, `submit_auto_crew_request`, `get_run_status`, `get_run_result` und `get_run_details`
bleiben registriert: Jeder Parameter ist optional, und jeder Aufruf liefert einen Werkzeugfehler, der `submit_order`,
`get_order_status` oder `get_order_result` nennt (Clients halten Werkzeuglisten im Cache). Sie entfallen in der nächsten Version.

**„Extra careful“** entfällt ganz (zweiter Zusatz zu D-087): Es gibt nur noch den Direktweg, und gespeicherte sorgfältige Aufträge
werden dort weitergeführt. `scripts/atelier-smoke.sh` nutzt `submit_order`, `answer_order(skip=true)`, `get_order_status` und `get_order_result`.

**Ältere Daten.** Kein `DROP`, keine Migration, keine gelöschte Zeile: Die Datenbank bleibt mit dem vorigen Image verträglich.
Entfernte Entitäten sind nicht mehr gemappt; ihre Tabellen und die Spalten `Runs.GroundingCostEur`, `FinalizerCostEur`,
`FinalizerErrorMessage` und `Iterations.AdvisorsTotalCostEur` bleiben. Snapshots behalten die Liste `advisors`, damit das vorige Image
neue Snapshots lesen kann, und unbekannte Eigenschaften älterer Snapshots werden ignoriert. Fertige ältere Läufe und Aufträge bleiben
in den Listen, in „My work“, auf Auftrags- und Ergebnisseite, in Exporten und auf der technischen Laufseite lesbar (sie nennt die
Aufstellung und sagt, dass deren Arbeitsweise nicht mehr existiert). Ein wartender Lauf einer entfernten Aufstellung (ältere
Prüfschleife, zusammengestelltes Team, Lernlauf) scheitert mit „This task was set up for a way of working that no longer exists, so it
cannot run. Please start it again.“; das Fortsetzen eines solchen Laufs wird mit „This task was set up for a way of working that no
longer exists, so it cannot be continued. Please start it again.“ abgelehnt. Wiederherstellung nach Absturz und Aufräumer behandeln
ältere Läufe wie bisher.

**Beibehaltene Regeln.** Reihenfolge je Teil: Navigation → Registrierung → Code → Tests → Seeds; thematisch getrennte Commits; nur
handgeschriebene Migrationen.

**Nachweis.** Vollständige Testsuite, die End-to-End-Kategorie und die Tests des Smoke-Skripts. Neue End-to-End-Tests legen fertige
und wartende Läufe und Aufträge entfernter Aufstellungen an und prüfen Listen, „My work“, Seiten, Orchestrator und Wiederherstellung
nach Absturz. Der Test der Produktions-Snapshots (`ATELIER_SNAPSHOT_FIXTURES`, Export außerhalb des Repositorys) prüft jetzt, dass jeder
Snapshot gelesen wird und entweder auf dem Direktweg läuft oder mit dem Satz oben abgelehnt wird; alle exportierten Snapshots bestehen.
Orchestrator-Tests, die Abschlussschritte nutzten, verwenden jetzt den Abschluss-Haken des Assemblers (`OnRunFinishedAsync`).

**Grenzen.**
- Der EF-Modell-Snapshot kennt die entfernten Entitäten noch (wie nach D-089); keine Migration erzeugen. Eine neue Basis folgt dem
  Aufräumen der Datenbank.
- Das spätere Aufräumen darf die Tabellen `AdvisorConsultations`, `AdvisorProfiles`, `CrewTemplateEmbeddings`, `CrewTemplates`,
  `ExecutorProfiles`, `FinalizationActorCosts`, `FinalizerProfiles`, `GroundingActorCosts`, `GroundingConsultations`,
  `GroundingProviderProfiles`, `KnowledgeDocumentChunks`, `KnowledgeDocuments`, `LearningEntries`, `ReviewerProfiles`, `RunArtifacts`,
  `StudioSettings`, `TemplateStudioAnalyses`, `mcp_server_configs`, `specialization_packs`, `tool_definitions`, `tool_invocations`, die
  Spalten oben und die alten Zusammenstellungs- und Lernläufe entfernen; das Compose-Volume für Exporte wird nicht mehr genutzt.
- `ModelSlot` behält seine Werte (Snapshots speichern Zahlen); die Matrixzeilen ausgemusterter Slots bleiben bis zum Aufräumen.
- Der Arm `atelier` des Auswertungswerkzeugs in `tools/Eval` nutzt die Auftragswerkzeuge (`submit_order`, `answer_order` mit
  `skip=true`, `get_order_status`, `get_order_result`); seine Prüfung der Werkzeugliste erwartet die Auftragswerkzeuge und die fünf
  Stummel.

**Rückweg.** Das vorige Image; in der Datenbank wurde nichts entfernt oder geändert. Im Code nur über Git (Tag vor dem Deploy).

## D-092 — Datenbank und Dateiablage aufgeräumt: eine Baseline-Migration (2026-11-27)

**Kontext.** Nach Stufe 8d (D-091) hielt die Datenbank noch jede Tabelle und Spalte der entfernten Arbeitsweisen, der
EF-Modell-Snapshot kannte noch entfernte Entitäten, und Teile des Codes gab es nur, um Altdaten zu lesen. Der Betreiber hat das
Aufräumen erlaubt: Alle gespeicherten Aufträge und Läufe waren Tests.

**Entscheidung — eine Baseline.** Alle früheren Migrationen sind durch eine Migration `Baseline` ersetzt, erzeugt aus dem Modell;
sie legt auch die Zeilen an, die eine frische Installation braucht (die Systemanbieter und den Platzhalter der
Seiteneinstellungen), mit demselben Inhalt, den die früheren Migrationen auf einer leeren Datenbank erzeugt haben. Schema und
Modell passen wieder zusammen: `HasPendingModelChanges` ist falsch (getestet), die Warnung `PendingModelChangesWarning` wird nicht
mehr unterdrückt, und spätere Migrationen dürfen wieder mit den EF-Werkzeugen erzeugt werden.

**Schutz beim Start.** `EnsureStartsFromBaselineAsync` läuft vor der Migration und liest nur. Eine Datenbank, deren
Migrationsgeschichte Einträge hat, aber nicht die Baseline, oder eine mit Tabellen ohne Geschichte hält den Start mit einer klaren
Meldung an; nichts wird angelegt oder geändert. Eine Datenbank mit der Baseline und späteren Migrationen, die diese Version nicht
kennt, startet weiterhin; der Rückweg auf das vorige Image nach einer additiven Migration funktioniert also wie bisher.

**Umstellung einer bestehenden Installation.** Die laufende Installation wurde einmal außerhalb der Anwendung umgestellt, mit
einem SQL-Skript in einer Transaktion. Das Skript ist an diese Installation gebunden und nicht Teil des Repositorys. Es bricht bei allem Unerwarteten ab (andere Migrationsgeschichte oder anderes Schema, offene
Verbindungen, laufende Arbeit, Dateien in der Bibliothek) und prüft am Ende, dass das Schema genau der Baseline entspricht und die
behaltenen Zeilen unverändert sind; ein zweiter Lauf ändert nichts. Behalten werden: alle Konten außer deaktivierten Testkonten,
die Passwort-Hashes, OAuth-Clients mit noch gültigem Token oder Code (oder jünger als ein Tag), gültige Zugriffstokens, alle nicht
abgelaufenen Erneuerungstokens (auch benutzte und widerrufene, weil die Erkennung einer Wiederverwendung sie braucht), das
OAuth-Protokoll, die Anbieter, die Seiteneinstellungen und die About-me-Profile. Gelöscht werden: alle Aufträge, Läufe und alles,
was zu ihnen gehört, hochgeladene Dateien und Upload-Tickets, gezählte Ablehnungen, abgelaufene Codes und Tokens sowie Clients ohne
gültiges Token. Die Data-Protection-Schlüssel bleiben unberührt, Anmelde-Cookies und Tokens gelten also weiter. Gespeicherte
Originale, auf die keine Zeile mehr zeigt, werden danach einmal entfernt; der Aufräumdienst der App tut das bewusst nicht, wenn die
meisten Dateien unbekannt sind, und behält diesen Schutz.

**Aus der Datenbank entfernt.**
- Tabellen: `AdvisorConsultations`, `AdvisorProfiles`, `CrewTemplateEmbeddings`, `CrewTemplates`, `ExecutorProfiles`,
  `FinalizationActorCosts`, `FinalizerProfiles`, `GroundingActorCosts`, `GroundingConsultations`, `GroundingProviderProfiles`,
  `KnowledgeDocumentChunks`, `KnowledgeDocuments`, `LearningEntries`, `ReviewerProfiles`, `RunArtifacts`, `StudioSettings`,
  `TemplateStudioAnalyses`, `mcp_server_configs`, `specialization_packs`, `tool_definitions`, `tool_invocations`; die Erweiterung
  `vector`.
- Spalten: `Runs.AdvisorRetryAttempted`, `ConfigJson`, `CostTotal`, `GroundingCostEur`, `FinalizerCostEur`,
  `FinalizerErrorMessage`, `Kind`, `ParentCompositionRunId`; `Orders.Mode`, `Orders.CrewTemplateName`;
  `Iterations.AdvisorsTotalCostEur`. Spaltenvorgaben, die das Modell nicht kennt, sind weg; die Namen der Constraints folgen dem
  Modell.
- Das Compose-Volume für Exporte und sein Einhängepunkt im Image.

**Aus dem Code entfernt** (alles las nur Altdaten): `RunKind` mit Composer- und Lernläufen, `OrderMode` mit dem gespeicherten Modus
„careful“ und seinen Fortschrittsschritten, der Eingang `Legacy` mit dem Verknüpfen auftragsloser Läufe, der Vorlagenname
`direct-outline` und seine Prüfung, alte Anzeigenamen von Prüfungen und Aufstellungen, die Ablehnung entfernter Aufstellungen bei
„Resume“, die Liste `advisors` im Snapshot (das Snapshot-Format ist jetzt Version 4, und nur diese Version läuft), das Lesen von
Berichten im Schema 1, der Prüfcode `arithmetic.partial`, das Herausputzen des entfallenen Satzes „länger“ aus gespeicherten
Änderungen, `ActorType.Advisor` und der Sonderfall für Läufe ohne Ergebnis aus der Zeit, bevor es Ergebnisse gab. „Resume“ legt den
neuen Lauf jetzt in einem Schritt im Auftrag seines Vorgängers an; ein Lauf ohne Auftrag kann nicht fortgesetzt werden.

**Bewusst behalten.** Die fünf MCP-Stummel und der versteckte Parameter `extraCareful` bleiben bis zur nächsten Version (D-087,
D-091). `RevisionQuick.Longer` bleibt, weil ein Aufrufer, der danach fragt, die Ablehnung bekommt, die sagt, was stattdessen zu tun
ist. `ModelSlot` behält seine Werte; keine Spalte speichert sie. `Runs.CrewTemplateName` bleibt (neue Läufe schreiben sie, die
Lauf-Seiten zeigen sie). `Runs.OrderId` bleibt nullable. Die Compose-Dateien behalten ihr Postgres-Image; die Erweiterung braucht
nichts mehr.

**Indizes**, aus den Abfragen im Code:

| Änderung | Grund |
|---|---|
| `IX_Findings_IterationId` neu | Befunde werden je Durchgang gelesen, und das Löschen von Durchgängen löscht sie mit |
| `IX_RunSourceUsages_SourceId`, `IX_UploadTickets_SourceId`, `IX_OAuthAuthorizationCodes_ClientId` neu | das Löschen einer Quelle oder eines Clients ändert oder löscht diese Zeilen mit |
| `IX_Orders_CreatedByUser_CreatedAt` → `IX_Orders_CreatedByUser_UpdatedAt` | „My work“ filtert nach Person und sortiert nach `UpdatedAt`, dann `CreatedAt` |
| `IX_Orders_ResearchAt` (partiell) neu | die Tagesgrenzen der Recherche zählen unter einer Sperre nach `ResearchAt` |
| `IX_Orders_State` entfernt | die Abfragen nach Zustand nutzen die zwei partiellen Indizes der Klärung |
| `IX_Runs_CreatedByUser` → `IX_Runs_CreatedByUser_CreatedAt` | die Laufliste filtert nach Person und sortiert nach `CreatedAt` |
| `UX_Runs_OrderId_OrderVersion` ersetzt den partiellen eindeutigen Index und `IX_Runs_OrderId_OrderVersion` | ein Lauf je Version eines Auftrags; bedient zugleich jede Suche nach Auftrag |
| `IX_Runs_Status`, `IX_Runs_ParentRunId`, `IX_Runs_ParentCompositionRunId` entfernt | der erste ist ein Präfix von `IX_Runs_Status_CompletedAt`, nach den anderen filtert keine Abfrage |
| `IX_Sources_OwnerUsername_CreatedAt` entfernt | Suchen nach Eigentümer nutzen `UX_Sources_OwnerUsername_Sha256`; keine Abfrage sortiert die Dateien einer Person nach Datum |
| drei Indizes von `Providers`, `IX_OAuthAuditLog_EventType` entfernt | eine Handvoll Zeilen; keine Abfrage filtert nach Ereignisart |

Nach der Umstellung wird die Datenbank einmal verdichtet (`VACUUM (FULL, ANALYZE)`).

**Beweis.** Auf einer privaten Kopie: Eine leere Datenbank mit der Baseline und die umgestellte Kopie ergeben dasselbe Schema
(`pg_dump --schema-only`, mit sortierten Spalten verglichen); ein zweiter Lauf des Skripts ändert nichts; die Anwendung startet
gegen beide und lehnt die nicht umgestellte ab, ohne sie zu ändern; die Anmeldung funktioniert, und ein Auftrag läuft mit der
Modell-Attrappe des Testgerüsts bis zur Lieferung. Neue Tests decken die Baseline, ihre Seeds und den Startschutz ab; die Tests der
alten Migrationen und der Altdaten sind weg.

**Rückweg.** Das vorige Image zusammen mit der unmittelbar vor der Umstellung gezogenen Datenbanksicherung (in eine leere
Datenbank zurückgespielt) und, falls nötig, der Sicherung der Dateiablage. Das vorige Image kann die umgestellte Datenbank nicht
lesen.

### Zusatz zu D-092 — abschließende Doku-Prüfung (2026-11-27)

Dokumente, öffentliche Seiten und der Installationsweg wurden am Code geprüft.

- **Keine festen Wartezeiten.** Wartefristen, die frühere Einträge für Arbeiten gesetzt hatten (zum Beispiel Tabellen löschen
  „frühestens einen Deploy und 30 Tage später“ in D-077 und D-078 oder Messungen „nachts“), gelten nicht mehr. Einen Schritt
  schützt die Prüfung des aktuellen Zustands: Es wird nichts geschrieben, die Dienste sind gesund, das Rückspielen der Sicherung
  ist geprüft, und es gibt einen Rückweg. Fachliche Zeitgrenzen des Produkts (Token-Gültigkeit, Timeouts, Aufbewahrung) sind
  davon nicht berührt.
- **DNS-Pinning.** Die in D-066 und D-077 genannte Härtung ist gegenstandslos: `url-fetch`, `rest-api` und der MCP-Client, um die
  es ging, sind entfernt (D-089, D-091).
- **Installationsweg**, gefunden beim Durchspielen aus einem frischen Klon: Der Webdienst bekommt jetzt seine öffentliche Adresse
  (`OAuth__Issuer`, aus `ATELIER_DOMAIN`), statt beim eingebauten Standard zu bleiben; ein Passwort-Hash in `.env` braucht
  einfache Anführungszeichen; die CLIs des Gateways werden mit ihrem eigenen Home-Ordner angemeldet;
  `scripts/restore-backup.sh` ersetzt die Daten jetzt wirklich, in einer Transaktion (vorher endete das Rückspielen in eine
  gefüllte Datenbank mit Fehlern, ließ die alten Daten stehen und meldete trotzdem Erfolg).
- **Bereinigt.** Betriebsdetails (Serveradresse, Namen von Sicherungsdateien, andere Anwendungen auf demselben Rechner) wurden aus
  früheren Einträgen entfernt; die Entscheidungen sind unverändert.
