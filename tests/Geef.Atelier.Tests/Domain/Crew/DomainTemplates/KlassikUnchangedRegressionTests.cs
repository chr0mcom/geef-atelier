using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Domain.Crew.DomainTemplates;

/// <summary>
/// Regression guard: ensures the Klassik template definition has not been accidentally
/// modified when adding domain templates. Behaviour-stability invariant from PS-5.
/// </summary>
public sealed class KlassikUnchangedRegressionTests
{
    [Fact]
    public void KlassikTemplate_NameUnchanged()
    {
        Assert.Equal("klassik", SystemCrew.KlassikTemplate.Name);
    }

    [Fact]
    public void KlassikTemplate_UsesDefaultExecutor()
    {
        Assert.Equal("default-executor", SystemCrew.KlassikTemplate.ExecutorProfileName);
    }

    [Fact]
    public void KlassikTemplate_HasExactlyTwoReviewers_BriefingFidelityAndClarity()
    {
        var reviewers = SystemCrew.KlassikTemplate.ReviewerProfileNames;
        Assert.Equal(2, reviewers.Count);
        Assert.Contains("briefing-fidelity", reviewers);
        Assert.Contains("clarity", reviewers);
    }

    [Fact]
    public void KlassikTemplate_UsesParallelStrategy()
    {
        Assert.Equal(EvaluationStrategy.Parallel, SystemCrew.KlassikTemplate.EvaluationStrategy);
    }

    [Fact]
    public void KlassikTemplate_HasNoAdvisors()
    {
        Assert.Empty(SystemCrew.KlassikTemplate.AdvisorProfileNames);
    }

    [Fact]
    public void KlassikTemplate_HasDefaultGroundingProviders()
    {
        Assert.Equal(2, SystemCrew.KlassikTemplate.GroundingProviderNames.Count);
        Assert.Contains("tavily-refined", SystemCrew.KlassikTemplate.GroundingProviderNames);
        Assert.Contains("run-attachments", SystemCrew.KlassikTemplate.GroundingProviderNames);
        Assert.DoesNotContain("learning-retriever-default", SystemCrew.KlassikTemplate.GroundingProviderNames);
    }

    [Fact]
    public void KlassikTemplate_IsSystem()
    {
        Assert.True(SystemCrew.KlassikTemplate.IsSystem);
    }

    [Fact]
    public void BriefingFidelityProfile_IsFactReviewer_OnCodexAstra()
    {
        Assert.Equal("codex-cli", SystemCrew.BriefingFidelityProfile.Provider);
        Assert.Equal("gpt-astra-latest", SystemCrew.BriefingFidelityProfile.Model);
        Assert.Equal(Geef.Atelier.Core.Domain.Crew.Models.ModelSlot.FactReviewer, SystemCrew.BriefingFidelityProfile.ModelSlot);
    }

    [Fact]
    public void ClarityProfile_IsStyleReviewer_OnCodexSol()
    {
        Assert.Equal("codex-cli", SystemCrew.ClarityProfile.Provider);
        Assert.Equal("gpt-sol-latest", SystemCrew.ClarityProfile.Model);
        Assert.Equal(Geef.Atelier.Core.Domain.Crew.Models.ModelSlot.StyleReviewer, SystemCrew.ClarityProfile.ModelSlot);
    }

    [Fact]
    public void DefaultExecutorProfile_UsesClaudeCli_OpusAlias()
    {
        Assert.Equal("claude-cli", SystemCrew.DefaultExecutorProfile.Provider);
        Assert.Equal("claude-opus-latest", SystemCrew.DefaultExecutorProfile.Model);
    }

    [Fact]
    public void KlassikTemplate_DoesNotContainDomainReviewers()
    {
        var reviewers = SystemCrew.KlassikTemplate.ReviewerProfileNames;
        Assert.DoesNotContain("legal-jargon-precision", reviewers);
        Assert.DoesNotContain("legal-clause-risk", reviewers);
        Assert.DoesNotContain("academic-citation-readiness", reviewers);
        Assert.DoesNotContain("academic-argumentation-rigor", reviewers);
        Assert.DoesNotContain("marketing-audience-clarity", reviewers);
        Assert.DoesNotContain("marketing-conversion-strength", reviewers);
    }
}
