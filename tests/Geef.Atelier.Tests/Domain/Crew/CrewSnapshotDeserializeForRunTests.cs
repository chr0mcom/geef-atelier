using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Tests.Fakes;

namespace Geef.Atelier.Tests.Domain.Crew;

/// <summary>The strict snapshot read used by the orchestrator: no fallback crew, a clear reason for every failure.</summary>
public sealed class CrewSnapshotDeserializeForRunTests
{
    [Fact]
    public void ReadsAValidSnapshot()
    {
        var snapshot = CrewSnapshot.DeserializeForRun(TestCrewSnapshots.KlassikJson);

        Assert.Equal(SystemCrew.DefaultExecutorProfile.Name, snapshot.Executor.Name);
        Assert.Equal(2, snapshot.Reviewers.Count);
    }

    [Theory]
    [InlineData(null, "no saved setup")]
    [InlineData("   ", "no saved setup")]
    [InlineData("{not json", "could not be read")]
    [InlineData("""{"schemaVersion":3,"executor":42,"reviewers":[],"advisors":[]}""", "could not be read")]
    [InlineData("null", "empty")]
    [InlineData("""{"schemaVersion":4,"executor":{"name":"x"},"reviewers":[],"advisors":[]}""", "unknown format (version 4)")]
    [InlineData("""{"schemaVersion":0,"executor":{"name":"x"},"reviewers":[],"advisors":[]}""", "unknown format (version 0)")]
    [InlineData("""{"schemaVersion":3,"reviewers":[],"advisors":[]}""", "missing: writer")]
    [InlineData("""{"schemaVersion":3,"executor":{"name":"x"}}""", "missing: checks, advisors")]
    public void RejectsUnusableSnapshotsWithAReason(string? json, string expected)
    {
        var ex = Assert.Throws<InvalidCrewSnapshotException>(() => CrewSnapshot.DeserializeForRun(json));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void TolerantReadStaysAvailableForDisplay()
    {
        Assert.Null(CrewSnapshot.Deserialize("{not json"));
        Assert.NotNull(CrewSnapshot.Deserialize(TestCrewSnapshots.KlassikJson));
    }
}
