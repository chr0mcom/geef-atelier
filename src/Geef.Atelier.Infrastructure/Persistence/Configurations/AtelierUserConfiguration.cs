using Geef.Atelier.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class AtelierUserConfiguration : IEntityTypeConfiguration<AtelierUser>
{
    public void Configure(EntityTypeBuilder<AtelierUser> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.UserId);

        builder.Property(u => u.UserId).HasColumnType("text").IsRequired();
        builder.Property(u => u.Username).HasColumnType("text").IsRequired();
        builder.Property(u => u.PasswordHash).HasColumnType("text").IsRequired();
        builder.Property(u => u.Email).HasColumnType("text").IsRequired(false);
        builder.Property(u => u.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(u => u.IsAdmin).IsRequired().HasDefaultValue(false);
        builder.Property(u => u.CreatedAt).IsRequired().HasDefaultValueSql("now()");
        builder.Property(u => u.UpdatedAt).IsRequired().HasDefaultValueSql("now()");

        builder.HasIndex(u => u.Username).IsUnique();
    }
}
