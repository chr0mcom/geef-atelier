using Geef.Atelier.Core.Domain.Sources;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.Sources;

/// <summary>Stores counted refusals (<c>SourceRefusals</c>) and deletes them after their retention; it never reads a file name or content.</summary>
internal sealed class SourceRefusalRepository(AtelierDbContext db)
{
    /// <summary>Inserts one counted refusal.</summary>
    public async Task AddAsync(SourceRefusalEntity refusal, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(refusal);
        db.SourceRefusals.Add(refusal);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>Deletes refusals counted before <paramref name="before"/>; returns their number.</summary>
    public Task<int> DeleteOlderThanAsync(DateTimeOffset before, CancellationToken ct = default) =>
        db.SourceRefusals.Where(r => r.CreatedAt < before).ExecuteDeleteAsync(ct);
}
