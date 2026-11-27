using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Tests.Domain.Crew.Models;

public sealed class SystemCrewModelMatrixTests
{
    [Fact]
    public void EveryLlmCodeOnlyProfileWithASlotCarriesASlotOfItsRole()
    {
        foreach (var e in SystemCrew.CodeOnlyExecutorProfiles.Values)
            Assert.Equal(ModelSlot.Executor, e.ModelSlot);
        foreach (var r in SystemCrew.CodeOnlyReviewerProfiles.Values.Where(r => r.ModelSlot is not null))
            Assert.True(r.ModelSlot!.Value.IsReviewerRole(), r.Name);
    }

    [Fact]
    public void CodeOnlyProfileBindingsAreTheMatrixPrimaries()
    {
        foreach (var e in SystemCrew.CodeOnlyExecutorProfiles.Values)
            Assert.Equal(ModelMatrix.Primary(e.ModelSlot!.Value, e.ModelDomain ?? ModelDomain.General), new ModelCandidate(e.Provider, e.Model));
        foreach (var r in SystemCrew.CodeOnlyReviewerProfiles.Values.Where(r => r.ModelSlot is not null))
            Assert.Equal(ModelMatrix.Primary(r.ModelSlot!.Value, r.ModelDomain ?? ModelDomain.General), new ModelCandidate(r.Provider, r.Model));
    }

    [Fact]
    public void CodeOnlyTemplatesDeclareTheGeneralDomain()
    {
        Assert.All(SystemCrew.CodeOnlyTemplates.Values, t => Assert.Equal(ModelDomain.General, t.Domain));
    }

    [Fact]
    public void NoCodeOnlyTemplateCheckSharesTheWritersPrimaryModel()
    {
        foreach (var template in SystemCrew.CodeOnlyTemplates.Values)
        {
            var executor = SystemCrew.CodeOnlyExecutorProfiles[template.ExecutorProfileName];
            var domain = template.Domain ?? ModelDomain.General;
            var executorModel = ModelMatrix.Primary(executor.ModelSlot!.Value, domain).Model;

            foreach (var name in template.ReviewerProfileNames)
            {
                var reviewer = SystemCrew.CodeOnlyReviewerProfiles[name];
                var model = reviewer.ModelSlot is { } slot ? ModelMatrix.CandidatesFor(slot, reviewer.ModelDomain ?? domain)[0].Model : reviewer.Model;
                if (model.Length == 0)
                    continue;
                Assert.False(CliRoute.IsSameModel(model, executorModel), $"{template.Name}/{name}");
            }
        }
    }
}
