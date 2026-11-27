using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Application.Security;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Application.Runs;

/// <summary>DI registration extensions for the Application layer.</summary>
public static class ApplicationServiceExtensions
{
    /// <summary>Registers all Application-layer services.</summary>
    public static IServiceCollection AddAtelierApplication(this IServiceCollection services)
    {
        services.AddOptions();
        services.AddSingleton<IEnvReferencePolicy, EnvReferencePolicy>();
        services.AddScoped<IRunService, RunService>();
        services.AddSingleton<IModelAssignmentService, ModelAssignmentService>();
        services.AddScoped<ICrewService, CrewService>();
        services.AddScoped<IProviderService, ProviderService>();
        services.AddAtelierOrders();
        return services;
    }
}
