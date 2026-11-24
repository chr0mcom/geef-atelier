using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Intake;

/// <summary>DI registration of the infrastructure side of the intake.</summary>
public static class IntakeServiceExtensions
{
    /// <summary>Registers the clarifier and the intake worker as hosted service. Empty until the intake is built.</summary>
    public static IServiceCollection AddOrderIntake(this IServiceCollection services) => services;
}
