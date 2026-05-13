using Geef.Atelier.Core.Domain.Crew.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class ExecutorProfileConfiguration : IEntityTypeConfiguration<ExecutorProfile>
{
    public void Configure(EntityTypeBuilder<ExecutorProfile> builder)
    {
        builder.ToTable("ExecutorProfiles");
        builder.HasKey(e => e.Name);

        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).IsRequired();
        builder.Property(e => e.SystemPrompt).IsRequired();
        builder.Property(e => e.Provider).HasMaxLength(100).IsRequired();
        builder.Property(e => e.Model).HasMaxLength(200).IsRequired();
        builder.Property(e => e.MaxTokens).IsRequired(false);
        builder.Property(e => e.IsSystem).IsRequired().HasDefaultValue(false);
    }
}
