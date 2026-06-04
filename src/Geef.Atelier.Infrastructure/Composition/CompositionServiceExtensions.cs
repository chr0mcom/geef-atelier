using Geef.Atelier.Application.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>DI registration for the crew composition infrastructure services.</summary>
public static class CompositionServiceExtensions
{
    /// <summary>
    /// Registers composition infrastructure services:
    /// <see cref="ICrewSpecValidator"/>, <see cref="ICrewMaterializer"/>,
    /// and their implementations.
    /// </summary>
    public static IServiceCollection AddCrewComposition(this IServiceCollection services)
    {
        services.AddScoped<ICrewSpecValidator, CrewSpecValidator>();
        services.AddScoped<ICrewMaterializer, CrewMaterializer>();
        return services;
    }
}
