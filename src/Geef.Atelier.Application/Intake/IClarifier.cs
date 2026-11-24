using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Application.Intake;

/// <summary>A file of the order as the clarifier sees it: header data and an excerpt (start and end) of its text.</summary>
public sealed record IntakeFile(int Number, Guid SourceId, string FileName, SourceKind Kind, int? PageCount, int CharCount, int WordCount, string Excerpt, bool Truncated);

/// <summary>Input of one clarifier call.</summary>
public sealed record ClarifierRequest(string RequestText, IReadOnlyList<IntakeFile> Files, string? ProfileBlock, bool RevisionHint);

/// <summary>Result of one clarifier call; <see cref="Output"/> is set only for <see cref="IntakeRecordStatus.Ok"/>.</summary>
public sealed record ClarifierCallResult(IntakeRecordStatus Status, ClarifierOutput? Output, string? Model, int DurationMs, string? ErrorCode);

/// <summary>One structured model call that analyses an order. Never throws except for cancellation by the caller; failures come back as a status.</summary>
public interface IClarifier
{
    /// <summary>Analyses the order described by <paramref name="request"/>.</summary>
    Task<ClarifierCallResult> ClarifyAsync(ClarifierRequest request, CancellationToken ct);
}
