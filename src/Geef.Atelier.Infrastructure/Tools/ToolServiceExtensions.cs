using Geef.Atelier.Application.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Tools;

/// <summary>DI registration for the tool execution infrastructure.</summary>
public static class ToolServiceExtensions
{
    /// <summary>Registers <see cref="IToolExecutor"/> and <see cref="IToolSchemaProvider"/>.</summary>
    public static IServiceCollection AddToolExecutor(this IServiceCollection services)
    {
        services.AddScoped<IToolExecutor, ToolExecutor>();
        services.AddScoped<IToolSchemaProvider, ToolSchemaProvider>();
        return services;
    }
}
