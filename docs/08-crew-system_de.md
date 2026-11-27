# 08 — Crew-System (PS-5)

*[English](08-crew-system.md) · **Deutsch***

Letzte Aktualisierung: 2026-11-27 (zweiter Zusatz zu D-087: „Extra careful“ entfernt, eine Aufstellung; D-091: Stufe 8d, neu geschrieben auf den heutigen Stand: zwei Aufstellungen im Code, Crew-Snapshot, Modellmatrix, Prüfer des Direktwegs, alte Läufe; die entfernten Teile stehen am Ende; zuvor 2026-11-26 Hinweis, wo das Crew-System steht, D-076: Dateien über das Upload-Ticket; 2026-11-25, D-075 Teil B: Nachbesserungslauf, D-074: Recherche im Grounding-Schritt; 2026-11-24, D-067: strenges Lesen des Snapshots; ältere Vermerke stehen in der Git-Historie)

## Überblick

Eine „Crew“ ist die Aufstellung eines Laufs: ein Schreiber und seine Prüfungen. Seit Stufe 8d (D-091) gibt es **eine
Arbeitsweise, den Direktweg** (D-068 bis D-077, [15-developer-guide_de.md](15-developer-guide_de.md)), und seine
Aufstellung steht **nur im Code**. Niemand setzt mehr Crews zusammen, bearbeitet oder speichert sie. Vom Crew-System
bleiben:

- eine Aufstellung in `Geef.Atelier.Core.Domain.Crew.SystemCrew`,
- der **Crew-Snapshot**, den jeder Lauf speichert und der so festhält, womit er gelaufen ist,
- die **Modellmatrix**, die beim Bau des Snapshots die Modelle zuweist.

Das frühere System (Vorlagen und Profile in der Datenbank, Editoren und weitere Akteure) steht unter
[Entfernte Teile](#entfernte-teile-stufe-8d-d-091). Seine ausführliche Beschreibung steht in der Git-Historie dieser Datei.

## Kernbegriffe

| Begriff | Bedeutung |
|---|---|
| **ExecutorProfile** | Der Schreiber: Name, Anzeigename, System-Prompt, Provider, Modell, `MaxTokens`, optional `ModelSlot`. |
| **ReviewerProfile** | Eine Prüfung des Entwurfs mit denselben Feldern. Die Implementierung wird beim Bau der Pipeline eingesetzt. |
| **CrewTemplate** | Eine Aufstellung im Code: Schreiber, Prüfungen, Evaluationsstrategie, Convergence-Override, Domäne. |
| **CrewSnapshot** | Die vollständig eingebettete Kopie der Aufstellung, als JSONB in `Runs.CrewSnapshot` gespeichert. |

## Die Aufstellung

`SystemCrew` hat genau eine Vorlage, `direct`. `ResolveOrderSnapshotAsync` nimmt sie für jeden neuen Lauf, und
`CrewService.ResolveCodeTemplateSnapshotAsync` macht daraus den Snapshot (bauen, Modelle zuweisen, Modell-Aliase auflösen).
Es gibt einen produktiven Pipeline-Assembler, `DirectPipelineAssembler`; er nimmt nur diesen Namen an. Die frühere Variante
„Extra careful“ wurde nach ihrer Messung entfernt (zweiter Zusatz zu D-087).

| Vorlage | Verwendet für | Schreiber | Prüfungen |
|---|---|---|---|
| `direct` | jeden Auftrag und jeden Nachbesserungslauf | `direct-writer` | `deterministic-checks`, `source-fact-check` |

| Profil | Anzeigename | Modell |
|---|---|---|
| `direct-writer` | Writer | Slot `Executor` |
| `deterministic-checks` | Automatic checks | keins |
| `source-fact-check` | Fact check | Slot `FactReviewer` |

Sie hat keine Advisors, Werkzeuge oder Schritte nach den Prüfungen. Der **einzige Zugang zum Web** ist
die Recherche im Grounding-Schritt ([unten](#recherche-im-grounding-schritt-d-074)).

## EvaluationStrategies

Das Enum `EvaluationStrategy` behält `Parallel` (0), `Sequential` (1), `FailFast` (2) und `Priority` (3), weil gespeicherte
Snapshots sie nennen. Die Aufstellung nutzt `Parallel`: Alle Prüfungen laufen, alle Befunde werden gesammelt.

## CrewSnapshot-Format (SchemaVersion 4)

Das Beispiel zeigt den **Aufbau**. Prompts sind gekürzt, Modell-IDs sind Platzhalter. Enums werden als Zahlen gespeichert.

```json
{
  "schemaVersion": 4,
  "templateName": "direct",
  "executor": {
    "name": "direct-writer", "displayName": "Writer", "description": "...", "systemPrompt": "...",
    "provider": "claude-cli", "model": "<konkrete Modell-ID>", "maxTokens": 256000, "isSystem": true,
    "modelSlot": 0, "modelDomain": null
  },
  "reviewers": [
    { "name": "deterministic-checks", "provider": "", "model": "", "...": "..." },
    { "name": "source-fact-check", "provider": "codex-cli", "model": "<konkrete Modell-ID>", "modelSlot": 1, "...": "..." }
  ],
  "evaluationStrategy": 0,
  "convergenceOverride": { "maxIterations": 2, "abortOnCritical": false, "detectRegression": false, "stagnationThreshold": 2 },
  "crewDomain": 0
}
```

- Serialisiert wird mit `JsonNamingPolicy.CamelCase`.
- Nur ein Snapshot der aktuellen Schemaversion läuft; jede andere Version beendet den Lauf mit einem einfachen Satz.
- Aliase werden aufgelöst, bevor der Snapshot gespeichert wird (`CrewSnapshotModelResolver`): Er hält die konkreten
  Modell-IDs, und ein fortgesetzter Lauf nutzt dieselben. Ist der Katalog nicht erreichbar, bleibt der Alias stehen.

**Snapshot für einen Lauf lesen (D-067).** Der Orchestrator liest ihn streng mit `CrewSnapshot.DeserializeForRun`. Ein
fehlender, unlesbarer oder leerer Snapshot, eine Schema-Version außerhalb von 1 bis 3 oder ein Snapshot ohne Schreiber,
Reviewer-Liste oder Advisor-Liste löst `InvalidCrewSnapshotException` aus, und der Lauf endet `Failed` mit klarer Meldung.
Eine Ersatz-Aufstellung gibt es nicht. Das tolerante `CrewSnapshot.Deserialize` dient nur der Anzeige. Auch ein Snapshot ohne
Reviewer oder mit einem Reviewer, den diese Version nicht kennt, wird beim Bau der Pipeline abgelehnt: Ohne Prüfungen gibt
es keinen Text.

## Modellmatrix (D-066)

Ein Profil nennt seine **Rolle** (`ModelSlot`), die Aufstellung ihre **Domäne** (`ModelDomain`; beide Aufstellungen sind
`General`), und `Core/Domain/Crew/Models/ModelMatrix` hält die geordneten Kandidaten. `SystemCrew` enthält keine
Modell-Literale: Die an einem Profil gezeigte Bindung ist der erste Kandidat seines Slots. Claude läuft über `claude-cli`,
OpenAI über `codex-cli`.

| Slot | Verwendet von | Kandidaten der Reihe nach (General) |
|---|---|---|
| `Executor` | Schreiber, Planungsaufruf | Claude Opus, Codex Sol |
| `FactReviewer` | Faktenprüfung | Codex Astra, Claude Opus, Gemini 4 Neon (bezahlt), Claude Sonnet |
| `Intake` | Rückfragen vor dem Schreiben (D-071); die Prüfung der Anforderungen nimmt den ersten Kandidaten als feste Bindung | Claude Sonnet, Codex Sol |
| `GroundingRefiner` | Auswahl der Ausschnitte in der Recherche | Claude Sonnet, Codex Sol |

Enum und Matrix behalten weitere Slots und Domänen, die derzeit keine Aufstellung nutzt; die Zahlen stehen in alten
Snapshots, deshalb werden Einträge nur angehängt.

**Modellzuweisung** (`IModelAssignmentService.ApplyAsync`, einmal je Lauf beim Bau des Snapshots):

- Ein Kandidat muss vom Live-Katalog gemeldet sein. Ein nicht erreichbarer Katalog zählt als verfügbar, ein HTTP-Provider
  ohne API-Schlüssel als nicht verfügbar.
- Alle Regeln stehen in einer Funktion (`ModelChooser`), abgesichert durch `ModelAssignmentInvariantTests`. Harte Regeln
  werden nie gelockert: Der Provider ist nutzbar, und ein bezahlter Kandidat geht nur an einen Reviewer, nur in der Domäne
  Legal oder Academic oder ab 3 Reviewern desselben Slots, und an höchstens 2 Reviewer je Aufstellung. Bei der heutigen
  Aufstellung bekommt die Faktenprüfung deshalb nie ein bezahltes Modell.
- Reviewer mit Slot teilen sich das Modell des Schreibers nicht, solange es eine erlaubte Alternative gibt, und bevorzugen
  eine andere Anbieterfamilie.
- Ein Profil ohne Slot behält seine Bindung. Ist es das Modell des Schreibers, sagt eine Warnung das.
- Ein Ausweichen oder eine fehlende Modellvielfalt wird zur Lauf-Warnung `model.assignment`.
- Die Matrix nennt Familien-Aliase (`claude-opus-latest`, `gpt-sol-latest`, `gpt-astra-latest`). Der cli-proxy löst sie auf
  das neueste Modell der Linie auf, sodass eine neue Version keine Code-Änderung braucht (D-066/10).

Die Begründung der Matrix steht in D-066 im Entscheidungsprotokoll.

## Konvergenz und Auslieferung

Die Aufstellung läuft mit `MaxIterations: 2` (der Schreiber schreibt einmal und korrigiert höchstens einmal),
`AbortOnCritical: false`, `DetectRegression: false` und `StagnationThreshold: 2`. Werte, die eine Aufstellung nicht
überschreibt, kommen aus dem Abschnitt `Convergence`. Nur Befunde ab Schweregrad `Error` blockieren. Ein Reviewer, dessen
Aufruf scheitert, wird isoliert und blockiert nicht; der Lauf bekommt die Warnung `review.reviewer_failed`.

- Ein konvergierter Lauf liefert seine letzte Fassung aus.
- Ein Lauf, der nicht konvergiert, liefert die Fassung mit den wenigsten ablehnenden Prüfungen (bei Gleichstand die spätere)
  mit dem Ergebnis „mit offenen Punkten geliefert“. Ohne jeden Text endet er als gescheitert.
- `Runs.Outcome` wird gesetzt, nachdem der Bericht (`Runs.ReportJson`) geschrieben ist
  ([02-architecture](02-architecture_de.md#lauf-ergebnis-outcome-und-runstatus)). Lässt sich das Ergebnis nicht vollständig
  speichern, bekommt der Lauf `run.result_not_saved` und scheitert. Wurde der Dienst gestoppt, während ein Lauf abschloss,
  trägt der nächste Start ein Ergebnis ein und ergänzt `run.restarted_while_finishing`.
- Die Schalter `Convergence:BestDraft`, `WriterBlockingOnly` und `StopWithoutProgress` (D-085) wurden mit der alten
  Prüfschleife entfernt.

## Läufe von vor dem Aufräumen der Datenbank (D-092)

Die Aufträge und Läufe der entfernten Arbeitsweisen wurden beim Aufräumen der Datenbank gelöscht, zusammen mit dem Code, der nur
sie las. Eine gespeicherte Aufstellung, die eine Prüfung nennt, die diese Version nicht kennt, scheitert mit „The saved setup of
this task names a check that no longer exists, so it cannot run. Please start it again.“

## Hochgeladene Dateien sicher lesen (Zusatz zu D-069)

Dateien aus `POST /api/sources` und aus MCP-Anhängen laufen durch `SourceIntakeService`. PDF- und Word-Dateien liest
dieselbe Anwendung in einem **eigenen Prozess**, damit eine bösartige Datei den Webprozess nie beenden kann.

### Ablauf der Aufnahme

1. **Upload-Platz der Person und globaler Platz.** Jede Person hat höchstens `Sources:MaxConcurrentUploadsPerUser` (2)
   Uploads gleichzeitig; ein weiterer wird sofort mit HTTP 429, `Retry-After: 5` und „You are already adding 2 files at
   the same time. Please wait until one is done, then try again.“ abgelehnt. Über alle Personen laufen höchstens
   `Sources:MaxConcurrentIntakes` Uploads gleichzeitig; wer keinen Platz bekommt, wartet bis
   `Sources:ExtractionQueueTimeout`, sonst kommt HTTP 503 mit `Retry-After: 30` und der Quittung „The service is busy
   with other uploads. Please try again in a minute.“ (die Karte bietet „Try again“ an). Beide Plätze werden **vor** dem Lesen des Request-Bodys belegt: `POST /api/sources` liest den
   Multipart-Body als Strom (kein Formularpuffer) und nimmt das Antiforgery-Token nur aus dem Header. Eingefügter Text
   braucht keinen der Plätze, weil er schon im Speicher liegt.
2. **Streamen und Zeit.** Der Upload wird nach `<StorageRoot>/.incoming/<id>/upload.part` geschrieben (Verzeichnis
   0700, Datei 0600), unterwegs gehasht und bei `Sources:MaxFileBytes` begrenzt. Im Speicher bleiben nur die ersten
   8 KiB für die Typerkennung. Die Anfrage endet nach `Sources:UploadTimeout` (5 Minuten, HTTP 408 „The upload took too
   long. Check your connection and try again.“) und, hinter Kestrel, wenn sie langsamer als
   `Sources:MinUploadBytesPerSecond` sendet (4 KiB/s nach 10 s Schonfrist); beides gibt die Plätze frei. Ein kaputter
   oder abgebrochener Body endet als 400 „The upload could not be read.“; nur eine Datei über der Größengrenze bekommt
   413.
3. **Wiederverwendung und Kontingent.** Eine gleiche Datei desselben Besitzers wird wiederverwendet (der eindeutige
   Index `UX_Sources_OwnerUsername_Sha256` sorgt dafür, dass parallele Uploads derselben Datei bei einer Quelle
   enden). Dateien, die kein Auftrag nutzt, zählen gegen `Sources:MaxUnattachedSourcesPerUser` (50),
   `Sources:MaxUnattachedBytesPerUser` (500 MB) und `Sources:MaxUnattachedCharsPerUser` (20 Mio. Zeichen Text). Das
   Kontingent wird vor dem Lesen und noch einmal in der einfügenden Transaktion unter einer Sperre je Person
   (`pg_advisory_xact_lock(hashtext(owner))`) geprüft, sodass parallele Uploads es nicht gemeinsam überschreiten.
4. **Typprüfung.** Die Art ergibt sich aus dem Inhalt. Bevor der Webprozess ein ZIP-Archiv öffnet, liest er dessen
   Endsatz: mehr Einträge als `Sources:MaxArchiveEntries`, ein zentrales Verzeichnis über 1 KiB je erlaubtem Eintrag
   oder ein ZIP64-Verzeichnis (keine Word-Datei braucht eines) lehnen die Datei ab, ohne das Archiv aufzulisten.
   `%PDF-` in den ersten 1.024 Byte gilt absichtlich als PDF (Leseprogramme akzeptieren die Kopfzeile dort überall).
5. **Extraktion.** Text und Markdown liest der Webprozess selbst. PDF und Word belegen einen Extraktionsplatz
   (`Sources:ExtractionMaxConcurrency`, Standard 2) und laufen als
   `dotnet <Anwendungs-Assembly> --extract-source <pdf|docx> <datei>`. `Program.cs` verzweigt in diesen Modus, bevor ein
   Host gebaut wird: kein DI, keine Datenbank, keine Konfiguration, keine Anmeldung. Die Antwort des Prozesses wird
   gelesen, während sie hereinkommt; sie liegt nie als ganzes Byte-Array im Speicher.
6. **Textgrenze.** Eine Quelle hat höchstens `Sources:MaxExtractedChars` Zeichen (3 Mio.; ein Buch mit 1.000 Seiten
   hat rund 2 Mio.). Word-Dateien werden schon beim Prüfen ihrer XML-Teile gezählt, bevor das Dokumentmodell entsteht,
   und noch einmal beim Durchlaufen; das Lesen einer PDF endet bei der Seite, die die Grenze überschreitet; Textdateien
   werden vor dem Dekodieren gezählt; der Webprozess prüft die Länge noch einmal, bevor er den Text kopiert. Die
   Quittung lautet „This file contains far more text than we can use. Please upload a shorter version.“
7. **Aufbereitung in einem Durchlauf.** Zuerst werden die Zeilenenden zu `\n` (ein einzelnes `\r` beginnt also eine
   Zeile, bevor irgendetwas die Zeilen prüft), Zeilen, die wie `[[page N]]` aussehen, werden in Text-, Markdown- und
   Word-Dateien entschärft, lange Zeilen umbrochen (jedes Stück wird ebenfalls entschärft) und Zeichen und Wörter ohne
   Seitenmarken gezählt. Text, der nichts davon braucht, wird nicht kopiert. Echte Marken schreibt nur der
   PDF-Extraktor.
8. **Ablage.** Die empfangene Datei wird nach `<StorageRoot>/<Besitzer-Hash>/<id>.<endung>` verschoben; das
   temporäre Verzeichnis wird in jedem Fall entfernt.

### Grenzen des Extraktionsprozesses

| Grenze | Option | Standard | Wirkung |
|---|---|---|---|
| Gesamtzeit | `ExtractionTimeout` | 60 s | ganzer Prozessbaum wird beendet, „Reading this file took too long.“ |
| Verwalteter Speicher | `ExtractionMemoryLimitBytes` | 1 GiB | `DOTNET_GCHeapHardLimit` im Kind; ein Wächter beendet es zusätzlich oberhalb der Grenze plus 256 MiB |
| Text | `MaxExtractedChars` | 3.000.000 Zeichen | Lesen endet, „This file contains far more text than we can use. Please upload a shorter version.“ |
| Antwortgröße | `ExtractionMaxOutputBytes` | 20 MiB | Prozess wird beendet, Datei unlesbar; bemessen auf die Textgrenze mit höchstens sechs Byte je UTF-16-Einheit (Emoji und Formatzeichen werden als `\uXXXX` geschrieben, Buchstaben der Grundebene nicht maskiert) plus Platz für das JSON |
| Umgebung | — | — | geleert; nur `PATH`, `HOME`/`TMPDIR` (das private Upload-Verzeichnis), GC-Einstellungen und die Extraktionsgrenzen als JSON |
| Arbeitsverzeichnis | — | — | das private Upload-Verzeichnis |

Ein Absturz (etwa ein Stack Overflow, Exit-Code ab 128), ein Speicherende oder ein Abbruch endet als Quittung mit
„This file is too complex to read. Please save it again or split it into smaller files.“ Der Webprozess protokolliert
nur Art, Ergebnis, Dauer, Exit-Code und Spitzenspeicher, nie Dateiinhalte oder -namen.
`Sources:ExtractionMode = InProcess` gibt es nur für Tests der Extraktoren.

### Schutz in den Extraktoren

- **Word:** Archivprüfung (Einträge, Gesamtgröße `MaxUncompressedBytes` = 64 MiB, Kompressionsverhältnis ab 64 KiB
  Eintragsgröße), danach wird jeder XML-Teil einmal streamend ohne DTD und mit Tiefenlimit (`MaxXmlDepth` = 100)
  gelesen, bevor das Dokumentmodell entsteht; der Inhalt wird ohne Rekursion durchlaufen.
- **PDF:** Vor dem Öffnen werden die Flate-Ströme, die keine Bilder sind, ins Leere entpackt und aufsummiert; über
  `MaxPdfInflatedBytes` (256 MiB) wird die Datei abgelehnt. Verschachtelte Filter fangen die Prozessgrenzen ab.

### Aufräumen

Der stündliche Aufräumdienst löscht nach `Sources:OrphanFileAge` (24 h) zusätzlich liegengebliebene
Upload-Verzeichnisse, `*.part`-Dateien und abgelegte Dateien, auf die keine Quelle verweist. Er betrachtet nur die
Wurzel und die Besitzerordner darunter, überspringt Links und löst jeden Pfad innerhalb der Wurzel auf. Sind
mindestens 10 abgelegte Dateien unbekannt und machen sie mehr als die Hälfte aller alten abgelegten Dateien aus, passt
die Datenbank vermutlich nicht zum Volume (aus einer älteren Sicherung wiederhergestellt oder leer gestartet); dann
löscht er keine abgelegte Datei und protokolliert einen Fehler.

### Hinweis für den Betrieb

Die Compose-Datei begrenzt den Speicher des Web-Containers auf 6 GiB (`mem_limit: 6g`), als zweite Schicht hinter den
Grenzen oben: Der Webprozess bleibt üblicherweise unter 1 GB, jeder Extraktionsprozess darf 1 GiB verwalteten Speicher
(plus den Spielraum des Wächters) nutzen, und höchstens zwei laufen gleichzeitig; der Rest ist Puffer. Das Limit
schützt die anderen Dienste eines gemeinsam genutzten Hosts. Hat der Host weniger Speicher übrig, senkt man
`ExtractionMaxConcurrency` oder `ExtractionMemoryLimitBytes` zusammen mit dem Limit.

## Automatische Längenprüfung des Direktwegs (Zusatz zu D-070)

Die Längenprüfung ohne Modell ist bewusst konservativ: Sie blockiert nur bei eindeutiger Lage und grober Abweichung;
sonst bleibt es bei einer Notiz. `RequestConstraintExtractor` liest Längen nur außerhalb von Anführungszeichen, Code
und Text, der nach einer Einleitung angehängt ist („Antworte auf diese Mail:“). `LengthRuleGate` lässt eine Länge nur
dann zur blockierenden Prüfung werden, wenn der ganze Auftrag genau eine Längenangabe in Wörtern oder Zeichen enthält
(oder ein klares Paar wie „zwischen 100 und 150 Wörter“), keine andere Mengenangabe (Stückzahlen, „je/pro/each“,
Varianten, Seiten, Minuten), kein Wort mit Rückbezug oder Überarbeitung („Entwurf“, „letzte“, „zu lang“, „kürze“,
„zusammenfassen“, „übersetze“ …), ein an die Zahl gebundenes Grenzwort („höchstens“, „at most“, „ca.“ … oder „mit/von/in
N Wörtern“ direkt hinter dem Ergebnis), keine fremde Präposition zwischen Befehlsverb und Länge im selben Teilsatz
(„einen Titel für ein Buch mit mindestens 80.000 Wörtern“) und keine Zusammensetzung wie „300-Wörter-Text“. Jede
andere Länge ergibt die Notiz „Your request mentions a length (…); it was not checked automatically.“ Auch eine
blockierende Länge löst eine Korrektur nur aus, wenn der Text die Grenze um mehr als 25 % verfehlt („höchstens 100
Wörter“ ab 126 Wörtern, „mindestens 200 Wörter“ unter 150); eine kleinere Abweichung ist ein offener Punkt. Ist die
Länge der einzige blockierende Befund, endet die Anweisung an den Schreiber mit „Keep everything else unchanged.“ Die
Falsch-Blockier-Rate wird an synthetischen, unabhängig beschrifteten Aufträgen gemessen
(`tests/Geef.Atelier.Tests/Domain/Checks/Corpus/`, `LengthCorpusTests`).

## Vorlage `direct`: zwei Prüfer (D-073)

In der Vorlage `direct` (`SystemCrew.DirectTemplate`) schreibt der Schreiber einmal und korrigiert höchstens einmal
(`MaxIterations: 2`). Sie hat zwei Prüfer, beide setzt `DirectPipelineAssembler` beim Bau der Pipeline ein.

| Prüfer | Profil | Modell | Was er tut |
|---|---|---|---|
| Automatische Prüfungen | `deterministic-checks` | keins | `DraftCheckEvaluator`: nicht leer, keine Vorrede, Länge, Sprache, Platzhalter (in eckigen Klammern, bis 300 Zeichen auf einer Zeile; der Faktenprüfer verwirft eine gemeldete Stelle innerhalb eines Platzhalters nur bis 200 Zeichen, damit eine Aussage in einer längeren Klammer stehen bleibt), verbotene Formen und Wörter, verlangte Wörter, ausgeschriebene Rechnungen. Blockierendes → `Error`, Rest `Info`. |
| Faktenprüfung | `source-fact-check` | Slot `FactReviewer` (heute Codex Astra; nicht das Schreibermodell, eine andere Anbieterfamilie bevorzugt) | `SourceFactCheckReviewer`: vergleicht die Tatsachen im Text mit Wortlaut, Antworten, Profil und Dateien (`FactCheckPrompts`, Version `fact-check-1`). |

Die Faktenprüfung läuft bei jedem Auftrag des Direktwegs. Nur `Direct:FactCheckMode = Off`, `Shadow` oder ein Ausfall des
Modells samt seinem einen Ersatz lassen die Tatsachen ungeprüft, und „What was checked“ sagt das.

- **Material** kommt aus dem Grounding (`AtelierContextKeys.FactMaterial`): Wortlaut, Antworten der Person und des Clients,
  Profil und Dateien, bei einer Überarbeitung der Ausgangstext als `S0`. Annahmen und Notizen des Schreibers sind nie
  Material. Über `Direct:FactCheckMaxMaterialChars` (150.000 Zeichen Material und Text) bekommt die Prüfung die Dateikarte
  statt des Inhalts.
- **Filter des Codes** (`FactFindingFilter`): Ein Befund bleibt nur mit wörtlichem Zitat; ob er blockiert, folgt aus der
  Fundstelle (Wortlaut, Antworten der Person und Profil blockieren; Antworten des Clients sind Hinweise; Widersprüche der
  Dateien blockieren nie). Eine Begründung mit Link, Adresse, Konto- oder Zugangsdaten verwirft den Befund nie: Die Begründung
  wird durch einen festen Satz ersetzt, das Zitat aus dem Text bleibt (eine erfundene IBAN muss die Person sehen); unsichere
  Materialstellen werden ebenso ersetzt. „Wörtlich gedeckt“ verlangt ganze Wörter.
- **Jede Fassung wird voll geprüft.** Ein aus beliebigem Grund geänderter Text (Faktenbefund, Länge, Sprache …) bekommt
  erneut ein volles `check_facts` mit denselben Ersatzregeln; nur eine Fassung, die eine frühere Runde schon wortgleich
  geprüft hat, übernimmt deren Befunde ohne Aufruf. Ein Modell, das in seine Zeitgrenze lief, wird im selben Lauf nicht erneut gerufen. Der Bericht kennt die
  geprüfte Fassung (`FactCheckRound.TextHash`); ein ausgelieferter Text, den keine Prüfung sah, ist `VersionNotChecked` („The
  facts of this version of the text were not checked …“), und frühere Befunde erscheinen nur, wenn ihre Stelle noch darin steht.
- **Kein Befund geht zwischen Runden verloren** (`FactCarryOver`): Nach jeder vollen Prüfung bleiben die Befunde aller früher
  beantworteten Runden offen, deren Stelle noch wörtlich (ganze Wörter) im Text steht, mit ihrer Blockierwirkung, und jeder
  Quellenwiderspruch bleibt (eine Korrektur ändert die Dateien nicht). Entdoppelt werden nur Wiederholungen: eine Stelle, die
  ganz in einer behaltenen Stelle gleicher Art liegt (bei Vorgaben gegen dieselbe Vorgabe), bzw. ein Widerspruch, dessen beide
  Materialstellen einander an Wortgrenzen enthalten und der dieselben Dateien nennt; eine nur überlappende Stelle wird
  zusätzlich gezeigt. Keine Kappe, IDs neu. Wird eine frühere Fassung als die beste ausgeliefert, kommen die Befunde späterer
  beantworteter Runden auf dieselbe Weise dazu (Widersprüche immer, Stellen nur, wenn sie im ausgelieferten Text stehen, nicht
  gelistete mitgezählt). In `On` lehnt eine
  blockierende Stelle, die die zweite Prüfung nicht erneut meldet, deshalb weiter ab. Nicht gelistete Stellen früherer Prüfungen
  bleiben gezählt. Zitate werden auf ganze Wörter erweitert, damit eine unveränderte Stelle nie als „entfernt“ gilt; ohne
  Prüfung der ausgelieferten Fassung ergänzt „Removed or replaced“, dass die Aussage umformuliert noch enthalten sein kann.
- **Stufen:** `Report` (ausgeliefert) → Befunde `Warning`, Urteil `Approved`, der Text wird nie geändert; `On` → blockierende
  Befunde `Error` und `Rejected`, der Schreiber bekommt feste Sätze; `Shadow` → `Info` mit Metadatum `shadow`, nichts sichtbar.
- **Ersatzmodell:** Ist das Modell nicht erreichbar, folgt ein Versuch mit dem nächsten Kandidaten von `FactReviewer`, der
  weder das Schreibermodell noch vom gescheiterten Anbieter noch bezahlt ist (`FactCheckModelFallback`, heute Sonnet). Das
  Failover des Gateways ist auf dasselbe Modell beschränkt (D-090). Aufrufe, Modell und Ersatz stehen im Bericht
  (`factCheck`), nie als Warnung.
- **Persistenz:** Befunde behalten `Category = fact-check`, `Code` (`fact.unsupported`, `fact.instruction`, `fact.superseded`,
  `fact.source_conflict`) und `Quote`; `Runs.FactCheckMode` hält den Modus des Laufs (Migration Step50). Wird ein Konto gelöscht
  oder umbenannt, verlieren seine Läufe jede Kopie der About-me-Angaben: den Block im Auftrag, den Auftrag in den gespeicherten
  Ereignissen (`PipelineStartedEvent.Input` und jeder andere Text eines Ereignisses, der den Block oder die Angaben wörtlich
  enthält) und was die Faktenprüfung in Bericht, Befunde und Ereignisse kopiert hat. Läufe, die eine frühere Version schon
  bereinigt hat, werden mit den Angaben aus ihren Ereignissen erneut bereinigt.
- **Die Rechenprüfung sagt nie „stimmt“:** Die Prüfung ausgeschriebener Rechnungen im Text und der Summen in den Dateien nennt
  nur, was eindeutig falsch aussieht, und bittet die Person, es zu prüfen; sonst sagt „What was checked“ höchstens, dass nichts
  eindeutig Falsches gefunden wurde und dass nicht jede Rechnung so prüfbar ist. Frühere Berichte lesen sich ebenso.

## Recherche im Grounding-Schritt (D-074)

Der Grounding-Schritt des Direktwegs (`OrderGroundingStep`) lädt die Dateien des Auftrags im Volltext und kann Ausschnitte
aus dem Web anhängen. Diese Recherche (Tavily, Abschnitt `Tavily` mit `Endpoint` und `ApiKey`; ohne Schlüssel wird nichts
gesendet) ist der einzige Zugang zum Web. Der Schritt fragt
`IOrderResearchService.EnsureAsync`, bevor er die Dateien rendert, mit der Version des Laufs (vom Assembler), ob der Lauf einen
Text überarbeitet, ob der Auftrag angehängte Dateien hat, und den Angaben „Über mich“ aus dem Briefing. Ein Auftrag mit
irgendeiner Datei (gleich welche Rolle) sucht nie: Der Dienst speichert einmal den Status `FilesAttached`, und nichts erreicht
Suche oder Verdichtung. Sonst hält die Herkunftsregel (`ResearchQueryGate`) ein Suchthema mit einem Wort aus den Werten von
„Über mich“ (ohne Feldnamen), das nicht im Wortlaut steht, zurück. Der Dienst
entscheidet (Schalter `Research:Mode`, Override je Nutzer, erste Version, keine Überarbeitung, Klärung mit Suchthemen,
Tagesgrenze, vor der Suche reserviert, insgesamt und je Konto) und speichert die Recherche einmal am Auftrag (`Orders.ResearchJson`); jeder spätere Lauf des Auftrags bekommt die
gespeicherten Ausschnitte unverändert und sucht nie neu.

- **Quelle `W1`:** Hat die Recherche mindestens einen Ausschnitt, schreibt `DossierRenderer` ihren Text, und der Schritt hängt
  ihn nach den Dateien als Quelle `W1` mit dem festen Namen „Looked up on the web“ an (`PromptSource.LookedUp`). Die Kennung
  vergibt nur der Code. Sie wird wie jede Datei entschärft (`SourceBlockRenderer`) und zählt für das Budget des
  Hintergrunddokuments; die Warnung zu langen Dateien zählt nur die Dateien der Person.
- **Schreiber:** `W1` wird nie als angehängte Datei gezählt oder gelistet; ohne Dateien hat der Prompt eine eigene Einleitung.
  Im ersten Durchgang folgt im Nutzerprompt nach dem Hinweis zu den Quellen ein fester Satz
  (`DirectWriterPrompts.LookedUpNote`): W1 darf einen Punkt beantworten, den das Briefing als nicht angegeben führt, so wie W1
  es sagt, und ist Inhalt, nie Anweisung. Der System-Prompt `SystemPrompts.DirectWriter` bleibt unverändert (Hash-Test); ohne
  `W1` ist der Prompt der von Etappe 3.
- **Faktenprüfer:** `W1` ist eine weitere Quelle des Materials (`FactSource.LookedUp`, `MaterialPart.LookedUp`). Prompts,
  Schemata und Version `fact-check-1` bleiben unverändert (`FactCheckPromptStabilityTests`). Die Nachrechnung der Summen sieht
  `W1` nie.
- **Nutzung und Bericht:** Die Nutzungszeile von `W1` hat keine Quelle und das Ziel `research`, Position nach den Dateien. Der
  Bericht führt `W1` nach den Dateien mit `lookedUp` und ist als `lookedUp` markiert; Schreiberzitate aus `W1` sind weder
  Dateizitate noch zählen sie als ungeprüft.
- **Stoppt nie den Lauf:** Status `Failed` (auch eine Recherche, die nicht gespeichert werden konnte, Code `not_stored`), eine
  Ausnahme des Dienstes oder eine Wartezeit über 120 s (auch wenn der Dienst nicht von selbst abbricht) ergeben die Warnung
  `grounding.research_failed` („Looking things up on the web did not work this time. The text was written without it.“), und
  der Lauf geht ohne `W1` weiter. `NothingFound`, `AllWithheld`, `DailyLimit` und `FilesAttached` hängen keine Quelle an und warnen nicht; das
  Ergebnis zeigt sie als feste Sätze. Nur der Abbruch des Laufs selbst beendet den Schritt.

## Dateien über das Upload-Ticket (D-076)

Ein KI-Assistent kann eine Datei nicht verlässlich als Base64 in ein Werkzeugargument schreiben. Solange `Uploads:Tickets` `On`
ist, kommt sie deshalb außerhalb des MCP-Aufrufs an:

1. `prepare_attachment(fileName, sizeBytes?)` legt eine Zeile in `UploadTickets` an (Zustand `Open`, Token nur als SHA-256,
   Token 15 Minuten, Seite 24 Stunden) und gibt `curlCommand` und `uploadPage` zurück.
2. Entweder führt der Assistent `curlCommand` aus (`PUT /api/uploads/{id}`, Token im Kopf `X-Upload-Token`), oder die Person legt
   die Datei auf der Seite `/u/{id}` ab (`POST /api/uploads/{id}/page`, angemeldet). Beide Wege beanspruchen das Ticket in einer
   Anweisung (`Open → Receiving`, `Attempts + 1`, `ReceivingSince = jetzt`) und geben die Datei an dieselbe Aufnahme wie die
   Weboberfläche (`ISourceIntakeService.IntakeAsync`: Erkennung am Inhalt, Extraktion im Kindprozess, Kontingente). Herkunft der
   Quelle: Token-Weg `McpAttachment`, Seite `WebUpload`.
3. Brauchbar → `Used` mit `SourceId`. Geprüft und abgelehnt → zurück auf `Open`, der Versuch bleibt gezählt, der Code steht in
   `LastRefusalCode`; nach 3 solchen Versuchen `Failed`. Nicht geprüft (zu viele gleichzeitige Uploads, Überlast, Abbruch,
   Speicherfehler, Extraktion nicht verfügbar, volles Kontingent, Zeitgrenze, Serverfehler) → zurück auf `Open`, der Versuch wird
   zurückgegeben (`Attempts - 1`). Eine Datei, die zu lang ist, um ganz verwendet zu werden, wird auf diesem Weg abgelehnt
   (`too_long_for_ticket`; Seiten wählen geht nur in der Web-App) und nicht gespeichert. Abschluss und Freigabe gelten nur für den
   eigenen Anspruch (gleiches `ReceivingSince`).
4. `submit_order(…, uploadIds)` verknüpft die Quellen der angekommenen Tickets der angemeldeten Person mit dem Auftrag (alles oder
   nichts); ab hier ist die Datei eine normale Quelle des Auftrags.

**Aufräumen** (stündlicher Aufräumdienst der Quellen): `Receiving` länger als `Sources:UploadTimeout` + 1 Minute → `Open` bzw.
`Failed` (nach einem Absturz; ein laufender Upload endet vorher an der Zeitgrenze seiner Anfrage). Dieselbe Frist prüft der
Ticketdienst bei jedem Zugriff auf das Ticket (Seite, Token- oder Seiten-Upload) und gibt ein hängendes Ticket sofort frei, statt
auf den nächsten Lauf zu warten; der Versuch bleibt dabei gezählt. Tickets 7 Tage
(`Uploads:TicketRetention`) nach Ablauf der Seite gelöscht; Zeilen in `SourceRefusals` nach 180 Tagen
(`Uploads:RefusalRetention`). Wird eine Quelle gelöscht, wird `UploadTickets.SourceId` `NULL`.

**Ablehnungen zählen.** `SourceRefusals` hält je abgelehnter Datei Zeit, Person, Weg (`web`, `mcp_inline`, `mcp_ticket`,
`upload_page`), Code, erkannte Art, Endung, angegebenen Typ und Größe, nie Name oder Inhalt. Nicht gezählt werden ein
Token-Upload über der Anfragegrenze (`413` vor dem Anspruch), ein ungültiger Token, eine Datei, die die Upload-Seite schon im
Browser abweist, ein Upload, der an der Zeitgrenze endet oder abgebrochen wird, und technische `400`; `too_large` ist damit
untererfasst. Grundlage der Entscheidung über
weitere Dateiarten (Auslöser in D-076: mindestens 3 Ablehnungen von mindestens 2 Konten außerhalb der Messkonten in 60 Tagen).
Auswertung, nur lesend; die Liste der Messkonten kommt aus der Betriebskonfiguration (`-v messkonten='{konto1,konto2}'`):

```sql
SELECT "Code", coalesce("Extension", '-') AS "Endung", "Path" AS "Weg",
       count(*) AS "Ablehnungen", count(DISTINCT "OwnerUsername") AS "Konten"
FROM "SourceRefusals"
WHERE "CreatedAt" > now() - interval '60 days'
  AND NOT ("OwnerUsername" = ANY (:'messkonten'::text[]))
GROUP BY 1, 2, 3
ORDER BY 4 DESC, 1, 2;
```

## Nachbesserungslauf im Direktweg (D-075, Teil B)

Ein Lauf, der eine frühere Version des Auftrags ändert („Change something“, `revise_order`), läuft immer über den Direktweg. Er
trägt `RunEntity.RevisionHint` (die Änderung der Person samt Sätzen der Schnellknöpfe), `ParentRunId` (Lauf der gezeigten Version)
und `SeedDraftText` (deren ausgelieferter Text). Den Lauf legt der Auftragsdienst an; „Resume“ auf einem gescheiterten oder
gestoppten Nachbesserungslauf legt ihn genauso an (`RunService.ResumeRunAsync`: dieselbe `RevisionHint`, die jetzt gezeigte Version
als `ParentRunId` und Ausgangstext, ihr Brief neu zusammengesetzt, gespeichert über `TryAddRevisionRunAsync`). Er lehnt mit
`RunNotResumableException` ab und legt keinen Lauf an, wenn der Nachbesserungslauf schon einen eigenen Text hat
(`RevisionLines.ResumeChangedText`; die Laufseite zeigt den Satz statt des Knopfs) oder eine spätere Version des Auftrags einen Text hat
(`RevisionLines.ResumeLaterVersion`). Der Direktweg macht daraus Folgendes:

- **Grounding:** Der Assembler reicht `RevisionHint` an `OrderGroundingStep`; der Schritt setzt den Kontextschlüssel
  `AtelierContextKeys.RevisionHint`. Der frühere Text ist nur Ausgangstext, nie Material des Faktenprüfers. Die Recherche liefert wie
  bei jeder Überarbeitung nur Gespeichertes.
- **Brief:** `RevisionBrief.Compose` fügt die Änderung als Zeile „the change to make now“ an und markiert jede ältere Zeile des Blocks
  als „earlier change: already made in the text – do not make it again, keep it“; die Überschrift sagt, dass die neueste Änderung
  gilt und die Worte der Person den Blöcken „Assumptions …“ und „Not given by the person“ vorgehen. `BriefingSections.Parse` gibt die
  Änderungen getrennt von den Antworten zurück (`BriefParts.Changes`, die letzte als neueste), und das Material des Faktenprüfers
  hält sie so (`FactMaterial.Changes`). Nur der Prompt des Prüfers schreibt sie unter die Antworten der Person, mit kurzer Marke
  („newest change, version n“ / „earlier change, version n; a newer change wins where they differ“, `FactChange.Marked`) vor dem
  unveränderten Wortlaut. Nachgeschlagen werden Zitate nur in den Worten; in einem Nachbesserungslauf (`FactMaterial.IsRevision`)
  nimmt der Filter die Marke vorher aus jedem Zitat des Prüfers (`FactChange.WithoutMarks`), sodass ein reines Marken-Zitat keine
  Vorgabe ist und kein gespeichertes Zitat die Marke trägt. Im ersten Lauf eines Auftrags lässt der Filter die Zitate, wie sie sind.
- **Schreiber:** Nur im Nutzerprompt des ersten Durchgangs ersetzt `DirectWriterPrompts.RevisionInstruction` die `SeedInstruction`
  (für Anbieter ohne Dokumentmodus über dieselbe Ersetzung „the text in draft.md“ → „this text“). Wortlaut seit Tor N′ (D-075):
  Anschlusskorrekturen so klein wie möglich, Ton-, Stil- und Längenänderungen ohne neue Aussage und ohne anderes Verhältnis zwischen
  Absender und Angesprochenen, ein längerer Text führt aus, was er schon sagt. Der System-Prompt bleibt bitgleich (Hash-Test);
  Korrekturdurchgang unverändert.
- **Prüfungen:** Jede Regel des ersten Auftrags (`DraftCheckResult.IsFirstRequestRule`: Länge, Sprache, keine Platzhalter, verbotene
  Formen, verbotene und verlangte Wörter) ist im Nachbesserungslauf nur eine Notiz, nie blockierend (`DeterministicChecksReviewer.Evaluate`,
  Satz `FirstRequestNoteFormat`); leerer Text, Vorrede, Anzeige der Platzhalter, Rechenprüfung und Faktenprüfung laufen wie immer. Der
  Bericht trägt `RunReport.Revision`, und „What was checked“ nennt eingehaltene Regeln als Regeln des ersten Auftrags.
- **Vorgaben im Faktenprüfer:** Im Nachbesserungslauf (`FactMaterial.IsRevision`) blockiert ein Befund der Art `Instruction` nur,
  wenn sein Vorgabenzitat in der neuesten Änderung steht (auch wenn es zugleich anderswo steht). Jeder andere gefundene Ursprung
  (Auftrag, Antworten, „About me“, Assistent, frühere Änderung) ergibt einen Hinweis mit `FactFinding.BeforeLastChange`: nie
  blockierend, also im Modus `On` `Info` und nie im Korrekturauftrag. Übernommene Vorgaben-Befunde einer Vorversion werden ebenso
  bewertet (`FactFindingFilter.ForRevision` in `DirectReportBuilder.EarlierFindings`), und `FactCarryOver.Merge` macht einen solchen
  Hinweis nie wieder blockierend. Andere Befundarten und der erste Lauf bleiben unverändert.
- **Nie verlieren über Versionen (`DirectReportBuilder`):** Nach dem Lauf liest der Assembler den Bericht des `ParentRunId`. Dessen
  offene Befunde (Stellen und Quellenwidersprüche) gehen über `FactCarryOver.Merge` in den neuen Bericht: Stellen nur, wenn ihr Zitat
  als ganze Wörter noch dasteht, Widersprüche immer, jeweils mit `FactFinding.FromVersion` (ein schon übernommener Befund behält die
  Version, die ihn fand); meldet der neue Prüfer dieselbe Stelle, steht sie einmal als eigener Befund – außer der neue Prüfer ist `Off`
  oder im Schattenmodus, dann geht der übernommene vor. Eigene Befunde eines früheren Prüfers in `Off` oder Schattenmodus werden nicht
  übernommen, von ihm schon übernommene (mit `FromVersion`) immer. Offene Punkte und Annahmen des Schreibers der Vorversion, die die neue
  nicht wortgleich in derselben Art (`SameAssumptionKey`) wiederholt, stehen in `RunReport.EarlierOpenPoints` mit Version und
  `EarlierPointKind` (auch die von der Vorversion schon übernommenen), dazu ein fester Satz (`EarlierPointKind.Note`) für ihre Notizen und
  Stellen, die nicht gezeigt werden konnten. Hat die Vorversion keinen Bericht (z. B. ein Lauf der entfernten Prüfschleife), sagt der feste Satz
  `ChecksNotTakenOverFormat`, dass ihre Prüfungen nicht übernommen und nicht erneut geprüft wurden. Kann der frühere Bericht nicht gelesen
  werden, steht im Bericht ein Satz dazu; der Lauf geht weiter. Wurde eine Vorversion ohne lesbaren Bericht selbst als „Ready – please
  check“ gezeigt (`DirectRevision.NeedsCheck`, bestimmt mit `OrderStateResolver`), setzt der Bericht `RunReport.EarlierNeedsCheck`; eine
  spätere Version erbt es aus dem Bericht ihrer Basis. `OrderStateResolver.ToRunState` liest es in jedem Prüfmodus als übernommenen
  blockierenden Punkt. Berichte anderer
  Läufe bleiben bitgleich (`null` wird nicht geschrieben).

## Entfernte Teile (Stufe 8d, D-091)

Der Code der folgenden Teile ist entfernt; ihre Datenbanktabellen und -spalten bleiben unangetastet.

- Die alte Prüfschleife mit den Vorlagen `klassik`, `juristisch`, `akademisch`, `marketing` und jedem eigenen Team; die
  Werkstatt-Seiten und Editoren für Teams und Profile (die Provider-Seiten bleiben) und das Werkstatt-Dashboard.
- Eigene Profile und Vorlagen in der Datenbank sowie die API, um eigene Crews anzulegen oder einzureichen.
- Advisors, ihre Profile, Beratungen und Kosten.
- Die Profile der Grounding-Quellen und ihre Provider-Typen, darunter der URL-Abruf mit seiner Adressprüfung, die
  Vektorsuche, Lauf-Anhänge als Quelle und statischer Kontext.
- Alle Abschlussschritte (Umschreibungen, Dateiexport, Metadaten) und die Lauf-Dateien samt Download-Endpunkt. Der Export
  des Ergebnisses eines Auftrags (`export_order`) ist eine eigene Funktion und bleibt.
- Composer und Auto-Crew samt Kompositions-Läufen und Materialisieren sowie die Reste des Template Studios.
- Der Lernzyklus, die Vektor-Wissensbasis und Embeddings, der agentische Werkzeug-Pfad
  ([10-tool-system_de.md](10-tool-system_de.md)) und die Spezialisierungs-Packs
  ([11-specialization-packs_de.md](11-specialization-packs_de.md)).
- Die älteren MCP-Werkzeuge dieser Teile. Die heutigen Werkzeuge beschreibt
  [13-ai-assistant-guide_de.md](13-ai-assistant-guide_de.md); fünf alte Namen antworten noch eine Version lang mit einem
  Fehler, der den Nachfolger nennt.

Schon in Stufe 8c entfernte Teile hält D-089 fest.
