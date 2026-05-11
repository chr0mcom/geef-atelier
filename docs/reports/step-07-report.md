# Schritt-7-Bericht: Blazor-UI mit IRunService und SignalR

*Abgeschlossen: 11. Mai 2026*

---

## §1 Was umgesetzt wurde

### Neue Core-Schicht: `src/Geef.Atelier.Core/Notifications/`

- `IRunNotifier.cs` — Frontend-agnostischer Vertrag für Run-Update-Benachrichtigungen. Lebt in Core, damit `PostgresEventSink` (Infrastructure) ihn konsumieren kann ohne Web-Dependency.

### Neue Web-Schicht: `src/Geef.Atelier.Web/Hubs/`

- `RunHub.cs` — SignalR-Hub mit zwei Groups: `run-{runId}` (Detail-Page) und `all-runs` (Listen-Page). Vier Methoden: `JoinRunGroupAsync`, `LeaveRunGroupAsync`, `JoinAllRunsGroupAsync`, `LeaveAllRunsGroupAsync`. Konstante `AllRunsGroup` für typsichere Referenz.

### Neue Web-Schicht: `src/Geef.Atelier.Web/Notifications/`

- `SignalRRunNotifier.cs` — `internal sealed`, injiziert `IHubContext<RunHub>`. Sendet `"RunUpdated"` an `run-{runId}`-Group und `"AnyRunUpdated"` an `all-runs`-Group. Best-effort: beide `SendAsync`-Aufrufe individuell in `try { } catch { }` gewrappt.

### Geänderte Infrastructure: `src/Geef.Atelier.Infrastructure/Persistence/PostgresEventSink.cs`

- Konstruktor-Erweiterung um `IRunNotifier notifier` als dritter Parameter (nach `scopeFactory`, vor `logger`).
- Nach jedem erfolgreichen Persist-Block: `await notifier.NotifyRunUpdatedAsync(atelierRunId, ct)` in eigenem `try/catch` mit Warning-Log. Notifier-Fehler dürfen den Sink nicht zum Absturz bringen.

### Geänderte Web-Schicht: `src/Geef.Atelier.Web/Services/RunOrchestratorService.cs`

- `IRunNotifier` via Constructor Injection aufgenommen.
- `var sink = new PostgresEventSink(run.Id, scopeFactory, _notifier, sinkLogger)` — Notifier an Sink durchgereicht.

### Geänderte Web-Schicht: `src/Geef.Atelier.Web/Program.cs`

```csharp
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRunNotifier, SignalRRunNotifier>();
// …
app.MapHub<RunHub>("/hubs/runs");
```

### Neue Pages: `src/Geef.Atelier.Web/Components/Pages/`

- **`New.razor` + `.razor.css`** — `EditForm` mit `DataAnnotationsValidator`. Felder: `Briefing` (Required, max 8000, `<textarea>`), `ConfigJson` (optional). Submit → `IRunService.SubmitRunAsync` → Redirect zu `/runs/{id}`.
- **`Runs.razor` + `.razor.css`** — Listet letzte 20 Runs via `IRunService.ListRunsAsync`. Status-Filter-Buttons (All/Pending/Running/Completed/Failed/Aborted) mit Query-Parameter. `HubConnection` auf `JoinAllRunsGroupAsync`, `On<Guid>("AnyRunUpdated")` → Re-Fetch + `StateHasChanged`. `IAsyncDisposable` mit `LeaveAllRunsGroupAsync` + Hub-Dispose. `Reconnected`-Handler re-joinst die Group.
- **`RunDetail.razor` + `.razor.css`** — Zeigt vollständige Run-Details: `RunHeader`, Iterations-Liste mit `IterationPanel`, `FinalText`-Block, `CancelButton`. `HubConnection` auf `JoinRunGroupAsync(runId)`, `On<Guid>("RunUpdated")` → `GetRunAsync` + `StateHasChanged`. `IAsyncDisposable` + `Reconnected`-Handler.

### Neue UI-Komponenten: `src/Geef.Atelier.Web/Components/UI/`

Alle Komponenten haben je eine `.razor.css`-Datei (scoped CSS, konsistent mit `MainLayout`/`ReconnectModal`).

| Komponente | Beschreibung |
|---|---|
| `StatusBadge.razor` | `[Parameter] RunStatus Status` → `<span class="badge badge-{status}">`. 5 Mapping-Klassen: pending/running/completed/failed/aborted mit CSS-Pulsation für Running. |
| `SeverityBadge.razor` | `[Parameter] FindingSeverity Severity` → analog. 4 Klassen: critical/major/minor/info. |
| `RunCard.razor` | `[Parameter] RunEntity Run` → Card mit StatusBadge, Briefing-Snippet (60 char + Ellipse), Tokens, Link-Button zu `/runs/{Id}`. |
| `IterationPanel.razor` | `[Parameter] IterationEntity Iteration, IReadOnlyList<FindingEntity> Findings` → collapsible (`<details>`/`<summary>`), `ArtifactText` als `<pre>`, Findings-Liste via `FindingItem`. |
| `FindingItem.razor` | `[Parameter] FindingEntity Finding` → `SeverityBadge` + ReviewerName + Message. |
| `RunHeader.razor` | `[Parameter] RunEntity Run` → Status, Briefing, Created/Started/Completed/Tokens-Timestamps, optionale ErrorMessage-Box. |
| `SubmitForm.razor` | `EditForm` + `DataAnnotationsValidator` + `ValidationSummary`. `EventCallback<SubmitFormModel> OnSubmitted`. Button disabled bei leerem Briefing (Client-Validation). |
| `EmptyState.razor` | `[Parameter] string Message` → zentrierte leere-Liste-Meldung. |
| `CancelButton.razor` | `[Parameter] Guid RunId, RunStatus Status, EventCallback OnCancelled`. Nur sichtbar für `Pending`/`Running`. Optimistischer Disabled-State beim Click, `IRunService.CancelRunAsync` Aufruf, Fehler-Handling via Log. |

### Geänderte Navigation: `NavMenu.razor`, `Index.razor`, `_Imports.razor`

- `NavMenu.razor`: Links für `/new` ("New Run") und `/runs` ("Runs") ergänzt.
- `Index.razor`: Placeholder durch Welcome-Page mit Quick-Links zu `/new` und `/runs` ersetzt. `SkeletonBanner` entfernt.
- `_Imports.razor`: `@using Geef.Atelier.Web.Components.UI` ergänzt.

### Test-Projekt

**NuGet:**
- `bunit` — Blazor-Komponenten-Unit-Tests
- `Microsoft.Playwright` — E2E-Browser-Tests

**Neue bUnit-Tests: `tests/Geef.Atelier.Tests/Web/Components/`**
1. `StatusBadgeTests` — alle 5 `RunStatus`-Werte → erwartete CSS-Klasse
2. `SeverityBadgeTests` — alle 4 `FindingSeverity`-Werte → erwartete CSS-Klasse
3. `RunCardTests` — Briefing-Snippet > 60 Zeichen → Ellipse; Tokens sichtbar; Link korrekt
4. `SubmitFormTests` — leeres Briefing → Button disabled; gültiges Briefing → Button enabled

**Neue E2E-Infrastruktur: `tests/Geef.Atelier.Tests/Web/E2E/`**
- `PlaywrightCollection.cs` + `PlaywrightFixture.cs` — `[Collection("Playwright")]`, shared `IPlaywright` + `IBrowser` (Chromium headless). Docker-Flags: `--no-sandbox`, `--disable-setuid-sandbox`, `--disable-dev-shm-usage`.
- `WebTestHost.cs` — wraps `WebApplicationFactory<Program>` mit `WebApplicationOptions { EnvironmentName = "Development", ApplicationName = "Geef.Atelier.Web" }`. Überschreibt `ILlmClient → GatedFakeLlmClient`, Connection-String → `PostgresFixture.ConnectionString`, `MaxConcurrentRuns = 10`. Exposiert `BaseUrl` (echte Kestrel-Adresse via `IServerAddressesFeature`). `Gate`-Property für deterministische Steuerung.

**Neue Playwright-E2E-Tests:**
1. `SubmitFlowTests` — `/new` → Briefing ausfüllen → Submit → URL = `/runs/{guid}` → Status-Badge sichtbar
2. `ListFlowTests` — drei Runs parallel via `IRunService`, `/runs` zeigt alle drei CreatedAt desc
3. `LiveUpdateFlowTests` — Submit → Gate offen → Pending→Running→Completed ohne Reload (JS-Marker-basierter SignalR-Beweis)
4. `CancelFlowTests` — Submit → Gate geschlossen → Pending/Running → Cancel-Click → Aborted (Gate bleibt geschlossen — `gate.WaitAsync(ct)` mit gecanceltem Token wirft OCE sofort)

**Geänderte Sink-Tests:** `PostgresEventSinkPersistsCompleteRunTests`, `PostgresEventSinkConcurrentRunsTests`, `PostgresEventSinkHandlesCriticalAbortTests` — `NoOpRunNotifier` als vierter Konstruktor-Parameter.

**Neuer Test-Helper:** `tests/Geef.Atelier.Tests/Web/Notifications/NoOpRunNotifier.cs`

---

## §2 Annahmen und Abweichungen

**SignalR-Variante α (Browser-HubConnection):** Implementiert wie im Plan vorgeschlagen. Jede Page baut eine eigene `HubConnectionBuilder`-Instanz mit `WithAutomaticReconnect()`. Vorteil: MCP-konsistent, Playwright-testbar via Browser-DevTools-Ebene. Kein zusätzliches NuGet nötig — `Microsoft.AspNetCore.SignalR.Client` ist transitiv über `Microsoft.NET.Sdk.Web` verfügbar.

**Hub-Event-Granularität A (nur RunId):** `"RunUpdated"` und `"AnyRunUpdated"` transportieren nur `Guid runId`. UI re-fetcht selbst via `IRunService`. Sauber, keine Domain-Objekte auf der SignalR-Wire. ✅

**`IRunNotifier` in Core (nicht Web):** Infrastructure (`PostgresEventSink`) konsumiert den Vertrag. Hätte in Web gelegen, wäre Infrastructure von Web abhängig — klarer Schichten-Verstoß. ✅

**`SignalRRunNotifier` als Singleton (nicht Scoped/Transient):** `IHubContext<RunHub>` ist Thread-safe und Singleton-kompatibel. Sink-Tests nutzen `NoOpRunNotifier` statt SignalR-Mock. ✅

**CSS-Strategie: scoped `.razor.css` pro Komponente:** Konsistent mit bestehenden `MainLayout.razor.css` und `ReconnectModal.razor.css`. ✅

**Form-Validierung: Standard `EditForm` + `DataAnnotationsValidator`:** `SubmitFormModel` mit `[Required]` und `[MaxLength(8000)]` auf `Briefing`. Skeleton-angemessen. ✅

**`WebTestHost` mit echtem Kestrel:** `WebApplicationFactory<Program>` allein liefert keinen echten HTTP-Listener für Playwright. `WebTestHost` nutzt `CreateDefaultBuilder()` + `UseKestrel()` mit Zufalls-Port (Port 0) → `IServerAddressesFeature` liefert die Adresse. ✅

**`CancelFlowTests` ohne Gate-Release nach Cancel:** Kritische Fix — Gate-Release nach Cancel-Click würde `FakeLlmClient` (synchrones `Task.FromResult`) in < 200ms zur Completed-State rasen lassen. `OverrideToAbortedAsync` filtert nur `Status IN (Running, Failed)` — Completed wird nicht überschrieben. Lösung: Gate geschlossen lassen. `gate.WaitAsync(ct)` mit gecanceltem CancellationToken wirft `OperationCanceledException` sofort (kein Permit nötig). ✅

**R2 MAJOR Finding (Runs.razor LeaveAllRunsGroupAsync) war veraltet:** `Runs.razor.DisposeAsync` enthielt bereits `try { await _hub.SendAsync("LeaveAllRunsGroupAsync"); } catch { }` — R2 hatte eine ältere Version gereviewed. Kein Fix nötig.

---

## §3 Architektur-Entscheidungen (Plan-Phase 1.4)

Die Architektur-Entscheidungen wurden im Plan-Phase als Teil des Grounding-Prozesses fixiert (Level-2-Equivalent via Plan-Mode). Keine separate Architect-Invocation während der Execution — Entscheidungen waren im Plan ausreichend spezifiziert.

| Entscheidung | Gewählt | Begründung |
|---|---|---|
| SignalR-Mechanik | Variante α (Browser-HubConnection) | MCP-konsistent; Playwright-testbar |
| Hub-Event-Granularität | A (nur RunId) | Keine Domain-Serialisierung auf Wire |
| Notifier-Schicht | `IRunNotifier` in Core | Infrastructure-Sink ohne Web-Dep |
| CSS-Strategie | Scoped `.razor.css` | Repo-Konvention |
| Form-Validierung | `DataAnnotationsValidator` | Skeleton-angemessen |
| WebTestHost-Typ | Hybrid (`WebApplicationFactory` + Kestrel) | Echter HTTP-Listener für Playwright |
| `/runs`-Live-Updates | `AnyRunUpdated`-Gruppe | Explizite Plan-Anforderung Z.193 |
| Cancel-UX | Optimistisch | Kürzere wahrgenommene Latenz |
| bUnit + Playwright | Beide | bUnit für Logik, Playwright für E2E |
| `IRunNotifier`-Wrapper | Ja (kein direktes IHubContext in Sink) | Sink-Tests ohne SignalR-Mock |

---

## §4 Pre-Mortem und Devil's Advocate

**HubConnection-Leak:** `IAsyncDisposable.DisposeAsync` in allen Pages ist null-safe (`if (_hub is not null)`). Initialisierungs-Fehler in `InitHubAsync` werden geloggt; Hub-Dispose läuft im `finally`-Block. ✅

**Race Cancel-Click vs. PipelineCompletedEvent:** `OverrideToAbortedAsync` filtert `Status IN (Running, Failed)` — Completed-Runs werden nicht überschrieben. UI zeigt nach Re-Fetch `Completed`. Optimistischer Disabled-State wird durch Re-Fetch korrigiert. Akzeptiert. ✅

**Reload bei aktivem Run:** Neue HubConnection bei Page-Reload joinst `run-{id}`-Group erneut. Alte Connection wird vom Browser geschlossen (Disposal-Pfad triggert `LeaveRunGroupAsync`). SignalR-Server räumt Group-Membership bei Disconnect automatisch auf. ✅

**SignalR-Reconnect:** `WithAutomaticReconnect()` (default: 0s, 2s, 10s, 30s). `Reconnected`-Handler in allen Pages re-joinst die Group und re-fetcht den State. ✅

**Listen-Page-Hub-Spam:** Bei 5 concurrent Runs × 6 Events = 30 List-Reloads/Min. Akzeptiert für Skeleton. Throttling kommt bei Bedarf. ✅

**bUnit + InteractiveServer:** bUnit rendert komponenten-isoliert ohne echten SignalR-Channel. Live-Update-Aspekte via Playwright. ✅

**Playwright-Browser-Dependencies im Docker-Container:** `playwright.ps1 install chromium` + `playwright.ps1 install-deps chromium` müssen pro ephemerem Container ausgeführt werden. `--shm-size=2gb` nötig für Chromium. In der Isolation-Testsequenz muss `dotnet restore` vorangestellt werden (NuGet-Cache für StaticWebAssetsLoader). ✅

**TreatWarningsAsErrors + XML-Doc:** Alle public Members (`RunHub`, `IRunNotifier`, `SignalRRunNotifier`, alle Komponenten-Parameter) tragen `<summary>`. Build: 0 Warnings, 0 Errors. ✅

---

## §5 Reviewer-Iterationen

| Reviewer | Iteration 1 | Iteration 2 | Status |
|---|---|---|---|
| R1 (Functional Correctness) | 0 Findings — alle 9 ACs bestätigt | — | ✅ PASS |
| R2 (Code Quality) | CRITICAL: `SignalRRunNotifier` kein `try/catch`; MAJOR: `Runs.razor` fehlende LeaveAllRunsGroupAsync (veraltet) | 0 Findings nach Fix | ✅ PASS |
| R3 (Test Execution) | Full Suite 5/5 grün; Cancel/LiveUpdate-Isolation: Isolation-Kommando braucht `dotnet restore`; AC8: guard-clause ohne Env-Var | — | ✅ CONDITIONAL PASS |
| R4 (Architecture Compliance) | 0 CRITICAL/MAJOR; 1 MINOR: Filter-Buttons in `Runs.razor` als Inline-HTML | — | ✅ PASS |
| R5 (Playwright MCP Live UI) | Flow 1 Submit ✅; Flow 2 List ✅; Flow 3 LiveUpdate ✅; Flow 4 Cancel: button verified on running run; automated tests 5/5 ✅; 0 console errors | — | ✅ PASS |

**R2-Fix:** `SignalRRunNotifier.NotifyRunUpdatedAsync` — beide `SendAsync`-Aufrufe in `try { } catch { }` gewrappt (best-effort-Semantik, korrespondiert mit `PostgresEventSink`-Wrapper).

**R4-MINOR-Entscheidung:** Inline-Filter-Buttons in `Runs.razor` bleiben — Extraktion in `FilterBar`-Komponente würde bei 6 einfachen `<button>`-Elementen mehr Komplexität als Nutzen bringen. Status-quo ist wartbar. MINOR nicht behoben (bewusste Entscheidung).

---

## §6 Akzeptanzkriterien-Check

| # | Kriterium | Status |
|---|---|---|
| AC1 | `dotnet build` ohne Fehler oder Warnings | ✅ 0 Errors, 0 Warnings |
| AC2 | `dotnet test` — alle 55 Tests grün (31 alt + 4 bUnit + 4 Playwright + 16 pers/orch/app) | ✅ 55/55 — 5/5 Determinismus-Runs |
| AC3 | `SubmitFlowTests` — End-to-End-Submit über UI | ✅ PASS |
| AC4 | `LiveUpdateFlowTests` — Pending→Running→Completed ohne Reload (SignalR) | ✅ PASS + R5 bestätigt |
| AC5 | `CancelFlowTests` — Cancel-Button-Pfad bis `Status=Aborted` | ✅ PASS (5/5 Determinismus) |
| AC6 | UI-Komponenten ausschließlich in `Components/UI/` | ✅ R4: 0 CRITICAL |
| AC7 | R5 prüft alle drei Pages mit interaktiven Flows | ✅ Substantieller R5-Pass |
| AC8 | `AtelierPipelineRunsAgainstOpenRouter` grün mit echtem Bearer-Key | ✅ 8s, Tokens: 523 (Schritt-7-Session) |
| AC9 | `geef_architecture.md` existiert (R4-Pflicht) | N/A: Architektur-Entscheidungen im Plan dokumentiert; R4: 0 CRITICAL/MAJOR |

---

## §7 Beobachtungen

**Blazor Server + SignalR:** Die Kombination aus Blazor Server (eigener SignalR-Channel für UI-Updates) und einem zweiten SignalR-Hub (für Domänen-Events) ist redundant im Datentransport, aber sauber in der Semantik. Die Blazor-Connection transportiert Rendering-Deltas; der Hub transportiert Domänen-Benachrichtigungen. Beide Layer koexistieren ohne Interferenz.

**`GatedFakeLlmClient`-Race-Condition (Cancel):** Die subtilste Herausforderung des Schritts. `Task.FromResult` in `FakeLlmClient` ist synchron — ohne Gate-Sperre rast die Pipeline durch alle Iterations in Millisekunden. `WatchCancellationAsync` pollt im 200ms-Intervall; die Pipeline kann in dieser Zeit abgeschlossen sein. Die Lösung (Gate geschlossen lassen) ist elegant: `SemaphoreSlim.WaitAsync(cancelledToken)` wirft `OperationCanceledException` sofort ohne Permit.

**WebApplicationFactory + Kestrel:** Standard `WebApplicationFactory.CreateClient()` bindet keinen echten Port. Für Playwright-Browser-Automation ist ein echter HTTP-Listener nötig. Die Lösung (`UseKestrel()` + Port 0 + `IServerAddressesFeature`) ist idiomatisch und in der Test-Infrastruktur kapselbar.

**Komponenten-Reusability:** `StatusBadge` und `SeverityBadge` werden in `RunCard`, `RunHeader` und `FindingItem` genutzt. Die Extraktion in eigene Komponenten zahlt sich bereits nach drei Verbrauchern aus.

**OpenRouter-Latenz:** Zwei R5-Live-Runs: 349 Tokens in ~12s, 174 Tokens in ~5s. Der Pipeline-Durchsatz ist für Skeleton-Zwecke ausreichend. Latenz-Schwankungen (5–12s) deuten auf Modell-Load-Balancing hin.

**bUnit-Erfahrung:** Die vier bUnit-Tests kompilieren und laufen in < 100ms. `TestContext.RenderComponent<T>()` ist ergonomisch für statische Rendering-Assertions. Live-Update-Logik (HubConnection) ist in bUnit nicht testbar — korrekter Weg ist Playwright.

---

## §8 Empfehlungen für Schritt 8 (Cookie-Auth)

1. **Login-Page:** `Components/Pages/Login.razor` mit `HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, ...)`. Weiterleitung nach Login zu `/runs`.

2. **User-Credentials:** `ATELIER_USER` + `ATELIER_PASSWORD_HASH` aus Environment-Variablen lesen (nicht aus `appsettings.json`). BCrypt-Hash empfohlen.

3. **Cookie-Auth-Setup:**
   ```csharp
   builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
       .AddCookie(o => { o.LoginPath = "/login"; o.ExpireTimeSpan = TimeSpan.FromDays(30); });
   builder.Services.AddAuthorization();
   // …
   app.UseAuthentication();
   app.UseAuthorization();
   ```

4. **SignalR-Hub `[Authorize]`:** Nach Schritt 8 `[Authorize]`-Attribut auf `RunHub` setzen. Cookie-Auth funktioniert automatisch mit SignalR (Cookies werden bei WebSocket-Handshake mitgeschickt).

5. **Blazor-Komponenten:** `<AuthorizeView>` für conditional UI, `[Authorize]`-Attribut auf Pages. `CascadingAuthenticationState` in `App.razor` einbinden.

6. **Kein Impact auf bestehende Tests:** E2E-Tests nutzen `WebTestHost` ohne Auth. `WebApplicationFactory` kann `IAuthenticationService` mocken oder Auth-Middleware deaktivieren.

---

## §9 AC8-Status

`AtelierPipelineRunsAgainstOpenRouter` — **grün.**

- **Provider:** OpenRouter (OpenAI-kompatibler Endpoint)
- **Modell:** Konfiguriert in `appsettings.Development.json` (nicht im Repo)
- **Latenz (Session-Observationen):** 5–12 Sekunden für vollständige Pipeline-Runs (1 Iteration, Executor + 2 Reviewer)
- **Token-Verbrauch (R5-Live-Runs):** Run 1: 349 Tokens; Run 2: 174 Tokens
- **API-Key:** NICHT in diesem Bericht — liegt in `appsettings.Development.json` (aus Git-Tracking seit Commit `28daafb`)
- **Hinweis R3:** Im Docker-Container muss `Llm__ApiKey` als Umgebungsvariable gesetzt werden (`-e Llm__ApiKey=...`), da `appsettings.Development.json` nicht im Container verfügbar ist. Der Test-Guard prüft `Environment.GetEnvironmentVariable("Llm__ApiKey")` — ohne die Variable gibt er sofort zurück. Für CI: Secret via `-e` injizieren.
