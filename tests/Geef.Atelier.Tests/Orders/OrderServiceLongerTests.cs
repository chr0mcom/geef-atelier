using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// Changing a text without the button “Longer” over the real wiring with the orchestrator held: a change asking for “Longer” is refused
/// with the sentence that a longer text needs new details and stores nothing, and a version that became clearly longer than the text it
/// changed carries the fixed sentence in its result, worked out from the stored texts.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class OrderServiceLongerTests(PostgresFixture postgres)
{
    private const string V1Text = "Dear team,\n\nthe meeting is on 12 May.\n\nBest regards";

    [Fact]
    public async Task Asking_for_longer_is_refused_with_the_sentence_and_stores_nothing()
    {
        await using var host = await StartAsync();
        var user = NewUser();
        var submission = await SubmitAsync(host, user);
        await DeliverAsync(host, submission.RunId!.Value, V1Text);

        await AssertRefusedAsync(host, new(submission.OrderId, user, null, [RevisionQuick.Longer], OrderEntry.Web));
        await AssertRefusedAsync(host, new(submission.OrderId, user, "Add the room number.", [RevisionQuick.Longer], OrderEntry.Mcp));
        await AssertRefusedAsync(host, new(submission.OrderId, user, null, [RevisionQuick.Shorter, RevisionQuick.Longer], OrderEntry.Web));

        var own = await ReviseAsync(host, new(submission.OrderId, user, "Bitte mach den Text länger.", [], OrderEntry.Web));
        Assert.Equal(2, own.Version);
        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == own.RunId));
        Assert.Equal("Bitte mach den Text länger.", run.RevisionHint);
    }

    [Fact]
    public async Task While_changing_is_switched_off_longer_gets_the_same_answer_as_everything_else()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var submission = await SubmitAsync(host, user);
        await DeliverAsync(host, submission.RunId!.Value, V1Text);

        var ex = await Assert.ThrowsAsync<OrderRevisionException>(() => ReviseAsync(host, new(submission.OrderId, user, null, [RevisionQuick.Longer], OrderEntry.Web)));

        Assert.Equal(RevisionLines.NotAvailable, ex.Message);
    }

    [Fact]
    public async Task A_clearly_longer_version_carries_the_sentence_and_one_that_kept_its_length_does_not()
    {
        await using var host = await StartAsync();
        var user = NewUser();
        var submission = await SubmitAsync(host, user);
        await DeliverAsync(host, submission.RunId!.Value, V1Text);
        Assert.Null((await ResultAsync(host, submission.OrderId, user)).LengthNotice);

        var v2 = await ReviseAsync(host, new(submission.OrderId, user, "Bitte mach den Text länger.", [], OrderEntry.Web));
        var v2Text = "Dear team,\n\nthe meeting is on 12 May in the hall.\n\nBest regards";
        await DeliverAsync(host, v2.RunId!.Value, v2Text);
        var second = await ResultAsync(host, submission.OrderId, user);
        Assert.Equal(2, second.Version);
        Assert.Equal("This version is noticeably longer than the one before. Please check the new parts: the writer may have added things you did not ask for.", second.LengthNotice);

        var v3 = await ReviseAsync(host, new(submission.OrderId, user, "The meeting is on 5 May, not 12 May.", [], OrderEntry.Mcp));
        await DeliverAsync(host, v3.RunId!.Value, v2Text.Replace("12 May", "5 May", StringComparison.Ordinal));
        var third = await ResultAsync(host, submission.OrderId, user);
        Assert.Equal(3, third.Version);
        Assert.Null(third.LengthNotice);

        var v4 = await ReviseAsync(host, new(submission.OrderId, user, "Add the room number.", [], OrderEntry.Web));
        Assert.Null((await ResultAsync(host, submission.OrderId, user)).LengthNotice);
        await host.QueryAsync(db => db.Runs.Where(r => r.Id == v4.RunId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, RunStatus.Failed).SetProperty(r => r.Outcome, RunOutcome.Failed).SetProperty(r => r.CompletedAt, DateTimeOffset.UtcNow)));
        var afterFailure = await ResultAsync(host, submission.OrderId, user);
        Assert.Equal((3, 4), (afterFailure.Version!.Value, afterFailure.LatestVersion!.Value));
        Assert.Null(afterFailure.LengthNotice);
    }

    [Fact]
    public async Task A_stored_older_version_gets_the_sentence_from_its_texts_and_a_run_without_a_change_request_never()
    {
        await using var host = await StartAsync();
        var user = NewUser();
        var submission = await SubmitAsync(host, user);
        await DeliverAsync(host, submission.RunId!.Value, V1Text);
        var v2 = await ReviseAsync(host, new(submission.OrderId, user, "Make the text about a third longer without adding new facts.", [], OrderEntry.Web));
        await DeliverAsync(host, v2.RunId!.Value, "Dear team,\n\nthe meeting is on 12 May. We look forward to it.\n\nBest regards");

        Assert.Equal(RevisionLines.LongerThanBefore, (await ResultAsync(host, submission.OrderId, user)).LengthNotice);

        await host.QueryAsync(db => db.Runs.Where(r => r.Id == v2.RunId).ExecuteUpdateAsync(s => s.SetProperty(r => r.RevisionHint, (string?)null)));
        Assert.Null((await ResultAsync(host, submission.OrderId, user)).LengthNotice);
    }

    private Task<AtelierEndToEndHost> StartAsync() =>
        AtelierEndToEndHost.StartAsync(postgres, services => services.PostConfigure<OrderOptions>(o => o.Revise = ReviseMode.On));

    private static Task<OrderSubmission> SubmitAsync(AtelierEndToEndHost host, string user) =>
        host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SubmitAsync(new SubmitOrderRequest("Write a note to the team.", user, OrderEntry.Web)));

    private static Task<OrderRevision> ReviseAsync(AtelierEndToEndHost host, ReviseOrderRequest request) =>
        host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().ReviseAsync(request));

    private static async Task<OrderResult> ResultAsync(AtelierEndToEndHost host, Guid orderId, string user) =>
        (await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, user)))!;

    private static Task DeliverAsync(AtelierEndToEndHost host, Guid runId, string text, string? reportJson = null) =>
        host.QueryAsync(db => db.Runs.Where(r => r.Id == runId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, RunStatus.Completed)
            .SetProperty(r => r.Outcome, RunOutcome.Converged)
            .SetProperty(r => r.FinalText, text)
            .SetProperty(r => r.WordCount, text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length)
            .SetProperty(r => r.ReportJson, reportJson)
            .SetProperty(r => r.CompletedAt, DateTimeOffset.UtcNow.AddMinutes(-10))));

    private static async Task AssertRefusedAsync(AtelierEndToEndHost host, ReviseOrderRequest request)
    {
        var before = await host.QueryAsync(db => db.Runs.CountAsync());
        var ex = await Assert.ThrowsAsync<OrderRevisionException>(() => ReviseAsync(host, request));
        Assert.Equal("To make the text longer, tell the writer what to add.", ex.Message);
        Assert.Equal(before, await host.QueryAsync(db => db.Runs.CountAsync()));
    }
}
