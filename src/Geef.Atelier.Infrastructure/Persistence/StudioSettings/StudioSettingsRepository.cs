using Geef.Atelier.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Persistence.StudioSettings;

internal sealed class StudioSettingsRepository(AtelierDbContext db) : IStudioSettingsRepository
{
    private static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public async Task<Core.Domain.StudioSettings> GetAsync(CancellationToken ct = default)
    {
        var entity = await db.StudioSettings.FirstOrDefaultAsync(ct);
        if (entity is null)
        {
            entity = CreateDefault();
            db.StudioSettings.Add(entity);
            await db.SaveChangesAsync(ct);
        }
        return MapToDomain(entity);
    }

    public async Task UpdateAsync(Core.Domain.StudioSettings settings, CancellationToken ct = default)
    {
        var entity = await db.StudioSettings.FirstOrDefaultAsync(ct);
        if (entity is null)
        {
            entity = CreateDefault();
            db.StudioSettings.Add(entity);
        }

        entity.Provider = settings.Provider;
        entity.Model = settings.Model;
        entity.MaxTokens = settings.MaxTokens;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
    }

    private static StudioSettingsEntity CreateDefault() => new()
    {
        Id = SingletonId,
        Provider = "",
        Model = "",
        MaxTokens = 0,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private static Core.Domain.StudioSettings MapToDomain(StudioSettingsEntity e) => new()
    {
        Id = e.Id,
        Provider = e.Provider,
        Model = e.Model,
        MaxTokens = e.MaxTokens,
        UpdatedAt = e.UpdatedAt,
    };
}
