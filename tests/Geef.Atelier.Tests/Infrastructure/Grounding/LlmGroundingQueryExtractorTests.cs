using Geef.Atelier.Application.Crew.Grounding;
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
    public async Task ExtractAsync_SearchesNothing_WhenResolverThrows()
    {
        var extractor = new LlmGroundingQueryExtractor(
            new ThrowingResolver(),
            NullLogger<LlmGroundingQueryExtractor>.Instance);

        var result = await extractor.ExtractAsync("the raw briefing text", CancellationToken.None);

        Assert.Equal(GroundingQuery.NotFormed, result);
        Assert.False(result.ShouldSearch);
        Assert.True(result.Failed);
        Assert.Equal(string.Empty, result.Query);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData("\"  \"\nexplanation")]
    public async Task ExtractAsync_SearchesNothing_WhenModelReturnsNoQuery(string answer)
    {
        var extractor = Build(answer);

        var result = await extractor.ExtractAsync("briefing that must never be the query", CancellationToken.None);

        Assert.Equal(GroundingQuery.NotFormed, result);
    }

    [Fact]
    public async Task ExtractAsync_SearchesNothing_WhenTheModelCallFails()
    {
        var extractor = new LlmGroundingQueryExtractor(new StubResolver(new HttpRequestException("gateway down")),
            NullLogger<LlmGroundingQueryExtractor>.Instance);

        var result = await extractor.ExtractAsync("briefing that must never be the query", CancellationToken.None);

        Assert.Equal(GroundingQuery.NotFormed, result);
    }

    // --- helpers ---

    private static LlmGroundingQueryExtractor Build(string responseText) =>
        new(new StubResolver(responseText), NullLogger<LlmGroundingQueryExtractor>.Instance);

    [Fact]
    public async Task ExtractAsync_UsesTheMatrixHelperModel_WhenTheActorIsNotConfigured()
    {
        var resolver = new UnconfiguredActorResolver("climate policy 2026");
        var extractor = new LlmGroundingQueryExtractor(resolver, NullLogger<LlmGroundingQueryExtractor>.Instance);

        var result = await extractor.ExtractAsync("Write about climate policy.", CancellationToken.None);

        Assert.Equal("climate policy 2026", result.Query);
        var helper = Geef.Atelier.Core.Domain.Crew.Models.ModelMatrix.Primary(Geef.Atelier.Core.Domain.Crew.Models.ModelSlot.Helper);
        Assert.Equal((helper.Provider, helper.Model, (int?)256), resolver.Requested);
    }

    private sealed class UnconfiguredActorResolver(string text) : ILlmClientResolver
    {
        public (string Provider, string Model, int? MaxTokens)? Requested { get; private set; }

        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName) =>
            throw new InvalidOperationException("Actor 'GroundingQueryExtractor' is not configured in Llm:Actors.");

        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
        {
            Requested = (provider, model, maxTokens);
            return (new StubClient(text), model, maxTokens ?? 256);
        }

        public bool SupportsAgenticTools(string providerName) => true;
        public bool SupportsStructuredOutputs(string providerName) => true;
    }

    private sealed class StubClient(string text, Exception? failure = null) : ILlmClient
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            failure is not null ? Task.FromException<LlmResponse>(failure) : Task.FromResult(new LlmResponse
            {
                Text         = text,
                TokenUsage   = new LlmTokenUsage { InputTokens = 1, OutputTokens = 1 },
                FinishReason = "stop",
            });
    }

    private sealed class StubResolver(string text, Exception? failure = null) : ILlmClientResolver
    {
        public StubResolver(Exception failure) : this(string.Empty, failure)
        {
        }

        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName) =>
            (new StubClient(text, failure), "test-model", 256);

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
