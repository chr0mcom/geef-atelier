using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Configuration;

namespace Geef.Atelier.Tests.Research;

/// <summary>
/// Holds the research settings that ship in <c>src/Geef.Atelier.Web/appsettings.json</c> (contract stage 4, section 10): research is
/// off for everyone until the paired measurement R3 passed, overrides only come from the environment, and no audit directory is set.
/// It also holds the part of <c>Program.cs</c> the passage picker relies on: the shared LLM client retries only by
/// <c>LlmHttpRetry.MayRetry</c>, which never repeats a request with a gateway time limit.
/// </summary>
public sealed class ShippedResearchSettingsTests
{
    [Fact]
    public void Research_ships_off_with_the_contract_limits()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();

        var research = configuration.GetSection(ResearchOptions.SectionName).Get<ResearchOptions>() ?? new ResearchOptions();

        Assert.Equal("Off", configuration["Research:Mode"]);
        Assert.Equal(ResearchMode.Off, research.Mode);
        Assert.Empty(research.ModeOverrides);
        Assert.Equal((4, 3, 6_000, 12, 50), (research.MaxSearches, research.ResultsPerSearch, research.RawCharsPerPage, research.MaxEntries, research.DailyOrderCap));
        Assert.Equal((TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(60)), (research.SearchTimeout, research.CondenseTimeout));
        Assert.True(string.IsNullOrEmpty(research.AuditDirectory));
    }

    [Fact]
    public void The_shared_llm_client_retries_only_by_the_rule_that_spares_time_limited_requests()
    {
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "Program.cs"));

        Assert.Contains("LlmHttpRetry.MayRetry(args.Outcome.Result)", program, StringComparison.Ordinal);
        Assert.Equal(1, Count(program, "AddStandardResilienceHandler"));
        Assert.Equal(1, Count(program, "builder.Services.AddResearch(builder.Configuration);"));
        Assert.DoesNotContain("ConfigureHttpClientDefaults", program, StringComparison.Ordinal);
    }

    private static int Count(string text, string part) => text.Split(part).Length - 1;

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
