using System.Text;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// While a change is written, and after it failed or was stopped, the result belongs to the shown version as a whole: its text, its
/// warnings, the files it used and the facts the person did not give. The running version is held open at the writer call, so every
/// state is reached by the application itself.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class ReviseShownVersionEndToEndTests(PostgresFixture postgres)
{
    private const string User = "e2e-revise-shown";
    private const string Request = "Schreibe eine kurze Einladung zum Elternabend der Klasse 3b, max. 80 Wörter.";
    private const string FirstText = "Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am [Datum] in der Aula.\n\nWir freuen uns auf Sie.";
    private const string SecondText = "Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am [Datum] in der Mensa.\n\nWir freuen uns auf Sie.";
    private const string OpenFact = "Der Abend findet im Mai statt";
    private const string FormAssumption = "Locker und herzlich";
    private const string MarkerCode = "e2e_version_marker";

    private const string Clarification =
        """{"text_type":"Einladung","language":"de","understanding":"Eine Einladung zum Elternabend.","is_revision":false,"revision_file":null,"length_scale":"standard","gaps":[{"topic":"date","resolution":"assume","user_can_know":true,"web_researchable":false,"impact":"low","assumption":"Der Abend findet im Mai statt","confidence":"sure","question":null},{"topic":"tone","resolution":"assume","user_can_know":true,"web_researchable":false,"impact":"low","assumption":"Locker und herzlich","confidence":"sure","question":null}],"research_topics":[]}""";

    private static Task<AtelierEndToEndHost> StartAsync(PostgresFixture postgres) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.PostConfigure<OrderOptions>(o =>
            {
                o.IntakeMode = IntakeMode.On;
                o.Revise = ReviseMode.On;
            });
            services.PostConfigure<IntakeOptions>(o =>
            {
                o.PollInterval = TimeSpan.FromMilliseconds(200);
                o.ContentAssumptions = ContentAssumptionMode.OpenPoints;
            });
        });

    private static async Task<Guid> FirstVersionAsync(AtelierEndToEndHost host)
    {
        host.Script.EnqueueClarifierJson(Clarification);
        host.Script.EnqueueExecutorText(FirstText);
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(Request, User, OrderEntry.Web));
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        return submission.OrderId;
    }

    private static async Task<(ExecutorHold Hold, Guid RunId)> StartChangeAsync(AtelierEndToEndHost host, Guid orderId, int version, string change)
    {
        var hold = host.Script.HoldNextExecutor();
        var revision = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>()
            .ReviseAsync(new ReviseOrderRequest(orderId, User, change, [], OrderEntry.Web)));
        Assert.Equal(version, revision.Version);
        await hold.Reached.WaitAsync(TimeSpan.FromSeconds(60));
        return (hold, revision.RunId!.Value);
    }

    private static Task<RunEntity> RunAsync(AtelierEndToEndHost host, Guid orderId, int version) =>
        host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.OrderVersion == version));

    private static async Task<OrderResult> ResultAsync(AtelierEndToEndHost host, Guid orderId) =>
        (await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, User)))!;

    private static async Task<string> ExportAsync(AtelierEndToEndHost host, Guid orderId)
    {
        var export = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderExportService>().ExportAsync(orderId, ExportFormat.Markdown, null, User));
        return Encoding.UTF8.GetString(export!.Content);
    }

    private static Task<bool> MarkAsync(AtelierEndToEndHost host, Guid runId, int version) =>
        host.WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IRunWarningSink>()
                .ReportAsync(runId, RunWarningSources.Grounding, MarkerCode, $"Warning of version {version}.");
            await sp.GetRequiredService<ISourceRepository>().AddRunUsagesAsync(
            [
                new RunSourceUsageEntity
                {
                    Id = Guid.NewGuid(), RunId = runId, SourceId = null, FileName = $"notes-of-version-{version}.txt", Mode = SourceUsageMode.FullText,
                    Target = "writer", CharsIncluded = 100 * version, PagesIncluded = null, Position = 0, CreatedAt = DateTimeOffset.UtcNow,
                },
            ]);
            return true;
        });

    private static void BelongsTo(OrderResult result, int shown, int latest, string text)
    {
        Assert.Equal(((int?)shown, (int?)latest, text), (result.Version, result.LatestVersion, result.Text));
        Assert.Equal([$"Warning of version {shown}."], result.Warnings.Where(w => w.Code == MarkerCode).Select(w => w.Message));
        var file = Assert.Single(result.SourcesUsed);
        Assert.Equal(($"notes-of-version-{shown}.txt", 100 * shown), (file.FileName, file.Characters));
        Assert.Equal([OpenFact], result.NotGivenFacts);
    }

    /// <summary>
    /// A change copies the facts not given from the briefing it is based on, so the application never produces two versions that differ
    /// in them. The last step therefore removes the block from the stored briefing of the failed version 3 by hand: the result must
    /// still read them from version 2, which is shown.
    /// </summary>
    [Fact]
    public async Task Warnings_files_and_facts_not_given_are_those_of_the_shown_version_in_every_state()
    {
        await using var host = await StartAsync(postgres);
        var orderId = await FirstVersionAsync(host);
        var first = await RunAsync(host, orderId, 1);
        Assert.Contains(OpenFact, BriefingSections.Parse(first.BriefingText).OpenFacts);
        Assert.DoesNotContain(FormAssumption, BriefingSections.Parse(first.BriefingText).OpenFacts);
        await MarkAsync(host, first.Id, 1);
        BelongsTo(await ResultAsync(host, orderId), 1, 1, FirstText);

        var (second, secondRun) = await StartChangeAsync(host, orderId, 2, "The meeting is in the canteen.");
        await MarkAsync(host, secondRun, 2);
        BelongsTo(await ResultAsync(host, orderId), 1, 2, FirstText);
        second.Answer(SecondText);
        await host.WaitForOrderFinishedAsync(orderId);
        BelongsTo(await ResultAsync(host, orderId), 2, 2, SecondText);

        var (third, thirdRun) = await StartChangeAsync(host, orderId, 3, "Make it shorter.");
        await MarkAsync(host, thirdRun, 3);
        BelongsTo(await ResultAsync(host, orderId), 2, 3, SecondText);
        third.Fail(503, LlmApiError.BackendUnavailableCode);
        await host.WaitForOrderFinishedAsync(orderId);
        Assert.Equal(RunStatus.Failed, (await RunAsync(host, orderId, 3)).Status);
        BelongsTo(await ResultAsync(host, orderId), 2, 3, SecondText);

        await host.QueryAsync(db => db.Runs.Where(r => r.Id == thirdRun)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.BriefingText, Request)));
        BelongsTo(await ResultAsync(host, orderId), 2, 3, SecondText);
    }

    [Fact]
    public async Task Stopping_a_running_third_version_ends_it_as_stopped_and_leaves_the_second_one_shown_and_downloadable()
    {
        await using var host = await StartAsync(postgres);
        var orderId = await FirstVersionAsync(host);
        var (second, _) = await StartChangeAsync(host, orderId, 2, "The meeting is in the canteen.");
        second.Answer(SecondText);
        await host.WaitForOrderFinishedAsync(orderId);

        var (third, thirdRun) = await StartChangeAsync(host, orderId, 3, "Make it shorter.");
        var running = await ResultAsync(host, orderId);
        Assert.Equal((OrderDisplayState.Working, (int?)2, (int?)3), (running.Display.State, running.Version, running.LatestVersion));
        Assert.Equal("A new version is being written. Version 2 stays here until it is ready.", running.Note);

        Assert.True(await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().CancelAsync(orderId, User)));
        await third.Abandoned.WaitAsync(TimeSpan.FromSeconds(30));
        await host.WaitForOrderFinishedAsync(orderId);

        var run = await RunAsync(host, orderId, 3);
        Assert.Equal(thirdRun, run.Id);
        Assert.Equal((RunStatus.Aborted, (RunOutcome?)RunOutcome.Aborted, true), (run.Status, run.Outcome, run.CancellationRequested));
        Assert.Null(run.FinalText);

        var result = await ResultAsync(host, orderId);
        Assert.Equal(OrderDisplayState.Stopped, result.Display.State);
        Assert.Equal(((int?)2, (int?)3, SecondText), (result.Version, result.LatestVersion, result.Text));
        Assert.Equal("Version 3 could not be finished. This is version 2.", result.Note);
        Assert.Equal(SecondText, await ExportAsync(host, orderId));
    }
}
