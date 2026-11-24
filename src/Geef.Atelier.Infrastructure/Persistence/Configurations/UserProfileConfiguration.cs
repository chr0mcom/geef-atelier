using Geef.Atelier.Core.Domain.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfileEntity>
{
    public void Configure(EntityTypeBuilder<UserProfileEntity> builder)
    {
        builder.ToTable("UserProfiles");
        builder.HasKey(p => p.Username);
        builder.Property(p => p.Username).HasColumnType("text").ValueGeneratedNever();

        builder.Property(p => p.DisplayName).HasMaxLength(UserProfileLimits.DisplayName).IsRequired(false);
        builder.Property(p => p.Role).HasMaxLength(UserProfileLimits.Role).IsRequired(false);
        builder.Property(p => p.Organization).HasMaxLength(UserProfileLimits.Organization).IsRequired(false);
        builder.Property(p => p.Formality).HasTextEnumConversion(20, Formality.Unset);
        builder.Property(p => p.Language).HasMaxLength(UserProfileLimits.Language).IsRequired(false);
        builder.Property(p => p.Signature).HasMaxLength(UserProfileLimits.Signature).IsRequired(false);
        builder.Property(p => p.NoGos).HasMaxLength(UserProfileLimits.NoGos).IsRequired(false);
        builder.Property(p => p.FreeText).HasMaxLength(UserProfileLimits.FreeText).IsRequired(false);
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.UpdatedAt).IsRequired();
    }
}
