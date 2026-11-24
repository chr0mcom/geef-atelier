namespace Geef.Atelier.Application.Crew.Knowledge;

/// <summary>Finds the runs whose attachments belong to a run.</summary>
public interface IRunAttachmentLineage
{
    /// <summary>
    /// The run itself and, following resume (<c>ParentRunId</c>) and auto-composition (<c>ParentCompositionRunId</c>)
    /// links, its ancestors, but only those that have attachments. Empty when none of them has one.
    /// </summary>
    Task<IReadOnlyList<Guid>> RunsWithAttachmentsAsync(Guid runId, CancellationToken ct);
}
