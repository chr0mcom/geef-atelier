using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Core.Persistence;
using Microsoft.Extensions.Caching.Memory;
using DomainStudioSettings = Geef.Atelier.Core.Domain.StudioSettings;

namespace Geef.Atelier.Infrastructure.TemplateStudio;

internal sealed class StudioSettingsService(IStudioSettingsRepository repository, IMemoryCache cache) : IStudioSettingsService
{
    private const string CacheKey = "StudioSettings";

    public async Task<DomainStudioSettings> GetAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out DomainStudioSettings? cached) && cached is not null)
            return cached;

        var settings = await repository.GetAsync(ct);
        cache.Set(CacheKey, settings, TimeSpan.FromMinutes(30));
        return settings;
    }

    public async Task UpdateAsync(DomainStudioSettings settings, CancellationToken ct = default)
    {
        await repository.UpdateAsync(settings, ct);
        cache.Remove(CacheKey);
    }
}
