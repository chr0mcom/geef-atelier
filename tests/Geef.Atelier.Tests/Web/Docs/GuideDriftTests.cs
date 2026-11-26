using System.Text.RegularExpressions;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Mcp;
using Geef.Atelier.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Tests.DocsFeature;

/// <summary>
/// The guides cannot silently drift from the system: the tool table of the AI-assistant guide (English and German) names exactly the
/// tools the server offers with the upload ticket on and the older tools off, and every guide exists in both languages, carries its
/// date line and is listed for <c>/docs</c>.
/// </summary>
public sealed class GuideDriftTests
{
    private static readonly string[] Guides = ["12-user-guide", "13-ai-assistant-guide", "14-admin-guide", "15-developer-guide"];

    [Theory]
    [InlineData("13-ai-assistant-guide.md")]
    [InlineData("13-ai-assistant-guide_de.md")]
    public void The_tool_table_names_exactly_the_tools_of_the_server(string file)
    {
        var documented = ToolTable(File.ReadAllText(Path.Combine(RepoRoot(), "docs", file)));

        Assert.Equal(OfferedTools().Order(StringComparer.Ordinal), documented.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_tool_table_is_found_and_not_empty()
    {
        var documented = ToolTable(File.ReadAllText(Path.Combine(RepoRoot(), "docs", "13-ai-assistant-guide.md")));

        Assert.Equal(10, documented.Count);
        Assert.Contains("prepare_attachment", documented);
    }

    [Fact]
    public void Every_guide_exists_in_both_languages_with_a_date_and_is_listed_for_docs()
    {
        var docs = Path.Combine(RepoRoot(), "docs");
        var service = new DocsService();
        foreach (var guide in Guides)
        {
            Assert.Contains("*Last updated: ", File.ReadAllText(Path.Combine(docs, guide + ".md")), StringComparison.Ordinal);
            Assert.Contains("*Letzte Aktualisierung: ", File.ReadAllText(Path.Combine(docs, guide + "_de.md")), StringComparison.Ordinal);
            Assert.True(DocsService.IsAllowed(guide), guide);
            Assert.False(string.IsNullOrWhiteSpace(service.GetMarkdown(guide)));
            Assert.Contains("Letzte Aktualisierung", service.GetMarkdown(guide, german: true), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_german_guides_use_real_umlauts()
    {
        var docs = Path.Combine(RepoRoot(), "docs");
        var substitutes = new Regex(@"\b(fuer|ueber|koennen|moeglich|Aenderung|pruefen|geprueft|Pruefung|muessen|waehlen|Schluessel)\b");
        foreach (var guide in Guides)
            Assert.DoesNotMatch(substitutes, File.ReadAllText(Path.Combine(docs, guide + "_de.md")));
    }

    private static List<string> ToolTable(string markdown)
    {
        var match = Regex.Match(markdown, @"<!-- tools:start -->(?<table>.*?)<!-- tools:end -->", RegexOptions.Singleline);
        Assert.True(match.Success, "The tool table markers are missing.");
        return Regex.Matches(match.Groups["table"].Value, @"^\|\s*`(?<name>[a-z_]+)`\s*\|", RegexOptions.Multiline)
            .Select(m => m.Groups["name"].Value)
            .ToList();
    }

    private static IReadOnlyList<string> OfferedTools()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:LegacyTools"] = "false" }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<UploadOptions>(o => o.Tickets = UploadTicketsMode.On);
        foreach (var type in typeof(McpAssemblyMarker).Assembly.ToolMethods().SelectMany(m => m.GetParameters()).Select(p => p.ParameterType)
                     .Where(t => t.IsInterface && !t.IsClientValue() && !t.IsSdkValue() && !t.IsGenericType).Distinct())
            services.AddScoped(type, _ => throw new NotSupportedException());
        services.AddAtelierMcp();
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ToolCollection!.Select(t => t.ProtocolTool.Name).ToList();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
