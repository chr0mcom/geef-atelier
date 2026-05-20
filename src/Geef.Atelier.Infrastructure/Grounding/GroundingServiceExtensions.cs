using System.Net.Http;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Grounding;
using Geef.Atelier.Infrastructure.Security;
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

        services.AddSingleton<IUrlSafetyValidator, UrlSafetyValidator>();
        services.AddSingleton<IHtmlContentExtractor, AngleSharpHtmlContentExtractor>();

        services.AddSingleton<IGroundingQueryExtractor, LlmGroundingQueryExtractor>();
        services.AddSingleton<IGroundingProvider, TavilyGroundingProvider>();
        services.AddSingleton<IGroundingProvider, VectorStoreGroundingProvider>();
        services.AddSingleton<IGroundingProvider, StaticContextGroundingProvider>();
        services.AddSingleton<IGroundingProvider, UrlFetchGroundingProvider>();
        services.AddSingleton<IGroundingProvider, NewsSearchGroundingProvider>();
        services.AddSingleton<IGroundingProviderFactory, GroundingProviderFactory>();

        services.AddScoped<IGroundingRefiner, GroundingRefiner>();

        services.AddHttpClient("url-fetch", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Geef.Atelier/1.0 (+url-fetch)");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            MaxAutomaticRedirections = 0,
        });

        return services;
    }
}
