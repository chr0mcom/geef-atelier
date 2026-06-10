using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Tests.Domain.Crew;

public sealed class CrewSnapshotTests
{
    [Fact]
    public void CurrentSchemaVersion_IsTwo()
    {
        Assert.Equal(2, CrewSnapshot.CurrentSchemaVersion);
    }

    [Fact]
    public void CrewSnapshot_PropertiesRoundTrip()
    {
        var executor = new ExecutorProfile(
            "exec", "Exec", "Executor", "prompt", "provider", "model", null, true);

        var reviewer = new ReviewerProfile(
            "rev", "Rev", "Reviewer", "prompt", "provider", "model", null, true);

        var snapshot = new CrewSnapshot(1, "klassik", executor, [reviewer],
            EvaluationStrategy.Parallel, null, []);

        Assert.Equal(1, snapshot.SchemaVersion);
        Assert.Equal("klassik", snapshot.TemplateName);
        Assert.Equal(executor, snapshot.Executor);
        Assert.Single(snapshot.Reviewers);
        Assert.Equal(reviewer, snapshot.Reviewers[0]);
        Assert.Equal(EvaluationStrategy.Parallel, snapshot.EvaluationStrategy);
    }

    [Fact]
    public void AdvisorsProperty_CanBeEmptyArray()
    {
        var snapshot = new CrewSnapshot(
            CrewSnapshot.CurrentSchemaVersion, "klassik",
            SystemCrew.DefaultExecutorProfile,
            [SystemCrew.BriefingFidelityProfile],
            EvaluationStrategy.Parallel, null,
            Array.Empty<AdvisorProfile>());

        Assert.Empty(snapshot.Advisors);
    }
}
