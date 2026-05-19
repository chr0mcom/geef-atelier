using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class RunArtifactConfiguration : IEntityTypeConfiguration<RunArtifact>
{
    public void Configure(EntityTypeBuilder<RunArtifact> builder)
    {
        builder.ToTable("RunArtifacts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).IsRequired();
        builder.Property(a => a.RunId).IsRequired();
        builder.Property(a => a.FinalizerProfileName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.ArtifactType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(a => a.Filename).HasMaxLength(500).IsRequired(false);
        builder.Property(a => a.ContentType).HasMaxLength(200).IsRequired(false);
        builder.Property(a => a.SizeBytes).IsRequired(false);
        builder.Property(a => a.StorageUri).HasMaxLength(2000).IsRequired();
        builder.Property(a => a.StatusMessage).IsRequired(false);
        builder.Property(a => a.CreatedAt).IsRequired();

        builder.HasIndex(a => a.RunId);
    }
}
