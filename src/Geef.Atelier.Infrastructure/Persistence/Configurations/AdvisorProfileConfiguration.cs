using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class AdvisorProfileConfiguration : IEntityTypeConfiguration<AdvisorProfile>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<IReadOnlyList<string>> ListComparer = new(
        (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
        v => v == null ? 0 : v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
        v => v == null ? (IReadOnlyList<string>)Array.Empty<string>() : (IReadOnlyList<string>)v.ToList());

    public void Configure(EntityTypeBuilder<AdvisorProfile> builder)
    {
        builder.ToTable("AdvisorProfiles");
        builder.HasKey(a => a.Name);

        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Description).IsRequired();
        builder.Property(a => a.SystemPrompt).IsRequired();
        builder.Property(a => a.Provider).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Model).HasMaxLength(200).IsRequired();
        builder.Property(a => a.MaxTokens).IsRequired(false);
        builder.Property(a => a.Mode)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(a => a.Trigger)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(a => a.IsSystem).IsRequired().HasDefaultValue(false);

        builder.Property(a => a.ToolNames)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v ?? (IReadOnlyList<string>)Array.Empty<string>(), JsonOpts),
                v => (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(v, JsonOpts) ?? new List<string>()),
                ListComparer)
            .HasDefaultValueSql("'[]'::jsonb");
    }
}
