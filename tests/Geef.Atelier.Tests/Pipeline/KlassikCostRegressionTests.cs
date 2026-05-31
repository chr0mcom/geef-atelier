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
/// Cost-tracking regression: Klassik pipeline with cost tracking active produces
/// LLM cost entries in the accumulator (executor + reviewers) and no grounding costs
/// (GroundingCostEur remains absent since no grounding provider is active).
/// </summary>
public sealed class KlassikCostRegressionTests
{
    private const string Briefing = "Schreib einen kurzen Text über Kostenabrechnung.";

    private static CrewSnapshot KlassikSnapshot() => new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: SystemCrew.KlassikTemplateName,
        Executor: SystemCrew.DefaultExecutorProfile,
        Reviewers: [SystemCrew.BriefingFidelityProfile, SystemCrew.ClarityProfile],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        Advisors: Array.Empty<AdvisorProfile>(),
        GroundingProviders: null);

    private static PricingCatalog BuildCatalog() =>
        new PricingCatalog(
            Options.Create(new PricingOptions
            {
                UsdToEurRate = 0.92,
                Models = new Dictionary<string, ModelPricing>
                {
                    // Executor model from SystemCrew.DefaultExecutorProfile (canonical name)
                    ["claude-opus-4-8"]            = new ModelPricing(15m, 75m),
                    // Reviewer models from SystemCrew reviewer profiles
                    ["openai/gpt-4o-mini"]          = new ModelPricing(0.15m, 0.60m),
                    ["anthropic/claude-sonnet-4-5"] = new ModelPricing(3m, 15m),
                }
            }),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PricingCatalog>.Instance);

    [Fact]
    public async Task KlassikPipeline_WithCostTracking_ProducesExecutorCosts()
    {
        var accumulator = new RunCostAccumulator();
        var catalog     = BuildCatalog();
        var resolver    = new TestLlmClientResolver(new FakeLlmClient());

        var runner = AtelierPipelineFactory.Build(
            KlassikSnapshot(), resolver, Options.Create(new ConvergenceOptions()),
            pricingCatalog: catalog, costAccumulator: accumulator);

        await runner.RunAsync(Briefing, CancellationToken.None);

        var all      = accumulator.Flush();
        var executor = all.Where(c => c.ActorType == ActorType.Executor).ToList();

        // At least 2 iterations: FakeLlmClient rejects iteration 1, approves iteration 2
        Assert.True(executor.Count >= 2, $"Expected ≥2 executor costs but got {executor.Count}");
        Assert.All(executor, c =>
        {
            Assert.Equal(ActorType.Executor, c.ActorType);
            Assert.True(c.IterationNumber >= 1);
            Assert.NotNull(c.CostEur);
        });
    }

    [Fact]
    public async Task KlassikPipeline_WithCostTracking_ProducesReviewerCosts()
    {
        var accumulator = new RunCostAccumulator();
        var catalog     = BuildCatalog();
        var resolver    = new TestLlmClientResolver(new FakeLlmClient());

        var runner = AtelierPipelineFactory.Build(
            KlassikSnapshot(), resolver, Options.Create(new ConvergenceOptions()),
            pricingCatalog: catalog, costAccumulator: accumulator);

        await runner.RunAsync(Briefing, CancellationToken.None);

        var all      = accumulator.Flush();
        var reviewers = all.Where(c => c.ActorType == ActorType.Reviewer).ToList();

        Assert.NotEmpty(reviewers);
        Assert.All(reviewers, c => Assert.Equal(ActorType.Reviewer, c.ActorType));
    }

    [Fact]
    public async Task KlassikPipeline_WithCostTracking_ProducesNoAdvisorCosts()
    {
        // Klassik template has no advisors — accumulator must contain 0 advisor entries
        var accumulator = new RunCostAccumulator();
        var catalog     = BuildCatalog();
        var resolver    = new TestLlmClientResolver(new FakeLlmClient());

        var runner = AtelierPipelineFactory.Build(
            KlassikSnapshot(), resolver, Options.Create(new ConvergenceOptions()),
            pricingCatalog: catalog, costAccumulator: accumulator);

        await runner.RunAsync(Briefing, CancellationToken.None);

        var all     = accumulator.Flush();
        var advisor = all.Where(c => c.ActorType == ActorType.Advisor).ToList();

        Assert.Empty(advisor);
    }

    [Fact]
    public async Task KlassikPipeline_WithCostTracking_HasNoGroundingEntries()
    {
        // No grounding providers on Klassik → accumulator must never contain entries
        // with ActorType outside Executor/Reviewer (grounding has no ActorType entry)
        var accumulator = new RunCostAccumulator();
        var catalog     = BuildCatalog();
        var resolver    = new TestLlmClientResolver(new FakeLlmClient());

        var runner = AtelierPipelineFactory.Build(
            KlassikSnapshot(), resolver, Options.Create(new ConvergenceOptions()),
            pricingCatalog: catalog, costAccumulator: accumulator);

        await runner.RunAsync(Briefing, CancellationToken.None);

        var all = accumulator.Flush();
        // Only Executor and Reviewer entries are valid for Klassik
        var unexpected = all.Where(c =>
            c.ActorType != ActorType.Executor && c.ActorType != ActorType.Reviewer).ToList();

        Assert.Empty(unexpected);
    }
}
