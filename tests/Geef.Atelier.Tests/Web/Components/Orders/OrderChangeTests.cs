using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Web.Components;
using Geef.Atelier.Web.Components.Pages;
using Geef.Atelier.Web.Components.UI.Orders;
using Geef.Atelier.Web.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// Changing a delivered text on the order page (contract stage 5, 4.3): the button "Change something" only for the owner of a finished
/// order while changing is switched on, the card with quick buttons and own words, refusals in the card, the earlier version staying on
/// the page while a new one is written or after it could not be finished, the visible list of versions with the changes as plain text,
/// and open points of an earlier version.
/// </summary>
public sealed class OrderChangeTests : TestContext
{
    private const string FirstText = "Liebe Eltern,\n\nder Elternabend ist am **12. Mai** um 19 Uhr.\n\n<b>Wir</b> freuen uns auf Sie.";

    private const string SecondText = "Liebe Eltern,\n\nder Elternabend ist am **5. Mai** um 19 Uhr.\n\n<b>Wir</b> freuen uns auf Sie.";

    private const string Asked = "The meeting is on 5 May, not 12 May.";

    private readonly FakeOrderService _orders = new();

    private readonly FakeOrderExports _exports = new();

    private ReviseMode _revise = ReviseMode.On;

    public OrderChangeTests()
    {
        Services.AddSingleton<IOrderService>(_orders);
        Services.AddSingleton<IOrderExportService>(_exports);
        Services.AddSingleton<IRunUpdateFeed>(new ScriptedOrderFeed());
        Services.AddScoped<CircuitDataGate>();
        Services.AddOptions();
        Services.Configure<OrderOptions>(o => o.Revise = _revise);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static OrderRunView RunView(int version, RunStatus status, RunOutcome? outcome, int? words, string? asked = null, int minutes = 0) =>
        new(Guid.NewGuid(), version, RunKind.Standard, status, outcome, OrderTestData.Start.AddMinutes(minutes), OrderTestData.Start.AddMinutes(minutes),
            status is RunStatus.Pending or RunStatus.Running ? null : OrderTestData.Start.AddMinutes(minutes + 2), words, asked);

    private OrderView AddOrder(OrderDisplayState state, IReadOnlyList<OrderRunView> runs, string owner = "alice")
    {
        var view = OrderTestData.View(state, hasText: runs[^1].WordCount is > 0) with { Runs = runs, CurrentRun = runs[^1] };
        return _orders.Add(owner, view);
    }

    private static OrderResult Result(OrderView view, int version, int latest, string text, string? note = null) =>
        OrderTestData.Result(view, text: text, note: note) with { Version = version, LatestVersion = latest, RunId = view.Runs.First(r => r.Version == version).RunId };

    private OrderView Delivered(params OrderRunView[] extra)
    {
        var runs = new List<OrderRunView> { RunView(1, RunStatus.Completed, RunOutcome.Converged, 14) };
        runs.AddRange(extra);
        var view = AddOrder(OrderDisplayState.Delivered, runs);
        var shown = runs.Last(r => r.WordCount is > 0).Version!.Value;
        _orders.SetResult(Result(view, shown, runs[^1].Version!.Value, shown == 1 ? FirstText : SecondText));
        _exports.Texts[(view.OrderId, 1)] = FirstText;
        _exports.Texts[(view.OrderId, 2)] = SecondText;
        return view;
    }

    private IRenderedComponent<OrderDetail> Render(OrderView view, string user = "alice", params string[] roles)
    {
        var auth = this.AddTestAuthorization().SetAuthorized(user);
        if (roles.Length > 0)
            auth.SetRoles(roles);
        return RenderComponent<OrderDetail>(p => p.Add(c => c.OrderId, view.OrderId));
    }

    [Fact]
    public void The_owner_of_a_finished_order_opens_a_card_with_the_quick_buttons_and_a_field()
    {
        var cut = Render(Delivered());

        cut.Find("[data-testid='order-change-open']").Click();

        var card = cut.Find("[data-testid='order-change-card']");
        Assert.Equal("Change something", card.QuerySelector("h2")!.TextContent);
        Assert.Equal(["Shorter", "Longer", "More formal", "Friendlier", "Simpler words"], cut.FindAll("[data-testid='order-change-quick']").Select(b => b.TextContent));
        Assert.All(cut.FindAll("[data-testid='order-change-quick']"), b => Assert.Equal("false", b.GetAttribute("aria-pressed")));
        Assert.Equal("What should change?", cut.Find("label[for='order-change-text']").TextContent);
        Assert.Equal("For example: The meeting is on 5 May, not 12 May.", cut.Find("[data-testid='order-change-text']").GetAttribute("placeholder"));
        Assert.Equal("2000", cut.Find("[data-testid='order-change-text']").GetAttribute("maxlength"));
        Assert.Equal("Only what you ask for is changed. Everything else stays as it is.", cut.Find("[data-testid='order-change-promise']").TextContent);
        Assert.Equal("Make the change", cut.Find("[data-testid='order-change-make']").TextContent.Trim());
        Assert.Equal("Cancel", cut.Find("[data-testid='order-change-cancel']").TextContent);
        var markup = cut.Markup;
        Assert.True(markup.IndexOf("data-testid=\"order-result\"", StringComparison.Ordinal) < markup.IndexOf("data-testid=\"order-change-card\"", StringComparison.Ordinal));

        cut.Find("[data-testid='order-change-cancel']").Click();
        Assert.Empty(cut.FindAll("[data-testid='order-change-card']"));
        Assert.Single(cut.FindAll("[data-testid='order-change-open']"));
    }

    [Fact]
    public void Quick_buttons_and_own_words_go_to_the_service_as_the_owner_and_the_card_closes()
    {
        var view = Delivered();
        var cut = Render(view);
        cut.Find("[data-testid='order-change-open']").Click();

        cut.Find("[data-quick='Simpler']").Click();
        cut.Find("[data-quick='Shorter']").Click();
        cut.Find("[data-quick='Longer']").Click();
        cut.Find("[data-quick='Longer']").Click();
        cut.Find("[data-testid='order-change-text']").Input("  " + Asked + "  ");
        Assert.Equal("true", cut.Find("[data-quick='Shorter']").GetAttribute("aria-pressed"));
        cut.Find("[data-testid='order-change-make']").Click();

        var request = Assert.Single(_orders.Revised);
        Assert.Equal((view.OrderId, "alice", Asked, OrderEntry.Web), (request.OrderId, request.RequestingUsername, request.Change, request.Entry));
        Assert.Equal([RevisionQuick.Shorter, RevisionQuick.Simpler], request.Quick);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='order-change-card']")));
    }

    [Fact]
    public void Without_words_and_buttons_the_card_says_what_to_do_and_asks_nobody()
    {
        var cut = Render(Delivered());
        cut.Find("[data-testid='order-change-open']").Click();

        cut.Find("[data-testid='order-change-make']").Click();

        Assert.Equal(RevisionLines.NoChangeGiven, cut.Find("[data-testid='order-change-error'] span").TextContent);
        Assert.Empty(_orders.Revised);
    }

    [Fact]
    public void A_refusal_of_the_service_stays_in_the_card_with_the_words_of_the_person()
    {
        _orders.ReviseError = new OrderRevisionException("This order already has 10 versions, the most one order can have. Please start a new order for further changes.");
        var cut = Render(Delivered());
        cut.Find("[data-testid='order-change-open']").Click();
        cut.Find("[data-testid='order-change-text']").Input(Asked);

        cut.Find("[data-testid='order-change-make']").Click();

        cut.WaitForAssertion(() => Assert.Equal(_orders.ReviseError.Message, cut.Find("[data-testid='order-change-error'] span").TextContent));
        Assert.Equal(Asked, cut.Find("[data-testid='order-change-text']").GetAttribute("value"));
    }

    [Fact]
    public void A_failure_on_our_side_keeps_the_words_and_says_so_plainly()
    {
        _orders.ReviseError = new TimeoutException("db");
        var cut = Render(Delivered());
        cut.Find("[data-testid='order-change-open']").Click();
        cut.Find("[data-testid='order-change-text']").Input(Asked);

        cut.Find("[data-testid='order-change-make']").Click();

        cut.WaitForAssertion(() => Assert.Equal("Something went wrong on our side. Your words are still here. Try again in a moment.",
            cut.Find("[data-testid='order-change-error'] span").TextContent));
        Assert.DoesNotContain("db", cut.Find("[data-testid='order-change-error']").TextContent);
    }

    [Fact]
    public void A_change_already_being_made_is_said_on_the_page()
    {
        var view = Delivered();
        _orders.OnRevise = r =>
        {
            _orders.Update(r.OrderId, v => v with
            {
                Display = new OrderDisplay(OrderDisplayState.Working, false, false),
                Runs = [.. v.Runs, RunView(2, RunStatus.Running, null, null, "Shorter", 5)],
            });
            return new OrderRevision(r.OrderId, OrderDisplayState.Working, Guid.NewGuid(), 2, [RevisionLines.Busy]);
        };
        var cut = Render(view);
        cut.Find("[data-testid='order-change-open']").Click();
        cut.Find("[data-quick='Shorter']").Click();

        cut.Find("[data-testid='order-change-make']").Click();

        cut.WaitForAssertion(() => Assert.Equal(RevisionLines.Busy, cut.Find("[data-testid='order-change-notice']").TextContent));
    }

    [Fact]
    public void No_button_when_changing_is_switched_off()
    {
        _revise = ReviseMode.Off;

        var cut = Render(Delivered());

        Assert.Single(cut.FindAll("[data-testid='order-result']"));
        Assert.Empty(cut.FindAll("[data-testid='order-change-open']"));
    }

    [Fact]
    public void No_button_for_an_administrator_looking_at_the_order_of_someone_else()
    {
        var cut = Render(Delivered(), "root", "admin");

        Assert.Single(cut.FindAll("[data-testid='order-result']"));
        Assert.Empty(cut.FindAll("[data-testid='order-change-open']"));
    }

    [Fact]
    public void The_link_from_another_page_opens_the_card_right_away()
    {
        var view = Delivered();
        this.AddTestAuthorization().SetAuthorized("alice");

        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo($"/orders/{view.OrderId}?change=1");

        var cut = RenderComponent<OrderDetail>(p => p.Add(c => c.OrderId, view.OrderId));

        Assert.Single(cut.FindAll("[data-testid='order-change-card']"));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("yes please")]
    public void Any_other_value_of_the_link_leaves_the_card_closed(string value)
    {
        var view = Delivered();
        this.AddTestAuthorization().SetAuthorized("alice");
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo($"/orders/{view.OrderId}?change={Uri.EscapeDataString(value)}");

        var cut = RenderComponent<OrderDetail>(p => p.Add(c => c.OrderId, view.OrderId));

        Assert.Empty(cut.FindAll("[data-testid='order-change-card']"));
        Assert.Single(cut.FindAll("[data-testid='order-change-open']"));
    }

    [Fact]
    public void While_a_new_version_is_written_the_earlier_one_stays_with_the_sentence_and_no_button()
    {
        var runs = new[] { RunView(1, RunStatus.Completed, RunOutcome.Converged, 14), RunView(2, RunStatus.Running, null, null, Asked, 5) };
        var view = AddOrder(OrderDisplayState.Working, runs);
        var note = string.Format(RevisionLines.StillWritingFormat, 1);
        _orders.SetResult(Result(view, 1, 2, FirstText, note) with { Display = view.Display });

        var cut = Render(view);

        Assert.Single(cut.FindAll("[data-testid='order-progress']"));
        Assert.Contains("12. Mai", cut.Find("[data-testid='order-result']").TextContent);
        Assert.Equal(note, cut.Find("[data-testid='order-version-now'] span").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='order-note']"));
        Assert.Empty(cut.FindAll("[data-testid='order-change-open']"));
        var markup = cut.Markup;
        Assert.True(markup.IndexOf("data-testid=\"order-version-now\"", StringComparison.Ordinal) < markup.IndexOf("data-testid=\"order-result\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(OrderDisplayState.Failed, RunStatus.Failed, RunOutcome.Failed, "The change could not be finished.")]
    [InlineData(OrderDisplayState.Stopped, RunStatus.Aborted, RunOutcome.Aborted, "The change was stopped.")]
    public void After_a_version_that_could_not_be_finished_the_earlier_one_is_shown_and_can_be_changed_again(
        OrderDisplayState state, RunStatus status, RunOutcome outcome, string headline)
    {
        var runs = new[] { RunView(1, RunStatus.Completed, RunOutcome.Converged, 14), RunView(2, status, outcome, null, Asked, 5) };
        var view = AddOrder(state, runs);
        var note = string.Format(RevisionLines.NotFinishedFormat, 2, 1);
        _orders.SetResult(Result(view, 1, 2, FirstText, note) with { Display = view.Display });

        var cut = Render(view);

        var banner = cut.Find("[data-testid='order-banner']");
        Assert.Equal("earlier-version", banner.GetAttribute("data-state"));
        Assert.Equal(headline, banner.QuerySelector("strong")!.TextContent);
        Assert.Equal("The earlier version is still here. You can try again with “Change something”.", banner.QuerySelector("span")!.TextContent);
        Assert.DoesNotContain("No text was written", cut.Markup);
        Assert.Contains("12. Mai", cut.Find("[data-testid='order-result']").TextContent);
        Assert.Equal(note, cut.Find("[data-testid='order-version-now'] span").TextContent);
        Assert.Single(cut.FindAll("[data-testid='order-change-open']"));
        Assert.Contains("version=1", cut.Find("[data-testid='order-download-md']").GetAttribute("href"));
    }

    [Fact]
    public void Versions_are_listed_newest_first_with_what_was_asked_and_a_download_each()
    {
        var view = Delivered(RunView(2, RunStatus.Completed, RunOutcome.Converged, 13, "Shorter, " + Asked, 5));

        var cut = Render(view);

        Assert.Equal("This is version 2, the newest.", cut.Find("[data-testid='order-version-now'] span").TextContent);
        Assert.Equal($"/orders/{view.OrderId}#order-versions", cut.Find("[data-testid='order-versions-link']").GetAttribute("href"));
        Assert.Equal("Versions", cut.Find("[data-testid='order-versions'] h2").TextContent);
        var versions = cut.FindAll("[data-testid='order-version']");
        Assert.Equal(["2", "1"], versions.Select(v => v.GetAttribute("data-version")));
        Assert.Equal($"Version 2 · {OrderTestData.Start.AddMinutes(5).ToListDate()} · 13 words", versions[0].QuerySelector("[data-testid='order-version-line']")!.TextContent);
        Assert.Equal($"You asked: “Shorter, {Asked}”", versions[0].QuerySelector("[data-testid='order-version-asked']")!.TextContent);
        Assert.Equal("Shown above", versions[0].QuerySelector("[data-testid='order-version-shown']")!.TextContent);
        Assert.Null(versions[1].QuerySelector("[data-testid='order-version-asked']"));
        Assert.Null(versions[1].QuerySelector("[data-testid='order-version-toggle']"));
        Assert.Equal($"/orders/{view.OrderId}/export/docx?version=1", versions[1].QuerySelector("[data-testid='order-version-download-docx']")!.GetAttribute("href"));
        Assert.Equal($"/orders/{view.OrderId}/export/pdf?version=2", versions[0].QuerySelector("[data-testid='order-version-download-pdf']")!.GetAttribute("href"));
        var markup = cut.Markup;
        Assert.True(markup.IndexOf("data-testid=\"order-versions\"", StringComparison.Ordinal) < markup.IndexOf("data-testid=\"order-how-made\"", StringComparison.Ordinal));
    }

    [Fact]
    public void One_version_shows_no_list_and_no_version_line()
    {
        var cut = Render(Delivered());

        Assert.Empty(cut.FindAll("[data-testid='order-versions']"));
        Assert.Empty(cut.FindAll("[data-testid='order-version-now']"));
    }

    [Fact]
    public void Show_changes_compares_with_the_version_before_as_plain_text()
    {
        var view = Delivered(RunView(2, RunStatus.Completed, RunOutcome.Converged, 13, Asked, 5));
        var cut = Render(view);

        cut.Find("[data-version='2'] [data-testid='order-version-toggle']").Click();

        var diff = cut.WaitForElement("[data-testid='order-diff']");
        Assert.Equal("Compared with version 1:", diff.QuerySelector("[data-testid='order-diff-lead']")!.TextContent);
        var removed = Assert.Single(cut.FindAll("[data-testid='order-diff-removed']"));
        var added = Assert.Single(cut.FindAll("[data-testid='order-diff-added']"));
        Assert.Equal("Removed", removed.QuerySelector(".order-diff-mark")!.TextContent);
        Assert.Equal("der Elternabend ist am **12. Mai** um 19 Uhr.", removed.QuerySelector(".order-diff-text")!.TextContent);
        Assert.Equal("Added", added.QuerySelector(".order-diff-mark")!.TextContent);
        Assert.Equal("der Elternabend ist am **5. Mai** um 19 Uhr.", added.QuerySelector(".order-diff-text")!.TextContent);
        Assert.True(diff.InnerHtml.IndexOf("order-diff-removed", StringComparison.Ordinal) < diff.InnerHtml.IndexOf("order-diff-added", StringComparison.Ordinal));
        var same = cut.FindAll("[data-testid='order-diff-same']");
        Assert.Equal(2, same.Count);
        Assert.All(same, s => Assert.Null(s.GetAttribute("open")));
        Assert.Equal("1 paragraph without changes", same[1].QuerySelector("summary")!.TextContent);
        Assert.Empty(diff.QuerySelectorAll("b, strong"));
        Assert.Contains("&lt;b&gt;Wir&lt;/b&gt;", diff.InnerHtml);
        Assert.Equal([(view.OrderId, 2, "alice"), (view.OrderId, 1, "alice")], _exports.Calls);
        Assert.Equal("Hide changes", cut.Find("[data-version='2'] [data-testid='order-version-toggle']").TextContent);

        cut.Find("[data-version='2'] [data-testid='order-version-toggle']").Click();
        Assert.Empty(cut.FindAll("[data-testid='order-diff']"));
        cut.Find("[data-version='2'] [data-testid='order-version-toggle']").Click();
        cut.WaitForElement("[data-testid='order-diff']");
        Assert.Equal(2, _exports.Calls.Count);
    }

    [Fact]
    public void A_version_that_could_not_be_loaded_says_so_and_nothing_breaks()
    {
        var view = Delivered(RunView(2, RunStatus.Completed, RunOutcome.Converged, 13, Asked, 5));
        _exports.Texts.Clear();
        var cut = Render(view);

        cut.Find("[data-version='2'] [data-testid='order-version-toggle']").Click();

        cut.WaitForAssertion(() => Assert.Equal(OrderVersions.LoadFailed, cut.Find("[data-testid='order-version-error']").TextContent));
    }

    [Fact]
    public void A_version_without_text_says_why_and_the_next_one_compares_with_the_last_one_with_text()
    {
        var view = Delivered(
            RunView(2, RunStatus.Failed, RunOutcome.Failed, null, "Longer", 5),
            RunView(3, RunStatus.Completed, RunOutcome.Converged, 13, Asked, 9));
        _exports.Texts[(view.OrderId, 3)] = SecondText;
        _orders.SetResult(Result(view, 3, 3, SecondText));
        var cut = Render(view);

        var failed = cut.Find("[data-version='2']");
        Assert.EndsWith(" · could not be finished", failed.QuerySelector("[data-testid='order-version-line']")!.TextContent);
        Assert.Null(failed.QuerySelector("[data-testid='order-version-download']"));
        cut.Find("[data-version='3'] [data-testid='order-version-toggle']").Click();
        Assert.Equal("Compared with version 1:", cut.WaitForElement("[data-testid='order-diff-lead']").TextContent);
    }

    [Fact]
    public void Open_points_of_an_earlier_version_and_where_a_passage_was_found_are_shown()
    {
        var view = Delivered(RunView(2, RunStatus.Completed, RunOutcome.Converged, 13, Asked, 5));
        _orders.SetResult(Result(view, 2, 2, SecondText) with
        {
            EarlierOpenPoints = [new OrderEarlierPoint(1, "Es fehlt, bis wann man sich anmelden soll."), new OrderEarlierPoint(1, " ")],
            UnbackedPassages = [new OrderUnbackedPassage("Wir freuen uns auf Sie.", FactFindingKind.Unsupported, null, true, FromVersion: 1)],
        });

        var cut = Render(view);

        Assert.Equal("Noted for version 1 – your change may have settled it:", cut.Find("[data-testid='order-earlier-lead']").TextContent);
        Assert.Equal(["Es fehlt, bis wann man sich anmelden soll."], cut.FindAll("[data-testid='order-earlier-point']").Select(p => p.TextContent));
        Assert.Equal("Found in version 1.", cut.Find("[data-testid='order-unbacked'] [data-testid='order-passage-from']").TextContent);
    }

    [Fact]
    public void The_diff_component_renders_unchanged_text_and_says_when_nothing_changed()
    {
        var cut = RenderComponent<OrderVersionDiff>(p => p.Add(c => c.Before, FirstText).Add(c => c.After, FirstText).Add(c => c.BeforeVersion, 1));

        Assert.Equal(OrderVersionDiff.NoChange, cut.Find("[data-testid='order-diff-none']").TextContent);
        Assert.Equal("3 paragraphs without changes", cut.Find("[data-testid='order-diff-same'] summary").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='order-diff-added'], [data-testid='order-diff-removed']"));
    }
}
