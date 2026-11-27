using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Tests.Fakes;

namespace Geef.Atelier.Tests.Domain.Crew;

public sealed class CrewSnapshotTests
{
    [Fact]
    public void CurrentSchemaVersion_IsFour()
    {
        Assert.Equal(4, CrewSnapshot.CurrentSchemaVersion);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void A_snapshot_of_another_format_version_is_not_run(int version)
    {
        var json = TestCrewSnapshots.Serialize(TestCrewSnapshots.Runnable with { SchemaVersion = version });

        var error = Assert.Throws<InvalidCrewSnapshotException>(() => CrewSnapshot.DeserializeForRun(json));

        Assert.Contains($"unknown format (version {version})", error.Message, StringComparison.Ordinal);
        Assert.False(CrewSnapshot.IsUsableForRun(json));
    }

    [Fact]
    public void CrewSnapshot_PropertiesRoundTrip()
    {
        var executor = new ExecutorProfile(
            "exec", "Exec", "Executor", "prompt", "provider", "model", null, true);

        var reviewer = new ReviewerProfile(
            "rev", "Rev", "Reviewer", "prompt", "provider", "model", null, true);

        var snapshot = new CrewSnapshot(CrewSnapshot.CurrentSchemaVersion, "direct", executor, [reviewer],
            EvaluationStrategy.Parallel, null);

        var read = CrewSnapshot.DeserializeForRun(TestCrewSnapshots.Serialize(snapshot));

        Assert.Equal("direct", read.TemplateName);
        Assert.Equal(executor.Name, read.Executor.Name);
        Assert.Equal(reviewer.Name, Assert.Single(read.Reviewers).Name);
        Assert.Equal(EvaluationStrategy.Parallel, read.EvaluationStrategy);
    }
}
