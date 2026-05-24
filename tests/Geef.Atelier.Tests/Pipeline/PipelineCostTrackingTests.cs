using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pricing;
using Geef.Atelier.Tests.Llm;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Verifies that <see cref="ProfileBasedExecutor"/>, <see cref="ProfileBasedReviewer"/>,
/// and <see cref="ProfileBasedAdvisor"/> correctly record actor costs into the
/// <see cref="ICostAccumulator"/> when both a catalog and accumulator are provided.
/// </summary>
public sealed class PipelineCostTrackingTests
{
    private const string Briefing = "Write a short test text.";

    private static CrewSnapshot BuildSnapshot(IReadOnlyList<AdvisorProfile>? advisors = null) => new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: SystemCrew.KlassikTemplateName,
        Executor: SystemCrew.DefaultExecutorProfile,
        Reviewers: [SystemCrew.BriefingFidelityProfile, SystemCrew.ClarityProfile],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        Advisors: advisors ?? Array.Empty<AdvisorProfile>());

    // Models used by SystemCrew profiles (canonical names — no vendor prefix)
    private const string ExecutorModel  = "claude-opus-4-7";
    private const string ReviewerModel  = "openai/gpt-4o-mini"; // briefing-fidelity + clarity use this

    private static IPricingCatalog BuildCatalog() =>
        new PricingCatalog(
            Options.Create(new PricingOptions
            {
                UsdToEurRate = 1.0,
                Models = new Dictionary<string, ModelPricing>
                {
                    [ExecutorModel] = new ModelPricing(1m, 2m),
                    // Include reviewer models from SystemCrew
                    ["openai/gpt-4o-mini"]         = new ModelPricing(0.15m, 0.60m),
                    ["anthropic/claude-sonnet-4-5"] = new ModelPricing(3m, 15m),
                }
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PricingCatalog>.Instance);

    [Fact]
    public async Task Pipeline_WithCostAccumulator_RecordsExecutorCost()
    {
        var accumulator = new RunCostAccumulator();
        var catalog     = BuildCatalog();
        var resolver    = new TestLlmClientResolver(new FakeLlmClient());
        var snapshot    = BuildSnapshot();

        var runner = AtelierPipelineFactory.Build(
            snapshot, resolver, Options.Create(new ConvergenceOptions()),
            pricingCatalog: catalog, costAccumulator: accumulator);

        await runner.RunAsync(Briefing, CancellationToken.None);

        var costs = accumulator.Flush();
        var executorCosts = costs.Where(c => c.ActorType == ActorType.Executor).ToList();
        Assert.NotEmpty(executorCosts);
        // Executor model is from the profile (claude-opus-4-7 = known model → cost not null)
        Assert.All(executorCosts, c =>
        {
            Assert.Equal(ActorType.Executor, c.ActorType);
            Assert.Equal(ExecutorModel, c.ModelName);
            Assert.NotNull(c.CostEur);
            Assert.True(c.CostEur >= 0);
        });
    }

    [Fact]
    public async Task Pipeline_WithCostAccumulator_RecordsReviewerCosts()
    {
        var accumulator = new RunCostAccumulator();
        var catalog     = BuildCatalog();
        var resolver    = new TestLlmClientResolver(new FakeLlmClient());
        var snapshot    = BuildSnapshot();

        var runner = AtelierPipelineFactory.Build(
            snapshot, resolver, Options.Create(new ConvergenceOptions()),
            pricingCatalog: catalog, costAccumulator: accumulator);

        await runner.RunAsync(Briefing, CancellationToken.None);

        var costs = accumulator.Flush();
        var reviewerCosts = costs.Where(c => c.ActorType == ActorType.Reviewer).ToList();
        // 2 reviewers × at least 2 iterations (FakeLlmClient rejects on first) = ≥4 reviewer records
        Assert.NotEmpty(reviewerCosts);
        Assert.All(reviewerCosts, c => Assert.Equal(ActorType.Reviewer, c.ActorType));
    }

    [Fact]
    public async Task Pipeline_WithCostAccumulator_IterationNumbersArePositive()
    {
        var accumulator = new RunCostAccumulator();
        var catalog     = BuildCatalog();
        var resolver    = new TestLlmClientResolver(new FakeLlmClient());
        var snapshot    = BuildSnapshot();

        var runner = AtelierPipelineFactory.Build(
            snapshot, resolver, Options.Create(new ConvergenceOptions()),
            pricingCatalog: catalog, costAccumulator: accumulator);

        await runner.RunAsync(Briefing, CancellationToken.None);

        var costs = accumulator.Flush();
        Assert.All(costs, c => Assert.True(c.IterationNumber >= 1));
    }

    [Fact]
    public async Task Pipeline_WithoutAccumulator_CompletesWithoutError()
    {
        var resolver = new TestLlmClientResolver(new FakeLlmClient());
        var snapshot = BuildSnapshot();

        // No accumulator or catalog passed — must not throw
        var runner = AtelierPipelineFactory.Build(
            snapshot, resolver, Options.Create(new ConvergenceOptions()));

        var ex = await Record.ExceptionAsync(() => runner.RunAsync(Briefing, CancellationToken.None));

        Assert.Null(ex);
    }

    [Fact]
    public async Task Pipeline_UnknownModel_RecordsNullCost()
    {
        // Build a catalog with only "known-model", but profiles use claude-opus-4-7
        var accumulator = new RunCostAccumulator();
        var emptyCatalog = new PricingCatalog(
            Options.Create(new PricingOptions { Models = new Dictionary<string, ModelPricing>() }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PricingCatalog>.Instance);
        var resolver = new TestLlmClientResolver(new FakeLlmClient());
        var snapshot = BuildSnapshot();

        var runner = AtelierPipelineFactory.Build(
            snapshot, resolver, Options.Create(new ConvergenceOptions()),
            pricingCatalog: emptyCatalog, costAccumulator: accumulator);

        await runner.RunAsync(Briefing, CancellationToken.None);

        var costs = accumulator.Flush();
        // All costs are null because no models are in the catalog
        Assert.All(costs, c => Assert.Null(c.CostEur));
    }
}
