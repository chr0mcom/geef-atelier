namespace Geef.Atelier.Application.Crew.TemplateStudio;

/// <summary>Reads and writes the persisted default provider/model for Template Studio analysis.</summary>
public interface IStudioSettingsService
{
    Task<Core.Domain.StudioSettings> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(Core.Domain.StudioSettings settings, CancellationToken ct = default);
}
