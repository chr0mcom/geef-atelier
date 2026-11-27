using System.Net;
using System.Text.RegularExpressions;
using Geef.Atelier.Web.Services;

namespace Geef.Atelier.Tests.DocsFeature;

/// <summary>
/// The documents are written and read on GitHub and on <c>/docs</c>. Their links to a section must work in both places, so the pages
/// of the application name their headings the way GitHub does.
/// </summary>
public sealed class DocsAnchorTests
{
    private static readonly Regex HeadingIdRegex = new(@"<h[1-6] id=""(?<id>[^""]*)""", RegexOptions.Compiled);

    private static readonly Regex LinkRegex = new(@"<a href=""(?<href>[^""]*#[^""]*)""", RegexOptions.Compiled);

    [Theory]
    [InlineData("## Funktionen schalten", "funktionen-schalten")]
    [InlineData("## Altdaten: Läufe entfernter Arbeitsweisen", "altdaten-läufe-entfernter-arbeitsweisen")]
    [InlineData("### Prüfung der Größe, über alles", "prüfung-der-größe-über-alles")]
    [InlineData("#### Aufträge: `submit_order`, `get_order_status`", "aufträge-submit_order-get_order_status")]
    [InlineData("## Old data — runs of removed ways", "old-data--runs-of-removed-ways")]
    [InlineData("## 3. A & B (D-091)", "3-a--b-d-091")]
    [InlineData("## What was checked?", "what-was-checked")]
    public void A_heading_gets_the_same_anchor_as_on_GitHub(string heading, string anchor) =>
        Assert.Equal(anchor, Assert.Single(HeadingIds(MarkdownRenderer.ToHtml(heading))));

    [Fact]
    public void Headings_with_the_same_text_are_numbered_like_on_GitHub() =>
        Assert.Equal(["grenzen", "grenzen-1", "grenzen-2"], HeadingIds(MarkdownRenderer.ToHtml("## Grenzen\n\n## Grenzen\n\n## Grenzen")));

    [Fact]
    public void Every_link_to_a_section_in_the_documents_finds_its_heading()
    {
        var docs = Path.Combine(RepositoryRoot(), "docs");
        var pages = Directory.GetFiles(docs, "*.md")
            .ToDictionary(file => Path.GetFileName(file),
                file => MarkdownRenderer.ToHtml(DocsService.WithoutDiagramsAndComments(File.ReadAllText(file))), StringComparer.Ordinal);
        var anchors = pages.ToDictionary(p => p.Key, p => HeadingIds(p.Value).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        var broken = new List<string>();
        var checkedLinks = 0;
        foreach (var (file, html) in pages)
        {
            foreach (Match link in LinkRegex.Matches(html))
            {
                var href = WebUtility.HtmlDecode(link.Groups["href"].Value);
                if (href.Contains("://", StringComparison.Ordinal))
                    continue;
                var hash = href.IndexOf('#', StringComparison.Ordinal);
                var target = hash == 0 ? file : href[..hash];
                if (!anchors.TryGetValue(target, out var ids))
                    continue;
                checkedLinks++;
                var anchor = Uri.UnescapeDataString(href[(hash + 1)..]);
                if (!ids.Contains(anchor))
                    broken.Add($"{file} → {target}#{anchor}");
            }
        }

        Assert.True(checkedLinks > 10, $"Only {checkedLinks} links to sections were found; the test no longer reads the documents.");
        Assert.True(broken.Count == 0, "Links to sections that do not exist:\n" + string.Join('\n', broken));
    }

    private static List<string> HeadingIds(string html) =>
        HeadingIdRegex.Matches(html).Select(m => WebUtility.HtmlDecode(m.Groups["id"].Value)).ToList();

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
