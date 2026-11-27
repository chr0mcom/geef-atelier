using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Pipeline.Assembly;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.Domain.Crew;

/// <summary>
/// Reads every exported production snapshot from the directory in <c>ATELIER_SNAPSHOT_FIXTURES</c> (one JSON file
/// per run) through the run-time read. The exports contain real orders and never belong in the repository;
/// without the variable or with an empty directory the test is skipped.
/// <para>
/// <c>ATELIER_SNAPSHOT_FIXTURES_MIN</c> is the number of snapshot files the export must contain at least. Set it to the
/// number of runs that were exported, so a partial or truncated export fails the test instead of passing with the files
/// that happen to be there. Without it the minimum is 1, which only proves that the directory is not empty. Example:
/// <c>ATELIER_SNAPSHOT_FIXTURES=&lt;export-dir&gt; ATELIER_SNAPSHOT_FIXTURES_MIN=&lt;number of exported runs&gt; dotnet test …</c>.
/// </para>
/// <para>
/// Since stage 8d (D-091) only the direct path runs. Every snapshot must still be readable, so old runs keep their pages. A snapshot
/// of the direct path must be accepted by <see cref="DirectPipelineAssembler"/> and name only checks this version has; every other
/// snapshot must be refused with the plain sentence <see cref="PipelineAssemblerSelector.RetiredSetupMessage"/> instead of running
/// as something else. The test reports how many snapshots fall into each group.
/// </para>
/// </summary>
public sealed class ProductionSnapshotFixtureTests(ITestOutputHelper output)
{
    [SnapshotFixturesFact]
    public void EveryProductionSnapshotIsReadAndEitherRunsOnTheDirectPathOrIsRefusedPlainly()
    {
        var files = SnapshotFixturesFactAttribute.Files();
        var minimum = SnapshotFixturesFactAttribute.MinimumCount();
        Assert.True(files.Count >= minimum,
            $"{SnapshotFixturesFactAttribute.Variable} holds {files.Count} snapshots, expected at least {minimum} " +
            $"({SnapshotFixturesFactAttribute.MinimumVariable}); the export looks incomplete.");
        var selector = new ServiceCollection().AddOptions().AddPipelineAssemblers().BuildServiceProvider()
            .GetRequiredService<IPipelineAssemblerSelector>();
        var run = new RunEntity { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Pending, BriefingText = "b", ConfigJson = "{}" };
        var problems = new List<string>();
        var direct = 0;
        var refused = 0;

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            CrewSnapshot snapshot;
            try
            {
                snapshot = CrewSnapshot.DeserializeForRun(File.ReadAllText(file));
            }
            catch (InvalidCrewSnapshotException ex)
            {
                problems.Add($"{name}: not readable: {ex.Message}");
                continue;
            }

            if (SystemCrew.IsDirectPathTemplate(snapshot.TemplateName))
            {
                direct++;
                if (selector.Select(run, snapshot) is not DirectPipelineAssembler)
                    problems.Add($"{name}: a direct snapshot is not built by the direct assembler");
                foreach (var reviewer in snapshot.Reviewers.Where(r => !SystemCrew.CodeOnlyReviewerProfiles.ContainsKey(r.Name)))
                    problems.Add($"{name}: names the check '{reviewer.Name}', which this version does not have");
                continue;
            }

            refused++;
            var error = Record.Exception(() => selector.Select(run, snapshot));
            if (error is not InvalidCrewSnapshotException { Message: PipelineAssemblerSelector.RetiredSetupMessage })
                problems.Add($"{name}: an old snapshot (template '{snapshot.TemplateName}') is not refused with the plain sentence");
        }

        output.WriteLine($"{files.Count} snapshots read; {direct} run on the direct path, {refused} are refused as a removed way of working.");
        Assert.True(problems.Count == 0, $"{problems.Count} of {files.Count} snapshots are handled wrongly:\n{string.Join("\n", problems)}");
    }
}

/// <summary>
/// Skips the test unless <c>ATELIER_SNAPSHOT_FIXTURES</c> points to a directory with JSON files. The optional
/// <c>ATELIER_SNAPSHOT_FIXTURES_MIN</c> sets how many files the export must contain at least (default 1), so a partial
/// export fails instead of passing quietly.
/// </summary>
public sealed class SnapshotFixturesFactAttribute : FactAttribute
{
    public const string Variable = "ATELIER_SNAPSHOT_FIXTURES";
    public const string MinimumVariable = "ATELIER_SNAPSHOT_FIXTURES_MIN";

    /// <summary>Minimum number of snapshot files from <see cref="MinimumVariable"/>; 1 when unset or not a positive number.</summary>
    public static int MinimumCount() =>
        int.TryParse(Environment.GetEnvironmentVariable(MinimumVariable), out var minimum) && minimum > 0 ? minimum : 1;

    public SnapshotFixturesFactAttribute()
    {
        if (Files().Count == 0)
            Skip = $"{Variable} is not set or contains no *.json files; production snapshots are not part of the repository.";
    }

    public static IReadOnlyList<string> Files()
    {
        var directory = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return [];
        return Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal).ToList();
    }
}
