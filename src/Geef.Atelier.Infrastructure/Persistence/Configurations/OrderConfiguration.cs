using Geef.Atelier.Core.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<OrderEntity>
{
    public void Configure(EntityTypeBuilder<OrderEntity> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();

        builder.Property(o => o.CreatedByUser).HasColumnType("text").IsRequired(false);
        builder.Property(o => o.RequestText).HasColumnType("text").IsRequired();
        builder.Property(o => o.Mode).HasTextEnumConversion(20, OrderMode.Careful);
        builder.Property(o => o.State).HasTextEnumConversion(20, OrderState.Ready);
        builder.Property(o => o.Entry).HasTextEnumConversion(20, OrderEntry.Legacy);
        builder.Property(o => o.CrewTemplateName).HasMaxLength(200).IsRequired(false);
        builder.Property(o => o.ClarifyJson).HasColumnType("jsonb").IsRequired(false);
        builder.Property(o => o.AnswersJson).HasColumnType("jsonb").IsRequired(false);
        builder.Property(o => o.ClaimedAt).IsRequired(false);
        builder.Property(o => o.CreatedAt).IsRequired();
        builder.Property(o => o.UpdatedAt).IsRequired();

        builder.HasIndex(o => new { o.CreatedByUser, o.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Orders_CreatedByUser_CreatedAt");
        builder.HasIndex(o => o.State).HasDatabaseName("IX_Orders_State");
    }
}
