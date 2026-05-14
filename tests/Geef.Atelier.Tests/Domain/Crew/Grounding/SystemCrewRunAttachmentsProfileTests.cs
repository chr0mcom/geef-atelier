using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Domain.Crew.Grounding;

public sealed class SystemCrewRunAttachmentsProfileTests
{
    [Fact]
    public void RunAttachmentsProfile_Name_IsRunAttachments()
    {
        Assert.Equal("run-attachments", SystemCrew.RunAttachmentsProfile.Name);
    }

    [Fact]
    public void RunAttachmentsProfile_ProviderType_IsVectorStore()
    {
        Assert.Equal("vector-store", SystemCrew.RunAttachmentsProfile.ProviderType);
    }

    [Fact]
    public void RunAttachmentsProfile_ProviderSettings_ScopeIsRunLocal()
    {
        Assert.Equal("run-local", SystemCrew.RunAttachmentsProfile.ProviderSettings["Scope"]);
    }

    [Fact]
    public void RunAttachmentsProfile_ProviderSettings_TopKIsFive()
    {
        Assert.Equal("5", SystemCrew.RunAttachmentsProfile.ProviderSettings["TopK"]);
    }

    [Fact]
    public void RunAttachmentsProfile_IsSystem_IsTrue()
    {
        Assert.True(SystemCrew.RunAttachmentsProfile.IsSystem);
    }

    [Fact]
    public void GroundingProviderProfiles_ContainsRunAttachments()
    {
        Assert.True(SystemCrew.GroundingProviderProfiles.ContainsKey("run-attachments"));
    }

    [Fact]
    public void GroundingProviderProfiles_RunAttachmentsEntry_MatchesProfile()
    {
        var profile = SystemCrew.GroundingProviderProfiles["run-attachments"];
        Assert.Equal(SystemCrew.RunAttachmentsProfile, profile);
    }

    [Fact]
    public void KnowledgeBaseDefaultProfile_ProviderSettings_ScopeIsGlobal()
    {
        Assert.Equal("global", SystemCrew.KnowledgeBaseDefaultProfile.ProviderSettings["Scope"]);
    }
}
