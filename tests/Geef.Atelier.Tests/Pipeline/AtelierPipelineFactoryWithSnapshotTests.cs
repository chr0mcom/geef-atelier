using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Verifies that <see cref="AtelierPipelineFactory.Build"/> correctly constructs a pipeline
/// from a <see cref="CrewSnapshot"/>, exercising all four EvaluationStrategy mappings.
/// </summary>
public sealed class AtelierPipelineFactoryWithSnapshotTests
{
    private static CrewSnapshot KlassikSnapshot(EvaluationStrategy strategy = EvaluationStrategy.Parallel) => new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: SystemCrew.KlassikTemplateName,
        Executor: SystemCrew.DefaultExecutorProfile,
        Reviewers: [SystemCrew.BriefingFidelityProfile, SystemCrew.ClarityProfile],
        EvaluationStrategy: strategy,
        ConvergenceOverride: null,
        Advisors: Array.Empty<AdvisorProfile>());

    [Theory]
    [InlineData(EvaluationStrategy.Parallel)]
    [InlineData(EvaluationStrategy.Sequential)]
    [InlineData(EvaluationStrategy.FailFast)]
    [InlineData(EvaluationStrategy.Priority)]
    public void Build_WithEachEvaluationStrategy_DoesNotThrow(EvaluationStrategy strategy)
    {
        var resolver = new TestLlmClientResolver(new FakeLlmClient());
        var snapshot = KlassikSnapshot(strategy);

        var ex = Record.Exception(() =>
            AtelierPipelineFactory.Build(snapshot, resolver, Options.Create(new ConvergenceOptions())));

        Assert.Null(ex);
    }

    [Fact]
    public void Build_WithConvergenceOverride_AppliesMaxIterationsOverride()
    {
        var resolver = new TestLlmClientResolver(new FakeLlmClient());
        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: SystemCrew.KlassikTemplateName,
            Executor: SystemCrew.DefaultExecutorProfile,
            Reviewers: [SystemCrew.BriefingFidelityProfile],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: new ConvergencePolicyOverride(MaxIterations: 1, AbortOnCritical: null, DetectRegression: null, StagnationThreshold: null),
            Advisors: Array.Empty<AdvisorProfile>());

        var ex = Record.Exception(() =>
            AtelierPipelineFactory.Build(snapshot, resolver, Options.Create(new ConvergenceOptions())));

        Assert.Null(ex);
    }
}
