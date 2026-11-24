# Mini-Lauf nach jedem Deploy (Smoke)

Stand: 2026-11-24 · Skript: `scripts/atelier-smoke.sh` · Tests: `cli-proxy/tests/test_atelier_smoke_script.py`

## Was er macht

1. `GET /health` der Web-App lokal im Container (`docker exec geef-atelier-web curl http://localhost:8080/health`).
2. Anmeldung mit einem eigenen **Smoke-Konto**: Login-Formular (Cookie), danach OAuth-Freigabe mit PKCE für einen eigens registrierten Smoke-Client und Tausch gegen ein Zugriffstoken. Der MCP-Endpunkt nimmt nur Bearer-Token an; deshalb geht der Weg über OAuth. Jeder Lauf holt sich frische Token aus Benutzername und Passwort; es gibt kein gespeichertes Refresh-Token, das veralten oder durch Rotation ungültig werden könnte.
3. Auftrag über den MCP-Endpunkt `/mcp`, Werkzeug `submit_request`: ein Ein-Satz-Briefing mit dem Klassik-Template.
4. Warten auf das Ende über `get_run_status` (Standard höchstens 1.500 Sekunden).
5. Prüfung über `get_run_result`: Status `Completed`, Text vorhanden, keine Fehlermeldung.
6. Genau eine Ergebniszeile auf stdout, dazu der Exit-Code.

| Exit-Code | Bedeutung |
|---|---|
| 0 | bestanden |
| 1 | Lauf gescheitert (Status, kein Text, Fehlermeldung) oder Zeitlimit überschritten |
| 2 | Smoke-Konto nicht eingerichtet, unvollständig oder unzulässig konfiguriert, zum Beispiel `http://` zu einem nicht lokalen Host (nur `/health` geprüft) |
| 3 | `/health`, Verbindung, Anmeldung oder MCP-Aufruf gescheitert |
| 4 | Unerwartete Antwort des Ateliers: HTTP 200, aber nicht das vereinbarte Format (zum Beispiel ein JSON-Array statt eines Objekts vom Token-Endpunkt, eine SSE-Datenzeile, die kein Objekt ist, oder ein `finalText`, der kein Text ist); ebenso ein unerwarteter interner Fehler |

Das Skript gibt nie Passwort, Cookie oder Token aus, und es endet immer mit genau einer Ergebniszeile, nie mit einem Traceback.

**Kein Passwort im Klartext:** `ATELIER_SMOKE_BASE_URL` muss `https://` sein. `http://` ist nur für die ausdrücklich gelisteten lokalen Hosts erlaubt: `localhost`, `127.0.0.1`, `::1` und `geef-atelier-web` (Container-Name im Docker-Netz). Für jeden anderen Host bricht das Skript vor der Anmeldung mit `SMOKE FEHLER (konfiguration)` und Exit 2 ab. Weiterleitungen folgt das Skript nie.

**Grenze:** Über `submit_request` lässt sich für ein Template keine Iterationsgrenze setzen (die Grenze gibt es nur in einer eigenen Crew-Spezifikation). Bremsen sind deshalb das sehr kurze Briefing und das Zeitlimit. Endet der Klassik-Lauf ohne Konvergenz als `Failed`, schlägt der Smoke fehl; das ist gewollt, weil genau das ein Anwender auch sähe.

## Smoke-Konto einrichten (einmalig, von Hand)

Das Skript legt nichts an. Die Einrichtung macht ein Admin in der Oberfläche:

1. **Benutzer anlegen:** `/admin/users` → neuer Benutzer, zum Beispiel `smoke`, **kein** Admin, langes Zufallspasswort (`openssl rand -base64 32`).
2. **OAuth-Client registrieren:** `/admin/oauth-clients` → „Register“, Name `atelier-smoke`, Redirect-URI genau `http://127.0.0.1/atelier-smoke/callback`. Die angezeigte Client-ID notieren. Die Redirect-URI wird nie aufgerufen; das Skript liest nur den Code aus der Weiterleitung.
3. **Datei anlegen** (gitignored über das Muster `*.env` und zusätzlich ausdrücklich):

   ```bash
   cd /srv/docker/websites/geef_atelier
   install -m 600 /dev/null .smoke.env
   ${EDITOR:-nano} .smoke.env
   ```

   Inhalt:

   ```ini
   ATELIER_SMOKE_USER=smoke
   ATELIER_SMOKE_PASSWORD=<Passwort aus Schritt 1>
   ATELIER_SMOKE_CLIENT_ID=<Client-ID aus Schritt 2>
   # optional:
   # ATELIER_SMOKE_BASE_URL=https://geef.stefan-bechtel.de
   # ATELIER_SMOKE_REDIRECT_URI=http://127.0.0.1/atelier-smoke/callback
   # ATELIER_SMOKE_TEMPLATE=klassik
   # ATELIER_SMOKE_TIMEOUT=1500
   # ATELIER_SMOKE_POLL=10
   ```

   Die Datei wird nicht als Shell-Skript ausgeführt, sondern zeilenweise als `SCHLÜSSEL=Wert` gelesen. Modus muss `600` (oder `400`) sein, sonst bricht das Skript ab.

Ein anderer Speicherort geht über `ATELIER_SMOKE_ENV=/pfad/zur/datei`.

## Aufruf

```bash
scripts/atelier-smoke.sh                         # nach jedem Deploy (erst cli-proxy, dann web)
ATELIER_SMOKE_VERBOSE=1 scripts/atelier-smoke.sh # Zwischenschritte auf stderr
```

Beispiele für die Ergebniszeile:

```text
SMOKE OK: Lauf 3f0c… Completed in 142 s, 87 Zeichen Text (klassik)
SMOKE NICHT EINGERICHTET: Smoke-Konto fehlt (/srv/docker/websites/geef_atelier/.smoke.env nicht vorhanden). …
SMOKE FEHLER (lauf): Lauf 3f0c… nach 1520 s: Status Failed; Fehlermeldung: …
SMOKE FEHLER (konfiguration): ATELIER_SMOKE_BASE_URL nutzt unverschlüsseltes http:// für den Host geef.example.org; …
SMOKE FEHLER (antwort): Antwort von /oauth/token ist kein JSON-Objekt, sondern list
```

Die Läufe des Smoke-Kontos erscheinen in der Lauf-Liste der Admins; sie dürfen dort bleiben oder von Hand gelöscht werden.

## Tests

```bash
cd cli-proxy && python3 -m pytest -q tests/test_atelier_smoke_script.py
```

Die Tests laufen offline: Ein kleiner HTTP-Testserver spielt Login, OAuth-Freigabe, Token und MCP; `docker` ist eine Attrappe. Geprüft werden der erfolgreiche Durchlauf, alle Fehlerpfade (kein Konto, falscher Dateimodus, fehlende Schlüssel, `/health`, Verbindung, falsches Passwort, falscher Client, falscher `state`, gescheiterter Lauf, Zeitlimit, `http://` zu einem nicht gelisteten Host ohne jede Anfrage an ihn, unerwartete Antworten von Token-Endpunkt und MCP) und dass keine Geheimnisse in der Ausgabe stehen.
