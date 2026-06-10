using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Repositories;

internal sealed class ToolDefinitionRepository(AtelierDbContext db) : IToolDefinitionRepository
{
    /// <inheritdoc/>
    public async Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var entity = await db.ToolDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Name == name, ct);
        return entity?.ToDomain();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        var entities = await db.ToolDefinitions
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
        return entities.Select(e => e.ToDomain()).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ToolDefinition>> GetSystemToolsAsync(CancellationToken ct = default)
    {
        var entities = await db.ToolDefinitions
            .AsNoTracking()
            .Where(t => t.IsSystem)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
        return entities.Select(e => e.ToDomain()).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ToolDefinition>> GetCustomToolsAsync(CancellationToken ct = default)
    {
        var entities = await db.ToolDefinitions
            .AsNoTracking()
            .Where(t => !t.IsSystem)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
        return entities.Select(e => e.ToDomain()).ToList();
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(ToolDefinition tool, CancellationToken ct = default)
    {
        var existing = await db.ToolDefinitions
            .FirstOrDefaultAsync(t => t.Name == tool.Name, ct);

        if (existing is null)
        {
            db.ToolDefinitions.Add(ToolDefinitionEntity.FromDomain(tool));
        }
        else
        {
            var updated = ToolDefinitionEntity.FromDomain(tool);
            existing.DisplayName = updated.DisplayName;
            existing.Description = updated.Description;
            existing.ToolType = updated.ToolType;
            existing.Settings = updated.Settings;
            existing.SecretRef = updated.SecretRef;
            existing.LlmSchemaJson = updated.LlmSchemaJson;
            existing.AccessClass = updated.AccessClass;
            existing.IsSystem = updated.IsSystem;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        var affected = await db.ToolDefinitions
            .Where(t => t.Name == name)
            .ExecuteDeleteAsync(ct);

        // No-op when not found — callers should not care if the entry was already absent.
        _ = affected;
    }
}
