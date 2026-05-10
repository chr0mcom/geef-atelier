# Claude-Code-Prompt: Schritt 4 — EventSink und Persistierung

*Diese Datei ist als Eingabe für Claude Code gedacht.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Schritte 1–3 sind abgeschlossen: Solution + DB + Pipeline-Skelett + echte Anthropic-Provider laufen. Deine Aufgabe ist **Schritt 4 von 10**: **EventSink und Persistierung** — die Brücke zwischen der laufenden Pipeline und dem Postgres-Schema aus Schritt 1.

Was sich ändert: Ein neuer `PostgresEventSink` wird in die Pipeline eingehängt. Ab jetzt landet jeder Pipeline-Run mit allen Iterationen, Findings, Events und Token-Verbrauch in der DB. Was bleibt unverändert: Provider, Pipeline-Struktur, Anthropic-Client. Wir berühren weder LLM-Calls noch Pipeline-Provider — wir hängen nur eine zusätzliche Senke an.

DB-Tabellen aus Schritt 1 (`Runs`, `Iterations`, `Findings`, `Events`) werden jetzt erstmals "echt" befüllt. BackgroundService kommt erst in Schritt 5, UI in Schritt 7, MCP in Schritt 9.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors, alle Hard Rules.

**Phase-1.4-Hinweis:** In Schritt 3 hat **Level 2** geklappt — konkret die Form `cat /tmp/prompt.txt | claude -p` (Pipe-Redirect). Probiere das direkt, falls Level 1 (`claude -p` mit Heredoc) interaktiv hängt. Atelier-Level-4-Fallback nur wenn auch das scheitert.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

Vor Beginn der Implementierung lies in dieser Reihenfolge:

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`CLAUDE.md`** im Repo-Root.
3. **`docs/02-architecture.md`**, besonders das DB-Schema und das Mapping auf GEEF-Provider.
4. **`docs/03-walking-skeleton-plan.md`**, Abschnitt "Schritt 4".
5. **`docs/05-decisions-log.md`**, alle Einträge **D-010 bis D-013** — insbesondere die Realfakten-Listen aus D-010, D-012, D-013. Sind verbindlich.
6. **`docs/reports/step-03-report.md`**, besonders **Sektion 8 (Empfehlungen für Schritt 4)** — der Executor hat dort konkrete Hinweise zu Token-Tracking, verlässlichen Event-Daten, Schema-Stabilität.
7. **Aktueller Code im Repo:**
   - `src/Geef.Atelier.Core/` — Domain-Records (`RunEntity`, `IterationEntity`, `FindingEntity`, `EventEntity`, `RunStatus`-Enum, Atelier-eigenes `FindingSeverity`)
   - `src/Geef.Atelier.Infrastructure/Persistence/` — `AtelierDbContext` und EF-Konfigurationen
   - `src/Geef.Atelier.Infrastructure/Pipeline/` — `AtelierContextKeys`, `AtelierPipelineFactory`, `LlmExecutionStep`, `LlmReviewer`
   - `src/Geef.Atelier.Infrastructure/Llm/` — `AnthropicTokenUsage`-Record (für Token-Format)
8. **Geef SDK Event-Typen:** Die Liste der Events, die das SDK feuert. Im SDK lesen — nicht erfinden. Verlässliche Events laut Schritt-3-Bericht:
   - `PipelineStartedEvent`, `PipelineCompletedEvent`, `PipelineFailedEvent`
   - `GroundingPhase*`, `ExecutionPhase*` (insbesondere `ExecutionPhaseCompletedEvent` mit Draft im Context)
   - `EvaluationApprovedEvent`, `EvaluationRejectedEvent` (jeweils mit Findings)
   - `FinalizePhase*`
   - Reviewer-Events (Started/Completed je Reviewer × Iteration)

## In Schritten 1–3 etablierte Realfakten (verbindlich)

Aus D-010, D-012 und D-013 fixiert. Brainstorming-Doku-Annahmen die diesen widersprechen sind ungültig. Zentrale Punkte für Schritt 4:

**SDK-Vokabular:**
- `Geef.Sdk.Results.FindingSeverity` mit `{ Info, Warning, Error, Critical }`. **Nicht Major/Minor.**
- `EvaluationApprovedEvent` / `EvaluationRejectedEvent` (keine Phase-Started/Completed-Events für Evaluation).
- `ConvergenceFailedException` bei `AbortOnCritical = true`. Exception-Message enthält `"AbortCriticalBlocker"`. `PipelineFailedEvent` feuert, `PipelineCompletedEvent` nicht.
- `Finding.Fingerprint` = SHA-256 der Message → Base64 → 12 Zeichen → `{name}:{hash}`. `Finding.Category = "review"`, `Finding.ArtifactReference = "draft"`.
- `Finding.Metadata` ist `IReadOnlyDictionary<string, object>`.
- `using SdkGeef = Geef.Sdk.Geef;` für statische Builder-Methoden.

**Atelier-Konventionen:**
- `internal sealed` für alle Provider; `<InternalsVisibleTo Include="Geef.Atelier.Tests" />` ist gesetzt.
- Context-Keys in `AtelierContextKeys` mit `geef:atelier:`-Präfix.
- Pipeline-Konstruktion über `AtelierPipelineFactory.Build(...)` (Production) oder `BuildWithProviders(...)` (Tests).
- **Token-Verbrauch wird in `ExecutionResult.Notes` als String geschrieben:** `tokens_in=X tokens_out=Y`. Schritt 4 muss diese Form parsen — oder den Architect entscheiden lassen, ob ein typisierter Context-Key (`AtelierContextKeys.TokenUsage`) eingeführt wird, wo `LlmExecutionStep` zusätzlich zur Notes-Form den `AnthropicTokenUsage`-Record ablegt.

**Atelier-Domain:**
- `RunStatus { Pending, Running, Completed, Failed, Aborted }`.
- `RunEntity` hat: `Id` (Guid), `CreatedAt`, `StartedAt?`, `CompletedAt?`, `Status`, `BriefingText`, `ConfigJson` (jsonb), `FinalText?`, `ErrorMessage?`, `TokensTotal`, `CostTotal`.
- `IterationEntity` hat: `Id`, `RunId`, `IterationNumber`, `ArtifactText`, `CreatedAt`.
- `FindingEntity` hat: `Id`, `IterationId`, `ReviewerName`, `Severity` (Atelier-eigenes Enum, nicht SDK!), `Message`, `CreatedAt`.
- `EventEntity` hat: `Id` (long identity), `RunId`, `EventType`, `PayloadJson` (jsonb), `CreatedAt`.

**Open in Schritt 4 zu klären:**
- Atelier-`FindingSeverity` ↔ SDK-`FindingSeverity` Mapping. Atelier-Werte stammen aus `02-architecture.md` (`Critical / Major / Minor / Info`). SDK-Werte sind `Info / Warning / Error / Critical`. Mapping muss explizit sein. Architect-Frage.

## Konkrete technische Anforderungen für Schritt 4

### `PostgresEventSink` (in `src/Geef.Atelier.Infrastructure/Persistence/`)

Neuer Sink, der vier Aufgaben gleichzeitig erfüllt:

**(1) Raw Event Logging** — jedes empfangene SDK-Event wird als Row in der `Events`-Tabelle persistiert. `EventType` = Type-Name oder Type-FullName (Architect-Entscheidung). `PayloadJson` = JSON-Serialisierung des Event-Records via `System.Text.Json`. `CreatedAt` = SDK-Timestamp falls vorhanden, sonst `DateTimeOffset.UtcNow`.

**(2) Run-Lifecycle-Aktualisierung:**
- `PipelineStartedEvent` → bestehender Run-Record auf `Status = Running`, `StartedAt` setzen. (Run wurde vorher mit `Pending` angelegt — siehe (5) unten.)
- `PipelineCompletedEvent` → `Status = Completed`, `CompletedAt` setzen, `FinalText` aus `FinalizedDocument.Markdown` extrahieren.
- `PipelineFailedEvent` → `Status` setzen abhängig vom Fehlertyp:
  - Bei `ConvergenceFailedException` mit `"AbortCriticalBlocker"` in der Message → `Status = Aborted` mit klarer `ErrorMessage` (z.B. `"Aborted due to critical reviewer finding"`).
  - Bei sonstigem Failure → `Status = Failed` mit Exception-Details.

**(3) Iterations-Snapshots:**
Bei `ExecutionPhaseCompletedEvent` extrahiere den aktuellen Draft (vermutlich aus dem Event-Payload oder dem mitgegebenen Context, gegen `AtelierContextKeys.CurrentDraft` o.ä.) und persistiere eine neue `IterationEntity` mit der aktuellen `IterationNumber` und dem Draft als `ArtifactText`. Architect klärt den exakten Zugriffspfad — Context oder Payload.

**(4) Findings-Persistierung:**
Bei `EvaluationApprovedEvent` / `EvaluationRejectedEvent` extrahiere die Findings für die aktuelle Iteration und persistiere sie als `FindingEntity` rows mit dem Mapping:
- `ReviewerName` = aus dem Finding (oder Reviewer-Identifier).
- `Severity` = Atelier-eigenes Enum, gemappt vom SDK-Wert (siehe Mapping-Frage oben).
- `Message` = direkt übernehmen.

**(5) Run-Initialisierung:**
Der Sink schreibt Lifecycle-Updates auf einen *bestehenden* Run-Record. **Wer legt den initialen Run-Record an?** Architect-Frage — entweder:
- Eine neue `IRunPersistenceService.CreateRunAsync(briefing, config)` Methode, die in Schritt 6 zu `IRunService.SubmitRunAsync` wird; im Skeleton manuell vom Test/Caller aufgerufen.
- Oder beim ersten `PipelineStartedEvent` automatisch ein Run-Record angelegt (riskanter, weil `RunId` noch fehlt — siehe nächster Punkt).

### `RunId`-Propagation — kritischer Architect-Punkt

Die `EventEntity`-Tabelle braucht eine `RunId`. Der `IGeefEventSink` bekommt vom SDK Events, die nicht zwingend eine `RunId` enthalten. Wege, wie der Sink die `RunId` weiß:

- **Variante A:** Pro Run-Start einen frischen Sink instantiieren mit injizierter `RunId`. Das passt zum DI-Pattern, braucht aber eine `IPostgresEventSinkFactory` oder ein Scoped-Lifetime-Setup.
- **Variante B:** `RunId` über den `RunContext` als `ContextKey<Guid>` propagieren — Sink liest aus dem mitgegebenen Context. Setzt voraus, dass das SDK den Context an Events weiterreicht (im SDK prüfen).
- **Variante C:** Ein Singleton-Sink, der eine `RunId` per `AsyncLocal<Guid>` während des Pipeline-Runs hält (Geef SDK nutzt `AsyncLocal` selbst — siehe Brainstorming-Notizen zu Advisor-Pattern).

Architect entscheidet — die Wahl beeinflusst Schritt 5 (BackgroundService) und Schritt 6 (IRunService) direkt.

### Token-Tracking

Aus Schritt 3 schreibt `LlmExecutionStep` Token-Info als String in `ExecutionResult.Notes`: `tokens_in=X tokens_out=Y`. Drei Optionen:

- **Option A — String parsen:** Im EventSink den `Notes`-String per Regex parsen. Funktioniert sofort, ist aber fragil.
- **Option B — Typisierter Context-Key:** `AtelierContextKeys.TokenUsage` als `ContextKey<AnthropicTokenUsage>` (oder `ContextKey<TokenUsage>` mit eigenem Atelier-Record) einführen. `LlmExecutionStep` schreibt zusätzlich zum String den typisierten Wert. EventSink liest typisiert.
- **Option C — Eigenes Custom-Event:** `LlmExecutionStep` feuert ein `AtelierTokensUsedEvent` mit den Werten. EventSink hört darauf.

Empfehlung des Schritt-3-Berichts: Option B oder C. Architect entscheidet. Akkumuliere die Werte pro Run in `RunEntity.TokensTotal`.

**Cost-Tracking ist NICHT Schritt-4-Scope** — `CostTotal` bleibt 0 oder null. Modell→Preis-Mapping kommt später.

### DbContext-Lifetime im Sink

Der `PostgresEventSink` ist vermutlich Singleton (Geef-Pipeline nutzt einen Sink-Instance pro Run, aber unsere Factory erzeugt sie pro Run sowieso neu). Pro Event-Empfang braucht er aber einen frischen `AtelierDbContext` — sonst Concurrency-Probleme.

Pattern: Inject `IServiceScopeFactory`, in jedem Event-Handler `using var scope = factory.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();`. Architect bestätigt.

### Tests (in `tests/Geef.Atelier.Tests/`)

Drei neue Test-Familien:

**A) `PostgresEventSinkPersistsCompleteRun`** — End-to-End-Test mit Testcontainers-Postgres und `FakeAnthropicClient`:
1. Run-Record vorab anlegen (Status=Pending).
2. Pipeline durchlaufen lassen.
3. Verifizieren:
   - Run hat `Status=Completed`, `CompletedAt` gesetzt, `FinalText` nicht-null.
   - 2 Iterations mit ArtifactText (für die zwei Iterationen unseres Test-Setups).
   - Findings für Iteration 1 (BriefingTreueStub + KlarheitStub haben rejected).
   - Events-Log enthält alle erwarteten Event-Typen, in chronologischer Reihenfolge.
   - `TokensTotal > 0` (FakeAnthropicClient liefert Token-Counts mit).

**B) `PostgresEventSinkHandlesCriticalAbort`** — wie A, aber mit `CriticalFakeAnthropicClient`:
1. Run-Record vorab anlegen.
2. Pipeline durchlaufen lassen.
3. Verifizieren:
   - Run hat `Status=Aborted` (nicht `Failed`!).
   - `ErrorMessage` enthält Hinweis auf Critical-Abort.
   - `CompletedAt` gesetzt.
   - Iteration mit dem Critical-Finding ist persistiert.
   - `PipelineFailedEvent` ist im Events-Log.

**C) `PostgresEventSinkConcurrentRuns`** — zwei Runs parallel mit derselben Sink-Konfiguration:
1. Zwei Run-Records anlegen.
2. Beide Pipelines parallel laufen lassen (zwei `Task`s).
3. Verifizieren:
   - Keine vermischten Events (jedes Event ist dem richtigen Run zugeordnet).
   - Keine Race-Condition-Exceptions.

**Bestehende Tests:** Alle 11 Tests aus Schritten 1–3 müssen weiter grün bleiben.

### Migration

Falls das Schritt-1-Schema irgendwo unzureichend ist (z.B. fehlender Index, falsche Spalten-Constraint), neue EF-Migration. Idealerweise reicht das bestehende Schema — Schritt 1 hat es exakt für diesen Zweck designed. **Wenn nicht** doch eine Migration nötig wird: Migration-Name `Step04Persistence` mit klarer Beschreibung im Bericht.

### EventSink-Komposition

Der bestehende `LoggingEventSink` (aus Schritt 2) bleibt aktiv für Live-Output. Der neue `PostgresEventSink` wird zusätzlich angehängt — Geef SDK unterstützt Multiple-Sinks via Composite-Pattern. Architect entscheidet ob Composite explizit gebaut wird oder das SDK das transparent macht.

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` ohne Fehler oder Warnings.
2. `dotnet test` (mit Docker-Daemon für Testcontainers): alle Tests grün — 11 bestehende + neue Persistence-Tests.
3. Nach `PostgresEventSinkPersistsCompleteRun` enthält die DB:
   - 1 Run mit Status=Completed, FinalText, TokensTotal > 0.
   - 2 Iterations mit nicht-leerem ArtifactText.
   - ≥ 2 Findings für Iteration 1, 0 Findings für Iteration 2.
   - Vollständigen Event-Log mit allen erwarteten Event-Typen.
4. Nach `PostgresEventSinkHandlesCriticalAbort` ist Run.Status = Aborted, ErrorMessage erkennbar.
5. `PostgresEventSinkConcurrentRuns` zeigt saubere Run-Trennung — kein Cross-Contamination.
6. `geef_architecture.md` existiert (R4-Pflicht).
7. **Vorab oder parallel zu diesem Schritt:** Der Integration-Test `AtelierPipelineRealAnthropicTests` aus Schritt 3 wird einmal mit echtem API-Key ausgeführt. Du stellst den Key selbst aus deinen verfügbaren Credentials bereit (siehe Step-3-Prompt-Konvention). Ergebnis im Bericht festhalten — bestätigt grünes Licht für Schritt 5.

## Was du in diesem Schritt NICHT tust

- **Kein BackgroundService** — Tests rufen den Runner direkt auf. Schritt 5.
- **Kein `IRunService`** — Schritt 6.
- **Keine UI-Anbindung** — Schritt 7.
- **Kein MCP** — Schritt 9.
- **Keine Cost-Berechnung** — `CostTotal` bleibt 0 oder null.
- **Keine Crash-Recovery / Resume-Logik** — naive Variante in Schritt 5.
- **Keine Provider-Änderungen** — `LlmExecutionStep` und `LlmReviewer` bleiben unverändert. **Ausnahme:** Wenn der Architect Option B oder C für Token-Tracking wählt, darf `LlmExecutionStep` minimal ergänzt werden um den typisierten Wert zusätzlich zu schreiben — der Notes-String bleibt aber zur Rückwärtskompatibilität.

## Architect-Konsultation (Phase 1.4) — fünf Schwerpunkte

1. **`RunId`-Propagation:** Variante A (Sink-pro-Run via Factory), B (über Context-Key), oder C (AsyncLocal)? Jede hat Folgen für Schritt 5/6.
2. **Run-Initialisierung:** Wer legt den initialen Run-Record an — eine `IRunPersistenceService.CreateRunAsync` (Vorgriff auf Schritt 6) oder automatisch beim ersten Event?
3. **Token-Tracking:** String parsen, typisierter Context-Key, oder Custom-Event? Empfehlung Schritt-3-Bericht: typisierter Weg.
4. **`Severity`-Mapping:** SDK-`FindingSeverity { Info, Warning, Error, Critical }` ↔ Atelier-`FindingSeverity { Info, Minor, Major, Critical }`. Mapping muss explizit, idealerweise als statische Methode oder Extension. Wo liegt die — Core, Infrastructure?
5. **DbContext-Lifetime im Sink:** `IServiceScopeFactory`-Pattern bestätigen. Was mit Race-Conditions bei vielen schnellen Events?

## Persistenter Abschlussbericht für den Brainstorming-Chat

Bericht nach `docs/reports/step-04-report.md`, gleicher Aufbau wie Schritt-1- bis Schritt-3-Berichte. Wichtig in diesem Schritt:

1. **Was wurde umgesetzt** — Datei-für-Datei.
2. **Annahmen und Abweichungen** — vor allem zu Event-Payload-Strukturen (welche Property heißt wie?), Context-Key-Erweiterungen, Mapping-Implementierungen.
3. **Architect-Output** — welcher Invocation-Level, welche Entscheidungen zu RunId / Run-Init / Token / Severity-Mapping / DbContext-Lifetime.
4. **Pre-Mortem & Devil's Advocate** — speziell zu Race-Conditions, Event-Reihenfolge-Annahmen, DB-Connection-Erschöpfung bei vielen Events.
5. **Reviewer-Iterationen** — Tabelle.
6. **Akzeptanzkriterien-Check** — Tabelle, inklusive AC7 (Real-Anthropic-Test einmal grün).
7. **Beobachtungen zum Geef-Event-System** — welche Events sind verlässlich, welche Payload-Strukturen waren überraschend, wo musste man im SDK-Code lesen?
8. **Empfehlungen für Schritt 5 (BackgroundService)** — was ist beim Polling-Mechanismus zu beachten? Wie sehen die `Pending`→`Running`-Transitions sauber aus angesichts der jetzt etablierten `IRunPersistenceService`-Schnittstelle?
9. **Status-Update zum Real-Anthropic-Test** — lief er grün? Was waren echte Token-Verbräuche und Latenzen?

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- TreatWarningsAsErrors aus Schritt 1 respektieren.
- API-Key niemals in source control, niemals in Logs, niemals im Bericht.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.