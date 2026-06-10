using Geef.Atelier.Core.Domain.Tools;

namespace Geef.Atelier.Core.Persistence.Tools;

/// <summary>Persistence contract for <see cref="ToolDefinition"/> catalogue entries.</summary>
public interface IToolDefinitionRepository
{
    /// <summary>Returns the tool with the given <paramref name="name"/>, or <c>null</c> when not found.</summary>
    Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>Returns all registered tools ordered by name.</summary>
    Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns only built-in system tools (<see cref="ToolDefinition.IsSystem"/> = <c>true</c>).</summary>
    Task<IReadOnlyList<ToolDefinition>> GetSystemToolsAsync(CancellationToken ct = default);

    /// <summary>Returns only user-defined custom tools (<see cref="ToolDefinition.IsSystem"/> = <c>false</c>).</summary>
    Task<IReadOnlyList<ToolDefinition>> GetCustomToolsAsync(CancellationToken ct = default);

    /// <summary>
    /// Inserts a new tool definition or replaces an existing one with the same <see cref="ToolDefinition.Name"/>.
    /// </summary>
    Task UpsertAsync(ToolDefinition tool, CancellationToken ct = default);

    /// <summary>Removes the tool with the given <paramref name="name"/>. No-op when not found.</summary>
    Task DeleteAsync(string name, CancellationToken ct = default);
}
