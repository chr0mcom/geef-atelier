using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Core.Persistence.Profiles;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Tests.TestData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The fact check of the direct path over the real wiring (contract stage 3, R3 cases 1–10): grounding builds the material and
/// the mode, the scripted writer and the scripted fact check answer through the faked gateway, the reviewer decides with the
/// filter of the code, the correction gets fixed sentences, the report (schema 2) and the result say what was checked. Needs the
/// grounding and the writer of package R2.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class FactCheckEndToEndTests(PostgresFixture postgres)
{
    private const string User = "e2e-facts";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static Task<AtelierEndToEndHost> StartAsync(PostgresFixture postgres, FactCheckMode mode, bool notes = false, bool intake = false, TimeSpan? limit = null) =>
        AtelierEndToEndHost.StartAsync(postgres, services =>
        {
            services.PostConfigure<DirectOptions>(o =>
            {
                o.FactCheckMode = mode;
                o.WriterNotes = notes;
                o.SourceArithmetic = true;
                if (limit is { } l)
                {
                    o.FactCheckTimeout = l;
                    o.FactCheckFallbackTimeout = l;
                }
            });
            services.PostConfigure<IntakeOptions>(o =>
            {
                o.ContentAssumptions = ContentAssumptionMode.OpenPoints;
                o.PollInterval = TimeSpan.FromMilliseconds(200);
            });
            if (intake)
                services.PostConfigure<OrderOptions>(o => o.IntakeMode = IntakeMode.On);
        });

    private static string Findings(params object[] findings) => JsonSerializer.Serialize(new { findings }, Json);

    private static object Finding(string kind, string? text, string why = "Not in the material.", string? instruction = null, string? material = null, string? material2 = null) =>
        new { kind, text_quote = text, what_is_wrong = why, instruction_quote = instruction, material_quote = material, material_quote_2 = material2 };

    private static IReadOnlyList<RecordedLlmCall> FactCalls(AtelierEndToEndHost host) => host.Llm.Calls.Where(ScriptedLlmEndpoint.IsFactCheckCall).ToList();

    private static IReadOnlyList<RecordedLlmCall> RecheckCalls(AtelierEndToEndHost host) => host.Llm.Calls.Where(ScriptedLlmEndpoint.IsRecheckCall).ToList();

    private static string UserPrompt(RecordedLlmCall call) => call.Messages.Last(m => m.Role == "user").Content ?? string.Empty;

    private static string MaterialOf(RecordedLlmCall call)
    {
        var prompt = UserPrompt(call);
        var start = prompt.IndexOf("<material>", StringComparison.Ordinal);
        var end = prompt.IndexOf("</material>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, prompt);
        return prompt[start..end];
    }

    private static async Task<Guid> SubmitAsync(AtelierEndToEndHost host, string request, IReadOnlyList<OrderSourceSelection>? sources = null, string user = User)
    {
        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest(request, user, OrderEntry.Web, sources));
        await host.WaitForOrderFinishedAsync(submission.OrderId);
        return submission.OrderId;
    }

    private static async Task<OrderSourceSelection> TextFileAsync(AtelierEndToEndHost host, string name, string text, SourceRole role = SourceRole.Facts, string user = User)
    {
        using var client = await host.CreateSignedInClientAsync(user);
        var receipt = await host.UploadSourceAsync(client, name, Encoding.UTF8.GetBytes(text));
        return new OrderSourceSelection(receipt.GetProperty("sourceId").GetGuid(), null, role);
    }

    private static Task<RunEntity> RunOfAsync(AtelierEndToEndHost host, Guid orderId) =>
        host.QueryAsync(db => db.Runs.AsNoTracking().SingleAsync(r => r.OrderId == orderId && r.Kind == RunKind.Standard));

    private static async Task<RunReport> ReportOfAsync(AtelierEndToEndHost host, Guid orderId) =>
        RunReportJson.TryParse((await RunOfAsync(host, orderId)).ReportJson) ?? throw new InvalidOperationException("No report.");

    private static Task<OrderResult> ResultOfAsync(AtelierEndToEndHost host, Guid orderId, string user = User) =>
        host.WithScopeAsync(async sp => (await sp.GetRequiredService<IOrderService>().GetResultAsync(orderId, user))!);

    private static Task<List<FindingEntity>> FactFindingsAsync(AtelierEndToEndHost host, Guid runId) =>
        host.QueryAsync(db => db.Findings.AsNoTracking()
            .Where(f => f.Category == "fact-check" && db.Iterations.Any(i => i.Id == f.IterationId && i.RunId == runId))
            .ToListAsync());

    private const string Photos = "Fotos des Schadens liegen diesem Schreiben bei.";
    private const string PhotoAssumption = "Fotos des Schadens werden als Anlage erwähnt";

    private static string Clarify(string gaps) =>
        $$"""{"text_type":"Beschwerde","language":"de","understanding":"Eine Beschwerde über ein beschädigt geliefertes Gerät.","is_revision":false,"revision_file":null,"length_scale":"standard","gaps":[{{gaps}}],"research_topics":[]}""";

    private static string Assume(string topic, string assumption) =>
        $$"""{"topic":"{{topic}}","resolution":"assume","user_can_know":true,"web_researchable":false,"impact":"low","assumption":"{{assumption}}","confidence":"sure","question":null}""";

    [Fact]
    public async Task Case1_photo_case_an_assumed_fact_is_no_material_the_claim_is_found_corrected_and_the_new_text_checked_in_full()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.On, intake: true);
        host.Script.EnqueueClarifierJson(Clarify(Assume("facts", PhotoAssumption)));
        host.Script.EnqueueExecutorText($"Sehr geehrte Damen und Herren,\n\ndas gelieferte Gerät war bei der Ankunft beschädigt. {Photos}\n\nMit freundlichen Grüßen");
        host.Script.EnqueueExecutorText("Sehr geehrte Damen und Herren,\n\ndas gelieferte Gerät war bei der Ankunft beschädigt. Bitte nennen Sie mir das weitere Vorgehen.\n\nMit freundlichen Grüßen");
        host.Script.EnqueueFactCheckJson(Findings(Finding("unsupported", Photos, "The material says nothing about photos.")));
        host.Script.EnqueueFactCheckJson(ScriptedLlmEndpoint.DefaultFactCheckJson);

        var orderId = await SubmitAsync(host, "Beschwerde schreiben, Gerät kam kaputt an.");

        var writer = host.Llm.CallsFor(LlmRole.Executor);
        Assert.Equal(2, writer.Count);
        var firstPrompt = UserPrompt(writer[0]);
        Assert.Contains(IntakeBriefComposer.OpenFactsHeadingStart, firstPrompt);
        Assert.Contains(PhotoAssumption, firstPrompt);
        var checks = FactCalls(host);
        Assert.Equal(2, checks.Count);
        var check = checks[0];
        Assert.DoesNotContain("als Anlage erwähnt", MaterialOf(check));
        Assert.Contains("/codex/", check.Url);
        Assert.Contains(Photos, UserPrompt(writer[1]));
        Assert.Contains("Bitte nennen Sie mir das weitere Vorgehen.", UserPrompt(checks[1]));
        Assert.DoesNotContain(Photos, UserPrompt(checks[1]).Split("<text>")[^1]);
        Assert.Empty(RecheckCalls(host));

        var run = await RunOfAsync(host, orderId);
        Assert.Equal((RunOutcome?)RunOutcome.Converged, run.Outcome);
        Assert.Equal("On", run.FactCheckMode);
        Assert.DoesNotContain("liegen diesem Schreiben bei", run.FinalText);
        var report = await ReportOfAsync(host, orderId);
        Assert.Equal(2, report.SchemaVersion);
        Assert.Equal(FactCheckState.Checked, report.FactCheck!.State);
        Assert.Equal(Photos, Assert.Single(report.FactCheck.Corrected).TextQuote);
        Assert.Contains(report.Corrections!, c => c.Code == FactFindingMetadata.UnsupportedCode && c.QuoteBefore == Photos);
        var stored = Assert.Single(await FactFindingsAsync(host, run.Id));
        Assert.Equal((FindingSeverity.Major, "fact.unsupported", Photos), (stored.Severity, stored.Code, stored.Quote));

        var result = await ResultOfAsync(host, orderId);
        Assert.Contains($"Removed or replaced: “{Photos}”", result.Checks!.Changed);
        Assert.Contains(OrderCheckLines.FactsCheckedNoFiles, result.Checks.Checked);
    }

    [Fact]
    public async Task Case2_a_quote_that_is_not_in_the_text_causes_no_second_writing_pass()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.On);
        host.Script.ExecutorText = "Liebe Kolleginnen und Kollegen, das Sommerfest findet am Freitag statt. Viele Grüße";
        host.Script.EnqueueFactCheckJson(Findings(Finding("unsupported", "Das Sommerfest findet im Stadtpark mit Musik statt.")));

        var orderId = await SubmitAsync(host, "Schreib eine kurze Einladung zum Sommerfest am Freitag.");

        Assert.Single(host.Llm.CallsFor(LlmRole.Executor));
        Assert.Equal(FactCheckState.Checked, (await ReportOfAsync(host, orderId)).FactCheck!.State);
        Assert.Empty(await FactFindingsAsync(host, (await RunOfAsync(host, orderId)).Id));
    }

    [Fact]
    public async Task Case3_the_origin_of_an_instruction_follows_where_it_stands()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report, intake: true);
        const string user = "e2e-facts-profile";
        await host.WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IUserProfileRepository>().UpsertAsync(new UserProfileEntity
            {
                Username = user, NoGos = "Keine Ausrufezeichen im Text", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
            return 0;
        });
        host.Script.EnqueueClarifierJson(Clarify(
            """{"topic":"length","resolution":"ask","user_can_know":true,"web_researchable":false,"impact":"high","assumption":"Etwa 150 Wörter","confidence":"unsure","question":null}"""
            + "," + Assume("tone", "Locker und herzlich")));
        host.Script.ExecutorText = "Hallo zusammen! Das Fest beginnt um 18 Uhr im Garten. Bringt gute Laune mit. Wir freuen uns sehr auf euch alle. "
            + string.Join(' ', Enumerable.Repeat("Hallo", 129));
        host.Script.EnqueueFactCheckJson(Findings(
            Finding("instruction", "Hallo zusammen! Das Fest beginnt um 18 Uhr", instruction: "Keine Ausrufezeichen im Text"),
            Finding("instruction", "Bringt gute Laune mit.", instruction: "Locker und herzlich"),
            Finding("instruction", "Wir freuen uns sehr auf euch alle.", instruction: "Kurz (etwa 150 Wörter)")));

        var submission = await host.SubmitOrderAsync(new SubmitOrderRequest("Schreib eine Einladung zum Gartenfest.", user, OrderEntry.Web));
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while ((await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().GetAsync(submission.OrderId, null)))!.State != OrderState.NeedsInput)
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, host.Llm.Describe());
            await Task.Delay(50);
        }
        await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>().AnswerAsync(new AnswerOrderRequest(
            submission.OrderId, user, [new OrderAnswer("length", "short", null, false, AnswerOrigin.Client)])));
        await host.WaitForOrderFinishedAsync(submission.OrderId);

        var open = (await ReportOfAsync(host, submission.OrderId)).FactCheck!.Open;
        Assert.Equal(2, open.Count);
        var memory = Assert.Single(open, f => f.Origin == InstructionOrigin.Memory);
        Assert.True(memory.Blocking);
        var client = Assert.Single(open, f => f.Origin == InstructionOrigin.Client);
        Assert.False(client.Blocking);
        Assert.DoesNotContain(open, f => f.InstructionQuote == "Locker und herzlich");
        Assert.Single(host.Llm.CallsFor(LlmRole.Executor));

        var result = await ResultOfAsync(host, submission.OrderId, user);
        Assert.Equal(["Keine Ausrufezeichen im Text"], result.BlockingPassages(FactFindingKind.Instruction).Select(p => p.Instruction));
        Assert.Single(result.HintPassages());
    }

    [Fact]
    public async Task Case4_when_the_fact_check_and_its_fallback_fail_the_text_is_delivered_and_the_check_is_not_completed()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.On);
        host.Script.EnqueueFactCheckError(503, "backend_unavailable");
        host.Script.EnqueueFactCheckError(503, "backend_unavailable");

        var orderId = await SubmitAsync(host, "Schreib eine kurze Notiz an das Team.");

        Assert.Equal(2, FactCalls(host).Count);
        var run = await RunOfAsync(host, orderId);
        Assert.False(string.IsNullOrWhiteSpace(run.FinalText));
        Assert.Equal(FactCheckState.NotCompleted, (await ReportOfAsync(host, orderId)).FactCheck!.State);
        var warnings = await host.QueryAsync(db => db.RunWarnings.AsNoTracking().Where(w => w.RunId == run.Id).ToListAsync());
        Assert.Contains(warnings, w => w.Code == RunWarningCodes.ReviewerFailed && w.Message.Contains("\"Fact check\" could not be completed", StringComparison.Ordinal));
        var result = await ResultOfAsync(host, orderId);
        Assert.Contains(OrderCheckLines.FactsNotCompleted, result.Checks!.Checked);
    }

    [Fact]
    public async Task Hang_an_aborted_check_in_on_mode_starts_no_correction_round_and_no_further_check()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.On, limit: TimeSpan.FromMilliseconds(400));
        host.Script.EnqueueFactCheckHang();
        host.Script.EnqueueFactCheckHang();

        var orderId = await SubmitAsync(host, "Schreib eine kurze Notiz an das Team.");

        Assert.Single(host.Llm.CallsFor(LlmRole.Executor));
        var calls = FactCalls(host);
        Assert.Equal(2, calls.Count);
        Assert.Contains("/codex/", calls[0].Url);
        Assert.Contains("/claude/", calls[1].Url);
        Assert.Empty(RecheckCalls(host));
        var run = await RunOfAsync(host, orderId);
        Assert.False(string.IsNullOrWhiteSpace(run.FinalText));
        var factCheck = (await ReportOfAsync(host, orderId)).FactCheck!;
        Assert.Equal(FactCheckState.NotCompleted, factCheck.State);
        var warnings = await host.QueryAsync(db => db.RunWarnings.AsNoTracking().Where(w => w.RunId == run.Id).ToListAsync());
        Assert.Contains(warnings, w => w.Code == RunWarningCodes.ReviewerFailed && w.Message.Contains("\"Fact check\" could not be completed", StringComparison.Ordinal));
        Assert.Contains(OrderCheckLines.FactsNotCompleted, (await ResultOfAsync(host, orderId)).Checks!.Checked);
    }

    [Fact]
    public async Task Hang_the_gateway_ending_both_calls_at_their_limit_is_neither_retried_nor_followed_by_a_correction_round()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.On);
        host.Script.EnqueueFactCheckError(504, "cli_timeout");
        host.Script.EnqueueFactCheckError(504, "cli_timeout");

        var orderId = await SubmitAsync(host, "Schreib eine kurze Notiz an das Team.");

        Assert.Single(host.Llm.CallsFor(LlmRole.Executor));
        Assert.Equal(2, FactCalls(host).Count);
        Assert.Equal(new string?[] { "85", "55" }, host.Script.FactCheckTimeoutHeaders.ToList());
        Assert.Equal(FactCheckState.NotCompleted, (await ReportOfAsync(host, orderId)).FactCheck!.State);
    }

    [Fact]
    public async Task Fallback_an_unavailable_codex_is_replaced_once_by_sonnet_never_by_the_writer_model()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report);
        host.Script.EnqueueFactCheckError(503, "backend_unavailable");

        var orderId = await SubmitAsync(host, "Schreib eine kurze Notiz an das Team.");

        var calls = FactCalls(host);
        Assert.Equal(2, calls.Count);
        Assert.Contains("/codex/", calls[0].Url);
        Assert.Contains("/claude/", calls[1].Url);
        Assert.Contains("sonnet", calls[1].Model, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(Assert.Single(host.Llm.CallsFor(LlmRole.Executor)).Model, calls[1].Model);
        var factCheck = (await ReportOfAsync(host, orderId)).FactCheck!;
        Assert.True(factCheck.UsedFallbackModel);
        Assert.Equal(FactCheckState.Checked, factCheck.State);
        Assert.Equal(2, factCheck.Calls);
    }

    [Fact]
    public async Task Case5_report_mode_shows_the_passages_without_a_second_writing_pass_and_checks_every_order()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report);
        const string unbacked = "Melden Sie sich bitte bis Freitag im Sekretariat an.";
        host.Script.ExecutorText = $"Liebe Eltern, am Montag beginnt die Projektwoche. {unbacked} Viele Grüße";
        host.Script.EnqueueFactCheckJson(Findings(Finding("unsupported", unbacked)));

        var first = await SubmitAsync(host, "Schreib einen kurzen Elternbrief zur Projektwoche ab Montag.");

        Assert.Single(host.Llm.CallsFor(LlmRole.Executor));
        var run = await RunOfAsync(host, first);
        Assert.Equal("Report", run.FactCheckMode);
        Assert.Equal((RunOutcome?)RunOutcome.Converged, run.Outcome);
        var stored = Assert.Single(await FactFindingsAsync(host, run.Id));
        Assert.Equal((FindingSeverity.Minor, unbacked), (stored.Severity, stored.Quote));
        var result = await ResultOfAsync(host, first);
        Assert.Equal(unbacked, Assert.Single(result.BlockingPassages(FactFindingKind.Unsupported)).Quote);
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, result.Checks!.ToReview);

        var orders = new List<Guid>();
        for (var i = 0; i < 29; i++)
        {
            var submission = await host.SubmitOrderAsync(new SubmitOrderRequest($"Schreib eine kurze Notiz Nummer {i}.", User, OrderEntry.Web));
            orders.Add(submission.OrderId);
        }
        foreach (var order in orders)
            await host.WaitForOrderFinishedAsync(order, TimeSpan.FromSeconds(180));

        Assert.Equal(30, FactCalls(host).Count);
        Assert.Equal(30, await host.QueryAsync(db => db.Runs.CountAsync(r => r.FactCheckMode == "Report")));
    }

    [Fact]
    public async Task Case6_the_fact_check_gets_every_file_in_full_and_the_text_to_revise()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report);
        const string first = "E2E-FACT-FIRST-PAGE-5K2";
        const string last = "E2E-FACT-LAST-PAGE-9Q7";
        const string revise = "E2E-FACT-REVISION-3M8";
        OrderSourceSelection pdf;
        using (var client = await host.CreateSignedInClientAsync(User))
        {
            var receipt = await host.UploadSourceAsync(client, "hafenbericht.pdf", SyntheticDocuments.Pdf(80, page => page switch
            {
                1 => $"Erste Seite. {first}",
                80 => $"Letzte Seite. {last}",
                _ => $"Routinetext der Seite {page}.",
            }));
            pdf = new OrderSourceSelection(receipt.GetProperty("sourceId").GetGuid());
        }
        var template = await TextFileAsync(host, "entwurf.txt", $"Sehr geehrte Damen und Herren, dies ist der alte Entwurf. {revise}", SourceRole.TemplateToRevise);
        host.Script.ExecutorText = "Sehr geehrte Damen und Herren, dies ist der überarbeitete Entwurf zum Hafenbericht.";

        await SubmitAsync(host, "Überarbeite den Entwurf mit dem Hafenbericht.", [pdf, template]);

        var material = MaterialOf(Assert.Single(FactCalls(host)));
        Assert.Contains(first, material);
        Assert.Contains(last, material);
        Assert.Contains(revise, material);
        Assert.Contains("id=\"S0\"", material);
    }

    [Fact]
    public async Task Case7_off_sends_no_fact_check_and_says_so()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Off);

        var orderId = await SubmitAsync(host, "Schreib eine kurze Notiz an das Team.");

        Assert.Empty(FactCalls(host));
        Assert.Equal("Off", (await RunOfAsync(host, orderId)).FactCheckMode);
        Assert.Equal(FactCheckState.Off, (await ReportOfAsync(host, orderId)).FactCheck!.State);
        Assert.Contains(OrderCheckLines.FactsNotChecked, (await ResultOfAsync(host, orderId)).Checks!.Checked);
    }

    [Fact]
    public async Task Case8_get_order_result_over_mcp_shows_the_passages_and_the_state()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report);
        const string unbacked = "Die Anmeldung läuft über unsere neue Webseite.";
        host.Script.ExecutorText = $"Liebe Mitglieder, der Ausflug findet im Mai statt. {unbacked} Viele Grüße";
        host.Script.EnqueueFactCheckJson(Findings(Finding("unsupported", unbacked)));

        var orderId = await SubmitAsync(host, "Schreib eine Ankündigung für den Vereinsausflug im Mai.", user: AtelierEndToEndHost.McpTokenUser);
        var result = await host.CallMcpToolAsync("get_order_result", new { orderId = orderId.ToString() });

        var passage = Assert.Single(result.GetProperty("unbackedPassages").EnumerateArray());
        Assert.Equal((unbacked, "not_backed", true), (passage.GetProperty("quote").GetString(), passage.GetProperty("kind").GetString(), passage.GetProperty("blocking").GetBoolean()));
        Assert.Equal("open_points", result.GetProperty("factCheck").GetProperty("state").GetString());
        Assert.False(result.GetProperty("factCheck").GetProperty("filesCompared").GetBoolean());
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, result.GetProperty("checks").GetProperty("toReview").GetString());
    }

    private const string ParentNotes = """
        Notizen zum Elternabend der Klasse 3b

        Termin: Der Elternabend findet am 14. März statt. Der alte Termin am 7. März entfällt.
        Raum: Wir treffen uns in der Aula, Raum 12 steht nicht mehr zur Verfügung.

        Bastelmaterial
        Scheren 22 €
        Papier 31 €
        Farben 19 €
        Summe 62 €

        Bitte das Geld bis zum 7. März in bar mitgeben.
        Intern: Die Kasse prüft Frau Beispiel noch.
        Hinweis: Ein Kind hat eine Nussallergie, bitte keine Nüsse mitbringen.
        """;

    private const string Reply = "Bitte geben Sie uns in der Gruppe Bescheid, ob Sie kommen.";
    private const string OldDate = "Der Elternabend findet am 7. März im Raum 12 statt";

    private static string ParentLetter(string middle) =>
        $"Liebe Eltern,\n\n{middle}\n\nBitte keine Nüsse mitbringen, ein Kind hat eine Allergie.\n\nViele Grüße\n" +
        "<!-- atelier-notes\n" +
        """{"assumptions":["Anrede: Liebe Eltern"],"open_points":["In den Notizen steht als Summe 62 €, die Posten ergeben 72 €; im Brief steht 72 €."],"quotes":[{"source":"S1","quote":"Ein Kind hat eine Nussallergie"},{"source":"S1","quote":"Farben kosten 18 € insgesamt"}]}"""
        + "\n-->";

    private static string ParentFindings() => Findings(
        Finding("unsupported", Reply, "The notes name no way to reply."),
        Finding("superseded", OldDate, "The notes move the date.", material: "Der alte Termin am 7. März entfällt."),
        Finding("source_conflict", null, "The total does not add up.", material: "Summe 62 €", material2: "Farben 19 €"));

    [Fact]
    public async Task Case9_parent_evening_in_on_mode_reports_the_wrong_total_and_corrects_reply_and_old_date()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.On, notes: true);
        var notes = await TextFileAsync(host, "notizen-elternabend.txt", ParentNotes);
        host.Script.EnqueueExecutorText(ParentLetter($"{OldDate}. Das Bastelmaterial kostet zusammen 72 €. {Reply}"));
        host.Script.EnqueueExecutorText(ParentLetter("Der Elternabend findet am 14. März in der Aula statt. Das Bastelmaterial kostet zusammen 72 €."));
        host.Script.EnqueueFactCheckJson(ParentFindings());
        host.Script.EnqueueFactCheckJson(Findings(Finding("source_conflict", null, "The total does not add up.", material: "Summe 62 €", material2: "Farben 19 €")));

        var orderId = await SubmitAsync(host, "Mach aus den Notizen einen Elternbrief und nenne kurz die Gesamtkosten des Materials.", [notes]);

        var writer = host.Llm.CallsFor(LlmRole.Executor);
        Assert.Equal(2, writer.Count);
        Assert.Contains("62", UserPrompt(writer[0]));
        Assert.Contains("72", UserPrompt(writer[0]));
        var correction = UserPrompt(writer[1]);
        Assert.Contains(Reply, correction);
        Assert.Contains(OldDate, correction);
        Assert.Contains("Der alte Termin am 7. März entfällt.", correction);

        var report = await ReportOfAsync(host, orderId);
        Assert.Single(report.SourceArithmetic!.Issues);
        Assert.Equal(2, report.Corrections!.Count(c => c.Code.StartsWith("fact.", StringComparison.Ordinal)));
        Assert.Single(report.FactCheck!.SourceConflicts);
        Assert.Equal(WriterNotesStatus.Ok, report.NotesStatus);
        Assert.Equal("Ein Kind hat eine Nussallergie", Assert.Single(report.SourceQuotes!).Quote);
        Assert.Equal(1, report.UnverifiedQuotes);

        var result = await ResultOfAsync(host, orderId);
        Assert.Equal(2, FactCalls(host).Count);
        Assert.Empty(RecheckCalls(host));
        Assert.Equal(2, result.SourceIssues!.Count);
        var issue = result.SourceIssues[0];
        Assert.True(issue.Contains("62", StringComparison.Ordinal) && issue.Contains("72", StringComparison.Ordinal) && issue.Contains("The text uses 72", StringComparison.Ordinal), issue);
        Assert.Contains("“Summe 62 €” and also “Farben 19 €”", result.SourceIssues[1], StringComparison.Ordinal);
        Assert.Equal(2, result.Checks!.Changed.Count(l => l.StartsWith("Removed or replaced:", StringComparison.Ordinal)));
        Assert.Contains(OrderCheckLines.TotalsSearchedOne, result.Checks.Checked);
        Assert.Contains(OrderCheckLines.Contradictions, result.Checks.Checked);
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, result.Checks.ToReview);
        Assert.DoesNotContain("atelier-notes", (await RunOfAsync(host, orderId)).FinalText);
    }

    [Fact]
    public async Task Case9_parent_evening_in_report_mode_keeps_the_text_and_lists_both_passages_as_open_points()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report, notes: true);
        var notes = await TextFileAsync(host, "notizen-elternabend.txt", ParentNotes);
        host.Script.EnqueueExecutorText(ParentLetter($"{OldDate}. Das Bastelmaterial kostet zusammen 72 €. {Reply}"));
        host.Script.EnqueueFactCheckJson(ParentFindings());

        var orderId = await SubmitAsync(host, "Mach aus den Notizen einen Elternbrief und nenne kurz die Gesamtkosten des Materials.", [notes]);

        Assert.Single(host.Llm.CallsFor(LlmRole.Executor));
        Assert.Empty(RecheckCalls(host));
        var result = await ResultOfAsync(host, orderId);
        Assert.Equal(Reply, Assert.Single(result.BlockingPassages(FactFindingKind.Unsupported)).Quote);
        Assert.Equal(OldDate, Assert.Single(result.BlockingPassages(FactFindingKind.Superseded)).Quote);
        Assert.Contains(result.SourceIssues!, s => s.Contains("The text uses 72", StringComparison.Ordinal));
        Assert.Empty(result.Checks!.Changed);
        Assert.Contains(string.Format(OrderCheckLines.FactsOpenPointsFormat, 2), result.Checks.Checked);
        Assert.Equal(["In den Notizen steht als Summe 62 €, die Posten ergeben 72 €; im Brief steht 72 €."], result.WriterOpenPoints);
    }

    [Fact]
    public async Task Case10_an_order_without_any_instruction_is_checked_too()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report);
        var file = await TextFileAsync(host, "stichpunkte.txt", "Treffen am Dienstag um 10 Uhr. Thema: neue Ablage.");
        host.Script.ExecutorText = "Am Dienstag um 10 Uhr treffen wir uns zum Thema neue Ablage.";

        var orderId = await SubmitAsync(host, "Mach daraus eine Nachricht.", [file]);

        Assert.Single(FactCalls(host));
        var result = await ResultOfAsync(host, orderId);
        Assert.Equal(FactCheckState.Checked, result.FactCheck);
        Assert.Equal(["Checked automatically: the text is not empty, starts directly.", OrderCheckLines.FactsChecked, OrderCheckLines.Contradictions], result.Checks!.Checked);
        Assert.Equal(OrderCheckLines.ToReviewNothing, result.Checks.ToReview);
    }

    private static string Hallo(int count) => string.Join(' ', Enumerable.Repeat("Hallo", count));

    private const string Invented = "Das Treffen ist am Freitag um 9 Uhr im Raum 3.";

    [Fact]
    public async Task Round2_in_report_mode_a_text_rewritten_for_the_length_check_is_checked_in_full_again()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report);
        host.Script.EnqueueExecutorText(Hallo(80));
        host.Script.EnqueueExecutorText(Hallo(30) + " " + Invented);
        host.Script.EnqueueFactCheckJson(ScriptedLlmEndpoint.DefaultFactCheckJson);
        host.Script.EnqueueFactCheckJson(Findings(Finding("unsupported", Invented, "The request names no meeting.")));

        var orderId = await SubmitAsync(host, "Schreibe eine kurze Notiz an das Team, max. 50 Wörter.");

        Assert.Equal(2, host.Llm.CallsFor(LlmRole.Executor).Count);
        var checks = FactCalls(host);
        Assert.Equal(2, checks.Count);
        Assert.Contains(Invented, UserPrompt(checks[1]));
        Assert.Empty(RecheckCalls(host));
        var run = await RunOfAsync(host, orderId);
        Assert.Equal(2, run.DeliveredIteration);
        Assert.Equal(FactCheckState.OpenPoints, (await ReportOfAsync(host, orderId)).FactCheck!.State);
        var result = await ResultOfAsync(host, orderId);
        Assert.Equal(Invented, Assert.Single(result.BlockingPassages(FactFindingKind.Unsupported)).Quote);
        Assert.Contains(OrderCheckLines.FactsOpenPointOneNoFiles, result.Checks!.Checked);
        Assert.DoesNotContain(OrderCheckLines.FactsCheckedNoFiles, result.Checks.Checked);
        Assert.Contains(OrderCheckLines.ChangedLength, result.Checks.Changed);
    }

    private const string TwoDays = "Treffen am Dienstag um 10 Uhr. Thema: neue Ablage. Nachtrag: Treffen am Mittwoch um 10 Uhr.";

    private static object DayConflict() =>
        Finding("source_conflict", null, "The notes name two days.", material: "Treffen am Dienstag um 10 Uhr", material2: "Treffen am Mittwoch um 10 Uhr");

    [Fact]
    public async Task Round2_in_report_mode_a_passage_still_in_the_text_stays_open_when_the_second_check_answers_empty()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report);
        var file = await TextFileAsync(host, "stichpunkte.txt", TwoDays);
        host.Script.EnqueueExecutorText(Hallo(80) + " " + Invented);
        host.Script.EnqueueExecutorText(Hallo(30) + " " + Invented);
        host.Script.EnqueueFactCheckJson(Findings(Finding("unsupported", Invented, "The notes name no meeting on Friday."), DayConflict()));
        host.Script.EnqueueFactCheckJson(ScriptedLlmEndpoint.DefaultFactCheckJson);

        var orderId = await SubmitAsync(host, "Schreibe eine kurze Notiz an das Team, max. 50 Wörter.", [file]);

        Assert.Equal(2, host.Llm.CallsFor(LlmRole.Executor).Count);
        Assert.Equal(2, FactCalls(host).Count);
        Assert.Equal(2, (await RunOfAsync(host, orderId)).DeliveredIteration);
        var report = await ReportOfAsync(host, orderId);
        Assert.Equal(FactCheckState.OpenPoints, report.FactCheck!.State);
        Assert.True(Assert.Single(report.FactCheck.Open).Blocking);
        Assert.Single(report.FactCheck.SourceConflicts);
        var result = await ResultOfAsync(host, orderId);
        Assert.Equal(Invented, Assert.Single(result.BlockingPassages(FactFindingKind.Unsupported)).Quote);
        Assert.Contains(result.SourceIssues!, s => s.Contains("Treffen am Mittwoch um 10 Uhr", StringComparison.Ordinal));
        Assert.Contains(OrderCheckLines.FactsOpenPointOne, result.Checks!.Checked);
        Assert.DoesNotContain(OrderCheckLines.FactsChecked, result.Checks.Checked);
        Assert.DoesNotContain(result.Checks.Changed, l => l.StartsWith("Removed or replaced:", StringComparison.Ordinal));
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, result.Checks.ToReview);
    }

    [Fact]
    public async Task Round2_in_on_mode_a_blocking_passage_still_in_the_text_is_never_approved_when_the_second_check_answers_empty()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.On);
        var file = await TextFileAsync(host, "stichpunkte.txt", TwoDays);
        host.Script.EnqueueExecutorText("Hallo Team, wir treffen uns zum Thema neue Ablage. " + Invented);
        host.Script.EnqueueExecutorText("Hallo Team, wir treffen uns zum Thema neue Ablage. " + Invented + " Bis bald.");
        host.Script.EnqueueFactCheckJson(Findings(Finding("unsupported", Invented, "The notes name no meeting on Friday."), DayConflict()));
        host.Script.EnqueueFactCheckJson(ScriptedLlmEndpoint.DefaultFactCheckJson);

        var orderId = await SubmitAsync(host, "Mach daraus eine Nachricht an das Team.", [file]);

        var run = await RunOfAsync(host, orderId);
        var findings = await FactFindingsAsync(host, run.Id);
        var iterations = await host.QueryAsync(db => db.Iterations.AsNoTracking().Where(i => i.RunId == run.Id).ToListAsync());
        var second = iterations.Single(i => i.IterationNumber == 2);
        Assert.Equal(2, FactCalls(host).Count);
        Assert.Contains(findings, f => f.IterationId == second.Id && f.Code == FactFindingMetadata.UnsupportedCode && f.Quote == Invented);
        Assert.Contains(findings, f => f.IterationId == second.Id && f.Message == "The notes name two days.");
        Assert.Equal(2, run.DeliveredIteration);
        var report = await ReportOfAsync(host, orderId);
        Assert.Equal(FactCheckState.OpenPoints, report.FactCheck!.State);
        Assert.True(Assert.Single(report.FactCheck.Open).Blocking);
        var result = await ResultOfAsync(host, orderId);
        Assert.Equal(Invented, Assert.Single(result.BlockingPassages(FactFindingKind.Unsupported)).Quote);
        Assert.Contains(result.SourceIssues!, s => s.Contains("Treffen am Mittwoch um 10 Uhr", StringComparison.Ordinal));
        Assert.Contains(OrderCheckLines.FactsOpenPointOne, result.Checks!.Checked);
        Assert.DoesNotContain(OrderCheckLines.FactsChecked, result.Checks.Checked);
        Assert.DoesNotContain(result.Checks.Changed, l => l.StartsWith("Removed or replaced:", StringComparison.Ordinal));
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, result.Checks.ToReview);
        Assert.True(result.HasOpenPoints());
    }

    [Fact]
    public async Task Round2_when_the_check_of_the_rewritten_text_fails_the_result_never_says_its_facts_were_compared()
    {
        await using var host = await StartAsync(postgres, FactCheckMode.Report);
        host.Script.EnqueueExecutorText(Hallo(80));
        host.Script.EnqueueExecutorText(Hallo(45));
        host.Script.EnqueueFactCheckJson(ScriptedLlmEndpoint.DefaultFactCheckJson);
        host.Script.EnqueueFactCheckError(503, "backend_unavailable");
        host.Script.EnqueueFactCheckError(503, "backend_unavailable");

        var orderId = await SubmitAsync(host, "Schreibe eine kurze Notiz an das Team, max. 50 Wörter.");

        Assert.Equal(3, FactCalls(host).Count);
        Assert.Equal(2, (await RunOfAsync(host, orderId)).DeliveredIteration);
        Assert.Equal(FactCheckState.NotCompleted, (await ReportOfAsync(host, orderId)).FactCheck!.State);
        var result = await ResultOfAsync(host, orderId);
        Assert.Contains(OrderCheckLines.FactsNotCompleted, result.Checks!.Checked);
        Assert.DoesNotContain(result.Checks.Checked, l => l.StartsWith("Facts in the text were compared", StringComparison.Ordinal));
        Assert.Equal(OrderCheckLines.ToReviewFactsNotFullyChecked, result.Checks.ToReview);
    }
}
