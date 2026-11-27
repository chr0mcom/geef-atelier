using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Microsoft.Extensions.Configuration;

namespace Geef.Atelier.Tests.Intake;

/// <summary>
/// Holds the intake settings that ship in <c>src/Geef.Atelier.Web/appsettings.json</c>. The intake goes live (<c>On</c>) only
/// after the measuring gates of contract 6.6 passed on the corpus; it replaced the shadow gate by an independent check of the
/// questions (decision log D-072). Change the expected values here only together with a new measurement and the decision log;
/// after a change of the clarifier prompt or schema the stored results stop being evaluable and have to be measured again.
/// </summary>
public sealed class ShippedIntakeSettingsTests
{
    [Fact]
    public void The_shipped_settings_ask_before_the_run_and_append_the_answers()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();

        var orders = configuration.GetSection("Orders").Get<OrderOptions>() ?? new OrderOptions();
        var intake = configuration.GetSection("Intake").Get<IntakeOptions>() ?? new IntakeOptions();

        Assert.Equal(IntakeMode.On, orders.IntakeMode);
        Assert.Equal("On", configuration["Orders:IntakeMode"]);
        Assert.Equal(BriefMode.AppendAnswers, intake.BriefMode);
    }

    [Fact]
    public void The_shipped_settings_offer_tell_atelier_once_with_its_promise_while_the_code_default_stays_without_it()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();

        var orders = configuration.GetSection("Orders").Get<OrderOptions>() ?? new OrderOptions();

        Assert.Equal("On", configuration["Orders:TellOnce"]);
        Assert.Equal(TellOnceMode.On, orders.TellOnce);
        Assert.Equal(TellOnceMode.WithoutPromise, new OrderOptions().TellOnce);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
