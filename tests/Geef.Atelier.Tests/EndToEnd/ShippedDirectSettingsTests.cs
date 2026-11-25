using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Microsoft.Extensions.Configuration;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// Holds the switches of stage 3 that ship in <c>src/Geef.Atelier.Web/appsettings.json</c> (contract stage 3, section 10): the fact
/// check runs for every order and is shown without changing the text (<c>Report</c>), no user has an override in the file (the
/// measuring user gets <c>On</c> only through the environment), the writer's notes and the recalculation of totals are on, and
/// assumptions about facts reach the writer as open points. Change these values only together with the gates that decide them:
/// <c>FactCheckMode</c> after V2 (<c>Shadow</c> when V2 fails) and Q4 (<c>On</c>), <c>WriterNotes</c> after V1,
/// <c>SourceArithmetic</c> after V3, each with a line in the decision log D-073. The time limits of the fact check follow the
/// decision after V2 (D-073): 90 s for the fact-check model, 60 s for the one call of the fallback model.
/// </summary>
public sealed class ShippedDirectSettingsTests
{
    private static IConfiguration Shipped() =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();

    [Fact]
    public void The_fact_check_ships_in_report_mode_without_overrides_and_with_its_fallback()
    {
        var configuration = Shipped();
        var direct = configuration.GetSection(DirectOptions.SectionName).Get<DirectOptions>() ?? new DirectOptions();

        Assert.Equal(FactCheckMode.Report, direct.FactCheckMode);
        Assert.Equal("Report", configuration["Direct:FactCheckMode"]);
        Assert.Empty(direct.FactCheckModeOverrides);
        Assert.Empty(configuration.GetSection("Direct:FactCheckModeOverrides").GetChildren());
        Assert.True(direct.FactCheckFallbackModel);
    }

    [Fact]
    public void Notes_totals_and_open_facts_ship_as_measured()
    {
        var configuration = Shipped();
        var direct = configuration.GetSection(DirectOptions.SectionName).Get<DirectOptions>() ?? new DirectOptions();
        var intake = configuration.GetSection("Intake").Get<IntakeOptions>() ?? new IntakeOptions();

        Assert.True(direct.WriterNotes);
        Assert.True(direct.SourceArithmetic);
        Assert.Equal(ContentAssumptionMode.OpenPoints, intake.ContentAssumptions);
        Assert.Equal("OpenPoints", configuration["Intake:ContentAssumptions"]);
    }

    [Fact]
    public void Limits_not_in_the_file_keep_the_contract_defaults()
    {
        var direct = Shipped().GetSection(DirectOptions.SectionName).Get<DirectOptions>() ?? new DirectOptions();

        Assert.Equal(150_000, direct.FactCheckMaxMaterialChars);
        Assert.Equal(8_192, direct.FactCheckMaxTokens);
    }

    [Fact]
    public void The_time_limits_of_the_fact_check_ship_as_decided_after_v2()
    {
        var configuration = Shipped();
        var direct = configuration.GetSection(DirectOptions.SectionName).Get<DirectOptions>() ?? new DirectOptions();
        var defaults = new DirectOptions();

        Assert.Equal(TimeSpan.FromSeconds(90), defaults.FactCheckTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), defaults.FactCheckFallbackTimeout);
        Assert.Equal("00:01:30", configuration["Direct:FactCheckTimeout"]);
        Assert.Equal("00:01:00", configuration["Direct:FactCheckFallbackTimeout"]);
        Assert.Equal(TimeSpan.FromSeconds(90), direct.FactCheckTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), direct.FactCheckFallbackTimeout);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
