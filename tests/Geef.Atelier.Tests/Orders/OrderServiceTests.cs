using System.Text;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary><see cref="IOrderService"/> over the real wiring with the orchestrator held, so runs stay pending.</summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class OrderServiceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task DirectOrderCreatesTheRunWithTheDirectSnapshotInOneStep()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var source = await IntakeAsync(host, user, "notes.md", "Meeting notes: delivery moves to May. " + Guid.NewGuid());

        var submission = await SubmitAsync(host, new SubmitOrderRequest("Write a short email about the delay.", user, OrderEntry.Web,
            [new OrderSourceSelection(source)]));

        Assert.Equal(OrderDisplayState.Queued, submission.State);
        var run = await host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.Id == submission.RunId));
        Assert.Equal(submission.OrderId, run.OrderId);
        Assert.Equal(1, run.OrderVersion);
        Assert.Equal(RunStatus.Pending, run.Status);
        Assert.Equal("Write a short email about the delay.", run.BriefingText);
        Assert.Equal(SystemCrew.DirectTemplateName, run.CrewTemplateName);
        var snapshot = CrewSnapshot.Deserialize(run.CrewSnapshot!)!;
        Assert.Equal(SystemCrew.DirectTemplateName, snapshot.TemplateName);
        Assert.Equal(SystemCrew.DirectWriterProfile.Name, snapshot.Executor.Name);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Executor.Model));
        Assert.Equal([SystemCrew.DeterministicChecksProfile.Name, SystemCrew.SourceFactCheckProfile.Name], snapshot.Reviewers.Select(r => r.Name));
        var factCheck = snapshot.Reviewers.Single(r => r.Name == SystemCrew.SourceFactCheckProfile.Name);
        Assert.False(CliRoute.IsSameModel(factCheck.Model, snapshot.Executor.Model));
        Assert.NotEqual(CliRoute.FamilyOf(snapshot.Executor.Provider, snapshot.Executor.Model), CliRoute.FamilyOf(factCheck.Provider, factCheck.Model));
        Assert.Equal(2, snapshot.ConvergenceOverride!.MaxIterations);

        var order = await host.QueryAsync(db => db.Orders.AsNoTracking().SingleAsync(o => o.Id == submission.OrderId));
        Assert.Equal(OrderState.Ready, order.State);
        Assert.Equal(OrderMode.Direct, order.Mode);
        Assert.Null(await host.QueryAsync(db => db.Sources.Where(s => s.Id == source).Select(s => s.ExpiresAt).SingleAsync()));
        Assert.Equal(source, await host.QueryAsync(db => db.OrderSources.Where(l => l.OrderId == order.Id).Select(l => l.SourceId).SingleAsync()));
    }

    [Fact]
    public async Task ProblemsThePersonCanFixAreReportedInPlainEnglish()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var foreign = await IntakeAsync(host, NewUser(), "theirs.md", "Someone else's notes " + Guid.NewGuid());
        var text = await IntakeAsync(host, user, "mine.md", "My notes " + Guid.NewGuid());

        await AssertRefusedAsync(host, new SubmitOrderRequest("  ", user, OrderEntry.Web), "Please describe what you need.");
        await AssertRefusedAsync(host, new SubmitOrderRequest(new string('x', 50_001), user, OrderEntry.Web),
            "Your request is too long (50,001 characters, the limit is 50,000). Please shorten it or attach the material as a file.");
        await AssertRefusedAsync(host, new SubmitOrderRequest("Write.", user, OrderEntry.Web, [new OrderSourceSelection(foreign)]),
            "One of the files was not found. Please add it again.");
        await AssertRefusedAsync(host, new SubmitOrderRequest("Write.", user, OrderEntry.Web, [new OrderSourceSelection(Guid.NewGuid())]),
            "One of the files was not found. Please add it again.");
        await AssertRefusedAsync(host, new SubmitOrderRequest("Write.", user, OrderEntry.Web, [new OrderSourceSelection(text, "1-2")]),
            "Pages can only be chosen for PDF files, not for \"mine.md\".");
        await AssertRefusedAsync(host, new SubmitOrderRequest("Write.", user, OrderEntry.Web,
            Enumerable.Range(0, 11).Select(_ => new OrderSourceSelection(Guid.NewGuid())).ToList()), "You can use at most 10 files per order.");

        Assert.Equal(0, await host.QueryAsync(db => db.Orders.CountAsync(o => o.CreatedByUser == user)));
    }

    [Fact]
    public async Task FilesOverTheLimitNeedAPageSelectionWhichThenFits()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var page = string.Concat(Enumerable.Repeat("Long tender text with many details. ", 120));
        var big = SourceFor(user, pages: 200, page);
        await host.QueryAsync(async db =>
        {
            db.Sources.Add(big);
            return await db.SaveChangesAsync();
        });

        var refused = await Assert.ThrowsAsync<OrderSubmissionException>(() =>
            SubmitAsync(host, new SubmitOrderRequest("Summarise.", user, OrderEntry.Web, [new OrderSourceSelection(big.Id)])));
        Assert.Equal(
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Your files are too long to use completely ({big.CharCount:N0} characters, the limit is 600,000). Choose pages for: tender.pdf."),
            refused.Message);
        Assert.Equal(
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Your files are too long to use completely ({big.CharCount:N0} characters, the limit is 600,000). ")
            + "Pages cannot be chosen here: use fewer or shorter files, or ask the person to start the order in the Atelier web app and choose the pages there. Nothing was started.",
            refused.WithoutPageChoice);

        var badPages = await Assert.ThrowsAsync<OrderSubmissionException>(() =>
            SubmitAsync(host, new SubmitOrderRequest("Summarise.", user, OrderEntry.Web, [new OrderSourceSelection(big.Id, "1-300")])));
        Assert.Equal("Pages for \"tender.pdf\": Page 300 does not exist; the file has 200 pages.", badPages.Message);

        var submission = await SubmitAsync(host, new SubmitOrderRequest("Summarise.", user, OrderEntry.Web, [new OrderSourceSelection(big.Id, "55, 1-40")]));
        Assert.Contains(OrderService.MapNotice, submission.Notices);
        var link = await host.QueryAsync(db => db.OrderSources.AsNoTracking().SingleAsync(l => l.OrderId == submission.OrderId));
        Assert.Equal("1-40,55", link.PageSelection);
    }

    [Fact]
    public async Task CheckingTheChosenPagesCountsLikeTheStartWithoutCreatingAnything()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var page = string.Concat(Enumerable.Repeat("Long tender text with many details. ", 120));
        var big = SourceFor(user, pages: 200, page);
        await host.QueryAsync(async db =>
        {
            db.Sources.Add(big);
            return await db.SaveChangesAsync();
        });

        Task<SourceSelectionCheck> CheckAsync(params OrderSourceSelection[] selections) =>
            host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().CheckSourcesAsync(selections, user));

        var whole = await CheckAsync(new OrderSourceSelection(big.Id));
        Assert.Equal(new SourceSelectionCheck(big.CharCount, SourceBudgetClass.TooLarge, null), whole);

        var chosen = await CheckAsync(new OrderSourceSelection(big.Id, "55, 1-40"));
        var expected = PageMarkers.StripMarkers(PageSelection.TryParse("1-40,55", 200, out var selection, out _) ? selection!.ApplyTo(big.ExtractedText!) : "").Length;
        Assert.Equal(new SourceSelectionCheck(expected, SourceBudgetClass.WithMap, null), chosen);

        var wrong = await CheckAsync(new OrderSourceSelection(big.Id, "1-300"));
        Assert.Equal(SourceBudgetClass.TooLarge, wrong.Class);
        Assert.Equal("Pages for \"tender.pdf\": Page 300 does not exist; the file has 200 pages.", wrong.Problem);

        var foreign = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().CheckSourcesAsync([new OrderSourceSelection(big.Id)], NewUser()));
        Assert.Equal("One of the files was not found. Please add it again.", foreign.Problem);

        var submission = await SubmitAsync(host, new SubmitOrderRequest("Summarise.", user, OrderEntry.Web, [new OrderSourceSelection(big.Id, "55, 1-40")]));
        Assert.Equal(1, await host.QueryAsync(db => db.Orders.CountAsync(o => o.CreatedByUser == user)));
        Assert.NotEqual(Guid.Empty, submission.OrderId);
    }

    [Fact]
    public async Task ViewsListsCancellationDeletionAndWaiting()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var source = await IntakeAsync(host, user, "facts.md", "Facts " + Guid.NewGuid());
        var submission = await SubmitAsync(host, new SubmitOrderRequest("Write a thank-you note to the team.\nKeep it short.", user, OrderEntry.Web,
            [new OrderSourceSelection(source)]));

        await host.WithScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IOrderService>();
            Assert.Null(await service.GetAsync(submission.OrderId, NewUser()));
            Assert.Null(await service.GetResultAsync(submission.OrderId, NewUser()));
            Assert.False(await service.CancelAsync(submission.OrderId, NewUser()));

            var view = (await service.GetAsync(submission.OrderId, user))!;
            Assert.Equal("Write a thank-you note to the team.", view.Title);
            Assert.Equal(OrderDisplayState.Queued, view.Display.State);
            Assert.Equal(["files", "write", "check", "done"], view.Progress.Steps.Select(s => s.Key));
            Assert.Equal("facts.md", Assert.Single(view.Sources).FileName);
            Assert.Equal(1, Assert.Single(view.Runs).Version);

            var list = await service.ListAsync(user);
            Assert.Equal(submission.OrderId, Assert.Single(list).OrderId);
            Assert.Equal(1, list[0].SourceCount);
            Assert.Empty(await service.ListAsync(NewUser()));

            var started = DateTimeOffset.UtcNow;
            var unchanged = await service.WaitForChangeAsync(submission.OrderId, user, OrderDisplayState.Queued, TimeSpan.FromMilliseconds(1_500));
            var waited = DateTimeOffset.UtcNow - started;
            Assert.Equal(OrderDisplayState.Queued, unchanged!.Display.State);
            Assert.InRange(waited, TimeSpan.FromMilliseconds(1_200), TimeSpan.FromSeconds(10));

            var immediate = DateTimeOffset.UtcNow;
            await service.WaitForChangeAsync(submission.OrderId, user, OrderDisplayState.Working, TimeSpan.FromSeconds(30));
            Assert.True(DateTimeOffset.UtcNow - immediate < TimeSpan.FromSeconds(1));

            await Assert.ThrowsAsync<OrderStillWorkingException>(() => service.DeleteAsync(submission.OrderId, user));

            Assert.True(await service.CancelAsync(submission.OrderId, user));
            Assert.True(await sp.GetRequiredService<Geef.Atelier.Infrastructure.Persistence.AtelierDbContext>().Runs
                .Where(r => r.Id == submission.RunId).Select(r => r.CancellationRequested).SingleAsync());
            Assert.True(await service.CancelAsync(submission.OrderId, user));
            return 0;
        });

        await host.QueryAsync(db => db.Runs.Where(r => r.Id == submission.RunId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Aborted).SetProperty(r => r.Outcome, RunOutcome.Aborted)));

        await host.WithScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IOrderService>();
            Assert.False(await service.DeleteAsync(submission.OrderId, NewUser()));
            Assert.NotNull(await service.GetAsync(submission.OrderId, user));

            var stopped = (await service.GetResultAsync(submission.OrderId, user))!;
            Assert.Equal(OrderDisplayState.Stopped, stopped.Display.State);
            Assert.Null(stopped.Text);

            Assert.True(await service.DeleteAsync(submission.OrderId, user));
            Assert.Null(await service.GetAsync(submission.OrderId, null));
            return 0;
        });

        Assert.False(await host.QueryAsync(db => db.Runs.AnyAsync(r => r.Id == submission.RunId)));
        Assert.False(await host.QueryAsync(db => db.Sources.AnyAsync(s => s.Id == source)));
    }

    [Fact]
    public async Task WaitReturnsEarlyWhenTheStateChangesAndCancelStopsAnOrderWithoutRun()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var submission = await SubmitAsync(host, new SubmitOrderRequest("Write a slogan.", user, OrderEntry.Web));

        var waiting = host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>()
            .WaitForChangeAsync(submission.OrderId, user, OrderDisplayState.Queued, TimeSpan.FromSeconds(40)));
        await Task.Delay(500);
        var changedAt = DateTimeOffset.UtcNow;
        await host.QueryAsync(db => db.Runs.Where(r => r.Id == submission.RunId)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, RunStatus.Running)));
        var view = await waiting;
        Assert.Equal(OrderDisplayState.Working, view!.Display.State);
        Assert.True(DateTimeOffset.UtcNow - changedAt < TimeSpan.FromSeconds(5));

        var waitingOrder = Order(user, state: OrderState.Analyzing);
        await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderRepository>().CreateAsync(waitingOrder, []).ContinueWith(_ => 0));
        await host.WithScopeAsync(async sp =>
        {
            var service = sp.GetRequiredService<IOrderService>();
            Assert.Equal(OrderDisplayState.Preparing, (await service.GetAsync(waitingOrder.Id, user))!.Display.State);
            Assert.True(await service.CancelAsync(waitingOrder.Id, user));
            Assert.False(await service.CancelAsync(waitingOrder.Id, user));
            var stopped = (await service.GetAsync(waitingOrder.Id, user))!;
            Assert.Equal(OrderDisplayState.Stopped, stopped.Display.State);
            Assert.True(stopped.Display.IsFinished);
            return 0;
        });
    }

    [Fact]
    public async Task ResultShowsTheTextOfTheCurrentVersionWithUsagesAndWarnings()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var user = NewUser();
        var submission = await SubmitAsync(host, new SubmitOrderRequest("Write a slogan.", user, OrderEntry.Web));
        await host.QueryAsync(async db =>
        {
            await db.Runs.Where(r => r.Id == submission.RunId).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RunStatus.Failed)
                .SetProperty(r => r.Outcome, RunOutcome.DeliveredWithOpenPoints)
                .SetProperty(r => r.FinalText, "Fresh ideas, fast.")
                .SetProperty(r => r.WordCount, 3)
                .SetProperty(r => r.CompletedAt, DateTimeOffset.UtcNow));
            db.RunSourceUsages.Add(new RunSourceUsageEntity
            {
                Id = Guid.NewGuid(), RunId = submission.RunId!.Value, SourceId = null, FileName = "gone.pdf", Mode = SourceUsageMode.FullText,
                Target = "writer", CharsIncluded = 1200, PagesIncluded = "1-3", Position = 0, CreatedAt = DateTimeOffset.UtcNow,
            });
            return await db.SaveChangesAsync();
        });

        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(submission.OrderId, user));

        Assert.Equal(OrderDisplayState.DeliveredWithOpenPoints, result!.Display.State);
        Assert.Equal("Fresh ideas, fast.", result.Text);
        Assert.Equal(3, result.WordCount);
        Assert.Equal(1, result.Version);
        var usage = Assert.Single(result.SourcesUsed);
        Assert.Equal(("gone.pdf", 1200, "1-3"), (usage.FileName, usage.Characters, usage.Pages));
        Assert.NotNull(result.Note);
        Assert.Equal(3, Assert.Single(await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().ListAsync(user))).WordCount);
    }

    private static async Task AssertRefusedAsync(AtelierEndToEndHost host, SubmitOrderRequest request, string message)
    {
        var ex = await Assert.ThrowsAsync<OrderSubmissionException>(() => SubmitAsync(host, request));
        Assert.Equal(message, ex.Message);
    }

    private static Task<OrderSubmission> SubmitAsync(AtelierEndToEndHost host, SubmitOrderRequest request) =>
        host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().SubmitAsync(request));

    private static async Task<Guid> IntakeAsync(AtelierEndToEndHost host, string user, string name, string text)
    {
        var receipt = await host.WithScopeAsync(sp => sp.GetRequiredService<ISourceIntakeService>()
            .IntakeTextAsync(name, text, user, SourceOrigin.WebUpload));
        Assert.True(receipt.Usable, receipt.Reason);
        return receipt.SourceId!.Value;
    }

    private static SourceEntity SourceFor(string user, int pages, string pageText)
    {
        var text = string.Join('\n', Enumerable.Range(1, pages).Select(p => PageMarkers.For(p) + "\n" + pageText));
        return Source(user, text: text, chars: PageMarkers.StripMarkers(text).Length, pages: pages) with
        {
            FileName = "tender.pdf", Kind = SourceKind.Pdf, ContentType = "application/pdf",
        };
    }
}
