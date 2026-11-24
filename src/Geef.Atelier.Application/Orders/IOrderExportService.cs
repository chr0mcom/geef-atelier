namespace Geef.Atelier.Application.Orders;

/// <summary>File formats an order text can be downloaded in.</summary>
public enum ExportFormat { Docx = 0, Pdf = 1, Markdown = 2 }

/// <summary>A converted order text ready for download.</summary>
public sealed record OrderExport(byte[] Content, string ContentType, string FileName);

/// <summary>Converts the delivered text of an order (current or given version) with the existing format converters. Null when the order is unknown, foreign or has no text.</summary>
public interface IOrderExportService
{
    /// <summary>Converts the text of version <paramref name="version"/> (current version when <c>null</c>) to <paramref name="format"/>.</summary>
    Task<OrderExport?> ExportAsync(Guid orderId, ExportFormat format, int? version, string? requestingUsername, CancellationToken ct = default);
}

/// <summary>Route values of <see cref="ExportFormat"/>.</summary>
public static class ExportFormatExtensions
{
    /// <summary>Parses the route values <c>docx</c>, <c>pdf</c> and <c>md</c>.</summary>
    public static bool TryParseRoute(string value, out ExportFormat format)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "docx":
                format = ExportFormat.Docx;
                return true;
            case "pdf":
                format = ExportFormat.Pdf;
                return true;
            case "md":
                format = ExportFormat.Markdown;
                return true;
            default:
                format = default;
                return false;
        }
    }

    /// <summary>Returns the route value and file extension of <paramref name="format"/>.</summary>
    public static string ToRouteValue(this ExportFormat format) => format switch
    {
        ExportFormat.Docx => "docx",
        ExportFormat.Pdf => "pdf",
        ExportFormat.Markdown => "md",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
    };
}
