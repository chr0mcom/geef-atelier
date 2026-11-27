# Tool-System

***Deutsch** · [English](10-tool-system.md)*

*Letzte Aktualisierung: 2026-11-24 (D-067: Ist-Zustand nach Etappe 0 — agentischer Pfad nie verdrahtet und eingefroren, fünf System-Grounding-Profile wieder über den Provider-Typ-Weg, Umgebungsvariablen-Verweise nur über die Allowlist; zuvor 10. Juni 2026, D-060, Phasen A–C abgeschlossen)*

---

> **Ist-Zustand (D-067, 2026-11-24) — bitte zuerst lesen.** Große Teile dieses Dokuments beschreiben
> den Entwurf aus D-060, nicht das, was in der Produktion läuft:
>
> - **Der agentische Tool-Pfad (Pull) ist eingefroren, sein Rückbau ist vorgesehen** (D-068). Er war nie
>   mit der Pipeline verdrahtet: `AtelierPipelineFactory` baut `ProfileBasedExecutor`,
>   `ProfileBasedReviewer` und `ProfileBasedAdvisor` ohne `IToolUseRunner`. Akteure mit `ToolNames`-Bindings
>   laufen deshalb wie alle anderen als Einzelaufruf. Die Bindings werden gespeichert und geprüft, wirken
>   aber nicht.
> - **Nur ein Teil der Tool-Typen ist ausführbar.** `ToolExecutor` führt `static-context` und `web-search`
>   aus (bis Stufe 8c, D-089, auch `mcp-tool`); alle anderen Typen liefern einen Fehler „not yet wired“.
> - **Die fünf System-Grounding-Profile laufen wieder über den Provider-Typ-Weg.** `tavily-basic`,
>   `tavily-refined`, `run-attachments`, `knowledge-base-default` und `academic-default` (in Stufe 8c ausgemustert, D-089) haben keinen
>   `ToolName` mehr und laufen über ihren typspezifischen Grounding-Provider, auch wenn ein älterer
>   Snapshot noch einen `ToolName` trägt (siehe [A-T9](#a-t9--grounding-neuaufbau-auf-zentrale-tools)).
>   Nur eigene Profile mit `ToolName` nutzen noch den `ToolBackedGroundingProvider`.
> - **Verweise auf Umgebungsvariablen laufen über eine Allowlist** (`IEnvReferencePolicy`); siehe
>   [Secrets](#secrets-und-die-allowlist-für-umgebungsvariablen).
>
> Hintergrund: [Entscheidungslog D-067 und D-068](05-decisions-log_de.md).

## Überblick

Das Tool-System führt **agentic Tool-Use** für alle Atelier-Akteure (Executor, Reviewer, Advisor, Finalizer) ein. Anstatt ausschließlich aus dem Modellwissen heraus zu arbeiten, kann ein Akteur jetzt *während seines eigenen Turns Werkzeuge aufrufen* — eine Webquelle nachschlagen, die Wissensdatenbank befragen, eine URL laden oder einen MCP-Server aufrufen — und die Ergebnisse direkt in seine Ausgabe einfließen lassen.

Ein zentraler Eintrag, `ToolDefinition`, ist die einzige Quelle der Wahrheit für jede Fähigkeit. Dieselbe Definition treibt sowohl **Pull** (agentic: das LLM ruft das Tool auf, wenn es das für sinnvoll hält) als auch **Push** (Grounding: eager Kontexteinspeisung, bevor die Pipeline startet).

---

## Datenbankintegrität beim Upgrade

**Keine bestehenden Daten wurden gelöscht.** Die fünf neuen Migrationen (Step37–Step41) haben ausschließlich neue Tabellen und Spalten *hinzugefügt*. `DROP`- und `ALTER … DROP COLUMN`-Anweisungen stehen ausschließlich in den `Down()`-Methoden (Rollback-Pfad), die beim normalen Deploy *nicht* ausgeführt werden.

Alle vorhandenen Runs, Iterationen, Profile, Templates, Learnings und Snapshots sind vollständig erhalten.

---

## Kernkonzepte

### `ToolDefinition`

Datei: `Core/Domain/Tools/ToolDefinition.cs`

```csharp
public sealed record ToolDefinition(
    string Name,           // Kebab-case-Bezeichner, z. B. "web-search"
    string DisplayName,    // Anzeigename in der UI
    string Description,    // Wird dem LLM in der Tool-Liste übergeben
    string ToolType,       // Diskriminator — siehe ToolType-Konstanten
    IReadOnlyDictionary<string, string> Settings,
    string? SecretRef,     // Nur der Name einer ENV-Variablen — niemals der Wert
    JsonElement LlmSchema, // JSON Schema für das Eingabeobjekt des LLM
    ToolAccessClass AccessClass, // ReadOnly | Mutating
    bool IsSystem          // System-Tools sind schreibgeschützt
)
```

**Namensregeln:** Kleinbuchstaben-Kebab-Case, Anfang und Ende mit `[a-z0-9]`, Regex `^[a-z0-9]([a-z0-9\-]*[a-z0-9])?$`.

**Secrets:** `SecretRef` enthält ausschließlich den Namen einer Umgebungsvariablen (z. B. `"TAVILY_API_KEY"`). Der tatsächliche Wert wird niemals in der Datenbank, in Logs, Snapshots oder der Benutzeroberfläche gespeichert. Welche Namen erlaubt sind, steht im nächsten Abschnitt.

### Secrets und die Allowlist für Umgebungsvariablen

Seit D-067 läuft jeder Umgebungsvariablen-Name, den eine vom Nutzer bearbeitbare Konfiguration nennen kann, über `IEnvReferencePolicy` (`Application/Security/`). Das betrifft Tool-`SecretRef`, und Provider-`ApiKeyEnv`/`EndpointEnvOverride` (MCP-Server-`AuthHeaderEnv` und die Grounding-Einstellungen `authHeaderEnv`/`apiKeyEnv` sind mit dem MCP-Client und den Typen `rest-api`/`academic-search` in Stufe 8c entfallen, D-089).

- **Erlaubte Namen:** eine eingebaute Standardliste (`EnvReferenceOptions.DefaultAllowed`: die verwendeten Provider- und Tavily-Schlüsselnamen) plus das Präfix `ATELIER_SECRET_*` für neue Geheimnisse. Weitere Namen oder Präfixe (mit `*` am Ende) kommen aus dem Konfigurationsschlüssel `EnvReference:Allowed` (ein Array; es ergänzt die Standardliste und ersetzt sie nie). Infrastruktur-Geheimnisse (Datenbank, MCP-Token, cli-proxy-Schlüssel, Admin-Passwort) stehen bewusst nicht auf der Liste.
- **Beim Speichern:** Ein nicht erlaubter Name wird mit Meldung abgelehnt (Provider- und Tool-Editor).
- **Zur Laufzeit:** Ein nicht erlaubter Name wird nie gelesen; der Aufruf läuft ohne den Wert weiter, und im Log steht eine Warnung ohne Wert. Das schützt auch Altzeilen aus der Zeit vor der Allowlist.
- **Editoren:** Provider-, Tool- und Grounding-Editor sind nur für Admins.

Für ein neues Geheimnis entweder einen Namen mit dem Präfix `ATELIER_SECRET_` wählen oder den Namen in `EnvReference:Allowed` eintragen.

### Tool-Typen (`ToolType`-Konstanten)

| Konstante | Wert | Beschreibung |
|---|---|---|
| `WebSearch` | `web-search` | Tavily-Websuche |
| `KnowledgeBase` | `knowledge-base` | pgvector semantische Suche |
| `UrlFetch` | `url-fetch` | HTTP-GET mit SSRF-Schutz |
| `NewsSearch` | `news-search` | Nachrichtensuche |
| `AcademicSearch` | `academic-search` | arXiv / SemanticScholar / OpenAlex |
| `RestApi` | `rest-api` | Generischer HTTP-Aufruf mit JSONPath-Extraktion |
| `StaticContext` | `static-context` | Gibt konfigurierten Festtext zurück |
| `LearningRetrieval` | `learning-retrieval` | Atelier-Learning-Loop-Abruf |
| `McpTool` | `mcp-tool` | Mit dem MCP-Client in Stufe 8c ausgemustert (D-089); die Konstante bleibt `[Obsolete]`, kein Werkzeug dieses Typs läuft |

### Zugriffsklasse (`ToolAccessClass`)

- **`ReadOnly = 0`** — Tool liest nur Daten; Standard für alle Tools einschließlich MCP-entdeckter Tools.
- **`Mutating = 1`** — Tool schreibt, verändert oder löscht Daten. In Crew-Specs blockiert, solange der Betreiber nicht explizit `allow_mutating_tools: true` setzt.

### Settings-Schlüssel (`ToolDefinitionSettingsKeys`)

| Schlüssel | Verwendung |
|---|---|
| `apiKey` | Legacy; `SecretRef` bevorzugen |
| `baseUrl` | Websuche, REST-API |
| `maxResults` | Websuche, Nachrichten, Akademisch |
| `endpoint` | REST-API, MCP |
| `collectionName` | Wissensdatenbank (pgvector) |
| `includeDomains` / `excludeDomains` | Domain-Filter für Websuche |
| `newsLanguage` | Nachrichtensuche (BCP-47, z. B. `de`) |
| `academicSource` | `arXiv`, `SemanticScholar`, `OpenAlex` |
| `jsonPathExpression` | REST-API-Antwort-Extraktion |
| `httpMethod` | REST-API (`GET`, `POST`, …) |
| `staticContent` | Festtext für `static-context`-Tools |
| `refinementBinding` / `refinementMode` / `refinementInstructions` | Grounding-Verfeinerungs-Pass |
| `domainBoost` | Domänen-Priorisierung beim Learning-Retrieval |
| `mcpOriginalName` | MCP-Tool — Originalname des Tools auf dem Server |

---

## Architektur

```
                  ToolDefinition (einzige Quelle der Wahrheit)
                         │
          ┌──────────────┴───────────────────────────────┐
          ▼ PULL (agentic, während des Akteur-Turns)       ▼ PUSH (eager, vor der Pipeline)
     Executor / Reviewer / Advisor                  Grounding-Provider
     → IToolUseRunner (Mehrfach-Turn-Loop)          → MultiProviderGroundingStep
          └──────────────┬────────────────────────────────┘
                         ▼  eine Ausführungsschicht, zwei Konsumenten
                 IToolExecutor.ExecuteAsync(tool, inputJson, ctx, ct)
                    ├── web-search     → Tavily HTTP
                    ├── knowledge-base → pgvector
                    ├── url-fetch      → HttpClient + SSRF
                    ├── news-search    → News-API
                    ├── academic-search→ arXiv/SS/OAlex
                    ├── rest-api       → HttpClient + JSONPath
                    ├── static-context → Festtext
                    ├── learning-retrieval → LearningRepository
```

Entwurf nach D-060. Ist-Zustand (D-067): Der PULL-Zweig ist nicht mit der Pipeline verdrahtet, den PUSH-Zweig nutzen nur eigene Grounding-Profile mit `ToolName`, und `ToolExecutor` führt nur `static-context` und `web-search` aus (`mcp-tool` bis Stufe 8c, D-089).

---

## Phase A — Fundament + Agentic Tools + Grounding-Neuaufbau

### A-T1 — `ToolDefinition`-Domäne

`Core/Domain/Tools/`:
- `ToolDefinition` — Primary-Constructor-Record (siehe oben)
- `ToolType` — 9 Typ-Konstanten
- `ToolAccessClass` — `ReadOnly = 0`, `Mutating = 1`
- `ToolInvocation` — Audit-Record pro Tool-Aufruf

`ToolInvocation`-Felder:
```
Id, RunId, IterationNumber, ActorType, ActorName,
ToolName, ToolType, InputJson (Roh), OutputExcerpt (max. 500 Zeichen),
CostEur?, DurationMs, Sequence, Outcome (Success/Failed/CapReached/Blocked),
CreatedAt
```
Secrets werden niemals in `InputJson` oder `OutputExcerpt` geschrieben.

### A-T2 — Persistenz

- `IToolDefinitionRepository` — CRUD: `GetAllAsync`, `GetByNameAsync`, `GetSystemAsync`, `GetCustomAsync`, `SaveAsync`, `DeleteAsync`
- `IToolInvocationRepository` — append-only: `AddAsync`, `GetByRunAsync`
- EF-Konfigurationen: `ToolDefinitionConfiguration` (JSONB für Settings und LlmSchema), `ToolInvocationConfiguration`
- DB-Tabellen: `tool_definitions`, `tool_invocations` (Migration Step37)

### A-T3 — `IToolExecutor` + `IToolSchemaProvider`

**`IToolExecutor`** (`Application/Tools/`):

```csharp
Task<ToolExecutionResult> ExecuteAsync(
    ToolDefinition tool,
    string inputJson,
    ToolInvocationContext ctx,
    CancellationToken ct = default);
```

`ToolInvocationContext` enthält `RunId`, `IterationNumber`, `ActorType`, `ActorName`, `Sequence`. Jeder Aufruf schreibt automatisch einen `ToolInvocation`-Audit-Record.

**`IToolSchemaProvider`** erzeugt das `LlmTool`-Objekt (Name + Beschreibung + Eingabe-Schema) für die LLM-Tool-Registrierung. Das Schema stammt aus `ToolDefinition.LlmSchema` (oder wird typ-spezifisch auto-generiert, wenn das gespeicherte Schema leer ist).

### A-T4 — Mehrfach-Turn-LLM-Client

`LlmRequest` wurde um eine optionale `Messages`-Liste (`IReadOnlyList<LlmMessage>`) erweitert, die das System+User-Einzelprompt ersetzt. `LlmResponse` enthält jetzt alle `ToolCalls` (nicht nur den ersten). `OpenAiMessageFormat` serialisiert die vollständige Message-History inklusive `assistant tool_calls` und `tool`-Ergebnismeldungen.

### A-T5 — `IToolUseRunner` — der agentic Loop

`Infrastructure/Pipeline/ToolUseRunner.cs`:

```
1. History aufbauen (System-Prompt + User-Prompt)
2. LLM mit History + gebundenen LlmTools aufrufen
3. Antwort enthält tool_calls?
   → Pro tool_call:
       - Ausführen via IToolExecutor (mit Per-Tool-Timeout, Standard: 30 s)
       - Tool-Ergebnis als Message in History anhängen
   → Call-Zähler inkrementieren; wenn ≥ maxToolCalls → abbrechen (CapReached)
   → Weiter mit Schritt 2
4. Antwort ist reiner Text (keine tool_calls) → Loop endet, Text zurückgeben
5. Pflicht-End-Tool empfangen (z. B. submit_review) → Loop endet
```

Der Loop läuft vollständig provider-agnostisch. HTTP-Provider und CLI-Provider werden identisch angesteuert. Per-Turn-Cap (Standard): **5 Tool-Aufrufe**. Per-Tool-Timeout (Standard): **30 s**.

Jeder Tool-Aufruf wird als `ToolInvocation` protokolliert. LLM-Round-Trip-Kosten laufen über `ICostAccumulator`; Tool-eigene Kosten (z. B. Tavily-Credits) werden in `ToolExecutionResult.CostEur` erfasst.

### A-T6 — CLI-Proxy: Agentic Loop-Teilnahme

`cli-proxy/src/tool_use_parser.py` wurde um `build_agentic_tool_prompt()` ergänzt. Wenn der .NET-Loop einen Aufruf mit Tools, aber ohne erzwungenes `tool_choice` schickt, injiziert der Proxy einen Protokoll-Addendum: das Modell soll exakt ein JSON `{"tool_call": {"name": …, "arguments": {…}}}` oder reinen Abschlusstext antworten. Der Parser entscheidet pro Turn: wird ein `tool_call`-JSON erkannt, wird eine `tool_calls`-Antwort gebaut; andernfalls wird der Text als `stop` zurückgegeben.

Der erzwungene Einzeltool-Pfad (`submit_review` via `tool_choice`) bleibt unverändert.

### A-T7 — Provider-Capability-Detection

`ILlmClientResolver.SupportsAgenticTools(providerName)` gibt `true` für HTTP-Provider (OpenAI-kompatibel, OpenRouter, Anthropic, Custom) und `false` für `generic` oder explizit deaktivierte Provider zurück. Verwendung in:

- `ProfileBasedReviewer` / `ProfileBasedExecutor` / `ProfileBasedAdvisor` — entscheidet, ob der Tool-Loop oder Single-Shot verwendet wird
- `CrewSpecValidator` Step 8a — lehnt Specs ab, die Tools an nicht-fähige Provider binden
- `/tools`-Editor — zeigt eine Capability-Warnung

Degradation ist **sichtbar**, nie still: Run-Log und UI-Badge zeigen „Provider unterstützt kein agentic Tool-Use".

### A-T8 — Tool-Binding in alle Akteur-Profile

`ExecutorProfile`, `ReviewerProfile`, `AdvisorProfile` und `FinalizerProfile` (nur Typ Transform) haben jeweils:
```csharp
IReadOnlyList<string> ToolNames { get; init; } // Namen aus dem ToolDefinition-Katalog
```

Entwurf: Wenn `ToolNames` nicht leer ist und der Provider agentic Tools unterstützt, läuft der Akteur über `IToolUseRunner` statt Single-Shot. Bei Reviewern bleibt `submit_review` der obligatorische Abschluss-Tool-Call.

**Ist-Zustand (D-067):** Diese Weiche greift in keinem Lauf. Die `ProfileBased*`-Akteure nehmen einen optionalen `IToolUseRunner` an, `AtelierPipelineFactory` übergibt aber nie einen; jeder Akteur läuft deshalb als Einzelaufruf, egal welche `ToolNames` er trägt. Der Pfad ist eingefroren, sein Rückbau ist vorgesehen (D-068).

Migration Step38 fügt die `ToolNames`-JSON-Spalte zu `ExecutorProfiles`, `ReviewerProfiles`, `AdvisorProfiles` und `FinalizerProfiles` hinzu.

### A-T9 — Grounding-Neuaufbau auf zentrale Tools

D-060 sah vor, dass jedes Grounding-Profil über `ToolName` auf `GroundingProviderProfile` eine `ToolDefinition` referenziert und über den `ToolBackedGroundingProvider` läuft, der jeden Tool-Typ für eager-Push-Ausführung kapselt. Die rohe Fähigkeitslogik wanderte in wiederverwendbare Executors (z. B. `TavilySearchClient`, `VectorSearchExecutor`).

**Ist-Zustand (D-067):** Für die System-Profile war der Tool-Pfad kaputt (Wissensbasis und akademische Suche sind im `ToolExecutor` nicht verdrahtet, und die Websuche las einen Schlüsselnamen, den der Container nicht setzt). Anhänge, Websuche und Wissensbasis erreichten deshalb still keinen Prompt. Seit Etappe 0 gilt:

- Die fünf System-Profile `tavily-basic`, `tavily-refined`, `run-attachments`, `knowledge-base-default` und `academic-default` (in Stufe 8c ausgemustert, D-089; bleibt für ältere Snapshots in der Regel) tragen in `SystemCrew` keinen `ToolName`; Migration Step47 leert einen gespeicherten `ToolName` genau für diese Zeilen.
- `GroundingPathSelection.UsesToolPath()` schickt ein Profil nur dann auf den Tool-Pfad, wenn es einen `ToolName` hat **und** nicht zu diesen fünf System-Profilen gehört. Ältere Lauf-Snapshots, die noch einen `ToolName` tragen, nehmen deshalb ebenfalls den Provider-Typ-Weg (wichtig beim Fortsetzen eines Laufs).
- Ein übersprungener oder fehlgeschlagener Grounding-Provider wird zur Warnung am Lauf statt zu stillem leeren Kontext; ein fehlschlagendes Tool wirft jetzt einen Fehler, statt ein leeres Ergebnis zu liefern.
- `ToolInvocation.Sequence` wird in der Datenbank vergeben (mit Wiederholung bei Konflikt), das Audit schreibt in einem eigenen Scope; ein fehlgeschlagener Audit-Eintrag verwirft das Tool-Ergebnis nicht mehr.

System-Grounding-Provider-Profile sind in `SystemCrew` als Code-Konstanten definiert. System-`ToolDefinition`s sind Code-Konstanten in `SystemTools`; Migration Step40 seedet sie beim ersten Start idempotent in die Datenbank.

Migration Step39 fügt die `ToolName`-Spalte zu `GroundingProviderProfiles` hinzu.

### A-T10 — Snapshot + Audit-UI

`CrewSnapshot` Schema-Version 2: Jedes Akteur-Profil im Snapshot enthält jetzt seine gebundenen `ToolDefinition`s (vollständig dereferenziert — Name, Typ, Beschreibung, Settings, AccessClass — aber niemals Secrets). Runs bleiben auch nach späteren Katalog-Änderungen reproduzierbar.

Neue UI-Komponente `ToolInvocationsBlock.razor` auf der Run-Detailseite: pro Iteration und Akteur werden Tool-Name, Eingabe (gekürzt), Ausgabe (gekürzt), Kosten und Dauer angezeigt. Bietet Provenienz für Reviewer-Findings.

### A-T11 — `/tools` CRUD-UI + `ToolPicker`

Neue Seiten: `/tools` (Liste), `/tools/create`, `/tools/edit/{name}`, `/tools/view/{name}`.

`ToolEditor.razor`-Felder:
- Name (Kebab-Case, validiert)
- Anzeigename, Beschreibung
- Tool-Typ (Dropdown)
- Typ-spezifische Settings (API-Key / Base-URL / Endpoint / Collection / Domains / Sprache / Festtext usw.)
- SecretRef (ENV-Variablen-Name, **niemals** der tatsächliche Wert — Feldhilfe macht das unmissverständlich klar)
- AccessClass (Standard: ReadOnly; bei Mutating wird ein roter Gefahren-Badge angezeigt)
- LlmSchema (JSON-Editor für die Eingabe-Schema-Beschreibung)

`ToolPicker.razor` ist in allen Akteur-Editoren wiederverwendbar (`ExecutorProfileEditor`, `ReviewerProfileEditor`, `AdvisorProfileEditor`, `FinalizerEditor`) sowie im `GroundingProviderEditor` (dort wird genau ein Tool referenziert). Der Picker zeigt eine Capability-Warnung, wenn der Provider des Akteurs kein agentic Tool-Use unterstützt.

---

## Phase B — Auto-Crew-Composer-Integration

### B-T1 — `CrewPartSpec.ToolNames`

`CrewSpecArtifact` → `CrewPartSpec.ToolNames` (`IReadOnlyList<string>?`): optionale Liste von Tool-Namen für den zusammengesetzten Akteur.

`CrewSpecParser` deserialisiert das `tool_names`-JSON-Array. `CrewMaterializer` gibt `ToolNames` an das materialisierte Profil weiter. Das `CrewSpecTool`-JSON-Schema enthält `tool_names` als optionales Array für Executor, Reviewer, Advisor und Finalizer.

### B-T2 — Tool-Katalog als Composer-Grounding

`Infrastructure/Grounding/ToolCatalogGroundingProvider.cs` — Diskriminator: `"tool-catalog"`.

Bei jeder Grounding-Ausführung werden alle `ToolDefinition`s geladen (über scoped `IToolDefinitionRepository`), als Markdown-Tabelle (Name, Typ, Beschreibung, Zugriffsklasse) formatiert und als Kontext in den Kompositions-Run eingespeist. Das Composer-Meta-LLM kann vorhandene Tools damit namentlich referenzieren, statt Namen zu halluzinieren.

Registriert als dritter Grounding-Provider in `SystemCrew.CrewComposerTemplate.GroundingProviderNames`.

### B-T3 — `CrewComposerToolBindingProfile`-Reviewer

System-Reviewer `"crew-composer-tool-binding"` (Provider: `openrouter`, Modell: `google/gemini-3.5-flash`) prüft Tool-Binding-Entscheidungen in zusammengesetzten Specs:

| Prüfung | Schwere |
|---|---|
| Notwendigkeit: Tool ohne erkennbaren Nutzen für die Akteur-Rolle | Minor |
| Zugriffsklasse: Mutating-Tool in Phase B | Critical |
| Rollen-Fit: `static-context` als Pull beim Reviewer | Major |
| Anzahl: mehr als 3 Tools pro Akteur | Minor |
| Katalog-Mitgliedschaft: Name nicht im Katalog vorhanden | Critical |

Dieser Reviewer ist der 6. auf der `crew-composer`-Crew.

### B-T4 — Deterministischer Validator: Step 8

`CrewSpecValidator.ValidateToolBindingsAsync` läuft nach allen Profil-Validierungsschritten:

| Prüfung | Schwere | Bedingung |
|---|---|---|
| 8a — Provider-Capability | Critical | Akteur hat `tool_names`, aber `!SupportsAgenticTools(provider)` |
| 8b — Tool-Existenz | Critical | Tool-Name nicht in `IToolDefinitionRepository` gefunden |
| 8c — Mutating gesperrt | Critical | `tool.AccessClass == Mutating && !spec.AllowMutatingTools` |

Alle eindeutigen Tool-Namen werden in einer einzigen DB-Abfrage batch-geladen (kein N+1).

---

## Phase C — MCP-Client (entfernt)

Der MCP-Client (`McpServerConfig`, `AtelierMcpClientFactory`, Werkzeugsuche, die Seiten unter `/mcp-servers` und der `mcp-tool`-Zweig von `ToolExecutor`) wurde in Stufe 8c entfernt (D-089). Der eigene MCP-Server von Atelier (`/mcp`) ist nicht betroffen. Die Tabelle `mcp_server_configs` bleibt in der Datenbank.

## Datenbankänderungen

| Migration | Step | Änderung |
|---|---|---|
| `Step37ToolSystem` | 37 | Neue Tabellen: `tool_definitions` (JSONB Settings, LlmSchema), `tool_invocations` |
| `Step38ToolNamesOnProfiles` | 38 | Neue JSON-Spalte `ToolNames` in `ExecutorProfiles`, `ReviewerProfiles`, `AdvisorProfiles`, `FinalizerProfiles` |
| `Step39ToolNameOnGroundingProfiles` | 39 | Neue Spalte `ToolName` in `GroundingProviderProfiles` |
| `Step40SystemToolsSeed` | 40 | Idempotenter Upsert von 9 System-`ToolDefinition`s beim Start |
| `Step41McpServerConfigs` | 41 | Neue Tabelle: `mcp_server_configs` (Code in Stufe 8c entfernt, D-089; die Tabelle bleibt) |
| `Step47EngineHonesty` | 47 | Leert `ToolName` an den fünf System-Grounding-Profilen (D-067); die übrigen Teile von Step47 stehen in [02-architecture](02-architecture_de.md#runs) |

**Keine bestehenden Daten wurden gelöscht.** Alle Migrationen fügen nur neue Tabellen und Spalten hinzu. Die `Down()`-Methoden (Rollback-Pfad) enthalten `DROP`-Anweisungen, werden beim normalen Deploy aber nicht ausgeführt.

---

## Sicherheitshinweise

- **Secrets** — `SecretRef` enthält nur den ENV-Variablen-Namen. Der eigentliche API-Key/Token wird niemals in der DB, im `CrewSnapshot`, in Logs oder einem UI-Feld gespeichert. Nur Namen auf der Allowlist (`IEnvReferencePolicy`, `EnvReference:Allowed`, Präfix `ATELIER_SECRET_*`) werden beim Speichern angenommen und zur Laufzeit gelesen.
- **Editoren nur für Admins** — Tools, Provider und Grounding-Profile können nur Administratoren anlegen oder ändern.
- **SSRF** — `IUrlSafetyValidator` blockiert private IP-Bereiche, `localhost`, Link-Local und interne Hostnamen vor jedem HTTP-Aufruf aus einer `ToolDefinition` heraus (url-fetch; rest-api und mcp-tool sind in Stufe 8c entfallen, D-089).
- **Mutating-Zugriff** — `ToolAccessClass.Mutating` ist standardmäßig gesperrt. Der Betreiber muss `allow_mutating_tools: true` explizit pro Spec setzen. Die UI hebt Mutating-Tools mit einem roten Gefahren-Badge hervor.
- **Per-Turn-Cap** — der agentic Loop läuft nie unbegrenzt. Standard: 5 Tool-Aufrufe pro Akteur-Turn. Cap-Erreicht-Status wird als `ToolInvocationOutcome.CapReached` auditiert.
- **Per-Tool-Timeout** — Standard: 30 Sekunden pro Tool-Aufruf. Timeouts werden auditiert.

---

## Lokale Entwicklung

```bash
# Datenbank starten
cd /srv/docker/websites/geef_atelier
docker compose -f docker-compose.dev.yml up -d postgres

# Web-App mit Hot-Reload
dotnet watch --project src/Geef.Atelier.Web

# System-Tools werden automatisch beim Start geseedet (Migration Step40)
# Navigation zu /tools zeigt die 9 System-Tool-Definitionen
```

---

## Datei-Index

| Schicht | Pfad | Beschreibung |
|---|---|---|
| Core | `Core/Domain/Tools/ToolDefinition.cs` | Zentrale Definition + Settings-Schlüssel |
| Core | `Core/Domain/Tools/ToolType.cs` | 8 Typ-Konstanten (plus das ausgemusterte `mcp-tool`) |
| Core | `Core/Domain/Tools/ToolAccessClass.cs` | ReadOnly / Mutating |
| Core | `Core/Domain/Tools/ToolInvocation.cs` | Audit-Record |
| Core | `Core/Persistence/Tools/IToolDefinitionRepository.cs` | CRUD-Interface |
| Core | `Core/Persistence/Tools/IToolInvocationRepository.cs` | Append-only-Interface |
| Application | `Application/Tools/IToolExecutor.cs` | Einheitliches Ausführungs-Interface |
| Application | `Application/Tools/IToolSchemaProvider.cs` | LLM-Schema-Generierung |
| Infrastructure | `Infrastructure/Tools/ToolExecutor.cs` | Dispatcher für alle 9 Typen |
| Infrastructure | `Infrastructure/Pipeline/ToolUseRunner.cs` | Agentic-Mehrfach-Turn-Loop |
| Infrastructure | `Infrastructure/Grounding/ToolCatalogGroundingProvider.cs` | B-T2 Katalog-Grounding |
| Infrastructure | `Infrastructure/Composition/CrewSpecValidator.cs` | Step 8 Tool-Binding-Prüfungen |
| Infrastructure | `Infrastructure/Persistence/Migrations/` | Step37–Step41 |
| Web | `Web/Components/Pages/ToolsIndex.razor` | `/tools`-Liste |
| Web | `Web/Components/Pages/ToolEditor.razor` | Tool anlegen / bearbeiten |
| Web | `Web/Components/Pages/ToolView.razor` | Read-only-Detailansicht |
| Web | `Web/Components/UI/ToolPicker.razor` | Wiederverwendbarer Akteur-Tool-Picker |
| Web | `Web/Components/UI/ToolInvocationsBlock.razor` | Run-Detail-Audit-Block |
| Core | `Core/Domain/Crew/Composition/CrewSpecArtifact.cs` | `AllowMutatingTools`-Flag |
| Infrastructure | `Infrastructure/Composition/CrewSpecParser.cs` | `allow_mutating_tools`-Parsing |
| Infrastructure | `Infrastructure/Composition/CrewSpecTool.cs` | Composer-JSON-Schema |
