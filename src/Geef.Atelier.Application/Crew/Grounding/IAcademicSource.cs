namespace Geef.Atelier.Application.Crew.Grounding;

/// <summary>Common interface for scientific-paper search adapters (arXiv, Semantic Scholar, OpenAlex).</summary>
public interface IAcademicSource
{
    /// <summary>Discriminator name matching the configured academic source setting.</summary>
    string SourceName { get; }

    /// <summary>Searches for papers matching <paramref name="query"/> and returns up to <see cref="AcademicSearchOptions.MaxPapers"/> results.</summary>
    Task<IReadOnlyList<AcademicPaper>> SearchAsync(string query, AcademicSearchOptions options, CancellationToken ct);
}

/// <summary>Runtime options forwarded from <see cref="Core.Domain.Crew.Grounding.GroundingProviderProfile"/> to the adapter.</summary>
/// <param name="MaxPapers">Maximum number of papers to return.</param>
/// <param name="DateFrom">Optional ISO date or year string used to filter results by publication date.</param>
/// <param name="Fields">Optional field restriction hint (adapter-specific meaning).</param>
/// <param name="ApiKey">Optional API key resolved from the configured environment variable.</param>
public sealed record AcademicSearchOptions(int MaxPapers, string? DateFrom, string? Fields, string? ApiKey);

/// <summary>A single scientific paper returned by an academic search adapter.</summary>
/// <param name="Title">Full paper title.</param>
/// <param name="Authors">Formatted authors string (e.g. "Smith, J.; Jones, A.").</param>
/// <param name="Abstract">Paper abstract or summary text.</param>
/// <param name="Doi">DOI (without resolver prefix), or <c>null</c> if unavailable.</param>
/// <param name="ArxivId">arXiv identifier (e.g. "2301.07xxx"), or <c>null</c> if unavailable.</param>
/// <param name="Url">Canonical URL for the paper landing page.</param>
/// <param name="PublishedDate">Publication date, or <c>null</c> if unknown.</param>
public sealed record AcademicPaper(
    string Title,
    string? Authors,
    string? Abstract,
    string? Doi,
    string? ArxivId,
    string? Url,
    DateTimeOffset? PublishedDate);
