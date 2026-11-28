# FactCheckEval – Messwerkzeug für die Vorversuche V1–V3 (Etappe 3)

Misst den Block am Dateiende (V1), eicht den Faktenprüfer (V2) und prüft die Rechenprüfung der Quellen (V3). Die Korpora für
V2 und V3 entstehen mit `tools/Eval/geef_eval/factcheck_corpus.py` (siehe `tools/Eval/README.md`); die festen Aufträge für V1
liegen in `tests/Geef.Atelier.Tests/FactCheck/Corpus/notes-probe.jsonl` (synthetisch, von Hand geschrieben bzw. aus Bausteinen
erzeugt).

```bash
dotnet run --project tools/FactCheckEval -- probe-notes --inputs notes-probe.jsonl --gateway <gateway-url> --out probe.jsonl \
  --short 4 --long 2 --max-calls 6 --codex-lock <sperrdatei> [--codex-lock <weitere>] [--timeout-seconds 1800]
dotnet run --project tools/FactCheckEval -- probe-evaluate --inputs notes-probe.jsonl --results probe.jsonl
dotnet run --project tools/FactCheckEval -- run --corpus factcheck-corpus.jsonl --gateway <gateway-url> --out results-dev.jsonl \
  --split dev --max-calls 22 --codex-lock <sperrdatei> [--timeout-seconds 90] [--reasoning-effort low]
dotnet run --project tools/FactCheckEval -- evaluate --corpus factcheck-corpus.jsonl --results results.jsonl
dotnet run --project tools/FactCheckEval -- arithmetic --corpus arithmetic-corpus.jsonl
```

| Befehl | Wirkung |
|---|---|
| `probe-notes` | V1: schickt den echten ersten Schreibaufruf (Opus, Dokumentmodus, `DirectWriterPrompts.FirstPass(…, writerNotes: true)`, Quellen über den echten `SourceBlockRenderer`, Proxy-Failover nur auf dasselbe Modell, `same-model`, nie zu Codex) für die Probe-Aufträge, bis die Datei `--short` kurze und `--long` lange Versuche enthält. Die Zahlen sind kumulativ: Stufe a `4/2`, Stufe b `10/5`, Stufe c `10/10`. Kurze Versuche verteilen sich reihum auf die fünf kurzen Aufträge. Je Versuch eine Zeile mit Rohantwort, Split-Status, Schäden, Wortzahl, letzter Zeile, Dauer, Ausgabe-Token, Zeitgrenze und Eingabe-Hash. |
| `probe-evaluate` | V1: teilt die gespeicherten Rohantworten mit dem aktuellen `WriterNotesFormat.Split` neu und gibt Ergebnis A, B oder C nach der Entscheidungstabelle aus (oder „nicht auswertbar“ mit der nächsten Stufe). |
| `run` | V2: ruft den echten `LlmFactChecker` für jeden Text der Teilmenge (`dev`, `holdout`, `all`), einzeln nacheinander. Modell: was `ModelChooser` für den Slot `FactReviewer` neben dem Opus-Schreiber wählt (heute Astra über `/v1/codex`). Fortsetzbar: Texte, deren letzte Zeile zur aktuellen Eingabe `Ok` oder `Invalid` ist, werden übersprungen; gescheiterte Versuche bleiben in der Datei und zählen. |
| `evaluate` | V2: spielt die Rohantworten durch den echten Parser und `FactFindingFilter` nach, druckt die Zahlen der Entwicklungsmenge (nie ein Urteil) und Tor V2 auf der zurückgehaltenen Menge samt Pflichtfall Elternabend. |
| `arithmetic` | V3: wendet `SourceArithmeticCheck` auf den Rechenkorpus an, druckt die Zahlen der Entwicklungsmenge und Tor V3 auf der zurückgehaltenen Menge. |

Rückgabewerte: 0 fertig, bestanden bzw. entschieden (V1: A, B oder C); 1 Tor nicht bestanden; 2 falscher Aufruf oder
unbrauchbare Eingabedatei; 3 vom Codex-Wächter angehalten (mit demselben Befehl fortsetzen); 4 unvollständig oder nicht
auswertbar (auch: benötigter Produktionscode auf diesem Stand noch nicht umgesetzt).

## Schutz des geteilten Kontingents

- **Aufrufkappe ist Pflicht** (`--max-calls`, Kappe dieses Laufs). Unabhängig davon hält eine Ergebnisdatei höchstens 20
  (`probe-notes`, Vertrag: ≤ 20 Opus) bzw. 52 (`run`, Vertrag: ≤ 52 Codex) Versuche. Jeder Versuch zählt, auch ein
  gescheiterter. Die Kappe von 52 gilt je Datei; wer für Runde 2 oder die zurückgehaltene Menge eine neue Datei beginnt, achtet
  selbst auf die Gesamtzahl.
- **Codex-Wächter:** Vor jedem Aufruf hält das Werkzeug an (Rückgabewert 3), solange eine der mit `--codex-lock` genannten
  Dateien existiert oder `pgrep -a codex` einen laufenden Codex-Auftrag findet, also einen Prozess mit dem Unterbefehl `exec`.
  Ein ruhender Codex-Server einer Editor-Erweiterung verbraucht kein Kontingent und blockiert nicht. Lässt sich die
  Prozessliste nicht lesen, hält es ebenfalls an.
  Der Pfad der Sperrdatei ist ein Parameter und steht nirgends im Code.
- **Nie parallel:** `run` und `probe-notes` rufen strikt nacheinander.
- **Schlüssel:** Verlangt das Gateway einen Schlüssel, liest das Werkzeug ihn nur aus `FACTCHECK_EVAL_API_KEY`. Es gibt keine
  Option dafür; unbekannte Optionen werden abgelehnt. Der Schlüssel erscheint in keiner Ausgabe und keiner Ergebnisdatei.

## Regeln der Auswertung

- **Kein Tor besteht ohne Daten.** V2 ist nicht auswertbar bei unvollständigem Korpus (nicht 16 Grundlagen, unbeschriftet,
  ungepflanzt, Zitat nicht wörtlich, Elternabend fehlt), bei Ergebnissen zu unbekannten Texten oder zu einer anderen Eingabe
  (auch ohne Hash), wenn ein Text der zurückgehaltenen Menge oder der gepflanzte Elternabend keine Zeile hat, bei weniger als
  3 beantworteten sauberen oder fehlerhaften Texten, bei Gültigkeit unter 90 % und wenn ein erwarteter Fehler im eigenen Text
  nicht auffindbar ist. Die Gesamtzeile ist „FAILED“, sobald ein Teil an seinen Daten scheitert.
- **Eingabe-Hash:** V2 hasht Systemprompt, Schemaname, Schema und Nutzerprompt (Material samt Volltext der Dateien und Text);
  V1 hasht Modell, Systemprompt, Nutzerprompt und Hintergrunddokument. Ändert sich eines davon, passen alte Zeilen nicht mehr;
  dann eine neue Ergebnisdatei beginnen.
- **Gültigkeit** zählt jeden Versuch, auch Wiederholungen. Eine `Ok`-Antwort über der Produktionsgrenze
  (`--production-timeout-seconds`, Standard `Direct:FactCheckTimeout`, heute 90 s) oder eine, die sich nicht nachspielen lässt,
  ist ungültig. `run` nimmt dieselbe Grenze als Standard für `--timeout-seconds` und schickt sie wie der Produktionscode 5 s früher
  als `x-cli-proxy-timeout-seconds` an das Gateway.
- **Latenz** nur aus Antworten (auch unbrauchbaren) und Zeitüberschreitungen; schnelle Fehlschläge schönen den Median nicht.
- **Trefferquote** (Fragen a, b, `superseded`): gepflanzte oder beschriftete Fehler, deren Wortlaut sich mit dem `text_quote`
  eines blockierenden Befunds überschneidet (Fundstellen über `QuoteVerifier`). Gepflanzte stille Summen (`silent_total`) und
  Quellenfehler gehören zu Frage c und werden nur berichtet. Texte ohne gültige Antwort zählen weder als Treffer noch als still.
- **V1:** Jeder Versuch zählt; ein gescheiterter oder abgelaufener zählt als „nicht Ok“. Eine einzige Beschädigung (Rest des
  Blocks im Text, Text nach dem Block oder zwei Blöcke, Abbruch an der Token-Grenze) ergibt C.
- **V3:** Treffer = eine Meldung, deren angegebene Summe der Spur entspricht; Fehlalarm = ein sauberer Ausschnitt mit
  irgendeiner Meldung. Nicht auswertbar ohne 60 Ausschnitte (30 gepflanzt), bei einer Spur, die der Code nicht nachrechnen kann,
  mit weniger als 5 gepflanzten oder sauberen Ausschnitten in der zurückgehaltenen Menge oder solange die Prüfung fehlt.

## Abweichungen vom Vertrag (Etappe 3)

- **Zurückgehaltene Menge V2:** feste Grundlagen `fc-02`, `fc-04`, `fc-06`, `fc-10`, `fc-14` statt Hash-Teilung über den Auftrag
  (die Hash-Teilung hielt nur eine Grundlage zurück); vor dem ersten Prüferlauf festgelegt.
- **Korpuskappe** 60 statt 48 Claude-Aufrufe (16 × 3 plus Wiederholungen ungültiger Antworten).
- **Lange Texte** ab 1.500 statt 2.000 Wörtern.
- **Neutralisierte Daten:** Rufnummern im Korpus auf Nullen gesetzt, Adressen mit der Endung `.example`; vor dem ersten
  Prüferlauf, die Eingabe-Hashes der Ergebnisse passen dazu.
- **Codex-Wächter** hält nur bei laufenden `codex … exec`-Aufträgen.
- **Ergebnis V2 (Runde 1):** Alle Qualitätswerte bestanden, Gültigkeit und Latenz verfehlt (zwei Hänger in der zurückgehaltenen
  Menge). Das Tor gilt als **nicht bestanden**; die Auslieferung in `Report` ist eine dokumentierte Abweichung (Entscheidungslog
  D-073). `FactCheckCorpusTests` hält genau diese Verfehlung fest und wird rot, sobald ein Qualitätswert schlechter wird. Die
  Zweige des Entscheidungsbaums für Gültigkeit und Latenz (Parser/Schema prüfen, `reasoning_effort: low`) wurden nicht
  ausgeführt, weil keine Antwort ankam, also weder Parser noch Schema beteiligt waren.

## Code und Tests

Der Messcode liegt unter `tests/Geef.Atelier.Tests/FactCheck/Evaluation/` und wird in dieses Projekt verlinkt, damit Werkzeug und
Tests mit demselben Code urteilen. `FactCheckCorpusTests` spielt gespeicherte Rohantworten (`FactCheck/Corpus/results-*.jsonl`)
ohne Modell durch Parser und Filter nach; ohne Ergebnisdatei sind alle Teile von Tor V2 „nicht auswertbar“.

Voraussetzungen im Produktionscode: `probe-notes`/`probe-evaluate` brauchen `WriterNotesFormat.Split` (R1) und die Notizanweisung
in `DirectWriterPrompts` (R2); `run`/`evaluate` brauchen Prompts, Parser und `LlmFactChecker` (R3a) sowie `FactFindingFilter`,
`QuoteVerifier` und für den Elternabend `SourceArithmeticCheck` (R1); `arithmetic` braucht `SourceArithmeticCheck` (R1).
