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

        // Explicit FK so EF Core orders inserts correctly (Step29 added DB constraint).
        builder.HasOne<IterationEntity>()
            .WithMany()
            .HasForeignKey(f => f.IterationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
