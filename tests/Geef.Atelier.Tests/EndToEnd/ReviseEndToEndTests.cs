using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Infrastructure.Llm;
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
/// version 1 that still apply stay in version 2. Needs the order service and the persistence of the change (package N1, Step52).
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class ReviseEndToEndTests(PostgresFixture postgres)
{
    private const string AfterN1 = "Needs ReviseAsync, RevisionBrief and Step52 of package N1; remove the skip after the N1 merge.";

    private const string User = "e2e-revise";

    private const string Request = "Schreibe eine kurze Einladung zum Elternabend der Klasse 3b am 12. Mai um 19 Uhr in der Aula, max. 80 Wörter.";

    private const string FirstText = "Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am 12. Mai um 19 Uhr in der Aula.\n\nWir freuen uns auf Sie.";

    private const string SecondText = "Liebe Eltern,\n\nder Elternabend der Klasse 3b ist am 5. Mai um 19 Uhr in der Aula.\n\nWir freuen uns auf Sie.";

    private const string Change = "The meeting is on 5 May, not 12 May.";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static Task<AtelierEndToEndHost> StartAsync(PostgresFixture postgres, FactCheckMode mode = FactCheckMode.Off) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.PostConfigure<DirectOptions>(o => o.FactCheckMode = mode);
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

    [Fact(Skip = AfterN1)]
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
        Assert.Equal(RunStatus.Completed, second.Status);
        Assert.Equal(SecondText, second.FinalText);
        var result = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, User));
        Assert.Equal((2, SecondText), (result!.Version, result.Text));
    }

    [Fact(Skip = AfterN1)]
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

    [Fact(Skip = AfterN1)]
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

    [Fact(Skip = AfterN1)]
    public async Task An_open_finding_of_version_one_that_still_stands_is_kept_in_version_two()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report);
        host.Script.EnqueueFactCheckJson(Findings(Finding("unsupported", "Wir freuen uns auf Sie.", "Nobody said that.")));
        var orderId = await FirstVersionAsync(host);
        host.Script.EnqueueExecutorText(SecondText);
        host.Script.EnqueueFactCheckJson(Findings());

        await ReviseAsync(host, orderId);

        var report = RunReportJson.TryParse((await RunAsync(host, orderId, 2)).ReportJson)!;
        var kept = Assert.Single(report.FactCheck!.Open);
        Assert.Equal(("Wir freuen uns auf Sie.", 1), (kept.TextQuote, kept.FromVersion));
        Assert.Equal(FactFindingKind.Unsupported, kept.Kind);
    }
}
