using Geef.Atelier.Core.Domain.Crew.Composition;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity-type configuration for <see cref="CrewTemplateEmbedding"/>.
/// The vector(1536) embedding column uses a string value-converter as a bridge because
/// Pgvector.EntityFrameworkCore 0.3.0 is incompatible with Npgsql.EF 10.x. ANN queries
/// are performed via raw SQL in <c>CrewTemplateEmbeddingRepository</c>.
/// </summary>
internal sealed class CrewTemplateEmbeddingConfiguration : IEntityTypeConfiguration<CrewTemplateEmbedding>
{
    /// <summary>Value converter: float[] ↔ Postgres vector literal "[f0,f1,…]".</summary>
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

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<CrewTemplateEmbedding> builder)
    {
        builder.ToTable("CrewTemplateEmbeddings");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnType("uuid").IsRequired();
        builder.Property(e => e.TemplateName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Domain).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Summary).IsRequired();
        builder.Property(e => e.Embedding)
            .HasColumnType("vector(1536)")
            .HasConversion(EmbeddingConverter)
            .IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();

        builder.HasIndex(e => e.TemplateName)
            .IsUnique()
            .HasDatabaseName("IX_CrewTemplateEmbeddings_TemplateName");

        builder.HasIndex(e => e.Domain)
            .HasDatabaseName("IX_CrewTemplateEmbeddings_Domain");
    }
}
