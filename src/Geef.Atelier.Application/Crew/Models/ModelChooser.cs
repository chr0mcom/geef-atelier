using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Application.Crew.Models;

/// <summary>What the live catalog says about one candidate.</summary>
internal enum CandidateAvailability
{
    /// <summary>The live catalog lists the model.</summary>
    Confirmed,

    /// <summary>No authoritative live answer; counts as available so an outage never blocks a run.</summary>
    Unknown,

    /// <summary>The live catalog answered but does not list the model.</summary>
    Missing,

    /// <summary>The provider is known to be unusable (empty list, e.g. no API key).</summary>
    Unusable,
}

/// <summary>Context of one model choice.</summary>
/// <param name="Slot">Role being assigned.</param>
/// <param name="Domain">Effective domain (profile override, else crew domain).</param>
/// <param name="Executor">Executor binding, or null when the executor itself is assigned.</param>
/// <param name="RoleCount">Number of managed reviewers sharing <paramref name="Slot"/> in the crew (domain-independent).</param>
/// <param name="PaidUsed">Paid reviewers already assigned in the crew, manual ones included.</param>
/// <param name="UsedInSlot">How often each model has already been given to a reviewer of <paramref name="Slot"/>.</param>
internal sealed record ModelChoiceContext(
    ModelSlot Slot,
    ModelDomain Domain,
    ModelCandidate? Executor,
    int RoleCount,
    int PaidUsed,
    IReadOnlyDictionary<ModelCandidate, int> UsedInSlot);

/// <summary>
/// The single model-choice rule of the matrix (D-066/12, architecture §9.2), as a pure function over precomputed facts.
/// Hard rules are never relaxed: H1 the provider is not unusable; H2 a paid candidate only for reviewer slots, within the
/// per-crew cap and only in a core domain or from <see cref="ModelMatrix.PaidOutsideCoreDomainsMinRoleCount"/> same-slot
/// reviewers on. Soft rules order the permitted candidates (smaller is better): (1) missing from the live catalog,
/// (2) the executor's model, (3) outside the reviewer ring, (4) times already used in the slot, (5) the executor's vendor
/// family, (6) matrix position. The ring is formed per choice from the candidates the hard rules permit at that moment
/// (so a capped paid candidate never occupies a ring place), minus soft rules 1 and 2, other vendor family first, the
/// first <see cref="ModelMatrix.ReviewerRingSize"/>. If nothing satisfies the hard rules, the first unpaid candidate is
/// used.
/// </summary>
internal static class ModelChooser
{
    /// <summary>True when the hard rules H1 and H2 allow <paramref name="candidate"/>.</summary>
    public static bool IsPermitted(ModelCandidate candidate, ModelChoiceContext context, CandidateAvailability availability) =>
        availability != CandidateAvailability.Unusable
        && (!candidate.IsPaid || (PaidAllowedForRole(context.Slot, context.Domain, context.RoleCount) && context.PaidUsed < ModelMatrix.MaxPaidReviewersPerCrew));

    /// <summary>Chooses the model for one actor.</summary>
    public static ModelAssignment Choose(
        IReadOnlyList<ModelCandidate> candidates,
        ModelChoiceContext context,
        Func<ModelCandidate, CandidateAvailability> availability,
        Func<ModelCandidate, bool> isExecutorModel)
    {
        var reviewer = context.Slot.IsReviewerRole();
        var executorFamily = context.Executor?.Family();
        var permitted = candidates.Where(c => IsPermitted(c, context, availability(c))).ToList();

        if (permitted.Count == 0)
        {
            var last = candidates.FirstOrDefault(c => !c.IsPaid) ?? candidates[0];
            var lastWarning = $"no usable candidate for slot {context.Slot} in domain {context.Domain}; falling back to {last}";
            if (isExecutorModel(last))
                lastWarning += "; it is the executor's model, model plurality is not met";
            if (reviewer && context.UsedInSlot.GetValueOrDefault(last) > 0)
                lastWarning += $"; {last} is already used by another {context.Slot}";
            return new ModelAssignment(last, IsFallback: true, lastWarning);
        }

        var ring = reviewer
            ? FamilyOrdered(permitted.Where(c => availability(c) != CandidateAvailability.Missing && !isExecutorModel(c)), context.Executor)
                .Take(ModelMatrix.ReviewerRingSize)
                .ToList()
            : [];

        var chosen = permitted
            .OrderBy(c => availability(c) == CandidateAvailability.Missing ? 1 : 0)
            .ThenBy(c => isExecutorModel(c) ? 1 : 0)
            .ThenBy(c => reviewer && !ring.Contains(c) ? 1 : 0)
            .ThenBy(c => reviewer ? context.UsedInSlot.GetValueOrDefault(c) : 0)
            .ThenBy(c => reviewer && executorFamily is not null && c.Family() == executorFamily ? 1 : 0)
            .ThenBy(c => IndexOf(candidates, c))
            .First();

        var warnings = new List<string>();
        var primary = candidates[0];
        if (chosen != primary && availability(primary) is CandidateAvailability.Missing or CandidateAvailability.Unusable)
            warnings.Add($"primary candidate {primary} is not available in the live catalog");
        if (availability(chosen) == CandidateAvailability.Missing)
            warnings.Add($"{chosen} is not listed by the live catalog either; no listed candidate was permitted");
        if (isExecutorModel(chosen))
            warnings.Add("the only permitted candidate is the executor's model; model plurality is not met");
        if (reviewer && context.UsedInSlot.GetValueOrDefault(chosen) > 0 && context.RoleCount <= ModelMatrix.ReviewerRingSize)
            warnings.Add($"{chosen} is already used by another {context.Slot}; not enough distinct candidates for {context.RoleCount} reviewers");

        var isFallback = availability(chosen) == CandidateAvailability.Missing || isExecutorModel(chosen);
        return new ModelAssignment(chosen, isFallback, warnings.Count == 0 ? null : string.Join("; ", warnings));
    }

    private static bool PaidAllowedForRole(ModelSlot slot, ModelDomain domain, int roleCount) =>
        slot.IsReviewerRole() && (domain.IsCoreDomain() || roleCount >= ModelMatrix.PaidOutsideCoreDomainsMinRoleCount);

    private static IEnumerable<ModelCandidate> FamilyOrdered(IEnumerable<ModelCandidate> candidates, ModelCandidate? executor)
    {
        var list = candidates.ToList();
        if (executor is null)
            return list;
        var family = executor.Family();
        return list.Where(c => c.Family() != family).Concat(list.Where(c => c.Family() == family));
    }

    private static int IndexOf(IReadOnlyList<ModelCandidate> candidates, ModelCandidate candidate)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i] == candidate)
                return i;
        }

        return candidates.Count;
    }
}
