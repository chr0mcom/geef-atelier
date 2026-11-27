using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Microsoft.Extensions.Options;

using Geef.Atelier.Tests.Fakes;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Verifies that <see cref="AtelierPipelineFactory.Build"/> correctly constructs a pipeline
/// from a <see cref="CrewSnapshot"/>, exercising all four EvaluationStrategy mappings.
/// </summary>
public sealed class AtelierPipelineFactoryWithSnapshotTests
{
    private static CrewSnapshot KlassikSnapshot(EvaluationStrategy strategy = EvaluationStrategy.Parallel) => new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: TestLoop.TemplateName,
        Executor: TestLoop.Writer,
        Reviewers: TestLoop.Reviewers,
        EvaluationStrategy: strategy,
        ConvergenceOverride: null,
        Advisors: []);

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
            TestLoop.Build(snapshot, resolver, Options.Create(new ConvergenceOptions())));

        Assert.Null(ex);
    }

    [Fact]
    public void Build_WithConvergenceOverride_AppliesMaxIterationsOverride()
    {
        var resolver = new TestLlmClientResolver(new FakeLlmClient());
        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: TestLoop.TemplateName,
            Executor: TestLoop.Writer,
            Reviewers: [TestLoop.Reviewers[0]],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: new ConvergencePolicyOverride(MaxIterations: 1, AbortOnCritical: null, DetectRegression: null, StagnationThreshold: null),
            Advisors: []);

        var ex = Record.Exception(() =>
            TestLoop.Build(snapshot, resolver, Options.Create(new ConvergenceOptions())));

        Assert.Null(ex);
    }
}
