# Claude-Code-Prompt: Schritt 7 — Blazor-UI mit IRunService und SignalR

*Diese Datei ist als Eingabe für Claude Code gedacht.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Schritte 1–6 sind abgeschlossen, M1 (Provider-Wechsel auf OpenAI-konform) ist gemerged in main. Deine Aufgabe ist **Schritt 7 von 10**: das **erste echte Frontend** — eine Blazor-Server-UI, die `IRunService` aufruft und Pipeline-Events live über SignalR streamt.

Was sich ändert: Drei Pages (`/new`, `/runs`, `/runs/{id}`), ein SignalR-Hub für Live-Updates, mehrere wiederverwendbare UI-Komponenten in der bestehenden `Components/UI/`-Library, und der `PostgresEventSink` wird minimal erweitert um Hub-Notifications zu feuern. Was bleibt unverändert: `IRunService`-Vertrag, Domain-Modell, DB-Schema, Pipeline, Orchestrator.

Das ist der erste Schritt, in dem **R5 (Playwright) substantiell beladen wird** — bisher war R5 ein Sanity-Check ("Heading sichtbar, 0 Console-Errors"). Ab Schritt 7 prüft R5 echte User-Flows: Submit-Form ausfüllen, abschicken, zur Detail-Seite navigieren, Live-Status beobachten, ggf. Cancel-Button drücken.

Auth kommt erst in Schritt 8, MCP in Schritt 9, Production-Deploy in Schritt 10.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer (R5 jetzt umfangreicher), Pflicht-Advisors, alle Hard Rules.

**Phase-1.4-Hinweis:** Plan-Phase-Integration hat in Schritt 6 sehr gut funktioniert (kein separater `claude -p`-Aufruf, Architect-Antworten direkt im Plan-Dokument fixiert). Wähle dasselbe Vorgehen, wenn es sich anbietet — sonst Level 2 (`cat file | claude -p`).

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

Vor Beginn der Implementierung lies in dieser Reihenfolge:

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow. **Achte besonders** auf die Hard Rule zu UI-Komponenten: direkte HTML-Elemente in Pages sind eine CRITICAL-Verletzung, alles muss als wiederverwendbare Komponente in `Components/UI/` leben.
2. **`CLAUDE.md`** im Repo-Root.
3. **`docs/02-architecture.md`**, besonders das Schichtenbild und der UI-Sektion (falls vorhanden — sonst entwirft der Architect die UI-Schicht-Beschreibung in Phase 1.4).
4. **`docs/03-walking-skeleton-plan.md`**, Abschnitt "Schritt 7".
5. **`docs/05-decisions-log.md`**, alle Einträge **D-010 bis D-019**. Besonders **D-019** mit dem `IRunService`-Vertrag und der Cancellation-Watcher-Mechanik.
6. **`docs/reports/step-06-report.md`**, besonders **Sektion 8 (Empfehlungen für Schritt 7)** mit der SignalR-Architektur-Empfehlung.
7. **Aktueller Code im main-Branch (post M1+Schritt-6-Merge):**
   - `src/Geef.Atelier.Application/Runs/IRunService.cs` — die Schnittstelle, die deine UI aufrufen wird.
   - `src/Geef.Atelier.Core/Domain/RunEntity.cs` — was die UI anzeigt (inkl. `CancellationRequested`).
   - `src/Geef.Atelier.Infrastructure/Persistence/PostgresEventSink.cs` — der wird minimal erweitert für Hub-Notifications.
   - `src/Geef.Atelier.Web/Components/UI/SkeletonBanner.razor` — die einzige bisherige UI-Komponente; lerne den Stil kennen.
   - `src/Geef.Atelier.Web/Components/Pages/Index.razor` — die aktuelle Landing-Page, die du erweiterst.
   - `src/Geef.Atelier.Web/Program.cs` — DI-Registrierung; SignalR und der Hub kommen hier rein.
8. **Blazor Server Doku zu:** `@page`-Directive mit Route-Parametern, `IDisposable`/`IAsyncDisposable` in Komponenten, `OnInitializedAsync`/`OnAfterRenderAsync`, `StateHasChanged`/`InvokeAsync`. Besonders: das State-Persistence-Pattern bei Reload (Server-Side bedeutet kein Browser-Storage automatisch — Komponenten-State geht beim Reload verloren).
9. **SignalR Doku zu:** `Hub`-Klasse, `IHubContext<THub>` aus DI, `Clients.Group(...)`, `JoinGroupAsync`/`LeaveGroupAsync`, `await _connection.InvokeAsync(...)` aus dem Client. Besonders: Group-basiertes Routing, damit nur die UI-Clients den richtigen Run sehen.

## In Schritten 1–6+M1 etablierte Realfakten (verbindlich)

Aus D-010 bis D-019. Zentrale Punkte für Schritt 7:

**Application-Schicht (post-M1+Schritt-6-Merge):**
- `IRunService` in `Geef.Atelier.Application/Runs/` ist der einzige Weg für Frontends zur Pipeline-Welt.
- Methoden: `SubmitRunAsync`, `GetRunAsync`, `ListRunsAsync(limit, statusFilter)`, `CancelRunAsync` (`bool`-Return).
- `RunService` ist Scoped — UI-Komponenten lösen es pro Aktion über `IServiceProvider` oder DI.

**Domain-Modell:**
- `RunEntity` mit `Id, CreatedAt, StartedAt?, CompletedAt?, Status, BriefingText, ConfigJson, FinalText?, ErrorMessage?, TokensTotal, CostTotal, CancellationRequested`.
- `RunStatus { Pending, Running, Completed, Failed, Aborted }`.
- `IterationEntity` mit `RunId, IterationNumber, ArtifactText, CreatedAt`.
- `FindingEntity` mit `IterationId, ReviewerName, Severity` (Atelier-Enum: `Critical / Major / Minor / Info`), `Message`.

**Cancellation-Verhalten:**
- `CancelRunAsync` setzt DB-Flag atomar via `RunRepository.RequestCancellationAsync`.
- Cancellation-Watcher im Orchestrator pollt 1s-Intervall (siehe D-019(d)).
- UI sieht Cancellation typischerweise nach 1–2 Sekunden in `Status=Aborted`.

**LLM-Schicht (post-M1):**
- `ILlmClient`/`LlmOptions`/`OpenAiCompatibleClient` — die UI berührt diese Schicht nicht direkt.
- Tokens werden in `RunEntity.TokensTotal` akkumuliert — UI zeigt das in der Detail-Seite.

**UI-Component-Library:**
- `src/Geef.Atelier.Web/Components/UI/` mit `SkeletonBanner.razor` als einzigem bisherigen Element.
- Workflow-Hard-Rule: alle neuen UI-Elemente landen dort, Pages konsumieren nur. Direkte HTML-Elemente in Pages = CRITICAL.

## Konkrete technische Anforderungen für Schritt 7

### Drei Pages in `src/Geef.Atelier.Web/Components/Pages/`

**`New.razor` mit `@page "/new"`:**
- Submit-Form für neuen Run.
- Felder: `Briefing` (mehrzeiliges Textarea, required), `ConfigJson` (optional, default leer → Service normalisiert zu `"{}"`).
- Submit-Button: ruft `IRunService.SubmitRunAsync(briefing, configJson)`, navigiert nach Erfolg auf `/runs/{guid}`.
- Validierung: `Briefing` non-empty (sonst Submit-Button disabled). Server-side Fehler (z.B. ungültiges JSON) als User-friendly Error-Message anzeigen.

**`Runs.razor` mit `@page "/runs"`:**
- Listet die letzten 20 Runs absteigend nach `CreatedAt`.
- Spalten: Status-Badge, Created, Briefing-Snippet (erste 60 Zeichen + Ellipse), Tokens, "Open"-Link.
- Optional: Status-Filter-Dropdown (alle / Pending / Running / Completed / Failed / Aborted) mit URL-Query-Param `?status=Running`.
- "New Run"-Button oben rechts, navigiert zu `/new`.

**`RunDetail.razor` mit `@page "/runs/{RunId:guid}"`:**
- Zeigt Run-Header (Status, Briefing, CreatedAt/StartedAt/CompletedAt, Tokens, ErrorMessage falls vorhanden).
- Iterations-Liste mit ArtifactText (collapsible), Findings (Severity-Badge + Message + Reviewer).
- FinalText falls Run `Completed` ist (Markdown-gerendert wäre nice-to-have, simple `<pre>`-Darstellung reicht für Skeleton).
- Cancel-Button für Runs mit `Status IN (Pending, Running)` und `!CancellationRequested` — optimistisches Update (Button sofort disabled), Server-Confirm via Re-Fetch.
- **Live-Updates via SignalR:** Komponente joined die `run-{RunId}`-Group beim Init, leaved beim Dispose. Hub-Events lösen `StateHasChanged()` aus.

### SignalR-Hub: `src/Geef.Atelier.Web/Hubs/RunHub.cs`

```csharp
public sealed class RunHub : Hub
{
    public Task JoinRunGroupAsync(Guid runId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"run-{runId}");

    public Task LeaveRunGroupAsync(Guid runId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"run-{runId}");
}
```

DI-Registrierung in `Program.cs`:
```csharp
builder.Services.AddSignalR();
// ...
app.MapHub<RunHub>("/hubs/runs");
```

### `PostgresEventSink`-Erweiterung um Hub-Notifications

Minimaler Eingriff am Schritt-4-EventSink: nach jedem persistierten Event zusätzlich eine Hub-Notification an die `run-{RunId}`-Group senden.

```csharp
internal sealed class PostgresEventSink : IGeefEventSink
{
    private readonly IHubContext<RunHub> _hub;
    // ... bestehende Felder ...

    public PostgresEventSink(
        Guid runId,
        IServiceScopeFactory scopeFactory,
        IHubContext<RunHub> hub,         // <-- neu
        ILogger<PostgresEventSink> logger)
    { /* ... */ }

    public async Task HandleEventAsync(IGeefEvent @event, CancellationToken ct)
    {
        // ... bestehende Persistierung ...

        // Neu: Hub-Notification
        await _hub.Clients.Group($"run-{_runId}")
            .SendAsync("RunUpdated", _runId, ct);
    }
}
```

**Wichtig:** Die Hub-Notification trägt nur die `RunId` — die UI lädt Details selbst neu via `IRunService.GetRunAsync`. Das hält die Hub-Nachrichten klein und vermeidet Serialisierungs-Probleme mit Domain-Objekten.

**Architect-Frage:** Sollte das Hub-Event auch den Event-Typ mittragen (z.B. `"PipelineCompletedEvent"`), damit die UI bei großen Updates wie `PipelineCompletedEvent` zwischen Re-Fetch und z.B. einer Toast-Benachrichtigung unterscheiden kann? Oder reicht `"RunUpdated"` ohne weitere Daten? Empfehlung: nur `"RunUpdated"` für Skeleton, Event-Typ-Diskriminierung wenn UI das später braucht.

### UI-Komponenten in `src/Geef.Atelier.Web/Components/UI/`

Alle neuen UI-Elemente kommen hier rein. **Direkte HTML-Elemente in Pages = CRITICAL-Verletzung.**

Pflicht-Komponenten (mindestens):

- **`StatusBadge.razor`** — Visualisierung der `RunStatus`-Werte mit Farbcoding (Pending=neutral, Running=blau-pulse, Completed=grün, Failed=rot, Aborted=orange).
- **`SeverityBadge.razor`** — Visualisierung der Atelier-`FindingSeverity` (Critical=rot, Major=orange, Minor=gelb, Info=grau).
- **`RunCard.razor`** — Kompakte Run-Darstellung für die Liste (`/runs`).
- **`IterationPanel.razor`** — Collapsible Panel für eine Iteration auf der Detail-Seite (ArtifactText + Findings).
- **`SubmitForm.razor`** — Form-Komponente für `/new` mit Validierung.

Stilistische Konvention orientiert sich an `SkeletonBanner.razor`. Architect entscheidet ob CSS isolation (`.razor.css`) genutzt wird oder inline-Tailwind / globale Styles.

### Live-Update-Mechanik in `RunDetail.razor`

```razor
@page "/runs/{RunId:guid}"
@implements IAsyncDisposable
@inject IRunService RunService
@inject NavigationManager Nav
@inject IJSRuntime JS

@code {
    [Parameter] public Guid RunId { get; set; }
    private HubConnection? _hub;
    private RunEntity? _run;
    // ...

    protected override async Task OnInitializedAsync()
    {
        _run = await RunService.GetRunAsync(RunId);

        _hub = new HubConnectionBuilder()
            .WithUrl(Nav.ToAbsoluteUri("/hubs/runs"))
            .WithAutomaticReconnect()
            .Build();

        _hub.On<Guid>("RunUpdated", async (id) =>
        {
            if (id != RunId) return;
            _run = await RunService.GetRunAsync(RunId);
            await InvokeAsync(StateHasChanged);
        });

        await _hub.StartAsync();
        await _hub.SendAsync("JoinRunGroupAsync", RunId);
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            try { await _hub.SendAsync("LeaveRunGroupAsync", RunId); } catch { }
            await _hub.DisposeAsync();
        }
    }
}
```

**Architect-Frage:** Server-Side Blazor + SignalR ist eine ungewöhnliche Kombination, weil Blazor Server selbst eine SignalR-Verbindung zum Browser hat. Lösungs-Optionen:
- (α) Eigene SignalR-`HubConnection` aus dem Browser zum Server (wie oben skizziert) — sauber, aber zusätzlicher Round-Trip.
- (β) Server-internes Eventing über `IRunUpdateNotifier`-Service (Singleton mit Channel<RunId>), das von der Komponente abonniert wird. Pure server-side, kein zweiter SignalR-Channel.

Für Skeleton ist (α) einfacher zu testen und konsistent mit dem späteren MCP-Server (Schritt 9), der ähnliche Hub-Calls machen könnte.

### Tests

**A) Application-Component-Tests** (in `tests/Geef.Atelier.Tests/Web/`):

Neue Test-Familie via [bUnit](https://bunit.dev/) für Blazor-Komponenten-Tests:
- `StatusBadgeRendersExpectedColors` — alle 5 RunStatus-Werte → korrekte CSS-Klasse.
- `SeverityBadgeRendersExpectedIcons` — alle 4 Severity-Werte.
- `RunCardShowsBriefingSnippet` — lange Briefings werden auf 60 Zeichen + Ellipse gekürzt.
- `SubmitFormDisablesButtonOnEmptyBriefing` — Validierung.

**B) Playwright-E2E-Tests** (eigene Test-Familie, wahrscheinlich in einem Subfolder):

R5 wird ab jetzt substantiell. Folgende Flows müssen geprüft werden:
- **`SubmitFlow_E2E`**: Navigiere zu `/new`, fülle Briefing, klick Submit → URL ist `/runs/{guid}`, Status erscheint.
- **`ListFlow_E2E`**: Drei Runs anlegen, navigiere zu `/runs` → drei Cards sichtbar, sortiert nach CreatedAt desc.
- **`LiveUpdateFlow_E2E`**: Submit Run, beobachte Status-Übergang `Pending → Running → Completed` ohne manuellen Reload (SignalR-Live-Update). Timeout 60s.
- **`CancelFlow_E2E`**: Submit Run mit langem Mock-Pipeline-Lauf, klick Cancel → Status wird `Aborted` innerhalb 5 Sekunden.

**Test-Setup für Playwright:**
- `OrchestratorTestHost`-Pendant für Web (z.B. `WebTestHost` mit `WebApplicationFactory<Program>`).
- Testcontainer-Postgres + GatedFakeLlmClient für deterministische Live-Update-Beobachtung.
- Playwright-Browser-Lifecycle: ein Browser pro Test, headless für CI.

**Bestehende Tests:** Alle 31 Tests aus Schritten 1–6 + M1 müssen weiter grün bleiben. Application-Tests aus Schritt 6 verifizieren `IRunService` direkt — die UI testet hier nicht erneut, sondern verlässt sich auf die Application-Schicht.

### `Program.cs`-Erweiterung

```csharp
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSignalR();
// ... (bestehende AddLlmClient, AddAtelierPersistence, AddAtelierApplication, AddHostedService<RunOrchestratorService>)

// nach app.UseRouting / etc.
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapHub<RunHub>("/hubs/runs");
```

### `appsettings.json` ergänzen

Keine neuen UI-spezifischen Settings notwendig im Skeleton. Falls der Architect SignalR-Tuning (z.B. `KeepAliveInterval`) konfigurierbar machen will, kommt das hinein.

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` ohne Fehler oder Warnings.
2. `dotnet test` (mit Docker-Daemon für Testcontainers + Playwright-Browser): alle Tests grün — 31 bestehende + bUnit-Komponenten-Tests + Playwright-E2E-Tests.
3. **`SubmitFlow_E2E`** zeigt End-to-End-Submit über die UI.
4. **`LiveUpdateFlow_E2E`** zeigt Status-Übergang `Pending → Running → Completed` ohne manuellen Reload — SignalR funktioniert.
5. **`CancelFlow_E2E`** zeigt funktionierenden Cancel-Button-Pfad bis `Status=Aborted`.
6. UI-Komponenten ausschließlich in `Components/UI/`, keine direkten HTML-Elemente in Pages (Workflow-CRITICAL).
7. R5 (Playwright) prüft nicht nur Heading-Sanity, sondern alle drei Pages mit interaktiven Flows.
8. **AC8 (Hard-Gate vor Schritt 9): `AtelierPipelineRunsAgainstOpenRouter` läuft mindestens einmal mit echtem Bearer-Key grün.** Token-Verbrauch und Latenz im Bericht festhalten. Wenn Claude Code keinen Bearer-Key hat: User muss vor Schritt-7-Ausführung einen `Llm__ApiKey` als Env-Var bereitstellen. **Skip ist nicht erlaubt.** Wenn nach drei Versuchen kein Key verfügbar ist, halt-and-escalate an den User.
9. `geef_architecture.md` existiert (R4-Pflicht).

## Was du in diesem Schritt NICHT tust

- **Keine Auth** — Schritt 8. Bis dahin ist die UI öffentlich (auf `localhost`).
- **Kein MCP** — Schritt 9.
- **Kein Production-Deploy** — Schritt 10.
- **Keine Cost-Anzeige** — `RunEntity.CostTotal` bleibt 0, kein Modell→Preis-Mapping.
- **Keine Markdown-Renderer-Library** — `FinalText` als `<pre>` reicht. Schickere Darstellung wäre Post-Skeleton.
- **Keine Pagination** — `/runs` zeigt einfach die letzten 20.
- **Keine Search** — kommt mit MCP oder später.
- **Kein PWA / Offline** — Server-Side-Blazor reicht.
- **Keine Provider-Änderungen** — `OpenAiCompatibleClient`, `LlmExecutionStep`, `LlmReviewer` bleiben unverändert.

## Architect-Konsultation (Phase 1.4) — sechs Schwerpunkte

1. **SignalR-Mechanik:** Eigene `HubConnection` aus Browser (α) vs. Server-internes `IRunUpdateNotifier` (β)? Empfehlung von hier: α für Skeleton.
2. **Hub-Event-Granularität:** Nur `"RunUpdated"` mit `RunId` (UI lädt selbst neu) vs. typisierte Events mit Payload? Empfehlung: nur RunId.
3. **CSS-Isolation in Komponenten:** `.razor.css`-Files pro Komponente vs. zentraler Stylesheet vs. inline-Tailwind? Architect entscheidet — wichtig für die Konsistenz mit `SkeletonBanner.razor`.
4. **Form-Validation-Library:** `EditForm` + `DataAnnotationsValidator` (Standard-Blazor) vs. custom mit `OnValidSubmit` vs. FluentValidation? Für Skeleton reicht Standard.
5. **Test-Host-Setup:** `WebApplicationFactory<Program>` (Standard) vs. eigener `WebTestHost` analog zu `OrchestratorTestHost` aus Schritt 5? Letzteres ist konsistenter mit existierender Test-Infrastruktur.
6. **`PostgresEventSink`-Konstruktor-Erweiterung:** Direkt `IHubContext<RunHub>` injizieren vs. Wrapper-Service `IRunNotifier` mit Methode `NotifyAsync(Guid)`. Wrapper macht den Sink testbarer (kann ohne SignalR-Setup unit-getestet werden), kostet aber eine Indirektion.

`geef_architecture.md` prüft Konsistenz mit `docs/02-architecture.md` — und sollte die UI-Architektur formal beschreiben (drei Pages, SignalR-Hub, Komponenten-Library), falls das Architecture-Doc noch keine UI-Sektion hat.

## Persistenter Abschlussbericht für den Brainstorming-Chat

Bericht nach `docs/reports/step-07-report.md`, gleicher Aufbau wie Schritte 1–6. Wichtig in diesem Schritt:

1. **Was wurde umgesetzt** — Datei-für-Datei. Komponenten- und Page-Struktur deutlich.
2. **Annahmen und Abweichungen** — vor allem zu SignalR-Mechanik, Form-Validation-Strategy, Hub-Event-Granularität.
3. **Architect-Output** — alle sechs Schwerpunkte.
4. **Pre-Mortem & Devil's Advocate** — speziell zu: Connection-Leak in Blazor-Komponenten, Race zwischen Cancel-Click und Pipeline-Completion, Reload-Verhalten bei aktivem Run, SignalR-Reconnect-Verhalten bei Server-Neustart.
5. **Reviewer-Iterationen** — Tabelle. **R5 ist diesmal substantiell** — alle vier E2E-Flows.
6. **Akzeptanzkriterien-Check** — Tabelle, **inklusive AC8 (echter OpenRouter-Test grün)**.
7. **Beobachtungen** — Blazor-Server + SignalR-Verhalten, Komponenten-Reusability, UI-Performance.
8. **Empfehlungen für Schritt 8 (Auth)** — Cookie-Strategie für UI, Single-User-Setup, wie SignalR-Hub-Authentication zu handhaben ist.
9. **Status AC8** — echter OpenRouter-Test grün gelaufen oder halt-and-escalate.

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- API-Key niemals in source control, niemals in Logs, niemals im Bericht.
- **UI-Komponenten ausschließlich in `Components/UI/`** — direkte HTML-Elemente in Pages = CRITICAL.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.