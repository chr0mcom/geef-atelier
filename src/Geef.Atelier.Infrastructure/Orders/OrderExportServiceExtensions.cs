using Geef.Atelier.Application.Orders;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Orders;

/// <summary>DI registration of the order export.</summary>
public static class OrderExportServiceExtensions
{
    /// <summary>Registers <see cref="IOrderExportService"/>.</summary>
    public static IServiceCollection AddOrderExport(this IServiceCollection services)
    {
        services.AddScoped<IOrderExportService, OrderExportService>();
        return services;
    }
}
