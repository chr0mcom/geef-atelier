using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Persistence.Runs;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Web.Notifications;
using Geef.Sdk.Context;
using Geef.Sdk.Events;
using Geef.Sdk.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Persistence;

/// <summary>
/// <c>Runs.FactCheckMode</c> and the new finding columns against real Postgres: the mode is stored as text, writing it again
/// changes nothing, an unknown run is no error; the event sink stores category, code and quote of a finding.
/// </summary>
[Collection("Postgres")]
public sealed class RunFactCheckModeStoreTests(PostgresFixture fixture)
{
    [Fact]
    public async Task The_mode_is_stored_as_text_and_writing_it_again_changes_nothing()
    {
        var runId = await NewRunAsync();
        var store = new RunFactCheckModeStore(fixture.NewScopeFactory(), NullLogger<RunFactCheckModeStore>.Instance);

        await store.SetAsync(runId, FactCheckMode.Report);
        await store.SetAsync(runId, FactCheckMode.Report);

        await using var db = fixture.NewContext();
        Assert.Equal("Report", (await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId)).FactCheckMode);
        Assert.Equal(1, await db.Database.SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM "Runs" WHERE "Id" = {runId} AND "FactCheckMode" = 'Report'""").SingleAsync());
    }

    [Theory]
    [InlineData(FactCheckMode.Off)]
    [InlineData(FactCheckMode.Shadow)]
    [InlineData(FactCheckMode.On)]
    public async Task Every_mode_round_trips(FactCheckMode mode)
    {
        var runId = await NewRunAsync();
        await new RunFactCheckModeStore(fixture.NewScopeFactory(), NullLogger<RunFactCheckModeStore>.Instance).SetAsync(runId, mode);

        await using var db = fixture.NewContext();
        Assert.Equal(mode.ToString(), (await db.Runs.AsNoTracking().SingleAsync(r => r.Id == runId)).FactCheckMode);
    }

    [Fact]
    public async Task An_unknown_run_is_no_error() =>
        await new RunFactCheckModeStore(fixture.NewScopeFactory(), NullLogger<RunFactCheckModeStore>.Instance).SetAsync(Guid.NewGuid(), FactCheckMode.On);

    [Fact]
    public async Task The_sink_stores_category_code_and_quote_of_a_finding()
    {
        var runId = await NewRunAsync();
        var sink = new PostgresEventSink(runId, fixture.NewScopeFactory(), new NoOpRunNotifier(), NullLogger.Instance);
        var context = new RunContext().Set(AtelierContextKeys.CurrentDraft, "Text mit Fotos liegen bei.");
        await sink.PublishAsync(new ExecutionCompletedEvent(runId.ToString(), 1, new ExecutionResult { UpdatedContext = context }, TimeSpan.Zero, DateTimeOffset.UtcNow), default);

        var factFinding = new Finding
        {
            ReviewerName = "source-fact-check",
            Fingerprint = "fp-1",
            Message = "Not backed.",
            Severity = FindingSeverity.Warning,
            Category = "fact-check",
            Metadata = new Dictionary<string, object>
            {
                [FactFindingMetadata.Code] = FactFindingMetadata.UnsupportedCode,
                [FactFindingMetadata.Quote] = "Fotos liegen bei.",
                [FactFindingMetadata.Blocking] = true,
            },
        };
        var plain = new Finding { ReviewerName = "clarity", Fingerprint = "fp-2", Message = "Fine.", Severity = FindingSeverity.Info };
        var aggregate = new EvaluationAggregate
        {
            Reviews = [new ReviewResult { ReviewerName = "source-fact-check", Decision = ReviewDecision.Approved, Findings = [factFinding, plain] }],
        };
        await sink.PublishAsync(new EvaluationApprovedEvent(runId.ToString(), 1, aggregate, DateTimeOffset.UtcNow), default);

        await using var db = fixture.NewContext();
        var stored = await db.Findings.AsNoTracking()
            .Where(f => db.Iterations.Any(i => i.Id == f.IterationId && i.RunId == runId))
            .ToListAsync();
        var fact = Assert.Single(stored, f => f.ReviewerName == "source-fact-check");
        Assert.Equal(("fact-check", "fact.unsupported", "Fotos liegen bei."), (fact.Category, fact.Code, fact.Quote));
        var other = Assert.Single(stored, f => f.ReviewerName == "clarity");
        Assert.Equal((null, null, null), (other.Category, other.Code, other.Quote));
    }

    private async Task<Guid> NewRunAsync()
    {
        await using var db = fixture.NewContext();
        return await new RunPersistenceService(db).CreateRunAsync("Write a note.", "{}", cancellationToken: CancellationToken.None);
    }
}
