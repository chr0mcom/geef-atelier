namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>Resolves the correct <see cref="ILlmClient"/>, model, and token limit for pipeline actors and profiles.</summary>
public interface ILlmClientResolver
{
    /// <summary>
    /// Returns the client, model name, and max-tokens for the given actor.
    /// Throws <see cref="InvalidOperationException"/> when the actor or its provider is not configured.
    /// </summary>
    (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName);

    /// <summary>
    /// Returns the client, model, and effective max-tokens for a profile-defined provider/model pair.
    /// Falls back to <c>LlmOptions.DefaultMaxTokens</c> when <paramref name="maxTokens"/> is null.
    /// Throws <see cref="InvalidOperationException"/> when the provider is not configured.
    /// </summary>
    (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens);

    /// <summary>
    /// Returns <see langword="true"/> when the named provider supports agentic tool use
    /// (multi-turn tool calls / function calling).  Returns <see langword="false"/> when the
    /// provider is unknown or not yet loaded.
    /// </summary>
    bool SupportsAgenticTools(string providerName);
}
