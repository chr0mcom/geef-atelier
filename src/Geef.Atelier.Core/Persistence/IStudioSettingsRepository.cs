using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Core.Persistence;

public interface IStudioSettingsRepository
{
    Task<StudioSettings> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(StudioSettings settings, CancellationToken ct = default);
}
