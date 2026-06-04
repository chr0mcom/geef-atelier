namespace Geef.Atelier.Core.Domain.Crew.Grounding;

/// <summary>Known grounding provider type discriminator strings.</summary>
public static class GroundingProviderTypes
{
    public const string Tavily = "tavily";
    public const string VectorStore = "vector-store";
    public const string StaticContext = "static-context";
    public const string UrlFetch = "url-fetch";
    public const string NewsSearch = "news-search";
    public const string AcademicSearch = "academic-search";
    public const string RestApi = "rest-api";
    public const string LearningRetrieval = "learning-retrieval";
    public const string CrewCatalog = "crew-catalog";
}
