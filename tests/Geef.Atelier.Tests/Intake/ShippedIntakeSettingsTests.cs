using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Microsoft.Extensions.Configuration;

namespace Geef.Atelier.Tests.Intake;

/// <summary>
/// Holds the intake settings that ship in <c>src/Geef.Atelier.Web/appsettings.json</c>. The intake goes live (<c>On</c>) only
/// after the measuring gates of contract 6.6 have passed on the corpus and on shadow data; until then it ships as <c>Shadow</c>
/// with appended answers. Change the expected values here only together with passed measuring gates and the decision log.
/// </summary>
public sealed class ShippedIntakeSettingsTests
{
    [Fact]
    public void The_shipped_settings_keep_the_intake_in_shadow_mode_with_appended_answers()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();

        var orders = configuration.GetSection("Orders").Get<OrderOptions>() ?? new OrderOptions();
        var intake = configuration.GetSection("Intake").Get<IntakeOptions>() ?? new IntakeOptions();

        Assert.Equal(IntakeMode.Shadow, orders.IntakeMode);
        Assert.Equal("Shadow", configuration["Orders:IntakeMode"]);
        Assert.Equal(BriefMode.AppendAnswers, intake.BriefMode);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
