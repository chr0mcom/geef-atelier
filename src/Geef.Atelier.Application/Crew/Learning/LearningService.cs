using Geef.Atelier.Core.Domain.Crew.Learning;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Application.Crew.Learning;

public sealed class LearningService(ILearningRepository repo) : ILearningService
{
    public Task<IReadOnlyList<LearningEntry>> ListAsync(LearningStatus? status = null, string? domain = null, CancellationToken ct = default)
        => repo.ListAsync(status, domain, null, ct);

    public Task<LearningEntry?> GetAsync(Guid id, CancellationToken ct = default)
        => repo.GetByIdAsync(id, ct);

    public async Task ApproveAsync(Guid id, string requestingUsername, CancellationToken ct = default)
    {
        _ = await repo.GetByIdAsync(id, ct) ?? throw new InvalidOperationException("Learning entry not found.");
        await repo.UpdateStatusAsync(id, LearningStatus.Approved, DateTimeOffset.UtcNow, ct);
    }

    public async Task RejectAsync(Guid id, string requestingUsername, CancellationToken ct = default)
    {
        _ = await repo.GetByIdAsync(id, ct) ?? throw new InvalidOperationException("Learning entry not found.");
        await repo.UpdateStatusAsync(id, LearningStatus.Rejected, null, ct);
    }

    public async Task DeleteAsync(Guid id, string requestingUsername, CancellationToken ct = default)
    {
        _ = await repo.GetByIdAsync(id, ct) ?? throw new InvalidOperationException("Learning entry not found.");
        await repo.DeleteAsync(id, ct);
    }
}
