using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Finalizers.FormatConverters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace Geef.Atelier.Infrastructure.Finalizers;

internal sealed class FileExportFinalizerExecutor(
    IOptions<FinalizerOptions> options,
    ILogger<FileExportFinalizerExecutor> logger) : IFinalizerExecutor
{
    private readonly FinalizerOptions _options = options.Value;

    public FinalizerType Type => FinalizerType.FileExport;

    public async Task<FinalizerExecutionResult> ExecuteAsync(
        FinalizerProfile profile,
        FinalizerExecutionContext context,
        CancellationToken cancellationToken)
    {
        var settings = FileExportSettings.From(profile.Settings);
        var format = settings.Format.ToLowerInvariant().Trim('.');

        string? errorMessage = null;
        RunArtifact? artifact = null;
        try
        {
            var (bytes, extension, contentType) = ConvertToFormat(context.CurrentText, format, context.TemplateName ?? "document");

            if (bytes.Length > _options.MaxFileSizeBytes)
                throw new InvalidOperationException(
                    $"Export size {bytes.Length:N0} bytes exceeds the configured limit of {_options.MaxFileSizeBytes:N0} bytes.");

            var filename = BuildFilename(context.RunId, profile.Name, extension);
            var directory = Path.Combine(_options.ExportPath, context.RunId.ToString("N"));
            // GUID-based path — no traversal risk
            Directory.CreateDirectory(directory);
            var fullPath = Path.Combine(directory, filename);

            await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken);
            logger.LogInformation("File export written: {Path} ({Bytes} bytes)", fullPath, bytes.Length);

            artifact = new RunArtifact
            {
                Id = Guid.NewGuid(),
                RunId = context.RunId,
                FinalizerProfileName = profile.Name,
                ArtifactType = ArtifactType.File,
                Filename = filename,
                ContentType = contentType,
                SizeBytes = bytes.Length,
                StorageUri = fullPath,
                CreatedAt = DateTimeOffset.UtcNow,
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "File export failed for profile {Profile}", profile.Name);
            errorMessage = $"Export failed: {ex.Message}";
        }

        if (errorMessage is not null)
        {
            artifact = new RunArtifact
            {
                Id = Guid.NewGuid(),
                RunId = context.RunId,
                FinalizerProfileName = profile.Name,
                ArtifactType = ArtifactType.Status,
                StorageUri = "error",
                StatusMessage = errorMessage,
                CreatedAt = DateTimeOffset.UtcNow,
            };
        }

        return new FinalizerExecutionResult(
            UpdatedText: null,
            Artifact: artifact,
            CostEur: null,
            ActorName: profile.Name);
    }

    private static (byte[] Bytes, string Extension, string ContentType) ConvertToFormat(
        string text, string format, string title) => format switch
    {
        "html" => (
            Encoding.UTF8.GetBytes(MarkdownToHtmlConverter.Convert(text, title)),
            "html",
            "text/html; charset=utf-8"),
        "pdf" => (
            MarkdownToPdfConverter.Convert(text, title),
            "pdf",
            "application/pdf"),
        "docx" => (
            MarkdownToDocxConverter.Convert(text, title),
            "docx",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        "txt" => (
            Encoding.UTF8.GetBytes(PlaintextStripper.Strip(text)),
            "txt",
            "text/plain; charset=utf-8"),
        "json" => (
            Encoding.UTF8.GetBytes(BuildJson(text, title)),
            "json",
            "application/json"),
        _ => ( // default: markdown
            Encoding.UTF8.GetBytes(text),
            "md",
            "text/markdown; charset=utf-8"),
    };

    private static string BuildJson(string markdown, string title)
    {
        var payload = new
        {
            title,
            content = markdown,
            generatedAt = DateTimeOffset.UtcNow.ToString("O"),
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string BuildFilename(Guid runId, string profileName, string extension)
    {
        var safe = string.Concat(profileName
            .Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));
        return $"{safe}-{runId.ToString("N")[..8]}.{extension}";
    }
}
