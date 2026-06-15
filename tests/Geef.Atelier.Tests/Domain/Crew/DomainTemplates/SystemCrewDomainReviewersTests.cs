using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Specialization;

namespace Geef.Atelier.Tests.Domain.Crew.DomainTemplates;

/// <summary>
/// The former domain-specialized system reviewers (legal/academic/marketing) were replaced by two
/// generic reviewer roles plus reusable system specialization packs. These tests assert the new model:
/// generic role prompts carry the shared severity taxonomy and a {specialization} slot; the domain
/// deltas live in the packs.
/// </summary>
public sealed class SystemCrewDomainReviewersTests
{
    // ── Generic reviewer profiles ────────────────────────────────────────────────

    [Fact]
    public void DomainTerminologyReviewer_HasCorrectIdentity()
    {
        var p = SystemCrew.DomainTerminologyReviewerProfile;
        Assert.Equal("domain-terminology-reviewer", p.Name);
        Assert.True(p.IsSystem);
        Assert.Contains(PromptComposition.SpecializationSlot, p.SystemPrompt);
    }

    [Fact]
    public void SubstantiveRigorReviewer_HasCorrectIdentity()
    {
        var p = SystemCrew.SubstantiveRigorReviewerProfile;
        Assert.Equal("substantive-rigor-reviewer", p.Name);
        Assert.True(p.IsSystem);
        Assert.Contains(PromptComposition.SpecializationSlot, p.SystemPrompt);
    }

    [Fact]
    public void GenericReviewers_RolePrompts_AreTaskAgnostic()
    {
        // The role prompt must not carry domain specifics — those belong in packs.
        Assert.DoesNotContain("BGB", SystemCrew.DomainTerminologyReviewerProfile.SystemPrompt);
        Assert.DoesNotContain("§307", SystemCrew.SubstantiveRigorReviewerProfile.SystemPrompt);
    }

    [Fact]
    public void ReviewerProfiles_ContainGenericReviewers()
    {
        Assert.True(SystemCrew.ReviewerProfiles.ContainsKey("domain-terminology-reviewer"));
        Assert.True(SystemCrew.ReviewerProfiles.ContainsKey("substantive-rigor-reviewer"));
    }

    [Fact]
    public void ReviewerProfiles_NoLongerContainSpecializedReviewers()
    {
        Assert.False(SystemCrew.ReviewerProfiles.ContainsKey("legal-jargon-precision"));
        Assert.False(SystemCrew.ReviewerProfiles.ContainsKey("marketing-conversion-strength"));
    }

    // ── System packs carry the domain deltas ─────────────────────────────────────

    [Fact]
    public void SystemPacks_ContainAllSixDomainPacks()
    {
        foreach (var name in new[]
        {
            "legal-terminology", "legal-clause-risk",
            "academic-citation", "academic-argumentation",
            "marketing-voice", "marketing-conversion"
        })
        {
            Assert.True(SystemPacks.ByName.ContainsKey(name), $"missing pack {name}");
        }
    }

    [Fact]
    public void LegalTerminologyPack_IsDomainScopedReviewerPack_WithBgbContent()
    {
        var p = SystemPacks.LegalTerminology;
        Assert.Equal(PackScope.DomainScoped, p.Scope);
        Assert.Equal("legal", p.Domain);
        Assert.Contains(PackActorType.Reviewer, p.ApplicableActorTypes);
        Assert.Contains("BGB", p.SpecializationText);
    }

    [Fact]
    public void LegalClauseRiskPack_ContainsParagraph307()
    {
        Assert.Contains("§307", SystemPacks.LegalClauseRisk.SpecializationText);
    }

    [Fact]
    public void AcademicCitationPack_ContainsCommonKnowledge()
    {
        Assert.Contains("common-knowledge", SystemPacks.AcademicCitation.SpecializationText,
            System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AcademicArgumentationPack_ContainsFallacyCheck()
    {
        Assert.Contains("non sequitur", SystemPacks.AcademicArgumentation.SpecializationText,
            System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarketingConversionPack_ContainsCtaCheck()
    {
        Assert.Contains("CTA", SystemPacks.MarketingConversion.SpecializationText);
    }

    [Fact]
    public void GeneralPacks_AreReusableAnywhere()
    {
        Assert.Equal(PackScope.General, SystemPacks.ConciseOutput.Scope);
        Assert.Null(SystemPacks.ConciseOutput.Domain);
        Assert.Contains(PackActorType.Executor, SystemPacks.ConciseOutput.ApplicableActorTypes);
    }
}
