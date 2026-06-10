using System.Text.Json;
using Geef.Atelier.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class ToolDefinitionConfiguration : IEntityTypeConfiguration<ToolDefinitionEntity>
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<Dictionary<string, string>> DictComparer = new(
        (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
        v => v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key.GetHashCode(), kv.Value.GetHashCode())),
        v => new Dictionary<string, string>(v));

    public void Configure(EntityTypeBuilder<ToolDefinitionEntity> builder)
    {
        builder.ToTable("tool_definitions");
        builder.HasKey(t => t.Name);

        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Description).IsRequired();
        builder.Property(t => t.ToolType).HasMaxLength(64).IsRequired();
        builder.Property(t => t.SecretRef).HasMaxLength(200).IsRequired(false);
        builder.Property(t => t.AccessClass).IsRequired();
        builder.Property(t => t.IsSystem).IsRequired().HasDefaultValue(false);

        builder.Property(t => t.Settings)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOpts),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOpts) ?? new Dictionary<string, string>(),
                DictComparer)
            .IsRequired()
            .HasDefaultValueSql("'{}'::jsonb");

        builder.Property(t => t.LlmSchemaJson)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValueSql("'{}'::jsonb");
    }
}
