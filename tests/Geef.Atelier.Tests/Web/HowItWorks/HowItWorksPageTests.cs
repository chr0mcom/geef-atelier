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
    public void As_shipped_changing_and_looking_things_up_are_on()
    {
        this.AddTestAuthorization();
        UseShippedSettings();

        var cut = Render();

        Assert.Equal("true", cut.Find("[data-testid='hiw-status-change']").GetAttribute("data-on"));
        Assert.Equal("true", cut.Find("[data-testid='hiw-status-research']").GetAttribute("data-on"));
        Assert.Equal("true", cut.Find("[data-testid='hiw-status-questions']").GetAttribute("data-on"));
        Assert.Equal("true", cut.Find("[data-testid='hiw-status-upload']").GetAttribute("data-on"));
        Assert.Contains("changes nothing", cut.Find("[data-testid='hiw-status-factcheck']").TextContent, StringComparison.Ordinal);
        Assert.Contains(RevisionLines.ChangeButton, cut.Find("[data-testid='hiw-step-change']").TextContent, StringComparison.Ordinal);
        Assert.NotEmpty(cut.FindAll("[data-testid='hiw-assistant-change']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='hiw-step-research']"));
        Assert.NotEmpty(cut.FindAll("[data-testid='hiw-privacy-research']"));
        Assert.Contains("short topics only", cut.Find("[data-testid='hiw-flow-search']").TextContent, StringComparison.Ordinal);
        Assert.Contains("does not change the text", cut.Find("[data-testid='hiw-step-factcheck']").TextContent, StringComparison.Ordinal);
        Assert.Contains("for everyone", cut.Find("[data-testid='hiw-status-scope']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void As_shipped_an_exception_switches_looking_things_up_off_for_that_person_only()
    {
        UseShippedSettings();
        Services.Configure<ResearchOptions>(o => o.ModeOverrides["tester"] = ResearchMode.Off);
        var auth = this.AddTestAuthorization();

        auth.SetAuthorized("tester");
        var tester = Render();
        Assert.Equal("false", tester.Find("[data-testid='hiw-status-research']").GetAttribute("data-on"));
        Assert.Empty(tester.FindAll("[data-testid='hiw-step-research']"));
        Assert.Empty(tester.FindAll("[data-testid='hiw-privacy-research']"));

        auth.SetAuthorized("alice");
        Assert.Equal("true", Render().Find("[data-testid='hiw-status-research']").GetAttribute("data-on"));
    }

    [Fact]
    public void With_changing_switched_off_the_page_says_so_and_offers_no_change_step()
    {
        Services.Configure<OrderOptions>(o => { o.IntakeMode = IntakeMode.On; o.Revise = ReviseMode.Off; });
        this.AddTestAuthorization();

        var cut = Render();

        Assert.Equal("false", cut.Find("[data-testid='hiw-status-change']").GetAttribute("data-on"));
        Assert.Empty(cut.FindAll("[data-testid='hiw-step-change']"));
        Assert.Empty(cut.FindAll("[data-testid='hiw-assistant-change']"));
        Assert.DoesNotContain(RevisionLines.ChangeButton, cut.Find("[data-testid='hiw-steps']").TextContent, StringComparison.Ordinal);
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
    public void The_table_with_setting_names_is_shown_to_administrators_only_and_never_shows_an_exception()
    {
        Services.Configure<OrderOptions>(o => o.ReviseOverrides["tester"] = ReviseMode.On);
        Services.Configure<DirectOptions>(o => o.FactCheckMode = FactCheckMode.Shadow);
        var auth = this.AddTestAuthorization();
        string[] settingNames = ["Orders:", "Research:", "Direct:", "Uploads:", "Ui:", "Mcp:", "Shadow"];

        foreach (var viewer in new[] { (string?)null, "tester" })
        {
            if (viewer is null)
                auth.SetNotAuthorized();
            else
                auth.SetAuthorized(viewer);
            var cut = Render();
            Assert.Empty(cut.FindAll("[data-testid='hiw-admins-table']"));
            Assert.Empty(cut.FindAll("[data-testid='hiw-admins-workshop']"));
            foreach (var name in settingNames)
                Assert.DoesNotContain(name, cut.Markup, StringComparison.Ordinal);
        }

        auth.SetAuthorized("boss");
        auth.SetRoles(OwnerScope.AdminRole);
        var admin = Render();
        var row = admin.FindAll("[data-testid='hiw-admins'] tbody tr").Single(r => r.TextContent.Contains("Orders:Revise", StringComparison.Ordinal));
        Assert.Equal("Off", row.Children[2].TextContent);
        Assert.Contains("Shadow", admin.Find("[data-testid='hiw-admins-table']").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("tester", admin.Markup, StringComparison.Ordinal);
        Assert.Contains("no longer exists", admin.Find("[data-testid='hiw-admins-workshop']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Orders:CarefulOutline", admin.Find("[data-testid='hiw-admins-table']").TextContent, StringComparison.Ordinal);
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
        Assert.Contains("Not shown", cut.Find("[data-testid='hiw-status-factcheck']").TextContent, StringComparison.Ordinal);
        Assert.Equal("No fact check is shown for your texts right now.", cut.Find("[data-testid='hiw-step-factcheck']").TextContent);
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
        Assert.Contains(OrderCheckLines.FactsComparedLeadNoFiles + string.Format(System.Globalization.CultureInfo.InvariantCulture, OrderCheckLines.FactsOpenFormat, 2), checks, StringComparison.Ordinal);
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
    public void The_steps_are_a_numbered_list_and_the_picture_of_where_words_go_has_a_text()
    {
        this.AddTestAuthorization();

        var cut = Render();

        Assert.Equal(["1", "2", "3", "4", "5"], cut.FindAll("ol.hiw-thread > li .hiw-step-mark").Select(m => m.TextContent));
        Assert.All(cut.FindAll(".hiw-step-mark"), m => Assert.Equal("true", m.GetAttribute("aria-hidden")));
        var figure = cut.Find("[data-testid='hiw-flow']");
        Assert.Equal("hiw-flow-caption", figure.GetAttribute("aria-labelledby"));
        Assert.Contains("Nothing goes to a web search", figure.QuerySelector("figcaption")!.TextContent, StringComparison.Ordinal);
        Assert.Contains("not switched on", cut.Find("[data-testid='hiw-flow-search']").TextContent, StringComparison.Ordinal);
        Assert.All(cut.FindAll("section.hiw-section"), sec => Assert.NotNull(sec.QuerySelector("h2")));
        Assert.Empty(cut.FindAll("svg"));
    }

    [Fact]
    public void The_lay_parts_avoid_the_technical_words_of_the_older_way()
    {
        this.AddTestAuthorization();

        var cut = Render();
        var lay = string.Join(' ', new[] { "hiw-ways", "hiw-steps", "hiw-example", "hiw-why", "hiw-status", "hiw-files", "hiw-checks", "hiw-about" }
            .Select(id => cut.Find($"[data-testid='{id}']").TextContent));
        var assistant = cut.Find("[data-testid='hiw-assistant']").TextContent;
        string[] words = ["crew", "run", "grounding", "iteration", "convergence", "converged", "finalizer", "manuscript", "briefing", "LLM", "prompt", "executor"];

        foreach (var word in words.Append("MCP"))
            Assert.DoesNotMatch(new Regex($@"\b{word}s?\b", RegexOptions.IgnoreCase), lay);
        foreach (var word in words)
            Assert.DoesNotMatch(new Regex($@"\b{word}s?\b", RegexOptions.IgnoreCase), assistant);
    }

    [Fact]
    public void The_assistant_part_names_the_address_of_this_site_and_explains_mcp_in_one_sentence()
    {
        this.AddTestAuthorization();

        var cut = Render();

        Assert.Equal("http://localhost/mcp", cut.Find("[data-testid='hiw-mcp-address']").TextContent);
        Assert.Contains("through MCP, a common way for assistants to use outside tools", cut.Find("[data-testid='hiw-assistant']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Learning_is_named_as_missing_only_while_it_is_off()
    {
        this.AddTestAuthorization();
        Assert.NotEmpty(Render().FindAll("[data-testid='hiw-limit-learning']"));
    }

    [Fact]
    public void Without_research_no_web_search_is_promised_and_extra_careful_says_it_does_not_search()
    {
        Services.Configure<OrderOptions>(o => { o.IntakeMode = IntakeMode.On; o.CarefulOutline = CarefulOutlineMode.On; });
        this.AddTestAuthorization();

        var off = Render();

        Assert.Empty(off.FindAll("[data-testid='hiw-step-research']"));
        Assert.Empty(off.FindAll("[data-testid='hiw-privacy-research']"));
        Assert.Contains("does not look things up on the web", off.Find("[data-testid='hiw-limit-careful']").TextContent, StringComparison.Ordinal);
        Assert.Contains("Nothing goes to a web search", off.Find("[data-testid='hiw-flow']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void With_research_the_web_search_is_promised_only_for_the_standard_way_without_files()
    {
        Services.Configure<OrderOptions>(o => { o.IntakeMode = IntakeMode.On; o.CarefulOutline = CarefulOutlineMode.On; });
        Services.Configure<ResearchOptions>(o => o.Mode = ResearchMode.On);
        this.AddTestAuthorization();

        var on = Render();

        var step = on.Find("[data-testid='hiw-step-research']").TextContent;
        Assert.Contains("never for “Extra careful”", step, StringComparison.Ordinal);
        Assert.Contains("does not search again", step, StringComparison.Ordinal);
        Assert.Contains("daily limit", step, StringComparison.Ordinal);
        Assert.Contains("the result says so", step, StringComparison.Ordinal);
        var privacy = on.Find("[data-testid='hiw-privacy-research']").TextContent;
        Assert.Contains("sends a web search service a few short search words", privacy, StringComparison.Ordinal);
        Assert.Contains("when you add no files", privacy, StringComparison.Ordinal);
        Assert.Contains(OrderResearchLines.SearchPrivacy, privacy, StringComparison.Ordinal);
        Assert.DoesNotContain("private details", on.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("which search words were sent", step, StringComparison.Ordinal);
        Assert.Contains("“Extra careful” never searches the web", privacy, StringComparison.Ordinal);
        Assert.Contains("never your answers or your files; names or other details you wrote can end up in them", on.Find("[data-testid='hiw-flow']").TextContent, StringComparison.Ordinal);
        Assert.Contains("does not look things up on the web", on.Find("[data-testid='hiw-limit-careful']").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("tester")]
    public void Without_extra_careful_the_page_does_not_mention_it(string? viewer)
    {
        Services.Configure<OrderOptions>(o => { o.IntakeMode = IntakeMode.On; o.CarefulOutlineOverrides["someone-else"] = CarefulOutlineMode.On; });
        Services.Configure<ResearchOptions>(o => o.Mode = ResearchMode.On);
        var auth = this.AddTestAuthorization();
        if (viewer is null)
            auth.SetNotAuthorized();
        else
            auth.SetAuthorized(viewer);

        var cut = Render();

        Assert.DoesNotContain("Extra careful", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-testid='hiw-limit-careful']"));
    }

    [Fact]
    public void A_person_with_extra_careful_switched_on_reads_what_it_does()
    {
        Services.Configure<OrderOptions>(o => { o.IntakeMode = IntakeMode.On; o.CarefulOutlineOverrides["tester"] = CarefulOutlineMode.On; });
        this.AddTestAuthorization().SetAuthorized("tester");

        var line = Render().Find("[data-testid='hiw-limit-careful']").TextContent;

        Assert.Contains("plans the text in sections first", line, StringComparison.Ordinal);
        Assert.Contains("checks that everything you asked for is in it", line, StringComparison.Ordinal);
    }

    [Fact]
    public void The_standard_way_reaches_the_web_only_through_the_research_service()
    {
        Assert.Equal(Geef.Atelier.Core.Domain.Crew.SystemCrew.DirectTemplateName, Geef.Atelier.Core.Domain.Crew.SystemCrew.DirectTemplate.Name);
    }

    [Fact]
    public void The_copied_leads_of_the_example_match_the_result_page()
    {
        var orders = Path.Combine(RepoRoot(), "src", "Geef.Atelier.Web", "Components", "UI", "Orders");
        var openPoints = File.ReadAllText(Path.Combine(orders, "OrderOpenPoints.razor"));
        var assumptions = File.ReadAllText(Path.Combine(orders, "OrderAssumptions.razor"));

        Assert.Contains(HowItWorksPage.FillInLead, openPoints, StringComparison.Ordinal);
        Assert.Contains(HowItWorksPage.UnbackedLead, openPoints, StringComparison.Ordinal);
        Assert.Contains(HowItWorksPage.AssumedLead, assumptions, StringComparison.Ordinal);
    }

    [Fact]
    public void Sizes_are_shown_in_plain_numbers()
    {
        this.AddTestAuthorization();
        UseShippedSettings();

        var files = Render().Find("[data-testid='hiw-files']").TextContent;

        Assert.Contains("each up to 25 MB", files, StringComparison.Ordinal);
        Assert.Contains("about 30 days", files, StringComparison.Ordinal);
        Assert.Contains("roughly 60 pages", Render().Find("[data-testid='hiw-why-files']").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void The_assistant_part_says_exactly_what_was_tested()
    {
        this.AddTestAuthorization();

        var tested = Render().Find("[data-testid='hiw-assistant-tested']").TextContent;

        Assert.Contains("tested with Claude Code", tested, StringComparison.Ordinal);
        Assert.Contains("claude.ai chat window itself and from Claude Desktop has not been tested", tested, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(TellOnceMode.On, true)]
    [InlineData(TellOnceMode.WithoutPromise, true)]
    [InlineData(TellOnceMode.Off, false)]
    public void The_offer_to_save_missing_details_under_about_me_follows_its_switch(TellOnceMode mode, bool shown)
    {
        Services.Configure<OrderOptions>(o => o.TellOnce = mode);
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("boss");
        auth.SetRoles(OwnerScope.AdminRole);

        var cut = Render();

        Assert.Equal(shown, cut.FindAll("[data-testid='hiw-about-tell-once']").Count == 1);
        if (shown)
            Assert.Equal(mode == TellOnceMode.On,
                cut.Find("[data-testid='hiw-about-tell-once']").TextContent.Contains("Atelier takes it from About me instead of guessing or asking", StringComparison.Ordinal));
        Assert.Equal(shown, cut.FindAll("[data-testid='hiw-assistant-about-me']").Count == 1);
        var row = cut.FindAll("[data-testid='hiw-admins'] tbody tr").Single(r => r.TextContent.Contains("Orders:TellOnce", StringComparison.Ordinal));
        Assert.Equal(mode.ToString(), row.Children[2].TextContent);
        Assert.DoesNotContain("not leave this open again", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void As_shipped_the_offer_to_save_missing_details_promises_it_for_the_next_requests_only()
    {
        this.AddTestAuthorization();
        UseShippedSettings();

        var line = Render().Find("[data-testid='hiw-about-tell-once']").TextContent;
        Assert.Equal(TellOnceMode.WithoutPromise, new OrderOptions().TellOnce);

        Assert.Contains("From your next request on, Atelier takes it from About me instead of guessing or asking, and the writer gets it with every new request.", line, StringComparison.Ordinal);
        Assert.Contains("A change of a text you already have keeps the details it was written with", line, StringComparison.Ordinal);
        Assert.Contains("as long as that field is still empty", line, StringComparison.Ordinal);
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
