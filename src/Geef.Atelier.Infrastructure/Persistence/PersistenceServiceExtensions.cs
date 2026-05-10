using Geef.Atelier.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Persistence;

public static class PersistenceServiceExtensions
{
    public static IServiceCollection AddAtelierPersistence(this IServiceCollection services)
    {
        services.AddScoped<IRunPersistenceService, RunPersistenceService>();
        services.AddScoped<IRunRepository, RunRepository>();
        return services;
    }
}
