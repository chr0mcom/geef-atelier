using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Web.Components.UI.Orders;

/// <summary>Where a file on the start page stands.</summary>
public enum SourceUploadStatus { Uploading = 0, Ready = 1, Unusable = 2, Failed = 3 }

/// <summary>What a failed upload needs from the person.</summary>
public enum SourceUploadRecovery { None = 0, Retry = 1, SignIn = 2, Reload = 3 }

/// <summary>One file on the start page, from the first byte sent to its receipt.</summary>
public sealed class SourceUploadItem(string localId, string fileName, long sizeBytes)
{
    /// <summary>Id given by the upload script; stable while the page is open.</summary>
    public string LocalId { get; } = localId;

    /// <summary>Name as shown to the person (the receipt's name once it arrived).</summary>
    public string FileName { get; set; } = fileName;

    /// <summary>Size reported by the browser before the upload.</summary>
    public long SizeBytes { get; set; } = sizeBytes;

    /// <summary>Current state.</summary>
    public SourceUploadStatus Status { get; set; } = SourceUploadStatus.Uploading;

    /// <summary>Upload progress in percent.</summary>
    public int Percent { get; set; }

    /// <summary>Receipt from the server, once read.</summary>
    public SourceReceipt? Receipt { get; set; }

    /// <summary>Plain-English problem, for unusable or failed files.</summary>
    public string? Problem { get; set; }

    /// <summary>What the person can do about <see cref="Problem"/>.</summary>
    public SourceUploadRecovery Recovery { get; set; }

    /// <summary>Hint given before the upload, e.g. an unusual file type.</summary>
    public string? Hint { get; set; }

    /// <summary>Pages to use, e.g. <c>1-40,55</c>; only asked for files that are too long.</summary>
    public string? Pages { get; set; }

    /// <summary>True when the file came from an earlier order and must not be deleted on removal.</summary>
    public bool FromEarlierOrder { get; set; }

    /// <summary>True while the file still blocks the start button.</summary>
    public bool Blocks => Status is not SourceUploadStatus.Ready;
}

/// <summary>Reads the JSON answers of <c>POST /api/sources</c>.</summary>
public static class SourceReceiptJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Parses a receipt; <c>null</c> when the body is not a receipt.</summary>
    public static SourceReceipt? TryParseReceipt(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            var receipt = JsonSerializer.Deserialize<SourceReceipt>(body, Options);
            return receipt is { FileName: not null } ? receipt with { Warnings = receipt.Warnings ?? [], Preview = receipt.Preview ?? string.Empty } : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads <c>reason</c> from an error body such as <c>{ "reason": "…" }</c>.</summary>
    public static string? TryParseReason(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                   && doc.RootElement.TryGetProperty("reason", out var reason)
                   && reason.ValueKind == JsonValueKind.String
                ? reason.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Applies the answer of the upload endpoint to <paramref name="item"/>.</summary>
    public static void ApplyResponse(this SourceUploadItem item, int status, string? body, long maxFileBytes)
    {
        item.Percent = 100;
        var receipt = TryParseReceipt(body);
        switch (status)
        {
            case 200 or 201 when receipt is { Usable: true, SourceId: not null }:
                item.Receipt = receipt;
                item.FileName = receipt.FileName;
                item.SizeBytes = receipt.SizeBytes;
                item.Status = SourceUploadStatus.Ready;
                item.Problem = null;
                item.Recovery = SourceUploadRecovery.None;
                break;
            case 200 or 201 or 422 when receipt is not null:
                item.Receipt = receipt;
                item.Status = SourceUploadStatus.Unusable;
                item.Problem = string.IsNullOrWhiteSpace(receipt.Reason)
                    ? "This file cannot be used. Remove it, or try a different file."
                    : $"{receipt.Reason} Remove it, or try a different file.";
                item.Recovery = SourceUploadRecovery.None;
                break;
            case 413:
                item.Status = SourceUploadStatus.Unusable;
                item.Problem = (TryParseReason(body) ?? $"This file is larger than {maxFileBytes.ToFileSize()}.")
                               + " Remove it, or try a smaller file.";
                item.Recovery = SourceUploadRecovery.None;
                break;
            case 401:
                item.Status = SourceUploadStatus.Failed;
                item.Problem = "You were signed out, so the file was not added. Sign in again, then add it again.";
                item.Recovery = SourceUploadRecovery.SignIn;
                break;
            case 400 or 403:
                item.Status = SourceUploadStatus.Failed;
                item.Problem = (TryParseReason(body) is { } reason ? reason + " " : "The file was not accepted. ")
                               + "Reload the page and add it again.";
                item.Recovery = SourceUploadRecovery.Reload;
                break;
            case 0:
                item.Status = SourceUploadStatus.Failed;
                item.Problem = "The upload was interrupted. Check your connection and try again.";
                item.Recovery = SourceUploadRecovery.Retry;
                break;
            default:
                item.Status = SourceUploadStatus.Failed;
                item.Problem = "Something went wrong while reading this file. Try again in a moment.";
                item.Recovery = SourceUploadRecovery.Retry;
                break;
        }
    }
}
