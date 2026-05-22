using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Learning;
using Geef.Atelier.Application.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Application.Runs;

/// <summary>DI registration extensions for the Application layer.</summary>
public static class ApplicationServiceExtensions
{
    /// <summary>Registers all Application-layer services.</summary>
    public static IServiceCollection AddAtelierApplication(this IServiceCollection services)
    {
        // IHttpClientFactory is needed by ProviderService.TestConnectionAsync.
        // AddHttpClient() is idempotent and safe to call multiple times.
        services.AddHttpClient();
        services.AddScoped<IRunService, RunService>();
        services.AddScoped<ICrewService, CrewService>();
        services.AddScoped<IProviderService, ProviderService>();
        services.AddScoped<ILearningService, LearningService>();
        return services;
    }
}
