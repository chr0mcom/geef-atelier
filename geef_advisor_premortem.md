# Pre-Mortem: MCP OAuth 2.1 Authorization Server (Step 19)

**Advisor:** Strategic Pre-Mortem (Gary Klein Methode)  
**Datum:** 15. Mai 2026  
**Hypothese:** Es ist November 2026. Das Feature ist deployed — und gescheitert. Was ist passiert?

---

## 1. Top 5 Failure Modes (Probability × Impact, absteigend)

---

### F-1 — Migration bricht ab: `now()` in Partial-Index-Prädikaten

**Wahrscheinlichkeit:** Hoch (deterministisch, wenn kein Testlauf der Migration vor Commit)  
**Impact:** Hoch (komplette Deployment-Blockade)

Das Architecture-Dokument definiert zwei Partial Indexes:

```sql
CREATE INDEX "IX_OAuthAuthCodes_Active"
    ON "OAuthAuthCodes" ("CodeHash")
    WHERE "UsedAt" IS NULL AND "ExpiresAt" > now();

CREATE INDEX "IX_OAuthAccessTokens_Live"
    ON "OAuthAccessTokens" ("TokenHash")
    WHERE "RevokedAt" IS NULL AND "ExpiresAt" > now();
```

PostgreSQL verlangt, dass Indexprädikate ausschließlich `IMMUTABLE`-Funktionen verwenden. `now()` ist `STABLE`, nicht `IMMUTABLE`. Resultat:

```
ERROR: functions in index predicate must be marked IMMUTABLE
```

Die Migration schlägt fehl, rollt zurück, und das Feature deployed nicht. Da der Testcontainer-Setup in CI die Migration ausführt, bricht auch die CI. Weil dieser Fehler deterministisch und sofort auftritt, ist er der wahrscheinlichste Einzelpunkt des Scheiterns.

---

### F-2 — TOCTOU-Race-Condition beim Auth-Code-Exchange

**Wahrscheinlichkeit:** Mittel (unwahrscheinlich im Normalbetrieb, sehr wahrscheinlich unter Last oder bei doppeltem Klick)  
**Impact:** Hoch (Security-Invariante gebrochen: Doppel-Token-Ausgabe)

Die Plan-Anforderung lautet: Auth-Codes sind single-use, `UsedAt` wird "atomisch" gesetzt. EF Core's natürliches Pattern wäre:

```csharp
var code = await db.OAuthAuthCodes.FirstOrDefaultAsync(c => c.CodeHash == hash && c.UsedAt == null);
if (code == null) return Failure;
code.UsedAt = now;
await db.SaveChangesAsync();
```

Das ist kein atomischer Check. Zwei parallele POST-Requests an `/oauth/token` mit demselben Code (z.B. durch Claude Desktop-Retry-Logik, Netzwerk-Timeout oder doppelten Submit) lesen beide `UsedAt IS NULL`, beide bestehen den Check, und beide erhalten Access-Tokens. Die Single-Use-Garantie ist gebrochen — ein Token kann zweimal ausgetauscht werden, was in manchen Szenarien zu Token-Theft führt.

---

### F-3 — `@attribute [Authorize]` greift nicht auf Cookie-Scheme

**Wahrscheinlichkeit:** Hoch (ASP.NET Core Multi-Scheme-Trap ist gut dokumentiert)  
**Impact:** Hoch (gesamter Browser-OAuth-Flow bricht sofort)

Die `OAuthAuthorize.razor`-Komponente trägt `@attribute [Authorize]` ohne Schemaspezifikation. In einer App mit zwei Auth-Schemes (Cookie für UI, Bearer/McpPolicy für MCP) bestimmt `DefaultChallengeScheme` das Verhalten bei Unauthenticated-Request. Wenn `DefaultChallengeScheme` auf das Bearer-Schema zeigt (was wahrscheinlich ist, da MCP der primäre Treiber war), erhält der Browser bei GET `/oauth/authorize` eine `401`-Antwort mit `WWW-Authenticate: Bearer` — **keine Redirect zu `/login`**.

Gleiches Problem für den `POST /oauth/authorize` Minimal-API-Endpoint: `.RequireAuthorization()` ohne Schemabindung verwendet die Default-Policy. Die Architektur-Notiz sagt "default policy = Cookie scheme" — diese Annahme ist nicht durch Code belegt.

Resultat: Jeder Versuch, den OAuth-Flow im Browser durchzuführen, liefert ein 401. Claude Desktop zeigt dem Nutzer eine Fehlermeldung. Kein Consent-Screen ist jemals erreichbar.

---

### F-4 — Offene Dynamic Client Registration als DoS-Vektor

**Wahrscheinlichkeit:** Mittel (öffentlich erreichbarer Endpoint, kein Rate-Limiting)  
**Impact:** Mittel (DB-Wachstum, Audit-Trail-Spam, potenzieller Disk-Exhaustion)

`POST /oauth/register` ist `AllowAnonymous()` ohne Rate-Limiting, ohne IP-Throttling, ohne Maximum-Client-Count. Jeder Actor, der den Endpoint entdeckt (via /.well-known-Discovery öffentlich sichtbar), kann in Minuten Millionen von Clients registrieren:

- `OAuthClients`: wächst unbegrenzt
- `OAuthAuditEvents`: jede Registrierung erzeugt einen Event
- Keine Cleanup-Job für die fünf neuen Tabellen ist im Plan definiert

Auch ohne gezielten Angriff: Legitime Nutzung mit 30-tägigen Refresh-Tokens erzeugt über Monate Millionen von Token-Rows ohne Cleanup. Die Live-Token-Partial-Indexes (wenn sie nach F-1-Fix korrekt angelegt sind) verlangsamen sich mit wachsender Tabellengröße nicht, aber Audit-Events und revozierte Tokens akkumulieren ohne Bound.

---

### F-5 — Claude Desktop OAuth-Discovery- und Redirect-URI-Mismatch

**Wahrscheinlichkeit:** Hoch (undokumentiertes Claude Desktop-Verhalten, MCP-Spec in Bewegung)  
**Impact:** Hoch (OAuth-Flow startet gar nicht)

**Discovery:** Der Plan setzt voraus, dass Claude Desktop die Authorization-Server-Metadata via `GET /.well-known/oauth-authorization-server` entdeckt. Der aktuelle MCP-OAuth-Draft (2025) spezifiziert stattdessen, dass der Resource Server bei 401-Antworten einen `WWW-Authenticate`-Header mit `resource_metadata`-Parameter zurückgibt:

```
WWW-Authenticate: Bearer resource_metadata="https://geef.stefan-bechtel.de/.well-known/oauth-protected-resource"
```

Fehlt dieser Header auf `/mcp`, startet Claude Desktop den OAuth-Flow nie — weil es die Authorization-Server-URL nicht kennt. Der Plan enthält keine Modifikation des MCP-Endpoints für diesen Header.

**Redirect URI:** Der Plan nennt kein konkretes Redirect-URI-Format für Claude Desktop. Ob es `https://claude.ai/...`, `http://127.0.0.1/callback` oder ein Custom-Scheme ist, bestimmt die Registrierungsstrategie. Ohne genaue Kenntnis des Claude-Desktop-Verhaltens kann kein Test-Client korrekt vorregistriert werden, und Dynamic Client Registration durch den Claude-Desktop-Client (wenn dieser es nicht macht) würde von einem Menschen manuell gestartet werden müssen.

---

## 2. Hidden Assumptions

Diese Annahmen stecken implizit im Plan — keine wird explizit validiert:

**A-1: Claude Desktop folgt Standard-RFC-8414-Discovery.**  
Realität: Die MCP-Spec hat mehrfach gewechselt. `/.well-known/oauth-authorization-server` ist der IETF-Standard, aber Claude Desktop kann abweichend implementiert sein. Der `WWW-Authenticate`-Response-Header auf MCP-401-Antworten ist wahrscheinlich der tatsächliche Trigger.

**A-2: `code_challenge` überlebt den Login-Roundtrip URL-encoded korrekt.**  
`code_challenge` ist Base64Url und enthält `+`, `-`, `_`, `=`. `RedirectToLogin.razor` encoded `PathAndQuery` in den `ReturnUrl`-Parameter. Wenn diese Encoding-Chain nicht exakt symmetrisch ist (z.B. doppeltes Encoding durch QueryString-Builder), kommt ein korrumpierter `code_challenge` bei `OAuthAuthorize.razor` an — und PKCE-Verify schlägt für *jeden* OAuth-Versuch fehl.

**A-3: `Content-Type: application/x-www-form-urlencoded` erreicht Kestrel unverändert.**  
Traefik als Reverse Proxy könnte im Fehlerfall oder bei Konfigurationsänderung Anfragekörper transformieren oder Request-Logging mit Body-Capture aktiviert haben. `/oauth/token` liest `ctx.Request.Form` — bei leerem Form gibt es keine `client_secret`, keine `code`, und jede Token-Anfrage schlägt mit kryptischem Fehler fehl.

**A-4: `AddAtelierMcpAuth` läuft vor `AddAtelierOAuth` in `Program.cs`.**  
Die DI-Registrierungsreihenfolge ist eine explizite Anforderung im Architektur-Dokument. Wenn ein Implementierer die Zeilen vertauscht oder `AddAtelierOAuth` zuerst aufruft, ist `StaticTokenValidator` zum Zeitpunkt der `CompositeTokenValidator`-Registrierung noch nicht als Named-Service vorhanden. Das DI-System wirft entweder zur Laufzeit oder `CompositeTokenValidator` fällt auf `null` zurück — beides bricht den statischen Bearer-Pfad.

**A-5: Das System bleibt Single-User.**  
`Subject` in allen OAuth-Tokens ist fest der Env-Var-User. `GetConnectedClientsAsync(subject)` filtert nach Subject. Falls jemals ein zweiter Nutzer hinzukommt, sieht jeder Nutzer nur seine eigenen Clients — aber Client-Registrierungen sind global (kein Owner-Field auf `OAuthClients`). Die Revoke-UI zeigt nur eigene Tokens, kann aber nicht die des anderen Users revozen, auch wenn beide denselben Client nutzen.

**A-6: `state`-Parameter ist ausreichend als CSRF-Schutz für den Consent-POST.**  
`state` ist ein Client-Parameter, kein server-seitiges CSRF-Token. Der Plan disabled Antiforgery und begründet das mit "state + cookie-bound session". Aber `state` wird vom Authorization-Request-Initiator (Claude Desktop) gesetzt, nicht vom Server. Ein Angreifer, der einen Auth-Flow initiiert, kann einen eigenen `state` einschleusen. Der Cookie-Schutz verhindert CSRF für den POST selbst — aber der OAuth-spezifische "Authorization Code Injection"-Angriff (ein Angreifer bindet seinen Code an das Opfer-Konto) wird durch server-seitiges State-Binding verhindert, nicht durch Cookie-Auth allein.

---

## 3. Missing Context

Was im Plan fehlt oder übersprungen wurde:

**M-1: MCP-OAuth-Spezifikationsversion.**  
Der Plan referenziert RFC 8414, RFC 7591, RFC 7009, RFC 8252 — alles IETF-Standards. Aber Claude Desktop implementiert MCP-spezifische OAuth-Erweiterungen. Die MCP-Auth-Spec (modelcontextprotocol.io, 2025) definiert zusätzliche Anforderungen wie den `resource_metadata`-Discovery-Header und spezifische Fehlerformat-Anforderungen. Diese werden im Plan nicht adressiert.

**M-2: Redirect-URI-Format von Claude Desktop.**  
Ohne diese Information kann kein E2E-Test (Szenario B im Plan) korrekt laufen. Das Dokument nennt nur das Prinzip (loopback port-wildcard), nicht die tatsächliche URI, die Claude Desktop sendet.

**M-3: Token-Cleanup-Strategie.**  
Fünf neue Tabellen, keine Cleanup-Spezifikation. Expired Auth-Codes (10-min TTL), revozierte Access-Tokens (1h) und Refresh-Tokens (30 Tage) wachsen ohne Bound. Für eine Produktions-Applikation fehlt ein `IHostedService` für nächtliche Bereinigung oder eine PostgreSQL-Partition-Strategie.

**M-4: `ITokenValidator`-Callers im bestehenden Code.**  
Das Interface wechselt von `Task<bool>` zu `Task<TokenValidationOutcome>`. Es ist nicht dokumentiert, wie viele Stellen im existierenden Code (Tests, Mocks, direkte Aufrufe) von der alten Signatur abhängen. Ohne eine vollständige Auswirkungsanalyse können compilerbrechende Änderungen unentdeckt bleiben.

**M-5: Routing-Konflikt Blazor-GET vs. Minimal-API-POST auf `/oauth/authorize`.**  
In Blazor Server mit Minimal-API: `OAuthAuthorize.razor` handles `GET /oauth/authorize`, `OAuthEndpoints.cs` handles `POST /oauth/authorize`. In ASP.NET Core ist das prinzipiell möglich, aber die Reihenfolge der Middleware-Konfiguration (Blazor's `MapBlazorHub` + `MapRazorComponents` vs. `MapOAuthEndpoints`) bestimmt, welcher Handler gewinnt. Dieses Routing-Setup ist im Plan nicht explizit getestet.

**M-6: `client_secret` in Traefik-Access-Logs.**  
`POST /oauth/token` liest `client_secret` aus dem Form-Body. Wenn Traefik Access-Logging aktiviert ist (oder wird), erscheinen Client-Secrets in den Logs. Das widerspricht Security-Invariante 8 ("No secret in logs") — aber Traefik ist außerhalb des `BearerTokenHandler`-Scopes und liegt damit im blinden Fleck des Plans.

---

## 4. Mitigations

**M für F-1 — Partial-Index `now()` entfernen:**  
Ersetze die Prädikate durch immutable-kompatible Varianten:
```sql
-- Auth Codes: nur UsedAt IS NULL — Expiry-Check im Application-Code
CREATE INDEX "IX_OAuthAuthCodes_Active"
    ON "OAuthAuthCodes" ("CodeHash")
    WHERE "UsedAt" IS NULL;

-- Access Tokens: nur RevokedAt IS NULL — Expiry-Check im Application-Code  
CREATE INDEX "IX_OAuthAccessTokens_Live"
    ON "OAuthAccessTokens" ("TokenHash")
    WHERE "RevokedAt" IS NULL;
```
Expiry-Validation bleibt in `OAuthService`/`OAuthRepository` (ohnehin notwendig für korrekte Fehlermeldungen). **Muss gemacht werden, bevor die Migration in einem Testcontainer ausgeführt wird.**

**M für F-2 — Atomischer Auth-Code-Exchange:**  
In `OAuthRepository` kein SELECT-then-UPDATE. Stattdessen raw SQL:
```sql
UPDATE "OAuthAuthCodes"
SET "UsedAt" = now()
WHERE "CodeHash" = @hash AND "UsedAt" IS NULL
RETURNING *;
```
Prüfe `affectedRows == 1`. Bei 0: Code war bereits benutzt oder existiert nicht → `invalid_grant`. Dieses Muster ist in der Implementierung als Repository-Kontrakt vorzuschreiben, nicht der `OAuthService` überlassen.

**M für F-3 — Auth-Scheme explizit binden:**  
```csharp
// OAuthAuthorize.razor
@attribute [Authorize(AuthenticationSchemes = "Cookies")]

// POST /oauth/authorize in OAuthEndpoints.cs
app.MapPost("/oauth/authorize", ...)
    .RequireAuthorization(policy =>
        policy.AddAuthenticationSchemes("Cookies")
              .RequireAuthenticatedUser())
    .DisableAntiforgery();
```
Vor Implementierung: In der bestehenden `Program.cs` nachsehen, was `DefaultAuthenticateScheme` und `DefaultChallengeScheme` gesetzt sind. Ein Integrationstest `GET /oauth/authorize` ohne Cookie → muss auf `/login?ReturnUrl=...` redirecten (nicht 401).

**M für F-4 — Rate-Limiting und Cleanup:**  
```csharp
// Program.cs — vor MapOAuthEndpoints
app.UseRateLimiter();

// Konfiguration:
builder.Services.AddRateLimiter(opts =>
    opts.AddFixedWindowLimiter("oauth-register",
        o => { o.PermitLimit = 10; o.Window = TimeSpan.FromMinutes(1); }));

// OAuthEndpoints.cs:
app.MapPost("/oauth/register", ...).RequireRateLimiting("oauth-register");
```
Zusätzlich: `OAuthCleanupService : BackgroundService` implementieren, das täglich Auth-Codes `WHERE ExpiresAt < now() - 1 day`, Access-Tokens `WHERE ExpiresAt < now() - 7 days AND RevokedAt IS NOT NULL`, und Audit-Events `WHERE OccurredAt < now() - 90 days` bereinigt.

**M für F-5 — Discovery-Flow vor Implementierung testen:**  
Vor dem ersten Code-Commit: Eine nackte MCP-Endpoint-Response bei fehlendem Token in Claude Desktop beobachten (Browser DevTools oder Proxy). Wenn Claude Desktop einen `resource_metadata`-Parameter im `WWW-Authenticate`-Header erwartet, muss `BearerTokenHandler.AuthenticateAsync` bei `AuthenticateResult.NoResult()` oder `Fail()` diesen Header setzen:
```csharp
Response.Headers.WWWAuthenticate =
    $"Bearer resource_metadata=\"{options.Issuer}/.well-known/oauth-protected-resource\"";
```
Außerdem: Das tatsächliche Redirect-URI-Format von Claude Desktop vor dem Implementieren von Loopback-Matching-Logik verifizieren.

---

## 5. Confidence Level

**Gesamtbewertung: HOCH**

Begründung:

- **F-1** ist deterministisch: PostgreSQL erlaubt `now()` in Partial-Index-Prädikaten schlicht nicht. Dies ist kein spekulativer Fehler, sondern ein bekanntes PostgreSQL-Constraint. Jeder Lauf der Migration gegen einen echten Postgres-Container bestätigt es.

- **F-2** ist ein klassischer TOCTOU-Fehler, der in OAuth-Implementierungen gut dokumentiert ist. EF Core verführt zu SELECT-then-UPDATE-Patterns.

- **F-3** ist der häufigste Fehler beim Aufsetzen von Multi-Scheme-Auth in ASP.NET Core; er ist in Stack Overflow gut dokumentiert und erscheint zuverlässig, wenn kein explizites Schema-Binding gesetzt wird.

- **F-5** basiert auf der bekannten MCP-Spec-Fluktuation (2024–2025) und dem Fehlen konkreter Claude-Desktop-Verhaltensdokumentation im Plan.

Die Mitigationen für F-1 bis F-3 sind präzise und codierbar. F-5 erfordert empirische Verifikation gegen die laufende Claude-Desktop-App, bevor Code geschrieben wird — das ist die wichtigste Pre-Implementation-Aktion.

**Höchste Priorität vor erstem Commit:**
1. Partial-Index-Prädikate korrigieren (F-1)
2. Claude Desktop live gegen eine Stub-MCP-Endpoint beobachten: welchen `WWW-Authenticate`-Header erwartet es? (F-5)
3. Auth-Schema-Binding explizit setzen (F-3)
