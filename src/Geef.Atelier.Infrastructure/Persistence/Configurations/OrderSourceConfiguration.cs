using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class OrderSourceConfiguration : IEntityTypeConfiguration<OrderSourceEntity>
{
    public void Configure(EntityTypeBuilder<OrderSourceEntity> builder)
    {
        builder.ToTable("OrderSources");
        builder.HasKey(l => new { l.OrderId, l.SourceId });

        builder.Property(l => l.Role).HasTextEnumConversion(30, SourceRole.Facts);
        builder.Property(l => l.Position).IsRequired();
        builder.Property(l => l.PageSelection).HasMaxLength(200).IsRequired(false);
        builder.Property(l => l.AddedAt).IsRequired();

        builder.HasIndex(l => l.SourceId).HasDatabaseName("IX_OrderSources_SourceId");

        builder.HasOne<OrderEntity>()
            .WithMany()
            .HasForeignKey(l => l.OrderId)
            .HasConstraintName("FK_OrderSources_Orders_OrderId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<SourceEntity>()
            .WithMany()
            .HasForeignKey(l => l.SourceId)
            .HasConstraintName("FK_OrderSources_Sources_SourceId")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
