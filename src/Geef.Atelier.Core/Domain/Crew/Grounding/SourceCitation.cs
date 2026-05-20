namespace Geef.Atelier.Core.Domain.Crew.Grounding;

/// <summary>
/// A single source referenced in a grounding consultation.
/// Supports both web-search sources (<see cref="Url"/> is set) and vector-store sources
/// (<see cref="DocumentReference"/> is set), so the schema is forward-compatible with a
/// future <c>VectorStoreGroundingProvider</c> without a breaking change.
/// </summary>
/// <param name="Title">Display title of the source.</param>
/// <param name="Url">URL for web-search sources; <c>null</c> for vector-store sources.</param>
/// <param name="Snippet">Relevant extract from the source (max 300 chars as surfaced in the UI).</param>
/// <param name="DocumentReference">Document/chunk reference for vector-store sources; <c>null</c> for web-search.</param>
/// <param name="RelevanceScore">Optional relevance score returned by the provider (e.g. 0.0–1.0).</param>
/// <param name="PublishedDate">Optional publication date returned by news-search providers; <c>null</c> for all other source types.</param>
public sealed record SourceCitation(
    string Title,
    string? Url,
    string Snippet,
    string? DocumentReference,
    double? RelevanceScore,
    DateTimeOffset? PublishedDate = null);
