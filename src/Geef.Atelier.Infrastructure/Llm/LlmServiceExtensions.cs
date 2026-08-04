using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Infrastructure.Crew;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        services.Configure<CliProxyOptions>(configuration.GetSection("CliProxy"));
        services.Configure<ModelCatalogOptions>(configuration.GetSection(ModelCatalogOptions.SectionName));
        services.AddSingleton<ILlmClientResolver, LlmClientResolver>();
        services.AddSingleton<IProviderCatalog, ProviderCatalog>();
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ICliModelSource, CliProxyModelSource>();
        services.AddSingleton<IModelCatalog, ModelCatalog>();

        services.AddHttpClient(HttpClientNames.CliProxyModels, ConfigureModelProbeClient);

        return services.AddHttpClient(HttpClientNames.Llm, client =>
        {
            client.DefaultRequestHeaders.Add("HTTP-Referer", "https://github.com/chr0mcom/geef-atelier");
            client.DefaultRequestHeaders.Add("X-Title", "Geef.Atelier");
            client.Timeout = TimeSpan.FromMinutes(30);
        });
    }

    /// <summary>
    /// Configures the model-probe client. Its cap must sit ABOVE the largest per-attempt budget:
    /// a transport cap below a budget makes that budget silently ineffective, so the order
    /// Interactive &lt; Refresh &lt; WarmUp &lt; cap has to hold.
    /// </summary>
    private static void ConfigureModelProbeClient(IServiceProvider services, HttpClient client)
    {
        var options = services.GetRequiredService<IOptions<ModelCatalogOptions>>().Value;
        var largestBudget = Math.Max(
            options.WarmUpTimeoutSeconds,
            Math.Max(options.RefreshTimeoutSeconds, options.InteractiveTimeoutSeconds));

        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, largestBudget) + 30);
    }
}
