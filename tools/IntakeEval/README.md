# IntakeEval – Messwerkzeug für den Klärer

Misst den Klärer (die Rückfragen vor dem Schreiben) gegen die Abnahmetore der Etappe 2. Der synthetische Korpus entsteht mit
`tools/Eval/geef_eval/intake_corpus.py` (siehe `tools/Eval/README.md`).

```bash
dotnet run --project tools/IntakeEval -- run --corpus intake-corpus.jsonl --proxy <gateway-url> --out results.jsonl [--max-calls N] [--parallel 2] [--timeout-seconds 40]
dotnet run --project tools/IntakeEval -- evaluate --corpus intake-corpus.jsonl --second intake-corpus-second.jsonl --results results.jsonl
dotnet run --project tools/IntakeEval -- shadow --csv shadows.csv [--sample 10]
```

| Befehl | Wirkung |
|---|---|
| `run` | ruft den echten Klärer und das Gate je Eintrag und hängt je Aufruf eine Zeile an (Dauer, Modell, Rohantwort, Gate-Spur, Fragen, Zeitgrenze `timeout_ms`, Eingabe-Hash `input_hash`). Fortsetzbar: Einträge, deren letztes Ergebnis zur aktuellen Eingabe `Ok` oder `Invalid` ist, werden übersprungen; gescheiterte Aufrufe bleiben in der Datei. Ergebnisse zu einer anderen Eingabe werden nie übersprungen (Warnung im Protokoll); die Datei bleibt dann aber nicht auswertbar, also eine neue `--out`-Datei nehmen. |
| `evaluate` | spielt die Rohantworten durch Parser und Gate und gibt je Tor a–f (und die Warnschwelle e′) für die zurückgehaltene Menge und den Zweitkorpus `passed`, `FAILED` oder `NOT EVALUABLE` aus. Rückgabewert 0 nur, wenn alle Tore bestanden sind. |
| `shadow` | wertet einen CSV-Export der Tabelle `OrderIntakeShadows` aus: Tore a und f im Schatten und Tor g, dazu eine Stichprobe von Fragen für die Sichtprüfung. Rückgabewert 0 nur, wenn alle messbaren Tore bestanden sind. |

Verlangt das Gateway einen Schlüssel, liest `run` ihn aus der Umgebungsvariablen `INTAKE_EVAL_API_KEY` (nie als Argument übergeben, nie ausgeben).

## Regeln der Auswertung

- **Kein Tor besteht ohne Daten.** Fehlt ein Ergebnis zu einem Korpuseintrag, gehört ein Ergebnis zu keinem Eintrag, hat ein
  Korpus nicht die Sollgröße (120 Haupt, 40 Zweit), ist ein Eintrag unbeschriftet oder ist die zurückgehaltene Menge oder der
  Zweitkorpus leer oder wurde ein Ergebnis für eine andere Eingabe erzeugt, sind alle Tore „nicht auswertbar“. Die Eingabe ist
  der SHA-256 über Systemprompt, Schema und Nutzerprompt des Klärers (Auftrag, Dateiauszüge, Profil, Überarbeitungshinweis);
  ändert sich einer davon, passen alte Ergebnisse nicht mehr, auch wenn die Id gleich bleibt. Kategorie, Sprache und Teilmenge
  der Zeile müssen ebenfalls zum Eintrag passen; eine Zeile ohne Hash ist nicht zuzuordnen. Der Test `IntakeCorpusTests` schlägt dann fehl, sobald eine
  Ergebnisdatei `results-*.jsonl` im Testordner liegt.
- **Latenz (a)** zählt jede Antwort (`Ok`) und jede Zeitüberschreitung, auch Wiederholungen; andere Fehlschläge haben keine
  Latenz, damit schnelle Fehler den Median nicht schönen. **Fehlerquote (f)** zählt jeden Aufruf; die Ausgabe nennt die Zahl
  der Wiederholungen. Als Fehler gilt
  jeder Aufruf, der nicht `Ok` ist, eine `Ok`-Antwort über der Produktionsgrenze (`--production-timeout-seconds`, Standard wie
  `Intake:Timeout`) und eine `Ok`-Antwort, die sich nicht nachspielen lässt.
- **Fragenkennzahlen (b–e′)** nutzen je Eintrag die letzte erfolgreiche Antwort; Einträge ohne solche Antwort zählen nie als
  „keine Frage“.
- **Schatten:** `SkippedCap` und `Pending` bleiben draußen und werden gezählt. Latenz haben nur `Done` und `TimedOut`;
  `Failed` (auch eine gescheiterte Vorbereitung, `prepare_failed`) zählt als Fehler, nie als Latenz. `shadow` kennt keine
  Produktionsgrenze, die Zeitgrenze stand schon beim Lauf fest. Eine fehlende Fragenzahl zählt nie als 0.
  Tor g braucht mindestens 30 abgeschlossene Aufträge oder eine Woche; die Sichtprüfung von zehn Fragen bleibt immer einer
  Person überlassen.

## Export der Schattendaten (nur lesend)

Die Abfrage läuft in einer schreibgeschützten Sitzung und schreibt die CSV-Datei auf den Rechner, auf dem `psql` läuft:

```bash
QUERY=$(tr '\n' ' ' <<'SQL'
SELECT s."OrderId", s."Status", s."Attempts", s."DurationMs", s."QuestionCount",
       s."RecordJson"->>'errorCode' AS "ErrorCode", s."CreatedAt", s."CompletedAt",
       (SELECT string_agg(q->>'text', ' | ')
          FROM jsonb_array_elements(COALESCE(s."RecordJson"->'questions', '[]'::jsonb)) AS q) AS "Questions"
  FROM "OrderIntakeShadows" AS s
 ORDER BY s."CreatedAt"
SQL
)
PGOPTIONS="-c default_transaction_read_only=on" psql "<verbindung>" -c "\copy ($QUERY) TO 'shadows.csv' WITH (FORMAT csv, HEADER)"
```

Die Spalte `Questions` enthält die gezeigten Fragen echter Aufträge. Die Datei bleibt lokal und gehört nicht ins Git.
