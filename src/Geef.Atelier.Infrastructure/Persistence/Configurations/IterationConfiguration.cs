using Geef.Atelier.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class IterationConfiguration : IEntityTypeConfiguration<IterationEntity>
{
    public void Configure(EntityTypeBuilder<IterationEntity> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ArtifactText).IsRequired();

        builder.HasIndex(i => i.RunId);

        builder.Property(i => i.ExecutorInputTokens).IsRequired(false);
        builder.Property(i => i.ExecutorOutputTokens).IsRequired(false);

        builder.Property(i => i.ExecutorCostEur)
            .HasColumnType("numeric(10,6)")
            .IsRequired(false);

        builder.Property(i => i.ReviewersTotalCostEur)
            .HasColumnType("numeric(10,6)")
            .IsRequired(false);

        builder.Property(i => i.AdvisorsTotalCostEur)
            .HasColumnType("numeric(10,6)")
            .IsRequired(false);
    }
}
