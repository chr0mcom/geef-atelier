using System.Text.Encodings.Web;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Extensions;

namespace Geef.Atelier.Web.Auth;

/// <summary>
/// A page opened from another site arrives without the <c>SameSite=Strict</c> sign-in cookie, also for a signed-in person. A login
/// form shown then could not be sent: its antiforgery token is issued for nobody, while the browser sends the cookie along with the
/// form. So such a request is first answered with a page that opens the same address once more from this site, where the browser
/// sends the cookie. This changes nothing: it applies to <c>GET</c> page navigations only, whose pages only read, and every change
/// still needs its form with the antiforgery token.
/// </summary>
public static class CrossSiteReloadExtensions
{
    /// <summary>Query parameter of the second request; a request that carries it is never answered with the reload again.</summary>
    public const string ReloadMarker = "same_site_reload";

    /// <summary>
    /// Instead of the redirect to the login page, answers a top-level <c>GET</c> navigation that the browser marks as
    /// <c>cross-site</c> with the reload page. Every other challenge keeps the existing handler.
    /// </summary>
    public static CookieAuthenticationEvents ReloadCrossSiteNavigations(this CookieAuthenticationEvents events)
    {
        var redirectToLogin = events.OnRedirectToLogin;
        events.OnRedirectToLogin = context => ReloadTarget(context.Request) is { } target
            ? WriteReloadAsync(context.Response, target)
            : redirectToLogin(context);
        return events;
    }

    /// <summary>
    /// The address to open again: the path and query of <paramref name="request"/> with <see cref="ReloadMarker"/>, or
    /// <c>null</c> when the request gets no reload. The address is always a path on this site.
    /// </summary>
    internal static string? ReloadTarget(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method)
            || request.Headers["Sec-Fetch-Site"] != "cross-site"
            || request.Headers["Sec-Fetch-Mode"] != "navigate"
            || request.Headers["Sec-Fetch-Dest"] != "document"
            || request.Query.ContainsKey(ReloadMarker))
            return null;

        var address = request.GetEncodedPathAndQuery();
        return Login.IsPathOnThisSite(address)
            ? $"{address}{(request.QueryString.HasValue ? '&' : '?')}{ReloadMarker}=1"
            : null;
    }

    private static Task WriteReloadAsync(HttpResponse response, string target)
    {
        var address = HtmlEncoder.Default.Encode(target);
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.CacheControl = "no-store";
        response.Headers.ContentSecurityPolicy = "frame-ancestors 'self'";
        return response.WriteAsync(
            "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
            $"<meta http-equiv=\"refresh\" content=\"0;url={address}\"><title>Geef.Atelier</title></head>" +
            $"<body><p><a href=\"{address}\">Continue to Geef.Atelier</a></p></body></html>");
    }
}
