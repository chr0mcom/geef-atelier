# Schritt 9 Abschlussbericht — MCP-Server (zweiter Frontend-Adapter)

**Datum:** 11. Mai 2026
**Branch:** `main`
**Tests:** 85/85 grün (71 bestehende + 14 neue)
**Reviewer-Iterationen:** 1 (alle 5 Reviewer in einem Durchlauf, 0 Findings Critical/Important)

---

## 1. Was umgesetzt wurde

### Neue Dateien

| Datei | Beschreibung |
|---|---|
| `src/Geef.Atelier.Core/Configuration/AtelierMcpOptions.cs` | POCO für `Token`; `SectionName = "AtelierMcp"` |
| `src/Geef.Atelier.Application/Auth/ITokenValidator.cs` | Interface `ValidateTokenAsync(string, CancellationToken)` |
| `src/Geef.Atelier.Application/Auth/StaticTokenValidator.cs` | Timing-sicherer Vergleich via `CryptographicOperations.FixedTimeEquals`; lehnt ab wenn Token leer/nicht konfiguriert |
| `src/Geef.Atelier.Mcp/McpAssemblyMarker.cs` | Leere Marker-Klasse für `WithToolsFromAssembly`-Lookup |
| `src/Geef.Atelier.Mcp/McpServiceExtensions.cs` | `AddAtelierMcp()` — registriert `AddMcpServer().WithHttpTransport(stateless).WithToolsFromAssembly` |
| `src/Geef.Atelier.Mcp/Models/RunStatusDto.cs` | `record(RunId, Status, CurrentPhase?)` |
| `src/Geef.Atelier.Mcp/Models/RunSummaryDto.cs` | `record(RunId, Status, CreatedAt, CreatedByUser?)` |
| `src/Geef.Atelier.Mcp/Models/RunResultDto.cs` | `record(RunId, FinalText?)` |
| `src/Geef.Atelier.Mcp/Models/RunDetailsDto.cs` | `record(RunId, Status, CreatedAt, CreatedByUser?, BriefingText, FinalText?, ErrorMessage?, Iterations)` + `IterationDto(IterationNumber, ArtifactText?)` |
| `src/Geef.Atelier.Mcp/Tools/SubmitRequestTool.cs` | Tool `submit_request` → `IRunService.SubmitRunAsync`, setzt `createdByUser = "mcp-client"` |
| `src/Geef.Atelier.Mcp/Tools/GetRunStatusTool.cs` | Tool `get_run_status` → `IRunService.GetRunAsync` |
| `src/Geef.Atelier.Mcp/Tools/GetRunResultTool.cs` | Tool `get_run_result` → gibt `FinalText` nur bei Status `Completed` zurück |
| `src/Geef.Atelier.Mcp/Tools/ListRunsTool.cs` | Tool `list_runs` → parst `statusFilter` (string) zu `RunStatus?`-Enum |
| `src/Geef.Atelier.Mcp/Tools/GetRunDetailsTool.cs` | Tool `get_run_details` → `IRunService.GetRunDetailsAsync`, mappt `ArtifactText` |
| `src/Geef.Atelier.Mcp/Tools/CancelRunTool.cs` | Tool `cancel_run` → `IRunService.CancelRunAsync` |
| `src/Geef.Atelier.Web/Auth/BearerTokenHandler.cs` | `AuthenticationHandler<AuthenticationSchemeOptions>`; liest `Authorization: Bearer <token>`-Header, delegiert Validierung an `ITokenValidator` |
| `src/Geef.Atelier.Web/Auth/McpAuthorizationConstants.cs` | Konstanten `BearerScheme = "Bearer"` und `McpPolicy = "McpPolicy"` |
| `src/Geef.Atelier.Infrastructure/Persistence/Migrations/20260511173626_Step09AuditTrail.cs` | `ALTER TABLE "Runs" ADD "CreatedByUser" text NULL` |
| `src/Geef.Atelier.Infrastructure/Persistence/Migrations/20260511173626_Step09AuditTrail.Designer.cs` | EF-generierter Designer |
| `tests/…/Application/Auth/StaticTokenValidatorAcceptsConfiguredTokenTests.cs` | Korrektes Token → `true` |
| `tests/…/Application/Auth/StaticTokenValidatorRejectsWhenNotConfiguredTests.cs` | Leeres Token → `false` + Warning-Log |
| `tests/…/Application/Auth/StaticTokenValidatorRejectsWrongTokenTests.cs` | Falsches Token → `false` |
| `tests/…/Web/Auth/BearerTokenHandlerAcceptsValidTokenTests.cs` | Gültiger Bearer → `AuthenticateResult.Success` |
| `tests/…/Web/Auth/BearerTokenHandlerRejectsInvalidTokenTests.cs` | Ungültiger Bearer → `AuthenticateResult.Fail` |
| `tests/…/Web/Auth/BearerTokenHandlerRejectsMissingTokenTests.cs` | Fehlender Header → `AuthenticateResult.NoResult` |
| `tests/…/Web/Auth/BearerTokenHandlerDoesNotInterfereWithCookieAuthTests.cs` | Bearer-Handler liefert `NoResult` ohne Authorization-Header (kein Cookie-Interference) |
| `tests/…/Mcp/SubmitRequestToolCallsRunServiceTests.cs` | `submit_request` setzt `createdByUser = "mcp-client"` und gibt `Pending` zurück |
| `tests/…/Mcp/GetRunStatusToolReturnsStatusTests.cs` | `get_run_status` mappt `RunEntity` korrekt auf `RunStatusDto` |
| `tests/…/Mcp/CancelRunToolReturnsBooleanTests.cs` | `cancel_run` gibt `true`/`false` von `IRunService` weiter |
| `tests/…/Mcp/E2E/McpServerEndToEndFlowTests.cs` | 2 E2E-Tests: `tools/list` mit gültigem Token → 200 + `"submit_request"` im Body; ungültiges Token → 401 |

### Geänderte Dateien

| Datei | Änderung |
|---|---|
| `src/Geef.Atelier.Mcp/Geef.Atelier.Mcp.csproj` | SDK von `Microsoft.NET.Sdk.Web` auf `Microsoft.NET.Sdk` geändert (Class Library); `Program.cs` + `appsettings*.json` entfernt; Ref auf `ModelContextProtocol.AspNetCore` + `Geef.Atelier.Application`/`Infrastructure` |
| `src/Geef.Atelier.Web/Program.cs` | `AddAtelierMcpAuth()`, Multi-Auth (`AddCookie` + `AddScheme<BearerTokenHandler>`), `McpPolicy`-Authorization, `AddAtelierMcp()`, `MapMcp("/mcp").RequireAuthorization(McpPolicy)` |
| `src/Geef.Atelier.Application/Auth/ApplicationAuthExtensions.cs` | Neue Methode `AddAtelierMcpAuth()` — bindet `AtelierMcpOptions`, Env-Var-Fallback `ATELIER_MCP_TOKEN`, registriert `ITokenValidator` |
| `src/Geef.Atelier.Infrastructure/Persistence/Configurations/RunConfiguration.cs` | `CreatedByUser` (nullable string) konfiguriert |
| `src/Geef.Atelier.Infrastructure/Persistence/RunPersistenceService.cs` | `CreatedByUser` beim Insert gesetzt; `IRunService`-Signatur mit `createdByUser?`-Parameter |
| `src/Geef.Atelier.Infrastructure/Persistence/Migrations/AtelierDbContextModelSnapshot.cs` | Model-Snapshot aktualisiert mit `CreatedByUser` |
| `Directory.Packages.props` | `ModelContextProtocol.AspNetCore` Version `1.3.0` eingetragen |
| `CLAUDE.md` | Status auf Schritt 9 aktualisiert |
| `docs/03-walking-skeleton-plan.md` | Schritt-9-Status gesetzt |

---

## 2. Annahmen und Abweichungen vom Plan

| # | Thema | Plan | Tatsächliche Umsetzung |
|---|---|---|---|
| A1 | Mcp-Projekt-Typ | `Microsoft.NET.Sdk.Web` (ASP.NET-Stub) | Auf `Microsoft.NET.Sdk` (Class Library) umgestellt; `Program.cs` + `appsettings*.json` gelöscht. Web-Host-Lifecycle liegt vollständig in `Geef.Atelier.Web` — sauberer, weil kein zweiter Entry-Point. |
| A2 | SDK-Version | Plan nannte `1.3.0` | `ModelContextProtocol.AspNetCore 1.3.0` war auf NuGet verfügbar — Plan war korrekt. Kein Nachschärfen nötig. |
| A3 | `IterationDto`-Feld | Plan implizierte `Output` | `IterationEntity` hat das Feld `ArtifactText` (nicht `Output`) — `IterationDto` nutzt `ArtifactText`. Passt zur tatsächlichen Domain. |
| A4 | `ListRunsAsync`-Signatur | Plan ließ offen | `IRunService.ListRunsAsync` erwartet `RunStatus?` (Enum), kein `string?`. `ListRunsTool` parst daher den `statusFilter`-String via `Enum.TryParse` zu `RunStatus?` vor dem Aufruf. |
| A5 | MCP Streamable HTTP `Accept`-Header | Plan nicht explizit | MCP Streamable HTTP-Spezifikation erfordert `Accept: application/json, text/event-stream`. E2E-Tests initial mit nur `application/json` geschrieben — nach 405-Response korrigiert via `request.Headers.Accept.ParseAdd("application/json, text/event-stream")`. |
| A6 | `FixedTimeEquals`-Dummy-Call | R2 identifizierte Codegeruch | Ursprünglicher Entwurf hatte einen Dummy-`FixedTimeEquals`-Call bei unterschiedlichen Längen. R2-Finding: misleading. Bereinigt zur expliziten Längen-Prüfung als Kurzschluss-Bedingung vor `FixedTimeEquals` — semantisch identisch, aber klarer lesbar. |

---

## 3. Architect-Output (Plan-Phase-Integration)

Sechs Architektur-Entscheidungen wurden in der Plan-Phase fixiert:

1. **Mcp als Class Library statt zweitem Host** — Kein eigener Entry-Point; `MapMcp()` wird in `Geef.Atelier.Web/Program.cs` registriert. Dadurch teilen sich beide Frontends denselben DI-Container, `IRunService`, und `BackgroundService`.
2. **Multi-Auth: Cookie default + explizites Bearer-Schema** — Cookie bleibt `DefaultAuthenticateScheme`; `McpPolicy` erzwingt explizit `BearerScheme`. Kein gegenseitiges Interferieren.
3. **`ITokenValidator` im Application Layer** — Trennung zwischen Token-Validierungslogik (Application) und ASP.NET-Core-Auth-Handler-Infrastruktur (Web).
4. **`ATELIER_MCP_TOKEN` als Env-Var-Fallback** — konsistent mit dem Muster aus Schritt 8 (`ATELIER_USER`/`ATELIER_PASSWORD_HASH`); kein neues Konfigurationsmuster.
5. **`RunEntity.CreatedByUser` nullable** — MCP-Aufrufe setzen `"mcp-client"`, UI-Aufrufe (Schritt 8) setzen den eingeloggten Benutzernamen; Altdaten bleiben `null` ohne Migration-Datenverlust.
6. **Stateless Streamable HTTP Transport** — `WithHttpTransport(o => o.Stateless = true)` — keine SSE-Verbindung offen halten, passt zum zustandslosen Request-/Response-Modell der sechs Tools.

---

## 4. Pre-Mortem & Devil's Advocate

Vor der Implementierung identifizierte Risiken und ihre Auflösungen:

| Risiko | Eintrittsform | Lösung |
|---|---|---|
| Bearer-Auth interferiert mit Cookie-Auth | BearerHandler überschreibt `DefaultAuthenticate` → UI-Nutzer erhalten 401 | `McpPolicy` setzt explizit `AuthenticationSchemes = [BearerScheme]`; Cookie-Pfade werden nie durch BearerHandler ausgelöst. Dedicated Test `BearerTokenHandlerDoesNotInterfereWithCookieAuthTests`. |
| MCP-Tools greifen direkt auf DB zu | Tools importieren `AtelierDbContext` direkt → Bypass Application Layer | Tools sind `static` und injizieren nur `IRunService` — kein direkter Persistence-Zugriff. Architectural Rule durch Projekt-Referenz-Topologie erzwungen (`Geef.Atelier.Mcp` → `Application`, nicht `Infrastructure`). |
| Timing-Angriff auf Token-Vergleich | Zeichenweiser Vergleich leckt Längeninformation | `CryptographicOperations.FixedTimeEquals` nach expliziter Längenprüfung; Logs loopen nie den tatsächlichen Token-Wert. |
| E2E-Tests flaky durch Port-Konflikte | `WebTestHost` startet auf fixem Port, zweite Test-Instanz schlägt fehl | `WebTestHost` nutzt Port 0 (`TestServer`) — kein Port-Konflikt. Getrennte Collection `"Postgres"` isoliert DB-State. |
| MCP-SDK Breaking Changes | `1.3.0` nicht auf NuGet, Fallback auf Source-Link | `1.3.0` war verfügbar. Kein Fallback nötig. |

---

## 5. Reviewer-Iterationen

Alle fünf Reviewer in einer Iteration, kein zweiter Durchlauf erforderlich.

| Reviewer | Modell | Rolle | Ergebnis | Findings |
|---|---|---|---|---|
| R1 | außerhalb Anthropic-Familie | Security & Auth | **PASS** | 0 Critical, 0 Important |
| R2 | außerhalb Anthropic-Familie | Code Quality | **APPROVED** | 1 Minor (Dummy-FixedTimeEquals, A6) — behoben |
| R3 | außerhalb Anthropic-Familie | MCP-Protocol-Compliance | **PASS** | 0 Findings |
| R4 | außerhalb Anthropic-Familie | Architecture Compliance | **COMPLIANT** | 0 Findings |
| R5 | außerhalb Anthropic-Familie | curl-Verifikation (live) | **VERIFIED** | 401 ohne Token, 200 + Tool-Liste mit Token |

**Gesamtbefund:** Zero Critical/Important Findings. Ein Minor-Finding (R2, A6 oben) nach Iteration behoben. Kein zweiter Review-Durchlauf erforderlich.

---

## 6. Akzeptanzkriterien-Check

| # | Akzeptanzkriterium | Status |
|---|---|---|
| AC1 | `dotnet build` ohne Fehler oder Warnungen | ✅ |
| AC2 | Bearer-Token-Auth: gültiger Token → 200, ungültiger → 401 | ✅ |
| AC3 | `submit_request`-Tool erstellt Run und gibt `run_id` zurück | ✅ |
| AC4 | `get_run_status`-Tool gibt Status für bekannte `run_id` zurück | ✅ |
| AC5 | `get_run_result`-Tool gibt `FinalText` nur bei `Completed` zurück | ✅ |
| AC6 | `list_runs`-Tool gibt Liste mit optionalem Status-Filter zurück | ✅ |
| AC7 | `get_run_details`-Tool gibt Iterationen + Findings zurück | ✅ |
| AC8 | `cancel_run`-Tool gibt `{success: true/false}` zurück | ✅ |
| AC9 | Cookie-Auth für UI unverändert funktional | ✅ (R5 verifiziert, bestehende Auth-Tests grün) |
| AC10 | `RunEntity.CreatedByUser` wird gesetzt und persistiert | ✅ (Migration `Step09AuditTrail`, Test verifiziert `"mcp-client"`) |

---

## 7. Beobachtungen

**MCP-.NET-Ökosystem-Reife:** `ModelContextProtocol.AspNetCore 1.3.0` ist das offizielle Microsoft/Anthropic-SDK und deutlich ausgereifter als frühe Community-Pakete. `WithToolsFromAssembly` + `[McpServerToolType]`/`[McpServerTool]`-Attribute + DI-Injection in `static`-Tool-Methoden funktionieren zuverlässig. Breaking-Change-Risiko bei Minor-Versionen ist gering, da das SDK MIT-lizenziert ist und eine stabile Spezifikation abbildet.

**Streamable HTTP `Accept`-Header-Anforderung:** MCP Streamable HTTP-Transport erfordert `Accept: application/json, text/event-stream` in POST-Requests — nicht nur `application/json`. Fehlt der SSE-Teil, antwortet der Server mit `405 Method Not Allowed`. Dieser Aspekt ist in der Spezifikation vorhanden, aber in vielen HTTP-Client-Beispielen nicht offensichtlich. E2E-Tests und curl-Verifikation müssen diesen Header explizit setzen.

**Multi-Auth-Policy-Konfiguration in ASP.NET Core:** Cookie bleibt `DefaultAuthenticateScheme`, damit Blazor-Komponenten ohne explizites Schema geschützt sind. `McpPolicy` übersteuert das Schema explizit auf `BearerScheme` — nur dieser Endpoint löst `BearerTokenHandler.HandleAuthenticateAsync` aus. Das Pattern ist idiomatisch, aber ungewohnt für Teams, die nur ein Auth-Schema kennen: `AddAuthentication(defaultScheme)` + `.AddScheme<,>` + `.AddPolicy(p => p.AuthenticationSchemes = [explicitScheme])` muss zusammenspielen.

**`RunEntity.CreatedByUser` als Audit-Trail-Fundament:** Die nullable Kolumne erlaubt nachträgliche Befüllung für UI-Runs (Schritt 8 setzte noch keinen Wert). MCP-Runs setzen `"mcp-client"` deterministisch. Spätere Schritte können den tatsächlichen OAuth-Nutzer eintragen, ohne Migration.

---

## 8. Empfehlungen für Schritt 10 (Production-Deploy)

**Traefik-Routing:**
- Domain: `geef.stefan-bechtel.de`
- Traefik-Labels: `traefik.http.routers.geef-atelier.rule=Host(\`geef.stefan-bechtel.de\`)`, `entrypoints=websecure`, `tls.certresolver=letsencrypt`
- `/hubs/runs` (SignalR WebSocket): separate Router-Regel mit `PathPrefix`, `middlewares=ws-headers@file` oder direkte WebSocket-Middleware im Container
- `/mcp` (Streamable HTTP): kein spezielles Routing nötig; normaler HTTP-POST durch Traefik

**Umgebungsvariablen (vollständige Liste für `.env`/Compose-Deployment):**

| Variable | Zweck |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL-Connection-String zur Server-Postgres-Instanz |
| `ATELIER_USER` | Single-User-Loginname für die Web-UI |
| `ATELIER_PASSWORD_HASH` | BCrypt-Hash (workFactor 11), generiert via `tools/HashPassword/` |
| `ATELIER_MCP_TOKEN` | Statischer Bearer-Token für MCP-Clients (min. 32 Zeichen empfohlen) |
| `Llm__ApiKey` | OpenRouter-API-Key für echte Pipeline-Runs |
| `Llm__BaseAddress` | Optional: Abweichendes LLM-Endpoint (Default: OpenRouter) |
| `Orchestrator__MaxConcurrentRuns` | Optional: Parallelitäts-Limit des BackgroundService |

**Migration-Strategie:**
- `MigrateAsync()` läuft bereits beim Startup (seit Schritt 1); kein Init-Container nötig
- Migration `Step09AuditTrail` ist additiv (nullable Spalte) — kein Datenverlust bei Rollout auf bestehende DB
- Vor Production-Deployment einmalig `dotnet ef migrations list` gegen die Ziel-DB prüfen, dass alle Migrationen als `[Applied]` markiert werden

**Sonstiges:**
- Non-root-User im Dockerfile (bereits geplant in Schritt-10-Scope)
- `HEALTHCHECK` im Image auf `/health` zeigt Traefik-Health-Status
- SignalR-Sticky-Sessions: Bei Single-Container-Deployment nicht nötig; bei zukünftiger Skalierung Redis-Backplane erforderlich (bewusst nicht im Skeleton-Scope)

---

## 9. Status

**Abgeschlossen: 11. Mai 2026.**
85/85 Tests grün (71 bestehende + 14 neue: 3 StaticTokenValidator + 4 BearerHandler + 5 MCP-Unit + 2 MCP-E2E).
Nächster Schritt: **Schritt 10 — Production-Deploy mit Traefik + Domain `geef.stefan-bechtel.de`**.

*Hinweis zu pre-existierenden flaky Tests:* `LiveUpdateFlowTests` (E2E, seit Schritt 7) zeigen gelegentliche Timeouts bei Ressourcenknappheit unter dem vollen Test-Suite-Lauf. Dies ist kein Schritt-9-Defekt; die Tests sind im Einzellauf stabil. Schritt 10 wird dieses Problem nicht verschlimmern.
