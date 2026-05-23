using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Geef.Atelier.Web.Services;

/// <summary>
/// Provides access to the curated allowlist of documentation files embedded in the assembly.
/// Path traversal is impossible — only allowlisted slugs map to specific embedded resources.
/// </summary>
public sealed class DocsService
{
    // Slug → base filename (without language suffix and .md extension).
    // Language variant is selected at read-time via the lang parameter.
    // 05-decisions-log is intentionally excluded (contains internal architecture details).
    public static readonly IReadOnlyList<DocEntry> Entries =
    [
        new("readme",                   "README",                   "Overview / README"),
        new("01-vision-and-scope",      "01-vision-and-scope",      "1 · Vision & Scope"),
        new("02-architecture",          "02-architecture",          "2 · Architecture"),
        new("03-walking-skeleton-plan", "03-walking-skeleton-plan", "3 · Walking Skeleton Plan"),
        new("04-mcp-integration",       "04-mcp-integration",       "4 · MCP Integration"),
        new("06-reviewer-calibration",  "06-reviewer-calibration",  "6 · Reviewer Calibration"),
        new("07-design-system",         "07-design-system",         "7 · Design System"),
        new("08-crew-system",           "08-crew-system",           "8 · Crew System"),
        new("09-endpoint-reference",    "09-endpoint-reference",    "9 · Endpoint Reference"),
    ];

    private static readonly IReadOnlyDictionary<string, string> SlugToBaseName =
        Entries.ToDictionary(e => e.Slug, e => e.BaseName);

    private static readonly IReadOnlyDictionary<string, string> BaseNameToSlug =
        Entries.ToDictionary(e => e.BaseName, e => e.Slug, StringComparer.OrdinalIgnoreCase);

    // Rewrites relative Markdown links (e.g. href="08-crew-system_de.md") to Blazor routes.
    private static readonly Regex DocLinkRegex = new(
        @"href=""([^""/\\]+?)(_de)?\.md""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Assembly Asm = typeof(DocsService).Assembly;
    private const string ResourcePrefix = "Geef.Atelier.Web.EmbeddedDocs.";

    /// <summary>Reads the Markdown content for the given slug and language.
    /// Returns <see langword="null"/> when the slug is not in the allowlist or the resource is missing.</summary>
    public string? GetMarkdown(string slug, bool german = false)
    {
        if (!SlugToBaseName.TryGetValue(slug, out var baseName))
            return null;

        var filename = german ? $"{baseName}_de.md" : $"{baseName}.md";
        var resourceName = ResourcePrefix + filename;

        using var stream = Asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            // Fall back to English if _de variant is missing.
            if (german)
            {
                var fallback = ResourcePrefix + $"{baseName}.md";
                using var fbStream = Asm.GetManifestResourceStream(fallback);
                if (fbStream is null) return null;
                using var fbReader = new StreamReader(fbStream);
                return fbReader.ReadToEnd();
            }
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Returns true if the given slug is in the allowlist.</summary>
    public static bool IsAllowed(string slug) => SlugToBaseName.ContainsKey(slug);

    /// <summary>Returns the <see cref="MarkupString"/> rendered HTML for the given slug, or empty if not found.</summary>
    public MarkupString GetHtml(string slug, bool german = false)
    {
        var md = GetMarkdown(slug, german);
        if (md is null) return new MarkupString("");
        var html = MarkdownRenderer.ToHtml(md);
        return new MarkupString(RewriteDocLinks(html));
    }

    private static string RewriteDocLinks(string html) =>
        DocLinkRegex.Replace(html, m =>
        {
            var baseName = m.Groups[1].Value;
            var isDe = m.Groups[2].Success;
            if (!BaseNameToSlug.TryGetValue(baseName, out var targetSlug))
                return m.Value;
            return isDe ? $"href=\"/docs/{targetSlug}?lang=de\"" : $"href=\"/docs/{targetSlug}\"";
        });
}

/// <param name="Slug">URL slug used in <c>/docs/{slug}</c>.</param>
/// <param name="BaseName">Base filename without language suffix and extension.</param>
/// <param name="Title">Human-readable title for the sidebar.</param>
public sealed record DocEntry(string Slug, string BaseName, string Title);
