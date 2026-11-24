using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Web.Notifications;
using Geef.Sdk.Events;
using Geef.Sdk.Policies;
using Geef.Sdk.Runtime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// The reason a run stopped is stored at the run as one fixed plain sentence per stop reason; no enum name or other
/// technical word reaches the text the user sees, also for reasons without an own sentence.
/// </summary>
[Collection("Postgres")]
public sealed class RunStopMessageTests(PostgresFixture fixture)
{
    private static readonly string[] TechnicalWords = ["Pipeline", "convergence", "iteration", "reviewer", "grounding"];

    public static TheoryData<ConvergenceDecision> AllDecisions()
    {
        var data = new TheoryData<ConvergenceDecision>();
        foreach (var decision in Enum.GetValues<ConvergenceDecision>())
            data.Add(decision);
        data.Add((ConvergenceDecision)999);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllDecisions))]
    public void EveryReasonGivesAPlainSentenceWithoutEnumNames(ConvergenceDecision reason)
    {
        var message = reason.ToRunStopMessage();

        AssertPlain(message);
        Assert.EndsWith(".", message);
    }

    [Theory]
    [InlineData(ConvergenceDecision.StopStagnant, "The text did not settle after several rounds; the same points kept coming back.")]
    [InlineData(ConvergenceDecision.StopRegression, "The text got worse again after earlier fixes, so the work was stopped.")]
    [InlineData(ConvergenceDecision.EscalateToHuman, "The open points could not be resolved automatically and need a person to look at them.")]
    [InlineData(ConvergenceDecision.AbortReviewerUnavailable, "A check could not be carried out, so the work was stopped.")]
    [InlineData(ConvergenceDecision.Continue, RunStopMessageExtensions.UnknownStopMessage)]
    public async Task FailedEventStoresTheFixedSentenceAtTheRun(ConvergenceDecision reason, string expected)
    {
        await using var db = fixture.NewContext();
        var runId = await new RunPersistenceService(db).CreateRunAsync("brief", "{}", cancellationToken: CancellationToken.None);
        var sink = new PostgresEventSink(runId, fixture.NewScopeFactory(), new NoOpRunNotifier(), NullLogger.Instance);

        await sink.PublishAsync(
            new PipelineFailedEvent(runId.ToString(), reason, 3, new IterationHistory(), DateTimeOffset.UtcNow),
            CancellationToken.None);

        await using var verify = fixture.NewContext();
        var run = await verify.Runs.SingleAsync(r => r.Id == runId);
        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Equal(expected, run.ErrorMessage);
        AssertPlain(run.ErrorMessage!);
    }

    [Fact]
    public void MissingReasonGivesTheNeutralSentenceAndTheBestVersionNote()
    {
        ConvergenceDecision? missing = null;

        Assert.Equal(RunStopMessageExtensions.UnknownStopMessage, missing.ToRunStopMessage());
        Assert.Equal(
            $"{RunStopMessageExtensions.UnknownStopMessage} {RunStopMessageExtensions.BestVersionKeptMessage}",
            missing.ToBestVersionKeptMessage());
        AssertPlain(((ConvergenceDecision?)ConvergenceDecision.StopMaxAttemptsReached).ToBestVersionKeptMessage());
    }

    private static void AssertPlain(string message)
    {
        foreach (var name in Enum.GetNames<ConvergenceDecision>())
            Assert.DoesNotContain(name, message);
        foreach (var word in TechnicalWords)
            Assert.DoesNotContain(word, message, StringComparison.OrdinalIgnoreCase);
    }
}
