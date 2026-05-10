# Decisions Log

*Letzte Aktualisierung: 10. Mai 2026 (Schritt 4 abgeschlossen — EventSink + Postgres-Persistierung)*

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
Der in D-011(A) beschlossene Workflow-Patch enthielt einen Fehler: Level 2 referenziert `claude --input-file`, das CLI-Flag existiert nicht. **Korrekt funktionierende Form aus Schritt 3:** `cat /tmp/prompt.txt | claude -p` (Pipe-Redirect, nicht Flag). Workflow-Patch sollte entsprechend korrigiert werden.

**Beobachtung zur Reviewer-Effektivität:**
Null aktionierbare Findings in Iteration 1 ist **kein** Zeichen unzureichender Prüfung — der Bericht erwähnt explizit: *"Die meisten Findings wurden während der Execution-Phase (Compilation-Fehler-Fixierung) abgefangen, bevor die Reviewer liefen."* Phase 2 fängt das Naheliegende, Phase 3 prüft das Subtile. Das System funktioniert wie geplant.

**Pre-Mortem-Risiko für Schritt 3:**
*"`AbortOnCritical = true` wird die Pipeline hart stoppen, sobald ein echter LLM-Reviewer Critical-Findings produziert."* Dieses Verhalten muss in Schritt 3 explizit getestet werden — nicht erst entdeckt werden, wenn die Pipeline in Production stoppt.

### D-013: Schritt 3 abgeschlossen — Anthropic-Client und echte Provider

**Datum:** 10. Mai 2026
**Bericht:** [docs/reports/step-03-report.md](reports/step-03-report.md)
**Reviewer-Iterationen:** 1; Findings: 2 MAJOR (beide behoben), Rest MINOR/INFO
**Tests:** 11/11 grün (4 neue Mock-Tests, 7 Regression aus Schritt 1+2)

**Fixierte Realfakten aus Schritt 3 (verbindlich ab Schritt 4):**

**(a) `IAnthropicClient`-Vertrag:**
- Public interface in `Geef.Atelier.Infrastructure.Llm`; `CompleteAsync(AnthropicRequest, CancellationToken) → AnthropicResponse`
- `AnthropicResponse.ToolInputJson` ist `string?` (raw JSON), nicht `IReadOnlyDictionary<string,object>` — vermeidet `JsonElement`-Coupling im Interface
- `AnthropicTool.InputSchema` ist `JsonElement` — ermöglicht beliebige JSON-Schema-Strukturen

**(b) HTTP-Implementierung (`HttpAnthropicClient`):**
- Typed Client via `AddHttpClient<IAnthropicClient, HttpAnthropicClient>()` (nicht Named Client)
- API-Key per Request gesetzt (`httpRequest.Headers.Add("x-api-key", apiKey)`), **nicht** in `DefaultRequestHeaders` (verhindert Key-Leak in Singleton)
- `DefaultRequestHeaders.Add("anthropic-version", "2023-06-01")` — statisch, kein Pro-Request-Overhead
- `client.Timeout = TimeSpan.FromSeconds(120)` — schützt vor hängenden LLM-Calls
- Lazy-Validation beim ersten Call: `if (string.IsNullOrWhiteSpace(apiKey)) throw InvalidOperationException`

**(c) `Microsoft.Extensions.Http.Resilience` als Resilience-Default:**
- `AddStandardResilienceHandler()` wird in `Program.cs` an den `IHttpClientBuilder` gekettet (nicht in Infrastructure selbst)
- Infrastructure gibt `IHttpClientBuilder` aus `AddAnthropicClient()` zurück — Web entscheidet über Resilience-Konfiguration

**(d) `IOptions<AnthropicOptions>` für Konfig-Pattern:**
- `AnthropicOptions { ApiKey, ExecutorModel = "claude-opus-4-7", ReviewerModel = "claude-opus-4-7" }`
- Sektion `"Anthropic"` in `appsettings.json`
- Environment-Variable-Override: `Anthropic__ApiKey` (Doppelunterstrich = Sections-Separator)

**(e) Tool-Use-Pattern für Reviewer:**
- `tool_choice: "tool:submit_review"` zwingt Anthropic zur Tool-Benutzung
- Fallback bei fehlendem Tool-Call: `ReviewDecision.Failed` mit `SuggestedRetryHint`
- `LlmReviewer.ParseToolInput` verwendet `TryGetProperty` (defensiv, nicht `GetProperty`)
- Fingerprint-Strategie: SHA-256 der Finding-Message → Base64 → 12 Zeichen → `{name}:{hash}`

**(f) `AtelierPipelineFactory.BuildWithProviders` als Test-Hook:**
- Ersetzt `StubPipelineFactory`; `Build(IAnthropicClient, ...)` für Production, `BuildWithProviders(...)` für Tests mit beliebigen Provider-Kombinationen
- `StubExecutionStep` und `StubReviewer` bleiben im Repo als Regression-Test-Artefakte

**(g) `FakeAnthropicClient` Erkennungslogik:**
- Unterscheidet Executor vs. Reviewer über `request.Tools == null` (Executor sendet keine Tools)
- Zählt Executor-Calls intern: 1. Call = Iteration 1 (reject), 2+ = Iteration 2+ (approve)

**(h) `ConvergenceFailedException` bei Critical-Abort:**
- `AbortOnCritical = true` → SDK wirft `ConvergenceFailedException` (nicht `result.Success = false`)
- Exception-Message enthält "AbortCriticalBlocker" — für Assertions nutzbar
- `PipelineFailedEvent` feuert, `PipelineCompletedEvent` und `FinalizeStartedEvent` nicht

**(i) Modell-Pluralismus postponed:**
- Beide Provider (`LlmExecutionStep` + `LlmReviewer`) nutzen dasselbe `claude-opus-4-7`
- Multi-Provider-Adapter (OpenAI/OpenRouter als Reviewer) kommt nach Skeleton-Abschluss

**(j) PreviousFindings-Workaround bleibt:**
- `GeefKeys.IterationHistory.Records[^1].EvaluationResult.AllFindings` — funktional korrekt
- SDK-Bump auf `1.0.0` stable postponed bis nach Skeleton-Abschluss

**Architect-Konsultation Schritt 3:**
- Level 1 (`claude -p --dangerously-skip-permissions`) nicht versucht (aufgrund vorangegangenem Level-2-Muster aus D-012)
- Level 2 (`cat file | claude -p`) für Architect-Konsultation verwendet: erfolgreich
- `geef_architecture.md` durch Executor erstellt (Atelier-Level-4-Fallback nicht benötigt, da Level 2 erfolgreich)

**R2 MAJOR-Findings (behoben vor Phase 4):**
1. `AnthropicMessageFormat.DeserializeResponse`: `?? throw new JsonException(...)` statt `!`-Operator
2. `LlmReviewer.ParseToolInput`: `TryGetProperty` statt `GetProperty` — gibt `ReviewDecision.Failed` bei malformiertem Tool-Input zurück

**Offener Verifikationspunkt (für Schritt 5):**
Der Integration-Test `AtelierPipelineRealAnthropicTests` wurde **nicht** mit echtem API-Key ausgeführt. Die echten Anthropic-API-Aufrufe sind damit aktuell ungetestet. Vor Schritt 5 (BackgroundService) sollte dieser Test mit echtem Key laufen — sonst baut Schritt 5 auf einer ungetesteten Annahme auf.

### D-014: Production-Domain und Traefik-Routing für Schritt 10

**Datum:** 10. Mai 2026
**Status:** Vorbereitung für Schritt 10 (Production-Deploy).

**Entscheidung:**
- **Production-Domain:** `geef.stefan-bechtel.de`
- **Server-IP:** `95.216.100.213`
- **DNS:** A-Record bereits gesetzt, zeigt auf die IP.
- **Reverse-Proxy:** Traefik, bereits auf dem Zielserver aktiv.
- **TLS-Termination:** durch Traefik (Let's Encrypt o.ä. — bestehende Server-Konfiguration nutzen, nicht selbst verwalten).

**Konsequenz:**
- Der bestehende `docker-compose.yml` (Production-Skelett aus Schritt 1) enthält Placeholder `atelier.example.com` — wird in Schritt 10 durch `geef.stefan-bechtel.de` ersetzt.
- Traefik-Labels im `docker-compose.yml` müssen mit der existierenden Server-Konvention konsistent sein (Network, EntryPoints, Cert-Resolver — vermutlich aus `/srv/CLAUDE.md` oder `/srv/docker/docs/docker-deployment.md` ableitbar).
- Die App selbst lauscht intern unverändert auf Port 8080 — Traefik routet von 443 → interner Port.
- Health-Check `/health` aus Schritt 1 wird von Traefik als Healthcheck-Endpoint nutzbar.

**Nicht-Konsequenz für Schritte 4–9:**
Diese Domain ist Schritt-10-Material. Schritte 4 (Persistierung), 5 (BackgroundService), 6 (IRunService), 7 (UI), 8 (Auth), 9 (MCP) sind davon unbetroffen — sie laufen lokal über `docker-compose.dev.yml`. Erst Schritt 10 koppelt Production-Compose und Domain.

**Offener Punkt:** Wenn Auth in Schritt 8 Cookie-basiert ist und das Cookie auf `geef.stefan-bechtel.de` gesetzt wird, muss die Cookie-Konfiguration die Domain wissen. Aktuell kein Problem — kommt mit Schritt 8.

---

### D-015: Schritt 4 abgeschlossen — EventSink und Postgres-Persistierung

**Datum:** 10. Mai 2026
**Bericht:** [docs/reports/step-04-report.md](reports/step-04-report.md)
**Reviewer-Iterationen:** 1; Findings: 1 MAJOR (behoben), Rest MINOR/INFO
**Tests:** 15/15 grün (4 neue Persistence-Tests + 11 Regression aus Schritte 1–3)

**Fixierte Realfakten aus Schritt 4 (verbindlich ab Schritt 5):**

**(a) `IRunPersistenceService`-Vertrag:**
- Interface in `Geef.Atelier.Core.Persistence`: `CreateRunAsync(briefingText, configJson, ct) → Task<Guid>`
- Implementierung in `Geef.Atelier.Infrastructure.Persistence.RunPersistenceService`
- Legt `RunEntity` mit `Status=Pending`, `CreatedAt=UtcNow` an; gibt die neue `RunId` zurück

**(b) `PostgresEventSink`-Pattern und Verantwortungen:**
- `internal sealed class PostgresEventSink(Guid atelierRunId, IServiceScopeFactory, ILogger)`
- Pro `HandleEventAsync`-Aufruf: ein `CreateAsyncScope()` → ein frischer `AtelierDbContext`
- Verarbeitete Events: `PipelineStartedEvent`, `ExecutionCompletedEvent`, `EvaluationApprovedEvent`, `EvaluationRejectedEvent`, `PipelineCompletedEvent`, `PipelineFailedEvent`
- Alle Events → Raw-Log in `Events`-Tabelle (defensiv: Try-Catch um Serialisierung)
- `PublishAsync` wrapped `HandleEventAsync` in Try-Catch — Sink killt niemals die Pipeline

**(c) RunId-Propagation — Variante A (injizierte RunId):**
- `PostgresEventSink` bekommt `RunId: Guid` direkt im Konstruktor
- Tests und später `RunOrchestratorService` konstruieren Sink nach `CreateRunAsync`
- `IGeefEvent.RunId` (als `string`) wird nicht für Routing verwendet

**(d) Token-Tracking via typisiertem ContextKey:**
- `AtelierContextKeys.TokenUsage = new ContextKey<AnthropicTokenUsage>("geef:atelier:token-usage")`
- `LlmExecutionStep` schreibt nach jedem LLM-Call `AnthropicTokenUsage` in den Context
- `PostgresEventSink` liest bei `ExecutionCompletedEvent` via `TryGet` und akkumuliert via `ExecuteUpdateAsync` (atomar, kein Read-Modify-Write-Race)

**(e) Severity-Mapping-Tabelle:**
- SDK `Critical` → Atelier `Critical`
- SDK `Error` → Atelier `Major`
- SDK `Warning` → Atelier `Minor`
- SDK `Info` → Atelier `Info`
- Implementiert als Extension-Method `ToAtelierSeverity()` in `FindingSeverityExtensions.cs` (Infrastructure, nicht Core)

**(f) DbContext-per-Event-Pattern via `IServiceScopeFactory`:**
- `await using var scope = scopeFactory.CreateAsyncScope()` pro Event-Verarbeitung
- Token-Akkumulation via `ExecuteUpdateAsync(s => s.SetProperty(r => r.TokensTotal, r => r.TokensTotal + delta))` — DB-seitig atomar
- Keine Connection-Pool-Probleme bei 15/15 Tests mit Testcontainers PostgreSQL 16

**(g) `ConvergenceFailedException` → `Status=Aborted` (nicht `Failed`):**
- Erkennung: `failed.Reason == ConvergenceDecision.AbortCriticalBlocker`
- `Aborted`-Status, `ErrorMessage = "Aborted due to critical reviewer finding"`
- `Failed`-Status für alle anderen `PipelineFailedEvent`-Gründe

**(h) SDK-Realität: EvaluationRejectedEvent nicht für AbortCriticalBlocker:**
- SDK feuert `EvaluationRejectedEvent` **nur** für `ConvergenceDecision.Continue`
- Bei `AbortCriticalBlocker`: direkt `PipelineFailedEvent` ohne `EvaluationRejectedEvent`
- Findings bei Critical-Abort: `PipelineFailedEvent.History.Records.LastOrDefault().EvaluationResult.AllFindings`
- Verifiziert via SDK-Dekompilierung mit `ilspycmd`

**(i) Architect-Konsultation:**
- Level 2 (Pipe-basiert): erfolgreich, keine Eskalation nötig
- `geef_architecture.md` durch Executor erstellt (Atelier-Level-4-Fallback nicht benötigt)

**(j) AC7-Status (Real-Anthropic-Test):**
- Skip: Im Session-Kontext nur OAuth-Token verfügbar, kein API-Bearer-Key
- Skip-Pattern (`Skip.If`) verifiziert und korrekt implementiert
- Real-Lauf vor Schritt 5 nachholen (via `Anthropic__ApiKey` Umgebungsvariable)