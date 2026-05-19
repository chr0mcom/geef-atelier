namespace Geef.Atelier.Infrastructure.Persistence.Providers;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

internal sealed class ProviderConfiguration : IEntityTypeConfiguration<ProviderEntity>
{
    public void Configure(EntityTypeBuilder<ProviderEntity> builder)
    {
        builder.ToTable("Providers");
        builder.HasKey(e => e.Name);
        builder.Property(e => e.Name).HasMaxLength(100).IsRequired();
        builder.Property(e => e.DisplayName).IsRequired();
        builder.Property(e => e.Description).IsRequired();
        builder.Property(e => e.Type).IsRequired();
        builder.Property(e => e.Settings).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.IsSystem).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();
    }
}
