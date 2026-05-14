using Geef.Atelier.Infrastructure.Persistence.TemplateStudio;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class TemplateStudioAnalysisConfiguration : IEntityTypeConfiguration<TemplateStudioAnalysisEntity>
{
    public void Configure(EntityTypeBuilder<TemplateStudioAnalysisEntity> builder)
    {
        builder.ToTable("TemplateStudioAnalyses");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.TaskDescription).IsRequired();
        builder.Property(e => e.AnalysisResultJson).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'::jsonb");
        builder.Property(e => e.InputTokens).IsRequired();
        builder.Property(e => e.OutputTokens).IsRequired();
        builder.Property(e => e.CostEur).HasColumnType("decimal(10,6)").IsRequired(false);
        builder.Property(e => e.MaterializedTemplateName).IsRequired(false);
        builder.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("now()");

        builder.HasIndex(e => e.CreatedAt).IsDescending().HasDatabaseName("IX_TemplateStudioAnalyses_CreatedAt");
    }
}
