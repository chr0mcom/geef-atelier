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
        new("12-user-guide",            "12-user-guide",            "Guide: writing with Atelier"),
        new("13-ai-assistant-guide",    "13-ai-assistant-guide",    "Guide: from an AI assistant"),
        new("14-admin-guide",           "14-admin-guide",           "Guide: for administrators"),
        new("15-developer-guide",       "15-developer-guide",       "Guide: for developers and hosts"),
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

    /// <summary>Where a link to a file of the repository that <c>/docs</c> does not serve points to.</summary>
    public const string RepositoryFileBase = "https://github.com/chr0mcom/geef-atelier/blob/main/";

    private static readonly Regex DiagramRegex = new(@"^```mermaid[^\n]*\n.*?^```[ \t]*$\n?", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.Multiline);

    private static readonly Regex CommentRegex = new(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex AnchorRegex = new(@"<a href=""(?<href>[^""]*)""(?<rest>[^>]*)>(?<text>.*?)</a>", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly string[] UnpublishedFolders = ["docs/reports/", "docs/prompts/"];

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
        var html = MarkdownRenderer.ToHtml(WithoutDiagramsAndComments(md));
        return new MarkupString(RewriteLinks(html));
    }

    /// <summary>
    /// The Markdown without Mermaid blocks and HTML comments: GitHub draws the diagrams, the page would only show their source, and
    /// the guides tell the same in words; comments are markers for tests.
    /// </summary>
    internal static string WithoutDiagramsAndComments(string markdown) =>
        CommentRegex.Replace(DiagramRegex.Replace(markdown.Replace("\r\n", "\n"), string.Empty), string.Empty);

    /// <summary>
    /// Points every relative link to a place that exists: allow-listed documents to <c>/docs/{slug}</c> (with language and anchor),
    /// other files of the repository to <see cref="RepositoryFileBase"/>, and links into the unpublished working folders become plain text.
    /// </summary>
    internal static string RewriteLinks(string html) =>
        AnchorRegex.Replace(html, m =>
        {
            var href = System.Net.WebUtility.HtmlDecode(m.Groups["href"].Value);
            if (href.Length == 0 || href.StartsWith('#') || href.StartsWith('/') || href.Contains(':', StringComparison.Ordinal))
                return m.Value;

            var hash = href.IndexOf('#', StringComparison.Ordinal);
            var path = hash < 0 ? href : href[..hash];
            var anchor = hash < 0 ? string.Empty : href[hash..];
            var repoPath = RepositoryPath(path);
            if (repoPath is null || UnpublishedFolders.Any(f => repoPath.StartsWith(f, StringComparison.Ordinal)))
                return m.Groups["text"].Value;

            return $"<a href=\"{System.Net.WebUtility.HtmlEncode(Target(repoPath, anchor))}\"{m.Groups["rest"].Value}>{m.Groups["text"].Value}</a>";
        });

    private static string Target(string repoPath, string anchor)
    {
        if (repoPath.StartsWith("docs/", StringComparison.Ordinal) && repoPath.IndexOf('/', 5) < 0
            && repoPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = repoPath[5..^3];
            var german = fileName.EndsWith("_de", StringComparison.OrdinalIgnoreCase);
            var baseName = german ? fileName[..^3] : fileName;
            if (BaseNameToSlug.TryGetValue(baseName, out var slug))
                return $"/docs/{slug}{(german ? "?lang=de" : string.Empty)}{anchor}";
        }
        return RepositoryFileBase + repoPath + anchor;
    }

    private static string? RepositoryPath(string relativeToDocs)
    {
        var parts = new List<string> { "docs" };
        foreach (var part in relativeToDocs.Split('/'))
        {
            if (part is "" or ".")
                continue;
            if (part == "..")
            {
                if (parts.Count == 0)
                    return null;
                parts.RemoveAt(parts.Count - 1);
                continue;
            }
            parts.Add(part);
        }
        return parts.Count == 0 ? null : string.Join('/', parts);
    }
}

/// <param name="Slug">URL slug used in <c>/docs/{slug}</c>.</param>
/// <param name="BaseName">Base filename without language suffix and extension.</param>
/// <param name="Title">Human-readable title for the sidebar.</param>
public sealed record DocEntry(string Slug, string BaseName, string Title);
