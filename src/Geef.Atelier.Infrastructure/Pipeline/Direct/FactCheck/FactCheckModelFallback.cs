using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

/// <summary>
/// The one second attempt of a fact check whose model could not be reached (contract stage 3, 4.3): the next candidate of the
/// slot <see cref="ModelSlot.FactReviewer"/> that is neither the writer's model nor served by the provider that failed, and that
/// is not paid per token. The proxy's own failover stays off, because it would fall back to the CLI's default model, usually the
/// writer's.
/// </summary>
internal static class FactCheckModelFallback
{
    /// <summary>
    /// The profile of <paramref name="failed"/> bound to the next permitted candidate, or <c>null</c> when there is none.
    /// <paramref name="writerProvider"/>/<paramref name="writerModel"/> are the writer's binding from the run's snapshot.
    /// </summary>
    public static ReviewerProfile? For(ReviewerProfile failed, string writerProvider, string writerModel, ModelDomain domain = ModelDomain.General)
    {
        ArgumentNullException.ThrowIfNull(failed);
        var candidate = ModelMatrix.CandidatesFor(ModelSlot.FactReviewer, failed.ModelDomain ?? domain)
            .FirstOrDefault(c => !c.IsPaid
                && !string.Equals(c.Provider, failed.Provider, StringComparison.OrdinalIgnoreCase)
                && !CliRoute.IsSameModel(c.Model, failed.Model)
                && !IsWriterModel(c, writerProvider, writerModel));
        return candidate is null ? null : failed with { Provider = candidate.Provider, Model = candidate.Model };
    }

    private static bool IsWriterModel(ModelCandidate candidate, string writerProvider, string writerModel) =>
        !string.IsNullOrWhiteSpace(writerModel)
        && (CliRoute.IsSameModel(candidate.Model, writerModel) || candidate.SameBindingAs(writerProvider ?? string.Empty, writerModel));
}
