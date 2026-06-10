using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class GroundingProviderProfileConfiguration : IEntityTypeConfiguration<GroundingProviderProfile>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<Dictionary<string, string>> DictComparer = new(
        (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
        v => v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key.GetHashCode(), kv.Value.GetHashCode())),
        v => new Dictionary<string, string>(v));

    public void Configure(EntityTypeBuilder<GroundingProviderProfile> builder)
    {
        builder.ToTable("GroundingProviderProfiles");
        builder.HasKey(p => p.Name);

        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).IsRequired();
        builder.Property(p => p.ProviderType).HasMaxLength(64).IsRequired();
        builder.Property(p => p.MaxQueriesPerRun).IsRequired(false);
        builder.Property(p => p.IsSystem).IsRequired().HasDefaultValue(false);
        builder.Property(p => p.ToolName).HasMaxLength(100).IsRequired(false);

        builder.Property(p => p.ProviderSettings)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOpts) ?? new Dictionary<string, string>(),
                DictComparer)
            .IsRequired()
            .HasDefaultValueSql("'{}'::jsonb");
    }
}
