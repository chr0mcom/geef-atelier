using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Tests.Domain.Grounding;

public sealed class SourceCitationPublishedDateTests
{
    [Fact]
    public void SourceCitation_WithoutPublishedDate_HasNullPublishedDate()
    {
        var citation = new SourceCitation(
            Title: "Some Article",
            Url: "https://example.com",
            Snippet: "A short snippet.",
            DocumentReference: null,
            RelevanceScore: 0.9);

        Assert.Null(citation.PublishedDate);
    }

    [Fact]
    public void SourceCitation_WithPublishedDate_ReturnsCorrectDate()
    {
        var date = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);

        var citation = new SourceCitation(
            Title: "News Article",
            Url: "https://news.example.com",
            Snippet: "Breaking news snippet.",
            DocumentReference: null,
            RelevanceScore: 0.85,
            PublishedDate: date);

        Assert.Equal(date, citation.PublishedDate);
    }

    [Fact]
    public void SourceCitation_CanBeCreatedWithoutNamedPublishedDateParameter()
    {
        // Verifies backwards compatibility: existing call sites that do not pass
        // PublishedDate still compile and produce a null PublishedDate.
        var citation = new SourceCitation(
            Title: "Legacy Source",
            Url: "https://legacy.example.com",
            Snippet: "Old snippet.",
            DocumentReference: null,
            RelevanceScore: null);

        Assert.Null(citation.PublishedDate);
    }

    [Fact]
    public void SourceCitation_WithExplicitNullPublishedDate_HasNullPublishedDate()
    {
        var citation = new SourceCitation(
            Title: "No Date",
            Url: "https://example.com",
            Snippet: "Snippet.",
            DocumentReference: null,
            RelevanceScore: 0.7,
            PublishedDate: null);

        Assert.Null(citation.PublishedDate);
    }
}
