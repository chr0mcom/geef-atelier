using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Llm;

/// <summary>
/// Always returns the same client regardless of provider.
/// Used in E2E tests to inject a fake LLM client without configuring a full multi-provider setup.
/// </summary>
internal sealed class TestLlmClientResolver(
    ILlmClient client,
    int maxTokens = 4096) : ILlmClientResolver
{
    public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string profileModel, int? profileMaxTokens) =>
        (client, profileModel, profileMaxTokens ?? maxTokens);

    public bool SupportsStructuredOutputs(string providerName) => true;
}
