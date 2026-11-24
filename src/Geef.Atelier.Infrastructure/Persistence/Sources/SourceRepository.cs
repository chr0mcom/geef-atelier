using System.Linq.Expressions;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Sources;

internal sealed class SourceRepository(AtelierDbContext db) : ISourceRepository, ISourceRetentionStore
{
    private static readonly RunStatus[] ActiveStatuses = [RunStatus.Pending, RunStatus.Running];

    private static readonly Expression<Func<SourceEntity, SourceEntity>> WithoutText = s => new SourceEntity
    {
        Id = s.Id,
        OwnerUsername = s.OwnerUsername,
        FileName = s.FileName,
        ContentType = s.ContentType,
        Kind = s.Kind,
        SizeBytes = s.SizeBytes,
        Sha256 = s.Sha256,
        StoragePath = s.StoragePath,
        ExtractedText = null,
        PageCount = s.PageCount,
        CharCount = s.CharCount,
        WordCount = s.WordCount,
        Quality = s.Quality,
        Warnings = s.Warnings,
        Origin = s.Origin,
        InLibrary = s.InLibrary,
        AlwaysInclude = s.AlwaysInclude,
        ExpiresAt = s.ExpiresAt,
        CreatedAt = s.CreatedAt,
    };

    public async Task AddAsync(SourceEntity source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        db.Sources.Add(source);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public Task<SourceEntity?> GetAsync(Guid sourceId, bool includeText, CancellationToken ct = default) =>
        Project(db.Sources.AsNoTracking().Where(s => s.Id == sourceId), includeText).FirstOrDefaultAsync(ct);

    public Task<SourceEntity?> FindByHashAsync(string ownerUsername, string sha256, CancellationToken ct = default) =>
        Project(db.Sources.AsNoTracking().Where(s => s.OwnerUsername == ownerUsername && s.Sha256 == sha256), includeText: false)
            .OrderBy(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<SourceEntity>> GetManyAsync(IReadOnlyCollection<Guid> sourceIds, bool includeText, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        if (sourceIds.Count == 0)
            return [];
        var ids = sourceIds.Distinct().ToList();
        return await Project(db.Sources.AsNoTracking().Where(s => ids.Contains(s.Id)), includeText).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OrderSourceItem>> ListForOrderAsync(Guid orderId, bool includeText, CancellationToken ct = default)
    {
        var links = await db.OrderSources.AsNoTracking()
            .Where(l => l.OrderId == orderId)
            .OrderBy(l => l.Position)
            .ThenBy(l => l.AddedAt)
            .ToListAsync(ct);
        if (links.Count == 0)
            return [];

        var sources = (await GetManyAsync(links.Select(l => l.SourceId).ToList(), includeText, ct)).ToDictionary(s => s.Id);
        return links
            .Where(l => sources.ContainsKey(l.SourceId))
            .Select(l => new OrderSourceItem(l, sources[l.SourceId]))
            .ToList();
    }

    public Task<bool> IsAttachedAsync(Guid sourceId, CancellationToken ct = default) =>
        db.OrderSources.AnyAsync(l => l.SourceId == sourceId, ct);

    public async Task<bool> DeleteIfUnattachedAsync(Guid sourceId, CancellationToken ct = default)
    {
        var affected = await db.Sources
            .Where(s => s.Id == sourceId && !db.OrderSources.Any(l => l.SourceId == s.Id))
            .ExecuteDeleteAsync(ct);
        return affected > 0;
    }

    public async Task<IReadOnlyList<SourceEntity>> ListExpiredUnattachedAsync(DateTimeOffset now, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");

        return await Project(db.Sources.AsNoTracking()
                .Where(s => s.ExpiresAt != null && s.ExpiresAt < now && !s.InLibrary
                            && !db.OrderSources.Any(l => l.SourceId == s.Id)), includeText: false)
            .OrderBy(s => s.ExpiresAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task AddRunUsagesAsync(IReadOnlyList<RunSourceUsageEntity> usages, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(usages);
        if (usages.Count == 0)
            return;
        db.RunSourceUsages.AddRange(usages);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task<IReadOnlyList<RunSourceUsageEntity>> ListRunUsagesAsync(Guid runId, CancellationToken ct = default) =>
        await db.RunSourceUsages.AsNoTracking()
            .Where(u => u.RunId == runId)
            .OrderBy(u => u.Position)
            .ThenBy(u => u.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<(Guid SourceId, string StoragePath)>> ListReleasableOriginalsAsync(DateTimeOffset finishedBefore, int limit, CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");

        var rows = await db.Sources.AsNoTracking()
            .Where(s => s.StoragePath != null && !s.InLibrary
                        && db.OrderSources.Any(l => l.SourceId == s.Id)
                        && !db.OrderSources.Any(l => l.SourceId == s.Id
                                                     && db.Orders.Any(o => o.Id == l.OrderId && o.UpdatedAt >= finishedBefore))
                        && !db.OrderSources.Any(l => l.SourceId == s.Id
                                                     && db.Runs.Any(r => r.OrderId == l.OrderId && ActiveStatuses.Contains(r.Status))))
            .OrderBy(s => s.CreatedAt)
            .Select(s => new { s.Id, s.StoragePath })
            .Take(limit)
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.StoragePath!)).ToList();
    }

    public async Task ClearStoragePathAsync(Guid sourceId, CancellationToken ct = default) =>
        await db.Sources.Where(s => s.Id == sourceId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StoragePath, (string?)null), ct);

    private static IQueryable<SourceEntity> Project(IQueryable<SourceEntity> query, bool includeText) =>
        includeText ? query : query.Select(WithoutText);
}
