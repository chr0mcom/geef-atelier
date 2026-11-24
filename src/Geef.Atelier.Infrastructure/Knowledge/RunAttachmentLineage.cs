using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Infrastructure.Knowledge;

/// <summary>Follows resume and composition links of a run (at most <see cref="MaxLineageRuns"/> runs).</summary>
internal sealed class RunAttachmentLineage(IKnowledgeDocumentRepository documents, IRunRepository runs) : IRunAttachmentLineage
{
    /// <summary>Upper bound of runs visited along the links, a guard against cycles in damaged data.</summary>
    public const int MaxLineageRuns = 10;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>> RunsWithAttachmentsAsync(Guid runId, CancellationToken ct)
    {
        var result = new List<Guid>();
        var visited = new HashSet<Guid>();
        var pending = new Queue<Guid>([runId]);
        while (pending.Count > 0 && visited.Count < MaxLineageRuns)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current))
                continue;

            if ((await documents.ListByRunAsync(current, ct)).Count > 0)
                result.Add(current);

            var run = await runs.GetByIdAsync(current, ct);
            if (run?.ParentRunId is { } parent)
                pending.Enqueue(parent);
            if (run?.ParentCompositionRunId is { } composer)
                pending.Enqueue(composer);
        }

        return result;
    }
}
