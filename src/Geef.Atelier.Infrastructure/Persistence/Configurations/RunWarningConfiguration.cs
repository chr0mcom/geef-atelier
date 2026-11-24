using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Warnings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class RunWarningConfiguration : IEntityTypeConfiguration<RunWarningEntity>
{
    public void Configure(EntityTypeBuilder<RunWarningEntity> builder)
    {
        builder.ToTable("RunWarnings");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Iteration).IsRequired(false);
        builder.Property(w => w.Source).HasMaxLength(RunWarningLimits.SourceLength).IsRequired();
        builder.Property(w => w.Code).HasMaxLength(RunWarningLimits.CodeLength).IsRequired();
        builder.Property(w => w.Message).HasColumnType("text").IsRequired();
        builder.Property(w => w.CreatedAt).IsRequired();

        builder.HasIndex(w => w.RunId).HasDatabaseName("IX_RunWarnings_RunId");

        builder.HasOne<RunEntity>()
            .WithMany()
            .HasForeignKey(w => w.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
