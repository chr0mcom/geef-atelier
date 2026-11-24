using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace Geef.Atelier.Web.Auth;

/// <summary>Authorization policies of the web application.</summary>
public static class AuthorizationPolicyExtensions
{
    /// <summary>Name of the policy for administrator pages (signed in with the cookie and in the role <c>admin</c>).</summary>
    public const string AdminPolicy = "AdminPolicy";

    /// <summary>Adds the MCP bearer policy and <see cref="AdminPolicy"/>.</summary>
    public static AuthorizationOptions AddAtelierPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(McpAuthorizationConstants.McpPolicy, p =>
        {
            p.AuthenticationSchemes = [McpAuthorizationConstants.BearerScheme];
            p.RequireAuthenticatedUser();
        });
        options.AddPolicy(AdminPolicy, p =>
        {
            p.AuthenticationSchemes = [CookieAuthenticationDefaults.AuthenticationScheme];
            p.RequireRole("admin");
        });
        return options;
    }
}
