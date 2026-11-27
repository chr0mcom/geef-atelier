using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Pipeline;
using Microsoft.Extensions.Configuration;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// The review-loop safeguards of D-085 per user name (D-087): each switch has its own overrides, read from configuration in the form an
/// environment variable gives (<c>Convergence__BestDraftOverrides__&lt;user&gt;</c>); an override wins over the switch, only for runs of
/// that user. Shipped without overrides, so every run behaves as before.
/// </summary>
public sealed class LoopSafeguardsOverrideTests
{
    private const string Meter = "meter";

    [Fact]
    public void Shipped_settings_have_no_overrides_and_no_user_gets_a_safeguard()
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build()
            .GetSection("Convergence").Get<ConvergenceOptions>()!;

        Assert.Empty(shipped.BestDraftOverrides);
        Assert.Empty(shipped.WriterBlockingOnlyOverrides);
        Assert.Empty(shipped.StopWithoutProgressOverrides);
        Assert.Equal(LoopSafeguards.None, LoopSafeguards.From(shipped, Meter));
        Assert.Equal(LoopSafeguards.None, LoopSafeguards.From(new ConvergenceOptions(), Meter));
    }

    [Fact]
    public void Overrides_bind_from_environment_style_keys_and_apply_only_to_their_user()
    {
        var options = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Convergence:BestDraftOverrides:{Meter}"] = "On",
                [$"Convergence:WriterBlockingOnlyOverrides:{Meter}"] = "On",
                [$"Convergence:StopWithoutProgressOverrides:{Meter}"] = "On",
            })
            .Build()
            .GetSection("Convergence").Get<ConvergenceOptions>()!;

        Assert.Equal(new LoopSafeguards(true, true, true), LoopSafeguards.From(options, Meter));
        Assert.Equal(new LoopSafeguards(true, true, true), LoopSafeguards.From(options, "METER"));
        Assert.Equal(LoopSafeguards.None, LoopSafeguards.From(options, "someone-else"));
        Assert.Equal(LoopSafeguards.None, LoopSafeguards.From(options, null));
        Assert.Equal(LoopSafeguards.None, LoopSafeguards.From(options));
    }

    [Fact]
    public void Each_override_maps_on_its_own_and_wins_over_the_switch_in_both_directions()
    {
        Assert.Equal(new LoopSafeguards(true, false, false), LoopSafeguards.From(new ConvergenceOptions { BestDraftOverrides = { [Meter] = LoopSwitch.On } }, Meter));
        Assert.Equal(new LoopSafeguards(false, true, false), LoopSafeguards.From(new ConvergenceOptions { WriterBlockingOnlyOverrides = { [Meter] = LoopSwitch.On } }, Meter));
        Assert.Equal(new LoopSafeguards(false, false, true), LoopSafeguards.From(new ConvergenceOptions { StopWithoutProgressOverrides = { [Meter] = LoopSwitch.On } }, Meter));

        var on = new ConvergenceOptions
        {
            BestDraft = LoopSwitch.On,
            WriterBlockingOnly = LoopSwitch.On,
            StopWithoutProgress = LoopSwitch.On,
            BestDraftOverrides = { [Meter] = LoopSwitch.Off },
        };
        Assert.Equal(new LoopSafeguards(false, true, true), LoopSafeguards.From(on, Meter));
        Assert.Equal(new LoopSafeguards(true, true, true), LoopSafeguards.From(on, "someone-else"));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
