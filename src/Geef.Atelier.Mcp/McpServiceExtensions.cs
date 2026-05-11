using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp;

public static class McpServiceExtensions
{
    public static IServiceCollection AddAtelierMcp(this IServiceCollection services)
    {
        services
            .AddMcpServer()
            .WithHttpTransport(o => o.Stateless = true)
            .WithToolsFromAssembly(typeof(McpAssemblyMarker).Assembly);
        return services;
    }
}
