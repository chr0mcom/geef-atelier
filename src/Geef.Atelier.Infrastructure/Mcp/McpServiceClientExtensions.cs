using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Mcp;

public static class McpServiceClientExtensions
{
    public static IServiceCollection AddAtelierMcpClient(this IServiceCollection services)
    {
        services.AddHttpClient("mcp-client");
        services.AddSingleton<IAtelierMcpClientFactory, AtelierMcpClientFactory>();
        services.AddScoped<IMcpToolDiscoveryService, McpToolDiscoveryService>();
        return services;
    }
}
