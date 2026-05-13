using Geef.Atelier.Core.Domain.Crew.Advisors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class AdvisorConsultationConfiguration : IEntityTypeConfiguration<AdvisorConsultation>
{
    public void Configure(EntityTypeBuilder<AdvisorConsultation> builder)
    {
        builder.ToTable("AdvisorConsultations");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasColumnType("uuid").IsRequired();
        builder.Property(c => c.RunId).HasColumnType("uuid").IsRequired();
        builder.Property(c => c.IterationNumber).IsRequired();
        builder.Property(c => c.AdvisorProfileName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Output).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasIndex(c => c.RunId).HasDatabaseName("IX_AdvisorConsultations_RunId");
    }
}
