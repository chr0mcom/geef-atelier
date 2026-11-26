using Geef.Atelier.Core.Domain.Sources;
using ModelContextProtocol;

namespace Geef.Atelier.Mcp.Tools;

/// <summary>
/// A rejection of <see cref="McpAttachmentParser"/>: the message for the client is unchanged; <see cref="Code"/>, the extension of
/// the given file name, <see cref="DeclaredType"/> and <see cref="SizeBytes"/> are what is counted (never the name or the content).
/// </summary>
internal sealed class McpAttachmentRejectedException(
    string code,
    string? fileName,
    string? declaredType,
    long? sizeBytes,
    string message,
    Exception? innerException = null)
    : McpException(message, innerException)
{
    /// <summary>Code of <see cref="SourceRefusalCodes"/>.</summary>
    public string Code { get; } = code;

    /// <summary>Normalized declared content type, or <c>null</c>.</summary>
    public string? DeclaredType { get; } = declaredType;

    /// <summary>Size of the decoded content in bytes, or <c>null</c> when nothing was decoded.</summary>
    public long? SizeBytes { get; } = sizeBytes;

    /// <summary>The counted row for <paramref name="ownerUsername"/>: the extension of the file name only, as <see cref="SourceRefusalCodes.ExtensionOf"/> keeps it.</summary>
    public SourceRefusalRecord ToRecord(string ownerUsername) =>
        new(ownerUsername, RefusalPath.McpInline, Code, null, SourceRefusalCodes.ExtensionOf(fileName), DeclaredType, SizeBytes);
}
