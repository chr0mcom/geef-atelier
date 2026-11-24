using System.Security.Claims;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Tests.Mcp.Orders;

namespace Geef.Atelier.Tests.Application.Auth;

/// <summary>
/// The owner rule: administrators see every owner, everybody else only their own data, and a caller without a user
/// name who is no administrator is refused instead of getting "every owner" (fail closed).
/// </summary>
public sealed class OwnerScopeTests
{
    public static TheoryData<bool, string?, bool, string?> SingleItemCases => new()
    {
        { true, "root", true, null },
        { true, null, true, null },
        { true, "", true, null },
        { false, "alice", true, "alice" },
        { false, null, false, null },
        { false, "", false, null },
        { false, "   ", false, null },
    };

    public static TheoryData<bool, string?, bool, bool, string?> ListCases => new()
    {
        { true, "root", true, true, null },
        { true, "root", false, true, "root" },
        { true, null, true, true, null },
        { true, null, false, false, null },
        { false, "alice", true, true, "alice" },
        { false, "alice", false, true, "alice" },
        { false, null, true, false, null },
        { false, "", false, false, null },
    };

    [Theory]
    [MemberData(nameof(SingleItemCases))]
    public void The_single_item_filter_never_opens_up_for_callers_without_a_name(bool isAdmin, string? username, bool allowed, string? expected)
    {
        Assert.Equal(allowed, new FakeUser(username, isAdmin).TryGetOwnerFilter(out var fromService));
        Assert.Equal(expected, fromService);

        Assert.Equal(allowed, Principal(username, isAdmin).TryGetOwnerFilter(out var fromPrincipal));
        Assert.Equal(expected, fromPrincipal);
    }

    [Theory]
    [MemberData(nameof(ListCases))]
    public void The_list_filter_gives_every_user_only_to_administrators_who_ask_for_it(bool isAdmin, string? username, bool allUsers, bool allowed, string? expected)
    {
        Assert.Equal(allowed, new FakeUser(username, isAdmin).TryGetListOwnerFilter(allUsers, out var fromService));
        Assert.Equal(expected, fromService);

        Assert.Equal(allowed, Principal(username, isAdmin).TryGetListOwnerFilter(allUsers, out var fromPrincipal));
        Assert.Equal(expected, fromPrincipal);
    }

    [Theory]
    [InlineData("alice", false, true)]
    [InlineData("root", true, true)]
    [InlineData(null, true, false)]
    [InlineData("", false, false)]
    [InlineData(" ", true, false)]
    public void The_own_name_is_required_for_new_data_also_from_administrators(string? username, bool isAdmin, bool allowed)
    {
        Assert.Equal(allowed, new FakeUser(username, isAdmin).TryGetOwnUsername(out var own));
        Assert.Equal(allowed ? username : null, own);
    }

    private static ClaimsPrincipal Principal(string? username, bool isAdmin)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) };
        if (username is not null)
            claims.Add(new Claim(ClaimTypes.Name, username));
        if (isAdmin)
            claims.Add(new Claim(ClaimTypes.Role, OwnerScope.AdminRole));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
