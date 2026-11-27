using System.Text.RegularExpressions;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Components.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.HowItWorks;

/// <summary>
/// The start page is the first thing a layperson sees: it uses plain words, names no model, number of the operation, price or
/// promise it cannot keep, says that the team loop belongs to the workshop and not to every text, and leads to the explanation page
/// with a full page load.
/// </summary>
public sealed class LandingWordsTests : TestContext
{
    private static readonly string[] TechnicalWords =
    [
        "crew", "manuscript", "iteration", "iter", "convergence", "converged", "converges", "briefing", "executor", "finalizer", "run",
        "grounding", "token", "orchestrat", "pipeline", "LLM",
    ];

    private static readonly string[] NeverOnTheStartPage =
    [
        "review rounds", "Claude Sonnet", "Sonnet", "Opus", "GPT", "Gemini", "Llama", "Anthropic", "OpenAI", "Mistral",
        "$", "€", "R-0c41", "Round 4", "treatise", "Most AI tools", "one breath", "learns", "guarantee", "flawless", "error-free",
        "fact-checked", "verified", "nothing is made up", "never makes", "always right", "100%", "our customers", "trusted by", "award",
    ];

    private static readonly string[] PlainWordsOnly = ["MCP", "models?", "AI roles", "rounds?", "manufactory", "starts directly"];

    [Fact]
    public void The_start_page_uses_none_of_the_technical_words_of_the_older_way()
    {
        this.AddTestAuthorization().SetNotAuthorized();

        var text = RenderComponent<Landing>().Find(".landing").TextContent;

        foreach (var word in TechnicalWords)
            Assert.DoesNotMatch(new Regex($@"\b{word}s?\b", RegexOptions.IgnoreCase), text);
        Assert.DoesNotContain("powers every run", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Every text written the standard way comes with what was checked", text, StringComparison.Ordinal);
        Assert.Contains("This is the standard way every text is written.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_start_page_names_no_model_price_operating_figure_or_promise()
    {
        this.AddTestAuthorization().SetNotAuthorized();

        var text = RenderComponent<Landing>().Find(".landing").TextContent;

        foreach (var phrase in NeverOnTheStartPage)
            Assert.DoesNotContain(phrase, text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("It never claims that the text is right.", text, StringComparison.Ordinal);
        Assert.Contains("It can still be wrong.", text, StringComparison.Ordinal);
    }

    public static TheoryData<string> SwitchStates => new() { "shipped", "all-off", "research-off", "fact-check-on", "signed-in-exception", "workshop-open" };

    [Theory]
    [MemberData(nameof(SwitchStates))]
    public void In_every_switch_state_the_start_page_stays_free_of_technical_words_names_and_promises(string state)
    {
        var auth = this.AddTestAuthorization();
        auth.SetNotAuthorized();
        LandingSwitchesTests.UseShippedSettings(Services);
        switch (state)
        {
            case "all-off":
                Services.Configure<OrderOptions>(o => { o.IntakeMode = IntakeMode.Off; o.Revise = ReviseMode.Off; });
                Services.Configure<DirectOptions>(o => o.FactCheckMode = FactCheckMode.Off);
                break;
            case "research-off":
                Services.Configure<ResearchOptions>(o => o.Mode = ResearchMode.Off);
                break;
            case "fact-check-on":
                Services.Configure<DirectOptions>(o => o.FactCheckMode = FactCheckMode.On);
                break;
            case "signed-in-exception":
                Services.Configure<OrderOptions>(o => { o.Revise = ReviseMode.Off; o.ReviseOverrides["tester"] = ReviseMode.On; });
                Services.Configure<ResearchOptions>(o => o.ModeOverrides["tester"] = ResearchMode.Off);
                auth.SetAuthorized("tester");
                break;
            case "workshop-open":
                Services.Configure<UiOptions>(o => o.WorkshopForAdminsOnly = false);
                break;
        }

        var text = RenderComponent<Landing>().Find(".landing").TextContent;

        foreach (var word in TechnicalWords)
            Assert.DoesNotMatch(new Regex($@"\b{word}s?\b", RegexOptions.IgnoreCase), text);
        foreach (var phrase in NeverOnTheStartPage)
            Assert.DoesNotContain(phrase, text, StringComparison.OrdinalIgnoreCase);
        foreach (var plain in PlainWordsOnly)
            Assert.DoesNotMatch(new Regex($@"\b{plain}\b", RegexOptions.IgnoreCase), text);
    }

    [Fact]
    public void The_example_is_marked_as_made_up()
    {
        this.AddTestAuthorization().SetNotAuthorized();

        var cut = RenderComponent<Landing>();

        Assert.Equal("A made-up example", cut.Find("#example .lp-eyebrow").TextContent);
        Assert.Contains("This example is made up", cut.Find("[data-testid='landing-example-made-up']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_title_and_description_hold_for_every_setting_and_use_plain_words()
    {
        foreach (var text in new[] { Landing.Title, Landing.Description })
        {
            foreach (var word in TechnicalWords)
                Assert.DoesNotMatch(new Regex($@"\b{word}s?\b", RegexOptions.IgnoreCase), text);
            foreach (var phrase in NeverOnTheStartPage)
                Assert.DoesNotContain(phrase, text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("question", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("web", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("change", text, StringComparison.OrdinalIgnoreCase);
        }
        Assert.InRange(Landing.Description.Length, 70, 200);
    }

    [Fact]
    public void The_links_to_the_explanation_page_load_it_as_a_full_page()
    {
        this.AddTestAuthorization().SetNotAuthorized();

        var links = RenderComponent<Landing>().FindAll("a[href^='/how-it-works']");

        Assert.True(links.Count >= 8, $"Only {links.Count} links lead to the explanation page.");
        Assert.All(links, a => Assert.Equal("false", a.GetAttribute("data-enhance-nav")));
    }

    [Fact]
    public void Signed_out_the_menu_offers_to_sign_in()
    {
        Services.AddSingleton<Microsoft.AspNetCore.Http.IHttpContextAccessor>(new Microsoft.AspNetCore.Http.HttpContextAccessor());
        this.AddTestAuthorization().SetNotAuthorized();

        var cut = RenderComponent<UserMenu>();

        Assert.Equal("/login", cut.Find("[data-testid='menu-sign-in']").GetAttribute("href"));
    }
}
