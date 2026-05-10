using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>
/// Minimal DI registration for the Anthropic LLM client. Full infrastructure registration comes in Step 6.
/// </summary>
public static class AnthropicServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IAnthropicClient"/> with its named HttpClient.
    /// Returns <see cref="IHttpClientBuilder"/> so callers can chain resilience handlers.
    /// </summary>
    public static IHttpClientBuilder AddAnthropicClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AnthropicOptions>(configuration.GetSection("Anthropic"));
        return services.AddHttpClient<IAnthropicClient, HttpAnthropicClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.anthropic.com/");
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
            client.Timeout = TimeSpan.FromSeconds(120);
        });
    }
}
