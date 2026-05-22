using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Learning;

namespace Geef.Atelier.Tests.Domain.Learning;

public sealed class LearningDomainTests
{
    // ── RunKind enum ──────────────────────────────────────────────────────

    [Fact]
    public void RunKind_Standard_HasValue0()
        => Assert.Equal(0, (int)RunKind.Standard);

    [Fact]
    public void RunKind_Learning_HasValue1()
        => Assert.Equal(1, (int)RunKind.Learning);

    [Fact]
    public void RunKind_HasExactlyTwoValues()
        => Assert.Equal(2, Enum.GetValues<RunKind>().Length);

    // ── LearningStatus enum ──────────────────────────────────────────────

    [Fact]
    public void LearningStatus_Proposed_HasValue0()
        => Assert.Equal(0, (int)LearningStatus.Proposed);

    [Fact]
    public void LearningStatus_Approved_HasValue1()
        => Assert.Equal(1, (int)LearningStatus.Approved);

    [Fact]
    public void LearningStatus_Rejected_HasValue2()
        => Assert.Equal(2, (int)LearningStatus.Rejected);

    [Fact]
    public void LearningStatus_HasExactlyThreeValues()
        => Assert.Equal(3, Enum.GetValues<LearningStatus>().Length);

    // ── LearningEntry record ──────────────────────────────────────────────

    [Fact]
    public void LearningEntry_Construction_SetsAllProperties()
    {
        var id           = Guid.NewGuid();
        var sourceRunId  = Guid.NewGuid();
        var learningRunId = Guid.NewGuid();
        var createdAt    = DateTimeOffset.UtcNow;
        var approvedAt   = createdAt.AddMinutes(5);

        var entry = new LearningEntry(
            Id:                  id,
            Text:                "Some insight text",
            SourceRunId:         sourceRunId,
            LearningRunId:       learningRunId,
            Domain:              "akademisch",
            Status:              LearningStatus.Approved,
            StructuredFactsJson: "{\"key\":\"value\"}",
            OwnerUsername:       "alice",
            CreatedAt:           createdAt,
            ApprovedAt:          approvedAt);

        Assert.Equal(id, entry.Id);
        Assert.Equal("Some insight text", entry.Text);
        Assert.Equal(sourceRunId, entry.SourceRunId);
        Assert.Equal(learningRunId, entry.LearningRunId);
        Assert.Equal("akademisch", entry.Domain);
        Assert.Equal(LearningStatus.Approved, entry.Status);
        Assert.Equal("{\"key\":\"value\"}", entry.StructuredFactsJson);
        Assert.Equal("alice", entry.OwnerUsername);
        Assert.Equal(createdAt, entry.CreatedAt);
        Assert.Equal(approvedAt, entry.ApprovedAt);
    }

    [Fact]
    public void LearningEntry_NullLearningRunId_IsAllowed()
    {
        var entry = new LearningEntry(
            Id:                  Guid.NewGuid(),
            Text:                "text",
            SourceRunId:         Guid.NewGuid(),
            LearningRunId:       null,
            Domain:              "test",
            Status:              LearningStatus.Proposed,
            StructuredFactsJson: "{}",
            OwnerUsername:       "user",
            CreatedAt:           DateTimeOffset.UtcNow,
            ApprovedAt:          null);

        Assert.Null(entry.LearningRunId);
        Assert.Null(entry.ApprovedAt);
    }

    [Fact]
    public void LearningEntry_RecordEquality_SameValuesAreEqual()
    {
        var id        = Guid.NewGuid();
        var srcId     = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var a = new LearningEntry(id, "text", srcId, null, "domain",
            LearningStatus.Proposed, "{}", "user", createdAt, null);
        var b = new LearningEntry(id, "text", srcId, null, "domain",
            LearningStatus.Proposed, "{}", "user", createdAt, null);

        Assert.Equal(a, b);
    }

    [Fact]
    public void LearningEntry_RecordEquality_DifferentTextNotEqual()
    {
        var id        = Guid.NewGuid();
        var srcId     = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var a = new LearningEntry(id, "text A", srcId, null, "domain",
            LearningStatus.Proposed, "{}", "user", createdAt, null);
        var b = new LearningEntry(id, "text B", srcId, null, "domain",
            LearningStatus.Proposed, "{}", "user", createdAt, null);

        Assert.NotEqual(a, b);
    }
}
