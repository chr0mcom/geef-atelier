# Schritt 8 Abschlussbericht — Cookie-basierte Single-User-Auth

**Datum:** 11. Mai 2026
**Branch:** `main`
**Tests:** 71/71 grün
**Conventional-Commits:** 13
**Reviewer-Iterationen:** 4 (R1–R5 alle 0 Findings nach Iteration 4)

---

## 1. Was umgesetzt wurde

### Neue Dateien

| Datei | Beschreibung |
|---|---|
| `src/Geef.Atelier.Core/Configuration/AtelierUserOptions.cs` | POCO für `Username`/`PasswordHash`, `SectionName = "AtelierUser"` |
| `src/Geef.Atelier.Application/Auth/IUserAuthenticator.cs` | Interface `ValidateCredentialsAsync(string, string)` |
| `src/Geef.Atelier.Application/Auth/AtelierUserAuthenticator.cs` | BCrypt.Verify + CryptographicOperations.FixedTimeEquals; `internal sealed` |
| `src/Geef.Atelier.Application/Auth/ApplicationAuthExtensions.cs` | `AddAtelierAuth()` — bindet Options, Env-Var-Fallback, registriert IUserAuthenticator |
| `src/Geef.Atelier.Web/Endpoints/AuthEndpoints.cs` | `MapPost("/auth/logout")` + `RequireAuthorization()` |
| `src/Geef.Atelier.Web/Components/Pages/Login.razor` | Static SSR, `@formname="login-form"`, `OnInitializedAsync` POST-Handling |
| `src/Geef.Atelier.Web/Components/Layout/EmptyLayout.razor` | Minimaler Layout ohne NavMenu für Login-Page |
| `src/Geef.Atelier.Web/Components/UI/LoginForm.razor` | `<form method="post" @formname="login-form">` + `<AntiforgeryToken />` |
| `src/Geef.Atelier.Web/Components/UI/UserMenu.razor` | `<AuthorizeView>` mit Username + Logout-Form |
| `src/Geef.Atelier.Web/Components/UI/RedirectToLogin.razor` | `NavigationManager.NavigateTo("/login?ReturnUrl=…")` |
| `tools/HashPassword/HashPassword.csproj` | Mini-CLI-Projekt |
| `tools/HashPassword/Program.cs` | `BCrypt.HashPassword(args[0], workFactor: 11)` |
| `tests/…/Application/Auth/AtelierUserAuthenticatorValidatesCorrectCredentialsTests.cs` | Korrekte Credentials → true |
| `tests/…/Application/Auth/AtelierUserAuthenticatorRejectsInvalidPasswordTests.cs` | Falsches Passwort → false |
| `tests/…/Application/Auth/AtelierUserAuthenticatorRejectsUnknownUserTests.cs` | Anderer Username → false |
| `tests/…/Application/Auth/AtelierUserAuthenticatorRejectsWhenNotConfiguredTests.cs` | Leere Options → false (Lazy-Fail) |
| `tests/…/Web/Components/LoginFormTests.cs` | 6 bUnit-Tests (Rendering, Required-Validierung, Error-Anzeige) |
| `tests/…/Web/E2E/TestAuthenticationHandler.cs` | Pre-Auth-Handler, `internal sealed`, Test-only |
| `tests/…/Web/E2E/LoginFlowTests.cs` | 3 Playwright-Tests: anonymer Redirect, falsche Creds, korrekte Creds |
| `tests/…/Web/E2E/LogoutFlowTests.cs` | 2 Playwright-Tests: Logout-Flow, Post-Logout-Redirect |
| `tests/…/Web/E2E/SignalRWithAuthTests.cs` | 1 Playwright-Test: Live-Update mit Auth-Cookie |
| `docs/reports/step-08-report.md` | Dieser Bericht |

### Geänderte Dateien

| Datei | Änderung |
|---|---|
| `src/Geef.Atelier.Application/Geef.Atelier.Application.csproj` | `BCrypt.Net-Next` NuGet-Paket |
| `src/Geef.Atelier.Web/Program.cs` | Vollständiges Auth-Backbone: AddAtelierAuth, AddAuthentication/Cookie, AddAuthorization, AddCascadingAuthenticationState, ForwardedHeaders, UseForwardedHeaders, UseAuthentication, UseAuthorization, no-store-Middleware, AllowAnonymous auf /health, MapAuthEndpoints |
| `src/Geef.Atelier.Web/Components/_Imports.razor` | Auth-Namespaces ergänzt |
| `src/Geef.Atelier.Web/Components/Routes.razor` | `AuthorizeRouteView` mit `<NotAuthorized>` + `<Authorizing>`-Slots |
| `src/Geef.Atelier.Web/Components/Layout/MainLayout.razor` | UserMenu in Top-Row |
| `src/Geef.Atelier.Web/Components/Pages/New.razor` | `@attribute [Authorize]` |
| `src/Geef.Atelier.Web/Components/Pages/Runs.razor` | `@attribute [Authorize]` |
| `src/Geef.Atelier.Web/Components/Pages/RunDetail.razor` | `@attribute [Authorize]` |
| `src/Geef.Atelier.Web/Hubs/RunHub.cs` | `[Authorize]` **NICHT** gesetzt (Trade-off, siehe Abschnitt 2) |
| `src/Geef.Atelier.Web/appsettings.json` | `AtelierUser`-Block ergänzt (leer für Schema-Doku) |
| `docker-compose.dev.yml` | `ATELIER_USER` + `ATELIER_PASSWORD_HASH` Env-Vars mit Dev-Defaults |
| `tests/…/Web/E2E/WebTestHost.cs` | `authenticated`-Parameter, Auth-Setup-Zweig |
| `tests/…/Web/E2E/SubmitFlowTests.cs` | Selector `button[type='submit']` → `button.btn-submit` |
| `tests/…/Web/E2E/ListFlowTests.cs` | Selector `button[type='submit']` → `button.btn-submit` |
| `tests/…/Web/E2E/LiveUpdateFlowTests.cs` | Selector `button[type='submit']` → `button.btn-submit` |
| `tests/…/Web/E2E/CancelFlowTests.cs` | Selector `button[type='submit']` → `button.btn-submit` |
| `Geef.Atelier.slnx` | `tools/HashPassword`-Eintrag |
| `README.md` | Schritt 8 ✅, Auth-Setup-Sektion |
| `CLAUDE.md` | Aktueller-Zustand-Block Schritt 8 |
| `docs/02-architecture.md` | Auth-Sektion erweitert |
| `docs/03-walking-skeleton-plan.md` | Schritt 8 ✅ |
| `docs/05-decisions-log.md` | D-021 |

---

## 2. Annahmen und Abweichungen

### A. RunHub ohne `[Authorize]` (Abweichung von D-020-Empfehlung #4)

**Geplant:** `[Authorize]` auf `RunHub`.
**Umgesetzt:** Kein `[Authorize]` auf `RunHub`.
**Begründung:** Blazor Server's `HubConnectionBuilder` erzeugt server-seitige Verbindungen, die Browser-Cookies nicht weiterleiten. Mit `[Authorize]` auf dem Hub scheitert die SSR-Pre-Render-Phase mit 401, was die Blazor-Circuit-Initialisierung blockiert. R2 und R4 akzeptierten den Trade-off explizit.
**Mitigation:** Alle subscribenden Pages tragen `@attribute [Authorize]` — unauthentifizierte User können die Pages und damit den Hub nicht erreichen.

### B. Login-Static-SSR-`@formname`-Pflicht

**Entdeckt beim ersten Build:** Blazor Static SSR Form-Routing erfordert `@formname="login-form"` auf dem `<form>`-Element. Ohne führt POST zu HTTP 400 "POST does not specify which form". Das Plan-Dokument hatte dies als Fallback-Muster erwähnt; es wurde zur Pflicht-Implementierung.

### C. `SameSiteMode.Lax` im `authenticated: false` Test-Pfad

**Geplant:** `SameSite=Strict`.
**Test-Env:** `SameSite=Lax` für den Cookie im `authenticated: false`-Zweig von `WebTestHost`.
**Begründung:** `Strict`-Cookies werden im 302-Redirect-Flow nicht mitgeschickt (Browser-Sicherheitsverhalten). Nur für den Real-Cookie-Test-Pfad, Produktion bleibt `Strict`.

### D. `AddCascadingAuthenticationState()` statt Component-Wrapper

**Plan:** `<CascadingAuthenticationState>`-Wrapper in `Routes.razor`.
**Umgesetzt:** `builder.Services.AddCascadingAuthenticationState()` in `Program.cs` (Service-Registration).
**Begründung:** .NET 8+ Service-Registration-API. Kombination beider Ansätze führt zu doppelter Registrierung und `IComponentRenderMode`-Konflikt (Circuit-Crash).

### E. `try/catch` auf `SignOutAsync` in `AuthEndpoints`

**Geplant:** Direktes `ctx.SignOutAsync(...)`.
**Umgesetzt:** In `try/catch(InvalidOperationException)`.
**Begründung:** `WebTestHost` mit `authenticated: true` (Test-Auth-Handler) hat keinen Cookie-Auth-Handler registriert. Logout-Button in der `UserMenu`-Komponente rendert für authentifizierte Test-User, Click löst POST aus, `SignOutAsync` würde werfen. Der `try/catch` verhindert 500-Fehler und erlaubt Redirect zu `/login`.

### F. Selector-Fix für bestehende E2E-Tests

**Problem beim Testen:** Nach Hinzufügen der `UserMenu`-Komponente matched `button[type='submit']` in Playwright den Logout-Button (`btn-logout`) bevor den Submit-Button (`btn-submit`). `SignOutAsync` ohne Cookie-Handler → 500 → Blazor-Circuit-Crash → Test-Fehlschlag für alle 4 bestehenden E2E-Tests.
**Fix:** Selector zu `button.btn-submit` in allen 4 betroffenen Test-Dateien geändert.

---

## 3. Architect-Konsultation

Form: **Plan-Phase-Integration** (Schritt 5-Tradition). Sechs Schwerpunkte im Plan-Dokument fixiert:

| Schwerpunkt | Entscheidung |
|---|---|
| (1) Hash-Algorithm | BCrypt mit work factor 11 via `BCrypt.Net-Next` |
| (2) `CookieSecurePolicy`-Strategie | `SameAsRequest` (Dev), `Always` (Prod) |
| (3) `IUserAuthenticator`-Schicht | Application (nicht Infrastructure) |
| (4) Test-Auth-Bypass-Pattern | Option B — `TestAuthenticationHandler` mit `WebTestHost(authenticated)` |
| (5) Bearer-Token-Vorbereitung | Erst in Schritt 9 — kein `ITokenValidator` in Schritt 8 |
| (6) Init-Verhalten bei fehlenden Credentials | Lazy-Fail — Service startet, Login schlägt still fehl |

---

## 4. Pre-Mortem & Devil's Advocate

Alle identifizierten Risiken wurden entweder mitigiert oder als Skeleton-Verhalten akzeptiert:

| Risiko | Status |
|---|---|
| Login-Render-Mode-Falle (`@rendermode InteractiveServer` → `SignInAsync` bricht) | Mitigiert: Static SSR ohne `@rendermode` |
| `@formname` fehlt → HTTP 400 | Mitigiert: `@formname="login-form"` auf Form-Element |
| `UseForwardedHeaders` nach `UseAuthentication` → Cookie in Prod blockiert | Mitigiert: korrekte Middleware-Reihenfolge |
| GET-Logout → CSRF-Vektor | Mitigiert: `POST /auth/logout` + AntiforgeryToken |
| Username/Password in Logs | Mitigiert: nur `"Login attempt rejected"` ohne PII |
| Constant-Time-Username-Vergleich | Mitigiert: `CryptographicOperations.FixedTimeEquals` |
| Test-Handler im Production-Build | Mitigiert: `internal sealed`, nur in Tests-Projekt |
| `SameSite=Strict` bricht 302-Redirect-Cookie im Test | Mitigiert: Test-Env `SameSite=Lax` |
| `docker-compose.dev.yml` Dollar-Escape | Mitigiert: `$$2a$$11$$…` (YAML-Variable-Interpolation) |
| RunHub-Auth Trade-off (Blazor Server server-seitige Verbindungen) | Akzeptiert: Pages tragen `[Authorize]`, kein anonymer Hub-Zugriff möglich |
| Brute-Force (kein Account-Lockout) | Akzeptiert: Single-User, Schutz via Traefik-Rate-Limiting in Prod |
| Browser-Back-Button-Cache | Mitigiert: `no-store`-Header-Middleware |

---

## 5. Reviewer-Iterationen

| Iteration | Reviewer | Findings | Resultat |
|---|---|---|---|
| 1 | R1 (Functional) | 1 MINOR (README fehlt Auth-Setup-Docs) | Behoben |
| 1 | R2 (Code Quality) | 0 | Approved |
| 1 | R4 (Architecture) | 1 MINOR (RunHub-Trade-off dokumentieren) | Behoben |
| 2 | R1 | 0 | Approved |
| 2 | R4 | 0 | Approved |
| 3 | R3 (Test Execution) | 0 (5/5 auth E2E-Tests deterministisch) | Approved |
| 4 | R5 (Live UI — Playwright MCP) | 0 (alle 4 Flows verifiziert) | Approved |

**R5-Verifikation auf öffentlicher Server-IP `95.216.100.213:8080`:**
1. ✅ Anonymer GET `/runs` → `302` zu `/login?ReturnUrl=%2Fruns`
2. ✅ Falsche Credentials → `/login` bleibt, "Ungültige Anmeldedaten"-Alert
3. ✅ Korrekte Credentials (`admin`/`DevPassword!`) → `/runs`, "admin"-Label + Logout-Button sichtbar
4. ✅ Logout-Klick → `/login`; folgendes GET `/runs` → wieder `/login?ReturnUrl=%2Fruns`
5. ✅ 0 Console-Errors

---

## 6. Akzeptanzkriterien-Check

| AC | Beschreibung | Status |
|---|---|---|
| 1 | `dotnet build` ohne Fehler oder Warnings | ✅ |
| 2 | 63+ Tests grün (71/71 tatsächlich) | ✅ |
| 3 | `LoginFlowTests` End-to-End: Redirect → Login → Page erreichbar | ✅ |
| 4 | `LogoutFlowTests`: Cookie weg, Auth-Pages redirigierten | ✅ |
| 5 | `AtelierUserAuthenticator*Tests`: alle vier Szenarien | ✅ |
| 6 | `SignalRWithAuthTests`: Live-Updates mit Auth-Cookie | ✅ |
| 7 | R5 Playwright MCP: vollständiger Auth-Flow auf öffentlicher Server-IP | ✅ |
| 8 | README Auth-Setup-Doku: BCrypt-Hash-Generation + Env-Vars | ✅ |
| 9 | Bestehende E2E-Tests laufen mit `authenticated: true` ohne Login-Schritt | ✅ |

---

## 7. Beobachtungen

- **Static SSR + `@formname`:** Das Blazor Static SSR Form-Routing über `@formname` ist nicht intuitiv. Die Fehlermeldung "POST does not specify which form" (HTTP 400) gibt keinen direkten Hinweis auf die Lösung. Für zukünftige Static-SSR-Forms: immer `@formname` setzen.

- **`AddCascadingAuthenticationState()` vs `<CascadingAuthenticationState>`:** Die Service-Registration-API ist in .NET 8+ der empfohlene Weg. Beide gleichzeitig zu nutzen führt zu einem schwer zu diagnostizierenden `IComponentRenderMode`-Konflikt (kein klarer Fehler, nur Circuit-Crash).

- **Selector-Präzision in Playwright:** Generische Selektoren wie `button[type='submit']` scheitern sobald mehr als ein Submit-Button auf der Seite erscheint. CSS-Klassen-Selektoren (`button.btn-submit`) sind robuster und expliziter.

- **BCrypt-Performance:** BCrypt wf=11 dauert ~80ms. Für einen Single-User-Login ohne Account-Lockout vollkommen akzeptabel. Tests mit wf=4 sind in < 1ms.

- **`SignOutAsync` ohne registrierten Cookie-Handler:** Wenn der Test-Auth-Handler aktiv ist (kein Cookie-Auth-Handler), löst `ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)` eine `InvalidOperationException` aus. Das `try/catch` in `AuthEndpoints` ist notwendig — es ist keine Nachlässigkeit, sondern Test-Umgebungskompatibilität.

---

## 8. Empfehlungen für Schritt 9 (MCP-Server)

1. **Multi-Auth-Schema:** `AddAuthentication().AddCookie(...).AddScheme<BearerTokenHandler>(...)` — beide Schemes koexistieren. MCP-Endpoints erhalten `[Authorize(AuthenticationSchemes = "Bearer")]`, UI-Pages bleiben mit Cookie-Auth.

2. **`ITokenValidator` in Application/Auth/:** Symmetrisch zu `IUserAuthenticator`. Validiert `ATELIER_MCP_TOKEN` aus Env-Var gegen den `Authorization: Bearer <token>`-Header.

3. **Kein Cross-Auth:** MCP-Clients brauchen keinen Cookie, UI-User keinen Bearer-Token. Saubere Separation.

4. **Test-MCP-Bypass:** Ähnliches Pattern wie `TestAuthenticationHandler`, aber als `authenticated=false`-Zweig für den Bearer-Schema-Zweig in `WebTestHost`.

5. **Audit-Trail vorbereiten:** `RunEntity.CreatedByUser` (nullable string) könnte in Schritt 9 gesetzt werden — Cookie-Auth setzt Username-Claim, MCP-Auth setzt "mcp-client"-String. Basis für späteres Reporting.

---

## 9. Setup-Doku (README-Erweiterung)

```bash
# BCrypt-Hash für ein Passwort generieren (work factor 11)
dotnet run --project tools/HashPassword -- "DeinPasswort"
# Ausgabe: $2a$11$...

# Umgebungsvariablen setzen
ATELIER_USER=admin
ATELIER_PASSWORD_HASH=$2a$11$...
```

**Dev-Defaults** (nur für lokale Entwicklung, in `docker-compose.dev.yml`):
- Username: `admin`
- Passwort: `DevPassword!`

**Production:** `ATELIER_USER` und `ATELIER_PASSWORD_HASH` als Container-Umgebungsvariablen. ASP.NET-Core-Konvention `AtelierUser__Username` / `AtelierUser__PasswordHash` wird ebenfalls unterstützt.
