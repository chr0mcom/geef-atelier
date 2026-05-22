namespace Geef.Atelier.Application.SiteSettings;

public interface ISiteSettingsService
{
    Task<Core.Domain.SiteSettings> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(Core.Domain.SiteSettings settings, CancellationToken ct = default);
}
