using System.Text.RegularExpressions;
using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Intake;
using HowItWorksPage = Geef.Atelier.Web.Components.Pages.HowItWorks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.HowItWorks;

/// <summary>
/// The page "How it works": open without signing in, it follows the settings at the time it is shown (for a signed-in person with
/// their own exceptions) and never offers a feature that is off for the viewer. Switching a feature on needs no change of the page.
/// It uses the fixed sentences of "What was checked" itself, so the example cannot drift from the real result page.
/// </summary>
public sealed class HowItWorksPageTests : TestContext
{
    [Fact]
    public void The_page_is_open_without_signing_in_and_has_one_main_heading()
    {
        Assert.NotNull(typeof(HowItWorksPage).GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).SingleOrDefault());
        this.AddTestAuthorization();

        var cut = Render();

        Assert.Single(cut.FindAll("h1"));
        Assert.Equal("How Atelier works", cut.Find("h1").TextContent);
        Assert.Equal("/login", cut.Find("[data-testid='hiw-sign-in']").GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='hiw-start']"));
    }

    [Fact]
    public void With_the_shipped_settings_changing_and_research_are_shown_as_not_switched_on()
    {
        this.AddTestAuthorization();
        UseShippedSettings();

        var cut = Render();

        Assert.Equal("false", cut.Find("[data-testid='hiw-status-change']").GetAttribute("data-on"));
        Assert.Equal("false", cut.Find("[data-testid='hiw-status-research']").GetAttribute("data-on"));
        Assert.Equal("true", cut.Find("[data-testid='hiw-status-questions']").GetAttribute("data-on"));
        Assert.Equal("true", cut.Find("[data-testid='hiw-status-upload']").GetAttribute("data-on"));
        Assert.Contains("changes nothing", cut.Find("[data-testid='hiw-status-factcheck']").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='hiw-step-change']"));
        Assert.Empty(cut.FindAll("[data-testid='hiw-assistant-change']"));
        Assert.Empty(cut.FindAll("[data-testid='hiw-step-research']"));
        Assert.DoesNotContain(RevisionLines.ChangeButton, cut.Find("[data-testid='hiw-steps']").TextContent, StringComparison.Ordinal);
        Assert.Contains("does not change the text", cut.Find("[data-testid='hiw-step-factcheck']").TextContent, StringComparison.Ordinal);
        Assert.Contains("for everyone", cut.Find("[data-testid='hiw-status-scope']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_person_with_an_exception_sees_the_feature_and_nobody_else_does()
    {
        Services.Configure<OrderOptions>(o => { o.IntakeMode = IntakeMode.On; o.ReviseOverrides["tester"] = ReviseMode.On; });
        Services.Configure<ResearchOptions>(o => o.ModeOverrides["tester"] = ResearchMode.On);
        var auth = this.AddTestAuthorization();

        auth.SetAuthorized("tester");
        var tester = Render();
        Assert.Equal("true", tester.Find("[data-testid='hiw-status-change']").GetAttribute("data-on"));
        Assert.Equal("true", tester.Find("[data-testid='hiw-status-research']").GetAttribute("data-on"));
        Assert.Contains(RevisionLines.ChangeButton, tester.Find("[data-testid='hiw-step-change']").TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(tester.FindAll("[data-testid='hiw-assistant-change']"));
        Assert.NotEmpty(tester.FindAll("[data-testid='hiw-step-research']"));
        Assert.Contains("what you can use", tester.Find("[data-testid='hiw-status-scope']").TextContent, StringComparison.Ordinal);
        Assert.Equal("/start", tester.Find("[data-testid='hiw-start']").GetAttribute("href"));

        auth.SetAuthorized("alice");
        var alice = Render();
        Assert.Equal("false", alice.Find("[data-testid='hiw-status-change']").GetAttribute("data-on"));
        Assert.Equal("false", alice.Find("[data-testid='hiw-status-research']").GetAttribute("data-on"));
        Assert.Empty(alice.FindAll("[data-testid='hiw-step-change']"));
        Assert.Empty(alice.FindAll("[data-testid='hiw-assistant-change']"));
        Assert.Empty(alice.FindAll("[data-testid='hiw-step-research']"));
    }

    [Fact]
    public void The_settings_table_shows_the_values_for_everyone_and_never_an_exception()
    {
        Services.Configure<OrderOptions>(o => o.ReviseOverrides["tester"] = ReviseMode.On);
        this.AddTestAuthorization().SetAuthorized("tester");

        var cut = Render();

        var row = cut.FindAll("[data-testid='hiw-admins'] tbody tr").Single(r => r.TextContent.Contains("Orders:Revise", StringComparison.Ordinal));
        Assert.Equal("Off", row.Children[2].TextContent);
        Assert.DoesNotContain("tester", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Switched_on_for_everyone_every_feature_appears_without_a_change_of_the_page()
    {
        Services.Configure<OrderOptions>(o => { o.IntakeMode = IntakeMode.On; o.Revise = ReviseMode.On; });
        Services.Configure<ResearchOptions>(o => o.Mode = ResearchMode.On);
        Services.Configure<DirectOptions>(o => o.FactCheckMode = FactCheckMode.On);
        Services.Configure<UploadOptions>(o => o.Tickets = UploadTicketsMode.On);
        this.AddTestAuthorization();

        var cut = Render();

        foreach (var key in new[] { "questions", "change", "research", "factcheck", "upload" })
            Assert.Equal("true", cut.Find($"[data-testid='hiw-status-{key}']").GetAttribute("data-on"));
        Assert.Contains("may fix some", cut.Find("[data-testid='hiw-status-factcheck']").TextContent, StringComparison.Ordinal);
        Assert.Contains("fixed once", cut.Find("[data-testid='hiw-step-factcheck']").TextContent, StringComparison.Ordinal);
        Assert.Contains("uploads them for you", cut.Find("[data-testid='hiw-assistant-upload']").TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("[data-testid='hiw-step-change']"));
    }

    [Fact]
    public void Without_a_shown_fact_check_the_example_shows_no_flagged_passage_and_says_facts_were_not_checked()
    {
        Services.Configure<DirectOptions>(o => o.FactCheckMode = FactCheckMode.Shadow);
        this.AddTestAuthorization();

        var cut = Render();

        Assert.Empty(cut.FindAll("[data-testid='hiw-example-unbacked']"));
        Assert.Contains(OrderCheckLines.FactsNotChecked, cut.Find("[data-testid='hiw-example-checks']").TextContent, StringComparison.Ordinal);
        Assert.Equal("false", cut.Find("[data-testid='hiw-status-factcheck']").GetAttribute("data-on"));
        Assert.DoesNotContain("Another AI compares", cut.Find("[data-testid='hiw-steps']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_questions_the_example_has_no_question_and_the_step_says_it_starts_right_away()
    {
        Services.Configure<OrderOptions>(o => o.IntakeMode = IntakeMode.Off);
        this.AddTestAuthorization();

        var cut = Render();

        Assert.Empty(cut.FindAll("[data-testid='hiw-example-question']"));
        Assert.Contains(cut.FindAll("[data-testid='hiw-step']"), s => s.GetAttribute("data-step") == "no-questions");
        Assert.Equal("false", cut.Find("[data-testid='hiw-status-questions']").GetAttribute("data-on"));
    }

    [Fact]
    public void The_example_uses_the_fixed_sentences_of_the_result_page()
    {
        Services.Configure<DirectOptions>(o => o.FactCheckMode = FactCheckMode.Report);
        this.AddTestAuthorization();

        var checks = Render().Find("[data-testid='hiw-example-checks']").TextContent;

        Assert.Contains(OrderCheckLines.Heading, checks, StringComparison.Ordinal);
        Assert.Contains(OrderCheckLines.FactsOpenPointOneNoFiles, checks, StringComparison.Ordinal);
        Assert.Contains(OrderCheckLines.ToReviewOpenPoints, checks, StringComparison.Ordinal);
        Assert.Contains(OrderCheckLines.Limits, checks, StringComparison.Ordinal);
    }

    [Fact]
    public void The_example_is_marked_as_made_up_and_the_page_holds_no_contact_details()
    {
        this.AddTestAuthorization();

        var cut = Render();

        Assert.Contains("made up", cut.Find("[data-testid='hiw-example']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"[\w.+-]+@[\w-]+\.[\w.]+"), cut.Markup);
        Assert.DoesNotMatch(new Regex(@"\+?\d[\d /-]{7,}\d"), cut.Find("[data-testid='how-it-works']").TextContent);
        Assert.DoesNotContain("<img", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void The_drawings_are_hidden_from_screen_readers_and_the_steps_are_an_ordered_list()
    {
        this.AddTestAuthorization();

        var cut = Render();

        Assert.All(cut.FindAll("svg"), svg => Assert.Equal("true", svg.ParentElement!.GetAttribute("aria-hidden")));
        Assert.Equal(5, cut.FindAll("ol.hiw-thread > li").Count);
        Assert.All(cut.FindAll("section.hiw-section"), s => Assert.NotNull(s.QuerySelector("h2")));
    }

    [Fact]
    public void The_lay_parts_avoid_the_technical_words_of_the_older_way()
    {
        this.AddTestAuthorization();

        var cut = Render();
        var lay = string.Join(' ', new[] { "hiw-ways", "hiw-steps", "hiw-example", "hiw-why", "hiw-status", "hiw-files", "hiw-checks" }
            .Select(id => cut.Find($"[data-testid='{id}']").TextContent));

        foreach (var word in new[] { "crew", "run", "grounding", "iteration", "convergence", "finalizer", "manuscript", "MCP", "LLM", "prompt" })
            Assert.DoesNotMatch(new Regex($@"\b{word}s?\b", RegexOptions.IgnoreCase), lay);
    }

    [Fact]
    public void The_assistant_part_names_the_address_of_this_site_and_explains_mcp_in_one_sentence()
    {
        this.AddTestAuthorization();

        var cut = Render();

        Assert.Equal("http://localhost/mcp", cut.Find("[data-testid='hiw-mcp-address']").TextContent);
        Assert.Contains("MCP, a common way for assistants to use outside tools", cut.Find("[data-testid='hiw-assistant']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Learning_is_named_as_missing_only_while_it_is_off()
    {
        this.AddTestAuthorization();
        Assert.NotEmpty(Render().FindAll("[data-testid='hiw-limit-learning']"));
    }

    [Fact]
    public void While_learning_is_on_the_page_does_not_say_that_it_does_not_learn()
    {
        Services.Configure<LearningOptions>(o => o.Enabled = true);
        this.AddTestAuthorization();
        Assert.Empty(Render().FindAll("[data-testid='hiw-limit-learning']"));
    }

    private IRenderedComponent<HowItWorksPage> Render() => RenderComponent<HowItWorksPage>();

    private void UseShippedSettings()
    {
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();
        Services.Configure<OrderOptions>(shipped.GetSection(OrderOptions.SectionName));
        Services.Configure<IntakeOptions>(shipped.GetSection(IntakeOptions.SectionName));
        Services.Configure<ResearchOptions>(shipped.GetSection(ResearchOptions.SectionName));
        Services.Configure<DirectOptions>(shipped.GetSection(DirectOptions.SectionName));
        Services.Configure<UploadOptions>(shipped.GetSection(UploadOptions.SectionName));
        Services.Configure<UiOptions>(shipped.GetSection(UiOptions.SectionName));
        Services.Configure<LearningOptions>(shipped.GetSection(LearningOptions.SectionName));
        Services.Configure<SourceOptions>(shipped.GetSection(SourceOptions.SectionName));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Geef.Atelier.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
