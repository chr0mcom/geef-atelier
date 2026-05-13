using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Tests.Application.Runs;

/// <summary>
/// Unit tests for <see cref="RunWithGroundingViewModel"/> construction and grouping logic.
/// These tests validate the grouping contracts without hitting a database by building the
/// ViewModel directly from known consultation data using the same logic as RunService.
/// </summary>
public sealed class RunWithGroundingViewModelTests
{
    // ─── helpers ───────────────────────────────────────────────────────────────

    private static RunEntity MakeRun(Guid? id = null) => new()
    {
        Id           = id ?? Guid.NewGuid(),
        CreatedAt    = DateTimeOffset.UtcNow,
        Status       = RunStatus.Completed,
        BriefingText = "Test briefing",
        ConfigJson   = "{}",
    };

    private static RunDetails MakeDetails(RunEntity? run = null) =>
        new(run ?? MakeRun(), Array.Empty<IterationWithFindings>());

    private static AdvisorConsultation MakeConsultation(
        Guid runId,
        string profileName,
        int iterationNumber) =>
        new(
            Id:                Guid.NewGuid(),
            RunId:             runId,
            IterationNumber:   iterationNumber,
            AdvisorProfileName: profileName,
            Output:            "Some output.",
            CreatedAt:         DateTimeOffset.UtcNow);

    private static ExecutorProfile MakeExecutor() => new(
        Name:        "test-executor",
        DisplayName: "Test Executor",
        Description: "Desc",
        SystemPrompt: "Prompt",
        Provider:    "openrouter",
        Model:       "anthropic/claude-opus-4.7",
        MaxTokens:   null,
        IsSystem:    false);

    private static AdvisorProfile MakeAdvisorProfile(string name, AdvisorTrigger trigger) => new(
        Name:        name,
        DisplayName: name,
        Description: "Desc",
        SystemPrompt: "Prompt",
        Provider:    "openrouter",
        Model:       "google/gemini-2.5-flash",
        MaxTokens:   null,
        Mode:        AdvisorMode.Strategic,
        Trigger:     trigger,
        IsSystem:    true);

    private static CrewSnapshot MakeSnapshot(params AdvisorProfile[] advisors) => new(
        SchemaVersion:      CrewSnapshot.CurrentSchemaVersion,
        TemplateName:       "klassik",
        Executor:           MakeExecutor(),
        Reviewers:          Array.Empty<ReviewerProfile>(),
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        Advisors:           advisors);

    /// <summary>
    /// Applies the same grouping logic that <see cref="RunService.GetRunWithGroundingAsync"/> uses,
    /// so we can unit-test the algorithm without a database.
    /// </summary>
    private static RunWithGroundingViewModel BuildViewModel(
        RunDetails details,
        CrewSnapshot? snapshot,
        IReadOnlyList<AdvisorConsultation> consultations)
    {
        var triggerDict = snapshot is not null
            ? snapshot.Advisors.ToDictionary(a => a.Name, a => a.Trigger, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, AdvisorTrigger>(StringComparer.OrdinalIgnoreCase);

        var recoveryAdvisors = consultations
            .Where(c => c.IterationNumber == -1)
            .ToList();

        var nonRecovery = consultations
            .Where(c => c.IterationNumber != -1)
            .ToList();

        var groundingAdvisors = nonRecovery
            .Where(c => triggerDict.TryGetValue(c.AdvisorProfileName, out var t)
                        && t == AdvisorTrigger.BeforeFirstExecution)
            .ToList();

        var iterationSet = new HashSet<Guid>(groundingAdvisors.Select(c => c.Id));
        var advisorsByIteration = nonRecovery
            .Where(c => !iterationSet.Contains(c.Id))
            .ToLookup(c => c.IterationNumber);

        return new RunWithGroundingViewModel(
            details,
            snapshot,
            details.Run.BriefingText,
            groundingAdvisors,
            recoveryAdvisors,
            advisorsByIteration,
            []);
    }

    // ─── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GroundingAdvisors_ContainsOnly_BeforeFirstExecution()
    {
        var runId        = Guid.NewGuid();
        var details      = MakeDetails(MakeRun(runId));
        var snapshot     = MakeSnapshot(
            MakeAdvisorProfile("grounding-advisor", AdvisorTrigger.BeforeFirstExecution),
            MakeAdvisorProfile("iter-advisor",      AdvisorTrigger.BeforeEveryExecution));
        var consultations = new[]
        {
            MakeConsultation(runId, "grounding-advisor", iterationNumber: 1),
            MakeConsultation(runId, "iter-advisor",      iterationNumber: 1),
        };

        var vm = BuildViewModel(details, snapshot, consultations);

        var groundingName = Assert.Single(vm.GroundingAdvisors).AdvisorProfileName;
        Assert.Equal("grounding-advisor", groundingName);
    }

    [Fact]
    public void RecoveryAdvisors_ContainsOnly_IterationMinusOne()
    {
        var runId        = Guid.NewGuid();
        var details      = MakeDetails(MakeRun(runId));
        var snapshot     = MakeSnapshot(
            MakeAdvisorProfile("recovery-advisor", AdvisorTrigger.OnConvergenceFailure));
        var consultations = new[]
        {
            MakeConsultation(runId, "recovery-advisor", iterationNumber: -1),
            MakeConsultation(runId, "recovery-advisor", iterationNumber: 1),
        };

        var vm = BuildViewModel(details, snapshot, consultations);

        var recoveryItem = Assert.Single(vm.RecoveryAdvisors);
        Assert.Equal(-1, recoveryItem.IterationNumber);

        // The non-recovery consultation ends up in AdvisorsByIteration
        Assert.True(vm.AdvisorsByIteration[1].Any());
    }

    [Fact]
    public void AdvisorsByIteration_Contains_BeforeEveryExecution_Consultations()
    {
        var runId        = Guid.NewGuid();
        var details      = MakeDetails(MakeRun(runId));
        var snapshot     = MakeSnapshot(
            MakeAdvisorProfile("iter-advisor", AdvisorTrigger.BeforeEveryExecution));
        var consultations = new[]
        {
            MakeConsultation(runId, "iter-advisor", iterationNumber: 1),
            MakeConsultation(runId, "iter-advisor", iterationNumber: 2),
        };

        var vm = BuildViewModel(details, snapshot, consultations);

        Assert.Empty(vm.GroundingAdvisors);
        Assert.Empty(vm.RecoveryAdvisors);
        Assert.Equal(2, vm.AdvisorsByIteration[1].Count() + vm.AdvisorsByIteration[2].Count());
    }

    [Fact]
    public void AdvisorsByIteration_Contains_UnknownProfile_WhenNotInSnapshot()
    {
        var runId        = Guid.NewGuid();
        var details      = MakeDetails(MakeRun(runId));
        // Snapshot has no entry for "unknown-advisor"
        var snapshot     = MakeSnapshot();
        var consultations = new[]
        {
            MakeConsultation(runId, "unknown-advisor", iterationNumber: 1),
        };

        var vm = BuildViewModel(details, snapshot, consultations);

        // Falls back to AdvisorsByIteration because the profile is not found in the snapshot
        Assert.Empty(vm.GroundingAdvisors);
        Assert.Single(vm.AdvisorsByIteration[1]);
    }

    [Fact]
    public void NullSnapshot_AllNonRecovery_GoToAdvisorsByIteration()
    {
        var runId        = Guid.NewGuid();
        var details      = MakeDetails(MakeRun(runId));
        var consultations = new[]
        {
            MakeConsultation(runId, "any-advisor",      iterationNumber: 1),
            MakeConsultation(runId, "another-advisor",  iterationNumber: 2),
            MakeConsultation(runId, "recovery-advisor", iterationNumber: -1),
        };

        var vm = BuildViewModel(details, snapshot: null, consultations);

        Assert.Empty(vm.GroundingAdvisors);
        Assert.Single(vm.RecoveryAdvisors);
        Assert.Equal(2, vm.AdvisorsByIteration[1].Count() + vm.AdvisorsByIteration[2].Count());
    }

    [Fact]
    public void GroundedBrief_MatchesBriefingText()
    {
        var run     = MakeRun();
        var details = MakeDetails(run);

        var vm = BuildViewModel(details, snapshot: null, consultations: Array.Empty<AdvisorConsultation>());

        Assert.Equal(run.BriefingText, vm.GroundedBrief);
    }

    [Fact]
    public void EmptyConsultations_AllBucketsEmpty()
    {
        var details = MakeDetails();

        var vm = BuildViewModel(details, snapshot: null, consultations: Array.Empty<AdvisorConsultation>());

        Assert.Empty(vm.GroundingAdvisors);
        Assert.Empty(vm.RecoveryAdvisors);
        // An empty lookup still exists but returns no items for any key
        Assert.False(vm.AdvisorsByIteration.Any());
    }
}
