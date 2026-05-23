using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Infrastructure.Finalizers;

namespace Geef.Atelier.Tests.Infrastructure.Finalizers;

/// <summary>
/// Regression tests for the missing DI registration of LearningExtract and LearningPublish
/// finalizer executors (D-055 follow-up: same pattern as the grounding-provider registration gap).
/// These tests would have caught the missing registrations before they reached production.
/// </summary>
public sealed class LearningFinalizerExecutorRegistrationTests
{
    [Fact]
    public void SystemCrew_FinalizerProfiles_ContainsLearningExtractor()
    {
        Assert.True(SystemCrew.FinalizerProfiles.ContainsKey("learning-extractor"),
            "learning-extractor must be in SystemCrew.FinalizerProfiles.");
    }

    [Fact]
    public void SystemCrew_FinalizerProfiles_ContainsLearningPublisher()
    {
        Assert.True(SystemCrew.FinalizerProfiles.ContainsKey("learning-publisher"),
            "learning-publisher must be in SystemCrew.FinalizerProfiles.");
    }

    [Fact]
    public void SystemCrew_LearningExtractorProfile_HasCorrectType()
    {
        Assert.Equal(FinalizerType.LearningExtract, SystemCrew.LearningExtractorProfile.FinalizerType);
    }

    [Fact]
    public void SystemCrew_LearningPublisherProfile_HasCorrectType()
    {
        Assert.Equal(FinalizerType.LearningPublish, SystemCrew.LearningPublisherProfile.FinalizerType);
    }

    [Fact]
    public void FinalizerExecutorFactory_GetExecutor_ReturnsLearningExtract()
    {
        var factory = new FinalizerExecutorFactory([
            new StubExecutor(FinalizerType.LearningExtract),
            new StubExecutor(FinalizerType.LearningPublish),
        ]);

        var executor = factory.GetExecutor(FinalizerType.LearningExtract);

        Assert.Equal(FinalizerType.LearningExtract, executor.Type);
    }

    [Fact]
    public void FinalizerExecutorFactory_GetExecutor_ReturnsLearningPublish()
    {
        var factory = new FinalizerExecutorFactory([
            new StubExecutor(FinalizerType.LearningExtract),
            new StubExecutor(FinalizerType.LearningPublish),
        ]);

        var executor = factory.GetExecutor(FinalizerType.LearningPublish);

        Assert.Equal(FinalizerType.LearningPublish, executor.Type);
    }

    [Fact]
    public void FinalizerExecutorFactory_WithoutLearningExtract_ThrowsInvalidOperationException()
    {
        var factory = new FinalizerExecutorFactory([]);

        Assert.Throws<InvalidOperationException>(
            () => factory.GetExecutor(FinalizerType.LearningExtract));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class StubExecutor(FinalizerType type) : IFinalizerExecutor
    {
        public FinalizerType Type => type;

        public Task<FinalizerExecutionResult> ExecuteAsync(
            FinalizerProfile profile, FinalizerExecutionContext context, CancellationToken ct)
            => Task.FromResult(new FinalizerExecutionResult(
                UpdatedText: null,
                Artifact: null,
                CostEur: null,
                ActorName: profile.Name));
    }
}
