namespace Geef.Atelier.Infrastructure.Persistence.Providers;

/// <summary>EF Core entity for the <c>Providers</c> table. Flat mapping — Settings stored as JSONB.</summary>
internal sealed class ProviderEntity
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public int Type { get; set; }
    public string Settings { get; set; } = "{}";
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
