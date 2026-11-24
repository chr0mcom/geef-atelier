using Geef.Atelier.Application.Auth;
using ModelContextProtocol;

namespace Geef.Atelier.Mcp.Tools;

/// <summary>Identity and id checks shared by the order tools.</summary>
internal static class McpOrderAccess
{
    public const string OrderNotFound = "Order not found.";

    /// <summary>The user name new orders and sources belong to; fails when nobody is signed in.</summary>
    public static string RequireUsername(this ICurrentUserService currentUser) =>
        string.IsNullOrWhiteSpace(currentUser.Username)
            ? throw new McpException("You are not signed in, so no order can be created.")
            : currentUser.Username;

    /// <summary><c>null</c> for administrators (sees every order), otherwise the own user name.</summary>
    public static string? RequestingUsername(this ICurrentUserService currentUser) =>
        currentUser.IsAdmin ? null : currentUser.Username;

    /// <summary>Parses an order id from a tool argument; an invalid id is reported like an unknown order.</summary>
    public static Guid ParseOrderId(string? orderId) =>
        Guid.TryParse(orderId?.Trim(), out var id) ? id : throw new McpException(OrderNotFound);
}
