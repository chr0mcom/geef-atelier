# Leitfaden: Atelier entwickeln und betreiben

***Deutsch** · [English](15-developer-guide.md)*

*Letzte Aktualisierung: 2026-11-26 (About me am Ergebnis, D-083; davor nach Prüfung: Websuche, Darstellung unter `/docs`, Einstellungstabelle nur für Admins)*

Dieser Leitfaden ist für Entwicklerinnen und Entwickler und für alle, die Atelier selbst betreiben. Er gibt die Karte des heutigen
Systems und verweist auf die ausführlichen Dokumente. Die Einrichtungsbefehle stehen im [README](../README_de.md); die Gründe
hinter jeder Regel im [Entscheidungslog](05-decisions-log_de.md) (zuletzt D-067 bis D-077).

## Das System in einem Bild

Seit dem Umbau im November 2026 (D-068) ist der **Auftrag** die Einheit, die eine Person sieht. Läufe bleiben darunter das
technische Protokoll (ein Lauf je Version). Standard ist der **Direktweg**: klären, nachschlagen (wenn eingeschaltet), ein
Schreibaufruf mit den Dateien im Volltext, Prüfungen, höchstens eine Korrektur. Die ältere Prüfschleife („Crews“) gibt es weiter
für „Extra careful“ und die älteren Werkzeuge.

```mermaid
flowchart TB
    subgraph Entry["Zwei Eingänge, ein Dienst"]
        W["Web (Blazor Server)<br/>/start, /orders/{id}, /work"]
        M["MCP-Server (/mcp)<br/>submit_order, get_order_result …"]
    end
    W --> OS["IOrderService / OrderService"]
    M --> OS
    OS -->|"Orders:IntakeMode = On"| IW["OrderIntakeWorker<br/>LlmClarifier + QuestionGate"]
    IW -->|"Fragen"| Q["needs_input<br/>(answer_order / Karte im Web)"]
    Q --> R
    IW -->|"keine Fragen"| R["Lauf (Pending)"]
    OS -->|"ohne Klärung"| R
    R --> O["RunOrchestratorService"]
    O --> PA["IPipelineAssembler<br/>direct 90 · composition 100 · resume 50 · classic 0"]
    PA --> G["OrderGroundingStep<br/>Dateien im Volltext, Recherche W1, Briefing"]
    G --> X["Schreiber (Dokumentmodus,<br/>ohne Failover)"]
    X --> C["DeterministicChecksReviewer<br/>SourceFactCheckReviewer"]
    C -->|"blockierend, einmal"| X
    C --> REP["RunReport (Runs.ReportJson)"]
    REP --> RES["OrderCheckLines / OrderResultRules<br/>dieselben Sätze für Web und MCP"]
```

## Wo was liegt

| Thema | Code |
|---|---|
| Aufträge, Zustände, Versionen | `Application/Orders/OrderService.cs`, `Core/Domain/Orders/OrderStateResolver.cs` |
| Fragen vor dem Start | `Infrastructure/Intake/LlmClarifier.cs`, `Core/Domain/Intake/QuestionGate.cs`, `Application/Intake/OrderIntakeService.cs` |
| Dateien | `Infrastructure/Sources/SourceIntakeService.cs` (Extraktion im Kindprozess), `Infrastructure/Sources/UploadTicketService.cs` |
| Direktweg | `Infrastructure/Pipeline/Assembly/DirectPipelineAssembler.cs`, `Infrastructure/Pipeline/Direct/` |
| Prüfungen ohne Modell | `Core/Domain/Checks/RequestConstraintExtractor.cs`, `Infrastructure/Pipeline/Direct/DeterministicChecksReviewer.cs` |
| Faktenprüfung | `Infrastructure/Pipeline/Direct/FactCheck/SourceFactCheckReviewer.cs` |
| Recherche | `Infrastructure/Research/OrderResearchService.cs`, `Core/Domain/Research/DossierBuilder.cs` |
| Was die Person liest | `Application/Orders/OrderCheckLines.cs`, `Application/Orders/OrderResultRules.cs`, `Web/Components/UI/Orders/` |
| MCP-Werkzeuge und -Anweisungen | `Mcp/McpToolCatalog.cs`, `Mcp/McpServerInstructions.cs`, `Mcp/Tools/` |
| Erklärseite | `Web/Components/Pages/HowItWorks.razor`, `Web/Display/HowItWorksFeatures.cs` |
| About me am Ergebnis („Tell Atelier once“) | `Application/Orders/AboutMeTopics.cs`, `Application/Orders/AboutMeLines.cs`, `Application/Intake/ProfileMemoryResolution.cs`, `Web/Components/UI/Orders/OrderAboutMe.razor`, `Mcp/Tools/ProfileTool.cs` |
| Schalter | `Core/Configuration/*Options.cs`, `Web/appsettings.json` |

Alle Pfade liegen unter `src/Geef.Atelier.*`.

## Die Erklärseite folgt den Schaltern

`/how-it-works` ist ohne Anmeldung erreichbar. `HowItWorksFeatureExtensions.FeaturesFor` leitet ab, was die Seite als verfügbar
bezeichnen darf – aus denselben Optionen und denselben Ausnahmeregeln wie die Anwendung (`OrderOptions.ReviseModeFor`,
`ResearchOptions.ModeFor`, `DirectOptions.FactCheckModeFor`). Die Pipeline nutzt dieselben beiden Hilfen für Recherche und
Faktenprüfung. Getestete Regeln (`tests/…/Web/HowItWorks`):

- eine Funktion, die für die betrachtende Person aus ist, erscheint nie als verfügbar, auch wenn eine andere Person eine Ausnahme
  hat;
- abgemeldet zeigt die Seite die Werte für alle; die Tabelle mit Einstellungsnamen und Rohwerten erscheint nur für Administratoren
  und zeigt nie eine Ausnahme;
- Recherche gilt nur zusammen mit `Orders:IntakeMode = On` als verfügbar, und nur für den Standardweg: Die Vorlage `direct` hat keine
  Grounding-Quelle, ihr einziger Webzugang ist `OrderResearchService`. „Extra careful“ sucht nicht im Web; ausdrücklich gewählte
  Werkstatt-Teams (auch über die älteren Werkzeuge) nutzen ihre eigenen Quellen, und dazu kann eine Websuche gehören;
- einen unbekannten Modus der Faktenprüfung beschreibt die Seite als „meldet“, nie als „korrigiert“;
- Einschalten für alle braucht keine Änderung an der Seite;
- der Kasten „What was checked“ im Beispiel nutzt die festen Sätze aus `OrderCheckLines`; die Einleitungen der offenen Punkte und
  der Annahmen vergleicht ein Test mit `OrderOpenPoints.razor` und `OrderAssumptions.razor`.

Wer einen Schalter ergänzt, der ändert, was eine Person tun kann, nimmt ihn in derselben Änderung in `HowItWorksFeatures` und in
die Seite auf.

## Tests und Messungen

- **xUnit + Testcontainers:** `dotnet test` braucht einen Docker-Daemon. Für alles, was EF-Abfragen oder Migrationen berührt,
  echtes Postgres bevorzugen.
- **Ende-zu-Ende über die echte Verdrahtung:** `tests/Geef.Atelier.Tests/EndToEnd` startet die Anwendung gegen Postgres und
  ersetzt nur die HTTP-Gegenstellen (D-067). Jeder neue Pipeline-Weg bekommt hier einen Test.
- **Browser:** Playwright-Tests in `tests/Geef.Atelier.Tests/Web/E2E` (`WebTestHost`); mit `ATELIER_SCREENSHOT_DIR` legen einige
  davon Bildschirmfotos ab.
- **Messwerkzeuge:** `tools/Eval` (blinder Paarvergleich), `tools/IntakeEval` (Klärer), `tools/FactCheckEval` (Faktenprüfung),
  `tools/ResearchEval` (Recherche). Eine Änderung an Prompt oder Schema von Klärer, Faktenprüfer oder Stellenauswahl macht die
  gespeicherte Messung ungültig; für Klärer und Faktenprüfer werden Tests mit Eingabe-Hashes rot, bis neu gemessen ist.
- **Drift-Schutz für die Doku:** Die Werkzeugtabelle in [13-ai-assistant-guide_de.md](13-ai-assistant-guide_de.md) und ihrer
  englischen Fassung wird mit dem Werkzeugkatalog verglichen; jeder Leitfaden muss in beiden Sprachen existieren und für `/docs`
  eingetragen sein. Jedes unter `/docs` ausgelieferte Dokument wird in einem Test gerendert: Mermaid-Blöcke und HTML-Kommentare
  fallen weg (GitHub zeichnet die Diagramme), Links auf freigegebene Dokumente führen nach `/docs/…`, Links auf andere Dateien des
  Repositorys zu GitHub, und Links in die nicht veröffentlichten Arbeitsordner werden zu reinem Text.

## Regeln, die es ehrlich halten

- Nie mehr behaupten, als geprüft wurde; nie einen Befund still verlieren. „What was checked“ besteht nur aus festen Sätzen und
  wörtlichen Zitaten, nie aus eigenen Worten eines Modells über seine Arbeit (D-073).
- Laut statt still: Eine fehlende Registrierung scheitert beim Start, eine übersprungene Quelle oder ein ausgefallener Prüfer wird
  zur Warnung am Lauf (D-067).
- Migrationen sind handgeschrieben, additiv und idempotent und brauchen `[DbContext]` und `[Migration]`, sonst überspringt EF sie
  still.
- Das Repository ist öffentlich: keine Geheimnisse, keine Betriebsdetails, keine Kontonamen, keine echten Aufträge in Tests oder
  Beispielen.
- UI-Texte nur Englisch (D-062); Doku zweisprachig (Englisch führend, Deutsch als `_de.md`).
- About me ist streng je Konto: Die Ergebnisfelder `TellOnce` und `FromAboutMe` werden nur für die Person gefüllt, der der Auftrag
  gehört, die Web-Komponente erscheint nur ihr, und `IProfileService.SavePartAsync` sowie `profile` schreiben nur in das Profil der
  angemeldeten Person, und nur in leere Felder, in einer gesperrten Anweisung (`IUserProfileRepository.FillEmptyAsync`); Name, Tätigkeit
  und Organisation sind eine Zeile ohne Steuerzeichen, und keine Zeile der Unterschrift darf mit einem Feldnamen aus
  `ProfilePrompt.Labels` beginnen. Die Themen kommen aus dem gespeicherten Lesen des Auftrags (`IntakeRecord`) und dem Profilblock des
  gespeicherten Briefings, nie aus Wörtern im Text; Klärer-Prompt und Briefing von Aufträgen ohne ein solches Thema bleiben
  unverändert (D-083).

## Betrieb

- Docker Compose mit Webdienst, Postgres (pgvector) und dem CLI-Gateway `cli-proxy`; Ersteinrichtung, Sicherung und Wiederherstellung
  im README.
- Ändern sich beide, gilt die Reihenfolge: erst `cli-proxy`, dann `web` (D-064, D-066).
- Nur ausliefern, wenn kein Lauf `Pending` oder `Running` ist (D-077).
- Nach jedem Deploy `scripts/atelier-smoke.sh` ausführen ([ops/atelier-smoke.md](ops/atelier-smoke.md)); `scripts/atelier-watch.sh`
  meldet hängende Läufe und ungesunde Dienste ([ops/atelier-watch.md](ops/atelier-watch.md)).
- Schalter: siehe [Leitfaden für die Administration](14-admin-guide_de.md#funktionen-schalten).
