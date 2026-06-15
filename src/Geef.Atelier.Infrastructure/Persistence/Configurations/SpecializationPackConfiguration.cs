using System.Text.Json;
using Geef.Atelier.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class SpecializationPackConfiguration : IEntityTypeConfiguration<SpecializationPackEntity>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<List<int>> IntListComparer = new(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, HashCode.Combine),
        v => v.ToList());

    public void Configure(EntityTypeBuilder<SpecializationPackEntity> builder)
    {
        builder.ToTable("specialization_packs");
        builder.HasKey(p => p.Name);

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).IsRequired();
        builder.Property(p => p.SpecializationText).IsRequired();
        builder.Property(p => p.Scope).IsRequired();
        builder.Property(p => p.Domain).HasMaxLength(100).IsRequired(false);
        builder.Property(p => p.OwningCrewId).HasMaxLength(200).IsRequired(false);
        builder.Property(p => p.IsSystem).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.Archived).IsRequired().HasDefaultValue(false);

        builder.Property(p => p.ApplicableActorTypes)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<List<int>>(v, JsonOpts) ?? new List<int>(),
                IntListComparer)
            .IsRequired()
            .HasDefaultValueSql("'[]'::jsonb");

        builder.HasIndex(p => p.OwningCrewId);
        builder.HasIndex(p => new { p.Scope, p.Archived });
    }
}
