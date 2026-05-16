using Geef.Atelier.Core.Domain.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class OAuthClientConfiguration : IEntityTypeConfiguration<OAuthClient>
{
    public void Configure(EntityTypeBuilder<OAuthClient> builder)
    {
        builder.ToTable("OAuthClients");
        builder.HasKey(c => c.ClientId);

        builder.Property(c => c.ClientId).HasColumnType("text").IsRequired();
        builder.Property(c => c.ClientName).HasColumnType("text").IsRequired();
        builder.Property(c => c.RedirectUris).HasColumnType("text[]").IsRequired();
        builder.Property(c => c.ClientSecretHash).HasColumnType("text").IsRequired(false);
        builder.Property(c => c.LogoUri).HasColumnType("text").IsRequired(false);
        builder.Property(c => c.ClientUri).HasColumnType("text").IsRequired(false);
        builder.Property(c => c.IsPublic).IsRequired().HasDefaultValue(true);
        builder.Property(c => c.CreatedAt).IsRequired().HasDefaultValueSql("now()");
        builder.Property(c => c.UpdatedAt).IsRequired().HasDefaultValueSql("now()");
    }
}
