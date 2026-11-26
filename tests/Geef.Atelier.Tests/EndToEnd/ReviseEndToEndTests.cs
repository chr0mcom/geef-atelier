using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// Changing a delivered text over the real wiring (contract stage 5, 4.2 and N2 acceptance): order → version 1 →
/// <see cref="IOrderService.ReviseAsync"/> → the writer gets the delivered text as seed and the change, no clarifier and no web search are
/// asked; a failed version 2 leaves version 1 shown; with the fact check on, a passage against the change blocks; open findings of
/// version 1 that still apply stay in version 2, and open points of the writer for version 1 are shown with version 2. "Resume" on a failed
/// change without a text makes the same change again to the shown version (review E5B, B3), and on any other run resumes it as before; it is
/// refused for a change that already has its own text and for one followed by a later version with a text, and no run is created then.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class ReviseEndToEndTests(PostgresFixture postgres)
{
    private const string User = "e2e-revise";

    private const string Request = "Schreibe eine kurze Einladung zum Elternabend der Klasse 3b am 12. Mai um 19 Uhr in der Aula, max. 80 Wörter.";

    private const string FirstText = "Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am 12. Mai um 19 Uhr in der Aula.\n\nWir freuen uns auf Sie.";

    private const string SecondText = "Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am 5. Mai um 19 Uhr in der Aula.\n\nWir freuen uns auf Sie.";

    private const string Change = "The meeting is on 5 May, not 12 May.";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static Task<AtelierEndToEndHost> StartAsync(PostgresFixture postgres, FactCheckMode mode = FactCheckMode.Off, bool notes = false) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.PostConfigure<DirectOptions>(o =>
            {
                o.FactCheckMode = mode;
                o.WriterNotes = notes;
            });
            services.PostConfigure<OrderOptions>(o => o.Revise = ReviseMode.On);
        });

    private static string Findings(params object[] findings) => JsonSerializer.Serialize(new { findings }, Json);

    private static object Finding(string kind, string? text, string why = "Not in the material.", string? instruction = null) =>
        new { kind, text_quote = text, what_is_wrong = why, instruction_quote = instruction, material_quote = (string?)null, material_quote_2 = (string?)null };

    private static string UserPrompt(RecordedLlmCall call) => call.Messages.Last(m => m.Role == "user").Content ?? string.Empty;

    private static async Task<Guid> FirstVersionAsync(AtelierEndToEndHost host)
    {
        host.Script.EnqueueExecutorText(FirstText);
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(Request, User, OrderEntry.Web, null));
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        return submission.OrderId;
    }

    private static async Task<OrderRevision> ReviseAsync(AtelierEndToEndHost host, Guid orderId, string? change = Change, params RevisionQuick[] quick)
    {
        var revision = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>()
            .ReviseAsync(new ReviseOrderRequest(orderId, User, change, quick, OrderEntry.Web)));
        host.ReleaseOrchestrator();
        await host.WaitForOrderFinishedAsync(orderId);
        return revision;
    }

    private static Task<RunEntity> RunAsync(AtelierEndToEndHost host, Guid orderId, int version) =>
        host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.OrderVersion == version));

    [Fact]
    public async Task The_writer_gets_the_delivered_text_and_the_change_and_nobody_else_is_asked()
    {
        await using var host = await StartAsync(postgres);
        var orderId = await FirstVersionAsync(host);
        var callsBefore = host.Llm.Calls.Count;
        host.Script.EnqueueExecutorText(SecondText);

        var revision = await ReviseAsync(host, orderId);

        Assert.Equal(2, revision.Version);
        var calls = host.Llm.Calls.Skip(callsBefore).ToList();
        var writer = Assert.Single(calls, c => c.Role == LlmRole.Executor);
        Assert.Equal(FirstText, writer.Document);
        Assert.Contains(DirectWriterPrompts.RevisionInstruction, UserPrompt(writer), StringComparison.Ordinal);
        Assert.Contains(Change, UserPrompt(writer), StringComparison.Ordinal);
        Assert.Contains(RevisionBrief.Heading, UserPrompt(writer), StringComparison.Ordinal);
        Assert.DoesNotContain(calls, ScriptedLlmEndpoint.IsClarifierCall);
        Assert.Empty(host.External.For(FakeTavilyEndpoint.ClientName));

        var second = await RunAsync(host, orderId, 2);
        var first = await RunAsync(host, orderId, 1);
        Assert.Equal((first.Id, FirstText), (second.ParentRunId, second.SeedDraftText));
        Assert.Equal(RevisionBrief.Change(Change, []), second.RevisionHint);
        Assert.Null(first.RevisionHint);
        var brief = BriefingSections.Parse(second.BriefingText);
        Assert.Equal(Request, brief.Request);
        Assert.Equal("(newest change, version 2) " + Change, Assert.Single(brief.Changes).Marked);
        Assert.DoesNotContain(brief.PersonAnswers, a => a.Contains(Change, StringComparison.Ordinal));
        Assert.Equal(RunStatus.Completed, second.Status);
        Assert.Equal(SecondText, second.FinalText);
        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, User));
        Assert.Equal((2, SecondText), (result!.Version, result.Text));
    }

    [Fact]
    public async Task A_failed_second_version_leaves_the_first_one_shown_with_a_sentence()
    {
        await using var host = await StartAsync(postgres);
        var orderId = await FirstVersionAsync(host);
        host.Script.EnqueueExecutorError(503, LlmApiError.BackendUnavailableCode);

        await ReviseAsync(host, orderId);

        Assert.Equal(RunStatus.Failed, (await RunAsync(host, orderId, 2)).Status);
        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, User));
        Assert.Equal((1, 2, FirstText), (result!.Version, result.LatestVersion, result.Text));
        Assert.Equal(string.Format(RevisionLines.NotFinishedFormat, 2, 1), result.Note);
    }

    [Fact]
    public async Task With_the_fact_check_on_a_passage_against_the_change_blocks_and_is_corrected()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.On);
        host.Script.EnqueueFactCheckJson(Findings());
        var orderId = await FirstVersionAsync(host);
        var callsBefore = host.Llm.Calls.Count;
        host.Script.EnqueueExecutorText(FirstText);
        host.Script.EnqueueExecutorText(FirstText.Replace("ist am", "findet statt am", StringComparison.Ordinal));
        host.Script.EnqueueExecutorText(SecondText);
        host.Script.EnqueueFactCheckJson(Findings(Finding("instruction", "am 12. Mai um 19 Uhr", "The person asked for 5 May.", Change)));
        host.Script.EnqueueFactCheckJson(Findings());

        await ReviseAsync(host, orderId);

        var writers = host.Llm.Calls.Skip(callsBefore).Where(c => c.Role == LlmRole.Executor).ToList();
        Assert.True(writers.Count >= 2, host.Llm.Describe());
        Assert.Contains("goes against the instruction", UserPrompt(writers[^1]), StringComparison.Ordinal);
        Assert.Contains(Change, UserPrompt(writers[^1]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_open_finding_of_version_one_that_still_stands_is_kept_in_version_two()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report);
        host.Script.EnqueueFactCheckJson(Findings(Finding("unsupported", "Wir freuen uns auf Sie.", "Nobody said that.")));
        var orderId = await FirstVersionAsync(host);
        var callsBefore = host.Llm.Calls.Count;
        host.Script.EnqueueExecutorText(SecondText);
        host.Script.EnqueueFactCheckJson(Findings());

        await ReviseAsync(host, orderId);

        var check = Assert.Single(host.Llm.Calls.Skip(callsBefore), ScriptedLlmEndpoint.IsFactCheckCall);
        Assert.Contains(Change, check.PromptText, StringComparison.Ordinal);
        Assert.Contains("ist am 5. Mai", check.PromptText, StringComparison.Ordinal);
        Assert.DoesNotContain("ist am 12. Mai", check.PromptText, StringComparison.Ordinal);

        var report = RunReportJson.TryParse((await RunAsync(host, orderId, 2)).ReportJson)!;
        var kept = Assert.Single(report.FactCheck!.Open);
        Assert.Equal(("Wir freuen uns auf Sie.", 1), (kept.TextQuote, kept.FromVersion));
        Assert.Equal(FactFindingKind.Unsupported, kept.Kind);

        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, User));
        Assert.Equal(2, result!.Version);
        var shown = Assert.Single(result.ShownPassages());
        Assert.Equal(("Wir freuen uns auf Sie.", 1), (shown.Quote, shown.FromVersion));
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, result.Checks!.ToReview);
    }

    [Fact]
    public async Task An_open_point_of_the_writer_for_version_one_is_shown_with_version_two_and_never_reads_as_nothing_to_review()
    {
        const string point = "Es fehlt, bis wann man sich anmelden soll.";
        await using var host = await StartAsync(postgres, FactCheckMode.Report, notes: true);
        host.Script.EnqueueFactCheckJson(Findings());
        host.Script.EnqueueExecutorText(FirstText + "\n<!-- atelier-notes\n" + JsonSerializer.Serialize(new { assumptions = Array.Empty<string>(), open_points = new[] { point }, quotes = Array.Empty<object>() }) + "\n-->");
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(Request, User, OrderEntry.Web, null));
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        var orderId = submission.OrderId;
        var first = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, User));
        Assert.Equal([point], first!.WriterOpenPoints);
        host.Script.EnqueueExecutorText(SecondText + "\n<!-- atelier-notes\n{\"assumptions\":[],\"open_points\":[],\"quotes\":[]}\n-->");
        host.Script.EnqueueFactCheckJson(Findings());

        await ReviseAsync(host, orderId);

        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, User));
        Assert.Equal((2, SecondText), (result!.Version, result.Text));
        Assert.Empty(result.WriterOpenPoints!);
        Assert.Empty(result.ShownPassages());
        Assert.Equal([new OrderEarlierPoint(1, point)], result.EarlierOpenPoints);
        Assert.True(result.HasOpenPoints());
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, result.Checks!.ToReview);
    }

    private const string OpenPoint = "Es fehlt, bis wann man sich anmelden soll.";

    private static string WithNotes(string text, params string[] openPoints) =>
        text + "\n<!-- atelier-notes\n" + JsonSerializer.Serialize(new { assumptions = Array.Empty<string>(), open_points = openPoints, quotes = Array.Empty<object>() }) + "\n-->";

    private static async Task<Guid> ResumeAsync(AtelierEndToEndHost host, Guid orderId, Guid runId)
    {
        var resumedId = await host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>()
            .ResumeRunAsync(new ResumeOptions(runId, UseSeedDraft: true, MaxIterationsOverride: null), User));
        host.ReleaseOrchestrator();
        await host.WaitForTerminalAsync(resumedId);
        await host.WaitForOrderFinishedAsync(orderId);
        return resumedId;
    }

    [Fact]
    public async Task Resume_on_a_failed_change_makes_the_same_change_to_the_shown_version_and_keeps_what_was_open()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report, notes: true);
        host.Script.EnqueueFactCheckJson(Findings(Finding("unsupported", "Wir freuen uns auf Sie.", "Nobody said that.")));
        host.Script.EnqueueExecutorText(WithNotes(FirstText, OpenPoint));
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(Request, User, OrderEntry.Web, null));
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        var orderId = submission.OrderId;
        host.Script.EnqueueExecutorError(503, LlmApiError.BackendUnavailableCode);
        await ReviseAsync(host, orderId);
        var failed = await RunAsync(host, orderId, 2);
        Assert.Equal(RunStatus.Failed, failed.Status);
        var callsBefore = host.Llm.Calls.Count;
        host.Script.EnqueueExecutorText(WithNotes(SecondText));
        host.Script.EnqueueFactCheckJson(Findings());

        var resumedId = await ResumeAsync(host, orderId, failed.Id);

        var first = await RunAsync(host, orderId, 1);
        var resumed = await RunAsync(host, orderId, 3);
        Assert.Equal(resumedId, resumed.Id);
        Assert.Equal((first.Id, FirstText, failed.RevisionHint), (resumed.ParentRunId, resumed.SeedDraftText, resumed.RevisionHint));
        Assert.Equal(RunStatus.Completed, resumed.Status);
        var brief = BriefingSections.Parse(resumed.BriefingText);
        Assert.Equal(Request, brief.Request);
        Assert.Equal("(newest change, version 3) " + Change, Assert.Single(brief.Changes).Marked);

        var calls = host.Llm.Calls.Skip(callsBefore).ToList();
        var writer = Assert.Single(calls, c => c.Role == LlmRole.Executor);
        Assert.Equal(FirstText, writer.Document);
        Assert.Contains(DirectWriterPrompts.RevisionInstruction, UserPrompt(writer), StringComparison.Ordinal);
        Assert.Contains(Change, UserPrompt(writer), StringComparison.Ordinal);
        Assert.DoesNotContain(calls, ScriptedLlmEndpoint.IsClarifierCall);
        Assert.Empty(host.External.For(FakeTavilyEndpoint.ClientName));

        var report = RunReportJson.TryParse(resumed.ReportJson)!;
        var kept = Assert.Single(report.FactCheck!.Open);
        Assert.Equal(("Wir freuen uns auf Sie.", 1), (kept.TextQuote, kept.FromVersion));
        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, User));
        Assert.Equal((3, 3, SecondText), (result!.Version, result.LatestVersion, result.Text));
        Assert.Equal([new OrderEarlierPoint(1, OpenPoint)], result.EarlierOpenPoints);
        Assert.Equal(("Wir freuen uns auf Sie.", 1), (Assert.Single(result.ShownPassages()).Quote, result.ShownPassages()[0].FromVersion));
    }

    [Fact]
    public async Task Resume_on_a_failed_change_lets_a_rule_of_the_first_order_not_block()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.On);
        host.Script.EnqueueFactCheckJson(Findings());
        var orderId = await FirstVersionAsync(host);
        host.Script.EnqueueExecutorError(503, LlmApiError.BackendUnavailableCode);
        await ReviseAsync(host, orderId);
        var failed = await RunAsync(host, orderId, 2);
        Assert.Equal(RunStatus.Failed, failed.Status);
        var callsBefore = host.Llm.Calls.Count;
        host.Script.EnqueueExecutorText(SecondText);
        host.Script.EnqueueFactCheckJson(Findings(Finding("instruction", "am 5. Mai um 19 Uhr", "The text is too long.", "max. 80 Wörter")));
        host.Script.EnqueueFactCheckJson(Findings());

        await ResumeAsync(host, orderId, failed.Id);

        Assert.Single(host.Llm.Calls.Skip(callsBefore), c => c.Role == LlmRole.Executor);
        var resumed = await RunAsync(host, orderId, 3);
        Assert.Equal(RunStatus.Completed, resumed.Status);
        var hint = Assert.Single(RunReportJson.TryParse(resumed.ReportJson)!.FactCheck!.Open);
        Assert.Equal(FactFindingKind.Instruction, hint.Kind);
        Assert.False(hint.Blocking);
        Assert.True(hint.BeforeLastChange);
    }

    private static Task<int> RunCountAsync(AtelierEndToEndHost host, Guid orderId) =>
        host.QueryAsync(db => db.Runs.CountAsync(r => r.OrderId == orderId));

    [Fact]
    public async Task Resume_on_a_change_that_already_has_its_text_is_refused_and_never_makes_the_change_twice()
    {
        await using var host = await StartAsync(postgres);
        var orderId = await FirstVersionAsync(host);
        host.Script.EnqueueExecutorText(SecondText);
        await ReviseAsync(host, orderId);
        var changed = await RunAsync(host, orderId, 2);
        Assert.Equal(SecondText, changed.FinalText);
        await host.QueryAsync(db => db.Runs.Where(r => r.Id == changed.Id).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, RunStatus.Failed)
            .SetProperty(r => r.ErrorMessage, RunStopMessageExtensions.StoppedWithOpenPointsMessage)));
        var callsBefore = host.Llm.Calls.Count;

        foreach (var user in new[] { User, null })
        {
            var refused = await Assert.ThrowsAsync<RunNotResumableException>(() => host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>()
                .ResumeRunAsync(new ResumeOptions(changed.Id, UseSeedDraft: true, MaxIterationsOverride: null), user)));
            Assert.Equal(RevisionLines.ResumeChangedText, refused.Message);
        }

        Assert.Contains("Change something", RevisionLines.ResumeChangedText, StringComparison.Ordinal);
        Assert.Equal(2, await RunCountAsync(host, orderId));
        Assert.Equal(callsBefore, host.Llm.Calls.Count);
        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, User));
        Assert.Equal((2, 2, SecondText), (result!.Version, result.LatestVersion, result.Text));
    }

    [Fact]
    public async Task Resume_on_a_failed_change_is_refused_once_a_later_version_has_a_text()
    {
        const string laterChange = "Please add that the meeting ends at 21:00.";
        const string laterText = FirstText + "\n\nDer Abend endet um 21 Uhr.";
        await using var host = await StartAsync(postgres);
        var orderId = await FirstVersionAsync(host);
        host.Script.EnqueueExecutorError(503, LlmApiError.BackendUnavailableCode);
        await ReviseAsync(host, orderId);
        var failed = await RunAsync(host, orderId, 2);
        Assert.Equal(RunStatus.Failed, failed.Status);
        Assert.True(string.IsNullOrWhiteSpace(failed.FinalText));
        host.Script.EnqueueExecutorText(laterText);
        await ReviseAsync(host, orderId, laterChange);
        Assert.Equal(laterText, (await RunAsync(host, orderId, 3)).FinalText);
        var callsBefore = host.Llm.Calls.Count;

        var refused = await Assert.ThrowsAsync<RunNotResumableException>(() => host.WithScopeAsync(sp => sp.GetRequiredService<IRunService>()
            .ResumeRunAsync(new ResumeOptions(failed.Id, UseSeedDraft: true, MaxIterationsOverride: null), User)));

        Assert.Equal(RevisionLines.ResumeLaterVersion, refused.Message);
        Assert.Contains("Change something", RevisionLines.ResumeLaterVersion, StringComparison.Ordinal);
        Assert.Equal(3, await RunCountAsync(host, orderId));
        Assert.Equal(callsBefore, host.Llm.Calls.Count);
        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, User));
        Assert.Equal((3, 3, laterText), (result!.Version, result.LatestVersion, result.Text));
    }

    [Fact]
    public async Task Resume_on_a_failed_first_version_resumes_it_as_before()
    {
        await using var host = await StartAsync(postgres);
        host.Script.EnqueueExecutorError(503, LlmApiError.BackendUnavailableCode);
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(Request, User, OrderEntry.Web, null));
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        var orderId = submission.OrderId;
        var failed = await RunAsync(host, orderId, 1);
        Assert.Equal(RunStatus.Failed, failed.Status);
        var callsBefore = host.Llm.Calls.Count;
        host.Script.EnqueueExecutorText(FirstText);

        await ResumeAsync(host, orderId, failed.Id);

        var resumed = await RunAsync(host, orderId, 2);
        Assert.Equal((failed.Id, (string?)null, failed.BriefingText), (resumed.ParentRunId, resumed.RevisionHint, resumed.BriefingText));
        Assert.Equal(RunStatus.Completed, resumed.Status);
        var writer = Assert.Single(host.Llm.Calls.Skip(callsBefore), c => c.Role == LlmRole.Executor);
        Assert.DoesNotContain(DirectWriterPrompts.RevisionInstruction, UserPrompt(writer), StringComparison.Ordinal);
    }
}
