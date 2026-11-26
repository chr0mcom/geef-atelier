namespace Geef.Atelier.Core.Configuration;

/// <summary>Whether files can be uploaded through an upload ticket (MCP tool <c>prepare_attachment</c>, token upload and upload page).</summary>
public enum UploadTicketsMode
{
    /// <summary>No ticket tool, no upload routes; the server behaves as before stage 6.</summary>
    Off = 0,

    /// <summary>Tickets can be prepared over MCP and used with the token upload or the upload page.</summary>
    On = 1,
}

/// <summary>Upload tickets and the counting of refused files (section <c>Uploads</c>).</summary>
public sealed class UploadOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Uploads";

    /// <summary>Whether upload tickets are offered; one switch for every person.</summary>
    public UploadTicketsMode Tickets { get; set; } = UploadTicketsMode.Off;

    /// <summary>How long the token of a ticket can be used for the upload with <c>curl</c>.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long the upload page of a ticket can be used by its owner after signing in.</summary>
    public TimeSpan PageLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Most uploads tried for one ticket; after the last unusable one the ticket fails.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Most tickets one person may have open at the same time.</summary>
    public int MaxOpenTicketsPerUser { get; set; } = 10;

    /// <summary>Most tickets one person may prepare within 24 hours.</summary>
    public int MaxTicketsPerDay { get; set; } = 50;

    /// <summary>How long a ticket is kept after its page expired.</summary>
    public TimeSpan TicketRetention { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How long a counted refusal is kept.</summary>
    public TimeSpan RefusalRetention { get; set; } = TimeSpan.FromDays(180);
}
