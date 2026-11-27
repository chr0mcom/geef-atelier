using Microsoft.AspNetCore.Antiforgery;

namespace Geef.Atelier.Web.Endpoints;

/// <summary>
/// The antiforgery check of the endpoints that take a plain HTML form and read it themselves. Such an endpoint binds no form
/// parameter, so the antiforgery middleware does not check it and it has to call this check.
/// </summary>
internal static class FormAntiforgeryExtensions
{
    /// <summary>Answer to a form without a valid antiforgery token.</summary>
    public const string OutOfDateReason = "This page is out of date. Go back, reload it and try again.";

    /// <summary><c>null</c> when the form carries the valid token of the signed-in person, otherwise the answer 400.</summary>
    public static async Task<IResult?> RefuseOutdatedFormAsync(this IAntiforgery antiforgery, HttpContext http)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(http);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Text(OutOfDateReason, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
