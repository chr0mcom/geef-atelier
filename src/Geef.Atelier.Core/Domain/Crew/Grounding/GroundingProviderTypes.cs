namespace Geef.Atelier.Core.Domain.Crew.Grounding;

/// <summary>Known grounding provider type discriminator strings.</summary>
public static class GroundingProviderTypes
{
    public const string Tavily = "tavily";
    public const string VectorStore = "vector-store";
    public const string StaticContext = "static-context";
    public const string UrlFetch = "url-fetch";

    /// <summary>Retired in stage 8c (D-089): recent news via the Tavily news topic. Profiles of this type are skipped with a warning.</summary>
    [Obsolete("Retired in stage 8c (D-089); no provider is registered for this type any more.")]
    public const string NewsSearch = "news-search";

    /// <summary>Retired in stage 8c (D-089): papers from arXiv, Semantic Scholar or OpenAlex. Profiles of this type are skipped with a warning.</summary>
    [Obsolete("Retired in stage 8c (D-089); no provider is registered for this type any more.")]
    public const string AcademicSearch = "academic-search";

    /// <summary>Retired in stage 8c (D-089): a configurable HTTP endpoint. Profiles of this type are skipped with a warning.</summary>
    [Obsolete("Retired in stage 8c (D-089); no provider is registered for this type any more.")]
    public const string RestApi = "rest-api";

    public const string LearningRetrieval = "learning-retrieval";
    public const string CrewCatalog = "crew-catalog";
    public const string ToolCatalog = "tool-catalog";
    public const string PackCatalog = "pack-catalog";
    public const string OrderSources = "order-sources";

    /// <summary>
    /// Types whose providers were removed in stage 8c (D-089). Stored profiles and run snapshots may still name them: they stay
    /// readable, a run skips them with a warning, and they can no longer be saved.
    /// </summary>
    public static IReadOnlySet<string> Retired { get; } =
        new HashSet<string>(["news-search", "academic-search", "rest-api"], StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="providerType"/> is one of the <see cref="Retired"/> types.</summary>
    public static bool IsRetiredGroundingType(this string? providerType) =>
        providerType is not null && Retired.Contains(providerType);
}
