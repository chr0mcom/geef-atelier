namespace Geef.Atelier.Core.Domain.Llm;

public sealed record LlmBinding(
    string Provider,
    string Model,
    int MaxTokens,
    double? Temperature = null)
{
    public static LlmBinding Default(string provider, string model, int maxTokens)
        => new(provider, model, maxTokens, null);
}
