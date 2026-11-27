namespace Geef.Atelier.Web.Endpoints;

/// <summary>
/// The live connection of the interactive pages (<c>/_blazor</c>) uses the cookie sign-in and takes no antiforgery token,
/// so it is limited to pages of this host.
/// </summary>
internal static class LiveConnectionExtensions
{
    /// <summary>Path prefix of the live connection.</summary>
    public const string PathPrefix = "/_blazor";

    /// <summary>
    /// Answers 400 to a request for the live connection whose <c>Origin</c> header names another host. A request without the
    /// header passes, as on <c>POST /api/sources</c>.
    /// </summary>
    public static IApplicationBuilder UseSameHostLiveConnection(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(PathPrefix) && !SourceEndpoints.OriginMatchesHost(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await next(context);
        });
}
