using System.Text.Json;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class SourceConfiguration : IEntityTypeConfiguration<SourceEntity>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<IReadOnlyList<SourceWarning>> WarningsComparer = new(
        (a, b) => Serialize(a) == Serialize(b),
        v => Serialize(v).GetHashCode(StringComparison.Ordinal),
        v => Deserialize(Serialize(v)));

    public void Configure(EntityTypeBuilder<SourceEntity> builder)
    {
        builder.ToTable("Sources");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.OwnerUsername).HasColumnType("text").IsRequired();
        builder.Property(s => s.FileName).HasMaxLength(255).IsRequired();
        builder.Property(s => s.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Kind).HasTextEnumConversion(20, SourceKind.Text);
        builder.Property(s => s.SizeBytes).IsRequired();
        builder.Property(s => s.Sha256).HasColumnType("character(64)").IsRequired();
        builder.Property(s => s.StoragePath).HasMaxLength(500).IsRequired(false);
        builder.Property(s => s.ExtractedText).HasColumnType("text").IsRequired(false);
        builder.Property(s => s.PageCount).IsRequired(false);
        builder.Property(s => s.CharCount).IsRequired();
        builder.Property(s => s.WordCount).IsRequired();
        builder.Property(s => s.Quality).HasTextEnumConversion(20, SourceQuality.Poor);
        builder.Property(s => s.Warnings)
            .HasColumnName("WarningsJson")
            .HasColumnType("jsonb")
            .HasConversion(v => Serialize(v), v => Deserialize(v), WarningsComparer)
            .IsRequired();
        builder.Property(s => s.Origin).HasTextEnumConversion(20, SourceOrigin.WebUpload);
        builder.Property(s => s.InLibrary).IsRequired().HasDefaultValue(false);
        builder.Property(s => s.AlwaysInclude).IsRequired().HasDefaultValue(false);
        builder.Property(s => s.ExpiresAt).IsRequired(false);
        builder.Property(s => s.CreatedAt).IsRequired();

        builder.HasIndex(s => new { s.OwnerUsername, s.Sha256 }).IsUnique().HasDatabaseName("UX_Sources_OwnerUsername_Sha256");
        builder.HasIndex(s => s.ExpiresAt)
            .HasFilter("\"ExpiresAt\" IS NOT NULL")
            .HasDatabaseName("IX_Sources_ExpiresAt");
    }

    internal static string Serialize(IReadOnlyList<SourceWarning>? warnings) =>
        JsonSerializer.Serialize(warnings ?? [], JsonOptions);

    internal static IReadOnlyList<SourceWarning> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<SourceWarning>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
