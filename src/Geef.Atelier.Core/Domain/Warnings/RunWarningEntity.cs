namespace Geef.Atelier.Core.Domain.Warnings;

/// <summary>
/// A problem a run worked around instead of stopping: a skipped source, a failed reviewer, a replacement model,
/// a failover between AI services or a skipped finishing step. Shown to the user next to the result.
/// </summary>
public sealed record RunWarningEntity
{
    public required Guid Id { get; init; }
    public required Guid RunId { get; init; }
    public int? Iteration { get; init; }
    public required string Source { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}
