using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Regression tests that simulate a Klassik run after <c>RunService.SubmitRunAsync</c> has
/// prepended <see cref="SystemCrew.RunAttachmentsProfile"/> to the crew snapshot
/// (the behaviour introduced in the Run-Attachments feature).
///
/// These tests verify that the run-attachments pipeline path (Scope=run-local) correctly
/// calls the embedding provider and produces grounding context when attachments are present,
/// while the plain Klassik path (no grounding providers) continues to produce no grounding.
/// </summary>
public sealed class KlassikWithAttachmentsTests
{
    private const string Briefing = "Write a short briefing for attachment-grounding testing.";

    private static readonly Guid DocId = Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB");
    private static readonly float[] FakeVector = new float[1536];

    private static readonly IReadOnlyList<VectorSearchResult> AttachmentResults =
    [
        new VectorSearchResult(
            Chunk: new KnowledgeDocumentChunk(Guid.NewGuid(), DocId, 0, "Attachment content paragraph.", FakeVector, 12, DateTimeOffset.UtcNow),
            DocumentTitle: "Uploaded PDF",
            Similarity: 0.88),
    ];

    /// <summary>
    /// Klassik template with <see cref="SystemCrew.RunAttachmentsProfile"/> prepended —
    /// simulates what <c>RunService.SubmitRunAsync</c> does when a run has attachments.
    /// </summary>
    private static CrewSnapshot KlassikWithAttachmentsSnapshot(Guid runId) => new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: SystemCrew.KlassikTemplateName,
        Executor: SystemCrew.DefaultExecutorProfile,
        Reviewers: [SystemCrew.BriefingFidelityProfile, SystemCrew.ClarityProfile],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        Advisors: Array.Empty<AdvisorProfile>(),
        GroundingProviders: [SystemCrew.RunAttachmentsProfile]);

    // --- Test 2a: embedding provider is called when RunAttachmentsProfile is active ---

    [Fact]
    public async Task KlassikTemplate_WithAttachments_EmbeddingProviderIsCalled()
    {
        var embeddingStub = new CountingEmbeddingProviderStub();
        var runId = Guid.NewGuid();

        var factory = BuildFactory(embeddingStub);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            KlassikWithAttachmentsSnapshot(runId),
            resolver,
            Options.Create(new ConvergenceOptions()),
            runId: runId,
            groundingProviderFactory: factory);

        await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.Equal(1, embeddingStub.CallCount);
    }

    [Fact]
    public async Task KlassikTemplate_WithAttachments_RunAttachmentsProfileSearchedWithRunLocalScope()
    {
        var trackingRepo = new TrackingVectorSearchRepository(AttachmentResults);
        var runId = Guid.NewGuid();

        var factory = BuildFactory(searchRepo: trackingRepo);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            KlassikWithAttachmentsSnapshot(runId),
            resolver,
            Options.Create(new ConvergenceOptions()),
            runId: runId,
            groundingProviderFactory: factory);

        await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.Single(trackingRepo.Calls);
        Assert.Equal(KnowledgeScope.RunLocal, trackingRepo.Calls[0].Scope);
        Assert.NotNull(trackingRepo.Calls[0].RunId);
        Assert.Equal(runId, trackingRepo.Calls[0].RunId!.Value);
    }

    // --- Test 2b: grounding context contains "Knowledge Base Results" heading ---

    [Fact]
    public async Task KlassikTemplate_WithAttachments_ConsultationRecordedForGrounding()
    {
        var consultationRepo = new CapturingGroundingConsultationRepository();
        var runId = Guid.NewGuid();

        var factory = BuildFactory(consultationRepo: consultationRepo);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            KlassikWithAttachmentsSnapshot(runId),
            resolver,
            Options.Create(new ConvergenceOptions()),
            runId: runId,
            groundingProviderFactory: factory);

        await runner.RunAsync(Briefing, CancellationToken.None);

        // One consultation must have been persisted (one provider)
        Assert.Single(consultationRepo.All);
    }

    [Fact]
    public async Task KlassikTemplate_WithAttachments_GroundingResultIncludesKnowledgeBaseHeading()
    {
        var runId = Guid.NewGuid();

        // Build the provider directly and call EnrichAsync to inspect the enriched context
        var consultationRepo = new CapturingGroundingConsultationRepository();
        var trackingRepo = new TrackingVectorSearchRepository(AttachmentResults);

        var services = new ServiceCollection();
        services.AddScoped<IVectorSearchRepository>(_ => trackingRepo);
        services.AddScoped<IGroundingConsultationRepository>(_ => consultationRepo);

        var provider = new VectorStoreGroundingProvider(
            new CountingEmbeddingProviderStub(),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VectorStoreGroundingProvider>.Instance);

        var result = await provider.EnrichAsync(
            Briefing, SystemCrew.RunAttachmentsProfile, runId, CancellationToken.None);

        Assert.Contains("Knowledge Base", result.EnrichedContext, StringComparison.OrdinalIgnoreCase);
    }

    // --- factory helpers ---

    private static SingleProviderFactory BuildFactory(
        CountingEmbeddingProviderStub? embeddingStub = null,
        TrackingVectorSearchRepository? searchRepo = null,
        CapturingGroundingConsultationRepository? consultationRepo = null)
    {
        embeddingStub ??= new CountingEmbeddingProviderStub();
        searchRepo ??= new TrackingVectorSearchRepository(AttachmentResults);
        consultationRepo ??= new CapturingGroundingConsultationRepository();

        var services = new ServiceCollection();
        services.AddScoped<IVectorSearchRepository>(_ => searchRepo);
        services.AddScoped<IGroundingConsultationRepository>(_ => consultationRepo);

        var provider = new VectorStoreGroundingProvider(
            embeddingStub,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VectorStoreGroundingProvider>.Instance);

        return new SingleProviderFactory(provider);
    }

    // --- stubs ---

    private sealed class CountingEmbeddingProviderStub : IEmbeddingProvider
    {
        private int _callCount;

        public int CallCount => _callCount;
        public string ProviderName => "stub";
        public string ModelName => "stub-model";
        public int Dimensions => 1536;

        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(new EmbeddingResult(FakeVector, 10, null));
        }

        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<EmbeddingResult>>(
                texts.Select(_ => new EmbeddingResult(FakeVector, 10, null)).ToList());
    }

    private sealed class TrackingVectorSearchRepository(IReadOnlyList<VectorSearchResult> results)
        : IVectorSearchRepository
    {
        private readonly List<(float[] Embedding, int TopK, IReadOnlyList<string>? Tags, KnowledgeScope? Scope, Guid? RunId)> _calls = [];

        public IReadOnlyList<(float[] Embedding, int TopK, IReadOnlyList<string>? Tags, KnowledgeScope? Scope, Guid? RunId)> Calls
            => _calls;

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryEmbedding,
            int topK,
            IReadOnlyList<string>? tagFilter,
            KnowledgeScope? scopeFilter,
            Guid? runIdFilter,
            CancellationToken ct)
        {
            _calls.Add((queryEmbedding, topK, tagFilter, scopeFilter, runIdFilter));
            return Task.FromResult(results);
        }

        public Task<KnowledgeDocumentChunk> CreateChunkAsync(KnowledgeDocumentChunk chunk, CancellationToken ct)
            => Task.FromResult(chunk);

        public Task DeleteChunksForDocumentAsync(Guid documentId, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class CapturingGroundingConsultationRepository : IGroundingConsultationRepository
    {
        private readonly List<GroundingConsultation> _store = [];

        public IReadOnlyList<GroundingConsultation> All => _store;

        public Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct)
        {
            _store.Add(consultation);
            return Task.FromResult(consultation);
        }

        public Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<GroundingConsultation>>(
                _store.Where(c => c.RunId == runId).ToList());
    }

    private sealed class SingleProviderFactory(IGroundingProvider provider) : IGroundingProviderFactory
    {
        public IGroundingProvider Create(string providerType)
        {
            if (providerType == provider.ProviderType)
                return provider;
            throw new InvalidOperationException($"No provider registered for type '{providerType}'.");
        }

        public bool IsRegistered(string providerType) => providerType == provider.ProviderType;
    }
}
