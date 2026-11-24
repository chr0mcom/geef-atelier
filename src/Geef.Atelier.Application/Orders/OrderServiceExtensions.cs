using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Application.Orders;

/// <summary>DI registration of the order services.</summary>
public static class OrderServiceExtensions
{
    /// <summary>Registers <see cref="IOrderService"/>, <see cref="IOrderBriefComposer"/> and the run starter of the intake. <see cref="Sources.ISourceIntakeService"/> comes from the infrastructure (<c>AddSources</c>).</summary>
    public static IServiceCollection AddAtelierOrders(this IServiceCollection services)
    {
        services.AddOptions();
        services.AddSingleton<IOrderBriefComposer, IntakeBriefComposer>();
        services.AddScoped<IOrderRunStarter, OrderRunStarter>();
        services.AddScoped<IOrderService, OrderService>();
        return services;
    }
}
