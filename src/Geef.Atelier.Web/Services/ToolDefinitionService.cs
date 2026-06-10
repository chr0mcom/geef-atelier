using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;

namespace Geef.Atelier.Web.Services;

/// <summary>
/// Thin application-layer façade over <see cref="IToolDefinitionRepository"/>,
/// adding guard-rails for system-tool immutability.
/// </summary>
internal sealed class ToolDefinitionService(IToolDefinitionRepository repository) : IToolDefinitionService
{
    /// <inheritdoc/>
    public Task<IReadOnlyList<ToolDefinition>> GetAllAsync(CancellationToken ct = default) =>
        repository.GetAllAsync(ct);

    /// <inheritdoc/>
    public Task<ToolDefinition?> GetByNameAsync(string name, CancellationToken ct = default) =>
        repository.GetByNameAsync(name, ct);

    /// <inheritdoc/>
    public async Task SaveAsync(ToolDefinition tool, CancellationToken ct = default)
    {
        var existing = await repository.GetByNameAsync(tool.Name, ct);
        if (existing?.IsSystem == true)
            throw new InvalidOperationException(
                $"System tool '{tool.Name}' cannot be modified.");

        await repository.UpsertAsync(tool, ct);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        var existing = await repository.GetByNameAsync(name, ct);
        if (existing?.IsSystem == true)
            throw new InvalidOperationException(
                $"System tool '{name}' cannot be deleted.");

        await repository.DeleteAsync(name, ct);
    }
}
