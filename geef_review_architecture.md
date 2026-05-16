# Architectural Review: MCP OAuth 2.1 Implementation (Step 19)

**Reviewer:** Claude Code (claude-sonnet-4-6)  
**Date:** 2026-05-16  
**Binding document:** `geef_architecture.md`  
**Scope:** All files listed under Step 2 of the review prompt.

---

## Findings

### CRITICAL

**1. IOAuthService interface signature diverges from blueprint (multiple methods)**

Blueprint (`geef_architecture.md` §3) specifies a request-record-based interface:
- `Task<RegisterClientResult> RegisterClientAsync(RegisterClientRequest request, …)`
- `Task<ValidateAuthRequestResult> ValidateAuthRequestAsync(ValidateAuthRequestRequest request, …)`
- `Task<IssueAuthCodeResult> IssueAuthCodeAsync(IssueAuthCodeRequest request, …)`
- `Task<ExchangeCodeResult> ExchangeCodeAsync(ExchangeCodeRequest request, …)`
- `Task<RefreshTokenResult> RefreshTokenAsync(RefreshTokenRequest request, …)`
- `Task<RevokeTokenResult> RevokeTokenAsync(RevokeTokenRequest request, …)`
- `Task<TokenValidationOutcome> ValidateAccessTokenAsync(string token, …)` ← returns `TokenValidationOutcome`
- `Task<IReadOnlyList<ConnectedClientDto>> GetConnectedClientsAsync(string subject, …)` ← `ConnectedClientDto`

Implementation (`IOAuthService.cs`) has a completely different shape: raw-string parameters instead of request records, returns `Task<string>` from `CreateAuthorizationCodeAsync`, returns `Task<TokenValidationResult>` (a separate private record) from `ValidateAccessTokenAsync`, and uses `ConnectedClientInfo` instead of `ConnectedClientDto`. Extra methods (`RevokeAllUserTokensAsync`, `LogEventAsync`, `GetActiveTokensForUserAsync`) are present that the blueprint does not include. This breaks the contract that `OAuthAccessTokenValidator` and endpoints rely on.

**Files:** `src/Geef.Atelier.Application/OAuth/IOAuthService.cs`, `src/Geef.Atelier.Application/OAuth/OAuthServiceRecords.cs`

---

**2. `TokenValidationOutcome.Invalid` is a method, not a static property; `Kind` for the invalid case is `""` not `"none"`**

Blueprint (§3) specifies:
```csharp
public static TokenValidationOutcome Invalid { get; } =
    new(false, "none", null, null, null);
```

Implementation declares:
```csharp
public static TokenValidationOutcome Invalid() => new(false, "", null, null, null);
```

Two deviations: (a) method `Invalid()` vs. property `Invalid`; this breaks call sites in `StaticTokenValidator` and `OAuthAccessTokenValidator` which call `TokenValidationOutcome.Invalid` as a property (they compile only if changed to `Invalid()`). More importantly (b) `Kind` is `""` instead of `"none"`. The blueprint's §3 enumerates `"none"` as the required value for failed outcomes. If downstream code ever switches on `Kind`, the empty string will not match.

**File:** `src/Geef.Atelier.Application/Auth/ITokenValidator.cs` (lines 11-12)

---

**3. `OAuthCrypto.HashToken` uses Base64Url encoding, not hex; uses ASCII bytes for general token hashing**

Blueprint (§3, `OAuthCrypto` contract):
> `SHA-256 hex digest of a token string (UTF-8 bytes)`

Implementation:
```csharp
public static string Sha256Base64Url(string input)
{
    var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(input));   // ASCII, not UTF-8
    return Base64UrlEncode(bytes);                                 // Base64Url, not hex
}
public static string HashToken(string plainToken) => Sha256Base64Url(plainToken);
```

The encoding scheme diverges from the spec in two ways. Since tokens are opaque ASCII-safe Base64Url strings, the ASCII/UTF-8 distinction is benign in practice, but the Base64Url-vs-hex difference means the stored `TokenHash` values will not match what the spec calls for. This also misaligns the schema column definition which the blueprint implies stores hex (`text` with documented values like `TokenHash[..8]` for log truncation).

**File:** `src/Geef.Atelier.Application/OAuth/OAuthCrypto.cs`

---

**4. Security invariant violated: registration token compared with `!=` (not `FixedTimeEquals`)**

Blueprint §2 security invariant 2:
> All secret comparisons via `CryptographicOperations.FixedTimeEquals`. Never `==`, `string.Equals`, or `.SequenceEqual` for secrets.

`OAuthEndpoints.cs` line 28:
```csharp
authHeader["Bearer ".Length..] != registrationToken
```

This is a plain string `!=` comparison against a secret registration token. A timing-safe comparison must be used instead.

**File:** `src/Geef.Atelier.Web/Endpoints/OAuthEndpoints.cs` (line 28)

---

**5. Security invariant violated: `FamilyId`-based refresh-token reuse detection not implemented**

Blueprint §2 security invariant 5:
> If a `RevokedAt`-marked refresh token is presented, set `RevokedAt = now()` on ALL `OAuthAccessTokens` and `OAuthRefreshTokens` where `ClientId = clientId AND Subject = subject AND RevokedAt IS NULL`.

Blueprint Schema Notes (§4):
> `OAuthRefreshTokens.FamilyId`: all tokens in one rotation chain share the same FamilyId. A reused-revoked token triggers `RevokeAll WHERE FamilyId = @familyId`.

Implementation has no `FamilyId` field at all. `OAuthService.RefreshTokenAsync` falls back to `RevokeAllUserTokensAsync(existing.UserId)`, which revokes all tokens for the user across ALL clients — broader than the spec requires and potentially a DoS vector. Additionally, `ConsumeAsync` already marks the token as "used" before `RefreshTokenAsync` detects reuse, so the `FindByHashAsync` call to detect re-presented revoked tokens may arrive after the token has been consumed, creating a race condition in the reuse detection path.

**Files:** `src/Geef.Atelier.Core/Domain/OAuth/OAuthRefreshToken.cs`, `src/Geef.Atelier.Infrastructure/Persistence/Migrations/20260519120000_Step19McpOAuth.cs`, `src/Geef.Atelier.Application/OAuth/OAuthService.cs`

---

**6. Security invariant violated: loopback port-wildcard applied to `localhost`, not only `http://127.0.0.1`**

Blueprint §2 security invariant 6:
> Loopback port-wildcard. **Only for `http://127.0.0.1`**. `https://` loopback must match exactly (RFC 8252 §8.3).

Implementation (`OAuthService.cs` lines 277-281):
```csharp
private static bool IsLoopbackUri(string uri)
{
    if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
    return u.Host is "127.0.0.1" or "localhost";  // ← "localhost" not allowed by spec
}
```

The port-wildcard exemption must not be granted to `localhost`; only `127.0.0.1` qualifies under RFC 8252 §8.3 as interpreted by the blueprint.

**File:** `src/Geef.Atelier.Application/OAuth/OAuthService.cs` (lines 277-281)

---

### MAJOR

**7. `OAuthCrypto` public/internal visibility mismatch**

Blueprint (§3): `internal static class OAuthCrypto` with `internal static` methods.

Implementation: `internal static class OAuthCrypto` — class is correctly `internal`, but all methods (`GenerateToken`, `Sha256Base64Url`, `VerifyPkceS256`, `HashToken`) are declared `public static` instead of `internal static`. `InternalsVisibleTo` is configured for the test project, so `internal` would still be accessible from tests. `public` methods on an `internal` class are effectively `internal`, but the mismatch with the spec is an explicit convention violation.

**File:** `src/Geef.Atelier.Application/OAuth/OAuthCrypto.cs` (lines 8, 14, 20, 29)

---

**8. `OAuthAccessTokenValidator` and `CompositeTokenValidator` are in wrong namespace and folder**

Blueprint §2 / Section 5:
- `src/Geef.Atelier.Application/Auth/OAuthAccessTokenValidator.cs` → namespace `Geef.Atelier.Application.Auth`
- `src/Geef.Atelier.Application/Auth/CompositeTokenValidator.cs` → namespace `Geef.Atelier.Application.Auth`

Implementation places both in `src/Geef.Atelier.Application/OAuth/` with namespace `Geef.Atelier.Application.OAuth`. These are auth-chain components, not OAuth business logic; the blueprint explicitly places them in the `Auth/` folder to maintain separation and keep `IOAuthService` as the only OAuth dependency of the auth chain.

**Files:** `src/Geef.Atelier.Application/OAuth/OAuthAccessTokenValidator.cs`, `src/Geef.Atelier.Application/OAuth/CompositeTokenValidator.cs`

---

**9. `CompositeTokenValidator` and `OAuthAccessTokenValidator` registered in `AddAtelierMcpAuth`, not in a separate `AddAtelierOAuth`**

Blueprint §5 (Registration Wiring):
> `AddAtelierMcpAuth` registers `StaticTokenValidator` as named/internal only (not as `ITokenValidator`).  
> `CompositeTokenValidator` is registered as `ITokenValidator` by `AddAtelierOAuth` (which runs after `AddAtelierMcpAuth`).  
> A separate file `ApplicationOAuthExtensions.cs` owns `AddAtelierOAuth`.

Implementation registers `OAuthAccessTokenValidator` and `CompositeTokenValidator` inside `AddAtelierMcpAuth` in `ApplicationAuthExtensions.cs`. No separate `ApplicationOAuthExtensions.cs` file exists. This couples MCP-auth setup to OAuth before OAuth dependencies are available, reverses the intended dependency ordering, and violates the blueprint's explicit file-list requirement.

**Files:** `src/Geef.Atelier.Application/Auth/ApplicationAuthExtensions.cs`, missing `src/Geef.Atelier.Application/OAuth/ApplicationOAuthExtensions.cs`

---

**10. `IOAuthRepository` (single aggregated repository abstraction) replaced with five separate repository interfaces placed in `Core/Persistence/OAuth/`**

Blueprint §2 table / Section 5:
> `src/Geef.Atelier.Application/OAuth/IOAuthRepository.cs` — single persistence abstraction in the Application layer.  
> `Infrastructure`: `OAuthRepository.cs` — single class implementing `IOAuthRepository`.

Implementation uses five separate interfaces (`IOAuthClientRepository`, `IOAuthAccessTokenRepository`, `IOAuthRefreshTokenRepository`, `IOAuthAuthorizationCodeRepository`, `IOAuthAuditLogRepository`) placed in `Core/Persistence/OAuth/` (not `Application/OAuth/`). The blueprint's intent is to keep the persistence abstraction boundary inside the Application layer, not Core. Placing these interfaces in Core means Core now has knowledge of persistence operations — a violation of the blueprint's constraint that Core has "zero dependencies on EF/HTTP/crypto."

While moving them to Core (rather than Application) does not strictly add a binary dependency on EF Core (they remain plain interfaces), it contradicts the blueprint's explicit file placement and architectural intent.

**Files:** `src/Geef.Atelier.Core/Persistence/OAuth/` (entire folder), missing `src/Geef.Atelier.Application/OAuth/IOAuthRepository.cs`

---

**11. `OAuthOptions` property names differ from blueprint**

Blueprint (§3):
```csharp
public int AccessTokenLifetimeSeconds { get; set; } = 3600;
public int RefreshTokenLifetimeDays   { get; set; } = 30;
public int AuthCodeLifetimeSeconds    { get; set; } = 600;
```

Implementation:
```csharp
public int AuthorizationCodeTtlMinutes { get; set; } = 10;
public int AccessTokenTtlHours         { get; set; } = 1;
public int RefreshTokenTtlDays         { get; set; } = 30;   // ← this one matches
public int CleanupIntervalMinutes      { get; set; } = 60;   // ← not in blueprint
public string RegistrationToken        { get; set; } = "";   // ← not in blueprint
```

The binding configuration section key `"OAuth"` is correct, but any `appsettings.json` or environment variable overrides targeting the blueprint's property names will silently be ignored. The extra `RegistrationToken` property belongs to the Application layer (it is not a pure config option for the Core layer), and `CleanupIntervalMinutes` is infrastructure-operational config that the blueprint does not include in `OAuthOptions`.

**File:** `src/Geef.Atelier.Core/Configuration/OAuthOptions.cs`

---

**12. Env-var name for OAuth issuer is `OAUTH_ISSUER` instead of `ATELIER_OAUTH_ISSUER`**

Blueprint (§3):
```csharp
var issuer = Environment.GetEnvironmentVariable("ATELIER_OAUTH_ISSUER");
```

Implementation uses `OAUTH_ISSUER`. The `ATELIER_` prefix is the established convention for this project (matching `ATELIER_MCP_TOKEN`, `ATELIER_USER`, etc.). An operator following the blueprint or existing docs will set the wrong env var.

**File:** `src/Geef.Atelier.Application/Auth/ApplicationAuthExtensions.cs` (line 48)

---

**13. DB schema deviations from blueprint: table/column naming, missing UUID PK on OAuthClients, FK constraints present where blueprint says none**

Blueprint §4 schema:

| Blueprint | Implementation |
|---|---|
| Table `OAuthAuthCodes` | Table `OAuthAuthorizationCodes` |
| Table `OAuthAuditEvents` | Table `OAuthAuditLog` |
| `OAuthClients.Id` (UUID PK) + `ClientId` (text, unique index) | `ClientId` is the PK directly (no separate `Id` UUID) |
| `OAuthAuthCodes.Subject` (text) | `OAuthAuthorizationCodes.UserId` (text) |
| `OAuthAccessTokens.Subject` (text) | `OAuthAccessTokens.UserId` (text) |
| `OAuthRefreshTokens.Subject` (text) | `OAuthRefreshTokens.UserId` (text) |
| `OAuthAuditEvents.Subject` (text) | `OAuthAuditLog.UserId` (text) |
| `OAuthRefreshTokens.FamilyId` (uuid, NOT NULL) | Field absent |
| `OAuthRefreshTokens.ReplacedByTokenHash` (text, NULL) | Field absent |
| No FK constraints by design (blueprint note: "FK cascades would be noisy") | FK `REFERENCES "OAuthClients"("ClientId") ON DELETE CASCADE` on all child tables |
| Partial indexes for fast live-token validation (`WHERE RevokedAt IS NULL AND ExpiresAt > now()`) | No partial indexes |

The `Subject` vs `UserId` naming is pervasive — it affects domain records, repositories, service, endpoints, and the migration, creating a systemic inconsistency with the blueprint's data flow (step 6 specifies `Subject` in `TokenValidationOutcome`).

**Files:** `src/Geef.Atelier.Infrastructure/Persistence/Migrations/20260519120000_Step19McpOAuth.cs`, all five `Core/Domain/OAuth/*.cs` records

---

**14. `BearerTokenHandler` uses `"token_kind"` claim name instead of `ClaimTypes.Role`**

Blueprint (§3, BearerTokenHandler):
```csharp
new(ClaimTypes.Role, outcome.Kind),
```

Implementation (line 34):
```csharp
new("token_kind", outcome.Kind),
```

The `ClaimTypes.Role` claim is used by ASP.NET Core's `[Authorize(Roles=…)]` and `RequireRole()` policy builders. Using a custom `"token_kind"` claim means role-based authorization on the `Kind` value (e.g., distinguishing static-bearer vs. oauth-bearer for future access control) cannot use standard ASP.NET Core authorization primitives.

**File:** `src/Geef.Atelier.Web/Auth/BearerTokenHandler.cs` (line 34)

---

**15. `ConnectedClients.razor` is not in `Account/` subfolder**

Blueprint §1 component map and §5 file list:
> `src/Geef.Atelier.Web/Components/Pages/Account/ConnectedClients.razor`

Implementation places it at `src/Geef.Atelier.Web/Components/Pages/ConnectedClients.razor` (missing `Account/` nesting). This also affects the implied route (`/account/connected-clients` is correct in the `@page` directive, so routing still works, but the file is not where blueprint dictates).

**File:** `src/Geef.Atelier.Web/Components/Pages/ConnectedClients.razor`

---

**16. `ConnectedClients.razor` calls `RevokeAllUserTokensAsync` for per-client revocation (revokes all clients, not just the selected one)**

Blueprint §1 data flow (step 8) and the revocation UI intent:
> `POST /oauth/revoke token=… → OAuthService.RevokeTokenAsync (hash, mark RevokedAt)`

The UI's "Widerrufen" (revoke single client) button calls `RevokeAllUserTokensAsync(_userId)` — which revokes every token for the user across all connected clients. This is the same as "Alle widerrufen". A per-client revoke should call `RevokeTokenAsync` for the specific client's tokens.

**File:** `src/Geef.Atelier.Web/Components/Pages/ConnectedClients.razor` (lines 130-133)

---

### MINOR

**17. `WellKnownEndpoints` omits `revocation_endpoint` from RFC 8414 metadata**

Blueprint appendix `OAuthMetadataDto`:
```json
"revocation_endpoint": "https://geef.stefan-bechtel.de/oauth/revoke"
```

The implementation's `/.well-known/oauth-authorization-server` response does not include `revocation_endpoint`. Clients that discover capabilities from the metadata document will not know where to revoke tokens.

**File:** `src/Geef.Atelier.Web/Endpoints/WellKnownEndpoints.cs`

---

**18. `OAuthCrypto` missing required `FixedTimeEquals(string a, string b)` method**

Blueprint (§3, `OAuthCrypto`):
```csharp
// Fixed-time string comparison (UTF-8 bytes)
internal static bool FixedTimeEquals(string a, string b)
```

Implementation does not expose this method. `CryptographicOperations.FixedTimeEquals` is used inline inside `VerifyPkceS256` only. Exposing it as a named helper allows callers (including endpoint code that currently uses `!=` for the registration token) to use the correct comparison uniformly.

**File:** `src/Geef.Atelier.Application/OAuth/OAuthCrypto.cs`

---

**19. `OAuthServiceRequests.cs` renamed to `OAuthServiceRecords.cs`**

Blueprint (§1 component map, §5 file list):
> `OAuthServiceRequests.cs` — request/result records

Implementation uses `OAuthServiceRecords.cs`. This is a minor naming deviation; functional impact is zero.

**File:** `src/Geef.Atelier.Application/OAuth/OAuthServiceRecords.cs`

---

**20. `/oauth/register` and `/oauth/token` and `/oauth/revoke` endpoints missing `DisableAntiforgery()`**

Blueprint (§3, endpoint signatures):
```csharp
app.MapPost("/oauth/register", …).AllowAnonymous().DisableAntiforgery();
app.MapPost("/oauth/token",    …).AllowAnonymous().DisableAntiforgery();
app.MapPost("/oauth/revoke",   …).AllowAnonymous().DisableAntiforgery();
```

Implementation only has `AllowAnonymous()` on all three; `DisableAntiforgery()` is present only on `POST /oauth/authorize`. In a Blazor Server app with antiforgery middleware enabled globally, machine-to-machine endpoints that do not send the antiforgery token may receive 400 responses under certain middleware configurations.

**File:** `src/Geef.Atelier.Web/Endpoints/OAuthEndpoints.cs`

---

## Summary

| Severity | Count |
|---|---|
| CRITICAL | 6 |
| MAJOR | 10 |
| MINOR | 4 |
| **Total** | **20** |

The implementation is architecturally sound in its layer separation (Core has no EF deps, Application has no EF deps, Infrastructure correctly wraps DbContext), the `[DbContext]`/`[Migration]` attributes on the migration are present and correct, `OAuthAuthorize.razor` has `@layout Layout.EmptyLayout` and `@attribute [Authorize]`, `CompositeTokenValidator` correctly tries static first, and `OAuthCrypto` uses `RandomNumberGenerator.GetBytes(32)` for generation and `CryptographicOperations.FixedTimeEquals` for PKCE. However, the IOAuthService interface shape, FamilyId-based refresh-reuse detection, `Subject`/`UserId` field naming, `ClaimTypes.Role` vs custom claim, loopback `localhost` expansion, and the registration-token timing-safe comparison are all deviations that must be resolved before production use.
