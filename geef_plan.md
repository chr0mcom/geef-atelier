# GEEF Plan — MCP OAuth 2.1 Authorization Server

## Task

Implementiere einen self-hosted OAuth-2.1-Authorization-Server in Geef.Atelier, der Claude Desktop Custom Connectors UI und Claude.ai Web Custom Connectors ermöglicht. Der Server muss RFC 8414 (Authorization Server Metadata), RFC 7591 (Dynamic Client Registration), Authorization-Code-Flow mit Pflicht-PKCE/S256, Token-Endpoint, RFC 7009 Revocation und RFC 8252 Loopback-Unterstützung implementieren. Die bestehende statische Bearer-Token-Auth (Claude Code CLI) bleibt parallel voll funktional. Opaque-Tokens als SHA-256-Hash in der DB, Audit-Trail, Cookie-Auth-geschützte Consent-Page.

**Erfolgs-Kriterium:** Claude Desktop Custom Connector mit `https://geef.stefan-bechtel.de/mcp` als URL verbindet sich via OAuth, durchläuft Browser-Login + Consent, und die MCP-Tools sind benutzbar. Claude Code CLI mit statischem Bearer-Token funktioniert unverändert.

## Architecture Decisions (Knackpunkte — bereits vorab per User gelöst)

1. **Endpoints = Minimal-API + Razor-Consent** (User-Entscheidung). Maschinen-Endpoints in `OAuthEndpoints.cs`/`WellKnownEndpoints.cs`, Consent-UI in `OAuthAuthorize.razor` (server-rendered wie Login.razor).
2. **Service-Layer in Application**. `IOAuthService` + Records in `Geef.Atelier.Application/OAuth/`, Impl `internal sealed class OAuthService`. Krypto als `OAuthCrypto` (internal static).
3. **`ITokenValidator` evolvieren** → `TokenValidationOutcome { bool IsValid; string Kind; string? Subject; string? ClientId; string? Scope; }`. `CompositeTokenValidator` = static first, dann OAuth. Backwards-Compat: statischer Pfad bit-identisch.
4. **Cookie-Auth-Integration** — bereits gelöst: `/oauth/authorize` nur `@attribute [Authorize]`; `RedirectToLogin.razor` escaped vollständiges `PathAndQuery` → alle OAuth-Params überleben Login-Roundtrip.
5. **Loopback**: registrierte `http://127.0.0.1/callback` matcht beliebigen Port (RFC 8252). Refresh-Reuse → alle Tokens widerrufen (RFC 6819).
6. **Issuer**: `OAuthOptions` in `Core/Configuration/`, Sektion `OAuth`, Env-Fallback-Pattern. Issuer fix `https://geef.stefan-bechtel.de`.

## Test-Strategie

**Application-Tests (xUnit + Testcontainers.PostgreSql):**
- ClientRegistration: gültige/ungültige redirect_uris, Pflichtfelder
- AuthCode-TTL (10 min), Single-Use-Enforcement
- TokenExchange + PKCE-S256-Verification
- Refresh-Rotation + Reuse-Detection → RevokeAll
- TokenValidation: gültig, abgelaufen, revoked

**Security-Tests (nicht optional):**
- No-PKCE-Reject
- Code-Reuse-Reject (zweiter Exchange → 400)
- Wrong-Client, Wrong-Redirect, Expired
- Refresh-Reuse → RevokeAll

**Endpoint-Integration (WebApplicationFactory + TestAuthenticationHandler):**
- Well-Known RFC-8414-Shape (JSON-Keys)
- Register RFC-7591 (Location-Header, client_id in Body)
- Token-/Refresh-Flow E2E
- `/oauth/authorize` Cookie-Schutz (Redirect zu /login ohne Cookie)

**End-to-End:**
- Register → Authorize → Exchange → MCP-Call mit OAuth-Token
- Refresh-Rotation
- Revoke → 401 bei nächstem MCP-Call

**Backwards-Compat:**
- `McpStaticBearerTokenStillWorks`
- `ClaudeCodeCLICompatibility` (WebApplicationFactory + statischer Token)

**bUnit:**
- `OAuthAuthorize.razor` (data-testid, invalid request → error state)
- `ConnectedClients.razor` (Revoke-Button, Bestätigung)

**Live-UI (Szenario B/C/D — User-getrieben):**
- B: Claude Desktop Custom Connector OAuth-Flow
- C: Revoke in `/account/connected-clients`
- D: Refresh-Rotation
- User führt durch und meldet Ergebnis; ich dokumentiere im Report.

## Implementierungs-Reihenfolge

A → B → C → D → E → F (Reviewer-Loop) → G (Deploy)

Siehe Implementation-Plan in `/home/developer/.claude/plans/streamed-launching-moonbeam.md` für Details.
