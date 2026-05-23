using Geef.Atelier.Infrastructure.Persistence.SiteSettings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class SiteSettingsConfiguration : IEntityTypeConfiguration<SiteSettingsEntity>
{
    public void Configure(EntityTypeBuilder<SiteSettingsEntity> builder)
    {
        builder.ToTable("SiteSettings");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.OperatorName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.AddressStreet).IsRequired().HasMaxLength(300);
        builder.Property(e => e.AddressZip).IsRequired().HasMaxLength(20);
        builder.Property(e => e.AddressCity).IsRequired().HasMaxLength(100);
        builder.Property(e => e.AddressCountry).IsRequired().HasMaxLength(100);
        builder.Property(e => e.ContactEmail).IsRequired().HasMaxLength(300);
        builder.Property(e => e.ContactPhone).HasMaxLength(100);
        builder.Property(e => e.ResponsiblePerson).HasMaxLength(300);
        builder.Property(e => e.VatId).HasMaxLength(50);
        builder.Property(e => e.RegisterInfo).HasMaxLength(300);
        builder.Property(e => e.SupervisoryAuthority).HasMaxLength(300);
        builder.Property(e => e.Jurisdiction).HasMaxLength(100);
        builder.Property(e => e.PrivacyAppendMarkdown);
        builder.Property(e => e.TermsAppendMarkdown);
        builder.Property(e => e.LegalBoilerplateAccepted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.UpdatedAt).IsRequired().HasDefaultValueSql("now()");
    }
}
