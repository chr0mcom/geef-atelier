# Decisions Log

*Letzte Aktualisierung: 10. Mai 2026 (Provider-Strategie auf OpenAI-konform geändert; Migration M1 parallel zu Schritt 6)*

Chronologisches Protokoll aller Entscheidungen aus dem Brainstorming.

## 10. Mai 2026 — Erstes Brainstorming

### D-001: Erster Use-Case-Fokus
**Entscheidung:** Generische Pipeline ohne Domänen-Fokus.

### D-002: Mensch-im-Loop
**Entscheidung:** Reiner Fire-and-Forget (Start → Ergebnis).

### D-003: Frontend-Stack
**Entscheidung:** Blazor Server.

### D-004: Datenbank
**Entscheidung:** Postgres.

### D-005: MCP-Schnittstelle
**Entscheidung:** Ja — als zweites Frontend.

### D-006: Projekt-Name
**Entscheidung:** Geef.Atelier.

### D-007: Bau-Konventionen (initial)
**Status:** Durch D-009 konkretisiert.

### D-008: Reihenfolge der Schritte
**Entscheidung:** Walking Skeleton zuerst.

### D-009: Verbindlicher Workflow für Claude Code
**Entscheidung:** Kanonische Workflow-Datei `geef_workflow.md` unter `/srv/docker/docs/` (projekt-agnostisch). Atelier-Spezifisches kommt in Step-Prompts oder `docs/`.

### D-010: Schritt 1 abgeschlossen — Realitäts-Abgleich
**Bericht:** [reports/step-01-report.md](reports/step-01-report.md)
**Realfakten:** Geef.Sdk 1.0.0-ci.1, `.slnx`-Format, `Directory.Build.props` (`CS1591` suppressed), `docs/`-Struktur, `CLAUDE.md`, UI-Component-Library unter `src/Geef.Atelier.Web/Components/UI/`, Auto-Migration mit try-catch, Server-Pfad `/srv/docker/websites/geef_atelier`.

### D-011: Architect-Workflow-Update + Atelier-Fallback
**(A)** Phase 1.4 mit Fallback-Sequence (Levels 1–3); Hard Rule: `geef_architecture.md` MUSS existieren; R4 prüft Existenz.
**(B)** Atelier-Level-4: Executor schreibt selbst mit Pflicht-Header, Diff-Sektion, Bericht-Doku.

### D-012: Schritt 2 abgeschlossen — SDK-Realfakten
**Bericht:** [reports/step-02-report.md](reports/step-02-report.md)
**Sechs Korrekturen:** FindingSeverity `{Info, Warning, Error, Critical}`; `DefaultConvergencePolicy`; `UseMiddleware<T>()` generisch; nur `EvaluationApprovedEvent`/`RejectedEvent`; `PreviousFindings` via `IterationHistory`; `using SdkGeef = Geef.Sdk.Geef;`. Workflow-Bug: `--input-file` existiert nicht, korrekt ist `cat file | claude -p`.

### D-013: Schritt 3 abgeschlossen — Anthropic-Client (in M1 ersetzt)
**Bericht:** [reports/step-03-report.md](reports/step-03-report.md)
**Realfakten:** `IAnthropicClient` mit `ToolInputJson` als `string?`; Typed Client; API-Key per Request; `AddStandardResilienceHandler()`; `IOptions<AnthropicOptions>` mit `claude-opus-4-7`; Tool-Use mit `tool_choice: "tool:submit_review"`; `ConvergenceFailedException` mit `"AbortCriticalBlocker"`.

**Status durch M1 (D-017):** Diese Anthropic-spezifischen Strukturen werden durch provider-agnostische Pendants ersetzt (siehe D-017). Inhaltliche Realfakten (Tool-Use-Konzept, Convergence-Verhalten, Resilience) bleiben gültig — nur die Schicht-Adapter ändern sich.

### D-014: Production-Domain und Traefik-Routing für Schritt 10
**Domain:** `geef.stefan-bechtel.de`. **IP:** `95.216.100.213` (DNS gesetzt). **Reverse-Proxy:** Traefik (TLS dort).

### D-015: Schritt 4 abgeschlossen — EventSink und Persistierung
**Bericht:** [reports/step-04-report.md](reports/step-04-report.md)
**Realfakten:** `IRunPersistenceService` in `Core/Persistence/`. `PostgresEventSink` mit injizierter `Guid runId` (Variante A). Severity-Mapping via `ToAtelierSeverity()` Extension. Token-Tracking via typisierter `ContextKey<AnthropicTokenUsage>` (in M1 zu `ContextKey<LlmTokenUsage>` umbenannt — Inhalt identisch). Critical-Abort-Findings aus `PipelineFailedEvent.History.Records[^1].EvaluationResult.AllFindings` (SDK-Dekompilierung). `_lastExecutionContext` als `volatile`. `JsonSerializerOptions.ReferenceHandler = IgnoreCycles`. `IGeefEvent.RunId` ist `string`. `IServiceScopeFactory.CreateAsyncScope()` pro Event.

### D-016: Schritt 5 abgeschlossen — RunOrchestratorService
**Bericht:** [reports/step-05-report.md](reports/step-05-report.md)
**Realfakten:** Atomarer Pending→Running-Claim, `SemaphoreSlim` + `ConcurrentDictionary<Guid, Task>` + `WhenAll`-Drain, `OverrideToAbortedAsync` mit `CancellationToken.None`, `PipelineStartedEvent`-Handler nur idempotent `StartedAt`. Cancellation-Watcher-Vorbereitung via `_runCts`. `OrchestratorOptions` in `Core/Configuration/`. `GatedFakeAnthropicClient` (in M1 zu `GatedFakeLlmClient`). `OrchestratorTestHost`. Architect-Konsultation als Plan-Phase-Integration.

### D-017: Provider-Strategie-Wechsel auf OpenAI-konforme APIs (Migration M1)

**Datum:** 10. Mai 2026
**Status:** ✅ Abgeschlossen (Branch `feature/openai-compatible-providers` — nicht in main gemerged, wartet auf Maintainer-Entscheidung).

**Auslöser:**
- Der OAuth-Token (`sk-ant-oat01-*`), den Claude Code in seiner Session-Umgebung hat, wurde von Anthropic im Februar 2026 für die Messages-API deaktiviert (siehe Diskussion zwischen AC8/AC9-Skip in D-013, D-015, D-016).
- Ein API-Bearer-Key (`sk-ant-api03-*`) erfordert separaten Pay-as-you-go-Account auf der Anthropic-Console — vermeidbar.
- OpenAI-konforme APIs (insbesondere OpenRouter) bieten Anthropic Claude **plus** OpenAI GPT, Google Gemini, Meta Llama, Mistral, etc. über einen einzigen Bearer-Key.

**Entscheidung:**
Wechsel der LLM-Schicht von Anthropic-spezifisch auf **OpenAI-API-konform**. Default-Endpoint: **OpenRouter** (`https://openrouter.ai/api/v1`). Andere OpenAI-kompatible Endpoints (OpenAI direkt, lokales Ollama, Together AI, DeepInfra) sind durch denselben Adapter-Code ansprechbar.

**Vorgezogene Vision-Umsetzung:**
- "Modell-Pluralismus" aus `01-vision-and-scope.md` (Leitstern: *"Reviewer mit anderem Modell als Executor"*) wird ab M1 sofort verfügbar — nicht "nach Skeleton" wie ursprünglich in D-013(i) geplant.
- Pro Akteur (Executor, BriefingTreueReviewer, KlarheitReviewer) eigene Modell-Konfiguration. Beispiel: Executor `anthropic/claude-opus-4.7`, BriefingTreueReviewer `openai/gpt-5`, KlarheitReviewer `google/gemini-2.5-pro`.
- Cross-Provider-Reviewer-Effekt (R2 mit gpt-5.4 fängt Pattern, die Claude-Modelle übersehen) ist damit auch in der Atelier-Pipeline aktiv, nicht nur in Claude Codes eigenem Reviewer-Pass.

**Was sich ändert (LLM-Schicht):**
- `IAnthropicClient` → `ILlmClient`
- `AnthropicRequest`/`AnthropicResponse`/`AnthropicTool`/`AnthropicTokenUsage` → `LlmRequest`/`LlmResponse`/`LlmTool`/`LlmTokenUsage`
- `HttpAnthropicClient` → `OpenAiCompatibleClient`
- `AnthropicOptions` → `LlmOptions` mit Pro-Akteur-Modell-Mapping
- Tool-Use-Format wechselt von Anthropic-Schema auf OpenAI-`function`-Schema
- Token-Felder: `prompt_tokens`/`completion_tokens` (nur API-Wire-Format; `LlmTokenUsage` bekommt klare englische Properties)
- Endpoint-Pfad: `/v1/messages` → `/v1/chat/completions`
- `tool_choice`-Format: `"tool:submit_review"` → `{"type": "function", "function": {"name": "submit_review"}}`

**Was unverändert bleibt:**
- Pipeline-Struktur, alle Geef-SDK-Verträge, Convergence-Logik (D-012-Realfakten gelten weiter)
- `BriefingGroundingStep`, `MarkdownFinalizer`, `AtelierContextKeys` (außer Token-Key-Typ-Umbenennung)
- `PostgresEventSink`, `IRunPersistenceService`, `RunOrchestratorService` (alles aus D-015/D-016)
- Domain-Records (`RunEntity`, `IterationEntity`, `FindingEntity`, `EventEntity`, Atelier-`FindingSeverity`)
- DB-Schema, Migrations
- Tests oberhalb der LLM-Schicht (Persistence-Tests, Orchestrator-Tests bleiben grün; nur Fake-Client-Klasse wird umbenannt)
- Resilience-Strategie (`AddStandardResilienceHandler()`)
- Critical-Abort-Verhalten (`ConvergenceFailedException` mit `"AbortCriticalBlocker"`)

**Branch-Strategie:**
- Migration läuft auf eigenem Branch `feature/openai-compatible-providers`.
- Parallel zu numerischen Schritten (Schritt 6 läuft in `main` weiter, M1 in seinem Branch).
- M1-Branch wird **nicht** automatisch in main gemerged — nur gepusht. User entscheidet Merge-Zeitpunkt.
- Empfehlung: Merge vor Schritt 7 (UI), damit die UI direkt gegen die neuen Provider-Verträge gebaut wird.
- Konfliktbereiche bei Merge: `Program.cs` (M1 baut LLM-Layer um, Schritt 6 fügt `IRunService`-Registrierung hinzu) und `appsettings.json` (Sektions-Umbenennung). Beides handhabbar.

**OpenRouter-spezifische Aspekte:**
- Modell-Namen mit Provider-Präfix: `anthropic/claude-opus-4.7`, `openai/gpt-5`, `google/gemini-2.5-pro`.
- Optionale Header für Analytics: `HTTP-Referer: https://geef.stefan-bechtel.de`, `X-Title: Geef.Atelier`.
- Tool-Use unterstützt fast alle Modelle, Verhalten variiert minimal — Reviewer-Defensive-Parsing aus D-013(e) trägt diese Variabilität bereits.
- Pro-Modell-Pricing transparent auf openrouter.ai/models — Cost-Tracking (Schritt 10 oder später) wird damit einfacher als mit Anthropic-Console-Accounting.

**Konsequenzen für Step-Prompts ab Schritt 7:**
- `IAnthropicClient`-Referenzen werden zu `ILlmClient`.
- `AnthropicOptions`-Referenzen werden zu `LlmOptions`.
- AC9 (Real-Anthropic-Test) wird zu AC9 (Real-OpenRouter-Test) — und ist dann ohne weiteres Auth-Manöver erreichbar, weil OpenRouter-Bearer-Keys problemlos per `Llm__ApiKey`-Env-Var bereitgestellt werden können.

**Realfakten (M1-Abschluss):**
- `ILlmClient` mit `CompleteAsync(LlmRequest, CancellationToken)` ist das neue Provider-Interface.
- `OpenAiCompatibleClient` (internal sealed) implementiert `ILlmClient` via Typed HttpClient; baut Full-URL aus `LlmOptions.Endpoint`; API-Key per Request gesetzt (nicht in `DefaultRequestHeaders`).
- `LlmRequest.ToolChoice`: Atelier-Konvention `"function:submit_review"` → Client serialisiert zu `{"type":"function","function":{"name":"submit_review"}}`.
- `LlmResponse.ToolArgumentsJson`: Raw-JSON-String (analog zu `AnthropicResponse.ToolInputJson` aus D-013(a)) — kein `JsonElement`-Coupling im Interface.
- `LlmOptions.Actors`-Dictionary: Key = Akteur-Name (String, z. B. `"Executor"`), nicht `LlmActor`-Enum — simpler, erweiterbar ohne Enum-Änderung.
- `OpenAiMessageFormat` (internal static class neben `OpenAiCompatibleClient`) kapselt die komplette Serialisierung/Deserialisierung, analog zu `AnthropicMessageFormat`.
- `LlmServiceExtensions.AddLlmClient()` registriert Options + Typed HttpClient; setzt `HTTP-Referer` und `X-Title` in `DefaultRequestHeaders` (Analytics-Headers für OpenRouter).
- Keine `anthropic-version`-Header-Reste im neuen Client; Provider-agnostisch per Design.
- Bestehende 15 Tests (Schritt 5) + 3 neue Unit-Tests (`OpenAiCompatibleClientTests`) — 18 Tests ohne Docker; Integration-Test (`AtelierPipelineRunsAgainstOpenRouter`) skipped ohne `Llm__ApiKey`.
- `CountingEventSink.TotalEvents`-Property hinzugefügt (für Integration-Test-Assertion).