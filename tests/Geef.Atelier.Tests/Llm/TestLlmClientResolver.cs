using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Llm;

/// <summary>
/// Always returns the same client, model, and maxTokens regardless of actor name.
/// Used in E2E tests to inject a fake LLM client without configuring a full multi-provider setup.
/// </summary>
internal sealed class TestLlmClientResolver(
    ILlmClient client,
    string model = "test-model",
    int maxTokens = 4096) : ILlmClientResolver
{
    public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName) =>
        (client, model, maxTokens);
}
