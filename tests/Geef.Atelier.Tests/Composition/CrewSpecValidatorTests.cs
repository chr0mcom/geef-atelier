using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Composition;

namespace Geef.Atelier.Tests.Composition;

/// <summary>
/// Unit tests for <see cref="CrewSpecValidator"/>: structural validation of Crew-Spec JSON.
/// </summary>
public sealed class CrewSpecValidatorTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static CrewSpecValidator MakeValidator(
        ICrewService? crewService = null,
        IModelCatalog? modelCatalog = null,
        IGroundingProviderFactory? groundingFactory = null)
    {
        crewService ??= new EmptyCrewService();
        modelCatalog ??= new EmptyModelCatalog();
        groundingFactory ??= new StubGroundingFactory("tavily", "academic-search", "vector-store");
        return new CrewSpecValidator(crewService, modelCatalog, groundingFactory);
    }

    /// <summary>A valid existing-template spec JSON.</summary>
    private static string ExistingTemplateSpec(string templateName) =>
        $$"""{"mode":"existing-template","existing_template_name":"{{templateName}}"}""";

    /// <summary>A minimal valid composed spec JSON (reuse references; no inline fields to validate).</summary>
    private static string ComposedSpecWith(string? reviewerReuse = null, string? finalizerReuse = null) =>
        $$"""
        {
            "mode": "composed",
            "executor": {"reuse": "default-executor"},
            "reviewers": [{"reuse": "{{reviewerReuse ?? "briefing-fidelity"}}"}],
            "finalizers": [{"reuse": "{{finalizerReuse ?? "file-export"}}"}]
        }
        """;

    /// <summary>A composed spec with one inline grounding provider of the given provider_type.</summary>
    private static string ComposedSpecWithInlineGrounding(string providerType) =>
        $$"""
        {
            "mode": "composed",
            "executor": {"reuse": "default-executor"},
            "reviewers": [{"reuse": "briefing-fidelity"}],
            "finalizers": [{"reuse": "file-export"}],
            "grounding_providers": [{"name": "lit-search", "provider_type": "{{providerType}}"}]
        }
        """;

    // ── Tests: inline grounding provider_type validation ──────────────────

    [Fact]
    public async Task ValidateAsync_FlagsCritical_ForUnregisteredGroundingProviderType()
    {
        var validator = MakeValidator(
            groundingFactory: new StubGroundingFactory("tavily", "academic-search"));

        var issues = await validator.ValidateAsync(ComposedSpecWithInlineGrounding("web"));

        var typeIssue = Assert.Single(issues, i => i.Field == "grounding_providers[0].provider_type");
        Assert.True(typeIssue.IsCritical);
        Assert.Contains("not registered", typeIssue.Message);
    }

    [Fact]
    public async Task ValidateAsync_NoGroundingTypeIssue_ForRegisteredType()
    {
        var validator = MakeValidator(
            groundingFactory: new StubGroundingFactory("tavily", "academic-search"));

        var issues = await validator.ValidateAsync(ComposedSpecWithInlineGrounding("tavily"));

        Assert.DoesNotContain(issues, i => i.Field == "grounding_providers[0].provider_type");
    }

    // ── Tests: ExistingTemplate mode ──────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_ReturnsNoIssues_ForValidExistingTemplateSpec()
    {
        // Arrange: template "klassik" exists
        var crewService = new PreconfiguredCrewService(templateName: "klassik");
        var validator   = MakeValidator(crewService);

        // Act
        var issues = await validator.ValidateAsync(ExistingTemplateSpec("klassik"));

        // Assert
        Assert.Empty(issues);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsCriticalIssue_WhenExistingTemplateNotFound()
    {
        // Arrange: no template registered (EmptyCrewService returns null for all lookups)
        var validator = MakeValidator(new EmptyCrewService());

        // Act
        var issues = await validator.ValidateAsync(ExistingTemplateSpec("nonexistent-template"));

        // Assert: one critical issue about the missing template
        Assert.Single(issues);
        Assert.True(issues[0].IsCritical);
        Assert.Equal("existing_template_name", issues[0].Field);
    }

    // ── Tests: Composed mode ──────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_ReturnsCriticalIssue_WhenNoReviewers()
    {
        // Arrange: composed spec with empty reviewers array
        const string spec = """
            {
                "mode": "composed",
                "executor": {"reuse": "default-executor"},
                "reviewers": [],
                "finalizers": [{"reuse": "file-export"}]
            }
            """;
        var crewService = new PreconfiguredCrewService(
            executorName: "default-executor",
            finalizerName: "file-export");
        var validator = MakeValidator(crewService);

        // Act
        var issues = await validator.ValidateAsync(spec);

        // Assert: a critical issue on "reviewers"
        Assert.Contains(issues, i => i.Field == "reviewers" && i.IsCritical);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsCriticalIssue_WhenNoFinalizers()
    {
        // Arrange: composed spec with empty finalizers array
        const string spec = """
            {
                "mode": "composed",
                "executor": {"reuse": "default-executor"},
                "reviewers": [{"reuse": "briefing-fidelity"}],
                "finalizers": []
            }
            """;
        var crewService = new PreconfiguredCrewService(
            executorName: "default-executor",
            reviewerName: "briefing-fidelity");
        var validator = MakeValidator(crewService);

        // Act
        var issues = await validator.ValidateAsync(spec);

        // Assert: a critical issue on "finalizers"
        Assert.Contains(issues, i => i.Field == "finalizers" && i.IsCritical);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsCriticalIssue_WhenReuseNameNotFound()
    {
        // Arrange: spec with a reviewer reuse reference that does not exist
        const string spec = """
            {
                "mode": "composed",
                "executor": {"reuse": "default-executor"},
                "reviewers": [{"reuse": "nonexistent-reviewer"}],
                "finalizers": [{"reuse": "file-export"}]
            }
            """;
        // EmptyCrewService returns null for all profile lookups
        var crewService = new PreconfiguredCrewService(
            executorName: "default-executor",
            finalizerName: "file-export");
        // Note: reviewerName is NOT registered → GetReviewerProfileAsync returns null
        var validator = MakeValidator(crewService);

        // Act
        var issues = await validator.ValidateAsync(spec);

        // Assert: critical issue about the missing reviewer reuse reference
        Assert.Contains(issues, i => i.Field.Contains("reviewers") && i.IsCritical);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsNoIssues_WhenAllReuseReferencesResolve()
    {
        // Arrange: all references exist in the catalog
        var crewService = new PreconfiguredCrewService(
            executorName:  "default-executor",
            reviewerName:  "briefing-fidelity",
            finalizerName: "file-export");
        var validator = MakeValidator(crewService);

        // Act
        var issues = await validator.ValidateAsync(ComposedSpecWith("briefing-fidelity", "file-export"));

        // Assert
        Assert.Empty(issues);
    }

    // ── Fakes ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A crew service that returns pre-configured profiles for specific names.
    /// All other lookups return null (not found).
    /// </summary>
    private sealed class PreconfiguredCrewService(
        string? templateName  = null,
        string? executorName  = null,
        string? reviewerName  = null,
        string? finalizerName = null,
        string? advisorName   = null,
        string? groundingProviderName = null) : ICrewService
    {
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default)
        {
            if (name == templateName)
            {
                var template = new CrewTemplate(
                    Name:                 name,
                    DisplayName:          name,
                    Description:          string.Empty,
                    ExecutorProfileName:  "default-executor",
                    ReviewerProfileNames: [],
                    EvaluationStrategy:   EvaluationStrategy.Parallel,
                    ConvergenceOverride:  null,
                    AdvisorProfileNames:  [],
                    GroundingProviderNames: [],
                    IsSystem:             false);
                return Task.FromResult<CrewTemplate?>(template);
            }
            return Task.FromResult<CrewTemplate?>(null);
        }

        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default)
        {
            if (name == executorName)
            {
                var profile = new ExecutorProfile(
                    Name: name, DisplayName: name, Description: string.Empty,
                    SystemPrompt: "You are an executor.", Provider: "openai", Model: "gpt-4o",
                    MaxTokens: null, IsSystem: false);
                return Task.FromResult<ExecutorProfile?>(profile);
            }
            return Task.FromResult<ExecutorProfile?>(null);
        }

        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default)
        {
            if (name == reviewerName)
            {
                var profile = new ReviewerProfile(
                    Name: name, DisplayName: name, Description: string.Empty,
                    SystemPrompt: "You are a reviewer.", Provider: "openai", Model: "gpt-4o-mini",
                    MaxTokens: null, IsSystem: false);
                return Task.FromResult<ReviewerProfile?>(profile);
            }
            return Task.FromResult<ReviewerProfile?>(null);
        }

        public Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default)
        {
            if (name == finalizerName)
            {
                var profile = new FinalizerProfile(
                    Name: name, DisplayName: name, Description: string.Empty,
                    FinalizerType: FinalizerType.FileExport, Settings: new Dictionary<string, string>(),
                    IsSystem: false);
                return Task.FromResult<FinalizerProfile?>(profile);
            }
            return Task.FromResult<FinalizerProfile?>(null);
        }

        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default)
        {
            if (name == advisorName)
            {
                var profile = new AdvisorProfile(
                    Name: name, DisplayName: name, Description: string.Empty,
                    SystemPrompt: "You are an advisor.", Provider: "openai", Model: "gpt-4o-mini",
                    MaxTokens: null, Mode: AdvisorMode.Strategic, Trigger: AdvisorTrigger.BeforeFirstExecution,
                    IsSystem: false);
                return Task.FromResult<AdvisorProfile?>(profile);
            }
            return Task.FromResult<AdvisorProfile?>(null);
        }

        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default)
        {
            if (name == groundingProviderName)
            {
                var profile = new GroundingProviderProfile(
                    Name: name, DisplayName: name, Description: string.Empty,
                    ProviderType: "static-context",
                    ProviderSettings: new Dictionary<string, string>(),
                    MaxQueriesPerRun: null,
                    IsSystem: false);
                return Task.FromResult<GroundingProviderProfile?>(profile);
            }
            return Task.FromResult<GroundingProviderProfile?>(null);
        }

        // ── Unused interface members ───────────────────────────────────────

        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewerProfile>>([]);
        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomReviewerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExecutorProfile>>([]);
        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomExecutorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AdvisorProfile>>([]);
        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomAdvisorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>([]);
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomGroundingProviderProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FinalizerProfile>>([]);
        public Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomFinalizerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CrewTemplate>>([]);
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomCrewTemplateAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? crewTemplateName, CrewSpec? customCrew, CancellationToken ct = default)
            => Task.FromResult(new CrewSnapshot(1, crewTemplateName, null!, [], EvaluationStrategy.Parallel, null, []));
    }

    /// <summary>
    /// Returns empty / null for all catalog lookups.
    /// </summary>
    private sealed class EmptyCrewService : ICrewService
    {
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default) => Task.FromResult<CrewTemplate?>(null);
        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ExecutorProfile?>(null);
        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ReviewerProfile?>(null);
        public Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<FinalizerProfile?>(null);
        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<AdvisorProfile?>(null);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<GroundingProviderProfile?>(null);
        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewerProfile>>([]);
        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomReviewerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExecutorProfile>>([]);
        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomExecutorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AdvisorProfile>>([]);
        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomAdvisorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>([]);
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomGroundingProviderProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FinalizerProfile>>([]);
        public Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomFinalizerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CrewTemplate>>([]);
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomCrewTemplateAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? crewTemplateName, CrewSpec? customCrew, CancellationToken ct = default)
            => Task.FromResult(new CrewSnapshot(1, crewTemplateName, null!, [], EvaluationStrategy.Parallel, null, []));
    }

    /// <summary>Returns an empty model list for all providers.</summary>
    private sealed class EmptyModelCatalog : IModelCatalog
    {
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public bool IsUsingFallback(string providerName) => false;
    }

    /// <summary>Grounding factory stub with a fixed set of registered provider types.</summary>
    private sealed class StubGroundingFactory(params string[] types) : IGroundingProviderFactory
    {
        private readonly HashSet<string> _types = new(types, StringComparer.OrdinalIgnoreCase);
        public IGroundingProvider Create(string providerType) => throw new NotSupportedException();
        public bool IsRegistered(string providerType) => _types.Contains(providerType);
        public IReadOnlyCollection<string> RegisteredTypes => _types;
    }
}
