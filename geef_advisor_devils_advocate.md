# Devil's Advocate Report — MCP OAuth 2.1 Authorization Server (Step 19)

**Role:** Adversarial reviewer. Arguing *against* the plan as written.
**Documents reviewed:** `geef_plan.md`, `geef_architecture.md`
**Date:** 2026-05-15

---

## 1. Architectural Concerns

### 1.1 The Partial Index with `now()` Is Broken (BLOCKING)

The schema defines two partial indexes intended to accelerate the hottest query paths:

```sql
CREATE INDEX "IX_OAuthAuthCodes_Active"
    ON "OAuthAuthCodes" ("CodeHash")
    WHERE "UsedAt" IS NULL AND "ExpiresAt" > now();

CREATE INDEX "IX_OAuthAccessTokens_Live"
    ON "OAuthAccessTokens" ("TokenHash")
    WHERE "RevokedAt" IS NULL AND "ExpiresAt" > now();
```

**`now()` in a PostgreSQL partial-index `WHERE` clause is evaluated once at index-creation time**, not at query time. After migration runs, the predicate becomes a frozen timestamp (e.g., `WHERE "ExpiresAt" > '2026-05-19 12:00:00+00'`). All tokens issued after that moment enter the index regardless of their expiry, defeating the "live tokens only" intent. As the server ages, the index degrades to near-equivalence with a plain index on `TokenHash`. The architecture explicitly calls this the "hottest query" and "fast live-token validation path" — both claims are false as written.

**Fix:** Remove `ExpiresAt > now()` from the partial index predicate. Keep only `WHERE "RevokedAt" IS NULL`. Expiry check stays in application code (it already must, since `OAuthService` checks `ExpiresAt`). Alternatively, use a standard full index and rely on the application-layer check.

---

### 1.2 Schema-DTO Mismatch: `ConnectedClientDto.LastUsed` Has No Column (BLOCKING)

`ConnectedClientDto` declares:

```csharp
public sealed record ConnectedClientDto(
    string ClientId,
    string ClientName,
    string Scope,
    DateTimeOffset LastUsed,      // ← WHERE DOES THIS COME FROM?
    DateTimeOffset ExpiresAt);
```

The `OAuthAccessTokens` schema has `CreatedAt`, `ExpiresAt`, and `RevokedAt` — no `LastUsedAt`. There is no audit-event query specified for this purpose, and the repository interface is not defined in the architecture. `GetConnectedClientsAsync` will either silently substitute `CreatedAt` for `LastUsed` (wrong semantic) or fail to compile. This is a specification hole that will surface immediately in integration tests or at runtime.

**Fix:** Either add a `LastUsedAt timestamptz NULL` column to `OAuthAccessTokens` (updated on each validation) or change the DTO to `IssuedAt` / `CreatedAt` and update the UI label accordingly.

---

### 1.3 Open Dynamic Client Registration Is a Security Vulnerability (BLOCKING)

`POST /oauth/register` is `AllowAnonymous()` with no rate limiting, no registration token, and no admin approval mentioned anywhere. This is the internet-facing URL `https://geef.stefan-bechtel.de/oauth/register`. Any actor who discovers or guesses it can:

- Register unlimited malicious clients (denial of storage)
- Pre-register clients with lookalike `ClientName` values to confuse the consent UI
- Generate valid `client_id`/`client_secret` pairs for subsequent phishing attempts against the authorization endpoint

RFC 7591 Section 7 explicitly states that open registration SHOULD be protected. For a single-tenant personal MCP server, there is no legitimate use case for anonymous strangers registering clients. The plan is silent on this entire attack surface.

**Fix:** Require an `ATELIER_REGISTRATION_TOKEN` Bearer header for `POST /oauth/register` (one extra env var, one line of middleware), or restrict registration to the `[Authorize]` cookie scheme so only the logged-in admin can register clients.

---

### 1.4 `RevokeAll` Logic Is Self-Contradictory

Security Invariant #5 says:

> set `RevokedAt = now()` on ALL `OAuthAccessTokens` and `OAuthRefreshTokens` where **`ClientId = clientId AND Subject = subject AND RevokedAt IS NULL`**

The Schema Notes then say:

> A reused-revoked token triggers `RevokeAll WHERE **FamilyId = @familyId`** (tighter than client+subject)

These are different operations producing different results. `FamilyId` scoping is strictly tighter (only revokes the rotation chain that was compromised), while `ClientId+Subject` scoping revokes all sessions for that user+client pair, including unrelated concurrent sessions. The implementation will pick one — which one is correct? The architecture gives no resolution. If `FamilyId` is the intent (the tighter, more correct RFC 6819 interpretation), then the security invariant is wrong; if `ClientId+Subject` is the intent, the schema note is wrong. A developer implementing this will guess.

---

### 1.5 No FK Constraints → Orphaned Tokens Are Permanently Valid

The schema explicitly avoids FK constraints between `OAuthClients` and the token tables, relying on "application-layer cleanup." The `OAuthAuditEvent` justification is moot — audit events don't need FKs. The actual risk: if `OAuthService.DeleteClientAsync` (not present in `IOAuthService` — another gap) fails mid-execution, tokens referencing the now-deleted client remain in the DB with no `ClientId` FK to enforce their invalidity. The `OAuthAccessTokenValidator` does a `TokenHash` lookup; it will find and validate these orphaned tokens indefinitely. A background crash during client deletion produces permanently live tokens for a non-existent client.

---

### 1.6 Auth Code Single-Use Is Not Concurrency-Safe

The plan specifies "Marks code as used (single-use)" and the test "Code-Reuse-Reject (zweiter Exchange → 400)" — but this test is sequential. The real attack is two parallel requests racing to exchange the same code (attacker intercepts and replays simultaneously with the legitimate client). A naïve read-then-write sequence:

```
T1: SELECT CodeHash WHERE CodeHash=hash AND UsedAt IS NULL → found
T2: SELECT CodeHash WHERE CodeHash=hash AND UsedAt IS NULL → found
T1: UPDATE SET UsedAt = now()
T2: UPDATE SET UsedAt = now()  -- too late, T1 already issued tokens
```

Both succeed. The architecture says nothing about `SELECT FOR UPDATE`, optimistic concurrency with a row version, or a conditional update (`UPDATE ... WHERE UsedAt IS NULL RETURNING *`). This is a real auth code injection vector.

---

### 1.7 `IOAuthRepository` Return Types Are Undefined

The architecture defines `IOAuthRepository` in the Application layer to keep Application EF-free. But the interface body and its return types are never specified. The repository must return *something* — if it returns EF entities (e.g., `OAuthAccessToken`), that leaks Infrastructure types into Application, violating the layer contract. If it returns application-layer records, those records are not defined anywhere in the architecture. The implementer will invent them ad hoc, producing inconsistency.

---

### 1.8 No Token Cleanup → Unbounded DB Growth

`OAuthAuthCodes` with 10-minute TTL, `OAuthAccessTokens` with 1-hour TTL, and `OAuthAuditEvents` all accumulate indefinitely. With daily Claude Desktop use over a year, `OAuthAuditEvents` alone grows by hundreds of rows per session. No background cleanup job, no `pg_cron` schedule, no `IHostedService` sweeper is mentioned. The architecture documents the DB as running on "the existing server Postgres instance" (shared resource) — unbounded growth affects all other sites.

---

## 2. Test Gaps

### 2.1 No Concurrent Code Exchange Test

"Code-Reuse-Reject" is listed as a sequential test. There is no concurrent scenario (two parallel `HttpClient` requests racing on the same auth code within the same millisecond). This gap means the race condition in §1.6 will not be caught by the test suite and will reach production.

### 2.2 No Test for the Partial Index Bug

The "fast live-token validation path" optimization is untested. No test verifies that token validation remains correct 2 hours after issuance (when the token is valid but `ExpiresAt > <migration-timestamp>` is false). The bug manifests silently — validation still works via the full-table fallback, so tests pass, but performance degrades.

### 2.3 No Test for `GetConnectedClientsAsync` / `ConnectedClientDto.LastUsed`

The schema mismatch (§1.2) will not be caught by unit tests against mocked repositories. Only an integration test against a real Testcontainer will fail. The plan's integration tests focus on OAuth endpoints, not the `ConnectedClients.razor` data path. This is a feature that will be visually broken on first UI load.

### 2.4 No Test for Malformed PKCE Inputs

- `code_challenge` containing non-Base64Url characters
- `code_challenge` of wrong length (e.g., 0 bytes or 1000 bytes)
- `code_verifier` shorter than 43 characters (RFC 7636 minimum)
- `code_challenge_method` set to `"plain"` (must be rejected; `"none"` must be rejected)

`OAuthCryptoTests.cs` is listed but these adversarial inputs are not enumerated.

### 2.5 No Test for Open Registration Abuse Rate

Because there is no rate limiting (§1.3), there is no test verifying that 1000 sequential registrations are rejected or throttled. The absence of the control means the absence of a test for it — the gap in the test plan mirrors the gap in the architecture.

### 2.6 DI Registration Order Is Untested

The plan explicitly notes the dependency order: `AddAtelierMcpAuth` must precede `AddAtelierOAuth`. No test verifies that calling them in the wrong order produces a startup exception rather than a silent misconfiguration (e.g., `CompositeTokenValidator` with a null `StaticTokenValidator`).

### 2.7 No Test for Cookie-Expiry Mid-OAuth-Flow

The scenario: user begins the authorize flow → consent page loads → cookie expires before form is submitted → POST returns 302 to `/login` → OAuth client (Claude Desktop) never receives its redirect with the auth code → flow hangs. No test covers this state.

---

## 3. Edge Cases Not Addressed

### 3.1 Redirect URI Normalization Inconsistency

The architecture specifies "byte-exact equality" for non-loopback redirect URIs. Client libraries (including Claude Desktop's underlying HTTP stack) may normalize URIs in ways that differ by trailing slash, case, or percent-encoding. `https://example.com/callback` vs `https://example.com/callback/` would fail byte-exact matching. No normalization step is defined.

### 3.2 Cookie Expiry During Consent

Covered in test gaps (§2.7) — but the *architecture* also provides no guidance on the error handling path. Should `OAuthAuthorize.razor` render an error page? Should it redirect back to the OAuth client with `error=access_denied`? Currently undefined.

### 3.3 Scope Downgrade in Refresh Is Unspecified

`RefreshTokenRequest` includes `Scope` (for downscoping). The architecture does not specify:
- What happens if the requested scope exceeds the original grant
- Whether an empty scope defaults to the original grant
- Whether scope downgrade creates a new audit event

The implementer will invent behavior for all three cases.

### 3.4 `text[]` Column Needs EF Core Configuration

`OAuthClients.RedirectUris` is a PostgreSQL `text[]` array. The EF Core entity needs either `HasColumnType("text[]")` or the Npgsql array mapping. This configuration is not mentioned in the architecture. Without it, EF will attempt to map `IReadOnlyList<string>` incorrectly (as JSON or a separate table), or fail migration snapshot generation.

### 3.5 Loopback Port-Wildcard Applies Only to `http://` — What About Claude.ai Web?

The loopback rule (`http://127.0.0.1` with arbitrary port) is for Claude Desktop's local callback server. Claude.ai Web Custom Connectors use a fixed non-loopback redirect URI. The architecture handles this correctly by requiring byte-exact match for non-loopback URIs. However, the plan's success criterion mentions both "Claude Desktop Custom Connector" *and* "Claude.ai Web Custom Connectors" — but no test exists for the Claude.ai Web flow specifically, which has different redirect URI patterns.

### 3.6 No `client_id` Entropy Specification

The schema says `ClientId` is "e.g. `atelier_<uuid_prefix>`" but doesn't specify the generation algorithm or guaranteed uniqueness mechanism beyond the unique index. Collision probability for `uuid_prefix` truncations at high registration volume is unaddressed.

---

## 4. Better Alternatives Worth Considering

### 4.1 Use OpenIddict (Not Roll-It-Yourself)

OpenIddict for ASP.NET Core implements RFC 8414, RFC 7591, PKCE S256, refresh rotation, and revocation as a tested, audited library. The security invariants in §2 of the architecture are exactly what OpenIddict gets right by default. Rolling a custom OAuth server introduces implementation risk at every invariant — each one is a potential CVE. The plan gives no justification for the custom approach, and OpenIddict integrates with EF Core and Blazor with minimal ceremony.

Tradeoff: Learning curve and slightly less control over DB schema. The benefit is audited security correctness.

### 4.2 Self-Contained JWT Access Tokens Eliminate the DB Hot Path

The plan uses opaque tokens requiring a DB `SELECT` on every MCP call. Short-lived JWTs (15-minute lifetime) signed with a server key would allow stateless validation — verify signature, check `exp` claim, done. No DB round-trip on the hot path. Refresh tokens remain opaque and DB-backed for revocability. This eliminates §1.8's growth concern for access tokens and makes the system DB-failure-resilient for token validation.

Tradeoff: JWTs require key management and cannot be instantly revoked (must wait for expiry). For a 15-minute window, this is typically acceptable.

### 4.3 Registration Token for `POST /oauth/register`

Rather than open registration (§1.3), a single environment variable `ATELIER_REGISTRATION_TOKEN` that must be presented as `Authorization: Bearer <token>` on `POST /oauth/register` adds one check. This is four lines of code and eliminates the entire open-registration attack surface. This is not an "alternative" so much as a mandatory addition, but it's worth stating as a concrete alternative to the current "anonymous registration" stance.

---

## Severity Verdict

**SIGNIFICANT — with three BLOCKING items that must be resolved before implementation.**

The overall architecture is sound in intent and the layering decisions are defensible. However, the three BLOCKING items are not implementation details — they are specification errors that will produce either broken behavior (partial index, LastUsed mismatch) or a security vulnerability (open registration) in the deployed system.

---

## Top 3 Must-Fix Items Before Implementation

### 1. Fix the Partial Index Predicate (Schema Correctness)

Remove `AND "ExpiresAt" > now()` from both partial index definitions. Keep `WHERE "RevokedAt" IS NULL` only. Expiry is enforced in application code (already planned). Document this explicitly so future developers don't re-add the time-based predicate.

### 2. Resolve the `ConnectedClientDto.LastUsed` Gap (Schema + Interface)

Choose one: (a) add `LastUsedAt timestamptz NULL` to `OAuthAccessTokens` and update it on each successful validation, or (b) rename the DTO field to `IssuedAt` backed by `CreatedAt`. Either choice must be reflected in the migration SQL, the EF entity, the `IOAuthRepository` return type, and the `ConnectedClients.razor` UI label — all of which are currently absent from the architecture.

### 3. Restrict Client Registration + Add Concurrency Safety to Code Exchange (Security)

Two sub-items that together address the biggest security gaps:

- **Registration:** Add a registration bearer token requirement (`ATELIER_REGISTRATION_TOKEN` env var) to `POST /oauth/register`. One middleware check, one env var — completely closes the open-registration attack surface.
- **Code exchange race condition:** Use a conditional update (`UPDATE "OAuthAuthCodes" SET "UsedAt" = @now WHERE "CodeHash" = @hash AND "UsedAt" IS NULL`) and check the number of affected rows. If zero rows affected → 400 immediately (code already used). This makes single-use enforcement atomic without application-level locking.
