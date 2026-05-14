using Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class KnowledgeDocumentChunkConfiguration : IEntityTypeConfiguration<KnowledgeDocumentChunkEntity>
{
    /// <summary>
    /// Pgvector.EntityFrameworkCore 0.3.0 is incompatible with Npgsql.EF 10.x, so the
    /// Embedding column uses a string value-converter as a bridge. The vector(1536) column
    /// type is kept in the schema; repositories that need ANN search execute raw SQL with
    /// NpgsqlParameter&lt;Pgvector.Vector&gt; directly, bypassing EF for those queries.
    /// </summary>
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

    public void Configure(EntityTypeBuilder<KnowledgeDocumentChunkEntity> builder)
    {
        builder.ToTable("KnowledgeDocumentChunks");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnType("uuid").IsRequired();
        builder.Property(c => c.DocumentId).HasColumnType("uuid").IsRequired();
        builder.Property(c => c.ChunkIndex).IsRequired();
        builder.Property(c => c.Content).IsRequired();
        builder.Property(c => c.Embedding)
            .HasColumnType("vector(1536)")
            .HasConversion(EmbeddingConverter)
            .IsRequired();
        builder.Property(c => c.TokenCount).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasIndex(c => c.DocumentId)
            .HasDatabaseName("IX_KnowledgeDocumentChunks_DocumentId");
    }
}
