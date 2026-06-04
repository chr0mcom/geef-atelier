using Geef.Atelier.Application.Composition;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>DI registration for the crew composition infrastructure services.</summary>
public static class CompositionServiceExtensions
{
    /// <summary>
    /// Registers crew composition infrastructure services, including
    /// <see cref="ICrewTemplateEmbeddingRepository"/>.
    /// </summary>
    public static IServiceCollection AddCrewComposition(this IServiceCollection services)
    {
        services.AddScoped<ICrewTemplateEmbeddingRepository, CrewTemplateEmbeddingRepository>();
        return services;
    }
}
