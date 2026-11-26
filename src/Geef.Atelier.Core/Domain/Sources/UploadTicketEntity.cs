namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>
/// A ticket for the upload of one file by one person. The token is stored only as its SHA-256 in lower-case hex
/// (<see cref="TokenHash"/>); <see cref="FileName"/> is shown and decides between text and Markdown, it is never part of a path.
/// <see cref="SourceId"/> is set once the file arrived as a usable source and becomes <c>null</c> when that source is deleted.
/// <see cref="LastRefusalCode"/> is the code (<see cref="SourceRefusalCodes"/>) of the last file that was checked and refused on
/// this ticket; it never holds a file name or content.
/// </summary>
public sealed record UploadTicketEntity
{
    public required Guid Id { get; init; }
    public required string OwnerUsername { get; init; }
    public required string FileName { get; init; }
    public long? DeclaredBytes { get; init; }
    public required string TokenHash { get; init; }
    public required UploadTicketState State { get; init; }
    public int Attempts { get; init; }
    public Guid? SourceId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset TokenExpiresAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? ReceivingSince { get; init; }
    public DateTimeOffset? UsedAt { get; init; }
    public string? LastRefusalCode { get; init; }
}
