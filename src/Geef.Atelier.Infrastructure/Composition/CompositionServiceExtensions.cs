using Geef.Atelier.Application.Composition;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>DI registration for the crew composition infrastructure services.</summary>
public static class CompositionServiceExtensions
{
    /// <summary>
    /// Registers crew composition infrastructure services:
    /// <see cref="ICrewSpecValidator"/>, <see cref="ICrewMaterializer"/>,
    /// <see cref="ICrewTemplateEmbeddingRepository"/>, and <see cref="CrewComposerExecutor"/>.
    /// </summary>
    public static IServiceCollection AddCrewComposition(this IServiceCollection services)
    {
        services.AddScoped<ICrewSpecValidator, CrewSpecValidator>();
        services.AddScoped<ICrewTemplateEmbeddingRepository, CrewTemplateEmbeddingRepository>();
        services.AddScoped<ICrewMaterializer, CrewMaterializer>();
        return services;
    }
}
