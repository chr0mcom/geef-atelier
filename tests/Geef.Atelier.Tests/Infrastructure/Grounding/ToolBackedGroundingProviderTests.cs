using System.Text.Json;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Grounding;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

public sealed class ToolBackedGroundingProviderTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ToolDefinition MakeStaticContextTool(string content = "static text") =>
        new(
            Name: "static-context",
            DisplayName: "Static Context",
            Description: "Static context for tests.",
            ToolType: ToolType.StaticContext,
            Settings: new Dictionary<string, string>
            {
                [ToolDefinitionSettingsKeys.StaticContent] = content
            },
            SecretRef: null,
            LlmSchema: JsonDocument.Parse("{}").RootElement,
            AccessClass: ToolAccessClass.ReadOnly,
            IsSystem: true);

    private static GroundingProviderProfile MakeProfile(string? toolName) =>
        new(
            Name: "test-profile",
            DisplayName: "Test Profile",
            Description: "A profile for unit tests.",
            ProviderType: "__tool-backed__",
            ProviderSettings: new Dictionary<string, string>(),
            MaxQueriesPerRun: 1,
            IsSystem: false,
            ToolName: toolName);

    private static ToolBackedGroundingProvider BuildProvider(
        IToolExecutor executor,
        IToolDefinitionRepository repository) =>
        new(executor, repository, NullLogger<ToolBackedGroundingProvider>.Instance);

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EnrichAsync_StaticContextTool_ReturnsContent()
    {
        const string expectedContent = "This is static grounding content.";
        var tool = MakeStaticContextTool(expectedContent);
        var repo = new StubToolDefinitionRepository(tool);
        var executor = new StaticContextToolExecutor();
        var provider = BuildProvider(executor, repo);
        var profile = MakeProfile("static-context");

        var result = await provider.EnrichAsync("briefing text", profile, Guid.NewGuid(), default);

        Assert.Equal(expectedContent, result.EnrichedContext);
        Assert.Equal("test-profile", result.ProviderName);
    }

    [Fact]
    public async Task EnrichAsync_ToolNotFound_ReturnsEmptyResult()
    {
        var repo = new StubToolDefinitionRepository(null); // no tool found
        var executor = new StaticContextToolExecutor();
        var provider = BuildProvider(executor, repo);
        var profile = MakeProfile("non-existent-tool");

        // Should throw InvalidOperationException when tool not found
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EnrichAsync("briefing", profile, Guid.NewGuid(), default));
    }

    [Fact]
    public async Task EnrichAsync_ToolExecutionError_ReturnsEmptyResult()
    {
        var tool = MakeStaticContextTool();
        var repo = new StubToolDefinitionRepository(tool);
        var executor = new ErrorReturningToolExecutor("Test error");
        var provider = BuildProvider(executor, repo);
        var profile = MakeProfile("static-context");

        var result = await provider.EnrichAsync("briefing text", profile, Guid.NewGuid(), default);

        Assert.Equal(string.Empty, result.EnrichedContext);
        Assert.Empty(result.Citations);
        Assert.Null(result.CostEur);
    }

    [Fact]
    public void SystemTools_All_HasEightTools()
    {
        Assert.Equal(8, SystemTools.All.Count);
    }

    [Fact]
    public void SystemTools_WebSearch_HasCorrectToolType()
    {
        Assert.Equal(ToolType.WebSearch, SystemTools.WebSearch.ToolType);
    }
}

// ---------------------------------------------------------------------------
// Stubs
// ---------------------------------------------------------------------------

internal sealed class StubToolDefinitionRepository(ToolDefinition? toolToReturn) : IToolDefinitionRepository
{
    public Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(toolToReturn);

    public Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ToolDefinition>>([]);

    public Task<IReadOnlyList<ToolDefinition>> GetSystemToolsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ToolDefinition>>([]);

    public Task<IReadOnlyList<ToolDefinition>> GetCustomToolsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ToolDefinition>>([]);

    public Task UpsertAsync(ToolDefinition tool, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task DeleteAsync(string name, CancellationToken ct = default) =>
        Task.CompletedTask;
}

/// <summary>Executes static-context tools by reading Settings[StaticContent].</summary>
internal sealed class StaticContextToolExecutor : IToolExecutor
{
    public Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool,
        string inputJson,
        ToolInvocationContext ctx,
        CancellationToken ct = default)
    {
        var content = tool.Settings.TryGetValue(ToolDefinitionSettingsKeys.StaticContent, out var v) ? v : "";
        return Task.FromResult(new ToolExecutionResult(content, null, null));
    }
}

/// <summary>Always returns an error result.</summary>
internal sealed class ErrorReturningToolExecutor(string error) : IToolExecutor
{
    public Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool,
        string inputJson,
        ToolInvocationContext ctx,
        CancellationToken ct = default) =>
        Task.FromResult(new ToolExecutionResult("", null, error));
}
