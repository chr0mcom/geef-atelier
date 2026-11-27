# tools/Eval – Messwerkzeug für Geef.Atelier

Ein kleines, dauerhaftes Werkzeug, um die Textqualität des Ateliers gegen Vergleichswege zu messen: ein festes
Aufgabenset, mehrere **Arme** (z. B. roher Einzelaufruf gegen das laufende Atelier), ein **blinder Paarvergleich**
durch einen Juror und ein Markdown-Bericht. Vorlage waren die Messungen vom 2026-11-24
(Rohdaten außerhalb des Repos beim Betreiber); deren Daten gehören nicht ins Repo.

Nur Python-Standardbibliothek plus `subprocess`; die Tests brauchen `pytest`.

## Schnellstart

```bash
export EVAL_TASKS_DIR=/pfad/zu/aufgaben          # echte Aufträge: außerhalb von Git
export EVAL_ATELIER_URL=https://geef.example      # nur für Arm "atelier"
export EVAL_ATELIER_TOKEN=…                       # nur für Arm "atelier", nie ins Repo

python3 tools/Eval/eval.py all --run-name messung-etappe3 --arms roh,atelier
```

Die Befehle einzeln:

| Befehl | Wirkung |
|---|---|
| `run` | erzeugt je Aufgabe und Arm einen Text (`outputs/`) |
| `judge` | blinder Paarvergleich aller Armpaare, je Paar zwei Durchgänge mit vertauschter Reihenfolge (`judgments/`, Zuordnung getrennt in `assignments/`) |
| `report` | schreibt `bericht.md` |
| `all` | alles nacheinander |

Gemeinsame Optionen: `--run-name` (Pflicht), `--tasks` (sonst `EVAL_TASKS_DIR`), `--arms roh,atelier`,
`--plugin name=modul:attribut`, `--out`, `--seed`, `--max-judge-calls N`, `--codex-juror`, `--allow-in-repo`.

Alles ist **wiederaufnehmbar**: Erfolgreiche Texte und Urteile auf der Platte werden nie neu erzeugt; fehlgeschlagene
werden beim nächsten Aufruf erneut versucht. Ein abgebrochener Lauf wird mit demselben Befehl fortgesetzt.

Der Seed gehört zum Laufverzeichnis: Der erste Aufruf legt ihn (zusammen mit der Zuordnungsversion) in
`manifest.json` ab. Ohne `--seed`/`EVAL_SEED` nutzt jeder weitere Aufruf den gespeicherten Seed; ein abweichender
Seed wird mit einer Meldung abgelehnt, weil er bestehende Urteile dem falschen Arm zuordnen würde. Für einen anderen
Seed einen neuen `--run-name` wählen.

Probelauf mit den synthetischen Beispielen (3 Opus-Aufrufe, 6 Jurorenaufrufe, ohne Atelier):

```bash
python3 tools/Eval/eval.py all --run-name probe --tasks tools/Eval/beispiele \
  --arms roh,referenz --max-judge-calls 6 --out /tmp/geef-eval
```

## Arme

| Arm | Was passiert |
|---|---|
| `roh` | ein Aufruf `claude -p --model opus` ohne Werkzeuge, Prompt per stdin, leeres Arbeitsverzeichnis; Anhänge als `<anhang>`-Blöcke vor dem Kurzauftrag |
| `roh-sonnet` | wie `roh`, mit Sonnet |
| `atelier` | das laufende Atelier über den MCP-Endpunkt mit den Auftragswerkzeugen: `submit_order`; stellt Atelier Rückfragen, einmal `answer_order` mit `skip=true`; dann `get_order_status` (der Server wartet je Aufruf bis zu 25 s auf eine Änderung, dazwischen 10 s Pause, bis 60 min), bis der Auftrag fertig ist **und** `outcome` gesetzt ist, erst dann `get_order_result` (der Text ist erst mit `outcome` endgültig). Als Ergebnis zählen `Converged` und `DeliveredWithOpenPoints` mit Text (Entscheidung vom 2026-07-17). `outcome` `Failed` oder `Aborted` ist ein Fehler des Arms, ebenso ein gescheiterter oder gestoppter Auftrag ohne Lauf und ein Zeitlimit ohne `outcome` |
| `referenz` | der `referenztext` aus der Aufgabe (feste Vergleichsgröße, auch für Probeläufe) |
| eigene | `--plugin name=paket.modul:Fabrik` oder `--plugin name=pfad/datei.py:Fabrik`; die Fabrik wird mit `name=` aufgerufen und liefert ein Objekt mit `name` und `produce(task) -> ArmOutput` |

Die Namen der MCP-Werkzeuge stehen für diesen Arm nur in `geef_eval/mcp.py`, Klasse `AtelierToolAdapter`. Anhänge gehen als
Base64 in `attachments` mit (das Upload-Ticket nutzt der Arm nicht).

## Bewertung

- Juror: `claude -p --model sonnet`, je Paar **zwei Durchgänge** mit vertauschter Reihenfolge. Die Reihenfolge im
  ersten Durchgang ist zufällig, aber durch `--seed` festgelegt; die Zuordnung Text → Arm wird getrennt protokolliert,
  der Juror sieht nie Armnamen.
- Maßstab ist das zurückgehaltene `original_briefing`, sonst der Kurzauftrag. Der Juror antwortet als JSON mit
  `rangfolge`, `abstand` (`klar | knapp | gleichwertig`), `verstoesse` (harte Vorgaben) und `erfunden`
  (nicht gedeckte Tatsachen) je Text.
- Klassen je Aufgabe und Paar: Sieg (beide Durchgänge gleich), uneinig, Tendenz, gleich. Aggregat mit exaktem
  zweiseitigem Vorzeichentest.
- Deterministische Kennzahlen: Wortzahl, Platzhalter in eckigen Klammern, Längenvorgabe (aus `laenge` der Aufgabe,
  sonst aus Kurzauftrag oder Original-Briefing gelesen: „höchstens 80 Wörter“, „zwischen 200 und 300 Wörtern“,
  „ca. 400 Wörter“ = ±15 %, englische Formen ebenso).

## Aufgabenformat

Ein Verzeichnis, je Aufgabe eine Datei. Dateien, die mit `.` oder `_` beginnen, und `README.md` werden übersprungen.
Drei synthetische Beispiele liegen in `beispiele/`.

**JSON** (`<id>.json`):

```json
{
  "id": "optional, sonst Dateiname",
  "kurzauftrag": "Pflicht: das, was die Arme bekommen",
  "anhaenge": [
    {"name": "notiz.md", "text": "Inhalt inline"},
    {"name": "vorlage.pdf", "pfad": "dateien/vorlage.pdf"}
  ],
  "original_briefing": "optional: zurückgehaltener Maßstab, sieht nur der Juror",
  "referenztext": "optional: Text für den Arm 'referenz'",
  "laenge": {"min": 100, "max": 200}
}
```

Anhänge: `.md`, `.txt` (Text) und `.pdf` (nur per `pfad`; der Arm `roh` kann PDFs nicht lesen und vermerkt das,
der Arm `atelier` lädt sie hoch). Pfade sind relativ zur Aufgabendatei und müssen im Aufgabenordner bleiben:
absolute Pfade, `..` nach außen und Symlinks, die aus dem Ordner herausführen, lehnt das Werkzeug mit einer
Fehlermeldung ab. So kann eine geteilte Aufgabendatei keine fremden Dateien an das Atelier oder an Claude schicken.

**Markdown** (`<id>.md`) mit den Abschnitten `# Kurzauftrag` (Pflicht), `# Original-Briefing`, `# Referenztext`
und beliebig vielen `# Anhang: name.md`.

## Umgebungsvariablen

| Variable | Bedeutung |
|---|---|
| `EVAL_TASKS_DIR` | Aufgabenverzeichnis (statt `--tasks`) |
| `EVAL_OUT_DIR` | Ausgabeordner (Standard `~/.local/state/geef-eval`) |
| `EVAL_ATELIER_URL` | Basis-URL des Ateliers; `/mcp` wird angehängt. Weiterleitungen werden nicht verfolgt (das Token bleibt beim eingestellten Ursprung); bei einer Weiterleitung die Zieladresse eintragen |
| `EVAL_ATELIER_TOKEN` | Bearer-Token für den MCP-Endpunkt |
| `EVAL_SEED` | Seed der Reihenfolge (statt `--seed`); muss beim Fortsetzen zum gespeicherten Seed passen |
| `EVAL_CODEX_MAX` | Obergrenze der Codex-Jurorenaufrufe je Lauf; ohne Wert bleibt der Codex-Juror aus |

## Datenschutz

- **Echte Aufträge gehören nicht in Git.** Aufgaben über `EVAL_TASKS_DIR` von außerhalb des Repos einbinden.
- Die Ergebnisse landen standardmäßig unter `~/.local/state/geef-eval/`. Liegt der Ausgabeordner in einem
  Git-Repository, bricht das Werkzeug ab (nur mit `--allow-in-repo` überstimmbar).
- Der Bericht enthält nur Kennzahlen und Urteilsklassen, keine Texte und keine Juror-Begründungen. Wer ihn unter
  `reports/` ablegt, prüft trotzdem die Aufgaben-IDs (Dateinamen) auf Personenbezug.
- Tokens nie in Dateien schreiben oder ausgeben.

## Kontingent

- Jeder Arm-Aufruf und jedes Urteil kostet Abo-Kontingent (Claude). Höchstens **2 CLI-Aufrufe gleichzeitig**;
  Zeitlimit je Aufruf (Arm 15 min, Juror 10 min). Meldet die CLI ein Nutzungslimit, hält der Lauf an und lässt
  sich später fortsetzen.
- Faustregel je Lauf: Aufgaben × Arme Textaufrufe plus Aufgaben × Paare × 2 Urteile. `--max-judge-calls` deckelt
  die Zahl neuer Urteile; liefert der Juror ungültiges JSON, wird dasselbe Urteil einmal wiederholt (also höchstens
  doppelt so viele CLI-Aufrufe wie Urteile). Jeder CLI-Aufruf steht in `calls.jsonl`.
- **Codex ist aus.** Das Codex-Kontingent ist oft mit anderen Anwendungen geteilt. Der optionale zweite Juror (`--codex-juror`) startet nur mit
  `EVAL_CODEX_MAX > 0`, prüft vor jedem Aufruf `pgrep -a codex` und überspringt sich, solange Codex anderweitig
  läuft.

## Tests

```bash
cd tools/Eval && python3 -m pytest -q
```

Die Tests laufen vollständig offline mit Attrappen; kein Test ruft ein Modell auf. Ausnahme auf Wunsch:
`tests/test_upload_check_postgres.py` prüft die Abfragen von `upload_check` gegen ein echtes, **wegwerfbares** Postgres
(Containername in `EVAL_PG_TEST_CONTAINER`, Anleitung im Kopf der Datei); ohne die Variable wird er übersprungen.

## Klärer-Korpus (`geef_eval/intake_corpus.py`)

Zweck: ein synthetischer Korpus, mit dem sich messen lässt, ob der Klärer (die Rückfragen vor dem Schreiben) die
richtigen Fragen stellt. Jeder Eintrag ist eine erfundene Person mit verborgener, vollständiger Absicht (`intent`) und
einem Auftrag (`request`) in ihren eigenen Worten. Das Messwerkzeug selbst ist `tools/IntakeEval` (.NET, getrennt gebaut).

```bash
cd tools/Eval
python3 -m geef_eval.intake_corpus generate --out intake-corpus.jsonl --second 0 [--max-calls N] [--model sonnet]
python3 -m geef_eval.intake_corpus generate --out intake-corpus-second.jsonl --main 0 --second 40
python3 -m geef_eval.intake_corpus label --corpus intake-corpus.jsonl [--max-calls N] [--model opus]
python3 -m geef_eval.intake_corpus stats --corpus intake-corpus.jsonl
```

| Befehl | Wirkung |
|---|---|
| `generate` | ein Sonnet-Aufruf je Eintrag; Hauptkorpus 120 (`sparse` 50, `complete` 40, `revision` 10, `researchable` 10, `profile_covered` 10; etwa 70 % `de`, 30 % `en`, dazu genau 4 × `fr` als Gegenprobe), Zweitkorpus 40 im Eiltipp-Stil (Kleinschreibung, Tippfehler, halbe Sätze). Die Belegung folgt deterministisch aus Index und `--seed`; ein abgebrochener Lauf macht mit demselben Befehl genau dort weiter |
| `label` | zweiter, unabhängiger Aufruf mit Opus: sieht Auftrag, Dateien, Profil und Absicht (nie die Generator-Angaben) und beschriftet je Thema `necessary_ask`, `acceptable_ask` oder `forbidden_ask`; schreibt `labels`, `label_note`, `label_model` atomar in dieselbe Datei und überspringt beschriftete Einträge |
| `stats` | zählt je Korpus, Kategorie, Sprache, Teilung (`dev`/`holdout`) und Beschriftungsstand |

`--max-calls` kappt die Zahl der Modellaufrufe je Lauf. Ungültige Antworten werden nicht geschrieben und beim nächsten
Lauf erneut versucht; ein Nutzungslimit beendet den Lauf mit Meldung und Rückgabewert 3. Fehlen nach `generate` noch
Einträge oder sind nach `label` noch Einträge unbeschriftet (Abbruch, `--max-calls`, verworfene Antworten), endet der
Befehl mit Rückgabewert 4: Der Korpus ist dann unvollständig und für `tools/IntakeEval evaluate` nicht auswertbar.

Eintragsformat (JSONL, eine Zeile je Eintrag, UTF-8 ohne ASCII-Escapes):

```json
{"id": "main-017", "corpus": "main", "category": "sparse", "lang": "de",
 "request": "…", "files": [{"name": "brief.txt", "kind": "text", "text": "…", "pages": null}],
 "profile": null, "intent": "…", "generator_model": "sonnet",
 "labels": {"length": "necessary_ask", "audience": "forbidden_ask"}, "label_note": "…", "label_model": "opus"}
```

`files[].kind` ist `text`, `markdown`, `pdf` oder `docx` (nur Etikett, der Inhalt ist Klartext). `profile` ist `null` oder
`{display_name, role, organization, formality (unset|formal|informal), language, signature, no_gos, free_text}`.
Themen: `length`, `audience`, `purpose`, `tone`, `formality`, `language`, `sender`, `signature`, `facts`, `format`,
`date`, `scope`, `other`. `labels` fehlt, solange der Eintrag nicht beschriftet ist.

Teilung 70/30: `split(request)` bildet SHA-256 über die UTF-8-Bytes des Auftrags; erstes Byte `% 10 < 7` ergibt `dev`,
sonst `holdout` (wie `LengthCorpusTests.InDevelopmentSet` im C#-Test). Die Teilung wird nicht im Eintrag gespeichert.

Betrieb:

- Der Korpus ist synthetisch (alle Personen und Firmen erfunden) und darf ins öffentliche Git. Ablage:
  `tests/Geef.Atelier.Tests/Intake/Corpus/intake-corpus.jsonl` und `intake-corpus-second.jsonl`.
- Vor einem Lauf den aktuellen Stand prüfen: Nutzt gerade etwas anderes dasselbe Claude-Kontingent, ist das Gateway gesund und
  ist genug Kontingent frei? Nur dann starten (feste Zeitfenster gibt es nicht).
- Der Auftraggeber prüft 20 Einträge stichprobenartig und entfernt widersprüchliche.

## Faktenprüfer- und Rechenkorpus (`geef_eval/factcheck_corpus.py`)

Zweck: synthetische Korpora für die Vorversuche der Etappe 3. V2 eicht den Faktenprüfer an Texten mit bekannten Fehlern und an
sauberen Texten, V3 misst die Rechenprüfung der Quellen (`SourceArithmeticCheck`). Das Messwerkzeug ist `tools/FactCheckEval`
(.NET, getrennt gebaut, siehe dessen README).

```bash
cd tools/Eval
python3 -m geef_eval.factcheck_corpus generate --corpus factcheck-corpus.jsonl --max-calls 16 [--model sonnet]
python3 -m geef_eval.factcheck_corpus label --corpus factcheck-corpus.jsonl --max-calls 16 [--model opus]
python3 -m geef_eval.factcheck_corpus plant --corpus factcheck-corpus.jsonl --max-calls 16 [--model sonnet]
python3 -m geef_eval.factcheck_corpus arithmetic --out arithmetic-corpus.jsonl --max-calls 12 [--model sonnet]
python3 -m geef_eval.factcheck_corpus stats --corpus factcheck-corpus.jsonl
```

| Befehl | Wirkung |
|---|---|
| `generate` | ein Sonnet-Aufruf je Grundlage, 16 feste Plätze: 8 kurze (120–600 Wörter) und 4 lange (mindestens 1.500 Wörter; der Vertrag nannte 2.000, die Untergrenze wurde vor dem ersten Prüferlauf gesenkt, weil der Generator die 2.000 nicht zuverlässig erreichte) gewöhnliche (8 `de`, 4 `en`, darunter 2 Überarbeitungen mit Ausgangstext als Quelle `S0` und 2 mit Profil und `no_gos`) und 4 mit fehlerhafter Quelle: der feste **Elternabend-Nachbau** (Notizen von Hand, falsche Summe 9 + 6 + 4 = „17 €“, verschobener Termin, der Zahlungsfrist bleibt, verworfener Raum, interne Notiz, Allergiehinweis; das Modell schreibt nur den sauberen Brief), widersprüchliche Beträge in zwei Dateien, überholter Beschluss im Protokoll, falsche Zwischensumme in einer Tabelle. Jede Antwort liefert Auftrag, Antworten, Profil, Dateien und einen **sauberen Text** nur aus dem Material; Quellenfehler werden als wörtliche Zitate geliefert und geprüft |
| `label` | unabhängiger Opus-Aufruf je Grundlage: markiert Aussagen des sauberen Texts, die das Material nicht deckt, mit wörtlichem Zitat (mindestens fünf Wörter). Ein markierter Text gilt als fehlerhaft und behält die Markierungen als erwartete Fehler. Der Generator sieht die Beschriftung nie |
| `plant` | Sonnet-Aufruf je Grundlage: 2–4 Fehler in einer Kopie (geänderte Zahl, Datum oder Name; erfundene Tatsache; erfundener Rückmeldeweg; behauptete Anlage oder Handlung; verletzte Vorgabe; verwendete verworfene Angabe; still übernommene oder korrigierte Summe). Jeder eingefügte Wortlaut muss wörtlich in der Kopie und darf nicht im sauberen Text stehen; der Elternabend braucht einen erfundenen Rückmeldeweg. Sonst wird die Antwort verworfen und beim nächsten Lauf neu angefragt |
| `arithmetic` | 60 Ausschnitte, je 5 je Sonnet-Aufruf: 30 mit gepflanzt falscher Summe, 15 mit richtiger Rechnung, 15 mit Zahlen ohne Rechnung (Datum, Hausnummer, Uhrzeit, Telefonnummer, Preis ohne Summe); Formen Rechnung, Liste, Satz, Tabelle, Notizen; `de` und `en`. Der Code rechnet jede Spur nach (Posten ergeben die richtige Summe, die angegebene weicht bei gepflanzten ab und stimmt bei sauberen); ein Ausschnitt mit falscher Spur wird verworfen und bleibt offen |
| `stats` | zählt Grundlagen je Größe, Sprache, Art, Teilung, Beschriftung, Fehlerart und die bisher verbrauchten Aufrufe (beim Rechenkorpus Form, Sprache, gepflanzt, Teilung) |

`--max-calls` ist Pflicht. Jeder Aufrufversuch steht in `<datei>.calls.log` (JSON-Zeilen mit Bezeichnung, Modell, Erfolg,
Fehler, ohne Texte; bewusst nicht `*.jsonl`, damit kein Korpus-Glob sie erfasst) und zählt gegen die feste Kappe des Korpus über alle Läufe: 60 für den Faktenprüfer-Korpus (16 × 3 plus Wiederholungen), 12 für den
Rechenkorpus. Eine ungültige Antwort wird einmal wiederholt, solange die Kappe reicht. Rückgabewerte wie beim Klärer-Korpus:
3 beim Nutzungslimit, 4 solange Grundlagen oder Ausschnitte fehlen (auch wenn die Kappe verbraucht ist).

Formate (JSONL, UTF-8): Faktenprüfer-Grundlage

```json
{"id": "fc-13", "size": "short", "lang": "de", "kind": "faulty_source", "case": "elternabend",
 "request": "…", "answers": [], "profile": null,
 "files": [{"id": "S1", "name": "notizen-elternabend.txt", "text": "…"}],
 "source_errors": [{"kind": "wrong_total", "quote": "Gesamt pro Kind: 17 €", "quote_2": null, "stated": "17"}],
 "clean_text": "…", "generator_model": "sonnet",
 "labels": [{"quote": "…", "why": "…"}], "label_model": "opus",
 "planted_text": "…", "plants": [{"kind": "invented_contact", "inserted": "…"}], "plant_model": "sonnet"}
```

Rechenausschnitt (`trace` ist `null` bei Zahlen ohne Rechnung; `ops` leer heißt: alle Posten addiert):

```json
{"id": "ar-01", "lang": "de", "form": "list", "text": "…", "planted": true,
 "trace": {"total_line": "Summe: 62 €", "items": ["22 €", "31 €", "19 €"], "ops": [], "stated": "62", "correct": "72"},
 "generator_model": "sonnet"}
```

Teilung: Der Faktenprüfer-Korpus hält die Grundlagen `fc-02`, `fc-04`, `fc-06`, `fc-10` und `fc-14` fest zurück (kurz und lang,
eine Überarbeitung, ein Profil, eine fehlerhafte Quelle); alle anderen gehören zur Entwicklungsmenge, der Elternabend immer. Die
Liste steht gleichlautend in `factcheck_corpus.HELD_OUT_IDS` und `FactCheckCorpus.HeldOutIds` und wurde vor dem ersten Prüferlauf
festgelegt: Die Hash-Teilung über den Auftrag (Vertrag) hatte nur eine Grundlage zurückgehalten, das Tor braucht mindestens drei
saubere und drei fehlerhafte Texte. Der Rechenkorpus teilt weiter 70/30 über SHA-256 des Ausschnitts. Beide Korpora sind
synthetisch und dürfen ins öffentliche Git; Rufnummern in den Texten sind nachträglich auf Nullen gesetzt (etwa `0170 0000000`),
Mail- und Webadressen nutzen die reservierte Endung `.example`. Ablage
`tests/Geef.Atelier.Tests/FactCheck/Corpus/factcheck-corpus.jsonl` und
`tests/Geef.Atelier.Tests/Domain/Checks/Corpus/arithmetic-corpus.jsonl`. Für die Läufe gilt dieselbe Zustandsprüfung wie beim
Klärer-Korpus.

## Recherche: Nachprüfung, Paarvergleich R3 und Tor R2 (Etappe 4)

**Nachprüfung der Dossier-Einträge** (`geef_eval/research_verify.py`, eigene Implementierung): Jeder Eintrag muss wörtlich in
seinem gespeicherten Rohtext stehen (nur Leerraum vereinheitlicht, Groß/Klein zählt, Bereich in UTF-16 wie in .NET). Der
gespeicherte Bereich darf weder in einem Wort noch in einer Zahl beginnen oder enden (Verstoß `word_cut`): Wortteil sind wie im
`DossierBuilder` Buchstaben, Ziffern, das weiche Trennzeichen U+00AD und kombinierende Zeichen; außerdem darf keine Grenze eine
Zahl mit Trenner zerteilen (Ziffer, `.` oder `,`, Ziffer) und keine Zahl mit Leerzeichen oder Apostroph als Tausendertrenner
(1–3 Ziffern, Leerzeichen/schmales Leerzeichen/`'`/`’`, genau drei Ziffern: `1 500`, `4'850`). Liest die
Zeilen von `tools/ResearchEval run` (`--results`) und die Audit-Dateien des Ateliers (`--audit`, Datei oder Verzeichnis, Format
von `OrderResearchService`). Rückgabe 0 sauber, 1 Verstoß, 2 falscher Aufruf, 4 nichts geprüft.

**Paarvergleich R3** (`geef_eval/research_pairs.py`): acht unberührte Entwicklungsaufträge mit Suchthemen laufen je zweimal
über MCP durch das laufende Atelier, einmal je Messnutzer: Arm `on` (Recherche an), Arm `off` (aus), beide ohne Faktenprüfung.
Ein blinder Sonnet-Juror beurteilt je Auftrag beide Texte mit zwei Arten von Aufrufen, jede in zwei Durchgängen mit
vertauschter Reihenfolge: `rank` **ohne** nachgeschlagene Ausschnitte (Siege und Platzhalter) und `facts` **mit** den Ausschnitten
des Arms `on` (erfundene Tatsachen).

```bash
cd tools/Eval
C=../../tests/Geef.Atelier.Tests/Intake/Corpus
SETS="--intake-results $C/results-20261125.jsonl --corpus $C/intake-corpus.jsonl --corpus $C/intake-corpus-second.jsonl"
python3 -m geef_eval.research_pairs select $SETS
python3 -m geef_eval.research_pairs run $SETS --out-dir $EVAL_RESEARCH_DIR/r3 --max-submits 16 --codex-lock <sperrdatei>
python3 -m geef_eval.research_pairs judge $SETS --out-dir $EVAL_RESEARCH_DIR/r3 --max-judge-calls 32 --codex-lock <sperrdatei>
python3 -m geef_eval.research_pairs evaluate $SETS --out-dir $EVAL_RESEARCH_DIR/r3 [--confirmations <datei.json>]
```

| Befehl | Wirkung |
|---|---|
| `select` | zeigt die acht Aufträge. Regel: von den 15 nach VR unberührten Entwicklungsaufträgen (`RESERVED_IDS`, gleichlautend im C#-Test `ResearchPagesTests`) die ersten acht nach Kennung **ohne Profil** (über MCP bekommt ein Auftrag kein eigenes Profil; beide Messnutzer schreiben mit ihrem eigenen, gleichen) und **ohne Überarbeitung** (die recherchiert nie). Die Liste steht fest in `PAIR_IDS`; andere Daten ergeben einen Fehler, nie eine andere Menge. |
| `run` | je Auftrag und Arm genau eine Einreichung (`submit_order`), Fragen werden in beiden Armen gleich übersprungen (`answer_order` mit `skip`; recherchierbare Themen fragt das Atelier nie), dann `get_order_status` bis fertig und `get_order_result`. Welcher Arm zuerst läuft, wechselt je Auftrag. Jeder Versuch zählt und wird nie wiederholt. |
| `judge` | vier Urteile je Paar, das zählen kann (beide Texte geliefert, Arm `on` mit `found` und mindestens einem Eintrag; andere Paare kosten kein Urteil): zweimal `rank`, zweimal `facts`, Dateien `judgments/<id>/<art>_pass<n>.json`. Der Juror sieht nie einen Armnamen. Ungültiges JSON wird einmal wiederholt und sonst nicht gespeichert. |
| `evaluate` | Tor R3, schreibt `r3-gate.json` (je Auftrag der `lookedUp`-Status beider Arme unter `statuses`, beide Zählungen unter `fabricated_counts`, die Handfälle unter `exception_cases`). |

**Die zwei Juror-Aufrufe:** `rank` sieht Absicht, Auftrag, Dateien, Themen und die zwei Texte, aber keinen Ausschnitt. Er liefert
Rangfolge, Abstand und `platzhalter` (Stellen, an denen eine recherchierbare Tatsache fehlt oder als Platzhalter steht). Er soll
beim Ordnen auch Erfundenes beachten; ohne Ausschnitte kann ihm dabei Nachgeschlagenes als erfunden erscheinen, was nur gegen
Arm `on` wirken kann. `facts` sieht zusätzlich die nummerierten Ausschnitte. Er listet je Text jede Tatsache, die weder Absicht,
Auftrag noch Dateien decken (**auch** wenn ein Ausschnitt sie deckt), und nennt je Tatsache unter `ausschnitt` die Nummer des
Ausschnitts, der sie wörtlich deckt, sonst `null`. Ein Text, der Ausschnitte wiedergibt, kann in `facts` als Arm `on`
erkennbar sein; deshalb entscheidet dieser Aufruf weder Siege noch Platzhalter.

**Tor R3** (Vertrag 8, vorab festgelegt): (a) Siege mit Recherche ≥ Siege ohne + 2 (Sieg = beide `rank`-Durchgänge gleich),
(b) Summe `erfunden` mit ≤ ohne, (c) Summe `platzhalter` (aus `rank`) mit < ohne (je Auftrag Mittel der zwei Durchgänge).

**Gültige Paare:** *Technisch vollständig* ist ein Paar, wenn beide Texte geliefert sind und, falls es zählen kann, alle vier
Urteile vorliegen; mindestens **7 von 8** (`MIN_DELIVERED_PAIRS`). *Gültig* (nur diese gehen in a–c ein) ist ein technisch
vollständiges Paar, dessen Arm `on` `lookedUp.status = found` mit mindestens einem Eintrag hat; mindestens **6 von 8**
(`MIN_FOUND_PAIRS`). `failed`, `daily_limit`, `all_withheld`, `nothing_found`, `files_attached`, jeder andere Status, `found`
ohne Eintrag und `lookedUp = null` machen das Paar ungültig. Beide Schwellen gelten zugleich.

**Zwei Zählungen für `erfunden`** (je Auftrag und Arm das Mittel der zwei `facts`-Durchgänge):

- **streng:** jede gelistete Tatsache zählt.
- **mit Wortlaut-Ausnahme:** Tatsachen mit Ausschnittnummer zählen nicht, gleich in welchem der zwei Texte sie stehen.

Jede Tatsache, die nur durch die Ausnahme nicht zählt, ist ein **Fall** (je Auftrag, Durchgang, Arm und Listenplatz, z. B.
`p1-on-2`; dieselbe Aussage in zwei Durchgängen sind zwei Fälle). Jeder Fall ist vor einem `On` von Hand gegen die Absicht zu
prüfen. Regel:

| Lage | gezählt | Tor |
|---|---|---|
| kein Fall | streng (beide Zählungen sind gleich) | nach a–c |
| mindestens ein Fall ohne gültige Entscheidung oder ein Fehler in der Bestätigungsdatei | streng | `not_evaluable` |
| alle Fälle gültig entschieden | mit Ausnahme: `not_invented` zählt nicht, `invented` zählt als erfunden | nach a–c |

**Bestätigungsdatei** (`--confirmations`, außerhalb von Git ablegen). `order`, `case` und `fact` werden unverändert aus
`exception_cases` in `r3-gate.json` übernommen; dort stehen zum Prüfen auch Ausschnitt (`passage`) und Adresse (`url`).
`not_invented` heißt: von Hand geprüft, die Tatsache verträgt sich mit der Absicht. `invented` heißt: sie widerspricht ihr, ist
falsch oder veraltet.

```json
{"confirmations": [
  {"order": "main-063", "case": "p1-on-2", "fact": "<Wortlaut aus r3-gate.json>", "decision": "not_invented"}
]}
```

Weitere Felder (etwa `note`) sind erlaubt. Als Fehler zählen: unbekannter Fall, abweichender Wortlaut, doppelter Eintrag, andere
Entscheidung, Eintrag, der kein Objekt ist, fehlende Liste `confirmations`. Eine nicht lesbare Datei ergibt Rückgabe 2. Nach neuen
Urteilen passen alte Bestätigungen nicht mehr.

**Außerdem nicht auswertbar:** eine Ausgabe verletzt die Arm-Konfiguration (Arm `off` mit Recherche, ein Arm mit Faktenprüfung),
Arm `on` zeigt in keinem Ergebnis eine Recherche mit Fundstellen, eine Zuordnung weicht vom Seed ab oder im Verzeichnis liegen
Ausgaben zu fremden Aufträgen.

**Schutz:** `--max-submits` (höchstens 16 je Verzeichnis) und `--max-judge-calls` (höchstens 32 Urteile je Verzeichnis:
8 Paare × 2 Arten × 2 Durchgänge) sind
Pflicht; vor jeder Einreichung und jedem Urteil hält der Codex-Wächter an (Sperrdatei oder `pgrep -a codex`, Rückgabe 3).
`run` hält sofort an (Rückgabe 2), wenn eine Ausgabe die Arm-Konfiguration verletzt; liegt eine solche Zeile schon im
Verzeichnis, endet jeder weitere Aufruf mit Rückgabe 2, ohne etwas einzureichen. `run` bricht außerdem ab (Rückgabe 4), wenn eines
der ersten drei Paare technisch scheitert oder mehr als ein Paar gescheitert ist. Die MCP-Tokens kommen nur aus
`EVAL_R3_TOKEN_ON` und `EVAL_R3_TOKEN_OFF` (verschieden, nie geschrieben), die Adresse aus `EVAL_ATELIER_URL`. Das
Ausgabeverzeichnis muss außerhalb jedes Git-Baums liegen.

**Voraussetzung im Betrieb:** zwei Messnutzer mit leerem oder gleichem Profil; über die Umgebung des Web-Containers
`Research__ModeOverrides__<nutzer-on>=On`, `Research__ModeOverrides__<nutzer-off>=Off` und für beide
`Direct__FactCheckModeOverrides__<nutzer>=Off` (Nutzernamen nie ins Repository). Kosten: ≤ 16 Klärer- und ≤ 8 Condenser-Aufrufe,
16–32 Schreiberaufrufe, ≤ 32 Juror-Urteile (Sonnet: 16 `rank` ohne Ausschnitte und 16 `facts` mit Ausschnitten), zusammen
≤ 88 Claude-Aufrufe, 0 Codex, ≤ 32 Tavily-Suchen. Ungültiges JSON wird je Urteil einmal wiederholt, im ungünstigsten Fall also
64 CLI-Aufrufe für den Juror.

### Tor R2: der im Code geprüfte Teil (`geef_eval/research_r2.py`)

Eingabe sind die gespeicherten Antworten von `get_order_result` der R2-Aufträge, ein JSON-Objekt je Auftrag (`.json`, `.jsonl`
oder ein Verzeichnis mit `*.json`). Der Bericht enthält Zitate aus Texten und muss außerhalb jedes Git-Baums liegen.

```bash
cd tools/Eval
python3 -m geef_eval.research_r2 --results $EVAL_RESEARCH_DIR/r2/antworten --out $EVAL_RESEARCH_DIR/r2/r2-gate.json \
  [--confirmations $EVAL_RESEARCH_DIR/r2/bestaetigungen.json]
```

| Teil | Prüfung |
|---|---|
| (a) | **im Code:** jeder Auftrag hat `lookedUp` ungleich `null` und einen Status ungleich `not_needed` und `files_attached` (R2-Aufträge daher ohne Dateien) |
| (b) | **Handauswertung** (`manual`): 0 Fragen zu recherchierten Themen |
| (c) | **im Code und von Hand:** Zahl der `unbackedPassages` mit `kind = not_backed`, die die Recherche deckt; bestanden bei ≤ 1 insgesamt, ob blockierend oder nicht. Im Code zählt eine Stelle, deren Zitat eine Zahl enthält, die in Zitat oder Titel eines `lookedUp`-Eintrags desselben Auftrags vorkommt, in englischen Texten auch ein Name. In deutschen Texten ist eine Stelle, die ohne gemeinsame Zahl nur einen Namen mit einem Eintrag teilt, ein **Fall für die Bestätigungsdatei** |
| (d) | **Handauswertung** (`manual`): Median `DurationMs` ≤ 60 s (steht nicht in der MCP-Antwort) |
| (e) | **im Code:** 0 Notizmarker im Text (`<!-- atelier-notes`, Konstante `WriterNotesFormat.StartMarker`, in allen Schreibweisen, die der C#-Code annimmt, und in der entschärften Form) |

Zahl: Ziffernfolge; `.` und `,` zwischen Ziffern werden entfernt (`1.250,50` = `1,250.50` = `125050`), verglichen wird die ganze
Zahl; Datumsangaben zählen über ihre Ziffern. Eine Zahl mit Leerzeichen oder Apostroph vor Dreiergruppen (`1 500`, `4'850`)
zählt zusammengesetzt und in ihren Teilen (strenge Seite). Name: großgeschriebenes Wort ab 3 Buchstaben, das nicht am Satzanfang
steht (Anfang von Text oder Zeile, nach `.`, `!`, `?`; nach Ordnungszahlen und gängigen Abkürzungen wie `Dr.` nicht), verglichen
als ganzes Wort mit Groß/Klein; wo die Stelle im Text steht, entscheidet, ob ihr erstes Wort am Satzanfang steht. Sprache eines
Textes: Deutsch, außer er enthält mehr englische als deutsche Funktionswörter (feste Listen im Code); im Zweifel also Deutsch und
damit Handauswertung.

**Namen im Deutschen von Hand:** Im Deutschen ist jedes Substantiv großgeschrieben, ein gemeinsames großgeschriebenes Wort belegt
also nichts. Jede solche Stelle steht im Bericht als Fall (`orderId`, `case` = `n<Position in unbackedPassages>`, Zitat, gemeinsame
Wörter). Eine Person entscheidet je Fall, ob der Eintrag die Stelle deckt, und trägt das in die Bestätigungsdatei ein:
`{"confirmations": [{"order": "<orderId>", "case": "n2", "quote": "<Zitat wörtlich>", "decision": "covered" | "not_covered"}]}`.
`covered` zählt für (c), `not_covered` nicht. Solange ein Fall offen ist oder die Datei einen Fehler hat (unbekannter Fall,
anderer Wortlaut, ungültige Entscheidung, doppelt), ist das Ergebnis `not_evaluable`. Sinngemäße Deckung ohne gemeinsame Zahl und
ohne gemeinsamen Namen erkennt die Regel nicht.

**Nicht auswertbar:** weniger als 4 Aufträge mit Text, **weniger als 4 Aufträge mit `lookedUp.status = found` und mindestens
einem Eintrag** (sonst hätte (c) kaum Daten), ein Auftrag doppelt, ein Auftrag mit Text, dessen Faktenprüfung nicht für diesen
Text lief (`factCheck.state` weder `checked` noch `open_points`) oder dem `unbackedPassages` fehlt, ein offener Fall oder ein
Fehler der Bestätigungsdatei. Rückgabe 0: (a), (c), (e) bestanden (**R2 insgesamt erst mit (b) und (d) von Hand**), 1 ein geprüfter
Teil nicht bestanden, 2 falscher Aufruf oder unlesbare Eingabe, 4 nicht auswertbar.


## Etappe 5: Nachbessern (Tor N), MCP-Vertrag (Tor M) und 50-Sekunden-Aufruf (V-MCP)

Beide Werkzeuge reichen **nie** einen Auftrag ein, beantworten, ändern oder stoppen keinen und rufen kein Modell auf. Sie lesen
nur, was das Atelier zeigt (`tools/list`, `get_order_result`, `export_order`) oder was Claude Code mitgeschnitten hat. Alles, was
sie schreiben (Schnappschüsse, Mitschnitte, Berichte), enthält Texte echter Aufträge und muss außerhalb jedes Git-Baums liegen;
sonst brechen sie mit Rückgabe 2 ab. Anmeldung wie beim Smoke-Skript (`--env-file` im Format von `docs/ops/atelier-smoke.md`,
Modus 600/400, mit `ATELIER_SMOKE_BASE_URL`) oder `EVAL_ATELIER_URL` und `EVAL_ATELIER_TOKEN`. Tokens und Passwörter werden nie
geschrieben oder ausgegeben.

### Tor N (`geef_eval/revise_check.py`)

```bash
cd tools/Eval
python3 -m geef_eval.revise_check snapshot --order <orderId> --label v1 --out-dir $EVAL_N_DIR [--env-file <datei>]
# … Nachbesserung im Web oder über MCP, warten bis fertig …
python3 -m geef_eval.revise_check snapshot --order <orderId> --label v2 --out-dir $EVAL_N_DIR
python3 -m geef_eval.revise_check evaluate --plan $EVAL_N_DIR/plan.json --out-dir $EVAL_N_DIR
```

`snapshot` speichert die Antwort von `get_order_result` und `export_order` (Markdown, ohne Version) unter
`snapshots/<orderId>/<label>.json` und überschreibt nie. Offene Stellen und Schreiberpunkte einer Version zeigt nur
`get_order_result`, solange sie die gezeigte ist: den Schnappschuss der Ausgangsfassung **vor** der Nachbesserung ziehen. Für
N5 nach dem Stopp von v3 einen Schnappschuss (etwa `after-stop`) ziehen; er enthält den Export ohne Versionsangabe.

**Jeder Schnappschuss steht in höchstens einem Fall** (als Ausgangsfassung oder neue Fassung, der Stopp eingeschlossen). In einer
Kette schließt etwa `v2` die erste Nachbesserung ab; direkt vor der nächsten Nachbesserung (oder vor dem Start von v3 für den
Stopp) wird dieselbe Fassung erneut als eigener Schnappschuss gezogen, etwa `v2-basis`, und dient dort als Ausgangsfassung.

Der Plan (JSON, außerhalb von Git) nennt die 9 Fälle in fester Reihenfolge, Format im Kopf des Moduls: **8 echte
Nachbesserungen** (mindestens 3 × `local`, der erste mit `expect`, der neuen Angabe wörtlich, 2 × `tone`, je 1 × `shorter` und
`longer`; die achte ist eine weitere dieser vier Arten und muss deren Schwellen bestehen) **und zusätzlich** 1 × `stop` (mit
`stopped_version`). Von den 8 echten Nachbesserungen laufen 5 über `web` und 3 über `mcp`, verteilt auf 4 Aufträge; der Stopp
gehört zu einem davon. Weicht der Plan davon ab, ist das Tor nicht auswertbar. Für die lokalen Fälle Ausgangstexte mit
mindestens 4 Absätzen wählen.

| Teil | Schwelle (Vertrag 8, wörtlich) |
|---|---|
| N1 | unveränderte Absätze ≥ Absätze(v1) − 2, Wörter ±15 %, im ersten Fall steht die neue Angabe wörtlich im Text |
| N2 | ±15 %, neue `not_backed` mit Zitat nicht in v1 ≤ 1 zusammen |
| N3 | Kürzer 40–85 %, Länger 115–160 %, Länger: neue `not_backed` ≤ 1 |
| N4 | jede offene Stelle von v1, deren Zitat in v2 steht, und jeder offene Schreiberpunkt von v1 erscheint in v2; jeder Platzhalter im v2-Text steht in `placeholders` |
| N5 | v3 starten und stoppen → Ergebnis und Export zeigen v2 mit Satz |

**Vorgaben des Auftraggebers zusätzlich zum Vertrag (jede strenger):**

- Der Stopp zählt nie als „mit Text“: mindestens 6 der 8 echten Nachbesserungen brauchen eine neue Fassung mit Text.
- N4 deckt die ganze Versionskette ab: Jeder Eintrag aus `earlierOpenPoints` der Ausgangsfassung steht in der neuen Fassung
  unter `openPoints` oder unter `earlierOpenPoints` mit seiner ursprünglichen Version (und seiner Art). Jede Annahme des
  Schreibers der Ausgangsfassung (`writerAssumptions`), die die neue Fassung nicht unter `writerAssumptions` wiederholt, steht
  unter ihren `earlierOpenPoints` mit `kind` `assumption` und der Version der Ausgangsfassung. `kind` ist `openPoint` (auch wenn
  das Feld fehlt), `assumption` oder `note` (ein fester Satz von Atelier über diese Version). Eine Annahme (Eintrag der Art
  `assumption` oder Schreiber-Annahme der Ausgangsfassung) gilt auch als erhalten, wenn die neue Fassung sie als Annahme zeigt:
  unter `writerAssumptions` oder unter den Annahmen des Klärers (`assumptions[].text`). Ein Eintrag der Art `openPoint` ist durch
  eine Annahme nicht erhalten. Ein Eintrag der Art `note` gilt nur als erhalten, wenn er unter `earlierOpenPoints` mit seiner
  Version und der Art `note` steht, nie durch einen offenen Punkt oder eine Annahme.
- N5 verlangt `state` `stopped`; eine von selbst gescheiterte Fassung (`failed`) verfehlt N5.
- Ein lokaler Fall, dessen Ausgangstext weniger als 4 Absätze hat, ist für N1 nicht auswertbar.
- Jede Nachbesserung zählt einmal (G1): Ein Schnappschuss in mehreren Fällen, dasselbe `(Auftrag, Ausgangsfassung, neue
  Fassung)` in mehreren Fällen, Ausgangs- und neue Fassung als derselbe Schnappschuss oder zwei echte Nachbesserungen mit
  derselben neuen Version desselben Auftrags (etwa über Kopien unter anderen Bezeichnungen) machen das Tor nicht auswertbar.
- In N1 und N2 muss sich der Text geändert haben (G2): Gleicht der neue Text nach Zusammenfassen des Leerraums dem
  Ausgangstext, verfehlt der Fall sein Kriterium. Das ist ein **Fehler**, nicht „nicht auswertbar“: Die Daten sind vollständig
  und zeigen, dass die Änderung nicht gemacht wurde. „Nicht auswertbar“ verlangte nur einen anderen Fall und ließe einen Lauf,
  der die Bitte übergeht, aus dem Befund verschwinden; der Fehler bleibt im Bericht und löst die Folge aus Vertrag 8 aus (eine
  Runde am Satz `RevisionInstruction`). Kürzer und Länger können mit unverändertem Text ohnehin nicht bestehen (100 % liegt
  außerhalb ihrer Spannen).

Eigene Lesart, unabhängig vom C#-`ParagraphDiff`: Absatz = Block zwischen Leerzeilen, verglichen nach Zusammenfassen des
Leerraums; unverändert = längste gemeinsame Teilfolge. Wort = Folge zwischen Leerraum mit mindestens einem Buchstaben oder einer
Ziffer. Ein Zitat „steht“ im Text als ganze Wörter nach Vereinheitlichung von Leerraum, unsichtbaren Zeichen, Anführungszeichen
und Strichen, ohne Groß/Klein. Eine Stelle von v1 erscheint in v2, wenn eine Stelle von v2 (gleich welcher Art) ihr Zitat
enthält; ein Schreiberpunkt unter `openPoints` oder unter `earlierOpenPoints` (Art `openPoint`) mit der Version von v1. Punkte
und Annahmen werden ohne Groß/Klein, Schlusszeichen und einen führenden Artikel verglichen. Fehlende Quellenhinweise stehen nur
als Hinweis im Bericht (nicht Teil von N4).

**Nicht auswertbar:** weniger als 6 der 8 echten Nachbesserungen mit Text, Export fehlt, ein Kriterium ohne Fall mit Text
(Kürzer, Länger, der erste lokale Fall, alle Ton-Fälle), N2/N3/N4 ohne gelaufene Faktenprüfung (`factCheck.state` weder
`checked` noch `open_points`) oder ohne die nötige Liste (`openPoints`, `writerAssumptions`; fehlendes `earlierOpenPoints`
heißt „keine“, ein unlesbares ist nicht auswertbar), N1 mit einem lokalen Ausgangstext unter 4 Absätzen, N5 ohne
`latestVersion`, die neue Angabe stand schon in v1, ein Schnappschuss fehlt, ist unlesbar oder gehört zu einem anderen
Auftrag. Ein Fall zählt als „mit Text“, wenn nach der Nachbesserung ein Text einer neueren Version gezeigt wird; der
Stopp-Fall zählt dabei nicht. Ein verfehltes N4 oder N5 heißt **Halt** und macht das Tor `failed`, auch wenn anderswo Daten
fehlen. Bericht `revise-gate.json`; Rückgabe 0 bestanden, 1 verfehlt, 2 falscher Aufruf, 3 Anmeldung gescheitert, 4 nicht
auswertbar.

### Tor M und V-MCP (`geef_eval/mcp_contract.py`)

```bash
cd tools/Eval
python3 -m geef_eval.mcp_contract tools --out $EVAL_M_DIR/tools.json [--env-file <datei>]
python3 -m geef_eval.mcp_contract import --session ~/.claude/projects/<ordner>/<sitzung>.jsonl --server <name> \
  --out $EVAL_M_DIR/transcript.jsonl
python3 -m geef_eval.mcp_contract evaluate --tools $EVAL_M_DIR/tools.json --transcript $EVAL_M_DIR/transcript.jsonl \
  --out $EVAL_M_DIR/m-gate.json
python3 -m geef_eval.mcp_contract vmcp --transcript $EVAL_VMCP_DIR/transcript.jsonl --out $EVAL_VMCP_DIR/vmcp-gate.json
```

`tools` liest nur `tools/list`. `import` übernimmt aus einer Claude-Code-Sitzungsdatei alle Aufrufe `mcp__<name>__…` als
Mitschnitt (eine JSON-Zeile je Aufruf mit Werkzeug, Argumenten, Beginn, Dauer = protokolliertes Ergebnis minus protokollierter
Aufruf, also eher zu lang, Fehler und Antwort). Beide Tore nennen Claude Code als Client; ein Mitschnitt aus einer anderen Quelle
ist nicht auswertbar.

- **Werkzeugliste:** die neun Auftragswerkzeuge und die fünf Stummel entfernter Werkzeuge, mit Upload-Ticket zusätzlich
  `prepare_attachment` (D-091); Parameter von `revise_order` (nur `orderId` Pflicht, `quick` Liste, Beschreibung nennt
  `get_order_status`), `export_order` (`version` wahlweise, `format` nur md/docx/pdf), `list_orders` (`limit` wahlweise),
  `profile` höchstens mit den vier Speicher-Parametern und mit „About me“ in der Beschreibung; jeder Stummel beginnt mit
  „Removed: use … instead.“
- **Tor M:** Kette für Auftrag A `submit_order` (mit `needs_input`) → `answer_order` → `get_order_status` → `get_order_result`
  → `revise_order` (`version` 2, `notices`) → `get_order_result` (`version` 2) → `export_order` md **ohne `version`** (Inhalt =
  `text` der Datei; jeder md-Export ohne `version` nach diesem `get_order_result` ist textgleich mit dessen Version 2, verglichen
  nach Entfernen der BOM, Vereinheitlichen der Zeilenenden und Trimmen, sonst `failed`) und docx
  (ZIP) **ohne `version` oder mit `version` 2** (ein Export der Version 1 zählt nie als Schritt, G4; jeder Export von A nach
  diesem `get_order_result` ohne `version` oder mit `version` 2 muss im Dateinamen Version 2 nennen, `…-v2.docx`, sonst
  `failed`; nennt der Dateiname des Schritts keine Version, ist das Tor nicht auswertbar), dazu `list_orders` (mit A, alle Felder), `profile` und `cancel_order` an einem zweiten Auftrag (`changed`). Jeder
  Fehler eines Auftragswerkzeugs lässt das Tor scheitern; fehlende Schritte, keine Rückfrage, `changed = false`, eine
  unlesbare (etwa gekürzte) Antwort oder ein Aufruf ohne Ergebnis machen es nicht auswertbar.
- **V-MCP:** alle `get_order_status` mit `waitSeconds=50` ≤ 55 s ohne Fehler → 50 s bleibt (Rückgabe 0); Fehler oder > 55 s bei
  einem langen Aufruf → `Orders:MaxWaitSeconds` 25 s (Rückgabe 1). Nicht auswertbar (4) ohne langen Aufruf, wenn keiner
  mindestens 45 s wartete (der lange Aufruf wurde nicht geprüft), bei Fehlern oder fehlenden Ergebnissen anderer Aufrufe.

## Etappe 6: Upload-Ticket, Tore T-A2 bis T-A5 (`geef_eval/upload_check.py`)

Das Werkzeug lädt nie selbst hoch, startet keinen Auftrag und ruft kein Modell auf. Es erzeugt synthetische Testdateien,
merkt sich Zeitfenster, liest Mitschnitte und mitgeschnittene Antworten und fragt die Datenbank **nur lesend** ab
(`PGOPTIONS=-c default_transaction_read_only=on` und zusätzlich `BEGIN TRANSACTION READ ONLY … ROLLBACK`, über
`docker exec <container> psql`; Container aus `--container` oder `EVAL_PG_CONTAINER`, Benutzer und Datenbank aus der Umgebung des
Containers). Prüfsummen werden in der Abfrage verglichen; keine Abfrage liefert Dateinamen, Nutzernamen, Hashes oder Inhalte.
Bildschirm und `upload-gates.json` enthalten nur Zählungen und ja/nein. Alle Verzeichnisse (Testdateien, Mitschnitte, Antworten,
Bericht) liegen außerhalb jedes Git-Baums, sonst Abbruch mit Rückgabe 2. Mitschnitte enthalten den Einmal-Token und Dateiinhalt:
nicht weitergeben.

### Ablauf

```bash
cd tools/Eval
D=~/.local/state/geef-eval/e6-tore                       # außerhalb von Git
python3 -m geef_eval.upload_check generate --out-dir $D   # Testdateien, files.json, plan-vorlage.json
```

`generate` schreibt je Lauf neue Dateien (eigene Kennung im Inhalt, also nie eine Dublette eines früheren Uploads):

| Schlüssel in `files.json` | Datei | wofür |
|---|---|---|
| `s1` | Text-PDF, 24 Seiten, > 200 KB, Markersätze auf erster und letzter Seite (`markers`) | T-A2 S1 |
| `s2` | DOCX mit denselben Markersätzen | T-A2 S2 |
| `page` | kleines Text-PDF | T-A3 (Upload-Seite) |
| `scan` | PDF nur mit Bildern, ohne Textebene | T-A4 |
| `png` | PNG | T-A5 (Web) |
| `placeholder` | Platzhaltertext; `attachments` nennt die fertige JSON-Datei für das Argument `attachments` | T-A5 (MCP-Anhang) |
| `size` | Markdown; `declared_bytes` = echte Größe + 100 für `prepare_attachment(sizeBytes)` | T-A5 (falsche Größe) |

1. **T-A2:** je Sitzung `claude -p "Fasse die angehängte Datei zusammen: <Pfad>" --output-format stream-json --verbose
   --mcp-config <Konfiguration mit Token aus der Umgebung> > $D/s1-stream.jsonl` (S2 ebenso mit der DOCX, danach `cancel_order`).
   S1 erst auswerten, wenn ein Lauf des Auftrags fertig ist (vorher ist die Schreiber-Prüfung `fehlt`).
2. **T-A3:** `prepare_attachment` (Messkonto), die Datei `page` über `/u/<id>` im Chromium ablegen, `submit_order(uploadIds)`,
   dann `cancel_order`. Mitschneiden: `document.documentElement.outerHTML` der fertigen Seite (`t-a3-done.html`), dieselbe Seite
   als zweites Konto (`t-a3-foreign.html`, **erste Zeile** `<!-- url: <location.href> -->`, also
   `'<!-- url: ' + location.href + ' -->\n' + document.documentElement.outerHTML`) und die Antwort von `submit_order` des zweiten
   Kontos mit der Id (`t-a3-foreign-submit.txt`, Text der Antwort oder Fehlermeldung).
3. **T-A4:** `python3 -m geef_eval.upload_check mark --gate T-A4 --at start --out-dir $D`, dann zwei Tickets anlegen; die Datei
   `scan` per Token (`curl … -w '\n%{http_code}\n' > $D/t-a4-token.txt`) und über die Seite hochladen (Antwort des
   `POST …/page` über CDP im selben Format: Rumpf, letzte Zeile Statuscode, `t-a4-page.txt`), danach `mark --gate T-A4 --at end`.
4. **T-A5:** `mark --gate T-A5 --at start`; PNG über die Startseite (Antwort von `POST /api/sources` als `t-a5-png.txt`),
   Platzhalter über `attachments` (Fehlertext als `t-a5-placeholder.txt`), `prepare_attachment(sizeBytes=declared_bytes)` und
   die Datei `size` per Token (`t-a5-size.txt`); dann `mark --gate T-A5 --at end`. Die Fenster von T-A4 und T-A5 dürfen sich nicht
   überlappen; in ihnen darf sonst nichts hochgeladen werden.
5. `plan-vorlage.json` nach `plan.json` kopieren, Upload-Ids eintragen (relative Pfade gelten ab dem Planordner), dann

```bash
python3 -m geef_eval.upload_check evaluate --plan $D/plan.json --out-dir $D [--gate T-A4 …]
```

Hilfsbefehle: `compare --file <datei> --upload-id <id>` (SHA-256 gleich: ja/nein) und `largest-base64 --transcript <datei>`.

### Schwellen (Vertrag 10, wörtlich) und was das Werkzeug zusätzlich verlangt

| Tor | Vertrag | zusätzlich (jeweils strenger) |
|---|---|---|
| T-A2 | je Sitzung (a) `prepare_attachment` aufgerufen, (b) `curl` mit 201, (c) `submit_order` mit `uploadIds`, (d) `Sources.Sha256` = lokale SHA-256, (e) größtes `contentBase64` ≤ 1 KB; S1: `RunSourceUsages` Ziel `writer`, `FullText`, `CharsIncluded` = `CharCount`; 2/2 | (b) zählt nur eine Antwort mit Upload-Id, `sha256` und brauchbarer Quittung; (d) auch Ticket `Used`, Quelle desselben Besitzers, Herkunft `McpAttachment`, `sha256` der curl-Antwort = lokal; (f) Auftrag mit der Quelle verknüpft (K1); `contentBase64` wird in allen Werkzeugaufrufen gesucht, auch in JSON-Zeichenketten und Shell-Befehlen |
| T-A3 | (a) Seite zeigt Quittung und „Done …“, (b) `Sources.Sha256` gleich, (c) zweites Konto: Seite „Not found“, `submit_order` → „not found“ | (b) Herkunft `WebUpload` (über die Seite, nicht per curl); (c) das Seitenabbild nennt in der ersten Zeile seine Adresse, und die ist `/u/<Upload-Id des Plans>` (ohne Adresse `fehlt`, mit anderer `nein`); die Seite des zweiten Kontos zeigt weder Dateinamen noch einen Satz der Upload-Seite, kein Auftrag einer anderen Person verknüpft die Quelle |
| T-A4 | beide 422 mit dem Satz aus `PdfSourceExtractor.NoTextReason`; 0 neue `Sources`; genau 2 Zählzeilen `pdf_no_text` (`mcp_ticket`, `upload_page`) | beide Antworten nennen die Upload-Id ihres Tickets aus dem Plan (`uploadId`); Tickets ohne Quelle und nicht `Used`, mit mindestens einem geprüften Versuch und `LastRefusalCode` = `pdf_no_text` (ein unberührtes Ticket besteht nicht); Zeilen mit Endung `pdf`; jede Spalte von `SourceRefusals` stammt aus Vertrag 7.1, jeder Wert aus seinem Wortschatz, keiner enthält einen Dateinamen |
| T-A5 | `SourceRefusals` seit Torbeginn = genau die erwarteten Zeilen (Code, Weg); keine Spalte enthält einen Dateinamen | die drei Antworten sind die erwarteten Ablehnungen (422 und Satz bzw. Satz des Parsers), die Antwort zur falschen Größe nennt die Upload-Id des Plans; Ticket der falschen Größe ohne Quelle, mit geprüftem Versuch und `LastRefusalCode` = `size_mismatch`; 0 neue `Sources`; Endungen `png`, `txt`, `md`; Spalten und Wortschatz wie T-A4 |

Zeilen jeder Person zählen (eine fremde Ablehnung im Fenster lässt das Tor scheitern). **Nie bestanden ohne Daten:** fehlt ein
Planeintrag, eine Datei der verlangten Art, ein vollständiger Mitschnitt (mit Ergebniszeile), eine Antwort mit Statuszeile, eine
Marke oder eine Datenbankzeile, ist das Kriterium `fehlt` und das Tor nicht auswertbar; ein Ticket in zwei Toren ebenso. Ein
verfehltes Kriterium (`nein`) macht das Tor `failed`, auch wenn anderswo Daten fehlen; davon abhängige Kriterien stehen auf `folgt`.

**Folgen:** T-A2 nur bei (a)/(b)/(c)/(e) verfehlt → eine Runde an den Sätzen 5.5, dann zwei neue Sitzungen (Kappe 4); (d), (f)
oder Schreiber-Prüfung verfehlt → Halt, Schalter `Off`; T-A3/T-A4/T-A5 verfehlt → Halt, Schalter `Off`; alle vier bestanden →
eigener Commit `Uploads:Tickets=On`. Rückgabe 0 bestanden, 1 verfehlt, 2 falscher Aufruf oder Plan, 3 Datenbankabfrage
gescheitert, 4 nicht auswertbar.

**Trockenfall:** `tests/upload_world.py` baut einen vollständigen, korrekten Durchlauf aller vier Tore aus synthetischen Daten;
`tests/test_upload_check.py` belegt, dass er besteht, dass jedes einzelne Fehlverhalten sein Tor verfehlen lässt und dass ohne
Daten kein Tor besteht. Die Sätze, Codes und Wege sind gegen die C#-Konstanten geprüft (Drift-Test).
