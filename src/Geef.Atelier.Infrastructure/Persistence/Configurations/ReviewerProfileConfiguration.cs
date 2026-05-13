using Geef.Atelier.Core.Domain.Crew.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class ReviewerProfileConfiguration : IEntityTypeConfiguration<ReviewerProfile>
{
    public void Configure(EntityTypeBuilder<ReviewerProfile> builder)
    {
        builder.ToTable("ReviewerProfiles");
        builder.HasKey(r => r.Name);

        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(r => r.Description).IsRequired();
        builder.Property(r => r.SystemPrompt).IsRequired();
        builder.Property(r => r.Provider).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Model).HasMaxLength(200).IsRequired();
        builder.Property(r => r.MaxTokens).IsRequired(false);
        builder.Property(r => r.IsSystem).IsRequired().HasDefaultValue(false);
    }
}
