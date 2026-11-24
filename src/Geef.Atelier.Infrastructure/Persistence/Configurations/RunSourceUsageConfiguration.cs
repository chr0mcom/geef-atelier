using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class RunSourceUsageConfiguration : IEntityTypeConfiguration<RunSourceUsageEntity>
{
    public void Configure(EntityTypeBuilder<RunSourceUsageEntity> builder)
    {
        builder.ToTable("RunSourceUsages");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.SourceId).IsRequired(false);
        builder.Property(u => u.FileName).HasMaxLength(255).IsRequired();
        builder.Property(u => u.Mode).HasTextEnumConversion(20, SourceUsageMode.FullText);
        builder.Property(u => u.Target).HasMaxLength(20).IsRequired();
        builder.Property(u => u.CharsIncluded).IsRequired();
        builder.Property(u => u.PagesIncluded).HasMaxLength(200).IsRequired(false);
        builder.Property(u => u.Position).IsRequired();
        builder.Property(u => u.CreatedAt).IsRequired();

        builder.HasIndex(u => u.RunId).HasDatabaseName("IX_RunSourceUsages_RunId");

        builder.HasOne<RunEntity>()
            .WithMany()
            .HasForeignKey(u => u.RunId)
            .HasConstraintName("FK_RunSourceUsages_Runs_RunId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<SourceEntity>()
            .WithMany()
            .HasForeignKey(u => u.SourceId)
            .HasConstraintName("FK_RunSourceUsages_Sources_SourceId")
            .OnDelete(DeleteBehavior.SetNull);
    }
}
