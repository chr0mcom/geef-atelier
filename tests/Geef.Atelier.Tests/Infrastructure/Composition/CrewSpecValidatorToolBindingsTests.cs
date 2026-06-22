using System.Text.Json;
using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Composition;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Infrastructure.Composition;

/// <summary>
/// Unit tests for Step 8 (tool-binding validation) in <see cref="CrewSpecValidator"/>:
/// tool existence, provider agentic-tool capability, and Mutating-access restriction.
/// </summary>
public sealed class CrewSpecValidatorToolBindingsTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static CrewSpecValidator MakeValidator(
        IToolDefinitionRepository toolRepository,
        ILlmClientResolver llmClientResolver,
        ICrewService? crewService = null,
        IModelCatalog? modelCatalog = null)
    {
        crewService    ??= new AlwaysResolvedCrewService();
        modelCatalog   ??= new KnownModelCatalog("claude-cli", "claude-opus-4-8");
        var groundingFactory = new StubGroundingFactory("tavily");
        return new CrewSpecValidator(crewService, modelCatalog, groundingFactory, toolRepository,
            new Geef.Atelier.Tests.Fakes.InMemorySpecializationPackRepository(), llmClientResolver);
    }

    /// <summary>
    /// A minimal valid composed spec with an inline executor that binds a single tool.
    /// The executor provider is <c>claude-cli</c>.
    /// </summary>
    private static string ComposedSpecWithExecutorTool(string toolName) => $$"""
        {
            "mode": "composed",
            "executor": {
                "name": "task-executor",
                "provider": "claude-cli",
                "model": "claude-opus-4-8",
                "max_tokens": 32000,
                "system_prompt": "You are a specialist writer.",
                "tool_names": ["{{toolName}}"]
            },
            "reviewers": [{"reuse": "briefing-fidelity"}],
            "finalizers": [{"reuse": "file-export"}]
        }
        """;

    /// <summary>
    /// A minimal valid composed spec with an inline reviewer that binds a single tool.
    /// </summary>
    private static string ComposedSpecWithReviewerTool(string toolName) => $$"""
        {
            "mode": "composed",
            "executor": {
                "name": "task-executor",
                "provider": "claude-cli",
                "model": "claude-opus-4-8",
                "max_tokens": 32000,
                "system_prompt": "You are a specialist writer."
            },
            "reviewers": [{
                "name": "inline-reviewer",
                "provider": "openai",
                "model": "gpt-4o-mini",
                "system_prompt": "You review content.",
                "tool_names": ["{{toolName}}"]
            }],
            "finalizers": [{"reuse": "file-export"}]
        }
        """;

    private static ToolDefinition MakeTool(string name, ToolAccessClass accessClass) =>
        new(
            Name:        name,
            DisplayName: name,
            Description: "A test tool.",
            ToolType:    "web-search",
            Settings:    new Dictionary<string, string>(),
            SecretRef:   null,
            LlmSchema:   JsonDocument.Parse("{}").RootElement,
            AccessClass: accessClass,
            IsSystem:    false);

    // ── Tests: 8b – unknown tool name ─────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_FlagsCritical_WhenExecutorToolNameUnknown()
    {
        // Arrange: repository returns null for every lookup
        var toolRepo      = new FixedToolRepository([]);
        var llmResolver   = new AgenticCapableResolver();
        var validator     = MakeValidator(toolRepo, llmResolver);

        // Act
        var issues = await validator.ValidateAsync(ComposedSpecWithExecutorTool("nonexistent-tool"));

        // Assert
        var issue = Assert.Single(issues, i => i.Field == "executor.tool_names" && i.IsCritical);
        Assert.Contains("nonexistent-tool", issue.Message);
        Assert.Contains("not registered", issue.Message);
    }

    [Fact]
    public async Task ValidateAsync_FlagsCritical_WhenReviewerToolNameUnknown()
    {
        // Arrange: no tools in catalogue
        var toolRepo    = new FixedToolRepository([]);
        var llmResolver = new AgenticCapableResolver();
        var validator   = MakeValidator(toolRepo, llmResolver);

        // Act
        var issues = await validator.ValidateAsync(ComposedSpecWithReviewerTool("ghost-tool"));

        // Assert
        var issue = Assert.Single(issues, i => i.Field == "reviewers[0].tool_names" && i.IsCritical);
        Assert.Contains("ghost-tool", issue.Message);
    }

    // ── Tests: 8c – Mutating tool blocked ─────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_FlagsCritical_WhenMutatingToolBoundWithoutOptIn()
    {
        // Arrange: catalogue has "delete-file" as Mutating; spec has no allow_mutating_tools
        var mutatingTool = MakeTool("delete-file", ToolAccessClass.Mutating);
        var toolRepo     = new FixedToolRepository([mutatingTool]);
        var llmResolver  = new AgenticCapableResolver();
        var validator    = MakeValidator(toolRepo, llmResolver);

        // Act
        var issues = await validator.ValidateAsync(ComposedSpecWithExecutorTool("delete-file"));

        // Assert
        var issue = Assert.Single(issues, i => i.Field == "executor.tool_names" && i.IsCritical);
        Assert.Contains("Mutating", issue.Message);
        Assert.Contains("allow_mutating_tools", issue.Message);
    }

    [Fact]
    public async Task ValidateAsync_NoIssues_WhenMutatingToolBoundWithOptIn()
    {
        // Arrange: catalogue has "delete-file" as Mutating; spec sets allow_mutating_tools: true
        var mutatingTool = MakeTool("delete-file", ToolAccessClass.Mutating);
        var toolRepo     = new FixedToolRepository([mutatingTool]);
        var llmResolver  = new AgenticCapableResolver();
        var validator    = MakeValidator(toolRepo, llmResolver);

        const string spec = """
            {
                "mode": "composed",
                "allow_mutating_tools": true,
                "executor": {
                    "name": "task-executor",
                    "provider": "claude-cli",
                    "model": "claude-opus-4-8",
                    "max_tokens": 32000,
                    "system_prompt": "You are a specialist writer.",
                    "tool_names": ["delete-file"]
                },
                "reviewers": [{"reuse": "briefing-fidelity"}],
                "finalizers": [{"reuse": "file-export"}]
            }
            """;

        // Act
        var issues = await validator.ValidateAsync(spec);

        // Assert: no Mutating-block issue when opt-in is set
        Assert.DoesNotContain(issues, i => i.Field == "executor.tool_names" && i.IsCritical
            && i.Message.Contains("Mutating"));
    }

    // ── Tests: 8a – non-agentic provider ──────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_FlagsCritical_WhenProviderDoesNotSupportAgenticTools()
    {
        // Arrange: "claude-cli" is reported as NOT supporting agentic tools
        var readOnlyTool = MakeTool("web-search", ToolAccessClass.ReadOnly);
        var toolRepo     = new FixedToolRepository([readOnlyTool]);
        var llmResolver  = new NonAgenticResolver();
        var validator    = MakeValidator(toolRepo, llmResolver);

        // Act
        var issues = await validator.ValidateAsync(ComposedSpecWithExecutorTool("web-search"));

        // Assert
        var issue = Assert.Single(issues, i => i.Field == "executor.provider" && i.IsCritical);
        Assert.Contains("does not support agentic tool-use", issue.Message);
        Assert.Contains("claude-cli", issue.Message);
    }

    // ── Tests: happy path ─────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_NoToolIssues_WhenReadOnlyToolBoundToAgenticProvider()
    {
        // Arrange: one ReadOnly tool, provider supports agentic tools
        var readOnlyTool = MakeTool("web-search", ToolAccessClass.ReadOnly);
        var toolRepo     = new FixedToolRepository([readOnlyTool]);
        var llmResolver  = new AgenticCapableResolver();
        var validator    = MakeValidator(toolRepo, llmResolver);

        // Act
        var issues = await validator.ValidateAsync(ComposedSpecWithExecutorTool("web-search"));

        // Assert: no tool-binding issues
        Assert.DoesNotContain(issues, i => i.Field.Contains("tool_names") || i.Field.Contains("provider"));
    }

    [Fact]
    public async Task ValidateAsync_NoToolIssues_WhenToolNamesIsEmpty()
    {
        // Arrange: spec with no tool_names on any actor
        const string spec = """
            {
                "mode": "composed",
                "executor": {
                    "name": "task-executor",
                    "provider": "claude-cli",
                    "model": "claude-opus-4-8",
                    "max_tokens": 32000,
                    "system_prompt": "You are a specialist writer."
                },
                "reviewers": [{"reuse": "briefing-fidelity"}],
                "finalizers": [{"reuse": "file-export"}]
            }
            """;
        var toolRepo    = new FixedToolRepository([]);
        var llmResolver = new AgenticCapableResolver();
        var validator   = MakeValidator(toolRepo, llmResolver);

        // Act
        var issues = await validator.ValidateAsync(spec);

        // Assert: no tool-binding related issues whatsoever
        Assert.DoesNotContain(issues, i => i.Field.Contains("tool_names") || (i.Field.Contains("provider") && i.IsCritical));
    }

    // ── Fakes ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tool repository that returns pre-registered tools by name;
    /// all other lookups return null.
    /// </summary>
    private sealed class FixedToolRepository(IReadOnlyList<ToolDefinition> tools) : IToolDefinitionRepository
    {
        private readonly Dictionary<string, ToolDefinition> _map =
            tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        public Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(_map.TryGetValue(name, out var t) ? t : null);
        public Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ToolDefinition>>(tools);
        public Task<IReadOnlyList<ToolDefinition>> GetSystemToolsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ToolDefinition>>([]);
        public Task<IReadOnlyList<ToolDefinition>> GetCustomToolsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ToolDefinition>>([]);
        public Task UpsertAsync(ToolDefinition tool, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>Reports every provider as supporting agentic tools.</summary>
    private sealed class AgenticCapableResolver : ILlmClientResolver
    {
        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName)
            => throw new NotSupportedException();
        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
            => throw new NotSupportedException();
        public bool SupportsAgenticTools(string providerName) => true;
        public bool SupportsStructuredOutputs(string providerName) => true;
    }

    /// <summary>Reports every provider as NOT supporting agentic tools.</summary>
    private sealed class NonAgenticResolver : ILlmClientResolver
    {
        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName)
            => throw new NotSupportedException();
        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
            => throw new NotSupportedException();
        public bool SupportsAgenticTools(string providerName) => false;
        public bool SupportsStructuredOutputs(string providerName) => false;
    }

    /// <summary>
    /// Crew service that always resolves known reuse refs used in the spec fixtures.
    /// </summary>
    private sealed class AlwaysResolvedCrewService : ICrewService
    {
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default)
            => Task.FromResult<CrewTemplate?>(null);

        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default)
        {
            var p = new ExecutorProfile(
                Name: name, DisplayName: name, Description: string.Empty,
                SystemPrompt: "You are an executor.", Provider: "openai", Model: "gpt-4o",
                MaxTokens: null, IsSystem: false);
            return Task.FromResult<ExecutorProfile?>(p);
        }

        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default)
        {
            var p = new ReviewerProfile(
                Name: name, DisplayName: name, Description: string.Empty,
                SystemPrompt: "You are a reviewer.", Provider: "openai", Model: "gpt-4o-mini",
                MaxTokens: null, IsSystem: false);
            return Task.FromResult<ReviewerProfile?>(p);
        }

        public Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default)
        {
            var p = new FinalizerProfile(
                Name: name, DisplayName: name, Description: string.Empty,
                FinalizerType: FinalizerType.FileExport, Settings: new Dictionary<string, string>(),
                IsSystem: false);
            return Task.FromResult<FinalizerProfile?>(p);
        }

        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default)
        {
            var p = new AdvisorProfile(
                Name: name, DisplayName: name, Description: string.Empty,
                SystemPrompt: "You are an advisor.", Provider: "openai", Model: "gpt-4o-mini",
                MaxTokens: null, Mode: AdvisorMode.Strategic, Trigger: AdvisorTrigger.BeforeFirstExecution,
                IsSystem: false);
            return Task.FromResult<AdvisorProfile?>(p);
        }

        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default)
        {
            var p = new GroundingProviderProfile(
                Name: name, DisplayName: name, Description: string.Empty,
                ProviderType: "static-context",
                ProviderSettings: new Dictionary<string, string>(),
                MaxQueriesPerRun: null,
                IsSystem: false);
            return Task.FromResult<GroundingProviderProfile?>(p);
        }

        // ── Unused interface members ──────────────────────────────────────────
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

    /// <summary>Grounding factory stub with a fixed set of registered provider types.</summary>
    private sealed class StubGroundingFactory(params string[] types) : IGroundingProviderFactory
    {
        private readonly HashSet<string> _types = new(types, StringComparer.OrdinalIgnoreCase);
        public IGroundingProvider Create(string providerType) => throw new NotSupportedException();
        public bool IsRegistered(string providerType) => _types.Contains(providerType);
        public IReadOnlyCollection<string> RegisteredTypes => _types;
    }

    /// <summary>Reports one known (provider, model) pair as available; everything else empty.</summary>
    private sealed class KnownModelCatalog(string provider, string model) : IModelCatalog
    {
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>(
                string.Equals(providerName, provider, StringComparison.OrdinalIgnoreCase)
                    ? [new ModelInfo(model, model, null, true)]
                    : []);
        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default)
            => ListModelsAsync(providerName, ct);
        public bool IsUsingFallback(string providerName) => false;
    }
}
