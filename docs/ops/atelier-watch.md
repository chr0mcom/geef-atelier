# Wächter für hängende Läufe und Proxy-Störungen

Stand: 2026-11-27 · Skript: `scripts/atelier-watch.sh` · Tests: `cli-proxy/tests/test_atelier_watch_script.py`

Diese Anleitung beschreibt die Bedienung, nicht einen bestimmten Server. Werte in spitzen Klammern
(`<state-dir>`, `<watch-user>`, `<repo>`) stehen für die Einstellungen des Betreibers; die Standardwerte
(Zustandsverzeichnis, Container-Namen, Gateway, Melder) stehen im Kopf des Skripts und lassen sich per
Umgebung überschreiben.

## Was er prüft

| Prüfung | Problem, wenn … |
|---|---|
| `runs` | ein Lauf länger als 60 Minuten im Status `Running` steht (`"Runs"."Status" = 'Running'`, gemessen ab `"StartedAt"`, ersatzweise `"CreatedAt"`) oder die Abfrage scheitert |
| `proxy` | `GET $GATEWAY/health` des cli-proxy nicht antwortet, `status` nicht `ok` ist, `degraded` `true` ist oder ein Backend ohne Reserve-Kennzeichen einen offenen Failover-Breaker (`open_until` gesetzt) bzw. `healthy: false` meldet |
| `container-<name>` | ein Container aus `WATCH_CONTAINERS` (Standard: Web-App und cli-proxy) nicht läuft oder sein Healthcheck nicht `healthy` meldet |
| `state-dir` | das Zustandsverzeichnis `<state-dir>` nicht beschreibbar ist (volle Platte, Rechte) oder in diesem Lauf ein Zustand nicht geschrieben werden konnte |

Jede Prüfung kennt drei Ergebnisse: **ok**, **Problem** und **unklar**. Unklar heißt, dass sich die Daten nicht lesen ließen:

- `proxy`: `/health` antwortet, aber nicht mit einem lesbaren JSON-Objekt (zum Beispiel `[]`, `null`, leerer Körper, eine HTML-Seite oder ein `failover`-Feld, das kein Objekt ist). Meldung „Gesundheitsdaten des cli-proxy nicht lesbar“. Ist `/health` gar nicht erreichbar, ist das ein Problem, nicht unklar.
- `runs`: Die Abfrage scheitert, oder ihr fehlt die Kennzeile `#watch-ok`, die jede erfolgreiche Abfrage mitliefert.
- `container-…`: `docker inspect` scheitert aus einem anderen Grund als „no such object“ (zum Beispiel weil der Docker-Dienst nicht antwortet) oder liefert eine unerwartete Ausgabe. Ein fehlender Container bleibt ein Problem.

Unklar wird gemeldet und zählt beim Exit-Code als Problem, **beendet aber nie einen offenen Alarm**: „Wieder in Ordnung“ schickt nur ein eindeutiges ok. Auch ein Absturz der Auswertung führt zu unklar, nie zu ok.

Die Datenbankabfrage ist rein lesend (`docker exec $PG_CONTAINER … psql` mit `default_transaction_read_only=on`). Benutzer und Datenbank kommen aus der Umgebung des Postgres-Containers; das Skript liest keine `.env` und gibt keine Zugangsdaten aus. `"Status"` ist eine Textspalte (EF speichert den Namen des Enums), deshalb der Vergleich mit dem Text `Running`.

## Meldungen

Gemeldet wird über den Melder in `ALARM_CMD` (Standard im Skript: derselbe Kanal wie beim nächtlichen cli-proxy-Selfupdate; der Meldetext wird als letztes Argument angehängt). Der Melder bezieht seine Zugangsdaten selbst.

- **Neues Problem:** eine Meldung „Problem (…)“.
- **Unklar:** eine Meldung „Unklar (…)“; kommt sie während eines offenen Alarms, heißt sie „Unklar geändert (…)“ mit dem Zusatz, dass die gemeldete Störung weiter als offen gilt.
- **Geändertes Problem:** eine Meldung „Problem geändert (…)“, wenn sich die Art ändert (zum Beispiel ein weiterer hängender Lauf, ein anderes gestörtes Backend oder der Wechsel von unklar zu Problem). Reine Zahlenänderungen (Minuten, Fehlerzähler) lösen nichts aus.
- **Erinnerung:** frühestens nach 6 Stunden (`REMIND_SECONDS`), solange das Problem besteht oder der Zustand unklar bleibt.
- **Wieder in Ordnung:** genau eine Meldung mit der Dauer der Störung.

Den Zustand je Prüfung hält eine Datei in `<state-dir>` (Variable `STATE_DIR`, `<prüfung>.state`). Scheitert das Senden, bleibt der alte Zustand stehen, und der nächste Lauf versucht es erneut. Log: `<state-dir>/watch.log` (wird ab 1 MB einmal rotiert, und zwar nur von dem Lauf, der die Sperre `<state-dir>/lock` hält; ein zweiter Lauf schreibt nur „Anderer Lauf aktiv, Abbruch.“ ans Ende des Logs).

### Wenn der Zustand nicht gespeichert werden kann

Ohne gespeicherten Zustand hielte jeder Lauf ein Problem für neu und meldete es alle 5 Minuten erneut. Deshalb gilt:

1. Jeder Lauf prüft zuerst mit einer Probedatei, ob `<state-dir>` beschreibbar ist. Jeder Schreibfehler (Probe, Zustandsdatei, Löschen nach „Wieder in Ordnung“) wird mit „Schreibfehler: …“ geloggt und gezählt; die Schlusszeile lautet dann „Fertig (N Problem(e), M Schreibfehler).“ Halb geschriebene Temp-Dateien werden entfernt.
2. Der Zustand geht dann in eine **Ersatzablage** im Arbeitsspeicher (`WATCH_FALLBACK_DIR`, Standard `/dev/shm/atelier-watch-<uid>`, ohne `/dev/shm` unter `$TMPDIR` bzw. `/tmp`). Das Verzeichnis wird mit Modus 700 angelegt und nur benutzt, wenn es dem aufrufenden Benutzer gehört und kein symbolischer Link ist. Eine Datei dort hat Vorrang vor `<state-dir>`; die Meldungen laufen wie gewohnt (neu, geändert, Erinnerung, wieder in Ordnung).
3. Die Prüfung `state-dir` meldet das Problem **einmal** („Problem (state-dir): Zustandsverzeichnis … nicht beschreibbar …“), danach nur Erinnerungen im 6-Stunden-Takt.
4. Ist `<state-dir>` wieder beschreibbar, führt der nächste Lauf die Ersatzablage zurück und meldet einmal „Wieder in Ordnung (state-dir)“.
5. Ist auch die Ersatzablage nicht beschreibbar, gibt es keinen Zustand über Läufe hinweg. Dann sendet nur ein Lauf in den ersten `WATCH_INTERVAL` Sekunden (Standard 300, passend zum 5-Minuten-Takt) jedes `REMIND_SECONDS`-Fensters (beim Standard um 0, 6, 12 und 18 Uhr UTC); alle anderen Läufe loggen „Meldung gedrosselt“. Das sind höchstens eine Meldung je Prüfung und Fenster statt 288 am Tag. Eine Meldung kann sich dadurch bis zu 6 Stunden verzögern; lieber eine Meldung zu wenig als eine Flut.

Die Ersatzablage überlebt keinen Neustart des Rechners. Das Log selbst liegt in `<state-dir>`; bei voller Platte gehen auch Logzeilen verloren, die Meldungen nicht.

Exit-Code: 0 = alles in Ordnung, 1 = mindestens ein Problem oder ein unklares Ergebnis (auch ein nicht beschreibbares Zustandsverzeichnis), 2 = Zustandsverzeichnis oder Sperrdatei nicht anlegbar.

## Cron-Eintrag

Das Repository richtet ihn nicht selbst ein. Für den Betriebsbenutzer `<watch-user>`, der `docker` aufrufen darf (`crontab -e`):

```cron
*/5 * * * * <repo>/scripts/atelier-watch.sh
```

Das Skript schreibt selbst ins Log; Cron bekommt keine Ausgabe.

## Von Hand

```bash
DRY_RUN=1 scripts/atelier-watch.sh          # prüfen, Meldungen nur ausgeben, nichts senden, keinen Zustand schreiben
STUCK_MINUTES=90 scripts/atelier-watch.sh   # andere Schwelle für hängende Läufe
```

Weitere Variablen: `STATE_DIR`, `LOG`, `GATEWAY`, `PG_CONTAINER`, `WATCH_CONTAINERS`, `REMIND_SECONDS`, `WATCH_INTERVAL`, `WATCH_FALLBACK_DIR`, `ALARM_CMD`.

`STUCK_MINUTES`, `REMIND_SECONDS` und `WATCH_INTERVAL` müssen positive ganze Zahlen sein (höchstens sechs Stellen, ohne führende Null). Jeder andere Wert, etwa `60'` aus einer verrutschten Cron-Zeile, wird verworfen: Das Skript schreibt eine Warnung ins Log und nimmt den Standard (60 Minuten, 21600 Sekunden bzw. 300 Sekunden). Der Wert gelangt nie ungeprüft in die SQL-Abfrage oder eine Rechnung der Shell.

## Tests

```bash
cd cli-proxy && python3 -m pytest -q tests/test_atelier_watch_script.py
```

Die Tests laufen mit Attrappen für `docker`, `curl` und den Melder; sie fassen weder Docker noch die Datenbank noch den echten Melder an. Eine volle Platte wird mit einer `mktemp`-Attrappe nachgestellt, deren Temp-Datei auf `/dev/full` zeigt (Schreiben scheitert mit „No space left on device“); ein nicht beschreibbares Zustandsverzeichnis mit Modus 500 (dieser Test wird als root übersprungen).
