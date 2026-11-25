using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    public void The_date_rule_ships_switched_on_after_gate_g_a2()
    {
        var configuration = Shipped().Build();

        Assert.Equal("True", configuration["Intake:DropGuessedDateOptions"], ignoreCase: true);
        Assert.True(Bound(configuration).DropGuessedDateOptions);
    }

    [Fact]
    public void The_environment_variable_turns_the_date_rule_off_through_the_same_binding_as_the_web_host()
    {
        var prefix = $"GEEF_TEST_{Guid.NewGuid():N}_";
        Environment.SetEnvironmentVariable($"{prefix}Intake__DropGuessedDateOptions", "false");
        try
        {
            var configuration = Shipped().AddEnvironmentVariables(prefix).Build();

            Assert.False(Bound(configuration).DropGuessedDateOptions);
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{prefix}Intake__DropGuessedDateOptions", null);
        }
    }

    private static IConfigurationBuilder Shipped() => new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false);

    private static IntakeOptions Bound(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.Configure<IntakeOptions>(configuration.GetSection(IntakeOptions.SectionName));
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<IntakeOptions>>().Value;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
