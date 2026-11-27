using Geef.Atelier.Core.Domain.Intake;
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
        builder.Property(o => o.State).HasTextEnumConversion(20, OrderState.Ready);
        builder.Property(o => o.Entry).HasTextEnumConversion(20, OrderEntry.Web);
        builder.Property(o => o.ClarifyJson).HasColumnType("jsonb").IsRequired(false);
        builder.Property(o => o.AnswersJson).HasColumnType("jsonb").IsRequired(false);
        builder.Property(o => o.ClaimedAt).IsRequired(false);
        builder.Property(o => o.IntakeOutcome).HasTolerantEnumConversion<IntakeOutcome>().HasMaxLength(20);
        builder.Property(o => o.IntakeAttempts).IsRequired();
        builder.Property(o => o.IntakeStartedAt).IsRequired(false);
        builder.Property(o => o.IntakeFinishedAt).IsRequired(false);
        builder.Property(o => o.QuestionsAskedAt).IsRequired(false);
        builder.Property(o => o.AnsweredAt).IsRequired(false);
        builder.Property(o => o.ResearchJson).HasColumnType("text").IsRequired(false);
        builder.Property(o => o.ResearchAt).IsRequired(false);
        builder.Property(o => o.CreatedAt).IsRequired();
        builder.Property(o => o.UpdatedAt).IsRequired();

        builder.HasIndex(o => new { o.CreatedByUser, o.UpdatedAt, o.CreatedAt })
            .IsDescending(false, true, true)
            .HasDatabaseName("IX_Orders_CreatedByUser_UpdatedAt");
        builder.HasIndex(o => o.ResearchAt)
            .HasFilter("\"ResearchAt\" IS NOT NULL")
            .HasDatabaseName("IX_Orders_ResearchAt");
        builder.HasIndex(o => o.CreatedAt)
            .HasFilter("\"State\" = 'Analyzing'")
            .HasDatabaseName("IX_Orders_Intake_Analyzing");
        builder.HasIndex(o => o.QuestionsAskedAt)
            .HasFilter("\"State\" = 'NeedsInput'")
            .HasDatabaseName("IX_Orders_Intake_NeedsInput");
    }
}
