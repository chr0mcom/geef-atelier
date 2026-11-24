# Wächter für hängende Läufe und Proxy-Störungen

Stand: 2026-11-24 · Skript: `scripts/atelier-watch.sh` · Tests: `cli-proxy/tests/test_atelier_watch_script.py`

## Was er prüft

| Prüfung | Problem, wenn … |
|---|---|
| `runs` | ein Lauf länger als 60 Minuten im Status `Running` steht (`"Runs"."Status" = 'Running'`, gemessen ab `"StartedAt"`, ersatzweise `"CreatedAt"`) oder die Abfrage scheitert |
| `proxy` | `GET http://localhost:8090/health` des cli-proxy nicht antwortet, `status` nicht `ok` ist, `degraded` `true` ist oder ein Backend ohne Reserve-Kennzeichen einen offenen Failover-Breaker (`open_until` gesetzt) bzw. `healthy: false` meldet |
| `container-geef-atelier-web`, `container-geef-atelier-cli-proxy` | der Container nicht läuft oder sein Healthcheck nicht `healthy` meldet |

Jede Prüfung kennt drei Ergebnisse: **ok**, **Problem** und **unklar**. Unklar heißt, dass sich die Daten nicht lesen ließen:

- `proxy`: `/health` antwortet, aber nicht mit einem lesbaren JSON-Objekt (zum Beispiel `[]`, `null`, leerer Körper, eine HTML-Seite oder ein `failover`-Feld, das kein Objekt ist). Meldung „Gesundheitsdaten des cli-proxy nicht lesbar“. Ist `/health` gar nicht erreichbar, ist das ein Problem, nicht unklar.
- `runs`: Die Abfrage scheitert, oder ihr fehlt die Kennzeile `#watch-ok`, die jede erfolgreiche Abfrage mitliefert.
- `container-…`: `docker inspect` scheitert aus einem anderen Grund als „no such object“ (zum Beispiel weil der Docker-Dienst nicht antwortet) oder liefert eine unerwartete Ausgabe. Ein fehlender Container bleibt ein Problem.

Unklar wird gemeldet und zählt beim Exit-Code als Problem, **beendet aber nie einen offenen Alarm**: „Wieder in Ordnung“ schickt nur ein eindeutiges ok. Auch ein Absturz der Auswertung führt zu unklar, nie zu ok.

Die Datenbankabfrage ist rein lesend (`docker exec geef-atelier-postgres … psql` mit `default_transaction_read_only=on`). Benutzer und Datenbank kommen aus der Umgebung des Postgres-Containers; das Skript liest keine `.env` und gibt keine Zugangsdaten aus. `"Status"` ist eine Textspalte (EF speichert den Namen des Enums), deshalb der Vergleich mit dem Text `Running`.

## Meldungen

Gemeldet wird über denselben Kanal wie beim nächtlichen cli-proxy-Selfupdate: `hermes send --to telegram` (überschreibbar mit `ALARM_CMD`). Der Melder bezieht seine Zugangsdaten selbst.

- **Neues Problem:** eine Meldung „Problem (…)“.
- **Unklar:** eine Meldung „Unklar (…)“; kommt sie während eines offenen Alarms, heißt sie „Unklar geändert (…)“ mit dem Zusatz, dass die gemeldete Störung weiter als offen gilt.
- **Geändertes Problem:** eine Meldung „Problem geändert (…)“, wenn sich die Art ändert (zum Beispiel ein weiterer hängender Lauf, ein anderes gestörtes Backend oder der Wechsel von unklar zu Problem). Reine Zahlenänderungen (Minuten, Fehlerzähler) lösen nichts aus.
- **Erinnerung:** frühestens nach 6 Stunden, solange das Problem besteht oder der Zustand unklar bleibt.
- **Wieder in Ordnung:** genau eine Meldung mit der Dauer der Störung.

Den Zustand je Prüfung hält eine Datei in `/srv/backup/atelier-watch/` (`<prüfung>.state`). Scheitert das Senden, bleibt der alte Zustand stehen, und der nächste Lauf versucht es erneut. Log: `/srv/backup/atelier-watch/watch.log` (wird ab 1 MB einmal rotiert, und zwar nur von dem Lauf, der die Sperre `/srv/backup/atelier-watch/lock` hält; ein zweiter Lauf schreibt nur „Anderer Lauf aktiv, Abbruch.“ ans Ende des Logs).

Exit-Code: 0 = alles in Ordnung, 1 = mindestens ein Problem oder ein unklares Ergebnis, 2 = Zustandsverzeichnis oder Sperrdatei nicht anlegbar.

## Cron-Eintrag

Noch **nicht** eingetragen. Für den Benutzer `developer` (`crontab -e`):

```cron
*/5 * * * * /srv/docker/websites/geef_atelier/scripts/atelier-watch.sh
```

Das Skript schreibt selbst ins Log; Cron bekommt keine Ausgabe.

## Von Hand

```bash
DRY_RUN=1 scripts/atelier-watch.sh          # prüfen, Meldungen nur ausgeben, nichts senden, keinen Zustand schreiben
STUCK_MINUTES=90 scripts/atelier-watch.sh   # andere Schwelle für hängende Läufe
```

Weitere Variablen: `STATE_DIR`, `LOG`, `GATEWAY`, `PG_CONTAINER`, `WATCH_CONTAINERS`, `REMIND_SECONDS`.

`STUCK_MINUTES` und `REMIND_SECONDS` müssen positive ganze Zahlen sein (höchstens sechs Stellen, ohne führende Null). Jeder andere Wert, etwa `60'` aus einer verrutschten Cron-Zeile, wird verworfen: Das Skript schreibt eine Warnung ins Log und nimmt den Standard (60 Minuten bzw. 21600 Sekunden). Der Wert gelangt nie ungeprüft in die SQL-Abfrage.

## Tests

```bash
cd cli-proxy && python3 -m pytest -q tests/test_atelier_watch_script.py
```

Die Tests laufen mit Attrappen für `docker`, `curl` und den Melder; sie fassen weder Docker noch die Datenbank noch Telegram an.
