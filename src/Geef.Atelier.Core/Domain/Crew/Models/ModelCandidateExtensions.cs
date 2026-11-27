namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>Convenience extensions over <see cref="ModelCandidate"/>.</summary>
public static class ModelCandidateExtensions
{
    /// <summary>Vendor family of the candidate.</summary>
    public static VendorFamily Family(this ModelCandidate candidate) =>
        CliRoute.FamilyOf(candidate.Provider, candidate.Model);

    /// <summary>True when both candidates use the same provider and model (case-insensitive).</summary>
    public static bool SameBindingAs(this ModelCandidate candidate, string provider, string model) =>
        string.Equals(candidate.Provider, provider, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.Model, model, StringComparison.OrdinalIgnoreCase);
}
