using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geef.Atelier.Application.Intake;

/// <summary>DI registration of the application side of the intake.</summary>
public static class IntakeServiceRegistration
{
    /// <summary>Registers the intake service, the memory resolution and the worker signal. Repositories and the run starter come from their own registrations.</summary>
    public static IServiceCollection AddOrderIntakeServices(this IServiceCollection services)
    {
        services.AddOptions();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IOrderIntakeSignal, ChannelOrderIntakeSignal>();
        services.TryAddScoped<IMemoryResolution, ProfileMemoryResolution>();
        services.TryAddScoped<IOrderIntakeService, OrderIntakeService>();
        return services;
    }
}
