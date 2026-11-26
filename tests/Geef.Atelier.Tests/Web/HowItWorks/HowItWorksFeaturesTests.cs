using System.Security.Claims;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Web.Display;

namespace Geef.Atelier.Tests.Web.HowItWorks;

/// <summary>
/// The explanation page describes a feature as available only when the person really gets it: the same switches and the same
/// exceptions per person as the application, nothing for anybody signed out beyond the values for everyone, and a fact-check mode
/// the page does not know is never described as more than pointing things out.
/// </summary>
public sealed class HowItWorksFeaturesTests
{
    [Fact]
    public void With_the_code_defaults_nothing_switched_off_is_called_available()
    {
        var features = Settings().FeaturesFor(null);

        Assert.False(features.ForSignedInPerson);
        Assert.False(features.Questions);
        Assert.False(features.ChangeText);
        Assert.False(features.WebResearch);
        Assert.Equal(FactCheckOffer.ReportsOnly, features.FactCheck);
        Assert.False(features.UploadThroughAssistant);
        Assert.False(features.WorkshopVisible);
        Assert.False(features.LearnsFromEarlierTexts);
        Assert.True(features.OlderAssistantTools);
    }

    [Fact]
    public void The_exceptions_of_one_person_count_only_for_that_person()
    {
        var settings = Settings(
            orders: new OrderOptions { IntakeMode = IntakeMode.On, ReviseOverrides = { ["tester"] = ReviseMode.On } },
            research: new ResearchOptions { ModeOverrides = { ["tester"] = ResearchMode.On } },
            direct: new DirectOptions { FactCheckModeOverrides = { ["tester"] = FactCheckMode.On } });

        var tester = settings.FeaturesFor(User("Tester"));
        var other = settings.FeaturesFor(User("alice"));
        var nobody = settings.FeaturesFor(null);

        Assert.True(tester.ForSignedInPerson);
        Assert.True(tester.ChangeText);
        Assert.True(tester.WebResearch);
        Assert.Equal(FactCheckOffer.MayFixOnce, tester.FactCheck);
        foreach (var features in new[] { other, nobody })
        {
            Assert.False(features.ChangeText);
            Assert.False(features.WebResearch);
            Assert.Equal(FactCheckOffer.ReportsOnly, features.FactCheck);
        }
    }

    [Fact]
    public void An_exception_can_also_switch_a_feature_off_for_one_person()
    {
        var settings = Settings(
            orders: new OrderOptions { IntakeMode = IntakeMode.On, Revise = ReviseMode.On, ReviseOverrides = { ["tester"] = ReviseMode.Off } },
            research: new ResearchOptions { Mode = ResearchMode.On, ModeOverrides = { ["tester"] = ResearchMode.Off } },
            direct: new DirectOptions { FactCheckMode = FactCheckMode.On, FactCheckModeOverrides = { ["tester"] = FactCheckMode.Off } });

        var tester = settings.FeaturesFor(User("tester"));
        var everyone = settings.FeaturesFor(null);

        Assert.False(tester.ChangeText);
        Assert.False(tester.WebResearch);
        Assert.Equal(FactCheckOffer.NotShown, tester.FactCheck);
        Assert.True(everyone.ChangeText);
        Assert.True(everyone.WebResearch);
        Assert.Equal(FactCheckOffer.MayFixOnce, everyone.FactCheck);
    }

    [Theory]
    [InlineData(IntakeMode.Off)]
    [InlineData(IntakeMode.Shadow)]
    public void Research_is_never_called_available_without_questions_because_its_topics_come_from_reading_the_request(IntakeMode intake)
    {
        var settings = Settings(orders: new OrderOptions { IntakeMode = intake }, research: new ResearchOptions { Mode = ResearchMode.On });

        Assert.False(settings.FeaturesFor(null).WebResearch);
        Assert.False(settings.FeaturesFor(null).Questions);
    }

    [Fact]
    public void Switching_a_feature_on_for_everyone_shows_it_without_any_other_change()
    {
        var settings = Settings(
            orders: new OrderOptions { IntakeMode = IntakeMode.On, Revise = ReviseMode.On },
            research: new ResearchOptions { Mode = ResearchMode.On },
            direct: new DirectOptions { FactCheckMode = FactCheckMode.On },
            uploads: new UploadOptions { Tickets = UploadTicketsMode.On });

        foreach (var features in new[] { settings.FeaturesFor(null), settings.FeaturesFor(User("alice")) })
        {
            Assert.True(features.Questions);
            Assert.True(features.ChangeText);
            Assert.True(features.WebResearch);
            Assert.Equal(FactCheckOffer.MayFixOnce, features.FactCheck);
            Assert.True(features.UploadThroughAssistant);
        }
    }

    [Theory]
    [InlineData(FactCheckMode.Off, FactCheckOffer.NotShown)]
    [InlineData(FactCheckMode.Shadow, FactCheckOffer.NotShown)]
    [InlineData(FactCheckMode.Report, FactCheckOffer.ReportsOnly)]
    [InlineData(FactCheckMode.On, FactCheckOffer.MayFixOnce)]
    [InlineData((FactCheckMode)42, FactCheckOffer.ReportsOnly)]
    public void Every_fact_check_mode_has_a_description_and_an_unknown_one_never_claims_a_fix(FactCheckMode mode, FactCheckOffer expected) =>
        Assert.Equal(expected, mode.ToOffer());

    [Fact]
    public void A_signed_out_viewer_with_a_name_claim_is_treated_as_nobody()
    {
        var settings = Settings(orders: new OrderOptions { ReviseOverrides = { ["alice"] = ReviseMode.On } });
        var notSignedIn = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "alice")]));

        var features = settings.FeaturesFor(notSignedIn);

        Assert.False(features.ForSignedInPerson);
        Assert.False(features.ChangeText);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    public void The_workshop_is_called_visible_exactly_when_its_menu_is_shown(bool adminsOnly, bool admin, bool expected)
    {
        var settings = Settings(ui: new UiOptions { WorkshopForAdminsOnly = adminsOnly });

        Assert.Equal(expected, settings.FeaturesFor(User("alice", admin)).WorkshopVisible);
        Assert.False(settings.FeaturesFor(null).WorkshopVisible);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tester")]
    [InlineData("alice")]
    public void The_overrides_resolve_exactly_as_in_the_pipeline(string? user)
    {
        var direct = new DirectOptions { FactCheckMode = FactCheckMode.Report, FactCheckModeOverrides = { ["tester"] = FactCheckMode.On } };
        var research = new ResearchOptions { ModeOverrides = { ["tester"] = ResearchMode.On } };
        var orders = new OrderOptions { ReviseOverrides = { ["tester"] = ReviseMode.On } };

        Assert.Equal(OrderGroundingStep.Mode(direct, user), direct.FactCheckModeFor(user));
        Assert.Equal(user == "tester" ? ResearchMode.On : ResearchMode.Off, research.ModeFor(user));
        Assert.Equal(user == "tester" ? ReviseMode.On : ReviseMode.Off, orders.ReviseModeFor(user));
    }

    [Fact]
    public void The_limits_come_from_the_settings()
    {
        var settings = Settings(
            intake: new IntakeOptions { MaxQuestions = 2 },
            sources: new SourceOptions { MaxFilesPerOrder = 4, MaxFileBytes = 1_000, FullTextMaxChars = 10, MapMaxChars = 20 },
            orders: new OrderOptions { MaxVersionsPerOrder = 7 });

        var features = settings.FeaturesFor(null);

        Assert.Equal((2, 4, 1_000L, 10, 20, 7), (features.MaxQuestions, features.MaxFiles, features.MaxFileBytes, features.FullTextChars, features.MapChars, features.MaxVersions));
    }

    internal static ClaimsPrincipal User(string name, bool admin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        if (admin)
            claims.Add(new Claim(ClaimTypes.Role, OwnerScope.AdminRole));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookie"));
    }

    internal static HowItWorksSettings Settings(
        OrderOptions? orders = null, IntakeOptions? intake = null, ResearchOptions? research = null, DirectOptions? direct = null,
        UploadOptions? uploads = null, UiOptions? ui = null, McpOptions? mcp = null, LearningOptions? learning = null, SourceOptions? sources = null) =>
        new(orders ?? new OrderOptions(), intake ?? new IntakeOptions(), research ?? new ResearchOptions(), direct ?? new DirectOptions(),
            uploads ?? new UploadOptions(), ui ?? new UiOptions(), mcp ?? new McpOptions(), learning ?? new LearningOptions(), sources ?? new SourceOptions());
}
