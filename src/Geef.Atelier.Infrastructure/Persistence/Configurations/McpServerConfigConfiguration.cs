using Geef.Atelier.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Geef.Atelier.Infrastructure.Persistence.Configurations;

internal sealed class McpServerConfigConfiguration : IEntityTypeConfiguration<McpServerConfigEntity>
{
    public void Configure(EntityTypeBuilder<McpServerConfigEntity> builder)
    {
        builder.ToTable("mcp_server_configs");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Url).HasMaxLength(500).IsRequired();
        builder.Property(e => e.AuthHeaderEnv).HasMaxLength(200);
    }
}
