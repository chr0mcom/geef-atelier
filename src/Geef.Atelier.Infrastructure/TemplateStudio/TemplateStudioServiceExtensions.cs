using Geef.Atelier.Application.Crew.TemplateStudio;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.TemplateStudio;

/// <summary>DI registration for Template Studio infrastructure services.</summary>
public static class TemplateStudioServiceExtensions
{
    public static IServiceCollection AddTemplateStudio(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TemplateStudioOptions>(configuration.GetSection("TemplateStudio"));
        services.AddScoped<ProfileSimilarityService>();
        services.AddScoped<ITemplateStudioService, TemplateStudioService>();
        return services;
    }
}
