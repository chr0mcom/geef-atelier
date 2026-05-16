using Geef.Atelier.Core.Domain.OAuth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class OAuthAuditLogConfiguration : IEntityTypeConfiguration<OAuthAuditLogEntry>
{
    public void Configure(EntityTypeBuilder<OAuthAuditLogEntry> builder)
    {
        builder.ToTable("OAuthAuditLog");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.EventType).HasColumnType("text").IsRequired();
        builder.Property(e => e.ClientId).HasColumnType("text").IsRequired(false);
        builder.Property(e => e.UserId).HasColumnType("text").IsRequired(false);
        builder.Property(e => e.IpAddress).HasColumnType("text").IsRequired(false);
        builder.Property(e => e.UserAgent).HasColumnType("text").IsRequired(false);
        builder.Property(e => e.EventDataJson).HasColumnType("jsonb").IsRequired(false);
        builder.Property(e => e.CreatedAt).IsRequired().HasDefaultValueSql("now()");

        builder.HasIndex(e => e.CreatedAt).IsDescending().HasDatabaseName("IX_OAuthAuditLog_CreatedAt");
        builder.HasIndex(e => e.EventType).HasDatabaseName("IX_OAuthAuditLog_EventType");
    }
}
