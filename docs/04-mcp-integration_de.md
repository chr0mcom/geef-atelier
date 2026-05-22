# MCP-Integration

*[English](04-mcp-integration.md) · **Deutsch***

*Letzte Aktualisierung: 2026-05-20 (D-051: neue providerType-Werte static-context, url-fetch, news-search in list_grounding_provider_profiles + materialize_template_proposal dokumentiert)*

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

Neben den sechs Run-Tools bietet der Server neun weitere — insgesamt **15 MCP-Tools**:

| Tool | Zweck |
|---|---|
| `list_crew_templates` | Crew-Templates auflisten (System + Custom) |
| `list_reviewer_profiles` | Reviewer-Profile auflisten (System + Custom) |
| `list_advisor_profiles` | Advisor-Profile auflisten (System + Custom) |
| `list_grounding_provider_profiles` | Grounding-Provider-Profile auflisten |
| `list_knowledge_documents` | Globale Wissensbasis-Dokumente auflisten |
| `analyze_template_proposal` | Aufgabenbeschreibung analysieren, Template-Vorschlag erzeugen (persistiert) |
| `materialize_template_proposal` | Geprüften Vorschlag als Custom-Template + -Profile materialisieren |
| `list_run_artifacts` | Alle RunArtifacts eines abgeschlossenen Runs auflisten (erzeugt von Finalizern) |
| `download_run_artifact` | Binärinhalt eines File-Artifacts als Base64 herunterladen |

Vollständige Parameter-/Schema-Details: [`09-endpoint-reference.md`](09-endpoint-reference_de.md).

#### `list_grounding_provider_profiles`

**Input:** `{ "includeSystem": bool (default true) }`

**Output:** Array von Grounding-Provider-Profil-Objekten. Jedes Profil enthält:

| Feld | Typ | Beschreibung |
|---|---|---|
| `name` | string | Profilbezeichner |
| `displayName` | string | Anzeigename |
| `description` | string | Zweckbeschreibung |
| `providerType` | string | `"tavily"`, `"vector-store"`, `"static-context"`, `"url-fetch"` oder `"news-search"` |
| `maxQueriesPerRun` | int? | Maximale Anfragen pro Run |
| `isSystem` | boolean | Ob es sich um ein eingebautes System-Profil handelt |
| `refinementEnabled` | boolean | Ob ein KI-Refinement konfiguriert ist |
| `refinementMode` | string \| null | `"filter"` oder `"synthesize"` (null wenn kein Refinement) |

#### `analyze_template_proposal`

Führt ein Meta-LLM aus, das die Aufgabenbeschreibung analysiert und eine strukturierte `TemplateStudioAnalysis` erzeugt, die in der DB persistiert wird.

**Input:** `{ "task_description": "string" }`

**Output:** `TemplateStudioAnalysis` — enthält `id` (UUID, benötigt für `materialize_template_proposal`), ein `proposed_template` (DisplayName, Description, EvaluationStrategy, optional `evaluation_strategy_reasoning`, optionales `finalizer_profile_names`-Array, optionales `finalizer_reasoning`) und zwei Listen:

- `proposed_new_profiles` — Crew-/Advisor-/Grounding-/Executor-Profile. Jedes Profil trägt: `profile_type` (`"reviewer"` | `"advisor"` | `"grounding_provider"` | `"executor"`), Name, DisplayName, Description, Provider, Modell, MaxTokens, SystemPrompt sowie typ-spezifische optionale Felder (ReviewerFocus, AdvisorMode, AdvisorTrigger, GroundingProviderType, GroundingProviderSettings) und optionale LLM-Reasoning-Felder (`model_reasoning`, `system_prompt_reasoning`, `overall_reasoning`, `mode_reasoning`, `trigger_reasoning`).
- `proposed_new_finalizer_profiles` — Finalizer-Profil-Vorschläge. Jedes Profil trägt: `name`, `display_name`, `description`, `finalizer_type` (`"FileExport"` | `"MetadataEnrich"` | `"ExternalSink"` | `"Transform"`), `settings` (Objekt), `finalizer_type_reasoning` (optionaler String).

Fehlende Felder werden serverseitig aus `appsettings TemplateStudio:Defaults` befüllt.

Backwards-kompatibel: Alte Inputs ohne Finalizer-Felder funktionieren weiterhin — es werden standardmäßig keine Finalizer gesetzt.

#### `materialize_template_proposal`

Schreibt alle neuen Profile und das Crew-Template atomar in einer einzigen Transaktion in die DB.

**Input:**
```json
{
  "analysis_id": "uuid",
  "final_template": {
    "display_name": "...", "description": "...", "evaluation_strategy": "Sequential",
    "executor_profile_name": "...", "reviewer_profile_names": ["..."],
    "advisor_profile_names": ["..."], "grounding_provider_profile_names": ["..."],
    "finalizer_profile_names": ["..."],
    "finalizer_reasoning": "optionaler String"
  },
  "final_new_profiles": [
    {
      "profile_type": "reviewer", "name": "custom-my-reviewer",
      "display_name": "...", "system_prompt": "...", "provider": "openrouter",
      "model": "openai/gpt-4o-mini", "max_tokens": 16384
    }
  ],
  "final_new_finalizer_profiles": [
    {
      "name": "custom-my-exporter", "display_name": "...", "description": "...",
      "finalizer_type": "FileExport", "settings": {}
    }
  ]
}
```

`final_new_profiles` enthält nur Profile, die der Nutzer im CreateNew-Modus neu anlegen wollte. Profile im UseExisting-Modus erscheinen nur als Name in `final_template.*_profile_names`. `final_new_finalizer_profiles` folgt demselben Muster für Finalizer-Profile. Ein `max_tokens` unterhalb des harten Floors (`StudioDefaults.MinMaxTokens = 10000`) wird serverseitig hochgezogen; ohne Angabe greift der `TemplateStudio:Defaults`-Wert (Reviewer/Advisor 16384, Executor 60000). Das Weglassen von `finalizer_profile_names` oder `final_new_finalizer_profiles` ist backwards-kompatibel (keine Finalizer werden angehängt).

**Grounding-Provider-Typen:** `providerType` (bzw. `GroundingProviderType` im Proposal) kann folgende Werte haben: `"tavily"`, `"vector-store"`, `"static-context"`, `"url-fetch"`, `"news-search"`. Jeder Typ erwartet typ-spezifische Settings-Keys (siehe `08-crew-system_de.md` Provider-Typen-Tabelle).

**Grounding-Provider-Refinement-Keys in `groundingProviderSettings`:** Das `GroundingProviderSettings`-Dict für ein `grounding_provider`-Profil akzeptiert alle KI-Refinement-Keys (`refinementProvider`, `refinementModel`, `refinementMaxTokens`, `refinementTemperature`, `refinementMode`, `refinementInstructions`) als flache String-Einträge. Alle Keys sind optional und backwards-kompatibel — Profile ohne diese Keys verhalten sich wie bisher (kein Refinement-Pass).

**Output:** `{ "created_template_name": "custom-..." }` — Name des materialisierten Templates; kann direkt als `crew_template` an `submit_request` übergeben werden.

#### `list_run_artifacts`

Listet alle RunArtifacts auf, die von Finalizern für einen abgeschlossenen Run erzeugt wurden.

**Input:** `{ "run_id": "uuid (required)" }`

**Auth:** Owner-Isolation — Nicht-Admins sehen nur Artifacts ihrer eigenen Runs.

**Output:** Array von Artifact-Objekten:
```json
[
  {
    "artifact_id": "uuid",
    "finalizer_profile_name": "string",
    "artifact_type": "File | Url | Status",
    "filename": "string oder null",
    "content_type": "string oder null",
    "size_bytes": 12345,
    "storage_uri": "string — Dateipfad (File), URL (Url) oder 'error'/'info' (Status)",
    "status_message": "string oder null",
    "created_at": "2026-05-19T10:00:00Z"
  }
]
```

#### `download_run_artifact`

Lädt den Binärinhalt eines File-Artifacts als Base64 herunter. Funktioniert nur für `artifact_type: "File"` — für Url- und Status-Typen wird ein Fehler zurückgegeben. Nützlich für KI-Agenten, die erzeugte PDFs, DOCX, HTML usw. programmatisch abrufen müssen.

**Input:** `{ "run_id": "uuid (required)", "artifact_id": "uuid (required)" }`

**Auth:** Owner-Isolation.

**Output:**
```json
{
  "artifact_id": "uuid",
  "filename": "string",
  "content_type": "string",
  "size_bytes": 12345,
  "content_base64": "string"
}
```

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

## Tool `list_learnings` (D-054)

| Feld | Wert |
|---|---|
| Tool-Name | `list_learnings` |
| Parameter | `status_filter?` (Proposed / Approved / Rejected), `domain_filter?` (String) |
| Rückgabe | Array von `LearningEntryDto` |

**`LearningEntryDto`-Felder:** `id`, `text` (gekürzt auf 300 Zeichen), `source_run_id`, `learning_run_id?`, `domain`, `status`, `owner_username`, `created_at`, `approved_at?`.

`status_filter=Approved` gibt nur aktive Learnings zurück, die beim Retrieval tatsächlich genutzt werden. `domain_filter` matcht den Crew-Template-Namen des Ursprungs-Runs (z. B. `juristisch`, `akademisch`).
