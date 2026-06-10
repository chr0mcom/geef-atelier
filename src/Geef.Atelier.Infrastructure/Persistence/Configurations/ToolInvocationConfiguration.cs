using Geef.Atelier.Core.Domain.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class ToolInvocationConfiguration : IEntityTypeConfiguration<ToolInvocation>
{
    public void Configure(EntityTypeBuilder<ToolInvocation> builder)
    {
        builder.ToTable("tool_invocations");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).IsRequired();
        builder.Property(t => t.RunId).IsRequired();
        builder.Property(t => t.IterationNumber).IsRequired();

        builder.Property(t => t.ActorType).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ActorName).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ToolName).HasMaxLength(100).IsRequired();
        builder.Property(t => t.ToolType).HasMaxLength(100).IsRequired();

        builder.Property(t => t.InputJson).HasColumnType("text").IsRequired();
        builder.Property(t => t.OutputExcerpt).HasColumnType("text").IsRequired(false);

        builder.Property(t => t.CostEur)
            .HasColumnType("numeric(10,6)")
            .IsRequired(false);

        builder.Property(t => t.DurationMs).IsRequired();
        builder.Property(t => t.Sequence).IsRequired();

        builder.Property(t => t.Outcome)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(t => t.CreatedAt).IsRequired();

        builder.HasIndex(t => t.RunId);
        builder.HasIndex(t => new { t.RunId, t.Sequence })
            .IsUnique()
            .HasDatabaseName("IX_tool_invocations_RunId_Sequence");
    }
}
