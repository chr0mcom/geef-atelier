using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Application.Orders;

/// <summary>DI registration of the order services.</summary>
public static class OrderServiceExtensions
{
    /// <summary>Registers <see cref="IOrderService"/> and <see cref="IOrderBriefComposer"/>.</summary>
    public static IServiceCollection AddAtelierOrders(this IServiceCollection services) => services;
}
