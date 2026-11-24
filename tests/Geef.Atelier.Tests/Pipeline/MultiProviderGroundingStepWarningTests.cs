using System.Text.Json;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Tests.Infrastructure.Grounding;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using DomainGroundingResult = Geef.Atelier.Core.Domain.Crew.Grounding.GroundingResult;
using SdkGroundingResult = Geef.Sdk.Results.GroundingResult;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// A skipped grounding provider becomes a run warning, and the five system profiles always run through the
/// provider-type path, also when an older snapshot still carries their tool name.
/// </summary>
public sealed class MultiProviderGroundingStepWarningTests
{
    public static TheoryData<string> LegacySystemProfiles => new()
    {
        "tavily-refined", "tavily-basic", "run-attachments", "knowledge-base-default", "academic-default",
    };

    [Fact]
    public async Task FailingProviderIsSkippedWithAWarningAndTheOthersStillContribute()
    {
        var sink = new RecordingWarningSink();
        var runId = Guid.NewGuid();
        var step = Step(
            [Profile("broken", "broken-type", "Broken Source"), Profile("ok", "ok-type")],
            [new ThrowingProvider("broken-type", "Upstream said no: key sk-or-v1-0123456789abcdef0123456789abcdef rejected at cli-proxy:8090\nsecond line"), new FixedProvider("ok-type", "OK block")],
            runId, sink);

        var result = await step.RunAsync("briefing", CancellationToken.None);

        Assert.True(result.Context.TryGet(AtelierContextKeys.GroundingContext, out var context));
        Assert.Contains("OK block", context);
        var warning = Assert.Single(sink.Warnings);
        Assert.Equal((runId, RunWarningSources.Grounding, RunWarningCodes.GroundingProviderSkipped),
            (warning.RunId, warning.Source, warning.Code));
        Assert.Equal("The source \"Broken Source\" could not be read and was skipped.", warning.Message);
        Assert.DoesNotContain("sk-or-v1", warning.Message);
        Assert.DoesNotContain("cli-proxy", warning.Message);
        Assert.DoesNotContain("Upstream", warning.Message);
    }

    [Theory]
    [MemberData(nameof(LegacySystemProfiles))]
    public async Task SystemProfileWithAStaleToolNameRunsThroughTheProviderTypePath(string name)
    {
        var codeProfile = SystemCrew.GroundingProviderProfiles[name];
        Assert.Null(codeProfile.ToolName);
        var staleSnapshotProfile = codeProfile with { ToolName = "knowledge-base" };
        var toolExecutor = new CountingToolExecutor();
        var step = Step(
            [staleSnapshotProfile],
            [new FixedProvider(codeProfile.ProviderType, $"legacy block for {name}")],
            Guid.NewGuid(), new RecordingWarningSink(), ToolProvider(toolExecutor));

        var result = await step.RunAsync("briefing", CancellationToken.None);

        Assert.True(result.Context.TryGet(AtelierContextKeys.GroundingContext, out var context));
        Assert.Contains($"legacy block for {name}", context);
        Assert.Equal(0, toolExecutor.Calls);
    }

    [Fact]
    public async Task CustomProfileWithAToolNameStillUsesTheToolPath()
    {
        var toolExecutor = new CountingToolExecutor();
        var custom = Profile("custom-static", "static-context") with { ToolName = "static-context" };
        var step = Step([custom], [], Guid.NewGuid(), new RecordingWarningSink(), ToolProvider(toolExecutor));

        var result = await step.RunAsync("briefing", CancellationToken.None);

        Assert.Equal(1, toolExecutor.Calls);
        Assert.True(result.Context.TryGet(AtelierContextKeys.GroundingContext, out var context));
        Assert.Contains("tool output", context);
    }

    [Fact]
    public async Task ToolErrorBecomesAWarningInsteadOfAnEmptyContext()
    {
        var sink = new RecordingWarningSink();
        var custom = Profile("custom-search", "tavily", "Custom Search") with { ToolName = "static-context" };
        var provider = new ToolBackedGroundingProvider(
            new ErrorReturningToolExecutor("TAVILY_API_KEY environment variable is not set."),
            new StubToolDefinitionRepository(StaticTool()),
            NullLogger<ToolBackedGroundingProvider>.Instance);
        var step = Step([custom], [], Guid.NewGuid(), sink, provider);

        var result = await step.RunAsync("briefing", CancellationToken.None);

        Assert.False(result.Context.TryGet(AtelierContextKeys.GroundingContext, out _));
        var warning = Assert.Single(sink.Warnings);
        Assert.Equal(RunWarningCodes.GroundingProviderSkipped, warning.Code);
        Assert.Contains("Custom Search", warning.Message);
        Assert.DoesNotContain("TAVILY_API_KEY", warning.Message);
    }

    private static MultiProviderGroundingStep Step(
        IReadOnlyList<GroundingProviderProfile> profiles,
        IEnumerable<IGroundingProvider> providers,
        Guid runId,
        IRunWarningSink sink,
        ToolBackedGroundingProvider? toolProvider = null) =>
        new(new PassThroughStep(), profiles, new Factory(providers), runId,
            NullLogger<MultiProviderGroundingStep>.Instance, toolBackedProvider: toolProvider, warningSink: sink);

    private static ToolBackedGroundingProvider ToolProvider(IToolExecutor executor) =>
        new(executor, new StubToolDefinitionRepository(StaticTool()), NullLogger<ToolBackedGroundingProvider>.Instance);

    private static GroundingProviderProfile Profile(string name, string providerType, string? displayName = null) => new(
        Name: name,
        DisplayName: displayName ?? name,
        Description: "test",
        ProviderType: providerType,
        ProviderSettings: new Dictionary<string, string>(),
        MaxQueriesPerRun: null,
        IsSystem: false);

    private static ToolDefinition StaticTool() =>
        new(
            Name: "static-context",
            DisplayName: "Static Context",
            Description: "test",
            ToolType: ToolType.StaticContext,
            Settings: new Dictionary<string, string> { [ToolDefinitionSettingsKeys.StaticContent] = "tool output" },
            SecretRef: null,
            LlmSchema: JsonDocument.Parse("{}").RootElement,
            AccessClass: ToolAccessClass.ReadOnly,
            IsSystem: true);

    private sealed class PassThroughStep : IGroundingStep
    {
        public Task<SdkGroundingResult> RunAsync(string input, CancellationToken cancellationToken) =>
            Task.FromResult(new SdkGroundingResult { Context = new RunContext(), Notes = [] });
    }

    private sealed class FixedProvider(string providerType, string block) : IGroundingProvider
    {
        public string ProviderType => providerType;

        public Task<DomainGroundingResult> EnrichAsync(string briefingText, GroundingProviderProfile profile, Guid runId, CancellationToken ct) =>
            Task.FromResult(new DomainGroundingResult(profile.Name, block, [], 0, null));
    }

    private sealed class ThrowingProvider(string providerType, string message) : IGroundingProvider
    {
        public string ProviderType => providerType;

        public Task<DomainGroundingResult> EnrichAsync(string briefingText, GroundingProviderProfile profile, Guid runId, CancellationToken ct) =>
            throw new InvalidOperationException(message);
    }

    private sealed class Factory(IEnumerable<IGroundingProvider> providers) : IGroundingProviderFactory
    {
        private readonly Dictionary<string, IGroundingProvider> _map = providers.ToDictionary(p => p.ProviderType);

        public IGroundingProvider Create(string providerType) =>
            _map.TryGetValue(providerType, out var p) ? p : throw new InvalidOperationException($"No provider for type '{providerType}'");

        public bool IsRegistered(string providerType) => _map.ContainsKey(providerType);

        public IReadOnlyCollection<string> RegisteredTypes => _map.Keys;
    }

    private sealed class CountingToolExecutor : IToolExecutor
    {
        public int Calls { get; private set; }

        public Task<ToolExecutionResult> ExecuteAsync(ToolDefinition tool, string inputJson, ToolInvocationContext ctx, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ToolExecutionResult("tool output", null, null));
        }
    }

}

/// <summary>Collects reported warnings in memory.</summary>
internal sealed class RecordingWarningSink : IRunWarningSink
{
    private readonly List<RunWarningEntity> _warnings = [];

    public IReadOnlyList<RunWarningEntity> Warnings
    {
        get { lock (_warnings) return _warnings.ToList(); }
    }

    public Task ReportAsync(Guid runId, string source, string code, string message, int? iteration = null, CancellationToken ct = default)
    {
        lock (_warnings)
        {
            _warnings.Add(new RunWarningEntity
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                Iteration = iteration,
                Source = source,
                Code = code,
                Message = message,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        return Task.CompletedTask;
    }
}
