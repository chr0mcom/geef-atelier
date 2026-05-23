using Geef.Atelier.Application.Crew.TemplateStudio;
using DomainStudioSettings = Geef.Atelier.Core.Domain.StudioSettings;

namespace Geef.Atelier.Tests.Application.TemplateStudio;

/// <summary>In-memory <see cref="IStudioSettingsService"/> for unit tests. Starts with an empty default
/// (so the appsettings fallback applies) unless seeded.</summary>
internal sealed class FakeStudioSettingsService(DomainStudioSettings? initial = null) : IStudioSettingsService
{
    private DomainStudioSettings _current = initial ?? new DomainStudioSettings
    {
        Id = Guid.NewGuid(),
        Provider = "",
        Model = "",
        MaxTokens = 0,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public Task<DomainStudioSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(_current);

    public Task UpdateAsync(DomainStudioSettings settings, CancellationToken ct = default)
    {
        _current = settings with { UpdatedAt = DateTimeOffset.UtcNow };
        return Task.CompletedTask;
    }

    public DomainStudioSettings Current => _current;
}
