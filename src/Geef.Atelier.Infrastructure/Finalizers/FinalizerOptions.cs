namespace Geef.Atelier.Infrastructure.Finalizers;

/// <summary>Configuration options for the finalizer infrastructure (export path, size limit).</summary>
public sealed class FinalizerOptions
{
    /// <summary>Absolute path on the host/container where exported files are written.</summary>
    public string ExportPath { get; set; } = "/app/exports";

    /// <summary>Maximum file size in bytes for generated export files. Defaults to 50 MB.</summary>
    public long MaxFileSizeBytes { get; set; } = 50 * 1024 * 1024;
}
