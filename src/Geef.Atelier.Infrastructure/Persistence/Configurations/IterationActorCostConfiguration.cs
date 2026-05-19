using Geef.Atelier.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class IterationActorCostConfiguration : IEntityTypeConfiguration<IterationActorCostEntity>
{
    public void Configure(EntityTypeBuilder<IterationActorCostEntity> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.ActorType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(c => c.ActorName)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.ModelName)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(c => c.CostEur)
            .HasColumnType("numeric(10,6)")
            .IsRequired(false);

        builder.Property(c => c.ProviderName)
            .HasColumnType("text")
            .IsRequired(false);

        builder.HasOne(c => c.Iteration)
            .WithMany(i => i.ActorCosts)
            .HasForeignKey(c => c.IterationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasIndex(c => c.IterationId)
            .HasDatabaseName("IX_IterationActorCosts_IterationId");

        builder.ToTable("IterationActorCosts");
    }
}
