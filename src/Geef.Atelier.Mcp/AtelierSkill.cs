using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Extensions.Skills;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp;

/// <summary>
/// The skill about using Atelier (extension <c>io.modelcontextprotocol/skills</c>), offered while <c>Mcp:Skills</c> is <c>On</c>. Its files are
/// fixed: they are embedded in this assembly from <c>Skills/using-atelier</c> and never built from orders, files, users or anything else
/// known only at run time. The server instructions stay complete for clients without skills; the skill only repeats them in more detail.
/// </summary>
public static class AtelierSkill
{
    /// <summary>Name of the skill, from the frontmatter of its <c>SKILL.md</c>.</summary>
    public const string Name = "using-atelier";

    /// <summary>Address of the <c>SKILL.md</c> of the skill.</summary>
    public const string Uri = "skill://" + Name + "/SKILL.md";

    private const string ResourcePrefix = "Geef.Atelier.Mcp.Skills/" + Name + "/";

    private static readonly Lazy<IReadOnlyList<McpServerSkillFile>> LazyFiles = new(ReadFiles);

    /// <summary>The files of the skill, by their path inside the skill (<c>SKILL.md</c>, <c>references/…</c>), in ordinal order of the path.</summary>
    public static IReadOnlyList<McpServerSkillFile> Files => LazyFiles.Value;

    /// <summary>The text of one file of the skill by its path inside the skill; <c>null</c> for a path the skill does not have.</summary>
    public static string? Text(string path) =>
        Files.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.Ordinal)) is { } file
            ? System.Text.Encoding.UTF8.GetString(file.Content.Span)
            : null;

    /// <summary>A new package of the skill with its manifest of digests and sizes; the SDK reads the frontmatter from <c>SKILL.md</c>.</summary>
    public static McpServerSkillPackage CreatePackage() => McpServerSkillPackage.Create(Files);

    /// <summary>
    /// Adds the skill to <paramref name="options"/>: the skills extension and the <c>resources</c> capability, the handlers of <c>skills/list</c> and
    /// <c>skills/get</c> exactly as the SDK builds them, and the files of the skill as resources. Called only while <c>Mcp:Skills</c> is <c>On</c>;
    /// without the call the options stay as they were.
    /// </summary>
    public static void AddTo(McpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var package = CreatePackage();
        var services = new ServiceCollection();
        services.AddMcpServer().WithSkillPackages([package]);
        using var provider = services.BuildServiceProvider();
        var skillsAssembly = typeof(McpSkillsBuilderExtensions).Assembly;
        var configurers = services
            .Where(d => d.ServiceType == typeof(IConfigureOptions<McpServerOptions>) && d.ImplementationType?.Assembly == skillsAssembly)
            .Select(d => (IConfigureOptions<McpServerOptions>)ActivatorUtilities.CreateInstance(provider, d.ImplementationType!))
            .ToList();
        if (configurers.Count != 1)
            throw new InvalidOperationException($"The skills extension of the MCP SDK registered {configurers.Count} option setups instead of one.");

        configurers[0].Configure(options);
        options.ResourceCollection ??= new McpServerResourceCollection();
        foreach (var resource in package.Resources)
            options.ResourceCollection.Add(resource);
    }

    private static List<McpServerSkillFile> ReadFiles()
    {
        var assembly = typeof(AtelierSkill).Assembly;
        var files = assembly.GetManifestResourceNames()
            .Select(n => (Resource: n, Path: n.Replace('\\', '/')))
            .Where(n => n.Path.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(n => McpServerSkillFile.FromText(n.Path[ResourcePrefix.Length..], Read(assembly, n.Resource), "text/markdown"))
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();
        if (files.All(f => f.Path != "SKILL.md"))
            throw new InvalidOperationException("The skill " + Name + " has no embedded SKILL.md.");
        return files;
    }

    private static string Read(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException("The embedded file " + resource + " is missing.");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
