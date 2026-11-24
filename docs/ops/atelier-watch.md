# Wächter für hängende Läufe und Proxy-Störungen

Stand: 2026-11-24 · Skript: `scripts/atelier-watch.sh` · Tests: `cli-proxy/tests/test_atelier_watch_script.py`

## Was er prüft

| Prüfung | Problem, wenn … |
|---|---|
| `runs` | ein Lauf länger als 60 Minuten im Status `Running` steht (`"Runs"."Status" = 'Running'`, gemessen ab `"StartedAt"`, ersatzweise `"CreatedAt"`) oder die Abfrage scheitert |
| `proxy` | `GET http://localhost:8090/health` des cli-proxy nicht antwortet, `status` nicht `ok` ist, `degraded` `true` ist oder ein Backend ohne Reserve-Kennzeichen einen offenen Failover-Breaker (`open_until` gesetzt) bzw. `healthy: false` meldet |
| `container-geef-atelier-web`, `container-geef-atelier-cli-proxy` | der Container nicht läuft oder sein Healthcheck nicht `healthy` meldet |

Die Datenbankabfrage ist rein lesend (`docker exec geef-atelier-postgres … psql` mit `default_transaction_read_only=on`). Benutzer und Datenbank kommen aus der Umgebung des Postgres-Containers; das Skript liest keine `.env` und gibt keine Zugangsdaten aus. `"Status"` ist eine Textspalte (EF speichert den Namen des Enums), deshalb der Vergleich mit dem Text `Running`.

## Meldungen

Gemeldet wird über denselben Kanal wie beim nächtlichen cli-proxy-Selfupdate: `hermes send --to telegram` (überschreibbar mit `ALARM_CMD`). Der Melder bezieht seine Zugangsdaten selbst.

- **Neues Problem:** eine Meldung „Problem (…)“.
- **Geändertes Problem:** eine Meldung „Problem geändert (…)“, wenn sich die Art ändert (zum Beispiel ein weiterer hängender Lauf oder ein anderes gestörtes Backend). Reine Zahlenänderungen (Minuten, Fehlerzähler) lösen nichts aus.
- **Erinnerung:** frühestens nach 6 Stunden, solange das Problem besteht.
- **Wieder in Ordnung:** genau eine Meldung mit der Dauer der Störung.

Den Zustand je Prüfung hält eine Datei in `/srv/backup/atelier-watch/` (`<prüfung>.state`). Scheitert das Senden, bleibt der alte Zustand stehen, und der nächste Lauf versucht es erneut. Log: `/srv/backup/atelier-watch/watch.log` (wird ab 1 MB einmal rotiert).

Exit-Code: 0 = alles in Ordnung, 1 = mindestens ein Problem, 2 = Zustandsverzeichnis nicht anlegbar.

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

## Tests

```bash
cd cli-proxy && python3 -m pytest -q tests/test_atelier_watch_script.py
```

Die Tests laufen mit Attrappen für `docker`, `curl` und den Melder; sie fassen weder Docker noch die Datenbank noch Telegram an.
