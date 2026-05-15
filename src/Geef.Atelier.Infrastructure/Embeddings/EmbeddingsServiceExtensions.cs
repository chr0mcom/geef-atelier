using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Crew.Knowledge.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Embeddings;

/// <summary>DI registration for the embedding-provider infrastructure.</summary>
public static class EmbeddingsServiceExtensions
{
    public static IServiceCollection AddEmbeddings(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmbeddingsOptions>(configuration.GetSection("Embeddings"));

        var endpoint = configuration["Embeddings:Endpoint"] ?? "https://openrouter.ai/api/v1";
        if (!endpoint.EndsWith('/')) endpoint += '/';
        services.AddHttpClient("embeddings", client =>
            client.BaseAddress = new Uri(endpoint));

        services.AddSingleton<IEmbeddingProvider, OpenRouterEmbeddingProvider>();

        return services;
    }
}
