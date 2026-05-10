# Claude-Code-Prompt: Schritt 2 — Pipeline-Skelett mit Stub-Providern

*Diese Datei ist als Eingabe für Claude Code gedacht.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Schritt 1 (Solution-Setup mit Postgres und EF Core) ist abgeschlossen. Deine Aufgabe ist **Schritt 2 von 10**: das **Pipeline-Skelett mit Stub-Providern** — das erste Lebenszeichen der eigentlichen GEEF-Pipeline aus dem [Geef SDK](https://github.com/chr0mcom/geef).

Das Ziel ist nicht ein "fertiges" System — es ist ein **End-to-End-Beweis**, dass:
1. Die Geef-Pipeline mit Builder/Runner sauber konfiguriert werden kann.
2. Convergence-Loop und Evaluation-Strategy funktionieren.
3. Der EventSink alle relevanten Events liefert.
4. Die Provider-Verträge (`IGroundingStep`, `IExecutionStep`, `IReviewer`, `IFinalizer<T>`) korrekt implementiert sind.

Echte LLM-Calls kommen erst in Schritt 3. Persistierung in der DB kommt erst in Schritt 4. Diese Disziplin ist wichtig — wir bauen schichtweise, jede Schicht einzeln verifizierbar.

## Vorgehen

**Du folgst strikt dem Workflow in `/srv/docker/docs/geef-workflow.md`.** Vier Phasen, fünf Reviewer, Pflicht-Advisors, alle Hard Rules. Bei Konflikten zwischen diesem Prompt und dem Workflow gilt der Workflow.

### Atelier-spezifische Phase-1.4-Konvention (Level-4-Fallback)

Der Workflow definiert für Phase 1.4 die Invocation-Levels 1–3 (Standard `claude -p`, file-based input, interaktive Subsession). Falls alle drei scheitern, sagt der Workflow "halt-and-escalate" — und überlässt einen projekt-spezifischen Level-4-Fallback dem jeweiligen Step-Prompt.

**Für Geef.Atelier:** Wenn Levels 1–3 alle scheitern, schreibt der Executor `geef_architecture.md` selbst, aber **ausschließlich** mit den folgenden drei Bestandteilen — fehlt einer, kein Proceed:

1. **Pflicht-Header** ganz oben in der Datei:
   `> ⚠️ Architect-Fallback: Levels 1–3 failed (see report). Executor-authored — verify against existing architecture docs.`
2. **Explizite Diff-Sektion** gegen `docs/02-architecture.md`: was wurde übernommen, was ist neu für diesen Schritt, was wäre eine Abweichung von `02-architecture.md` (sollte es nicht geben — falls doch, eskalieren).
3. **Dokumentation im Phase-4-Bericht**: welche Levels gescheitert sind, mit den genauen Fehlermeldungen. Das hält die Workflow-Lücke sichtbar, bis der Maintainer eine bessere Lösung findet.

In Schritt 1 ist Level 1 gescheitert (Stdin-Redirect-Konflikt). Probiere also direkt **Level 2** zuerst — das umgeht das Problem. Welcher Level zum Einsatz kam, gehört zwingend in den Bericht.

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

Vor Beginn der Implementierung lies in dieser Reihenfolge:

1. **`/srv/docker/docs/geef-workflow.md`** — der Workflow.
2. **`CLAUDE.md`** im Repo-Root — die Projekt-Regeln und Doku-Hierarchie.
3. **`docs/02-architecture.md`** — Layer-Architektur und Mapping auf GEEF (Abschnitt "Mapping auf GEEF-Provider (Skeleton)").
4. **`docs/03-walking-skeleton-plan.md`**, Abschnitt "Schritt 2" — der Scope dieses Tasks.
5. **`docs/05-decisions-log.md`**, Einträge **D-010 (Realfakten aus Schritt 1)** und **D-011 (Architect-Workflow-Update + Atelier-Konvention)** — was etabliert ist und welche Konventionen jetzt gelten.
6. **`docs/reports/step-01-report.md`** — kompletter Schritt-1-Bericht inkl. Empfehlungen (Abschnitt 7).
7. **Geef SDK selbst — verbindliche Quelle für die Provider-Verträge:**
   - [README](https://github.com/chr0mcom/geef) — Übersicht, Konventionen.
   - `src/Geef.Sdk/Pipeline/IGroundingStep.cs`, `IExecutionStep.cs`, `IReviewer.cs`, `IFinalizer.cs` — exakte Method-Signaturen, Result-Records, Context-Mechanismus.
   - `src/Geef.Sdk/Pipeline/Convergence/` — `IConvergencePolicy` und Default-Implementierungen.
   - `src/Geef.Sdk/Pipeline/Evaluation/` — `IEvaluationStrategy` und Default-Implementierungen.
   - `src/Geef.Sdk/Events/IGeefEventSink.cs` und das Event-Vokabular.
   - `src/Geef.Sdk/Builder/` — wie `GeefPipelineBuilder<TOutput>` und `GeefPipelineRunner<TOutput>` zusammenhängen.

   **Wichtig:** Erfinde keine Geef-API. Lies das SDK und nutze es so, wie es ist. Wenn ein Stub-Reviewer ein `ReviewResult` zurückgibt, verwende exakt die SDK-eigenen Records.

## In Schritt 1 etablierte Realfakten (verbindlich)

Diese sind aus Schritt 1 fixiert und werden in Schritt 2 nicht hinterfragt oder verändert:

- **NuGet:** `Geef.Sdk 1.0.0-ci.1` via `Directory.Packages.props`. Falls `1.0.0` stable während der Arbeit veröffentlicht wird, darfst du updaten — aber dokumentiere das im Bericht.
- **Solution-Format:** `.slnx`. Nicht zurück auf `.sln`.
- **Build-Properties:** `TreatWarningsAsErrors=true` mit `CS1591` global suppressed. Das heißt: dein neuer Code muss warning-frei sein, aber XML-Doc-Comments sind nicht hart erzwungen.
- **Domain-Records:** `RunEntity`, `IterationEntity`, `FindingEntity`, `EventEntity` mit `sealed record` + `required init`-Properties. Falls du ein neues Domain-Record brauchst (z.B. `FinalizedDocument`), folgst du diesem Stil.
- **UI-Component-Library:** `src/Geef.Atelier.Web/Components/UI/`. In Schritt 2 fügst du *keine* neuen UI-Komponenten hinzu — Schritt 2 ist Pipeline-Logik, kein UI. Aber falls doch jemals nötig: Komponenten gehen dorthin.
- **Migration-Strategie:** Auto-on-Startup mit try-catch ist OK fürs Skeleton, wird in Schritt 10 für Production re-evaluiert. In Schritt 2 keine Änderung.
- **Health-Check:** Macht echte DB-Probe, liefert 503 bei DB-Ausfall. Bleibt unverändert.

## Konkrete technische Anforderungen für Schritt 2

### Provider-Implementierungen (in `src/Geef.Atelier.Infrastructure/Pipeline/`)

Alle vier Provider als **`internal sealed`** Klassen in einem neuen Namespace `Geef.Atelier.Infrastructure.Pipeline`:

#### `BriefingGroundingStep : IGroundingStep`

Stub-Verhalten: liest das Briefing aus dem `RunContext` und schreibt einen "Grounded-Brief" zurück. Im Skeleton ist der Grounded-Brief schlicht das Briefing selbst, ohne RAG, ohne externe Lookups.

**Context-Schlüssel:** Verwende stark-typisierte `ContextKey<T>`-Konstanten gemäß SDK-Konvention (Präfix `geef:` ist der Default des SDK; eine Atelier-Untergruppe wie `geef:atelier:` für unsere eigenen Schlüssel ist sinnvoll). **Architect entscheidet die finale Naming-Convention** in Phase 1.4.

Ergebnis: Der SDK-eigene Grounding-Result-Record. Bitte exakt den vom SDK definierten Typ verwenden — nicht erfinden.

#### `StubExecutionStep : IExecutionStep`

Stub-Verhalten: liest den Grounded-Brief und die `PreviousFindings` (falls vorhanden) und produziert Text nach folgender Logik:
- **Iteration 1:** `"DRAFT v1 — Briefing: {briefing}\n\n[Stub-Output]"`
- **Iteration N>1:** `"DRAFT v{N} — addressed {findingsCount} findings: {comma-separated finding messages}\n\n[Stub-Output]"`

Ein eindeutiger Iterations-Marker (z.B. `"DRAFT v{N}"`) muss im Output enthalten sein, damit der Konsolen-Test ihn am Ende verifizieren kann.

Der Iteration-Counter wird **nicht** vom Provider selbst gehalten (wäre Mutation und gegen das Geef-Prinzip) — er kommt aus dem Run-State des SDK, gelesen über den Context oder die Method-Parameter. Bitte das SDK studieren und den richtigen Mechanismus nutzen — der Architect klärt das in Phase 1.4.

#### `StubReviewer` (zwei Instanzen mit unterschiedlichen Namen)

Stub-Verhalten: gibt in **Iteration 1** je ein Finding zurück, ab **Iteration 2** keine Findings mehr (Approved). Damit konvergiert die Pipeline garantiert nach 2 Iterationen.

Konkrete Findings für Iteration 1:
- Reviewer-Instanz 1 (`"BriefingTreueStub"`): Severity Major, Message `"Stub finding: simulated briefing-coverage gap (will be cleared on next iteration)."`
- Reviewer-Instanz 2 (`"KlarheitStub"`): Severity Minor, Message `"Stub finding: simulated clarity nit (will be cleared on next iteration)."`

Beide Reviewer registrieren in der Builder-Konfiguration mit der `ParallelEvaluationStrategy`.

Severity-Werte: nutze die SDK-Enum-Werte. Falls das SDK `FindingSeverity` definiert, ist das die Quelle (nicht das Atelier-eigene `FindingSeverity` aus dem Domain-Layer — das ist für Persistierung in Schritt 4 da, wo wir das ggf. mappen).

#### `MarkdownFinalizer : IFinalizer<FinalizedDocument>`

Wraps den finalen Text-Stand aus dem Context in einen einfachen Record:

```csharp
public sealed record FinalizedDocument
{
    public required string Markdown { get; init; }
    public required int IterationCount { get; init; }
}
```

`FinalizedDocument` lebt in `src/Geef.Atelier.Core/Domain/` (Domain-Layer, weil der Vertrag nicht persistenz- oder LLM-spezifisch ist). Iterations-Anzahl wird aus dem Run-State gelesen.

### Pipeline-Builder-Konfiguration

In `src/Geef.Atelier.Infrastructure/Pipeline/` (nicht Web — die Composition-Wurzel ruft den Builder, aber der Builder selbst gehört in die Infrastruktur, weil er die Provider-Implementierungen kennt) eine Hilfs-Klasse:

```csharp
internal static class StubPipelineFactory
{
    public static GeefPipelineRunner<FinalizedDocument> Build(IServiceProvider services)
    {
        // Builder konfigurieren mit:
        // - BriefingGroundingStep
        // - StubExecutionStep
        // - StubReviewer × 2 (BriefingTreueStub, KlarheitStub)
        // - MarkdownFinalizer
        // - MaxIterationsPolicy(3) als Convergence-Policy
        // - ParallelEvaluationStrategy
        // - LoggingEventSink (vom SDK mitgeliefert) — schreibt Events in den Standard-ILogger
        // - Default-Middleware (Tracing, ExceptionHandling) wie vom SDK empfohlen
    }
}
```

Genauer Code-Stil orientiert sich am Geef-SDK-README und an den dortigen Builder-Beispielen.

DI-Registrierung: Wenn das SDK `AddGeefPipeline<TOutput>`-Extension-Method anbietet (laut Brainstorming-Notizen tut es das), nutze die. Damit wird die Factory-Klasse evtl. überflüssig und die Pipeline lebt direkt im DI-Container. **Architect entscheidet Pattern in Phase 1.4** — ob explizit Factory oder DI-Extension.

### Konsolen-Tests (in `tests/Geef.Atelier.Tests/`)

Zwei xUnit-Tests:

**Test 1: `StubPipelineRunsToConvergence`**
1. Den Runner über die Factory oder DI bauen (mit minimalem `IServiceProvider` ohne DB-Abhängigkeit).
2. Einen `RunContext` mit einem festen Briefing-String anlegen: `"Schreib mir einen Test-Text über Walking-Skeleton-Pattern."`
3. `runner.RunAsync(context, CancellationToken.None)` aufrufen.
4. Assertions:
   - Final-Output ist ein `FinalizedDocument`.
   - `FinalizedDocument.Markdown` enthält den Marker `"DRAFT v2"`.
   - `FinalizedDocument.IterationCount == 2`.
   - Keine Exception.

**Test 2: `StubPipelineEmitsExpectedEvents`**
Custom-EventSink (Test-Spy) registrieren, Pipeline laufen lassen, Sequenz prüfen:
- Genau **ein** `PipelineStarted` und ein `PipelineCompleted`.
- Genau **ein** `GroundingPhaseStarted` und ein `GroundingPhaseCompleted`.
- **Zwei** `ExecutionPhaseStarted` und zwei `ExecutionPhaseCompleted`.
- **Zwei** `EvaluationPhaseStarted` und zwei `EvaluationPhaseCompleted`.
- **Vier** Reviewer-Started/Completed-Events (zwei Reviewer × zwei Iterationen).
- Genau **ein** `FinalizePhaseStarted` und ein `FinalizePhaseCompleted`.
- Kein `PipelineFailed`.

Die exakten Event-Typ-Namen aus dem Geef-SDK lesen — nicht erfinden.

**Wichtig:** Diese Tests brauchen **kein Postgres** (keine Testcontainers). Reine in-memory-Logik. Das hält sie schnell.

### Logging

Microsoft.Extensions.Logging mit Default-Console-Provider in den Tests aktiv. Beim `dotnet test --logger "console;verbosity=detailed"` sieht man die Geef-Events live — gut zum Debuggen.

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` ohne Fehler oder Warnungen, die deinen Code betreffen (TreatWarningsAsErrors).
2. `dotnet test` läuft alle Tests grün — Schritt-1-Smoke-Tests **plus** die neuen Pipeline-Tests.
3. `StubPipelineRunsToConvergence` läuft in **genau 2 Iterationen** durch.
4. `StubPipelineEmitsExpectedEvents` zeigt, dass alle erwarteten Geef-Events emittiert werden.
5. Final-Output enthält den Marker `"DRAFT v2"`.
6. Bei `dotnet test --logger "console;verbosity=detailed"` sind die Geef-Events in chronologischer Reihenfolge im Output sichtbar.

`geef_architecture.md` muss existieren — das prüft Reviewer 4 jetzt automatisch (Workflow Hard Rule).

Diese Kriterien werden durch Reviewer 1 (Functional Correctness), Reviewer 3 (Test Execution) und Reviewer 4 (Architecture Compliance) im Detail geprüft.

## Was du in diesem Schritt NICHT tust

- **Keine echten LLM-Calls** — die Stubs produzieren deterministischen Output. Anthropic-/OpenAI-Clients kommen in Schritt 3.
- **Keine DB-Persistierung** — der `LoggingEventSink` reicht. Persistenter `PostgresEventSink` kommt in Schritt 4.
- **Kein BackgroundService** — Runner wird im Test direkt aufgerufen. Kommt in Schritt 5.
- **Keine UI-Anbindung** — Index.razor und SkeletonBanner.razor bleiben unverändert. UI kommt in Schritt 7.
- **Kein `IRunService`** — kommt in Schritt 6.
- **Kein MCP** — Schritt 9.

## Architect-Konsultation (Phase 1.4) — drei Schwerpunkte

Der Architect bekommt diese Schwerpunkte:

1. **Geef-SDK-Idiomatik:** Wie wird `GeefPipelineBuilder<T>` korrekt im DI-Container geladen? Existiert `AddGeefPipeline<TOutput>`? Wie sind Provider-Lifetimes (Singleton vs. Scoped vs. Transient)? Werden Provider pro Run instanziiert oder geteilt?
2. **Kontext-Schlüssel-Konvention:** Welche `ContextKey<T>`-Naming-Konvention verwenden wir? `geef:atelier:briefing`, `geef:atelier:grounded_brief` — oder gibt es im SDK schon eine Konvention, an die wir uns anlehnen sollten? Konstanten zentral oder pro Provider?
3. **Iteration-Awareness im Provider:** Wie liest ein `IExecutionStep` den aktuellen Iterations-Counter und die `PreviousFindings` korrekt aus dem Context oder den Method-Parametern? Im SDK lesen.

Der Architect-Output (`geef_architecture.md`) prüft Konsistenz mit `docs/02-architecture.md`. Bei Konflikt: das Repo-Doku (`02-architecture.md`) gewinnt — der Architect schlägt allenfalls Updates an `02-architecture.md` vor, ändert sie aber nicht selbst (das ist Brainstorming-Doku, kommt zurück in den Chat).

## Persistenter Abschlussbericht für den Brainstorming-Chat

Bericht nach `docs/reports/step-02-report.md`, gleicher Aufbau wie Schritt-1-Bericht:

1. **Was wurde umgesetzt** — Datei-für-Datei.
2. **Annahmen und Abweichungen** — Tabelle.
3. **Architect-Output-Zusammenfassung** — und welcher Invocation-Level (1–3, oder Atelier-Level-4) zum Einsatz kam, mit Fehlermeldung falls Fallback.
4. **Pre-Mortem & Devil's Advocate** — was haben die Pflicht-Advisors gefunden, was wurde adressiert.
5. **Reviewer-Iterationen** — Tabelle pro Iteration mit Findings pro Reviewer; Iteration-Advisor ab Iter. 2.
6. **Akzeptanzkriterien-Check** — Tabelle.
7. **Beobachtungen zum Geef SDK** — was ist gut, was hat überrascht, gab es API-Stolpersteine? Wertvoll für Schritt 3 und ggf. Feedback an den SDK-Maintainer.
8. **Empfehlungen für Schritt 3** — was ist beim Anthropic-Client-Schritt zu beachten? Welche Provider-Stellen sind so flexibel, dass der Übergang von Stub zu echt klappt? Welche werden komplizierter als gedacht?

## Konventionen

- Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- **Keine Geef-API erfinden** — nur Lesen + Verwenden.
- TreatWarningsAsErrors aus Schritt 1 respektieren.

Wenn du soweit bist: starte mit Phase 1.1 (Task Comprehension) gemäß Workflow.