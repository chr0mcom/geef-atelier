using Geef.Atelier.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class FinalizationActorCostConfiguration : IEntityTypeConfiguration<FinalizationActorCost>
{
    public void Configure(EntityTypeBuilder<FinalizationActorCost> builder)
    {
        builder.ToTable("FinalizationActorCosts");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).IsRequired();
        builder.Property(c => c.RunId).IsRequired();
        builder.Property(c => c.ActorName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.ModelName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.InputTokens).IsRequired();
        builder.Property(c => c.OutputTokens).IsRequired();
        builder.Property(c => c.CostEur).HasColumnType("numeric(10,6)").IsRequired(false);
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasIndex(c => c.RunId);
    }
}
