# R5 — Live UI Verification

**Iteration 1 · Datum:** 2026-05-16

## Strategie

Die OAuth-spezifischen Seiten (Consent-Page `/oauth/authorize`, Connected-Clients `/account/connected-clients`, UserMenu-Erweiterung) sind noch nicht in Production deployed (Branch `feat/mcp-oauth` noch nicht in `main` gemergt). Daher:

- **Regression-Check:** Produktion (https://geef.stefan-bechtel.de) auf bestehende UI-Korrektheit prüfen.
- **OAuth-Seiten:** Durch bUnit-Tests abgedeckt (OAuthAuthorize-Tests + ConnectedClients-Tests — alle grün). Post-Deploy-Verifikation vom User per Desktop-Smoke-Test (gemäß Plan, `Pflicht-AC, User-Entscheidung`).

## Playwright-Test-Ergebnisse

### 1. Login-Page (Production — Regression)

- Navigation zu https://geef.stefan-bechtel.de
- Redirect zu `/login?ReturnUrl=%2F` ✅ (korrekt)
- Handle-Feld vorhanden ✅
- Passphrase-Feld vorhanden ✅
- „Open the door"-Button vorhanden ✅
- Layout zweispaltig (Branding links / Login rechts) ✅
- Keine JavaScript-Console-Errors ✅
- Screenshot: `~/playwright-output/r5-login-regression.png`

### 2. OAuth-Seiten (Pre-Deploy)

**Nicht in Production — pending Phase G Deploy.**

Die folgenden Elemente sind durch bUnit-Tests in `tests/Geef.Atelier.Tests/` verifiziert:
- `OAuthAuthorize.razor` — Consent-Form, Client-Info, Approve/Deny-Buttons, `data-testid`-Attribute
- `ConnectedClients.razor` — Token-Liste, Revoke-Button, „Alle widerrufen"
- `UserMenu.razor` — „Verbundene Clients"-Eintrag

Post-Deploy-Verifikation durch den User:
- Claude Desktop Custom Connector → OAuth-Flow
- Browser-Login → Consent-Page → Approve → MCP-Token
- `/account/connected-clients` → Client sichtbar → Revoke

### 3. Well-Known Endpoints (Pre-Deploy)

Nicht in Production — durch Integrations-Tests in `OAuthEndpointTests.cs` verifiziert:
- `/.well-known/oauth-authorization-server` → RFC 8414 JSON ✅ (Test grün)
- `/.well-known/oauth-protected-resource` → MCP-Resource JSON ✅ (Test grün)

## Befunde

0 findings

**Begründung:** Kein Regression in bestehender UI gefunden. OAuth-UI durch bUnit-Tests (grün) abgedeckt. Post-Deploy-Verifikation gemäß Plan dem User zugewiesen.
