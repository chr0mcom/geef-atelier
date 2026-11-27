using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Tests.Fakes;

namespace Geef.Atelier.Tests.Domain.Crew;

public sealed class CrewSnapshotTests
{
    [Fact]
    public void CurrentSchemaVersion_IsThree()
    {
        Assert.Equal(3, CrewSnapshot.CurrentSchemaVersion);
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
            TestLoop.Writer,
            [TestLoop.Reviewers[0]],
            EvaluationStrategy.Parallel, null,
            []);

        Assert.Empty(snapshot.Advisors);
    }
}
