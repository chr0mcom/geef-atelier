using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Application.Crew.Models;

/// <inheritdoc />
internal sealed class ModelAssignmentService(IModelCatalog modelCatalog, ILogger<ModelAssignmentService> logger) : IModelAssignmentService
{
    /// <inheritdoc />
    public Task<ModelAssignment> AssignAsync(ModelSlot slot, ModelDomain domain, ModelCandidate? executor = null, CancellationToken ct = default) =>
        new Session(modelCatalog, ct).AssignSingleAsync(slot, domain, executor);

    /// <inheritdoc />
    public Task<IReadOnlyList<ModelAssignment>> AssignReviewersAsync(
        IReadOnlyList<ReviewerAssignmentRequest> reviewers,
        ModelDomain crewDomain,
        ModelCandidate executor,
        CancellationToken ct = default) =>
        new Session(modelCatalog, ct).AssignReviewersAsync(reviewers, crewDomain, executor);

    /// <inheritdoc />
    public Task<CrewSnapshot> ApplyAsync(CrewSnapshot snapshot, CancellationToken ct = default) =>
        ApplyAsync(snapshot, new List<ModelAssignmentWarning>(), ct);

    /// <inheritdoc />
    public async Task<CrewSnapshot> ApplyAsync(CrewSnapshot snapshot, ICollection<ModelAssignmentWarning> warnings, CancellationToken ct = default)
    {
        var session = new Session(modelCatalog, ct);
        var crewDomain = snapshot.CrewDomain ?? ModelDomain.General;

        var executor = snapshot.Executor;
        if (executor.ModelSlot is { } executorSlot)
        {
            var assignment = await session.AssignSingleAsync(executorSlot, executor.ModelDomain ?? crewDomain, executor: null);
            ReportTo(warnings, executor.Name, executor.DisplayName, assignment);
            executor = executor with { Provider = assignment.Candidate.Provider, Model = assignment.Candidate.Model };
        }

        var executorBinding = new ModelCandidate(executor.Provider, executor.Model);

        var reviewerRequests = snapshot.Reviewers
            .Select(r => new ReviewerAssignmentRequest(r.ModelSlot, r.ModelDomain, new ModelCandidate(r.Provider, r.Model)))
            .ToList();
        var reviewerAssignments = await session.AssignReviewersAsync(reviewerRequests, crewDomain, executorBinding);
        var reviewers = snapshot.Reviewers
            .Select((r, i) =>
            {
                ReportTo(warnings, r.Name, r.DisplayName, reviewerAssignments[i]);
                if (r.ModelSlot is null)
                    return r;
                return r with { Provider = reviewerAssignments[i].Candidate.Provider, Model = reviewerAssignments[i].Candidate.Model };
            })
            .ToList();

        return snapshot with
        {
            Executor = executor,
            Reviewers = reviewers,
        };
    }

    private void ReportTo(ICollection<ModelAssignmentWarning> warnings, string actorName, string? displayName, ModelAssignment assignment)
    {
        if (assignment.Warning is null)
            return;
        warnings.Add(new ModelAssignmentWarning(actorName, assignment.Warning, displayName));
        logger.LogWarning(
            "Model matrix: actor '{Actor}' assigned {Candidate} — {Warning}",
            actorName, assignment.Candidate, assignment.Warning);
    }

    private sealed class Session(IModelCatalog modelCatalog, CancellationToken ct)
    {
        private readonly Dictionary<string, IReadOnlyList<string>?> _available = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(string Provider, string Model), string> _resolved = [];

        public async Task<ModelAssignment> AssignSingleAsync(ModelSlot slot, ModelDomain domain, ModelCandidate? executor)
        {
            var candidates = ModelMatrix.CandidatesFor(slot, domain);
            var facts = await FactsAsync(candidates, executor);
            var context = new ModelChoiceContext(slot, domain, executor, RoleCount: 1, PaidUsed: 0, new Dictionary<ModelCandidate, int>());
            return ModelChooser.Choose(candidates, context, facts.Availability, facts.IsExecutorModel);
        }

        public async Task<IReadOnlyList<ModelAssignment>> AssignReviewersAsync(
            IReadOnlyList<ReviewerAssignmentRequest> reviewers, ModelDomain crewDomain, ModelCandidate executor)
        {
            var result = new ModelAssignment[reviewers.Count];
            var paidUsed = 0;

            for (var i = 0; i < reviewers.Count; i++)
            {
                if (reviewers[i].Slot is not null)
                    continue;
                var sharesExecutor = await IsSameModelAsync(reviewers[i].Current, executor);
                result[i] = new ModelAssignment(reviewers[i].Current, IsFallback: false,
                    Warning: sharesExecutor ? "manual reviewer binding shares the executor's model; model plurality is not met" : null);
                if (reviewers[i].Current.IsPaid)
                    paidUsed++;
            }

            var roleCounts = reviewers.Where(r => r.Slot is not null).GroupBy(r => r.Slot!.Value).ToDictionary(g => g.Key, g => g.Count());
            var usedInSlot = new Dictionary<ModelSlot, Dictionary<ModelCandidate, int>>();

            for (var i = 0; i < reviewers.Count; i++)
            {
                if (reviewers[i].Slot is not { } slot)
                    continue;

                var domain = reviewers[i].ProfileDomain ?? crewDomain;
                var candidates = ModelMatrix.CandidatesFor(slot, domain);
                var facts = await FactsAsync(candidates, executor);

                if (!usedInSlot.TryGetValue(slot, out var used))
                {
                    used = [];
                    usedInSlot[slot] = used;
                }

                var context = new ModelChoiceContext(slot, domain, executor, roleCounts[slot], paidUsed, used);
                var assignment = ModelChooser.Choose(candidates, context, facts.Availability, facts.IsExecutorModel);
                result[i] = assignment;
                used[assignment.Candidate] = used.GetValueOrDefault(assignment.Candidate) + 1;
                if (assignment.Candidate.IsPaid)
                    paidUsed++;
            }

            return result;
        }

        private async Task<(Func<ModelCandidate, CandidateAvailability> Availability, Func<ModelCandidate, bool> IsExecutorModel)> FactsAsync(
            IReadOnlyList<ModelCandidate> candidates, ModelCandidate? executor)
        {
            var availability = new Dictionary<ModelCandidate, CandidateAvailability>();
            var executorModel = new Dictionary<ModelCandidate, bool>();
            foreach (var candidate in candidates)
            {
                availability[candidate] = await AvailabilityOfAsync(candidate);
                executorModel[candidate] = executor is not null && await IsSameModelAsync(candidate, executor);
            }

            return (c => availability.GetValueOrDefault(c, CandidateAvailability.Unknown), c => executorModel.GetValueOrDefault(c));
        }

        private async Task<CandidateAvailability> AvailabilityOfAsync(ModelCandidate candidate)
        {
            if (!_available.TryGetValue(candidate.Provider, out var ids))
            {
                ids = await LoadAsync(candidate.Provider);
                _available[candidate.Provider] = ids;
            }

            return ids switch
            {
                null => CandidateAvailability.Unknown,
                { Count: 0 } => CandidateAvailability.Unusable,
                _ when ids.Contains(candidate.Model, StringComparer.OrdinalIgnoreCase) => CandidateAvailability.Confirmed,
                _ => CandidateAvailability.Missing,
            };
        }

        private async Task<IReadOnlyList<string>?> LoadAsync(string provider)
        {
            try
            {
                return await modelCatalog.ListReportedModelIdsAsync(provider, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return null;
            }
        }

        private async Task<bool> IsSameModelAsync(ModelCandidate candidate, ModelCandidate executor)
        {
            if (CliRoute.IsSameModel(candidate.Model, executor.Model))
                return true;
            var resolvedCandidate = await ResolveAsync(candidate);
            var resolvedExecutor = await ResolveAsync(executor);
            return CliRoute.IsSameModel(resolvedCandidate, resolvedExecutor);
        }

        private async Task<string> ResolveAsync(ModelCandidate candidate)
        {
            var key = (candidate.Provider, candidate.Model);
            if (_resolved.TryGetValue(key, out var cached))
                return cached;
            string resolved;
            try
            {
                resolved = await modelCatalog.ResolveModelAsync(candidate.Provider, candidate.Model, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                resolved = candidate.Model;
            }

            _resolved[key] = resolved;
            return resolved;
        }
    }
}
