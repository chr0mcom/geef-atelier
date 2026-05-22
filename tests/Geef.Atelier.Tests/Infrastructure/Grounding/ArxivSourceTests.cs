using System.Net;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Infrastructure.Grounding.AcademicSearch;
using Geef.Atelier.Tests.Domain.Crew;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

/// <summary>
/// Tests for <see cref="ArxivSource"/> — covers Atom/XML parsing, author extraction,
/// identifier extraction, and HTTP failure handling.
/// </summary>
public sealed class ArxivSourceTests
{
    private const string SinglePaperAtom = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom" xmlns:arxiv="http://arxiv.org/schemas/atom">
          <entry>
            <id>http://arxiv.org/abs/2301.07234v1</id>
            <title>Neural Scaling Laws Revisited</title>
            <summary>This paper revisits scaling laws with new experiments.</summary>
            <author><name>Alice Smith</name></author>
            <author><name>Bob Jones</name></author>
            <published>2023-01-18T00:00:00Z</published>
            <link title="doi" href="https://doi.org/10.1234/test.56789" />
          </entry>
        </feed>
        """;

    private const string MultiPaperAtom = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry>
            <id>http://arxiv.org/abs/2301.00001v1</id>
            <title>Paper One</title>
            <summary>Abstract one.</summary>
            <author><name>Author A</name></author>
            <published>2023-01-01T00:00:00Z</published>
          </entry>
          <entry>
            <id>http://arxiv.org/abs/2301.00002v1</id>
            <title>Paper Two</title>
            <summary>Abstract two.</summary>
            <author><name>Author B</name></author>
            <published>2023-01-02T00:00:00Z</published>
          </entry>
          <entry>
            <id>http://arxiv.org/abs/2301.00003v1</id>
            <title>Paper Three</title>
            <summary>Abstract three.</summary>
            <published>2023-01-03T00:00:00Z</published>
          </entry>
        </feed>
        """;

    private const string NoAbstractAtom = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry>
            <id>http://arxiv.org/abs/2301.99999v1</id>
            <title>Minimal Paper</title>
          </entry>
        </feed>
        """;

    private const string NewlineTitleAtom = """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <entry>
            <id>http://arxiv.org/abs/2301.11111v1</id>
            <title>Title  with
                   Newlines  and   Spaces</title>
            <summary>Abstract.</summary>
            <published>2023-01-01T00:00:00Z</published>
          </entry>
        </feed>
        """;

    [Fact]
    public async Task SearchAsync_ValidAtomXml_ReturnsPapers()
    {
        var source = BuildSource(FakeHttpHandler.Ok(SinglePaperAtom));
        var papers = await source.SearchAsync("scaling laws", DefaultOptions(), CancellationToken.None);
        Assert.Single(papers);
    }

    [Fact]
    public async Task SearchAsync_ParsesTitle()
    {
        var source = BuildSource(FakeHttpHandler.Ok(SinglePaperAtom));
        var papers = await source.SearchAsync("q", DefaultOptions(), CancellationToken.None);
        Assert.Equal("Neural Scaling Laws Revisited", papers[0].Title);
    }

    [Fact]
    public async Task SearchAsync_ParsesAbstract()
    {
        var source = BuildSource(FakeHttpHandler.Ok(SinglePaperAtom));
        var papers = await source.SearchAsync("q", DefaultOptions(), CancellationToken.None);
        Assert.Equal("This paper revisits scaling laws with new experiments.", papers[0].Abstract);
    }

    [Fact]
    public async Task SearchAsync_JoinsMultipleAuthorsWithSemicolon()
    {
        var source = BuildSource(FakeHttpHandler.Ok(SinglePaperAtom));
        var papers = await source.SearchAsync("q", DefaultOptions(), CancellationToken.None);
        Assert.Equal("Alice Smith; Bob Jones", papers[0].Authors);
    }

    [Fact]
    public async Task SearchAsync_ExtractsArxivIdFromUrl()
    {
        var source = BuildSource(FakeHttpHandler.Ok(SinglePaperAtom));
        var papers = await source.SearchAsync("q", DefaultOptions(), CancellationToken.None);
        Assert.Equal("2301.07234v1", papers[0].ArxivId);
    }

    [Fact]
    public async Task SearchAsync_ExtractsDoiFromDoiLink()
    {
        var source = BuildSource(FakeHttpHandler.Ok(SinglePaperAtom));
        var papers = await source.SearchAsync("q", DefaultOptions(), CancellationToken.None);
        Assert.Equal("10.1234/test.56789", papers[0].Doi);
    }

    [Fact]
    public async Task SearchAsync_ParsesPublishedDate()
    {
        var source = BuildSource(FakeHttpHandler.Ok(SinglePaperAtom));
        var papers = await source.SearchAsync("q", DefaultOptions(), CancellationToken.None);
        Assert.Equal(2023, papers[0].PublishedDate?.Year);
        Assert.Equal(1, papers[0].PublishedDate?.Month);
        Assert.Equal(18, papers[0].PublishedDate?.Day);
    }

    [Fact]
    public async Task SearchAsync_RespectsMaxPapers()
    {
        var source = BuildSource(FakeHttpHandler.Ok(MultiPaperAtom));
        var opts = new AcademicSearchOptions(MaxPapers: 2, DateFrom: null, Fields: null, ApiKey: null);
        var papers = await source.SearchAsync("q", opts, CancellationToken.None);
        Assert.Equal(2, papers.Count);
    }

    [Fact]
    public async Task SearchAsync_NoAbstractEntry_ProducesNullAbstract()
    {
        var source = BuildSource(FakeHttpHandler.Ok(NoAbstractAtom));
        var papers = await source.SearchAsync("q", DefaultOptions(), CancellationToken.None);
        Assert.Single(papers);
        Assert.Null(papers[0].Abstract);
    }

    [Fact]
    public async Task SearchAsync_MultipleEntries_AllReturned()
    {
        var source = BuildSource(FakeHttpHandler.Ok(MultiPaperAtom));
        var papers = await source.SearchAsync("q", DefaultOptions(10), CancellationToken.None);
        Assert.Equal(3, papers.Count);
    }

    [Fact]
    public async Task SearchAsync_TitleWithNewlines_IsSanitized()
    {
        var source = BuildSource(FakeHttpHandler.Ok(NewlineTitleAtom));
        var papers = await source.SearchAsync("q", DefaultOptions(), CancellationToken.None);
        Assert.Single(papers);
        // Newlines are removed; leading/trailing whitespace is trimmed
        Assert.DoesNotContain('\n', papers[0].Title);
        Assert.False(papers[0].Title.StartsWith(" "), "Title should not start with a space");
        Assert.False(papers[0].Title.EndsWith(" "), "Title should not end with a space");
    }

    [Fact]
    public async Task SearchAsync_HttpFailure_ReturnsEmpty()
    {
        var source = BuildSource(FakeHttpHandler.Fail(HttpStatusCode.ServiceUnavailable));
        var papers = await source.SearchAsync("q", DefaultOptions(), CancellationToken.None);
        Assert.Empty(papers);
    }

    [Fact]
    public async Task SearchAsync_InvalidXml_ReturnsEmpty()
    {
        var source = BuildSource(FakeHttpHandler.Ok("this is not xml at all <<<"));
        var papers = await source.SearchAsync("q", DefaultOptions(), CancellationToken.None);
        Assert.Empty(papers);
    }

    [Fact]
    public async Task SearchAsync_EmptyFeed_ReturnsEmpty()
    {
        const string emptyFeed = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
            </feed>
            """;
        var source = BuildSource(FakeHttpHandler.Ok(emptyFeed));
        var papers = await source.SearchAsync("q", DefaultOptions(), CancellationToken.None);
        Assert.Empty(papers);
    }

    [Fact]
    public void SourceName_IsArxiv()
    {
        var source = BuildSource(FakeHttpHandler.Ok(SinglePaperAtom));
        Assert.Equal("arxiv", source.SourceName);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ArxivSource BuildSource(FakeHttpHandler handler)
    {
        var client = new HttpClient(handler);
        return new ArxivSource(
            new SingleNamedHttpClientFactory("academic-arxiv", client),
            NullLogger<ArxivSource>.Instance);
    }

    private static AcademicSearchOptions DefaultOptions(int maxPapers = 5)
        => new(MaxPapers: maxPapers, DateFrom: null, Fields: null, ApiKey: null);
}
