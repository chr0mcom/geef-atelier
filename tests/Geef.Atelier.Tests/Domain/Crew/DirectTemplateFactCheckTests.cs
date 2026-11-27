using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Tests.Domain.Crew;

/// <summary>The template of the direct path has the checks without a model and the fact check; both exist only in code.</summary>
public sealed class DirectTemplateFactCheckTests
{
    [Fact]
    public void The_direct_template_has_both_reviewers_in_order()
    {
        Assert.Equal([SystemCrew.DeterministicChecksProfile.Name, SystemCrew.SourceFactCheckProfile.Name], SystemCrew.DirectTemplate.ReviewerProfileNames);
        Assert.Equal(2, SystemCrew.DirectTemplate.ConvergenceOverride!.MaxIterations);
    }

    [Fact]
    public void The_fact_check_is_a_code_only_reviewer_of_the_fact_slot()
    {
        var profile = SystemCrew.CodeOnlyReviewerProfiles[SystemCrew.SourceFactCheckProfile.Name];

        Assert.Same(SystemCrew.SourceFactCheckProfile, profile);
        Assert.Equal("source-fact-check", profile.Name);
        Assert.Equal("Fact check", profile.DisplayName);
        Assert.Equal(ModelSlot.FactReviewer, profile.ModelSlot);
        Assert.Equal(ModelMatrix.Primary(ModelSlot.FactReviewer).Model, profile.Model);
        Assert.True(profile.IsSystem);
    }

    [Fact]
    public void The_fact_check_model_is_of_another_family_than_the_writer()
    {
        var writer = SystemCrew.DirectWriterProfile;
        var check = SystemCrew.SourceFactCheckProfile;

        Assert.NotEqual(CliRoute.FamilyOf(writer.Provider, writer.Model), CliRoute.FamilyOf(check.Provider, check.Model));
    }
}
