# Claude-Code-Prompt: Grounding-Provider Foundation + Tavily Web-Search

*Erster Schritt zur RAG/Recherche-Erweiterung. Etabliert die `IGroundingProvider`-Abstraktion, implementiert Tavily als ersten Web-Search-Provider, ergänzt CrewTemplate um Grounding-Provider-Konfiguration, persistiert Citations und Costs, zeigt alles in der Grounding-Sektion auf RunDetail.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. PS-7 hat Advisor-Pässe etabliert, der parallele Grounding-Visualization-Step macht die Grounding-Phase in der UI sichtbar. Was fehlt: Grounding tut aktuell technisch nichts — `BriefingGroundingStep` ist Pass-Through, der `AdvisorContextGroundingStep` injiziert nur Advisor-Output.

Dein Job ist die **Grounding-Provider-Foundation plus Web-Search-Aktivierung**: eine generische `IGroundingProvider`-Abstraktion (analog zu `ILlmClient`) in Atelier-Core, eine erste konkrete Implementierung mit **Tavily** als Web-Search-Service, Konfiguration pro CrewTemplate (opt-in, Klassik bleibt unverändert), Persistierung der Citations und Costs, UI-Anzeige der Recherche-Ergebnisse in der Grounding-Sektion.

Architekturell wichtig: die Foundation muss **provider-agnostisch genug** sein, dass ein späterer `VectorStoreGroundingProvider` (PgVector + eigene Dokumente) **ohne Refactor** angedockt werden kann. Das ist ein expliziter Architekt-Auftrag in Phase 1.4.

## Vorgehen

**Du folgst dem Workflow in `/srv/docker/docs/geef-workflow.md`** vollständig. Vier Phasen, fünf Reviewer, Pflicht-Advisors, plus **Phase 5: Merge & Deploy** (verbindlich, siehe unten).

**Branch:** `feat/grounding-providers-tavily`. PR gegen `main` (Step enthält Migration, kein Direct-Push).

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

1. **`/srv/docker/docs/geef-workflow.md`** und **`CLAUDE.md`**
2. **`docs/Vom_Prompt_zur_Pipeline.pdf`** — besonders Grounding-Phase-Beschreibung, Bild 6 (Grounding läuft **einmal pro Run**, nicht pro Iteration)
3. **`docs/02-architecture.md`** — Schichtenarchitektur
4. **`docs/05-decisions-log.md`** — alle bisherigen Entscheidungen
5. **`docs/reports/step-02-report.md`** — wie `BriefingGroundingStep` ursprünglich angelegt wurde
6. **`docs/reports/post-skeleton-07-advisor-passes-report.md`** — Decorator-Pattern aus Layer-Trennung-Gründen, das nachgemacht werden sollte für Grounding-Provider
7. **`docs/reports/feature-grounding-visualization-report.md`** falls bereits vorhanden — die Grounding-Sektion-UI ist die Stelle, wo Citations angezeigt werden
8. **Aktueller Code:**
   - `src/Geef.Atelier.Infrastructure/Pipeline/BriefingGroundingStep.cs` — wird erweitert
   - `src/Geef.Atelier.Infrastructure/Pipeline/AtelierPipelineFactory.cs` — Provider-Wiring
   - `src/Geef.Atelier.Core/Domain/Crew/CrewTemplate.cs` — bekommt `GroundingProviderNames[]`
   - `src/Geef.Atelier.Core/Domain/Crew/CrewSnapshot.cs` — wird erweitert um Grounding-Provider-Profile
   - `src/Geef.Atelier.Web/Components/UI/GroundingSection.razor` (falls vom Visualization-Step da) — wird um Citations-Anzeige erweitert
9. **Tavily-API-Dokumentation:**
   - `https://docs.tavily.com/` — Endpoint, Request/Response-Schema, Pricing
   - Speziell: `POST https://api.tavily.com/search` mit `api_key`, `query`, `search_depth`, `include_answer`, `max_results`

## Verbindliche Architektur-Entscheidungen

| Bereich | Entscheidung |
|---|---|
| **Abstraktion** | `IGroundingProvider` in Application-Layer (nicht Infrastructure). Analog zu `ILlmClient`, aber höher angesiedelt weil Grounding-Provider Atelier-spezifische Konzepte sind, nicht SDK-Konzepte. |
| **Implementation-Pattern** | Decorator-Pattern wie bei Advisor (D-030): `MultiProviderGroundingStep` als Atelier-eigener `IGroundingStep`-Decorator, der eine Liste konfigurierter `IGroundingProvider`-Implementationen aufruft. **Nicht** SDK-natives Grounding-Interface mit eigener Implementation belasten. |
| **Tavily-Pricing-Tier** | "Basic" als Default (1 Credit/Search). "Advanced" (2 Credits, mehr Tiefe) als Provider-Profile-Option. Pro `TavilyGroundingProviderProfile` konfigurierbar. |
| **Citation-Persistierung** | Neue Tabelle `GroundingConsultations` analog zu `AdvisorConsultations`. Schema: `Id, RunId, GroundingProviderName, Query, Citations (JSONB), TokensOrCreditsUsed, CostEur (decimal nullable), CreatedAt`. |
| **Cost-Schema-Foundation** | Generisches Cost-Tracking-Feld direkt mit anlegen — `CostEur decimal NULL` Spalte in `GroundingConsultations`. Vollständiges Cost-Tracking-Feature (LLM-Token-Costs, Welcome-Stats) ist separater Step, aber die DB-Foundation kommt jetzt. |
| **Klassik-Default** | Klassik-Template bekommt **keinen** Grounding-Provider. Bleibt unverändert. Verhaltens-Regression-Schutz analog PS-5/PS-7. |
| **System-Grounding-Provider-Profile** | Ein System-Profile: `tavily-basic` (Provider: Tavily, Tier: Basic, Max Results: 5). Read-only. Beweis-of-Concept. |
| **Custom-Profile-Pattern** | Custom-Grounding-Provider-Profile mit `custom-`-Prefix, analog zu Reviewer/Advisor-Profilen. |
| **API-Key-Handling** | `TAVILY_API_KEY` Env-Var in `.env`. Beim Container-Start: wenn nicht gesetzt, Tavily-Provider ist **deaktiviert aber registriert** — Custom-Templates die ihn referenzieren scheitern beim Run mit klarer Error-Message. Kein hartes App-Crash beim Start. |
| **MCP-Tool-Erweiterung** | Neues Tool `list_grounding_provider_profiles` analog zu `list_advisor_profiles`. `submit_request.custom_crew.grounding_provider_profile_names` als neues Array-Feld. |
| **Migration** | `Step12GroundingProviders` (oder nächste freie Nummer). Tabellen: `GroundingProviderProfiles`, `GroundingConsultations`. `CrewTemplates.GroundingProviderNames` als JSONB-Array. `Runs` braucht keine neue Spalte (Costs sind auf Consultation-Ebene). |
| **Cost-Anzeige-Granularität** | Pro Run auf RunDetail: "Recherche: 2 Tavily-Searches, ~$0.002". Pro Consultation in der Grounding-Sektion: Query, Citations-Count, Cost. |
| **Citation-Display** | In `GroundingSection.razor`: pro Citation Title, URL, Snippet (max 200 Zeichen), Klick öffnet neues Tab. Klarer "Powered by Tavily"-Hinweis bei Web-Search-Quellen für Transparenz. |
| **Foundation für Vector-Store-Step (später)** | `IGroundingProvider` muss `VectorStoreGroundingProvider` ohne Refactor unterstützen. Konkret: `GroundingResult.Citations` ist generisch genug für Web-URLs **und** lokale Document-References (`SourceCitation` mit nullable `Url`, optionalen `DocumentReference`). |

## Konkrete Anforderungen

### 1. Domain-Layer (Core)

**`Geef.Atelier.Core/Domain/Crew/Grounding/`** — neues Verzeichnis:

```csharp
public sealed record GroundingProviderProfile(
    string Name,
    string DisplayName,
    string Description,
    string ProviderType,          // "tavily" | "vector-store" (zukünftig)
    Dictionary<string, string> ProviderSettings,  // z.B. "Tier": "basic", "MaxResults": "5"
    int? MaxQueriesPerRun,        // optional Safety-Cap (default null = 1)
    bool IsSystem
);

public sealed record SourceCitation(
    string Title,
    string? Url,                  // null bei Vector-Store-Quellen
    string Snippet,
    string? DocumentReference,    // null bei Web-Search-Quellen, z.B. "doc-uuid/chunk-3" für Vector-Store
    double? RelevanceScore        // optional, je nach Provider
);

public sealed record GroundingConsultation(
    Guid Id,
    Guid RunId,
    string GroundingProviderName,
    string Query,
    IReadOnlyList<SourceCitation> Citations,
    int TokensOrCreditsUsed,
    decimal? CostEur,
    DateTimeOffset CreatedAt
);

public sealed record GroundingResult(
    string ProviderName,
    string EnrichedContext,       // Was dem Briefing-Context hinzugefügt wird (formatierter Text)
    IReadOnlyList<SourceCitation> Citations,
    int TokensOrCreditsUsed,
    decimal? CostEur
);
```

**System-Grounding-Provider-Profile** als Code-Konstante in `SystemCrew.cs`:

```csharp
public static readonly GroundingProviderProfile TavilyBasicProfile = new(
    Name: "tavily-basic",
    DisplayName: "Tavily Basic Web Search",
    Description: "Web-Recherche via Tavily API (Basic-Tier, 1 Credit pro Suche). Liefert ~5 Web-Quellen mit Titel, URL und Snippet.",
    ProviderType: "tavily",
    ProviderSettings: new() { ["Tier"] = "basic", ["MaxResults"] = "5", ["IncludeAnswer"] = "true" },
    MaxQueriesPerRun: 1,
    IsSystem: true
);
```

`CrewTemplate` wird erweitert:

```csharp
public sealed record CrewTemplate(
    // ... bestehende Felder ...
    string[] GroundingProviderNames    // neu, default []
);
```

Klassik-Template hat `GroundingProviderNames: []` (leeres Array). Bestehendes Verhalten unverändert.

`CrewSnapshot` analog:

```csharp
public sealed record CrewSnapshot(
    // ... bestehende Felder ...
    IReadOnlyList<GroundingProviderProfile> GroundingProviders  // neu, vollständig dereferenziert
);
```

### 2. Application-Layer

**`IGroundingProvider`-Interface** in `Geef.Atelier.Application/Crew/Grounding/`:

```csharp
public interface IGroundingProvider
{
    string ProviderType { get; }    // muss matchen mit GroundingProviderProfile.ProviderType

    Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct);
}

public interface IGroundingProviderFactory
{
    IGroundingProvider Create(string providerType);   // Resolve per ProviderType, throws if not registered
    bool IsRegistered(string providerType);
}
```

**`ICrewService` erweitern** um Grounding-Provider-CRUD analog zu Advisor-CRUD aus PS-7:
- `ListGroundingProviderProfilesAsync(ct)`
- `GetGroundingProviderProfileAsync(name, ct)`
- `CreateCustomGroundingProviderProfileAsync(...)`
- `UpdateCustomGroundingProviderProfileAsync(...)`
- `DeleteCustomGroundingProviderProfileAsync(...)`

**`CrewSnapshotBuilder` erweitern:** `ResolveGroundingProvidersAsync(...)`.

### 3. Infrastructure-Layer: Tavily-Provider

**`Geef.Atelier.Infrastructure/Grounding/TavilyGroundingProvider.cs`:**

```csharp
internal sealed class TavilyGroundingProvider : IGroundingProvider
{
    public string ProviderType => "tavily";

    public TavilyGroundingProvider(
        HttpClient httpClient,                // konfiguriert auf https://api.tavily.com
        IOptions<TavilyOptions> options,      // mit ApiKey aus .env
        IGroundingConsultationRepository consultations,
        ILogger<TavilyGroundingProvider> logger)
    {
        // ...
    }

    public async Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException(
                "TAVILY_API_KEY is not configured. Grounding-Provider 'tavily' cannot be used.");

        // 1. Tavily-Request bauen aus profile.ProviderSettings + briefingText als Query
        // 2. POST /search mit Bearer-Key
        // 3. Response parsen zu SourceCitations
        // 4. EnrichedContext bauen: formatierter Text der die Citations zusammenfasst
        // 5. Cost berechnen: tier-spezifisch (basic = 1 credit, advanced = 2 credits, credit-to-eur Mapping)
        // 6. GroundingConsultation persistieren
        // 7. GroundingResult zurückgeben
    }
}
```

**Tavily-Request-Schema** (aus deren Doku):

```json
{
  "api_key": "tvly-...",
  "query": "...",
  "search_depth": "basic" | "advanced",
  "include_answer": true,
  "max_results": 5
}
```

**Response-Schema:**

```json
{
  "answer": "Tavily's synthesized answer...",
  "results": [
    {
      "title": "...",
      "url": "...",
      "content": "...",
      "score": 0.95
    }
  ],
  "response_time": 1.23
}
```

**`EnrichedContext`-Formatierung** (was dem Briefing als Kontext hinzugefügt wird):

```
[Web research context]

Tavily synthesized answer:
{answer}

Sources:
1. {title} ({url})
   {content snippet, max 300 chars}
2. ...

[End of web research context]
```

Dieser Text wird im Pipeline-Context unter `AtelierContextKeys.GroundingContext` abgelegt. Der `ProfileBasedExecutor` (analog zur AdvisorBlock-Logik aus PS-7) prepended diesen Block vor dem eigentlichen System-Prompt.

**Cost-Berechnung:**
- Tavily-Pricing: Basic = $0.001/Search, Advanced = $0.002/Search
- Umrechnung in EUR via fixe Rate (z.B. 0.92 USD/EUR, in `appsettings.json` konfigurierbar — Architect-Entscheidung)
- `CostEur` in `GroundingConsultation` persistieren

### 4. Pipeline-Integration

**`MultiProviderGroundingStep : IGroundingStep`** als neuer Decorator in `Geef.Atelier.Infrastructure/Pipeline/`:

```csharp
internal sealed class MultiProviderGroundingStep : IGroundingStep
{
    public MultiProviderGroundingStep(
        IGroundingStep inner,                        // BriefingGroundingStep oder AdvisorContextGroundingStep
        IReadOnlyList<GroundingProviderProfile> providers,
        IGroundingProviderFactory factory,
        Guid runId,
        ILogger<MultiProviderGroundingStep> logger) { ... }

    public async Task<IRunContext> InitializeContextAsync(string inputPrompt, CancellationToken ct)
    {
        // 1. Inner-Step zuerst (BriefingGrounding + AdvisorContext)
        var context = await _inner.InitializeContextAsync(inputPrompt, ct);

        // 2. Pro konfiguriertem GroundingProvider:
        //    - Factory.Create(profile.ProviderType)
        //    - provider.EnrichAsync(inputPrompt, profile, runId, ct)
        //    - GroundingResult-Output an Context anhängen
        //
        // 3. Alle EnrichedContext-Blöcke werden konkateniert und unter
        //    AtelierContextKeys.GroundingContext gespeichert
        //
        // 4. Bei Provider-Failure: Run schlägt fehl (Konsistent mit Advisor-Failure aus PS-7)
        //
        // 5. Return enriched Context
    }
}
```

**`AtelierPipelineFactory` erweitern:** `BuildWithGroundingProviders(crewSnapshot, runId)`. Decorator-Wiring:

```
MultiProviderGroundingStep
  → AdvisorContextGroundingStep
    → BriefingGroundingStep
```

**`ProfileBasedExecutor` erweitern:** liest auch `AtelierContextKeys.GroundingContext` und prepended ihn vor dem AdvisorBlock im System-Prompt:

```
[Web research context (from Grounding)]
...

[Advisor consultations]
...

[Original system prompt continues]
...
```

### 5. Persistierung

**Migration `Step12GroundingProviders`:**

```sql
CREATE TABLE "GroundingProviderProfiles" (
    "Name" text PRIMARY KEY,
    "DisplayName" text NOT NULL,
    "Description" text NOT NULL,
    "ProviderType" text NOT NULL,
    "ProviderSettings" jsonb NOT NULL DEFAULT '{}'::jsonb,
    "MaxQueriesPerRun" integer NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT now(),
    "UpdatedAt" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE "GroundingConsultations" (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "RunId" uuid NOT NULL REFERENCES "Runs"("Id") ON DELETE CASCADE,
    "GroundingProviderName" text NOT NULL,
    "Query" text NOT NULL,
    "Citations" jsonb NOT NULL,
    "TokensOrCreditsUsed" integer NOT NULL DEFAULT 0,
    "CostEur" decimal(10,4) NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX "IX_GroundingConsultations_RunId" ON "GroundingConsultations"("RunId");

-- Klassik-Template explizit auf leeres GroundingProviderNames-Array setzen
-- (idempotent, sollte bereits Default sein)
UPDATE "CrewTemplates"
SET "GroundingProviderNames" = '[]'::jsonb
WHERE "Name" = 'klassik' AND "GroundingProviderNames" IS NULL;
```

**Repository-Interfaces** in Core, Implementations in Infrastructure:
- `IGroundingProviderProfileRepository` (CRUD)
- `IGroundingConsultationRepository` (Create, GetByRunId)

### 6. Tavily-Konfiguration

**`appsettings.json` ergänzen:**

```json
{
  "Tavily": {
    "Endpoint": "https://api.tavily.com",
    "BasicSearchCostUsd": 0.001,
    "AdvancedSearchCostUsd": 0.002,
    "UsdToEurRate": 0.92,
    "RequestTimeoutSeconds": 30
  }
}
```

**`.env.example` ergänzen:**

```
# Tavily Web Search (https://tavily.com)
# Get your API key at https://app.tavily.com/
# Free tier: 1000 searches/month
TAVILY_API_KEY=
```

**`TavilyOptions`-Record** mit Bind-Pattern wie bei `LlmOptions`.

**`Program.cs` Service-Registrierung:**

```csharp
builder.Services.Configure<TavilyOptions>(builder.Configuration.GetSection("Tavily"));
builder.Services.AddHttpClient<TavilyGroundingProvider>(client => {
    client.BaseAddress = new Uri(/* from options */);
});
builder.Services.AddSingleton<IGroundingProviderFactory, GroundingProviderFactory>();
```

### 7. MCP-Layer

**Neues Tool `list_grounding_provider_profiles`** analog zu `list_advisor_profiles`. DTO:

```csharp
public sealed record GroundingProviderProfileDto(
    string Name,
    string DisplayName,
    string Description,
    string ProviderType,
    int? MaxQueriesPerRun,
    bool IsSystem
);
```

**`submit_request`-Tool:** `custom_crew.grounding_provider_profile_names` als neues optionales Array-Feld.

### 8. UI-Layer

**Neue Pages unter `/crew/profiles/grounding/`:**
- `GroundingProvidersIndex.razor` — Liste analog zu Reviewer/Advisor
- `GroundingProviderEditor.razor` — Editor mit Provider-Type-Dropdown (aktuell nur "Tavily"), Tier-Auswahl bei Tavily, MaxQueriesPerRun

**`CrewTemplateEditor` erweitern** — neuer Picker-Block "Grounding Providers" zwischen Briefing-Section und Executor-Section (visuell vor dem Iteration-Loop). Verwendet bestehende Picker-Komponenten-Pattern.

**`CrewSummary` erweitern** — wenn `snapshot.GroundingProviders` nicht leer: zusätzliche Sektion "Grounding: tavily-basic (Tavily Web Search, Basic)".

**`GroundingSection.razor`** (aus dem parallel laufenden Visualization-Step) erweitern um Citation-Block:

```
═══════════════════════════════════════
🔍 Grounding
═══════════════════════════════════════
Briefing
   "..."

[Web Research]                            ▾
  Provider: Tavily (Basic) — 1 search, ~$0.001 (~€0.001)

  Query: "<extrahierte Query oder vollständiges Briefing>"

  Citations (5):
    1. Beispiel-Titel
       https://example.com/article
       "Snippet aus dem Quell-Text, max 200 Zeichen..."
       Relevance: 0.95
    2. ...

[Advisor consultations]                   ▾
  ...

[Grounded Brief]                          ▾
  ...
═══════════════════════════════════════
```

**Cost-Anzeige im Run-Header:** kleine Zeile rechts oben "Recherche: ~$0.001 (~€0.001)" wenn Grounding-Consultations existieren. Wird Grundlage für späteres vollständiges Cost-Tracking.

**`AdvisorPicker`-Pattern wiederverwenden** für `GroundingProviderPicker`. Selbe UI-Konventionen.

**`ReviewerDisplay`-Helper erweitern** um Grounding-Provider-Display-Methoden (analog zu Advisor-Display).

### 9. Tests

**Domain-Tests:**
- `GroundingProviderProfileEqualityTests`
- `SystemCrewTavilyBasicProfileTests`
- `SourceCitationTests` (mit/ohne URL, mit/ohne DocumentReference)
- `GroundingResultTests`

**Application-Tests:**
- `CrewServiceGroundingProviderCrudTests`
- `CrewSnapshotBuilderGroundingProvidersTests`
- `GroundingProviderFactoryTests` (Factory resolved korrekten Provider per Type, throws bei unbekanntem Type)

**Infrastructure-Tests:**
- `TavilyGroundingProviderTests` mit Mock-HttpClient — Request-Format korrekt, Response-Parsing korrekt, Cost-Berechnung korrekt
- `TavilyMissingApiKeyTests` — wirft `InvalidOperationException` mit klarer Message
- `MultiProviderGroundingStepTests` — ruft konfigurierte Provider sequenziell auf, konkateniert EnrichedContext, persistiert Consultations
- `GroundingProviderRepositoryTests` (CRUD)
- `GroundingConsultationRepositoryTests` (Create, GetByRunId)

**Pipeline-Tests:**
- `AtelierPipelineFactoryWithGroundingTests` — Pipeline mit aktivem Grounding-Provider, Mock-Tavily, vollständiger Run-Durchlauf
- `KlassikRegressionTests` — Klassik-Template bleibt ohne Grounding-Provider, keine Tavily-Calls, keine GroundingConsultations

**MCP-Tests:**
- `ListGroundingProviderProfilesToolTests`

**UI-Tests (bUnit):**
- `GroundingProvidersIndexTests`
- `GroundingProviderEditorTests`
- `CrewTemplateEditorWithGroundingPickerTests`
- `GroundingSectionWithCitationsTests`
- `CrewSummaryWithGroundingTests`

**Bestehende 239+ Tests müssen grün bleiben.**

### 10. Real-Pipeline-Verifikation (Pflicht-AC)

Auf der Production-Instance `https://geef.stefan-bechtel.de/` (nach Deploy):

1. **Tavily-Account anlegen** (manueller Schritt, Anleitung im Bericht): https://app.tavily.com/ → Sign up → API-Key generieren
2. **API-Key in `.env` setzen:** `TAVILY_API_KEY=tvly-...`
3. **Container restart:** `docker compose restart web`
4. **UI-Test:** `/crew/profiles/grounding` → `tavily-basic` als System-Profile sichtbar
5. **Custom-Template anlegen:** "klassik-mit-recherche" mit Klassik-Crew + `tavily-basic` als Grounding-Provider
6. **Test-Briefing** einreichen mit der Custom-Crew, idealerweise ein aktuelles Thema das echte Recherche braucht (z.B. "Schreibe einen kurzen Bericht über die Entwicklung der LLM-Open-Source-Modelle im Jahr 2026")
7. **RunDetail beobachten:**
   - Grounding-Sektion zeigt Web-Research-Block
   - Citations sind sichtbar mit Titel/URL/Snippet
   - Cost-Anzeige im Header zeigt ~$0.001
8. **Vergleichs-Run mit Klassik** (ohne Recherche): andere Output-Charakteristik (kein aktueller Kontext)
9. **Beobachtungen im Bericht** dokumentieren: wie hat das Recherche-Ergebnis den Executor-Output beeinflusst, welche Citations waren relevant.

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` 0 Errors, 0 Warnings.
2. `dotnet test` — alle bestehenden Tests grün + neue Tests.
3. **DB-Migration `Step12GroundingProviders`** läuft sauber. Tabellen `GroundingProviderProfiles` und `GroundingConsultations` vorhanden. `CrewTemplates.GroundingProviderNames` als JSONB.
4. **System-Profile `tavily-basic` verfügbar** via `ICrewService.ListGroundingProviderProfilesAsync()`.
5. **Custom-Grounding-Provider via UI anlegbar** mit `custom-`-Prefix. System-Profile read-only.
6. **CrewTemplate referenziert Grounding-Provider** über `GroundingProviderNames[]`. Snapshot enthält dereferenzierte Profile.
7. **`MultiProviderGroundingStep` ruft Tavily** mit konfiguriertem Profile auf. EnrichedContext landet in `AtelierContextKeys.GroundingContext`.
8. **`ProfileBasedExecutor` prepended Grounding-Context** vor Advisor-Block und System-Prompt.
9. **`GroundingConsultations` werden persistiert** mit Query, Citations, Cost.
10. **Klassik-Regression:** Klassik-Template ruft kein Tavily, erzeugt keine GroundingConsultations.
11. **TAVILY_API_KEY missing** führt zu klarer Error-Message beim Run, nicht zu App-Crash beim Start.
12. **UI: Grounding-Sektion zeigt Citations** mit Titel, URL, Snippet, Cost-Info.
13. **UI: Cost-Hint im Run-Header** wenn Recherche stattfand.
14. **MCP-Tool `list_grounding_provider_profiles`** funktional.
15. **Real-Test mit Custom-Crew + Tavily** durchgeführt und im Bericht dokumentiert (vergleichend mit Klassik-Run).
16. **Decisions-Log-Eintrag** (D-032 oder nächste freie Nummer) mit Architect-Entscheidungen.
17. **Foundation-Check:** `IGroundingProvider`-Abstraktion ist generisch genug für späteren `VectorStoreGroundingProvider` — Architect dokumentiert das im Bericht.
18. **Merge auf `main` durchgeführt** (PR gemerged).
19. **Production-Deploy verifiziert** — Container neu gebaut, Migration gelaufen, Tavily-Key konfiguriert, Live-Test erfolgreich.

## Phase 5 — Merge & Deploy (verbindlich)

Direkt nach Phase 4 (Bericht), bevor der Step als abgeschlossen gilt:

```bash
# 1. Tavily-Account-Setup (manueller Schritt vorab durch Stefan)
# Account anlegen: https://app.tavily.com/
# API-Key kopieren

# 2. Merge auf main
cd /srv/docker/websites/geef_atelier
git checkout main
git pull --ff-only
gh pr merge <PR-Number> --merge --delete-branch

# 3. .env aktualisieren
echo "TAVILY_API_KEY=tvly-..." >> .env

# 4. Deploy
docker compose build --no-cache web
docker compose up -d

# 5. Migration verifizieren
docker exec geef-atelier-postgres psql -U geef_atelier -d geef_atelier -c \
  "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1 DESC LIMIT 3;"
# Step12GroundingProviders sollte sichtbar sein

# 6. Health-Check
docker compose ps
curl -I https://geef.stefan-bechtel.de/

# 7. Live-Verifikation (Browser):
# - /crew/profiles/grounding → tavily-basic sichtbar
# - Custom-Template anlegen → Run einreichen
# - RunDetail zeigt Web-Research-Block + Citations
```

**Im Bericht festhalten:** Merge-Commit-Hash, Deploy-Timestamp, Tavily-Account-Status (Free-Tier-Credits verbraucht), Live-Verifikation pro AC.

**Bei Issues:** Direct-Fix-Commits oder klare Folge-Step-Notiz im Bericht.

## Was du in diesem Step NICHT tust

- **Kein Vector-Store** — `IGroundingProvider`-Foundation ermöglicht das, aber Implementation kommt im Folge-Step. Trotzdem `SourceCitation.DocumentReference` und `RelevanceScore` jetzt schon im Schema, damit kein späteres Refactor nötig wird.
- **Kein vollständiges Cost-Tracking** — nur Grounding-Costs. LLM-Token-Costs und Welcome-Stats sind separater Step. Aber die DB-Foundation (`CostEur`-Spaltenmuster) wird hier gelegt.
- **Keine Reviewer-Tools** — Reviewer-Faktencheck mit Web-Search wäre nice, ist aber eigenes Feature ("Reviewer with Tools"). Auf späterer Roadmap.
- **Keine Executor-Tools** — Anti-Pattern, Executor löst das Problem nach Grounding, ruft nichts mehr nach.
- **Keine Multi-Query-Logik** — pro Run **eine** Tavily-Query pro Provider-Profile (max via `MaxQueriesPerRun` cap-bar). Komplexere Query-Strategien (z.B. Briefing wird in Sub-Queries zerlegt) wäre separater Step.
- **Kein Caching von Tavily-Responses** — jeder Run macht eigene Search. Caching ist Optimization, separater Step.
- **Kein Query-Editor in der UI** — die Query ist aktuell das vollständige Briefing. Custom-Query-Templating wäre späterer Step.

## Architect-Konsultation (Phase 1.4) — vier echte Knackpunkte

1. **Foundation-Generizität:** `IGroundingProvider` muss `VectorStoreGroundingProvider` ohne Refactor unterstützen. Konkrete Architekt-Prüfung: ist `GroundingProviderProfile.ProviderSettings: Dictionary<string,string>` flexibel genug für Vector-Store-Konfiguration (z.B. CollectionName, TopK, EmbeddingModel)? Oder brauchen wir ein typsicheres Pattern mit Provider-spezifischen Profil-Records? Empfehlung: **Dictionary für jetzt, typsichere Records als Refactor wenn nötig**. Architect bestätigt.

2. **Query-Extraktion-Strategie:** Aktuell wird das volle Briefing als Tavily-Query verwendet. Das ist suboptimal — Briefings sind oft länger als Tavily's empfohlene Query-Länge. Optionen: (a) volle Briefing-Übergabe trotzdem (Tavily handhabt das vermutlich), (b) erste N Wörter, (c) eigene Query-Extraktions-LLM-Call (zusätzlicher Provider-Aufruf). **Empfehlung für Step 1:** Option (a), Tavily handhabt lange Queries. Option (c) als Folge-Step falls Real-Tests zeigen, dass Query-Qualität schlecht ist.

3. **Cost-Schema-Ausweitung:** `CostEur decimal NULL` auf `GroundingConsultations`. Folgende Steps werden `Costs` auf `IterationEntity` für LLM-Token-Costs ergänzen. **Empfehlung:** keine Vorab-Migration anderer Tabellen — Cost-Tracking-Step bringt eigene Migration. Foundation-Pattern wird hier nur etabliert.

4. **Failure-Verhalten:** Wenn Tavily-Call scheitert (Network, Quota erschöpft, 5xx), Run schlägt fehl analog zu Advisor-Failure aus PS-7. **Empfehlung bestätigt:** Hard-Fail, kein Soft-Fallback. Nutzer hat explizit Web-Search konfiguriert, stille Deaktivierung wäre Fehlverhalten.

## Reviewer-Hinweise

- **R1 (Functional Correctness):** Alle 19 ACs. Besonders 10 (Klassik-Regression), 15 (Real-Test), 17 (Foundation-Check), 19 (Deploy-Verifikation).
- **R2 (Code Quality):** Decorator-Pattern sauber, `IGroundingProvider`-Interface generisch genug für Vector-Store-Erweiterung.
- **R3 (Test Execution):** Mock-Tavily-Tests vollständig, KlassikRegressionTests deckt Verhaltens-Stability ab.
- **R4 (Architecture Compliance):** `IGroundingProvider` in Application-Layer (nicht Infrastructure!), konkrete Provider-Implementations in Infrastructure. Decorator-Pattern statt SDK-natives Grounding-Hook.
- **R5 (Live UI):** Real-Test auf Production mit echtem Tavily-Call, drei-Themes-Check der GroundingSection, Citation-Klick-Behavior.

## Persistenter Abschlussbericht

Bericht nach `docs/reports/feature-grounding-providers-tavily-report.md`. Inhalt:

1. **Was wurde umgesetzt** — Foundation, Tavily-Provider, UI, Cost-Foundation
2. **Architect-Output** — die vier Knackpunkte mit Entscheidungen
3. **Foundation-Check** — wie der `IGroundingProvider`-Vertrag Vector-Store-Erweiterung ohne Refactor ermöglicht
4. **Tavily-Integration-Beobachtungen** — Response-Qualität, Latenz, Token-Verbrauch im Real-Test
5. **Cost-Tracking-Foundation** — was hier gebaut wurde, was im Cost-Tracking-Step folgt
6. **Real-Test-Ergebnis** — Briefing, Citations, Executor-Output-Vergleich Klassik vs. mit-Recherche
7. **Akzeptanzkriterien-Check** — Tabelle mit allen 19 ACs
8. **Merge-Commit-Hash + Deploy-Timestamp**
9. **Tavily-Free-Tier-Verbrauch** — wie viele Credits beim Testen genutzt
10. **Empfehlungen** — Vector-Store-Step als nächstes? Query-Extraktion-Verbesserung? Reviewer-mit-Tools?

## Tavily-Account-Setup-Anleitung (im Bericht)

```
1. Besuche https://app.tavily.com/
2. "Sign up" — Email oder GitHub/Google-SSO
3. Nach Login: Dashboard öffnen
4. "API Keys" → neuen Key erstellen
5. Key kopieren (format: tvly-...)
6. In Atelier-.env: TAVILY_API_KEY=tvly-...
7. Container restart: docker compose restart web

Free-Tier:
- 1000 Searches/Monat
- Reicht für initiale Tests und mittlere Nutzung
- Bei Bedarf: Pay-as-you-go ($30 für 4000 zusätzliche Calls)
```

## Konventionen

- C#-Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- UI-Strings: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- **Niemals Secrets** in source control, Logs oder Bericht. **Insbesondere niemals `TAVILY_API_KEY`-Wert in Logs.** Der Key wird über `IOptions<TavilyOptions>` injiziert, nie im Klartext geloggt.
- Cost-Werte im Bericht: immer mit ~ Präfix bei Schätzungen, exakter Wert nur wenn aus DB gelesen.

Erwarteter Aufwand: 2-3 Arbeitstage inklusive Deploy und Real-Test.

---

**Nach erfolgreichem Abschluss:** Atelier kann Web-Recherche als Grounding-Pre-Processing durchführen. Custom-Templates mit Tavily-Provider produzieren faktisch fundiertere Texte, mit transparenten Citations und Cost-Anzeige. Foundation für Vector-Store-Step ist gelegt — der nächste Step kann `VectorStoreGroundingProvider` ohne Architekt-Refactor andocken. Cost-Tracking-Foundation für vollständiges Tracking ist etabliert.