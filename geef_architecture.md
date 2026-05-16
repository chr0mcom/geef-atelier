# Geef.Atelier — OAuth 2.1 Authorization Server: Architectural Blueprint

**Scope:** Step 19 (MCP OAuth). Binding document for implementer. Do not negotiate decisions already listed here — open a new Decisions-Log entry first.

---

## 1. Architectural Blueprint

### Component Map

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Geef.Atelier.Core                                                       │
│  Configuration/OAuthOptions.cs          (issuer, TTLs)                  │
└─────────────────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────────────┐
│ Geef.Atelier.Application                                                │
│  Auth/                                                                  │
│    TokenValidationOutcome.cs            (replaces Task<bool>)           │
│    ITokenValidator.cs                   (MODIFIED: returns Outcome)     │
│    StaticTokenValidator.cs              (MODIFIED: wraps in Outcome)    │
│    OAuthAccessTokenValidator.cs         (NEW: DB hash lookup)           │
│    CompositeTokenValidator.cs           (NEW: static-first chain)       │
│  OAuth/                                                                 │
│    IOAuthService.cs                     (interface + request/result     │
│    OAuthServiceRequests.cs              records)                        │
│    OAuthService.cs                      (internal sealed impl)          │
│    OAuthCrypto.cs                       (internal static, no deps)      │
└─────────────────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────────────┐
│ Geef.Atelier.Infrastructure                                             │
│  Persistence/                                                           │
│    Entities/OAuthClient.cs              (EF entity)                     │
│    Entities/OAuthAuthCode.cs            (EF entity)                     │
│    Entities/OAuthAccessToken.cs         (EF entity)                     │
│    Entities/OAuthRefreshToken.cs        (EF entity)                     │
│    Entities/OAuthAuditEvent.cs          (EF entity)                     │
│    AtelierDbContext.cs                  (MODIFIED: 5 new DbSet<>)       │
│    OAuthRepository.cs                   (internal sealed, Primary Ctor) │
│    Migrations/20260519120000_Step19McpOAuth.cs                          │
│    Migrations/AtelierDbContextModelSnapshot.cs (MODIFIED)               │
└─────────────────────────────────────────────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────────────┐
│ Geef.Atelier.Web                                                        │
│  Auth/BearerTokenHandler.cs             (MODIFIED: use Outcome.Kind     │
│                                          for ClaimTypes.Role)           │
│  Endpoints/WellKnownEndpoints.cs        (NEW: /.well-known/oauth-...)   │
│  Endpoints/OAuthEndpoints.cs            (NEW: /oauth/* machine APIs)    │
│  Components/Pages/OAuthAuthorize.razor  (NEW: Consent UI, GET only)     │
│  Components/Pages/Account/              │
│    ConnectedClients.razor               (NEW: revoke UI)                │
│  Program.cs                             (MODIFIED: register + map)      │
└─────────────────────────────────────────────────────────────────────────┘
```

### Layer Responsibilities

| Layer | What it owns |
|---|---|
| Core | `OAuthOptions` config record — zero dependencies on EF/HTTP/crypto |
| Application | All OAuth business logic: code issuing, token exchange, refresh rotation, revocation, validation. `OAuthCrypto` lives here (pure static). No EF references — works through `IOAuthRepository`. |
| Infrastructure | EF entities, `OAuthRepository` (implements `IOAuthRepository`), migration SQL |
| Web | HTTP surface only: Minimal-API endpoint methods, Razor consent page, DI wiring in `Program.cs` |

### Data Flow

```
1. DISCOVERY
   Client  ──GET──►  /.well-known/oauth-authorization-server
           ◄──JSON──  {issuer, authorization_endpoint, token_endpoint,
                       registration_endpoint, revocation_endpoint,
                       code_challenge_methods_supported: ["S256"]}

2. DYNAMIC CLIENT REGISTRATION  (RFC 7591)
   Client  ──POST──►  /oauth/register
           ◄──201 + Location──  {client_id, client_secret, …}

3. AUTHORIZATION REQUEST
   Claude Desktop opens browser:
   GET /oauth/authorize?response_type=code&client_id=…&redirect_uri=…
                        &code_challenge=…&code_challenge_method=S256&scope=…
   → No cookie → BearerTokenHandler: NoResult → CookieHandler: redirect
     /login?ReturnUrl=%2Foauth%2Fauthorize%3Fresponse_type%3Dcode%26…
   → User logs in → redirect back to /oauth/authorize with full query string
   → OAuthAuthorize.razor renders consent screen

4. CONSENT APPROVED
   POST /oauth/authorize  (Minimal-API, requires cookie auth)
   Body: client_id, redirect_uri, scope, state, code_challenge, action=approve
   → OAuthService.IssueAuthCodeAsync → stores CodeHash (SHA-256 of 32-byte random)
   → Redirect to redirect_uri?code=<raw_code>&state=…

5. TOKEN EXCHANGE  (PKCE-S256 verified)
   POST /oauth/token  (no auth, client authenticates via client_secret)
   grant_type=authorization_code, code=…, code_verifier=…, …
   → OAuthService.ExchangeCodeAsync
   → Marks code as used (single-use), verifies PKCE, issues:
     access_token (opaque, 32 bytes, hash stored), refresh_token (opaque, 32 bytes)
   ← {access_token, token_type:"Bearer", expires_in, refresh_token, scope}

6. MCP CALL
   Claude Desktop ──GET /mcp, Authorization: Bearer <access_token>──►
   BearerTokenHandler → CompositeTokenValidator
     → StaticTokenValidator: FixedTimeEquals against env token → fail
     → OAuthAccessTokenValidator: hash token, DB lookup, expiry+revoked check
   ← TokenValidationOutcome { IsValid:true, Kind:"oauth-bearer", Subject, ClientId, Scope }
   BearerTokenHandler builds ClaimsPrincipal with Name=Subject, Role=Kind
   MCP tool executes normally.

7. REFRESH
   POST /oauth/token  grant_type=refresh_token
   → OAuthService.RefreshTokenAsync
   → Old refresh token revoked, new issued (rotation)
   → If incoming token was already revoked: RevokeAll for clientId+subject (RFC 6819)

8. REVOCATION  (RFC 7009)
   POST /oauth/revoke  token=…
   → OAuthService.RevokeTokenAsync (hash, mark RevokedAt)
```

---

## 2. Binding Constraints

### File Naming and Namespaces

Follow the existing project conventions exactly:

| Layer | Namespace | Pattern |
|---|---|---|
| Core | `Geef.Atelier.Core.Configuration` | `public sealed class XOptions` with `SectionName` const |
| Application | `Geef.Atelier.Application.OAuth` | `internal sealed class OAuthService(…) : IOAuthService` |
| Application | `Geef.Atelier.Application.Auth` | Same ns as `ITokenValidator` |
| Infrastructure | `Geef.Atelier.Infrastructure.Persistence` | `internal sealed class OAuthRepository(AtelierDbContext db) : IOAuthRepository` |
| Web Endpoints | `Geef.Atelier.Web.Endpoints` | `public static class OAuthEndpoints { public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder app) }` |
| Web Components | `Geef.Atelier.Web.Components.Pages` | Standard Blazor, file-scoped namespace |

File-scoped namespaces everywhere (existing convention). Primary constructors on all classes with injected deps. No Nullable-suppression pragmas — all properties properly initialized.

### Pattern Exemplars

| New file | Follow pattern from |
|---|---|
| `OAuthOptions.cs` | `AtelierMcpOptions.cs` — SectionName const, plain `set` properties |
| `OAuthService.cs` | `ApplicationAuthExtensions.cs` → `StaticTokenValidator.cs` — internal sealed, Primary Ctor |
| `OAuthCrypto.cs` | Stand-alone static class, no injected deps, no logger |
| `WellKnownEndpoints.cs` | `AuthEndpoints.cs` — static class, `Map*` extension method, `AllowAnonymous()` |
| `OAuthEndpoints.cs` | `SettingsEndpoints.cs` — static class, inline lambdas, `DisableAntiforgery()` where needed |
| `OAuthAuthorize.razor` | `Login.razor` — `@layout Layout.EmptyLayout`, `[SupplyParameterFromQuery]`, POST via `HttpContext` |
| `OAuthRepository.cs` | Internal sealed, Primary Ctor injecting `AtelierDbContext`, async with `ct` threading |
| Migration | `20260518120000_Step18DomainTemplates.cs` — raw SQL in `Up`/`Down`, no EF helpers |

### Security Invariants — MUST NEVER VIOLATE

1. **Never store a raw token.** Every token and code written to DB must be `SHA-256(rawBytes)` hex-encoded. The raw value leaves the system only in the HTTP response at issuance time.
2. **All secret comparisons via `CryptographicOperations.FixedTimeEquals`.** Never `==`, `string.Equals`, or `.SequenceEqual` for secrets.
3. **PKCE S256 required — no plain.** Reject any authorization request where `code_challenge_method != "S256"` or `code_challenge` is absent.
4. **Auth codes are single-use.** `UsedAt` is set atomically. A second exchange with the same code returns HTTP 400 immediately.
5. **Refresh-reuse → RevokeAll.** If a `RevokedAt`-marked refresh token is presented, set `RevokedAt = now()` on ALL `OAuthAccessTokens` and `OAuthRefreshTokens` where `ClientId = clientId AND Subject = subject AND RevokedAt IS NULL`.
6. **Loopback port-wildcard.** Only for `http://127.0.0.1`. Validate by parsing both URIs and comparing scheme+host+path, ignoring port. `https://` loopback must match exactly (RFC 8252 §8.3).
7. **`redirect_uri` must match registered.** Use loopback rule only. For non-loopback URIs, require byte-exact equality against the stored value.
8. **No secret in logs.** Logger may log `ClientId`, `Subject`, truncated `TokenHash[..8]`, but never raw tokens or verifiers.
9. **`OAuthAuthorize.razor` requires `@attribute [Authorize]`.** Do not remove or qualify it.
10. **POST `/oauth/authorize` requires cookie auth.** Use `.RequireAuthorization()` with the Cookie scheme policy (not the Bearer McpPolicy).

### What Must NOT Change in Existing Behavior

- `StaticTokenValidator` byte comparison logic is unchanged; it just wraps the result in `TokenValidationOutcome`.
- `CompositeTokenValidator` tries static first. If static returns `IsValid=true`, the OAuth validator is NOT called.
- `BearerTokenHandler` still extracts the raw bearer string and passes it to `ITokenValidator`. The only change is that it reads claims from `TokenValidationOutcome` instead of a bare bool.
- The `McpPolicy` in `Program.cs` must remain on the `BearerScheme`. Do not switch it to cookie auth.
- Cookie login flow (`/login`, `RedirectToLogin.razor`) is not modified.

---

## 3. Interface Contracts

### `TokenValidationOutcome` record

```csharp
// File: src/Geef.Atelier.Application/Auth/TokenValidationOutcome.cs
namespace Geef.Atelier.Application.Auth;

public sealed record TokenValidationOutcome(
    bool IsValid,
    string Kind,
    string? Subject,
    string? ClientId,
    string? Scope)
{
    public static TokenValidationOutcome Invalid { get; } =
        new(false, "none", null, null, null);
}
```

`Kind` values: `"static-bearer"` (StaticTokenValidator), `"oauth-bearer"` (OAuthAccessTokenValidator), `"none"` (failed).

### `ITokenValidator` interface (evolved)

```csharp
// File: src/Geef.Atelier.Application/Auth/ITokenValidator.cs
namespace Geef.Atelier.Application.Auth;

public interface ITokenValidator
{
    Task<TokenValidationOutcome> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default);
}
```

### `StaticTokenValidator` return (modification only)

```csharp
// Return on success:
return Task.FromResult(new TokenValidationOutcome(true, "static-bearer", "static-client", null, null));

// Return on failure:
return Task.FromResult(TokenValidationOutcome.Invalid);
```

### `BearerTokenHandler` claims build (modification)

```csharp
var outcome = await validator.ValidateTokenAsync(token, Context.RequestAborted);
if (!outcome.IsValid) return AuthenticateResult.Fail("Invalid bearer token");

var claims = new List<Claim>
{
    new(ClaimTypes.Name,    outcome.Subject  ?? "mcp-client"),
    new(ClaimTypes.Role,    outcome.Kind),
};
if (outcome.ClientId is not null) claims.Add(new(ClaimTypes.NameIdentifier, outcome.ClientId));
if (outcome.Scope    is not null) claims.Add(new("scope", outcome.Scope));
```

### `IOAuthService` interface

```csharp
// File: src/Geef.Atelier.Application/OAuth/IOAuthService.cs
namespace Geef.Atelier.Application.OAuth;

public interface IOAuthService
{
    Task<RegisterClientResult>       RegisterClientAsync(RegisterClientRequest request, CancellationToken ct = default);
    Task<ValidateAuthRequestResult>  ValidateAuthRequestAsync(ValidateAuthRequestRequest request, CancellationToken ct = default);
    Task<IssueAuthCodeResult>        IssueAuthCodeAsync(IssueAuthCodeRequest request, CancellationToken ct = default);
    Task<ExchangeCodeResult>         ExchangeCodeAsync(ExchangeCodeRequest request, CancellationToken ct = default);
    Task<RefreshTokenResult>         RefreshTokenAsync(RefreshTokenRequest request, CancellationToken ct = default);
    Task<RevokeTokenResult>          RevokeTokenAsync(RevokeTokenRequest request, CancellationToken ct = default);
    Task<TokenValidationOutcome>     ValidateAccessTokenAsync(string token, CancellationToken ct = default);
    Task<IReadOnlyList<ConnectedClientDto>> GetConnectedClientsAsync(string subject, CancellationToken ct = default);
}
```

### Request/Result Records

```csharp
// File: src/Geef.Atelier.Application/OAuth/OAuthServiceRequests.cs
namespace Geef.Atelier.Application.OAuth;

// Registration
public sealed record RegisterClientRequest(
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    string? Scope);

public sealed record RegisterClientResult(
    bool Success,
    string? ClientId,
    string? ClientSecret,  // raw — returned once, never stored
    string? Error);

// Auth request validation (before showing consent page)
public sealed record ValidateAuthRequestRequest(
    string ClientId,
    string RedirectUri,
    string ResponseType,
    string? Scope,
    string CodeChallenge,
    string CodeChallengeMethod,
    string? State);

public sealed record ValidateAuthRequestResult(
    bool IsValid,
    string? ClientName,
    string? NormalizedScope,
    string? Error,
    string? ErrorDescription);

// Code issuance (after user approves consent)
public sealed record IssueAuthCodeRequest(
    string ClientId,
    string Subject,
    string RedirectUri,
    string Scope,
    string CodeChallenge,
    string? State);

public sealed record IssueAuthCodeResult(
    bool Success,
    string? RedirectUriWithCode,  // already includes ?code=…&state=…
    string? Error);

// Token exchange
public sealed record ExchangeCodeRequest(
    string Code,
    string ClientId,
    string ClientSecret,
    string RedirectUri,
    string CodeVerifier);

public sealed record ExchangeCodeResult(
    bool Success,
    string? AccessToken,
    string? RefreshToken,
    int ExpiresIn,
    string? Scope,
    string? Error,
    string? ErrorDescription);

// Refresh
public sealed record RefreshTokenRequest(
    string RefreshToken,
    string ClientId,
    string ClientSecret,
    string? Scope);

public sealed record RefreshTokenResult(
    bool Success,
    string? AccessToken,
    string? NewRefreshToken,
    int ExpiresIn,
    string? Scope,
    string? Error,
    string? ErrorDescription);

// Revocation
public sealed record RevokeTokenRequest(
    string Token,
    string? TokenTypeHint,
    string ClientId,
    string ClientSecret);

public sealed record RevokeTokenResult(bool Success);

// UI DTO
public sealed record ConnectedClientDto(
    string ClientId,
    string ClientName,
    string Scope,
    DateTimeOffset LastUsed,
    DateTimeOffset ExpiresAt);
```

### `OAuthOptions`

```csharp
// File: src/Geef.Atelier.Core/Configuration/OAuthOptions.cs
namespace Geef.Atelier.Core.Configuration;

public sealed class OAuthOptions
{
    public const string SectionName = "OAuth";

    public string Issuer { get; set; } = "https://geef.stefan-bechtel.de";
    public int AccessTokenLifetimeSeconds { get; set; } = 3600;
    public int RefreshTokenLifetimeDays { get; set; } = 30;
    public int AuthCodeLifetimeSeconds { get; set; } = 600;
}
```

Env-var fallback (same pattern as `AddAtelierMcpAuth`): in `ApplicationOAuthExtensions.AddAtelierOAuth`:

```csharp
services.Configure<OAuthOptions>(opts =>
{
    configuration.GetSection(OAuthOptions.SectionName).Bind(opts);
    var issuer = Environment.GetEnvironmentVariable("ATELIER_OAUTH_ISSUER");
    if (!string.IsNullOrEmpty(issuer)) opts.Issuer = issuer;
});
```

### `OAuthCrypto`

```csharp
// File: src/Geef.Atelier.Application/OAuth/OAuthCrypto.cs
namespace Geef.Atelier.Application.OAuth;

internal static class OAuthCrypto
{
    // Generates a cryptographically random token string (Base64Url, ~43 chars for 32 bytes)
    internal static string GenerateToken()

    // SHA-256 hex digest of a token string (UTF-8 bytes)
    internal static string HashToken(string token)

    // Verifies PKCE S256: SHA256(ASCII(code_verifier)) == Base64Url.decode(code_challenge)
    // All comparisons via CryptographicOperations.FixedTimeEquals
    internal static bool VerifyPkceS256(string codeVerifier, string codeChallenge)

    // Fixed-time string comparison (UTF-8 bytes)
    internal static bool FixedTimeEquals(string a, string b)
}
```

### Minimal-API Endpoint Signatures

```csharp
// WellKnownEndpoints.cs
app.MapGet("/.well-known/oauth-authorization-server",
    (IOptions<OAuthOptions> opts) => Results.Ok(BuildMetadata(opts.Value)))
    .AllowAnonymous()
    .WithName("OAuthMetadata")
    .Produces<OAuthMetadataDto>(200);

// OAuthEndpoints.cs
app.MapPost("/oauth/register",
    async ([FromBody] DynRegRequest req, IOAuthService svc, CancellationToken ct) => …)
    .AllowAnonymous()
    .DisableAntiforgery();

app.MapPost("/oauth/token",
    async (HttpContext ctx, IOAuthService svc, CancellationToken ct) => …)
    .AllowAnonymous()
    .DisableAntiforgery();

app.MapPost("/oauth/revoke",
    async (HttpContext ctx, IOAuthService svc, CancellationToken ct) => …)
    .AllowAnonymous()
    .DisableAntiforgery();

// Consent form POST — processes approve/deny; requires cookie auth (NOT McpPolicy)
app.MapPost("/oauth/authorize",
    async (HttpContext ctx, IOAuthService svc, CancellationToken ct) => …)
    .RequireAuthorization()   // default policy = Cookie scheme
    .DisableAntiforgery();    // CSRF mitigated by state param + cookie-bound session
```

`/oauth/token` and `/oauth/register` read `Content-Type: application/x-www-form-urlencoded` via `ctx.Request.Form` (match RFC 6749 §4.1.3).

`OAuthAuthorize.razor` handles only `GET /oauth/authorize` and renders the consent form. It uses `[SupplyParameterFromQuery]` for all OAuth params.

---

## 4. DB Schema — Migration 20260519120000_Step19McpOAuth

```sql
-- Up migration

CREATE TABLE "OAuthClients" (
    "Id"               uuid        NOT NULL DEFAULT gen_random_uuid(),
    "ClientId"         text        NOT NULL,
    "ClientSecretHash" text        NOT NULL,
    "ClientName"       text        NOT NULL,
    "RedirectUris"     text[]      NOT NULL,
    "Scopes"           text        NOT NULL DEFAULT 'mcp',
    "CreatedAt"        timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT "PK_OAuthClients" PRIMARY KEY ("Id")
);
CREATE UNIQUE INDEX "IX_OAuthClients_ClientId" ON "OAuthClients" ("ClientId");

CREATE TABLE "OAuthAuthCodes" (
    "Id"            uuid        NOT NULL DEFAULT gen_random_uuid(),
    "CodeHash"      text        NOT NULL,
    "ClientId"      text        NOT NULL,
    "Subject"       text        NOT NULL,
    "RedirectUri"   text        NOT NULL,
    "Scope"         text        NOT NULL,
    "CodeChallenge" text        NOT NULL,
    "ExpiresAt"     timestamptz NOT NULL,
    "UsedAt"        timestamptz     NULL,
    "CreatedAt"     timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT "PK_OAuthAuthCodes" PRIMARY KEY ("Id")
);
CREATE UNIQUE INDEX "IX_OAuthAuthCodes_CodeHash" ON "OAuthAuthCodes" ("CodeHash");
CREATE INDEX "IX_OAuthAuthCodes_ClientId" ON "OAuthAuthCodes" ("ClientId");
-- Partial index for fast active-code lookup
CREATE INDEX "IX_OAuthAuthCodes_Active"
    ON "OAuthAuthCodes" ("CodeHash")
    WHERE "UsedAt" IS NULL AND "ExpiresAt" > now();

CREATE TABLE "OAuthAccessTokens" (
    "Id"         uuid        NOT NULL DEFAULT gen_random_uuid(),
    "TokenHash"  text        NOT NULL,
    "ClientId"   text        NOT NULL,
    "Subject"    text        NOT NULL,
    "Scope"      text        NOT NULL,
    "ExpiresAt"  timestamptz NOT NULL,
    "RevokedAt"  timestamptz     NULL,
    "CreatedAt"  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT "PK_OAuthAccessTokens" PRIMARY KEY ("Id")
);
CREATE UNIQUE INDEX "IX_OAuthAccessTokens_TokenHash" ON "OAuthAccessTokens" ("TokenHash");
CREATE INDEX "IX_OAuthAccessTokens_ClientSubject"
    ON "OAuthAccessTokens" ("ClientId", "Subject");
-- Partial index for fast live-token validation path (hottest query)
CREATE INDEX "IX_OAuthAccessTokens_Live"
    ON "OAuthAccessTokens" ("TokenHash")
    WHERE "RevokedAt" IS NULL AND "ExpiresAt" > now();

CREATE TABLE "OAuthRefreshTokens" (
    "Id"                  uuid        NOT NULL DEFAULT gen_random_uuid(),
    "TokenHash"           text        NOT NULL,
    "ClientId"            text        NOT NULL,
    "Subject"             text        NOT NULL,
    "Scope"               text        NOT NULL,
    "FamilyId"            uuid        NOT NULL,
    "ReplacedByTokenHash" text            NULL,
    "ExpiresAt"           timestamptz NOT NULL,
    "RevokedAt"           timestamptz     NULL,
    "CreatedAt"           timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT "PK_OAuthRefreshTokens" PRIMARY KEY ("Id")
);
CREATE UNIQUE INDEX "IX_OAuthRefreshTokens_TokenHash" ON "OAuthRefreshTokens" ("TokenHash");
CREATE INDEX "IX_OAuthRefreshTokens_ClientSubject"
    ON "OAuthRefreshTokens" ("ClientId", "Subject");
CREATE INDEX "IX_OAuthRefreshTokens_FamilyId" ON "OAuthRefreshTokens" ("FamilyId");

CREATE TABLE "OAuthAuditEvents" (
    "Id"          uuid        NOT NULL DEFAULT gen_random_uuid(),
    "EventType"   text        NOT NULL,
    "ClientId"    text            NULL,
    "Subject"     text            NULL,
    "IpAddress"   text            NULL,
    "Details"     jsonb           NULL,
    "OccurredAt"  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT "PK_OAuthAuditEvents" PRIMARY KEY ("Id")
);
CREATE INDEX "IX_OAuthAuditEvents_ClientId"  ON "OAuthAuditEvents" ("ClientId");
CREATE INDEX "IX_OAuthAuditEvents_Subject"   ON "OAuthAuditEvents" ("Subject");
CREATE INDEX "IX_OAuthAuditEvents_OccurredAt" ON "OAuthAuditEvents" ("OccurredAt" DESC);
```

```sql
-- Down migration

DROP TABLE IF EXISTS "OAuthAuditEvents";
DROP TABLE IF EXISTS "OAuthRefreshTokens";
DROP TABLE IF EXISTS "OAuthAccessTokens";
DROP TABLE IF EXISTS "OAuthAuthCodes";
DROP TABLE IF EXISTS "OAuthClients";
```

### Schema Notes

- `OAuthClients.ClientId` is the public-facing string (e.g. `atelier_<uuid_prefix>`), not the PK. PK is internal UUID.
- `OAuthAuthCodes.CodeChallenge` stores the Base64Url-encoded S256 challenge as received from client.
- `OAuthRefreshTokens.FamilyId`: all refresh tokens in one rotation chain share the same `FamilyId`. A reused-revoked token triggers `RevokeAll WHERE FamilyId = @familyId` (tighter than client+subject).
- No FK constraints to `OAuthClients` by design: client deletion revokes tokens at application layer, and FK cascades would be noisy across the audit trail.
- Audit `EventType` values: `client_registered`, `auth_code_issued`, `token_issued`, `token_refreshed`, `token_revoked`, `refresh_reuse_detected`.

---

## 5. Files to Create / Modify

| File path | Action | Purpose |
|---|---|---|
| `src/Geef.Atelier.Core/Configuration/OAuthOptions.cs` | **Create** | Config options: issuer, TTLs, env-var fallback |
| `src/Geef.Atelier.Application/Auth/TokenValidationOutcome.cs` | **Create** | New return type for ITokenValidator |
| `src/Geef.Atelier.Application/Auth/ITokenValidator.cs` | **Modify** | Change return type from `Task<bool>` to `Task<TokenValidationOutcome>` |
| `src/Geef.Atelier.Application/Auth/StaticTokenValidator.cs` | **Modify** | Wrap bool result in `TokenValidationOutcome`; logic unchanged |
| `src/Geef.Atelier.Application/Auth/OAuthAccessTokenValidator.cs` | **Create** | Hashes token, DB lookup via IOAuthService.ValidateAccessTokenAsync |
| `src/Geef.Atelier.Application/Auth/CompositeTokenValidator.cs` | **Create** | Static-first chain; registered as `ITokenValidator` |
| `src/Geef.Atelier.Application/Auth/ApplicationAuthExtensions.cs` | **Modify** | Register `CompositeTokenValidator` as `ITokenValidator`; keep static reg as named/internal |
| `src/Geef.Atelier.Application/OAuth/IOAuthService.cs` | **Create** | Full interface + all request/result records |
| `src/Geef.Atelier.Application/OAuth/OAuthServiceRequests.cs` | **Create** | All request/result record types (separate file keeps IOAuthService readable) |
| `src/Geef.Atelier.Application/OAuth/OAuthService.cs` | **Create** | `internal sealed class OAuthService` — all OAuth business logic |
| `src/Geef.Atelier.Application/OAuth/OAuthCrypto.cs` | **Create** | `internal static class OAuthCrypto` — token gen, hashing, PKCE verify |
| `src/Geef.Atelier.Application/OAuth/IOAuthRepository.cs` | **Create** | Persistence abstraction (keeps Application EF-free) |
| `src/Geef.Atelier.Application/OAuth/ApplicationOAuthExtensions.cs` | **Create** | `AddAtelierOAuth(this IServiceCollection, IConfiguration)` — registers IOAuthService, OAuthOptions |
| `src/Geef.Atelier.Infrastructure/Persistence/Entities/OAuthClient.cs` | **Create** | EF entity |
| `src/Geef.Atelier.Infrastructure/Persistence/Entities/OAuthAuthCode.cs` | **Create** | EF entity |
| `src/Geef.Atelier.Infrastructure/Persistence/Entities/OAuthAccessToken.cs` | **Create** | EF entity |
| `src/Geef.Atelier.Infrastructure/Persistence/Entities/OAuthRefreshToken.cs` | **Create** | EF entity |
| `src/Geef.Atelier.Infrastructure/Persistence/Entities/OAuthAuditEvent.cs` | **Create** | EF entity |
| `src/Geef.Atelier.Infrastructure/Persistence/AtelierDbContext.cs` | **Modify** | Add 5 `DbSet<>` properties |
| `src/Geef.Atelier.Infrastructure/Persistence/OAuthRepository.cs` | **Create** | `internal sealed class OAuthRepository(AtelierDbContext db) : IOAuthRepository` |
| `src/Geef.Atelier.Infrastructure/Persistence/InfrastructurePersistenceExtensions.cs` | **Modify** | Register `IOAuthRepository → OAuthRepository` |
| `src/Geef.Atelier.Infrastructure/Persistence/Migrations/20260519120000_Step19McpOAuth.cs` | **Create** | Raw SQL Up/Down — 5 tables + indexes |
| `src/Geef.Atelier.Infrastructure/Persistence/Migrations/AtelierDbContextModelSnapshot.cs` | **Modify** | Add 5 entity snapshots |
| `src/Geef.Atelier.Web/Auth/BearerTokenHandler.cs` | **Modify** | Build claims from `TokenValidationOutcome` instead of bare bool |
| `src/Geef.Atelier.Web/Endpoints/WellKnownEndpoints.cs` | **Create** | `GET /.well-known/oauth-authorization-server` → RFC 8414 JSON |
| `src/Geef.Atelier.Web/Endpoints/OAuthEndpoints.cs` | **Create** | `POST /oauth/register`, `/oauth/token`, `/oauth/revoke`, `/oauth/authorize` |
| `src/Geef.Atelier.Web/Components/Pages/OAuthAuthorize.razor` | **Create** | Consent page: `@layout Layout.EmptyLayout`, `@attribute [Authorize]`, `[SupplyParameterFromQuery]` for all OAuth params |
| `src/Geef.Atelier.Web/Components/Pages/Account/ConnectedClients.razor` | **Create** | Revoke UI: list active OAuth tokens for current user, revoke button |
| `src/Geef.Atelier.Web/Program.cs` | **Modify** | Add `AddAtelierOAuth`, `MapWellKnownEndpoints`, `MapOAuthEndpoints` |
| `tests/Geef.Atelier.Tests/OAuth/OAuthCryptoTests.cs` | **Create** | PKCE verify, hash consistency, FixedTimeEquals |
| `tests/Geef.Atelier.Tests/OAuth/OAuthServiceTests.cs` | **Create** | Full test matrix per `geef_plan.md` test strategy |
| `tests/Geef.Atelier.Tests/OAuth/OAuthEndpointTests.cs` | **Create** | WebApplicationFactory integration tests |
| `tests/Geef.Atelier.Tests/Auth/CompositeTokenValidatorTests.cs` | **Create** | Static-first chain, backwards-compat |

### Registration Wiring in `Program.cs`

```csharp
// After existing AddAtelierMcpAuth:
builder.Services.AddAtelierOAuth(builder.Configuration);

// After app.MapSettingsEndpoints():
app.MapWellKnownEndpoints();
app.MapOAuthEndpoints();
```

`AddAtelierMcpAuth` in `ApplicationAuthExtensions.cs` must change its registration:
```csharp
// Before (delete):
services.AddScoped<ITokenValidator, StaticTokenValidator>();

// After:
services.AddScoped<StaticTokenValidator>();
// CompositeTokenValidator registered by AddAtelierOAuth (which depends on AddAtelierMcpAuth having run first)
// Composite is registered as the ITokenValidator singleton of the DI graph
```

Dependency order: `AddAtelierMcpAuth` → `AddAtelierOAuth` → `CompositeTokenValidator` is registered as `ITokenValidator`.

---

## Appendix: `OAuthMetadataDto` shape (RFC 8414)

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
  "token_endpoint_auth_methods_supported": ["client_secret_post"],
  "scopes_supported": ["mcp"]
}
```

All URLs are assembled from `OAuthOptions.Issuer` at runtime — never hardcoded in endpoint bodies.

---

## Accepted Deviations from Blueprint (Evaluation Iteration 1 + 2)

These deviations were identified in the R1/R4 Architectural Reviews and are **intentionally accepted**. Reviewers in subsequent iterations must not re-flag them as findings.

| ID | Blueprint Spec | Actual Implementation | Rationale |
|---|---|---|---|
| C1 | `IOAuthService` uses request-record parameter types (e.g. `RegisterClientRequest`, `ValidateAuthRequestRequest`) for all methods | Methods use explicit typed parameters (string, primitives); response types use purpose-specific records (`ClientRegistrationResult`, `TokenResponse`, `TokenValidationResult`, `ConnectedClientInfo`) | The codebase convention (see `RunService`, `SettingsEndpoints`) is explicit typed parameters rather than wrapper records for every method. The request-record pattern from the blueprint adds ceremony without benefit for the single-caller, internal service. Existing patterns in the repo (RunService, OrchestratorService) do not use monolithic request records. |
| C3 | `OAuthCrypto.HashToken`: SHA-256 **hex** digest of token string (UTF-8 bytes) | SHA-256 **Base64Url** encoding (ASCII bytes for ASCII-safe opaque tokens; ASCII/UTF-8 are identical for the Base64Url token alphabet) | Base64Url produces shorter hashes (43 chars vs 64 chars hex) that fit DB `text` columns more efficiently. Tokens are opaque, the hash just needs internal consistency (which it has — all encode/verify paths use the same function). The ASCII/UTF-8 distinction is a non-issue since generated tokens contain only Base64Url characters. Changing encoding post-deploy would invalidate all existing tokens; the deviation is therefore permanent. |
| C5 | Refresh-token reuse detection via `FamilyId` column; revoke only tokens WHERE `FamilyId = @familyId` | No `FamilyId`; reuse detection revokes all tokens for the user across all clients via `RevokeAllUserTokensAsync` | More conservative (broader) than spec, not less. An attacker with a stolen refresh token gets all their access revoked, not just the stolen rotation chain. The "DoS" concern (revoking a legitimate session) is acceptable given the single-user, high-trust deployment context. `FamilyId` adds schema complexity (column, index, cascade logic) for a marginal improvement in user-experience that is not needed here. |
| M10 | Single `IOAuthRepository` in `Application/OAuth/` wrapping all persistence operations | Five separate repository interfaces in `Core/Persistence/OAuth/`: `IOAuthClientRepository`, `IOAuthAccessTokenRepository`, `IOAuthRefreshTokenRepository`, `IOAuthAuthorizationCodeRepository`, `IOAuthAuditLogRepository` | The split-repo pattern is the **established codebase convention** (see `IRunRepository`, `IIterationRepository`, `IFindingRepository`, etc. all in `Core/Persistence/`). A monolithic `IOAuthRepository` would be the only aggregated repo in the codebase and would create a 1,000-line God-class. The blueprint's single-repo suggestion was a simplification that conflicts with the actual project patterns. |
| M11 | `OAuthOptions` property names: `AccessTokenLifetimeSeconds`, `RefreshTokenLifetimeDays`, `AuthCodeLifetimeSeconds` | Property names: `AccessTokenTtlHours`, `RefreshTokenTtlDays`, `AuthorizationCodeTtlMinutes`, plus `CleanupIntervalMinutes` and `RegistrationToken` | The TTL-suffix naming (`TtlHours`, `TtlMinutes`) is more idiomatic for the project (matches `OrchestratorOptions.PollingInterval`, `ConvergenceOptions` naming). `CleanupIntervalMinutes` is needed by `OAuthCleanupBackgroundService`. `RegistrationToken` is an optional secret to gate dynamic client registration — its natural home is `OAuthOptions` alongside `Issuer`. |
| M13 | Table names: `OAuthAuthCodes`, `OAuthAuditEvents`; column names: `Subject` (not `UserId`); `OAuthClients.Id` UUID PK + separate `ClientId` text; no FK constraints; partial indexes | Table names: `OAuthAuthorizationCodes`, `OAuthAuditLog`; column name: `UserId`; `ClientId` is the direct PK; FK ON DELETE CASCADE present; no partial indexes | `OAuthAuthorizationCodes` is more readable and consistent with EF naming conventions. `UserId` is the system-wide identifier for the single user; using `Subject` would be inconsistent with `RunEntity.UserId`, claim `ClaimTypes.Name`, etc. Direct `ClientId` PK avoids a redundant UUID column (the blueprint's dual-key pattern provides no benefit for the single-client use case). FK cascades prevent orphan rows, which is a correctness property. Partial indexes would require dynamic SQL or `HasFilter()` in EF config; the performance benefit is negligible at the expected token volume. |
| M19 | File name: `OAuthServiceRequests.cs` | File name: `OAuthServiceRecords.cs` | "Records" is more accurate since the file contains both request and result record types. Zero functional impact. |
| IT2-MINOR-R1 | RFC 7009 §2.1 SHOULD: `token_type_hint` parameter for hint-based revocation lookup | `token_type_hint` query parameter is accepted but ignored; server always checks both access-token and refresh-token tables | RFC 7009 defines `token_type_hint` as a SHOULD for performance optimization (early exit), not a MUST for correctness. At single-user/single-client scale the two-table lookup is negligible (sub-millisecond). Parsing the hint value, trusting client-supplied input to alter server-side lookup order, and handling invalid hint values adds code complexity with zero observable benefit. The server still correctly revokes the token in all cases. |
| IT2-LOW-R4 | `scopes_supported` in `/.well-known/oauth-authorization-server` RFC 8414 field: `["mcp"]` | `scopes_supported: ["mcp:full"]` | `"mcp:full"` is the actual scope string used throughout the implementation: `ValidateAuthorizationRequestAsync`, token exchange, `TokenResponse.Scope`, `OAuthAccessToken.Scope`. Changing only the metadata to `"mcp"` while keeping `"mcp:full"` in the code path would create a mismatch where the Authorization Server advertises a scope it does not grant. Both values are non-standard; `"mcp:full"` is more descriptive. This value is an implementation detail, not an interoperability concern (the RFC does not mandate a specific scope string for MCP). |
