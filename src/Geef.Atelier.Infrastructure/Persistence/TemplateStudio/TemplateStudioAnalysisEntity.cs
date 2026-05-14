namespace Geef.Atelier.Infrastructure.Persistence.TemplateStudio;

internal sealed class TemplateStudioAnalysisEntity
{
    public Guid Id { get; set; }
    public string TaskDescription { get; set; } = "";
    public string AnalysisResultJson { get; set; } = "{}";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal? CostEur { get; set; }
    public string? MaterializedTemplateName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
