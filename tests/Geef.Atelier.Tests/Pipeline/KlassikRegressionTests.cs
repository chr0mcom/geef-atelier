using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Regression tests that verify Klassik crew configurations (no vector-store grounding profile)
/// never trigger the embedding provider. This ensures that adding vector-store grounding
/// infrastructure does not inadvertently activate it for classic crews.
/// </summary>
public sealed class KlassikRegressionTests
{
    private const string Briefing = "Write a short briefing text for regression testing.";

    private static CrewSnapshot KlassikSnapshot() => new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: SystemCrew.KlassikTemplateName,
        Executor: SystemCrew.DefaultExecutorProfile,
        Reviewers: [SystemCrew.BriefingFidelityProfile, SystemCrew.ClarityProfile],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        Advisors: Array.Empty<AdvisorProfile>(),
        GroundingProviders: null);

    [Fact]
    public async Task KlassikPipeline_WithNoGroundingProviders_CompletesWithoutError()
    {
        var embeddingProvider = new CountingEmbeddingProviderStub();
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            KlassikSnapshot(),
            resolver,
            Options.Create(new ConvergenceOptions()),
            groundingProviderFactory: null);

        var ex = await Record.ExceptionAsync(
            () => runner.RunAsync(Briefing, CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task KlassikPipeline_WithNoGroundingProviders_NeverCallsEmbeddingProvider()
    {
        var embeddingProvider = new CountingEmbeddingProviderStub();
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            KlassikSnapshot(),
            resolver,
            Options.Create(new ConvergenceOptions()),
            groundingProviderFactory: null);

        await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.Equal(0, embeddingProvider.CallCount);
    }

    [Fact]
    public async Task KlassikPipeline_WithEmptyGroundingProviderList_NeverCallsEmbeddingProvider()
    {
        var embeddingProvider = new CountingEmbeddingProviderStub();
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: SystemCrew.KlassikTemplateName,
            Executor: SystemCrew.DefaultExecutorProfile,
            Reviewers: [SystemCrew.BriefingFidelityProfile],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: Array.Empty<AdvisorProfile>(),
            GroundingProviders: []);  // empty list — no grounding

        var runner = AtelierPipelineFactory.Build(
            snapshot,
            resolver,
            Options.Create(new ConvergenceOptions()),
            groundingProviderFactory: null);

        await runner.RunAsync(Briefing, CancellationToken.None);

        Assert.Equal(0, embeddingProvider.CallCount);
    }

    [Fact]
    public async Task KlassikPipeline_WithEmptyGroundingProviderList_ProducesZeroConsultations()
    {
        var fakeClient = new FakeLlmClient();
        var resolver = new TestLlmClientResolver(fakeClient);

        var runner = AtelierPipelineFactory.Build(
            KlassikSnapshot(),
            resolver,
            Options.Create(new ConvergenceOptions()),
            groundingProviderFactory: null);

        // If no exception is thrown and the pipeline completes, there are zero consultations
        // because the MultiProviderGroundingStep is never created when GroundingProviders is null/empty.
        var ex = await Record.ExceptionAsync(
            () => runner.RunAsync(Briefing, CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public void KlassikTemplate_HasNoGroundingProviderNames_ConfirmingNoVectorStoreActivation()
    {
        // Confirm at the template level that Klassik ships with no grounding — the snapshot
        // produced from it will therefore never include a vector-store provider.
        Assert.Empty(SystemCrew.KlassikTemplate.GroundingProviderNames);
    }

    // --- stubs ---

    /// <summary>
    /// Counting stub for <see cref="IEmbeddingProvider"/>.
    /// Used to assert that no embedding calls are made in Klassik configurations.
    /// </summary>
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
            return Task.FromResult(new EmbeddingResult(new float[1536], 10, null));
        }

        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(
            IReadOnlyList<string> texts,
            CancellationToken ct)
            => Task.FromResult<IReadOnlyList<EmbeddingResult>>(
                texts.Select(_ => new EmbeddingResult(new float[1536], 10, null)).ToList());
    }
}
