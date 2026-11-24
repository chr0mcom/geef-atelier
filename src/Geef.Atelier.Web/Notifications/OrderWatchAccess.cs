using System.Security.Claims;
using Geef.Atelier.Application.Orders;

namespace Geef.Atelier.Web.Notifications;

/// <summary>Who may watch which order updates; used by <see cref="IRunUpdateFeed"/>.</summary>
public static class OrderWatchAccess
{
    /// <summary>True when <paramref name="user"/> may see order <paramref name="orderId"/> (owner or administrator).</summary>
    public static async Task<bool> CanWatchOrderAsync(this IOrderService orderService, ClaimsPrincipal? user, Guid orderId, CancellationToken ct = default)
    {
        var name = user.WatcherName();
        if (name is null)
            return false;
        var order = await orderService.GetAsync(orderId, user.IsAtelierAdmin() ? null : name, ct);
        return order is not null;
    }

    /// <summary>The order-list topic for <paramref name="user"/>: all orders for administrators, own orders otherwise.</summary>
    public static string? OrderListTopic(this ClaimsPrincipal? user) =>
        user.IsAtelierAdmin() ? RunUpdateTopics.AdminOrders
        : user.WatcherName() is { } name ? RunUpdateTopics.UserOrders(name)
        : null;
}
