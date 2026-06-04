using Geef.Atelier.Application.Composition;
using Geef.Atelier.Infrastructure.Composition;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Sdk.Context;
using Geef.Sdk.Results;

namespace Geef.Atelier.Tests.Composition;

/// <summary>
/// Unit tests for <see cref="CrewSpecValidatorReviewer"/>: verifies the deterministic
/// (non-LLM) reviewer that gates the Auto-Crew composition loop.
/// </summary>
public sealed class CrewSpecValidatorReviewerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static CrewSpecValidatorReviewer MakeReviewer(ICrewSpecValidator validator) =>
        new(validator);

    private static IRunContext ContextWithDraft(string draft) =>
        new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "Compose a crew for writing an essay.")
            .Set(AtelierContextKeys.CurrentDraft,  draft);

    private static IRunContext EmptyContext() =>
        new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "Compose a crew.");
    // Note: no CurrentDraft key set

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReviewAsync_ReturnsApproved_WhenSpecIsValid()
    {
        // Arrange: validator returns no issues
        var validator = new StubValidator([]);
        var reviewer  = MakeReviewer(validator);

        var context = ContextWithDraft("""{"mode":"existing-template","existing_template_name":"klassik"}""");

        // Act
        var result = await reviewer.ReviewAsync(context, default);

        // Assert
        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task ReviewAsync_ReturnsRejected_WhenValidatorFindsIssues()
    {
        // Arrange: validator returns one critical issue
        var issues = new[]
        {
            new CrewSpecValidationIssue(
                Field:      "reviewers",
                Message:    "At least one reviewer is required.",
                IsCritical: true),
        };
        var validator = new StubValidator(issues);
        var reviewer  = MakeReviewer(validator);

        var context = ContextWithDraft("""{"mode":"composed","executor":{"reuse":"x"},"reviewers":[],"finalizers":[{"reuse":"y"}]}""");

        // Act
        var result = await reviewer.ReviewAsync(context, default);

        // Assert: rejected with a Critical finding
        Assert.Equal(ReviewDecision.Rejected, result.Decision);
        Assert.NotEmpty(result.Findings);
        Assert.Contains(result.Findings, f => f.Severity == FindingSeverity.Critical);
    }

    [Fact]
    public async Task ReviewAsync_ReturnsApproved_WhenNoArtifactYet()
    {
        // Arrange: no CurrentDraft in the context (first iteration before any execution)
        var validator = new CapturingValidator();
        var reviewer  = MakeReviewer(validator);

        var context = EmptyContext();

        // Act
        var result = await reviewer.ReviewAsync(context, default);

        // Assert: approved without calling the validator
        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.False(validator.ValidateCalled, "Validator must NOT be called when no draft exists.");
    }

    [Fact]
    public async Task ReviewAsync_ReturnsApproved_WhenDraftIsWhitespaceOnly()
    {
        // Arrange: draft is whitespace — treated as "no draft yet"
        var validator = new CapturingValidator();
        var reviewer  = MakeReviewer(validator);

        var context = ContextWithDraft("   ");

        // Act
        var result = await reviewer.ReviewAsync(context, default);

        // Assert: approved without calling the validator
        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.False(validator.ValidateCalled, "Validator must NOT be called for whitespace-only drafts.");
    }

    [Fact]
    public async Task ReviewAsync_MapsNonCriticalIssuesToError()
    {
        // Arrange: a non-critical issue should map to FindingSeverity.Error (not Critical)
        var issues = new[]
        {
            new CrewSpecValidationIssue(
                Field:      "executor.model",
                Message:    "Model 'gpt-99' is not available.",
                IsCritical: false),
        };
        var validator = new StubValidator(issues);
        var reviewer  = MakeReviewer(validator);

        var context = ContextWithDraft("""{"mode":"composed","executor":{"provider":"openai","model":"gpt-99","system_prompt":"You are x.","name":"x"},"reviewers":[{"reuse":"y"}],"finalizers":[{"reuse":"z"}]}""");

        // Act
        var result = await reviewer.ReviewAsync(context, default);

        // Assert: rejected with Error (not Critical) severity
        Assert.Equal(ReviewDecision.Rejected, result.Decision);
        Assert.True(result.Findings.All(f => f.Severity == FindingSeverity.Error));
    }

    // ── Fakes ─────────────────────────────────────────────────────────────

    /// <summary>Always returns the same pre-configured list of issues.</summary>
    private sealed class StubValidator(IReadOnlyList<CrewSpecValidationIssue> issues) : ICrewSpecValidator
    {
        public Task<IReadOnlyList<CrewSpecValidationIssue>> ValidateAsync(string specJson, CancellationToken ct = default)
            => Task.FromResult(issues);
    }

    /// <summary>Tracks whether <see cref="ValidateAsync"/> was called at all.</summary>
    private sealed class CapturingValidator : ICrewSpecValidator
    {
        public bool ValidateCalled { get; private set; }

        public Task<IReadOnlyList<CrewSpecValidationIssue>> ValidateAsync(string specJson, CancellationToken ct = default)
        {
            ValidateCalled = true;
            return Task.FromResult<IReadOnlyList<CrewSpecValidationIssue>>([]);
        }
    }
}
