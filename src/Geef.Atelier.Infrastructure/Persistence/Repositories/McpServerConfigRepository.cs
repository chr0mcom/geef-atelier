using Geef.Atelier.Core.Domain.Mcp;
using Geef.Atelier.Core.Persistence.Mcp;
using Geef.Atelier.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Repositories;

internal sealed class McpServerConfigRepository(AtelierDbContext db) : IMcpServerConfigRepository
{
    public async Task<IReadOnlyList<McpServerConfig>> GetAllAsync(CancellationToken ct = default) =>
        (await db.McpServerConfigs.OrderBy(e => e.Name).ToListAsync(ct))
            .Select(e => e.ToDomain()).ToList();

    public async Task<IReadOnlyList<McpServerConfig>> GetActiveAsync(CancellationToken ct = default) =>
        (await db.McpServerConfigs.Where(e => e.IsActive).OrderBy(e => e.Name).ToListAsync(ct))
            .Select(e => e.ToDomain()).ToList();

    public async Task<McpServerConfig?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.McpServerConfigs.FindAsync([id], ct);
        return entity?.ToDomain();
    }

    public async Task UpsertAsync(McpServerConfig config, CancellationToken ct = default)
    {
        var existing = await db.McpServerConfigs.FindAsync([config.Id], ct);
        if (existing is null)
        {
            db.McpServerConfigs.Add(McpServerConfigEntity.FromDomain(config));
        }
        else
        {
            var updated = McpServerConfigEntity.FromDomain(config);
            db.Entry(existing).CurrentValues.SetValues(updated);
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.McpServerConfigs.FindAsync([id], ct);
        if (entity is not null)
        {
            db.McpServerConfigs.Remove(entity);
            await db.SaveChangesAsync(ct);
        }
    }
}
