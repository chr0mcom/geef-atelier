using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocumentEntity>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocumentEntity> builder)
    {
        builder.ToTable("KnowledgeDocuments");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id).HasColumnType("uuid").IsRequired();
        builder.Property(d => d.Title).IsRequired();
        builder.Property(d => d.Description).IsRequired();
        builder.Property(d => d.OriginalFilename).IsRequired();
        builder.Property(d => d.ContentType).IsRequired();
        builder.Property(d => d.FileSizeBytes).IsRequired();
        builder.Property(d => d.RawContent).IsRequired();
        builder.Property(d => d.Tags).HasColumnType("text[]").IsRequired();
        builder.Property(d => d.EmbeddingModel).IsRequired();
        builder.Property(d => d.EmbeddingDimensions).IsRequired();
        builder.Property(d => d.ChunkCount).IsRequired();
        builder.Property(d => d.IndexingCostEur).HasColumnType("numeric(10,4)").IsRequired(false);
        builder.Property(d => d.CreatedAt).IsRequired();
        builder.Property(d => d.UpdatedAt).IsRequired();

        builder.Property(d => d.Scope)
            .HasDefaultValue(0);    // 0 = KnowledgeScope.Global

        builder.Property(d => d.RunId)
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.HasOne<RunEntity>()
            .WithMany()
            .HasForeignKey(d => d.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => d.RunId)
            .HasFilter("\"RunId\" IS NOT NULL");

        builder.HasIndex(d => d.Scope);

        builder.HasMany(d => d.Chunks)
            .WithOne(c => c.Document)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
