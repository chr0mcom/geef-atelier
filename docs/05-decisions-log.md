# Decisions Log

*Letzte Aktualisierung: 10. Mai 2026*

Chronologisches Protokoll aller Entscheidungen aus dem Brainstorming. Format: Frage / Entscheidung / Begründung / ggf. Konsequenzen.

## 10. Mai 2026 — Erstes Brainstorming

### D-001: Erster Use-Case-Fokus

**Frage:** Mit welchem Use-Case fangen wir an?
**Optionen:** Juristischer Schriftsatz / Fachartikel / Generische Pipeline / Mehrere parallel
**Entscheidung:** Generische Pipeline ohne Domänen-Fokus.
**Begründung:** Verhindert, dass die Architektur einen Domänen-Bias einbacken bekommt. Spezialisierung (z.B. juristisch) kommt später als *Konfiguration* dazu — neue Reviewer-Profile, neue Crew-Templates — nicht als Code-Branch.
**Konsequenz:** Die Pipeline-Implementierung muss textsorten-agnostisch sein. Klassifikator denkt in Tags/Eigenschaften, nicht in fixen Kategorien.

### D-002: Mensch-im-Loop

**Frage:** Wieviel Mensch-Eingriff während eines laufenden Runs?
**Entscheidung:** Reiner Fire-and-Forget (Start → Ergebnis).
**Begründung:** Simpelste Variante; keine Pause-Mechanik, kein Resume aus User-Sicht, keine UI-Interaktion mitten im Run.
**Konsequenz:** Crash-Recovery bleibt eine System-Anforderung (nicht User-Feature). Abbruch-Button in der UI bleibt drin als einziger User-Eingriff.

### D-003: Frontend-Stack

**Entscheidung:** Blazor Server.
**Begründung:** Derselbe .NET-Stack wie Geef SDK, kein Kontextwechsel. SignalR ist eingebaut → Live-Status quasi gratis. Single-User → keine Skalierungs-Sorgen.

### D-004: Datenbank

**Entscheidung:** Postgres.
**Begründung:** Anwendung wird im Docker auf Server gehostet, dort ist Postgres bereits etabliert. Bonus: pgvector kann später für RAG genutzt werden.

### D-005: MCP-Schnittstelle

**Entscheidung:** Ja — als zweites Frontend neben der Web-UI.
**Konsequenz:** Application-Service-Layer (`IRunService`) wird zwingend. Eigenes Projekt `Geef.Atelier.Mcp`. Auth-Strategie zweispurig (Cookie/Bearer-Token). Bauplan wächst auf 10 Schritte.

### D-006: Projekt-Name

**Entscheidung:** Geef.Atelier.

### D-007: Bau-Konventionen (initial)

**Status:** Durch D-009 konkretisiert und durch `geef_workflow.md` formalisiert.

### D-008: Reihenfolge der Schritte

**Entscheidung:** Walking Skeleton zuerst.

### D-009: Verbindlicher Workflow für Claude Code

**Entscheidung:** Es gibt eine **kanonische Workflow-Datei `geef_workflow.md`** unter `/srv/docker/docs/geef-workflow.md` (projekt-agnostisch). Sie definiert vier Phasen, drei Rollen, fünf Reviewer (Functional, Code Quality via codex+gpt, Test Execution, Architecture Compliance, Live UI Sanity), Pflicht-Advisors (Pre-Mortem, Devil's Advocate, Iteration-Advisor, Pre-Deploy-Advisor), Hard Rules.
**Trennlinie:** Atelier-spezifisches kommt ausschließlich in Step-Prompts oder `docs/`.

### D-010: Schritt 1 abgeschlossen — Realitäts-Abgleich

**Datum:** 10. Mai 2026
**Bericht:** [docs/reports/step-01-report.md](reports/step-01-report.md)
**Reviewer-Iterationen:** 1; Findings: 1 CRITICAL + 4 MAJOR + 3 MINOR (alle behoben), 1 MAJOR nicht aktionierbar (.slnx), 5 nicht prüfbar (siehe D-011).

**Realfakten aus Schritt 1 (verbindlich für alle weiteren Schritte):**
- `Geef.Sdk 1.0.0-ci.1` (prerelease) via `Directory.Packages.props` + `nuget.config`.
- Solution-Format: `Geef.Atelier.slnx`.
- `Directory.Build.props` zentralisiert Build-Properties; `CS1591` global suppressed.
- Doku unter `docs/`, Berichte unter `docs/reports/`, Prompts unter `docs/prompts/`.
- `CLAUDE.md` im Root verweist auf Workflow + Doku-Hierarchie + übergeordnete `/srv/docker/docs/` und `/srv/CLAUDE.md`.
- UI-Component-Library: `src/Geef.Atelier.Web/Components/UI/` (erste Komponente: `SkeletonBanner.razor`). Direkte HTML-Elemente in Pages = CRITICAL.
- Migration-Strategie: Auto-on-Startup mit try-catch (Re-Eval in Schritt 10).
- Lokaler Server-Pfad: `/srv/docker/websites/geef_atelier`.

### D-011: Architect-Konsultation (Phase 1.4) — Workflow-Update + Atelier-Konvention

**Beobachtung Schritt 1:** Architect-Konsultation via `claude -p` scheiterte; R4 hatte 5 nicht-prüfbare Findings ohne Architect-File.

**(A) Generisches Workflow-Update am 10. Mai 2026:**
- Phase 1.4 mit Invocation-Fallback-Sequence (Levels 1–3) ergänzt.
- Hard Rules: `geef_architecture.md` MUSS vor Phase 2 existieren.
- Reviewer 4 prüft Existenz als ersten Punkt.

**(B) Atelier-spezifische Konvention** (in Step-Prompts):
- Atelier-Level-4-Fallback: Executor schreibt `geef_architecture.md` selbst, mit Pflicht-Header, Diff-Sektion gegen `docs/02-architecture.md`, Bericht-Doku der Fehlermeldungen.

### D-012: Schritt 2 abgeschlossen — SDK-Realfakten und Workflow-Bug

**Datum:** 10. Mai 2026
**Bericht:** [docs/reports/step-02-report.md](reports/step-02-report.md)
**Reviewer-Iterationen:** 1 (alle 5 Reviewer, 0 aktionierbare Findings)
**Tests:** 7/7 grün (5 aus Schritt 1 + 2 neue Pipeline-Tests)

**Wichtigster Punkt:** Pipeline-Skelett mit Stub-Providern läuft. Convergence in 2 Iterationen, 14 Event-Count-Assertions grün, In-Memory ohne LLM/DB/UI.

**Sechs Geef-SDK-Realfakt-Korrekturen** (verbindlich ab Schritt 3, ersetzen Annahmen aus früheren Step-Prompts):

1. **`FindingSeverity`-Enum:** SDK definiert `{ Info, Warning, Error, Critical }`. **NICHT** `Major/Minor`. Mapping aus Brainstorming: "Major" → `Error`, "Minor" → `Warning`. Code-Form: `Geef.Sdk.Results.FindingSeverity.Error/.Warning` voll-qualifiziert (sonst Konflikt mit `Geef.Atelier.Core.Domain.FindingSeverity`).

2. **Convergence-Policy:** `MaxIterationsPolicy(3)` aus dem Brainstorming existiert nicht. Korrekt:
   ```csharp
   new DefaultConvergencePolicy {
       MaxIterations       = 3,
       AbortOnCritical     = true,
       DetectRegression    = true,
       StagnationThreshold = 3
   }
   ```

3. **Middleware:** `UseMiddleware()` ist generisch (`UseMiddleware<TMiddleware>()` oder `UseMiddleware(IGeefMiddleware)`), keine "alle Defaults laden"-Methode. Mittlewares müssen einzeln explizit registriert werden.

4. **Evaluation-Events:** `EvaluationPhaseStarted/Completed` existieren nicht. SDK kennt nur `EvaluationApprovedEvent` (Iter ohne Blocker-Findings) und `EvaluationRejectedEvent` (Iter mit Findings).

5. **`PreviousFindings`-Access:** `GeefKeys.PreviousFindings` ist ohne Source/Symbols nicht eindeutig typisierbar. Workaround: `GeefKeys.IterationHistory` mit `history.Records[^1].EvaluationResult.AllFindings` — funktional gleichwertig.

6. **Namespace-Konflikt:** `using SdkGeef = Geef.Sdk.Geef;` notwendig, da `Geef`-Namespace die `Geef.Sdk.Geef`-Klasse überdeckt. Aufruf: `SdkGeef.CreatePipeline<FinalizedDocument>()`.

**Weitere kleinere Fixes:**
- `CreateLogger<TStaticClass>()` schlägt fehl (statische Klassen nicht als Generic-Argument). String-Overload nutzen: `CreateLogger("Geef.Atelier.Pipeline")`.
- `Finding.Metadata` ist `IReadOnlyDictionary<string, object>`, nicht `Dictionary<string, string>`.
- `IFinalizeResult<T>` Konstruktor benötigt expliziten `FinalContext`.

**Atelier-Konventionen aus Schritt 2 (Architect-Level-4-Output):**
- **Pipeline-Konstruktion:** `StubPipelineFactory.Build()` Pattern; ab DI-Container (Schritt 6) durch `IServiceCollection.AddGeefPipeline<T>()` ersetzen.
- **Context-Keys:** `internal static class AtelierContextKeys` in `src/Geef.Atelier.Infrastructure/Pipeline/` mit `geef:atelier:`-Präfix.
- **Provider-Sichtbarkeit:** `internal sealed` + `<InternalsVisibleTo Include="Geef.Atelier.Tests" />` in `Geef.Atelier.Infrastructure.csproj`.

**Workflow-Bug entdeckt:**
Der in D-011(A) beschlossene Workflow-Patch enthielt einen Fehler: Level 2 referenziert `claude --input-file`, das CLI-Flag existiert nicht (*"error: unknown option '--input-file'"*). Der eigentliche Fehler in Level 1 war kein Stdin-Redirect, sondern eine **Permissions-Wartung** — `claude -p` hat interaktiv um Erlaubnis gebeten und mit Exit 0 + bedeutungsloser Zeile beendet.

Korrigierter Workflow-Patch (Empfehlung an Maintainer):
- **Level 1:** Standard `claude -p` mit Heredoc.
- **Level 2:** `claude -p --dangerously-skip-permissions` mit Heredoc — adressiert das tatsächliche Problem (Permissions-Wartung).
- **Level 3:** Interaktive Subsession (wie bisher).

In Schritt 2 wurde Atelier-Level-4-Fallback aktiviert: Executor hat `geef_architecture.md` selbst geschrieben mit Pflicht-Header, Diff gegen `docs/02-architecture.md`, dokumentierten Fehlermeldungen. R4 hat Existenz verifiziert — 0 CRITICAL.

**Beobachtung zur Reviewer-Effektivität:**
Null aktionierbare Findings in Iteration 1 ist **kein** Zeichen unzureichender Prüfung — der Bericht erwähnt explizit: *"Die meisten Findings wurden während der Execution-Phase (Compilation-Fehler-Fixierung) abgefangen, bevor die Reviewer liefen."* Phase 2 fängt das Naheliegende, Phase 3 prüft das Subtile. Das System funktioniert wie geplant.

**Pre-Mortem-Risiko für Schritt 3:**
*"`AbortOnCritical = true` wird die Pipeline hart stoppen, sobald ein echter LLM-Reviewer Critical-Findings produziert."* Dieses Verhalten muss in Schritt 3 explizit getestet werden — nicht erst entdeckt werden, wenn die Pipeline in Production stoppt.