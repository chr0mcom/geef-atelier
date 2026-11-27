using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Crew.Models;

/// <summary>
/// Runs the CPU-heavy invariant enumeration alone: in parallel it starves the thread pool and makes wall-clock budget
/// tests elsewhere (e.g. the model-catalog deadline tests) fail spuriously.
/// </summary>
[CollectionDefinition(nameof(ModelAssignmentInvariantCollection), DisableParallelization = true)]
public sealed class ModelAssignmentInvariantCollection;

/// <summary>
/// Exhaustive invariants of the model-matrix assignment (D-066/12, architecture §9.2). Every combination of catalog state
/// per provider, executor, crew domain, reviewer composition (slot, 1–4 same-slot reviewers, mixed profile domains) and
/// pre-used paid budget is assigned, and each invariant is checked as an independent property — not by re-implementing
/// the algorithm. The example tests in <see cref="ModelAssignmentServiceTests"/> stay the regression anchor (I6).
/// </summary>
[Collection(nameof(ModelAssignmentInvariantCollection))]
public sealed class ModelAssignmentInvariantTests
{
    private enum CatalogState
    {
        Unknown,
        AllListed,
        PartlyMissing,
        Unusable,
    }

    private static readonly string[] Providers = [ModelMatrix.ClaudeCli, ModelMatrix.CodexCli, "openrouter"];
    private static readonly ModelSlot[] ReviewerSlots = [ModelSlot.FactReviewer, ModelSlot.StyleReviewer, ModelSlot.DomainReviewer, ModelSlot.Helper];

    private static readonly ModelDomain?[][] OverridePatterns =
    [
        [null],
        [ModelDomain.General],
        [ModelDomain.Legal],
        [ModelDomain.Academic],
        [null, ModelDomain.Legal],
    ];
    private static readonly ModelSlot[] AdvisorSlots = [ModelSlot.StrategicAdvisor, ModelSlot.CriticalAdvisor, ModelSlot.DomainExpertAdvisor];

    private static readonly ModelCandidate[] Executors =
    [
        new(ModelMatrix.ClaudeCli, "claude-opus-latest"),
        new(ModelMatrix.CodexCli, ModelMatrix.CodexSolAlias),
        new(ModelMatrix.ClaudeCli, "claude-fable-latest"),
    ];

    private static readonly ModelCandidate PaidManual = new("xai", "grok-4.5");
    private static readonly ModelCandidate UnpaidManual = new(ModelMatrix.ClaudeCli, "claude-haiku-latest");

    private sealed record Case(
        IReadOnlyDictionary<string, CatalogState> Catalog,
        ModelCandidate Executor,
        ModelDomain CrewDomain,
        IReadOnlyList<ReviewerAssignmentRequest> Requests,
        IReadOnlyList<ModelAssignment> Result)
    {
        public override string ToString() =>
            $"catalog=[{string.Join(", ", Catalog.Select(kv => $"{kv.Key}:{kv.Value}"))}] executor={Executor} crewDomain={CrewDomain} " +
            $"requests=[{string.Join(", ", Requests.Select(r => r.Slot is null ? $"manual:{r.Current}" : $"{r.Slot}/{r.ProfileDomain?.ToString() ?? "-"}"))}] " +
            $"result=[{string.Join(", ", Result.Select(a => $"{a.Candidate}{(a.Warning is null ? "" : $" ({a.Warning})")}"))}]";
    }

    private static readonly Lazy<IReadOnlyList<Case>> ReviewerCases = new(() => BuildReviewerCasesAsync().GetAwaiter().GetResult());

    [Fact]
    public void I1_NoPaidReviewerOutsideCoreDomainsBelowTheMinimumRoleCount()
    {
        foreach (var c in ReviewerCases.Value)
        {
            var roleCounts = c.Requests.Where(r => r.Slot is not null).GroupBy(r => r.Slot!.Value).ToDictionary(g => g.Key, g => g.Count());
            for (var i = 0; i < c.Requests.Count; i++)
            {
                if (c.Requests[i].Slot is not { } slot || !c.Result[i].Candidate.IsPaid)
                    continue;
                var domain = c.Requests[i].ProfileDomain ?? c.CrewDomain;
                Assert.True(domain.IsCoreDomain() || roleCounts[slot] >= ModelMatrix.PaidOutsideCoreDomainsMinRoleCount, $"I1 violated: {c}");
            }
        }
    }

    [Fact]
    public void I2_AtMostTwoPaidReviewersPerCrew()
    {
        foreach (var c in ReviewerCases.Value)
        {
            var manualPaid = c.Requests.Count(r => r.Slot is null && r.Current.IsPaid);
            var managedPaid = c.Requests.Select((r, i) => (r, i)).Count(x => x.r.Slot is not null && c.Result[x.i].Candidate.IsPaid);
            Assert.True(managedPaid == 0 || manualPaid + managedPaid <= ModelMatrix.MaxPaidReviewersPerCrew, $"I2 violated: {c}");
        }
    }

    [Fact]
    public void I3_AnUnusableProviderOnlyWhenNoPermittedCandidateWasLeft()
    {
        foreach (var c in ReviewerCases.Value)
        {
            foreach (var choice in Choices(c))
            {
                if (Availability(c.Catalog, choice.Assignment.Candidate) != Fact.Unusable)
                    continue;
                Assert.True(choice.Permitted.Count == 0, $"I3 violated (permitted candidates existed: {string.Join(", ", choice.Permitted)}): {c}");
                Assert.True(choice.Assignment.IsFallback && choice.Assignment.Warning?.Contains("no usable candidate", StringComparison.Ordinal) == true, $"I3 violated (not marked): {c}");
            }
        }
    }

    [Fact]
    public void I4_TheExecutorsModelAlwaysCarriesAPluralityWarning()
    {
        foreach (var c in ReviewerCases.Value)
        {
            foreach (var choice in Choices(c))
            {
                if (CliRoute.IsSameModel(choice.Assignment.Candidate.Model, c.Executor.Model))
                    Assert.True(choice.Assignment.Warning?.Contains("plurality", StringComparison.Ordinal) == true, $"I4 violated: {c}");
            }
        }
    }

    [Fact]
    public void I5_ARepeatWithinTheRingSizeOnlyWhenNoFreeCandidateWasLeft()
    {
        foreach (var c in ReviewerCases.Value)
        {
            var roleCounts = c.Requests.Where(r => r.Slot is not null).GroupBy(r => r.Slot!.Value).ToDictionary(g => g.Key, g => g.Count());
            foreach (var choice in Choices(c))
            {
                if (roleCounts[choice.Slot] > ModelMatrix.ReviewerRingSize || !choice.UsedBefore.Contains(choice.Assignment.Candidate))
                    continue;
                var free = choice.Permitted
                    .Where(p => Availability(c.Catalog, p) != Fact.Missing)
                    .Where(p => !CliRoute.IsSameModel(p.Model, c.Executor.Model))
                    .Where(p => !choice.UsedBefore.Contains(p))
                    .ToList();
                Assert.True(free.Count == 0, $"I5 violated (repeated {choice.Assignment.Candidate} although {string.Join(", ", free)} was free): {c}");
                Assert.True(choice.Assignment.Warning?.Contains("already used", StringComparison.Ordinal) == true, $"I5 violated (repeat without warning): {c}");
            }
        }
    }

    private enum Fact
    {
        Unknown,
        Listed,
        Missing,
        Unusable,
    }

    private sealed record Choice(ModelSlot Slot, ModelAssignment Assignment, IReadOnlyList<ModelCandidate> Permitted, IReadOnlySet<ModelCandidate> UsedBefore);

    /// <summary>
    /// Replays the managed choices of a case in index order and reconstructs, for each, the state at that moment from the
    /// results alone: the paid budget already spent and the models already given to the slot. "Permitted" applies the hard
    /// rules as stated in the architecture (H1 provider usable, H2 paid only within the cap and the role/domain rule).
    /// </summary>
    private static IEnumerable<Choice> Choices(Case c)
    {
        var roleCounts = c.Requests.Where(r => r.Slot is not null).GroupBy(r => r.Slot!.Value).ToDictionary(g => g.Key, g => g.Count());
        var paidUsed = c.Requests.Count(r => r.Slot is null && r.Current.IsPaid);
        var used = new Dictionary<ModelSlot, HashSet<ModelCandidate>>();
        for (var i = 0; i < c.Requests.Count; i++)
        {
            if (c.Requests[i].Slot is not { } slot)
                continue;
            var domain = c.Requests[i].ProfileDomain ?? c.CrewDomain;
            var paidAllowed = paidUsed < ModelMatrix.MaxPaidReviewersPerCrew
                              && (domain.IsCoreDomain() || roleCounts[slot] >= ModelMatrix.PaidOutsideCoreDomainsMinRoleCount);
            var permitted = ModelMatrix.CandidatesFor(slot, domain)
                .Where(p => Availability(c.Catalog, p) != Fact.Unusable)
                .Where(p => !p.IsPaid || paidAllowed)
                .ToList();
            var usedBefore = used.TryGetValue(slot, out var set) ? new HashSet<ModelCandidate>(set) : [];
            yield return new Choice(slot, c.Result[i], permitted, usedBefore);

            if (!used.TryGetValue(slot, out set))
                used[slot] = set = [];
            set.Add(c.Result[i].Candidate);
            if (c.Result[i].Candidate.IsPaid)
                paidUsed++;
        }
    }

    private static Fact Availability(IReadOnlyDictionary<string, CatalogState> catalog, ModelCandidate candidate) =>
        catalog.GetValueOrDefault(candidate.Provider, CatalogState.AllListed) switch
        {
            CatalogState.Unknown => Fact.Unknown,
            CatalogState.Unusable => Fact.Unusable,
            CatalogState.PartlyMissing when StateCatalog.MissingInPartlyMissing.Contains(candidate.Model) => Fact.Missing,
            _ => Fact.Listed,
        };

    [Fact]
    public async Task I1_I3_I4_AdvisorsAndExecutorsNeverGetPaidModelsAndWarnWhenTheyCannotAvoidTheExecutor()
    {
        foreach (var catalog in CatalogStates())
        {
            var service = Service(catalog);
            foreach (var domain in Enum.GetValues<ModelDomain>())
            {
                var executorAssignment = await service.AssignAsync(ModelSlot.Executor, domain);
                Assert.False(executorAssignment.Candidate.IsPaid, $"executor got a paid model: {Describe(catalog)} {domain}");
                if (catalog[executorAssignment.Candidate.Provider] == CatalogState.Unusable)
                {
                    Assert.DoesNotContain(ModelMatrix.CandidatesFor(ModelSlot.Executor, domain), p => catalog[p.Provider] != CatalogState.Unusable);
                    Assert.Contains("no usable candidate", executorAssignment.Warning ?? string.Empty);
                }

                foreach (var executor in Executors)
                {
                    foreach (var slot in AdvisorSlots)
                    {
                        var assignment = await service.AssignAsync(slot, domain, executor);
                        var context = $"{Describe(catalog)} {domain} {slot} executor={executor} → {assignment.Candidate} ({assignment.Warning})";
                        Assert.False(assignment.Candidate.IsPaid, $"advisor got a paid model: {context}");
                        if (catalog[assignment.Candidate.Provider] == CatalogState.Unusable)
                        {
                            Assert.DoesNotContain(ModelMatrix.CandidatesFor(slot, domain), p => catalog[p.Provider] != CatalogState.Unusable);
                            Assert.Contains("no usable candidate", assignment.Warning ?? string.Empty);
                        }

                        if (CliRoute.IsSameModel(assignment.Candidate.Model, executor.Model))
                            Assert.True(assignment.Warning?.Contains("plurality", StringComparison.Ordinal) == true, $"advisor on the executor's model without plurality warning: {context}");
                    }
                }
            }
        }
    }

    [Fact]
    public void EveryCandidateListHasAnUnpaidCandidateAndOnlyReviewerSlotsHavePaidOnes()
    {
        foreach (var slot in Enum.GetValues<ModelSlot>())
        {
            foreach (var domain in Enum.GetValues<ModelDomain>())
            {
                var candidates = ModelMatrix.CandidatesFor(slot, domain);
                Assert.Contains(candidates, c => !c.IsPaid);
                if (!slot.IsReviewerRole())
                    Assert.DoesNotContain(candidates, c => c.IsPaid);
            }
        }
    }

    [Fact]
    public void TheCaseSpaceIsExhaustive()
    {
        Assert.True(ReviewerCases.Value.Count > 300_000, $"only {ReviewerCases.Value.Count} cases");
    }

    private static async Task<IReadOnlyList<Case>> BuildReviewerCasesAsync()
    {
        var compositions = Compositions().ToList();
        var perCatalog = await Task.WhenAll(CatalogStates().Select(catalog => Task.Run(async () =>
        {
            var service = Service(catalog);
            var cases = new List<Case>();
            foreach (var executor in Executors)
            {
                foreach (var crewDomain in Enum.GetValues<ModelDomain>())
                {
                    foreach (var requests in compositions)
                    {
                        var result = await service.AssignReviewersAsync(requests, crewDomain, executor);
                        cases.Add(new Case(catalog, executor, crewDomain, requests, result));
                    }
                }
            }

            return cases;
        })));

        return perCatalog.SelectMany(c => c).ToList();
    }

    private static IEnumerable<IReadOnlyList<ReviewerAssignmentRequest>> Compositions()
    {
        var manualVariants = new[] { Array.Empty<ModelCandidate>(), [PaidManual, PaidManual], [PaidManual, UnpaidManual] };
        foreach (var slot in ReviewerSlots)
        {
            var other = slot == ModelSlot.DomainReviewer ? ModelSlot.FactReviewer : ModelSlot.DomainReviewer;
            for (var count = 1; count <= 4; count++)
            {
                foreach (var pattern in OverridePatterns)
                {
                    foreach (var otherCount in new[] { 0, 2 })
                    {
                        foreach (var manual in manualVariants)
                        {
                            var requests = manual.Select(m => new ReviewerAssignmentRequest(null, null, m)).ToList();
                            var placedOther = 0;
                            for (var k = 0; k < count; k++)
                            {
                                requests.Add(new ReviewerAssignmentRequest(slot, pattern[k % pattern.Length], new ModelCandidate("x", "y")));
                                if (placedOther < otherCount)
                                {
                                    requests.Add(new ReviewerAssignmentRequest(other, null, new ModelCandidate("x", "y")));
                                    placedOther++;
                                }
                            }

                            for (; placedOther < otherCount; placedOther++)
                                requests.Add(new ReviewerAssignmentRequest(other, null, new ModelCandidate("x", "y")));
                            yield return requests;
                        }
                    }
                }
            }
        }
    }

    private static IEnumerable<IReadOnlyDictionary<string, CatalogState>> CatalogStates()
    {
        foreach (var claude in Enum.GetValues<CatalogState>())
        {
            foreach (var codex in Enum.GetValues<CatalogState>())
            {
                foreach (var openRouter in Enum.GetValues<CatalogState>())
                {
                    yield return new Dictionary<string, CatalogState>
                    {
                        [ModelMatrix.ClaudeCli] = claude,
                        [ModelMatrix.CodexCli] = codex,
                        ["openrouter"] = openRouter,
                    };
                }
            }
        }
    }

    private static string Describe(IReadOnlyDictionary<string, CatalogState> catalog) =>
        string.Join(", ", catalog.Select(kv => $"{kv.Key}:{kv.Value}"));

    private static ModelAssignmentService Service(IReadOnlyDictionary<string, CatalogState> catalog) =>
        new(new StateCatalog(catalog), NullLogger<ModelAssignmentService>.Instance);

    private sealed class StateCatalog(IReadOnlyDictionary<string, CatalogState> states) : IModelCatalog
    {
        public static readonly IReadOnlySet<string> MissingInPartlyMissing =
            new HashSet<string> { "claude-opus-latest", "claude-fable-latest", ModelMatrix.CodexAstraAlias, "google/gemini-4-neon" };

        public Task<IReadOnlyList<string>?> ListReportedModelIdsAsync(string providerName, CancellationToken ct = default)
        {
            var all = ModelMatrix.All.Where(c => c.Provider == providerName).Select(c => c.Model).ToList();
            IReadOnlyList<string>? ids = states.GetValueOrDefault(providerName, CatalogState.AllListed) switch
            {
                CatalogState.Unknown => null,
                CatalogState.Unusable => [],
                CatalogState.PartlyMissing => all.Where(m => !MissingInPartlyMissing.Contains(m)).Append("placeholder-model").ToList(),
                _ => all,
            };
            return Task.FromResult(ids);
        }

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(string providerName, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public Task<IReadOnlyList<ModelInfo>> RefreshAsync(string providerName, CancellationToken ct = default) => ListModelsAsync(providerName, ct);
        public Task<IReadOnlyList<ModelInfo>> WarmUpAsync(string providerName, CancellationToken ct = default) => ListModelsAsync(providerName, ct);
        public ModelCatalogSource GetSource(string providerName) => ModelCatalogSource.Live;
        public Task<string> ResolveModelAsync(string providerName, string modelId, CancellationToken ct = default) => Task.FromResult(modelId);
    }
}
