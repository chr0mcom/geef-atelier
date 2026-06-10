using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class ReviewerProfileConfiguration : IEntityTypeConfiguration<ReviewerProfile>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<IReadOnlyList<string>> ListComparer = new(
        (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
        v => v == null ? 0 : v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
        v => v == null ? (IReadOnlyList<string>)Array.Empty<string>() : (IReadOnlyList<string>)v.ToList());

    public void Configure(EntityTypeBuilder<ReviewerProfile> builder)
    {
        builder.ToTable("ReviewerProfiles");
        builder.HasKey(r => r.Name);

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Description).IsRequired();
        builder.Property(r => r.SystemPrompt).IsRequired();
        builder.Property(r => r.Provider).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Model).HasMaxLength(200).IsRequired();
        builder.Property(r => r.MaxTokens).IsRequired(false);
        builder.Property(r => r.IsSystem).IsRequired().HasDefaultValue(false);

        builder.Property(r => r.ToolNames)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v ?? (IReadOnlyList<string>)Array.Empty<string>(), JsonOpts),
                v => (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(v, JsonOpts) ?? new List<string>()),
                ListComparer)
            .HasDefaultValueSql("'[]'::jsonb");
    }
}
