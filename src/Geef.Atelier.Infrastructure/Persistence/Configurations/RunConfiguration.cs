using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class RunConfiguration : IEntityTypeConfiguration<RunEntity>
{
    public void Configure(EntityTypeBuilder<RunEntity> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.BriefingText).IsRequired();

        builder.Property(r => r.CreatedByUser).HasColumnType("text").IsRequired(false);

        builder.Property(r => r.CancellationRequested)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.CrewTemplateName)
            .HasMaxLength(200)
            .IsRequired(false);

        builder.Property(r => r.CrewSnapshot)
            .HasColumnType("jsonb")
            .IsRequired(false);

        builder.Property(r => r.TotalCostEur)
            .HasColumnType("numeric(10,6)")
            .IsRequired(false);

        builder.Property(r => r.LlmCostEur)
            .HasColumnType("numeric(10,6)")
            .IsRequired(false);

        builder.Property(r => r.ParentRunId).IsRequired(false);
        builder.Property(r => r.SeedDraftText).IsRequired(false);
        builder.Property(r => r.RevisionHint).HasColumnType("text").IsRequired(false);

        builder.Property(r => r.WordCount).IsRequired(false);
        builder.HasIndex(r => r.CreatedAt).HasDatabaseName("IX_Runs_CreatedAt");
        builder.HasIndex(r => new { r.CreatedByUser, r.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Runs_CreatedByUser_CreatedAt");
        builder.HasIndex(r => new { r.Status, r.CompletedAt }).HasDatabaseName("IX_Runs_Status_CompletedAt");

        builder.Property(r => r.Outcome).HasConversion<int?>().IsRequired(false);
        builder.Property(r => r.DeliveredIteration).IsRequired(false);
        builder.Property(r => r.ReportJson).HasColumnType("text").IsRequired(false);

        builder.Property(r => r.OrderId).IsRequired(false);
        builder.Property(r => r.OrderVersion).IsRequired(false);
        builder.Property(r => r.FactCheckMode).HasMaxLength(20).IsRequired(false);
        builder.HasIndex(r => new { r.OrderId, r.OrderVersion })
            .IsUnique()
            .HasDatabaseName("UX_Runs_OrderId_OrderVersion");
        builder.HasOne<OrderEntity>()
            .WithMany()
            .HasForeignKey(r => r.OrderId)
            .HasConstraintName("FK_Runs_Orders_OrderId")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
