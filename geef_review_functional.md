# Functional QA Review: MCP OAuth 2.1 Authorization Server

**Reviewer:** Claude Sonnet 4.6 (strict QA mode)
**Date:** 2026-05-16
**Scope:** OAuth 2.1 Authorization Server implementation for Geef.Atelier

---

## Summary

29 OAuth-related tests pass (0 failures). Core cryptography, PKCE, single-use codes, refresh rotation, reuse detection, composite validator, and WWW-Authenticate header all work correctly. Four findings are reported below.

---

## Findings

### 1. MAJOR — Open Redirect on POST /oauth/authorize (deny and error paths)

**File:** `src/Geef.Atelier.Web/Endpoints/OAuthEndpoints.cs`, lines 160–164 and 185–186

The POST handler for `/oauth/authorize` reads `redirect_uri` directly from the form body without re-validating it against the client's registered redirect URIs. Both the deny branch and the server_error catch redirect to this unvalidated URI:

```csharp
// deny path — redirectUri is taken from form, not validated:
var denyUrl = $"{redirectUri}?error=access_denied";
return Results.Redirect(denyUrl);

// error path:
return Results.Redirect($"{redirectUri}?error=server_error");
```

The approve path also calls `CreateAuthorizationCodeAsync` with the unvalidated `redirect_uri`. The GET path (Blazor page) correctly calls `ValidateAuthorizationRequestAsync`, but the POST path does not re-validate. This means a forged form POST (see Finding 2 for CSRF context) could redirect the authenticated admin's browser to an attacker-controlled URL.

**Fix:** Call `ValidateAuthorizationRequestAsync` (or an equivalent redirect-URI-only check) at the top of the POST handler before any redirect is issued. Refuse to redirect at all if validation fails.

---

### 2. MAJOR — CSRF on POST /oauth/authorize (antiforgery disabled)

**File:** `src/Geef.Atelier.Web/Endpoints/OAuthEndpoints.cs`, line 192

The consent endpoint has `.DisableAntiforgery()`. This means any website the authenticated admin visits can submit a cross-origin form POST to `/oauth/authorize` and trigger a redirect to an attacker-controlled `redirect_uri` (exploiting Finding 1). Combined with Finding 1 this forms a complete open-redirect CSRF chain: admin is tricked into loading an attacker page → silent cross-origin form POST → admin's browser is redirected to attacker's site carrying `error=access_denied`.

**Context:** The Blazor page `OAuthAuthorize.razor` renders a plain HTML `<form method="post">` (not a Blazor interactive form), so it does not automatically receive a Blazor antiforgery token. Disabling antiforgery was probably done to make the static form work, but the correct fix is to add an explicit antiforgery token to the razor form and re-enable validation on the endpoint.

**Fix:** Either (a) re-enable antiforgery and add `AntiforgeryToken` component/hidden field to `OAuthAuthorize.razor`, or (b) if antiforgery cannot be used (e.g., because the form must be submitted by non-Blazor clients), validate `redirect_uri` strictly before any redirect so that CSRF cannot result in an open redirect.

---

### 3. MINOR — RevokeTokenAsync (by token string) revokes ALL refresh tokens for user, not client-scoped

**File:** `src/Geef.Atelier.Application/OAuth/OAuthService.cs`, line 188

When `RevokeTokenAsync` is given a refresh token string, it calls `refreshTokenRepo.RevokeByUserIdAsync(refreshToken.UserId, ct)` which revokes every refresh token for that user across all clients. This differs from the access-token branch which calls `RevokeByClientIdAndUserIdAsync` (client-scoped). A revocation request for one client's refresh token silently revokes all other clients' refresh tokens for the same user.

RFC 7009 does not mandate client-scoped revocation, but the asymmetric behavior (access tokens are client-scoped, refresh tokens are not) is surprising and undocumented. In a single-user deployment this has no practical impact (all tokens belong to one user), but it is worth noting.

**Fix (optional):** Add `RevokeByClientIdAndUserIdAsync` to `IOAuthRefreshTokenRepository` and use it in the refresh-token branch of `RevokeTokenAsync`, or document that refresh token revocation is intentionally user-wide.

---

### 4. MINOR — OAuthCleanupBackgroundService does not clean up revoked tokens or audit log

**File:** `src/Geef.Atelier.Web/Services/OAuthCleanupBackgroundService.cs`

`DeleteExpiredAsync` on all three repos uses a 1-day-past-expiry cutoff (`ExpiresAt < UtcNow.AddDays(-1)`) on `ExpiresAt` only. Two gaps:

a. **Revoked-but-not-expired tokens** accumulate indefinitely in the database. For example, an access token revoked after 5 minutes but with a 1-hour TTL will not be deleted by cleanup for another 55 minutes + 24 hours. In a long-running system with many revocations this creates unnecessary table bloat.

b. **OAuthAuditLog** is never cleaned up by the background service. With default settings (access token refresh every hour, 30-day refresh TTL) the audit log grows without bound.

**Fix:** Consider adding `WHERE RevokedAt IS NOT NULL AND RevokedAt < now() - interval '1 day'` to the cleanup queries, and add a retention-based cleanup for audit log entries (e.g., delete entries older than 90 days).

---

## Verification of Correct Behaviours (no findings)

1. **End-to-end flow:** Register → GET /oauth/authorize (Blazor validates, shows consent) → POST approve → redirect with code → POST /oauth/token with code + PKCE verifier → access + refresh tokens → MCP Bearer call → CompositeTokenValidator → access granted. Correct.

2. **PKCE-S256:** `OAuthCrypto.VerifyPkceS256` computes `BASE64URL(SHA256(ASCII(verifier)))` and uses `CryptographicOperations.FixedTimeEquals` for constant-time comparison. Matches RFC 7636 §4.6. Correct.

3. **Single-use auth code (atomic):** `OAuthAuthorizationCodeRepository.ConsumeAsync` issues a single `UPDATE ... WHERE UsedAt IS NULL` and returns null if 0 rows affected. TOCTOU-safe. Correct.

4. **Refresh token rotation with reuse detection:** `ConsumeAsync` marks the token used atomically; if consume returns null, `FindByHashAsync` distinguishes "token existed but was already used" (→ `RevokeAllUserTokensAsync`) from "token never existed" (→ just throw). Correct.

5. **CompositeTokenValidator order:** Tries `StaticTokenValidator` first; if valid, returns immediately without touching the DB. Falls through to `OAuthAccessTokenValidator` only on failure. Backwards-compatible with static `ATELIER_MCP_TOKEN`. Correct.

6. **WWW-Authenticate header:** `OnStarting` callback in `Program.cs` fires before auth short-circuits; adds `Bearer resource_metadata=.../.well-known/oauth-protected-resource` on all 401 responses from `/mcp`. Test `McpWwwAuthenticateHeaderTests` verifies this with a `TestServer`. Correct.

7. **Well-known endpoints:** `.well-known/oauth-authorization-server` returns RFC 8414 fields including `code_challenge_methods_supported: ["S256"]`. `.well-known/oauth-protected-resource` returns `resource` and `authorization_servers`. Correct.

8. **Test coverage:** 29 unit/integration tests covering PKCE correct/wrong verifier, single-use code, expired code, wrong client/redirect_uri, refresh rotation, reuse detection + revoke-all, expired/revoked access tokens, backwards-compat composite validator, well-known endpoints, registration happy/error paths. Coverage is comprehensive for the critical paths.

9. **Loopback redirect URI (RFC 8252):** `IsLoopbackUri` accepts `127.0.0.1` and `localhost`. `IsLoopbackMatch` compares scheme, host, and path while ignoring port. Tests verify port-insensitive matching for both `127.0.0.1` and `localhost`. Non-loopback URIs require exact port match. Correct.

10. **OAuthCleanupBackgroundService:** Correctly creates a DI scope per tick, resolves the three repos, and calls `DeleteExpiredAsync` on each. Uses `PeriodicTimer.WaitForNextTickAsync` which waits one full interval before the first tick (so no cleanup on startup, which is acceptable). Correct within the noted limitation (Finding 4).

---

## Total Findings: 4

| # | Severity | Title |
|---|----------|-------|
| 1 | MAJOR    | Open redirect on POST /oauth/authorize (deny and error paths use unvalidated redirect_uri) |
| 2 | MAJOR    | CSRF on POST /oauth/authorize (.DisableAntiforgery() without redirect_uri guard) |
| 3 | MINOR    | RevokeTokenAsync revokes all refresh tokens user-wide (asymmetric vs access token behavior) |
| 4 | MINOR    | Cleanup does not remove revoked-but-not-expired tokens or audit log entries |
