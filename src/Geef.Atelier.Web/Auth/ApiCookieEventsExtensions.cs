using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Geef.Atelier.Web.Auth;

/// <summary>Cookie authentication behaviour for JSON endpoints under <c>/api</c>.</summary>
public static class ApiCookieEventsExtensions
{
    /// <summary>The path prefix whose challenges are answered with status codes.</summary>
    public const string ApiPathPrefix = "/api";

    /// <summary>
    /// Answers a challenge with 401 and a forbid with 403 for paths under <see cref="ApiPathPrefix"/> instead of a
    /// redirect, so a <c>fetch</c> caller never receives the login page. Every other path keeps the existing handlers.
    /// </summary>
    public static CookieAuthenticationEvents AnswerApiWithStatusCodes(this CookieAuthenticationEvents events)
    {
        var redirectToLogin = events.OnRedirectToLogin;
        var redirectToAccessDenied = events.OnRedirectToAccessDenied;
        events.OnRedirectToLogin = context => context.IsApiRequest()
            ? context.RespondWith(StatusCodes.Status401Unauthorized)
            : redirectToLogin(context);
        events.OnRedirectToAccessDenied = context => context.IsApiRequest()
            ? context.RespondWith(StatusCodes.Status403Forbidden)
            : redirectToAccessDenied(context);
        return events;
    }

    private static bool IsApiRequest(this RedirectContext<CookieAuthenticationOptions> context) =>
        context.Request.Path.StartsWithSegments(ApiPathPrefix, StringComparison.OrdinalIgnoreCase);

    private static Task RespondWith(this RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}
