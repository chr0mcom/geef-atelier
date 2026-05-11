# Claude-Code-Prompt: Schritt 10 — Production-Deploy mit Traefik und Domain

*Diese Datei ist als Eingabe für Claude Code gedacht. Letzter Walking-Skeleton-Schritt.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Schritte 1–9 + M1 sind abgeschlossen: das System hat UI über Cookie-Auth, MCP-Server über Bearer-Auth, OpenRouter als LLM-Provider, 85 Tests grün, und der Container läuft bereits auf `95.216.100.213:8080`. Deine Aufgabe ist **Schritt 10 von 10**: **Production-Deploy mit Traefik-Routing und Domain `geef.stefan-bechtel.de`**.

Was sich ändert: Eine `docker-compose.yml` (oder Ergänzung der bestehenden) mit Traefik-Labels. Cookie-`SecurePolicy.Always` wird aktiviert. Optional Dockerfile-Hardening (Non-root-User, HEALTHCHECK). README-Update für Production-Setup. Eine `.env`-Datei mit Production-Secrets, die du selbst aus Shell-Env-Vars + `openssl`/`tools/HashPassword` generierst. Was bleibt unverändert: jeglicher Anwendungs-Code, alle 85 Tests, Domain-Modell, Provider-Schichten.

Dies ist ein **Konfigurations-Schritt**, kein Code-Schritt. Der schwerste Eingriff in Anwendungs-Code ist ein einziger Boolean-Switch für Cookie-`SecurePolicy`. Alles andere ist Docker und Setup-Doku.

**Das ist der letzte Walking-Skeleton-Schritt.** Nach erfolgreichem Schritt 10 ist das System produktiv erreichbar unter `https://geef.stefan-bechtel.de/` und für Post-Skeleton-Erweiterungen (Cost-Tracking, RAG, Multi-User, Domänen-Spezialisierung etc.) bereit.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors, alle Hard Rules.

**Phase-1.4-Hinweis:** Plan-Phase-Integration ist Standard. Hier besonders wichtig: Architect muss die **existierende Traefik-Server-Konvention** verifizieren, nicht nur die theoretisch idiomatische Form. Lies dafür `/srv/CLAUDE.md` und ggf. `/srv/docker/docs/`-Inhalte oder bestehende `docker-compose.yml`-Dateien anderer Services auf dem Server.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

Vor Beginn der Implementierung lies in dieser Reihenfolge:

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`/srv/CLAUDE.md`** und (falls existent) **`/srv/docker/docs/docker-deployment.md`** — **kritisch**, weil die existierende Traefik-Konfiguration auf dem Server konventionsgebunden ist. Welche Netzwerke (`web`, `traefik_default`, etc.)? Welche Entry-Points (`websecure`, `web`)? Welcher Cert-Resolver (`letsencrypt`, oder anderer Name)? Welche Middleware (z.B. `secured-headers@file`)?
3. **`CLAUDE.md`** im Repo-Root.
4. **`docs/02-architecture.md`** — besonders Deployment-Sektion (falls vorhanden) oder die Auth-Sektion, die Cookie-`SecurePolicy` thematisiert.
5. **`docs/03-walking-skeleton-plan.md`**, Abschnitt "Schritt 10".
6. **`docs/05-decisions-log.md`**, alle Einträge **D-010 bis D-022**. Besonders **D-014** (Production-Domain festgelegt) und **D-022** (Schritt-9-Realfakten, MCP-Endpoint).
7. **`docs/reports/step-09-report.md`**, besonders **Sektion 8 (Empfehlungen für Schritt 10)** — der Schritt-9-Executor hat dort eine fast komplette Spezifikation hinterlassen: Traefik-Labels, Env-Var-Liste, Migration-Strategie, SignalR-Sticky-Sessions-Hinweis.
8. **Aktueller Code im Repo:**
   - `src/Geef.Atelier.Web/Program.cs` — Cookie-`SecurePolicy` Switch-Stelle aus Schritt 8.
   - `docker-compose.yml` (falls existiert) bzw. `docker-compose.dev.yml` aus Schritt 8 — als Vorlage.
   - `Dockerfile` — Production-Hardening-Stelle.
   - `appsettings.json` und ggf. `appsettings.Development.json` — Verständnis der bestehenden Config-Hierarchie.
   - `tools/HashPassword/` aus Schritt 8 — wird für `ATELIER_PASSWORD_HASH`-Generierung benutzt.
9. **Traefik v2/v3 Doku** (je nach Server-Version): Router-Rules, TLS-Resolver, Middleware-Chain, WebSocket-Upgrade-Handling. Besonders die Konfiguration für SignalR-`/hubs/runs`.

## In Schritten 1–9 etablierte Realfakten (verbindlich)

Aus D-010 bis D-022. Zentrale Punkte für Schritt 10:

**Auth-Schichten (aus Schritt 8 + 9):**
- Cookie-Auth für UI mit `SameSite=Strict` und `Secure=Always` als geplante Production-Werte (in Schritt 8 mit `SameAsRequest` in Dev konfiguriert — Schritt 10 aktiviert `Always`).
- Bearer-Auth für MCP via `Authorization: Bearer <token>`-Header. Token aus `ATELIER_MCP_TOKEN`.
- `ForwardedHeaders`-Middleware bereits in Schritt 8 konfiguriert (Reverse-Proxy-aware).
- `[Authorize]`-Pages, `RunHub` **ohne** `[Authorize]` (Blazor Server-Constraint, siehe D-021).

**Domain-Konfiguration (aus D-014):**
- Production-Domain: **`geef.stefan-bechtel.de`**
- Server-IP: **`95.216.100.213`** (DNS A-Record bereits gesetzt)
- Reverse-Proxy: **Traefik** (auf Server bereits aktiv)

**Aktueller App-Stand:**
- Container läuft auf `95.216.100.213:8080` (direkt erreichbar, kein Traefik-Routing) — verifiziert in Schritt 8 R5.
- Migration `Step09AuditTrail` zuletzt angewendet. Auto-Migration beim Startup.
- `Llm__ApiKey` als Env-Var konfiguriert (OpenRouter-Bearer-Key).

**MCP-Endpoint:**
- `POST /mcp` mit Bearer-Auth.
- `Accept: application/json, text/event-stream` ist Pflicht (Streamable HTTP-Transport).
- Traefik darf den Accept-Header nicht filtern oder umschreiben.

**SignalR:**
- `/hubs/runs` als WebSocket-Endpoint.
- **Wichtig:** WebSocket-Upgrade muss durch Traefik unterstützt werden. Bei vielen Traefik-Konfigurationen ist das Default-Verhalten, aber explizite `middlewares=ws-headers`-Verkettung kann nötig sein.

## Konkrete technische Anforderungen für Schritt 10

### 1. `Program.cs` — Cookie-`SecurePolicy.Always` in Production

Aus Schritt 8 vorbereitet: die Cookie-Konfiguration unterscheidet zwischen Development (`SameAsRequest`) und Production (`Always`) via `IHostEnvironment.IsDevelopment()`. In Schritt 10 verifizieren, dass diese Logik existiert und korrekt ist. Falls nicht: einbauen.

```csharp
.AddCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/auth/logout";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Name = "Atelier.Auth";
});
```

In Production läuft `ASPNETCORE_ENVIRONMENT=Production` (Default beim Image-Build), also gilt `Always`. Wird `Always` aktiviert ohne HTTPS, schickt der Browser die Cookies einfach nicht — was im Falle eines Konfigurations-Fehlers ein klares Symptom liefert.

### 2. `Dockerfile`-Hardening (falls noch nicht in Schritt 1)

Prüfen, ob das bestehende Dockerfile bereits Non-root-User und HEALTHCHECK enthält. Falls nicht: ergänzen.

```dockerfile
# Non-root-User
RUN groupadd --gid 1000 atelier && \
    useradd --uid 1000 --gid atelier --shell /bin/bash --create-home atelier
USER atelier

# Health-Check
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1
```

Falls Dockerfile-Patterns aus dem Server-Setup (`/srv/CLAUDE.md`) eine andere Konvention vorgeben (z.B. spezifische UID/GID): die Server-Konvention gewinnt.

### 3. `docker-compose.yml` (oder Erweiterung) für Production

Architect entscheidet, ob `docker-compose.yml` oder `docker-compose.prod.yml`. Empfehlung: `docker-compose.yml` als Production-Form, `docker-compose.dev.yml` als Override für lokale Entwicklung.

Minimal-Struktur für Production (Werte via `${VAR_NAME}` aus der `.env`-Datei, die du in Sektion 4 selbst generierst):

```yaml
services:
  geef-atelier:
    image: ghcr.io/chr0mcom/geef-atelier:latest  # oder lokaler Build
    container_name: geef-atelier
    restart: unless-stopped
    networks:
      - traefik  # oder Server-Convention-Name
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - ConnectionStrings__DefaultConnection=Host=postgres;Database=geef_atelier;Username=geef;Password=${POSTGRES_PASSWORD}
      - ATELIER_USER=admin
      - ATELIER_PASSWORD_HASH=${ATELIER_PASSWORD_HASH}
      - ATELIER_MCP_TOKEN=${ATELIER_MCP_TOKEN}
      - Llm__ApiKey=${LLM_API_KEY}
    depends_on:
      - postgres
    labels:
      - "traefik.enable=true"
      - "traefik.docker.network=traefik"

      # Hauptrouter für UI und MCP
      - "traefik.http.routers.geef-atelier.rule=Host(`geef.stefan-bechtel.de`)"
      - "traefik.http.routers.geef-atelier.entrypoints=websecure"
      - "traefik.http.routers.geef-atelier.tls.certresolver=letsencrypt"
      - "traefik.http.services.geef-atelier.loadbalancer.server.port=8080"

      # WebSocket-Upgrade-Header für SignalR
      - "traefik.http.middlewares.geef-headers.headers.customRequestHeaders.X-Forwarded-Proto=https"
      # Falls Server-Setup eine ws-Middleware definiert, hier verketten

      # HTTP→HTTPS-Redirect (falls Server-Setup das nicht global macht)
      - "traefik.http.routers.geef-atelier-http.rule=Host(`geef.stefan-bechtel.de`)"
      - "traefik.http.routers.geef-atelier-http.entrypoints=web"
      - "traefik.http.routers.geef-atelier-http.middlewares=https-redirect@file"

  postgres:
    image: postgres:16-alpine
    container_name: geef-atelier-postgres
    restart: unless-stopped
    networks:
      - traefik
    environment:
      - POSTGRES_DB=geef_atelier
      - POSTGRES_USER=geef
      - POSTGRES_PASSWORD=${POSTGRES_PASSWORD}
    volumes:
      - geef-atelier-pgdata:/var/lib/postgresql/data

volumes:
  geef-atelier-pgdata:

networks:
  traefik:
    external: true  # auf dem Server bereits angelegt
```

**Architect-Aufgabe:** Die `networks:traefik:external: true`-Form prüfen. Manche Server-Setups nutzen `traefik_default` oder einen anderen Namen. Cert-Resolver-Name (`letsencrypt`) prüfen. Middleware-Namen (z.B. `https-redirect@file`) prüfen.

**Port-Exposure:** Direkt-Port 8080 von außen wird **entfernt** (kein `ports:`-Block am `geef-atelier`-Service). Traefik routet von 443 → internen Port 8080.

### 4. Production-Secret-Generierung (von dir, Claude Code, automatisiert)

Du erhältst zwei Werte als Shell-Environment-Variablen, die der Maintainer vor dem Start dieser Subagent-Session exportiert hat:

- **`ATELIER_UI_PASSWORD_CLEAR`** — das gewünschte UI-Login-Passwort im Klartext. Dieser Wert ist nur für die einmalige Hash-Generierung gedacht und darf **niemals** ins Repo, in Logs, oder in den Bericht.
- **`GEEF_OPENROUTER_KEY`** — der OpenRouter-Bearer-Key (Format `sk-or-v1-...`). Dieser Wert geht in die `.env`-Datei als `LLM_API_KEY`, aber nicht in Source-Control oder den Bericht.

**Erste Verifikation am Beginn:**
```bash
if [ -z "$ATELIER_UI_PASSWORD_CLEAR" ] || [ -z "$GEEF_OPENROUTER_KEY" ]; then
    echo "ERROR: Production-Secret-Vars fehlen. Maintainer muss vor Subagent-Start beide exportieren."
    exit 1
fi
```

Falls eine oder beide Variablen nicht gesetzt sind, halt-and-escalate an den Maintainer mit klarer Anweisung welche Variable fehlt.

**Du generierst selbst:**

```bash
# ATELIER_MCP_TOKEN — zufälliger 64-stelliger Hex-Token (256 Bit)
ATELIER_MCP_TOKEN=$(openssl rand -hex 32)

# POSTGRES_PASSWORD — zufälliges Base64-Passwort (32 Zeichen)
POSTGRES_PASSWORD=$(openssl rand -base64 24)

# ATELIER_PASSWORD_HASH — BCrypt-Hash via tools/HashPassword
# (vorher dotnet build, damit --no-build sicher ist)
dotnet build tools/HashPassword -c Release --nologo --verbosity quiet
ATELIER_PASSWORD_HASH=$(dotnet run --project tools/HashPassword -c Release --no-build -- "$ATELIER_UI_PASSWORD_CLEAR" 2>/dev/null | tail -1)
```

**`.env`-Datei schreiben** (im Repo-Root, dieselbe Ebene wie `docker-compose.yml`):

```env
# Production-Secrets — NICHT in Git committen!
# Generiert von Schritt 10 (Claude Code), Datum: <ISO-Date>
POSTGRES_PASSWORD=<generierter Wert>
ATELIER_USER=admin
ATELIER_PASSWORD_HASH=<generierter BCrypt-Hash>
ATELIER_MCP_TOKEN=<generierter Token>
LLM_API_KEY=<aus GEEF_OPENROUTER_KEY übernommen>
```

**`.gitignore` prüfen und ergänzen:**

```bash
if ! grep -q "^\.env$" .gitignore 2>/dev/null; then
    echo "" >> .gitignore
    echo "# Production-Secrets (Schritt 10)" >> .gitignore
    echo ".env" >> .gitignore
    echo ".env.*" >> .gitignore
    echo "!.env.example" >> .gitignore  # falls später Beispiel-Datei kommt
fi
```

**Verifikation am Ende:**
- `git status .env` zeigt "ignored" oder die Datei gar nicht erst auf.
- `cat .env | wc -l` zeigt mindestens 5 nicht-leere Zeilen.
- `cat .env | grep -c "="` zeigt mindestens 5 (Schlüssel-Wert-Paare).

**Speicher-Hygiene nach Abschluss:**
- `unset ATELIER_UI_PASSWORD_CLEAR` am Ende der Subagent-Session.
- Niemals den Klartext-Passwort-Wert in Bash-History, Logs, oder Reports loggen.
- Im Bericht sind Token-Längen erwähnbar (z.B. "64-stelliger Hex-Token"), nicht aber die Werte selbst.

**Im Abschlussbericht dokumentieren** (Bericht-Sektion 1 "Was wurde umgesetzt"):
> Production-Secrets generiert via `openssl rand` und `tools/HashPassword`. `.env`-Datei angelegt mit fünf Schlüssel-Wert-Paaren, in `.gitignore` registriert. Klartext-Werte: nicht in dieser Datei dokumentiert (Sicherheits-Disziplin).

### 5. README-Update für Production-Setup

Ergänze README-Sektion "Production-Deployment":
- Voraussetzungen: Traefik läuft auf dem Server, DNS für `geef.stefan-bechtel.de` zeigt auf Server.
- Setup-Schritte für Re-Deploy oder Neu-Aufsatz: `.env`-File anlegen (mit `openssl rand`-Beispielen und Hash-Generation-Anleitung), `docker compose up -d`, Migration läuft automatisch beim Startup, Health-Check via `https://geef.stefan-bechtel.de/health` prüfen.
- Token-Generation-Beispiele.
- Hash-Generation via `tools/HashPassword`.
- Hinweis: `.env` ist gitignored, neue Maintainer müssen sich die Werte selbst regenerieren oder vom Vorgänger erhalten.

### 6. Verifikation der Live-Domain

**R5 (Playwright) muss diesmal echte Production-Verifikation leisten** — nicht gegen Test-Server, sondern gegen `https://geef.stefan-bechtel.de/`. Fünf Punkte:

1. **HTTPS-Reachability:** `curl -I https://geef.stefan-bechtel.de/` liefert 200 oder 302 (zu `/login`).
2. **TLS-Zertifikat gültig:** Browser zeigt kein Cert-Warning. `openssl s_client -connect geef.stefan-bechtel.de:443 -servername geef.stefan-bechtel.de` zeigt valide Cert-Chain.
3. **Cookie-Auth über HTTPS:** Login mit echten Credentials → Cookie wird gesetzt (mit `Secure`-Flag), nachfolgende Requests sind authentifiziert.
4. **SignalR über Domain:** Submit-Flow auf `/new`, Live-Update auf Detail-Page erfolgt ohne Reload (WebSocket-Upgrade funktioniert über Traefik).
5. **MCP über Domain:** `curl -X POST https://geef.stefan-bechtel.de/mcp -H "Authorization: Bearer <token>" -H "Accept: application/json, text/event-stream" -H "Content-Type: application/json" -d '{"jsonrpc":"2.0","id":1,"method":"tools/list"}'` liefert Tool-Liste.

### 7. Migration auf Production-DB

Auto-Migration beim Startup ist die etablierte Strategie (D-010). In Schritt 10 verifizieren:
- Container-Start zeigt Migration-Log: alle bisherigen Migrations als `[Applied]`, neue keine.
- `dotnet ef migrations list --context AtelierDbContext` gegen die Production-DB ist konsistent.

Falls Production-DB noch leer ist (frischer Postgres-Container): Migration legt das komplette Schema an.

Falls Production-DB Daten enthält (z.B. von Tests auf `95.216.100.213:8080`): Migration ist additiv (`Step09AuditTrail` ist nullable Spalte), keine Daten-Probleme.

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` ohne Fehler oder Warnings.
2. `dotnet test` — alle 85 bestehenden Tests grün. Es kommen keine neuen Tests dazu, weil Schritt 10 nur Konfiguration ist.
3. **`.env`-Datei vorhanden und `.gitignore`-geschützt** — `git status` zeigt sie nicht als zu committen an. `.env` enthält alle fünf Secret-Felder (`POSTGRES_PASSWORD`, `ATELIER_USER`, `ATELIER_PASSWORD_HASH`, `ATELIER_MCP_TOKEN`, `LLM_API_KEY`) mit nicht-leeren Werten. Verifikation ohne die Werte zu loggen.
4. **`docker compose up -d` startet den Stack** ohne Fehler.
5. **`https://geef.stefan-bechtel.de/` lädt die Welcome-Page** mit gültigem TLS-Zertifikat (kein Browser-Warning).
6. **Cookie-Auth-Flow über HTTPS funktioniert** — Login mit Production-Credentials, Redirect-Schutz auf geschützten Pages, Logout.
7. **SignalR-Live-Updates funktionieren über die Domain** — Submit auf `/new`, Status-Übergang Pending→Running→Completed ohne manuellen Reload auf `/runs/{id}`.
8. **MCP-Endpoint erreichbar:** `curl` mit gültigem Bearer-Token gegen `https://geef.stefan-bechtel.de/mcp` liefert die Tool-Liste.
9. **Health-Check erreichbar:** `https://geef.stefan-bechtel.de/health` liefert 200 (Traefik-Health-Detection funktioniert).
10. **Direkt-Port-Exposure entfernt:** `curl http://95.216.100.213:8080/` ist nicht mehr erreichbar (entweder Connection-Refused oder Filter via Firewall).
11. **README ist aktuell** — Production-Deployment-Sektion beschreibt den vollständigen Setup-Pfad.

## Was du in diesem Schritt NICHT tust

- **Keine neuen Features** — kein Code, der nicht Konfiguration ist.
- **Keine neuen Tests** — die 85 bestehenden müssen weiter grün sein. Production-Verifikation läuft via R5/curl, nicht via xUnit.
- **Keine Backup-Implementation** — Postgres-Backup ist Post-Skeleton (oder per externem Cron/Tool).
- **Keine Monitoring-Tools** — Grafana, Prometheus etc. sind Post-Skeleton.
- **Keine CI/CD-Pipeline** — manueller `docker compose up -d` reicht für Skeleton-Production. GitHub Actions etc. Post-Skeleton.
- **Keine Secrets-Rotation-Automatik** — Token/Hash via Env-Vars, Rotation manuell.
- **Keine Multi-User-Erweiterung** — Single-User bleibt.
- **Keine Cost-Tracking-Aktivierung** — `RunEntity.CostTotal` bleibt 0.
- **Keine LiveUpdateFlowTests-Stabilisierung** — die in Schritt 9 identifizierten gelegentlichen Timeouts sind Tech-Debt, nicht Schritt-10-Material.

## Architect-Konsultation (Phase 1.4) — sechs Schwerpunkte

1. **Traefik-Konvention auf dem Zielserver:** Welche Netzwerk-Namen, Entry-Points, Cert-Resolver, Middleware existieren bereits? `/srv/CLAUDE.md` oder ähnliche Server-Doku lesen. Ohne Übereinstimmung mit Server-Konvention scheitert das Routing.
2. **WebSocket-Upgrade für SignalR:** Standard-Traefik unterstützt WebSocket-Upgrade automatisch, aber explizite Middleware kann nötig sein. Wie sind andere Apps auf dem Server konfiguriert, die WebSocket nutzen?
3. **Secrets-Strategie:** `.env`-File (geplant, in Sektion 4 oben), Docker-Secrets (sauberer), oder direkte Env-Vars beim `docker compose up`? Pro/Contra für Single-User-Skeleton.
4. **Migration-Strategie revisited:** Bleibt Auto-on-Startup (D-010), oder Init-Container/separater Migration-Run für Production? Bisher hat Auto-on-Startup gut funktioniert; Init-Container wäre nur sinnvoll bei großen Schemas mit langer Migration-Zeit (Atelier hat das nicht).
5. **Image-Source:** Lokaler Build via `docker build` und Image-Push zu `ghcr.io/chr0mcom/geef-atelier`, oder direkter `image: build:` mit Dockerfile-Pfad in der Compose-Datei? Erstes ist deployment-fähiger, zweites ist einfacher.
6. **Production-Cookie-Settings finalisieren:** `SecurePolicy.Always`, `SameSite.Strict`, `HttpOnly.true`, `ExpireTimeSpan = 30d`. Plus: muss `Cookie.Domain` explizit auf `geef.stefan-bechtel.de` gesetzt werden, oder reicht der Default (auto-detect)?

`geef_architecture.md` prüft Konsistenz mit der Server-Konfiguration und mit den D-010 bis D-022-Realfakten.

## Persistenter Abschlussbericht für den Brainstorming-Chat

Bericht nach `docs/reports/step-10-report.md`, gleicher Aufbau wie Schritte 1–9. Wichtig in diesem Schritt:

1. **Was wurde umgesetzt** — Datei-für-Datei. Vor allem: finale `docker-compose.yml`, Traefik-Labels, etwaige Dockerfile-Änderungen, README-Updates, `.env`-Generierung (ohne Werte).
2. **Annahmen und Abweichungen** — vor allem zu Server-Traefik-Konvention vs. Vermutung im Plan, etwaigen WebSocket-Konfigurations-Subtilitäten, finalen Cookie-Settings.
3. **Architect-Output** — alle sechs Schwerpunkte als Plan-Phase-Output, mit konkreter Server-Konvention-Verifikation.
4. **Pre-Mortem & Devil's Advocate** — speziell zu: Cert-Resolver-Versagen (Let's-Encrypt Rate Limit, DNS-Validierung), WebSocket-Upgrade-Probleme, Migration-Failure beim ersten Start, Cookie-Domain-Konflikte.
5. **Reviewer-Iterationen** — Tabelle. R5 ist diesmal **Live-Production-Verifikation** auf `https://geef.stefan-bechtel.de/`, nicht gegen Test-Server.
6. **Akzeptanzkriterien-Check** — Tabelle, mit besonderem Augenmerk auf AC3 (`.env` korrekt), AC5 (HTTPS reachability), AC8 (MCP-Endpoint live).
7. **Beobachtungen zur Production-Umgebung** — Latenz, TLS-Setup-Geschwindigkeit, ggf. erste Real-Pipeline-Runs gegen OpenRouter über die Production-URL.
8. **Walking-Skeleton-Retrospektive** — kurze Reflexion: Welche Schritte waren am wertvollsten? Welche Architektur-Entscheidungen haben sich bewährt? Welche Tech-Debt bleibt offen für Post-Skeleton (LiveUpdateFlowTests, Backup, Monitoring, Cost-Tracking, Multi-User, RAG, Domänen-Spezialisierung)?
9. **Empfehlungen für Post-Skeleton-Roadmap** — knappe Liste der naheliegenden nächsten Brocken, in Reihenfolge der Wichtigkeit.

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- **Niemals Secrets** (DB-Password, `LLM_API_KEY`, `ATELIER_PASSWORD_HASH`, `ATELIER_MCP_TOKEN`, **insbesondere `ATELIER_UI_PASSWORD_CLEAR`**) **in source control**, niemals in Logs, niemals im Bericht.
- `.env`-Files mit Secrets niemals committen — `.gitignore` muss korrekt konfiguriert sein.
- Im Bericht dürfen Token-Längen erwähnt werden (z.B. "64-stelliger Hex-Token"), nicht aber Token-Werte.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.

Nach erfolgreichem Abschluss: **Walking Skeleton komplett.**