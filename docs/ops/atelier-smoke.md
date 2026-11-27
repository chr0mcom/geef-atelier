# Mini-Lauf nach jedem Deploy (Smoke)

Stand: 2026-11-27 · Skript: `scripts/atelier-smoke.sh` · Tests: `cli-proxy/tests/test_atelier_smoke_script.py`

Diese Anleitung beschreibt die Bedienung, nicht einen bestimmten Server. Werte in spitzen Klammern
(`<smoke-user>`, `<client-name>`, `<repo>`) wählt der Betreiber selbst; Standardwerte stehen im Kopf des
Skripts und lassen sich per Umgebung überschreiben.

## Was er macht

1. `GET /health` der Web-App lokal im Container (`docker exec $WEB_CONTAINER curl http://localhost:8080/health`; Standardname des Containers im Skript).
2. Anmeldung mit einem eigenen **Smoke-Konto**: Login-Formular (Cookie), danach OAuth-Freigabe mit PKCE für einen eigens registrierten Smoke-Client (das Skript liest die Consent-Seite und sendet ihr Formular samt Antiforgery-Feld ab; ein Formular ohne das Feld, wie in älteren Fassungen, geht ohne dieses ab, damit die Rauchprobe auch nach einem Rückweg läuft) und Tausch gegen ein Zugriffstoken. Der MCP-Endpunkt nimmt nur Bearer-Token an; deshalb geht der Weg über OAuth. Jeder Lauf holt sich frische Token aus Benutzername und Passwort; es gibt kein gespeichertes Refresh-Token, das veralten oder durch Rotation ungültig werden könnte.
3. Auftrag über den MCP-Endpunkt `/mcp`, Werkzeug `submit_order` (Standardweg, wie ihn jeder KI-Assistent nutzt): ein Ein-Satz-Auftrag. Antwortet Atelier mit Rückfragen (`state` `needs_input`), überspringt das Skript sie einmal mit `answer_order(skip=true)`; Atelier schreibt dann mit seinen Annahmen.
4. Warten über `get_order_status` mit `waitSeconds` (Standard 25, höchstens 50), bis **`done` und `outcome` gesetzt** sind (Standard höchstens 1.500 Sekunden). Erst mit `outcome` ist der Text endgültig.
5. Erst danach Prüfung über `get_order_result`: `outcome` `Converged` oder `DeliveredWithOpenPoints` **und** Text vorhanden ist bestanden; `outcome` `Failed` oder `Aborted` oder ein fehlender Text ist gescheitert.
6. Genau eine Ergebniszeile auf stdout, dazu der Exit-Code.

| Exit-Code | Bedeutung |
|---|---|
| 0 | bestanden (`Converged`, oder `DeliveredWithOpenPoints` mit dem Hinweis „offene Punkte“ in der Ergebniszeile) |
| 1 | Auftrag gescheitert (`outcome` `Failed` oder `Aborted`, kein Text) oder Zeitlimit überschritten; beim Zeitlimit sagt die Zeile, ob der Auftrag noch arbeitete oder schon fertig war, aber noch kein `outcome` hatte |
| 2 | Smoke-Konto nicht eingerichtet, unvollständig oder unzulässig konfiguriert, zum Beispiel `http://` zu einem nicht lokalen Host (nur `/health` geprüft) |
| 3 | `/health`, Verbindung, Anmeldung oder MCP-Aufruf gescheitert |
| 4 | Unerwartete Antwort des Ateliers: HTTP 200, aber nicht das vereinbarte Format (zum Beispiel ein JSON-Array statt eines Objekts vom Token-Endpunkt, eine SSE-Datenzeile, die kein Objekt ist, ein `text`, der kein Text ist, oder ein unbekannter `outcome`-Wert); ebenso ein unerwarteter interner Fehler |

Das Skript gibt nie Passwort, Cookie oder Token aus, und es endet immer mit genau einer Ergebniszeile, nie mit einem Traceback. Es braucht ein Atelier ab Stufe 8d (D-091); ältere Fassungen nutzten `submit_request` mit dem Klassik-Template, das es nicht mehr gibt.

**Kein Passwort im Klartext:** `ATELIER_SMOKE_BASE_URL` muss `https://` sein. `http://` ist nur für die im Skript in `ALLOWED_HTTP_HOSTS` gelisteten lokalen Hosts erlaubt (`localhost`, `127.0.0.1`, `::1` und der Name des Web-Containers im Docker-Netz). Für jeden anderen Host bricht das Skript vor der Anmeldung mit `SMOKE FEHLER (konfiguration)` und Exit 2 ab. Weiterleitungen folgt das Skript nie.

**Grenze:** Der Mini-Lauf geht den Standardweg mit allen Schritten, die für das Smoke-Konto eingeschaltet sind (Klärer, Schreiber, Faktenprüfung; bei eingeschalteter Recherche auch die Suche). Bremsen sind der sehr kurze Auftrag und das Zeitlimit. Endet der Auftrag mit Text und offenen Punkten (`DeliveredWithOpenPoints`), besteht der Smoke mit dem Hinweis „offene Punkte“; endet er ohne brauchbaren Text, schlägt er fehl.

## Smoke-Konto einrichten (einmalig, von Hand)

Das Skript legt nichts an. Die Einrichtung macht ein Admin in der Oberfläche:

1. **Benutzer anlegen:** `/admin/users` → neuer Benutzer `<smoke-user>`, **kein** Admin, langes Zufallspasswort (`openssl rand -base64 32`). Den Namen frei wählen und nicht öffentlich dokumentieren.
2. **OAuth-Client registrieren:** `/admin/oauth-clients` → „Register“, Name `<client-name>`, Redirect-URI genau der Wert, den das Skript verwendet (Standard von `ATELIER_SMOKE_REDIRECT_URI` im Skript, eine Loopback-Adresse; wer eine andere einträgt, setzt `ATELIER_SMOKE_REDIRECT_URI` in der Datei unten gleich). Die angezeigte Client-ID notieren. Die Redirect-URI wird nie aufgerufen; das Skript liest nur den Code aus der Weiterleitung.
3. **Datei anlegen** an dem Ort, den `$ATELIER_SMOKE_ENV` nennt (Standard im Skript: `.smoke.env` im Repository-Wurzelverzeichnis; gitignored über das Muster `*.env` und zusätzlich ausdrücklich):

   ```bash
   install -m 600 /dev/null "$ATELIER_SMOKE_ENV"
   ${EDITOR:-nano} "$ATELIER_SMOKE_ENV"
   ```

   Inhalt:

   ```ini
   ATELIER_SMOKE_USER=<smoke-user>
   ATELIER_SMOKE_PASSWORD=<Passwort aus Schritt 1>
   ATELIER_SMOKE_CLIENT_ID=<Client-ID aus Schritt 2>
   # optional:
   # ATELIER_SMOKE_BASE_URL=https://<atelier-host>
   # ATELIER_SMOKE_REDIRECT_URI=<Redirect-URI aus Schritt 2, falls abweichend vom Standard>
   # ATELIER_SMOKE_TIMEOUT=1500
   # ATELIER_SMOKE_WAIT=25
   # ATELIER_SMOKE_POLL=2
   ```

   Die Datei wird nicht als Shell-Skript ausgeführt, sondern zeilenweise als `SCHLÜSSEL=Wert` gelesen. Modus muss `600` (oder `400`) sein, sonst bricht das Skript ab. Ein altes `ATELIER_SMOKE_TEMPLATE` wird nicht mehr gelesen.

Ohne gesetztes `ATELIER_SMOKE_ENV` nimmt das Skript seinen Standardpfad; ein anderer Ort geht über `ATELIER_SMOKE_ENV=/pfad/zur/datei`.

## Aufruf

```bash
scripts/atelier-smoke.sh                         # nach jedem Deploy (erst cli-proxy, dann web)
ATELIER_SMOKE_VERBOSE=1 scripts/atelier-smoke.sh # Zwischenschritte auf stderr
```

Beispiele für die Ergebniszeile:

```text
SMOKE OK: Auftrag 3f0c… Converged in 62 s, 87 Zeichen Text (submit_order)
SMOKE OK: Auftrag 3f0c… DeliveredWithOpenPoints in 95 s, 95 Zeichen Text (submit_order), Rückfragen übersprungen, Zustand delivered_with_open_points, offene Punkte
SMOKE NICHT EINGERICHTET: Smoke-Konto fehlt (<Pfad aus ATELIER_SMOKE_ENV> nicht vorhanden). …
SMOKE FEHLER (lauf): Auftrag 3f0c… nach 120 s: outcome Failed (Zustand failed); Hinweis: …
SMOKE FEHLER (zeitlimit): Auftrag 3f0c… nach 1500 s fertig im Zustand delivered, aber ohne outcome; …
SMOKE FEHLER (konfiguration): ATELIER_SMOKE_BASE_URL nutzt unverschlüsseltes http:// für den Host geef.example.org; …
SMOKE FEHLER (antwort): Antwort von /oauth/token ist kein JSON-Objekt, sondern list
```

Die Aufträge des Smoke-Kontos stehen unter „My work“ dieses Kontos und in der Lauf-Liste der Admins; sie dürfen dort bleiben oder von Hand gelöscht werden.

## Tests

```bash
cd cli-proxy && python3 -m pytest -q tests/test_atelier_smoke_script.py
```

Die Tests laufen offline: Ein kleiner HTTP-Testserver spielt Login, OAuth-Freigabe, Token und MCP; `docker` ist eine Attrappe. Geprüft werden der erfolgreiche Durchlauf (`Converged` und `DeliveredWithOpenPoints`), das einmalige Überspringen von Rückfragen, dass nur `submit_order`, `answer_order`, `get_order_status` und `get_order_result` gerufen werden, dass der Text erst nach gesetztem `outcome` und genau einmal gelesen wird, alle Fehlerpfade (kein Konto, falscher Dateimodus, fehlende Schlüssel, `/health`, Verbindung, falsches Passwort, falscher Client, falscher `state`, `outcome` `Failed` oder `Aborted`, kein Text, Zeitlimit mit und ohne terminalen Status, `http://` zu einem nicht gelisteten Host ohne jede Anfrage an ihn, unerwartete Antworten von Token-Endpunkt und MCP, unbekannter `outcome`-Wert) und dass keine Geheimnisse in der Ausgabe stehen.
