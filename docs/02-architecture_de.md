# Architektur

*[English](02-architecture.md) · **Deutsch***

*Letzte Aktualisierung: 2026-11-27 (zweiter Zusatz zu D-087: „Extra careful“ entfernt, eine Zusammenstellung; D-091: Etappe 8d, Dokument auf den aktuellen Stand gebracht: Es läuft nur noch der Direktweg, die entfernten Teile stehen nicht mehr in der Beschreibung; zuvor 2026-11-26: Verweis auf die Schalter der Auftragsfunktionen und die Seite `/how-it-works`; 2026-11-24, D-067: Warnungen am Lauf, `Runs.Outcome`, lautes Verhalten bei Start und Snapshot, Vorbereitungs-Zeitlimit, Live-Updates im Prozess, Ende-zu-Ende-Tests; 2026-11-22, D-066: Modellmatrix; 2026-08-04, D-064: Modell-Katalog für CLI-Provider)*

Atelier macht aus dem Anliegen und den Dateien einer Person einen fertigen Text. Die Einheit, die die Person sieht, ist der
**Auftrag**; **Läufe** sind das technische Protokoll darunter (ein Lauf je Version eines Auftrags). Seit Etappe 8d (D-091)
gibt es **eine Arbeitsweise, den Direktweg**. Die früheren Wege (die Prüfschleife mit
Crews, zusammengestellte Teams, die Learning-Schleife, der agentische Werkzeug-Pfad, die Vektor-Wissensbasis, Advisor,
Spezialisierungs-Pakete, Abschlussschritte und die Werkstatt-Editoren) sind aus dem Code entfernt; ihre Beschreibungen
bewahren die Git-Historie und das [Decisions-Log](05-decisions-log_de.md).

## Schichtenbild

```
┌──────────────────────────────────────────────────────────────────┐
│                     Frontends (zwei Adapter)                     │
│  ┌──────────────────────────────┐  ┌───────────────────────────┐ │
│  │  Web-UI (Blazor Server)      │  │  MCP-Server (/mcp)        │ │
│  │  /start, /work, /orders/{id} │  │  submit_order,            │ │
│  │  /runs, /runs/{id}           │  │  get_order_status,        │ │
│  │  Cookie-Auth                 │  │  get_order_result, ...    │ │
│  └──────────────┬───────────────┘  └─────────────┬─────────────┘ │
└─────────────────┼────────────────────────────────┼───────────────┘
                  ▼                                ▼
┌──────────────────────────────────────────────────────────────────┐
│  Anwendungsschicht: IOrderService, IRunService, Klärung,         │
│  Quellen, Profil („About me“), Provider, Auth und OAuth          │
└────────────────────────────────┬─────────────────────────────────┘
                                 ▼
┌──────────────────────────────────────────────────────────────────┐
│  Hintergrunddienste: Klärungs-Worker, Lauf-Orchestrator,         │
│  Quellen-Aufräumen, OAuth-Aufräumen, Modell-Katalog-Aufwärmen    │
└────────────────────────────────┬─────────────────────────────────┘
                                 ▼
┌──────────────────────────────────────────────────────────────────┐
│  Geef-SDK-Pipeline des Direktwegs                                │
│  OrderGroundingStep → DirectWriterExecutor → Prüfungen (höchstens│
│  zwei Durchgänge) → MarkdownFinalizer → Bericht                  │
└────────────────────────────────┬─────────────────────────────────┘
                                 ▼
┌──────────────────────────────────────────────────────────────────┐
│  Persistenz: Postgres über EF Core; Dateien auf einem Volume     │
└──────────────────────────────────────────────────────────────────┘
```

## Solution-Struktur

```
Geef.Atelier.slnx
├── src/
│   ├── Geef.Atelier.Core/           // Domain-Records, Regeln ohne LLM und Datenbank,
│   │                                // Konfigurationsklassen, Repository-Verträge
│   ├── Geef.Atelier.Application/    // Dienste: Aufträge, Läufe, Klärung, Quellen, Profil,
│   │                                // Provider, Modellzuweisung, Auth, OAuth
│   ├── Geef.Atelier.Infrastructure/ // EF Core, LLM-Clients, Modell-Katalog, Pipeline des
│   │                                // Direktwegs, Klärungs-Worker, Recherche (Websuche),
│   │                                // Textextraktion, Auftragsexport, Event-Sink
│   ├── Geef.Atelier.Web/            // Blazor-Server-UI, HTTP-Endpunkte, Orchestrator,
│   │                                // DI-Zusammenbau
│   └── Geef.Atelier.Mcp/            // Klassenbibliothek: MCP-Werkzeuge, Server-Hinweise,
│                                    // Skill; gehostet im Web-Projekt
├── tests/
│   └── Geef.Atelier.Tests/          // xUnit; EndToEnd/ = Ende-zu-Ende-Tests
└── tools/                           // Eval (Python), IntakeEval, FactCheckEval, ResearchEval,
                                     // HashPassword
```

- **Core** braucht weder Datenbank noch LLM, seine Regeln sind deshalb für sich testbar.
- **Application** enthält die Anwendungsfälle und hängt nur von Core ab.
- **Infrastructure** kapselt die externen Abhängigkeiten (Postgres, LLM-APIs, Websuche, Geef SDK).
- **Web** ist der einzige Host. **Mcp** ist eine Klassenbibliothek ohne eigenen Host: Der Endpunkt `/mcp` liegt im
  Web-Projekt, teilt dessen DI-Container und ruft dieselben Dienste auf, ohne HTTP-Sprung dazwischen.

## Aufträge und Quellen (D-069, D-070)

| Tabelle | Inhalt |
|---|---|
| `Orders` | Das Anliegen der Person im Wortlaut, Modus (`Direct`/`Careful`), Eingang (`Web`/`Mcp`/`Legacy`), Zustand (`Analyzing`, `NeedsInput`, `Ready`, `Cancelled`), Eigentümer, der Verlauf der Fragen und Antworten, das Rechercheergebnis |
| `Sources` | Eine hochgeladene Datei: extrahierter Text mit Seitenmarken (`[[page N]]`), Zähler, Qualität, Warnungen, Pfad des aufbewahrten Originals, Ablauf, solange kein Auftrag sie verwendet |
| `OrderSources` | Welche Quelle zu welchem Auftrag gehört, mit Rolle, Position und Seitenauswahl (`1-40,55`) |
| `RunSourceUsages` | Welche Datei in welchen Lauf ging, in welcher Form (`FullText`, `FullTextWithMap`) und mit wie vielen Zeichen |

`Runs.OrderId`/`Runs.OrderVersion` verbinden einen Lauf mit seinem Auftrag. `OrderService` (Application) prüft und legt
Aufträge an; `OrderStateResolver` (Core) leitet den Anzeigezustand nur aus `Orders.State` und dem aktuellen Lauf ab. Die
Größengrenzen kommen aus `SourceBudgetPlanner.Classify`, das Startseite, Auftragsdienst und der Quellenblock des Schreibers
(`SourceBlockRenderer`) gemeinsam nutzen. Der Text wird in einem eigenen, im Speicher begrenzten Prozess aus den Uploads
gelesen (`Sources:ExtractionMode = Isolated`).

**Vom Anliegen zum Lauf.** Ein Auftrag startet entweder sofort mit seinem ersten Lauf oder, wenn für ihn Rückfragen
eingeschaltet sind (`Orders:IntakeMode = On`), im Zustand `Analyzing`: Der `OrderIntakeWorker` lässt den Klärer
(`LlmClarifier`) das Anliegen lesen, das `QuestionGate` behält höchstens drei kurze Fragen, und der Auftrag wartet in
`NeedsInput`, bis die Person antwortet oder überspringt (oder die Frist `Intake:NeedsInputAutoStartAfter` abläuft). Fragen
gibt es nur vor dem Schreiben, nie währenddessen.

**Einen fertigen Text ändern** („Change something“, `revise_order`) fügt einen Lauf als nächste Version hinzu. Er wird mit
dem Text der gezeigten Version vorbelegt (`Runs.SeedDraftText`, `Runs.ParentRunId`) und trägt den Wunsch der Person in
`Runs.RevisionHint`.

## Die Pipeline des Direktwegs

Es gibt eine Zusammenstellung, nur im Code (`SystemCrew`): `direct` — Schreiber, automatische Prüfungen ohne Modell
(`DeterministicChecksReviewer`) und Faktenprüfung (`SourceFactCheckReviewer`); höchstens zwei Durchgänge, also höchstens eine
Korrektur. Jeder neue Lauf jedes Auftrags nutzt sie, gleich welcher Modus am Auftrag gespeichert ist. Aufträge mit dem
gespeicherten Modus `Careful` und Läufe mit der entfernten Vorlage `direct-outline` gehören weiter zum Direktweg (zweiter Zusatz
zu D-087): Dort werden sie gelesen, fortgesetzt und geändert; wird ein gespeicherter `direct-outline`-Lauf ausgeführt, entfällt
seine Zusatzprüfung `requirements-check`.

`IPipelineAssembler` hat genau einen produktiven Assembler, den `DirectPipelineAssembler`; der `PipelineAssemblerSelector`
weist jeden Lauf ab, den keiner annimmt (siehe [Altdaten](#altdaten-läufe-entfernter-arbeitsweisen)).
`AtelierPipelineFactory` baut die Geef-Pipeline aus dem Snapshot des Laufs:

1. **`OrderGroundingStep`** lädt den Auftrag und seine Dateien, setzt die Dateien in das Hintergrunddokument, liest die im
   Anliegen genannten Regeln, schreibt die `RunSourceUsages`, bereitet das Material der Faktenprüfung vor und legt den
   Modus der Faktenprüfung des Laufs fest (`Runs.FactCheckMode`). Für die erste Version eines Auftrags im Modus `Direct` ohne Dateien fragt
   er den `OrderResearchService` nach Stellen aus dem Web (Tavily; Schalter `Research:Mode`). Dieser Grounding-Schritt
   zusammen mit der Web-Recherche ist das einzige Grounding.
2. **`DirectWriterExecutor`** schreibt den Text. Mit einem CLI-Provider schreibt er in ein Dokument (`draft.md`) und bittet
   das Gateway, nicht auf eine andere CLI auszuweichen. In einem Korrekturdurchgang bekommt er nur die blockierenden Befunde
   des vorigen Durchgangs. Notizen des Schreibers (Annahmen, offene Punkte) werden vom Text getrennt. Eine leere oder
   unveränderte Antwort wird nie angenommen.
3. **Prüfungen** laufen nach jedem Durchgang. Welche Befunde die Korrektur auslösen dürfen, hängt von der Prüfung und von
   `Direct:FactCheckMode` ab (`Off`, `Shadow`, `Report`, `On`; D-073).
4. **`MarkdownFinalizer`** liefert den Text. Bestehen die Prüfungen in den erlaubten Durchgängen nicht, wird der
   Best-Effort-Text des SDK mit seinen offenen Punkten geliefert.
5. **Bericht:** Der `DirectReportBuilder` baut den `RunReport` (Vorgaben, Prüfergebnisse des gelieferten Textes,
   Platzhalter, verwendete Dateien, Lesebeleg, Notizen); er wird in `Runs.ReportJson` abgelegt, bevor das Ergebnis
   (`Outcome`) gesetzt wird. Die Sätze „What was checked“ der Webseite und von `get_order_result` stammen daraus.

**Konvergenzregel:** `DefaultConvergencePolicy` aus dem Abschnitt `Convergence` (`ConvergenceOptions`), überschrieben von
der Zusammenstellung (beide: zwei Durchgänge, kein Abbruch bei einem kritischen Befund, keine Regressionserkennung). Ein
Reviewer, der technisch ausfällt, blockiert nicht; der Lauf bekommt eine Warnung.

## Lauf-Orchestrator

`RunOrchestratorService` (im Web-Projekt) fragt die Datenbank alle `Orchestrator:PollingInterval` (2 s) ab, führt höchstens
`Orchestrator:MaxConcurrentRuns` (5) Läufe gleichzeitig aus und nimmt nur Läufe im Status `Pending` **mit** Snapshot, jeden
mit einem atomaren Wechsel auf `Running`. Er setzt **genau eine Instanz** voraus: Die Crash-Wiederherstellung beim Start
setzt jeden laufenden Lauf zurück.

Für jeden Lauf liest er den Snapshot streng, wählt den Assembler, führt die Pipeline mit dem `PostgresEventSink` aus
(Ereignisse, Durchgänge, Befunde, Status und Text; Live-Updates über `IRunNotifier`), speichert die Kosten, lässt den
Assembler den Bericht schreiben und setzt dann das Ergebnis. Ein Wächter fragt `Runs.CancellationRequested` ab; Abbruch
durch die Person oder ein Stopp des Dienstes enden als `Aborted`. Ein unerwarteter Fehler endet als `Failed` mit einem von
wenigen festen, einfachen Sätzen; die Einzelheiten gehen nur ins Log.

**Crash-Wiederherstellung beim Start:** Jeder Lauf, der noch `Running` ist, wird `Failed` („Service restarted“);
`Pending`-Läufe ohne Snapshot, die älter sind als diese Instanz, werden `Failed`; Läufe mit Endstatus, aber ohne `Outcome`
(beim Speichern ihres Ergebnisses gestoppt) bekommen ein Ergebnis und eine Warnung (siehe unten).

### Altdaten: Läufe entfernter Arbeitsweisen

Die Datenbank behält jede Tabelle und Spalte (keine `DROP`-Migration), damit das vorige Image sie weiter lesen kann. Deshalb
tragen neue Snapshots weiterhin eine (leere) Liste `advisors`.

- Abgeschlossene Läufe und Aufträge der entfernten Wege bleiben lesbar: Listen, „My work“, Auftragsseite, Ergebnis, Export
  und die technische Laufseite, die den Namen der Zusammenstellung zeigt und sagt, dass sie zu einer Arbeitsweise gehört,
  die es nicht mehr gibt.
- Ein wartender Lauf einer entfernten Zusammenstellung (alte Prüfschleife, zusammengestelltes Team, Learning-Lauf) schlägt
  fehl mit „This task was set up for a way of working that no longer exists, so it cannot run. Please start it again.“
- „Resume“ wird für einen solchen Lauf abgelehnt („This task was set up for a way of working that no longer exists, so it
  cannot be continued. Please start it again.“).
- Ein Snapshot, der eine Prüfung nennt, die es in dieser Version nicht mehr gibt, lässt den Lauf ebenfalls mit einem
  einfachen Satz fehlschlagen.
- Ein wartender Auftrag mit dem gespeicherten Modus `Careful` ist in diesem Sinn kein alter Weg: Er läuft auf dem Direktweg.
- Crash-Wiederherstellung und Aufräumen behandeln alte Läufe wie alle anderen. `RunKind` behält die Werte `Learning` und
  `CrewComposition`, damit alte Zeilen gelesen werden können.

## Zusammenstellungen, Snapshots und Modelle

Jeder Lauf speichert einen `CrewSnapshot` (JSON in `Runs.CrewSnapshot`): Schemaversion, Name der Zusammenstellung, das
vollständige Schreiberprofil, die Prüfprofile, die Auswertungsstrategie und die Konvergenz-Überschreibung. `ICrewService`
baut ihn aus den Profilen, die nur im Code stehen: Die Modellmatrix (`ModelMatrix`, `IModelAssignmentService`, D-066) weist
jeder Rolle mit Slot ein Modell zu, und Modell-Aliasse werden zu konkreten IDs aufgelöst. Von Nutzern änderbare Profile oder
Vorlagen gibt es nicht mehr. Die Anforderungsprüfung hat keinen Slot und behält eine feste Bindung, auf einem anderen Modell
als der Schreiber.

## Datenmodell

Die Migrationen sind handgeschrieben (`InitialCreate` und die Reihe `StepNN`). Das EF-Core-Modell bildet diese Tabellen ab:

| Gruppe | Tabellen |
|---|---|
| Aufträge und Dateien | `Orders`, `Sources`, `OrderSources`, `RunSourceUsages`, `UploadTickets`, `SourceRefusals` |
| Läufe | `Runs`, `Iterations`, `Findings`, `Events`, `RunWarnings`, `IterationActorCosts` |
| Klärung und Profil | `OrderIntakeShadows`, `UserProfiles` |
| Konten und Einstellungen | `Users`, `Providers`, `SiteSettings` |
| OAuth 2.1 | `OAuthClients`, `OAuthAuthorizationCodes`, `OAuthAccessTokens`, `OAuthRefreshTokens`, `OAuthAuditLog` |

**Tabellen entfernter Teile.** Sie sind nicht mehr abgebildet, **bleiben aber in der Datenbank** bis zu einem eigenen
Aufräumschritt, der nicht zu Etappe 8d gehört und sie entfernen darf: `AdvisorConsultations`, `AdvisorProfiles`,
`CrewTemplateEmbeddings`, `CrewTemplates`, `ExecutorProfiles`, `FinalizationActorCosts`, `FinalizerProfiles`,
`GroundingActorCosts`, `GroundingConsultations`, `GroundingProviderProfiles`, `KnowledgeDocumentChunks`,
`KnowledgeDocuments`, `LearningEntries`, `ReviewerProfiles`, `RunArtifacts`, `StudioSettings`, `TemplateStudioAnalyses`,
`mcp_server_configs`, `specialization_packs`, `tool_definitions`, `tool_invocations`. Derselbe Schritt darf die nicht
abgebildeten Spalten `Runs.GroundingCostEur`, `Runs.FinalizerCostEur`, `Runs.FinalizerErrorMessage` und
`Iterations.AdvisorsTotalCostEur` entfernen (später vielleicht auch `Orders.CrewTemplateName` und
`Runs.ParentCompositionRunId`), außerdem alte Kompositions- und Learning-Läufe sowie das frühere Export-Volume.
`Runs.AdvisorRetryAttempted` ist ebenfalls nicht abgebildet. Weil einige dieser Tabellen Vektorspalten enthalten, nutzen die
Compose-Dateien weiterhin ein Postgres-Image mit `pgvector`.

### Runs

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | uuid (PK) | |
| CreatedAt / StartedAt / CompletedAt | timestamptz | die letzten beiden nullable |
| Status | varchar(50) | Pending / Running / Completed / Failed / Aborted |
| BriefingText | text | das Briefing, das der Schreiber bekommt |
| ConfigJson | jsonb | beim Anlegen gespeicherte Konfiguration |
| FinalText | text | nullable; der gelieferte Text |
| WordCount | int | nullable; Wörter von `FinalText` |
| ErrorMessage | text | nullable; ein fester, einfacher Satz für die Person |
| TokensTotal | int | über die LLM-Aufrufe summiert |
| CostTotal, TotalCostEur, LlmCostEur | numeric | die letzten beiden nullable; Kostenerfassung |
| CachedInputTokens, ReasoningTokens | int | nullable; Teilmengen der Tokens |
| CancellationRequested | bool | gesetzt, wenn die Person abbrechen will |
| CreatedByUser | text | nullable; Eigentümer (Trennung nach Nutzern, D-042) |
| CrewTemplateName | varchar(200) | nullable; Name der Zusammenstellung (`direct` oder bei alten Läufen eine entfernte, zum Beispiel `direct-outline`) |
| CrewSnapshot | jsonb | nullable; der Snapshot der Zusammenstellung |
| Kind | int | `0 = Standard`; `1 = Learning` und `2 = CrewComposition` gibt es nur in alten Zeilen |
| ParentRunId | uuid | nullable; der Lauf, den dieser fortsetzt oder ändert |
| SeedDraftText | text | nullable; Text, von dem der erste Durchgang ausgeht |
| RevisionHint | text | nullable; die gewünschte Änderung |
| ParentCompositionRunId | uuid | nullable; nur in alten Zeilen |
| Outcome | int | nullable; `0 = Converged`, `1 = DeliveredWithOpenPoints`, `2 = Failed`, `3 = Aborted`. Siehe [Lauf-Ergebnis](#lauf-ergebnis-outcome-und-runstatus) |
| DeliveredIteration | int | nullable; Durchgang, dessen Text geliefert wurde |
| ReportJson | text | nullable; der `RunReport` des Direktwegs |
| OrderId | uuid (FK→Orders) | nullable; `ON DELETE CASCADE`. Index `IX_Runs_OrderId_OrderVersion` |
| OrderVersion | int | nullable; je Auftrag eindeutig für Standardläufe (`UX_Runs_OrderId_OrderVersion_Standard`) |
| FactCheckMode | varchar(20) | nullable; der Modus der Faktenprüfung des Laufs |

### RunWarnings

Probleme, die ein Lauf umgangen hat, statt anzuhalten (D-067). Werden mit dem Lauf gelöscht.

| Spalte | Typ | Bemerkung |
|---|---|---|
| Id | uuid (PK) | |
| RunId | uuid (FK→Runs) | indiziert |
| Iteration | int | nullable; Durchgang, in dem das Problem auftrat |
| Source | varchar(100) | `grounding`, `review`, `model-assignment`, `llm`, `run`, `intake` oder `fact-check` (`RunWarningSources`) |
| Code | varchar(100) | stabiler maschinenlesbarer Code, z.B. `grounding.research_failed`, `grounding.source_warning`, `review.reviewer_failed`, `llm.failover`, `writer.partial_read`, `run.result_not_saved`, `run.restarted_while_finishing` (`RunWarningCodes`, `DirectWarningCodes`). Alte Zeilen können Codes entfernter Teile tragen; sie behalten ihren Text und werden weiter angezeigt |
| Message | text | kurze, einfache englische Erklärung für die Person |
| CreatedAt | timestamptz | |

### Iterations, Findings, Events

- `Iterations`: `RunId`, `IterationNumber`, `ArtifactText` (der Text nach dem Durchgang), Kosten- und Token-Spalten von
  Schreiber und Prüfungen.
- `Findings`: `IterationId`, `ReviewerName`, `Severity`, `Message`, optional `Category`, `Code` und `Quote`.
- `Events`: `RunId`, `EventType` (aus dem Vokabular des Geef-Event-Sinks), `PayloadJson`.

## LLM-Provider-Schicht

Provider sind Zeilen der Tabelle `Providers` (System-Provider wie `openrouter`, `claude-cli` und `codex-cli` sowie
CLI-Provider, die ein Administrator anlegt); `Llm:ProvidersFallback` in der Konfiguration gilt für einen Namen, den der
Provider-Dienst nicht kennt. `ILlmClientResolver` liefert einen `ILlmClient`: `OpenAiResponsesClient` für Endpunkte auf
`api.openai.com`, sonst `OpenAiCompatibleClient`. Der Seitencontainer `cli-proxy` (FastAPI, Python) stellt die Abo-CLIs
hinter OpenAI-kompatiblen Endpunkten bereit, einen je CLI (zum Beispiel `http://cli-proxy:8090/v1/claude`). Ein Aufruf, der
nicht auf eine andere CLI wechseln darf, verlangt vom Gateway `x-cli-proxy-failover: same-model` (D-090).

Die Prüfungen laufen bewusst auf anderen Modellen als der Schreiber. Der Tokenverbrauch geht über den Pipeline-Kontext in
`Runs.TokensTotal`; mit `CostTracking:Enabled` werden die Kosten je Akteur und Durchgang in `IterationActorCosts` abgelegt
und am Lauf summiert. API-Schlüssel kommen aus Umgebungsvariablen; welche Namen änderbare Konfiguration nennen darf,
begrenzt eine Positivliste (`EnvReference:Allowed` zusätzlich zu einer eingebauten Liste und dem Präfix `ATELIER_SECRET_*`).

## Schutzmechanismen am Lauf (D-067)

### Lauf-Ergebnis (`Outcome`) und `RunStatus`

`Runs.Status` behält seine Bedeutung. Ein Lauf, der endet, ohne dass alle Prüfungen bestehen, endet als `Failed`, auch wenn
er einen Text liefert. `Runs.Outcome` ist das zusätzliche Signal, was der Lauf geliefert hat:

| `Outcome` | Bedeutung |
|---|---|
| `Converged` | Die Prüfungen haben bestanden; Text und Bericht sind gespeichert. |
| `DeliveredWithOpenPoints` | Der Lauf endete mit offenen Punkten und hat seinen besten verfügbaren Entwurf geliefert. |
| `Failed` | Der Lauf endete ohne brauchbaren Text, oder sein Ergebnis ließ sich nicht vollständig speichern (Warnung `run.result_not_saved`). Ein Text kann trotzdem vorhanden sein; Vollständigkeit ist nicht garantiert. |
| `Aborted` | Von der Person abgebrochen oder vom Dienst gestoppt. Ein vorher gespeicherter Text bleibt lesbar. |

`Outcome` bedeutet **fertig und gespeichert**: Es wird gesetzt, wenn der endgültige Text und der Bericht in der Datenbank
stehen. Der erste Wert gilt. `Status` und `Outcome` können kurz auseinanderfallen, weil der Event-Sink den Status zuerst
setzt; Clients warten deshalb, bis der Auftrag fertig ist und ein Ergebnis gesetzt ist (wie `scripts/atelier-smoke.sh`).
Wird der Dienst dazwischen hart gestoppt, setzt die Crash-Wiederherstellung das Ergebnis: mit Text `Converged` (Status
`Completed`) oder `DeliveredWithOpenPoints`, ohne Text `Failed`, dazu die Warnung `run.restarted_while_finishing`. Läufe aus
der Zeit vor der Spalte behalten `NULL`; repariert werden nur Läufe, die nach dem frühesten Lauf mit Ergebnis endeten.

### Warnungen am Lauf

Ein Lauf hält jedes umgangene Problem in `RunWarnings` fest: zum Beispiel eine fehlgeschlagene Web-Recherche, eine Warnung
aus dem Lesen einer Datei, eine Prüfung, die in einem Durchgang nicht abgeschlossen werden konnte, ein Modell, das nicht wie
geplant zugewiesen werden konnte, oder ein Ausweichen im LLM-Client. `IRunWarningSink` schreibt jede Warnung in einem
eigenen, kurzlebigen Datenbank-Scope; ein fehlgeschlagener Schreibvorgang wird protokolliert und hält den Lauf nie an. Der
Dienst ist eine Pflichtabhängigkeit, eine fehlende Registrierung fällt also schon beim Start auf. Der LLM-Client meldet
Ausweichvorgänge über den mitlaufenden `RunWarningScope`, den der Orchestrator je Lauf öffnet.

Warnungen erscheinen auf der technischen Laufseite und im Feld `warnings` von `get_order_status` und `get_order_result`.
Warn- und Fehlertexte am Lauf sind feste, kurze, einfache Sätze; technische Einzelheiten gehen nur ins Log. Bevor etwas
Technisches protokolliert wird, ersetzt `SecretRedactionExtensions` (Core) zugangsdatenähnliche Teile durch `[redacted]`.

### Laut statt still

| Situation | Verhalten |
|---|---|
| Eine Datenbankmigration schlägt beim Start fehl | Der Start bricht mit einem kritischen Logeintrag ab (`MigrateDatabaseOrFailAsync`). Der Health-Check `pending-migrations` meldet offene Migrationen. |
| Der Snapshot eines Laufs fehlt, ist unlesbar, leer, von unbekannter Version oder ohne Schreiber, Prüfungen oder Advisor-Liste | `CrewSnapshot.DeserializeForRun` wirft `InvalidCrewSnapshotException`; der Lauf endet als `Failed` mit klarer Meldung. Eine Ersatz-Zusammenstellung gibt es nicht; das tolerante `Deserialize` dient nur der Anzeige. |
| Ein Snapshot hat keine Prüfungen oder nennt eine unbekannte | `AtelierPipelineFactory` baut die Pipeline nicht; der Lauf endet als `Failed` mit Meldung. |
| Ein Lauf gehört zu einer entfernten Arbeitsweise | Kein Assembler nimmt ihn an; er schlägt mit dem unter [Altdaten](#altdaten-läufe-entfernter-arbeitsweisen) zitierten Satz fehl. |
| Ein Lauf ohne brauchbaren Snapshot wird fortgesetzt | `RunService.ResumeRunAsync` lehnt mit `RunNotResumableException` ab, statt einen Lauf anzulegen, der nie starten kann. |
| Der Suchschlüssel fehlt | Eine Warnung mit dem Namen des Schlüssels beim Start (`ResearchKeyGuard`); Werte werden nie protokolliert. |

### Vorbereitungs-Zeitlimit für Läufe mit Anhängen

Lauf-Anhänge wurden in Etappe 8d (D-091) entfernt; die Dateien eines Auftrags werden gelesen, bevor sein Lauf angelegt wird.
Die Absicherung selbst bleibt: Der Orchestrator nimmt nur `Pending`-Läufe **mit** Snapshot, und ein `Pending`-Lauf, der nach
`Orchestrator:PreparationTimeout` (Standard 15 Minuten) noch keinen hat, wird als `Failed` beendet, mit der Bitte, ihn neu
einzureichen; ein Neustart räumt auf dieselbe Weise auf.

## MCP-Server

Der MCP-Server (`Geef.Atelier.Mcp`) läuft unter `/mcp` über zustandsloses Streamable HTTP mit Bearer-Anmeldung. Er bietet
zehn Werkzeuge: `submit_order`, `answer_order`, `get_order_status`, `get_order_result`, `list_orders`, `cancel_order`,
`revise_order`, `export_order`, `prepare_attachment` (nur solange `Uploads:Tickets` auf `On` steht) und `profile`. Die fünf
Namen `submit_request`, `submit_auto_crew_request`, `get_run_status`, `get_run_result` und `get_run_details` bleiben für
eine Version als Stummel registriert: Jeder Aufruf scheitert mit einer Meldung, die den Nachfolger nennt. Die Werkzeuge
rufen `IOrderService` auf, denselben Dienst wie die Webseiten. `McpToolCatalog` registriert die Werkzeuge einzeln, und
`McpToolBindingCheck` verhindert den Start, wenn ein Werkzeug-Parameter, der ein Dienst ist, nicht registriert ist.
Einzelheiten: [MCP-Integration](04-mcp-integration_de.md) und [Endpunkt-Referenz](09-endpoint-reference_de.md).

## Konfiguration

Die Einstellungen kommen aus `appsettings.json` und Umgebungsvariablen (`__` statt `:`) und werden beim Start gelesen.

| Abschnitt | Inhalt |
|---|---|
| `ConnectionStrings` | Postgres-Verbindung (`DefaultConnection`) |
| `Llm`, `CliProxy`, `ModelCatalog` | Ersatz-Provider, Zugang zum CLI-Gateway, Modelllisten |
| `Orchestrator` | Abfrageintervall, parallele Läufe, Abbruchabfrage, Vorbereitungs-Zeitlimit |
| `Convergence` | Standardwerte der Konvergenzregel |
| `Sources`, `Uploads` | Grenzen und Aufbewahrung von Dateien; Upload-Tickets |
| `Orders` | Grenzen und Wartezeiten der Aufträge, Rückfragen (`IntakeMode`), Text ändern (`Revise`), `TellOnce` |
| `Intake` | der Klärer und seine Fragen |
| `Direct` | Notizen des Schreibers, Nachrechnen von Summen in Dateien, Faktenprüfung |
| `Research`, `Tavily` | Web-Recherche und ihre Grenzen; `Tavily:Endpoint` und `Tavily:ApiKey` |
| `Mcp` | `Skills` und `Elicitation` (D-088) |
| `Ui` | `WorkshopForAdminsOnly` |
| `AtelierUser`, `AtelierMcp`, `OAuth` | erster Administrator, statisches MCP-Token und sein Nutzer, OAuth-Server |
| `Pricing`, `CostTracking` | Preistabelle und Kostenerfassung |
| `EnvReference` | zusätzlich erlaubte Namen von Umgebungsvariablen |
| `Logging`, `AllowedHosts` | Standardeinstellungen von ASP.NET Core |

Die Schalter der Auftragsfunktionen, ihre ausgelieferten Werte und ihre Ausnahmen je Person stehen im
[Admin-Leitfaden](14-admin-guide_de.md#funktionen-schalten). Die öffentliche Seite `/how-it-works` liest dieselben Schalter.

`Ui:WorkshopForAdminsOnly` (Standard `true`) blendet das Werkstatt-Menü für Nicht-Administratoren aus; das Menü enthält nur
„Runs“ und für Administratoren „Providers“. Der Schalter ändert, was gezeigt wird, nicht die Zugriffsrechte.

## Testen und Messen

### Ende-zu-Ende-Tests (`tests/Geef.Atelier.Tests/EndToEnd/`)

`AtelierEndToEndHost` startet die **echte** Anwendung über `WebApplicationFactory<Program>` gegen eine eigene Datenbank je
Test auf dem Testcontainers-Postgres der `PostgresFixture`. Migration, DI-Verdrahtung, Klärung, Orchestrator, Pipeline,
Event-Sink und MCP-Endpunkt sind der produktive Code. Nur externes HTTP wird ersetzt: `ScriptedLlmEndpoint` steht für den
cli-proxy und antwortet je Rolle, `LlmTrafficRecorder` zeichnet die Anfragen auf, `FakeTavilyEndpoint` und
`FakeCliProxyModelsEndpoint` liefern Websuche und Modellliste, jeder andere benannte HTTP-Client ist gesperrt.

```bash
export MSBUILDNODEREUSE=0 DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet test tests/Geef.Atelier.Tests/Geef.Atelier.Tests.csproj --filter "FullyQualifiedName~Geef.Atelier.Tests.EndToEnd"
```

Die Tests brauchen Docker und laufen in einer eigenen Sammlung ohne Parallelität. Ein neuer Weg durch das System soll einen
eigenen Ende-zu-Ende-Test bekommen, vom Eingang bis zum gespeicherten Ergebnis.

### Test gegen exportierte Snapshots

`ProductionSnapshotFixtureTests` liest exportierte Snapshots durch das strenge Lesen zur Laufzeit und prüft, dass jeder
entweder auf dem Direktweg läuft oder mit dem einfachen Satz oben abgewiesen wird. Die Exporte enthalten echte Aufträge und
gehören **nie** ins Repository; ohne die Variablen wird der Test übersprungen.

| Variable | Bedeutung |
|---|---|
| `ATELIER_SNAPSHOT_FIXTURES` | Verzeichnis mit einem exportierten Snapshot je Lauf (`*.json`). |
| `ATELIER_SNAPSHOT_FIXTURES_MIN` | Mindestzahl der Dateien im Export (Standard 1), damit ein unvollständiger Export auffällt. |

### Messwerkzeuge und Betriebsskripte

`tools/Eval` vergleicht das Atelier im blinden Paarvergleich mit anderen Wegen, einen Text zu erzeugen
([`tools/Eval/README.md`](../tools/Eval/README.md)); echte Aufgaben bleiben außerhalb von Git. `IntakeEval`,
`FactCheckEval` und `ResearchEval` messen Klärer, Faktenprüfung und Recherche.

`scripts/atelier-smoke.sh` (ein Mini-Auftrag über MCP nach jedem Deploy: `submit_order`, bei Rückfragen einmal
überspringen, `get_order_status` bis fertig, `get_order_result`) und `scripts/atelier-watch.sh` (Alarm bei hängenden Läufen
und ungesundem cli-proxy) sind in [`ops/atelier-smoke.md`](ops/atelier-smoke.md) und
[`ops/atelier-watch.md`](ops/atelier-watch.md) beschrieben.

## UI-Architektur

**Blazor Server.** Derselbe .NET-Stack wie das Geef SDK und Live-Status ohne eigene Client-Anwendung.

### Wichtigste Seiten

| Route | Funktion |
|---|---|
| `/start` | Neuer Auftrag: Anliegen, Dateien mit Quittungen |
| `/work`, `/orders` | „My work“: die Aufträge der Person |
| `/orders/{id}` | Auftragsseite: Fragen, Fortschritt, Text, offene Punkte, Versionen, Änderungen, Export |
| `/about-me` | „About me“ (das Profil, das in Texten verwendet wird) |
| `/runs`, `/runs/{id}` | Technische Laufliste und Laufseite |
| `/crew/providers…` | Provider-Verwaltung (Administratoren) |
| `/admin/users`, `/admin/oauth-clients`, `/admin/site-settings` | Verwaltung |

Die vollständige Liste samt öffentlicher Seiten steht in der
[Endpunkt-Referenz](09-endpoint-reference_de.md#web-ui---account-endpunkte).

### Live-Updates

Die Seiten abonnieren den prozessinternen `IRunUpdateFeed` (`Web/Notifications/`, `InProcessRunUpdateFeed`, registriert
durch `AddRunLiveUpdates()`); einen eigenen SignalR-Hub der Anwendung gibt es nicht.

- Das Thema leitet der Server aus dem angemeldeten Nutzer ab (`RunUpdateTopics`). Das Thema eines Laufs oder Auftrags
  bekommt nur der Eigentümer oder ein Administrator; Listenthemen tragen die eigenen Einträge, für Administratoren alle.
- Jeder Abonnent hat eine eigene Warteschlange; Handler laufen nacheinander, gleiche wartende Updates werden
  zusammengefasst, und das Veröffentlichen wartet nie auf Abonnenten.
- Der Zugriff wird beim Abonnieren entschieden; ein Abonnement gilt so lange wie der Circuit der Seite.

`LiveRunNotifier` setzt die Core-Verträge `IRunNotifier` und `IOrderNotifier` um und veröffentlicht in den Feed; ein Lauf
eines Auftrags aktualisiert auch die Auftragsthemen. Benachrichtigungen sind Best-Effort.

**`CircuitDataGate`** (je Circuit): Alle Komponenten eines Circuits teilen sich einen `DbContext`, deshalb läuft jeder
Datenbankzugriff einer Seite mit Live-Updates durch `DataGate.RunAsync(...)`. Aufrufe dürfen nicht geschachtelt werden.

## Auth-Strategie

### Web-UI — Cookie-Auth

> **Multi-User seit Step20 (D-041-Umfeld):** Ursprünglich Single-User aus
> Environment-Variablen; inzwischen **DB-basierte Mehrbenutzerverwaltung**
> (Tabelle `Users`, BCrypt). Der Admin-Account wird beim Start aus
> `ATELIER_USER`/`ATELIER_PASSWORD_HASH` geseedet/synchronisiert; weitere
> Konten verwaltet der Admin unter `/admin/users` (`IUserAdminService`).
> `IUserAuthenticator` liefert seitdem ein `AtelierUser?` (statt nur `bool`).
> Die Cookie-Konfiguration unten gilt unverändert.

BCrypt-Hash (work factor 11) wird via `tools/HashPassword/` erzeugt.

**Cookie-Konfiguration:**

| Option | Wert |
|---|---|
| Cookie-Name | `Atelier.Auth` |
| `HttpOnly` | `true` |
| `SameSite` | `Strict` (Produktion) / `Lax` (Test-Env) |
| `SecurePolicy` | `SameAsRequest` (Dev) / `Always` (Prod) |
| `ExpireTimeSpan` | 30 Tage |
| `SlidingExpiration` | `true` |
| `LoginPath` | `/login` |

**Login-Flow (Static SSR):**

```
Anonymer Browser → /runs → [Authorize] → RedirectToLogin
  → NavigationManager.NavigateTo("/login?ReturnUrl=%2Fruns")
  → Login.razor (Static SSR, kein @rendermode)
  → POST /login (Blazor Static SSR Form-Handler, @formname="login-form")
  → IUserAuthenticator.ValidateCredentialsAsync (BCrypt.Verify)
  → HttpContext.SignInAsync → Cookie gesetzt → Redirect zu /runs
```

**Wichtig: Login-Page muss Static SSR bleiben.** `@rendermode InteractiveServer` würde die Form-POST im WebSocket-Kontext abwickeln ohne `HttpContext` → `SignInAsync` wäre nicht aufrufbar. Das `@formname="login-form"`-Attribut auf dem `<form>`-Element ist Pflicht für Blazor Static SSR Form-Routing.

**Logout:** `POST /auth/logout` (Minimal API) mit `<AntiforgeryToken />` in der `UserMenu`-Komponente. GET-Logout wäre ein CSRF-Angriffspunkt.

**`IUserAuthenticator`-Schicht:**

```
Geef.Atelier.Core/Configuration/AtelierUserOptions.cs   → POCO für Username/PasswordHash
Geef.Atelier.Application/Auth/IUserAuthenticator.cs     → Interface (Application, nicht Infrastructure)
Geef.Atelier.Application/Auth/AtelierUserAuthenticator.cs → BCrypt.Verify + CryptographicOperations.FixedTimeEquals
Geef.Atelier.Application/Auth/ApplicationAuthExtensions.cs → AddAtelierAuth(IServiceCollection, IConfiguration)
```

`AtelierUserAuthenticator` ist `internal sealed`. Env-Var-Fallback (`ATELIER_USER`/`ATELIER_PASSWORD_HASH`) wird in `ApplicationAuthExtensions` aufgelöst — docker-compose-User müssen nicht die ASP.NET-Core-Doppelunderstrich-Konvention kennen.

**Timing-Schutz:** `FixedTimeEquals` für Username-Vergleich, `BCrypt.Verify` aufgerufen auch bei falschem Username (konstante Timing-Eigenschaft). Kein Username/Password-Hash wird in Logs geschrieben — nur `"Login attempt rejected"` (ohne PII).

**Lazy-Fail bei fehlender Konfiguration:** Service startet auch ohne Env-Vars, Login gibt `false` zurück. Health-Check bleibt anonym (`.AllowAnonymous()` auf `MapHealthChecks`). Init-Warning-Log beim ersten fehlkonfigurierten Login-Versuch.

**`ForwardedHeaders` vor `UseAuthentication`:**

```csharp
app.UseForwardedHeaders();   // ZUERST — damit Request.IsHttps korrekt ist
app.UseAuthentication();
app.UseAuthorization();
```

Traefik terminiert TLS und leitet HTTP weiter. Ohne `UseForwardedHeaders` würde `SecurePolicy.Always` in Produktion Cookies blockieren. `KnownIPNetworks.Clear()` öffnet für alle Proxy-IPs (Docker-Netzwerk-invariant).

### Test-Auth-Bypass

`TestAuthenticationHandler` (in `tests/Geef.Atelier.Tests/Web/E2E/`, `internal sealed`) markiert jeden Request als pre-authenticated mit `ClaimTypes.Name = "test-user"`. `WebTestHost.StartAsync(authenticated: true/false)` — `true` aktiviert den Test-Handler, `false` startet echte Cookie-Auth mit BCrypt-wf=4-Hash für LoginFlow/LogoutFlow-Tests. **Der Handler darf nie in `Program.cs` oder dem Web-Projekt referenziert werden.**

### MCP-Server — Bearer-Token / Multi-Auth (umgesetzt in Schritt 9, siehe D-022)

**Multi-Auth-Schema-Setup:** Die Anwendung nutzt zwei parallele Authentication Schemes.

| Scheme | Name | Zweck |
|---|---|---|
| Cookie | `CookieAuthenticationDefaults.AuthenticationScheme` | Web-UI, Default-Scheme |
| Bearer | `"Bearer"` | MCP-Endpoint `/mcp`, explizit via `McpPolicy` |

**Default-Scheme:** Cookie (alle Blazor-Routen, `[Authorize]` ohne Argument).

**MCP-Endpoint:** Ist explizit mit `RequireAuthorization("McpPolicy")` geschützt. Die `McpPolicy` setzt das Authentication-Scheme auf `"Bearer"`, sodass der MCP-Pfad nie Cookie-Auth versucht.

**`ITokenValidator` / `BearerTokenHandler` (Stand nach D-041 OAuth 2.1):**

`ITokenValidator.ValidateTokenAsync` liefert seit D-041 ein reiches Ergebnis
`TokenValidationOutcome { IsValid, Kind, Subject, ClientId, Scope }` (nicht mehr nur `bool`).

```
Geef.Atelier.Application/Auth/ITokenValidator.cs           → Interface (Application Layer)
Geef.Atelier.Application/Auth/StaticTokenValidator.cs      → Statisches ATELIER_MCP_TOKEN
                                                              (FixedTimeEquals); Kind="static-bearer"
Geef.Atelier.Application/Auth/OAuthAccessTokenValidator.cs → OAuth-Access-Token via DB-Lookup
                                                              (SHA-256-Hash); Subject = OAuth-Nutzer
Geef.Atelier.Application/Auth/CompositeTokenValidator.cs   → registriert als ITokenValidator:
                                                              prüft statisch, dann OAuth
Geef.Atelier.Web/Auth/BearerTokenHandler.cs                → AuthenticationHandler; baut Claims
                                                              aus dem Outcome (Name/NameIdentifier/Role)
```

`BearerTokenHandler` mappt das Outcome auf Claims: `ClaimTypes.Name` ← `Subject`,
`ClaimTypes.NameIdentifier` ← `ClientId ?? Subject`, und für statisches Bearer-Token
`ClaimTypes.Role = "admin"`. Damit greift die Run-User-Isolation (D-042) auch über MCP:
OAuth-Aufträge gehören dem autorisierenden Nutzer, Aufträge mit dem statischen Token `AtelierMcp:StaticTokenUser` oder dem konfigurierten Administrator.
`ICurrentUserService`/`HttpContextCurrentUserService` exponieren `Username`/`IsAdmin`
für Service- und MCP-Schicht.

**OAuth 2.1 ist seit D-041 vollständig implementiert** (kein „nach dem Skeleton“ mehr):
self-hosted Authorization Server mit Pflicht-PKCE/S256, Opaque-Tokens (nur SHA-256 in DB),
Refresh-Rotation + Reuse-Detection. Endpunkt- und Flow-Details siehe
[`04-mcp-integration.md`](04-mcp-integration_de.md) und [`09-endpoint-reference.md`](09-endpoint-reference_de.md);
Begründungen im [Decisions-Log](05-decisions-log_de.md) D-041. Beide Auth-Pfade
(statisches Bearer-Token für Claude Code CLI, OAuth 2.1 für Claude Desktop/Claude.ai)
koexistieren ohne Konfigurationsänderung.

## Production-Deployment

### Traefik-Flow

```
Browser → HTTPS:443 → Traefik (TLS über Let's Encrypt, Cert-Resolver 'le') → HTTP:8080 → Container geef-atelier-web → ASP.NET
```

Traefik terminiert TLS und leitet HTTP (Port 8080) an den Container weiter, der keinen Port nach außen öffnet.

| Parameter | Wert |
|---|---|
| Externes Netz | `proxy` |
| Cert-Resolver | `le` (HTTP-Challenge über den Entry-Point `web`) |
| Entry-Point (HTTPS) | `websecure` |
| HTTP→HTTPS-Weiterleitung | global in `traefik.yml` (kein eigener Redirect-Router in der App) |
| Middleware-Kette | `chain@file` (Secure-Header, Kompression, Rate-Limit) |
| `traefik.docker.network` | `proxy` (Pflicht, wenn ein Container in mehreren Netzen ist) |

`ASPNETCORE_ENVIRONMENT=Production` schaltet `CookieSecurePolicy.Always` ein. `ForwardedHeaders` (vor
`UseAuthentication`) liest `X-Forwarded-Proto=https`; ohne das würden die Cookies blockiert. Cookie- und Bearer-Anmeldung
funktionieren hinter dem Proxy gleich.

### Postgres und Backup

Die Compose-Datei betreibt einen eigenen Postgres-Container (Image `pgvector/pgvector:pg16`, siehe
[Datenmodell](#datenmodell)). Der Dienst `postgres-backup` (`prodrigestivill/postgres-backup-local:16`) schreibt täglich um
03:00 UTC einen gzip-komprimierten SQL-Dump in das Volume `geef-atelier-backups` (7 tägliche, 4 wöchentliche, 6 monatliche).
Wiederherstellung: `scripts/restore-backup.sh <datei.sql.gz>`. Hochgeladene Originale liegen auf dem Volume
`geef-atelier-sources`, außerhalb des Datenbank-Dumps.

### Reviewer-Kalibrierung

[`06-reviewer-calibration.md`](06-reviewer-calibration_de.md) beschreibt die Schweregrade und Regeln für Reviewer-Rollen.

### Deployment-Ablauf

1. Die Datei `.env` anlegen (`openssl rand`, `tools/HashPassword`); sie steht in `.gitignore` und kommt nie ins Repository.
2. Die Images bauen und den Compose-Stack starten; die Migration läuft beim Start (D-010), eine fehlgeschlagene Migration
   hält den Start an (D-067).
3. Traefik erkennt den Container an seinen Labels und stellt das Zertifikat aus.
4. Nach jedem Deploy `scripts/atelier-smoke.sh` ausführen ([`ops/atelier-smoke.md`](ops/atelier-smoke.md)).

Das nächtliche Selbst-Update des cli-proxy (`scripts/cli-proxy-selfupdate.sh`) baut nur aus dem committeten Stand; bei nicht
committeten Änderungen unter `cli-proxy/` pausiert es und schickt einen Alarm, statt zu bauen (D-067).

### Modell-Katalog für CLI-Provider (D-064)

CLI-Provider führen ihre Modellliste nicht im Atelier. `ModelCatalog` liest sie über `ICliModelSource` /
`CliProxyModelSource` vom cli-proxy (`GET /v1/cli/{providerName}/models`). Das Gateway meldet, woher die Liste stammt
(`x_source`: `live`, `static` oder `degraded`) und bei claude eine `x_aliases`-Zuordnung von `claude-<Familie>-latest` zum
konkreten Modell.

* **Die Herkunft ist dreiwertig.** `ModelCatalogSource` unterscheidet `Live`, `StaticByDesign` und `Fallback`.
* **Die Live-Liste wird mit der gepflegten zusammengeführt**, nie ersetzt.
* **Die Modellmatrix enthält `*-latest`-Aliasse, Lauf-Snapshots konkrete IDs.** Der Alias wird einmal beim Bau des Snapshots
  aufgelöst (`CrewSnapshotModelResolver`): Neue Läufe folgen neuen Versionen, ein fortgesetzter Lauf behält das Modell
  seines Vorgängers.

Zwischenspeicher: 24 h für eine erfolgreiche CLI-Abfrage, 1 h für HTTP-Provider, 5 Minuten nach einem Fehler.
`ModelCatalogWarmUpService` wärmt jeden aktiven CLI-Provider kurz nach dem Start und jede Nacht auf;
`ModelCatalog:CliLiveCatalogEnabled` schaltet auf die konfigurierten Listen zurück.

## Observability

- `PostgresEventSink` (ein `IGeefEventSink`) schreibt Pipeline-Ereignisse in die Datenbank und löst Live-Updates aus.
- `System.Diagnostics.ActivitySource("Geef.Sdk")` für verteiltes Tracing; die Pipeline nutzt die Middleware des SDK für
  Fehlerbehandlung und Tracing.
- Protokollierung über `Microsoft.Extensions.Logging` auf die Konsole.
