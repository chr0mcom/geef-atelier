using Geef.Atelier.Core.Domain.Mcp;

namespace Geef.Atelier.Infrastructure.Persistence.Entities;

internal sealed class McpServerConfigEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? AuthHeaderEnv { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public McpServerConfig ToDomain() => new()
    {
        Id            = Id,
        Name          = Name,
        Url           = Url,
        AuthHeaderEnv = AuthHeaderEnv,
        IsActive      = IsActive,
        UpdatedAt     = UpdatedAt,
    };

    public static McpServerConfigEntity FromDomain(McpServerConfig d) => new()
    {
        Id            = d.Id,
        Name          = d.Name,
        Url           = d.Url,
        AuthHeaderEnv = d.AuthHeaderEnv,
        IsActive      = d.IsActive,
        UpdatedAt     = d.UpdatedAt,
    };
}
