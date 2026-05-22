using Geef.Atelier.Core.Domain.Crew.Learning;

namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>Persistence access for learning entries extracted from runs.</summary>
public interface ILearningRepository
{
    Task<LearningEntry> CreateAsync(LearningEntry entry, float[] embedding, CancellationToken ct = default);

    Task<LearningEntry?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns the proposed learning entry whose <c>SourceRunId</c> matches <paramref name="sourceRunId"/>.</summary>
    Task<LearningEntry?> GetProposedBySourceRunIdAsync(Guid sourceRunId, CancellationToken ct = default);

    Task UpdateStatusAsync(Guid id, LearningStatus status, DateTimeOffset? approvedAt, CancellationToken ct = default);

    Task SetLearningRunIdAsync(Guid id, Guid learningRunId, CancellationToken ct = default);

    Task SetEmbeddingAsync(Guid id, float[] embedding, CancellationToken ct = default);

    Task<IReadOnlyList<LearningEntry>> ListAsync(
        LearningStatus? status = null,
        string? domain = null,
        string? owner = null,
        CancellationToken ct = default);

    /// <summary>Cosine-similarity search over Approved entries. Returns entries sorted by final domain-boosted score descending.</summary>
    Task<IReadOnlyList<(LearningEntry Entry, double Similarity)>> SearchApprovedAsync(
        float[] queryEmbedding,
        string? currentDomain,
        double sameDomainBoost,
        double crossDomainPenalty,
        int topK,
        CancellationToken ct = default);

    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
