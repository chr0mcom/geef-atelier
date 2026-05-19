using System.ComponentModel;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class GetProviderModelsTool
{
    [McpServerTool, Description("Returns the list of available models for a given provider. Results are cached for 24 hours and fall back to a static list when the provider endpoint is unreachable.")]
    public static async Task<ProviderModelsDto> GetProviderModels(
        IModelCatalog modelCatalog,
        [Description("The provider name (e.g. 'openrouter', 'codex-cli', 'custom-myllm').")] string provider_name,
        CancellationToken cancellationToken = default)
    {
        var models = await modelCatalog.ListModelsAsync(provider_name, cancellationToken);
        return new ProviderModelsDto(
            provider_name,
            models.Select(m => m.Id).ToList());
    }
}
