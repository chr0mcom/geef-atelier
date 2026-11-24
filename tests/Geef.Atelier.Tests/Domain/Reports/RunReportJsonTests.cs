using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;

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

        Assert.Contains("\"schemaVersion\":1", json);
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
}
