using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Core.Persistence;

public interface ISiteSettingsRepository
{
    Task<SiteSettings> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(SiteSettings settings, CancellationToken ct = default);
}
