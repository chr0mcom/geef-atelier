using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Domain.Crew;

/// <summary>
/// Guards the "no frozen Claude CLI model id in production code" rule. A dynamic catalog is
/// pointless if a constant somewhere still pins the composer, a system crew or a prompt to the
/// generation that happened to be current when it was written.
/// </summary>
public sealed class ModelAliasConstantsTests
{
    private const string OpusAlias = "claude-opus-latest";

    [Fact]
    public void MatrixExecutorAndComposerUseTheAlias()
    {
        Assert.Equal(new ModelCandidate("claude-cli", OpusAlias), ModelMatrix.Primary(ModelSlot.Executor));
        Assert.Equal(new ModelCandidate("claude-cli", OpusAlias), ModelMatrix.Primary(ModelSlot.Composer));
    }

    [Fact]
    public void MatrixClaudeCandidatesAreAllAliasesInTheClaudeCliCatalog()
    {
        foreach (var candidate in ModelMatrix.All.Where(c => c.Provider == "claude-cli"))
        {
            Assert.EndsWith("-latest", candidate.Model);
            Assert.Contains(StaticModelFallback.ForClaudeCli, m => m.Id == candidate.Model);
        }
    }

    [Fact]
    public void MatrixCliCandidatesAreInTheirProviderCatalogs()
    {
        foreach (var candidate in ModelMatrix.All.Where(c => c.Provider.EndsWith("-cli")))
        {
            Assert.Contains(StaticModelFallback.For(candidate.Provider), m => m.Id == candidate.Model);
        }
    }

    [Fact]
    public void SystemCliProviderOffersTheAliases()
    {
        var settings = CliProviderSettings.FromSettings(SystemProviders.ClaudeCli.Settings);

        Assert.Contains(OpusAlias, settings.Models);
        Assert.Contains("claude-sonnet-latest", settings.Models);
        Assert.Contains("claude-haiku-latest", settings.Models);
    }

    [Fact]
    public void TheAliasSurvivesModelNameNormalisation()
    {
        Assert.Equal(OpusAlias, ModelNameNormalizer.Normalize("claude-cli", OpusAlias));
        Assert.Equal(OpusAlias, ModelNameNormalizer.Normalize("claude-cli", "anthropic/" + OpusAlias));
    }

    [Fact]
    public void NoSystemCrewProfileIsPinnedToAFrozenClaudeGeneration()
    {
        var pinned = SystemCrew.CodeOnlyExecutorProfiles.Values.Select(p => p.Model)
            .Concat(SystemCrew.CodeOnlyReviewerProfiles.Values.Select(p => p.Model))
            .ToList();

        Assert.All(pinned, model =>
        {
            if (model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
                Assert.EndsWith("-latest", model);
        });
    }

    [Fact]
    public void ProductionCodeCarriesNoFrozenClaudeCliModelId()
    {
        var frozen = new Regex(@"claude-(opus|sonnet|haiku)-\d+([-.]\d+)?", RegexOptions.IgnoreCase);
        var srcRoot = FindSourceRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(srcRoot, "*.razor", SearchOption.AllDirectories)))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!frozen.IsMatch(line))
                    continue;

                if (line.Contains("anthropic/", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (file.EndsWith("StaticModelFallback.cs", StringComparison.Ordinal))
                    continue;
                if (file.EndsWith("SystemProviders.cs", StringComparison.Ordinal))
                    continue;
                if (file.EndsWith("ModelNameNormalizer.cs", StringComparison.Ordinal))
                    continue;

                offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Frozen Claude CLI model ids left in production code:\n" + string.Join("\n", offenders));
    }

    private static string FindSourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Geef.Atelier.Core")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src");
    }
}
