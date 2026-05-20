using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Tests.Domain.Grounding;

public sealed class GroundingProviderTypesTests
{
    [Fact]
    public void Tavily_HasCorrectStringValue()
    {
        Assert.Equal("tavily", GroundingProviderTypes.Tavily);
    }

    [Fact]
    public void VectorStore_HasCorrectStringValue()
    {
        Assert.Equal("vector-store", GroundingProviderTypes.VectorStore);
    }

    [Fact]
    public void StaticContext_HasCorrectStringValue()
    {
        Assert.Equal("static-context", GroundingProviderTypes.StaticContext);
    }

    [Fact]
    public void UrlFetch_HasCorrectStringValue()
    {
        Assert.Equal("url-fetch", GroundingProviderTypes.UrlFetch);
    }

    [Fact]
    public void NewsSearch_HasCorrectStringValue()
    {
        Assert.Equal("news-search", GroundingProviderTypes.NewsSearch);
    }
}
