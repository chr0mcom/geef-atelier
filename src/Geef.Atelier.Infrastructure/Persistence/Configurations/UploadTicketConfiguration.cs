using Geef.Atelier.Core.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class UploadTicketConfiguration : IEntityTypeConfiguration<UploadTicketEntity>
{
    internal const int StateLength = 16;
    internal const int FileNameLength = 255;
    internal const int LastRefusalCodeLength = 40;

    public void Configure(EntityTypeBuilder<UploadTicketEntity> builder)
    {
        builder.ToTable("UploadTickets");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.OwnerUsername).HasColumnType("text").IsRequired();
        builder.Property(t => t.FileName).HasMaxLength(FileNameLength).IsRequired();
        builder.Property(t => t.DeclaredBytes).IsRequired(false);
        builder.Property(t => t.TokenHash).HasColumnType("character(64)").IsRequired();
        builder.Property(t => t.State).HasTextEnumConversion(StateLength, UploadTicketState.Failed);
        builder.Property(t => t.Attempts).IsRequired().HasDefaultValue(0);
        builder.Property(t => t.SourceId).IsRequired(false);
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.TokenExpiresAt).IsRequired();
        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.ReceivingSince).IsRequired(false);
        builder.Property(t => t.UsedAt).IsRequired(false);
        builder.Property(t => t.LastRefusalCode).HasMaxLength(LastRefusalCodeLength).IsRequired(false);

        builder.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("UX_UploadTickets_TokenHash");
        builder.HasIndex(t => new { t.OwnerUsername, t.State }).HasDatabaseName("IX_UploadTickets_OwnerUsername_State");
        builder.HasIndex(t => t.ExpiresAt).HasDatabaseName("IX_UploadTickets_ExpiresAt");

        builder.HasOne<SourceEntity>()
            .WithMany()
            .HasForeignKey(t => t.SourceId)
            .HasConstraintName("FK_UploadTickets_Sources_SourceId")
            .OnDelete(DeleteBehavior.SetNull);
    }
}
