using System.Text.RegularExpressions;

namespace Geef.Atelier.Tests.DocsFeature;

/// <summary>
/// A reader on GitHub can follow every relative link of the published documents: the target exists and does not lie in a folder or
/// file that stays out of the public repository, even when it is present in the local working copy.
/// </summary>
public sealed class PublishedLinkTests
{
    private static readonly string[] DocumentFolders = ["docs", "cli-proxy", "src", "tools"];

    private static readonly string[] SkippedFolders = ["bin", "obj", "node_modules", ".venv", ".pytest_cache"];

    private static readonly string[] Unpublished = ["docs/reports", "docs/prompts", "docs/superpowers"];

    private static readonly Regex FenceRegex = new(@"^[ \t]*(```|~~~).*?^[ \t]*\1[^\n]*$", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.Multiline);

    private static readonly Regex InlineCodeRegex = new(@"(`+)(?:(?!\1)[^\n])+\1", RegexOptions.Compiled);

    private static readonly Regex LinkRegex = new(@"\]\(\s*<?(?<target>[^)\s>]+)>?(?:\s+""[^""]*"")?\s*\)|(?:href|src|srcset)=""(?<target>[^""]+)""", RegexOptions.Compiled);

    [Fact]
    public void Every_relative_link_of_the_published_documents_leads_to_a_published_file()
    {
        var root = RepositoryRoot();
        var broken = new List<string>();
        var checkedLinks = 0;
        foreach (var file in Documents(root))
        {
            var text = InlineCodeRegex.Replace(FenceRegex.Replace(File.ReadAllText(file).Replace("\r\n", "\n"), string.Empty), "code");
            foreach (Match link in LinkRegex.Matches(text))
            {
                var href = link.Groups["target"].Value;
                var path = Uri.UnescapeDataString(href.Split('#', '?')[0]);
                if (path.Length == 0 || href.Contains(':', StringComparison.Ordinal))
                    continue;
                checkedLinks++;
                var full = Path.GetFullPath(path.StartsWith('/') ? Path.Combine(root, path.TrimStart('/')) : Path.Combine(Path.GetDirectoryName(file)!, path));
                var target = Path.GetRelativePath(root, full).Replace('\\', '/');
                var unpublished = Unpublished.Any(f => target == f || target.StartsWith(f + "/", StringComparison.Ordinal))
                    || Path.GetFileName(target) == "CLAUDE.md" || target.StartsWith("..", StringComparison.Ordinal);
                if (unpublished || !(File.Exists(full) || Directory.Exists(full)))
                    broken.Add($"{Path.GetRelativePath(root, file)} → {href}");
            }
        }

        Assert.True(checkedLinks > 100, $"Only {checkedLinks} links were found; the test no longer reads the documents.");
        Assert.True(broken.Count == 0, $"{broken.Count} links a reader cannot follow:\n" + string.Join('\n', broken));
    }

    private static IEnumerable<string> Documents(string root) =>
        Directory.GetFiles(root, "README*.md")
            .Concat(DocumentFolders.SelectMany(folder => Directory.GetFiles(Path.Combine(root, folder), "*.md", SearchOption.AllDirectories)))
            .Where(file => IsPublished(Path.GetRelativePath(root, file).Replace('\\', '/')));

    private static bool IsPublished(string relative) =>
        !Unpublished.Any(f => relative.StartsWith(f + "/", StringComparison.Ordinal))
        && !relative.Split('/').Any(SkippedFolders.Contains)
        && Path.GetFileName(relative) != "CLAUDE.md";

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
