using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Crew.Knowledge.Options;
using Geef.Atelier.Core.Domain.Crew.Knowledge.Chunking;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Persistence.Crew.Knowledge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Knowledge;

/// <summary>DI registration for the knowledge-base infrastructure.</summary>
public static class KnowledgeServiceExtensions
{
    public static IServiceCollection AddKnowledge(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<KnowledgeOptions>(configuration.GetSection("Knowledge"));

        services.AddSingleton<RecursiveCharacterTextSplitter>();
        services.AddSingleton<PdfTextExtractor>();
        services.AddScoped<DocumentIndexingService>();
        services.AddScoped<IKnowledgeService, KnowledgeService>();
        services.AddScoped<IKnowledgeDocumentRepository, KnowledgeDocumentRepository>();
        services.AddScoped<IVectorSearchRepository, VectorSearchRepository>();

        return services;
    }
}
