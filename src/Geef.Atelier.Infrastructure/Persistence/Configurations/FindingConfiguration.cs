using Geef.Atelier.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class FindingConfiguration : IEntityTypeConfiguration<FindingEntity>
{
    public void Configure(EntityTypeBuilder<FindingEntity> builder)
    {
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Severity)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(f => f.ReviewerName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(f => f.Message).IsRequired();

        builder.Property(f => f.Category).HasMaxLength(60).IsRequired(false);
        builder.Property(f => f.Code).HasMaxLength(60).IsRequired(false);
        builder.Property(f => f.Quote).HasColumnType("text").IsRequired(false);

        builder.HasOne<IterationEntity>()
            .WithMany()
            .HasForeignKey(f => f.IterationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
