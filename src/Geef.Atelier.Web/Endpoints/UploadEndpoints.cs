using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Geef.Atelier.Web.Endpoints;

/// <summary>
/// HTTP endpoints of the upload ticket. <c>PUT /api/uploads/{id}</c> takes the raw file with the one-time token in the
/// <see cref="UploadTicketRoutes.TokenHeader"/> header and nothing else: cookies and sign-ins are ignored and there is no
/// CORS, so another site cannot use it. Unknown, foreign, expired and used tickets and a wrong or missing token all answer
/// 404 with the same sentence. <c>POST /api/uploads/{id}/page</c> is the upload page of the signed-in owner: cookie,
/// antiforgery header and <c>Origin</c> exactly as <c>POST /api/sources</c>, a foreign ticket looks like an unknown one.
/// While <c>Uploads:Tickets</c> is <c>Off</c> both answer 404. Answers are JSON in the format of
/// <see cref="SourceEndpoints.JsonOptions"/>; a received file is answered with its receipt plus <c>uploadId</c>, its
/// <c>sha256</c> and, on the token route, the next step for the assistant (<c>next</c>), also after a refusal. The token route is
/// read by an AI assistant, so its answer leaves out the beginning of the text and the file name: nothing from the file reaches it.
/// </summary>
public static class UploadEndpoints
{
    /// <summary>Answer of the page upload for an unknown or foreign ticket.</summary>
    public const string PageNotFoundReason = "Not found.";

    /// <summary>Longest token header value that is passed on; real tokens are 43 characters.</summary>
    public const int MaxTokenLength = 256;

    private const string NotReceivedReason = "The file could not be received. Try again.";

    /// <summary>Route of the token upload, built from <see cref="UploadTicketRoutes.TokenUploadPathFormat"/>.</summary>
    public static readonly string TokenUploadRoute = string.Format(CultureInfo.InvariantCulture, UploadTicketRoutes.TokenUploadPathFormat, "{uploadId:guid}");

    /// <summary>Route of the page upload, built from <see cref="UploadTicketRoutes.PageUploadPathFormat"/>.</summary>
    public static readonly string PageUploadRoute = string.Format(CultureInfo.InvariantCulture, UploadTicketRoutes.PageUploadPathFormat, "{uploadId:guid}");

    /// <summary>
    /// Maps the token upload and the page upload, only while upload tickets are on; otherwise these addresses do not exist and answer
    /// like any unknown address.
    /// </summary>
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app)
    {
        using (var scope = app.ServiceProvider.CreateScope())
        {
            if (!scope.ServiceProvider.GetRequiredService<IUploadTicketService>().Enabled)
                return app;
        }

        var uploadTimeout = app.ServiceProvider.GetRequiredService<IOptions<SourceOptions>>().Value.UploadTimeout;
        var timeout = new RequestTimeoutPolicy
        {
            Timeout = uploadTimeout,
            TimeoutStatusCode = StatusCodes.Status408RequestTimeout,
            WriteTimeoutResponse = context => context.Response.WriteAsJsonAsync(new { reason = SourceEndpoints.TimeoutReason }, SourceEndpoints.JsonOptions),
        };

        app.MapPut(TokenUploadRoute, ReceiveByTokenAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(SourceEndpoints.RequestLimitBytes))
            .WithRequestTimeout(timeout);

        app.MapPost(PageUploadRoute, ReceiveFromPageAsync)
            .RequireAuthorization()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(SourceEndpoints.RequestLimitBytes))
            .WithRequestTimeout(timeout);

        return app;
    }

    /// <summary>
    /// While upload tickets are off, takes the matched upload page out of the request, so that <c>/u/{id}</c> answers exactly like an
    /// address that does not exist (no sign-in redirect, no page). Must run after routing and before authentication.
    /// </summary>
    public static IApplicationBuilder UseUploadPageSwitch(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>()?.Type == typeof(UploadPage)
                && !context.RequestServices.GetRequiredService<IUploadTicketService>().Enabled)
                context.SetEndpoint(null);
            await next(context);
        });

    private static async Task<IResult> ReceiveByTokenAsync(
        Guid uploadId,
        HttpContext http,
        [FromServices] IUploadTicketService tickets,
        [FromServices] IOptions<SourceOptions> options,
        CancellationToken ct)
    {
        if (!tickets.Enabled)
            return Results.NotFound();

        var headers = http.Request.Headers[UploadTicketRoutes.TokenHeader];
        var token = headers.Count == 1 ? headers[0] : null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
            return Reason(StatusCodes.Status404NotFound, UploadTicketLines.LinkNotValid);

        if (http.Request.ContentLength > SourceEndpoints.RequestLimitBytes)
            return Reason(StatusCodes.Status413PayloadTooLarge, SourceEndpoints.TooLargeReason(options.Value));

        SourceEndpoints.RequireMinimumRate(http, options.Value.MinUploadBytesPerSecond);
        var outcome = await tickets.ReceiveByTokenAsync(uploadId, token, http.Request.Body, ct);
        return Answer(http, uploadId, outcome, forAssistant: true);
    }

    private static async Task<IResult> ReceiveFromPageAsync(
        Guid uploadId,
        HttpContext http,
        IAntiforgery antiforgery,
        [FromServices] IUploadTicketService tickets,
        [FromServices] ICurrentUserService currentUser,
        [FromServices] IOptions<SourceOptions> options,
        [FromServices] IOptions<AntiforgeryOptions> antiforgeryOptions,
        CancellationToken ct)
    {
        if (!tickets.Enabled)
            return Reason(StatusCodes.Status404NotFound, PageNotFoundReason);

        var tooLarge = SourceEndpoints.TooLargeReason(options.Value);
        if (http.Request.ContentLength > SourceEndpoints.RequestLimitBytes)
        {
            await SourceEndpoints.CountTooLargeAsync(http, antiforgery, antiforgeryOptions.Value, currentUser, RefusalPath.UploadPage, ct);
            return Reason(StatusCodes.Status413PayloadTooLarge, tooLarge);
        }

        if (antiforgeryOptions.Value.HeaderName is not { } tokenHeader || !http.Request.Headers.ContainsKey(tokenHeader))
            return Reason(StatusCodes.Status400BadRequest, SourceEndpoints.OutOfDateReason);
        if (await SourceEndpoints.CheckWriteAsync(http, antiforgery) is { } refused)
            return refused;
        if (!currentUser.TryGetOwnUsername(out var owner))
            return Reason(StatusCodes.Status403Forbidden, OwnerScope.UnknownUserMessage);

        var probe = await tickets.ProbeAsync(uploadId, owner, ct);
        if (probe != UploadClaimProbe.Open)
            return PageRefusal(probe);

        if (SourceEndpoints.MultipartBoundary(http.Request) is not { } boundary)
            return Reason(StatusCodes.Status400BadRequest, "Send the file as multipart form data.");

        SourceEndpoints.RequireMinimumRate(http, options.Value.MinUploadBytesPerSecond);
        var reader = new MultipartReader(boundary, http.Request.Body)
        {
            HeadersCountLimit = 16,
            HeadersLengthLimit = 16 * 1_024,
            BodyLengthLimit = SourceEndpoints.RequestLimitBytes,
        };

        MultipartSection? section;
        ContentDispositionHeaderValue? disposition;
        try
        {
            (section, disposition) = await SourceEndpoints.NextFileAsync(reader, ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or BadHttpRequestException)
        {
            return ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }
                ? Reason(StatusCodes.Status413PayloadTooLarge, tooLarge)
                : Reason(StatusCodes.Status400BadRequest, SourceEndpoints.UnreadableReason);
        }

        if (section is null || disposition is null)
            return Reason(StatusCodes.Status400BadRequest, SourceEndpoints.OneFileReason);

        var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? string.Empty;
        var outcome = await tickets.ReceiveFromPageAsync(uploadId, owner, fileName, section.Body, ct);
        if (outcome is { Receipt: null, StatusCode: StatusCodes.Status404NotFound or StatusCodes.Status409Conflict }
            && await tickets.ProbeAsync(uploadId, owner, CancellationToken.None) is not UploadClaimProbe.Open and var now)
            return PageRefusal(now);

        return Answer(http, uploadId, outcome, forAssistant: false);
    }

    private static IResult PageRefusal(UploadClaimProbe probe) => probe switch
    {
        UploadClaimProbe.Used => Reason(StatusCodes.Status409Conflict, UploadTicketLines.PageAlreadyUsed),
        UploadClaimProbe.Expired => Reason(StatusCodes.Status410Gone, UploadTicketLines.PageExpired),
        UploadClaimProbe.Failed => Reason(StatusCodes.Status410Gone, UploadTicketLines.PageFailed),
        UploadClaimProbe.Receiving => Reason(StatusCodes.Status409Conflict, UploadTicketLines.AlreadyRunning),
        _ => Reason(StatusCodes.Status404NotFound, PageNotFoundReason),
    };

    private static IResult Answer(HttpContext http, Guid uploadId, UploadOutcome outcome, bool forAssistant)
    {
        switch (outcome.StatusCode)
        {
            case StatusCodes.Status429TooManyRequests:
                http.Response.Headers.RetryAfter = "5";
                break;
            case StatusCodes.Status503ServiceUnavailable:
                http.Response.Headers.RetryAfter = SourceEndpoints.BusyRetryAfterSeconds;
                break;
        }

        if (outcome.Receipt is not { } receipt)
            return Reason(outcome.StatusCode, outcome.Reason ?? FallbackReason(outcome.StatusCode, forAssistant));

        var body = JsonSerializer.SerializeToNode(receipt, SourceEndpoints.JsonOptions)!.AsObject();
        if (string.IsNullOrWhiteSpace(receipt.Reason) && !string.IsNullOrWhiteSpace(outcome.Reason))
            body["reason"] = outcome.Reason;
        body["uploadId"] = uploadId.ToString();
        if (outcome.Sha256 is { } sha256)
            body["sha256"] = sha256;
        if (forAssistant)
        {
            body.Remove("preview");
            body.Remove("fileName");
            body["next"] = NextFor(receipt, uploadId);
        }
        return Results.Json(body, SourceEndpoints.JsonOptions, statusCode: outcome.StatusCode);
    }

    private static string NextFor(SourceReceipt receipt, Guid uploadId) => receipt switch
    {
        { Usable: true, SourceId: not null } => string.Format(CultureInfo.InvariantCulture, UploadTicketLines.AfterUploadFormat, uploadId),
        { RefusalCode: SourceRefusalCodes.Quota } => UploadTicketLines.AfterQuota,
        { RefusalCode: SourceRefusalCodes.Unavailable } => UploadTicketLines.AfterUnavailable,
        _ when SourceRefusalCodes.CountsAsAttempt(receipt.RefusalCode ?? SourceRefusalCodes.Other) => UploadTicketLines.AfterRefusal,
        _ => UploadTicketLines.AfterNotChecked,
    };

    private static string FallbackReason(int statusCode, bool forAssistant) => statusCode switch
    {
        StatusCodes.Status404NotFound => forAssistant ? UploadTicketLines.LinkNotValid : PageNotFoundReason,
        StatusCodes.Status409Conflict => UploadTicketLines.AlreadyRunning,
        StatusCodes.Status408RequestTimeout => SourceEndpoints.TimeoutReason,
        _ => NotReceivedReason,
    };

    private static IResult Reason(int statusCode, string reason) =>
        Results.Json(new { reason }, SourceEndpoints.JsonOptions, statusCode: statusCode);
}
