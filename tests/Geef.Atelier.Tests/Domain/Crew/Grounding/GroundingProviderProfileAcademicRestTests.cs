using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Tests.Domain.Crew.Grounding;

/// <summary>
/// Tests for the academic-search and rest-api settings properties added to
/// <see cref="GroundingProviderProfile"/>.
/// </summary>
public sealed class GroundingProviderProfileAcademicRestTests
{
    // ── academic-search defaults ────────────────────────────────────────────

    [Fact]
    public void AcademicSource_DefaultsToSemanticScholar_WhenKeyAbsent()
    {
        var profile = MakeProfile("academic-search", []);
        Assert.Equal("semantic-scholar", profile.AcademicSource);
    }

    [Fact]
    public void AcademicSource_ReturnsConfiguredValue()
    {
        var profile = MakeProfile("academic-search",
            new() { [GroundingProviderProfile.KeyAcademicSource] = "arxiv" });
        Assert.Equal("arxiv", profile.AcademicSource);
    }

    [Fact]
    public void AcademicMaxPapers_DefaultsFive_WhenKeyAbsent()
    {
        var profile = MakeProfile("academic-search", []);
        Assert.Equal(5, profile.AcademicMaxPapers);
    }

    [Fact]
    public void AcademicMaxPapers_ReturnsConfiguredValue()
    {
        var profile = MakeProfile("academic-search",
            new() { [GroundingProviderProfile.KeyAcademicMaxPapers] = "3" });
        Assert.Equal(3, profile.AcademicMaxPapers);
    }

    [Fact]
    public void AcademicDateFrom_ReturnsNull_WhenKeyAbsent()
    {
        var profile = MakeProfile("academic-search", []);
        Assert.Null(profile.AcademicDateFrom);
    }

    [Fact]
    public void AcademicDateFrom_ReturnsConfiguredValue()
    {
        var profile = MakeProfile("academic-search",
            new() { [GroundingProviderProfile.KeyAcademicDateFrom] = "2023" });
        Assert.Equal("2023", profile.AcademicDateFrom);
    }

    [Fact]
    public void AcademicFields_ReturnsNull_WhenKeyAbsent()
    {
        var profile = MakeProfile("academic-search", []);
        Assert.Null(profile.AcademicFields);
    }

    [Fact]
    public void AcademicApiKeyEnv_ReturnsNull_WhenKeyAbsent()
    {
        var profile = MakeProfile("academic-search", []);
        Assert.Null(profile.AcademicApiKeyEnv);
    }

    [Fact]
    public void AcademicApiKeyEnv_ReturnsConfiguredValue()
    {
        var profile = MakeProfile("academic-search",
            new() { [GroundingProviderProfile.KeyAcademicApiKeyEnv] = "SS_API_KEY" });
        Assert.Equal("SS_API_KEY", profile.AcademicApiKeyEnv);
    }

    // ── rest-api defaults ─────────────────────────────────────────────────────

    [Fact]
    public void RestApiUrl_ReturnsNull_WhenKeyAbsent()
    {
        var profile = MakeProfile("rest-api", []);
        Assert.Null(profile.RestApiUrl);
    }

    [Fact]
    public void RestApiUrl_ReturnsConfiguredValue()
    {
        var profile = MakeProfile("rest-api",
            new() { [GroundingProviderProfile.KeyRestApiUrl] = "https://api.example.com/items" });
        Assert.Equal("https://api.example.com/items", profile.RestApiUrl);
    }

    [Fact]
    public void RestApiMethod_DefaultsGet_WhenKeyAbsent()
    {
        var profile = MakeProfile("rest-api", []);
        Assert.Equal("GET", profile.RestApiMethod);
    }

    [Fact]
    public void RestApiMethod_UppercasesValue()
    {
        var profile = MakeProfile("rest-api",
            new() { [GroundingProviderProfile.KeyRestApiMethod] = "post" });
        Assert.Equal("POST", profile.RestApiMethod);
    }

    [Fact]
    public void RestApiMaxItems_DefaultsTen_WhenKeyAbsent()
    {
        var profile = MakeProfile("rest-api", []);
        Assert.Equal(10, profile.RestApiMaxItems);
    }

    [Fact]
    public void RestApiMaxItems_ReturnsConfiguredValue()
    {
        var profile = MakeProfile("rest-api",
            new() { [GroundingProviderProfile.KeyRestApiMaxItems] = "3" });
        Assert.Equal(3, profile.RestApiMaxItems);
    }

    [Fact]
    public void RestApiAuthHeaderName_DefaultsAuthorization()
    {
        var profile = MakeProfile("rest-api", []);
        Assert.Equal("Authorization", profile.RestApiAuthHeaderName);
    }

    [Fact]
    public void RestApiAuthHeaderFormat_DefaultsBearerTemplate()
    {
        var profile = MakeProfile("rest-api", []);
        Assert.Equal("Bearer {token}", profile.RestApiAuthHeaderFormat);
    }

    [Fact]
    public void RestApiAuthHeaderEnv_ReturnsNull_WhenKeyAbsent()
    {
        var profile = MakeProfile("rest-api", []);
        Assert.Null(profile.RestApiAuthHeaderEnv);
    }

    [Fact]
    public void RestApiHeaders_ReturnsEmptyDict_WhenKeyAbsent()
    {
        var profile = MakeProfile("rest-api", []);
        Assert.Empty(profile.RestApiHeaders);
    }

    [Fact]
    public void RestApiHeaders_DeserializesJsonString()
    {
        var profile = MakeProfile("rest-api",
            new() { [GroundingProviderProfile.KeyRestApiHeaders] = """{"X-Custom":"value1","Accept":"application/json"}""" });
        Assert.Equal("value1", profile.RestApiHeaders["X-Custom"]);
        Assert.Equal("application/json", profile.RestApiHeaders["Accept"]);
    }

    [Fact]
    public void RestApiHeaders_ReturnsEmptyDict_OnInvalidJson()
    {
        var profile = MakeProfile("rest-api",
            new() { [GroundingProviderProfile.KeyRestApiHeaders] = "not-json" });
        Assert.Empty(profile.RestApiHeaders);
    }

    [Fact]
    public void RestApiResponsePath_ReturnsNull_WhenKeyAbsent()
    {
        var profile = MakeProfile("rest-api", []);
        Assert.Null(profile.RestApiResponsePath);
    }

    [Fact]
    public void RestApiBodyTemplate_ReturnsNull_WhenKeyAbsent()
    {
        var profile = MakeProfile("rest-api", []);
        Assert.Null(profile.RestApiBodyTemplate);
    }

    // ── discriminator constants ───────────────────────────────────────────────

    [Fact]
    public void GroundingProviderTypes_AcademicSearch_HasCorrectValue()
        => Assert.Equal("academic-search", GroundingProviderTypes.AcademicSearch);

    [Fact]
    public void GroundingProviderTypes_RestApi_HasCorrectValue()
        => Assert.Equal("rest-api", GroundingProviderTypes.RestApi);

    // ── helpers ──────────────────────────────────────────────────────────────

    private static GroundingProviderProfile MakeProfile(string providerType, Dictionary<string, string> settings)
        => new("test", "Test", "desc", providerType, settings, null, false);
}
