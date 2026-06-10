using Geef.Atelier.Core.Domain.Mcp;

namespace Geef.Atelier.Core.Persistence.Mcp;

/// <summary>Persistence contract for <see cref="McpServerConfig"/> entries.</summary>
public interface IMcpServerConfigRepository
{
    Task<IReadOnlyList<McpServerConfig>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<McpServerConfig>> GetActiveAsync(CancellationToken ct = default);
    Task<McpServerConfig?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task UpsertAsync(McpServerConfig config, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
