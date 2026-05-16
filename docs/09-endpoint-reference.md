# Endpoint-Referenz

*Letzte Aktualisierung: 2026-05-16*

Alle HTTP-Endpunkte von Geef.Atelier, die extern erreichbar sind. Basis-URL: `https://geef.stefan-bechtel.de`.

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
