using System.Text;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// <see cref="IOrderService.ReviseAsync"/> and the shown version over the real wiring with the orchestrator held, so runs stay pending:
/// every condition of a change, exactly one new version for calls at the same time, and the shown version stays visible and
/// exportable while the new one is pending, running, failed or stopped.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class OrderServiceReviseTests(PostgresFixture postgres)
{
    private const string Change = "The meeting is on 5 May, not 12 May.";
    private const string V1Text = "Dear team,\n\nthe meeting is on 12 May.\n\nBest regards";
    private const string ProfileBlock = BriefingSections.ProfileHeading + "\nName: Anna Beispiel";

    [Fact]
    public async Task A_change_creates_version_two_seeded_with_the_shown_text_on_the_direct_path()
    {
        await using var host = await StartAsync();
        var user = NewUser();
        var submission = await SubmitAsync(host, new SubmitOrderRequest("Write an article.", user, OrderEntry.Web,
            Mode: OrderMode.Careful, CrewTemplateName: SystemCrew.KlassikTemplateName));
        var v1Briefing = "Write an article." + BriefingSections.Separator + ProfileBlock;
        await DeliverAsync(host, submission.RunId!.Value, V1Text, briefing: v1Briefing);

        var revision = await ReviseAsync(host, new ReviseOrderRequest(submission.OrderId, user, "  " + Change + "\n", [RevisionQuick.Shorter], OrderEntry.Mcp));

        Assert.Equal((OrderDisplayState.Queued, 2), (revision.State, revision.Version));
        Assert.Empty(revision.Notices);
        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == revision.RunId));
        var expectedChange = Change + " " + RevisionQuick.Shorter.Instruction();
        Assert.Equal((submission.OrderId, 2, RunKind.Standard, RunStatus.Pending), (run.OrderId!.Value, run.OrderVersion!.Value, run.Kind, run.Status));
        Assert.Equal(submission.RunId, run.ParentRunId);
        Assert.Equal(V1Text, run.SeedDraftText);
        Assert.Equal(expectedChange, run.RevisionHint);
        Assert.Equal("Write an article." + BriefingSections.Separator + RevisionBrief.Heading + "\n- (version 2, the change to make now) " + expectedChange + "\n\n" + ProfileBlock,
            run.BriefingText);
        Assert.Equal(user, run.CreatedByUser);
        Assert.Equal(SystemCrew.DirectTemplateName, run.CrewTemplateName);
        Assert.Equal(SystemCrew.DirectTemplateName, CrewSnapshot.Deserialize(run.CrewSnapshot!)!.TemplateName);
        var order = await host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == submission.OrderId));
        Assert.Equal((OrderState.Ready, OrderMode.Careful), (order.State, order.Mode));
        Assert.Null(order.ClarifyJson);

        var view = (await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(submission.OrderId, user)))!;
        Assert.Equal([null, $"{Change} (Shorter)"], view.Runs.Select(r => r.ChangeRequested));
        Assert.Equal(OrderDisplayState.Queued, view.Display.State);
    }

    [Fact]
    public async Task Each_condition_of_a_change_is_checked_and_a_refusal_stores_nothing()
    {
        await using var host = await StartAsync(o => o.MaxVersionsPerOrder = 3);
        var user = NewUser();

        var delivered = await SubmitAsync(host, new SubmitOrderRequest("Write a slogan.", user, OrderEntry.Web));
        await DeliverAsync(host, delivered.RunId!.Value, "Fresh ideas, fast.");
        await AssertRefusedAsync(host, new(delivered.OrderId, NewUser(), Change, [], OrderEntry.Web), RevisionLines.NotFound);
        await AssertRefusedAsync(host, new(Guid.NewGuid(), user, Change, [], OrderEntry.Web), RevisionLines.NotFound);
        await AssertRefusedAsync(host, new(delivered.OrderId, null, Change, [], OrderEntry.Web), RevisionLines.NotFound);
        await AssertRefusedAsync(host, new(delivered.OrderId, user, "  \n ", [], OrderEntry.Web), RevisionLines.NoChangeGiven);
        await AssertRefusedAsync(host, new(delivered.OrderId, user, null, [], OrderEntry.Web), RevisionLines.NoChangeGiven);
        await AssertRefusedAsync(host, new(delivered.OrderId, user, new string('x', 2_001), [], OrderEntry.Web),
            "Your change is too long (2,001 characters, the limit is 2,000). Please shorten it.");

        var busy = await SubmitAsync(host, new SubmitOrderRequest("Write a poem.", user, OrderEntry.Web));
        var busyAnswer = await ReviseAsync(host, new(busy.OrderId, user, Change, [], OrderEntry.Web));
        Assert.Equal((busy.OrderId, OrderDisplayState.Queued, busy.RunId, 1), (busyAnswer.OrderId, busyAnswer.State, busyAnswer.RunId, busyAnswer.Version));
        Assert.Equal([RevisionLines.Busy], busyAnswer.Notices);
        Assert.Equal(1, await RunCountAsync(host, busy.OrderId));

        var finishing = await SubmitAsync(host, new SubmitOrderRequest("Write a toast.", user, OrderEntry.Web));
        await host.QueryAsync(db => db.Runs.Where(r => r.Id == finishing.RunId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, RunStatus.Completed).SetProperty(r => r.FinalText, "Cheers.").SetProperty(r => r.CompletedAt, DateTimeOffset.UtcNow)));
        Assert.Equal([RevisionLines.Busy], (await ReviseAsync(host, new(finishing.OrderId, user, Change, [], OrderEntry.Web))).Notices);
        Assert.Equal(1, await RunCountAsync(host, finishing.OrderId));

        var noText = await SubmitAsync(host, new SubmitOrderRequest("Write a rhyme.", user, OrderEntry.Web));
        await host.QueryAsync(db => db.Runs.Where(r => r.Id == noText.RunId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, RunStatus.Failed).SetProperty(r => r.Outcome, RunOutcome.Failed).SetProperty(r => r.CompletedAt, DateTimeOffset.UtcNow.AddMinutes(-5))));
        await AssertRefusedAsync(host, new(noText.OrderId, user, Change, [], OrderEntry.Web), RevisionLines.NothingToChange);

        var full = await SubmitAsync(host, new SubmitOrderRequest("Write a motto.", user, OrderEntry.Web));
        await DeliverAsync(host, full.RunId!.Value, "Onward.");
        await host.WithScopeAsync(async sp =>
        {
            var repository = sp.GetRequiredService<IOrderRepository>();
            await repository.AddRunAsync(full.OrderId, Run(user, status: RunStatus.Failed, at: DateTimeOffset.UtcNow.AddMinutes(-4)));
            return await repository.AddRunAsync(full.OrderId, Run(user, status: RunStatus.Aborted, at: DateTimeOffset.UtcNow.AddMinutes(-3)));
        });
        await AssertRefusedAsync(host, new(full.OrderId, user, Change, [], OrderEntry.Web),
            "This order already has 3 versions, the most one order can have. Please start a new order for further changes.");

        await host.QueryAsync(db => db.Orders.Where(o => o.Id == delivered.OrderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.State, OrderState.Cancelled)));
        await AssertRefusedAsync(host, new(delivered.OrderId, user, Change, [], OrderEntry.Web), RevisionLines.Stopped);
    }

    [Fact]
    public async Task Changing_is_off_unless_switched_on_globally_or_for_the_owner()
    {
        var tester = NewUser();
        await using var host = await AtelierEndToEndHost.StartAsync(postgres, services =>
            services.PostConfigure<OrderOptions>(o => o.ReviseOverrides[tester] = ReviseMode.On));
        var other = NewUser();

        var theirs = await SubmitAsync(host, new SubmitOrderRequest("Write a slogan.", other, OrderEntry.Web));
        await DeliverAsync(host, theirs.RunId!.Value, "Fresh ideas, fast.");
        await AssertRefusedAsync(host, new(theirs.OrderId, other, Change, [], OrderEntry.Web), RevisionLines.NotAvailable);

        var mine = await SubmitAsync(host, new SubmitOrderRequest("Write a slogan.", tester, OrderEntry.Web));
        await DeliverAsync(host, mine.RunId!.Value, "Fresh ideas, fast.");
        Assert.Equal(2, (await ReviseAsync(host, new(mine.OrderId, tester, null, [RevisionQuick.Friendlier], OrderEntry.Web))).Version);
    }

    [Fact]
    public async Task Ten_changes_at_the_same_time_start_exactly_one_version()
    {
        await using var host = await StartAsync();
        var user = NewUser();
        var submission = await SubmitAsync(host, new SubmitOrderRequest("Write a slogan.", user, OrderEntry.Web));
        await DeliverAsync(host, submission.RunId!.Value, "Fresh ideas, fast.");
        using var start = new ManualResetEventSlim(false);

        var calls = Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            start.Wait();
            return await ReviseAsync(host, new(submission.OrderId, user, Change + " " + i, [], OrderEntry.Web));
        })).ToList();
        start.Set();
        var answers = await Task.WhenAll(calls);

        var created = Assert.Single(answers, a => a.Notices.Count == 0);
        Assert.Equal(2, created.Version);
        Assert.All(answers.Where(a => a != created), a =>
        {
            Assert.Equal([RevisionLines.Busy], a.Notices);
            Assert.NotEqual(OrderDisplayState.Delivered, a.State);
        });
        var versions = await host.QueryAsync(db => db.Runs.Where(r => r.OrderId == submission.OrderId).Select(r => r.OrderVersion!.Value).OrderBy(v => v).ToListAsync());
        Assert.Equal([1, 2], versions);
        Assert.Equal(1, await host.QueryAsync(db => db.Runs.CountAsync(r => r.OrderId == submission.OrderId && r.RevisionHint != null)));
    }

    [Fact]
    public async Task The_shown_version_stays_visible_and_exportable_while_the_new_one_runs_fails_or_is_stopped()
    {
        await using var host = await StartAsync();
        var user = NewUser();
        var submission = await SubmitAsync(host, new SubmitOrderRequest("Write a note to the team.", user, OrderEntry.Web));
        await DeliverAsync(host, submission.RunId!.Value, V1Text, words: 9);

        var v2 = await ReviseAsync(host, new(submission.OrderId, user, Change, [], OrderEntry.Web));
        await AssertShowsV1Async(host, submission, user, OrderDisplayState.Queued, "A new version is being written. Version 1 stays here until it is ready.");

        await host.QueryAsync(db => db.Runs.Where(r => r.Id == v2.RunId).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Running)));
        await AssertShowsV1Async(host, submission, user, OrderDisplayState.Working, "A new version is being written. Version 1 stays here until it is ready.");

        await host.QueryAsync(db => db.Runs.Where(r => r.Id == v2.RunId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, RunStatus.Failed).SetProperty(r => r.Outcome, RunOutcome.Failed).SetProperty(r => r.CompletedAt, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await AssertShowsV1Async(host, submission, user, OrderDisplayState.Failed, "Version 2 could not be finished. This is version 1.");

        await host.QueryAsync(db => db.Runs.Where(r => r.Id == v2.RunId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, RunStatus.Aborted).SetProperty(r => r.Outcome, RunOutcome.Aborted)));
        await AssertShowsV1Async(host, submission, user, OrderDisplayState.Stopped, "Version 2 could not be finished. This is version 1.");

        var v3 = await ReviseAsync(host, new(submission.OrderId, user, "Add the room number.", [], OrderEntry.Web));
        Assert.Equal(3, v3.Version);
        var v3Run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == v3.RunId));
        Assert.Equal(submission.RunId, v3Run.ParentRunId);
        Assert.Equal(V1Text, v3Run.SeedDraftText);
        Assert.Contains("- (version 3, the change to make now) Add the room number.", v3Run.BriefingText, StringComparison.Ordinal);
        Assert.DoesNotContain("(version 2)", v3Run.BriefingText, StringComparison.Ordinal);

        await DeliverAsync(host, v3.RunId!.Value, "Dear team,\n\nthe meeting is on 12 May in room 4.\n\nBest regards", words: 12);
        await host.WithScopeAsync(async sp =>
        {
            var result = (await sp.GetRequiredService<IOrderService>().GetResultAsync(submission.OrderId, user))!;
            Assert.Equal((3, 3, OrderDisplayState.Delivered), (result.Version!.Value, result.LatestVersion!.Value, result.Display.State));
            Assert.Equal(v3.RunId, result.RunId);
            Assert.Equal(1, result.AssumptionsVersion);
            Assert.Contains("room 4", result.Text, StringComparison.Ordinal);
            Assert.Null(result.Note);
            var export = (await sp.GetRequiredService<IOrderExportService>().ExportAsync(submission.OrderId, ExportFormat.Markdown, null, user))!;
            Assert.EndsWith("-v3.md", export.FileName, StringComparison.Ordinal);
            var list = Assert.Single(await sp.GetRequiredService<IOrderService>().ListAsync(user));
            Assert.Equal((12, 3), (list.WordCount!.Value, list.Version!.Value));
            return 0;
        });
    }

    private static async Task AssertShowsV1Async(AtelierEndToEndHost host, OrderSubmission submission, string user, OrderDisplayState state, string note)
    {
        await host.WithScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IOrderService>();
            var result = (await service.GetResultAsync(submission.OrderId, user))!;
            Assert.Equal(state, result.Display.State);
            Assert.Equal(V1Text, result.Text);
            Assert.Equal(9, result.WordCount);
            Assert.Equal((1, 2), (result.Version!.Value, result.LatestVersion!.Value));
            Assert.Equal(submission.RunId, result.RunId);
            Assert.Null(result.AssumptionsVersion);
            Assert.Equal(note, result.Note);

            var exports = sp.GetRequiredService<IOrderExportService>();
            var markdown = (await exports.ExportAsync(submission.OrderId, ExportFormat.Markdown, null, user))!;
            Assert.Equal(V1Text, Encoding.UTF8.GetString(markdown.Content));
            Assert.EndsWith("-v1.md", markdown.FileName, StringComparison.Ordinal);
            Assert.NotNull(await exports.ExportAsync(submission.OrderId, ExportFormat.Docx, null, user));
            Assert.Null(await exports.ExportAsync(submission.OrderId, ExportFormat.Markdown, 2, user));
            Assert.Null(await exports.ExportAsync(submission.OrderId, ExportFormat.Markdown, null, NewUser()));

            var list = Assert.Single(await service.ListAsync(user));
            Assert.Equal((state, 9, 1), (list.Display.State, list.WordCount!.Value, list.Version!.Value));

            var view = (await service.GetAsync(submission.OrderId, user))!;
            Assert.Equal(2, view.CurrentRun!.Version);
            Assert.Equal(Change, view.Runs.Single(r => r.Version == 2).ChangeRequested);
            return 0;
        });
    }

    private Task<AtelierEndToEndHost> StartAsync(Action<OrderOptions>? configure = null) =>
        AtelierEndToEndHost.StartAsync(postgres, services => services.PostConfigure<OrderOptions>(o =>
        {
            o.Revise = ReviseMode.On;
            configure?.Invoke(o);
        }));

    private static Task DeliverAsync(AtelierEndToEndHost host, Guid runId, string text, int words = 3, string? briefing = null) =>
        host.QueryAsync(db => db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, RunStatus.Completed)
            .SetProperty(r => r.Outcome, RunOutcome.Converged)
            .SetProperty(r => r.FinalText, text)
            .SetProperty(r => r.WordCount, words)
            .SetProperty(r => r.BriefingText, r => briefing ?? r.BriefingText)
            .SetProperty(r => r.CompletedAt, DateTimeOffset.UtcNow.AddMinutes(-10))));

    private static async Task AssertRefusedAsync(AtelierEndToEndHost host, ReviseOrderRequest request, string message)
    {
        var before = await host.QueryAsync(db => db.Runs.CountAsync());
        var ex = await Assert.ThrowsAsync<OrderRevisionException>(() => ReviseAsync(host, request));
        Assert.Equal(message, ex.Message);
        Assert.Equal(before, await host.QueryAsync(db => db.Runs.CountAsync()));
    }

    private static Task<int> RunCountAsync(AtelierEndToEndHost host, Guid orderId) =>
        host.QueryAsync(db => db.Runs.CountAsync(r => r.OrderId == orderId));

    private static Task<OrderRevision> ReviseAsync(AtelierEndToEndHost host, ReviseOrderRequest request) =>
        host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().ReviseAsync(request));

    private static Task<OrderSubmission> SubmitAsync(AtelierEndToEndHost host, SubmitOrderRequest request) =>
        host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SubmitAsync(request));
}
