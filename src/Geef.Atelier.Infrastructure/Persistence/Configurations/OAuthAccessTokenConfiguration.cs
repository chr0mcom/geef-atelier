using Geef.Atelier.Core.Domain.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class OAuthAccessTokenConfiguration : IEntityTypeConfiguration<OAuthAccessToken>
{
    public void Configure(EntityTypeBuilder<OAuthAccessToken> builder)
    {
        builder.ToTable("OAuthAccessTokens");
        builder.HasKey(t => t.TokenHash);

        builder.Property(t => t.TokenHash).HasColumnType("text").IsRequired();
        builder.Property(t => t.ClientId).HasColumnType("text").IsRequired();
        builder.Property(t => t.UserId).HasColumnType("text").IsRequired();
        builder.Property(t => t.Scope).HasColumnType("text").IsRequired();
        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.RevokedAt).IsRequired(false);
        builder.Property(t => t.CreatedAt).IsRequired().HasDefaultValueSql("now()");

        builder.HasOne<OAuthClient>()
            .WithMany()
            .HasForeignKey(t => t.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.ClientId).HasDatabaseName("IX_OAuthAccessTokens_ClientId");
        builder.HasIndex(t => t.UserId).HasDatabaseName("IX_OAuthAccessTokens_UserId");
        builder.HasIndex(t => t.ExpiresAt).HasDatabaseName("IX_OAuthAccessTokens_ExpiresAt");
    }
}
