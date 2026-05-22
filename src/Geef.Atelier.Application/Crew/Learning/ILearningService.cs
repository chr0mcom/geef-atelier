using Geef.Atelier.Core.Domain.Crew.Learning;

namespace Geef.Atelier.Application.Crew.Learning;

/// <summary>Application-level management of extracted learning entries.</summary>
public interface ILearningService
{
    Task<IReadOnlyList<LearningEntry>> ListAsync(LearningStatus? status = null, string? domain = null, CancellationToken ct = default);
    Task<LearningEntry?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Manually approve a proposed or rejected learning. Owner-checked.</summary>
    Task ApproveAsync(Guid id, string requestingUsername, CancellationToken ct = default);

    /// <summary>Manually reject a proposed or approved learning. Owner-checked.</summary>
    Task RejectAsync(Guid id, string requestingUsername, CancellationToken ct = default);

    /// <summary>Permanently deletes a learning entry. Owner-checked.</summary>
    Task DeleteAsync(Guid id, string requestingUsername, CancellationToken ct = default);
}
