using Geef.Atelier.Application.Auth;
using ModelContextProtocol;

namespace Geef.Atelier.Mcp.Tools;

/// <summary>Identity and id checks shared by the order and run tools.</summary>
internal static class McpOrderAccess
{
    public const string OrderNotFound = "Order not found.";

    /// <summary>The user name new orders and sources belong to; fails when nobody is signed in.</summary>
    public static string RequireUsername(this ICurrentUserService currentUser) =>
        currentUser.TryGetOwnUsername(out var username)
            ? username
            : throw new McpException("You are not signed in, so no order can be created.");

    /// <summary>
    /// <c>null</c> for administrators (sees everything), otherwise the own user name (<see cref="OwnerScope"/>); fails
    /// when a caller who is no administrator has no user name.
    /// </summary>
    public static string? RequestingUsername(this ICurrentUserService currentUser) =>
        currentUser.TryGetOwnerFilter(out var owner) ? owner : throw new McpException(OwnerScope.UnknownUserMessage);

    /// <summary>
    /// Owner filter of a list: <c>null</c> only for administrators who asked for every user, otherwise the own user
    /// name (<see cref="OwnerScope"/>); fails when the caller has no user name.
    /// </summary>
    public static string? ListOwner(this ICurrentUserService currentUser, bool allUsers) =>
        currentUser.TryGetListOwnerFilter(allUsers, out var owner) ? owner : throw new McpException(OwnerScope.UnknownUserMessage);

    /// <summary>Parses an order id from a tool argument; an invalid id is reported like an unknown order.</summary>
    public static Guid ParseOrderId(string? orderId) =>
        Guid.TryParse(orderId?.Trim(), out var id) ? id : throw new McpException(OrderNotFound);
}
