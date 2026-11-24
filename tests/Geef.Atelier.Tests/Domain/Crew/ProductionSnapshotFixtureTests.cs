using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Pipeline;

namespace Geef.Atelier.Tests.Domain.Crew;

/// <summary>
/// Reads every exported production snapshot from the directory in <c>ATELIER_SNAPSHOT_FIXTURES</c> (one JSON file
/// per run) through the strict run-time read. The exports contain real orders and never belong in the repository;
/// without the variable or with an empty directory the test is skipped.
/// </summary>
public sealed class ProductionSnapshotFixtureTests
{
    [SnapshotFixturesFact]
    public void EveryProductionSnapshotIsUsableForARun()
    {
        var files = SnapshotFixturesFactAttribute.Files();
        var minimum = SnapshotFixturesFactAttribute.MinimumCount();
        Assert.True(files.Count >= minimum,
            $"{SnapshotFixturesFactAttribute.Variable} holds {files.Count} snapshots, expected at least {minimum} " +
            $"({SnapshotFixturesFactAttribute.MinimumVariable}); the export looks incomplete.");
        var problems = new List<string>();

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            try
            {
                var snapshot = CrewSnapshot.DeserializeForRun(File.ReadAllText(file));
                if (snapshot.SchemaVersion != CrewSnapshot.CurrentSchemaVersion)
                    problems.Add($"{name}: schema version {snapshot.SchemaVersion}");
                if (snapshot.Reviewers.Count == 0)
                    problems.Add($"{name}: no reviewers (would now fail instead of auto-approving)");
                foreach (var profile in snapshot.GroundingProviders ?? [])
                {
                    if (profile.IsSystem && GroundingPathSelection.ProviderTypePathSystemProfiles.Contains(profile.Name) && profile.UsesToolPath())
                        problems.Add($"{name}: system profile {profile.Name} would still take the tool path");
                }
            }
            catch (InvalidCrewSnapshotException ex)
            {
                problems.Add($"{name}: {ex.Message}");
            }
        }

        Assert.True(problems.Count == 0, $"{problems.Count} of {files.Count} snapshots are not usable:\n{string.Join("\n", problems)}");
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
