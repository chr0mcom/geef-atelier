using Geef.Atelier.Application.Intake;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geef.Atelier.Infrastructure.Intake;

/// <summary>DI registration of the infrastructure side of the intake.</summary>
public static class IntakeServiceExtensions
{
    /// <summary>Registers the clarifier and the intake worker as hosted service.</summary>
    public static IServiceCollection AddOrderIntake(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IClarifier, LlmClarifier>();
        services.AddHostedService<OrderIntakeWorker>();
        return services;
    }
}
