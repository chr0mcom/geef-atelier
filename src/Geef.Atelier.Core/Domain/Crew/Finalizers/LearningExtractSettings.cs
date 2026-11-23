namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>
/// Typed settings for <see cref="FinalizerType.LearningExtract"/> profiles. Without explicit provider/model the
/// extraction runs on the model-matrix <see cref="Models.ModelSlot.TransformCondense"/> primary (source-faithful condensing).
/// </summary>
public sealed record LearningExtractSettings(
    string Provider,
    string Model,
    int MaxTokens,
    int MinIterations,
    bool RequireMajorFinding)
{
    /// <summary>Settings key of the explicit extraction provider.</summary>
    public const string KeyProvider = "provider";

    /// <summary>Settings key of the explicit extraction model.</summary>
    public const string KeyModel = "model";

    public static LearningExtractSettings From(Dictionary<string, string> s) => new(
        Provider:            s.GetValueOrDefault(KeyProvider, Models.ModelMatrix.Primary(Models.ModelSlot.TransformCondense).Provider),
        Model:               s.GetValueOrDefault(KeyModel, Models.ModelMatrix.Primary(Models.ModelSlot.TransformCondense).Model),
        MaxTokens:           s.TryGetValue("maxTokens", out var t) && int.TryParse(t, out var n) ? n : 60000,
        MinIterations:       s.TryGetValue("minIterations", out var mi) && int.TryParse(mi, out var miv) ? miv : 2,
        RequireMajorFinding: s.TryGetValue("requireMajorFinding", out var r) && bool.TryParse(r, out var rb) ? rb : false);
}
