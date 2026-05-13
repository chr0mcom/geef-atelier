using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class GroundingConsultationConfiguration : IEntityTypeConfiguration<GroundingConsultation>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<IReadOnlyList<SourceCitation>> CitationsComparer = new(
        (a, b) => a!.Count == b!.Count && a.SequenceEqual(b),
        v => v.Aggregate(0, (h, c) => HashCode.Combine(h, c.GetHashCode())),
        v => (IReadOnlyList<SourceCitation>)v.ToList());

    public void Configure(EntityTypeBuilder<GroundingConsultation> builder)
    {
        builder.ToTable("GroundingConsultations");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnType("uuid").IsRequired();
        builder.Property(c => c.RunId).HasColumnType("uuid").IsRequired();
        builder.Property(c => c.GroundingProviderName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Query).IsRequired();
        builder.Property(c => c.TokensOrCreditsUsed).IsRequired();
        builder.Property(c => c.CostEur).HasColumnType("numeric(10,4)").IsRequired(false);
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.Property(c => c.Citations)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => (IReadOnlyList<SourceCitation>)JsonSerializer.Deserialize<List<SourceCitation>>(v, JsonOpts)!,
                CitationsComparer)
            .IsRequired()
            .HasDefaultValueSql("'[]'::jsonb");

        builder.HasIndex(c => c.RunId).HasDatabaseName("IX_GroundingConsultations_RunId");
    }
}
