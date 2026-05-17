# MCP-Integration

*Letzte Aktualisierung: 2026-05-17 (Endpunkt-Tabelle präzisiert, vollständige Tool-Liste und Run-User-Isolation D-042 ergänzt)*

## Warum MCP

Geef.Atelier soll nicht nur per Web-UI bedienbar sein, sondern auch von KI-Agenten konsumierbar. Der typische Anwendungsfall: ein Claude (oder ein anderer MCP-fähiger Client) arbeitet an einer komplexeren Aufgabe und braucht für einen Teilschritt einen besonders sorgfältig erzeugten Text. Statt diesen Text inline zu generieren — wo der aufrufende Claude weder mehrere Iterationen noch eine Reviewer-Crew zur Verfügung hat — delegiert er den Auftrag an Geef.Atelier, holt sich später das Ergebnis und arbeitet weiter.

MCP (Model Context Protocol) ist der Standard, mit dem solche Delegationen sauber laufen: Tools mit JSON-Schema-definierten Inputs/Outputs, einheitliche Auth, einheitlicher Transport.

## Architektonische Konsequenz

Der Service hat **zwei Frontends**: Web-UI und MCP-Server. Beide nutzen denselben Application-Service-Layer (`IRunService`). Die Pipeline-Logik, die Persistierung, das EventSink — all das ist Frontend-agnostisch.

```
Web-UI ──┐
         ├──> IRunService ──> Background-Orchestrator ──> Geef-Pipeline
MCP   ───┘
```

Daraus folgt: alles, was der User in der UI tun kann, soll auch via MCP tun können — bis auf rein UI-spezifische Dinge (Live-Stream-Anzeige).

## Tools, die der MCP-Server anbietet

### `submit_request`

Stellt einen neuen Auftrag in die Warteschlange.

**Input:**
```json
{
  "briefing": "string (required) — Beschreibung der Aufgabe und des gewünschten Ergebnisses",
  "options": {
    "executor_model": "string (optional) — z.B. 'claude-opus-4-7'",
    "reviewer_models": ["array (optional) — Liste von Modellen für die Reviewer"],
    "max_iterations": "int (optional, default 3)"
  }
}
```

**Output:**
```json
{
  "run_id": "uuid",
  "status": "Pending"
}
```

### `get_run_status`

Liefert den aktuellen Status eines Runs.

**Input:** `{ "run_id": "uuid" }`

**Output:**
```json
{
  "run_id": "uuid",
  "status": "Pending | Running | Completed | Failed | Aborted",
  "current_phase": "Grounding | Execution | Evaluation | Finalize | null",
  "current_iteration": 2,
  "tokens_used": 12345,
  "cost_total": 0.234,
  "started_at": "2026-05-10T12:00:00Z",
  "completed_at": null
}
```

### `get_run_result`

Liefert das fertige Ergebnis. Nur bei Status=Completed.

**Input:** `{ "run_id": "uuid" }`

**Output (bei Completed):**
```json
{
  "run_id": "uuid",
  "final_text": "string",
  "tokens_used": 12345,
  "cost_total": 0.234,
  "iteration_count": 2
}
```

**Output (bei anderem Status):** Error mit aktuellem Status, damit der Client weiß, ob er warten soll oder nicht.

### `list_runs`

Listet bestehende Runs.

**Input:**
```json
{
  "limit": "int (optional, default 20)",
  "status_filter": "string (optional)"
}
```

**Output:** Array von Run-Summaries (Id, Status, CreatedAt, BriefingPreview).

### `get_run_details`

Liefert den vollständigen Trail eines Runs.

**Input:** `{ "run_id": "uuid" }`

**Output:** Run-Daten plus alle Iterationen (mit Artefakt-Text), alle Findings, optional die letzten N Events.

### `cancel_run`

Bricht einen laufenden Run ab. Gibt `true` zurück, wenn der Abbruch ausgelöst wurde; `false`, wenn der Run bereits terminal war (Completed, Failed, Aborted) oder nicht existiert.

**Input:** `{ "run_id": "uuid" }`

**Output:** `bool` (`true` | `false`)

### Weitere Tools (Crew, Wissensbasis, Template Studio)

Neben den sechs Run-Tools bietet der Server sieben weitere — insgesamt **13 MCP-Tools**:

| Tool | Zweck |
|---|---|
| `list_crew_templates` | Crew-Templates auflisten (System + Custom) |
| `list_reviewer_profiles` | Reviewer-Profile auflisten (System + Custom) |
| `list_advisor_profiles` | Advisor-Profile auflisten (System + Custom) |
| `list_grounding_provider_profiles` | Grounding-Provider-Profile auflisten |
| `list_knowledge_documents` | Globale Wissensbasis-Dokumente auflisten |
| `analyze_template_proposal` | Aufgabenbeschreibung analysieren, Template-Vorschlag erzeugen (persistiert) |
| `materialize_template_proposal` | Geprüften Vorschlag als Custom-Template + -Profile materialisieren |

Vollständige Parameter-/Schema-Details: [`09-endpoint-reference.md`](09-endpoint-reference.md).

### Run-Sichtbarkeit über MCP (D-042)

Seit der Run-User-Isolation sind Runs pro Nutzer getrennt sichtbar — auch über MCP:
Über OAuth gestellte/abgefragte Runs gehören dem autorisierenden Nutzer; Requests mit
statischem `ATELIER_MCP_TOKEN` (Claude Code CLI) werden dem Admin zugeordnet.
`list_runs`/`get_run_*` liefern nur die Runs des jeweiligen Nutzers (kein
Run-Existenz-Leak); ist der Aufrufer Admin, kann er mit dem `list_runs`-Parameter
`includeAllUsers=true` systemweit sehen (für Nicht-Admins wirkungslos).

## SDK

**`ModelContextProtocol.AspNetCore` v1.3.0** — das offizielle Anthropic+Microsoft C# SDK ([modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)). Die Tools werden als `[McpServerTool]`-annotierte Methoden in `Geef.Atelier.Mcp` (Class Library) definiert und über `AddMcpServer().WithToolsFromAssembly()` im Web-Host registriert.

## Transport

**Streamable HTTP** (stateless, `Stateless=true`) ist der aktiv genutzte Transport. Vorteil gegenüber dem älteren SSE-Transport: bidirektional über eine einzige Verbindung, leichter durch Reverse-Proxies zu führen, einfacheres Auth-Handling.

Endpunkt: `POST https://atelier.example.com/mcp` (Pfad `/mcp` ist fest im `MapMcp()`-Aufruf des Web-Hosts).

## Auth

Zwei parallele Auth-Pfade — beide aktiv, kein Config-Schalter nötig:

### Pfad A: Statisches Bearer-Token (Claude Code CLI)

**Bearer-Token** im `Authorization`-Header. Token aus Umgebungsvariable `ATELIER_MCP_TOKEN`. Keine Rotation, kein Refresh. Ausreichend für Single-User-CLI-Betrieb.

```
Authorization: Bearer <ATELIER_MCP_TOKEN>
```

### Pfad B: OAuth 2.1 (Claude Desktop / Claude.ai Custom Connectors)

Self-hosted OAuth-2.1-Authorization-Server, direkt in Geef.Atelier implementiert. Unterstützt den vollständigen Authorization-Code-Flow mit Pflicht-PKCE/S256.

**Relevante Spezifikationen:** RFC 8414 (Metadata), RFC 7591 (Dynamic Client Registration), RFC 7636 (PKCE), RFC 7009 (Revocation), RFC 8252 (Loopback).

**Endpunkte:**

| Endpunkt | Methode | Zweck |
|----------|---------|-------|
| `/.well-known/oauth-authorization-server` | GET | RFC 8414 Server Metadata |
| `/.well-known/oauth-protected-resource` | GET | MCP Resource Metadata |
| `/oauth/register` | POST | RFC 7591 Dynamic Client Registration |
| `/oauth/authorize` | GET | Consent-Seite (Blazor, `[Authorize]` Cookie — bei fehlender Session Redirect auf `/login`) |
| `/oauth/consent` | POST | Approve/Deny-Submit der Consent-Seite → Redirect zur `redirect_uri` |
| `/oauth/token` | POST | Token-Endpoint (authorization_code + refresh_token) |
| `/oauth/revoke` | POST | RFC 7009 Token-Revocation |
| `/account/connected-clients` | GET | Selbstverwaltung verbundener Clients (Nutzer-UI) |
| `/admin/oauth-clients` | GET | OAuth-Client-Verwaltung (nur Admin) |

**Flow:**

```
1. Client → GET /.well-known/oauth-authorization-server  (Discovery)
2. Client → POST /oauth/register                         (Dynamic Client Registration)
3. Client → GET /oauth/authorize?...&code_challenge=...  (→ Browser-Login + Consent)
4. Nutzer genehmigt → Browser-Redirect zurück mit ?code=...
5. Client → POST /oauth/token (code + code_verifier)     (Token-Exchange)
6. Client → MCP-Request mit Bearer <access_token>
7. Client → POST /oauth/token (refresh_token)            (Refresh-Rotation, optional)
8. Client → POST /oauth/revoke                           (Revocation, optional)
```

**Token-Design:** Opaque Tokens (32-Byte-Zufallsstring, Base64Url). Nur SHA-256-Hash in DB. Access-Token: 1 Stunde. Refresh-Token: 30 Tage, Rotation bei jedem Refresh.

**Sicherheit:**
- Alle geheimen Vergleiche via `CryptographicOperations.FixedTimeEquals`
- Token-Generierung ausschließlich `RandomNumberGenerator.GetBytes(32)`
- PKCE S256 erzwungen — `plain` abgelehnt
- Refresh-Reuse-Detection: verbrauchtes Refresh-Token → sofortige Revocation aller User-Tokens

### Kompatibilität

`CompositeTokenValidator` prüft beide Pfade — statisches Token zuerst. Claude Code CLI-Requests mit `ATELIER_MCP_TOKEN` erreichen den OAuth-Pfad nie. Beide Pfade koexistieren ohne Konfigurationsänderung.

## Beziehung zu Web-UI

Beide Frontends rufen denselben `IRunService` auf. Konsequenzen:

- Ein via MCP gestarteter Run erscheint sofort in der Web-UI (gleiche DB).
- Ein via UI gestarteter Run kann via MCP abgefragt werden.
- Status-Updates eines via MCP gestarteten Runs sind in der UI live sichtbar (SignalR-Stream).
- Cancellation funktioniert von beiden Seiten.

Das ist ein bewusstes Design-Ziel: ein Auftrag ist ein Auftrag, unabhängig vom Eintragsweg.

## Hosting

Im Skeleton läuft der MCP-Server **als Teil derselben ASP.NET-Anwendung** wie die Web-UI — derselbe Container, derselbe Prozess, eigener Pfad-Präfix (`/mcp`). Das spart Deployment-Aufwand. Sollte sich später Bedarf ergeben (z.B. unterschiedliche Skalierungs-Anforderungen), kann der MCP-Server in einen eigenen Container ausgegliedert werden, ohne dass sich Domain-Logik ändern muss.

## Discovery und Konfiguration

### Claude Code CLI (statisches Token)

```json
{
  "mcpServers": {
    "geef-atelier": {
      "url": "https://geef.stefan-bechtel.de/mcp",
      "transport": "streamable-http",
      "auth": {
        "type": "bearer",
        "token": "<ATELIER_MCP_TOKEN>"
      }
    }
  }
}
```

### Claude Desktop / Claude.ai Custom Connector (OAuth)

URL `https://geef.stefan-bechtel.de/mcp` eingeben — der Client erkennt `WWW-Authenticate: Bearer resource_metadata=".../.well-known/oauth-protected-resource"` und startet den OAuth-Flow automatisch (Dynamic Client Registration → Browser-Login → Consent → Token-Exchange).

## Nicht im Scope

- Rate-Limiting (Single-User, kein Bedarf)
- Mehrere Scopes / feingranulare Berechtigungen (nur `mcp:full`)
- JWTs / OpenID Connect (Opaque Tokens + DB-Lookup ist ausreichend)
- Multi-Tenant