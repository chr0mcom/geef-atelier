using System.Runtime.CompilerServices;
using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Infrastructure.Finalizers;
using Geef.Atelier.Infrastructure.Grounding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Geef.Atelier.Tests.Domain.Crew;

/// <summary>
/// Reads every exported production snapshot from the directory in <c>ATELIER_SNAPSHOT_FIXTURES</c> (one JSON file
/// per run) through the strict run-time read. The exports contain real orders and never belong in the repository;
/// without the variable or with an empty directory the test is skipped.
/// <para>
/// <c>ATELIER_SNAPSHOT_FIXTURES_MIN</c> is the number of snapshot files the export must contain at least. Set it to the
/// number of runs that were exported, so a partial or truncated export fails the test instead of passing with the files
/// that happen to be there. Without it the minimum is 1, which only proves that the directory is not empty. Example:
/// <c>ATELIER_SNAPSHOT_FIXTURES=&lt;export-dir&gt; ATELIER_SNAPSHOT_FIXTURES_MIN=&lt;number of exported runs&gt; dotnet test …</c>.
/// </para>
/// <para>
/// Against the model reduced in stage 8c (D-089), every grounding type a snapshot names must have a provider registered by
/// <see cref="GroundingServiceExtensions.AddGroundingProviders"/> or be one of <see cref="GroundingProviderTypes.Retired"/>, and
/// every finalizer type must be a defined value with an executor registered by <see cref="FinalizerServiceExtensions.AddFinalizers"/>
/// or a retired one. Retired entries are skipped with a warning when a run uses them; the test reports how many snapshots carry them.
/// </para>
/// </summary>
public sealed class ProductionSnapshotFixtureTests(ITestOutputHelper output)
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
        var registeredGrounding = RegisteredGroundingTypes();
        var registeredFinalizers = RegisteredFinalizerTypes();
        var withRetiredGrounding = 0;
        var withRetiredFinalizer = 0;

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
                    if (!registeredGrounding.Contains(profile.ProviderType) && !profile.ProviderType.IsRetiredGroundingType())
                        problems.Add($"{name}: grounding type '{profile.ProviderType}' is neither registered nor retired");
                }
                if ((snapshot.GroundingProviders ?? []).Any(p => p.ProviderType.IsRetiredGroundingType()))
                    withRetiredGrounding++;

                foreach (var finalizer in snapshot.Finalizers ?? [])
                {
                    if (!Enum.IsDefined(finalizer.FinalizerType))
                        problems.Add($"{name}: finalizer type {(int)finalizer.FinalizerType} is not defined");
                    else if (!registeredFinalizers.Contains(finalizer.FinalizerType) && !finalizer.FinalizerType.IsRetired())
                        problems.Add($"{name}: finalizer type {finalizer.FinalizerType} has no executor and is not retired");
                }
                if ((snapshot.Finalizers ?? []).Any(f => f.FinalizerType.IsRetired()))
                    withRetiredFinalizer++;
            }
            catch (InvalidCrewSnapshotException ex)
            {
                problems.Add($"{name}: {ex.Message}");
            }
        }

        output.WriteLine($"{files.Count} snapshots read; {withRetiredGrounding} name a retired grounding type, {withRetiredFinalizer} a retired finalizer type.");
        Assert.True(problems.Count == 0, $"{problems.Count} of {files.Count} snapshots are not usable:\n{string.Join("\n", problems)}");
    }

    [Fact]
    public void The_registered_types_are_read_from_the_production_registration()
    {
        var grounding = RegisteredGroundingTypes();
        var finalizers = RegisteredFinalizerTypes();

        Assert.Contains(GroundingProviderTypes.Tavily, grounding);
        Assert.Contains(GroundingProviderTypes.UrlFetch, grounding);
        Assert.DoesNotContain(grounding, t => t.IsRetiredGroundingType());
        Assert.Contains(FinalizerType.MetadataEnrich, finalizers);
        Assert.DoesNotContain(finalizers, t => t.IsRetired());
    }

    private static IReadOnlySet<string> RegisteredGroundingTypes()
    {
        var services = new ServiceCollection().AddGroundingProviders(new ConfigurationBuilder().Build());
        return services
            .Where(d => d.ServiceType == typeof(IGroundingProvider) && d.ImplementationType is not null)
            .Select(d => ((IGroundingProvider)RuntimeHelpers.GetUninitializedObject(d.ImplementationType!)).ProviderType)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<FinalizerType> RegisteredFinalizerTypes()
    {
        var services = new ServiceCollection().AddFinalizers(new ConfigurationBuilder().Build());
        return services
            .Where(d => d.ServiceType == typeof(IFinalizerExecutor) && d.ImplementationType is not null)
            .Select(d => ((IFinalizerExecutor)RuntimeHelpers.GetUninitializedObject(d.ImplementationType!)).Type)
            .ToHashSet();
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
