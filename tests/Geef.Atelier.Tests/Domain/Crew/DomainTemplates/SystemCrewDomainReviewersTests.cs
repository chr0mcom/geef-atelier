using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Domain.Crew.DomainTemplates;

public sealed class SystemCrewDomainReviewersTests
{
    // ── Legal reviewers ──────────────────────────────────────────────────────────

    [Fact]
    public void LegalJargonPrecision_HasCorrectName()
    {
        Assert.Equal("legal-jargon-precision", SystemCrew.LegalJargonPrecisionProfile.Name);
    }

    [Fact]
    public void LegalJargonPrecision_IsSystem_UsesCodexCli_Gpt55()
    {
        Assert.True(SystemCrew.LegalJargonPrecisionProfile.IsSystem);
        Assert.Equal("codex-cli", SystemCrew.LegalJargonPrecisionProfile.Provider);
        Assert.Equal("gpt-5.5", SystemCrew.LegalJargonPrecisionProfile.Model);
    }

    [Fact]
    public void LegalClauseRisk_HasCorrectName()
    {
        Assert.Equal("legal-clause-risk", SystemCrew.LegalClauseRiskProfile.Name);
    }

    [Fact]
    public void LegalClauseRisk_IsSystem_UsesCodexCli_Gpt55()
    {
        Assert.True(SystemCrew.LegalClauseRiskProfile.IsSystem);
        Assert.Equal("codex-cli", SystemCrew.LegalClauseRiskProfile.Provider);
        Assert.Equal("gpt-5.5", SystemCrew.LegalClauseRiskProfile.Model);
    }

    // ── Academic reviewers ───────────────────────────────────────────────────────

    [Fact]
    public void AcademicCitationReadiness_HasCorrectName()
    {
        Assert.Equal("academic-citation-readiness", SystemCrew.AcademicCitationReadinessProfile.Name);
    }

    [Fact]
    public void AcademicCitationReadiness_IsSystem_UsesCodexCli_Gpt55()
    {
        Assert.True(SystemCrew.AcademicCitationReadinessProfile.IsSystem);
        Assert.Equal("codex-cli", SystemCrew.AcademicCitationReadinessProfile.Provider);
        Assert.Equal("gpt-5.5", SystemCrew.AcademicCitationReadinessProfile.Model);
    }

    [Fact]
    public void AcademicArgumentationRigor_HasCorrectName()
    {
        Assert.Equal("academic-argumentation-rigor", SystemCrew.AcademicArgumentationRigorProfile.Name);
    }

    [Fact]
    public void AcademicArgumentationRigor_IsSystem_UsesClaudioCli_Opus47()
    {
        Assert.True(SystemCrew.AcademicArgumentationRigorProfile.IsSystem);
        Assert.Equal("claude-cli", SystemCrew.AcademicArgumentationRigorProfile.Provider);
        Assert.Equal("claude-opus-4-7", SystemCrew.AcademicArgumentationRigorProfile.Model);
    }

    // ── Marketing reviewers ──────────────────────────────────────────────────────

    [Fact]
    public void MarketingAudienceClarity_HasCorrectName()
    {
        Assert.Equal("marketing-audience-clarity", SystemCrew.MarketingAudienceClarityProfile.Name);
    }

    [Fact]
    public void MarketingAudienceClarity_IsSystem_UsesCodexCli_Gpt55()
    {
        Assert.True(SystemCrew.MarketingAudienceClarityProfile.IsSystem);
        Assert.Equal("codex-cli", SystemCrew.MarketingAudienceClarityProfile.Provider);
        Assert.Equal("gpt-5.5", SystemCrew.MarketingAudienceClarityProfile.Model);
    }

    [Fact]
    public void MarketingConversionStrength_HasCorrectName()
    {
        Assert.Equal("marketing-conversion-strength", SystemCrew.MarketingConversionStrengthProfile.Name);
    }

    [Fact]
    public void MarketingConversionStrength_IsSystem_UsesCodexCli_Gpt55()
    {
        Assert.True(SystemCrew.MarketingConversionStrengthProfile.IsSystem);
        Assert.Equal("codex-cli", SystemCrew.MarketingConversionStrengthProfile.Provider);
        Assert.Equal("gpt-5.5", SystemCrew.MarketingConversionStrengthProfile.Model);
    }

    // ── Dictionary completeness ──────────────────────────────────────────────────

    [Fact]
    public void ReviewerProfiles_ContainsAllSixDomainReviewers()
    {
        Assert.True(SystemCrew.ReviewerProfiles.ContainsKey("legal-jargon-precision"));
        Assert.True(SystemCrew.ReviewerProfiles.ContainsKey("legal-clause-risk"));
        Assert.True(SystemCrew.ReviewerProfiles.ContainsKey("academic-citation-readiness"));
        Assert.True(SystemCrew.ReviewerProfiles.ContainsKey("academic-argumentation-rigor"));
        Assert.True(SystemCrew.ReviewerProfiles.ContainsKey("marketing-audience-clarity"));
        Assert.True(SystemCrew.ReviewerProfiles.ContainsKey("marketing-conversion-strength"));
    }

    [Fact]
    public void ReviewerProfiles_HasEightTotalEntries()
    {
        // 2 original (briefing-fidelity, clarity) + 6 domain-specific
        Assert.Equal(8, SystemCrew.ReviewerProfiles.Count);
    }

    // ── System-prompt content smoke tests ────────────────────────────────────────

    [Fact]
    public void LegalJargonPrecision_SystemPrompt_ContainsBgbReference()
    {
        Assert.Contains("BGB", SystemCrew.LegalJargonPrecisionProfile.SystemPrompt);
    }

    [Fact]
    public void LegalClauseRisk_SystemPrompt_ContainsParagraph307()
    {
        Assert.Contains("§307", SystemCrew.LegalClauseRiskProfile.SystemPrompt);
    }

    [Fact]
    public void AcademicCitationReadiness_SystemPrompt_ContainsCommonKnowledge()
    {
        Assert.Contains("common knowledge", SystemCrew.AcademicCitationReadinessProfile.SystemPrompt,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcademicArgumentationRigor_SystemPrompt_ContainsFallacyCheck()
    {
        Assert.Contains("non sequitur", SystemCrew.AcademicArgumentationRigorProfile.SystemPrompt,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarketingConversionStrength_SystemPrompt_ContainsCtaCheck()
    {
        Assert.Contains("CTA", SystemCrew.MarketingConversionStrengthProfile.SystemPrompt);
    }
}
