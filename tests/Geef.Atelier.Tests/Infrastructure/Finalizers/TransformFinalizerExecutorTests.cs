using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Infrastructure.Finalizers;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Finalizers;

public sealed class TransformFinalizerExecutorTests
{
    private static FinalizerProfile ProfileWith(string systemPrompt, string provider = "codex-cli",
        string model = "gpt-5.5", int maxTokens = 4096) => new(
        Name: "transform-test",
        DisplayName: "Transform Test",
        Description: "test",
        FinalizerType: FinalizerType.Transform,
        Settings: new TransformSettings(systemPrompt, provider, model, maxTokens).ToDict(),
        IsSystem: false);

    private static FinalizerExecutionContext MakeContext(string text = "Original text content.") => new(
        RunId: Guid.NewGuid(),
        TemplateName: "test-template",
        FinalText: text,
        CurrentText: text,
        RunCompletedAt: DateTimeOffset.UtcNow);

    private static TransformFinalizerExecutor BuildExecutor(ILlmClient client,
        IProviderService? providerService = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(providerService ?? new FakeProviderService());
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new(
            new TestLlmClientResolver(client),
            new ZeroPricingCatalog(),
            scopeFactory,
            NullLogger<TransformFinalizerExecutor>.Instance);
    }

    [Fact]
    public async Task Execute_WithValidSystemPrompt_ReturnsUpdatedText()
    {
        var fakeClient = new ConstantTextClient("Transformed text content.");
        var executor = BuildExecutor(fakeClient);

        var result = await executor.ExecuteAsync(
            ProfileWith("You are a text transformer. Improve the text."),
            MakeContext(), default);

        Assert.Equal("Transformed text content.", result.UpdatedText);
    }

    [Fact]
    public async Task Execute_WithChangedText_ProducesDiffArtifact()
    {
        var fakeClient = new ConstantTextClient("Para one.\n\nRevised paragraph.\n\nPara three.");
        var executor = BuildExecutor(fakeClient);

        var result = await executor.ExecuteAsync(
            ProfileWith("Transform prompt."),
            MakeContext("Para one.\n\nOriginal paragraph.\n\nPara three."),
            default);

        Assert.NotNull(result.Artifact);
        Assert.Equal("transform-diff", result.Artifact!.StorageUri);
        Assert.Contains("\"delete\"", result.Artifact.StatusMessage);
        Assert.Contains("\"insert\"", result.Artifact.StatusMessage);
    }

    [Fact]
    public async Task Execute_WithUnchangedText_ProducesNoChangesDiffArtifact()
    {
        const string text = "Para one.\n\nPara two.";
        var fakeClient = new ConstantTextClient(text);
        var executor = BuildExecutor(fakeClient);

        var result = await executor.ExecuteAsync(
            ProfileWith("Transform prompt."), MakeContext(text), default);

        Assert.NotNull(result.Artifact);
        Assert.Equal("no-changes", result.Artifact!.StorageUri);
    }

    [Fact]
    public async Task Execute_WhenLlmReturnsEmpty_PreservesOriginalAndProducesWarningArtifact()
    {
        var fakeClient = new ConstantTextClient(string.Empty);
        var executor = BuildExecutor(fakeClient);

        var result = await executor.ExecuteAsync(
            ProfileWith("Transform prompt."), MakeContext("Original text."), default);

        Assert.Null(result.UpdatedText);
        Assert.NotNull(result.Artifact);
        Assert.Equal("warning", result.Artifact!.StorageUri);
    }

    [Fact]
    public async Task Execute_WithEmptySystemPrompt_SkipsAndReturnsNull()
    {
        var fakeClient = new ConstantTextClient("Should not be called.");
        var executor = BuildExecutor(fakeClient);

        var result = await executor.ExecuteAsync(
            ProfileWith(string.Empty), MakeContext(), default);

        Assert.Null(result.UpdatedText);
        Assert.Null(result.Artifact);
        Assert.Equal(0, fakeClient.CallCount);
    }

    [Fact]
    public async Task Execute_WhenLlmThrows_ProducesStatusArtifact()
    {
        var throwingClient = ThrowingLlmClient.GenericError("LLM unavailable");
        var executor = BuildExecutor(throwingClient);

        var result = await executor.ExecuteAsync(
            ProfileWith("Some prompt."), MakeContext(), default);

        Assert.Null(result.UpdatedText);
        Assert.NotNull(result.Artifact);
        Assert.Equal(ArtifactType.Status, result.Artifact!.ArtifactType);
        Assert.Contains("Transform failed", result.Artifact.StatusMessage);
    }

    [Fact]
    public async Task Execute_RecordsTokenCounts()
    {
        var fakeClient = new ConstantTextClient("Output.", inputTokens: 100, outputTokens: 50);
        var executor = BuildExecutor(fakeClient);

        var result = await executor.ExecuteAsync(
            ProfileWith("Some prompt."), MakeContext(), default);

        Assert.Equal(100, result.InputTokens);
        Assert.Equal(50, result.OutputTokens);
    }

    [Fact]
    public async Task Execute_WhenProviderNotFound_ProducesStatusArtifactWithoutCallingLlm()
    {
        var fakeClient = new ConstantTextClient("Should not be called.");
        var executor = BuildExecutor(fakeClient, new NullProviderService());

        var result = await executor.ExecuteAsync(
            ProfileWith("Some prompt.", provider: "nonexistent-provider"),
            MakeContext(), default);

        Assert.Null(result.UpdatedText);
        Assert.NotNull(result.Artifact);
        Assert.Equal(ArtifactType.Status, result.Artifact!.ArtifactType);
        Assert.Contains("not found or inactive", result.Artifact.StatusMessage);
        Assert.Equal(0, fakeClient.CallCount);
    }

    [Fact]
    public async Task Execute_WhenProviderInactive_ProducesStatusArtifactWithoutCallingLlm()
    {
        var fakeClient = new ConstantTextClient("Should not be called.");
        var executor = BuildExecutor(fakeClient, new InactiveProviderService());

        var result = await executor.ExecuteAsync(
            ProfileWith("Some prompt.", provider: "codex-cli"),
            MakeContext(), default);

        Assert.Null(result.UpdatedText);
        Assert.NotNull(result.Artifact);
        Assert.Equal(ArtifactType.Status, result.Artifact!.ArtifactType);
        Assert.Contains("not found or inactive", result.Artifact.StatusMessage);
        Assert.Equal(0, fakeClient.CallCount);
    }

    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class ConstantTextClient(
        string text,
        int inputTokens = 10,
        int outputTokens = 20) : ILlmClient
    {
        public int CallCount { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new LlmResponse
            {
                Text = text,
                FinishReason = "stop",
                TokenUsage = new LlmTokenUsage { InputTokens = inputTokens, OutputTokens = outputTokens },
            });
        }
    }

    private sealed class ZeroPricingCatalog : IPricingCatalog
    {
        public decimal? CalculateCostEur(string modelName, int inputTokens, int outputTokens, string? providerName = null, int cachedInputTokens = 0) => 0m;
    }

    /// <summary>Provider service that returns null for every lookup (simulates unknown provider).</summary>
    private sealed class NullProviderService : IProviderService
    {
        public Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Provider>>([]);

        public Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult<Provider?>(null);

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

    /// <summary>Provider service that returns a provider with <see cref="Provider.IsActive"/> = false.</summary>
    private sealed class InactiveProviderService : IProviderService
    {
        private static Provider MakeInactive(string name) => new(
            Name: name,
            DisplayName: name,
            Description: "inactive",
            Type: Geef.Atelier.Core.Domain.Providers.ProviderType.Http,
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
}
