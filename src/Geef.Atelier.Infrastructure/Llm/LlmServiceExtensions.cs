using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>
/// DI registration for the OpenAI-compatible LLM client (default: OpenRouter).
/// </summary>
public static class LlmServiceExtensions
{
    /// <summary>
    /// Registers <see cref="ILlmClient"/> with its named HttpClient.
    /// Returns <see cref="IHttpClientBuilder"/> so callers can chain resilience handlers.
    /// </summary>
    public static IHttpClientBuilder AddLlmClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection("Llm"));
        return services.AddHttpClient<ILlmClient, OpenAiCompatibleClient>(client =>
        {
            client.DefaultRequestHeaders.Add("HTTP-Referer", "https://geef.stefan-bechtel.de");
            client.DefaultRequestHeaders.Add("X-Title", "Geef.Atelier");
            client.Timeout = TimeSpan.FromSeconds(120);
        });
    }
}
