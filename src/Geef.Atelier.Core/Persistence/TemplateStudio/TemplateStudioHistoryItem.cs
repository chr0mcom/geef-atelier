namespace Geef.Atelier.Core.Persistence.TemplateStudio;

/// <summary>Lightweight projection of a TemplateStudioAnalysis for history list queries.</summary>
public sealed record TemplateStudioHistoryItem(
    Guid Id,
    string TaskDescription,
    string ReasoningSummary,
    string? MaterializedTemplateName,
    decimal? CostEur,
    DateTimeOffset CreatedAt);
