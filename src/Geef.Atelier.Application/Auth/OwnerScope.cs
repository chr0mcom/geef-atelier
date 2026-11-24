using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace Geef.Atelier.Application.Auth;

/// <summary>
/// The one rule that turns the signed-in identity into the owner filter of owned data (orders, sources, runs).
/// Administrators get <c>null</c> (every owner), everybody else their own user name. A caller who is no administrator
/// and has no user name never gets "every owner": the rule refuses, and the caller answers with
/// <see cref="UnknownUserMessage"/> (pages, MCP tools) or 403 (HTTP endpoints).
/// </summary>
public static class OwnerScope
{
    /// <summary>Role that grants access to the data of every user.</summary>
    public const string AdminRole = "admin";

    /// <summary>Answer for a signed-in caller without a user name who is no administrator.</summary>
    public const string UnknownUserMessage = "We could not tell who you are, so nothing can be shown. Please sign out and sign in again.";

    /// <summary>
    /// Owner filter for a single item: <c>null</c> for administrators, otherwise the user name. Returns false when the
    /// caller is no administrator and has no user name.
    /// </summary>
    public static bool TryGetOwnerFilter(this ICurrentUserService currentUser, out string? ownerFilter) =>
        Resolve(currentUser.IsAdmin, currentUser.Username, showAll: true, out ownerFilter);

    /// <inheritdoc cref="TryGetOwnerFilter(ICurrentUserService, out string?)"/>
    public static bool TryGetOwnerFilter(this ClaimsPrincipal user, out string? ownerFilter) =>
        Resolve(user.IsInRole(AdminRole), user.Identity?.Name, showAll: true, out ownerFilter);

    /// <summary>
    /// Owner filter for a list: <c>null</c> only for administrators who asked for every user, otherwise the own user
    /// name. Returns false when the caller has no user name and does not get every user.
    /// </summary>
    public static bool TryGetListOwnerFilter(this ICurrentUserService currentUser, bool allUsers, out string? ownerFilter) =>
        Resolve(currentUser.IsAdmin, currentUser.Username, allUsers, out ownerFilter);

    /// <inheritdoc cref="TryGetListOwnerFilter(ICurrentUserService, bool, out string?)"/>
    public static bool TryGetListOwnerFilter(this ClaimsPrincipal user, bool allUsers, out string? ownerFilter) =>
        Resolve(user.IsInRole(AdminRole), user.Identity?.Name, allUsers, out ownerFilter);

    /// <summary>The own user name, for data the caller creates; returns false when there is none, also for administrators.</summary>
    public static bool TryGetOwnUsername(this ICurrentUserService currentUser, [NotNullWhen(true)] out string? username)
    {
        username = string.IsNullOrWhiteSpace(currentUser.Username) ? null : currentUser.Username;
        return username is not null;
    }

    private static bool Resolve(bool isAdmin, string? username, bool showAll, out string? ownerFilter)
    {
        if (isAdmin && showAll)
        {
            ownerFilter = null;
            return true;
        }

        ownerFilter = string.IsNullOrWhiteSpace(username) ? null : username;
        return ownerFilter is not null;
    }
}
