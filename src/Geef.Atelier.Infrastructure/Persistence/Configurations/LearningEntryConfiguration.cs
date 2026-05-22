using Geef.Atelier.Infrastructure.Persistence.Crew.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class LearningEntryConfiguration : IEntityTypeConfiguration<LearningEntryEntity>
{
    private static readonly ValueConverter<float[], string> EmbeddingConverter = new(
        v => "[" + string.Join(",", v.Select(f => f.ToString(System.Globalization.CultureInfo.InvariantCulture))) + "]",
        v => ParseVector(v));

    private static float[] ParseVector(string s)
    {
        var inner = s.Trim('[', ']');
        if (string.IsNullOrEmpty(inner)) return [];
        var parts = inner.Split(',');
        var result = new float[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            result[i] = float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
        return result;
    }

    public void Configure(EntityTypeBuilder<LearningEntryEntity> builder)
    {
        builder.ToTable("LearningEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnType("uuid").IsRequired();
        builder.Property(e => e.Text).IsRequired();
        builder.Property(e => e.SourceRunId).HasColumnType("uuid").IsRequired(false);
        builder.Property(e => e.LearningRunId).HasColumnType("uuid").IsRequired(false);
        builder.Property(e => e.Domain).IsRequired();
        builder.Property(e => e.Status).IsRequired();
        builder.Property(e => e.StructuredFactsJson).IsRequired();
        builder.Property(e => e.OwnerUsername).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.ApprovedAt).IsRequired(false);
        builder.Property(e => e.Embedding)
            .HasColumnType("vector(1536)")
            .HasConversion(EmbeddingConverter)
            .IsRequired(false);

        builder.HasIndex(e => new { e.Domain, e.Status }).HasDatabaseName("IX_LearningEntries_Domain_Status");
    }
}
