# Claude-Code-Prompt: Schritt 9 — MCP-Server mit Bearer-Token-Auth

*Diese Datei ist als Eingabe für Claude Code gedacht.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Schritte 1–8 + M1 sind abgeschlossen und in main: das System hat eine Cookie-geschützte UI mit Live-Updates via SignalR, läuft mit OpenRouter als LLM-Provider, alle 71 Tests grün, AC8 (Real-Pipeline-Test) verifiziert, App läuft auf `95.216.100.213:8080`. Deine Aufgabe ist **Schritt 9 von 10**: ein **MCP-Server als zweites Frontend**, mit Bearer-Token-Auth, parallel zur bestehenden Web-UI.

Was sich ändert: Ein neues Projekt `Geef.Atelier.Mcp` mit MCP-Server-Implementation. Multi-Auth-Schema in `Program.cs` (Cookie für UI, Bearer für MCP). Ein neuer `ITokenValidator` in Application/Auth/. Sechs MCP-Tools, die `IRunService` aufrufen. Eine Migration für `RunEntity.CreatedByUser` (Audit-Trail-Vorbereitung). Was bleibt unverändert: Pipeline, Orchestrator, Persistierung, LLM-Schicht, Application-Service-Layer (außer Audit-Trail-Setting), UI, Cookie-Auth.

Das ist der **erste Schritt mit zwei parallel laufenden Frontends** über denselben Application-Service. Wenn die Schichten-Disziplin der bisherigen Schritte sauber war, sollte MCP keinerlei Eingriff in Pipeline-/Domain-/Orchestrator-Code erfordern.

Production-Deploy in Schritt 10 (das ist nach Schritt-8-Bericht vereinfacht zu Routing-und-Domain-Setup, weil App bereits auf der Server-IP läuft).

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors, alle Hard Rules.

**Phase-1.4-Hinweis:** Plan-Phase-Integration ist seit Schritt 5 etabliert (siehe D-016 bis D-021). Sechs Architect-Schwerpunkte direkt im Plan-Dokument fixieren — kein separater `claude -p`-Aufruf erforderlich.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

Vor Beginn der Implementierung lies in dieser Reihenfolge:

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`CLAUDE.md`** im Repo-Root.
3. **`docs/01-vision-and-scope.md`** — die ursprüngliche Vision der MCP-Integration als zweite Schnittstelle für KI-Agenten.
4. **`docs/02-architecture.md`**, besonders das Schichtenbild und die Auth-Sektion (aus Schritt 8 ergänzt).
5. **`docs/03-walking-skeleton-plan.md`**, Abschnitt "Schritt 9".
6. **`docs/04-mcp-integration.md`** — die ursprüngliche MCP-Server-Spezifikation aus dem Brainstorming. Wahrscheinlich enthält sie eine Vorab-Tool-Liste, die du als Ausgangspunkt nutzen kannst.
7. **`docs/05-decisions-log.md`**, alle Einträge **D-010 bis D-021**. Besonders **D-021** mit den Schritt-8-Realfakten (Auth-Pattern, Multi-Auth-Schema-Vorbereitung).
8. **`docs/reports/step-08-report.md`**, besonders **Sektion 8 (Empfehlungen für Schritt 9)** mit den fünf konkreten Empfehlungen: Multi-Auth-Schema, `ITokenValidator`-Position, kein Cross-Auth, Test-MCP-Bypass, Audit-Trail-Vorbereitung.
9. **Aktueller Code im Repo:**
   - `src/Geef.Atelier.Application/Auth/IUserAuthenticator.cs` und `AtelierUserAuthenticator.cs` — das Symmetrie-Vorbild für `ITokenValidator`.
   - `src/Geef.Atelier.Application/Runs/IRunService.cs` — die Schnittstelle, die deine MCP-Tools aufrufen werden.
   - `src/Geef.Atelier.Application/Auth/ApplicationAuthExtensions.cs` — Auth-DI-Setup-Pattern.
   - `src/Geef.Atelier.Web/Program.cs` — Multi-Auth-Schema kommt hier rein.
   - `src/Geef.Atelier.Web/Endpoints/AuthEndpoints.cs` — Endpoint-Mapping-Pattern.
   - `src/Geef.Atelier.Core/Domain/RunEntity.cs` — bekommt `CreatedByUser`-Property.
10. **MCP-Spezifikation** (`https://modelcontextprotocol.io/`): JSON-RPC 2.0 als Transport, Server-Capabilities-Manifest, Tool-Definitionen mit JSON-Schema, Resources, Prompts. Transports: stdio, HTTP+SSE, WebSocket.
11. **NuGet-Suche für MCP-.NET-Libraries:** `ModelContextProtocol` (offiziell oder Community-Paket prüfen), Alternativen. Wenn keine reife Library existiert: eigene JSON-RPC-Implementation auf Basis von ASP.NET Core ist machbar.

## In Schritten 1–8+M1 etablierte Realfakten (verbindlich)

Aus D-010 bis D-021. Zentrale Punkte für Schritt 9:

**Application-Schicht (post-Schritt-8):**
- `IRunService` mit `SubmitRunAsync`, `GetRunAsync`, `ListRunsAsync`, `CancelRunAsync` — das ist die einzige Schnittstelle, die MCP-Tools aufrufen.
- `IUserAuthenticator` mit BCrypt-basierter Implementation — Symmetrie-Vorbild für `ITokenValidator`.
- Application-Projekt referenziert nur Core, keine Infrastructure-Dep.
- DI-Registrierung via `AddAtelierAuth()` und `AddAtelierApplication()` als Extension-Pattern.

**Auth-Schicht (post-Schritt-8):**
- Cookie-basiert für UI mit `AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(...)`.
- `[Authorize]` auf Pages, **nicht** auf `RunHub` (server-seitige Blazor-Connections übertragen keine Cookies — Mitigation: Pages tragen `[Authorize]`).
- `AtelierUserOptions` aus Env-Vars (`ATELIER_USER` + `ATELIER_PASSWORD_HASH`).
- Lazy-Fail bei fehlenden Credentials (Service startet, Login schlägt still fehl, Health-Check bleibt grün).
- `CryptographicOperations.FixedTimeEquals` für Username-Vergleich (Constant-Time gegen Timing-Attacks).

**Test-Infrastruktur:**
- `WebTestHost` mit `authenticated`-Parameter — `TestAuthenticationHandler` simuliert eingeloggten User.
- Selector-Konvention in Playwright: spezifische CSS-Klassen statt generische `button[type='submit']` (siehe Schritt-8-Bericht-Sektion 7).
- 71/71 bestehende Tests müssen grün bleiben.

**Domain-Modell:**
- `RunEntity` ohne `CreatedByUser` aktuell — bekommt das Property in Schritt 9 als Audit-Trail-Vorbereitung.

**App-Erreichbarkeit:**
- Läuft auf `95.216.100.213:8080` (Container direkt erreichbar, kein Traefik-Routing).
- Traefik-Routing und Domain `geef.stefan-bechtel.de` kommen in Schritt 10.

## Konkrete technische Anforderungen für Schritt 9

### Neues Projekt: `src/Geef.Atelier.Mcp/`

`Geef.Atelier.Mcp.csproj` referenziert:
- `Geef.Atelier.Core` (Domain-Records für Tool-Antworten)
- `Geef.Atelier.Application` (`IRunService`)
- MCP-Library (Architect entscheidet — siehe F1 unten)
- `Microsoft.Extensions.Hosting` (für DI/Hosted-Service-Pattern)

**Architekturelle Trennung:**
- `Geef.Atelier.Mcp` enthält die MCP-Tool-Implementationen.
- `Geef.Atelier.Web` hostet den MCP-Endpoint unter `/mcp/...` (oder bietet ihn als separaten Listener — siehe F3 unten).
- `Geef.Atelier.Mcp` darf **nicht** direkt auf `Geef.Atelier.Infrastructure` oder Pipeline-Code zugreifen — nur über `IRunService`.

### `ITokenValidator` in `src/Geef.Atelier.Application/Auth/`

Symmetrisch zu `IUserAuthenticator`:

```csharp
public interface ITokenValidator
{
    Task<bool> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default);
}

internal sealed class StaticTokenValidator : ITokenValidator
{
    private readonly IOptions<AtelierMcpOptions> _options;
    // Constant-Time-Vergleich (CryptographicOperations.FixedTimeEquals)
    // Token kommt aus AtelierMcpOptions.Token (Env-Var ATELIER_MCP_TOKEN)
}
```

`AtelierMcpOptions` in `Geef.Atelier.Core/Configuration/`:
```csharp
public sealed class AtelierMcpOptions
{
    public string Token { get; set; } = "";
    public const string SectionName = "AtelierMcp";
}
```

Env-Var: `ATELIER_MCP_TOKEN`. Lazy-Fail bei leerem Token (Service startet, Bearer-Auth schlägt still fehl).

### Multi-Auth-Schema in `Program.cs`

```csharp
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(/* bestehende Konfig aus Schritt 8 */)
    .AddScheme<AuthenticationSchemeOptions, BearerTokenHandler>(
        "Bearer", _ => { });
```

**`BearerTokenHandler`** als `internal sealed class : AuthenticationHandler<AuthenticationSchemeOptions>` in `Geef.Atelier.Web/Auth/`:
- Liest `Authorization: Bearer <token>`-Header.
- Ruft `ITokenValidator.ValidateTokenAsync(token)`.
- Bei Erfolg: gibt `AuthenticationResult.Success` mit `ClaimsPrincipal` zurück (Claims: `Name="mcp-client"`, `AuthenticationType="Bearer"`).
- Bei Fehler: `AuthenticationResult.NoResult()` oder `Fail()`. Keine Logging des Token-Werts.

**Endpoint-Authorization:**
- UI-Pages: behalten Cookie-Auth (Default-Scheme).
- MCP-Endpoints: `[Authorize(AuthenticationSchemes = "Bearer")]`.

**Architect-Frage:** Wie geht `IAuthorizationPolicyProvider` mit gemischten Schemes um? Mein Verständnis: Default-Scheme bleibt Cookie für `[Authorize]` ohne Angabe; nur explizit `[Authorize(AuthenticationSchemes = "Bearer")]` triggert Bearer-Validierung. Validiert durch Architect.

### Domain-Erweiterung: `RunEntity.CreatedByUser`

```csharp
public string? CreatedByUser { get; init; }
```

Nullable, weil:
- Bestehende Daten haben den Wert nicht (Migration setzt Default `NULL`).
- Aus Schritt 7 stammen Runs ohne Auth-Info.

EF-Migration `Step09AuditTrail`:
```sql
ALTER TABLE "Runs" ADD COLUMN "CreatedByUser" text NULL;
```

`IRunService.SubmitRunAsync` muss erweitert werden um `createdByUser`-Parameter:
```csharp
Task<Guid> SubmitRunAsync(
    string briefingText,
    string configJson,
    string? createdByUser = null,
    CancellationToken cancellationToken = default);
```

**Aufrufer:**
- UI-Page `New.razor` — liest Username aus `AuthenticationState.User.Identity.Name` (Cookie-Auth → echter Username).
- MCP-Tool `submit_request` — übergibt `"mcp-client"` als String (oder aus Claims des Bearer-Auth-Schemes).

Bestehende Tests müssen entweder den neuen Default-Parameter ignorieren (Backward-Compat-Default `null`) oder explizit setzen. Architect entscheidet, ob der Parameter `optional null` oder `required string` ist.

### MCP-Tools

Sechs Tools, die `IRunService` aufrufen. Tool-Set entspricht `04-mcp-integration.md`-Spec:

#### `submit_request`
- **Input:** `{ "briefing_text": string, "config_json": string? }`
- **Output:** `{ "run_id": string (Guid), "status": "Pending" }`
- **Implementation:** `IRunService.SubmitRunAsync(briefing, configJson ?? "", "mcp-client")` → `runId.ToString()`.

#### `get_run_status`
- **Input:** `{ "run_id": string (Guid) }`
- **Output:** `{ "status": string, "created_at": string (ISO-8601), "started_at": string?, "completed_at": string?, "tokens_total": int, "cancellation_requested": bool }`
- **Implementation:** `IRunService.GetRunAsync(Guid.Parse(runId))` → Status-Felder mappen. `null` wenn Run nicht gefunden → JSON-RPC-Error.

#### `get_run_result`
- **Input:** `{ "run_id": string }`
- **Output:** `{ "status": string, "final_text": string?, "error_message": string? }`
- **Implementation:** `IRunService.GetRunAsync(...)` → final_text + error_message direkt zurückgeben.

#### `list_runs`
- **Input:** `{ "limit": int? (default 20), "status_filter": string? }`
- **Output:** Array von Run-Summaries: `[{ "run_id": string, "status": string, "briefing_snippet": string, "created_at": string, "tokens_total": int }]`
- **Implementation:** `IRunService.ListRunsAsync(limit, parseStatus(statusFilter))`.

#### `get_run_details`
- **Input:** `{ "run_id": string }`
- **Output:** Run + Iterations + Findings als verschachtelte JSON-Struktur.
- **Implementation:** `IRunService.GetRunAsync(...)` mit Eager-Loading (siehe D-019 — derzeit kein Eager-Loading, MCP braucht es). **Architect-Frage:** Wird `GetRunAsync` für MCP erweitert um Iterations/Findings, oder gibt es einen neuen `GetRunDetailsAsync`?

#### `cancel_run`
- **Input:** `{ "run_id": string }`
- **Output:** `{ "cancelled": bool }`
- **Implementation:** `IRunService.CancelRunAsync(runId)` → `bool` direkt zurückgeben.

### MCP-Server-Endpoint

**Architect-Schwerpunkt F2:** Transport-Form. Drei Optionen:
- **(α) HTTP + SSE** unter `/mcp/sse` — Standard für Web-basierte MCP-Clients (Claude Desktop, Claude Code als Remote).
- **(β) WebSocket** unter `/mcp/ws` — bidirektional, niedrigere Latenz, aber komplexer.
- **(γ) Stdio** als separater Process — nur für lokale Claude-Desktop-Integration auf demselben Host.

Empfehlung von hier: **(α) HTTP + SSE** für Production. Stdio kann später als zweiter Adapter ergänzt werden, wenn ein lokaler Use-Case dazu kommt.

### Tests

**Application-Tests** in `tests/Geef.Atelier.Tests/Application/Auth/`:
1. `StaticTokenValidatorAcceptsConfiguredTokenTests` — korrekter Token → `true`.
2. `StaticTokenValidatorRejectsWrongTokenTests` — falscher Token → `false`.
3. `StaticTokenValidatorRejectsWhenNotConfiguredTests` — leere `AtelierMcpOptions` → `false`.

**Bearer-Handler-Integration-Tests** in `tests/Geef.Atelier.Tests/Web/Auth/`:
1. `BearerTokenHandlerAcceptsValidTokenTests` — Request mit gültigem Token → 200.
2. `BearerTokenHandlerRejectsInvalidTokenTests` — Request mit falschem Token → 401.
3. `BearerTokenHandlerRejectsMissingTokenTests` — Request ohne Header → 401.
4. `BearerTokenHandlerDoesNotInterfereWithCookieAuthTests` — Request an UI-Page mit Cookie → 200 (kein 401 weil Bearer-Header fehlt).

**MCP-Tool-Integration-Tests** in `tests/Geef.Atelier.Tests/Mcp/`:
1. `SubmitRequestToolCallsRunServiceTests` — Mock `IRunService`, Tool-Call → `SubmitRunAsync` mit korrekten Parametern + `createdByUser="mcp-client"`.
2. `GetRunStatusToolReturnsStatusTests` — End-to-End mit `WebTestHost` (oder eigenem MCP-Test-Host), echter MCP-Tool-Call → korrekte JSON-Response.
3. `CancelRunToolReturnsBooleanTests` — analog.

**E2E-MCP-Test** in `tests/Geef.Atelier.Tests/Mcp/E2E/`:
1. `McpServerEndToEndFlowTests` — startet `WebTestHost`, sendet JSON-RPC-Request gegen `/mcp/sse` (oder gewählten Transport), verifiziert Tool-Antwort. Kann FakeLlmClient verwenden, Test ist Token-Auth-fokussiert, nicht Pipeline-fokussiert.

**Bestehende 71 Tests müssen grün bleiben.** Insbesondere die UI-E2E-Tests dürfen vom Multi-Auth-Schema nicht gestört werden. `WebTestHost` muss möglicherweise erweitert werden um Bearer-Auth-Setup für MCP-Tests.

### Setup-Doku im `README.md`

Ergänze Setup-Sektion mit:
- `ATELIER_MCP_TOKEN` Env-Var-Setup für lokale Entwicklung und Docker-Compose.
- Token-Generation-Anleitung (zufälliger 256-bit-Wert, z.B. `openssl rand -hex 32`).
- MCP-Client-Konfiguration-Beispiel für Claude Desktop oder Claude Code: wie verbindet sich ein externer Agent mit dem Geef.Atelier-MCP-Server.

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` ohne Fehler oder Warnings.
2. `dotnet test` — alle bestehenden 71 Tests + neue Auth-/MCP-/Tool-Tests grün.
3. **`StaticTokenValidator*Tests`** verifizieren Token-Validierung in allen Szenarien.
4. **`BearerTokenHandler*Tests`** verifizieren Multi-Auth-Schema (Cookie funktioniert weiter, Bearer-Endpoint schützt korrekt).
5. **MCP-Tool-Integration-Tests** zeigen, dass alle sechs Tools `IRunService` korrekt aufrufen.
6. **`McpServerEndToEndFlowTests`** zeigt End-to-End MCP-Server-Erreichbarkeit.
7. **R5 (Playwright):** Bestehende Auth-Flows aus Schritt 8 laufen weiter, plus eine manuelle MCP-Verifikation (mit `curl` oder einem MCP-Client) — JSON-RPC-Request gegen den Server liefert eine valide Antwort.
8. **AC8 (Schritt-9-Erweiterung):** MCP-Server ist mit einem **realen MCP-Client** (Claude Code als Subagent oder Claude Desktop) erreichbar und kann mindestens einen Tool-Call durchführen. Token via `ATELIER_MCP_TOKEN`-Env-Var. Wenn Setup zu komplex für die Test-Session: Skip mit klarer Begründung im Bericht, aber Logging der versuchten Schritte.
9. README aktualisiert mit MCP-Setup-Doku.
10. `RunEntity.CreatedByUser` Migration läuft sauber gegen bestehende DB (Test-DB und ggf. Container-DB auf `95.216.100.213`).

## Was du in diesem Schritt NICHT tust

- **Keine Pipeline-Änderungen** — MCP ist ein Adapter über `IRunService`. Pipeline-/Domain-/Orchestrator-Code bleibt unverändert (außer `RunEntity.CreatedByUser`).
- **Kein Multi-User-MCP** — ein einziger Bearer-Token, kein Token-Management, keine Token-Rotation. Wenn der Token kompromittiert ist: Env-Var-Update + Service-Restart.
- **Keine Streaming-MCP-Antworten** — auch wenn MCP-Spec Streaming unterstützt: für Skeleton reichen synchrone Tool-Antworten. SignalR-Live-Updates bleiben UI-only.
- **Keine MCP-Resources oder MCP-Prompts** — nur Tools. Resources und Prompts könnten in einem späteren Schritt ergänzt werden.
- **Kein Production-Deploy** — Schritt 10.
- **Keine Audit-Log-Tabelle** — `CreatedByUser` ist Vorbereitung, vollständiges Audit-Log (z.B. wer hat wann was gemacht) kommt später.

## Architect-Konsultation (Phase 1.4) — sechs Schwerpunkte

1. **MCP-Library-Wahl:** Existiert eine reife `ModelContextProtocol`-NuGet-Library, oder ist eigene JSON-RPC-Implementation auf Basis von ASP.NET Core sinnvoller? **Recherche-Pflicht:** zum Zeitpunkt der Phase 1 das aktuelle Package-Ökosystem prüfen. Wenn Library: Reife/Aktivität/Vertragsstabilität bewerten. Wenn Eigenbau: Aufwand-Schätzung.
2. **MCP-Transport-Form:** HTTP+SSE (α), WebSocket (β), oder Stdio (γ)? Empfehlung: α. Architect validiert mit Blick auf MCP-Client-Erreichbarkeit aus Claude Desktop/Code.
3. **Endpoint-Position:** MCP unter `/mcp/...` im Web-Host integriert, oder eigener Listener-Port/Container? Empfehlung: im Web-Host integriert (einfacher, ein Container, gemeinsame Auth).
4. **`SubmitRunAsync`-Parameter-Erweiterung:** `string? createdByUser` als optionaler Default-`null`-Parameter (Backward-Compat) oder `required string` (Breaking-Change mit Test-Anpassung)? Empfehlung: optional `null` für Backward-Compat.
5. **`GetRunDetailsAsync` vs. `GetRunAsync` mit Eager-Loading:** MCP-Tool `get_run_details` braucht Iterations + Findings. Bestehender `GetRunAsync` lädt das nicht (Skeleton-YAGNI aus D-019). Neue Methode oder Erweiterung? Empfehlung: neue Methode `GetRunDetailsAsync(Guid)`, damit `GetRunAsync` schlank bleibt.
6. **MCP-Client-Identifikation:** `"mcp-client"` als statischer String, oder pro Token unterschiedliche Client-IDs (z.B. `"mcp-claude-desktop"`, `"mcp-claude-code"`)? Für Skeleton-Single-Token reicht statisch — bei Multi-Token später erweitern.

Plan-Phase-Integration: Die sechs Antworten gehören direkt in den Plan-Phase-Output, kein separater Architect-Aufruf nötig.

## Persistenter Abschlussbericht für den Brainstorming-Chat

Bericht nach `docs/reports/step-09-report.md`, gleicher Aufbau wie Schritte 1–8. Wichtig in diesem Schritt:

1. **Was wurde umgesetzt** — Datei-für-Datei. Vor allem: MCP-Library-Wahl + Begründung, Transport-Form, Tool-Implementation-Details.
2. **Annahmen und Abweichungen** — vor allem zu MCP-Library-Stabilität, JSON-RPC-Edge-Cases, Multi-Auth-Schema-Subtilitäten.
3. **Architect-Output** — alle sechs Schwerpunkte als Plan-Phase-Output.
4. **Pre-Mortem & Devil's Advocate** — speziell zu: Token-Leak (in Logs, in Test-Outputs), Race zwischen Bearer-Auth und Cookie-Auth, MCP-Client-Disconnect-Verhalten, SSE-Connection-Limits.
5. **Reviewer-Iterationen** — Tabelle.
6. **Akzeptanzkriterien-Check** — Tabelle, inklusive AC8 (Real-MCP-Client-Test) und AC10 (Migration sauber gelaufen).
7. **Beobachtungen** — MCP-.NET-Ökosystem-Status, JSON-RPC-Implementation-Subtilitäten, Multi-Auth-Schema-Konfiguration.
8. **Empfehlungen für Schritt 10 (Production-Deploy)** — was sind die finalen Schritte für `geef.stefan-bechtel.de`? Welche Migration-Strategie für Production (Auto-on-Startup bleibt, oder Init-Container)? Welche Env-Var-Liste muss in `docker-compose.yml`?
9. **Status MCP-Real-Test (AC8)** — gelang die Verbindung aus einem echten MCP-Client (Claude Desktop, Claude Code, oder curl)? Token-Verbrauch in einem Test-Tool-Call?

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- **Niemals Token-Werte in Logs.** Bei Validierung-Logging: nur "Token accepted" / "Token rejected" ohne PII.
- API-Key niemals in source control, niemals in Logs, niemals im Bericht.
- Test-Tokens dürfen im Test-Code Klartext sein, aber nicht in `appsettings.json` oder anderswo in main.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.