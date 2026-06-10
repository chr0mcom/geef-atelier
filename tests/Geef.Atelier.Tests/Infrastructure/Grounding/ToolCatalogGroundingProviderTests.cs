using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Grounding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

public sealed class ToolCatalogGroundingProviderTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static ToolDefinition MakeTool(
        string name = "web-search",
        string toolType = "web-search",
        ToolAccessClass accessClass = ToolAccessClass.ReadOnly,
        string description = "Performs web searches.") =>
        new(
            Name: name,
            DisplayName: name,
            Description: description,
            ToolType: toolType,
            Settings: new Dictionary<string, string>(),
            SecretRef: null,
            LlmSchema: JsonDocument.Parse("{}").RootElement,
            AccessClass: accessClass,
            IsSystem: true);

    private static GroundingProviderProfile MakeProfile() =>
        new(
            Name: "tool-catalog-default",
            DisplayName: "Tool Catalog",
            Description: "Test profile.",
            ProviderType: GroundingProviderTypes.ToolCatalog,
            ProviderSettings: new Dictionary<string, string>(),
            MaxQueriesPerRun: 1,
            IsSystem: true);

    private static (ToolCatalogGroundingProvider Provider, InMemoryToolCatalogConsultationRepository ConsultRepo)
        BuildProvider(IReadOnlyList<ToolDefinition> tools)
    {
        var consultRepo = new InMemoryToolCatalogConsultationRepository();
        var toolRepo    = new ListToolDefinitionRepository(tools);

        var services = new ServiceCollection();
        services.AddScoped<IToolDefinitionRepository>(_ => toolRepo);
        services.AddScoped<IGroundingConsultationRepository>(_ => consultRepo);

        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var provider     = new ToolCatalogGroundingProvider(
            scopeFactory,
            NullLogger<ToolCatalogGroundingProvider>.Instance);

        return (provider, consultRepo);
    }

    // ── tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_WithTools_ContextContainsToolName()
    {
        var tools = new[] { MakeTool("web-search") };
        var (provider, _) = BuildProvider(tools);

        var result = await provider.EnrichAsync("test briefing", MakeProfile(), Guid.NewGuid(), default);

        Assert.Contains("web-search", result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_WithTools_ContextContainsCatalogHeader()
    {
        var tools = new[] { MakeTool("web-search") };
        var (provider, _) = BuildProvider(tools);

        var result = await provider.EnrichAsync("test briefing", MakeProfile(), Guid.NewGuid(), default);

        Assert.Contains("Available Tool Catalog", result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_WithTools_CitationsCountMatchesToolCount()
    {
        var tools = new[]
        {
            MakeTool("web-search"),
            MakeTool("knowledge-base", "vector-store"),
        };
        var (provider, _) = BuildProvider(tools);

        var result = await provider.EnrichAsync("test briefing", MakeProfile(), Guid.NewGuid(), default);

        Assert.Equal(2, result.Citations.Count);
    }

    [Fact]
    public async Task EnrichAsync_EmptyToolList_ContextContainsNoToolsMessage()
    {
        var (provider, _) = BuildProvider([]);

        var result = await provider.EnrichAsync("test briefing", MakeProfile(), Guid.NewGuid(), default);

        Assert.Contains("_No tools registered yet._", result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_EmptyToolList_CitationsIsEmpty()
    {
        var (provider, _) = BuildProvider([]);

        var result = await provider.EnrichAsync("test briefing", MakeProfile(), Guid.NewGuid(), default);

        Assert.Empty(result.Citations);
    }

    [Fact]
    public async Task EnrichAsync_PersistsConsultation()
    {
        var tools = new[] { MakeTool("web-search") };
        var (provider, consultRepo) = BuildProvider(tools);
        var runId = Guid.NewGuid();

        await provider.EnrichAsync("test briefing", MakeProfile(), runId, default);

        Assert.Single(consultRepo.Stored);
        Assert.Equal(runId, consultRepo.Stored[0].RunId);
    }

    [Fact]
    public async Task EnrichAsync_MutatingTool_ContextContainsMutatingLabel()
    {
        var tools = new[] { MakeTool("dangerous-tool", accessClass: ToolAccessClass.Mutating) };
        var (provider, _) = BuildProvider(tools);

        var result = await provider.EnrichAsync("test briefing", MakeProfile(), Guid.NewGuid(), default);

        Assert.Contains("Mutating", result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_ReturnsCorrectProviderName()
    {
        var tools = new[] { MakeTool("web-search") };
        var (provider, _) = BuildProvider(tools);

        var result = await provider.EnrichAsync("test briefing", MakeProfile(), Guid.NewGuid(), default);

        Assert.Equal("tool-catalog-default", result.ProviderName);
    }
}

// ── stubs ──────────────────────────────────────────────────────────────────────

internal sealed class ListToolDefinitionRepository(IReadOnlyList<ToolDefinition> tools) : IToolDefinitionRepository
{
    public Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(tools.FirstOrDefault(t => t.Name == name));

    public Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult(tools);

    public Task<IReadOnlyList<ToolDefinition>> GetSystemToolsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ToolDefinition>>(tools.Where(t => t.IsSystem).ToList());

    public Task<IReadOnlyList<ToolDefinition>> GetCustomToolsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ToolDefinition>>(tools.Where(t => !t.IsSystem).ToList());

    public Task UpsertAsync(ToolDefinition tool, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task DeleteAsync(string name, CancellationToken ct = default) =>
        Task.CompletedTask;
}

internal sealed class InMemoryToolCatalogConsultationRepository : IGroundingConsultationRepository
{
    public List<GroundingConsultation> Stored { get; } = [];

    public Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct)
    {
        Stored.Add(consultation);
        return Task.FromResult(consultation);
    }

    public Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GroundingConsultation>>(Stored.Where(c => c.RunId == runId).ToList());

    public Task UpdateRefinementOutcomeAsync(Guid consultationId, RefinementOutcome outcome, CancellationToken ct) =>
        Task.CompletedTask;
}
