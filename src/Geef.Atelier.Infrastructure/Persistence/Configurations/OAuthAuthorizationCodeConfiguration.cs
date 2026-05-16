using Geef.Atelier.Core.Domain.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class OAuthAuthorizationCodeConfiguration : IEntityTypeConfiguration<OAuthAuthorizationCode>
{
    public void Configure(EntityTypeBuilder<OAuthAuthorizationCode> builder)
    {
        builder.ToTable("OAuthAuthorizationCodes");
        builder.HasKey(c => c.CodeHash);

        builder.Property(c => c.CodeHash).HasColumnType("text").IsRequired();
        builder.Property(c => c.ClientId).HasColumnType("text").IsRequired();
        builder.Property(c => c.UserId).HasColumnType("text").IsRequired();
        builder.Property(c => c.RedirectUri).HasColumnType("text").IsRequired();
        builder.Property(c => c.Scope).HasColumnType("text").IsRequired();
        builder.Property(c => c.CodeChallenge).HasColumnType("text").IsRequired();
        builder.Property(c => c.CodeChallengeMethod).HasColumnType("text").IsRequired();
        builder.Property(c => c.ExpiresAt).IsRequired();
        builder.Property(c => c.UsedAt).IsRequired(false);
        builder.Property(c => c.CreatedAt).IsRequired().HasDefaultValueSql("now()");

        builder.HasOne<OAuthClient>()
            .WithMany()
            .HasForeignKey(c => c.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => c.ExpiresAt).HasDatabaseName("IX_OAuthAuthorizationCodes_ExpiresAt");
    }
}
