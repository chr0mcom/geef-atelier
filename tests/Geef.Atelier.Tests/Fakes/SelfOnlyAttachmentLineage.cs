using Geef.Atelier.Application.Crew.Knowledge;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>Treats every run as having its own attachments and no ancestors.</summary>
internal sealed class SelfOnlyAttachmentLineage : IRunAttachmentLineage
{
    public Task<IReadOnlyList<Guid>> RunsWithAttachmentsAsync(Guid runId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Guid>>([runId]);
}
