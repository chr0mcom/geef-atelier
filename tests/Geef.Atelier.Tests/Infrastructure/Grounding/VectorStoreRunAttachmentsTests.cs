using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Grounding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

/// <summary>
/// The legacy run-attachments path (vector store, run-local scope): no embedding call for runs without
/// attachments, and a resumed run still finds the attachments of the run it was resumed from.
/// </summary>
public sealed class VectorStoreRunAttachmentsTests
{
    [Fact]
    public async Task RunWithoutAttachmentsReturnsEmptyWithoutAnEmbeddingCall()
    {
        var runId = Guid.NewGuid();
        var embeddings = new CountingEmbeddingProvider();
        var search = new PerRunSearchRepository(new Dictionary<Guid, double>());
        var provider = Build(embeddings, search, new Documents([]), new Runs([Run(runId)]));

        var result = await provider.EnrichAsync("Summarise the attached file.", SystemCrew.RunAttachmentsProfile, runId, CancellationToken.None);

        Assert.Empty(result.EnrichedContext);
        Assert.Equal(0, embeddings.Calls);
        Assert.Empty(search.SearchedRuns);
    }

    [Fact]
    public async Task RunWithAttachmentsSearchesOnlyItsOwnDocuments()
    {
        var runId = Guid.NewGuid();
        var embeddings = new CountingEmbeddingProvider();
        var search = new PerRunSearchRepository(new Dictionary<Guid, double> { [runId] = 0.9 });
        var provider = Build(embeddings, search, new Documents([runId]), new Runs([Run(runId)]));

        var result = await provider.EnrichAsync("Summarise the attached file.", SystemCrew.RunAttachmentsProfile, runId, CancellationToken.None);

        Assert.Contains($"chunk of {runId}", result.EnrichedContext);
        Assert.Equal(1, embeddings.Calls);
        Assert.Equal([runId], search.SearchedRuns);
    }

    [Fact]
    public async Task ResumedRunFindsTheAttachmentsOfItsParent()
    {
        var parent = Guid.NewGuid();
        var resumed = Guid.NewGuid();
        var search = new PerRunSearchRepository(new Dictionary<Guid, double> { [parent] = 0.8 });
        var provider = Build(new CountingEmbeddingProvider(), search, new Documents([parent]),
            new Runs([Run(parent), Run(resumed, parentRunId: parent)]));

        var result = await provider.EnrichAsync("Revise the attached text.", SystemCrew.RunAttachmentsProfile, resumed, CancellationToken.None);

        Assert.Contains($"chunk of {parent}", result.EnrichedContext);
        Assert.Equal([parent], search.SearchedRuns);
    }

    [Fact]
    public async Task ResultsOfSeveralRunsAreMergedByRelevanceAndCutToTopK()
    {
        var parent = Guid.NewGuid();
        var resumed = Guid.NewGuid();
        var search = new PerRunSearchRepository(new Dictionary<Guid, double> { [parent] = 0.5, [resumed] = 0.9 }, perRun: 4);
        var provider = Build(new CountingEmbeddingProvider(), search, new Documents([parent, resumed]),
            new Runs([Run(parent), Run(resumed, parentRunId: parent)]));

        var result = await provider.EnrichAsync("b", SystemCrew.RunAttachmentsProfile, resumed, CancellationToken.None);

        Assert.Equal(5, result.Citations.Count);
        Assert.Equal(4, result.Citations.Count(c => c.RelevanceScore >= 0.9));
    }

    private static VectorStoreGroundingProvider Build(
        IEmbeddingProvider embeddings, IVectorSearchRepository search, IKnowledgeDocumentRepository documents, IRunRepository runs)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => search);
        services.AddScoped(_ => documents);
        services.AddScoped(_ => runs);
        services.AddScoped<IRunAttachmentLineage, Geef.Atelier.Infrastructure.Knowledge.RunAttachmentLineage>();
        services.AddScoped<IGroundingConsultationRepository, NullConsultations>();
        return new VectorStoreGroundingProvider(
            embeddings,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VectorStoreGroundingProvider>.Instance);
    }

    private static RunEntity Run(Guid id, Guid? parentRunId = null) => new()
    {
        Id = id,
        CreatedAt = DateTimeOffset.UtcNow,
        Status = RunStatus.Running,
        BriefingText = "b",
        ConfigJson = "{}",
        ParentRunId = parentRunId,
    };

    private sealed class CountingEmbeddingProvider : IEmbeddingProvider
    {
        public int Calls { get; private set; }
        public string ProviderName => "stub";
        public string ModelName => "stub";
        public int Dimensions => 3;

        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new EmbeddingResult([0.1f, 0.2f, 0.3f], 5, null));
        }

        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(IReadOnlyList<string> texts, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class PerRunSearchRepository(IReadOnlyDictionary<Guid, double> similarityByRun, int perRun = 1) : IVectorSearchRepository
    {
        public List<Guid> SearchedRuns { get; } = [];

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryEmbedding, int topK, IReadOnlyList<string>? tagFilter, KnowledgeScope? scopeFilter, Guid? runIdFilter, CancellationToken ct)
        {
            SearchedRuns.Add(runIdFilter!.Value);
            IReadOnlyList<VectorSearchResult> results = similarityByRun.TryGetValue(runIdFilter.Value, out var similarity)
                ? Enumerable.Range(0, perRun)
                    .Select(i => new VectorSearchResult(
                        new KnowledgeDocumentChunk(Guid.NewGuid(), Guid.NewGuid(), i, $"chunk of {runIdFilter}", [], 1, DateTimeOffset.UtcNow),
                        "doc", similarity))
                    .ToList()
                : [];
            return Task.FromResult(results);
        }

        public Task<KnowledgeDocumentChunk> CreateChunkAsync(KnowledgeDocumentChunk chunk, CancellationToken ct) => Task.FromResult(chunk);

        public Task DeleteChunksForDocumentAsync(Guid documentId, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Documents(IReadOnlyList<Guid> runsWithAttachments) : IKnowledgeDocumentRepository
    {
        public Task<IReadOnlyList<KnowledgeDocument>> ListByRunAsync(Guid runId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KnowledgeDocument>>(runsWithAttachments.Contains(runId)
                ? [new KnowledgeDocument(Guid.NewGuid(), "doc", "", "doc.md", "text/markdown", 1, "x", [], "m", 3, 1, null,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, KnowledgeScope.RunLocal, runId)]
                : []);

        public Task<KnowledgeDocument> CreateAsync(KnowledgeDocument document, CancellationToken ct) => throw new NotSupportedException();
        public Task<KnowledgeDocument?> GetAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<KnowledgeDocument>> ListAsync(string? tagFilter, CancellationToken ct, KnowledgeScope? scope = null) => throw new NotSupportedException();
        public Task UpdateAsync(KnowledgeDocument document, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetAllTagsAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Runs(IReadOnlyList<RunEntity> runs) : IRunRepository
    {
        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default) =>
            Task.FromResult(runs.FirstOrDefault(r => r.Id == runId));

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class NullConsultations : IGroundingConsultationRepository
    {
        public Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct) => Task.FromResult(consultation);
        public Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct) => Task.FromResult<IReadOnlyList<GroundingConsultation>>([]);
        public Task UpdateRefinementOutcomeAsync(Guid consultationId, RefinementOutcome outcome, CancellationToken ct) => Task.CompletedTask;
    }
}
