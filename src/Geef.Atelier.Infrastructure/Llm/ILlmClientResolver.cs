namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>Resolves the correct <see cref="ILlmClient"/>, model, and token limit for profile-defined provider and model pairs.</summary>
public interface ILlmClientResolver
{
    /// <summary>
    /// Returns the client, model, and effective max-tokens for a profile-defined provider/model pair.
    /// Falls back to <c>LlmOptions.DefaultMaxTokens</c> when <paramref name="maxTokens"/> is null.
    /// Throws <see cref="InvalidOperationException"/> when the provider is not configured.
    /// </summary>
    (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens);

    /// <summary>
    /// Returns <see langword="true"/> when the named provider supports OpenAI <c>response_format</c>
    /// structured outputs (json_object / json_schema). Returns <see langword="false"/> for the
    /// OpenAI Responses path (which does not accept response_format here) and unknown providers,
    /// so callers can fall back to forced tool-calling.
    /// </summary>
    bool SupportsStructuredOutputs(string providerName);
}
