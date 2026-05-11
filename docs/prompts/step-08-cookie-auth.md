# Claude-Code-Prompt: Schritt 8 — Cookie-basierte Single-User-Auth

*Diese Datei ist als Eingabe für Claude Code gedacht.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Schritte 1–7 + M1 sind abgeschlossen und in main: das System hat eine funktionierende UI mit Live-Updates via SignalR, läuft mit OpenRouter als LLM-Provider, alle 55 Tests grün, AC8 (Real-Pipeline-Test) verifiziert. Deine Aufgabe ist **Schritt 8 von 10**: **Cookie-basierte Single-User-Authentifizierung** für die Web-UI.

Was sich ändert: Eine Login-Page kommt hinzu, alle bestehenden Pages und der SignalR-Hub werden mit `[Authorize]` geschützt, User-Credentials werden über Environment-Variablen konfiguriert. Was bleibt unverändert: Pipeline, Orchestrator, Persistierung, LLM-Schicht, Application-Service-Layer, UI-Komponenten. Die Auth-Schicht ist eine zusätzliche **Middleware-Spange**, kein Eingriff in die bestehende Logik.

MCP-Bearer-Token-Auth kommt erst in Schritt 9 — Cookie-Auth jetzt deckt nur die UI ab. Production-Deploy in Schritt 10.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors, alle Hard Rules.

**Phase-1.4-Hinweis:** Plan-Phase-Integration hat sich seit Schritt 5 etabliert — Architect-Entscheidungen direkt im Plan-Dokument fixieren (siehe D-020). Sollte auch hier gut funktionieren, weil Schritt 8 ein klar umrissener Auth-Aufsatz ist.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

Vor Beginn der Implementierung lies in dieser Reihenfolge:

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`CLAUDE.md`** im Repo-Root.
3. **`docs/02-architecture.md`**, besonders das Schichtenbild. Auth-Sektion ggf. ergänzen (falls noch nicht vorhanden — beim Architect entscheiden).
4. **`docs/03-walking-skeleton-plan.md`**, Abschnitt "Schritt 8".
5. **`docs/05-decisions-log.md`**, alle Einträge **D-010 bis D-020**. Besonders **D-020** mit den Schritt-7-Realfakten — UI-Schicht, SignalR-Hub, Pages-Struktur.
6. **`docs/reports/step-07-report.md`**, besonders **Sektion 8 (Empfehlungen für Schritt 8)** mit der Cookie-Auth-Architektur-Empfehlung.
7. **Aktueller Code im Repo:**
   - `src/Geef.Atelier.Web/Program.cs` — DI-Registrierung; Auth-Middleware kommt hier rein.
   - `src/Geef.Atelier.Web/Components/Pages/` — alle bestehenden Pages werden mit `[Authorize]` geschützt.
   - `src/Geef.Atelier.Web/Hubs/RunHub.cs` — SignalR-Hub wird mit `[Authorize]` geschützt.
   - `src/Geef.Atelier.Web/Components/Layout/MainLayout.razor` — Auth-Status-Anzeige (Logout-Button etc.) wird ergänzt.
   - `tests/Geef.Atelier.Tests/Web/E2E/WebTestHost.cs` — muss für Auth-Tests angepasst werden (entweder Auth deaktivieren oder Test-User einrichten).
8. **ASP.NET Core Cookie Authentication Doku:** `CookieAuthenticationDefaults`, `AddCookie()`, `LoginPath`/`LogoutPath`, `SignInAsync`/`SignOutAsync`, `ClaimsPrincipal`, `ClaimsIdentity`.
9. **Blazor Authentication Doku:** `CascadingAuthenticationState`, `<AuthorizeView>`, `[Authorize]`-Attribut auf `@page`-Komponenten, `AuthenticationStateProvider`.
10. **SignalR + Auth Doku:** Cookie-basierte Auth funktioniert automatisch mit SignalR über WebSocket-Handshake, `[Authorize]`-Attribut auf Hub.
11. **BCrypt.Net-Next** Doku (für Password-Hashing): `BCrypt.HashPassword(password, workFactor: 11)` und `BCrypt.Verify(password, hash)`.

## In Schritten 1–7+M1 etablierte Realfakten (verbindlich)

Aus D-010 bis D-020. Zentrale Punkte für Schritt 8:

**UI-Schicht (post-Schritt-7):**
- Drei Pages: `/new`, `/runs`, `/runs/{RunId:guid}` — alle bekommen `[Authorize]`.
- SignalR-Hub `/hubs/runs` — bekommt `[Authorize]`. Cookie-Auth funktioniert über WebSocket-Handshake automatisch.
- UI-Komponenten in `Components/UI/` — Auth-spezifische Komponenten (Login-Form, User-Avatar) kommen ggf. dort hin (siehe Atelier-Auslegung in D-020(g)).

**Atelier-Konvention für UI-Komponenten:**
- Wiederverwendbare UI-**Logik** gehört in `Components/UI/`.
- Triviale Page-Steuerelemente (einfache Buttons, Container) dürfen in Pages bleiben.
- Auth-Felder (`<input type="text">`, `<input type="password">`) sind triviale Page-Form-Elemente — gehören in die Login-Page, nicht zwingend in eine Komponente.

**Test-Infrastruktur:**
- `WebTestHost` (aus Schritt 7) mit `WebApplicationFactory<Program>` + Kestrel auf Port 0.
- bUnit für Komponenten-Unit-Tests.
- Playwright für E2E-Tests.
- 55/55 bestehende Tests — alle müssen weiter grün bleiben. Test-Infrastruktur muss Auth umgehen können (für E2E-Tests die nicht den Login-Flow testen).

**SignalR-Notifier:**
- `IRunNotifier` in Core, `SignalRRunNotifier` in Web als Singleton.
- Best-effort-Pattern mit try/catch in beiden Schichten — bei Auth-Failure (z.B. unauthenticated Client) wird der `SendAsync` einfach fehlschlagen, der Sink läuft trotzdem weiter.

## Konkrete technische Anforderungen für Schritt 8

### `AtelierUserOptions` und Konfiguration

Neue `AtelierUserOptions` in `src/Geef.Atelier.Core/Configuration/`:
```csharp
public sealed class AtelierUserOptions
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}
```

Konfiguration: **Ausschließlich** über Environment-Variablen:
- `ATELIER_USER` → `AtelierUser__Username`
- `ATELIER_PASSWORD_HASH` → `AtelierUser__PasswordHash`

Keine Sektion in `appsettings.json` — dort steht nur ein leerer Default-Block für Schema-Dokumentation:
```json
{
  "AtelierUser": {
    "Username": "",
    "PasswordHash": ""
  }
}
```

Falls `Username` oder `PasswordHash` leer sind beim Service-Start: Sofortige `InvalidOperationException` in der Login-Verifizierung. Service startet trotzdem (Health-Check soll funktionieren), aber Login schlägt fehl. Architect-Entscheidung: Alternative wäre Fail-Fast beim Startup — beide Varianten haben Pro/Contra.

**BCrypt für Password-Hash:**
- Hash-Format: `$2a$11$...` (BCrypt mit work factor 11)
- NuGet-Paket: `BCrypt.Net-Next`
- Hash-Generation: Setup-Script oder Doc im README mit `dotnet run --project tools/HashPassword` oder einfach Kommando-Anweisung: `dotnet user-secrets` oder `htpasswd`-Hinweis.

### Auth-Service: `IUserAuthenticator` in `Geef.Atelier.Application/Auth/`

Application-Layer-Vertrag für Authentifizierung. Schicht-Entscheidung: Application, weil:
- Auth ist Anwendungs-Logik, nicht Infrastruktur.
- MCP (Schritt 9) wird vermutlich eine andere `IUserAuthenticator`-Variante haben (Bearer-Token statt Username/Password) — beide leben dann in Application.

```csharp
public interface IUserAuthenticator
{
    Task<bool> ValidateCredentialsAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);
}

internal sealed class AtelierUserAuthenticator : IUserAuthenticator
{
    private readonly IOptions<AtelierUserOptions> _options;
    // BCrypt.Verify-basierte Implementierung
}
```

**Wichtige Disziplin:** Niemals Username oder Password-Hash in Logs.

### Login-Page: `src/Geef.Atelier.Web/Components/Pages/Login.razor`

```razor
@page "/login"
@layout EmptyLayout  // ohne NavMenu — User ist noch nicht eingeloggt

<LoginForm OnLogin="HandleLogin" Error="@_error" />
```

**Logik:**
- `LoginForm`-Komponente mit Username- und Password-Feldern, Submit-Button, optionaler Error-Anzeige.
- `HandleLogin` ruft `IUserAuthenticator.ValidateCredentialsAsync`. Bei Erfolg: `HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(...))` mit `ClaimsIdentity` für den User.
- Redirect nach erfolgreichem Login zu `ReturnUrl` (Query-Param) oder `/runs` als Default.
- Bei Fehlschlag: Error-Message "Ungültige Anmeldedaten" anzeigen — **nicht** zwischen "User nicht gefunden" und "falsches Passwort" unterscheiden (Standard-Sicherheits-Pattern gegen User-Enumeration).

**Login-Form-Komponente in `Components/UI/`:**
- `LoginForm.razor` + `.razor.css` mit `EditForm` + `DataAnnotationsValidator`.
- `LoginFormModel { Username, Password }` mit `[Required]` auf beiden.
- `EventCallback<LoginFormModel> OnLogin`.

### Logout-Endpoint

Da Blazor-Komponenten keinen direkten Zugriff auf `HttpContext.SignOutAsync` haben (Render-Tree-Limitation), brauchen wir einen kleinen MVC-Endpoint:

```csharp
// in src/Geef.Atelier.Web/Endpoints/AuthEndpoints.cs (oder direkt in Program.cs)
app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});
```

**Logout-Button** in `MainLayout.razor` oder NavMenu:
- Sichtbar nur bei eingeloggtem User (`<AuthorizeView>`).
- Form mit `method="post" action="/auth/logout"` — kein JS nötig.
- Username-Anzeige neben dem Button.

**Architect-Frage:** Alternative wäre `SignOutManager`-Pattern via JS-Interop — overkill für Skeleton. Empfehlung: MVC-Endpoint.

### `[Authorize]` auf Pages und Hub

Auf allen Pages: `@attribute [Authorize]` direkt unter `@page`-Direktive:
```razor
@page "/runs"
@attribute [Authorize]
```

Für `Index.razor` (Welcome-Page): **Kein** `[Authorize]` — die Welcome-Page soll auch für anonyme User sichtbar sein als "Bitte einloggen"-Hinweis. Architect kann widersprechen, falls die Welcome-Page entfernt werden soll.

Auf `RunHub`:
```csharp
[Authorize]
public sealed class RunHub : Hub { ... }
```

Cookie wird beim SignalR-WebSocket-Handshake automatisch mitgeschickt — keine zusätzliche Konfiguration nötig.

### `App.razor` und Layout-Anpassung

```razor
<CascadingAuthenticationState>
    <Router AppAssembly="@typeof(Program).Assembly">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)">
                <NotAuthorized>
                    <RedirectToLogin />
                </NotAuthorized>
            </AuthorizeRouteView>
        </Found>
    </Router>
</CascadingAuthenticationState>
```

`RedirectToLogin.razor` als Helper-Komponente in `Components/UI/`:
- Liest aktuelle URL als `ReturnUrl`.
- Navigiert zu `/login?ReturnUrl={url}`.

`EmptyLayout.razor` als Layout für Login-Page (ohne NavMenu):
- Minimaler Wrapper, kein NavMenu, kein User-Avatar.

### `Program.cs`-Erweiterung

```csharp
builder.Services.Configure<AtelierUserOptions>(builder.Configuration.GetSection("AtelierUser"));
builder.Services.AddScoped<IUserAuthenticator, AtelierUserAuthenticator>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.LogoutPath = "/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;  // erfordert HTTPS
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Name = "Atelier.Auth";
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// ... bestehende Services ...

// nach app.UseRouting():
app.UseAuthentication();
app.UseAuthorization();

// am Ende:
app.MapPost("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});
```

**Architect-Frage zu `CookieSecurePolicy.Always`:** In Development (`http://localhost:8080`) ohne HTTPS würde das Cookies nicht setzen. Lösung: `CookieSecurePolicy.SameAsRequest` in Development oder explizit über `IHostEnvironment` switchen. Empfehlung: `SameAsRequest` in Dev, `Always` in Production.

### Tests

**Neue Application-Tests** in `tests/Geef.Atelier.Tests/Application/`:
1. `AtelierUserAuthenticatorValidatesCorrectCredentialsTests` — gültige Username/Password-Kombi → `true`.
2. `AtelierUserAuthenticatorRejectsInvalidPasswordTests` — falsches Password → `false`.
3. `AtelierUserAuthenticatorRejectsUnknownUserTests` — anderer Username → `false`.
4. `AtelierUserAuthenticatorRejectsWhenNotConfiguredTests` — leere `AtelierUserOptions` → `false` (oder throw, abhängig von Architect-Entscheidung).

**Neue bUnit-Tests** in `tests/Geef.Atelier.Tests/Web/Components/`:
1. `LoginFormTests` — Form-Rendering, Validierung, Submit-Button-State.

**Neue Playwright E2E-Tests** in `tests/Geef.Atelier.Tests/Web/E2E/`:
1. `LoginFlowTests`:
   - Navigiere zu `/runs` → Redirect zu `/login?ReturnUrl=...`
   - Falsche Credentials → Error-Message angezeigt, bleibt auf `/login`
   - Korrekte Credentials → Redirect zu `/runs`, Logout-Button sichtbar
2. `LogoutFlowTests`:
   - Eingeloggter User klickt Logout → Cookie weg, Redirect zu `/login`
   - Anschließender Versuch `/runs` zu erreichen → wieder Redirect zu `/login`
3. `SignalRWithAuthTests`:
   - Eingeloggter User submitted Run, SignalR-Live-Updates funktionieren weiterhin.
   - Schwächerer Negativtest: Anonyme `HubConnection` schlägt fehl beim `StartAsync` (optional, falls einfach zu testen).

**Bestehende E2E-Tests anpassen:**

`WebTestHost` muss eine Auth-Bypass-Option bekommen, damit `SubmitFlowTests`, `ListFlowTests`, `LiveUpdateFlowTests`, `CancelFlowTests` weiter ohne Login-Schritt durchlaufen können. Drei Optionen:

- **Option A:** `WebTestHost` setzt `AtelierUserOptions` auf bekannte Test-Credentials und automatisiert Login beim Test-Setup.
- **Option B:** `WebTestHost` registriert eine Test-`IAuthenticationHandler`-Implementierung, die jeden Request als authenticated User markiert.
- **Option C:** `WebTestHost` deaktiviert Auth-Middleware komplett.

Architect entscheidet. Empfehlung: Option B — sauberer als komplettes Deaktivieren, kein Wartungs-Overhead beim Login pro Test.

**Bestehende 55 Tests müssen grün bleiben** — Auth darf bestehende Logik nicht brechen.

### Setup-Doku für `README.md`

Ergänze Setup-Sektion mit:
- BCrypt-Hash-Generation-Anleitung (entweder `dotnet run --project tools/HashPassword` Mini-Tool oder Inline-Kommando-Beispiel).
- `ATELIER_USER` und `ATELIER_PASSWORD_HASH` Env-Var-Setup für lokale Entwicklung und Docker-Compose.
- Login-Flow-Beschreibung für neue Maintainer.

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` ohne Fehler oder Warnings.
2. `dotnet test` — alle bestehenden 55 Tests + neue Auth-Tests grün.
3. **`LoginFlowTests`** zeigt End-to-End-Login-Pfad: Redirect bei anonymem Zugriff, Login mit korrekten Credentials, Redirect zu geschützter Page.
4. **`LogoutFlowTests`** zeigt Logout-Pfad: Cookie wird gelöscht, anschließende Page-Aufrufe redirecten zu Login.
5. **`AtelierUserAuthenticator*Tests`** verifizieren Credential-Validierung in allen vier Szenarien.
6. **`SignalRWithAuthTests`** bestätigt: Live-Updates funktionieren mit Auth-Cookie weiterhin.
7. **R5 (Playwright)** prüft den vollständigen Auth-Flow mit allen drei Pages.
8. Setup-Doku im README für BCrypt-Hash-Generation und Env-Var-Konfiguration.
9. Bestehende E2E-Tests laufen mit Auth-Bypass-Option in `WebTestHost` weiter ohne Login-Schritt.

## Was du in diesem Schritt NICHT tust

- **Kein Multi-User** — Single-User-Setup, ein hardcoded User aus Env-Vars.
- **Kein Password-Reset** — Hash-Regeneration via Env-Var-Update + Service-Restart.
- **Kein 2FA / TOTP / OAuth** — nur Username + Password.
- **Kein Session-Management komplex** — Cookie-only, 30-Tage-Lifetime.
- **Kein Account-Lockout** — kein Brute-Force-Schutz im Skeleton (Single-User, hinter Auth-Reverse-Proxy in Production).
- **Kein Bearer-Token-Auth für MCP** — kommt in Schritt 9.
- **Keine LDAP/AD-Integration** — Single-User, Env-Var-basiert.
- **Keine Provider-Änderungen** — LLM-Schicht bleibt unverändert.
- **Keine UI-Komponenten-Refactorings** — bestehende Pages bekommen nur das `[Authorize]`-Attribut, Logik unverändert.

## Architect-Konsultation (Phase 1.4) — sechs Schwerpunkte

1. **Hash-Algorithm:** BCrypt (Standard, einfach) vs. Argon2 (modern, langsamer) vs. PBKDF2 (alt-vertraut)? Empfehlung: BCrypt mit work factor 11.
2. **`CookieSecurePolicy`-Strategie:** `Always` in Production (HTTPS-only), `SameAsRequest` in Development. Wie wird das umgeschaltet — `IHostEnvironment.IsDevelopment()` oder Config-Flag?
3. **`IUserAuthenticator`-Schicht:** Application (Empfehlung) vs. Infrastructure vs. Web? Konsequenzen für MCP-Schritt 9.
4. **Test-Auth-Bypass-Pattern:** Option A (Auto-Login in `WebTestHost`), B (Test-Auth-Handler), C (Middleware deaktivieren)? Empfehlung: B.
5. **Bearer-Token-Vorbereitung für MCP:** Jetzt schon ein `ITokenValidator`-Interface vorbereiten (für Schritt 9 ready) oder erst dann? Empfehlung: erst in Schritt 9.
6. **Init-Verhalten bei fehlenden Credentials:** Fail-Fast beim Startup (Service stürzt ab wenn `ATELIER_USER` nicht gesetzt) vs. Lazy-Fail (Login funktioniert nicht, Health-Check bleibt grün)? Empfehlung: Lazy-Fail — Health-Check muss funktionieren, falls Container hochfährt aber Credentials in K8s-Secret noch nicht da sind.

Plan-Phase-Integration: Die sechs Antworten gehören direkt in den Plan-Phase-Output, kein separater Architect-Aufruf nötig.

## Persistenter Abschlussbericht für den Brainstorming-Chat

Bericht nach `docs/reports/step-08-report.md`, gleicher Aufbau wie Schritte 1–7. Wichtig in diesem Schritt:

1. **Was wurde umgesetzt** — Datei-für-Datei.
2. **Annahmen und Abweichungen** — vor allem zu Hash-Algorithm, Auth-Bypass-Pattern, Logout-Mechanik.
3. **Architect-Output** — alle sechs Schwerpunkte (als Plan-Phase-Output dokumentiert).
4. **Pre-Mortem & Devil's Advocate** — speziell zu: Cookie-Hijacking-Risiko, Race bei Logout während aktivem Run, SignalR-Connection-Verhalten bei Cookie-Ablauf, Brute-Force-Bedrohung (Atelier-Single-User-Kontext).
5. **Reviewer-Iterationen** — Tabelle.
6. **Akzeptanzkriterien-Check** — Tabelle.
7. **Beobachtungen** — Cookie-Verhalten in Dev/Prod, BCrypt-Performance, Test-Auth-Bypass-Mechanik.
8. **Empfehlungen für Schritt 9 (MCP)** — wie greift Bearer-Token-Auth mit Cookie-Auth zusammen? Wo lebt der Token-Validator-Service? Multi-Auth-Scheme-Setup in ASP.NET Core.
9. **Setup-Doku** — wurde der README ergänzt? Wie wird der Hash generiert?

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- **Niemals Username oder Password-Hash in Logs.**
- API-Key niemals in source control, niemals in Logs, niemals im Bericht.
- Auth-Test-Credentials in Test-Code dürfen Klartext sein, aber nicht in `appsettings.json` oder anderswo in main.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.