using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>Signs a bUnit test in with an authenticated identity that has no user name (a broken sign-in).</summary>
internal static class UnknownUserAuthentication
{
    /// <summary>
    /// Replaces the authentication state with an authenticated principal that carries an id but no name claim and no
    /// role. Call after <c>AddTestAuthorization</c>.
    /// </summary>
    public static void SignInWithoutUserName(this TestContext context) =>
        context.Services.AddSingleton<AuthenticationStateProvider>(new NamelessAuthenticationStateProvider());

    private sealed class NamelessAuthenticationStateProvider : AuthenticationStateProvider
    {
        private static readonly AuthenticationState State = new(new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test")));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(State);
    }
}
