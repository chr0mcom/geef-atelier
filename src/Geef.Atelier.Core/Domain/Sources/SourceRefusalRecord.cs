namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>
/// One refused file as it is counted: who, on which way, why (<see cref="SourceRefusalCodes"/>), the detected kind, the
/// extension of the given name, the declared type and the size. Never the file name, a preview, the content or a hash.
/// </summary>
public sealed record SourceRefusalRecord(
    string OwnerUsername,
    RefusalPath Path,
    string Code,
    SourceKind? Kind,
    string? Extension,
    string? DeclaredType,
    long? SizeBytes);
