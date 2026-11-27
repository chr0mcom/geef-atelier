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

    [Theory]
    [InlineData("news-search")]
    [InlineData("academic-search")]
    [InlineData("rest-api")]
    [InlineData("REST-API")]
    public void The_types_removed_in_stage_8c_are_retired(string type)
    {
        Assert.True(type.IsRetiredGroundingType());
    }

    [Theory]
    [InlineData("tavily")]
    [InlineData("url-fetch")]
    [InlineData("vector-store")]
    [InlineData(null)]
    public void The_remaining_types_are_not_retired(string? type)
    {
        Assert.False(type.IsRetiredGroundingType());
    }
}
