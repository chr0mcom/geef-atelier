namespace Geef.Atelier.Core.Domain.Dashboard;

/// <summary>Gallery of the most recent completed manuscripts.</summary>
public sealed record ManuscriptCard(
    Guid RunId,
    string BriefingSnippet,
    string? TemplateName,
    int WordCount,
    decimal? CostEur,
    DateTimeOffset CompletedAt,
    int Iterations);
