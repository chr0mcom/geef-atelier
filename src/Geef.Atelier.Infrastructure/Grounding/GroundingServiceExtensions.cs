using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Grounding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>DI registration for grounding-provider infrastructure.</summary>
public static class GroundingServiceExtensions
{
    public static IServiceCollection AddGroundingProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TavilyOptions>(configuration.GetSection("Tavily"));

        var endpoint = configuration["Tavily:Endpoint"] ?? "https://api.tavily.com";
        if (!endpoint.EndsWith('/')) endpoint += '/';
        services.AddHttpClient("tavily", client =>
            client.BaseAddress = new Uri(endpoint));

        services.AddSingleton<IGroundingQueryExtractor, LlmGroundingQueryExtractor>();
        services.AddSingleton<IGroundingProvider, TavilyGroundingProvider>();
        services.AddSingleton<IGroundingProvider, VectorStoreGroundingProvider>();
        services.AddSingleton<IGroundingProviderFactory, GroundingProviderFactory>();

        services.AddScoped<IGroundingRefiner, GroundingRefiner>();

        return services;
    }
}
