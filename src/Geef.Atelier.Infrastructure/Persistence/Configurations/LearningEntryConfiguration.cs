using Geef.Atelier.Infrastructure.Persistence.Crew.Learning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class LearningEntryConfiguration : IEntityTypeConfiguration<LearningEntryEntity>
{
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

        // Embedding is a vector(1536) column managed exclusively via raw SQL in LearningRepository
        // (EF's value-converter cannot cast string literals to Postgres vector type).
        builder.Ignore(e => e.Embedding);

        builder.HasIndex(e => new { e.Domain, e.Status }).HasDatabaseName("IX_LearningEntries_Domain_Status");
    }
}
