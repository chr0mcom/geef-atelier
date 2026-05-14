using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using Geef.Atelier.Core.Persistence.TemplateStudio;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.TemplateStudio;

internal sealed class TemplateStudioAnalysisRepository(AtelierDbContext db) : ITemplateStudioAnalysisRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task CreateAsync(TemplateStudioAnalysis analysis, CancellationToken ct = default)
    {
        var entity = ToEntity(analysis);
        db.TemplateStudioAnalyses.Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public async Task<TemplateStudioAnalysis?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.TemplateStudioAnalyses.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task MarkMaterializedAsync(Guid analysisId, string templateName, CancellationToken ct = default)
    {
        await db.TemplateStudioAnalyses
            .Where(e => e.Id == analysisId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.MaterializedTemplateName, templateName), ct);
    }

    public async Task<IReadOnlyList<TemplateStudioAnalysis>> ListRecentAsync(int limit = 10, CancellationToken ct = default)
    {
        var entities = await db.TemplateStudioAnalyses.AsNoTracking()
            .OrderByDescending(e => e.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        return entities.Select(ToDomain).ToList();
    }

    private static TemplateStudioAnalysisEntity ToEntity(TemplateStudioAnalysis analysis) => new()
    {
        Id = analysis.Id,
        TaskDescription = analysis.TaskDescription,
        AnalysisResultJson = JsonSerializer.Serialize(analysis, JsonOpts),
        InputTokens = analysis.InputTokens,
        OutputTokens = analysis.OutputTokens,
        CostEur = analysis.CostEur,
        MaterializedTemplateName = null,
        CreatedAt = analysis.CreatedAt
    };

    private static TemplateStudioAnalysis ToDomain(TemplateStudioAnalysisEntity entity)
    {
        var analysis = JsonSerializer.Deserialize<TemplateStudioAnalysis>(entity.AnalysisResultJson, JsonOpts)
            ?? throw new InvalidOperationException($"Failed to deserialize TemplateStudioAnalysis for id {entity.Id}");
        return analysis;
    }
}
