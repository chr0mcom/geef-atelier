using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Infrastructure.Grounding;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

/// <summary>
/// Regression tests for the learning-retrieval provider registration gap (D-055 bugfix).
/// These tests would have caught the missing DI registration before it reached production.
/// </summary>
public sealed class LearningRetrievalProviderResolutionTests
{
    [Fact]
    public void GroundingProviderTypes_ContainsLearningRetrieval()
    {
        Assert.Equal("learning-retrieval", GroundingProviderTypes.LearningRetrieval);
    }

    [Fact]
    public void SystemCrew_GroundingProviderProfiles_ContainsLearningRetrieverDefault()
    {
        Assert.True(SystemCrew.GroundingProviderProfiles.ContainsKey("learning-retriever-default"),
            "learning-retriever-default must be registered in SystemCrew.GroundingProviderProfiles. " +
            "Missing entry causes the profile to be silently dropped from listings (bug D-055).");
    }

    [Fact]
    public void SystemCrew_LearningRetrieverDefaultProfile_HasCorrectProviderType()
    {
        Assert.Equal(GroundingProviderTypes.LearningRetrieval,
            SystemCrew.LearningRetrieverDefaultProfile.ProviderType);
    }

    [Fact]
    public void SystemCrew_LearningRetrieverDefaultProfile_IsSystem()
    {
        Assert.True(SystemCrew.LearningRetrieverDefaultProfile.IsSystem);
    }

    [Fact]
    public void SystemCrew_LearningRetrieverDefaultProfile_HasExpectedSettings()
    {
        var s = SystemCrew.LearningRetrieverDefaultProfile.ProviderSettings;
        Assert.Equal("1.0", s["sameDomainBoost"]);
        Assert.Equal("0.5", s["crossDomainPenalty"]);
        Assert.Equal("4",   s["maxLearnings"]);
    }

    [Fact]
    public void GroundingProviderFactory_IsRegistered_ReturnsTrueForLearningRetrieval()
    {
        // Arrange: create factory with a stub provider for the learning-retrieval type
        var stubProvider = new StubLearningProvider();
        var factory = new GroundingProviderFactory([stubProvider]);

        // Act + Assert
        Assert.True(factory.IsRegistered(GroundingProviderTypes.LearningRetrieval),
            "GroundingProviderFactory must resolve 'learning-retrieval'. " +
            "Missing DI registration causes InvalidOperationException at runtime (bug D-055).");
    }

    [Fact]
    public void GroundingProviderFactory_Create_ReturnsLearningProviderForCorrectType()
    {
        var stubProvider = new StubLearningProvider();
        var factory = new GroundingProviderFactory([stubProvider]);

        var resolved = factory.Create(GroundingProviderTypes.LearningRetrieval);

        Assert.Same(stubProvider, resolved);
    }

    [Fact]
    public void IsSystemGroundingProviderName_ReturnsTrueForLearningRetrieverDefault()
    {
        Assert.True(SystemCrew.IsSystemGroundingProviderName("learning-retriever-default"));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private sealed class StubLearningProvider : IGroundingProvider
    {
        public string ProviderType => GroundingProviderTypes.LearningRetrieval;

        public Task<GroundingResult> EnrichAsync(
            string briefingText, GroundingProviderProfile profile, Guid runId, CancellationToken ct)
            => Task.FromResult(new GroundingResult(
                ProviderName:        profile.Name,
                EnrichedContext:     string.Empty,
                Citations:           [],
                TokensOrCreditsUsed: 0,
                CostEur:             0m,
                ConsultationId:      null));
    }
}
