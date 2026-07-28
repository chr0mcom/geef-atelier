using System.Reflection;
using Geef.Atelier.Web.Components.Pages.Admin;
using Microsoft.AspNetCore.Authorization;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class OAuthClientsAccessTests
{
    [Fact]
    public void OAuthClients_RequiresAuthorization()
    {
        Assert.NotNull(typeof(OAuthClients).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void OAuthClients_IsNotRestrictedToAdmins()
    {
        var attr = typeof(OAuthClients).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.True(string.IsNullOrEmpty(attr.Policy),
            "OAuth client self-service must be available to every signed-in user; " +
            "anonymous dynamic client registration (RFC 7591) is already open, so an " +
            "admin-only page adds no security and blocks non-admin users from connecting MCP clients.");
    }
}
