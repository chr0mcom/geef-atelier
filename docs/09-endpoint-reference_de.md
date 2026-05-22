# Endpoint-Referenz

*[English](09-endpoint-reference.md) · **Deutsch***

*Letzte Aktualisierung: 2026-05-22 (D-054: Tool list_learnings ergänzt; providerType um learning-retrieval erweitert)*

Alle HTTP-Endpunkte von Geef.Atelier, die extern erreichbar sind — MCP, OAuth 2.1
sowie die Web-UI-/Account-Endpunkte. Basis-URL: `https://geef.stefan-bechtel.de`.

---

## MCP-Endpunkt

| Endpunkt | Methode | Auth |
|----------|---------|------|
| `/mcp` | POST | Bearer-Token (statisch oder OAuth) |

Der eigentliche MCP-Endpunkt. Clients senden hier ihre JSON-RPC-Requests (Tool-Calls). Unterstützt Streamable-HTTP-Transport (`Stateless=true`).

**Auth-Möglichkeiten:**

- **Claude Code CLI:** `Authorization: Bearer <ATELIER_MCP_TOKEN>` (statisches Token aus `.env`)
- **Claude Desktop / Claude.ai:** OAuth-2.1-Access-Token (Bearer), ausgestellt nach dem unten beschriebenen OAuth-Flow

Wenn kein oder ein ungültiges Token mitgeschickt wird, antwortet der Server mit `401 Unauthorized` und dem Header:
```
WWW-Authenticate: Bearer resource_metadata="https://geef.stefan-bechtel.de/.well-known/oauth-protected-resource"
```
Darüber entdecken OAuth-fähige Clients automatisch den Authorization Server.

### MCP-Tools

Der MCP-Server stellt folgende Tools bereit, die über den `/mcp`-Endpunkt aufgerufen werden können. Run-bezogene Tools erzwingen dieselbe Run-User-Isolation wie die Web-UI (D-042): Nicht-Admin-Nutzer können nur auf eigene Runs zugreifen.

#### `list_run_artifacts`

Listet alle Artefakte auf, die ein Run erzeugt hat.

**Eingabe:**
```json
{ "run_id": "<uuid>" }
```

**Ausgabe:** Array von Artefakt-Objekten:
```json
[
  {
    "artifact_id": "<uuid>",
    "finalizer_profile_name": "<string>",
    "artifact_type": "File|Url|Status",
    "filename": "<string|null>",
    "content_type": "<string|null>",
    "size_bytes": 12345,
    "storage_uri": "<string>",
    "status_message": "<string|null>",
    "created_at": "<ISO8601>"
  }
]
```

Gibt ein leeres Array zurück, wenn der Run keine Artefakte hat. Auth: Owner-Check (gleiche Run-Isolation wie andere Run-Tools).

---

#### `list_grounding_provider_profiles`

Listet alle Grounding-Provider-Profile (System + Custom) auf.

**Eingabe:** `{ "includeSystem": bool (default true) }`

**Ausgabe:** Array von Grounding-Provider-Profil-Objekten:
```json
[
  {
    "name": "tavily-refined",
    "displayName": "Tavily Refined",
    "description": "...",
    "providerType": "tavily",
    "maxQueriesPerRun": 3,
    "isSystem": true,
    "refinementEnabled": true,
    "refinementMode": "filter"
  },
  {
    "name": "tavily-news",
    "displayName": "Tavily News",
    "description": "...",
    "providerType": "news-search",
    "maxQueriesPerRun": 1,
    "isSystem": true,
    "refinementEnabled": true,
    "refinementMode": "filter"
  },
  {
    "name": "custom-mein-provider",
    "displayName": "Mein Provider",
    "description": "...",
    "providerType": "vector-store",
    "maxQueriesPerRun": null,
    "isSystem": false,
    "refinementEnabled": false,
    "refinementMode": null
  }
]
```

| Feld | Typ | Beschreibung |
|---|---|---|
| `name` | string | Profilbezeichner |
| `displayName` | string | Anzeigename |
| `description` | string | Zweckbeschreibung |
| `providerType` | string | `"tavily"`, `"vector-store"`, `"static-context"`, `"url-fetch"`, `"news-search"`, `"academic-search"`, `"rest-api"` oder `"learning-retrieval"` |
| `maxQueriesPerRun` | int? | Max. Anfragen pro Run (null = unbegrenzt) |
| `isSystem` | boolean | Eingebautes System-Profil |
| `refinementEnabled` | boolean | Ob ein KI-Refinement konfiguriert ist |
| `refinementMode` | string \| null | `"filter"` oder `"synthesize"` (null wenn kein Refinement) |

---

#### `download_run_artifact`

Lädt den binären Inhalt eines `File`-Artefakts herunter und gibt ihn als Base64 zurück.

**Eingabe:**
```json
{ "run_id": "<uuid>", "artifact_id": "<uuid>" }
```

**Ausgabe:**
```json
{
  "artifact_id": "<uuid>",
  "filename": "<string>",
  "content_type": "<string>",
  "size_bytes": 12345,
  "content_base64": "<base64-kodierter Dateiinhalt>"
}
```

Funktioniert nur für `ArtifactType.File`-Artefakte. Liest die Datei vom `ExportPath` auf der Festplatte und gibt die rohen Bytes als Base64 zurück. Auth: Owner-Check.

---

#### `list_learnings`

Listet Learning-Einträge aus dem Learning-Store (D-054).

**Input:**
```json
{
  "status_filter": "Proposed|Approved|Rejected",
  "domain_filter": "<string>"
}
```

Beide Parameter sind optional. Ohne `status_filter` werden alle Status zurückgegeben; ohne `domain_filter` alle Domänen.

**Output:** Array von Learning-Entry-Objekten:
```json
[
  {
    "id": "<uuid>",
    "text": "<string, auf 300 Zeichen gekürzt>",
    "domain": "<string>",
    "status": "Proposed|Approved|Rejected",
    "source_run_id": "<uuid>",
    "learning_run_id": "<uuid|null>",
    "owner_username": "<string>",
    "created_at": "<ISO8601>",
    "approved_at": "<ISO8601|null>"
  }
]
```

| Feld | Typ | Beschreibung |
|---|---|---|
| `id` | uuid | Learning-Entry-Bezeichner |
| `text` | string | Der kondensierte Lerntext, auf 300 Zeichen gekürzt |
| `domain` | string | Template-Name des Quell-Runs (für Domänen-Boost-Retrieval) |
| `status` | string | `"Proposed"`, `"Approved"` oder `"Rejected"` |
| `source_run_id` | uuid | Der Standard-Run, der die Extraktion ausgelöst hat |
| `learning_run_id` | uuid \| null | Der Learning-Evaluation-Run, der den Eintrag verarbeitet hat (null solange Proposed) |
| `owner_username` | string | Geerbt vom erstellenden Nutzer des Quell-Runs |
| `created_at` | ISO8601 | Extraktions-Zeitstempel |
| `approved_at` | ISO8601 \| null | Genehmigungs-Zeitstempel (null solange Proposed oder Rejected) |

**Hinweis:** `structured_facts_json` wird bewusst nicht exponiert — das rohe Fakten-JSON ist für MCP-Kontext zu umfangreich und nur über die Web-UI zugänglich. Auth: Standard-Run-User-Isolation; Nicht-Admin-Nutzer sehen nur ihre eigenen Einträge.

---

## OAuth-Endpunkte

### Discovery

#### `GET /.well-known/oauth-authorization-server`

**Auth:** Keine  
**RFC:** 8414

Gibt die Server-Metadaten als JSON zurück. Clients nutzen diesen Endpunkt zur automatischen Discovery aller anderen OAuth-Endpunkte.

```json
{
  "issuer": "https://geef.stefan-bechtel.de",
  "authorization_endpoint": "https://geef.stefan-bechtel.de/oauth/authorize",
  "token_endpoint": "https://geef.stefan-bechtel.de/oauth/token",
  "registration_endpoint": "https://geef.stefan-bechtel.de/oauth/register",
  "revocation_endpoint": "https://geef.stefan-bechtel.de/oauth/revoke",
  "response_types_supported": ["code"],
  "grant_types_supported": ["authorization_code", "refresh_token"],
  "code_challenge_methods_supported": ["S256"],
  "token_endpoint_auth_methods_supported": ["none"],
  "scopes_supported": ["mcp:full"]
}
```

---

#### `GET /.well-known/oauth-protected-resource`

**Auth:** Keine  
**RFC:** Draft (MCP Resource Metadata)

Gibt Metadaten zur geschützten Ressource (dem MCP-Server) zurück.

```json
{
  "resource": "https://geef.stefan-bechtel.de/mcp",
  "authorization_servers": ["https://geef.stefan-bechtel.de"],
  "bearer_methods_supported": ["header"],
  "scopes_supported": ["mcp:full"]
}
```

---

### Client-Registrierung

#### `POST /oauth/register`

**Auth:** Keine (oder optionaler `Authorization: Bearer <REGISTRATION_TOKEN>` wenn konfiguriert)  
**RFC:** 7591 — Dynamic Client Registration

Registriert einen neuen OAuth-Client. Typischerweise vom MCP-Client automatisch aufgerufen.

**Request (JSON):**
```json
{
  "client_name": "Mein Client",
  "redirect_uris": ["https://example.com/callback"],
  "client_id": "mein-client-id",
  "logo_uri": null,
  "client_uri": null
}
```

`client_id` ist optional — wird weggelassen, generiert der Server eine UUID. `client_name` und `redirect_uris` sind Pflichtfelder.

**Response (201):**
```json
{
  "client_id": "mein-client-id",
  "client_id_issued_at": 1747390000,
  "redirect_uris": ["https://example.com/callback"],
  "client_name": "Mein Client",
  "token_endpoint_auth_method": "none",
  "grant_types": ["authorization_code", "refresh_token"],
  "response_types": ["code"]
}
```

---

### Authorization-Code-Flow

#### `GET /oauth/authorize`

**Auth:** Cookie (Geef.Atelier-Login — wird bei fehlender Session zu `/login` weitergeleitet)

Startet den Authorization-Code-Flow. Zeigt dem eingeloggten Nutzer die Consent-Seite mit Client-Name und beantragter Berechtigung.

**Pflicht-Query-Parameter:**

| Parameter | Beschreibung |
|-----------|-------------|
| `response_type` | Muss `code` sein |
| `client_id` | Registrierte Client-ID |
| `redirect_uri` | Muss exakt mit einer registrierten URI übereinstimmen |
| `code_challenge` | PKCE-Challenge (Base64Url-codierter SHA-256-Hash des Verifiers) |
| `code_challenge_method` | Muss `S256` sein (`plain` wird abgelehnt) |

**Optionale Parameter:** `scope`, `state`

Beispiel-URL wie sie Claude Desktop aufruft:
```
https://geef.stefan-bechtel.de/oauth/authorize
  ?response_type=code
  &client_id=claude-ai
  &redirect_uri=https%3A%2F%2Fclaude.ai%2Fapi%2Fmcp%2Fauth_callback
  &code_challenge=Oaa_K782ehJ6ZNf-INVXFk1mEKtzQz7xOERSXZUiGXA
  &code_challenge_method=S256
  &state=<zufälliger-state>
  &scope=mcp%3Afull
```

Die `GET /oauth/authorize`-Seite ist eine server-gerenderte Blazor-Consent-Seite
(`[Authorize]` Cookie). Die Approve/Deny-Entscheidung wird per Formular-POST an
`/oauth/consent` gesendet (siehe unten) — nicht an `/oauth/authorize` selbst.

---

#### `POST /oauth/consent`

**Auth:** Cookie (Geef.Atelier-Login) + Anti-Forgery-Token  
**Content-Type:** `application/x-www-form-urlencoded`

Submit-Ziel der Consent-Seite. Verarbeitet die Zustimmung/Ablehnung des Nutzers,
erzeugt bei Zustimmung den Authorization-Code und führt den Redirect aus.

**Nach Zustimmung:** Redirect zu `redirect_uri?code=<auth_code>&state=<state>`  
**Nach Ablehnung:** Redirect zu `redirect_uri?error=access_denied&state=<state>`  
**Bei ungültigem Request:** Fehlerseite (kein Redirect — schützt vor Open-Redirect)

---

### Token-Endpunkt

#### `POST /oauth/token`

**Auth:** Keine (Public Clients — Authentifizierung über PKCE statt Client-Secret)  
**Content-Type:** `application/x-www-form-urlencoded`

Tauscht einen Authorization-Code gegen Tokens oder erneuert über Refresh-Token.

**Grant: `authorization_code`**

```
grant_type=authorization_code
&code=<auth_code>
&client_id=<client_id>
&redirect_uri=<redirect_uri>
&code_verifier=<pkce_verifier>
```

**Grant: `refresh_token`**

```
grant_type=refresh_token
&refresh_token=<refresh_token>
&client_id=<client_id>
```

**Response (200):**
```json
{
  "access_token": "<opaque_token>",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "<opaque_token>",
  "scope": "mcp:full"
}
```

Response-Header: `Cache-Control: no-store`, `Pragma: no-cache` (RFC 6749 §5.1).

**Fehlerverhalten:**
- Ungültiger oder verbrauchter Code → `400 invalid_grant`
- Refresh-Token bereits benutzt → `400 invalid_grant` **+ sofortige Revocation aller Tokens des Nutzers** (Diebstahl-Erkennung nach RFC 6819)

---

### Revocation

#### `POST /oauth/revoke`

**Auth:** Keine  
**Content-Type:** `application/x-www-form-urlencoded`  
**RFC:** 7009

Widerruft einen Access-Token oder Refresh-Token. Gibt immer `200 OK` zurück (auch wenn das Token unbekannt ist).

```
token=<token>
&client_id=<client_id>
```

---

## Web-UI- & Account-Endpunkte

Die Web-Oberfläche ist Blazor Server (Cookie-Auth). Auswahl der relevanten,
extern erreichbaren Routen:

| Endpunkt | Methode | Auth | Zweck |
|----------|---------|------|-------|
| `/` | GET | Cookie | Startseite / Atelier-Übersicht |
| `/health` | GET | Keine | Health-Check (`Healthy`) — für Reverse-Proxy/Container-Lifecycle |
| `/login` | GET/POST | Keine | Login-Seite (Static SSR) |
| `/auth/logout` | POST | Cookie | Logout (Anti-Forgery, Minimal-API) |
| `/settings/theme` | POST | Keine | Theme-Wechsel-Fallback (No-JS), Redirect zum Referer |
| `/hubs/runs` | WS | — | SignalR-Hub für Live-Run-Updates |
| `/admin/users` | GET | Cookie (Admin) | Benutzerverwaltung |
| `/admin/oauth-clients` | GET | Cookie (Admin) | OAuth-Client-Verwaltung |
| `/account/connected-clients` | GET | Cookie | Selbstverwaltung der eigenen verbundenen OAuth-Clients |
| `/crew`, `/crew/templates`, `/crew/profiles/*`, `/crew/studio`, `/crew/knowledge` | GET | Cookie | Crew-/Template-/Profil-/Studio-/Wissensbasis-Verwaltung |
| `/runs`, `/runs/{id}`, `/new` | GET | Cookie | Run-Liste, Run-Detail, neuer Auftrag |

Run-bezogene Seiten unterliegen der Run-User-Isolation (D-042): jeder Nutzer sieht
nur eigene Runs; der Admin kann per explizitem Umschalter systemweit sehen.

---

## Artefakt-Download-Endpunkt

### `GET /runs/{runId:guid}/artifacts/{artifactId:guid}/download`

**Auth:** `.RequireAuthorization()` — erfordert eine aktive Session (Cookie oder Bearer-Token)

Lädt eine Artefakt-Datei herunter, die ein Finalizer während eines Runs erzeugt hat. Nur Artefakte vom Typ `File` können heruntergeladen werden; `Url`- und `Status`-Artefakte liefern 404.

**Owner-Check:** Nicht-Admin-Nutzer dürfen nur Artefakte aus eigenen Runs herunterladen. Die Prüfung erfolgt über `IRunService.GetRunAsync` mit dem anfragenden Benutzernamen. Admin-Nutzer umgehen den Owner-Check.

**Sicherheitshinweis:** Ein Path-Containment-Guard (`Path.GetFullPath`-Vergleich) verhindert Directory-Traversal-Angriffe auf den serverseitigen Dateipfad.

**Erfolgsantwort:** `200 OK` — Datei-Stream mit `Content-Disposition: attachment` (Dateiname aus dem Artefakt-Datensatz).

**Fehlerantworten:**

| Status | Bedingung |
|--------|-----------|
| `401 Unauthorized` | Nicht authentifiziert |
| `403 Forbidden` | Authentifiziert, aber nicht der Run-Eigentümer (und kein Admin) |
| `404 Not Found` | `runId` oder `artifactId` nicht gefunden |
| `404 Not Found` | Artefakt vorhanden, aber vom Typ `Url` oder `Status` (nicht `File`) |
| `404 Not Found` | Datei nicht auf der Festplatte gefunden |

---

## Token-Design

| Eigenschaft | Wert |
|-------------|------|
| Format | Opaque — 32-Byte Zufallsdaten, Base64Url-codiert |
| Speicherung | Nur SHA-256-Hash in der Datenbank |
| Generierung | `RandomNumberGenerator.GetBytes(32)` |
| Vergleich | `CryptographicOperations.FixedTimeEquals` |
| Access-Token-Lebensdauer | 1 Stunde |
| Refresh-Token-Lebensdauer | 30 Tage, Rotation bei jedem Refresh |
| Scope | Nur `mcp:full` (Vollzugriff auf den MCP-Server) |

---

## Vollständiger Flow (Zusammenfassung)

```
Client                          Geef.Atelier                    Browser/Nutzer
  │                                 │                                 │
  │── GET /.well-known/oauth-... ──>│                                 │
  │<── Server-Metadaten ────────────│                                 │
  │                                 │                                 │
  │── POST /oauth/register ────────>│                                 │
  │<── client_id ───────────────────│                                 │
  │                                 │                                 │
  │── Öffne Browser mit GET ────────────────────────────────────────>│
  │   /oauth/authorize?...          │                                 │
  │                                 │<── Login (falls nötig) ─────────│
  │                                 │<── Consent "Zugriff gewähren" ──│
  │                                 │── Redirect ?code=... ──────────>│
  │<── code (via redirect_uri) ─────────────────────────────────────│
  │                                 │                                 │
  │── POST /oauth/token ───────────>│                                 │
  │   (code + code_verifier)        │                                 │
  │<── access_token + refresh_token─│                                 │
  │                                 │                                 │
  │── POST /mcp ───────────────────>│                                 │
  │   Authorization: Bearer <token> │                                 │
  │<── Tool-Response ───────────────│                                 │
```
