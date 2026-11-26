namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>State of an upload ticket. <see cref="Used"/> and <see cref="Failed"/> are final. New states are appended at the end.</summary>
public enum UploadTicketState { Open = 0, Receiving = 1, Used = 2, Failed = 3 }

/// <summary>Way on which a file was refused. New ways are appended at the end.</summary>
public enum RefusalPath { Web = 0, McpInline = 1, McpTicket = 2, UploadPage = 3 }

/// <summary>
/// What a ticket looks like to its owner (upload page) or to the holder of its token (answer 409). <see cref="Failed"/> is a ticket
/// whose attempts were all used by files that were checked and refused; <see cref="Expired"/> is one whose time ran out.
/// </summary>
public enum UploadClaimProbe { NotFound = 0, Open = 1, Receiving = 2, Used = 3, Expired = 4, Failed = 5 }

/// <summary>The fixed names of <see cref="RefusalPath"/> as they are stored and counted.</summary>
public static class RefusalPathNames
{
    public const string Web = "web";
    public const string McpInline = "mcp_inline";
    public const string McpTicket = "mcp_ticket";
    public const string UploadPage = "upload_page";

    /// <summary>The stored name of <paramref name="path"/>.</summary>
    public static string ToStorageName(this RefusalPath path) => path switch
    {
        RefusalPath.McpInline => McpInline,
        RefusalPath.McpTicket => McpTicket,
        RefusalPath.UploadPage => UploadPage,
        _ => Web,
    };

    /// <summary>The way with the stored <paramref name="name"/>; an unknown name reads as <see cref="RefusalPath.Web"/>.</summary>
    public static RefusalPath FromStorageName(string? name) => name switch
    {
        McpInline => RefusalPath.McpInline,
        McpTicket => RefusalPath.McpTicket,
        UploadPage => RefusalPath.UploadPage,
        _ => RefusalPath.Web,
    };
}
