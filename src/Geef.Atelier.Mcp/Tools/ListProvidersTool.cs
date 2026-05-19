using System.ComponentModel;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class ListProvidersTool
{
    [McpServerTool, Description("Lists all registered LLM providers (system built-ins and custom). By default only active providers are returned.")]
    public static async Task<IReadOnlyList<ProviderDto>> ListProviders(
        IProviderService providerService,
        [Description("When true, inactive custom providers are included in the result. Defaults to false.")] bool include_inactive = false,
        CancellationToken cancellationToken = default)
    {
        var providers = await providerService.ListAsync(includeInactive: include_inactive, ct: cancellationToken);
        return providers
            .Select(p => new ProviderDto(
                p.Name,
                p.DisplayName,
                p.Description,
                p.Type.ToString(),
                p.IsSystem,
                p.IsActive))
            .ToList();
    }
}
