using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Tests.Fakes;

namespace Geef.Atelier.Tests.Domain.Crew;

/// <summary>The strict snapshot read used by the orchestrator: no fallback crew, a clear reason for every failure.</summary>
public sealed class CrewSnapshotDeserializeForRunTests
{
    [Fact]
    public void ReadsAValidSnapshot()
    {
        var snapshot = CrewSnapshot.DeserializeForRun(TestCrewSnapshots.RunnableJson);

        Assert.Equal(TestLoop.Writer.Name, snapshot.Executor.Name);
        Assert.Equal(2, snapshot.Reviewers.Count);
    }

    [Theory]
    [InlineData(null, "no saved setup")]
    [InlineData("   ", "no saved setup")]
    [InlineData("{not json", "could not be read")]
    [InlineData("""{"schemaVersion":4,"executor":42,"reviewers":[]}""", "could not be read")]
    [InlineData("null", "empty")]
    [InlineData("""{"schemaVersion":3,"executor":{"name":"x"},"reviewers":[],"advisors":[]}""", "unknown format (version 3)")]
    [InlineData("""{"schemaVersion":5,"executor":{"name":"x"},"reviewers":[]}""", "unknown format (version 5)")]
    [InlineData("""{"schemaVersion":0,"executor":{"name":"x"},"reviewers":[]}""", "unknown format (version 0)")]
    [InlineData("""{"schemaVersion":4,"reviewers":[]}""", "missing: writer")]
    [InlineData("""{"schemaVersion":4,"executor":{"name":"x"}}""", "missing: checks")]
    public void RejectsUnusableSnapshotsWithAReason(string? json, string expected)
    {
        var ex = Assert.Throws<InvalidCrewSnapshotException>(() => CrewSnapshot.DeserializeForRun(json));

        Assert.Contains(expected, ex.Message);
    }

    /// <summary>
    /// Funktional 5 / B7 (Etappe 0, review round 2): the reason is a fixed sentence; the JSON path and position of the
    /// parser error stay in the inner exception for the log.
    /// </summary>
    [Theory]
    [InlineData("{not json")]
    [InlineData("""{"schemaVersion":4,"executor":42,"reviewers":[]}""")]
    [InlineData("""{"schemaVersion":4,"executor":{"name":"x"},"reviewers":[{"name":1}]}""")]
    public void UnreadableSnapshotsGiveAFixedSentenceWithoutParserDetails(string json)
    {
        var ex = Assert.Throws<InvalidCrewSnapshotException>(() => CrewSnapshot.DeserializeForRun(json));

        Assert.Equal("The saved setup of this task could not be read. Please submit it again.", ex.Message);
        Assert.NotNull(ex.InnerException);
    }

    [Fact]
    public void TolerantReadStaysAvailableForDisplay()
    {
        Assert.Null(CrewSnapshot.Deserialize("{not json"));
        Assert.NotNull(CrewSnapshot.Deserialize(TestCrewSnapshots.RunnableJson));
    }
}
