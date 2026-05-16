# Code Review: MCP OAuth 2.1 Authorization Server

**Datum:** 2026-05-16  
**Prüfer:** Claude Sonnet 4.6 (Strict Quality Reviewer)  
**Scope:** Geef.Atelier OAuth 2.1 Implementation (Step 19)

---

## Ergebnis

**6 Befunde** — 0 CRITICAL, 2 MAJOR, 4 MINOR

---

## Befunde

### 1. MAJOR — `RevokeClientAsync` ignoriert `clientId` und widerruft alle Tokens

**Datei:** `src/Geef.Atelier.Web/Components/Pages/ConnectedClients.razor`, Zeile 127–143

Der Benutzer klickt „Widerrufen" für einen spezifischen Client (z. B. „Claude Desktop"). `RevokeClientAsync(clientId)` empfängt den Parameter, leitet ihn aber nicht weiter — stattdessen wird `RevokeAllUserTokensAsync(_userId)` aufgerufen. Das bedeutet: das Widerrufen eines einzelnen Clients widerruft alle verbundenen Clients des Benutzers. Funktional identisch mit „Alle widerrufen".

**Reproduzierbar:** Zwei Clients verbinden, einen widerrufen → beide sind danach revoked.

**Ursache:** `IOAuthService` bietet keine `RevokeByClientIdForUserAsync`-Methode. Die Infrastruktur hat `RevokeByClientIdAndUserIdAsync` am Repository, aber es ist nicht auf dem Service exponiert.

**Fix:** `RevokeByClientIdForUserAsync(string userId, string clientId, CancellationToken ct)` auf `IOAuthService` hinzufügen, Repository-Methode aufrufen und in `ConnectedClients.razor` verwenden.

---

### 2. MAJOR — Registrierungs-Token-Vergleich nicht constant-time

**Datei:** `src/Geef.Atelier.Web/Endpoints/OAuthEndpoints.cs`, Zeile 28

```csharp
authHeader["Bearer ".Length..] != registrationToken
```

Der `!=`-Operator für Strings ist **kein** konstant-zeitlicher Vergleich. Bei einem zeitbasierten Angriff könnte ein Angreifer den `OAUTH_REGISTRATION_TOKEN` Zeichen für Zeichen erraten.

**Kontext:** Der Registration-Token schützt `POST /oauth/register`. Er ist ein Geheimnis im Sinne von API-Keys. Der Vergleich muss `CryptographicOperations.FixedTimeEquals` verwenden, genau wie `StaticTokenValidator` es korrekt macht.

**Fix:**
```csharp
var headerToken = authHeader["Bearer ".Length..];
var expectedBytes = System.Text.Encoding.UTF8.GetBytes(registrationToken);
var actualBytes   = System.Text.Encoding.UTF8.GetBytes(headerToken);
if (expectedBytes.Length != actualBytes.Length ||
    !CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
    return Results.Json(new { error = "unauthorized" }, statusCode: 401);
```

---

### 3. MINOR — `POST /oauth/authorize` validiert `redirect_uri` nicht erneut gegen registrierte URIs

**Datei:** `src/Geef.Atelier.Web/Endpoints/OAuthEndpoints.cs`, Zeile 154–186

Der GET-Handler (Razor Page) ruft `ValidateAuthorizationRequestAsync` auf und prüft die `redirect_uri`. Der POST-Handler liest `redirect_uri` direkt aus dem Formular und leitet dorthin weiter (sowohl bei Deny als auch bei Approve), **ohne erneut zu prüfen**, ob sie zu den registrierten URIs des Clients passt.

**Angriffsszenario:** Ein Angreifer, der XSS auf der Seite ausführt, könnte eine manipulierte Formularanfrage an `/oauth/authorize` senden. Die `SameSite=Strict`-Cookie-Einstellung verhindert klassisches CSRF aus einem Fremd-Origin, macht den Angriff aber schwerer, nicht unmöglich.

**Risiko:** Defense-in-depth-Lücke. `ExchangeAuthorizationCodeAsync` validiert `redirect_uri` erneut beim Token-Tausch (korrekte letzte Verteidigungslinie), aber der direkte Redirect im POST ist ungeschützt.

**Fix:** Im POST-Handler `ValidateAuthorizationRequestAsync` aufrufen oder eine Hilfsmethode extrahieren, die `redirect_uri` gegen registrierte URIs prüft, bevor redirected wird.

---

### 4. MINOR — `error_description` gibt interne Details preis

**Datei:** `src/Geef.Atelier.Web/Endpoints/OAuthEndpoints.cs`, Zeile 111

```csharp
return Results.Json(new { error = "invalid_grant", error_description = ex.Message }, statusCode: 400);
```

`ex.Message` enthält Strings wie `"client_id mismatch"`, `"redirect_uri mismatch"` und `"PKCE verification failed"`. Diese enthüllen, an welchem Prüfschritt der Austausch gescheitert ist, was einem Angreifer bei der Fehleranalyse hilft.

RFC 6749 erlaubt `error_description`, aber Best Practice ist, generische Meldungen zu verwenden, die keine Implementierungsdetails preisgeben.

**Fix:** Entweder `error_description` weglassen oder auf generische Texte wie `"Token exchange failed"` beschränken, unabhängig vom spezifischen Fehler.

---

### 5. MINOR — Fehlende Datenbankindizes auf `UserId`-Spalten

**Datei:** `src/Geef.Atelier.Infrastructure/Persistence/Migrations/20260519120000_Step19McpOAuth.cs`

Folgende Operationen führen Full-Table-Scans aus:

- `OAuthRefreshTokens.RevokeByUserIdAsync` — kein Index auf `UserId` (nur `ClientId`-Index vorhanden)
- `OAuthAuditLog.GetRecentByUserIdAsync` — kein Index auf `UserId`

Bei einem einzelnen Benutzer-System (aktueller Stand) ist das kein Problem. Sollte das System skalieren, werden diese Abfragen teuer.

**Fix:** Migration erweitern:
```sql
CREATE INDEX "IX_OAuthRefreshTokens_UserId" ON "OAuthRefreshTokens"("UserId");
CREATE INDEX "IX_OAuthAuditLog_UserId" ON "OAuthAuditLog"("UserId");
```

---

### 6. MINOR — Scope wird nicht gegen erlaubte Werte validiert

**Dateien:** `src/Geef.Atelier.Application/OAuth/OAuthService.cs` (gesamte Datei), `OAuthEndpoints.cs`

Der `scope`-Parameter wird nirgends gegen die erlaubten Werte (`["mcp:full"]`) geprüft. Ein Client könnte beliebige Scope-Werte übergeben, die dann unverändert im Token gespeichert werden.

Aktuell gibt es nur einen Scope (`mcp:full`) und der Bearer-Handler prüft nur Authentisierung, nicht Scopes — daher kein direktes Sicherheitsproblem heute. Aber wenn weitere Scopes mit unterschiedlichen Berechtigungen hinzukommen, fehlt die Validierungsgrundlage.

**Fix:** In `ValidateAuthorizationRequestAsync` prüfen, dass der angeforderte Scope ein Subset der erlaubten Scopes ist. `OAuthOptions` kann ein `AllowedScopes`-Array bekommen.

---

## Was korrekt ist

- **Token-Generierung:** `RandomNumberGenerator.GetBytes(32)` in `OAuthCrypto.GenerateToken()` — korrekt. Kein `Guid.NewGuid()`, kein `Math.Random`.
- **Token-Speicherung:** Nur SHA-256-Hashes in der Datenbank, Plaintext nur an den Client zurückgegeben.
- **PKCE S256:** Korrekt implementiert mit `CryptographicOperations.FixedTimeEquals`. RFC 7636 §4.6 konform (ASCII-Encoding per Spec).
- **Refresh Token Rotation + Reuse Detection:** Korrekt — atomisches `ConsumeAsync` + `FindByHashAsync` + `RevokeAllUserTokensAsync` bei Replay-Angriff.
- **`StaticTokenValidator`:** Verwendet `CryptographicOperations.FixedTimeEquals` korrekt.
- **Kein Token-Logging:** Weder `OAuthService` noch `BearerTokenHandler` noch `OAuthEndpoints` loggen Plaintext-Token-Werte.
- **`OAuthCrypto` intern:** Korrekt als `internal static class` — nicht außerhalb der Application-Assembly sichtbar.
- **Authorization Code:** Single-use, 10 Minuten TTL, atomisch konsumiert.
- **Consent-Seite:** `[Authorize]`-Attribut auf der Razor Page, RequireAuthorization mit CookieScheme auf dem POST-Endpoint.
- **SQL-Injection:** Die Raw-SQL in der Migration verwendet ausschließlich DDL ohne User-Input.
- **Fehlerbehandlung:** `InvalidOperationException` wird im Token-Endpoint korrekt gefangen.
- **CancellationToken:** Durchgängig weitergeleitet.
- **Loopback-Redirect:** RFC 8252-konformes Port-Ignorieren für 127.0.0.1/localhost.
- **Test-Qualität:** Tests prüfen echte Negativfälle (falsche PKCE, falsche Client-ID, abgelaufene Codes, Replay-Angriff). Keine `assertTrue(true)`-Attrappen.
- **OAuthCleanupBackgroundService:** Korrekt mit `IServiceScopeFactory`, `PeriodicTimer` und Exception-Handling.
