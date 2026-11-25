# ResearchEval – Messwerkzeug für die Vorversuche VT und VR (Etappe 4)

Prüft Tavily (VT) und misst das Dossier der Recherche (VR) auf den 36 Korpusaufträgen mit gespeicherten Suchthemen
(`tests/Geef.Atelier.Tests/Intake/Corpus/results-20261125.jsonl`, kein neuer Klärer-Aufruf). Die unabhängige Nachprüfung liegt in
`tools/Eval/geef_eval/research_verify.py`.

```bash
C=tests/Geef.Atelier.Tests/Intake/Corpus
SETS="--intake-results $C/results-20261125.jsonl --corpus $C/intake-corpus.jsonl --corpus $C/intake-corpus-second.jsonl"
dotnet run --project tools/ResearchEval -- orders $SETS
dotnet run --project tools/ResearchEval -- probe-tavily $SETS --out $EVAL_RESEARCH_DIR/vt.jsonl --max-searches 3 --codex-lock <sperrdatei>
dotnet run --project tools/ResearchEval -- run $SETS --set dev --gateway <gateway-url> --out $EVAL_RESEARCH_DIR/vr-dev.jsonl \
  --max-searches 28 --max-claude 12 --codex-lock <sperrdatei>
dotnet run --project tools/ResearchEval -- run $SETS --set holdout --gateway <gateway-url> --out $EVAL_RESEARCH_DIR/vr-holdout.jsonl \
  --max-searches 18 --max-claude 9 --codex-lock <sperrdatei>
dotnet run --project tools/ResearchEval -- judge --run $EVAL_RESEARCH_DIR/vr-holdout.jsonl --gateway <gateway-url> \
  --out $EVAL_RESEARCH_DIR/vr-judge.jsonl --max-claude 9 --codex-lock <sperrdatei>
(cd tools/Eval && python3 -m geef_eval.research_verify --results $EVAL_RESEARCH_DIR/vr-holdout.jsonl \
  --results $EVAL_RESEARCH_DIR/vr-dev.jsonl --out $EVAL_RESEARCH_DIR/vr-verify.json)
dotnet run --project tools/ResearchEval -- evaluate $SETS --holdout $EVAL_RESEARCH_DIR/vr-holdout.jsonl \
  --also $EVAL_RESEARCH_DIR/vr-dev.jsonl --judgments $EVAL_RESEARCH_DIR/vr-judge.jsonl --verification $EVAL_RESEARCH_DIR/vr-verify.json
```

| Befehl | Wirkung |
|---|---|
| `orders` | zeigt die Mengen: 12 Entwicklungsaufträge (die ersten nach Kennung), 9 zurückgehaltene, 15 für R3 reservierte. Kein Aufruf. |
| `probe-tavily` | VT: je Thema genau ein `POST search` wie im Betrieb (`basic`, `max_results` 3, `include_raw_content` an, `include_answer` aus), Schlüssel im Körper; nach 401/403 genau eine Anfrage mit `Authorization: Bearer`. Themen: das erste Thema, das der Herkunftsfilter für die ersten Entwicklungsaufträge freigibt. Speichert Status, Dauer, Anteil und Länge der Rohtexte und alle Antwortfelder außer Inhalten (Ergebnistexte, Tavily-Antwort, Bilder), dazu die Kopfzeilen. Gibt die Entscheidung nach Vertrag 3.2 aus, auch „Kreditverbrauch unbekannt“. |
| `run` | VR: echter `ResearchQueryGate`, `TavilySearchClient`, `LlmDossierCondenser` (Sonnet, Failover aus) und `DossierBuilder`, Auftrag für Auftrag. Je Auftrag eine Zeile mit Rohtexten (gekürzt wie im Betrieb), Vorschlägen, Rohantwort, Einträgen mit Start/Länge, Verwurfgründen, Dauer und Eingabe-Hash. |
| `judge` | VR: je zurückgehaltenem Auftrag mit Einträgen ein Opus-Aufruf, blind: sieht nur Themen und Ausschnitte, nie Seite, Adresse, Titel oder Modell; je Ausschnitt „hilft beim Thema ja/nein“. |
| `evaluate` | Tor VR auf der zurückgehaltenen Menge (K1, Gültigkeit, Ausbeute, Relevanz, Median) und Zahlen der Entwicklungsmenge ohne Urteil. |

Rückgabewerte: 0 fertig, bestanden bzw. weiter; 1 Tor nicht bestanden bzw. Halt (VT); 2 falscher Aufruf oder verweigert (auch:
Ergebnisdatei im Git-Baum, Zeilen zu einer anderen Eingabe, Transport hat eine Anfrage abgelehnt); 3 vom Codex-Wächter angehalten
(mit demselben Befehl fortsetzen); 4 unvollständig, nicht auswertbar oder benötigter Produktionscode noch nicht umgesetzt.

## Voraussetzungen

- Merge-Reihenfolge F1 → F2 → F5a. Auf einem Stand ohne F1/F2 enden `probe-tavily`, `run` und `evaluate` mit Rückgabewert 4
  (`NotImplementedException` der Hülle); `orders` geht immer.
- `TAVILY_API_KEY` (VT, VR) und, falls das Gateway einen Schlüssel verlangt, `RESEARCH_EVAL_API_KEY`. Es gibt keine Option dafür;
  unbekannte Optionen werden abgelehnt. Beide Schlüssel werden aus jeder geschriebenen Zeile entfernt und nie ausgegeben.
- Ergebnisdateien außerhalb jedes Git-Baums (Rohtexte sind Drittinhalte), etwa unter `EVAL_RESEARCH_DIR`; sonst Rückgabewert 2.

## Schutz von Kontingent und Daten

- **Kappen sind Pflicht** und gelten am Transport, unabhängig vom geprüften Code: `--max-searches` zählt jede Tavily-Anfrage,
  `--max-claude` jeden Modellaufruf. Obergrenzen je Datei: 3 Anfragen (VT), 84 Anfragen und 33 Aufrufe (VR-Lauf), 9 Aufrufe (Juror).
  Ein Auftrag startet nur, wenn beide Kappen ihn ganz decken (alle seine Suchen und ein Aufruf). Die Obergrenzen gelten je Datei;
  wer für eine Promptrunde eine neue Datei beginnt, achtet selbst auf die Summe (Vertrag: ≤ 87 Suchen, ≤ 33 Sonnet, ≤ 9 Opus).
- **Transportwächter:** Eine Suche geht nur hinaus, wenn ihr `query` genau ein vom Herkunftsfilter gesendetes Thema des laufenden
  Auftrags ist. Ein Modellaufruf geht nur hinaus mit abgeschaltetem Gateway-Failover und nur über die Claude-Route; Codex wird nie
  aufgerufen. Eine abgelehnte Anfrage wird nicht gesendet, die Zeile bleibt, der Lauf endet mit 2.
- **Codex-Wächter** vor jedem Auftrag (wie `FactCheckEval`): Halt, solange eine `--codex-lock`-Datei existiert oder `pgrep -a codex`
  einen laufenden `exec`-Auftrag findet; Halt auch, wenn die Prozessliste nicht lesbar ist.
- **Jeder Versuch zählt:** Ein Auftrag wird je Ergebnisdatei genau einmal gemessen, gleich mit welchem Ausgang; ein erneuter Aufruf
  setzt nur fort. Zeilen zu einer anderen Eingabe (anderer Prompt, anderes Schema, andere Themen oder Einstellungen) lassen den Lauf
  verweigern: neue Datei beginnen. Nur der Juror ist Messinstrument und darf einen gescheiterten Aufruf wiederholen (innerhalb der Kappe).
- Die 15 für R3 reservierten Entwicklungsaufträge gehören zu keiner Menge; Zeilen zu ihnen machen das Tor nicht auswertbar.

## Regeln der Auswertung

- **Eingabe-Hash** je Zeile über Condenser-Systemprompt, Schemaname, Schema, den Nutzerprompt für eine feste Probe-Eingabe (damit
  eine Änderung der Vorlage auffällt), die gespeicherten Themen und die Einstellungen (Suchen, Treffer je Suche, Zeichen je Seite,
  Einträge, Zeitgrenzen).
- **Kein Tor besteht ohne Daten.** Nicht auswertbar, wenn ein zurückgehaltener Auftrag keine oder mehr als eine Zeile zur aktuellen
  Eingabe hat (die zurückgehaltene Menge wird einmal gemessen; eine Wiederholung wird allein ausgewertet), eine Zeile ohne oder mit
  anderem Hash vorliegt, weniger als 7 von 9 Aufträgen eine gültige Condenser-Antwort haben, Rohtexte fehlen, die Nachprüfung fehlt
  oder andere Dateien (SHA-256 der Bytes) bzw. eine andere Eintragszahl gelesen hat, Herkunftsfilter oder `DossierBuilder` heute aus
  den gespeicherten Daten etwas anderes machen als im Lauf, oder ein zurückgehaltener Eintrag kein gültiges Urteil hat.
- **K1 ist hart:** Ein Verstoß der unabhängigen Nachprüfung oder der eigenen Nachrechnung (Seite über den Hash, gleiche Adresse,
  Bereich im Rohtext, Bereich = Zitat bei vereinheitlichtem Leerraum, Groß/Klein unterschieden) ergibt „nicht bestanden“, auch wenn
  sonst Daten fehlen. Ohne Einträge ist K1 nicht auswertbar.
- **Gültigkeit** über alle Condenser-Aufrufe der zurückgehaltenen Menge; **Ausbeute** über alle 9 Aufträge (ein gescheiterter zählt
  als ohne Eintrag); **Relevanz** über alle Einträge; **Median** nur über Aufträge mit echter Dauer (Ergebnis, unbrauchbare Antwort
  oder Zeitüberschreitung); schnelle Fehlschläge und vollständig zurückgehaltene Aufträge werden genannt, nicht gemittelt.
- Berichtet, nicht Tor: Ausgänge je Status, gesendete/zurückgehaltene Themen, Suchen, Seiten mit Rohtext, Suchdauer, Verwurf je
  Grund (Anteil an den Vorschlägen), Modelle.

## Code und Tests

Der Messcode liegt unter `tests/Geef.Atelier.Tests/Tools/ResearchEval/Evaluation/` und wird in dieses Projekt verlinkt, damit
Werkzeug und Tests mit demselben Code urteilen; dazu unverändert Wächter, Kommandozeile und Gateway-Routen aus
`tests/Geef.Atelier.Tests/FactCheck/Evaluation/` und die Korpustypen aus `tools/IntakeEval/CorpusEntry.cs`. Tests:
`dotnet test --filter "FullyQualifiedName~ResearchEvalTool"` und `python3 -m pytest tools/Eval/tests/test_research_verify.py`.

Die Seitennummerierung `P1…` (Themen in Reihenfolge, je Thema die Treffer, ohne Rohtext entfallen, eine Adresse aus einer
weiteren Suche behält ihre erste Seite) ist eine einzige Funktion (`ResearchPageNumbering`), die Recherchedienst und Werkzeug
teilen (`ResearchPagesTests`). Dem Herkunftsfilter gibt das Werkzeug Text **und Namen** jeder Datei
(`ResearchCorpus.FileWords`); im Betrieb sucht ein Auftrag mit Datei seit der Prüfrunde 1 gar nicht mehr (`FilesAttached`, D-074),
das Werkzeug misst solche Aufträge aber weiter wie bisher (nur ein Entwicklungsauftrag hat eine Datei, die zurückgehaltene Menge
keine). Auch die Sprachregel des Betriebs (nur mit Freitext im Profil) wendet das Werkzeug nicht an; beides kann im Betrieb nur
weniger senden. Die gespeicherten VR-Zeilen bleiben gültig: `evaluate` rechnet Herkunftsfilter und Builder mit dem Stand nach der
Prüfrunde 1 nach (Zahlen ab vier Ziffern, Teile, Genitiv, unsichtbare Zeichen, ganze Zahlen, längerer Ausschnitt ersetzt
kürzeren, sichere Titel) und besteht weiter; keiner der 95 Einträge liegt im Teil, den der neue Schnitt am Satzende wegnimmt.
Die Nachprüfung (C# und `research_verify.py`) meldet auch einen Ausschnitt, der in einem Wort oder einer Zahl beginnt oder endet.

**Abnahme am echten Lauf (Vertrag 9.1):** Die Audit-Dateien des Recherchedienstes (`Research:AuditDirectory`, nur für Nutzer mit
Override) prüft dieselbe Nachprüfung: `(cd tools/Eval && python3 -m geef_eval.research_verify --audit <verzeichnis> --out <bericht>)`.
Danach das Verzeichnis löschen.

**R3 (Paarvergleich mit/ohne Recherche)** liegt nicht hier, sondern in `tools/Eval/geef_eval/research_pairs.py` (siehe
`tools/Eval/README.md`).
