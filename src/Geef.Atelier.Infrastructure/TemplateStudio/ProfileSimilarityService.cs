using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;

namespace Geef.Atelier.Infrastructure.TemplateStudio;

/// <summary>
/// Checks proposed profiles against existing ones using embedding cosine-similarity.
/// Profiles with similarity above the threshold are considered duplicates.
/// </summary>
internal sealed class ProfileSimilarityService(ICrewService crewService, IEmbeddingProvider embeddingProvider)
{
    /// <summary>
    /// Returns true and the name of the similar existing profile when an existing profile
    /// with cosine-similarity above <paramref name="threshold"/> is found.
    /// </summary>
    public async Task<(bool IsDuplicate, string? ExistingName)> FindSimilarAsync(
        ProposedProfile proposed,
        double threshold,
        CancellationToken ct)
    {
        var candidates = await LoadCandidatesAsync(proposed.ProfileType, ct);
        if (candidates.Count == 0) return (false, null);

        var proposedText = $"{proposed.Name}: {proposed.Description}";
        var proposedEmbedding = await embeddingProvider.CreateAsync(proposedText, ct);

        foreach (var (name, description) in candidates)
        {
            var candidateText = $"{name}: {description}";
            var candidateEmbedding = await embeddingProvider.CreateAsync(candidateText, ct);
            var similarity = CosineSimilarity(proposedEmbedding.Vector, candidateEmbedding.Vector);
            if (similarity >= threshold)
                return (true, name);
        }

        return (false, null);
    }

    private async Task<IReadOnlyList<(string Name, string Description)>> LoadCandidatesAsync(
        ProposedProfileType type, CancellationToken ct) => type switch
    {
        ProposedProfileType.Reviewer =>
            (await crewService.ListReviewerProfilesAsync(includeSystem: true, ct))
                .Select(p => (p.Name, p.Description)).ToList(),
        ProposedProfileType.Advisor =>
            (await crewService.ListAdvisorProfilesAsync(includeSystem: true, ct))
                .Select(p => (p.Name, p.Description)).ToList(),
        ProposedProfileType.GroundingProvider =>
            (await crewService.ListGroundingProviderProfilesAsync(includeSystem: true, ct))
                .Select(p => (p.Name, p.Description)).ToList(),
        _ => []
    };

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0;
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot   += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
