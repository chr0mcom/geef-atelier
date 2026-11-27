namespace Geef.Atelier.Infrastructure.Persistence.Providers;

using System.Text.Json;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Core.Persistence.Providers;
using Microsoft.EntityFrameworkCore;

internal sealed class ProviderRepository(AtelierDbContext db) : IProviderRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var query = db.Providers.AsNoTracking();
        if (!includeInactive)
            query = query.Where(e => e.IsActive);
        var entities = await query.OrderBy(e => e.Name).ToListAsync(ct);
        return entities.Select(ToModel).ToList();
    }

    /// <inheritdoc/>
    public async Task<Provider?> GetByNameAsync(string name, CancellationToken ct)
    {
        var entity = await db.Providers.AsNoTracking().FirstOrDefaultAsync(e => e.Name == name, ct);
        return entity is null ? null : ToModel(entity);
    }

    /// <inheritdoc/>
    public async Task CreateAsync(Provider provider, CancellationToken ct)
    {
        var entity = ToEntity(provider);
        db.Providers.Add(entity);
        await db.SaveChangesAsync(ct);
        db.Entry(entity).State = EntityState.Detached;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Provider provider, CancellationToken ct)
    {
        var existing = await db.Providers.FirstOrDefaultAsync(e => e.Name == provider.Name, ct)
            ?? throw new InvalidOperationException($"Provider '{provider.Name}' not found.");
        existing.DisplayName = provider.DisplayName;
        existing.Description = provider.Description;
        existing.Type = (int)provider.Type;
        existing.Settings = JsonSerializer.Serialize(provider.Settings, JsonOpts);
        existing.IsActive = provider.IsActive;
        existing.UpdatedAt = provider.UpdatedAt;
        await db.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string name, CancellationToken ct)
    {
        var affected = await db.Providers.Where(e => e.Name == name).ExecuteDeleteAsync(ct);
        if (affected == 0)
            throw new InvalidOperationException($"Provider '{name}' not found.");
    }

    /// <inheritdoc/>
    public async Task SetActiveAsync(string name, bool isActive, CancellationToken ct)
    {
        var affected = await db.Providers
            .Where(e => e.Name == name)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsActive, isActive), ct);
        if (affected == 0)
            throw new InvalidOperationException($"Provider '{name}' not found.");
    }


    private static Provider ToModel(ProviderEntity e)
    {
        var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(e.Settings, JsonOpts)
            ?? [];
        return new Provider(
            Name: e.Name,
            DisplayName: e.DisplayName,
            Description: e.Description,
            Type: (ProviderType)e.Type,
            Settings: settings,
            IsSystem: e.IsSystem,
            IsActive: e.IsActive,
            CreatedAt: e.CreatedAt,
            UpdatedAt: e.UpdatedAt
        );
    }

    private static ProviderEntity ToEntity(Provider p) => new()
    {
        Name = p.Name,
        DisplayName = p.DisplayName,
        Description = p.Description,
        Type = (int)p.Type,
        Settings = JsonSerializer.Serialize(p.Settings, JsonOpts),
        IsSystem = p.IsSystem,
        IsActive = p.IsActive,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
    };
}
