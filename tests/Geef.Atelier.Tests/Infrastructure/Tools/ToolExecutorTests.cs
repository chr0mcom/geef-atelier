using System.Text.Json;
using Geef.Atelier.Application.Security;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain.Mcp;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Mcp;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Mcp;
using Geef.Atelier.Infrastructure.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

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

    private static ToolExecutor BuildExecutor(IToolInvocationRepository repo) =>
        new(repo.ToScopeFactory(), new NoOpHttpClientFactory(), new NullMcpServerConfigRepository(), new NullMcpClientFactory(), NullLogger<ToolExecutor>.Instance);

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
    public async Task ExecuteAsync_McpTool_WithoutServerId_ReturnsError()
    {
        // mcp-tool without mcpServerId setting → descriptive error result, no throw
        var tool = MakeTool(ToolType.McpTool);
        var repo = new InMemoryToolInvocationRepository();
        var executor = BuildExecutor(repo);

        var result = await executor.ExecuteAsync(tool, "{}", MakeContext());

        Assert.NotNull(result.Error);
        Assert.Contains("mcpServerId", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_McpTool_ServerNotFound_ReturnsError()
    {
        // mcp-tool with valid serverId but server not found in repository
        var serverId = Guid.NewGuid();
        var tool = MakeTool(ToolType.McpTool, new Dictionary<string, string>
        {
            [ToolDefinitionSettingsKeys.McpServerId]     = serverId.ToString(),
            [ToolDefinitionSettingsKeys.McpOriginalName] = "some-tool",
        });
        var repo = new InMemoryToolInvocationRepository();
        var executor = BuildExecutor(repo);

        var result = await executor.ExecuteAsync(tool, "{}", MakeContext());

        Assert.NotNull(result.Error);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_McpTool_InactiveServer_ReturnsError()
    {
        // mcp-tool with valid serverId, but server is inactive
        var serverId = Guid.NewGuid();
        var inactiveConfig = new McpServerConfig
        {
            Id       = serverId,
            Name     = "inactive-server",
            Url      = "http://localhost:9999",
            IsActive = false,
        };
        var tool = MakeTool(ToolType.McpTool, new Dictionary<string, string>
        {
            [ToolDefinitionSettingsKeys.McpServerId]     = serverId.ToString(),
            [ToolDefinitionSettingsKeys.McpOriginalName] = "some-tool",
        });
        var repo = new InMemoryToolInvocationRepository();
        var executor = new ToolExecutor(
            repo.ToScopeFactory(),
            new NoOpHttpClientFactory(),
            new FixedMcpServerConfigRepository(inactiveConfig),
            new NullMcpClientFactory(),
            NullLogger<ToolExecutor>.Instance);

        var result = await executor.ExecuteAsync(tool, "{}", MakeContext());

        Assert.NotNull(result.Error);
        Assert.Contains("inactive", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_NotYetWiredType_ReturnsNotYetWiredResult()
    {
        // knowledge-base type is not wired yet in ToolExecutor
        var tool = MakeTool(ToolType.KnowledgeBase);
        var repo = new InMemoryToolInvocationRepository();
        var executor = BuildExecutor(repo);

        var result = await executor.ExecuteAsync(tool, "{}", MakeContext());

        // Must not throw — returns a not-yet-wired notice
        Assert.Equal("NotYetWired", result.Error);
        Assert.Contains("GroundingProvider path", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_AuditWriteFails_KeepsTheToolResult()
    {
        var tool = MakeTool(
            ToolType.StaticContext,
            new Dictionary<string, string> { [ToolDefinitionSettingsKeys.StaticContent] = "kept" });
        var executor = BuildExecutor(new ThrowingToolInvocationRepository());

        var result = await executor.ExecuteAsync(tool, "{}", MakeContext());

        Assert.Equal("kept", result.Output);
        Assert.Null(result.Error);
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

    private const string LeakedSecret = "abcd1234efgh5678";

    [Fact]
    public async Task ExecuteAsync_McpCallThrows_ReturnsAFixedSentenceAndLogsOnlyTheRedactedException()
    {
        var serverId = Guid.NewGuid();
        var config = new McpServerConfig { Id = serverId, Name = "server", Url = "http://localhost:9999", IsActive = true };
        var tool = MakeTool(ToolType.McpTool, new Dictionary<string, string>
        {
            [ToolDefinitionSettingsKeys.McpServerId]     = serverId.ToString(),
            [ToolDefinitionSettingsKeys.McpOriginalName] = "some-tool",
        });
        var logger = new CapturingToolExecutorLogger();
        var executor = new ToolExecutor(
            new InMemoryToolInvocationRepository().ToScopeFactory(), new NoOpHttpClientFactory(),
            new FixedMcpServerConfigRepository(config),
            new ThrowingMcpClientFactory(new HttpRequestException($"bad credentials supplied: token={LeakedSecret}")),
            logger);

        var result = await executor.ExecuteAsync(tool, "{}", MakeContext());

        Assert.Equal(ToolExecutor.McpToolFailedMessage, result.Error);
        logger.AssertRedacted();
    }

    [Fact]
    public async Task ExecuteAsync_WebSearchThrows_ReturnsAFixedSentenceAndLogsOnlyTheRedactedException()
    {
        var tool = MakeTool(ToolType.WebSearch) with { SecretRef = "TAVILY_API_KEY" };
        var logger = new CapturingToolExecutorLogger();
        var executor = new ToolExecutor(
            new InMemoryToolInvocationRepository().ToScopeFactory(),
            new ThrowingHttpClientFactory(new HttpRequestException($"upstream said key={LeakedSecret}")),
            new NullMcpServerConfigRepository(), new NullMcpClientFactory(), logger, new FixedKeyPolicy());

        var result = await executor.ExecuteAsync(tool, """{"query":"test"}""", MakeContext());

        Assert.Equal(ToolExecutor.WebSearchFailedMessage, result.Error);
        logger.AssertRedacted();
    }

    [Fact]
    public async Task ExecuteAsync_UnexpectedException_ReturnsAFixedSentenceAndLogsOnlyTheRedactedException()
    {
        var tool = MakeTool(ToolType.McpTool, new Dictionary<string, string>
        {
            [ToolDefinitionSettingsKeys.McpServerId]     = Guid.NewGuid().ToString(),
            [ToolDefinitionSettingsKeys.McpOriginalName] = "some-tool",
        });
        var logger = new CapturingToolExecutorLogger();
        var repo = new InMemoryToolInvocationRepository();
        var executor = new ToolExecutor(
            repo.ToScopeFactory(), new NoOpHttpClientFactory(),
            new ThrowingMcpServerConfigRepository(new InvalidOperationException($"connection failed: password={LeakedSecret}")),
            new NullMcpClientFactory(), logger);
        var ctx = MakeContext();

        var result = await executor.ExecuteAsync(tool, "{}", ctx);

        Assert.Equal(ToolExecutor.ToolFailedMessage, result.Error);
        logger.AssertRedacted();
        Assert.Contains(logger.Entries, e => e.Message.Contains(ctx.RunId.ToString(), StringComparison.Ordinal));
        Assert.Equal(ToolInvocationOutcome.Failed, Assert.Single(await repo.GetByRunIdAsync(ctx.RunId)).Outcome);
    }

    private sealed class CapturingToolExecutorLogger : ILogger<ToolExecutor>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));

        public void AssertRedacted()
        {
            var failure = Assert.Single(Entries, e => e.Level >= LogLevel.Warning);
            Assert.Null(failure.Exception);
            Assert.DoesNotContain(LeakedSecret, failure.Message);
            Assert.Contains(Geef.Atelier.Core.Domain.Warnings.SecretRedactionExtensions.Placeholder, failure.Message);
        }
    }

    private sealed class ThrowingMcpClientFactory(Exception exception) : IAtelierMcpClientFactory
    {
        public Task<McpClient> ConnectAsync(McpServerConfig config, CancellationToken ct = default) => throw exception;
    }

    private sealed class ThrowingHttpClientFactory(Exception exception) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new ThrowingHandler(exception)) { BaseAddress = new Uri("http://localhost.invalid/") };

        private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw exception;
        }
    }

    private sealed class FixedKeyPolicy : IEnvReferencePolicy
    {
        public bool IsAllowed(string? name) => true;
        public void EnsureAllowed(string? name, string field) { }
        public string? Read(string? name, string usage) => "test-key";
    }

    private sealed class ThrowingMcpServerConfigRepository(Exception exception) : IMcpServerConfigRepository
    {
        public Task<IReadOnlyList<McpServerConfig>> GetAllAsync(CancellationToken ct = default) => throw exception;
        public Task<IReadOnlyList<McpServerConfig>> GetActiveAsync(CancellationToken ct = default) => throw exception;
        public Task<McpServerConfig?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw exception;
        public Task UpsertAsync(McpServerConfig config, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
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

internal sealed class ThrowingToolInvocationRepository : IToolInvocationRepository
{
    public Task AddAsync(ToolInvocation invocation, CancellationToken ct = default) =>
        throw new InvalidOperationException("audit store unavailable");

    public Task<IReadOnlyList<ToolInvocation>> GetByRunIdAsync(Guid runId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ToolInvocation>>([]);
}

internal static class ToolInvocationRepositoryScopeExtensions
{
    public static IServiceScopeFactory ToScopeFactory(this IToolInvocationRepository repository) =>
        new ServiceCollection()
            .AddSingleton(repository)
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();
}

/// <summary>Returns null for every GetByIdAsync call (simulates missing server config).</summary>
internal sealed class NullMcpServerConfigRepository : IMcpServerConfigRepository
{
    public Task<IReadOnlyList<McpServerConfig>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<McpServerConfig>>([]);
    public Task<IReadOnlyList<McpServerConfig>> GetActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<McpServerConfig>>([]);
    public Task<McpServerConfig?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<McpServerConfig?>(null);
    public Task UpsertAsync(McpServerConfig config, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Should never be called in unit tests that don't reach connection logic.</summary>
internal sealed class NullMcpClientFactory : IAtelierMcpClientFactory
{
    public Task<McpClient> ConnectAsync(McpServerConfig config, CancellationToken ct = default)
        => throw new NotSupportedException("NullMcpClientFactory should not be called in unit tests.");
}

/// <summary>Returns a fixed <see cref="McpServerConfig"/> when queried by its ID; null for all others.</summary>
internal sealed class FixedMcpServerConfigRepository(McpServerConfig config) : IMcpServerConfigRepository
{
    public Task<IReadOnlyList<McpServerConfig>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<McpServerConfig>>([config]);
    public Task<IReadOnlyList<McpServerConfig>> GetActiveAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<McpServerConfig>>(config.IsActive ? [config] : []);
    public Task<McpServerConfig?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<McpServerConfig?>(config.Id == id ? config : null);
    public Task UpsertAsync(McpServerConfig c, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
}
