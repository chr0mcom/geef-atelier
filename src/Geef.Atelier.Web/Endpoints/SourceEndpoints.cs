using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using MinDataRate = Microsoft.AspNetCore.Server.Kestrel.Core.MinDataRate;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Geef.Atelier.Web.Endpoints;

/// <summary>
/// HTTP endpoints for uploaded sources: <c>POST /api/sources</c>, <c>GET</c> and <c>DELETE /api/sources/{id}</c>.
/// Cookie sign-in is required; writes need the antiforgery token in the <c>RequestVerificationToken</c> header and,
/// when the browser sends one, an <c>Origin</c> equal to the host. Receipts are JSON in camelCase with enum values
/// as camelCase strings (for example <c>"kind": "pdf"</c>, <c>"budgetClass": "withMap"</c>).
/// The upload is read as a stream (no form buffer): the antiforgery token must come in the header, the person's upload
/// place and an upload slot are taken before the body is read, and the request ends after
/// <see cref="SourceOptions.UploadTimeout"/> (408) or when it sends slower than
/// <see cref="SourceOptions.MinUploadBytesPerSecond"/>.
/// </summary>
public static class SourceEndpoints
{
    /// <summary>Largest accepted request body: the largest file plus room for the multipart envelope.</summary>
    public const long RequestLimitBytes = 27_262_976;

    /// <summary>Answer of an upload that took longer than <see cref="SourceOptions.UploadTimeout"/>.</summary>
    public const string TimeoutReason = "The upload took too long. Check your connection and try again.";

    private const string UnreadableReason = "The upload could not be read.";
    private const string OutOfDateReason = "The page is out of date. Reload it and try again.";
    private const string OneFileReason = "Send exactly one file per upload.";
    private static readonly TimeSpan MinRateGracePeriod = TimeSpan.FromSeconds(10);

    /// <summary>JSON format of receipts and error answers.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Maps the source endpoints.</summary>
    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sources").RequireAuthorization();
        var uploadTimeout = app.ServiceProvider.GetRequiredService<IOptions<SourceOptions>>().Value.UploadTimeout;

        group.MapPost("", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(RequestLimitBytes))
            .WithRequestTimeout(new RequestTimeoutPolicy
            {
                Timeout = uploadTimeout,
                TimeoutStatusCode = StatusCodes.Status408RequestTimeout,
                WriteTimeoutResponse = context => context.Response.WriteAsJsonAsync(new { reason = TimeoutReason }, JsonOptions),
            });

        group.MapGet("/{sourceId:guid}", async (Guid sourceId, ISourceIntakeService intake, ICurrentUserService currentUser, CancellationToken ct) =>
        {
            if (!currentUser.TryGetOwnerFilter(out var owner))
                return UnknownUser();

            var receipt = await intake.GetReceiptAsync(sourceId, owner, ct);
            return receipt is null ? Results.NotFound() : Results.Json(receipt, JsonOptions);
        });

        group.MapDelete("/{sourceId:guid}", async (Guid sourceId, HttpContext http, IAntiforgery antiforgery, ISourceIntakeService intake, ICurrentUserService currentUser, CancellationToken ct) =>
        {
            if (await CheckWriteAsync(http, antiforgery) is { } refused)
                return refused;
            if (!currentUser.TryGetOwnerFilter(out var owner))
                return UnknownUser();

            return await intake.DeleteAsync(sourceId, owner, ct) switch
            {
                SourceDeleteResult.Deleted => Results.NoContent(),
                SourceDeleteResult.InUse => Reason(StatusCodes.Status409Conflict, "This file is used by an order and cannot be removed."),
                _ => Results.NotFound(),
            };
        }).DisableAntiforgery();

        return app;
    }

    private static async Task<IResult> UploadAsync(
        HttpContext http,
        IAntiforgery antiforgery,
        ISourceIntakeService intake,
        ICurrentUserService currentUser,
        IOptions<SourceOptions> options,
        IOptions<AntiforgeryOptions> antiforgeryOptions,
        CancellationToken ct)
    {
        var opts = options.Value;
        var tooLarge = string.Create(CultureInfo.InvariantCulture, $"The file is larger than {opts.MaxFileBytes / 1_048_576.0:0.#} MB.");
        if (http.Request.ContentLength > RequestLimitBytes)
            return Reason(StatusCodes.Status413PayloadTooLarge, tooLarge);

        if (antiforgeryOptions.Value.HeaderName is not { } tokenHeader || !http.Request.Headers.ContainsKey(tokenHeader))
            return Reason(StatusCodes.Status400BadRequest, OutOfDateReason);
        if (await CheckWriteAsync(http, antiforgery) is { } refused)
            return refused;
        if (!currentUser.TryGetOwnUsername(out var username))
            return UnknownUser();

        if (MultipartBoundary(http.Request) is not { } boundary)
            return Reason(StatusCodes.Status400BadRequest, "Send the file as multipart form data.");

        RequireMinimumRate(http, opts.MinUploadBytesPerSecond);
        var reader = new MultipartReader(boundary, http.Request.Body)
        {
            HeadersCountLimit = 16,
            HeadersLengthLimit = 16 * 1_024,
            BodyLengthLimit = RequestLimitBytes,
        };

        MultipartSection? section;
        ContentDispositionHeaderValue? disposition;
        try
        {
            (section, disposition) = await NextFileAsync(reader, ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or BadHttpRequestException)
        {
            return ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }
                ? Reason(StatusCodes.Status413PayloadTooLarge, tooLarge)
                : Reason(StatusCodes.Status400BadRequest, UnreadableReason);
        }

        if (section is null || disposition is null)
            return Reason(StatusCodes.Status400BadRequest, OneFileReason);

        var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? string.Empty;
        var receipt = await intake.IntakeAsync(new SourceUpload(fileName, section.ContentType, 0, section.Body), username, SourceOrigin.WebUpload, ct);

        switch (receipt.Refusal)
        {
            case SourceRefusal.TooLarge:
                return Reason(StatusCodes.Status413PayloadTooLarge, tooLarge);
            case SourceRefusal.Interrupted:
                return Reason(StatusCodes.Status400BadRequest, UnreadableReason);
            case SourceRefusal.TooManyUploads:
                http.Response.Headers.RetryAfter = "5";
                return Results.Json(receipt, JsonOptions, statusCode: StatusCodes.Status429TooManyRequests);
        }

        if (!receipt.Usable)
            return Results.Json(receipt, JsonOptions, statusCode: StatusCodes.Status422UnprocessableEntity);

        if (await HasAnotherFileAsync(reader, ct))
        {
            if (!receipt.Duplicate && receipt.SourceId is { } extra)
                await intake.DeleteAsync(extra, username, CancellationToken.None);
            return Reason(StatusCodes.Status400BadRequest, OneFileReason);
        }

        if (receipt.Duplicate)
            return Results.Json(receipt, JsonOptions);

        http.Response.Headers.Location = $"/api/sources/{receipt.SourceId}";
        return Results.Json(receipt, JsonOptions, statusCode: StatusCodes.Status201Created);
    }

    private static string? MultipartBoundary(HttpRequest request)
    {
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return null;
        var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
        return string.IsNullOrWhiteSpace(boundary) || boundary.Length > 200 ? null : boundary;
    }

    private static async Task<(MultipartSection? Section, ContentDispositionHeaderValue? Disposition)> NextFileAsync(MultipartReader reader, CancellationToken ct)
    {
        while (await reader.ReadNextSectionAsync(ct) is { } section)
        {
            if (ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition) && disposition.IsFileDisposition())
                return (section, disposition);
        }

        return (null, null);
    }

    private static async Task<bool> HasAnotherFileAsync(MultipartReader reader, CancellationToken ct)
    {
        try
        {
            return (await NextFileAsync(reader, ct)).Section is not null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or BadHttpRequestException)
        {
            return false;
        }
    }

    private static void RequireMinimumRate(HttpContext http, int bytesPerSecond)
    {
        if (bytesPerSecond <= 0 || http.Features.Get<IHttpMinRequestBodyDataRateFeature>() is not { } rate)
            return;
        try
        {
            rate.MinDataRate = new MinDataRate(bytesPerSecond, MinRateGracePeriod);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException)
        {
        }
    }

    private static async Task<IResult?> CheckWriteAsync(HttpContext http, IAntiforgery antiforgery)
    {
        if (!OriginMatchesHost(http.Request))
            return Reason(StatusCodes.Status400BadRequest, "The request came from another site.");

        try
        {
            await antiforgery.ValidateRequestAsync(http);
            return null;
        }
        catch (AntiforgeryValidationException)
        {
            return Reason(StatusCodes.Status400BadRequest, OutOfDateReason);
        }
    }

    internal static bool OriginMatchesHost(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
            return true;
        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
               && string.Equals(uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}", request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static IResult UnknownUser() => Reason(StatusCodes.Status403Forbidden, OwnerScope.UnknownUserMessage);

    private static IResult Reason(int statusCode, string reason) =>
        Results.Json(new { reason }, JsonOptions, statusCode: statusCode);
}
