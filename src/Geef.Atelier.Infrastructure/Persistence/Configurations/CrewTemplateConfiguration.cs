using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class CrewTemplateConfiguration : IEntityTypeConfiguration<CrewTemplate>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<IReadOnlyList<string>> StringListComparer = new(
        (a, b) => a!.SequenceEqual(b!),
        v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode(StringComparison.Ordinal))),
        v => (IReadOnlyList<string>)v.ToList());

    public void Configure(EntityTypeBuilder<CrewTemplate> builder)
    {
        builder.ToTable("CrewTemplates");
        builder.HasKey(t => t.Name);

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).IsRequired();
        builder.Property(t => t.ExecutorProfileName).HasMaxLength(200).IsRequired();
        builder.Property(t => t.EvaluationStrategy)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(t => t.IsSystem).IsRequired().HasDefaultValue(false);

        builder.Property(t => t.ReviewerProfileNames)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => (IReadOnlyList<string>)JsonSerializer.Deserialize<List<string>>(v, JsonOpts)!,
                StringListComparer)
            .IsRequired()
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(t => t.AdvisorProfileNames)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => (IReadOnlyList<string>)JsonSerializer.Deserialize<List<string>>(v, JsonOpts)!,
                StringListComparer)
            .IsRequired()
            .HasDefaultValueSql("'[]'::jsonb");

        builder.Property(t => t.ConvergenceOverride)
            .HasColumnType("jsonb")
            .HasConversion(
                v => v == null ? null : JsonSerializer.Serialize(v, JsonOpts),
                v => v == null ? null : JsonSerializer.Deserialize<ConvergencePolicyOverride>(v, JsonOpts))
            .IsRequired(false);
    }
}
