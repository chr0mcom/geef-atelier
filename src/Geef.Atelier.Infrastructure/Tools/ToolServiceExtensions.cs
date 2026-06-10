using Geef.Atelier.Application.Tools;
using Geef.Atelier.Infrastructure.Mcp;
using Geef.Atelier.Infrastructure.Pipeline;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Tools;

/// <summary>DI registration for the tool execution infrastructure.</summary>
public static class ToolServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IToolExecutor"/>, <see cref="IToolSchemaProvider"/>,
    /// and <see cref="IToolUseRunner"/>.
    /// Depends on <see cref="IAtelierMcpClientFactory"/> being registered (via MCP service extensions).
    /// </summary>
    public static IServiceCollection AddToolExecutor(this IServiceCollection services)
    {
        services.AddScoped<IToolExecutor, ToolExecutor>();
        services.AddScoped<IToolSchemaProvider, ToolSchemaProvider>();
        services.AddScoped<IToolUseRunner, ToolUseRunner>();
        return services;
    }
}
