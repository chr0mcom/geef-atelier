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
/// Verifies that <see cref="MultiProviderGroundingStep"/> invokes providers strictly in the
/// order declared in the <see cref="CrewSnapshot.GroundingProviders"/> list.
/// Key invariant: <see cref="SystemCrew.RunAttachmentsProfile"/> (Scope=run-local) always runs
/// BEFORE <see cref="SystemCrew.KnowledgeBaseDefaultProfile"/> (Scope=global) when both are present.
/// </summary>
public sealed class MultiProviderOrderingTests
{
    private const string Briefing = "Order verification briefing.";

    private static readonly float[] FakeVector = new float[1536];
    private static readonly IReadOnlyList<VectorSearchResult> EmptyResults = [];

    // --- Test 4a: run-attachments search occurs BEFORE knowledge-base-default search ---

    [Fact]
    public async Task CustomTemplate_WithKnowledgeBaseAndAttachments_RunAttachmentsFirst()
    {
        var callOrderTracking = new CallOrderTrackingSearchRepository();
        var runId = Guid.NewGuid();

        // Snapshot with run-attachments FIRST, knowledge-base-default SECOND
        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: null,
            Executor: SystemCrew.DefaultExecutorProfile,
            Reviewers: [SystemCrew.BriefingFidelityProfile],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: Array.Empty<AdvisorProfile>(),
            GroundingProviders: [SystemCrew.RunAttachmentsProfile, SystemCrew.KnowledgeBaseDefaultProfile]);

        var factory = BuildFactory(callOrderTracking);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            snapshot,
            resolver,
            Options.Create(new ConvergenceOptions()),
            runId: runId,
            groundingProviderFactory: factory);

        await runner.RunAsync(Briefing, CancellationToken.None);

        // Must have exactly two calls
        Assert.Equal(2, callOrderTracking.ScopeOrder.Count);

        // First call: run-local (from run-attachments)
        Assert.Equal(KnowledgeScope.RunLocal, callOrderTracking.ScopeOrder[0]);

        // Second call: global (from knowledge-base-default)
        Assert.Equal(KnowledgeScope.Global, callOrderTracking.ScopeOrder[1]);
    }

    [Fact]
    public async Task CustomTemplate_WithReversedOrder_GlobalBeforeRunLocal()
    {
        var callOrderTracking = new CallOrderTrackingSearchRepository();
        var runId = Guid.NewGuid();

        // Snapshot with knowledge-base-default FIRST, run-attachments SECOND (reversed)
        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: null,
            Executor: SystemCrew.DefaultExecutorProfile,
            Reviewers: [SystemCrew.BriefingFidelityProfile],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: Array.Empty<AdvisorProfile>(),
            GroundingProviders: [SystemCrew.KnowledgeBaseDefaultProfile, SystemCrew.RunAttachmentsProfile]);

        var factory = BuildFactory(callOrderTracking);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            snapshot,
            resolver,
            Options.Create(new ConvergenceOptions()),
            runId: runId,
            groundingProviderFactory: factory);

        await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.Equal(2, callOrderTracking.ScopeOrder.Count);

        // Provider order is respected: global first, then run-local
        Assert.Equal(KnowledgeScope.Global, callOrderTracking.ScopeOrder[0]);
        Assert.Equal(KnowledgeScope.RunLocal, callOrderTracking.ScopeOrder[1]);
    }

    [Fact]
    public async Task CustomTemplate_WithSingleRunAttachmentsProvider_OnlyOneSearchCall()
    {
        var callOrderTracking = new CallOrderTrackingSearchRepository();
        var runId = Guid.NewGuid();

        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: null,
            Executor: SystemCrew.DefaultExecutorProfile,
            Reviewers: [SystemCrew.BriefingFidelityProfile],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: Array.Empty<AdvisorProfile>(),
            GroundingProviders: [SystemCrew.RunAttachmentsProfile]);

        var factory = BuildFactory(callOrderTracking);
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            snapshot,
            resolver,
            Options.Create(new ConvergenceOptions()),
            runId: runId,
            groundingProviderFactory: factory);

        await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.Single(callOrderTracking.ScopeOrder);
        Assert.Equal(KnowledgeScope.RunLocal, callOrderTracking.ScopeOrder[0]);
    }

    // --- factory helpers ---

    private static SingleProviderFactory BuildFactory(CallOrderTrackingSearchRepository trackingRepo)
    {
        var services = new ServiceCollection();
        services.AddScoped<IVectorSearchRepository>(_ => trackingRepo);
        services.AddScoped<IGroundingConsultationRepository>(_ => new NullGroundingConsultationRepository());

        var provider = new VectorStoreGroundingProvider(
            new StubEmbeddingProvider(),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<VectorStoreGroundingProvider>.Instance);

        return new SingleProviderFactory(provider);
    }

    // --- stubs ---

    private sealed class StubEmbeddingProvider : IEmbeddingProvider
    {
        public string ProviderName => "stub";
        public string ModelName => "stub-model";
        public int Dimensions => 1536;

        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct)
            => Task.FromResult(new EmbeddingResult(FakeVector, 10, null));

        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<EmbeddingResult>>(
                texts.Select(_ => new EmbeddingResult(FakeVector, 10, null)).ToList());
    }

    /// <summary>
    /// Records the <see cref="KnowledgeScope"/> of each <see cref="SearchAsync"/> call in order.
    /// Enables asserting that providers are invoked in snapshot-declared sequence.
    /// </summary>
    private sealed class CallOrderTrackingSearchRepository : IVectorSearchRepository
    {
        private readonly List<KnowledgeScope?> _scopeOrder = [];

        public IReadOnlyList<KnowledgeScope?> ScopeOrder => _scopeOrder;

        public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            float[] queryEmbedding,
            int topK,
            IReadOnlyList<string>? tagFilter,
            KnowledgeScope? scopeFilter,
            Guid? runIdFilter,
            CancellationToken ct)
        {
            _scopeOrder.Add(scopeFilter);
            return Task.FromResult(EmptyResults);
        }

        public Task<KnowledgeDocumentChunk> CreateChunkAsync(KnowledgeDocumentChunk chunk, CancellationToken ct)
            => Task.FromResult(chunk);

        public Task DeleteChunksForDocumentAsync(Guid documentId, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class NullGroundingConsultationRepository : IGroundingConsultationRepository
    {
        public Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct)
            => Task.FromResult(consultation);

        public Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<GroundingConsultation>>([]);

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
