using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.HowItWorks;

/// <summary>
/// The start page reads the same switches as the page "How it works" (through <c>HowItWorksFeatures</c>): it never offers asking,
/// changing, web research or a fact check that is off for the viewer, and every number it names comes from the settings.
/// </summary>
public sealed class LandingSwitchesTests : TestContext
{
    [Fact]
    public void As_shipped_it_offers_questions_changing_and_looking_things_up_and_says_the_fact_check_changes_nothing()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        UseShippedSettings(Services);

        var cut = RenderComponent<Landing>();
        var text = cut.Find(".landing").TextContent;

        Assert.Contains("Atelier may ask up to 3 quick questions first.", cut.Find("[data-testid='landing-hero-sub']").TextContent, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[data-testid='landing-step-questions']"));
        Assert.Single(cut.FindAll("[data-testid='landing-example-question']"));
        Assert.Contains(RevisionLines.ChangeButton, cut.Find("[data-testid='landing-step-change']").TextContent, StringComparison.Ordinal);
        Assert.Contains("never does this when you add files", cut.Find("[data-testid='landing-step-research']").TextContent, StringComparison.Ordinal);
        Assert.StartsWith("It looks things up on the web only for gaps", cut.Find("[data-testid='landing-limit-web']").TextContent, StringComparison.Ordinal);
        Assert.StartsWith("A web search service gets only a few short topics", cut.Find("[data-testid='landing-privacy-research']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Right now nothing goes to a web search", text, StringComparison.Ordinal);
        Assert.Contains("It does not change the text.", cut.Find("[data-testid='landing-step-factcheck']").TextContent, StringComparison.Ordinal);
        Assert.Contains(OrderCheckLines.FactsComparedLeadNoFiles + OrderCheckLines.FactsOpenOne, cut.Find("[data-testid='landing-example-checks']").TextContent, StringComparison.Ordinal);
        Assert.Contains(OrderCheckLines.Limits, cut.Find("[data-testid='landing-example-checks']").TextContent, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[data-testid='landing-example-unbacked']"));
        Assert.Single(cut.FindAll("[data-testid='landing-limit-learning']"));
        Assert.Contains("shown only to administrators", cut.Find("[data-testid='landing-teams-visibility']").TextContent, StringComparison.Ordinal);
        Assert.Contains("about 30 days", cut.Find("[data-testid='landing-privacy-kept']").TextContent, StringComparison.Ordinal);
        Assert.Contains("up to 10 per request", text, StringComparison.Ordinal);
        Assert.Contains("150,000 characters", text, StringComparison.Ordinal);
    }

    [Fact]
    public void With_questions_changing_and_the_fact_check_off_none_of_them_is_offered()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        Services.Configure<OrderOptions>(o => { o.IntakeMode = IntakeMode.Shadow; o.Revise = ReviseMode.Off; });
        Services.Configure<DirectOptions>(o => o.FactCheckMode = FactCheckMode.Off);

        var cut = RenderComponent<Landing>();
        var text = cut.Find(".landing").TextContent;

        Assert.Empty(cut.FindAll("[data-testid='landing-step-questions']"));
        Assert.Empty(cut.FindAll("[data-testid='landing-example-question']"));
        Assert.Empty(cut.FindAll("[data-testid='landing-example-from-answer']"));
        Assert.DoesNotContain("quick questions", cut.Find("[data-testid='landing-hero-sub']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("asks first", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("may ask", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Four steps from your words to your text.", cut.Find("#lp-steps-title").TextContent);

        Assert.Empty(cut.FindAll("[data-testid='landing-step-change']"));
        Assert.DoesNotContain(RevisionLines.ChangeButton, text, StringComparison.Ordinal);
        Assert.Contains("Everything in one place", cut.Find("[data-testid='landing-cap-versions']").TextContent, StringComparison.Ordinal);

        Assert.DoesNotContain("fact check", text, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(cut.FindAll("[data-testid='landing-example-flag-note']"));
        Assert.Empty(cut.FindAll("[data-testid='landing-example-unbacked']"));
        Assert.Empty(cut.FindAll("[data-testid='landing-limit-factcheck']"));
        Assert.Contains(OrderCheckLines.FactsNotChecked, cut.Find("[data-testid='landing-example-checks']").TextContent, StringComparison.Ordinal);
        Assert.Contains(OrderCheckLines.ToReviewFactsNotFullyChecked, cut.Find("[data-testid='landing-example-checks']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void With_research_on_it_says_when_it_looks_things_up_and_that_files_never_go_into_a_search()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        Services.Configure<OrderOptions>(o => o.IntakeMode = IntakeMode.On);
        Services.Configure<ResearchOptions>(o => o.Mode = ResearchMode.On);

        var cut = RenderComponent<Landing>();

        Assert.Contains("never does this when you add files", cut.Find("[data-testid='landing-step-research']").TextContent, StringComparison.Ordinal);
        Assert.Contains("shows what was looked up and on which page", cut.Find("[data-testid='landing-step-research']").TextContent, StringComparison.Ordinal);
        Assert.StartsWith("It looks things up on the web only for gaps", cut.Find("[data-testid='landing-limit-web']").TextContent, StringComparison.Ordinal);
        Assert.Contains("A page it finds can be wrong", cut.Find("[data-testid='landing-limit-web']").TextContent, StringComparison.Ordinal);
        var privacy = cut.Find("[data-testid='landing-privacy-research']").TextContent;
        Assert.Contains("only when you add no files", privacy, StringComparison.Ordinal);
        Assert.Contains("It never gets your answers or your files.", privacy, StringComparison.Ordinal);
        Assert.Contains("About me details are held back", privacy, StringComparison.Ordinal);
    }

    [Fact]
    public void With_research_switched_off_again_it_says_nothing_goes_to_a_web_search()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        UseShippedSettings(Services);
        Services.Configure<ResearchOptions>(o => o.Mode = ResearchMode.Off);

        var cut = RenderComponent<Landing>();

        Assert.Empty(cut.FindAll("[data-testid='landing-step-research']"));
        Assert.StartsWith("Right now it does not look things up on the web", cut.Find("[data-testid='landing-limit-web']").TextContent, StringComparison.Ordinal);
        Assert.StartsWith("Right now nothing goes to a web search", cut.Find("[data-testid='landing-privacy-research']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Research_without_questions_is_not_offered_because_the_topics_come_from_reading_the_request()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        Services.Configure<OrderOptions>(o => o.IntakeMode = IntakeMode.Off);
        Services.Configure<ResearchOptions>(o => o.Mode = ResearchMode.On);

        var cut = RenderComponent<Landing>();

        Assert.Empty(cut.FindAll("[data-testid='landing-step-research']"));
        Assert.StartsWith("Right now", cut.Find("[data-testid='landing-limit-web']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void With_the_fact_check_on_it_says_some_passages_may_be_fixed_once()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        Services.Configure<DirectOptions>(o => o.FactCheckMode = FactCheckMode.On);

        var cut = RenderComponent<Landing>();

        Assert.Contains("may be fixed once", cut.Find("[data-testid='landing-step-factcheck']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("It does not change the text.", cut.Find("[data-testid='landing-step-factcheck']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_person_with_an_exception_sees_changing_and_nobody_else_does()
    {
        Services.Configure<OrderOptions>(o => { o.IntakeMode = IntakeMode.On; o.Revise = ReviseMode.Off; o.ReviseOverrides["tester"] = ReviseMode.On; });
        var auth = this.AddTestAuthorization();

        auth.SetAuthorized("tester");
        var tester = RenderComponent<Landing>();
        Assert.Single(tester.FindAll("[data-testid='landing-step-change']"));
        Assert.Equal("/start", tester.Find("[data-testid='landing-hero-start']").GetAttribute("href"));
        Assert.Empty(tester.FindAll("[data-testid='landing-hero-account']"));

        auth.SetAuthorized("someone-else");
        Assert.Empty(RenderComponent<Landing>().FindAll("[data-testid='landing-step-change']"));

        auth.SetNotAuthorized();
        var anonymous = RenderComponent<Landing>();
        Assert.Empty(anonymous.FindAll("[data-testid='landing-step-change']"));
        Assert.Equal("/login", anonymous.Find("[data-testid='landing-hero-start']").GetAttribute("href"));
    }

    [Fact]
    public void Every_number_on_the_page_comes_from_the_settings()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        Services.Configure<OrderOptions>(o => { o.IntakeMode = IntakeMode.On; o.Revise = ReviseMode.On; o.MaxVersionsPerOrder = 4; });
        Services.Configure<IntakeOptions>(o => o.MaxQuestions = 2);
        Services.Configure<SourceOptions>(o => { o.MaxFilesPerOrder = 7; o.FullTextMaxChars = 100_000; o.OriginalRetention = TimeSpan.FromDays(14); });

        var cut = RenderComponent<Landing>();
        var text = cut.Find(".landing").TextContent;

        Assert.Contains("up to 2 quick questions", text, StringComparison.Ordinal);
        Assert.Contains("up to 2 short questions", text, StringComparison.Ordinal);
        Assert.DoesNotContain("up to 3", text, StringComparison.Ordinal);
        Assert.Contains("up to 7 per request", text, StringComparison.Ordinal);
        Assert.Contains("Add up to 7 files", text, StringComparison.Ordinal);
        Assert.Contains("100,000 characters (roughly 40 pages)", text, StringComparison.Ordinal);
        Assert.Contains("up to 4 in all", text, StringComparison.Ordinal);
        Assert.Contains("about 14 days", cut.Find("[data-testid='landing-privacy-kept']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void With_the_workshop_open_to_everyone_the_page_does_not_call_it_admin_only()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        Services.Configure<UiOptions>(o => o.WorkshopForAdminsOnly = false);

        var cut = RenderComponent<Landing>();

        Assert.DoesNotContain("only to administrators", cut.Find("[data-testid='landing-teams']").TextContent, StringComparison.Ordinal);
        Assert.Equal("The workshop, for teams.", cut.Find("#lp-workshop-title").TextContent);
    }

    [Fact]
    public void The_page_has_one_main_heading_and_never_skips_a_heading_level()
    {
        this.AddTestAuthorization().SetNotAuthorized();
        UseShippedSettings(Services);

        var headings = RenderComponent<Landing>().FindAll("h1, h2, h3, h4, h5, h6");

        Assert.Single(headings, h => h.LocalName == "h1");
        Assert.Equal("h1", headings[0].LocalName);
        var previous = 1;
        foreach (var heading in headings)
        {
            var level = heading.LocalName[1] - '0';
            Assert.True(level <= previous + 1, $"The heading “{heading.TextContent.Trim()}” ({heading.LocalName}) skips a level after h{previous}.");
            previous = level;
        }
    }

    [Fact]
    public void Every_section_is_named_by_its_heading()
    {
        this.AddTestAuthorization().SetNotAuthorized();

        var cut = RenderComponent<Landing>();

        Assert.All(cut.FindAll("main section[aria-labelledby]"), s =>
            Assert.False(string.IsNullOrWhiteSpace(cut.Find("#" + s.GetAttribute("aria-labelledby")).TextContent)));
        Assert.Equal(cut.FindAll("main section:not(#film)").Count, cut.FindAll("main section[aria-labelledby]").Count);
    }

    /// <summary>Configures <paramref name="services"/> with the settings shipped in <c>appsettings.json</c>.</summary>
    internal static void UseShippedSettings(IServiceCollection services)
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(LandingLinksTests.RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();
        services.Configure<OrderOptions>(shipped.GetSection(OrderOptions.SectionName));
        services.Configure<IntakeOptions>(shipped.GetSection(IntakeOptions.SectionName));
        services.Configure<ResearchOptions>(shipped.GetSection(ResearchOptions.SectionName));
        services.Configure<DirectOptions>(shipped.GetSection(DirectOptions.SectionName));
        services.Configure<UploadOptions>(shipped.GetSection(UploadOptions.SectionName));
        services.Configure<UiOptions>(shipped.GetSection(UiOptions.SectionName));
        services.Configure<LearningOptions>(shipped.GetSection(LearningOptions.SectionName));
        services.Configure<SourceOptions>(shipped.GetSection(SourceOptions.SectionName));
    }
}
