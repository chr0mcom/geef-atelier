using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Pipeline.Direct;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>The report of a run with the passages looked up on the web: marked, listed after the files, and the writer's quotes from them kept apart.</summary>
public sealed class DirectReportBuilderResearchTests
{
    private const string Notes = "Aushang Lager. Pause von 12 bis 13 Uhr.";
    private const string Passage = "Ab dem 1. Januar 2027 beträgt der gesetzliche Mindestlohn 14,60 Euro brutto je Stunde.";
    private const string Text = "Liebe Kolleginnen und Kollegen, ab dem 1. Januar 2027 beträgt der Mindestlohn 14,60 Euro.";

    private static readonly Guid FileId = Guid.NewGuid();
    private static readonly PromptSource File = new(FileId, "S1", "notizen.txt", SourceKind.Text, null, null, Notes, Notes.Length);
    private static readonly string W1Text = DossierRenderer.Render(ResearchSamples.Found(ResearchSamples.Entry(1, Passage)));
    private static readonly PromptSource W1 = new(Guid.Empty, "W1", DossierRenderer.FileName, SourceKind.Text, null, null, W1Text, W1Text.Length, LookedUp: true);

    private static RunSourceUsageEntity Usage(Guid? sourceId, string fileName, string target, int chars, int position) => new()
    {
        Id = Guid.NewGuid(), RunId = Guid.NewGuid(), SourceId = sourceId, FileName = fileName, Mode = SourceUsageMode.FullText, Target = target,
        CharsIncluded = chars, PagesIncluded = null, Position = position, CreatedAt = DateTimeOffset.UtcNow,
    };

    private static readonly IReadOnlyList<RunSourceUsageEntity> Usages =
    [
        Usage(null, DossierRenderer.FileName, OrderGroundingStep.ResearchTarget, W1Text.Length, 1),
        Usage(FileId, "notizen.txt", OrderGroundingStep.WriterTarget, Notes.Length, 0),
    ];

    [Fact]
    public void The_report_is_marked_and_lists_the_passages_after_the_files()
    {
        var report = DirectReportBuilder.Build(OrderConstraints.None, Text, Usages, 1, 1, null, new DirectRunFacts(Sources: [File, W1]));

        Assert.True(report.LookedUp);
        Assert.Equal([("notizen.txt", false, (Guid?)FileId), (DossierRenderer.FileName, true, null)],
            report.Sources.Select(s => (s.FileName, s.LookedUp, s.SourceId)));

        var parsed = RunReportJson.TryParse(RunReportJson.Serialize(report))!;
        Assert.True(parsed.LookedUp);
        Assert.Equal([false, true], parsed.Sources.Select(s => s.LookedUp));
        Assert.Equal(RunReport.CurrentSchemaVersion, parsed.SchemaVersion);
    }

    [Fact]
    public void Without_passages_the_report_is_unchanged()
    {
        var usages = new[] { Usage(FileId, "notizen.txt", OrderGroundingStep.WriterTarget, Notes.Length, 0) };

        var report = DirectReportBuilder.Build(OrderConstraints.None, Text, usages, 1, 1, null, new DirectRunFacts(Sources: [File]));

        Assert.False(report.LookedUp);
        Assert.DoesNotContain("lookedUp", RunReportJson.Serialize(report), StringComparison.Ordinal);
    }

    [Fact]
    public void The_marker_also_holds_when_the_usage_rows_could_not_be_stored()
    {
        var report = DirectReportBuilder.Build(OrderConstraints.None, Text, [], 1, 1, null, new DirectRunFacts(Sources: [File, W1]));

        Assert.True(report.LookedUp);
    }

    [Fact]
    public void Quotes_from_the_passages_are_neither_file_quotes_nor_unverified()
    {
        var notes = new WriterNotes([], [],
        [
            new WriterQuote("W1", "beträgt der gesetzliche Mindestlohn 14,60 Euro"),
            new WriterQuote("w1", "Ein Satz, den es auf keiner Seite gibt."),
            new WriterQuote("S1", "Pause von 12 bis 13 Uhr"),
        ]);
        var material = new FactMaterial("Schreib einen Aushang.", [], [], null,
            [new FactSource("S1", "notizen.txt", Notes), new FactSource("W1", DossierRenderer.FileName, W1Text, LookedUp: true)], FactCheckScope.Full, null);

        var report = DirectReportBuilder.Build(OrderConstraints.None, Text, Usages, 1, 1, null,
            new DirectRunFacts(new WriterNotesSplit(Text, notes, WriterNotesStatus.Ok), [File, W1], material));

        var quote = Assert.Single(report.SourceQuotes!);
        Assert.Equal(("S1", "notizen.txt"), (quote.SourceLabel, quote.FileName));
        Assert.Equal(0, report.UnverifiedQuotes);
        Assert.Equal(3, report.WriterNotes!.Quotes.Count);
    }

    [Fact]
    public void A_quote_naming_W1_in_a_run_without_passages_stays_unverified()
    {
        var notes = new WriterNotes([], [], [new WriterQuote("W1", "beträgt der gesetzliche Mindestlohn 14,60 Euro")]);

        var report = DirectReportBuilder.Build(OrderConstraints.None, Text, [], 1, 1, null,
            new DirectRunFacts(new WriterNotesSplit(Text, notes, WriterNotesStatus.Ok), [File]));

        Assert.Empty(report.SourceQuotes!);
        Assert.Equal(1, report.UnverifiedQuotes);
        Assert.False(report.LookedUp);
    }
}
