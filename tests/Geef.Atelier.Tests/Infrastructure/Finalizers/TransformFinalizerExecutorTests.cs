using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Finalizers;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tests.Llm;
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

    private static TransformFinalizerExecutor BuildExecutor(ILlmClient client) =>
        new(
            new TestLlmClientResolver(client),
            new ZeroPricingCatalog(),
            NullLogger<TransformFinalizerExecutor>.Instance);

    [Fact]
    public async Task Execute_WithValidSystemPrompt_ReturnsUpdatedText()
    {
        var fakeClient = new ConstantTextClient("Transformed text content.");
        var executor = BuildExecutor(fakeClient);

        var result = await executor.ExecuteAsync(
            ProfileWith("You are a text transformer. Improve the text."),
            MakeContext(), default);

        Assert.Equal("Transformed text content.", result.UpdatedText);
        Assert.Null(result.Artifact);
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
        public decimal? CalculateCostEur(string modelName, int inputTokens, int outputTokens) => 0m;
    }
}
