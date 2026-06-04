using Geef.Atelier.Application.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>DI registration for the crew composition infrastructure services.</summary>
public static class CompositionServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ICrewSpecValidator"/> and <see cref="CrewSpecValidatorReviewer"/>
    /// with the DI container.
    /// </summary>
    public static IServiceCollection AddCrewComposition(this IServiceCollection services)
    {
        services.AddScoped<ICrewSpecValidator, CrewSpecValidator>();
        return services;
    }
}
