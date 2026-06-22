using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

public sealed class LlmGroundingQueryExtractorTests
{
    [Fact]
    public async Task ExtractAsync_ReturnsNoSearch_WhenModelRespondsNoSearch()
    {
        var extractor = Build("NO_SEARCH");

        var result = await extractor.ExtractAsync("Prove that sqrt(2) is irrational.", CancellationToken.None);

        Assert.False(result.ShouldSearch);
        Assert.Equal(string.Empty, result.Query);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsNoSearch_WhenModelPrefixesNoSearchTokenCaseInsensitively()
    {
        var extractor = Build("no_search — this is a pure reasoning task");

        var result = await extractor.ExtractAsync("A logic riddle.", CancellationToken.None);

        Assert.False(result.ShouldSearch);
    }

    [Fact]
    public async Task ExtractAsync_ReturnsFirstLine_StrippedOfQuotes()
    {
        var extractor = Build("\"chromatic number of the plane Hadwiger-Nelson\"\nextra explanation line");

        var result = await extractor.ExtractAsync("long briefing", CancellationToken.None);

        Assert.True(result.ShouldSearch);
        Assert.Equal("chromatic number of the plane Hadwiger-Nelson", result.Query);
    }

    [Fact]
    public async Task ExtractAsync_FallsBackToBriefing_WhenResolverThrows()
    {
        var extractor = new LlmGroundingQueryExtractor(
            new ThrowingResolver(),
            NullLogger<LlmGroundingQueryExtractor>.Instance);

        var result = await extractor.ExtractAsync("the raw briefing text", CancellationToken.None);

        Assert.True(result.ShouldSearch);
        Assert.Equal("the raw briefing text", result.Query);
    }

    [Fact]
    public async Task ExtractAsync_FallsBackToBriefing_WhenModelReturnsBlank()
    {
        var extractor = Build("   ");

        var result = await extractor.ExtractAsync("briefing fallback", CancellationToken.None);

        Assert.True(result.ShouldSearch);
        Assert.Equal("briefing fallback", result.Query);
    }

    // --- helpers ---

    private static LlmGroundingQueryExtractor Build(string responseText) =>
        new(new StubResolver(responseText), NullLogger<LlmGroundingQueryExtractor>.Instance);

    private sealed class StubClient(string text) : ILlmClient
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new LlmResponse
            {
                Text         = text,
                TokenUsage   = new LlmTokenUsage { InputTokens = 1, OutputTokens = 1 },
                FinishReason = "stop",
            });
    }

    private sealed class StubResolver(string text) : ILlmClientResolver
    {
        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName) =>
            (new StubClient(text), "test-model", 256);

        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens) =>
            throw new NotSupportedException();

        public bool SupportsAgenticTools(string providerName) => true;
        public bool SupportsStructuredOutputs(string providerName) => true;
    }

    private sealed class ThrowingResolver : ILlmClientResolver
    {
        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName) =>
            throw new InvalidOperationException("Actor 'GroundingQueryExtractor' is not configured in Llm:Actors.");

        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens) =>
            throw new NotSupportedException();

        public bool SupportsAgenticTools(string providerName) => false;
        public bool SupportsStructuredOutputs(string providerName) => false;
    }
}
