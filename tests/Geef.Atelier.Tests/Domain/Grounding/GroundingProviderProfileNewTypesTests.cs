using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Tests.Domain.Grounding;

public sealed class GroundingProviderProfileNewTypesTests
{
    private static GroundingProviderProfile MakeProfile(Dictionary<string, string>? settings = null) =>
        new(Name: "test",
            DisplayName: "T",
            Description: "",
            ProviderType: "static-context",
            ProviderSettings: settings ?? new(),
            MaxQueriesPerRun: 1,
            IsSystem: false);

    // ── StaticContent / StaticLabel ──────────────────────────────────────────

    [Fact]
    public void StaticContent_WhenSet_ReturnsValue()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyStaticContent] = "Brand voice text here."
        });

        Assert.Equal("Brand voice text here.", profile.StaticContent);
    }

    [Fact]
    public void StaticContent_WhenMissing_ReturnsNull()
    {
        var profile = MakeProfile();

        Assert.Null(profile.StaticContent);
    }

    [Fact]
    public void StaticContent_WhenWhitespace_ReturnsNull()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyStaticContent] = "   "
        });

        Assert.Null(profile.StaticContent);
    }

    [Fact]
    public void StaticLabel_WhenSet_ReturnsValue()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyStaticLabel] = "Markenstimme"
        });

        Assert.Equal("Markenstimme", profile.StaticLabel);
    }

    [Fact]
    public void StaticLabel_WhenMissing_ReturnsNull()
    {
        var profile = MakeProfile();

        Assert.Null(profile.StaticLabel);
    }

    // ── Urls ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Urls_WhenNewlineSeparated_ReturnsList()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyUrls] = "https://example.com\nhttps://docs.example.org"
        });

        Assert.Equal(2, profile.Urls.Count);
        Assert.Equal("https://example.com", profile.Urls[0]);
        Assert.Equal("https://docs.example.org", profile.Urls[1]);
    }

    [Fact]
    public void Urls_WhenMissing_ReturnsEmptyList()
    {
        var profile = MakeProfile();

        Assert.Empty(profile.Urls);
    }

    [Fact]
    public void Urls_WhenEmpty_ReturnsEmptyList()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyUrls] = ""
        });

        Assert.Empty(profile.Urls);
    }

    [Fact]
    public void Urls_StripsBlankLines()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyUrls] = "https://a.com\n\n\nhttps://b.com\n"
        });

        Assert.Equal(2, profile.Urls.Count);
    }

    // ── MaxContentPerUrl ─────────────────────────────────────────────────────

    [Fact]
    public void MaxContentPerUrl_WhenMissing_DefaultsTo8000()
    {
        var profile = MakeProfile();

        Assert.Equal(8000, profile.MaxContentPerUrl);
    }

    [Fact]
    public void MaxContentPerUrl_WhenSet_ReturnsCustomValue()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyMaxContentPerUrl] = "12000"
        });

        Assert.Equal(12000, profile.MaxContentPerUrl);
    }

    // ── StripBoilerplate ─────────────────────────────────────────────────────

    [Fact]
    public void StripBoilerplate_WhenMissing_DefaultsToTrue()
    {
        var profile = MakeProfile();

        Assert.True(profile.StripBoilerplate);
    }

    [Fact]
    public void StripBoilerplate_WhenExplicitlyFalse_ReturnsFalse()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyStripBoilerplate] = "false"
        });

        Assert.False(profile.StripBoilerplate);
    }

    [Fact]
    public void StripBoilerplate_WhenExplicitlyTrue_ReturnsTrue()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyStripBoilerplate] = "true"
        });

        Assert.True(profile.StripBoilerplate);
    }

    // ── RecencyDays ──────────────────────────────────────────────────────────

    [Fact]
    public void RecencyDays_WhenMissing_DefaultsTo7()
    {
        var profile = MakeProfile();

        Assert.Equal(7, profile.RecencyDays);
    }

    [Fact]
    public void RecencyDays_WhenSet_ReturnsCustomValue()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyRecencyDays] = "14"
        });

        Assert.Equal(14, profile.RecencyDays);
    }

    // ── NewsMaxResults ───────────────────────────────────────────────────────

    [Fact]
    public void NewsMaxResults_WhenMissing_DefaultsTo5()
    {
        var profile = MakeProfile();

        Assert.Equal(5, profile.NewsMaxResults);
    }

    [Fact]
    public void NewsMaxResults_WhenSet_ReturnsCustomValue()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyNewsMaxResults] = "10"
        });

        Assert.Equal(10, profile.NewsMaxResults);
    }

    // ── NewsSearchDepth ──────────────────────────────────────────────────────

    [Fact]
    public void NewsSearchDepth_WhenMissing_ReturnsNull()
    {
        var profile = MakeProfile();

        Assert.Null(profile.NewsSearchDepth);
    }

    [Fact]
    public void NewsSearchDepth_WhenSet_ReturnsValue()
    {
        var profile = MakeProfile(new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyNewsSearchDepth] = "advanced"
        });

        Assert.Equal("advanced", profile.NewsSearchDepth);
    }
}
