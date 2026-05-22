using Geef.Atelier.Web.Services;

namespace Geef.Atelier.Tests.DocsFeature;

public sealed class DocsAllowlistTests
{
    // ── Allowlist size ────────────────────────────────────────────────────

    [Fact]
    public void Entries_HasNineAllowlistedDocs()
    {
        Assert.Equal(9, DocsService.Entries.Count);
    }

    // ── Allowed slugs ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("readme")]
    [InlineData("01-vision-and-scope")]
    [InlineData("02-architecture")]
    [InlineData("03-walking-skeleton-plan")]
    [InlineData("04-mcp-integration")]
    [InlineData("06-reviewer-calibration")]
    [InlineData("07-design-system")]
    [InlineData("08-crew-system")]
    [InlineData("09-endpoint-reference")]
    public void IsAllowed_KnownSlug_ReturnsTrue(string slug)
    {
        Assert.True(DocsService.IsAllowed(slug));
    }

    // ── Rejected slugs ────────────────────────────────────────────────────

    [Theory]
    [InlineData("05-decisions-log")]         // intentionally excluded
    [InlineData("05-decisions-log_de")]       // _de variant also excluded
    [InlineData("")]                          // empty
    [InlineData("../../../etc/passwd")]       // path traversal
    [InlineData("README")]                    // case-sensitive
    [InlineData("00-nonexistent")]            // not in list
    [InlineData("admin")]
    public void IsAllowed_UnknownOrDisallowedSlug_ReturnsFalse(string slug)
    {
        Assert.False(DocsService.IsAllowed(slug));
    }

    // ── Slug/BaseName/Title consistency ──────────────────────────────────

    [Fact]
    public void Entries_AllHaveNonEmptySlugBasenameTitle()
    {
        foreach (var entry in DocsService.Entries)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Slug), $"Slug is empty for entry");
            Assert.False(string.IsNullOrWhiteSpace(entry.BaseName), $"BaseName is empty for slug '{entry.Slug}'");
            Assert.False(string.IsNullOrWhiteSpace(entry.Title), $"Title is empty for slug '{entry.Slug}'");
        }
    }

    [Fact]
    public void Entries_AllSlugsMatchIsAllowed()
    {
        foreach (var entry in DocsService.Entries)
        {
            Assert.True(DocsService.IsAllowed(entry.Slug), $"Slug '{entry.Slug}' in Entries but not in allowlist");
        }
    }

    // ── GetMarkdown returns null for disallowed ───────────────────────────

    [Fact]
    public void GetMarkdown_DisallowedSlug_ReturnsNull()
    {
        var svc = new DocsService();
        var result = svc.GetMarkdown("05-decisions-log");
        Assert.Null(result);
    }

    [Fact]
    public void GetMarkdown_PathTraversal_ReturnsNull()
    {
        var svc = new DocsService();
        var result = svc.GetMarkdown("../../../CLAUDE.md");
        Assert.Null(result);
    }

    // ── GetMarkdown returns content for allowed slugs ─────────────────────

    [Fact]
    public void GetMarkdown_ReadmeSlug_ReturnsNonNullContent()
    {
        var svc = new DocsService();
        // Embedded resource is built into the Web assembly; reading it here verifies
        // the EmbeddedResource MSBuild item is configured and the slug mapping is correct.
        var result = svc.GetMarkdown("readme");
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    // ── Language fallback ─────────────────────────────────────────────────

    [Fact]
    public void GetMarkdown_GermanRequestForSlugWithoutDeVariant_FallsBackToEnglish()
    {
        // If a _de file exists it returns German; if not it falls back to English.
        // Either way the result must be non-null for a known slug.
        var svc = new DocsService();
        var result = svc.GetMarkdown("readme", german: true);
        Assert.NotNull(result);
    }
}
