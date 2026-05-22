namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>Typed settings for <see cref="FinalizerType.LearningExtract"/> profiles.</summary>
public sealed record LearningExtractSettings(
    string Provider,
    string Model,
    int MaxTokens,
    int MinIterations,
    bool RequireMajorFinding)
{
    public static LearningExtractSettings From(Dictionary<string, string> s) => new(
        Provider:            s.GetValueOrDefault("provider", "openrouter"),
        Model:               s.GetValueOrDefault("model", "openai/gpt-4.1"),
        MaxTokens:           s.TryGetValue("maxTokens", out var t) && int.TryParse(t, out var n) ? n : 2048,
        MinIterations:       s.TryGetValue("minIterations", out var mi) && int.TryParse(mi, out var miv) ? miv : 2,
        RequireMajorFinding: s.TryGetValue("requireMajorFinding", out var r) && bool.TryParse(r, out var rb) ? rb : false);
}
