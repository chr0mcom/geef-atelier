using Geef.Atelier.Application.SiteSettings;
using Geef.Atelier.Core.Persistence;
using Microsoft.Extensions.Caching.Memory;
using DomainSiteSettings = Geef.Atelier.Core.Domain.SiteSettings;

namespace Geef.Atelier.Infrastructure.SiteSettings;

internal sealed class SiteSettingsService(ISiteSettingsRepository repository, IMemoryCache cache) : ISiteSettingsService
{
    private const string CacheKey = "SiteSettings";

    public async Task<DomainSiteSettings> GetAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out DomainSiteSettings? cached) && cached is not null)
            return cached;

        var settings = await repository.GetAsync(ct);
        cache.Set(CacheKey, settings, TimeSpan.FromMinutes(30));
        return settings;
    }

    public async Task UpdateAsync(DomainSiteSettings settings, CancellationToken ct = default)
    {
        await repository.UpdateAsync(settings, ct);
        cache.Remove(CacheKey);
    }
}
