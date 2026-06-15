using Geef.Atelier.Core.Domain.Crew.Specialization;

namespace Geef.Atelier.Infrastructure.Persistence.Entities;

/// <summary>
/// EF Core mapping entity for the <c>specialization_packs</c> table.
/// Maps to and from the immutable <see cref="SpecializationPack"/> domain record.
/// </summary>
internal sealed class SpecializationPackEntity
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string SpecializationText { get; set; } = "";
    public int Scope { get; set; }
    public string? Domain { get; set; }
    public List<int> ApplicableActorTypes { get; set; } = new();
    public string? OwningCrewId { get; set; }
    public bool IsSystem { get; set; }
    public bool Archived { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    public SpecializationPack ToDomain() => new(
        Name,
        DisplayName,
        Description,
        SpecializationText,
        (PackScope)Scope,
        Domain,
        ApplicableActorTypes.Select(i => (PackActorType)i).ToList(),
        OwningCrewId,
        IsSystem,
        Archived,
        CreatedAt,
        UpdatedAt,
        LastUsedAt);

    public static SpecializationPackEntity FromDomain(SpecializationPack p) => new()
    {
        Name = p.Name,
        DisplayName = p.DisplayName,
        Description = p.Description,
        SpecializationText = p.SpecializationText,
        Scope = (int)p.Scope,
        Domain = p.Domain,
        ApplicableActorTypes = p.ApplicableActorTypes.Select(t => (int)t).ToList(),
        OwningCrewId = p.OwningCrewId,
        IsSystem = p.IsSystem,
        Archived = p.Archived,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        LastUsedAt = p.LastUsedAt
    };
}
