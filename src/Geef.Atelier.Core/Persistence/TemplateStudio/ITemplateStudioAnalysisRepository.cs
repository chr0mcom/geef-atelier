using Geef.Atelier.Core.Domain.Crew.TemplateStudio;

namespace Geef.Atelier.Core.Persistence.TemplateStudio;

/// <summary>Persistence contract for Template Studio analysis records.</summary>
public interface ITemplateStudioAnalysisRepository
{
    Task CreateAsync(TemplateStudioAnalysis analysis, CancellationToken ct = default);
    Task<TemplateStudioAnalysis?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task MarkMaterializedAsync(Guid analysisId, string templateName, CancellationToken ct = default);
    Task<IReadOnlyList<TemplateStudioAnalysis>> ListRecentAsync(int limit = 10, CancellationToken ct = default);
}
