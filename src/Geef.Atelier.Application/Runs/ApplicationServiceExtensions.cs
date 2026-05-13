using Geef.Atelier.Application.Crew;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Application.Runs;

/// <summary>DI registration extensions for the Application layer.</summary>
public static class ApplicationServiceExtensions
{
    /// <summary>Registers all Application-layer services.</summary>
    public static IServiceCollection AddAtelierApplication(this IServiceCollection services)
    {
        services.AddScoped<IRunService, RunService>();
        services.AddScoped<ICrewService, CrewService>();
        return services;
    }
}
