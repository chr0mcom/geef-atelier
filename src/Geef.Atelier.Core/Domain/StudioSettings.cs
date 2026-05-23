namespace Geef.Atelier.Core.Domain;

/// <summary>
/// Persisted default provider/model for the Template Studio meta-LLM analysis call.
/// Exactly one row exists in the database. Empty <see cref="Provider"/>/<see cref="Model"/>
/// or a non-positive <see cref="MaxTokens"/> mean "fall back to the appsettings default".
/// </summary>
public sealed record StudioSettings
{
    public Guid Id { get; init; }
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
    public int MaxTokens { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}
