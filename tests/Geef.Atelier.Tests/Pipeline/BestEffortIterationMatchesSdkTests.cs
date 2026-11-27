using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Geef.Sdk;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Microsoft.Extensions.Options;

using Geef.Atelier.Tests.Fakes;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// B9 (Etappe 0, review round 2): <see cref="DeliveredOutcomeExtensions.BestEffortIteration"/> mirrors the SDK's
/// best-effort choice. Three rounds, the middle one with the fewest rejected reviews: the SDK delivers the middle
/// draft (<c>result.Output</c>), and the recorded iteration must be that same round, not the last one.
/// </summary>
public sealed class BestEffortIterationMatchesSdkTests
{
    [Fact]
    public async Task TheMiddleRoundWithTheFewestRejectionsIsDeliveredAndRecorded()
    {
        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: TestLoop.TemplateName,
            Executor: TestLoop.Writer,
            Reviewers: TestLoop.Reviewers,
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: new ConvergencePolicyOverride(MaxIterations: 3, AbortOnCritical: false, DetectRegression: false, StagnationThreshold: null),
            Advisors: []);
        var scripted = new Dictionary<string, IReviewer>
        {
            [TestLoop.Reviewers[0].Name] = new ScriptedReviewer("always-rejects", 1, 2, 3),
            [TestLoop.Reviewers[1].Name] = new ScriptedReviewer("satisfied-in-round-two", 1, 3),
        };
        var runner = TestLoop.Build(
            snapshot,
            new TestLlmClientResolver(new RoundDraftClient()),
            Options.Create(new ConvergenceOptions()),
            reviewers: profile => scripted[profile.Name]);

        var result = await runner.RunAsync("Write a short test text.", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal([1, 2, 3], result.History.Records.Select(r => r.Iteration).ToList());
        Assert.Equal([2, 1, 2], result.History.Records.Select(r => r.EvaluationResult.Reviews.Count(rv => rv.Decision == ReviewDecision.Rejected)).ToList());
        Assert.Contains(RoundDraftClient.DraftOf(2), result.Output.Markdown);
        Assert.Equal(2, result.History.BestEffortIteration());
        Assert.Equal(new DeliveredOutcome(RunOutcome.DeliveredWithOpenPoints, 2), result.ToDeliveredOutcome());
    }

    /// <summary>Writes "Draft of round N" for the N-th writing call.</summary>
    private sealed class RoundDraftClient : ILlmClient
    {
        private int _round;

        public static string DraftOf(int round) => $"Draft of round {round}.";

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct) =>
            Task.FromResult(FakeLlmClient.MakeTextResponse(DraftOf(Interlocked.Increment(ref _round))));
    }

    /// <summary>
    /// Rejects with a blocking finding in the listed rounds (a new finding text per round, so no stagnation rule fires)
    /// and approves in all others.
    /// </summary>
    private sealed class ScriptedReviewer(string name, params int[] rejectedRounds) : IReviewer
    {
        public string Name => name;

        public int Priority => 0;

        public Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken cancellationToken)
        {
            var round = context.GetRequired(GeefKeys.CurrentIteration);
            if (!rejectedRounds.Contains(round))
                return Task.FromResult(new ReviewResult { ReviewerName = name, Decision = ReviewDecision.Approved, Findings = [], Duration = TimeSpan.Zero });

            var finding = new Finding
            {
                ReviewerName = name,
                Fingerprint = $"{name}:round-{round}",
                Message = $"Finding of {name} in round {round}.",
                Severity = Geef.Sdk.Results.FindingSeverity.Error,
                Category = "test",
                ArtifactReference = "draft",
                Metadata = new Dictionary<string, object>(),
            };
            return Task.FromResult(new ReviewResult { ReviewerName = name, Decision = ReviewDecision.Rejected, Findings = [finding], Duration = TimeSpan.Zero });
        }
    }
}
