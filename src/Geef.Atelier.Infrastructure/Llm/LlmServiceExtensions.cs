using Geef.Atelier.Application.Crew;
using Geef.Atelier.Infrastructure.Crew;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>DI registration for the multi-provider LLM infrastructure.</summary>
public static class LlmServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ILlmClientResolver"/>, <see cref="IProviderCatalog"/>,
    /// <see cref="IModelCatalog"/>, and a shared named HttpClient "llm".
    /// Returns <see cref="IHttpClientBuilder"/> so callers can chain resilience handlers.
    /// </summary>
    public static IHttpClientBuilder AddLlmClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection("Llm"));
        services.AddSingleton<ILlmClientResolver, LlmClientResolver>();
        services.AddSingleton<IProviderCatalog, ProviderCatalog>();
        services.AddMemoryCache();
        services.AddSingleton<IModelCatalog, ModelCatalog>();

        return services.AddHttpClient("llm", client =>
        {
            client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/chr0mcom/geef-atelier");
            client.DefaultRequestHeaders.Add("X-Title", "Geef.Atelier");
            client.Timeout = TimeSpan.FromMinutes(30);
        });
    }
}
