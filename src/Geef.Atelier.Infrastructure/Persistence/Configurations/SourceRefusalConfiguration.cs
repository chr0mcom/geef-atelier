using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class SourceRefusalConfiguration : IEntityTypeConfiguration<SourceRefusalEntity>
{
    internal const int PathLength = 16;
    internal const int CodeLength = 40;
    internal const int KindLength = 16;

    private static readonly ValueConverter<RefusalPath, string> PathConverter = new(
        v => v.ToStorageName(),
        v => RefusalPathNames.FromStorageName(v));

    private static readonly ValueConverter<SourceKind?, string?> KindConverter = new(
        v => v.HasValue ? v.Value.ToString() : null,
        v => v == null ? null : EnumNames.ParseExact<SourceKind>(v));

    public void Configure(EntityTypeBuilder<SourceRefusalEntity> builder)
    {
        builder.ToTable("SourceRefusals");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.OwnerUsername).HasColumnType("text").IsRequired();
        builder.Property(r => r.Path).HasConversion(PathConverter).HasMaxLength(PathLength).IsRequired();
        builder.Property(r => r.Code).HasMaxLength(CodeLength).IsRequired();
        builder.Property(r => r.Kind).HasConversion(KindConverter).HasMaxLength(KindLength).IsRequired(false);
        builder.Property(r => r.Extension).HasMaxLength(SourceRefusalCodes.MaxExtensionLength).IsRequired(false);
        builder.Property(r => r.DeclaredType).HasMaxLength(SourceRefusalCodes.MaxDeclaredTypeLength).IsRequired(false);
        builder.Property(r => r.SizeBytes).IsRequired(false);

        builder.HasIndex(r => r.CreatedAt).HasDatabaseName("IX_SourceRefusals_CreatedAt");
    }
}
