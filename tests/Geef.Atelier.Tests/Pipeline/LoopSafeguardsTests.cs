using System.Collections.Concurrent;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Llm;
using Geef.Sdk;
using Geef.Sdk.Context;
using Geef.Sdk.Policies;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Geef.Sdk.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SdkSeverity = Geef.Sdk.Results.FindingSeverity;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// D-085: the three safeguards of the review loop. Each is its own switch and ships off; with all three off the loop is built and
/// behaves exactly as before. The pipeline tests run the real factory with scripted reviewers that behave like the profile-based
/// ones (blocking findings as <see cref="ReviewDecision.ApprovedWithWarnings"/>, never <see cref="ReviewDecision.Rejected"/>).
/// </summary>
public sealed class LoopSafeguardsTests
{
    private const SdkSeverity Major = SdkSeverity.Error;
    private const SdkSeverity Minor = SdkSeverity.Warning;

    [Fact]
    public void Every_switch_defaults_to_off_and_ships_off()
    {
        var defaults = new ConvergenceOptions();
        Assert.Equal((LoopSwitch.Off, LoopSwitch.Off, LoopSwitch.Off), (defaults.BestDraft, defaults.WriterBlockingOnly, defaults.StopWithoutProgress));
        Assert.Equal(LoopSafeguards.None, LoopSafeguards.From(defaults));

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();
        Assert.Equal("Off", configuration["Convergence:BestDraft"]);
        Assert.Equal("Off", configuration["Convergence:WriterBlockingOnly"]);
        Assert.Equal("Off", configuration["Convergence:StopWithoutProgress"]);
        var shipped = configuration.GetSection("Convergence").Get<ConvergenceOptions>()!;
        Assert.Equal(LoopSafeguards.None, LoopSafeguards.From(shipped));
    }

    [Fact]
    public void Each_switch_maps_on_its_own()
    {
        Assert.Equal(new LoopSafeguards(true, false, false), LoopSafeguards.From(new ConvergenceOptions { BestDraft = LoopSwitch.On }));
        Assert.Equal(new LoopSafeguards(false, true, false), LoopSafeguards.From(new ConvergenceOptions { WriterBlockingOnly = LoopSwitch.On }));
        Assert.Equal(new LoopSafeguards(false, false, true), LoopSafeguards.From(new ConvergenceOptions { StopWithoutProgress = LoopSwitch.On }));
    }

    [Fact]
    public void Blocking_findings_leave_out_minor_ones_and_those_of_failed_reviewers_but_keep_everything_a_rejecting_reviewer_says()
    {
        var round = Round(
            Review("a", ReviewDecision.ApprovedWithWarnings, ("a-major", Major), ("a-minor", Minor), ("a-critical", SdkSeverity.Critical)),
            Review("b", ReviewDecision.Failed, ("b-outage", Major)),
            Review("c", ReviewDecision.Rejected, ("c-minor", Minor)),
            Review("d", ReviewDecision.Rejected));

        Assert.Equal(["a-major", "a-critical", "c-minor"], round.BlockingFindings(Major).Select(f => f.Message).ToList());
        Assert.Equal(4, round.BlockingCount(Major));
        Assert.Equal(new LoopRank(1, 4), round.Rank(Major));
    }

    [Theory]
    [MemberData(nameof(RoundsForPolicy))]
    public void A_round_has_no_blocking_findings_exactly_when_the_loop_policy_approves_it(string label, EvaluationAggregate round)
    {
        var policy = ConvergencePolicyBuilder.Build(new ConvergenceOptions { MaxIterations = 10 }, null);
        var history = new IterationHistory();
        history.Add(new IterationRecord { Iteration = 1, StartedAt = DateTimeOffset.UtcNow, ExecutionDuration = TimeSpan.Zero, EvaluationResult = round });

        var approved = policy.Evaluate(history, round, TimeSpan.Zero) == ConvergenceDecision.Approved;

        Assert.True(approved == (round.BlockingCount(ConvergencePolicyBuilder.BlockingSeverity) == 0), label);
    }

    public static TheoryData<string, EvaluationAggregate> RoundsForPolicy() => new()
    {
        { "only minor", Round(Review("a", ReviewDecision.ApprovedWithWarnings, ("m", Minor))) },
        { "one major", Round(Review("a", ReviewDecision.ApprovedWithWarnings, ("M", Major))) },
        { "failed reviewer with an outage finding", Round(Review("a", ReviewDecision.Failed, ("o", Major)), Review("b", ReviewDecision.Approved)) },
        { "rejected without findings", Round(Review("a", ReviewDecision.Rejected)) },
        { "retry suggested with a minor finding", Round(Review("a", ReviewDecision.RetrySuggested, ("m", Minor))) },
        { "all approved", Round(Review("a", ReviewDecision.Approved), Review("b", ReviewDecision.NotApplicable)) },
    };

    [Fact]
    public void The_best_round_has_the_fewest_blocking_findings_the_most_recent_on_a_tie_and_a_failed_reviewer_ranks_first()
    {
        Assert.Equal(2, History(Counts(3, 1, 2)).BestRound(Major)!.Iteration);
        Assert.Equal(3, History(Counts(3, 1, 1)).BestRound(Major)!.Iteration);

        var withOutage = History(
            Round(Review("a", ReviewDecision.ApprovedWithWarnings, ("1", Major), ("2", Major))),
            Round(Review("a", ReviewDecision.Failed, ("outage", Major))),
            Round(Review("a", ReviewDecision.ApprovedWithWarnings, ("1", Major), ("2", Major), ("3", Major))));
        Assert.Equal(1, withOutage.BestRound(Major)!.Iteration);
        Assert.Null(new IterationHistory().BestRound(Major));
    }

    [Theory]
    [InlineData(new[] { 3, 3 }, false)]
    [InlineData(new[] { 3, 2, 1 }, false)]
    [InlineData(new[] { 3, 3, 3 }, true)]
    [InlineData(new[] { 3, 4, 2 }, false)]
    [InlineData(new[] { 3, 2, 4 }, false)]
    [InlineData(new[] { 3, 2, 4, 2 }, true)]
    [InlineData(new[] { 3, 1, 2, 1 }, true)]
    [InlineData(new[] { 5, 4, 4, 3 }, false)]
    public void Two_rounds_without_a_new_best_mean_no_progress(int[] counts, bool expected)
    {
        Assert.Equal(expected, History(Counts(counts)).LastTwoRoundsWithoutProgress(Major));
    }

    [Fact]
    public void The_stop_sentence_says_what_the_rule_checked()
    {
        Assert.Equal(RunStopMessageExtensions.NoProgressStopMessage, LoopStopReasons.NoProgress.ToRunStopMessage());
        Assert.Equal("Two more rounds did not reduce the open points, so the work was stopped. The best version so far was kept.",
            ((ConvergenceDecision?)LoopStopReasons.NoProgress).ToBestVersionKeptMessage());
        Assert.False(Enum.IsDefined(LoopStopReasons.NoProgress));
    }

    [Fact]
    public async Task With_every_switch_off_the_loop_runs_exactly_as_without_safeguards()
    {
        var script = Script(Counts(2, 1, 3), minorEveryRound: true);

        var baseline = await RunAsync(script, maxRounds: 3, safeguards: null);
        var allOff = await RunAsync(script, maxRounds: 3, safeguards: LoopSafeguards.From(new ConvergenceOptions()));

        Assert.Equal(baseline.Result.Output.Markdown, allOff.Result.Output.Markdown);
        Assert.Equal(baseline.Result.StopReason, allOff.Result.StopReason);
        Assert.Equal(baseline.Result.TotalIterations, allOff.Result.TotalIterations);
        Assert.Equal(baseline.Result.ToDeliveredOutcome(), allOff.Result.ToDeliveredOutcome());
        Assert.Equal(baseline.WriterPrompts, allOff.WriterPrompts);
        Assert.False(allOff.Result.FinalContext.TryGet(AtelierContextKeys.DeliveredRound, out _));
        Assert.Contains(RoundDraftClient.DraftOf(3), allOff.Result.Output.Markdown);
        Assert.Equal(new DeliveredOutcome(RunOutcome.DeliveredWithOpenPoints, 3), allOff.Result.ToDeliveredOutcome());
    }

    [Fact]
    public async Task Best_draft_delivers_and_records_the_round_with_the_fewest_blocking_findings()
    {
        var run = await RunAsync(Script(Counts(2, 1, 3)), maxRounds: 3, safeguards: new LoopSafeguards(true, false, false));

        Assert.False(run.Result.Success);
        Assert.Equal(ConvergenceDecision.StopMaxAttemptsReached, run.Result.StopReason);
        Assert.Equal(3, run.Result.History.BestEffortIteration());
        Assert.Contains(RoundDraftClient.DraftOf(2), run.Result.Output.Markdown);
        Assert.Equal(2, run.Result.Output.IterationCount);
        Assert.Equal(new DeliveredOutcome(RunOutcome.DeliveredWithOpenPoints, 2), run.Result.ToDeliveredOutcome());
    }

    [Fact]
    public async Task Best_draft_takes_the_most_recent_round_on_a_tie()
    {
        var run = await RunAsync(Script(Counts(2, 1, 1)), maxRounds: 3, safeguards: new LoopSafeguards(true, false, false));

        Assert.Contains(RoundDraftClient.DraftOf(3), run.Result.Output.Markdown);
        Assert.Equal(new DeliveredOutcome(RunOutcome.DeliveredWithOpenPoints, 3), run.Result.ToDeliveredOutcome());
    }

    [Fact]
    public async Task Best_draft_leaves_an_approved_loop_alone()
    {
        var run = await RunAsync(Script(Counts(1, 2, 0)), maxRounds: 3, safeguards: new LoopSafeguards(true, false, false));

        Assert.True(run.Result.Success);
        Assert.Contains(RoundDraftClient.DraftOf(3), run.Result.Output.Markdown);
        Assert.Equal(new DeliveredOutcome(RunOutcome.Converged, 3), run.Result.ToDeliveredOutcome());
        Assert.False(run.Result.FinalContext.TryGet(AtelierContextKeys.DeliveredRound, out _));
    }

    [Fact]
    public async Task Best_draft_leaves_an_approved_loop_alone_also_when_a_reviewer_failed_in_its_last_round()
    {
        var run = await RunAsync(Script(Counts(2, 0)), maxRounds: 3, safeguards: new LoopSafeguards(true, false, false), failClarityInRounds: [2]);

        Assert.True(run.Result.Success);
        Assert.Contains(RoundDraftClient.DraftOf(2), run.Result.Output.Markdown);
        Assert.Equal(new DeliveredOutcome(RunOutcome.Converged, 2), run.Result.ToDeliveredOutcome());
    }

    [Fact]
    public async Task A_resumed_loop_takes_the_safeguards_as_well()
    {
        var writer = new RoundDraftClient();
        var runner = AtelierPipelineFactory.BuildWithSeedDraft(
            Snapshot(maxRounds: 6) with { Reviewers = [SystemCrew.BriefingFidelityProfile] },
            new TestLlmClientResolver(new ReviewingClient(writer, Counts(2, 3, 4, 1))),
            Options.Create(new ConvergenceOptions()),
            "Seed text.",
            new Geef.Atelier.Tests.Fakes.InMemoryRunWarnings(),
            loopSafeguards: new LoopSafeguards(true, false, true));

        var result = await runner.RunAsync("Write a short test text.", CancellationToken.None);

        Assert.Equal(LoopStopReasons.NoProgress, result.StopReason);
        Assert.Equal(3, result.TotalIterations);
        Assert.Contains(RoundDraftClient.DraftOf(1), result.Output.Markdown);
        Assert.Equal(new DeliveredOutcome(RunOutcome.DeliveredWithOpenPoints, 1), result.ToDeliveredOutcome());
    }

    [Fact]
    public async Task Best_draft_does_not_prefer_a_round_whose_reviewer_failed()
    {
        var script = Script(Counts(2, 1, 3));
        var run = await RunAsync(script, maxRounds: 3, safeguards: new LoopSafeguards(true, false, false), failClarityInRounds: [2]);

        Assert.Single(run.Result.History.Records[1].EvaluationResult.FailedReviewers);
        Assert.Contains(RoundDraftClient.DraftOf(1), run.Result.Output.Markdown);
        Assert.Equal(new DeliveredOutcome(RunOutcome.DeliveredWithOpenPoints, 1), run.Result.ToDeliveredOutcome());
    }

    [Fact]
    public async Task Stop_without_progress_ends_after_two_rounds_without_a_new_best()
    {
        var run = await RunAsync(Script(Counts(2, 3, 2, 1, 1, 1)), maxRounds: 6, safeguards: new LoopSafeguards(false, false, true));

        Assert.False(run.Result.Success);
        Assert.Equal(LoopStopReasons.NoProgress, run.Result.StopReason);
        Assert.Equal(3, run.Result.TotalIterations);
        Assert.Equal(3, run.WriterPrompts.Count);
        Assert.Contains(RoundDraftClient.DraftOf(3), run.Result.Output.Markdown);
        Assert.Equal(new DeliveredOutcome(RunOutcome.DeliveredWithOpenPoints, 3), run.Result.ToDeliveredOutcome());
    }

    [Fact]
    public async Task Stop_without_progress_keeps_going_while_rounds_get_better_and_never_overrides_approval_or_the_round_limit()
    {
        var improving = await RunAsync(Script(Counts(4, 3, 3, 2, 2)), maxRounds: 5, safeguards: new LoopSafeguards(false, false, true));
        Assert.Equal(ConvergenceDecision.StopMaxAttemptsReached, improving.Result.StopReason);
        Assert.Equal(5, improving.Result.TotalIterations);

        var approved = await RunAsync(Script(Counts(2, 2, 0)), maxRounds: 5, safeguards: new LoopSafeguards(false, false, true));
        Assert.True(approved.Result.Success);
        Assert.Equal(3, approved.Result.TotalIterations);

        var limit = await RunAsync(Script(Counts(2, 2, 2)), maxRounds: 3, safeguards: new LoopSafeguards(false, false, true));
        Assert.Equal(ConvergenceDecision.StopMaxAttemptsReached, limit.Result.StopReason);
    }

    [Fact]
    public async Task Stop_and_best_draft_together_deliver_the_best_of_the_rounds_that_ran()
    {
        var run = await RunAsync(Script(Counts(2, 3, 4, 1)), maxRounds: 6, safeguards: new LoopSafeguards(true, false, true));

        Assert.Equal(LoopStopReasons.NoProgress, run.Result.StopReason);
        Assert.Equal(3, run.Result.TotalIterations);
        Assert.Contains(RoundDraftClient.DraftOf(1), run.Result.Output.Markdown);
        Assert.Equal(new DeliveredOutcome(RunOutcome.DeliveredWithOpenPoints, 1), run.Result.ToDeliveredOutcome());
    }

    [Fact]
    public async Task Writer_blocking_only_hands_over_neither_minor_findings_nor_outages()
    {
        var script = Script(Counts(1, 0), minorEveryRound: true);

        var off = await RunAsync(script, maxRounds: 2, safeguards: null, failClarityInRounds: [1]);
        var on = await RunAsync(script, maxRounds: 2, safeguards: new LoopSafeguards(false, true, false), failClarityInRounds: [1]);

        var offRevision = off.WriterPrompts[1];
        Assert.Contains(BlockingText(1, 1), offRevision);
        Assert.Contains(MinorText(1), offRevision);
        Assert.Contains("infrastructure error", offRevision);

        var onRevision = on.WriterPrompts[1];
        Assert.Contains(BlockingText(1, 1), onRevision);
        Assert.DoesNotContain(MinorText(1), onRevision);
        Assert.DoesNotContain("infrastructure error", onRevision);
        Assert.Equal(off.WriterPrompts[0], on.WriterPrompts[0]);
    }

    private static string BlockingText(int round, int index) => $"Blocking point {index} of round {round}.";

    private static string MinorText(int round) => $"Minor point of round {round}.";

    private static Dictionary<int, int> Counts(params int[] counts) =>
        counts.Select((count, index) => (Round: index + 1, Count: count)).ToDictionary(x => x.Round, x => x.Count);

    private static RoundScript Script(Dictionary<int, int> blockingPerRound, bool minorEveryRound = false) => new(blockingPerRound, minorEveryRound);

    private static async Task<LoopRun> RunAsync(RoundScript script, int maxRounds, LoopSafeguards? safeguards, int[]? failClarityInRounds = null)
    {
        var snapshot = Snapshot(maxRounds);
        var reviewers = new Dictionary<string, IReviewer>
        {
            [SystemCrew.BriefingFidelityProfile.Name] = new ScriptedReviewer(SystemCrew.BriefingFidelityProfile.Name, script),
            [SystemCrew.ClarityProfile.Name] = new OutageReviewer(SystemCrew.ClarityProfile.Name, failClarityInRounds ?? []),
        };
        var writer = new RoundDraftClient();
        var runner = AtelierPipelineFactory.Build(
            snapshot,
            new TestLlmClientResolver(writer),
            Options.Create(new ConvergenceOptions()),
            new Geef.Atelier.Tests.Fakes.InMemoryRunWarnings(),
            specialReviewerResolver: profile => reviewers[profile.Name],
            loopSafeguards: safeguards);

        var result = await runner.RunAsync("Write a short test text.", CancellationToken.None);
        return new LoopRun(result, writer.Prompts.ToList());
    }

    private static CrewSnapshot Snapshot(int maxRounds) => new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: SystemCrew.KlassikTemplateName,
        Executor: SystemCrew.DefaultExecutorProfile,
        Reviewers: [SystemCrew.BriefingFidelityProfile, SystemCrew.ClarityProfile],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: new ConvergencePolicyOverride(MaxIterations: maxRounds, AbortOnCritical: false, DetectRegression: false, StagnationThreshold: null),
        Advisors: Array.Empty<Geef.Atelier.Core.Domain.Crew.Advisors.AdvisorProfile>());

    private static EvaluationAggregate Round(params ReviewResult[] reviews) => new() { Reviews = reviews };

    private static ReviewResult Review(string name, ReviewDecision decision, params (string Message, SdkSeverity Severity)[] findings) => new()
    {
        ReviewerName = name,
        Decision = decision,
        Findings = findings.Select(f => Finding(name, f.Message, f.Severity)).ToList(),
        Duration = TimeSpan.Zero,
    };

    private static Finding Finding(string reviewer, string message, SdkSeverity severity) => new()
    {
        ReviewerName = reviewer,
        Fingerprint = $"{reviewer}:{message}",
        Message = message,
        Severity = severity,
        Category = "test",
        ArtifactReference = "draft",
        Metadata = new Dictionary<string, object>(),
    };

    private static IterationHistory History(Dictionary<int, int> counts) =>
        History(counts.OrderBy(c => c.Key).Select(c => Round(Review("a", c.Value == 0 ? ReviewDecision.Approved : ReviewDecision.ApprovedWithWarnings,
            Enumerable.Range(1, c.Value).Select(i => (BlockingText(c.Key, i), Major)).ToArray()))).ToArray());

    private static IterationHistory History(params EvaluationAggregate[] rounds)
    {
        var history = new IterationHistory();
        for (var i = 0; i < rounds.Length; i++)
            history.Add(new IterationRecord { Iteration = i + 1, StartedAt = DateTimeOffset.UtcNow, ExecutionDuration = TimeSpan.Zero, EvaluationResult = rounds[i] });
        return history;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed record RoundScript(Dictionary<int, int> BlockingPerRound, bool MinorEveryRound);

    private sealed record LoopRun(GeefPipelineResult<FinalizedDocument> Result, IReadOnlyList<string> WriterPrompts);

    /// <summary>Writes "Draft of round N" for the N-th writing call and keeps every prompt it got.</summary>
    private sealed class RoundDraftClient : ILlmClient
    {
        private int _round;

        public ConcurrentQueue<string> Prompts { get; } = new();

        public static string DraftOf(int round) => $"Draft of round {round}.";

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Prompts.Enqueue(request.UserPrompt);
            return Task.FromResult(FakeLlmClient.MakeTextResponse(DraftOf(Interlocked.Increment(ref _round))));
        }
    }

    /// <summary>
    /// Hands writer calls to <paramref name="writer"/> and answers each review call of the real profile-based reviewer with the scripted
    /// number of major findings for the current round.
    /// </summary>
    private sealed class ReviewingClient(RoundDraftClient writer, Dictionary<int, int> majorsPerRound) : ILlmClient
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            if (request.Tools is null || request.Tools.Count == 0)
                return writer.CompleteAsync(request, ct);

            var round = writer.Prompts.Count;
            var findings = string.Join(",", Enumerable.Range(1, majorsPerRound[round])
                .Select(i => $$"""{"severity":"major","message":"{{BlockingText(round, i)}}"}"""));
            return Task.FromResult(new LlmResponse
            {
                Text = "",
                FinishReason = "tool_calls",
                ToolName = "submit_review",
                ToolArgumentsJson = $$"""{"approved":false,"findings":[{{findings}}]}""",
                TokenUsage = new LlmTokenUsage { InputTokens = 8, OutputTokens = 15 },
            });
        }
    }

    /// <summary>Reports the scripted number of major findings per round (new texts each round) like a profile-based reviewer.</summary>
    private sealed class ScriptedReviewer(string name, RoundScript script) : IReviewer
    {
        public string Name => name;

        public int Priority => 0;

        public Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken cancellationToken)
        {
            var round = context.GetRequired(GeefKeys.CurrentIteration);
            var findings = Enumerable.Range(1, script.BlockingPerRound.GetValueOrDefault(round))
                .Select(i => (BlockingText(round, i), Major))
                .ToList();
            if (script.MinorEveryRound)
                findings.Add((MinorText(round), Minor));
            return Task.FromResult(Review(name, findings.Count == 0 ? ReviewDecision.Approved : ReviewDecision.ApprovedWithWarnings, findings.ToArray()));
        }
    }

    /// <summary>Approves without findings, except in the listed rounds, where it fails with an exception (an outage).</summary>
    private sealed class OutageReviewer(string name, int[] failInRounds) : IReviewer
    {
        public string Name => name;

        public int Priority => 0;

        public Task<ReviewResult> ReviewAsync(IRunContext context, CancellationToken cancellationToken)
        {
            if (failInRounds.Contains(context.GetRequired(GeefKeys.CurrentIteration)))
                throw new InvalidOperationException("Simulated outage.");
            return Task.FromResult(Review(name, ReviewDecision.Approved));
        }
    }
}
