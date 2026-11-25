using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// The research of an order in its result over the real order service and Postgres: shown from the stored research only when the
/// delivered run used it, every other status as its sentence, the usage row of the research never listed as a file, and the
/// sentence about research that did not work shown once. The research is stored in its real column (<c>Orders.ResearchJson</c>,
/// Step51) through <see cref="IOrderRepository.TrySetResearchAsync"/> and read back by the real repository.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class OrderServiceResearchResultTests(PostgresFixture postgres)
{
    private const string User = "e2e-research-result";

    private static readonly ResearchEntry Entry = OrderResearchLinesTests.Entry();

    private static RunReport Report(bool lookedUp) =>
        new(2, RunReport.DirectKind, OrderConstraints.None, [new DraftCheckResult(DraftCheckCodes.Empty, true, true, "ok", null)], [],
            lookedUp
                ? [new RunReportSource(null, DossierRenderer.FileName, SourceUsageMode.FullText, 300, LookedUp: true)]
                : [],
            1, 1, null, [], WriterNotesStatus.Off, WriterNotes.Empty, [], 0,
            new FactCheckSummary(FactCheckMode.Report, FactCheckScope.Full, FactCheckState.Checked, [], [], [], 1, "m", false, "fact-check-1"),
            null, [], lookedUp);

    private Task<AtelierEndToEndHost> StartAsync() => AtelierEndToEndHost.StartAsync(postgres);

    private static async Task StoreResearchAsync(AtelierEndToEndHost host, Guid orderId, OrderResearch research) =>
        Assert.True(await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderRepository>()
            .TrySetResearchAsync(orderId, ResearchJson.Serialize(research), research.At)));

    [Fact]
    public async Task Found_research_used_by_the_delivered_run_is_shown_and_its_usage_row_is_no_file()
    {
        await using var host = await StartAsync();
        var (orderId, runId) = await DeliveredAsync(host, Report(lookedUp: true), "Liebe Eltern, der Mindestlohn steigt.");
        await StoreResearchAsync(host, orderId, OrderResearchLinesTests.Research(ResearchStatus.Found, [Entry]));
        await AddUsageAsync(host, runId, null, DossierRenderer.FileName, DossierRenderer.UsageTarget, 0);

        var result = await ResultAsync(host, orderId);

        var lookedUp = result.LookedUp!;
        Assert.Equal(ResearchStatus.Found, lookedUp.Status);
        Assert.Equal(Entry.Quote, Assert.Single(lookedUp.Entries).Quote);
        Assert.DoesNotContain(result.SourcesUsed, s => s.FileName == DossierRenderer.FileName);
        Assert.Contains(OrderResearchLines.FactsComparedLeadLookedUpNoFiles + OrderCheckLines.FactsNothingFound, result.Checks!.Checked);
        Assert.True(result.UsedLookedUp());
    }

    [Fact]
    public async Task Found_research_is_not_shown_for_a_run_whose_report_does_not_say_it_used_it()
    {
        await using var host = await StartAsync();
        var (orderId, _) = await DeliveredAsync(host, Report(lookedUp: false), "Liebe Eltern.");
        await StoreResearchAsync(host, orderId, OrderResearchLinesTests.Research(ResearchStatus.Found, [Entry]));

        var result = await ResultAsync(host, orderId);

        Assert.Null(result.LookedUp);
        Assert.DoesNotContain(result.Checks!.Checked, l => l.Contains(OrderResearchLines.FromLookedUp, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Research_that_did_not_work_is_told_once_under_looked_up_and_other_warnings_stay()
    {
        await using var host = await StartAsync();
        var (orderId, runId) = await DeliveredAsync(host, Report(lookedUp: false), "Liebe Eltern.");
        await StoreResearchAsync(host, orderId, OrderResearchLinesTests.Research(ResearchStatus.Failed, error: "search_failed"));
        await AddWarningAsync(host, runId, RunWarningCodes.ResearchFailed, OrderResearchLines.Failed);
        await AddWarningAsync(host, runId, RunWarningCodes.LlmFailover, "Another service answered.");

        var result = await ResultAsync(host, orderId);

        Assert.Equal(OrderResearchLines.Failed, result.LookedUp!.Note);
        Assert.Equal([RunWarningCodes.LlmFailover], result.Warnings.Select(w => w.Code));
    }

    [Fact]
    public async Task Without_stored_research_nothing_is_shown_and_warnings_are_unchanged()
    {
        await using var host = await StartAsync();
        var (orderId, runId) = await DeliveredAsync(host, Report(lookedUp: false), "Liebe Eltern.");
        await AddWarningAsync(host, runId, RunWarningCodes.ResearchFailed, OrderResearchLines.Failed);
        await AddUsageAsync(host, runId, null, "notizen.txt", "writer", 0);

        var result = await ResultAsync(host, orderId);

        Assert.Null(result.LookedUp);
        Assert.Equal([RunWarningCodes.ResearchFailed], result.Warnings.Select(w => w.Code));
        Assert.Equal(["notizen.txt"], result.SourcesUsed.Select(s => s.FileName));
    }

    [Fact]
    public async Task A_run_without_text_keeps_the_warning_and_shows_no_research()
    {
        await using var host = await StartAsync();
        var (orderId, runId) = await DeliveredAsync(host, Report(lookedUp: false), null);
        await StoreResearchAsync(host, orderId, OrderResearchLinesTests.Research(ResearchStatus.Failed, error: "timeout"));
        await AddWarningAsync(host, runId, RunWarningCodes.ResearchFailed, OrderResearchLines.Failed);

        var result = await ResultAsync(host, orderId);

        Assert.Null(result.LookedUp);
        Assert.Equal([RunWarningCodes.ResearchFailed], result.Warnings.Select(w => w.Code));
    }

    private static Task<OrderResult> ResultAsync(AtelierEndToEndHost host, Guid orderId) =>
        host.WithScopeAsync(async sp => (await sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, User))!);

    private static async Task<(Guid OrderId, Guid RunId)> DeliveredAsync(AtelierEndToEndHost host, RunReport report, string? text)
    {
        var submission = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>()
            .SubmitAsync(new SubmitOrderRequest("Write a short note about the minimum wage.", User, OrderEntry.Web)));
        await host.QueryAsync(db => db.Runs.Where(r => r.Id == submission.RunId).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, text is null ? RunStatus.Failed : RunStatus.Completed)
            .SetProperty(r => r.Outcome, text is null ? (RunOutcome?)null : RunOutcome.Converged)
            .SetProperty(r => r.FinalText, text)
            .SetProperty(r => r.WordCount, text is null ? null : 5)
            .SetProperty(r => r.ReportJson, RunReportJson.Serialize(report))
            .SetProperty(r => r.CompletedAt, DateTimeOffset.UtcNow)));
        return (submission.OrderId, submission.RunId!.Value);
    }

    private static Task<int> AddUsageAsync(AtelierEndToEndHost host, Guid runId, Guid? sourceId, string fileName, string target, int position) =>
        host.QueryAsync(db =>
        {
            db.RunSourceUsages.Add(new RunSourceUsageEntity
            {
                Id = Guid.NewGuid(), RunId = runId, SourceId = sourceId, FileName = fileName, Mode = SourceUsageMode.FullText, Target = target,
                CharsIncluded = 300, Position = position, CreatedAt = DateTimeOffset.UtcNow,
            });
            return db.SaveChangesAsync();
        });

    private static Task<int> AddWarningAsync(AtelierEndToEndHost host, Guid runId, string code, string message) =>
        host.QueryAsync(db =>
        {
            db.RunWarnings.Add(new RunWarningEntity
            {
                Id = Guid.NewGuid(), RunId = runId, Source = RunWarningSources.Grounding, Code = code, Message = message, CreatedAt = DateTimeOffset.UtcNow,
            });
            return db.SaveChangesAsync();
        });
}
