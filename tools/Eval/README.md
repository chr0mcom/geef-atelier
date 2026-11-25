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
| `atelier` | das laufende Atelier über den MCP-Endpunkt: einreichen, alle 15 s Status abfragen (bis 60 min), bis `outcome` gesetzt ist, erst dann das Ergebnis holen (ein terminaler Status allein reicht nicht: der Text ist erst mit `outcome` endgültig, also nach den Abschlussschritten). Als Ergebnis zählen `Converged` und `DeliveredWithOpenPoints` mit Text; damit zählt ein Lauf mit Status `Failed`, der einen besten Text liefert, weiter als Ergebnis (Entscheidung vom 2026-07-17). `outcome` `Failed` oder `Aborted` ist ein Fehler des Arms, ebenso ein Zeitlimit ohne `outcome`. Braucht ein Atelier ab Etappe 0 (Feld `outcome`, Step47) |
| `referenz` | der `referenztext` aus der Aufgabe (feste Vergleichsgröße, auch für Probeläufe) |
| eigene | `--plugin name=paket.modul:Fabrik` oder `--plugin name=pfad/datei.py:Fabrik`; die Fabrik wird mit `name=` aufgerufen und liefert ein Objekt mit `name` und `produce(task) -> ArmOutput` |

Die Namen der MCP-Werkzeuge ändern sich in späteren Etappen (`submit_order` …). Sie stehen nur in
`geef_eval/mcp.py`, Klasse `AtelierToolAdapter` (`DEFAULT_TOOL_NAMES`); dort oder in einer Unterklasse anpassen.

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
| `EVAL_ATELIER_CREW` | optional: Crew-Vorlage für den Arm `atelier` (sonst Standard des Ateliers) |
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
- **Codex ist aus.** Das Codex-Abo gehört BookForge. Der optionale zweite Juror (`--codex-juror`) startet nur mit
  `EVAL_CODEX_MAX > 0`, prüft vor jedem Aufruf `pgrep -a codex` und überspringt sich, solange Codex anderweitig
  läuft.

## Tests

```bash
cd tools/Eval && python3 -m pytest -q
```

Die Tests laufen vollständig offline mit Attrappen; kein Test ruft ein Modell auf.

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
Lauf erneut versucht; ein Nutzungslimit beendet den Lauf mit Meldung und Rückgabewert 3.

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
- Der Lauf geschieht nachts und nur, wenn BookForge nicht läuft (geteiltes Claude-Kontingent).
- Der Auftraggeber prüft 20 Einträge stichprobenartig und entfernt widersprüchliche.
