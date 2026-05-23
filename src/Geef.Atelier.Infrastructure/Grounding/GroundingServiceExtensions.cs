using System.Net.Http;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Grounding;
using Geef.Atelier.Infrastructure.Grounding.AcademicSearch;
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
        services.AddSingleton<IGroundingProvider, AcademicSearchGroundingProvider>();
        services.AddSingleton<IGroundingProvider, RestApiGroundingProvider>();
        services.AddSingleton<IGroundingProvider, LearningRetrievalGroundingProvider>();
        services.AddSingleton<IGroundingProviderFactory, GroundingProviderFactory>();

        // Academic source adapters
        services.AddSingleton<IAcademicSource, ArxivSource>();
        services.AddSingleton<IAcademicSource, SemanticScholarSource>();
        services.AddSingleton<IAcademicSource, OpenAlexSource>();

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

        // arXiv: moderate rate limits — polite 1 s gap enforced by callers
        services.AddHttpClient("academic-arxiv", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Geef.Atelier/1.0 (+academic-search)");
        });

        // Semantic Scholar: optional API key for higher rate limits
        services.AddHttpClient("academic-semantic-scholar", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Geef.Atelier/1.0 (+academic-search)");
        });

        // OpenAlex: "polite pool" — user-agent with contact email recommended
        services.AddHttpClient("academic-openalex", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Geef.Atelier/1.0 (mailto:geef-atelier@stefan-bechtel.de; +https://geef.stefan-bechtel.de)");
        });

        // Generic REST-API grounding client — no auto-redirect for SSRF safety
        services.AddHttpClient("rest-api-grounding", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Geef.Atelier/1.0 (+rest-api-grounding)");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            MaxAutomaticRedirections = 0,
        });

        return services;
    }
}
