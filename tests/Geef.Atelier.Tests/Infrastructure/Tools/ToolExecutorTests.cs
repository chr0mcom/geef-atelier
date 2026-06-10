using System.Text.Json;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Tools;

public sealed class ToolExecutorTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ToolDefinition MakeTool(
        string toolType,
        IReadOnlyDictionary<string, string>? settings = null) =>
        new(
            Name: "test-tool",
            DisplayName: "Test Tool",
            Description: "A tool used in unit tests.",
            ToolType: toolType,
            Settings: settings ?? new Dictionary<string, string>(),
            SecretRef: null,
            LlmSchema: JsonDocument.Parse("{}").RootElement,
            AccessClass: ToolAccessClass.ReadOnly,
            IsSystem: false);

    private static ToolInvocationContext MakeContext() =>
        new(
            RunId: Guid.NewGuid(),
            IterationNumber: 0,
            ActorType: "executor",
            ActorName: "default-executor",
            Sequence: 1);

    private static ToolExecutor BuildExecutor(InMemoryToolInvocationRepository repo) =>
        new(repo, new NoOpHttpClientFactory(), NullLogger<ToolExecutor>.Instance);

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_StaticContext_ReturnsContent()
    {
        const string expectedContent = "This is the static content for the test.";
        var tool = MakeTool(
            ToolType.StaticContext,
            new Dictionary<string, string>
            {
                [ToolDefinitionSettingsKeys.StaticContent] = expectedContent
            });

        var repo = new InMemoryToolInvocationRepository();
        var executor = BuildExecutor(repo);

        var result = await executor.ExecuteAsync(tool, "{}", MakeContext());

        Assert.Equal(expectedContent, result.Output);
        Assert.Null(result.Error);
        Assert.Null(result.CostEur);
    }

    [Fact]
    public async Task ExecuteAsync_PersistsInvocation()
    {
        var tool = MakeTool(
            ToolType.StaticContext,
            new Dictionary<string, string>
            {
                [ToolDefinitionSettingsKeys.StaticContent] = "hello"
            });

        var repo = new InMemoryToolInvocationRepository();
        var executor = BuildExecutor(repo);
        var ctx = MakeContext();

        await executor.ExecuteAsync(tool, "{}", ctx);

        var invocations = await repo.GetByRunIdAsync(ctx.RunId);
        Assert.Single(invocations);

        var recorded = invocations[0];
        Assert.Equal(ctx.RunId, recorded.RunId);
        Assert.Equal(ctx.IterationNumber, recorded.IterationNumber);
        Assert.Equal(ctx.ActorType, recorded.ActorType);
        Assert.Equal(ctx.ActorName, recorded.ActorName);
        Assert.Equal(tool.Name, recorded.ToolName);
        Assert.Equal(tool.ToolType, recorded.ToolType);
        Assert.Equal(ToolInvocationOutcome.Success, recorded.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_WebSearch_WithoutApiKey_ReturnsErrorResult()
    {
        // No TAVILY_API_KEY env var set in unit test environment — executor returns a
        // descriptive error result rather than throwing.
        var tool = MakeTool(ToolType.WebSearch, new Dictionary<string, string>
        {
            [ToolDefinitionSettingsKeys.MaxResults] = "5"
        });
        // Override SecretRef to ensure env var lookup is exercised
        var toolWithSecretRef = tool with { SecretRef = "TAVILY_API_KEY_UNIT_TEST_ABSENT" };

        var repo = new InMemoryToolInvocationRepository();
        var executor = BuildExecutor(repo);

        var result = await executor.ExecuteAsync(toolWithSecretRef, """{"query":"test"}""", MakeContext());

        // Must not throw — returns an error result instead
        Assert.NotNull(result.Error);
        Assert.Contains("TAVILY_API_KEY", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_NotYetWiredType_ReturnsNotYetWiredResult()
    {
        // MCP tool type is not wired yet
        var tool = MakeTool(ToolType.McpTool);
        var repo = new InMemoryToolInvocationRepository();
        var executor = BuildExecutor(repo);

        var result = await executor.ExecuteAsync(tool, "{}", MakeContext());

        // Must not throw — returns a not-yet-wired notice
        Assert.Equal("NotYetWired", result.Error);
        Assert.Contains("GroundingProvider path", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_WebSearch_WithoutApiKey_StillPersistsInvocation()
    {
        var tool = MakeTool(ToolType.WebSearch) with { SecretRef = "TAVILY_API_KEY_UNIT_TEST_ABSENT" };
        var repo = new InMemoryToolInvocationRepository();
        var executor = BuildExecutor(repo);
        var ctx = MakeContext();

        await executor.ExecuteAsync(tool, """{"query":"test"}""", ctx);

        var invocations = await repo.GetByRunIdAsync(ctx.RunId);
        Assert.Single(invocations);
        Assert.Equal(ToolInvocationOutcome.Failed, invocations[0].Outcome);
    }
}

// ---------------------------------------------------------------------------
// In-memory fakes
// ---------------------------------------------------------------------------

internal sealed class NoOpHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new();
}

internal sealed class InMemoryToolInvocationRepository : IToolInvocationRepository
{
    private readonly List<ToolInvocation> _store = [];

    public Task AddAsync(ToolInvocation invocation, CancellationToken ct = default)
    {
        _store.Add(invocation);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ToolInvocation>> GetByRunIdAsync(Guid runId, CancellationToken ct = default)
    {
        IReadOnlyList<ToolInvocation> result = _store
            .Where(i => i.RunId == runId)
            .OrderBy(i => i.Sequence)
            .ToList();
        return Task.FromResult(result);
    }
}
