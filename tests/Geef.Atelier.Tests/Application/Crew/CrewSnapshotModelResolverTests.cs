using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Tests.Application.Crew;

/// <summary>
/// The profile keeps the always-latest alias so new runs follow new releases; the snapshot must not,
/// or a resumed run silently executes on a different model than its parent while claiming otherwise.
/// </summary>
public sealed class CrewSnapshotModelResolverTests
{
    private static CrewSnapshot BuildSnapshot(string executorModel, params string[] reviewerModels) =>
        new(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: "t",
            Executor: new ExecutorProfile("exec", "Exec", "d", "p", "claude-cli", executorModel, 1024, false),
            Reviewers: reviewerModels
                .Select((m, i) => new ReviewerProfile($"rev{i}", $"Rev{i}", "d", "p", "claude-cli", m, 1024, false))
                .ToList(),
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: [new AdvisorProfile("adv", "Adv", "d", "p", "claude-cli", "claude-sonnet-latest", 512,
                AdvisorMode.Strategic, AdvisorTrigger.BeforeFirstExecution, false)]);

    [Fact]
    public async Task ResolvesAliasesToConcreteIdsInTheSnapshot()
    {
        var catalog = new MappingModelCatalog(new Dictionary<string, string>
        {
            ["claude-opus-latest"] = "claude-opus-5",
            ["claude-sonnet-latest"] = "claude-sonnet-5",
        });
        var snapshot = BuildSnapshot("claude-opus-latest", "claude-sonnet-latest");

        var resolved = await CrewSnapshotModelResolver.ResolveAsync(snapshot, catalog);

        Assert.Equal("claude-opus-5", resolved.Executor.Model);
        Assert.Equal("claude-sonnet-5", resolved.Reviewers[0].Model);
        Assert.Equal("claude-sonnet-5", resolved.Advisors[0].Model);
    }

    [Fact]
    public async Task LeavesConcreteIdsUntouched()
    {
        var catalog = new MappingModelCatalog(new Dictionary<string, string>
        {
            ["claude-opus-latest"] = "claude-opus-5",
        });
        var snapshot = BuildSnapshot("claude-opus-4-8", "gpt-5.6-sol");

        var resolved = await CrewSnapshotModelResolver.ResolveAsync(snapshot, catalog);

        Assert.Equal("claude-opus-4-8", resolved.Executor.Model);
        Assert.Equal("gpt-5.6-sol", resolved.Reviewers[0].Model);
    }

    [Fact]
    public async Task KeepsTheAlias_WhenTheCatalogThrows()
    {
        var snapshot = BuildSnapshot("claude-opus-latest", "claude-sonnet-latest");

        var resolved = await CrewSnapshotModelResolver.ResolveAsync(snapshot, new ThrowingModelCatalog());

        Assert.Equal("claude-opus-latest", resolved.Executor.Model);
    }

    [Fact]
    public async Task ResolvesTheRefinerModelAGroundingProviderKeepsInItsSettings()
    {
        var catalog = new MappingModelCatalog(new Dictionary<string, string>
        {
            ["claude-opus-latest"] = "claude-opus-5",
        });
        var snapshot = BuildSnapshot("claude-opus-4-8") with
        {
            GroundingProviders =
            [
                new GroundingProviderProfile("g", "G", "d", "tavily", new Dictionary<string, string>
                {
                    [GroundingProviderProfile.KeyRefinementProvider] = "claude-cli",
                    [GroundingProviderProfile.KeyRefinementModel] = "claude-opus-latest",
                }, null, false, null),
            ],
        };

        var resolved = await CrewSnapshotModelResolver.ResolveAsync(snapshot, catalog);

        Assert.Equal("claude-opus-5",
            resolved.GroundingProviders![0].ProviderSettings[GroundingProviderProfile.KeyRefinementModel]);
    }

    [Fact]
    public async Task ResolvesTheModelATransformFinalizerKeepsInItsSettings()
    {
        var catalog = new MappingModelCatalog(new Dictionary<string, string>
        {
            ["claude-opus-latest"] = "claude-opus-5",
        });
        var snapshot = BuildSnapshot("claude-opus-4-8") with
        {
            Finalizers =
            [
                new FinalizerProfile("f", "F", "d", FinalizerType.Transform, new Dictionary<string, string>
                {
                    [TransformSettings.KeyProvider] = "claude-cli",
                    [TransformSettings.KeyModel] = "claude-opus-latest",
                }, false),
            ],
        };

        var resolved = await CrewSnapshotModelResolver.ResolveAsync(snapshot, catalog);

        Assert.Equal("claude-opus-5", resolved.Finalizers![0].Settings[TransformSettings.KeyModel]);
    }

    [Fact]
    public async Task LeavesSettingsWithoutAModelBindingUntouched()
    {
        var catalog = new MappingModelCatalog(new Dictionary<string, string>());
        var snapshot = BuildSnapshot("claude-opus-4-8") with
        {
            Finalizers =
            [
                new FinalizerProfile("f", "F", "d", FinalizerType.FileExport,
                    new Dictionary<string, string> { ["Path"] = "/tmp/x" }, false),
            ],
        };

        var resolved = await CrewSnapshotModelResolver.ResolveAsync(snapshot, catalog);

        Assert.Equal("/tmp/x", resolved.Finalizers![0].Settings["Path"]);
    }

    [Fact]
    public async Task PreservesEverythingElseAboutTheSnapshot()
    {
        var catalog = new MappingModelCatalog(new Dictionary<string, string>
        {
            ["claude-opus-latest"] = "claude-opus-5",
        });
        var snapshot = BuildSnapshot("claude-opus-latest", "claude-sonnet-latest", "claude-haiku-latest");

        var resolved = await CrewSnapshotModelResolver.ResolveAsync(snapshot, catalog);

        Assert.Equal(snapshot.SchemaVersion, resolved.SchemaVersion);
        Assert.Equal(snapshot.TemplateName, resolved.TemplateName);
        Assert.Equal(snapshot.Executor.Name, resolved.Executor.Name);
        Assert.Equal(snapshot.Reviewers.Count, resolved.Reviewers.Count);
        Assert.Equal(snapshot.EvaluationStrategy, resolved.EvaluationStrategy);
    }

    private sealed class MappingModelCatalog(IReadOnlyDictionary<string, string> map) : IModelCatalog
    {
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string p, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string p, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public Task<IReadOnlyList<ModelInfo>> WarmUpAsync(string p, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public ModelCatalogSource GetSource(string p) => ModelCatalogSource.Live;
        public bool IsUsingFallback(string p) => false;
        public Task<string> ResolveModelAsync(string p, string modelId, CancellationToken ct = default)
            => Task.FromResult(map.TryGetValue(modelId, out var resolved) ? resolved : modelId);
    }

    private sealed class ThrowingModelCatalog : IModelCatalog
    {
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string p, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string p, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public Task<IReadOnlyList<ModelInfo>> WarmUpAsync(string p, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public ModelCatalogSource GetSource(string p) => ModelCatalogSource.Unknown;
        public bool IsUsingFallback(string p) => false;
        public Task<string> ResolveModelAsync(string p, string modelId, CancellationToken ct = default)
            => throw new InvalidOperationException("gateway unreachable");
    }
}
