using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Crew;

internal sealed class CrewTemplateRepository(AtelierDbContext db) : ICrewTemplateRepository
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<CrewTemplate>> ListAsync(bool includeSystem = true, CancellationToken cancellationToken = default)
    {
        var custom = await db.CrewTemplates.AsNoTracking().ToListAsync(cancellationToken);
        if (!includeSystem)
            return custom;
        return SystemCrew.CrewTemplates.Values.Concat(custom).ToList();
    }

    /// <inheritdoc/>
    public async Task<CrewTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.CrewTemplates.TryGetValue(name, out var systemTemplate))
            return systemTemplate;
        return await db.CrewTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Name == name, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CreateAsync(CrewTemplate template, CancellationToken cancellationToken = default)
    {
        db.CrewTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(CrewTemplate template, CancellationToken cancellationToken = default)
    {
        db.CrewTemplates.Update(template);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var affected = await db.CrewTemplates
            .Where(t => t.Name == name)
            .ExecuteDeleteAsync(cancellationToken);
        if (affected == 0)
            throw new InvalidOperationException($"Crew template '{name}' not found in the database.");
    }
}
