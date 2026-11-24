using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Web.Endpoints;

/// <summary>
/// HTTP endpoints for uploaded sources: <c>POST /api/sources</c>, <c>GET</c> and <c>DELETE /api/sources/{id}</c>.
/// Cookie sign-in is required; writes need the antiforgery token in the <c>RequestVerificationToken</c> header and,
/// when the browser sends one, an <c>Origin</c> equal to the host. Receipts are JSON in camelCase with enum values
/// as camelCase strings (for example <c>"kind": "pdf"</c>, <c>"budgetClass": "withMap"</c>).
/// </summary>
public static class SourceEndpoints
{
    /// <summary>Largest accepted request body: the largest file plus room for the multipart envelope.</summary>
    public const long RequestLimitBytes = 27_262_976;

    /// <summary>JSON format of receipts and error answers.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Maps the source endpoints.</summary>
    public static IEndpointRouteBuilder MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sources").RequireAuthorization();

        group.MapPost("", UploadAsync)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(RequestLimitBytes));

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
        CancellationToken ct)
    {
        var maxFile = options.Value.MaxFileBytes;
        var tooLarge = string.Create(CultureInfo.InvariantCulture, $"The file is larger than {maxFile / 1_048_576.0:0.#} MB.");
        if (http.Request.ContentLength > RequestLimitBytes)
            return Reason(StatusCodes.Status413PayloadTooLarge, tooLarge);

        if (await CheckWriteAsync(http, antiforgery) is { } refused)
            return refused;
        if (!currentUser.TryGetOwnUsername(out var username))
            return UnknownUser();

        if (!http.Request.HasFormContentType)
            return Reason(StatusCodes.Status400BadRequest, "Send the file as multipart form data.");

        IFormCollection form;
        try
        {
            form = await http.Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = RequestLimitBytes }, ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            return ex is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge } or InvalidDataException
                ? Reason(StatusCodes.Status413PayloadTooLarge, tooLarge)
                : Reason(StatusCodes.Status400BadRequest, "The upload could not be read.");
        }

        if (form.Files.Count != 1)
            return Reason(StatusCodes.Status400BadRequest, "Send exactly one file per upload.");

        var file = form.Files[0];
        if (file.Length > maxFile)
            return Reason(StatusCodes.Status413PayloadTooLarge, tooLarge);

        await using var stream = file.OpenReadStream();
        var receipt = await intake.IntakeAsync(
            new SourceUpload(file.FileName, file.ContentType, file.Length, stream),
            username,
            SourceOrigin.WebUpload,
            ct);

        if (!receipt.Usable)
            return Results.Json(receipt, JsonOptions, statusCode: StatusCodes.Status422UnprocessableEntity);
        if (receipt.Duplicate)
            return Results.Json(receipt, JsonOptions);

        http.Response.Headers.Location = $"/api/sources/{receipt.SourceId}";
        return Results.Json(receipt, JsonOptions, statusCode: StatusCodes.Status201Created);
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
            return Reason(StatusCodes.Status400BadRequest, "The page is out of date. Reload it and try again.");
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
