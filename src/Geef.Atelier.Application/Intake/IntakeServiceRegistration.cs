using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Application.Intake;

/// <summary>DI registration of the application side of the intake.</summary>
public static class IntakeServiceRegistration
{
    /// <summary>Registers the intake service, the memory resolution and the worker signal. Empty until the intake is built.</summary>
    public static IServiceCollection AddOrderIntakeServices(this IServiceCollection services) => services;
}
