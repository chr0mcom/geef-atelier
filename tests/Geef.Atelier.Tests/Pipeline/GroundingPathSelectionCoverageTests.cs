using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Persistence.Migrations;
using Geef.Atelier.Infrastructure.Pipeline;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// A10 (Etappe 0 review): the runtime rule for old snapshots covers exactly the system profiles that once carried a
/// tool name (frozen in Step47), and each of them still exists under that name in <see cref="SystemCrew"/>; a rename
/// of a system profile can therefore not silently drop the rule. The one exception is <c>academic-default</c>, retired in
/// stage 8c (D-089): it is no system profile any more, but stays in the rule so older snapshots never take the tool path.
/// </summary>
public sealed class GroundingPathSelectionCoverageTests
{
    [Fact]
    public void EverySystemProfileThatOnceHadAToolNameTakesTheProviderTypePath() =>
        Assert.Equal(
            Step47EngineHonesty.LegacyGroundingProfiles.Order(StringComparer.Ordinal),
            GroundingPathSelection.ProviderTypePathSystemProfiles.Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(FormerToolProfiles))]
    public void TheFormerToolProfileStillExistsAsSystemProfileWithoutToolName(string name)
    {
        if (RetiredInStage8c.Contains(name))
        {
            Assert.False(SystemCrew.GroundingProviderProfiles.ContainsKey(name));
            var fromOlderSnapshot = new Geef.Atelier.Core.Domain.Crew.Grounding.GroundingProviderProfile(
                name, name, "older snapshot", "academic-search", [], null, IsSystem: true, ToolName: "academic-search");
            Assert.False(fromOlderSnapshot.UsesToolPath());
            return;
        }

        Assert.True(SystemCrew.GroundingProviderProfiles.TryGetValue(name, out var profile), $"System profile '{name}' was renamed or removed.");
        Assert.True(profile!.IsSystem);
        Assert.Null(profile.ToolName);
        Assert.False(profile.UsesToolPath());
        Assert.False((profile with { ToolName = "knowledge-base" }).UsesToolPath());
    }

    private static readonly HashSet<string> RetiredInStage8c = ["academic-default"];

    public static TheoryData<string> FormerToolProfiles()
    {
        var data = new TheoryData<string>();
        foreach (var name in Step47EngineHonesty.LegacyGroundingProfiles)
            data.Add(name);
        return data;
    }
}
