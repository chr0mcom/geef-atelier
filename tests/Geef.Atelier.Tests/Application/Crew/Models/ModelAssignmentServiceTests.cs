using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Crew.Models;

public sealed class ModelAssignmentServiceTests
{
    private static readonly ModelCandidate Opus = new("claude-cli", "claude-opus-latest");
    private static readonly ModelCandidate Sonnet = new("claude-cli", "claude-sonnet-latest");
    private static readonly ModelCandidate Fable = new("claude-cli", "claude-fable-latest");
    private static readonly ModelCandidate Astra = new("codex-cli", "gpt-astra-latest");
    private static readonly ModelCandidate Sol = new("codex-cli", "gpt-sol-latest");
    private static readonly ModelCandidate Neon = new("openrouter", "google/gemini-4-neon");

    private static ModelAssignmentService Service(FakeCatalog? catalog = null) =>
        new(catalog ?? new FakeCatalog(), NullLogger<ModelAssignmentService>.Instance);

    [Fact]
    public async Task ExecutorGetsTheFirstCandidate()
    {
        var result = await Service().AssignAsync(ModelSlot.Executor, ModelDomain.Correspondence);

        Assert.Equal(Opus, result.Candidate);
        Assert.False(result.IsFallback);
        Assert.Null(result.Warning);
    }

    [Fact]
    public async Task UnavailablePrimaryFallsThroughToTheNextCandidateWithAWarning()
    {
        var catalog = new FakeCatalog { Lists = { ["claude-cli"] = ["claude-sonnet-latest"] } };

        var result = await Service(catalog).AssignAsync(ModelSlot.Executor, ModelDomain.General);

        Assert.Equal(Sol, result.Candidate);
        Assert.Contains("not available", result.Warning);
    }

    [Fact]
    public async Task ThrowingCatalogCountsAsAvailable()
    {
        var catalog = new FakeCatalog { Throws = true };

        var result = await Service(catalog).AssignAsync(ModelSlot.Executor, ModelDomain.Technical);

        Assert.Equal(Fable, result.Candidate);
    }

    [Fact]
    public async Task ReviewerOfAClaudeExecutorPrefersAnotherFamily()
    {
        var result = await Service().AssignAsync(ModelSlot.StyleReviewer, ModelDomain.General, Opus);

        Assert.Equal(Sol, result.Candidate);
    }

    [Fact]
    public async Task ReviewerOfAGptExecutorPrefersClaude()
    {
        var result = await Service().AssignAsync(ModelSlot.FactReviewer, ModelDomain.General, Astra);

        Assert.Equal(Opus, result.Candidate);
    }

    [Fact]
    public async Task ExecutorModelIsExcludedEvenWhenOnlyTheAliasResolvesToIt()
    {
        var catalog = new FakeCatalog { Aliases = { ["claude-opus-latest"] = "claude-opus-5-5" } };
        var codexDown = new FakeCatalog { Aliases = { ["claude-opus-latest"] = "claude-opus-5-5" }, Lists = { ["codex-cli"] = ["gpt-x"] } };

        var advisor = await Service(catalog).AssignAsync(ModelSlot.DomainExpertAdvisor, ModelDomain.Legal, new ModelCandidate("claude-cli", "claude-opus-5-5"));
        var reviewer = await Service(codexDown).AssignAsync(ModelSlot.StyleReviewer, ModelDomain.General, new ModelCandidate("claude-cli", "claude-opus-5-5"));

        Assert.Equal(Astra, advisor.Candidate);
        Assert.Equal(Sonnet, reviewer.Candidate);
    }

    [Fact]
    public async Task ExecutorExclusionWorksWithoutAliasResolution()
    {
        var catalog = new FakeCatalog { ResolveThrows = true, Lists = { ["codex-cli"] = ["gpt-x"] } };

        var result = await Service(catalog).AssignAsync(ModelSlot.StyleReviewer, ModelDomain.General, new ModelCandidate("claude-cli", "claude-opus-5-5"));

        Assert.Equal(Sonnet, result.Candidate);
    }

    [Fact]
    public async Task NoEligibleCandidateFallsBackToTheFirstWithAWarning()
    {
        var catalog = new FakeCatalog { Lists = { ["claude-cli"] = ["none"], ["codex-cli"] = ["none"] } };

        var result = await Service(catalog).AssignAsync(ModelSlot.Helper, ModelDomain.General);

        Assert.True(result.IsFallback);
        Assert.Equal(new ModelCandidate("claude-cli", "claude-haiku-latest"), result.Candidate);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public async Task SameRoleReviewersRotateThroughTheRing()
    {
        var requests = Enumerable.Range(0, 4).Select(_ => Managed(ModelSlot.DomainReviewer)).ToList();

        var result = await Service().AssignReviewersAsync(requests, ModelDomain.Speech, Opus);

        Assert.Equal([Astra, Neon, Fable, Astra], result.Select(r => r.Candidate));
    }

    [Fact]
    public async Task PaidCandidatesOutsideCoreDomainsNeedThreeSameRoleReviewers()
    {
        var two = await Service().AssignReviewersAsync([Managed(ModelSlot.DomainReviewer), Managed(ModelSlot.DomainReviewer)], ModelDomain.Marketing, Opus);
        var oneInLegal = await Service().AssignReviewersAsync([Managed(ModelSlot.DomainReviewer)], ModelDomain.Legal, Opus);

        Assert.Equal([Astra, Fable], two.Select(r => r.Candidate));
        Assert.Equal(Neon, oneInLegal[0].Candidate);
    }

    [Fact]
    public async Task AtMostTwoReviewersPerCrewArePaid()
    {
        var requests = Enumerable.Range(0, 5).Select(_ => Managed(ModelSlot.DomainReviewer)).ToList();

        var result = await Service().AssignReviewersAsync(requests, ModelDomain.Academic, Opus);

        Assert.Equal([Neon, Astra, Fable, Neon, Sonnet], result.Select(r => r.Candidate));
        Assert.Equal(2, result.Count(r => r.Candidate.IsPaid));
    }

    [Fact]
    public async Task ProviderReportedAsUnusableIsSkipped()
    {
        var catalog = new FakeCatalog { Reported = { ["openrouter"] = [] } };

        var result = await Service(catalog).AssignAsync(ModelSlot.DomainReviewer, ModelDomain.Legal, Opus);

        Assert.Equal(Astra, result.Candidate);
        Assert.Contains("not available", result.Warning);
    }

    [Fact]
    public async Task FallbackRespectsThePaidCap()
    {
        var catalog = new FakeCatalog { Reported = { ["claude-cli"] = [], ["codex-cli"] = [] } };
        var paidManual = new ReviewerAssignmentRequest(null, null, new ModelCandidate("xai", "grok-4.5"));
        var requests = new List<ReviewerAssignmentRequest> { paidManual, paidManual, Managed(ModelSlot.DomainReviewer) };

        var result = await Service(catalog).AssignReviewersAsync(requests, ModelDomain.Legal, Opus);

        Assert.True(result[2].IsFallback);
        Assert.False(result[2].Candidate.IsPaid);
        Assert.Equal(2, result.Count(r => r.Candidate.IsPaid));
    }

    [Fact]
    public async Task FallbackNeverPicksAProviderWithoutApiKey()
    {
        var catalog = new FakeCatalog { Reported = { ["claude-cli"] = [], ["codex-cli"] = [], ["openrouter"] = [] } };

        var result = await Service(catalog).AssignReviewersAsync([Managed(ModelSlot.DomainReviewer)], ModelDomain.Legal, Opus);

        Assert.True(result[0].IsFallback);
        Assert.NotEqual("openrouter", result[0].Candidate.Provider);
        Assert.Contains("no usable candidate", result[0].Warning);
    }

    [Fact]
    public async Task FallbackPrefersAUsableCandidateEvenOnTheExecutorsModel()
    {
        var catalog = new FakeCatalog { Reported = { ["codex-cli"] = [], ["openrouter"] = [] } };

        var result = await Service(catalog).AssignAsync(ModelSlot.CriticalAdvisor, ModelDomain.General, Fable);

        Assert.Equal(new ModelCandidate("claude-cli", "claude-fable-latest"), result.Candidate);
        Assert.Contains("plurality", result.Warning);
    }

    [Fact]
    public async Task ManualReviewerOnTheExecutorsModelIsReported()
    {
        var catalog = new FakeCatalog { Aliases = { ["claude-opus-latest"] = "claude-opus-5" } };
        var manual = new ReviewerAssignmentRequest(null, null, new ModelCandidate("claude-cli", "claude-opus-5"));

        var result = await Service(catalog).AssignReviewersAsync([manual], ModelDomain.General, Opus);

        Assert.Equal(manual.Current, result[0].Candidate);
        Assert.Contains("plurality", result[0].Warning);
    }

    [Fact]
    public async Task ManualPaidReviewersCountTowardsTheCapAndKeepTheirBinding()
    {
        var manual = new ReviewerAssignmentRequest(null, null, new ModelCandidate("xai", "grok-4.5"));
        var requests = new List<ReviewerAssignmentRequest> { manual, manual, Managed(ModelSlot.DomainReviewer) };

        var result = await Service().AssignReviewersAsync(requests, ModelDomain.Legal, Opus);

        Assert.Equal(manual.Current, result[0].Candidate);
        Assert.Equal(manual.Current, result[1].Candidate);
        Assert.Equal(Astra, result[2].Candidate);
    }

    [Fact]
    public async Task ProfileDomainOverridesTheCrewDomain()
    {
        var request = new ReviewerAssignmentRequest(ModelSlot.DomainReviewer, ModelDomain.Legal, Opus);

        var result = await Service().AssignReviewersAsync([request], ModelDomain.Marketing, Opus);

        Assert.Equal(Neon, result[0].Candidate);
    }

    [Fact]
    public async Task ApplyBindsManagedActorsAndLeavesManualOnesUntouched()
    {
        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: "t",
            Executor: new ExecutorProfile("e", "E", "d", "p", "claude-cli", "claude-opus-latest", 32000, false, ModelSlot: ModelSlot.Executor),
            Reviewers:
            [
                new ReviewerProfile("fact", "F", "d", "p", "x", "y", 8000, false, ModelSlot: ModelSlot.FactReviewer),
                new ReviewerProfile("manual", "M", "d", "p", "xai", "grok-4.5", 8000, false),
                new ReviewerProfile("domain", "D", "d", "p", "x", "y", 8000, false, ModelSlot: ModelSlot.DomainReviewer),
            ],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors:
            [
                new AdvisorProfile("expert", "X", "d", "p", "x", "y", 8000, AdvisorMode.DomainExpert, AdvisorTrigger.BeforeFirstExecution, false, ModelSlot: ModelSlot.DomainExpertAdvisor),
                new AdvisorProfile("manual-advisor", "M", "d", "p", "claude-cli", "claude-opus-latest", 8000, AdvisorMode.Strategic, AdvisorTrigger.BeforeFirstExecution, false),
            ],
            GroundingProviders:
            [
                new GroundingProviderProfile("g", "G", "d", "tavily", new Dictionary<string, string>
                {
                    [GroundingProviderProfile.KeyRefinementProvider] = "deepseek",
                    [GroundingProviderProfile.KeyRefinementModel] = "deepseek/deepseek-v4-flash",
                    [GroundingProviderProfile.KeyModelSlot] = "GroundingRefiner",
                }, 1, false),
            ],
            Finalizers:
            [
                new FinalizerProfile("t-empty", "T", "d", FinalizerType.Transform, [], false),
                new FinalizerProfile("t-manual", "T", "d", FinalizerType.Transform, new Dictionary<string, string>
                {
                    [TransformSettings.KeyProvider] = "codex-cli",
                    [TransformSettings.KeyModel] = "gpt-6.1-terra",
                }, false),
                new FinalizerProfile("t-rewrite", "T", "d", FinalizerType.Transform, new Dictionary<string, string>
                {
                    [TransformSettings.KeyModelSlot] = "TransformRewrite",
                }, false),
            ],
            CrewDomain: ModelDomain.Legal);

        var result = await Service().ApplyAsync(snapshot);

        Assert.Equal(("claude-cli", "claude-opus-latest"), (result.Executor.Provider, result.Executor.Model));
        Assert.Equal(("codex-cli", "gpt-astra-latest"), (result.Reviewers[0].Provider, result.Reviewers[0].Model));
        Assert.Equal(("xai", "grok-4.5"), (result.Reviewers[1].Provider, result.Reviewers[1].Model));
        Assert.Equal(("openrouter", "google/gemini-4-neon"), (result.Reviewers[2].Provider, result.Reviewers[2].Model));
        Assert.Equal(("codex-cli", "gpt-astra-latest"), (result.Advisors[0].Provider, result.Advisors[0].Model));
        Assert.Equal(("claude-cli", "claude-opus-latest"), (result.Advisors[1].Provider, result.Advisors[1].Model));
        Assert.Equal("claude-sonnet-latest", result.GroundingProviders![0].ProviderSettings[GroundingProviderProfile.KeyRefinementModel]);
        Assert.Equal("deepseek/deepseek-v4-flash", snapshot.GroundingProviders![0].ProviderSettings[GroundingProviderProfile.KeyRefinementModel]);
        Assert.Equal("claude-sonnet-latest", result.Finalizers![0].Settings[TransformSettings.KeyModel]);
        Assert.Equal("gpt-6.1-terra", result.Finalizers[1].Settings[TransformSettings.KeyModel]);
        Assert.Equal("gpt-sol-latest", result.Finalizers[2].Settings[TransformSettings.KeyModel]);
    }

    [Fact]
    public async Task ApplyWithoutCrewDomainUsesGeneral()
    {
        var snapshot = new CrewSnapshot(
            CrewSnapshot.CurrentSchemaVersion, null,
            new ExecutorProfile("e", "E", "d", "p", "claude-cli", "claude-opus-latest", 32000, false, ModelSlot: ModelSlot.Executor),
            [new ReviewerProfile("domain", "D", "d", "p", "x", "y", 8000, false, ModelSlot: ModelSlot.DomainReviewer)],
            EvaluationStrategy.Parallel, null, []);

        var result = await Service().ApplyAsync(snapshot);

        Assert.Equal("gpt-astra-latest", result.Reviewers[0].Model);
    }

    private static ReviewerAssignmentRequest Managed(ModelSlot slot) => new(slot, null, new ModelCandidate("", ""));

    private sealed class FakeCatalog : IModelCatalog
    {
        public Dictionary<string, IReadOnlyList<string>> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Aliases { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IReadOnlyList<string>> Reported { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool Throws { get; init; }

        public async Task<IReadOnlyList<string>?> ListReportedModelIdsAsync(string providerName, CancellationToken ct = default)
        {
            if (Reported.TryGetValue(providerName, out var reported))
                return reported;
            return (await ListModelsAsync(providerName, ct)).Select(m => m.Id).ToList();
        }
        public bool ResolveThrows { get; init; }

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default)
        {
            if (Throws)
                throw new HttpRequestException("down");
            if (!Lists.TryGetValue(providerName, out var ids))
                ids = ModelMatrix.All.Where(c => c.Provider == providerName).Select(c => c.Model).ToList();
            return Task.FromResult<IReadOnlyList<ModelInfo>>(ids.Select(id => new ModelInfo(id, id, "", true)).ToList());
        }

        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default) => ListModelsAsync(providerName, ct);
        public Task<IReadOnlyList<ModelInfo>> WarmUpAsync(string providerName, CancellationToken ct = default) => ListModelsAsync(providerName, ct);
        public ModelCatalogSource GetSource(string providerName) => ModelCatalogSource.Live;
        public bool IsUsingFallback(string providerName) => false;

        public Task<string> ResolveModelAsync(string providerName, string modelId, CancellationToken ct = default)
        {
            if (ResolveThrows)
                throw new HttpRequestException("down");
            return Task.FromResult(Aliases.TryGetValue(modelId, out var concrete) ? concrete : modelId);
        }
    }
}
