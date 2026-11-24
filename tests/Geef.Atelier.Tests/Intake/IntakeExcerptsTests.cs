using Geef.Atelier.Application.Intake;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Tests.Pipeline.Direct;

namespace Geef.Atelier.Tests.Intake;

public sealed class IntakeExcerptsTests
{
    private static readonly IntakeOptions Small = new() { ExcerptHeadChars = 10, ExcerptTailChars = 5, ExcerptTotalChars = 60 };

    private static async Task<IReadOnlyList<Geef.Atelier.Application.Intake.IntakeFile>> Build(InMemorySourceRepository sources, Guid orderId, IntakeOptions options) =>
        IntakeExcerpts.Build(await sources.ListForOrderAsync(orderId, true), options);

    [Fact]
    public async Task Short_files_are_shown_whole_and_long_files_by_start_and_end()
    {
        var sources = new InMemorySourceRepository();
        var orderId = Guid.NewGuid();
        sources.Attach(orderId, "kurz.txt", SourceKind.Text, "Kurzer Text");
        sources.Attach(orderId, "lang.txt", SourceKind.Text, "ABSENDER-Mitte des langen Textes-GRUSS");

        var files = await Build(sources, orderId, Small);

        Assert.Equal([1, 2], files.Select(f => f.Number));
        Assert.Equal(("Kurzer Text", false), (files[0].Excerpt, files[0].Truncated));
        Assert.Equal(($"ABSENDER-M{IntakeExcerpts.Gap}GRUSS", true), (files[1].Excerpt, files[1].Truncated));
    }

    [Fact]
    public async Task Page_markers_are_removed_and_the_page_selection_applies()
    {
        var sources = new InMemorySourceRepository();
        var orderId = Guid.NewGuid();
        sources.Attach(orderId, "bericht.pdf", SourceKind.Pdf, "[[page 1]]\nSeite eins\n[[page 2]]\nSeite zwei", pageCount: 2, pageSelection: "2");

        var file = Assert.Single(await Build(sources, orderId, new IntakeOptions()));

        Assert.Equal("Seite zwei", file.Excerpt);
        Assert.Equal(2, file.PageCount);
    }

    [Fact]
    public async Task Files_beyond_the_budget_or_the_fifth_are_listed_by_name_only()
    {
        var sources = new InMemorySourceRepository();
        var orderId = Guid.NewGuid();
        for (var i = 0; i < 7; i++)
            sources.Attach(orderId, $"datei{i}.txt", SourceKind.Text, $"Text {i}");
        var budget = new IntakeOptions { ExcerptHeadChars = 10, ExcerptTailChars = 5, ExcerptTotalChars = 20 };

        var files = await Build(sources, orderId, budget);

        Assert.Equal(7, files.Count);
        Assert.Equal(["Text 0", "Text 1", "Text 2", "", "", "", ""], files.Select(f => f.Excerpt));

        var roomy = await Build(sources, orderId, new IntakeOptions());
        Assert.Equal(IntakeExcerpts.MaxFilesWithExcerpt, roomy.Count(f => f.Excerpt.Length > 0));
        Assert.Equal("", roomy[5].Excerpt);
    }
}
