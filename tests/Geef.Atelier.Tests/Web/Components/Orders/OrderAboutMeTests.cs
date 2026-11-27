using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Profiles;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Tests.Web.Components.Profile;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Components.UI.Orders;
using Geef.Atelier.Web.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// "Tell Atelier once" on the result page: one field per topic the result had to assume or leave open, saved only into the profile of the
/// signed-in owner, the heading with or without the promise as the switch says, the line "From About me", plain words only, and nothing
/// for an administrator looking at the order of someone else.
/// </summary>
public sealed class OrderAboutMeTests : TestContext
{
    private readonly FakeOrderService _orders = new();
    private readonly FakeProfileService _profiles = new();
    private TellOnceMode _mode = TellOnceMode.On;

    public OrderAboutMeTests()
    {
        Services.AddSingleton<IOrderService>(_orders);
        Services.AddSingleton<IProfileService>(_profiles);
        Services.AddSingleton<IRunUpdateFeed>(new ScriptedOrderFeed());
        Services.AddScoped<CircuitDataGate>();
        Services.AddOptions();
        Services.Configure<OrderOptions>(o => o.TellOnce = _mode);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<OrderDetail> RenderPage(IReadOnlyList<GapTopic>? tellOnce, IReadOnlyList<GapTopic>? fromAboutMe = null, string user = "alice", params string[] roles)
    {
        var view = _orders.Add("alice", OrderTestData.View(OrderDisplayState.Delivered) with
        {
            Clarification = OrderTestData.Clarification(outcome: IntakeOutcome.Clarified, language: "de"),
        });
        _orders.SetResult(OrderTestData.Result(view) with { TellOnce = tellOnce, FromAboutMe = fromAboutMe });
        var auth = this.AddTestAuthorization().SetAuthorized(user);
        if (roles.Length > 0)
            auth.SetRoles(roles);
        return RenderComponent<OrderDetail>(p => p.Add(c => c.OrderId, view.OrderId));
    }

    [Fact]
    public void The_owner_sees_one_field_per_topic_with_the_promise_after_the_assumptions()
    {
        var cut = RenderPage([GapTopic.Sender, GapTopic.Signature]);

        Assert.Equal(AboutMeLines.TellOnce, cut.Find("[data-testid='order-tell-once-heading']").TextContent);
        Assert.Equal("Tell Atelier once – your next requests get it from About me", AboutMeLines.TellOnce);
        Assert.Equal(["sender", "signature"], cut.FindAll("[data-testid='order-tell-once-row']").Select(r => r.GetAttribute("data-topic")));
        Assert.Equal(["Your name", "How you sign off"], cut.FindAll("[data-testid='order-tell-once-row'] label > span").Select(s => s.TextContent));
        Assert.All(cut.FindAll("[data-testid='order-tell-once-save']"), b => Assert.True(b.HasAttribute("disabled")));
        var markup = cut.Markup;
        Assert.True(markup.IndexOf("data-testid=\"order-result\"", StringComparison.Ordinal) < markup.IndexOf("data-testid=\"order-tell-once\"", StringComparison.Ordinal));
        HonestResultData.AssertPlain(cut.Find("[data-testid='order-tell-once']").ChildNodes);
    }

    [Fact]
    public void Saving_one_field_writes_only_that_field_into_the_own_profile_and_says_so()
    {
        var cut = RenderPage([GapTopic.Sender, GapTopic.Signature]);

        var signatureRow = cut.Find("[data-topic='signature']");
        signatureRow.QuerySelector("textarea")!.Input("Viele Grüße\nErika");
        cut.Find("[data-topic='signature'] [data-testid='order-tell-once-save']").Click();

        var (user, part) = Assert.Single(_profiles.SavedParts);
        Assert.Equal("alice", user);
        Assert.Equal(new UserProfilePart(Signature: "Viele Grüße\nErika"), part);
        Assert.Empty(_profiles.Saved);
        Assert.Equal(AboutMeLines.Saved + " About me", cut.Find("[data-topic='signature'] [data-testid='order-tell-once-saved']").TextContent);
        Assert.Equal("/about-me", cut.Find("[data-topic='signature'] [data-testid='order-tell-once-saved'] a").GetAttribute("href"));
        Assert.NotNull(cut.Find("[data-topic='sender'] input"));
        Assert.Equal(new UserProfileInput(null, null, null, Formality.Unset, null, "Viele Grüße\nErika", null, null), _profiles.Stored("alice"));
    }

    [Fact]
    public void Form_of_address_and_language_are_chosen_not_typed_and_the_language_of_the_order_is_preselected()
    {
        var cut = RenderPage([GapTopic.Formality, GapTopic.Language]);

        Assert.Equal(["formal", "informal"], cut.FindAll("[data-topic='formality'] input[type='radio']").Select(r => r.GetAttribute("value")!.ToLowerInvariant()));
        Assert.True(cut.Find("[data-topic='formality'] [data-testid='order-tell-once-save']").HasAttribute("disabled"));
        cut.Find("[data-testid='order-tell-once-formality-informal']").Change(true);
        cut.Find("[data-topic='formality'] [data-testid='order-tell-once-save']").Click();
        Assert.Equal("de", cut.Find("[data-topic='language'] select").GetAttribute("value"));
        cut.Find("[data-topic='language'] [data-testid='order-tell-once-save']").Click();

        Assert.Equal([new UserProfilePart(Formality: Formality.Informal), new UserProfilePart(Language: "de")], _profiles.SavedParts.Select(s => s.Part));
    }

    [Fact]
    public void A_field_set_under_about_me_in_the_meantime_is_not_overwritten()
    {
        var cut = RenderPage([GapTopic.Signature]);
        _profiles.Set("alice", FakeProfileService.Empty with { Signature = "Liebe Grüße, Erika" });

        cut.Find("[data-topic='signature'] textarea").Input("Viele Grüße\nErika");
        cut.Find("[data-topic='signature'] [data-testid='order-tell-once-save']").Click();

        Assert.Equal(AboutMeLines.AlreadySaved + " About me", cut.Find("[data-testid='order-tell-once-already']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='order-tell-once-saved']"));
        Assert.Equal("Liebe Grüße, Erika", _profiles.Stored("alice")!.Signature);
        Assert.Empty(cut.FindAll("[data-testid='order-tell-once-save']"));
    }

    [Fact]
    public void When_nothing_could_be_saved_the_page_says_so_and_keeps_the_field()
    {
        _profiles.NothingChanged = true;
        var cut = RenderPage([GapTopic.Signature]);

        cut.Find("[data-topic='signature'] textarea").Input("Viele Grüße");
        cut.Find("[data-topic='signature'] [data-testid='order-tell-once-save']").Click();

        Assert.Equal(AboutMeLines.SaveFailed, cut.Find("[data-testid='order-tell-once-error']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='order-tell-once-saved']"));
        Assert.Empty(cut.FindAll("[data-testid='order-tell-once-already']"));
    }

    [Fact]
    public void The_saved_line_says_that_only_the_next_requests_get_it()
    {
        Assert.Contains("Your next requests get it", AboutMeLines.Saved, StringComparison.Ordinal);
        Assert.Contains("any change you ask for to it, keeps the details it was written with", AboutMeLines.Saved, StringComparison.Ordinal);
        Assert.DoesNotContain("Change something", AboutMeLines.Saved, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_value_keeps_the_field_and_shows_the_reason()
    {
        _profiles.SaveError = new ProfileValidationException("\"Your name\" is too long (201 characters, at most 200).");
        var cut = RenderPage([GapTopic.Sender]);

        cut.Find("[data-topic='sender'] input").Input("Erika");
        cut.Find("[data-topic='sender'] [data-testid='order-tell-once-save']").Click();

        Assert.Equal("\"Your name\" is too long (201 characters, at most 200).", cut.Find("[data-testid='order-tell-once-error']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='order-tell-once-saved']"));
        Assert.NotNull(cut.Find("[data-topic='sender'] input"));
    }

    [Fact]
    public void Without_the_promise_the_heading_says_only_where_it_is_saved()
    {
        _mode = TellOnceMode.WithoutPromise;
        var cut = RenderPage([GapTopic.Signature]);

        Assert.Equal(AboutMeLines.TellOnceWithoutPromise, cut.Find("[data-testid='order-tell-once-heading']").TextContent);
        Assert.DoesNotContain("not leave this open", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Switched_off_the_component_shows_no_offer_even_with_topics()
    {
        _mode = TellOnceMode.Off;
        var result = OrderTestData.Result(OrderTestData.View(OrderDisplayState.Delivered)) with { TellOnce = [GapTopic.Signature] };

        var cut = RenderComponent<OrderAboutMe>(p => p.Add(c => c.Result, result).Add(c => c.Username, "alice"));

        Assert.Empty(cut.FindAll("[data-testid='order-tell-once']"));
    }

    [Fact]
    public void What_came_from_about_me_is_named_with_a_link_to_change_it()
    {
        var cut = RenderPage(null, [GapTopic.Sender, GapTopic.Signature]);

        var line = cut.Find("[data-testid='order-from-about-me']");
        Assert.Equal("With this request the writer got from About me: who you are, how you sign off. You can change or delete it under About me; that counts for your next requests.", line.TextContent);
        Assert.Equal("/about-me", line.QuerySelector("a")!.GetAttribute("href"));
        Assert.Empty(cut.FindAll("[data-testid='order-tell-once']"));
        HonestResultData.AssertPlain(line.ChildNodes);
    }

    [Fact]
    public void An_administrator_looking_at_the_order_of_someone_else_sees_nothing_of_it()
    {
        var cut = RenderPage([GapTopic.Signature], [GapTopic.Sender], "root", "admin");

        Assert.Single(cut.FindAll("[data-testid='order-result']"));
        Assert.Empty(cut.FindAll("[data-testid='order-tell-once']"));
        Assert.Empty(cut.FindAll("[data-testid='order-from-about-me']"));
        Assert.Empty(_profiles.SavedParts);
    }

    [Fact]
    public void A_result_without_topics_renders_exactly_as_before()
    {
        var cut = RenderPage(null);

        Assert.Empty(cut.FindAll("[data-testid='order-tell-once']"));
        Assert.Empty(cut.FindAll("[data-testid='order-from-about-me']"));
        Assert.Empty(_profiles.Loaded);
    }
}
