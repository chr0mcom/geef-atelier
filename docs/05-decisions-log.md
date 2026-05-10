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
**Optionen:** Fire-and-Forget / Crew bestätigen dann F&F / Mehrere Eingriffspunkte / Maximale Kontrolle
**Entscheidung:** Reiner Fire-and-Forget (Start → Ergebnis).
**Begründung:** Simpelste Variante; keine Pause-Mechanik, kein Resume aus User-Sicht, keine UI-Interaktion mitten im Run.
**Konsequenz:** Crash-Recovery bleibt eine System-Anforderung (nicht User-Feature). Abbruch-Button in der UI bleibt drin als einziger User-Eingriff.

### D-003: Frontend-Stack

**Frage:** Welcher Frontend-Stack?
**Optionen:** Blazor Server (schnell) / React+API (flexibel) / Egal-mach-Vorschlag
**Entscheidung:** Blazor Server.
**Begründung:** Derselbe .NET-Stack wie Geef SDK, kein Kontextwechsel. SignalR ist eingebaut → Live-Status quasi gratis. Single-User → keine Skalierungs-Sorgen. Server-Hosting hinter Reverse-Proxy → typischer Blazor-Server-Nachteil (Roundtrip-Latenz) irrelevant. Falls später Wechsel zu Blazor WebAssembly oder React+API nötig: Backend-Logik bleibt unverändert.

### D-004: Datenbank

**Frage:** SQLite oder Postgres?
**Entscheidung:** Postgres.
**Begründung:** Anwendung wird im Docker auf Server gehostet, dort ist Postgres bereits etabliert. Kein zusätzlicher DB-Service nötig (außer für lokale Dev). Bonus: pgvector kann später für RAG genutzt werden, ohne separaten Vector-Store-Container.
**Konsequenz:** Solution nutzt Npgsql.EntityFrameworkCore.PostgreSQL. Connection-String aus Environment-Variable.

### D-005: MCP-Schnittstelle

**Frage:** Soll der Service auch per MCP ansprechbar sein?
**Entscheidung:** Ja — als zweites Frontend neben der Web-UI.
**Begründung:** Erlaubt KI-Agenten (Claude Desktop, Claude Code, Custom-Agents), Aufträge zu delegieren und Ergebnisse abzuholen. Nutzwert über reine UI hinaus.
**Konsequenz:**
- Application-Service-Layer (`IRunService`) wird zwingend, damit beide Frontends dieselbe Logik aufrufen.
- Eigenes Projekt `Geef.Atelier.Mcp` in der Solution.
- Auth-Strategie wird zweispurig: Cookie für UI, Bearer-Token für MCP.
- Bauplan wächst von 8 auf 10 Schritte.

### D-006: Projekt-Name

**Frage:** Wie heißt das Projekt?
**Entscheidung:** Geef.Atelier.
**Begründung:** "Atelier" passt thematisch (Werkstatt für hochwertige handwerkliche Arbeit), klingt nicht nach generischem SaaS, macht das Verhältnis zum SDK klar (das ist die Werkstatt, die das SDK nutzt).

### D-007: Bau-Konventionen (initial)

**Frage:** Welche Konventionen für Code und Kommunikation während der Umsetzung?
**Entscheidung (vorläufig):**
- Claude Code soll **selbst nach GEEF-Prinzip vorgehen**.
- Reviewer-Modell außerhalb der Anthropic-Familie (für echte Außenperspektive).
- Am Ende jedes Schritts: umfassender Abschlussbericht.
- Doku wird laufend in MD-Dateien im Projekt-Ordner gepflegt.
- Code/XML-Docs auf Englisch, Doku auf Deutsch.
**Status:** Durch D-009 konkretisiert und durch `geef_workflow.md` formalisiert.

### D-008: Reihenfolge der Schritte

**Frage:** Walking Skeleton oder erst tieferes Vorab-Brainstorming?
**Entscheidung:** Walking Skeleton zuerst.
**Begründung:** Schneller Realitätsabgleich zwischen Annahmen und Codebase. Themen wie Quellen-Handling, Multi-Provider-Adapter, Crew-Composition werden erst danach detailliert ausgearbeitet.

### D-009: Verbindlicher Workflow für Claude Code

**Frage:** Wie genau wird Claude Code arbeiten? Welche Reviewer, welche Advisor, welche Phasen?
**Entscheidung:** Es gibt eine **kanonische Workflow-Datei `geef_workflow.md`**, die Claude Code strikt befolgt. Sie lebt unter `/srv/docker/docs/geef-workflow.md` (Server-Infrastruktur-Ebene, **bewusst projekt-agnostisch** — gilt für alle Projekte, die diesem Workflow folgen).

Sie definiert:
- Vier Phasen (Grounding mit fünf Sub-Phasen, Execution mit On-Demand-Advisor, Evaluation mit Iteration-Advisor, Finalize mit Pre-Deploy-Advisor und Cleanup).
- Drei Rollen (Executor, Reviewer, Advisor) — nie verschmolzen.
- Fünf Reviewer:
  1. Functional Correctness (claude)
  2. Code Quality (codex mit gpt-5.4)
  3. Test Execution Verifier (claude)
  4. Architecture Compliance (claude)
  5. Live UI Verification via Playwright MCP
- Pflicht-Advisors: Pre-Mortem (strategic), Devil's Advocate (critical), Iteration-Advisor (ab Iter. 2), Pre-Deploy-Advisor (vor Production-Release).
- Hard Rules: Architektur ist binding, Tests sind Pflicht, eigene UI-Components zwingend, max. 15 Iterationen, Stagnation-Detection bei drei gleichen Findings hintereinander.

**Konsequenz für die Brainstorming-Doku:**
- Bau-Prompts werden drastisch kürzer — sie verweisen nur noch auf den Workflow und ergänzen den schritt-spezifischen Scope.
- Berichte für den Brainstorming-Chat werden in `docs/reports/` abgelegt — das überlebt das Phase-4.3-Cleanup, weil der Pfad nicht der `geef_*.md`-Naming-Konvention entspricht.
- D-007 wird durch diese Entscheidung formalisiert; das ursprüngliche "GPT 5.5 als Reviewer" ist durch den Workflow auf "gpt-5.4 in Reviewer 2" konkretisiert.
- **Trennlinie:** Atelier-spezifische Konventionen kommen ausschließlich in die Step-Prompts oder `docs/`. Der Workflow bleibt agnostisch.

### D-010: Schritt 1 abgeschlossen — Realitäts-Abgleich

**Datum Abschluss:** 10. Mai 2026
**Repository:** [chr0mcom/geef-atelier](https://github.com/chr0mcom/geef-atelier) (public)
**Bericht:** [docs/reports/step-01-report.md](reports/step-01-report.md)
**Reviewer-Iterationen:** 1 (alle fünf Reviewer in einer Runde)
**Findings:** 1 CRITICAL + 4 MAJOR + 3 MINOR (alle behoben), 1 MAJOR als nicht aktionierbar markiert (.slnx-Format), 5 nicht prüfbar (R4-Befunde ohne Architect-File, siehe D-011)

**Wichtige Realitäts-Bestätigungen — keine Änderung an Plan/Architektur/Vision nötig:**
- Solution-Struktur exakt wie in `02-architecture.md` vorgesehen: `src/Geef.Atelier.{Core,Infrastructure,Web,Mcp}` + `tests/Geef.Atelier.Tests`.
- DB-Schema (Runs, Iterations, Findings, Events) ist umgesetzt mit `MigrateAsync()` verifiziert.
- Health-Check `/health` reagiert mit `Healthy` gegen lokale und Container-Postgres, mit `Unhealthy` (503) bei DB-Ausfall.

**Technische Konkretisierungen, die jetzt fixiert sind (Geef-Atelier-spezifische Realfakten):**
- **Geef SDK ist als NuGet-Paket verfügbar:** `Geef.Sdk 1.0.0-ci.1` (prerelease, NuGet.org). Eingebunden über `Directory.Packages.props` (Central Package Management) und `nuget.config` (Prerelease-Feed). Update auf stable `1.0.0` sobald verfügbar.
- **Solution-Format:** `Geef.Atelier.slnx` (XML-Format) statt klassischer `.sln`. SDK-generiert.
- **Build-Properties:** `Directory.Build.props` zentralisiert TargetFramework, Nullable, TreatWarningsAsErrors etc. **Wichtig:** `CS1591` (Missing XML comment) ist global suppressed — Code-Doku via XML-Comments ist also nicht hart erzwungen.
- **Doku-Pfad:** `docs/` im Repo-Root.
- **Berichte-Pfad:** `docs/reports/`. Überlebt das Workflow-Phase-4.3-Cleanup.
- **Prompts-Pfad:** `docs/prompts/`.
- **CLAUDE.md** im Root verweist auf Workflow, Doku-Hierarchie, Stack, Konventionen, übergeordnete Server-Konventionen unter `/srv/docker/docs/` und `/srv/CLAUDE.md`.
- **UI-Component-Library etabliert:** `src/Geef.Atelier.Web/Components/UI/` ist die "project's own UI component package" gemäß Workflow-Hard-Rule. Erste Komponente: `SkeletonBanner.razor`. Künftige UI-Arbeit (ab Schritt 7) muss Komponenten dort ablegen und von dort konsumieren — direkte HTML-Elemente in Pages/Routes wären eine CRITICAL-Verletzung gemäß Workflow.
- **Migration-Strategie:** Auto-on-Startup mit try-catch (damit Health-Check Unhealthy melden kann). Re-Evaluation in Schritt 10 für Production-Hardening (Init-Container vs. Auto-Migration).
- **Migration-Identifier:** `20260510094017_InitialCreate`.
- **Lokaler Server-Pfad:** `/srv/docker/websites/geef_atelier`.

**Findings im Detail (alle aktionierbaren behoben):**
- **R1 (Functional Correctness)** — 2 MAJOR, 3 MINOR: Migration in try-catch (damit Health-Check Unhealthy melden kann); echte `CountAsync()`-Assertion im Smoke-Test statt vacuosem `Assert.NotNull`; weitere Verbesserungen.
- **R2 (Code Quality / codex+gpt-5.4)** — 2 MAJOR: Dev-Connection-String aus `appsettings.json` nach `appsettings.Development.json` verschoben; ASP.NET-Default-Templates `Counter.razor` und `Weather.razor` entfernt + NavMenu bereinigt.
- **R3 (Test Execution)** — 0 Findings, 5/5 Tests grün.
- **R4 (Architecture Compliance)** — 1 CRITICAL, 1 MAJOR (nicht aktionierbar), 5 nicht prüfbar: `Home.razor` → `Index.razor` (Konvention); `.slnx` vs `.sln` als nicht aktionierbar markiert (SDK-generiert); 5 weitere Punkte konnten ohne Architect-File nicht final bewertet werden — siehe D-011.
- **R5 (Live UI / Playwright MCP)** — 0 Findings; Screenshot in `~/playwright-output/geef-atelier-index.png`.

**Bedeutung für die Reviewer-Strategie:**
Eine Iteration für Solution-Setup ist die untere Grenze. Schritt 2 wird komplexer (Geef-SDK-Integration, Convergence-Loop) und braucht voraussichtlich 2–3 Iterationen. Der Cross-Provider-Reviewer-Effekt (R2 mit gpt-5.4 fängt Pattern, die Anthropic-Modelle übersehen würden) hat sich in Schritt 1 deutlich gezeigt — die Modell-Pluralismus-Entscheidung trägt.

### D-011: Architect-Konsultation (Phase 1.4) — Workflow-Update + Atelier-Konvention

**Beobachtung aus Schritt 1:** Die Architect-Konsultation via `claude -p` scheiterte am Stdin-Redirect-Konflikt. Der Blueprint wurde stattdessen direkt vom Executor erstellt. Folgen:
1. R4 (Architecture Compliance) hatte fünf Findings als "nicht prüfbar" markiert — ohne `geef_architecture.md` keine harte Compliance-Prüfung.
2. Das Risiko, das Phase 1.4 abdecken soll (LLMs sind schwach in Architektur, deshalb dedizierte Architekt-Pass), wurde umgangen.

**Sauber getrennt in zwei Ebenen:**

**(A) Workflow-Update am 10. Mai 2026** — projekt-agnostisch, gilt für alle Projekte, die `geef_workflow.md` folgen:
1. **Phase 1.4** ergänzt um eine dreistufige Invocation-Fallback-Sequence:
   - Level 1: Standard `claude -p`-Heredoc.
   - Level 2: File-based input via `claude --print --input-file` (umgeht Stdin-Konflikte).
   - Level 3: Interaktive Subsession in separater Claude-Code-Instanz.
   - Falls alle drei scheitern: halt-and-escalate. Ein optionaler projekt-spezifischer Level-4-Fallback kann im Step-Prompt definiert werden — der Workflow selbst schreibt das nicht vor, weil es davon abhängt, welche Architektur-Referenz-Doku das jeweilige Projekt führt.
2. **Hard Rules / Architecture & Components** ergänzt: `geef_architecture.md` MUSS vor Phase 2 existieren; bei vollständigem Versagen → halt und User informieren.
3. **Reviewer 4** ergänzt: prüft als ersten Punkt die Existenz von `geef_architecture.md`; bei Fehlen ist das ein CRITICAL-Finding.

**(B) Atelier-spezifische Konvention** (gehört in Step-Prompts, NICHT in den Workflow):
- **Atelier-Level-4-Fallback:** Falls die Workflow-Levels 1–3 alle scheitern, schreibt der Executor `geef_architecture.md` selbst — aber **nur** mit einem Pflicht-Header (`> ⚠️ Architect-Fallback: Levels 1–3 failed (see report). Executor-authored — verify against existing architecture docs.`), einer expliziten **Diff-Liste gegen `docs/02-architecture.md`** (was übernommen, was neu, was widersprochen) und einer Dokumentation im Bericht (Phase 4) inkl. der genauen Fehlermeldungen aus Levels 1–3. Ohne diese drei Bestandteile kein Proceed.
- Diese Konvention ist atelier-spezifisch, weil sie auf eine konkrete Repo-Datei (`docs/02-architecture.md`) verweist. Sie steht in den Step-Prompts.

**Status:** ✅ Workflow generisch aktualisiert; Atelier-Konvention in Step-Prompts verankert.

### D-012: Schritt 2 abgeschlossen — SDK-Realfakten fixiert

**Datum Abschluss:** 10. Mai 2026
**Repository:** [chr0mcom/geef-atelier](https://github.com/chr0mcom/geef-atelier) (public)
**Bericht:** [docs/reports/step-02-report.md](reports/step-02-report.md)
**Reviewer-Iterationen:** 1 (alle fünf Reviewer in einer Runde, 0 aktionierbare Findings)
**Tests:** 7/7 grün (5 Schritt-1-Tests + 2 neue Pipeline-Tests: `StubPipelineRunsToConvergence`, `StubPipelineEmitsExpectedEvents`)

**SDK-Realfakten (überschreiben Bau-Prompt-Annahmen, gelten ab jetzt als kanonisch):**

- **`DefaultConvergencePolicy` statt `MaxIterationsPolicy(3)`:** Die im Bau-Prompt genannte `MaxIterationsPolicy` existiert nicht. Korrekte API: `new DefaultConvergencePolicy { MaxIterations = 3, AbortOnCritical = true, DetectRegression = true, StagnationThreshold = 3 }`.

- **`FindingSeverity = { Info, Warning, Error, Critical }`:** Das SDK kennt kein `Major`/`Minor`. Mapping: Prompt-"Major" → `Error`, Prompt-"Minor" → `Warning`. Vollqualifizierter Zugriff empfohlen (`Geef.Sdk.Results.FindingSeverity.Error`), da `Geef.Atelier.Core.Domain` ggf. eigene Enum-Namen definiert.

- **`GeefKeys.CurrentIteration` + `GeefKeys.IterationHistory` als kanonischer Iterations-Mechanismus:** Provider lesen `CurrentIteration` (int) für iteration-aware Verhalten. Vorige Findings via `history.Records[^1].EvaluationResult.AllFindings` aus `IterationHistory`. Kein Provider hält eigenen Mutations-State.

- **`internal sealed` Provider + `InternalsVisibleTo` Geef.Atelier.Tests:** Provider-Klassen sind `internal sealed` (Kapselung). Tests im separaten Projekt haben Zugriff über `<InternalsVisibleTo Include="Geef.Atelier.Tests" />` in `Geef.Atelier.Infrastructure.csproj`.

- **Factory-Pattern statt DI-Extension:** `internal static StubPipelineFactory.Build()` statt `IServiceCollection.AddGeefPipeline<T>()`. Begründung: keine DI-Konsumenten in Schritt 2 (Tests rufen direkt). Re-Evaluation in Schritt 3 bei Einführung von `IHostedService`/BackgroundService (→ dann DI-Extension sinnvoll).

- **`using SdkGeef = Geef.Sdk.Geef;` Alias notwendig:** Der Namespace `Geef` (Projektreferenz) überdeckt die statische Factory-Klasse `Geef.Sdk.Geef`. Alias-Pattern ist kanonisch für alle weiteren Factory-Aufrufe.

- **`UseMiddleware(IGeefMiddleware)` — einzeln, nicht als Batch:** `UseMiddleware()` ist generisch (`UseMiddleware<TMiddleware>()`), kein "alle Defaults laden"-Muster. Explizit: `.UseMiddleware(new ExceptionHandlingMiddleware()).UseMiddleware(new TracingMiddleware())`.

- **`EvaluationApprovedEvent`/`EvaluationRejectedEvent` — keine Phase-Events:** `EvaluationPhaseStarted/Completed` existieren nicht. Test-Assertions prüfen direkt `EvaluationApprovedEvent` (1×) und `EvaluationRejectedEvent` (1×) für ein 2-Iterations-Run.

**Architect-Konsultation:** Level-4-Fallback (Executor-authored `geef_architecture.md`) — Levels 1–3 scheiterten (CLI-Inkompatibilität). Dokumentiert in [docs/reports/step-02-report.md §3](reports/step-02-report.md).

**Pipeline-Namespace:** `Geef.Atelier.Infrastructure.Pipeline` mit `AtelierContextKeys`, `BriefingGroundingStep`, `StubExecutionStep`, `StubReviewer`, `MarkdownFinalizer`, `StubPipelineFactory`. Context-Key-Präfix: `geef:atelier:`.

**Domain-Record:** `Geef.Atelier.Core.Domain.FinalizedDocument { required string Markdown; required int IterationCount; }`.

**Status:** ✅ Schritt 2 abgeschlossen. Schritt 3 (Anthropic-Client, echte Provider) kann starten.