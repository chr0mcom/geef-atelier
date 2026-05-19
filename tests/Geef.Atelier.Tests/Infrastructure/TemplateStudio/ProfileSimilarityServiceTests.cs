using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using Geef.Atelier.Infrastructure.TemplateStudio;

namespace Geef.Atelier.Tests.Infrastructure.TemplateStudio;

public sealed class ProfileSimilarityServiceTests
{
    // -------------------------------------------------------------------------
    // Test doubles
    // -------------------------------------------------------------------------

    private sealed class StubCrewService(
        IReadOnlyList<ReviewerProfile>? reviewers = null,
        IReadOnlyList<AdvisorProfile>? advisors = null,
        IReadOnlyList<GroundingProviderProfile>? grounding = null) : ICrewService
    {
        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult(reviewers ?? (IReadOnlyList<ReviewerProfile>)[]);

        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult(advisors ?? (IReadOnlyList<AdvisorProfile>)[]);

        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult(grounding ?? (IReadOnlyList<GroundingProviderProfile>)[]);

        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<ExecutorProfile>)[]);

        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult<ReviewerProfile?>(null);
        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult<ExecutorProfile?>(null);
        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult<AdvisorProfile?>(null);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default)
            => Task.FromResult<GroundingProviderProfile?>(null);

        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyList<CrewTemplate>)[]);
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default)
            => Task.FromResult<CrewTemplate?>(null);
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> RenameCustomReviewerProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomExecutorProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomAdvisorProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomGroundingProviderProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FinalizerProfile>>([]);
        public Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<FinalizerProfile?>(null);
        public Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile profile, CancellationToken ct = default) => Task.FromResult(profile);
        public Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomFinalizerProfileAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);
        public Task<string> RenameCustomCrewTemplateAsync(string oldName, string newName, CancellationToken ct = default) => Task.FromResult(newName);

        public Task<CrewSnapshot> ResolveSnapshotAsync(string? crewTemplateName, CrewSpec? customCrew, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns the same fixed vector for every text — the caller specifies which vector per call index.
    /// </summary>
    private sealed class SequencedEmbeddingProvider(params float[][] vectors) : IEmbeddingProvider
    {
        private int _callCount;

        public string ProviderName => "test";
        public string ModelName => "test-model";
        public int Dimensions => vectors.Length > 0 ? vectors[0].Length : 3;

        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct)
        {
            var idx = Math.Min(Interlocked.Increment(ref _callCount) - 1, vectors.Length - 1);
            return Task.FromResult(new EmbeddingResult(vectors[idx], 1, null));
        }

        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
            => throw new NotSupportedException();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ProposedProfile MakeProposedReviewer(string name = "custom-reviewer") => new(
        ProfileType: ProposedProfileType.Reviewer,
        Name: name,
        DisplayName: "Custom Reviewer",
        Description: "A reviewer that checks content quality.",
        Model: "gpt-4o-mini",
        Provider: "openrouter",
        SystemPrompt: "Review the content.",
        MaxTokens: null,
        ReviewerFocus: null,
        AdvisorMode: null,
        AdvisorTrigger: null,
        GroundingProviderType: null,
        GroundingProviderSettings: null);

    private static ProposedProfile MakeProposedAdvisor(string name = "custom-advisor") => new(
        ProfileType: ProposedProfileType.Advisor,
        Name: name,
        DisplayName: "Custom Advisor",
        Description: "Strategic advisor.",
        Model: "gemini-2.5-flash",
        Provider: "openrouter",
        SystemPrompt: "Advise strategically.",
        MaxTokens: null,
        ReviewerFocus: null,
        AdvisorMode: "Strategic",
        AdvisorTrigger: "BeforeFirstExecution",
        GroundingProviderType: null,
        GroundingProviderSettings: null);

    private static ReviewerProfile MakeReviewerProfile(string name, string description) => new(
        Name: name,
        DisplayName: name,
        Description: description,
        SystemPrompt: "System prompt.",
        Provider: "openrouter",
        Model: "gpt-4o-mini",
        MaxTokens: null,
        IsSystem: false);

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FindSimilarAsync_WhenNoExistingProfiles_ReturnsFalse()
    {
        // No existing reviewers
        var crewService = new StubCrewService(reviewers: []);
        // Embedding won't even be called, but provide a vector anyway
        var embeddingProvider = new SequencedEmbeddingProvider([1f, 0f, 0f]);
        var svc = new ProfileSimilarityService(crewService, embeddingProvider);

        var proposed = MakeProposedReviewer();
        var (isDuplicate, existingName) = await svc.FindSimilarAsync(proposed, 0.85, CancellationToken.None);

        Assert.False(isDuplicate);
        Assert.Null(existingName);
    }

    [Fact]
    public async Task FindSimilarAsync_WhenExistingProfileBelowThreshold_ReturnsFalse()
    {
        // One existing reviewer with orthogonal embedding (cosine similarity = 0.0)
        var existing = MakeReviewerProfile("clarity", "Checks text clarity.");
        var crewService = new StubCrewService(reviewers: [existing]);

        // proposed vector = (1,0,0), candidate vector = (0,1,0) → cosine = 0
        var embeddingProvider = new SequencedEmbeddingProvider(
            [1f, 0f, 0f],  // proposed
            [0f, 1f, 0f]); // candidate

        var svc = new ProfileSimilarityService(crewService, embeddingProvider);
        var proposed = MakeProposedReviewer();
        var (isDuplicate, existingName) = await svc.FindSimilarAsync(proposed, 0.85, CancellationToken.None);

        Assert.False(isDuplicate);
        Assert.Null(existingName);
    }

    [Fact]
    public async Task FindSimilarAsync_WhenExistingProfileAboveThreshold_ReturnsDuplicate()
    {
        // One existing reviewer with identical embedding (cosine similarity = 1.0)
        var existing = MakeReviewerProfile("briefing-fidelity", "Verifies briefing requirements.");
        var crewService = new StubCrewService(reviewers: [existing]);

        // Both vectors identical → cosine = 1.0, well above threshold 0.85
        var embeddingProvider = new SequencedEmbeddingProvider(
            [1f, 0f, 0f],  // proposed
            [1f, 0f, 0f]); // candidate (same)

        var svc = new ProfileSimilarityService(crewService, embeddingProvider);
        var proposed = MakeProposedReviewer();
        var (isDuplicate, existingName) = await svc.FindSimilarAsync(proposed, 0.85, CancellationToken.None);

        Assert.True(isDuplicate);
        Assert.Equal("briefing-fidelity", existingName);
    }

    [Fact]
    public async Task FindSimilarAsync_FiltersByProfileType_DoesNotCompareAcrossTypes()
    {
        // Proposed is a Reviewer type, but existing profiles are Advisors only
        var advisorProfile = new AdvisorProfile(
            Name: "strategic-advisor",
            DisplayName: "Strategic Advisor",
            Description: "Provides strategic advice for text creation.",
            SystemPrompt: "Advise.",
            Provider: "openrouter",
            Model: "gemini-2.5-flash",
            MaxTokens: null,
            Mode: AdvisorMode.Strategic,
            Trigger: AdvisorTrigger.BeforeFirstExecution,
            IsSystem: false);

        // Only advisors in the service; reviewers list is empty
        var crewService = new StubCrewService(
            reviewers: [],
            advisors: [advisorProfile]);

        // Even if vectors would match, the type filter means no comparison happens
        var embeddingProvider = new SequencedEmbeddingProvider(
            [1f, 0f, 0f],
            [1f, 0f, 0f]);

        var svc = new ProfileSimilarityService(crewService, embeddingProvider);

        // Proposed is a Reviewer — should not match the Advisor profile
        var proposed = MakeProposedReviewer();
        var (isDuplicate, _) = await svc.FindSimilarAsync(proposed, 0.85, CancellationToken.None);

        Assert.False(isDuplicate);
    }
}
