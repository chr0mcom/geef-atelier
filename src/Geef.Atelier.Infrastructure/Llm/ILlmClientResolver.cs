namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>Resolves the correct <see cref="ILlmClient"/>, model, and token limit for a named pipeline actor.</summary>
public interface ILlmClientResolver
{
    /// <summary>
    /// Returns the client, model name, and max-tokens for the given actor.
    /// Throws <see cref="InvalidOperationException"/> when the actor or its provider is not configured.
    /// </summary>
    (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName);
}
