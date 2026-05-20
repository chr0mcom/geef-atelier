using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Tests.Domain.Grounding;

public sealed class GroundingProviderProfileRefinementTests
{
    private static GroundingProviderProfile MakeProfile(Dictionary<string, string>? settings = null) =>
        new(Name: "test",
            DisplayName: "T",
            Description: "",
            ProviderType: "tavily",
            ProviderSettings: settings ?? new(),
            MaxQueriesPerRun: 1,
            IsSystem: false);

    [Fact]
    public void RefinementBinding_WhenProviderAndModelSet_ReturnsBinding()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyRefinementProvider] = "openrouter",
            [GroundingProviderProfile.KeyRefinementModel] = "gpt-4o",
        });

        var binding = profile.RefinementBinding;

        Assert.NotNull(binding);
        Assert.Equal("openrouter", binding.Provider);
        Assert.Equal("gpt-4o", binding.Model);
        Assert.Equal(2048, binding.MaxTokens); // default
    }

    [Fact]
    public void RefinementBinding_WhenProviderMissing_ReturnsNull()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyRefinementModel] = "gpt-4o",
        });

        Assert.Null(profile.RefinementBinding);
    }

    [Fact]
    public void RefinementBinding_WhenModelMissing_ReturnsNull()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyRefinementProvider] = "openrouter",
        });

        Assert.Null(profile.RefinementBinding);
    }

    [Fact]
    public void RefinementBinding_WithTemperature_ParsesCorrectly()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyRefinementProvider] = "openrouter",
            [GroundingProviderProfile.KeyRefinementModel] = "gpt-4o",
            [GroundingProviderProfile.KeyRefinementMaxTokens] = "1024",
            [GroundingProviderProfile.KeyRefinementTemperature] = "0.7",
        });

        var binding = profile.RefinementBinding;

        Assert.NotNull(binding);
        Assert.Equal(1024, binding.MaxTokens);
        Assert.Equal(0.7, binding.Temperature);
    }

    [Fact]
    public void RefinementMode_WhenNotSet_DefaultsToFilter()
    {
        var profile = MakeProfile();

        Assert.Equal(GroundingRefinementMode.Filter, profile.RefinementMode);
    }

    [Fact]
    public void RefinementMode_WhenSetToSynthesize_ReturnsSynthesize()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyRefinementMode] = "1",
        });

        Assert.Equal(GroundingRefinementMode.Synthesize, profile.RefinementMode);
    }

    [Fact]
    public void RefinementInstructions_WhenEmpty_ReturnsNull()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyRefinementInstructions] = "   ",
        });

        Assert.Null(profile.RefinementInstructions);
    }

    [Fact]
    public void RefinementInstructions_WhenSet_ReturnsValue()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyRefinementInstructions] = "Only keep sources from 2025.",
        });

        Assert.Equal("Only keep sources from 2025.", profile.RefinementInstructions);
    }

    [Fact]
    public void BackwardsCompat_OldProfileWithoutRefinementKeys_RefinementBindingIsNull()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            ["Tier"] = "basic",
            ["MaxResults"] = "5",
            ["IncludeAnswer"] = "true",
        });

        Assert.Null(profile.RefinementBinding);
        Assert.Equal(GroundingRefinementMode.Filter, profile.RefinementMode);
        Assert.Null(profile.RefinementInstructions);
    }
}
