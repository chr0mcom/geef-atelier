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
/// Integration tests for the run-attachments pipeline path.
/// Verifies that <see cref="VectorStoreGroundingProvider"/> correctly filters by
/// <see cref="KnowledgeScope.RunLocal"/> when the profile is <see cref="SystemCrew.RunAttachmentsProfile"/>,
/// and by <see cref="KnowledgeScope.Global"/> for <see cref="SystemCrew.KnowledgeBaseDefaultProfile"/>.
/// Also covers multi-provider ordering and combined citation results.
/// </summary>
public sealed class AtelierPipelineFactoryWithRunAttachmentsTests
{
    private static readonly Guid DocId = Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA");
    private static readonly float[] FakeVector = new float[1536];

    private static readonly IReadOnlyList<VectorSearchResult> TwoResults =
    [
        new VectorSearchResult(
            Chunk: new KnowledgeDocumentChunk(Guid.NewGuid(), DocId, 0, "Attachment chunk zero.", FakeVector, 15, DateTimeOffset.UtcNow),
            DocumentTitle: "Uploaded Attachment Alpha",
            Similarity: 0.91),
        new VectorSearchResult(
            Chunk: new KnowledgeDocumentChunk(Guid.NewGuid(), DocId, 2, "Attachment chunk two.", FakeVector, 10, DateTimeOffset.UtcNow),
            DocumentTitle: "Uploaded Attachment Beta",
            Similarity: 0.74),
    ];

    // --- Test 1a: run-attachments provider searches only run-local scope ---

    [Fact]
    public async Task RunAttachmentsProvider_SearchesOnlyRunLocalScope()
    {
        var trackingRepo = new TrackingVectorSearchRepository(TwoResults);
        var runId = Guid.NewGuid();

        var provider = BuildProviderDirectly(trackingRepo: trackingRepo);
        var profile = SystemCrew.RunAttachmentsProfile;

        await provider.EnrichAsync("Test briefing.", profile, runId, CancellationToken.None);

        Assert.Single(trackingRepo.Calls);
        var call = trackingRepo.Calls[0];
        Assert.Equal(KnowledgeScope.RunLocal, call.Scope);
    }

    [Fact]
    public async Task RunAttachmentsProvider_PassesNonNullRunIdFilter()
    {
        var trackingRepo = new TrackingVectorSearchRepository(TwoResults);
        var runId = Guid.NewGuid();

        var provider = BuildProviderDirectly(trackingRepo: trackingRepo);
        var profile = SystemCrew.RunAttachmentsProfile;

        await provider.EnrichAsync("Test briefing.", profile, runId, CancellationToken.None);

        var call = trackingRepo.Calls[0];
        Assert.NotNull(call.RunId);
        Assert.Equal(runId, call.RunId!.Value);
    }

    [Fact]
    public async Task RunAttachmentsProvider_ReturnsCitationsFromSearch()
    {
        var provider = BuildProviderDirectly();
        var profile = SystemCrew.RunAttachmentsProfile;

        var result = await provider.EnrichAsync("Test briefing.", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, result.Citations.Count);
    }

    // --- Test 1b: citations have correct DocumentReference format and null Url ---

    [Fact]
    public async Task RunAttachmentsProvider_CitationsHaveRunLocalDocumentReferences()
    {
        var provider = BuildProviderDirectly();
        var profile = SystemCrew.RunAttachmentsProfile;

        var result = await provider.EnrichAsync("Test briefing.", profile, Guid.NewGuid(), CancellationToken.None);

        // All citations must have DocumentReference in format "{docId}/chunk-{n}"
        Assert.All(result.Citations, c =>
        {
            Assert.NotNull(c.DocumentReference);
            Assert.Matches(@"^[0-9a-fA-F\-]{36}/chunk-\d+$", c.DocumentReference);
        });
        Assert.Equal($"{DocId}/chunk-0", result.Citations[0].DocumentReference);
        Assert.Equal($"{DocId}/chunk-2", result.Citations[1].DocumentReference);
    }

    [Fact]
    public async Task RunAttachmentsProvider_Citations_UrlIsNull()
    {
        var provider = BuildProviderDirectly();
        var profile = SystemCrew.RunAttachmentsProfile;

        var result = await provider.EnrichAsync("Test briefing.", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.All(result.Citations, c => Assert.Null(c.Url));
    }

    // --- Test 1c: global knowledge-base provider searches global scope ---

    [Fact]
    public async Task GlobalKnowledgeBaseProvider_SearchesGlobalScope()
    {
        var trackingRepo = new TrackingVectorSearchRepository(TwoResults);
        var provider = BuildProviderDirectly(trackingRepo: trackingRepo);
        var profile = SystemCrew.KnowledgeBaseDefaultProfile;

        await provider.EnrichAsync("Test briefing.", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.Single(trackingRepo.Calls);
        Assert.Equal(KnowledgeScope.Global, trackingRepo.Calls[0].Scope);
    }

    [Fact]
    public async Task GlobalKnowledgeBaseProvider_RunIdFilterIsNull()
    {
        var trackingRepo = new TrackingVectorSearchRepository(TwoResults);
        var provider = BuildProviderDirectly(trackingRepo: trackingRepo);
        var profile = SystemCrew.KnowledgeBaseDefaultProfile;

        await provider.EnrichAsync("Test briefing.", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(trackingRepo.Calls[0].RunId);
    }

    // --- Test 1d: multi-provider snapshot — RunAttachments first then Global ---

    [Fact]
    public async Task MultipleProviders_BothProvidersAreCalled()
    {
        var runId = Guid.NewGuid();
        var callTracking = new TrackingVectorSearchRepository(TwoResults);
        var consultationRepo = new CapturingGroundingConsultationRepository();

        var snapshot = BuildSnapshotWithGrounding(
            [SystemCrew.RunAttachmentsProfile, SystemCrew.KnowledgeBaseDefaultProfile]);

        var factory = BuildFactory(callTracking, consultationRepo);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            snapshot,
            resolver,
            Options.Create(new ConvergenceOptions()),
            runId: runId,
            groundingProviderFactory: factory);

        await runner.RunAsync("Test briefing text.", CancellationToken.None);

        // Both providers must have triggered a search call
        Assert.Equal(2, callTracking.Calls.Count);
    }

    [Fact]
    public async Task MultipleProviders_RunAttachmentsSearchedWithRunLocalScope()
    {
        var runId = Guid.NewGuid();
        var callTracking = new TrackingVectorSearchRepository(TwoResults);
        var consultationRepo = new CapturingGroundingConsultationRepository();

        var snapshot = BuildSnapshotWithGrounding(
            [SystemCrew.RunAttachmentsProfile, SystemCrew.KnowledgeBaseDefaultProfile]);

        var factory = BuildFactory(callTracking, consultationRepo);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            snapshot,
            resolver,
            Options.Create(new ConvergenceOptions()),
            runId: runId,
            groundingProviderFactory: factory);

        await runner.RunAsync("Test briefing text.", CancellationToken.None);

        // First call must be run-local (run-attachments)
        var firstCall = callTracking.Calls[0];
        Assert.Equal(KnowledgeScope.RunLocal, firstCall.Scope);
        Assert.NotNull(firstCall.RunId);
    }

    [Fact]
    public async Task MultipleProviders_KnowledgeBaseSearchedWithGlobalScope()
    {
        var runId = Guid.NewGuid();
        var callTracking = new TrackingVectorSearchRepository(TwoResults);
        var consultationRepo = new CapturingGroundingConsultationRepository();

        var snapshot = BuildSnapshotWithGrounding(
            [SystemCrew.RunAttachmentsProfile, SystemCrew.KnowledgeBaseDefaultProfile]);

        var factory = BuildFactory(callTracking, consultationRepo);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            snapshot,
            resolver,
            Options.Create(new ConvergenceOptions()),
            runId: runId,
            groundingProviderFactory: factory);

        await runner.RunAsync("Test briefing text.", CancellationToken.None);

        // Second call must be global (knowledge-base-default)
        var secondCall = callTracking.Calls[1];
        Assert.Equal(KnowledgeScope.Global, secondCall.Scope);
        Assert.Null(secondCall.RunId);
    }

    [Fact]
    public async Task MultipleProviders_TwoConsultationsRecorded()
    {
        var runId = Guid.NewGuid();
        var callTracking = new TrackingVectorSearchRepository(TwoResults);
        var consultationRepo = new CapturingGroundingConsultationRepository();

        var snapshot = BuildSnapshotWithGrounding(
            [SystemCrew.RunAttachmentsProfile, SystemCrew.KnowledgeBaseDefaultProfile]);

        var factory = BuildFactory(callTracking, consultationRepo);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            snapshot,
            resolver,
            Options.Create(new ConvergenceOptions()),
            runId: runId,
            groundingProviderFactory: factory);

        await runner.RunAsync("Test briefing text.", CancellationToken.None);

        // One consultation per provider
        Assert.Equal(2, consultationRepo.All.Count);
    }

    // --- factory helpers ---

    private static VectorStoreGroundingProvider BuildProviderDirectly(
        TrackingVectorSearchRepository? trackingRepo = null,
        CapturingGroundingConsultationRepository? consultationRepo = null)
    {
        trackingRepo ??= new TrackingVectorSearchRepository(TwoResults);
        consultationRepo ??= new CapturingGroundingConsultationRepository();

        var services = new ServiceCollection();
        services.AddScoped<IVectorSearchRepository>(_ => trackingRepo);
        services.AddScoped<IGroundingConsultationRepository>(_ => consultationRepo);

        return new VectorStoreGroundingProvider(
            new CountingEmbeddingProviderStub(),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VectorStoreGroundingProvider>.Instance);
    }

    private static SingleProviderFactory BuildFactory(
        TrackingVectorSearchRepository trackingRepo,
        CapturingGroundingConsultationRepository? consultationRepo = null)
    {
        consultationRepo ??= new CapturingGroundingConsultationRepository();
        var provider = BuildProviderDirectly(trackingRepo, consultationRepo);
        return new SingleProviderFactory(provider);
    }

    private static CrewSnapshot BuildSnapshotWithGrounding(
        IReadOnlyList<GroundingProviderProfile> groundingProviders) =>
        new(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: SystemCrew.KlassikTemplateName,
            Executor: SystemCrew.DefaultExecutorProfile,
            Reviewers: [SystemCrew.BriefingFidelityProfile],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: Array.Empty<AdvisorProfile>(),
            GroundingProviders: groundingProviders);

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

    /// <summary>
    /// Tracks all <see cref="IVectorSearchRepository.SearchAsync"/> calls with their scope and
    /// run-id parameters. Enables assertions on which scope each provider used.
    /// </summary>
    internal sealed class TrackingVectorSearchRepository(IReadOnlyList<VectorSearchResult> results)
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

        public Task UpdateRefinementOutcomeAsync(Guid consultationId, RefinementOutcome outcome, CancellationToken ct)
            => Task.CompletedTask;
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
        public IReadOnlyCollection<string> RegisteredTypes => new[] { provider.ProviderType };
    }
}
