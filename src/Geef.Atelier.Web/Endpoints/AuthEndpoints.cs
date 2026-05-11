using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Geef.Atelier.Web.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/logout", async (HttpContext ctx) =>
        {
            try
            {
                await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
            catch (InvalidOperationException)
            {
                // No cookie sign-out handler registered (e.g. test environment uses a custom scheme).
            }
            return Results.Redirect("/login");
        }).RequireAuthorization();

        return app;
    }
}
