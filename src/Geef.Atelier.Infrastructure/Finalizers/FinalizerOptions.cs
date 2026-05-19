namespace Geef.Atelier.Infrastructure.Finalizers;

/// <summary>Configuration options for the finalizer infrastructure (export paths, SMTP, limits).</summary>
public sealed class FinalizerOptions
{
    /// <summary>Absolute path on the host/container where exported files are written.</summary>
    public string ExportPath { get; set; } = "/app/exports";

    /// <summary>Maximum file size in bytes for generated export files. Defaults to 50 MB.</summary>
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;

    // --- SMTP (email sink) ---

    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;

    /// <summary>SMTP authentication username; empty = anonymous relay.</summary>
    public string SmtpUser { get; set; } = string.Empty;

    /// <summary>SMTP authentication password; keep out of source control — set via environment variable.</summary>
    public string SmtpPassword { get; set; } = string.Empty;

    public string SenderAddress { get; set; } = "noreply@geef.atelier";
    public string SenderName { get; set; } = "Geef.Atelier";

    /// <summary>Timeout in seconds for SMTP connections. Defaults to 30 s.</summary>
    public int SmtpTimeoutSeconds { get; set; } = 30;
}
