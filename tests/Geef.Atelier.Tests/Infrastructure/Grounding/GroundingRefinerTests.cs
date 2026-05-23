using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Llm;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tests.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

public sealed class GroundingRefinerTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static GroundingResult MakeRaw(int citationCount = 2) =>
        new(ProviderName: "tavily-test",
            EnrichedContext: "raw context",
            Citations: Enumerable.Range(0, citationCount)
                .Select(i => new SourceCitation(
                    Title: $"Source {i}",
                    Url: $"https://example.com/{i}",
                    Snippet: $"Snippet {i}",
                    DocumentReference: null,
                    RelevanceScore: 0.8 - i * 0.1))
                .ToList(),
            TokensOrCreditsUsed: 5,
            CostEur: 0.01m);

    private static GroundingRefinementConfig MakeConfig(
        GroundingRefinementMode mode = GroundingRefinementMode.Filter,
        string provider = "openrouter",
        string model = "gpt-4o",
        string? instructions = null) =>
        new(Binding: new LlmBinding(provider, model, 2048),
            Mode: mode,
            Instructions: instructions);

    private static GroundingRefiner BuildRefiner(
        ILlmClient llmClient,
        IGroundingActorCostRepository? costRepo = null,
        IProviderService? providerService = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(providerService ?? new ActiveProviderService());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new GroundingRefiner(
            new TestLlmClientResolver(llmClient),
            new ZeroPricingCatalog(),
            costRepo ?? new SpyGroundingActorCostRepository(),
            scopeFactory,
            NullLogger<GroundingRefiner>.Instance);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RefineAsync_InactiveProvider_ReturnsSkippedWithRawCitations()
    {
        var client = new ToolCallClient(BuildFilterResponse([new(0, true, "ok"), new(1, false, "irrelevant")]));
        var refiner = BuildRefiner(client, providerService: new InactiveProviderService());
        var raw = MakeRaw(2);
        var config = MakeConfig();

        var (refined, outcome) = await refiner.RefineAsync(raw, "briefing", config, "tavily-test", Guid.NewGuid(), default);

        Assert.True(outcome.WasSkipped);
        Assert.Contains("not found or inactive", outcome.SkipReason);
        Assert.Equal(raw.Citations.Count, outcome.RefinedCitations.Count);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public async Task RefineAsync_FilterMode_RetainsAndDropsCorrectly()
    {
        // Source 0: keep=true, Source 1: keep=false (reason "irrelevant")
        var toolJson = BuildFilterResponse([
            new(0, true, "relevant"),
            new(1, false, "irrelevant")
        ]);
        var client = new ToolCallClient(toolJson);
        var costRepo = new SpyGroundingActorCostRepository();
        var refiner = BuildRefiner(client, costRepo);
        var raw = MakeRaw(2);
        var config = MakeConfig(GroundingRefinementMode.Filter);

        var (_, outcome) = await refiner.RefineAsync(raw, "briefing", config, "tavily-test", Guid.NewGuid(), default);

        Assert.False(outcome.WasSkipped);
        Assert.Single(outcome.RefinedCitations);
        Assert.Equal("Source 0", outcome.RefinedCitations[0].Title);
        Assert.Single(outcome.DroppedCitations);
        Assert.Equal("Source 1", outcome.DroppedCitations[0].Original.Title);
        Assert.Equal("irrelevant", outcome.DroppedCitations[0].Reason);
    }

    [Fact]
    public async Task RefineAsync_SynthesizeMode_ReturnsSynthesizedText()
    {
        var toolJson = """
            {
                "synthesized_text": "Combined synthesis of sources.",
                "referenced_indices": [0, 1]
            }
            """;
        var client = new ToolCallClient(toolJson);
        var refiner = BuildRefiner(client);
        var raw = MakeRaw(2);
        var config = MakeConfig(GroundingRefinementMode.Synthesize);

        var (_, outcome) = await refiner.RefineAsync(raw, "briefing", config, "tavily-test", Guid.NewGuid(), default);

        Assert.False(outcome.WasSkipped);
        Assert.Equal("Combined synthesis of sources.", outcome.SynthesizedText);
        Assert.Equal(2, outcome.RefinedCitations.Count); // all citations retained in synthesize mode
    }

    [Fact]
    public async Task RefineAsync_LlmCallFails_ReturnsSkippedWithRawCitations()
    {
        var throwingClient = ThrowingLlmClient.GenericError("LLM unavailable");
        var refiner = BuildRefiner(throwingClient);
        var raw = MakeRaw(2);
        var config = MakeConfig();

        var (_, outcome) = await refiner.RefineAsync(raw, "briefing", config, "tavily-test", Guid.NewGuid(), default);

        Assert.True(outcome.WasSkipped);
        Assert.Equal(2, outcome.RefinedCitations.Count);
    }

    [Fact]
    public async Task RefineAsync_SourcesCappedAt20()
    {
        // Provide 25 citations — the refiner should cap at 20 before calling the LLM.
        var capturingClient = new CapturingLlmClient(
            BuildFilterResponse(Enumerable.Range(0, 20).Select(i => new FilterDecision(i, true, "ok")).ToArray()));
        var refiner = BuildRefiner(capturingClient);
        var raw = MakeRaw(25);
        var config = MakeConfig(GroundingRefinementMode.Filter);

        await refiner.RefineAsync(raw, "briefing", config, "tavily-test", Guid.NewGuid(), default);

        // The user prompt should mention exactly 20 sources to review, not 25.
        Assert.NotNull(capturingClient.LastRequest);
        Assert.Contains("20 total", capturingClient.LastRequest.UserPrompt);
        Assert.DoesNotContain("25 total", capturingClient.LastRequest.UserPrompt);
    }

    [Fact]
    public async Task RefineAsync_Success_WritesCostRecord()
    {
        var toolJson = BuildFilterResponse([new(0, true, "relevant"), new(1, true, "relevant")]);
        var client = new ToolCallClient(toolJson);
        var costRepo = new SpyGroundingActorCostRepository();
        var runId = Guid.NewGuid();
        var refiner = BuildRefiner(client, costRepo);

        await refiner.RefineAsync(MakeRaw(2), "briefing", MakeConfig(), "tavily-test", runId, default);

        Assert.Single(costRepo.Recorded);
        var recorded = costRepo.Recorded[0];
        Assert.Equal(runId, recorded.RunId);
        Assert.Equal("tavily-test", recorded.GroundingProviderName);
        Assert.Equal("GroundingRefiner", recorded.ActorName);
        Assert.Equal("openrouter", recorded.ProviderName);
    }

    [Fact]
    public async Task RefineAsync_CustomInstructions_AppendedToPrompt()
    {
        var toolJson = BuildFilterResponse([new(0, true, "ok")]);
        var capturingClient = new CapturingLlmClient(toolJson);
        var refiner = BuildRefiner(capturingClient);
        var config = MakeConfig(instructions: "Only keep sources from 2025 or newer.");

        await refiner.RefineAsync(MakeRaw(1), "briefing", config, "tavily-test", Guid.NewGuid(), default);

        Assert.NotNull(capturingClient.LastRequest);
        Assert.Contains("Only keep sources from 2025 or newer.", capturingClient.LastRequest.SystemPrompt);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static string BuildFilterResponse(FilterDecision[] decisions)
    {
        var items = string.Join(",\n", decisions.Select(d =>
            $$$"""{"index": {{{d.Index}}}, "keep": {{{(d.Keep ? "true" : "false")}}}, "reason": "{{{d.Reason}}}"}"""));
        return $$"""{"sources": [{{items}}]}""";
    }

    private sealed record FilterDecision(int Index, bool Keep, string Reason);

    // -------------------------------------------------------------------------
    // Fakes
    // -------------------------------------------------------------------------

    /// <summary>Returns a deterministic tool_calls response with the given JSON.</summary>
    private sealed class ToolCallClient(string toolArgumentsJson) : ILlmClient
    {
        public int CallCount { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new LlmResponse
            {
                Text = string.Empty,
                ToolName = "submit_refinement",
                ToolArgumentsJson = toolArgumentsJson,
                FinishReason = "tool_calls",
                TokenUsage = new LlmTokenUsage { InputTokens = 50, OutputTokens = 30 },
            });
        }
    }

    /// <summary>Captures the last LLM request for assertion purposes.</summary>
    private sealed class CapturingLlmClient(string toolArgumentsJson) : ILlmClient
    {
        public LlmRequest? LastRequest { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new LlmResponse
            {
                Text = string.Empty,
                ToolName = "submit_refinement",
                ToolArgumentsJson = toolArgumentsJson,
                FinishReason = "tool_calls",
                TokenUsage = new LlmTokenUsage { InputTokens = 50, OutputTokens = 30 },
            });
        }
    }

    /// <summary>Provider service that always returns an active provider.</summary>
    private sealed class ActiveProviderService : IProviderService
    {
        private static Provider MakeActive(string name) => new(
            Name: name,
            DisplayName: name,
            Description: "active",
            Type: ProviderType.Http,
            Settings: [],
            IsSystem: false,
            IsActive: true,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        public Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Provider>>([]);

        public Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<Provider?>(MakeActive(name));

        public Task<Provider> CreateCustomAsync(Provider provider, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Provider> UpdateCustomAsync(string name, Provider provider, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteCustomAsync(string name, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SetActiveAsync(string name, bool isActive, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ConnectionTestResult> TestConnectionAsync(string name, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>Provider service that always returns an inactive provider.</summary>
    private sealed class InactiveProviderService : IProviderService
    {
        private static Provider MakeInactive(string name) => new(
            Name: name,
            DisplayName: name,
            Description: "inactive",
            Type: ProviderType.Http,
            Settings: [],
            IsSystem: false,
            IsActive: false,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        public Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Provider>>([]);

        public Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<Provider?>(MakeInactive(name));

        public Task<Provider> CreateCustomAsync(Provider provider, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Provider> UpdateCustomAsync(string name, Provider provider, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteCustomAsync(string name, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SetActiveAsync(string name, bool isActive, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ConnectionTestResult> TestConnectionAsync(string name, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>Spy repository that records every cost written.</summary>
    private sealed class SpyGroundingActorCostRepository : IGroundingActorCostRepository
    {
        public List<GroundingActorCost> Recorded { get; } = [];

        public Task AddAsync(GroundingActorCost cost, CancellationToken ct)
        {
            Recorded.Add(cost);
            return Task.CompletedTask;
        }
    }

    private sealed class ZeroPricingCatalog : IPricingCatalog
    {
        public decimal? CalculateCostEur(string modelName, int inputTokens, int outputTokens, string? providerName = null) => 0m;
    }
}
