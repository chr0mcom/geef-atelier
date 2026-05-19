using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Crew;

/// <summary>
/// Cascades a custom profile/template rename into the by-name references that custom crew
/// templates and runs hold. References are plain string / JSONB-list columns (no DB-level
/// foreign keys), so the cascade is performed in code. System templates live as code
/// constants and never reference custom profiles, so only DB-backed rows are touched.
/// </summary>
internal static class CrewTemplateCascade
{
    internal enum ListRef { Reviewer, Advisor, Grounding, Finalizer }

    /// <summary>Repoints the scalar <c>ExecutorProfileName</c> of every custom template that references <paramref name="oldName"/>.</summary>
    public static Task RenameExecutorRefAsync(
        AtelierDbContext db, string oldName, string newName, CancellationToken ct)
        => db.CrewTemplates
             .Where(t => t.ExecutorProfileName == oldName)
             .ExecuteUpdateAsync(s => s.SetProperty(t => t.ExecutorProfileName, newName), ct);

    /// <summary>Replaces <paramref name="oldName"/> inside a JSONB string-list field of every custom template that references it.</summary>
    public static async Task RenameListRefAsync(
        AtelierDbContext db, ListRef field, string oldName, string newName, CancellationToken ct)
    {
        var templates = await db.CrewTemplates.ToListAsync(ct);
        foreach (var t in templates)
        {
            var current = field switch
            {
                ListRef.Reviewer  => t.ReviewerProfileNames,
                ListRef.Advisor   => t.AdvisorProfileNames,
                ListRef.Grounding => t.GroundingProviderNames,
                ListRef.Finalizer => t.FinalizerProfileNames,
                _                 => throw new ArgumentOutOfRangeException(nameof(field)),
            };
            if (!current.Contains(oldName))
                continue;

            var replaced = current.Select(n => n == oldName ? newName : n).ToList();
            var updated = field switch
            {
                ListRef.Reviewer  => t with { ReviewerProfileNames = replaced },
                ListRef.Advisor   => t with { AdvisorProfileNames = replaced },
                ListRef.Grounding => t with { GroundingProviderNames = replaced },
                ListRef.Finalizer => t with { FinalizerProfileNames = replaced },
                _                 => t,
            };
            db.Entry(t).CurrentValues.SetValues(updated);
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Repoints <c>RunEntity.CrewTemplateName</c> for past runs. The frozen <c>CrewSnapshot</c> JSON is left untouched.</summary>
    public static Task RenameRunTemplateRefAsync(
        AtelierDbContext db, string oldName, string newName, CancellationToken ct)
        => db.Runs
             .Where(r => r.CrewTemplateName == oldName)
             .ExecuteUpdateAsync(s => s.SetProperty(r => r.CrewTemplateName, newName), ct);
}
