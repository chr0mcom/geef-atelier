using Geef.Atelier.Core.Domain.Tools;

namespace Geef.Atelier.Web.Services;

/// <summary>Application-level service for managing <see cref="ToolDefinition"/> catalogue entries.</summary>
public interface IToolDefinitionService
{
    /// <summary>Returns all tool definitions ordered by name.</summary>
    Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Returns the tool with the given <paramref name="name"/>, or <c>null</c> when not found.</summary>
    Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Inserts a new tool definition or replaces an existing one with the same
    /// <see cref="ToolDefinition.Name"/>.
    /// Throws <see cref="InvalidOperationException"/> when attempting to overwrite a system tool.
    /// </summary>
    Task SaveAsync(ToolDefinition tool, CancellationToken ct = default);

    /// <summary>
    /// Removes the custom tool with the given <paramref name="name"/>.
    /// Throws <see cref="InvalidOperationException"/> when the tool is a system tool.
    /// No-op when not found.
    /// </summary>
    Task DeleteAsync(string name, CancellationToken ct = default);
}
