using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Tests.Domain.Crew.Models;

public sealed class SystemCrewModelMatrixTests
{
    [Fact]
    public void EveryLlmSystemProfileCarriesAnAllowedSlot()
    {
        foreach (var e in SystemCrew.ExecutorProfiles.Values)
            Assert.True(e.ModelSlot is { } s && s.IsAllowedFor(ModelActorKind.Executor), e.Name);
        foreach (var r in SystemCrew.ReviewerProfiles.Values.Where(r => r.Name != SystemCrew.CrewSpecValidatorProfile.Name))
            Assert.True(r.ModelSlot is { } s && s.IsAllowedFor(ModelActorKind.Reviewer), r.Name);
        foreach (var a in SystemCrew.AdvisorProfiles.Values)
            Assert.True(a.ModelSlot is { } s && s.IsAllowedFor(ModelActorKind.Advisor), a.Name);

        Assert.Null(SystemCrew.CrewSpecValidatorProfile.ModelSlot);
    }

    [Fact]
    public void SystemProfileBindingsAreTheMatrixPrimaries()
    {
        foreach (var r in SystemCrew.ReviewerProfiles.Values.Where(r => r.ModelSlot is not null))
            Assert.Equal(ModelMatrix.Primary(r.ModelSlot!.Value, r.ModelDomain ?? ModelDomain.General), new ModelCandidate(r.Provider, r.Model));
        foreach (var a in SystemCrew.AdvisorProfiles.Values)
            Assert.Equal(ModelMatrix.Primary(a.ModelSlot!.Value, a.ModelDomain ?? ModelDomain.General), new ModelCandidate(a.Provider, a.Model));
    }

    [Fact]
    public void SystemTemplatesDeclareTheirDomain()
    {
        Assert.Equal(ModelDomain.General, SystemCrew.KlassikTemplate.Domain);
        Assert.Equal(ModelDomain.Legal, SystemCrew.JuristischTemplate.Domain);
        Assert.Equal(ModelDomain.Academic, SystemCrew.AkademischTemplate.Domain);
        Assert.Equal(ModelDomain.Marketing, SystemCrew.MarketingTemplate.Domain);
        Assert.Equal(ModelDomain.General, SystemCrew.CrewComposerTemplate.Domain);
    }

    [Fact]
    public void NoSystemTemplateReviewerSharesTheExecutorsPrimaryModel()
    {
        foreach (var template in SystemCrew.CrewTemplates.Values)
        {
            var executor = SystemCrew.ExecutorProfiles[template.ExecutorProfileName];
            var domain = template.Domain ?? ModelDomain.General;
            var executorModel = ModelMatrix.Primary(executor.ModelSlot!.Value, domain).Model;

            foreach (var name in template.ReviewerProfileNames)
            {
                var reviewer = SystemCrew.ReviewerProfiles[name];
                if (reviewer.ModelSlot is not { } slot)
                    continue;
                var first = ModelMatrix.CandidatesFor(slot, reviewer.ModelDomain ?? domain)[0];
                Assert.False(CliRoute.IsSameModel(first.Model, executorModel), $"{template.Name}/{name}");
            }
        }
    }

    [Fact]
    public void SystemRefinersAndTransformsAreManagedByTheMatrix()
    {
        foreach (var g in SystemCrew.GroundingProviderProfiles.Values.Where(g => g.RefinementBinding is not null))
        {
            Assert.Equal(ModelSlot.GroundingRefiner, g.RefinementModelSlot);
            Assert.Equal("claude-cli", g.ProviderSettings[GroundingProviderProfile.KeyRefinementProvider]);
        }

        var transforms = SystemCrew.FinalizerProfiles.Values.Where(f => f.FinalizerType == FinalizerType.Transform).ToList();
        Assert.Equal(6, transforms.Count);
        foreach (var t in transforms)
        {
            var settings = TransformSettings.From(t.Settings);
            Assert.NotNull(settings.ModelSlot);
            Assert.Equal(ModelMatrix.Primary(settings.ModelSlot!.Value), new ModelCandidate(settings.Provider, settings.Model));
        }

        Assert.Equal(ModelSlot.TransformRewrite, TransformSettings.From(SystemCrew.AntiAiVoiceProfile.Settings).ModelSlot);
        Assert.Equal(ModelSlot.TransformCondense, TransformSettings.From(SystemCrew.GlossaryProfile.Settings).ModelSlot);
    }

    [Fact]
    public void DesignRulesTellTheComposerToChooseSlotsNotModels()
    {
        var rules = SystemCrew.CrewDesignRulesProfile.ProviderSettings[GroundingProviderProfile.KeyStaticContent];

        Assert.Contains("model_slot", rules);
        Assert.DoesNotContain("gpt-5", rules);
        Assert.DoesNotContain("grok", rules, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Recommended defaults", rules);
    }

}
