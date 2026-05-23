using Geef.Atelier.Infrastructure.Persistence.StudioSettings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class StudioSettingsConfiguration : IEntityTypeConfiguration<StudioSettingsEntity>
{
    public void Configure(EntityTypeBuilder<StudioSettingsEntity> builder)
    {
        builder.ToTable("StudioSettings");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Provider).IsRequired().HasMaxLength(100).HasDefaultValue("");
        builder.Property(e => e.Model).IsRequired().HasMaxLength(200).HasDefaultValue("");
        builder.Property(e => e.MaxTokens).IsRequired().HasDefaultValue(0);
        builder.Property(e => e.UpdatedAt).IsRequired().HasDefaultValueSql("now()");
    }
}
