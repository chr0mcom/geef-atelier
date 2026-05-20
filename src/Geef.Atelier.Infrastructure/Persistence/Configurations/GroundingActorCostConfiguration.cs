using Geef.Atelier.Core.Domain.Crew;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class GroundingActorCostConfiguration : IEntityTypeConfiguration<GroundingActorCost>
{
    public void Configure(EntityTypeBuilder<GroundingActorCost> builder)
    {
        builder.ToTable("GroundingActorCosts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).IsRequired();
        builder.Property(c => c.RunId).IsRequired();
        builder.Property(c => c.GroundingProviderName).HasMaxLength(500).IsRequired();
        builder.Property(c => c.ActorName).HasMaxLength(500).IsRequired();
        builder.Property(c => c.ProviderName).HasMaxLength(500).IsRequired(false);
        builder.Property(c => c.ModelName).HasMaxLength(500).IsRequired(false);
        builder.Property(c => c.InputTokens).IsRequired();
        builder.Property(c => c.OutputTokens).IsRequired();
        builder.Property(c => c.CostEur).HasColumnType("numeric(12,6)").IsRequired(false);
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasIndex(c => c.RunId);
    }
}
