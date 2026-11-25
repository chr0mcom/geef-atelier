using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Tests.Domain.Reports;

public sealed class RunReportJsonTests
{
    private static RunReport Sample() => new(
        RunReport.CurrentSchemaVersion,
        RunReport.DirectKind,
        OrderConstraints.None with
        {
            Length = new LengthConstraint(LengthUnit.Words, null, 200, "max. 200 Wörter", LengthKind.AtMost),
            Language = "de",
            ForbiddenTerms = [new TermRule("Synergie", "Vermeide „Synergie“")],
        },
        [new DraftCheckResult(DraftCheckCodes.Length, false, true, "The text has 263 words.", "max. 200 Wörter")],
        ["[Lieferdatum]"],
        [new RunReportSource(Guid.NewGuid(), "angebot.pdf", SourceUsageMode.FullText, 1234)],
        2,
        2,
        new ReadEvidence(160_000, 1.9, 105_911, 40_000, 0.378, false),
        ["Parts of your files were searched, not read in full."]);

    [Fact]
    public void Round_trips_with_camel_case_and_enum_names()
    {
        var report = Sample();

        var json = RunReportJson.Serialize(report);
        var parsed = RunReportJson.TryParse(json);

        Assert.Contains("\"schemaVersion\":2", json);
        Assert.Contains("\"kind\":\"direct\"", json);
        Assert.Contains("\"mode\":\"FullText\"", json);
        Assert.Contains("\"kind\":\"AtMost\"", json);
        Assert.DoesNotContain("openChecks", json);
        Assert.NotNull(parsed);
        Assert.Equal(report.Constraints.Length, parsed.Constraints.Length);
        Assert.Equal(report.Checks, parsed.Checks);
        Assert.Equal(report.Placeholders, parsed.Placeholders);
        Assert.Equal(report.Sources, parsed.Sources);
        Assert.Equal(report.ReadEvidence, parsed.ReadEvidence);
        Assert.Equal(report.Notes, parsed.Notes);
        Assert.Single(parsed.OpenChecks);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    public void Unreadable_reports_give_null(string? json) =>
        Assert.Null(RunReportJson.TryParse(json));

    [Fact]
    public void Missing_lists_become_empty()
    {
        var parsed = RunReportJson.TryParse("{\"schemaVersion\":1,\"kind\":\"direct\",\"iterations\":1}");

        Assert.NotNull(parsed);
        Assert.Empty(parsed.Checks);
        Assert.Empty(parsed.Placeholders);
        Assert.Empty(parsed.Sources);
        Assert.Empty(parsed.Constraints.Notes);
        Assert.Null(parsed.ReadEvidence);
    }

    [Fact]
    public void Schema_two_round_trips_notes_quotes_fact_check_totals_and_corrections()
    {
        var conflict = new FactFinding("f2", FactFindingKind.SourceConflict, null, "Totals differ.", null, "Summe 62 €", "22 + 31 + 19", "notizen.txt", null, false, QuoteMatchKind.NotFound);
        var open = new FactFinding("f1", FactFindingKind.Unsupported, "Bitte geben Sie uns in der Gruppe Bescheid.", "No such way to reply.", null, null, null, null, null, true, QuoteMatchKind.Exact);
        var report = Sample() with
        {
            NotesStatus = WriterNotesStatus.Ok,
            WriterNotes = new WriterNotes(["Anrede: Liebe Eltern"], ["Summe 62 € passt nicht zu 72 €"], [new WriterQuote("S1", "Schere 22 €")]),
            SourceQuotes = [new RunReportQuote(Guid.NewGuid(), "S1", "notizen.txt", "Schere 22 €", QuoteMatchKind.Exact)],
            UnverifiedQuotes = 2,
            FactCheck = new FactCheckSummary(FactCheckMode.Report, FactCheckScope.Full, FactCheckState.OpenPoints, [open], [conflict], [], 1, "gpt-astra-latest", false, "fact-check-1"),
            SourceArithmetic = new SourceArithmeticResult(1, [new SourceArithmeticIssue("S1", "notizen.txt", ArithmeticForm.ListTotal, "Summe 62 €", ["22 €", "31 €", "19 €"], "62 €", "72 €", TotalUseInText.Computed)]),
            Corrections = [new RunReportCorrection("length", null), new RunReportCorrection("fact.unsupported", "Die Fotos liegen bei.")],
        };

        var json = RunReportJson.Serialize(report);
        var parsed = RunReportJson.TryParse(json)!;

        Assert.Contains("\"notesStatus\":\"Ok\"", json);
        Assert.Contains("\"state\":\"OpenPoints\"", json);
        Assert.Equal(WriterNotesStatus.Ok, parsed.NotesStatus);
        Assert.Equal(report.WriterNotes!.OpenPoints, parsed.WriterNotes!.OpenPoints);
        Assert.Equal(report.SourceQuotes, parsed.SourceQuotes);
        Assert.Equal(2, parsed.UnverifiedQuotes);
        Assert.Equal(FactCheckState.OpenPoints, parsed.FactCheck!.State);
        Assert.Equal(open, Assert.Single(parsed.FactCheck.Open));
        Assert.Equal(conflict, Assert.Single(parsed.FactCheck.SourceConflicts));
        Assert.Equal(TotalUseInText.Computed, Assert.Single(parsed.SourceArithmetic!.Issues).UseInText);
        Assert.Equal(report.Corrections, parsed.Corrections);
    }

    [Fact]
    public void A_schema_one_report_reads_with_empty_stage_three_fields()
    {
        var parsed = RunReportJson.TryParse("""
            {"schemaVersion":1,"kind":"direct","constraints":{},"checks":[{"code":"length","passed":true,"blocking":true,"message":"The text has 40 words."}],
             "placeholders":[],"sources":[],"deliveredIteration":1,"iterations":1,"notes":[]}
            """);

        Assert.NotNull(parsed);
        Assert.Equal(1, parsed.SchemaVersion);
        Assert.Equal(WriterNotesStatus.Off, parsed.NotesStatus);
        Assert.Same(WriterNotes.Empty, parsed.WriterNotes);
        Assert.Empty(parsed.SourceQuotes!);
        Assert.Empty(parsed.Corrections!);
        Assert.Equal(0, parsed.UnverifiedQuotes);
        Assert.Null(parsed.FactCheck);
        Assert.Null(parsed.SourceArithmetic);
        Assert.Single(parsed.Checks);
    }
}
