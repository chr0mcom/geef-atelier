using Geef.Atelier.Application.Composition;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>DI registration for the crew composition infrastructure services.</summary>
public static class CompositionServiceExtensions
{
    /// <summary>
    /// Registers crew composition infrastructure services:
    /// <see cref="ICrewSpecValidator"/>, <see cref="CrewSpecValidatorReviewer"/>,
    /// and <see cref="ICrewTemplateEmbeddingRepository"/>.
    /// </summary>
    public static IServiceCollection AddCrewComposition(this IServiceCollection services)
    {
        services.AddScoped<ICrewSpecValidator, CrewSpecValidator>();
        services.AddScoped<ICrewTemplateEmbeddingRepository, CrewTemplateEmbeddingRepository>();
        return services;
    }
}
