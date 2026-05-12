using Microsoft.AspNetCore.Mvc;

namespace Geef.Atelier.Web.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/settings/theme", (HttpContext ctx, [FromForm] string value) =>
        {
            if (value is not ("vellum" or "noir" or "petrol"))
                return Results.BadRequest();

            ctx.Response.Cookies.Append("Atelier.Theme", value, new CookieOptions
            {
                Path     = "/",
                MaxAge   = TimeSpan.FromDays(365),
                SameSite = SameSiteMode.Strict,
                Secure   = !ctx.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
                HttpOnly = false,
            });

            var referer = ctx.Request.Headers.Referer.ToString();
            return Results.Redirect(string.IsNullOrEmpty(referer) ? "/" : referer);
        })
        .RequireAuthorization()
        .DisableAntiforgery();

        return app;
    }
}
