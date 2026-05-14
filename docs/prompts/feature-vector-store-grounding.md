# Claude-Code-Prompt: Vector-Store-Grounding-Provider (Phase 2 RAG)

*Aktiviert PgVector + eigenes Wissens-Repository als zweiten Grounding-Provider. Phase 2 der RAG-Roadmap, baut auf der Foundation aus dem Tavily-Step auf. Document-Upload, Embedding-Pipeline (via OpenRouter), Vector-Search, Knowledge-Management-UI.*

---

## Mission

Du bist Senior .NET-Architekt am Projekt **Geef.Atelier**. Die `IGroundingProvider`-Foundation wurde im Tavily-Step etabliert mit explizitem Foundation-Check für genau diesen Folge-Step. `SourceCitation.DocumentReference` und `RelevanceScore` sind dort schon vorgesehen — Vector-Store-Quellen passen ohne Refactor in das bestehende Schema.

Dein Job ist die **Vector-Store-Aktivierung**: PgVector-Extension in Postgres, Document-Upload-Feature, Embedding-Pipeline (OpenRouter), `VectorStoreGroundingProvider`-Implementation, Knowledge-Management-UI unter `/crew/knowledge`. Domain-spezifisches Wissen wird damit als Grounding-Quelle verfügbar — Style-Guides, vergangene Texte, juristische Referenzen, etc.

Architekt-Auftrag besonders wichtig: **Postgres-Image-Migration** ist der riskanteste Teil. Sauberer Backup-Plan und Recovery-Strategie sind Pflicht.

## Vorgehen

**Du folgst dem Workflow in `/srv/docker/docs/geef-workflow.md`** vollständig. Vier Phasen, fünf Reviewer, Pflicht-Advisors, plus **Phase 5: Merge & Deploy** (verbindlich).

**Branch:** `feat/vector-store-grounding`. PR gegen `main` (Step enthält Migration, Postgres-Image-Wechsel, kein Direct-Push).

## Pflicht-Lektüre fürs Grounding (Phase 1.2)

1. **`/srv/docker/docs/geef-workflow.md`** und **`CLAUDE.md`**
2. **`docs/Vom_Prompt_zur_Pipeline.pdf`** — Grounding-Phase, RAG-Konzept
3. **`docs/02-architecture.md`** — Schichtenarchitektur
4. **`docs/05-decisions-log.md`** — alle bisherigen Entscheidungen, besonders D-035 zu Grounding-Foundation
5. **`docs/reports/feature-grounding-providers-tavily-report.md`** — die Foundation-Beschreibung, besonders Architect-Output zu Vector-Store-Generizität
6. **`docs/reports/post-skeleton-01-postgres-backup-report.md`** — wie Backup-Strategie aktuell funktioniert (PS-1)
7. **Aktueller Code:**
   - `src/Geef.Atelier.Application/Crew/Grounding/IGroundingProvider.cs` — die generische Foundation
   - `src/Geef.Atelier.Core/Domain/Crew/Grounding/SourceCitation.cs` — `DocumentReference` und `RelevanceScore` sind hier
   - `src/Geef.Atelier.Infrastructure/Grounding/TavilyGroundingProvider.cs` — Referenz-Implementierung für eigenen Provider
   - `src/Geef.Atelier.Infrastructure/Grounding/GroundingProviderFactory.cs` — Factory-Pattern
   - `src/Geef.Atelier.Infrastructure/Llm/OpenAiCompatibleClient.cs` — OpenRouter-Integration für Chat, kann für Embeddings ähnlich nachgebaut werden
   - `docker-compose.yml` — wo das Postgres-Image steht
   - `appsettings.json` — Konfigurationsmuster
8. **pgvector-Doku:**
   - `https://github.com/pgvector/pgvector` — Extension-Installation, Index-Typen (HNSW, IVFFlat)
   - Image: `pgvector/pgvector:pg16` (offizielles Image)
9. **OpenRouter-Embeddings-API:**
   - `https://openrouter.ai/docs/api/reference/embeddings` — vollständige Doku
   - Endpoint: `POST https://openrouter.ai/api/v1/embeddings` (OpenAI-kompatibel)
   - Models-Liste: `https://openrouter.ai/models?fmt=cards&output_modalities=embeddings`
   - Default-Modell: `openai/text-embedding-3-small` (1536 dim, ~$0.02/1M tokens)
   - Provider-Routing: `provider.allow_fallbacks: true` für automatischen Fallback

## Verbindliche Architektur-Entscheidungen

| Bereich | Entscheidung |
|---|---|
| **Postgres-Image** | Wechsel auf `pgvector/pgvector:pg16` (oder die zu Atelier passende Major-Version). Data bleibt erhalten dank Volume-Mount. |
| **Backup-Pflicht vor Image-Wechsel** | Vollständiges `pg_dump` vor Container-Restart. Datei-Pfad: `/backup/before-pgvector-migration-{timestamp}.dump`. Kein Skip, kein Override. |
| **Embedding-Provider** | **OpenRouter** (nicht OpenAI direkt). Wiederverwendung des bestehenden `LLM_OPENROUTER_API_KEY`. Endpoint `https://openrouter.ai/api/v1/embeddings`. Konsistente Provider-Architektur, einheitliche Cost-Buchhaltung. |
| **Embedding-Modell-Default** | `openai/text-embedding-3-small` (1536 Dimensionen). Über OpenRouter — selbe Modell-ID-Konvention wie für Chat (`<provider>/<model>`). |
| **Provider-Routing** | `allow_fallbacks: true` im Embedding-Request. OpenRouter routet automatisch bei Provider-Overload (529-Response). Latenz-Schutz für UX. |
| **Embedding-Modell-Wechsel-Strategie** | Globale Konfiguration in `appsettings.json`. Bei Wechsel: explizite Bulk-Re-Index-Operation nötig (UI-Knopf "Re-Index All Documents"). Atelier akzeptiert nur Docs mit aktuellem Modell — bei Mismatch: Warning + Re-Index-Anforderung. |
| **Vector-Index-Typ** | HNSW (Hierarchical Navigable Small World) für ANN-Suche. Bessere Query-Performance als IVFFlat, etwas mehr Index-Speicher — bei kleinen-mittleren Datenmengen (<100k Chunks) der bessere Tradeoff. |
| **Similarity-Metrik** | Cosine-Similarity (`vector_cosine_ops`). Standard für Text-Embeddings, normiert für Length-Independence. |
| **Chunking-Strategie** | Recursive Character Splitting analog zu LangChain. Max 1000 Tokens, Overlap 100 Tokens, Separatoren in Reihenfolge: `\n\n`, `\n`, `. `, ` `, `""`. Eigene Implementierung in Core (keine externe Library, weil Atelier `TreatWarningsAsErrors=true` ist und LangChain-NET-Bindings nicht stabil sind). |
| **Unterstützte Document-Formate (Phase 1)** | Markdown (`.md`), Plain Text (`.txt`). **PDF und andere Formate sind eigener Folge-Step** — reduziert Scope. |
| **Upload-Verarbeitung** | Synchron im Web-Request für initiale Implementation. Loading-State im UI. Falls Real-Tests zeigen, dass Embedding-Zeit > 30s für typische Docs: Background-Job-Refactor in Folge-Step. |
| **System-Vector-Store-Profile** | `knowledge-base-default` als Code-Konstante. Provider-Type: `vector-store`, Settings: `TopK=5`, ohne Tag-Filter (alle Docs). Read-only. |
| **Custom-Profile-Pattern** | Wie bisher: `custom-`-Prefix, mit Tag-Filter, individuelles TopK. |
| **Klassik-Default-Verhalten** | Klassik-Template bleibt **ohne** Grounding-Provider (auch nach diesem Step). Verhaltens-Regression-Schutz wie immer. |
| **Knowledge-Doc-Schema** | Zwei Tabellen: `KnowledgeDocuments` (Metadata + RawContent), `KnowledgeDocumentChunks` (Embeddings). Foreign-Key mit CASCADE-DELETE. |
| **Citation-Form für Vector-Store** | `Url: null`, `DocumentReference: "{DocumentId}/chunk-{ChunkIndex}"`, `RelevanceScore: cosine_similarity`. Kein Vermischen mit Web-URLs. |
| **EnrichedContext-Formatierung** | Pro Citation: Document-Title als Heading, Chunk-Content als Body. Klar getrennt von Tavily-Output durch Header `[Knowledge base context]` vs. `[Web research context]`. |
| **Tag-Filter-Logik** | Per Custom-Profile: `ProviderSettings["TagFilter"]` als JSON-Array (z.B. `["legal", "v2"]`). Match-Logik: **AND** (Doc muss alle Tags haben). OR-Logic als Folge-Step falls gewünscht. |
| **MCP-Erweiterung** | `list_knowledge_documents` als neues Tool. `submit_request` braucht keine API-Änderung (Vector-Store-Provider wird wie Tavily über `custom_crew.grounding_provider_profile_names` referenziert). |
| **Cost-Tracking** | Embedding-Costs pro Indexierung und pro Such-Query persistieren. Bei Indexierung: in `KnowledgeDocuments.IndexingCostEur`. Bei Such-Query: in `GroundingConsultations.CostEur` (Tabellen-Foundation aus Tavily-Step). OpenRouter liefert `usage.total_tokens` in der Response — Cost-Berechnung über aktuelle Pricing-Konstante. |
| **Migration** | `Step14VectorStore` (oder nächste freie Nummer). Enthält: `CREATE EXTENSION vector`, `KnowledgeDocuments`-Tabelle, `KnowledgeDocumentChunks`-Tabelle, HNSW-Index. |

## Konkrete Anforderungen

### 1. Postgres-Image-Migration

**`docker-compose.yml` anpassen:**

```yaml
postgres:
  image: pgvector/pgvector:pg16   # vorher: postgres:16-alpine
  # Rest unverändert
```

**Backup-Sequenz vor dem Wechsel** (kritisch — Phase 5 Deploy):

```bash
# 1. Vollständiges Backup
docker exec geef-atelier-postgres pg_dump -U geef_atelier -d geef_atelier \
  --format=custom --compress=9 \
  > /srv/docker/websites/geef_atelier/backup/before-pgvector-migration-$(date +%Y%m%d-%H%M%S).dump

# 2. Verifizieren Backup ist nicht-leer und gültig
ls -la /srv/docker/websites/geef_atelier/backup/before-pgvector-migration-*.dump
docker exec -i geef-atelier-postgres pg_restore --list \
  < /srv/docker/websites/geef_atelier/backup/before-pgvector-migration-*.dump | head -20

# 3. Postgres-Container stoppen
docker compose stop postgres

# 4. Image-Pull für neues Image
docker compose pull postgres

# 5. Container neu starten mit neuem Image, gleichem Volume
docker compose up -d postgres

# 6. Health-Check
sleep 5
docker compose ps postgres
docker exec geef-atelier-postgres psql -U geef_atelier -d geef_atelier -c \
  "SELECT version();"

# 7. Migration vom Web-Container ausführen lassen (auto beim Restart)
docker compose up -d web
sleep 10
docker exec geef-atelier-postgres psql -U geef_atelier -d geef_atelier -c \
  "SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';"
# Erwartet: vector | 0.x.x
```

**Rollback-Plan bei Image-Wechsel-Failure:**

```bash
# Container stoppen
docker compose stop postgres

# Image zurück
# docker-compose.yml editieren: image: postgres:16-alpine

# Volume bleibt — Daten bleiben erhalten weil Volume-Mount
docker compose up -d postgres

# Web-Container restart
docker compose restart web

# Falls Daten korrumpiert (sollte nicht passieren, aber):
docker compose stop postgres web
docker volume rm geef-atelier_postgres_data
docker compose up -d postgres
sleep 5
docker exec -i geef-atelier-postgres pg_restore -U geef_atelier -d geef_atelier \
  --create --clean \
  < /srv/docker/websites/geef_atelier/backup/before-pgvector-migration-*.dump
docker compose up -d web
```

Im Bericht das Backup-File-Pfad und das Verify-Ergebnis dokumentieren.

### 2. Domain-Layer (Core)

**`Geef.Atelier.Core/Domain/Crew/Knowledge/`** — neues Verzeichnis:

```csharp
public sealed record KnowledgeDocument(
    Guid Id,
    string Title,
    string Description,
    string OriginalFilename,
    string ContentType,           // "text/markdown" oder "text/plain"
    long FileSizeBytes,
    string RawContent,            // volltextlich gespeichert für Re-Indexing
    IReadOnlyList<string> Tags,
    string EmbeddingModel,        // z.B. "openai/text-embedding-3-small"
    int EmbeddingDimensions,
    int ChunkCount,
    decimal? IndexingCostEur,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record KnowledgeDocumentChunk(
    Guid Id,
    Guid DocumentId,
    int ChunkIndex,
    string Content,
    float[] Embedding,
    int TokenCount,
    DateTimeOffset CreatedAt
);

public sealed record VectorSearchResult(
    KnowledgeDocumentChunk Chunk,
    string DocumentTitle,
    double Similarity            // cosine, 0..1
);
```

**`Geef.Atelier.Core/Domain/Crew/Knowledge/Chunking/`** — Recursive Splitter:

```csharp
public sealed class RecursiveCharacterTextSplitter
{
    public RecursiveCharacterTextSplitter(int maxTokens = 1000, int overlapTokens = 100) { ... }

    public IReadOnlyList<TextChunk> Split(string text)
    {
        // Recursive Algorithmus:
        // 1. Versuche Split bei "\n\n" — wenn Chunks <= maxTokens, fertig
        // 2. Sonst bei "\n"
        // 3. Sonst bei ". "
        // 4. Sonst bei " "
        // 5. Sonst character-level
        // Overlap zwischen aufeinanderfolgenden Chunks
        // Token-Schätzung: ~4 chars/token (grob, gut genug für Splitting)
    }
}

public sealed record TextChunk(int Index, string Content, int EstimatedTokens);
```

**System-Vector-Store-Profile** in `SystemCrew.cs`:

```csharp
public static readonly GroundingProviderProfile KnowledgeBaseDefaultProfile = new(
    Name: "knowledge-base-default",
    DisplayName: "Knowledge Base (All Documents)",
    Description: "Sucht in der gesamten Wissensbasis nach Briefing-relevanten Passagen. Top 5 Treffer.",
    ProviderType: "vector-store",
    ProviderSettings: new() { ["TopK"] = "5" },
    MaxQueriesPerRun: 1,
    IsSystem: true
);
```

### 3. Application-Layer

**`IEmbeddingProvider`-Interface** in `Geef.Atelier.Application/Crew/Knowledge/`:

```csharp
public interface IEmbeddingProvider
{
    string ProviderName { get; }    // "openrouter"
    string ModelName { get; }       // "openai/text-embedding-3-small"
    int Dimensions { get; }         // 1536

    Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct);
    Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct);
}

public sealed record EmbeddingResult(float[] Vector, int TokenCount, decimal? CostEur);
```

**`IKnowledgeService`** für CRUD + Indexing:

```csharp
public interface IKnowledgeService
{
    Task<KnowledgeDocument> UploadAsync(
        string title,
        string description,
        IReadOnlyList<string> tags,
        Stream content,
        string filename,
        string contentType,
        CancellationToken ct);

    Task<KnowledgeDocument?> GetAsync(Guid documentId, CancellationToken ct);
    Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct);
    Task UpdateMetadataAsync(Guid documentId, string title, string description, IReadOnlyList<string> tags, CancellationToken ct);
    Task DeleteAsync(Guid documentId, CancellationToken ct);
    Task ReindexAsync(Guid documentId, CancellationToken ct);
    Task ReindexAllAsync(CancellationToken ct);  // für Embedding-Modell-Wechsel
}
```

**`IVectorSearchService`** für Such-Queries:

```csharp
public interface IVectorSearchService
{
    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        IReadOnlyList<string>? tagFilter,
        CancellationToken ct);
}
```

### 4. Infrastructure-Layer

**`OpenRouterEmbeddingProvider`** in `Geef.Atelier.Infrastructure/Embeddings/`:

```csharp
internal sealed class OpenRouterEmbeddingProvider : IEmbeddingProvider
{
    public OpenRouterEmbeddingProvider(
        HttpClient httpClient,                    // BaseAddress: https://openrouter.ai/api/v1
        IOptions<EmbeddingsOptions> options,
        IOptions<LlmOptions> llmOptions,          // für OpenRouter-API-Key wiederverwenden
        ILogger<OpenRouterEmbeddingProvider> logger) { ... }

    public string ProviderName => "openrouter";
    public string ModelName => _options.Model;
    public int Dimensions => _options.Dimensions;

    public async Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct)
    {
        // POST /embeddings
        // Headers: Authorization: Bearer <LLM_OPENROUTER_API_KEY>
        // Body: {
        //   "model": "openai/text-embedding-3-small",
        //   "input": "...",
        //   "encoding_format": "float",
        //   "provider": { "allow_fallbacks": true }
        // }
        // Response: { "data": [{ "embedding": [...] }], "usage": { "total_tokens": N } }
        
        // Cost-Berechnung: usage.total_tokens / 1_000_000 * options.CostPerMillionTokensUsd * UsdToEurRate
    }
    
    // CreateBatchAsync: bis zu 100 inputs pro Request, falls mehr: chunken
}
```

**Wichtiger Punkt:** Der `LLM_OPENROUTER_API_KEY` wird wiederverwendet. Im DI-Container existiert vermutlich schon ein konfigurierter HttpClient mit OpenRouter-Headers — falls möglich, denselben nutzen. Falls separater HttpClient sinnvoller (Embeddings nutzen anderen Endpoint-Pfad): neuer HttpClient registrieren, aber denselben API-Key aus `LlmOptions` ziehen.

**`VectorStoreGroundingProvider`** in `Geef.Atelier.Infrastructure/Grounding/`:

```csharp
internal sealed class VectorStoreGroundingProvider : IGroundingProvider
{
    public string ProviderType => "vector-store";

    public VectorStoreGroundingProvider(
        IServiceScopeFactory scopeFactory,    // Captive-Dep-Fix wie bei Tavily
        IEmbeddingProvider embeddings,
        ILogger<VectorStoreGroundingProvider> logger) { ... }

    public async Task<GroundingResult> EnrichAsync(
        string briefingText, GroundingProviderProfile profile, Guid runId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var searchService = scope.ServiceProvider.GetRequiredService<IVectorSearchService>();
        var consultations = scope.ServiceProvider.GetRequiredService<IGroundingConsultationRepository>();

        // 1. Briefing-Text → Embedding-Query
        var queryEmbedding = await _embeddings.CreateAsync(briefingText, ct);

        // 2. Provider-Settings auslesen
        var topK = int.Parse(profile.ProviderSettings.GetValueOrDefault("TopK", "5"));
        var tagFilter = ParseTagFilter(profile.ProviderSettings.GetValueOrDefault("TagFilter"));

        // 3. Vector-Search
        var results = await searchService.SearchAsync(queryEmbedding.Vector, topK, tagFilter, ct);

        // 4. Citations bauen
        var citations = results.Select(r => new SourceCitation(
            Title: r.DocumentTitle,
            Url: null,
            Snippet: r.Chunk.Content.Truncate(200),
            DocumentReference: $"{r.Chunk.DocumentId}/chunk-{r.Chunk.ChunkIndex}",
            RelevanceScore: r.Similarity
        )).ToList();

        // 5. EnrichedContext formatieren
        var enrichedContext = FormatEnrichedContext(results);

        // 6. GroundingConsultation persistieren mit Cost (Query-Embedding + reine Search hat keine zusätzlichen Costs)
        // 7. Return GroundingResult
    }
}
```

**`KnowledgeDocumentRepository`** und **`VectorSearchRepository`** als EF-Implementierungen.

**`DocumentIndexingService`** für die Upload→Chunk→Embed→Persist-Pipeline:

```csharp
internal sealed class DocumentIndexingService
{
    public async Task<KnowledgeDocument> IndexAsync(
        string title, string description, IReadOnlyList<string> tags,
        string content, string contentType, string filename,
        CancellationToken ct)
    {
        // 1. Document-Record anlegen (ohne Chunks)
        // 2. Chunking: Splitter.Split(content)
        // 3. Batch-Embeddings: Embeddings.CreateBatchAsync(chunks)
        // 4. Chunk-Records persistieren mit Embeddings
        // 5. Document.ChunkCount + IndexingCostEur updaten (sum aller batch costs)
        // 6. Return saved Document
    }
}
```

### 5. Vector-Search via PgVector

EF-Core-Migration-File enthält:

```sql
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE "KnowledgeDocuments" (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "Title" text NOT NULL,
    "Description" text NOT NULL DEFAULT '',
    "OriginalFilename" text NOT NULL,
    "ContentType" text NOT NULL,
    "FileSizeBytes" bigint NOT NULL,
    "RawContent" text NOT NULL,
    "Tags" text[] NOT NULL DEFAULT '{}',
    "EmbeddingModel" text NOT NULL,
    "EmbeddingDimensions" integer NOT NULL,
    "ChunkCount" integer NOT NULL DEFAULT 0,
    "IndexingCostEur" decimal(10,4) NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT now(),
    "UpdatedAt" timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE "KnowledgeDocumentChunks" (
    "Id" uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    "DocumentId" uuid NOT NULL REFERENCES "KnowledgeDocuments"("Id") ON DELETE CASCADE,
    "ChunkIndex" integer NOT NULL,
    "Content" text NOT NULL,
    "Embedding" vector(1536) NOT NULL,
    "TokenCount" integer NOT NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX "IX_KnowledgeDocumentChunks_DocumentId" 
    ON "KnowledgeDocumentChunks"("DocumentId");

CREATE INDEX "IX_KnowledgeDocumentChunks_Embedding_HNSW" 
    ON "KnowledgeDocumentChunks" 
    USING hnsw ("Embedding" vector_cosine_ops);

CREATE INDEX "IX_KnowledgeDocuments_Tags" 
    ON "KnowledgeDocuments" USING gin("Tags");
```

**EF-Core-pgvector-Integration:** Atelier nutzt Npgsql; das Package `Pgvector.EntityFrameworkCore` muss als NuGet-Dependency aufgenommen werden (offizielles Package). Property-Type: `Pgvector.Vector` für `Embedding`-Feld.

**Vector-Search-Query** (EF-Core mit pgvector):

```csharp
public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
    float[] queryEmbedding, int topK, IReadOnlyList<string>? tagFilter, CancellationToken ct)
{
    var queryVector = new Pgvector.Vector(queryEmbedding);
    
    var query = _context.KnowledgeDocumentChunks
        .Include(c => c.Document)
        .AsQueryable();
    
    if (tagFilter is { Count: > 0 })
    {
        query = query.Where(c => tagFilter.All(t => c.Document.Tags.Contains(t)));
    }
    
    var results = await query
        .OrderBy(c => c.Embedding.CosineDistance(queryVector))
        .Take(topK)
        .Select(c => new {
            Chunk = c,
            DocumentTitle = c.Document.Title,
            Distance = c.Embedding.CosineDistance(queryVector)
        })
        .ToListAsync(ct);
    
    return results.Select(r => new VectorSearchResult(
        r.Chunk, r.DocumentTitle, 1.0 - r.Distance  // similarity = 1 - cosine_distance
    )).ToList();
}
```

### 6. Konfiguration

**`appsettings.json` ergänzen:**

```json
{
  "Embeddings": {
    "Provider": "openrouter",
    "Model": "openai/text-embedding-3-small",
    "Dimensions": 1536,
    "Endpoint": "https://openrouter.ai/api/v1",
    "CostPerMillionTokensUsd": 0.02,
    "UsdToEurRate": 0.92,
    "BatchSize": 100,
    "AllowFallbacks": true,
    "RequestTimeoutSeconds": 30
  },
  "Knowledge": {
    "MaxDocumentSizeBytes": 5242880,
    "AllowedContentTypes": ["text/markdown", "text/plain"]
  }
}
```

**`.env.example` Änderung:** Keine neue Env-Var nötig! `LLM_OPENROUTER_API_KEY` wird wiederverwendet. Comment-Update reicht:

```
# OpenRouter API Key — wird sowohl für LLM-Calls als auch für Embeddings genutzt
LLM_OPENROUTER_API_KEY=
```

**`Program.cs` Service-Registrierung:**

```csharp
builder.Services.Configure<EmbeddingsOptions>(builder.Configuration.GetSection("Embeddings"));
builder.Services.Configure<KnowledgeOptions>(builder.Configuration.GetSection("Knowledge"));

builder.Services.AddHttpClient<OpenRouterEmbeddingProvider>(client => {
    client.BaseAddress = new Uri("https://openrouter.ai/api/v1/");
    // Auth-Header wird im Provider gesetzt aus LlmOptions
});
builder.Services.AddSingleton<IEmbeddingProvider, OpenRouterEmbeddingProvider>();

builder.Services.AddScoped<IVectorSearchService, VectorSearchRepository>();
builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();
builder.Services.AddScoped<DocumentIndexingService>();

// Vector-Store Provider in der Factory registrieren
// (analog zu Tavily-Registrierung)
```

### 7. UI-Layer — Knowledge-Management

**Neue Pages unter `/crew/knowledge/`:**

`KnowledgeIndex.razor`:
- Liste aller Dokumente: Title, Tags, ChunkCount, IndexingCost, CreatedAt
- Such-/Filter-Bar (nach Tag)
- "Upload"-Knopf prominent
- Pro Doc: Quick-Actions (Edit, Re-Index, Delete)
- Empty-State mit Hinweis "No documents yet. Upload your first knowledge document to get started."

`KnowledgeUpload.razor`:
- File-Upload-Field (drag-drop fähig)
- Title, Description, Tags-Input
- Upload-Button mit Loading-State
- Bei Success: Redirect zu Document-Detail
- Bei Failure: klare Error-Message (File too big, unsupported format, embedding API error)

`KnowledgeDocumentDetail.razor`:
- Metadata: Title, Description, Tags, Filesize, CreatedAt, EmbeddingModel
- Cost-Info: IndexingCost in EUR
- Chunks-Vorschau: erste 3 Chunks mit Content-Snippets
- Buttons: Edit Metadata, Re-Index, Delete (mit Confirmation-Modal)

**Components:**
- `KnowledgeDocumentCard.razor` — Card-Layout für Listen
- `KnowledgeUploadForm.razor` — wiederverwendbares Upload-Form
- `TagInput.razor` — Komma-separierte Tag-Eingabe mit Auto-Complete (aus existierenden Tags)
- `IndexingStatusBadge.razor` — Status-Anzeige

**`GroundingProviderEditor.razor` erweitern:**

ProviderType-Dropdown bekommt zusätzlich "Vector Store" Option. Wenn ausgewählt:
- TopK-Eingabe (default 5)
- Tag-Filter-Input (Komma-separierte Tags, AND-Match)
- Read-only-Anzeige des aktiven Embedding-Modells

**`CrewSummary.razor` erweitern** — wenn Vector-Store-Provider im Snapshot: zusätzliche Sektion zeigt "Knowledge Base (TopK=5)" oder "Knowledge Base (Tags: legal, v2, TopK=10)".

**`GroundingSection.razor`** in RunDetail erweitern — Citations von Vector-Store-Quellen werden anders dargestellt als Web-Sources:
- Kein URL-Link, stattdessen Doc-Title als interner Link zu `/crew/knowledge/{documentId}`
- Klares "From your knowledge base"-Label
- DocumentReference (z.B. "chunk-3") als kleine Metadata-Zeile

### 8. MCP-Layer

**Neues Tool `list_knowledge_documents`** analog zu bestehenden List-Tools:

```csharp
public sealed record KnowledgeDocumentDto(
    Guid Id,
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    long FileSizeBytes,
    int ChunkCount,
    string EmbeddingModel,
    DateTimeOffset CreatedAt
);
```

`submit_request` braucht keine Änderung — Vector-Store-Provider wird via `custom_crew.grounding_provider_profile_names` referenziert.

### 9. Tests

**Core-Tests:**
- `KnowledgeDocumentEqualityTests`, `KnowledgeDocumentChunkEqualityTests`
- `RecursiveCharacterTextSplitterTests` — verschiedene Test-Texte, Edge-Cases (leerer String, sehr kurzer Text, sehr langer Text, Markdown-Headers, Code-Blocks)
- `SystemCrewKnowledgeBaseDefaultProfileTests`

**Infrastructure-Tests:**
- `OpenRouterEmbeddingProviderTests` (Mock-HttpClient) — Single + Batch, Cost-Berechnung, Error-Handling, allow_fallbacks-Header korrekt
- `KnowledgeDocumentRepositoryTests` (mit Test-DB) — CRUD-Operationen
- `VectorSearchRepositoryTests` (mit Test-DB + pgvector!) — Search, Tag-Filter, TopK
- `VectorStoreGroundingProviderTests` (Mock-Dependencies)
- `DocumentIndexingServiceTests` — End-to-End mit Mock-Embeddings

**Application-Tests:**
- `KnowledgeServiceTests` — Upload, Update, Delete, Re-Index
- `VectorSearchServiceTests` — Cosine-Distance-Logic, Tag-Filter-Logic

**Pipeline-Tests:**
- `AtelierPipelineFactoryWithVectorStoreTests` — Mock-VectorStoreProvider, vollständiger Run
- `KlassikRegressionTests` — Klassik ohne Knowledge-Base-Call

**MCP-Tests:**
- `ListKnowledgeDocumentsToolTests`

**UI-Tests (bUnit):**
- `KnowledgeIndexTests`
- `KnowledgeUploadTests`
- `KnowledgeDocumentDetailTests`
- `VectorStoreProviderEditorTests`
- `GroundingSectionWithVectorStoreCitationsTests`

**Pgvector-Integration-Tests:**

Tests die echte pgvector-Funktionalität nutzen brauchen pgvector im Test-Container. Architect entscheidet:
- (a) `Testcontainers.PostgreSql` mit `pgvector/pgvector:pg16` Image
- (b) Mock-Layer der pgvector-spezifische Calls abfängt

**Empfehlung: (a)** — Real-pgvector im Test gibt höhere Confidence. Test-Setup mehr Aufwand, aber lohnt sich.

**Bestehende 304+ Tests müssen grün bleiben.**

### 10. Real-Live-Test (Pflicht-AC)

Auf Production nach Deploy:

1. **OpenRouter-API-Key bereits vorhanden** — wird wiederverwendet, kein neuer Account nötig
2. **Test-Markdown-Doc erstellen** (z.B. fiktive Brand-Guidelines, ~2000 Wörter):
   ```markdown
   # Brand Guidelines v2.0
   
   ## Voice and Tone
   Our brand voice is...
   
   ## Color Palette
   Primary: ...
   ```
3. **Upload via UI:** `/crew/knowledge/upload`
   - Title: "Brand Guidelines v2"
   - Tags: ["brand", "v2", "marketing"]
   - File: brand-guidelines-v2.md
4. **Verifizieren:** Document-Detail zeigt Chunks, IndexingCost (~€0.001 für 2000 Wörter)
5. **Custom-Template anlegen:** "Marketing-Brief mit Brand-Knowledge"
   - Klassik-Crew + `knowledge-base-default` als Grounding-Provider
   - **Oder:** Custom-Profile mit Tag-Filter `["brand"]`
6. **Test-Briefing einreichen:** z.B. "Schreibe eine Produkt-Beschreibung für unseren neuen Service. Stelle sicher, dass die Markenrichtlinien eingehalten werden."
7. **RunDetail beobachten:**
   - Grounding-Sektion zeigt Vector-Store-Citations
   - Citations verweisen auf Brand-Guidelines-Chunks mit Similarity-Scores
   - Executor-Output reflektiert die Guidelines

8. **Cost-Anzeige:** Run-Header zeigt Embedding-Costs (~€0.001 für die Such-Query)

9. **Vergleichs-Test:** dasselbe Briefing ohne Vector-Store-Provider → Executor erfindet vermutlich generische Brand-Eigenschaften statt die spezifischen aus der Wissensbasis zu nutzen

10. **Beobachtungen im Bericht dokumentieren:** Welche Chunks wurden gefunden? War der Recall sinnvoll? Wie hat der Executor die Guidelines integriert?

## Akzeptanzkriterien (verbindlich)

1. `dotnet build` 0 Errors, 0 Warnings.
2. `dotnet test` — alle bestehenden Tests grün + neue Tests.
3. **Postgres-Image-Migration erfolgreich:** Backup vorhanden, neuer Container läuft, `vector`-Extension installiert.
4. **DB-Migration `Step14VectorStore`** läuft sauber. Tabellen `KnowledgeDocuments`, `KnowledgeDocumentChunks` mit HNSW-Index.
5. **System-Profile `knowledge-base-default` verfügbar.**
6. **Document-Upload via UI funktional** — MD und TXT, mit Chunking + Embedding + Persistierung.
7. **Vector-Search funktioniert** — Cosine-Similarity, TopK, Tag-Filter.
8. **`VectorStoreGroundingProvider` end-to-end** im Pipeline-Lauf integriert.
9. **GroundingSection zeigt Vector-Store-Citations** mit Doc-Title, Chunk-Snippet, Similarity-Score, internem Link.
10. **Knowledge-Management-UI** unter `/crew/knowledge`: Index, Upload, Detail, Delete, Re-Index funktional.
11. **Re-Index-Operation funktional** — sowohl per-Doc als auch Bulk.
12. **Klassik-Regression:** Klassik-Run nutzt keinen Vector-Store, keine Embedding-Costs.
13. **Tag-Filter funktional** — Custom-Profile mit Tag-Filter sucht nur in gematchten Docs.
14. **OpenRouter-Key wiederverwendet** — keine neue API-Key-Konfiguration nötig. `provider.allow_fallbacks=true` im Embedding-Request.
15. **Foundation-Check:** Vector-Store nutzt dieselbe `IGroundingProvider`-Abstraktion wie Tavily, kein Refactor an der Foundation.
16. **MCP-Tool `list_knowledge_documents`** funktional.
17. **Cost-Tracking pro Indexing und Search** persistiert und sichtbar.
18. **Real-Test auf Production** durchgeführt mit Brand-Guidelines-Beispiel (oder Stefans Wahl).
19. **Decisions-Log-Eintrag** (D-036 oder nächste freie Nummer) mit Architect-Entscheidungen.
20. **Merge auf `main`** durchgeführt (PR gemerged).
21. **Production-Deploy vollständig:** Postgres-Image gewechselt, Web-Container neu gebaut, Live-Test erfolgreich.

## Phase 5 — Merge & Deploy (verbindlich)

Direkt nach Phase 4 (Bericht). Claude Code führt das auf dem Hetzner-Server selbst aus:

```bash
cd /srv/docker/websites/geef_atelier

# 1. PR mergen
gh pr merge <PR-Number> --merge --delete-branch

# 2. Latest main pullen
git checkout main
git pull --ff-only

# 3. Backup (kritisch vor Postgres-Image-Wechsel)
mkdir -p backup
docker exec geef-atelier-postgres pg_dump -U geef_atelier -d geef_atelier \
  --format=custom --compress=9 \
  > backup/before-pgvector-migration-$(date +%Y%m%d-%H%M%S).dump

ls -la backup/before-pgvector-migration-*.dump
# Verify size > 0, file exists

# 4. Sanity-Check: OpenRouter-Key bereits konfiguriert?
grep LLM_OPENROUTER_API_KEY .env | head -1
# Erwartet: LLM_OPENROUTER_API_KEY=sk-or-... (nicht leer)

# 5. Postgres-Image-Pull und Container-Restart
docker compose pull postgres
docker compose stop postgres
docker compose up -d postgres

sleep 10

# 6. Extension-Verify
docker exec geef-atelier-postgres psql -U geef_atelier -d geef_atelier -c \
  "SELECT version();"
docker exec geef-atelier-postgres psql -U geef_atelier -d geef_atelier -c \
  "SELECT extname, extversion FROM pg_extension;"

# 7. Web-Container neu bauen und starten
docker compose build --no-cache web
docker compose up -d web

sleep 15

# 8. Migration verifizieren
docker exec geef-atelier-postgres psql -U geef_atelier -d geef_atelier -c \
  "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1 DESC LIMIT 3;"
# Step14VectorStore sollte sichtbar sein

docker exec geef-atelier-postgres psql -U geef_atelier -d geef_atelier -c \
  "SELECT extname, extversion FROM pg_extension WHERE extname = 'vector';"
# vector | 0.x.x

# 9. Health-Check
docker compose ps
curl -I https://geef.stefan-bechtel.de/

# 10. Smoke-Test im Browser:
# - /crew/knowledge → leere Liste mit Upload-Button
# - Test-Markdown-Doc hochladen
# - Document-Detail zeigt Chunks
# - Custom-Template mit knowledge-base-default
# - Test-Run einreichen
# - Grounding-Sektion zeigt Vector-Store-Citations
```

**Bei Issues während Deploy:**
- Postgres-Image-Probleme → Rollback-Plan (siehe Sektion 1 oben)
- Migration-Failure → Logs inspizieren, evtl. Manual-Fix in DB
- OpenRouter-Embeddings-API-Probleme → Key-Check, Quota-Check, allow_fallbacks aktivieren
- Klare Folge-Step-Notiz im Bericht falls partielle Probleme

## Was du in diesem Step NICHT tust

- **Kein PDF-Support** — eigener Folge-Step ("Document-Format-Extension"). MD/TXT reichen für Phase 1.
- **Kein Background-Job für Indexing** — synchron im Web-Request. Refactor falls Real-Tests Performance-Probleme zeigen.
- **Keine Multi-Modal-Embeddings** — text-only (auch wenn OpenRouter Image-Embeddings unterstützt — separater Step).
- **Keine OR-Tag-Filter-Logic** — nur AND. OR als Folge-Step.
- **Keine Hybrid-Search** (Vector + Keyword) — pure Vector-Search reicht für Phase 1.
- **Keine Re-Ranking** mit Cross-Encoder — Vanilla Cosine-Similarity reicht für Phase 1.
- **Keine Embedding-Modell-Wechsel-UI** — Konfiguration in `appsettings.json`. Bulk-Re-Index-Knopf reicht.
- **Keine Document-Versionierung** — Upload überschreibt nicht, jedes Upload erstellt neues Doc. Manual-Delete von Old-Version.
- **Keine OpenAI-direkt-Integration** — alles über OpenRouter (Architektur-Entscheidung).

## Architect-Konsultation (Phase 1.4) — fünf Knackpunkte

1. **Postgres-Image-Wechsel-Strategie:** offizielles `pgvector/pgvector:pg16` vs. Custom-Dockerfile mit pgvector-Build. Empfehlung: **offizielles Image**, weniger Maintenance-Aufwand. Architect verifiziert dass das Image alle Atelier-Postgres-Anforderungen erfüllt (Encoding, Locale, Extensions wie `gen_random_uuid`).

2. **Embedding-Modell-Default:** `openai/text-embedding-3-small` vs. `openai/text-embedding-3-large` vs. Alternativen (z.B. `qwen/qwen3-embedding-0.6b` für Open-Source-Pfad). Empfehlung: **`openai/text-embedding-3-small`** als Default — bewährt, günstig, gute Performance. Architect kann alternative Modelle vergleichen und im Bericht Trade-offs dokumentieren.

3. **Chunking-Strategie:** Recursive-Splitter selbstgebaut vs. Library (Microsoft Semantic Kernel hat eigene Chunker). Empfehlung: **selbstgebaut** — eine kleine, getestete Klasse statt ungeprüfte Library-Dep. Logik ist nicht komplex.

4. **Pgvector-EF-Integration:** `Pgvector.EntityFrameworkCore` Package (offiziell, aber relativ jung) vs. Raw-SQL für Vector-Operationen. Empfehlung: **Package**, type-safety wert. Architect prüft Package-Aktualität und Atelier-Kompatibilität.

5. **HttpClient-Sharing mit LLM-OpenRouter-Client:** Wiederverwendung des existierenden konfigurierten HttpClient vs. eigener HttpClient für Embeddings-Endpoint. Empfehlung: **eigener HttpClient** (cleaner, sauberer Scope), aber selber API-Key aus `LlmOptions`. Architect bestätigt nach Code-Inspektion.

## Reviewer-Hinweise

- **R1 (Functional Correctness):** Alle 21 ACs. Besonders 3 (Postgres-Migration), 12 (Klassik-Regression), 15 (Foundation-Check), 18 (Real-Test), 21 (Deploy).
- **R2 (Code Quality):** Decorator-Pattern wie bei Tavily, `IServiceScopeFactory`-Pattern für Captive-Dep-Avoidance, sauber gekapseltes Chunking.
- **R3 (Test Execution):** pgvector-Integration-Tests mit Testcontainers, KlassikRegression-Test verifiziert Verhaltens-Stability.
- **R4 (Architecture Compliance):** `IEmbeddingProvider` in Application-Layer, konkrete Provider in Infrastructure. Vector-Search-Service auch im richtigen Layer.
- **R5 (Live UI):** Real-Test auf Production, drei-Themes-Check der Knowledge-UI, Citation-Klick-Behavior, Cost-Anzeige.

## Persistenter Abschlussbericht

Bericht nach `docs/reports/feature-vector-store-grounding-report.md`. Inhalt:

1. **Was wurde umgesetzt** — Postgres-Migration, Domain, Embedding (OpenRouter), Vector-Search, UI
2. **Postgres-Migration-Details** — Backup-Pfad, Verify-Output, Pre/Post-Container-Status
3. **Architect-Output** — alle fünf Knackpunkte mit Entscheidungen
4. **Foundation-Check** — wie der `IGroundingProvider`-Vertrag aus Tavily-Step wiederverwendet wurde, ohne Refactor
5. **Embedding-Integration-Beobachtungen** — Latenz via OpenRouter, Token-Verbrauch, Batch-Verhalten, Provider-Fallback aktiviert?
6. **Vector-Search-Performance** — Latenz pro Query, Index-Größe nach Test-Doc-Upload
7. **Real-Test-Ergebnis** — Brand-Guidelines-Beispiel (oder Stefans Wahl), Citations, Executor-Output-Vergleich mit/ohne Vector-Store
8. **Cost-Berichte** — Indexing-Cost für Test-Doc, Search-Cost pro Query (via OpenRouter)
9. **Akzeptanzkriterien-Check** — Tabelle mit allen 21 ACs
10. **Merge-Commit-Hash + Deploy-Timestamp**
11. **OpenRouter-Quota-Verbrauch beim Testen** (rough)
12. **Empfehlungen** — PDF-Support? Background-Indexing? Multi-Modal? Hybrid-Search? Alternative Embedding-Modelle?

## OpenRouter-Embedding-Modell-Auswahl (im Bericht)

Modell-Optionen die OpenRouter aktuell für Embeddings anbietet (Architect verifiziert in Phase 1.2 via `https://openrouter.ai/models?fmt=cards&output_modalities=embeddings`):

| Modell | Dimensionen | ~Cost / 1M tokens | Use-Case |
|---|---|---|---|
| `openai/text-embedding-3-small` | 1536 | $0.02 | **Default**: günstig, gute Qualität |
| `openai/text-embedding-3-large` | 3072 | $0.13 | Bei Quality-Issues mit small |
| `qwen/qwen3-embedding-0.6b` | 1024 | ggf. günstiger | Open-Source-Pfad |
| ... | ... | ... | weitere Optionen |

Architect prüft das aktuelle Angebot und dokumentiert die finale Wahl mit Begründung.

## Konventionen

- C#-Code, Code-Kommentare, XML-Doc-Comments: **Englisch**.
- UI-Strings: **Englisch**.
- Doku, Bericht, Commits: **Deutsch**.
- Commits: fein-granulare Conventional Commits.
- **Niemals Secrets** in source control, Logs oder Bericht.
- Embedding-Vektoren in Tests: kurze Test-Vektoren (~10 dim) für Lesbarkeit; Production-Code arbeitet mit 1536 dim.
- Cost-Werte mit `~` Präfix bei Schätzungen.

Erwarteter Aufwand: 3-4 Arbeitstage inklusive Postgres-Migration, Real-Test und Deploy.

---

**Nach erfolgreichem Abschluss:** Atelier hat eigene Wissensbasis. Domain-Templates können jetzt sinnvoll konfiguriert werden mit Vector-Store + Tavily kombiniert (Knowledge-Base zuerst, Web-Search ergänzend). Foundation für Reviewer-mit-Tools (Faktencheck) und Domain-Templates ist komplett. RAG-Roadmap erreicht Phase-2-Vollendung. **Single Provider** für Chat, Embeddings, und (zukünftig) weitere Modalitäten — saubere, wartbare Architektur.