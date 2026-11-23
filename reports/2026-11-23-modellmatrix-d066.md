# Abschlussbericht D-066 — Modellmatrix und immer aktuelle Modelle

**Stand:** 2026-11-23 · **Commits:** `b979c02` (D-065 Teil 1, vorbestehende Bilderzeugung), `8a44190` (D-066) · **Deploy:** cli-proxy und web am 2026-11-23 gegen 15:10 CET

## Was umgesetzt ist

- **Modellmatrix.** `ModelMatrix` bildet Rolle × Gebiet auf Kandidaten ab. Die Grundlage ist die Recherche in `reports/2026-11-22-modellrecherche.md`. Claude läuft immer über `claude-cli`, OpenAI immer über `codex-cli`.
- **Eine Zuweisungsregel** (`ModelChooser`), angewendet beim Snapshot-Bau.
  - **Harte Regeln:** nur nutzbare Provider; bezahlt nur für Reviewer, höchstens 2 je Crew, außerhalb Legal/Academic erst ab 3 Reviewern derselben Rolle.
  - **Weiche Regeln:** Live-Katalog, Executor-Modell meiden, Ring je Wahl, andere Familie bevorzugen.
  - **Absicherung:** ein erschöpfender Invarianten-Test über rund 645.000 Kombinationen, per Mutation geprüft.
- **Profile und Crews.**
  - Profile tragen eine Rolle und optional ein Gebiet (verwaltet oder manuell), Crews ein Gebiet.
  - Der Composer setzt `model_slot` und `model_domain` ausdrücklich; es gibt keine Gebiets-Heuristik.
  - Composer-Settings laufen über eine Whitelist.
- **Migration Step46.**
  - Neue Spalten und Klassifikation aller 11 eigenen Executoren und 46 eigenen Reviewer.
  - Gebiet für alle 11 eigenen Templates.
  - CLI-Umschreibung nach einer Regel, mit Paritätstest C# ↔ SQL.
  - System-Transforms und Refiner sind verwaltet.
- **Immer aktuell (Betreiberwunsch).**
  - Der cli-proxy kennt Codex-Aliase `gpt-<linie>-latest`, die immer auf die neueste Version einer Linie zeigen. Die Matrix nutzt sie, Snapshots halten die konkrete ID.
  - Der Failover auf codex läuft auf der neuesten Sol-Version.
  - Der nächtliche Job `scripts/cli-proxy-selfupdate.sh` hält Container- und Host-CLIs aktuell und stellt das Host-Codex-Modell auf die neueste Sol-Version. Jeder Schritt hat Testaufruf, Rollback und Telegram-Meldung; das Skript ist mit Fake-CLIs getestet.
- **Vorfall unterwegs.** Am 22.11. lehnte die veraltete Host-codex-CLI (0.151) `gpt-6.2-sol` ab. Die CLI ist jetzt auf 0.171.0; der Abgleich verhindert eine Wiederholung.

## Nachweise

- **Tests:**
  - .NET: 2366 bestanden. Rot ist nur `Step10CrewSystemMigrationTests`; der Fehler ist vorbestehend und auf HEAD reproduziert.
  - Web.E2E: 4 Timeouts am `/workshop`-Dashboard, vorbestehend.
  - cli-proxy (pytest): 601 bestanden.
- **Probe vor dem Deploy:** Step46 lief gegen eine Kopie der Prod-Datenbank. Danach gab es keine Altrouten, alle eigenen Profile hatten eine Rolle, alle Templates ein Gebiet, und es gab keine ungültigen Enum-Werte.
- **Prod nach dem Deploy:**
  - **Migration:** Step46 ist angewendet.
  - **Codex-Katalog:** `live` mit `x_aliases_complete: true`; `gpt-sol-latest` → `gpt-6.2-sol`, `gpt-astra-latest` → `gpt-6.2-astra`.
  - **Smoke-Tests:** claude und codex antworten jeweils direkt, ohne Failover.
  - **Neustart:** Open WebUI wurde neu gestartet.
- **Live-UI** (eigener Chromium per CDP, weil Playwright-MCP nicht erreichbar war): Modellzuweisungs-Block, Rollen, Gebiets-Badges sowie Finalizer- und Refiner-Marker sind korrekt (Ergebnis „0 findings“).
- **Testläufe in Prod:**
  - **klassik:** Executor `claude-opus-5-5`, Reviewer `codex-cli/gpt-6.2-astra` (Fakten) und `codex-cli/gpt-6.2-sol` (Stil).
  - **Auto-Crew:** Composer-Reviewer verteilt auf Astra, Sol, Astra, Fable, Sonnet. Der Composer setzte `model_domain` auf Correspondence; die erzeugte Crew lief mit Astra (Fakten) und Sol (Stil) gegen einen Opus-Executor.
  - **Learning-Bewertung:** Sie bekam ebenfalls Matrix-Modelle (Astra, Sol, Haiku).
  - **Ergebnis:** Alle Läufe sind abgeschlossen.

## GEEF-Verlauf

- **Phase 1:** Architekt per `claude -p` (Fallback-Stufe: Prompt per stdin mit `--allowedTools`). Danach Pre-Mortem und Devil's Advocate mit dem Urteil SIGNIFICANT; die Entscheidungen stehen in `docs/05-decisions-log_de.md` D-066.
- **Runde 1:**
  - Grundlage: drei Reviewer per `claude -p`, Code-Qualität per Codex.
  - Ergebnis: 3 CRITICAL, darunter Composer-`settings` als Abflusskanal für Webhook-Ziele und Umgebungsgeheimnisse; alle behoben.
- **Runden 2 und 3:** jeweils mit Iteration Advisor.
  - Grundursachen: D-066/10 kam ohne Design-Gate; Fixes nur am Fundort; doppelte Regeln ohne Paritätstest.
  - Nach Runde 3 Stagnation an zwei Brennpunkten (Gebiets-Parser, Fallback). Der Fable-Berater, an den der Betreiber die Entscheidung delegiert hatte, entschied für Strukturänderungen statt Stopp. Daraus entstanden das explizite `model_domain` und eine einzige Zuweisungsregel.
- **Runde 4:** Die Stoppschwelle war wörtlich erreicht (Gegenbeispiele I4/I5). Der Fable-Berater wertete das als Verdrahtungsfehler: eine weitere Runde, und Runde 5 sollte die letzte sein.
- **Runde 5:** Erneut ein I3-Gegenbeispiel, diesmal auf Katalogebene. Angehalten und dem Betreiber vorgelegt; Entscheidung „Fixen + 1 Prüfrunde“.
- **Runde 6:**
  - functional 0, Architektur und Tests nur MINOR, Quality 3 MAJOR und 2 MINOR; alles behoben.
  - Die anschließende gezielte Gegenprüfung fand Randfehler in diesen Fixes; auch die sind behoben.
  - Danach gab es bewusst **keine weitere volle Runde**. Restrisiko: kleine Randfälle in den zuletzt geänderten Stellen. Abgedeckt durch Tests, nicht durch ein weiteres Review.
- **Release-Gate:**
  - Urteil YELLOW.
  - Vier Auflagen, alle erledigt: Images getaggt, frische geprüfte Sicherung, Step46-Probe auf Prod-Kopie, Commits vor dem Build inklusive Cron-Skript, Neustart von Open WebUI.

## Rückweg

1. web stoppen.
2. DB aus `/srv/backup/geef-atelier-predeploy-d066-20261123-145634.dump` wiederherstellen.
3. `geef-atelier-web:prev-20261123-predeploy-d066` als `latest` taggen und starten.
4. Erst danach den cli-proxy auf `geef-atelier-cli-proxy:prev-20261123-predeploy-d066` zurücksetzen.

Ein Rollback nur des cli-proxy würde die Aliase in der DB unauflösbar machen.

## Offene Punkte (benannt, nicht in D-066)

1. **DNS-Pinning** für `url-fetch`, `rest-api` und den MCP-Client: priorisierte eigene Härtung, betrifft auch das Template-Studio über MCP.
2. **Fable-Alias:** Die claude-Alias-Probe (D-064) liefert `claude-fable-latest` nicht in `x_aliases`. Fable-Akteure halten im Snapshot deshalb den Alias statt der konkreten ID; in den Testläufen bestätigt.
3. **Template-Studio-Crews** haben kein Matrix-Gebiet (General).
4. **Zwei Transform-Finalizer** (`custom-natural-german-voice-polish`, `custom-german-review-consolidation-transform`) haben seit einem alten Fehler leere Settings und werden still übersprungen.
5. **Bezahlte Reviewer:** Legal- und Academic-Crews nutzen bis zu zwei Neon-Reviewer über OpenRouter. Das Guthaben vor dem ersten echten Legal- oder Academic-Run prüfen.
6. **Kommunikation:**
   - **BookForge:** Der Codex-Katalog hat jetzt `-latest`-Aliase und ein gefülltes `x_aliases`; ein Failover auf codex läuft auf der neuesten Sol-Version.
   - **Selfupdate:** Der Job fasst ab heute Nacht (03:40) auch die Host-CLIs und `~/.codex/config.toml` an.
7. **Beobachtet beim Prod-Testlauf, außerhalb D-066:**
   - **Fehler:** Parallele Grounding-Provider kollidieren beim Speichern der Tool-Aufrufe (`duplicate key … IX_tool_invocations_RunId_Sequence`). Im klassik-Lauf wurde `run-attachments` deshalb übersprungen; der Lauf endete trotzdem erfolgreich.
   - **Herkunft:** Der Code stammt aus dem Tool-System (D-060) und wurde in D-066 nicht geändert. Die Sequenzvergabe sollte atomar werden; das ist ein eigener Fix.
8. **Bekannte Testprobleme:** vorbestehende Fehler (Step10-Migrationstest, E2E-Timeouts am `/workshop`-Dashboard).
