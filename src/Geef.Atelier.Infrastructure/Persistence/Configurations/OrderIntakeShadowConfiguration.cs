using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class OrderIntakeShadowConfiguration : IEntityTypeConfiguration<OrderIntakeShadowEntity>
{
    public void Configure(EntityTypeBuilder<OrderIntakeShadowEntity> builder)
    {
        builder.ToTable("OrderIntakeShadows");
        builder.HasKey(s => s.OrderId);
        builder.Property(s => s.OrderId).ValueGeneratedNever();

        builder.Property(s => s.Status).HasMaxLength(20).IsRequired();
        builder.Property(s => s.Attempts).IsRequired();
        builder.Property(s => s.ClaimedAt).IsRequired(false);
        builder.Property(s => s.DurationMs).IsRequired(false);
        builder.Property(s => s.QuestionCount).IsRequired(false);
        builder.Property(s => s.RecordJson).HasColumnType("jsonb").IsRequired(false);
        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.CompletedAt).IsRequired(false);

        builder.HasIndex(s => s.CreatedAt)
            .HasFilter("\"Status\" = 'Pending'")
            .HasDatabaseName("IX_OrderIntakeShadows_Pending");

        builder.HasOne<OrderEntity>()
            .WithOne()
            .HasForeignKey<OrderIntakeShadowEntity>(s => s.OrderId)
            .HasConstraintName("FK_OrderIntakeShadows_Orders_OrderId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
