namespace Geef.Atelier.Infrastructure.Persistence.StudioSettings;

internal sealed class StudioSettingsEntity
{
    public Guid Id { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public int MaxTokens { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
