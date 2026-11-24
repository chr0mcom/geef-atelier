using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;

namespace Geef.Atelier.Web.Endpoints;

/// <summary>
/// HTTP endpoints for orders: <c>GET /orders/{id}/export/{format}</c> with <c>docx</c>, <c>pdf</c> or <c>md</c> and an
/// optional <c>?version=n</c>. A signed-in caller without a user name who is no administrator gets 403.
/// </summary>
public static class OrderEndpoints
{
    /// <summary>Maps the order endpoints.</summary>
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/orders/{orderId:guid}/export/{format}", async (
                Guid orderId,
                string format,
                int? version,
                IOrderExportService exports,
                ICurrentUserService currentUser,
                CancellationToken ct) =>
            {
                if (!ExportFormatExtensions.TryParseRoute(format, out var exportFormat))
                    return Results.BadRequest();

                if (!currentUser.TryGetOwnerFilter(out var requestingUsername))
                    return Results.Problem(OwnerScope.UnknownUserMessage, statusCode: StatusCodes.Status403Forbidden);

                var export = await exports.ExportAsync(orderId, exportFormat, version, requestingUsername, ct);
                return export is null
                    ? Results.NotFound()
                    : Results.File(export.Content, export.ContentType, export.FileName);
            })
            .RequireAuthorization();

        return app;
    }
}
