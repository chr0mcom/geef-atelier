using Geef.Atelier.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class IterationConfiguration : IEntityTypeConfiguration<IterationEntity>
{
    public void Configure(EntityTypeBuilder<IterationEntity> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.ArtifactText).IsRequired();

        builder.HasIndex(i => i.RunId);
    }
}
