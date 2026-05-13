using Geef.Atelier.Application.Crew.Grounding;
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
        services.AddHttpClient<TavilyGroundingProvider>(client =>
            client.BaseAddress = new Uri(endpoint));

        services.AddSingleton<IGroundingProvider, TavilyGroundingProvider>();
        services.AddSingleton<IGroundingProviderFactory, GroundingProviderFactory>();

        return services;
    }
}
