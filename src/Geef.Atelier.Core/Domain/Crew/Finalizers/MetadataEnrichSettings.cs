namespace Geef.Atelier.Core.Domain.Crew.Finalizers;

/// <summary>
/// Typed wrapper over the <c>Settings</c> dictionary for <see cref="FinalizerType.MetadataEnrich"/> profiles.
/// Valid <c>EnricherType</c> values: <c>front-matter</c>, <c>word-count-footer</c>, <c>reading-level</c>.
/// </summary>
public sealed record MetadataEnrichSettings(string EnricherType)
{
    public const string KeyEnricherType = "EnricherType";

    public const string FrontMatter = "front-matter";
    public const string WordCountFooter = "word-count-footer";
    public const string ReadingLevel = "reading-level";

    public static MetadataEnrichSettings From(Dictionary<string, string> settings) =>
        new(settings.GetValueOrDefault(KeyEnricherType, FrontMatter));

    public Dictionary<string, string> ToDict() =>
        new() { [KeyEnricherType] = EnricherType };
}
