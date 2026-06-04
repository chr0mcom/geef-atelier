using Geef.Atelier.Core.Domain;
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

        builder.Property(r => r.ConfigJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(r => r.CreatedByUser).HasColumnType("text").IsRequired(false);

        builder.Property(r => r.CostTotal)
            .HasColumnType("numeric(10,4)");

        builder.Property(r => r.CancellationRequested)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.CrewTemplateName)
            .HasMaxLength(100)
            .IsRequired(false);

        builder.Property(r => r.CrewSnapshot)
            .HasColumnType("jsonb")
            .IsRequired(false);

        builder.Property(r => r.AdvisorRetryAttempted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.TotalCostEur)
            .HasColumnType("numeric(10,6)")
            .IsRequired(false);

        builder.Property(r => r.LlmCostEur)
            .HasColumnType("numeric(10,6)")
            .IsRequired(false);

        builder.Property(r => r.GroundingCostEur)
            .HasColumnType("numeric(10,6)")
            .IsRequired(false);

        builder.HasIndex(r => r.Status);

        builder.Property(r => r.ParentRunId).IsRequired(false);
        builder.Property(r => r.SeedDraftText).IsRequired(false);
        builder.HasIndex(r => r.ParentRunId).HasDatabaseName("IX_Runs_ParentRunId");

        builder.Property(r => r.WordCount).IsRequired(false);
        builder.HasIndex(r => r.CreatedAt).HasDatabaseName("IX_Runs_CreatedAt");
        builder.HasIndex(r => new { r.Status, r.CompletedAt }).HasDatabaseName("IX_Runs_Status_CompletedAt");

        builder.Property(r => r.ParentCompositionRunId).IsRequired(false);
        builder.HasIndex(r => r.ParentCompositionRunId).HasDatabaseName("IX_Runs_ParentCompositionRunId");
    }
}
