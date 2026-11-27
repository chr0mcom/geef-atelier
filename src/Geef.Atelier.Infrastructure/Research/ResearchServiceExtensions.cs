using Geef.Atelier.Application.Research;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geef.Atelier.Infrastructure.Research;

/// <summary>DI registration of the research before the first run of an order.</summary>
public static class ResearchServiceExtensions
{
    /// <summary>
    /// Binds <see cref="ResearchOptions"/> and <see cref="TavilyOptions"/>, registers the named HTTP client of the search
    /// (<see cref="TavilySearchClient.HttpClientName"/>) and the search, passage picker and research service.
    /// </summary>
    public static IServiceCollection AddResearch(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ResearchOptions>(configuration.GetSection(ResearchOptions.SectionName));
        services.Configure<TavilyOptions>(configuration.GetSection(TavilyOptions.SectionName));
        var endpoint = configuration[$"{TavilyOptions.SectionName}:Endpoint"] ?? new TavilyOptions().Endpoint;
        if (!endpoint.EndsWith('/'))
            endpoint += '/';
        services.AddHttpClient(TavilySearchClient.HttpClientName, client => client.BaseAddress = new Uri(endpoint));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<ITavilySearchClient, TavilySearchClient>();
        services.TryAddScoped<IDossierCondenser, LlmDossierCondenser>();
        services.TryAddScoped<IOrderResearchService, OrderResearchService>();
        return services;
    }
}
