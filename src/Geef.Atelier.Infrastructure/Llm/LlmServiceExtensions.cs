using Geef.Atelier.Application.Crew;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>DI registration for the multi-provider LLM infrastructure.</summary>
public static class LlmServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ILlmClientResolver"/> and a shared named HttpClient "llm".
    /// Returns <see cref="IHttpClientBuilder"/> so callers can chain resilience handlers.
    /// </summary>
    public static IHttpClientBuilder AddLlmClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection("Llm"));
        services.AddSingleton<ILlmClientResolver, LlmClientResolver>();
        services.AddSingleton<IProviderCatalog, ProviderCatalog>();

        return services.AddHttpClient("llm", client =>
        {
            client.DefaultRequestHeaders.Add("HTTP-Referer", "https://geef.stefan-bechtel.de");
            client.DefaultRequestHeaders.Add("X-Title", "Geef.Atelier");
            client.Timeout = TimeSpan.FromSeconds(120);
        });
    }
}
