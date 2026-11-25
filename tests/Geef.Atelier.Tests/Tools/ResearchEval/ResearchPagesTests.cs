using Geef.Atelier.Infrastructure.Research;
using Geef.Atelier.Tests.Intake;
using Geef.Atelier.Tools.FactCheckEval;
using Geef.Atelier.Tools.IntakeEval;
using Geef.Atelier.Tools.ResearchEval;
using CorpusFile = Geef.Atelier.Tools.IntakeEval.CorpusFile;

namespace Geef.Atelier.Tests.ResearchEvalTool;

/// <summary>
/// The measurement numbers the pages exactly like the research service (the page ids are part of the passage picker's prompt), and
/// the development orders kept for the paired measurement R3 stay the ones that <c>tools/Eval/geef_eval/research_pairs.py</c> names.
/// </summary>
public sealed class ResearchPagesTests
{
    private static readonly IReadOnlyList<IReadOnlyList<(string Url, string Title, string? RawText)>> Results =
    [
        [("https://a.example/1", "A1", "Text A1"), ("https://a.example/2", "A2", null), ("https://shared.example/x", "Shared", "Text X")],
        [("https://shared.example/x", "Shared again", "Other text"), ("https://b.example/1", "B1", "  "), ("https://b.example/2", "B2", "Text B2")],
        [("https://a.example/1", "A1 again", "Text A1"), ("https://c.example/1", "C1", "Text C1")],
    ];

    [Fact]
    public void The_measurement_numbers_pages_like_the_research_service()
    {
        var service = Results.Select(hits => (IReadOnlyList<TavilyHit>)hits.Select(h => new TavilyHit(h.Url, h.Title, h.RawText)).ToList()).ToList().ToResearchPages();

        var measured = ResearchPages.From(Results);

        Assert.Equal(service, measured);
        Assert.Equal(
            [("P1", 1, "https://a.example/1"), ("P2", 1, "https://shared.example/x"), ("P3", 2, "https://b.example/2"), ("P4", 3, "https://c.example/1")],
            measured.Select(p => (p.Id, p.Topic, p.Url)));
        Assert.Equal("Shared", measured[1].Title);
    }

    [Fact]
    public void The_orders_kept_for_R3_are_the_fifteen_that_research_pairs_reads()
    {
        var directory = IntakeCorpusTests.CorpusDirectory();
        var corpus = new[] { "intake-corpus.jsonl", "intake-corpus-second.jsonl" }.SelectMany(n => Corpus.Load(Path.Combine(directory, n))).ToList();
        var (orders, problems) = ResearchCorpus.Load(JsonLines.Load<StoredGateLine>(Path.Combine(directory, "results-20261125.jsonl")), corpus);

        Assert.Empty(problems);
        Assert.Equal(
            ["main-061", "main-063", "main-066", "main-073", "main-094", "main-100", "main-108", "main-117",
             "second-002", "second-003", "second-006", "second-009", "second-018", "second-019", "second-035"],
            ResearchCorpus.Reserved(orders).Select(o => o.Id));
    }

    [Fact]
    public void The_measurement_hands_texts_and_names_of_the_files_to_the_query_gate_like_Atelier()
    {
        IReadOnlyList<CorpusFile> files = [new("mietvertrag-hartmann.txt", "text", "Vertragstext.", null), new("leer.md", "markdown", "", null)];

        Assert.Equal(["Vertragstext.", "mietvertrag-hartmann.txt", "leer.md"], ResearchCorpus.FileWords(files));
    }
}
