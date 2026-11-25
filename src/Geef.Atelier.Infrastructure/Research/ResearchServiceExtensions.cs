using Geef.Atelier.Application.Research;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geef.Atelier.Infrastructure.Research;

/// <summary>DI registration of the research before the first run of an order.</summary>
public static class ResearchServiceExtensions
{
    /// <summary>Binds <see cref="ResearchOptions"/> and registers search, passage picker and research service; the Tavily client comes from the grounding registration.</summary>
    public static IServiceCollection AddResearch(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ResearchOptions>(configuration.GetSection(ResearchOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ITavilySearchClient, TavilySearchClient>();
        services.TryAddScoped<IDossierCondenser, LlmDossierCondenser>();
        services.TryAddScoped<IOrderResearchService, OrderResearchService>();
        return services;
    }
}
