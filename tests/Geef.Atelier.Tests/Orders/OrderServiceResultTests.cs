using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Tests.EndToEnd;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// The stage-3 fields of <see cref="OrderResult"/>: filled from a report of schema 2, deduplicated assumptions, shadow findings
/// never shown, schema 1 with "What was checked" saying that facts were not checked and every other new field <c>null</c>.
/// </summary>
public sealed class OrderServiceResultTests
{
    private static readonly FactFinding Unbacked = new("f1", FactFindingKind.Unsupported, " Bitte geben Sie uns in der Gruppe Bescheid. ", "x", null, null, null, null, null, true, QuoteMatchKind.Exact);
    private static readonly FactFinding Against = new("f2", FactFindingKind.Instruction, "Liebe Eltern, viele Grüße", "x", "ohne Emojis", null, null, null, InstructionOrigin.Stated, true, QuoteMatchKind.Exact);
    private static readonly FactFinding Conflict = new("f3", FactFindingKind.SourceConflict, null, "x", null, "Summe 62 €", "22 + 31 + 19", "notizen.txt", null, false, QuoteMatchKind.NotFound);

    private static OrderResult Base(IReadOnlyList<IntakeAssumption>? assumptions = null, IReadOnlyList<string>? placeholders = null) =>
        new(Guid.NewGuid(), OrderStateResolver.Resolve(OrderState.Ready, null), Guid.NewGuid(), 1, "Liebe Eltern, viele Grüße", 4,
            placeholders ?? [], [], [new OrderSourceUsageView("notizen.txt", SourceUsageMode.FullText, 400, null)], [], null, assumptions);

    private static RunReport Schema2(FactCheckSummary? factCheck, WriterNotes? notes = null, SourceArithmeticResult? arithmetic = null) =>
        new(2, RunReport.DirectKind, OrderConstraints.None, [new DraftCheckResult(DraftCheckCodes.Empty, true, true, "ok", null)], [],
            [new RunReportSource(Guid.NewGuid(), "notizen.txt", SourceUsageMode.FullText, 400)], 1, 1, null, [],
            notes is null ? WriterNotesStatus.Off : WriterNotesStatus.Ok, notes ?? WriterNotes.Empty,
            [new RunReportQuote(Guid.NewGuid(), "S1", "notizen.txt", "Schere 22 €", QuoteMatchKind.Exact)], 1, factCheck, arithmetic, []);

    private static FactCheckSummary Summary(FactCheckState state, FactCheckMode mode = FactCheckMode.Report, FactCheckScope scope = FactCheckScope.Full, params FactFinding[] open) =>
        new(mode, scope, state, open.Where(f => f.Kind != FactFindingKind.SourceConflict).ToList(), open.Where(f => f.Kind == FactFindingKind.SourceConflict).ToList(), [], 1, "m", false, "fact-check-1");

    [Fact]
    public void Schema_two_fills_every_new_field()
    {
        var notes = new WriterNotes(["Anrede: Liebe Eltern"], ["Die Summe 62 € stimmt nicht.", " "], []);
        var issue = new SourceArithmeticIssue("S1", "notizen.txt", ArithmeticForm.ListTotal, "Summe 62 €", ["22 €", "31 €", "19 €"], "62 €", "72 €", TotalUseInText.Computed);

        var repeatsTotal = Conflict with
        {
            MaterialQuote2 = "22 € + 31 € + 19 €", MaterialFrom = MaterialPart.Files, Material2From = MaterialPart.Files, Material2File = "notizen.txt",
        };
        var result = Base().WithReport(Schema2(Summary(FactCheckState.OpenPoints, open: [Unbacked, Against, repeatsTotal]), notes, new SourceArithmeticResult(1, [issue])));

        Assert.Equal(["Anrede: Liebe Eltern"], result.WriterAssumptions);
        Assert.Equal(["Die Summe 62 € stimmt nicht."], result.WriterOpenPoints);
        Assert.Equal(
            [
                new OrderUnbackedPassage("Bitte geben Sie uns in der Gruppe Bescheid.", FactFindingKind.Unsupported, null, true),
                new OrderUnbackedPassage("Liebe Eltern, viele Grüße", FactFindingKind.Instruction, "ohne Emojis", true, InstructionOrigin.Stated),
            ],
            result.UnbackedPassages);
        Assert.StartsWith("Your file \"notizen.txt\" gives 62 € as the total", Assert.Single(result.SourceIssues!), StringComparison.Ordinal);
        Assert.Equal([new OrderSourceQuoteView("notizen.txt", "Schere 22 €")], result.SourceQuotes);
        Assert.Equal(FactCheckState.OpenPoints, result.FactCheck);
        Assert.Equal(FactCheckScope.Full, result.FactCheckScope);
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, result.Checks!.ToReview);
        Assert.Contains(string.Format(OrderCheckLines.FactsOpenPointsFormat, 2), result.Checks.Checked);
    }

    [Fact]
    public void A_check_with_passages_that_could_not_be_listed_is_never_reported_as_checked_with_nothing_found()
    {
        var result = Base().WithReport(Schema2(Summary(FactCheckState.Checked) with { NotShown = 1 }));

        Assert.Equal(FactCheckState.OpenPoints, result.FactCheck);
        Assert.Equal("open_points", Geef.Atelier.Mcp.Models.OrderDtoMappings.ToFactCheckDto(result)!.State);
        Assert.Contains(OrderCheckLines.FactsComparedLead + OrderCheckLines.FactsNotShownOnlyOne, result.Checks!.Checked);
        Assert.Equal(FactCheckState.Checked, Base().WithReport(Schema2(Summary(FactCheckState.Checked))).FactCheck);
    }

    [Fact]
    public void Writer_notes_that_could_not_be_shown_are_counted_in_a_fixed_sentence_and_count_as_open_points()
    {
        var notes = new WriterNotes([], [], [], Hidden: 2);

        var result = Base().WithReport(Schema2(Summary(FactCheckState.Checked), notes));

        Assert.Equal(string.Format(OrderResultReport.NotesNotShownFormat, 2), result.NotesNotShown);
        Assert.True(result.HasOpenPoints());
        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, result.Checks!.ToReview);
        Assert.Equal(OrderResultReport.NotesNotShownOne, Base().WithReport(Schema2(Summary(FactCheckState.Checked), new WriterNotes([], [], [], Hidden: 1))).NotesNotShown);
        Assert.Null(Base().WithReport(Schema2(Summary(FactCheckState.Checked), new WriterNotes([], [], []))).NotesNotShown);
    }

    [Fact]
    public void An_instruction_from_the_about_me_details_is_shown_only_to_the_owner()
    {
        var fromProfile = Against with { InstructionQuote = "Ich duze nie", Origin = InstructionOrigin.Memory };

        var owner = Base().WithReport(Schema2(Summary(FactCheckState.OpenPoints, open: [fromProfile])), showProfile: true);
        var other = Base().WithReport(Schema2(Summary(FactCheckState.OpenPoints, open: [fromProfile])), showProfile: false);

        Assert.Equal(("Ich duze nie", InstructionOrigin.Memory), (Assert.Single(owner.UnbackedPassages!).Instruction, owner.UnbackedPassages![0].Origin));
        Assert.Equal(((string?)null, InstructionOrigin.Memory), (Assert.Single(other.UnbackedPassages!).Instruction, other.UnbackedPassages![0].Origin));
    }

    [Fact]
    public void Assumptions_about_facts_that_were_not_given_are_not_listed_as_assumed()
    {
        var photos = new IntakeAssumption("a1", GapTopic.Facts, "Fotos des Schadens werden als Anlage erwähnt", AssumptionConfidence.Sure);
        var tone = new IntakeAssumption("a2", GapTopic.Tone, "Freundlicher Ton", AssumptionConfidence.Sure);
        var result = Base([photos, tone]) with { NotGivenFacts = ["Fotos des Schadens werden als Anlage erwähnt"] };

        Assert.Equal(["Freundlicher Ton"], result.AllAssumptions());
        Assert.Equal(["Fotos des Schadens werden als Anlage erwähnt"], result.NotGivenFacts());
    }

    [Fact]
    public void Writer_assumptions_that_repeat_the_clarifier_are_shown_once()
    {
        var clarifier = new IntakeAssumption("a1", GapTopic.Tone, "Der Ton ist freundlich.", AssumptionConfidence.Sure);
        var notes = new WriterNotes(["der Ton ist  freundlich", "Anrede: Liebe Eltern", "Anrede: Liebe Eltern."], [], []);

        var result = Base([clarifier]).WithReport(Schema2(Summary(FactCheckState.Checked), notes));

        Assert.Equal(["Anrede: Liebe Eltern"], result.WriterAssumptions);
        Assert.Equal(["Der Ton ist freundlich.", "Anrede: Liebe Eltern"], result.AllAssumptions());
    }

    [Fact]
    public void A_clean_result_has_nothing_to_review()
    {
        var result = Base().WithReport(Schema2(Summary(FactCheckState.Checked)));

        Assert.Empty(result.UnbackedPassages!);
        Assert.Empty(result.SourceIssues!);
        Assert.Equal(OrderCheckLines.ToReviewNothing, result.Checks!.ToReview);
        Assert.Contains(OrderCheckLines.FactsChecked, result.Checks.Checked);
    }

    [Fact]
    public void Placeholders_alone_are_open_points()
    {
        var result = Base(placeholders: ["[Datum]"]).WithReport(Schema2(Summary(FactCheckState.Checked)));

        Assert.Equal(OrderCheckLines.ToReviewOpenPoints, result.Checks!.ToReview);
    }

    [Fact]
    public void Shadow_findings_are_never_shown()
    {
        var result = Base().WithReport(Schema2(Summary(FactCheckState.NotShown, FactCheckMode.Shadow, open: [Unbacked, Conflict])));

        Assert.Empty(result.UnbackedPassages!);
        Assert.Empty(result.SourceIssues!);
        Assert.Equal(FactCheckState.NotShown, result.FactCheck);
        Assert.Contains(OrderCheckLines.FactsNotChecked, result.Checks!.Checked);
        Assert.Equal(OrderCheckLines.ToReviewFactsNotFullyChecked, result.Checks.ToReview);
    }

    [Fact]
    public void A_check_that_could_not_be_completed_still_shows_what_it_kept_open()
    {
        var result = Base().WithReport(Schema2(Summary(FactCheckState.NotCompleted, FactCheckMode.On, open: [Unbacked])));

        Assert.Single(result.UnbackedPassages!);
        Assert.Contains(OrderCheckLines.FactsNotCompleted + OrderCheckLines.FactsEarlierOne, result.Checks!.Checked);
    }

    [Fact]
    public void Schema_one_gets_checks_saying_facts_were_not_checked_and_nothing_else()
    {
        var report = Schema2(null) with { SchemaVersion = 1 };

        var result = Base().WithReport(report);

        Assert.Contains(OrderCheckLines.FactsNotChecked, result.Checks!.Checked);
        Assert.Null(result.WriterAssumptions);
        Assert.Null(result.WriterOpenPoints);
        Assert.Null(result.UnbackedPassages);
        Assert.Null(result.SourceIssues);
        Assert.Null(result.SourceQuotes);
        Assert.Null(result.FactCheck);
        Assert.Null(result.FactCheckScope);
    }

    [Collection(EndToEndCollection.Name)]
    [Trait("Category", "EndToEnd")]
    public sealed class OverTheRealService(PostgresFixture postgres)
    {
        [Fact]
        public async Task Stored_reports_of_both_schemas_and_no_report_give_the_right_result()
        {
            await using var host = await AtelierEndToEndHost.StartAsync(postgres);
            const string user = "e2e-result";
            var schema2 = await DeliveredAsync(host, user, RunReportJson.Serialize(Schema2(Summary(FactCheckState.OpenPoints, open: [Unbacked]))));
            var schema1 = await DeliveredAsync(host, user, """{"schemaVersion":1,"kind":"direct","checks":[{"code":"empty","passed":true,"blocking":true,"message":"ok"}],"iterations":1}""");
            var none = await DeliveredAsync(host, user, null);

            var results = await host.WithScopeAsync(async sp =>
            {
                var service = sp.GetRequiredService<IOrderService>();
                return (await service.GetResultAsync(schema2, user), await service.GetResultAsync(schema1, user), await service.GetResultAsync(none, user));
            });

            Assert.Equal(FactCheckState.OpenPoints, results.Item1!.FactCheck);
            Assert.Equal("Bitte geben Sie uns in der Gruppe Bescheid.", Assert.Single(results.Item1.UnbackedPassages!).Quote);
            Assert.Equal(OrderCheckLines.ToReviewOpenPoints, results.Item1.Checks!.ToReview);
            Assert.Equal(["Checked automatically: the text is not empty.", OrderCheckLines.FactsNotChecked], results.Item2!.Checks!.Checked);
            Assert.Null(results.Item2.UnbackedPassages);
            Assert.Null(results.Item3!.Checks);
        }

        [Fact]
        public async Task An_administrator_sees_the_about_me_details_of_an_own_order_but_never_those_of_another_person()
        {
            await using var host = await AtelierEndToEndHost.StartAsync(postgres);
            const string owner = "e2e-result-admin";
            var fromProfile = Against with { InstructionQuote = "Ich duze nie", Origin = InstructionOrigin.Memory };
            var orderId = await DeliveredAsync(host, owner, RunReportJson.Serialize(Schema2(Summary(FactCheckState.OpenPoints, open: [fromProfile]))));

            var results = await host.WithScopeAsync(async sp =>
            {
                var service = sp.GetRequiredService<IOrderService>();
                return (await service.GetResultAsync(orderId, null, owner), await service.GetResultAsync(orderId, null, "another-admin"),
                    await service.GetResultAsync(orderId, null), await service.GetResultAsync(orderId, owner));
            });

            Assert.Equal("Ich duze nie", Assert.Single(results.Item1!.UnbackedPassages!).Instruction);
            Assert.Null(Assert.Single(results.Item2!.UnbackedPassages!).Instruction);
            Assert.Null(Assert.Single(results.Item3!.UnbackedPassages!).Instruction);
            Assert.Equal("Ich duze nie", Assert.Single(results.Item4!.UnbackedPassages!).Instruction);
        }

        private static async Task<Guid> DeliveredAsync(AtelierEndToEndHost host, string user, string? reportJson)
        {
            var submission = await host.WithScopeAsync(sp => sp.GetRequiredService<IOrderService>()
                .SubmitAsync(new SubmitOrderRequest("Write a short note.", user, OrderEntry.Web)));
            await host.QueryAsync(db => db.Runs.Where(r => r.Id == submission.RunId).ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, RunStatus.Completed)
                .SetProperty(r => r.Outcome, RunOutcome.Converged)
                .SetProperty(r => r.FinalText, "Liebe Eltern, bitte geben Sie uns in der Gruppe Bescheid.")
                .SetProperty(r => r.WordCount, 9)
                .SetProperty(r => r.ReportJson, reportJson)
                .SetProperty(r => r.CompletedAt, DateTimeOffset.UtcNow)));
            return submission.OrderId;
        }
    }
}
