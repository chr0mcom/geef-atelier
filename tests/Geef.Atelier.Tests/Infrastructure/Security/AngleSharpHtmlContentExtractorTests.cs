using Geef.Atelier.Infrastructure.Security;

namespace Geef.Atelier.Tests.Infrastructure.Security;

public sealed class AngleSharpHtmlContentExtractorTests
{
    private static AngleSharpHtmlContentExtractor CreateExtractor() => new();

    [Fact]
    public async Task ExtractAsync_ExtractsTitle()
    {
        var extractor = CreateExtractor();
        const string html = "<html><head><title>Test Title</title></head><body><p>Content here</p></body></html>";
        var result = await extractor.ExtractAsync(html, false, CancellationToken.None);
        Assert.Equal("Test Title", result.Title);
        Assert.Contains("Content here", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_PrefersOgTitle()
    {
        var extractor = CreateExtractor();
        const string html = """
            <html>
                <head>
                    <title>Page Title</title>
                    <meta property="og:title" content="OG Title">
                </head>
                <body>content</body>
            </html>
            """;
        var result = await extractor.ExtractAsync(html, false, CancellationToken.None);
        Assert.Equal("OG Title", result.Title);
    }

    [Fact]
    public async Task ExtractAsync_StripBoilerplate_RemovesNavAndScript()
    {
        var extractor = CreateExtractor();
        const string html = """
            <html><body>
                <nav>NAV CONTENT</nav>
                <script>alert('xss')</script>
                <main><p>Main content here</p></main>
                <footer>Footer text</footer>
            </body></html>
            """;
        var result = await extractor.ExtractAsync(html, stripBoilerplate: true, CancellationToken.None);
        Assert.Contains("Main content here", result.Text);
        Assert.DoesNotContain("NAV CONTENT", result.Text);
        Assert.DoesNotContain("alert", result.Text);
        Assert.DoesNotContain("Footer text", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_NoStripBoilerplate_KeepsNav()
    {
        var extractor = CreateExtractor();
        const string html = "<html><body><nav>NAV</nav><p>Main</p></body></html>";
        var result = await extractor.ExtractAsync(html, stripBoilerplate: false, CancellationToken.None);
        Assert.Contains("Main", result.Text);
        Assert.Contains("NAV", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_EmptyHtml_ReturnsEmptyText()
    {
        var extractor = CreateExtractor();
        var result = await extractor.ExtractAsync("", false, CancellationToken.None);
        Assert.Null(result.Title);
        Assert.Equal(string.Empty, result.Text);
    }

    [Fact]
    public async Task ExtractAsync_NormalizesWhitespace()
    {
        var extractor = CreateExtractor();
        const string html = "<html><body><p>Word1   Word2\n\n\nWord3</p></body></html>";
        var result = await extractor.ExtractAsync(html, false, CancellationToken.None);
        // Whitespace-normalization collapses runs of 2+ whitespace chars to a single space
        Assert.DoesNotContain("   ", result.Text); // no triple spaces
    }

    [Fact]
    public async Task ExtractAsync_StripBoilerplate_RemovesStyle()
    {
        var extractor = CreateExtractor();
        const string html = """
            <html><head><style>body { color: red; }</style></head>
            <body><p>Visible text</p></body></html>
            """;
        var result = await extractor.ExtractAsync(html, stripBoilerplate: true, CancellationToken.None);
        Assert.Contains("Visible text", result.Text);
        Assert.DoesNotContain("color: red", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_NoOgTitle_FallsBackToTitleTag()
    {
        var extractor = CreateExtractor();
        const string html = "<html><head><title>Fallback Title</title></head><body>text</body></html>";
        var result = await extractor.ExtractAsync(html, false, CancellationToken.None);
        Assert.Equal("Fallback Title", result.Title);
    }

    [Fact]
    public async Task ExtractAsync_NoTitleOrOgTitle_ReturnsNullTitle()
    {
        var extractor = CreateExtractor();
        const string html = "<html><body><p>Just content, no title</p></body></html>";
        var result = await extractor.ExtractAsync(html, false, CancellationToken.None);
        Assert.Null(result.Title);
        Assert.Contains("Just content", result.Text);
    }
}
