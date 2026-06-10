using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class ExecutorProfileConfiguration : IEntityTypeConfiguration<ExecutorProfile>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<IReadOnlyList<string>> ListComparer = new(
        (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
        v => v == null ? 0 : v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
        v => v == null ? (IReadOnlyList<string>)Array.Empty<string>() : (IReadOnlyList<string>)v.ToList());

    public void Configure(EntityTypeBuilder<ExecutorProfile> builder)
    {
        builder.ToTable("ExecutorProfiles");
        builder.HasKey(e => e.Name);

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).IsRequired();
        builder.Property(e => e.SystemPrompt).IsRequired();
        builder.Property(e => e.Provider).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Model).HasMaxLength(200).IsRequired();
        builder.Property(e => e.MaxTokens).IsRequired(false);
        builder.Property(e => e.IsSystem).IsRequired().HasDefaultValue(false);

        builder.Property(e => e.ToolNames)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v ?? (IReadOnlyList<string>)Array.Empty<string>(), JsonOpts),
                v => (IReadOnlyList<string>)(JsonSerializer.Deserialize<List<string>>(v, JsonOpts) ?? new List<string>()),
                ListComparer)
            .HasDefaultValueSql("'[]'::jsonb");
    }
}
