using Geef.Atelier.Core.Domain.Crew.Advisors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class AdvisorProfileConfiguration : IEntityTypeConfiguration<AdvisorProfile>
{
    public void Configure(EntityTypeBuilder<AdvisorProfile> builder)
    {
        builder.ToTable("AdvisorProfiles");
        builder.HasKey(a => a.Name);

        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Description).IsRequired();
        builder.Property(a => a.SystemPrompt).IsRequired();
        builder.Property(a => a.Provider).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Model).HasMaxLength(200).IsRequired();
        builder.Property(a => a.MaxTokens).IsRequired(false);
        builder.Property(a => a.Mode)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(a => a.Trigger)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(a => a.IsSystem).IsRequired().HasDefaultValue(false);
    }
}
